# Agent notes

Shared notes for coding agents working in `drishti-sdk`.

## Layout

- `python/`, `js/` (TypeScript), `php/` (with `composer.json` at the repo root), `java/`, `dotnet/`: the five hand-written SDKs.
- `conformance/`: language-neutral cases every SDK must pass (`rest/`, `websocket/`, `helpers/`, `constants.json`). See `conformance/README.md`.
- `openapi/openapi.json`: snapshot of the public API spec, used by the OpenAPI sync workflow (`openapi/README.md`).
- `mcp/`: the `drishti-mcp` installer. `skills/drishti-sdk/`: agent skill docs.

## Changing an SDK

1. Change the conformance case first, then every SDK, so they stay consistent. Java and .NET map conformance calls by hand in `ConformanceTest.invoke` and `ConformanceTests.Invoke`.
2. Keep the READMEs, `skills/drishti-sdk/references/usage.md`, and examples in step.
3. When the API changes, also run `node scripts/openapi-diff.mjs --write` so the snapshot matches.

## Tests

`bash scripts/run-sdk-tests.sh all`, or one suite at a time: `python`, `js`, `php`, `java`, `dotnet`, `tooling`. CI (`.github/workflows/test.yml`) runs the same commands across a version matrix.

## Releasing

Each package publishes when its version changes on `main`: `python/pyproject.toml` (PyPI), `js/package.json` (npm), `java/pom.xml` (Maven Central), `dotnet/src/Manasija.Drishti/Manasija.Drishti.csproj` (NuGet), `mcp/package.json` (npm). Also update the Java and .NET User-Agent strings (`DrishtiClient.java`, `DrishtiClientOptions.cs`), the root of `js/package-lock.json`, and version numbers in install snippets. PHP is released by pushing a `vX.Y.Z` tag. Use a minor bump when the public surface changes, including removals.

## OpenAPI sync

`.github/workflows/openapi-sync.yml` checks the live spec every 6 hours. On an SDK-relevant change it opens a `spec-sync` issue listing the changes with a checklist. Resolve it in one PR that updates the conformance cases and SDKs and runs `node scripts/openapi-diff.mjs --write`.
