# drishti-mcp

CLI wizard that configures supported AI clients to use the hosted [Drishti MCP](https://drishti.manasija.in/docs-md/guides/drishti-mcp) server. It does not run an MCP server locally; it only writes client config (or invokes `claude mcp add` for Claude Code).

It writes client-specific MCP entries for `https://mcp.drishti.manasija.in` with a
Bearer token. Shapes vary by client, for example:

- **Cursor / Claude Desktop** (`mcpServers`): `{ "url", "headers" }`
- **VS Code** (`servers`): `{ "type": "http", "url", "headers" }`
- **Zed** (`context_servers`): `{ "enabled": true, "url", "headers" }`

The API key is entered during setup and written directly into the client
config. The MCP URL is baked into the installer so the supported clients get a
direct remote MCP server entry.

## Usage

From this repository:

```bash
node mcp/cli.js
```

After publishing the package, the same installer can be run with:

```bash
npx drishti-mcp
```

The installer auto-detects supported clients and updates the matching config
files when they are present:

- Cursor: `~/.cursor/mcp.json`
- VS Code: `~/Library/Application Support/Code/User/mcp.json`, `~/.config/Code/User/mcp.json`, or `%APPDATA%\Code\User\mcp.json`
- Zed: `~/Library/Application Support/Zed/settings.json`, `~/.config/zed/settings.json`, or `%APPDATA%\Zed\settings.json`
- Codex: `~/.codex/config.toml`
- Claude Code: `claude mcp add --transport http` (user scope, via Claude Code CLI)
- Claude Desktop: `claude_desktop_config.json`

When you run it in an interactive terminal, it shows a small picker so you can
choose which detected clients to configure, with a Drishti banner at the top.
