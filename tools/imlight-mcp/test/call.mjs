// Calls one tool of the Imlight MCP server from the command line, for debugging:
//   node test/call.mjs combat_creatures '{"limit":5}'
import { Client } from "@modelcontextprotocol/sdk/client/index.js";
import { StdioClientTransport } from "@modelcontextprotocol/sdk/client/stdio.js";
import path from "node:path";
import { fileURLToPath } from "node:url";

const here = path.dirname(fileURLToPath(import.meta.url));
const [tool, rawArgs] = process.argv.slice(2);
if (!tool) { console.error("usage: node test/call.mjs <tool> [jsonArgs]"); process.exit(2); }
const client = new Client({ name: "call", version: "1" });
await client.connect(new StdioClientTransport({ command: "node", args: [path.join(here, "..", "index.js")] }));
const r = await client.callTool({ name: tool, arguments: rawArgs ? JSON.parse(rawArgs) : {} }, undefined, { timeout: 600000 });
console.log(r.content.map((c) => c.text).join("\n"));
await client.close();
process.exit(r.isError ? 1 : 0);
