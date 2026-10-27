# OpenAPI sync

`openapi.json` is a snapshot of the public Drishti spec
(`https://developers.manasija.in/openapi.json`), stored with sorted keys so diffs stay readable.
The [OpenAPI sync workflow](../.github/workflows/openapi-sync.yml) keeps the SDKs in step with it.

## How it works

1. Every 6 hours, or when started by hand, the workflow runs `scripts/openapi-diff.mjs` to compare
   the live spec with this snapshot. It compares operations, parameters, request bodies, responses,
   and component schemas. Descriptions, summaries, examples, and `info` are ignored, so prose and
   billing-table edits in the API don't start any work.
2. If nothing SDK-relevant changed, the run stops. If a sync branch already exists for the same
   spec shape (`spec-sync/<hash>`), it stops too, so repeat runs don't open duplicate PRs.
3. Otherwise it writes the new snapshot and runs Claude Code
   ([`anthropics/claude-code-action`](https://github.com/anthropics/claude-code-action)) with the diff.
   Claude updates the `conformance/` cases first, then all five SDKs, their READMEs and the skill docs,
   bumps minor versions when a public surface changed, and runs every suite with
   `scripts/run-sdk-tests.sh`. Its tools are limited to reading and editing files and that one
   test command, and any change under `.github/` is discarded.
4. The workflow runs every suite again and opens a PR labelled `spec-sync` with the diff, Claude's
   notes, and the results. The PR is a draft if Claude or a suite failed. Nothing merges or publishes
   on its own: merging a PR with a version bump releases through the existing publish workflows,
   and PHP still needs a `vX.Y.0` tag after merge.

## Setup

Add two repository secrets (Settings → Secrets and variables → Actions):

| Secret | What it is |
| --- | --- |
| `ANTHROPIC_API_KEY` | An Anthropic API key for Claude Code. `CLAUDE_CODE_OAUTH_TOKEN` (from `claude setup-token`) works instead. |
| `SDK_SYNC_TOKEN` | A fine-grained personal access token (or GitHub App token) limited to this repository, with **Contents** and **Pull requests** read and write. PRs opened with the default `GITHUB_TOKEN` don't trigger the Tests workflow, so this token is needed. |

Without them, scheduled runs still report the diff in the run summary, then fail at "Check secrets".

## Running it by hand

```bash
# Report the diff only; no Claude, no PR
gh workflow run openapi-sync.yml -f dry_run=true

# Full run against another spec, e.g. the testing host
gh workflow run openapi-sync.yml -f spec_url=https://developers.testing.manasija.in/openapi.json
```

Locally:

```bash
node scripts/openapi-diff.mjs                      # exit 0: no change, 10: changes, 1: error
node scripts/openapi-diff.mjs --spec-file spec.json --report report.md
node scripts/openapi-diff.mjs --write              # accept the live spec as the new snapshot
bash scripts/run-sdk-tests.sh all                  # or: python js php java dotnet tooling
```

When you change an SDK by hand to match the API, run `node scripts/openapi-diff.mjs --write` in
the same PR so the next scheduled run doesn't redo the work.
