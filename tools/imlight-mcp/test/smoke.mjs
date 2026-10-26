// Smoke test for the Imlight MCP server: starts it over stdio like an MCP client does and runs every tool.
//   node test/smoke.mjs            everything that is safe on a running server (reads, scripts, commands)
//   node test/smoke.mjs --combat   also teleports the logged-in player and fights a whole battle
//   node test/smoke.mjs --quick    skip dotnet_test and the build
// Needs the server running with a character logged in for the game and combat parts.
import { Client } from "@modelcontextprotocol/sdk/client/index.js";
import { StdioClientTransport } from "@modelcontextprotocol/sdk/client/stdio.js";
import path from "node:path";
import { fileURLToPath } from "node:url";

const here = path.dirname(fileURLToPath(import.meta.url));
const args = new Set(process.argv.slice(2));
const transport = new StdioClientTransport({ command: "node", args: [path.join(here, "..", "index.js")] });
const client = new Client({ name: "smoke", version: "1" });
await client.connect(transport);

let passed = 0, failed = 0;
const failures = [];
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

async function call(name, a = {}) {
  const t0 = Date.now();
  const r = await client.callTool({ name, arguments: a }, undefined, { timeout: 600000 });
  const text = r.content?.map((c) => c.text).join("\n") ?? "";
  return { ok: !r.isError, text, ms: Date.now() - t0, json: () => JSON.parse(text) };
}

async function check(label, fn) {
  try {
    const note = await fn();
    passed++;
    console.log(`  ok    ${label}${note ? "  — " + note : ""}`);
  } catch (e) {
    failed++;
    failures.push(label);
    console.log(`  FAIL  ${label}\n        ${String(e.message).split("\n").join("\n        ")}`);
  }
}
const must = (cond, msg) => { if (!cond) throw new Error(msg); };
const evalOk = async (code, extra = {}) => { const r = await call("server_eval", { code, ...extra }); must(r.ok, `eval failed: ${r.text}`); return r.text; };

console.log("tools");
const tools = (await client.listTools()).tools.map((t) => t.name);
await check("lists the expected tools", () => {
  const want = ["server_status", "server_start", "server_stop", "server_restart", "server_logs", "server_errors", "server_info", "server_eval", "server_shutdown", "actor_tree", "db_query",
    "dotnet_build", "dotnet_test", "game_players", "game_command", "game_commands_run", "game_commands_help", "id_lookup", "config_get", "git_status",
    "shell_run", "fs_read", "fs_write", "fs_search", "job_list", "job_get", "job_cancel", "combat_creatures", "combat_start", "combat_state", "combat_move", "combat_auto"];
  const missing = want.filter((w) => !tools.includes(w));
  must(!missing.length, "missing: " + missing.join(", "));
  return `${tools.length} tools`;
});

