<img src="./mcp/assets/logo-mark.svg" alt="Drishti logo" style="height: 4em; width: auto; margin: 0;" /> 

# Drishti SDK

Official HTTP and WebSocket client libraries for Alpha API (`/v1`) using API key authentication.

Drishti is an [Indian stock market API](https://drishti.manasija.in) for building
research, monitoring, and agent workflows around listed companies. Use these
SDKs when an application needs structured market context, exchange filings,
announcements, earnings, conference calls, news, or real-time updates.

| Package | Directory | Package name |
|---------|-----------|--------------|
| Python SDK | `python/` | `drishti-sdk` on PyPI (import `drishti_sdk`) |
| TypeScript SDK | `js/` | `drishti-sdk` on npm |
| PHP SDK | `php/` | `manasija/drishti-sdk` on Packagist (namespace `Drishti\`) |
| Java SDK | `java/` | `io.github.techmanasija:drishti-sdk` (Maven, package `in.manasija.drishti`) |
| .NET SDK | `dotnet/` | `Manasija.Drishti` on NuGet (namespace `Manasija.Drishti`) |
| MCP installer | `mcp/` | `drishti-mcp` on npm |

See `python/README.md`, `js/README.md`, `php/README.md`, `java/README.md`, `dotnet/README.md`, and `mcp/README.md` for install and usage.
All SDKs pass the shared [conformance suite](conformance/README.md), so they behave the same.
A scheduled [OpenAPI sync](openapi/README.md) workflow opens an issue when the public API spec changes in a way the SDKs need to follow.

**Getting started:** [create a Drishti API key](https://platform.manasija.in/)
and read the [Drishti API documentation](https://developers.manasija.in/docs).

PHP install (PHP 8.2+):

```bash
composer require manasija/drishti-sdk
```

Java install (Java 17+), Maven:

```xml
<dependency>
  <groupId>io.github.techmanasija</groupId>
  <artifactId>drishti-sdk</artifactId>
  <version>1.1.1</version>
</dependency>
```

.NET install (.NET 8+ or .NET Standard 2.0):

```bash
dotnet add package Manasija.Drishti
```

JavaScript / TypeScript install options:

```bash
npm install drishti-sdk
```

Browser bundle via CDN (demos and prototypes only):

```html
<script src="https://cdn.jsdelivr.net/npm/drishti-sdk"></script>
<script>
  const { DrishtiClient } = DrishtiSDK;
</script>
```

> **Warning:** API keys in the browser are visible to users. Prefer npm on the server for production, or proxy Drishti through your backend.

## MCP installer (`drishti-mcp`)

The `mcp/` package is a CLI wizard that adds the hosted Drishti MCP endpoint to
supported LLM clients (Cursor, VS Code, Zed, Codex, Claude Code, Antigravity CLI).

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
npx skills add manasijatech/drishti-sdk
```
