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
 */

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Imlight.Setup;

/// <summary>
/// The app the installed Imlight.exe opens: start and stop the server, play Wizard101 on it or on
/// another private server, send in-game commands, read the log with repeated lines folded together,
/// change any setting in Imlight.ini (including the randomizer), and manage player accounts.
/// The page is served on localhost only and shown in its own window. Every request must carry a key
/// that only the page knows, so other web pages cannot use it.
/// </summary>
internal sealed partial class ControlPanel {

    internal const int FIRST_PORT = 12380;
    internal const int PORT_TRIES = 20;
    private const string COMMAND_INBOX = "command-inbox";
    private const int LOG_TAIL_BYTES = 512 * 1024;

    private readonly string _installFolder;
    private readonly string _token = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);
    private int _port;

    private ControlPanel(string installFolder) {
        _installFolder = Path.GetFullPath(installFolder);
    }

    private string ConfigFile => InstalledCommands.ConfigFile(_installFolder);
    private string ServerFolder => InstalledCommands.ServerFolder(_installFolder);

    internal static async Task<int> RunAsync(string installFolder) {
        Console.Title = Edition.NAME;
        var panel = new ControlPanel(installFolder);
        if (!File.Exists(panel.ConfigFile)) {
            Console.WriteLine($"Imlight is not installed in {installFolder} yet. Run {Edition.RELEASE_ASSET} first.");
            WaitForKey();

            return 1;
        }

        using var listener = panel.Listen();
        var url = $"http://localhost:{panel._port}/";
        _ = Task.Run(async () => {
            while (true) {
                var context = await listener.GetContextAsync();
                _ = Task.Run(() => panel.HandleAsync(context));
            }
        });

        // In its own window, the server lives as long as the app: closing the window stops it.
        // Versions before 0.4.2 used a normal profile here, which Edge signed in to the person's account.
        TryDeleteFolder(Path.Combine(panel._installFolder, "AppWindow"));
        var window = AppWindow.Open(url, Path.Combine(panel._installFolder, "AppWindowPrivate"));
        if (window is not null) {
            AppWindow.HideConsole();
            await window.WaitForExitAsync();
            panel.StopServer();

            return 0;
        }

        Console.WriteLine($"{Edition.NAME} is open in your browser at {url}");
        Console.WriteLine("Keep this window open while you use it. Closing it does not stop the server.");
        OpenBrowser(url);
        await Task.Delay(Timeout.Infinite);

        return 0;
    }

    private static void TryDeleteFolder(string folder) {
        try {
            if (Directory.Exists(folder)) {
                Directory.Delete(folder, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
            // Still in use by an open window; removed next time.
        }
    }

    private HttpListener Listen() {
        for (var port = FIRST_PORT; port < FIRST_PORT + PORT_TRIES; port++) {
            var listener = new HttpListener();
            listener.Prefixes.Add($"http://localhost:{port}/");
            try {
                listener.Start();
                _port = port;

                return listener;
            }
            catch (HttpListenerException) {
                listener.Close();
            }
        }

        throw new InvalidOperationException("No free port was found for the control panel.");
    }

    private static void OpenBrowser(string url) {
        try {
            if (OperatingSystem.IsWindows()) {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();
            }
        }
        catch (Exception) {
            // The address is printed above; the person can open it themselves.
        }
    }

    // ---------------------------------------------------------------------
    // Requests
    // ---------------------------------------------------------------------

    private async Task HandleAsync(HttpListenerContext context) {
        var request = context.Request;
        var response = context.Response;
        try {
            // Only this computer, by name, so another site cannot reach the panel through its own domain.
            if (!string.Equals(request.Url?.Host, "localhost", StringComparison.OrdinalIgnoreCase)) {
                await WriteAsync(response, 403, "text/plain", "Forbidden");

                return;
            }

            var path = request.Url!.AbsolutePath;
            if (request.HttpMethod == "GET" && path == "/") {
                await WriteAsync(response, 200, "text/html; charset=utf-8", Page());

                return;
            }

            if (path == "/favicon.ico") {
                response.StatusCode = 204;

                return;
            }

            if (request.HttpMethod == "GET" && path == "/ids") {
                await ServeIdLookupAsync(response);

                return;
            }

            if (!path.StartsWith("/api/", StringComparison.Ordinal) || request.Headers["X-Panel-Key"] != _token) {
                await WriteAsync(response, 403, "text/plain", "Forbidden");

                return;
            }

            var body = request.HasEntityBody
                ? await JsonSerializer.DeserializeAsync<JsonObject>(request.InputStream, _json) ?? []
                : [];
            var result = await RouteAsync(request.HttpMethod, path, body);
            await WriteJsonAsync(response, 200, result);
        }
        catch (Exception ex) when (ex is AccountException or PanelException) {
            await WriteJsonAsync(response, 400, new { error = ex.Message });
        }
        catch (Exception ex) {
            await WriteJsonAsync(response, 500, new { error = $"Something went wrong: {ex.Message}" });
        }
        finally {
            response.Close();
        }
    }

    private async Task<object> RouteAsync(string method, string path, JsonObject body) {
        string Text(string name) => (string?) body[name] ?? "";

        switch (method, path) {
            case ("GET", "/api/status"):
                return Status();
            case ("POST", "/api/server/start"):
                return StartServer();
            case ("POST", "/api/server/stop"):
                return StopServer();
            case ("POST", "/api/server/restart"):
                StopServer();
                await Task.Delay(1500);

                return StartServer();
            case ("GET", "/api/log"):
                return ReadLog();
            case ("GET", "/api/settings"):
                return ReadSettings();
            case ("POST", "/api/settings"):
                return SaveSettings(body);
            case ("POST", "/api/open"):
                return OpenFolder(Text("what"));
            case ("POST", "/api/play"):
                return PlayHere();
            case ("GET", "/api/servers"):
                return new { servers = GameClient.LoadServers(_installFolder) };
            case ("POST", "/api/servers"):
                var added = await GameClient.AddServerAsync(_installFolder, Text("name"), Text("host"),
                    int.TryParse(Text("port"), out var port) ? port : 0);

                return new { message = $"{added.Name} was saved." };
            case ("POST", "/api/servers/remove"):
                GameClient.RemoveServer(_installFolder, Text("name"));

                return new { message = $"{Text("name")} was removed." };
            case ("POST", "/api/servers/play"):
                var server = GameClient.LoadServers(_installFolder)
                    .FirstOrDefault(s => s.Name.Equals(Text("name"), StringComparison.OrdinalIgnoreCase))
                    ?? throw new PanelException($"There is no saved server named {Text("name")}.");
                await GameClient.LaunchAsync(_installFolder, server.Host, server.Port);

                return new { message = $"Starting Wizard101 on {server.Name}." };
            case ("POST", "/api/command"):
                return await SendCommandAsync(Text("username"), Text("command"));
        }

        if (path.StartsWith("/api/accounts", StringComparison.Ordinal)) {
            using var accounts = PlayerAccounts.FromConfig(ConfigFile, out var reason) ?? throw new PanelException(reason!);
            switch (method, path) {
                case ("GET", "/api/accounts"):
                    return new { accounts = await accounts.ListAsync(), levels = PlayerAccounts.AUTH_LEVELS };
                case ("POST", "/api/accounts"):
                    await accounts.CreateAsync(Text("username"), Text("password"), Text("level"));

                    return new { message = $"Account {Text("username")} was created." };
                case ("POST", "/api/accounts/password"):
                    await accounts.SetPasswordAsync(Text("username"), Text("password"));

                    return new { message = $"The password for {Text("username")} was changed." };
                case ("POST", "/api/accounts/level"):
                    await accounts.SetAuthLevelAsync(Text("username"), Text("level"));

                    return new { message = $"{Text("username")} is now {Text("level")}. It applies the next time they log in." };
                case ("POST", "/api/accounts/lock"):
                    var locked = body["locked"]?.GetValue<bool>() ?? true;
                    await accounts.SetLockedAsync(Text("username"), locked);

                    return new { message = locked ? $"{Text("username")} can no longer log in." : $"{Text("username")} can log in again." };
            }
        }

        throw new PanelException("Unknown request.");
    }

    // ---------------------------------------------------------------------
    // Server
    // ---------------------------------------------------------------------

    private object Status() {
        var running = InstalledCommands.IsServerRunning(_installFolder);
        var loginPort = int.TryParse(IniFile.Get(ConfigFile, "Login Server", "LoginServerPort"), out var port) ? port : 12000;

        return new {
            edition = Edition.NAME,
            version = SelfUpdate.CurrentVersion.ToString(),
            installFolder = _installFolder,
            running,
            ready = running && IsListening(loginPort),
            randomizer = IniFile.Get(ConfigFile, "Randomizer", "Enabled") is { } enabled
                ? enabled.Equals("true", StringComparison.OrdinalIgnoreCase)
                : (bool?) null,
            idLookup = IdLookupPage() is not null,
            clientFound = GameClient.ClientExe(_installFolder) is not null,
            databaseUrl = $"http://127.0.0.1:{IniFile.Get(ConfigFile, "Database", "EmbeddedDatabasePort") ?? "8080"}",
        };
    }

    private object StartServer() {
        if (InstalledCommands.IsServerRunning(_installFolder)) {
            return new { message = "The server is already running." };
        }

        var self = Environment.ProcessPath ?? throw new PanelException("The panel cannot find its own program.");
        // No window of its own: the app shows the server's log.
        var startInfo = new ProcessStartInfo(self) {
            WorkingDirectory = _installFolder,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        // A framework-dependent build runs through dotnet; pass the program file along.
        if (Path.GetFileNameWithoutExtension(self).Equals("dotnet", StringComparison.OrdinalIgnoreCase)) {
            startInfo.ArgumentList.Add(Environment.GetCommandLineArgs()[0]);
        }

        foreach (var arg in new[] { "--start", "--no-wait", "--dir", _installFolder }) {
            startInfo.ArgumentList.Add(arg);
        }

        Process.Start(startInfo)?.Dispose();

        return new { message = "The server is starting. It is ready for players when the status says so." };
    }

    private object StopServer() {
        var exe = Path.GetFullPath(InstalledCommands.ServerExe(_installFolder));
        var stopped = 0;
        foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(exe))) {
            using (process) {
                try {
                    if (!string.Equals(process.MainModule?.FileName, exe, StringComparison.OrdinalIgnoreCase)) {
                        continue;
                    }

                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(15000);
                    stopped++;
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) {
                    // Already gone, or not ours to stop.
                }
            }
        }

        return new { message = stopped > 0 ? "The server was stopped." : "The server was not running." };
    }

    private static bool IsListening(int port) {
        try {
            using var client = new TcpClient();

            return client.ConnectAsync("127.0.0.1", port).Wait(TimeSpan.FromMilliseconds(500)) && client.Connected;
        }
        catch (AggregateException) {
            return false;
        }
    }

    private int LoginPort => int.TryParse(IniFile.Get(ConfigFile, "Login Server", "LoginServerPort"), out var port) ? port : 12000;

    /// <summary>
    /// Plays on the server on this computer, starting it first when it is not running.
    /// </summary>
    private object PlayHere() {
        if (GameClient.ClientExe(_installFolder) is null) {
            throw new PanelException("Wizard101 could not be found. Run \"Update Imlight\" from the Start menu once, then try again.");
        }

        if (IsListening(LoginPort)) {
            GameClient.LaunchAsync(_installFolder, "127.0.0.1", LoginPort).GetAwaiter().GetResult();

            return new { message = "Starting Wizard101. Log in with your account on this server." };
        }

        StartServer();
        _ = Task.Run(async () => {
            for (var waited = 0; waited < 300; waited += 2) {
                await Task.Delay(TimeSpan.FromSeconds(2));
                if (IsListening(LoginPort)) {
                    await GameClient.LaunchAsync(_installFolder, "127.0.0.1", LoginPort);

                    return;
                }
            }
        });

        return new { message = "Starting the server first. Wizard101 opens by itself when the server is ready." };
    }

    /// <summary>
    /// Runs an in-game command for an account's character through the server's command inbox.
    /// </summary>
    private async Task<object> SendCommandAsync(string username, string command) {
        if (username.Trim().Length == 0 || command.Trim().Length == 0) {
            throw new PanelException("Choose an account and type a command.");
        }

        if (!IsListening(LoginPort)) {
            throw new PanelException("Start the server and log in with a character first.");
        }

        var inbox = Path.Combine(ServerFolder, COMMAND_INBOX);
        if (!Directory.Exists(inbox)) {
            throw new PanelException("This server is too old for the command box. Run \"Update Imlight\" from the Start menu, then try again.");
        }

        var id = Guid.NewGuid().ToString("N");
        var staging = Path.Combine(Path.GetTempPath(), $"imlight-command-{id}.tmp");
        await File.WriteAllTextAsync(staging, JsonSerializer.Serialize(new { Username = username, Command = command }));
        File.Move(staging, Path.Combine(inbox, id + ".json"));

        var resultFile = Path.Combine(inbox, id + ".result.json");
        for (var waited = 0; waited < 50; waited++) {
            await Task.Delay(200);
            if (!File.Exists(resultFile)) {
                continue;
            }

            try {
                var result = JsonSerializer.Deserialize<JsonObject>(await File.ReadAllTextAsync(resultFile));
                File.Delete(resultFile);
                var message = (string?) result?["Message"] ?? "Done.";

                return result?["Ok"]?.GetValue<bool>() == true ? new { message } : throw new PanelException(message);
            }
            catch (IOException) {
                // Still being written; read it on the next try.
            }
        }

        throw new PanelException("The server did not pick up the command. Check that it is running, then try again.");
    }

    // ---------------------------------------------------------------------
    // Log
    // ---------------------------------------------------------------------

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}(\.\d+)?\s+(?<level>[A-Z]{3})\s+")]
    private static partial Regex LogLinePrefix();

    /// <summary>
    /// The end of the newest log file, with runs of the same message folded into one line and a count.
    /// </summary>
    private object ReadLog() {
        var logs = Path.Combine(ServerFolder, "logs");
        var file = Directory.Exists(logs)
            ? new DirectoryInfo(logs).EnumerateFiles().Where(f => f.Extension is ".txt" or ".log")
                .OrderByDescending(f => f.LastWriteTimeUtc).FirstOrDefault()
            : null;
        if (file is null) {
            return new { file = (string?) null, lines = Array.Empty<object>() };
        }

        string text;
        using (var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)) {
            var start = Math.Max(0, stream.Length - LOG_TAIL_BYTES);
            stream.Seek(start, SeekOrigin.Begin);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            text = reader.ReadToEnd();
            if (start > 0) {
                // Drop the partial first line.
                text = text[(text.IndexOf('\n') + 1)..];
            }
        }

        return new { file = file.FullName, lines = FoldLines(text.Split('\n')) };
    }

    internal static List<LogLine> FoldLines(IEnumerable<string> rawLines) {
        var lines = new List<LogLine>();
        foreach (var raw in rawLines) {
            var line = raw.TrimEnd('\r');
            if (line.Length == 0) {
                continue;
            }

            var match = LogLinePrefix().Match(line);
            if (!match.Success && lines.Count > 0) {
                // A continuation, such as a stack trace line.
                lines[^1] = lines[^1] with { Text = lines[^1].Text + "\n" + line };

                continue;
            }

            var level = match.Success ? match.Groups["level"].Value : "INF";
            var message = match.Success ? line[match.Length..] : line;
            if (lines.Count > 0 && lines[^1].Message == message) {
                lines[^1] = lines[^1] with { Count = lines[^1].Count + 1, Text = line };

                continue;
            }

            lines.Add(new LogLine(line, message, level, 1));
        }

        return lines.Count > 400 ? lines[^400..] : lines;
    }

    internal sealed record LogLine(string Text, string Message, string Level, int Count);

    // ---------------------------------------------------------------------
    // Settings
    // ---------------------------------------------------------------------

    /// <summary>
    /// Every section and key in Imlight.ini, with the comment above each key as its help text.
    /// </summary>
    private object ReadSettings() {
        var sections = new List<object>();
        string? section = null;
        List<object>? entries = null;
        var help = new List<string>();

        foreach (var raw in File.ReadLines(ConfigFile)) {
            var line = raw.Trim();
            if (line.StartsWith('[') && line.EndsWith(']')) {
                section = line[1..^1].Trim();
                entries = [];
                sections.Add(new { name = section, entries });
                help.Clear();

                continue;
            }

            if (line.StartsWith(';') || line.StartsWith('#')) {
                help.Add(line.TrimStart(';', '#').Trim());

                continue;
            }

            var equals = line.IndexOf('=');
            if (equals <= 0 || entries is null) {
                help.Clear();

                continue;
            }

            entries.Add(new {
                key = line[..equals].Trim(),
                value = line[(equals + 1)..].Trim(),
                help = string.Join(" ", help),
            });
            help.Clear();
        }

        return new { sections };
    }

    private object SaveSettings(JsonObject body) {
        if (body["changes"] is not JsonArray changes || changes.Count == 0) {
            return new { message = "Nothing to save." };
        }

        var lines = File.ReadAllLines(ConfigFile).ToList();
        foreach (var change in changes.OfType<JsonObject>()) {
            var section = (string?) change["section"] ?? "";
            var key = (string?) change["key"] ?? "";
            var value = ((string?) change["value"] ?? "").ReplaceLineEndings(" ");

            // Only keys already in the file; the panel never invents settings.
            if (IniFile.Get(ConfigFile, section, key) is null) {
                throw new PanelException($"{section} > {key} is not a setting in Imlight.ini.");
            }

            IniFile.Set(lines, section, key, value);
        }

        File.WriteAllLines(ConfigFile, lines);
        var running = InstalledCommands.IsServerRunning(_installFolder);

        return new {
            message = running
                ? $"Saved {changes.Count} setting(s). Restart the server to use them."
                : $"Saved {changes.Count} setting(s). They apply the next time the server starts.",
            restartNeeded = running,
        };
    }

    // ---------------------------------------------------------------------
    // Tools
    // ---------------------------------------------------------------------

    private string? IdLookupPage() {
        var output = IniFile.Get(ConfigFile, "ID Lookup", "OutputPath");
        if (output is null) {
            return null;
        }

        var page = Path.Combine(Path.GetFullPath(Path.Combine(ServerFolder, output)), "index.html");

        return File.Exists(page) ? page : null;
    }

    private async Task ServeIdLookupAsync(HttpListenerResponse response) {
        var page = IdLookupPage();
        if (page is null) {
            await WriteAsync(response, 404, "text/plain",
                "The ID list is not ready yet. Start the server and give it a minute to build the list.");

            return;
        }

        await WriteAsync(response, 200, "text/html; charset=utf-8", await File.ReadAllTextAsync(page));
    }

    private object OpenFolder(string what) {
        var folder = what switch {
            "logs" => Path.Combine(ServerFolder, "logs"),
            "install" => _installFolder,
            "config" => Path.GetDirectoryName(ConfigFile)!,
            _ => throw new PanelException("Unknown folder."),
        };
        Directory.CreateDirectory(folder);
        if (OperatingSystem.IsWindows()) {
            Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { folder } })?.Dispose();
        }

        return new { message = $"Opened {folder}." };
    }

    // ---------------------------------------------------------------------
    // Output
    // ---------------------------------------------------------------------

    private string Page() {
        using var stream = typeof(ControlPanel).Assembly.GetManifestResourceStream("ControlPanel.html")
            ?? throw new InvalidOperationException("The control panel page is missing from the program.");
        using var reader = new StreamReader(stream);

        return reader.ReadToEnd()
            .Replace("{{PANEL_KEY}}", _token, StringComparison.Ordinal)
            .Replace("{{EDITION}}", WebUtility.HtmlEncode(Edition.NAME), StringComparison.Ordinal);
    }

    private Task WriteJsonAsync(HttpListenerResponse response, int status, object value)
        => WriteAsync(response, status, "application/json", JsonSerializer.Serialize(value, _json));

    private static async Task WriteAsync(HttpListenerResponse response, int status, string contentType, string body) {
        var bytes = Encoding.UTF8.GetBytes(body);
        response.StatusCode = status;
        response.ContentType = contentType;
        response.Headers["Cache-Control"] = "no-store";
        response.Headers["X-Frame-Options"] = "DENY";
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes);
    }

    private static void WaitForKey() {
        if (Console.IsInputRedirected) {
            return;
        }

        Console.WriteLine("Press any key to close this window.");
        Console.ReadKey(true);
    }

}

/// <summary>
/// A panel request that could not be done. The message is written for the person using the panel.
/// </summary>
internal sealed class PanelException(string message) : Exception(message);
