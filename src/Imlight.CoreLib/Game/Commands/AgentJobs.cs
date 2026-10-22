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
 * AGENT JOBS
 * ========================================================================
 *
 * PURPOSE:
 * Long work the agent bridge starts (a scripted battle, a long script) runs as a job: the request returns
 * at once with a job id, and the agent reads the job's log and result, or cancels it, with later requests.
 *
 * NOTE:
 * Cancelling asks the job's token to stop; work that never looks at its token keeps running to the end.
 */

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Imlight.CoreLib.Game.Commands;

/// <summary>One piece of background work started through the agent bridge.</summary>
public sealed class AgentJob {

    private const int MaxLogLines = 1000;

    private readonly ConcurrentQueue<string> _log = new();
    private int _logLines;
    internal CancellationTokenSource Cancellation { get; } = new();
    internal Task Work { get; set; }

    public string Id { get; init; }
    public string Name { get; init; }
    public DateTime StartedUtc { get; } = DateTime.UtcNow;
    public DateTime? EndedUtc { get; internal set; }
    /// <summary>running, done, failed or cancelled.</summary>
    public string Status { get; internal set; } = "running";
    public object Result { get; internal set; }
    public string Error { get; internal set; }

    public CancellationToken Token => Cancellation.Token;

    /// <summary>Adds a timestamped line to the job's log (the last 1000 lines are kept).</summary>
    public void Log(string line) {
        _log.Enqueue($"[{DateTime.Now:HH:mm:ss}] {line}");
        if (Interlocked.Increment(ref _logLines) > MaxLogLines && _log.TryDequeue(out _)) {
            Interlocked.Decrement(ref _logLines);
        }
    }

    internal string[] LogLines() => [.. _log];

}

public static class AgentJobs {

    private const int KeepJobs = 60;

    private static readonly ConcurrentDictionary<string, AgentJob> s_jobs = new();
    private static int s_counter;

    /// <summary>Starts the work in the background. The work gets the job (to log to) and its cancellation token.</summary>
    public static AgentJob Start(string name, Func<AgentJob, CancellationToken, Task<object>> work) {
        var job = new AgentJob { Id = $"job{Interlocked.Increment(ref s_counter)}", Name = name };
        s_jobs[job.Id] = job;
        Trim();

        job.Work = Task.Run(async () => {
            try {
                job.Result = await work(job, job.Token);
                job.Status = job.Token.IsCancellationRequested ? "cancelled" : "done";
            }
            catch (OperationCanceledException) {
                job.Status = "cancelled";
            }
            catch (Exception ex) {
                job.Status = "failed";
                job.Error = ex.GetBaseException().ToString();
                job.Log("FAILED: " + ex.GetBaseException().Message);
            }
            finally {
                job.EndedUtc = DateTime.UtcNow;
            }
        });

        return job;
    }

    public static void Map() {
        AgentBridge.MapRoute("/jobs", _ => Task.FromResult<object>(new { jobs = s_jobs.Values.OrderByDescending(j => j.StartedUtc).Select(Summary) }));
        AgentBridge.MapRoute("/jobs/get", Get);
        AgentBridge.MapRoute("/jobs/cancel", Cancel);
    }

    private static object Summary(AgentJob job) => new {
        id = job.Id,
        name = job.Name,
        status = job.Status,
        startedUtc = job.StartedUtc,
        seconds = (long) ((job.EndedUtc ?? DateTime.UtcNow) - job.StartedUtc).TotalSeconds,
    };

    private static AgentJob Find(JsonElement body) {
        var id = body.TryGetProperty("id", out var i) ? i.GetString() : null;
        if (id is null || !s_jobs.TryGetValue(id, out var job)) {
            throw new InvalidOperationException($"No job {id ?? "(missing id)"}. Known: {string.Join(", ", s_jobs.Keys.OrderBy(k => k.Length).ThenBy(k => k))}");
        }

        return job;
    }

    private static async Task<object> Get(JsonElement body) {
        var job = Find(body);
        var tail = body.TryGetProperty("tail", out var t) ? Math.Clamp(t.GetInt32(), 0, 1000) : 60;
        var waitMs = body.TryGetProperty("waitMs", out var w) ? Math.Clamp(w.GetInt32(), 0, 120000) : 0;

        // Optionally hold the request until the job ends (or the wait runs out).
        if (waitMs > 0 && job.Work is not null) {
            await Task.WhenAny(job.Work, Task.Delay(waitMs));
        }

        var lines = job.LogLines();

        return new {
            id = job.Id,
            name = job.Name,
            status = job.Status,
            seconds = (long) ((job.EndedUtc ?? DateTime.UtcNow) - job.StartedUtc).TotalSeconds,
            error = job.Error,
            result = job.Result,
            logLines = lines.Length,
            log = lines.TakeLast(tail),
        };
    }

    private static Task<object> Cancel(JsonElement body) {
        var job = Find(body);
        if (job.Status != "running") {
            return Task.FromResult<object>(new { id = job.Id, status = job.Status, message = "The job is not running." });
        }

        job.Cancellation.Cancel();
        job.Log("Cancellation requested.");

        return Task.FromResult<object>(new { id = job.Id, status = job.Status, message = "Cancellation requested; the job stops when it next checks its token." });
    }

    private static void Trim() {
        foreach (var old in s_jobs.Values.Where(j => j.EndedUtc is not null).OrderBy(j => j.StartedUtc).Take(Math.Max(0, s_jobs.Count - KeepJobs))) {
            s_jobs.TryRemove(old.Id, out _);
        }
    }

}
