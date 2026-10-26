/*
 * Imlight
 * Copyright (C) 2025 Revive101
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published by
 * the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 * GNU Affero General Public License for more details.
 *
 * You should have received a copy of the GNU Affero General Public License
 * along with this program. If not, see <http://www.gnu.org/licenses/>.
 *
 * ========================================================================
 * AGENT SCRIPTING
 * ========================================================================
 *
 * PURPOSE:
 * Runs C# the agent sends (through the agent bridge) inside the live server, with the server's own
 * objects in reach, and turns any object into readable JSON so an agent can see the server's state.
 *
 * NOTE:
 * Each script is compiled on its own as an assembly the CoreLib lets see its internals; use Vars to keep values.
 * The last expression is the result; for statements use return.
 * A script that never ends cannot be stopped; the caller gets a timeout while it keeps running.
 */

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Imlight.CoreLib.Game.Commands;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Imlight.Director;

/// <summary>What a script can use without writing a using or a path.</summary>
public class ScriptGlobals {

    internal readonly StringBuilder Output = new();

    /// <summary>Values that stay between scripts (each script is compiled on its own).</summary>
    public static readonly System.Collections.Concurrent.ConcurrentDictionary<string, object> Vars = new();

    /// <summary>The server's actor system.</summary>
    public ActorSystem Actors => AgentBridge.Actors;

    /// <summary>Asked to stop: the request timed out or the job was cancelled. Pass it to awaits (Task.Delay(ms, Ct)) so the script can be stopped.</summary>
    public CancellationToken Ct { get; internal set; }

    /// <summary>The job this script runs as (null for a script that answers its request directly).</summary>
    public AgentJob Job { get; internal set; }

    /// <summary>Writes a line to the result the agent gets back (and to the job's log, for a background script).</summary>
    public void Print(object value = null) {
        var line = value as string ?? AgentInspector.Dump(value);
        Output.AppendLine(line);
        Job?.Log(line);
    }

    /// <summary>Turns any object into indented JSON (fields and properties, any visibility).</summary>
    public string Dump(object value, int depth = 4) => AgentInspector.Dump(value, depth);

    /// <summary>The players in the game right now, with their wizard, account and core object.</summary>
    public List<LivePlayer> Players() => AgentBridge.GetLivePlayers().GetAwaiter().GetResult();

    /// <summary>A player whose name contains the text. Throws, naming who is online, when nobody matches.</summary>
    public LivePlayer Player(string name) {
        var all = Players();

        return all.FirstOrDefault(p => p.Name.Contains(name ?? "", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"No player matches '{name}'. Online: {(all.Count == 0 ? "nobody" : string.Join(", ", all.Select(p => p.Name)))}.");
    }

}

internal static class AgentScripting {

    // CoreLib lets this assembly name see its internals, so scripts can use the server's own internal types.
    private const string ScriptAssemblyName = "Imlight.AgentScript";
    private const string Header = "using System; using System.Collections.Generic; using System.Linq; using System.IO; "
        + "using System.Text; using System.Threading; using System.Threading.Tasks; using Akka.Actor; using Imlight.Common; "
        + "using Imlight.CoreLib.Game.Commands; using Imlight.CoreLib.Game.Services; using Imlight.CoreLib.WizardData; "
        + "using Imlight.CoreLib.WizardData.Collections; using Imlight.CoreLib.WizardData.Databases; "
        + "using Imlight.CoreLib.WizardData.Models.Player; using Imlight.Director; ";

    private static int s_counter;
    private static List<MetadataReference> s_references;

    public static void Map() => AgentBridge.MapRoute("/eval", Eval);

    private static List<MetadataReference> References() => s_references ??= [.. AppDomain.CurrentDomain.GetAssemblies()
        .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
        .Select(a => (MetadataReference) MetadataReference.CreateFromFile(a.Location))];

