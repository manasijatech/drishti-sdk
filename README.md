# Market-Stack SDK

Official HTTP and WebSocket client libraries for Alpha API (`/v1`) using API key authentication.

| Language   | Directory | Package name      |
|-----------|-----------|-------------------|
| Python    | `python/` | `drishti-sdk` on PyPI (import `drishti_sdk`) |
| TypeScript | `js/`    | `drishti-sdk` on npm |

See `python/README.md` and `js/README.md` for install and usage.

## MCP installer (`drishti-mcp`)

The `mcp/` package is a CLI wizard that adds the hosted Drishti MCP endpoint to
supported LLM clients (Cursor, VS Code, Zed, Codex, Claude Code).

<p align="center">
  <img src="./mcp/assets/logo-mark.svg" alt="Drishti logo" width="96" />
</p>

```bash
npx drishti-mcp
```

See `mcp/README.md` for details. Package source lives in `mcp/` on npm as
[`drishti-mcp`](https://www.npmjs.com/package/drishti-mcp) after publish.

## Agent Skill

This repository includes a Drishti SDK skill for coding agents under
`skills/drishti-sdk/`.

Install it with:

```bash
npx skills add manasijatech/Market-Stack-SDK
```
