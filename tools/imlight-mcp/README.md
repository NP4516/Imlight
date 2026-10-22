# Imlight MCP

An optional local MCP server for inspecting and operating an Imlight development server. It exposes server status, logs, builds and tests, game commands, combat helpers, database queries, and repository tools.

## Setup

1. Install Node.js and the .NET SDK required by Imlight.
2. From the repository root, install the MCP dependencies:

   ```sh
   npm --prefix tools/imlight-mcp install
   ```

3. Copy `.mcp.example.json` to the MCP client's configuration location, or add its `imlight` server entry to the client's existing configuration. Keep the command's working directory at the Imlight repository root.
4. In `src/Imlight.Director/Config/Imlight.ini`, set `[Agent Bridge] Enabled = true`, choose a long random `Token`, and keep the listener on `127.0.0.1`.
5. Start or restart Imlight.Director, then launch the MCP client.

The MCP server reads the bridge port and token from `Imlight.ini`. `IMLIGHT_ROOT`, `IMLIGHT_BRIDGE_URL`, and `IMLIGHT_BRIDGE_TOKEN` can override the repository path and bridge connection for an isolated setup. Do not commit private tokens or local MCP client configuration.

## Security

The bridge is disabled by default, binds only to loopback, and requires the configured token on every request. Keep it local and treat the token as a password. The `server_eval` tool compiles and executes arbitrary C# inside the live Director process with access to Imlight internals. `shell_run`, `fs_write`, `server_start`, `server_stop`, and game command/combat tools also perform actions on the host or live server. Only connect a trusted MCP client and use these tools on a server you control. This is a developer control surface, not a sandbox or a public network API.

## Tools

The MCP server exposes tools for:

- Server health, logs, errors, startup, shutdown, builds, and tests.
- Logged-in players, in-game commands, combat state and moves, and background jobs.
- C# evaluation in the running Director, actor inspection, and database queries.
- Repository file search/read/write, shell commands, configuration, command help, and ID lookup.

The MCP process uses stdio for the MCP protocol; the Director bridge uses authenticated HTTP on loopback.