    /// <summary>Compiles the code as the body of an async method; returns the assembly bytes or the errors.</summary>
    private static (byte[] Image, string Class, string[] Errors) Compile(string code, bool asExpression) {
        var className = $"Script{Interlocked.Increment(ref s_counter)}";
        var body = asExpression ? $"return (object) (\n{code}\n);" : $"{code}\nreturn null;";
        var source = $"{Header}public sealed class {className} : ScriptGlobals {{ "
            + $"public async Task<object> Run() {{\n{body}\n}} }}";

        var options = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true)
            .WithMetadataImportOptions(MetadataImportOptions.All);
        var compilation = CSharpCompilation.Create(ScriptAssemblyName + "." + className,
            [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest))], References(), options);

        // The assembly name decides which internals are visible, so it must be exactly the granted one.
        compilation = compilation.WithAssemblyName(ScriptAssemblyName);

        using var stream = new MemoryStream();
        var emitted = compilation.Emit(stream);
        if (!emitted.Success) {
            // The wrapper takes the first line (two when the code is an expression).
            var errors = emitted.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => {
                    var line = d.Location.GetLineSpan().StartLinePosition.Line - (asExpression ? 1 : 0);
                    return $"line {Math.Max(line, 1)}: {d.Id} {d.GetMessage()}";
                }).ToArray();

            return (null, className, errors);
        }

        return (stream.ToArray(), className, null);
    }

    private static async Task<object> Eval(JsonElement body) {
        var code = body.GetProperty("code").GetString() ?? "";
        var timeoutMs = body.TryGetProperty("timeoutMs", out var t) ? Math.Clamp(t.GetInt32(), 500, 300000) : 30000;
        var background = body.TryGetProperty("background", out var b) && b.ValueKind == JsonValueKind.True;

        // A background script runs as a job: the request returns at once and the agent reads the job later.
        if (background) {
            var job = AgentJobs.Start("eval", async (job, token) => {
                var outcome = await RunScript(code, token, job);

                return outcome;
            });

            return new { ok = true, jobId = job.Id, message = "Running in the background. Read it with job_get; stop it with job_cancel (the script must pass Ct to its awaits)." };
        }

        using var cancel = new CancellationTokenSource();
        var running = RunScript(code, cancel.Token, null);
        if (await Task.WhenAny(running, Task.Delay(timeoutMs)) == running) {
            return await running;
        }

        // Scripts that pass Ct to their awaits stop here; others keep running to their end.
        cancel.Cancel();

        return new { ok = false, error = $"The script is still running after {timeoutMs} ms. Cancellation was requested (it only stops if the script uses Ct); for long work use background." };
    }

    /// <summary>Compiles and runs the code; the result is what the agent bridge hands back.</summary>
    private static async Task<object> RunScript(string code, CancellationToken token, AgentJob job) {
        var context = new AssemblyLoadContext(null, isCollectible: true);
        try {
            var (image, className, errors) = Compile(code.Trim().TrimEnd(';'), asExpression: true);
            if (image is null) {
                (image, className, errors) = Compile(code, asExpression: false);
            }

            if (image is null) {
                return new { ok = false, compileErrors = errors };
            }

            var assembly = context.LoadFromStream(new MemoryStream(image));
            var script = (ScriptGlobals) Activator.CreateInstance(assembly.GetType(className)!);
            script.Ct = token;
            script.Job = job;
            var value = await (Task<object>) assembly.GetType(className)!.GetMethod("Run")!.Invoke(script, null);

            return new { ok = true, output = script.Output.ToString(), result = value is null ? null : value as string ?? AgentInspector.Dump(value) };
        }
        catch (Exception ex) {
            var inner = ex is TargetInvocationException { InnerException: not null } ? ex.InnerException : ex;

            return new { ok = false, exception = inner.ToString() };
        }
        finally {
            context.Unload();
        }
    }

}

/// <summary>Turns objects into JSON by reflection: any visibility, cycle-safe, depth and size limited.</summary>
internal static class AgentInspector {

    private const int MaxItems = 60;
    private const int MaxString = 600;

    public static string Dump(object value, int depth = 4) {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true })) {
            Write(writer, value, Math.Clamp(depth, 0, 10), new HashSet<object>(ReferenceEqualityComparer.Instance));
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void Write(Utf8JsonWriter w, object v, int depth, HashSet<object> seen) {
        switch (v) {
            case null: w.WriteNullValue(); return;
            case string s: w.WriteStringValue(s.Length > MaxString ? s[..MaxString] + "…" : s); return;
            case bool b: w.WriteBooleanValue(b); return;
            case char c: w.WriteStringValue(c.ToString()); return;
            case byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal:
                w.WriteRawValue(Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture)
                    is { } n && n is not ("NaN" or "Infinity" or "-Infinity") ? n : "null");
                return;
            case Enum e: w.WriteStringValue(e.ToString()); return;
            case Exception ex: w.WriteStringValue(ex.ToString()); return;
            case DateTime or DateTimeOffset or TimeSpan or Guid or Uri or IActorRef or Delegate or Assembly or MemberInfo or Module:
                w.WriteStringValue(v.ToString()); return;
        }

        var type = v.GetType();
        if (type.IsPrimitive || v is IntPtr or UIntPtr) { w.WriteStringValue(v.ToString()); return; }

        if (depth <= 0) { w.WriteStringValue($"<{type.Name}…>"); return; }
        if (!type.IsValueType && !seen.Add(v)) { w.WriteStringValue($"<cycle {type.Name}>"); return; }

        try {
            if (v is IDictionary dict) {
                w.WriteStartObject();
                var count = 0;
                foreach (DictionaryEntry entry in dict) {
                    if (count++ >= MaxItems) { w.WriteString("…", $"{dict.Count - MaxItems} more"); break; }
                    w.WritePropertyName(entry.Key?.ToString() ?? "null");
                    Write(w, entry.Value, depth - 1, seen);
                }
                w.WriteEndObject();
            }
            else if (v is IEnumerable seq && v is not IActorRef) {
                w.WriteStartArray();
                var count = 0;
                foreach (var item in seq) {
                    if (count++ >= MaxItems) { w.WriteStringValue("… more"); break; }
                    Write(w, item, depth - 1, seen);
                }
                w.WriteEndArray();
            }
            else {
                w.WriteStartObject();
                w.WriteString("$type", type.Name);
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                foreach (var f in type.GetFields(flags).Where(f => !f.Name.Contains('<'))) {
                    w.WritePropertyName(f.Name);
                    WriteMember(w, () => f.GetValue(v), depth, seen);
                }
                foreach (var p in type.GetProperties(flags).Where(p => p.GetIndexParameters().Length == 0 && p.CanRead)) {
                    w.WritePropertyName(p.Name);
                    WriteMember(w, () => p.GetValue(v), depth, seen);
                }
                w.WriteEndObject();
            }
        }
        finally {
            if (!type.IsValueType) { seen.Remove(v); }
        }
    }

    private static void WriteMember(Utf8JsonWriter w, Func<object> read, int depth, HashSet<object> seen) {
        object value;
        try { value = read(); }
        catch (Exception ex) { w.WriteStringValue($"<{ex.GetBaseException().GetType().Name}: {ex.GetBaseException().Message}>"); return; }

        Write(w, value, depth - 1, seen);
    }

}