console.log("server");
let players = [];
await check("server_status: bridge up", async () => {
  const r = await call("server_status"); must(r.ok, r.text);
  const j = r.json(); must(j.bridgeUp, "bridge is down"); players = j.players;
  return `pid ${j.pid}, up ${j.uptimeSeconds}s, ${players.length} player(s)`;
});
await check("server_info", async () => { const j = (await call("server_info")).json(); must(j.pid && j.threads, "no process facts"); return `${j.workingSetMb} MB`; });
await check("server_logs: tail, filter, sinceMinutes", async () => {
  const a = await call("server_logs", { lines: 5 }); must(a.ok && a.text.split("\n").length >= 2, "no log lines");
  const b = await call("server_logs", { lines: 20, filter: "INF" }); must(b.ok && !/\[DBG\]/.test(b.text.split("\n").slice(1).join("\n")), "filter let DBG lines through");
  const c = await call("server_logs", { lines: 50, sinceMinutes: 60 }); must(c.ok, c.text);
});
await check("server_errors digest", async () => { const r = await call("server_errors", { level: "WRN", top: 5 }); must(r.ok && /distinct lines/.test(r.text), r.text); });
await check("actor_tree", async () => { const r = await call("actor_tree", { maxDepth: 3, maxLines: 40 }); must(r.ok && /Imlight\.Login/.test(r.text), r.text); });
await check("db_query", async () => { const r = await call("db_query", { rql: "from @all_docs", limit: 3 }); must(r.ok, r.text); });
await check("config_get hides the token, rejects a bad section", async () => {
  const r = await call("config_get", { section: "Agent Bridge" }); must(r.ok && /\(hidden\)/.test(r.text), r.text);
  const bad = await call("config_get", { section: "No Such Section" }); must(!bad.ok, "bad section should be an error");
});
await check("git_status", async () => { const r = await call("git_status"); must(r.ok && /##/.test(r.text), r.text); });

console.log("reference");
await check("id_lookup: ids, creatures, zones, quests", async () => {
  for (const [kind, query] of [["ids", "Super Snowburst"], ["creatures", "Troll"], ["zones", "Cyclops"], ["quests", "KT"], ["phrases", "the"], ["bundles", "a"]]) {
    const r = await call("id_lookup", { kind, query, limit: 3 }); must(r.ok, `${kind}: ${r.text}`);
  }
});
await check("game_commands_help: all and search", async () => {
  const all = await call("game_commands_help"); must(all.ok && all.text.length > 1000, "no docs");
  const some = await call("game_commands_help", { search: "maxall" }); must(/maxall/i.test(some.text), "search found nothing");
});

console.log("files and shell");
await check("fs_write / fs_read / fs_search round trip", async () => {
  const f = "tools/imlight-mcp/test/tmp/hello.txt";
  const w = await call("fs_write", { path: f, content: "line one\nneedle-in-haystack\nline three\n" }); must(w.ok, w.text);
  const r = await call("fs_read", { path: f, from: 2, to: 2 }); must(r.ok && /2\tneedle-in-haystack/.test(r.text), r.text);
  const s = await call("fs_search", { pattern: "needle-in-haystack", ext: ".txt", limit: 5 }); must(/hello\.txt:2/.test(s.text), "search did not find the file: " + s.text);
});
await check("fs_read rejects a path outside the repo", async () => { const r = await call("fs_read", { path: "../../../Windows/win.ini" }); must(!r.ok && /outside/i.test(r.text), r.text); });
await check("shell_run: output and failure exit code", async () => {
  const a = await call("shell_run", { command: "Write-Output hi-from-shell" }); must(a.ok && /hi-from-shell/.test(a.text), a.text);
  const b = await call("shell_run", { command: "exit 3" }); must(!b.ok && /exit 3/.test(b.text), b.text);
});

console.log("scripts (server_eval)");
await check("expression and statements", async () => {
  must((await evalOk("1 + 41")).includes("42"), "expression");
  must((await evalOk("var x = 5; Print(\"printed\"); return x * 2;")).match(/printed[\s\S]*10/), "statements/Print");
});
await check("compile error is reported with a line", async () => { const r = await call("server_eval", { code: "int x = \"no\";" }); must(!r.ok && /CS\d+/.test(r.text) && /line/.test(r.text), r.text); });
await check("runtime exception is reported", async () => { const r = await call("server_eval", { code: "throw new InvalidOperationException(\"boom\");" }); must(!r.ok && /boom/.test(r.text), r.text); });
await check("Player() with no match names who is online", async () => { const r = await call("server_eval", { code: "Player(\"zzz-nobody\")" }); must(!r.ok && /No player matches/.test(r.text) && /Online:/.test(r.text), r.text); });
await check("Vars persist between scripts", async () => { await evalOk("Vars[\"smoke\"] = 123; return 1;"); must((await evalOk("(int) Vars[\"smoke\"]")).includes("123"), "Vars lost"); });
await check("a long script past the HTTP default still returns its result", async () => {
  const r = await call("server_eval", { code: "await Task.Delay(35000, Ct); return \"slept\";", timeoutMs: 60000 }); must(r.ok && /slept/.test(r.text), r.text); return `${Math.round(r.ms / 1000)}s`;
});
await check("a timeout cancels Ct-aware scripts", async () => {
  const r = await call("server_eval", { code: "await Task.Delay(60000, Ct); return \"never\";", timeoutMs: 1500 }); must(!r.ok && /Cancellation was requested/.test(r.text), r.text);
});
await check("scripts run concurrently (no global gate)", async () => {
  const t0 = Date.now();
  const rs = await Promise.all([1, 2, 3].map((i) => call("server_eval", { code: `await Task.Delay(2000, Ct); return ${i};`, timeoutMs: 20000 })));
  must(rs.every((r) => r.ok), "a script failed"); must(Date.now() - t0 < 5500, `took ${Date.now() - t0} ms; they ran one after another`);
});
await check("background script: job_list / job_get / log / result", async () => {
  const start = await call("server_eval", { code: "for (int i = 1; i <= 3; i++) { Print(\"step \" + i); await Task.Delay(300, Ct); } return \"finished\";", background: true });
  must(start.ok, start.text); const id = start.json().jobId; must(id, "no job id");
  const done = await call("job_get", { id, waitSeconds: 20 }); const j = done.json();
  must(j.status === "done", "status " + j.status); must(j.log.some((l) => /step 3/.test(l)), "log missing"); must(JSON.stringify(j.result).includes("finished"), "result missing");
  must((await call("job_list")).json().jobs.some((x) => x.id === id), "not listed");
});
await check("background script can be cancelled", async () => {
  const id = (await call("server_eval", { code: "Print(\"started\"); await Task.Delay(120000, Ct); return \"never\";", background: true })).json().jobId;
  await sleep(500);
  must((await call("job_cancel", { id })).ok, "cancel failed");
  const j = (await call("job_get", { id, waitSeconds: 10 })).json(); must(j.status === "cancelled", "status " + j.status);
});
await check("job_get on an unknown id is a clear error", async () => { const r = await call("job_get", { id: "job9999" }); must(!r.ok && /No job/.test(r.text), r.text); });

console.log("build");
await check("dotnet_build checkOnly works while the server runs", async () => {
  if (args.has("--quick")) return "skipped (--quick)";
  const r = await call("dotnet_build", { checkOnly: true }); must(r.ok && /0 Error\(s\)/.test(r.text), r.text.slice(-800));
});
await check("dotnet_build on the running server explains the lock instead of spamming", async () => {
  if (args.has("--quick")) return "skipped (--quick)";
  const r = await call("dotnet_build", { project: "src/Imlight.Director/Imlight.Director.csproj" });
  if (r.ok) return "built (no lock)";
  must(!/Beginning retry/.test(r.text) && /Stop the server first/.test(r.text), r.text.slice(0, 600));
});

console.log("game");
const hasPlayer = players.length > 0;
await check("game_players", async () => { const j = (await call("game_players")).json(); must(Array.isArray(j.players), "no players array"); return `${j.players.length} online`; });
if (!hasPlayer) { console.log("  (no player logged in: skipping game and combat tests)"); }
else {
  await check("game_command: debug gps", async () => { const r = await call("game_command", { command: "debug gps" }); must(r.ok && /Position/.test(r.text), r.text); });
  await check("game_command: unknown command gives the server's reply", async () => { const r = await call("game_command", { command: "definitely-not-a-command" }); must(r.ok && /not found/i.test(r.text), r.text); });
  await check("game_command: no-reply command carries a note", async () => { const r = await call("game_command", { command: "mod heal" }); must(r.ok && /no reply/i.test(r.text), r.text); });
  await check("game_command: wrong player is a clear error", async () => { const r = await call("game_command", { command: "debug gps", player: "zzz-nobody" }); must(!r.ok && /No matching player/.test(r.text), r.text); });
  await check("game_commands_run: sequence", async () => {
    const r = await call("game_commands_run", { commands: ["mod addgold 1", "mod heal", "debug gps"] }); must(r.ok, r.text);
    const j = r.json(); must(j.length === 3 && /Position/.test(JSON.stringify(j[2].replies)), r.text);
  });
  await check("game_command changes the save: level then speed", async () => {
    must((await call("game_command", { command: "mod speed 40" })).ok, "speed");
    must(/Position|Zone/.test((await call("game_command", { command: "debug gps" })).text), "gps after");
  });
  await check("combat_state outside a duel", async () => { const j = (await call("combat_state")).json(); must(j.inDuel === false, "should not be in a duel"); });
  await check("combat_move outside a duel is a clear error", async () => { const r = await call("combat_move", { action: "pass", waitSeconds: 1 }); must(!r.ok && /not in a duel/i.test(r.text), r.text); });

  if (!args.has("--combat")) console.log("  (run with --combat to teleport and fight a battle)");
  else {
    console.log("combat");
    await check("clear any leftover duel", async () => {
      const st = (await call("combat_state")).json();
      if (!st.inDuel) return "none";
      await call("combat_move", { action: "flee", waitSeconds: 90 });
      for (let i = 0; i < 30; i++) { await sleep(1000); if (!(await call("combat_state")).json().inDuel) return "fled"; }
      throw new Error("still in a duel");
    });
    await check("teleport to Cyclops Lane", async () => {
      await call("game_command", { command: "teleport WizardCity/WC_Streets/WC_Cyclops", waitMs: 500 });
      for (let i = 0; i < 40; i++) { await sleep(500); if (/Cyclops/.test((await call("game_players")).text)) { await sleep(2500); return; } }
      throw new Error("never arrived");
    });
    let creature;
    await check("combat_creatures lists nearest first", async () => {
      const j = (await call("combat_creatures", { limit: 5 })).json(); must(j.total > 0 && j.creatures[0].name, "none"); creature = j.creatures[0].name;
      must(j.creatures.every((c, i, a) => i === 0 || a[i - 1].distance <= c.distance), "not sorted by distance"); return `${j.total} creatures, nearest ${creature}`;
    });
    await check("combat_start with an unknown creature is a clear error", async () => { const r = await call("combat_start", { creature: "zzz-no-such-monster" }); must(!r.ok && /No creature matching/.test(r.text), r.text); });
    await check("combat_start begins a real duel", async () => {
      const r = await call("combat_start", { creature }); must(r.ok, r.text); const j = r.json();
      must(j.started && j.state.inDuel && j.state.circles.length >= 2, "no duel"); return `vs ${j.against}`;
    });
    await check("combat_start while fighting is refused", async () => { const r = await call("combat_start", {}); must(!r.ok && /already in a duel/.test(r.text), r.text); });
    await check("pip give light 7 gives ordinary pips", async () => {
      const r = await call("game_command", { command: "pip give light 7" }); must(r.ok, r.text);
      const st = (await call("combat_state")).json(); const me = st.circles.find((c) => c.you);
      must(me.pips.generic + me.pips.power >= 1 || Object.keys(me.pips.school).length, "pips: " + JSON.stringify(me.pips)); return JSON.stringify(me.pips);
    });
    await check("combat_state shows circles, pips and a hand with reasons", async () => {
      const j = (await call("combat_state")).json(); must(j.inDuel && j.yourSlot !== null, "state"); must(j.circles.some((c) => c.you && c.maxHp > 0), "no player circle");
      must(j.circles.some((c) => c.team === "Monster" && c.alive), "no enemy");
      must(j.hand.length > 0 && j.hand.every((h) => h.castable || h.whyNot), "hand entries need castable or whyNot");
    });
    await check("combat_move rejects an uncastable/unknown spell", async () => { const r = await call("combat_move", { spell: "zzz-no-such-spell", waitSeconds: 60 }); must(!r.ok && /No castable card/.test(r.text), r.text); });
    await check("combat_move casts for a round and the round plays out", async () => {
      // wait for a planning phase so the hand is the round's own, then pick a castable damage card
      for (let i = 0; i < 100; i++) { if ((await call("combat_state")).json().acceptingMoves) break; await sleep(300); }
      const st = (await call("combat_state")).json();
      if (!st.inDuel) return "duel already over";
      const card = st.hand.find((h) => h.castable && h.damage && !h.enchant);
      must(card, "no castable damage card in " + JSON.stringify(st.hand.map((h) => h.name)));
      const m = await call("combat_move", { card: card.card, waitSeconds: 60, waitForRound: true }); must(m.ok, m.text); return m.json().played;
    });
    await check("combat_auto plays the battle to the end", async () => {
      const st = (await call("combat_state")).json();
      if (!st.inDuel) return "duel already over";
      await call("game_command", { command: "pip give light 7" });
      const start = await call("combat_auto", { maxRounds: 30, refillPips: true }); must(start.ok, start.text); const id = start.json().jobId;
      let j; for (let i = 0; i < 12; i++) { j = (await call("job_get", { id, waitSeconds: 100 })).json(); if (j.status !== "running") break; }
      must(j.status === "done", `job ${j.status}: ${j.error ?? ""}\n${j.log.join("\n")}`);
      console.log("        " + j.log.slice(-6).join("\n        "));
      return `${JSON.stringify(j.result)}`;
    });
    await check("after the battle the player is out of the duel", async () => {
      await sleep(3000);
      must((await call("combat_state")).json().inDuel === false, "still in a duel");
      const w = await evalOk("Player(\"\").Wizard.IsInDuel"); must(/false/i.test(w), "wizard still flagged in duel: " + w);
    });
    await check("combat_start works again for a second battle, and flee ends it", async () => {
      const r = await call("combat_start", {}); must(r.ok, r.text);
      const f = await call("combat_move", { action: "flee", waitSeconds: 60 }); must(f.ok, f.text);
      await sleep(4000); must((await call("combat_state")).json().inDuel === false, "still in a duel after fleeing");
    });
  }
}

console.log(`\n${passed} passed, ${failed} failed`);
if (failed) console.log("failed:\n  " + failures.join("\n  "));
await client.close();
process.exit(failed ? 1 : 0);
