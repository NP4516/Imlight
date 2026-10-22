// Tools that give an agent full control of the live server: run code in it, read any state, find why it is broken.
import fs from "node:fs";
import path from "node:path";
import { z } from "zod";

export function registerFullControl(server, { ROOT, text, fail, json, run, bridge, newestLog }) {
  const guard = (fn) => async (args) => { try { return await fn(args); } catch (e) { return fail(e.message); } };

  function evalResult(r) {
    const parts = [];
    const failed = r.ok === false || !!r.error;
    if (failed) parts.push("FAILED");
    if (r.compileErrors) parts.push("Compile errors:\n" + r.compileErrors.join("\n"));
    if (r.exception) parts.push("Exception:\n" + r.exception);
    if (r.error) parts.push(r.error);
    if (r.output) parts.push("Output:\n" + r.output);
    if (r.result !== undefined && r.result !== null) parts.push("Result:\n" + r.result);
    const body = parts.join("\n\n") || "(ran, no result)";
    return failed ? fail(body) : text(body);
  }

  // The HTTP call must outlast the script's own timeout, or a long script is cut off from its result.
  const evalOnServer = (code, timeoutMs = 30000, background = false) => bridge("/eval", { code, timeoutMs, background }, timeoutMs + 15000);

  server.tool("server_eval",
    "Run C# inside the LIVE server process. Full access to its objects, including internal types (CommandDispatcher, services, collections, PlayerDatabase.Instance.Store...). " +
    "If the code is a single expression its value is returned; otherwise write statements and use `return x;`. await is allowed. " +
    "Helpers: Print(x), Dump(x, depth), Players(), Player(\"name\") (throws and lists who is online when nobody matches), Actors (the ActorSystem), Vars[\"key\"] (kept between scripts), " +
    "Ct (a CancellationToken: pass it to awaits like Task.Delay(ms, Ct) so the script can be stopped), Job (the job log when running in the background). " +
    "Scripts run concurrently. For anything longer than ~30 seconds set background:true: it returns a job id at once; read it with job_get and stop it with job_cancel. " +
    "Returned objects are dumped as JSON with every field. This is arbitrary code execution with no sandbox.",
    { code: z.string().min(1), timeoutMs: z.number().int().min(500).max(300000).default(30000), background: z.boolean().default(false).describe("Run as a job and return its id immediately") },
    guard(async ({ code, timeoutMs, background }) => {
      const r = await evalOnServer(code, timeoutMs, background);
      if (background) return json(r);
      return evalResult(r);
    }));

  // ---------- jobs ----------
  server.tool("job_list", "List the background jobs on the server (background scripts, combat_auto): id, status, running time.", {},
    guard(async () => json(await bridge("/jobs"))));

  server.tool("job_get", "Read a background job: status, the tail of its log, and its result once it ended. waitSeconds holds the call until the job ends (or the wait runs out).",
    { id: z.string(), tail: z.number().int().min(0).max(1000).default(60), waitSeconds: z.number().int().min(0).max(110).default(0) },
    guard(async ({ id, tail, waitSeconds }) => json(await bridge("/jobs/get", { id, tail, waitMs: waitSeconds * 1000 }, waitSeconds * 1000 + 15000))));

  server.tool("job_cancel", "Ask a background job to stop. It stops when it next checks its cancellation token (combat_auto does; a script must pass Ct to its awaits).",
    { id: z.string() },
    guard(async ({ id }) => json(await bridge("/jobs/cancel", { id }))));

  // ---------- combat ----------
  const player = z.string().optional().describe("Part of the character or account name; optional when one player is online");

  server.tool("combat_creatures",
    "List the fightable creatures in the player's zone, nearest first (name, template id, distance). Use before combat_start to pick an enemy. Monsters only exist in zones that have them (Cyclops Lane, Krokotopia...); teleport there first.",
    { player, creature: z.string().optional().describe("Name part or template id"), limit: z.number().int().min(1).max(200).default(25) },
    guard(async (a) => json(await bridge("/combat/creatures", a))));

  server.tool("combat_start",
    "Start a real duel for the player against a creature, as if it had aggroed them: the client enters battle. creature picks by name part, template id or actor name; default is the nearest. Fails if the player is already fighting.",
    { player, creature: z.string().optional() },
    guard(async (a) => json(await bridge("/combat/start", a, 30000))));

  server.tool("combat_state",
    "Read the player's battle: round, phase, whether moves are being accepted, every circle (slot, team, name, hp, pips) and the player's hand (card index, spell, rank, castable, damage/heal/buff/debuff). inDuel:false when not fighting.",
    { player },
    guard(async (a) => json(await bridge("/combat/state", a))));

  server.tool("combat_move",
    "Play the player's move for this round. It first waits for the planning phase (waitSeconds), then sends the move like the client does. action: cast (default), pass, flee, discard. " +
    "For cast give card (hand index from combat_state) or spell (name part or id); target is a slot number, 'enemy' or 'self' (default: weakest enemy for attacks and debuffs, yourself otherwise). " +
    "waitForRound:true holds the call until the round has played out. Returns what was played and the new state.",
    { player, action: z.enum(["cast", "pass", "flee", "discard"]).default("cast"), card: z.number().int().min(0).max(23).optional(), spell: z.string().optional(),
      target: z.union([z.number().int().min(0).max(7), z.enum(["enemy", "self"])]).optional(), waitSeconds: z.number().int().min(1).max(90).default(45), waitForRound: z.boolean().default(false) },
    guard(async (a) => json(await bridge("/combat/move", a, (a.waitSeconds ?? 45) * 2000 + 15000))));

  server.tool("combat_auto",
    "Let a simple strategy fight the whole battle as a background job: each round it casts the highest-rank castable damage spell at the weakest enemy, heals below healBelowPercent, passes when nothing can be cast, and can flee below fleeBelowPercent. Returns a job id; follow it with job_get (waitSeconds) and stop it with job_cancel.",
    { player, maxRounds: z.number().int().min(1).max(200).default(40), healBelowPercent: z.number().int().min(0).max(100).default(35), fleeBelowPercent: z.number().int().min(0).max(100).default(0),
      refillPips: z.boolean().default(false).describe("Test helper: fill free pip slots with normal pips each round (like .pip give light), so the fight is not stalled by school pips or an empty pool") },
    guard(async (a) => json(await bridge("/combat/auto", a))));

  server.tool("server_info", "Process facts: pid, uptime, memory, GC, threads, thread pool pressure, player count.", {},
    guard(async () => json(await bridge("/info"))));

  server.tool("server_shutdown", "Gracefully exit the running server, even if it was started by hand. Players are disconnected.", {},
    guard(async () => json(await bridge("/shutdown", {}))));

  server.tool("server_errors",
    "Digest the newest log: each distinct WRN/ERR/FTL line (numbers collapsed) with a count, most frequent first, plus the last time and any stack trace that followed.",
    { level: z.enum(["WRN", "ERR", "FTL"]).default("ERR"), top: z.number().int().min(1).max(100).default(25), sinceMinutes: z.number().int().min(1).max(100000).optional() },
    guard(async ({ level, top, sinceMinutes }) => {
      const f = newestLog();
      if (!f) return fail("No log file found.");
      const levels = level === "WRN" ? ["WRN", "ERR", "FTL"] : level === "ERR" ? ["ERR", "FTL"] : ["FTL"];
      const lines = fs.readFileSync(f, "utf8").split(/\r?\n/);
      const cutoff = sinceMinutes ? Date.now() - sinceMinutes * 60000 : 0;
      const groups = new Map();
      for (let i = 0; i < lines.length; i++) {
        const m = lines[i].match(/^(\d{4}-\d\d-\d\d \d\d:\d\d:\d\d\.\d+) (\w{3}) (.*)$/);
        if (!m || !levels.includes(m[2])) continue;
        if (cutoff && new Date(m[1].replace(" ", "T")).getTime() < cutoff) continue;
        const key = `${m[2]} ${m[3].replace(/\d+/g, "#").slice(0, 220)}`;
        let trace = "";
        for (let j = i + 1; j < lines.length && !/^\d{4}-\d\d-\d\d /.test(lines[j]) && j < i + 12; j++) trace += lines[j] + "\n";
        const g = groups.get(key) || { count: 0 };
        groups.set(key, { count: g.count + 1, last: m[1], trace: trace.trim() });
      }
      const ranked = [...groups.entries()].sort((a, b) => b[1].count - a[1].count).slice(0, top);
      return text(`${f}\n${ranked.length} of ${groups.size} distinct lines\n\n` +
        ranked.map(([k, g]) => `x${g.count}  last ${g.last}\n${k}\n${g.trace ? g.trace + "\n" : ""}`).join("\n"));
    }));

  server.tool("actor_tree", "Print the Akka actor hierarchy of the live server (names only), to see what is running.",
    { maxDepth: z.number().int().min(1).max(8).default(3), maxLines: z.number().int().min(10).max(2000).default(300) },
    guard(async ({ maxDepth, maxLines }) => evalResult(await evalOnServer([
      "var sb = new StringBuilder(); var lines = 0;",
      "void Walk(IActorRef r, int d) {",
      `  if (lines++ > ${maxLines}) return;`,
      "  sb.AppendLine(new string(' ', d * 2) + r.Path.Name);",
      `  if (d < ${maxDepth}) foreach (var c in (r.GetType().GetProperty("Children")?.GetValue(r) as IEnumerable<IActorRef>) ?? Enumerable.Empty<IActorRef>()) Walk(c, d + 1);`,
      "}",
      "Walk(((Akka.Actor.Internal.ActorSystemImpl) Actors).Guardian, 0);",
      "return sb.ToString();",
    ].join("\n")))));

  server.tool("db_query",
    "Query the player database (RavenDB) with RQL, e.g. `from @all_docs limit 5` or `from Wizards where Name = 'X'`. Returns the documents as JSON lines. A trailing `limit N` in the RQL is replaced by the limit argument.",
    { rql: z.string().min(1), limit: z.number().int().min(1).max(500).default(50) },
    guard(async ({ rql, limit }) => evalResult(await evalOnServer([
      "using var session = PlayerDatabase.Instance.Store.OpenSession();",
      `var query = session.Advanced.RawQuery<Newtonsoft.Json.Linq.JObject>(${JSON.stringify(rql.replace(/\s+limit\s+\d+(\s*,\s*\d+)?\s*$/i, ""))}).NoTracking().Take(${limit});`,
      'return string.Join("\\n", query.ToList().Select(d => d.ToString(Newtonsoft.Json.Formatting.None)));',
    ].join("\n"), 60000))));

  server.tool("shell_run", "Run a PowerShell command on the machine hosting the server (working directory defaults to the repo). Not sandboxed.",
    { command: z.string().min(1), cwd: z.string().optional(), timeoutSeconds: z.number().int().min(1).max(900).default(120) },
    guard(async ({ command, cwd, timeoutSeconds }) => {
      const r = await run("powershell", ["-NoProfile", "-NonInteractive", "-Command", command], { cwd: cwd ? path.resolve(ROOT, cwd) : ROOT, timeoutMs: timeoutSeconds * 1000 });
      return (r.code ? fail : text)(`exit ${r.code}${r.timedOut ? " (timed out)" : ""}\n${r.out}`);
    }));

  const inRoot = (p) => {
    const full = path.resolve(ROOT, p);
    if (full !== ROOT && !full.startsWith(ROOT + path.sep)) throw new Error("Path is outside the repo.");
    return full;
  };

  server.tool("fs_read", "Read a text file in the repo, optionally a line range (default first 400 lines).",
    { path: z.string(), from: z.number().int().min(1).optional(), to: z.number().int().min(1).optional() },
    guard(async ({ path: p, from, to }) => {
      const lines = fs.readFileSync(inRoot(p), "utf8").split(/\r?\n/);
      const a = (from ?? 1) - 1;
      const b = to ?? Math.min(lines.length, a + 400);
      return text(lines.slice(a, b).map((l, i) => `${a + i + 1}\t${l}`).join("\n"));
    }));

  server.tool("fs_write", "Write a text file in the repo (creates folders, overwrites).", { path: z.string(), content: z.string() },
    guard(async ({ path: p, content }) => {
      const full = inRoot(p);
      fs.mkdirSync(path.dirname(full), { recursive: true });
      fs.writeFileSync(full, content, "utf8");
      return text(`Wrote ${content.length} characters to ${path.relative(ROOT, full)}`);
    }));

  server.tool("fs_search", "Regex search of the repo's text files (skips bin, obj, node_modules, .git, caches). Returns file:line: text.",
    { pattern: z.string(), ext: z.string().default(".cs").describe("File extension like .cs, .ini, .md, or * for all"), limit: z.number().int().min(1).max(500).default(100) },
    guard(async ({ pattern, ext, limit }) => {
      const re = new RegExp(pattern, "i");
      const skip = new Set(["bin", "obj", "node_modules", ".git", "spiraldb-cache", "cache", "logs"]);
      const hits = [];
      const walk = (dir) => {
        for (const e of fs.readdirSync(dir, { withFileTypes: true })) {
          if (hits.length >= limit) return;
          const f = path.join(dir, e.name);
          if (e.isDirectory()) { if (!skip.has(e.name)) walk(f); continue; }
          if (ext !== "*" && !e.name.endsWith(ext)) continue;
          if (fs.statSync(f).size > 2_000_000) continue;
          fs.readFileSync(f, "utf8").split(/\r?\n/).forEach((l, i) => {
            if (hits.length < limit && re.test(l)) hits.push(`${path.relative(ROOT, f)}:${i + 1}: ${l.trim().slice(0, 200)}`);
          });
        }
      };
      walk(ROOT);
      return text(hits.join("\n") || "(no matches)");
    }));
}
