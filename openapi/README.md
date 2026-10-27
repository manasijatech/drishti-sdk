# OpenAPI sync

`openapi.json` is a snapshot of the public Drishti spec
(`https://developers.manasija.in/openapi.json`), stored with sorted keys so diffs stay readable.
The [OpenAPI sync workflow](../.github/workflows/openapi-sync.yml) watches for changes to it.

## How it works

1. Every 6 hours, or when started by hand, the workflow runs `scripts/openapi-diff.mjs` to compare
   the live spec with this snapshot. It compares operations, parameters, request bodies, responses,
   and component schemas. Descriptions, summaries, examples, and `info` are ignored, so prose and
   billing-table edits in the API don't open anything.
2. If something SDK-relevant changed, it opens an issue labelled `spec-sync` with the list of
   changes and a checklist: conformance cases first, then each SDK, docs, versions, and the
   snapshot. The title carries a short hash of the spec's shape, so later runs see the open issue
   and don't repeat it. If the spec changes again before that's done, a new issue opens with every
   change since the snapshot, and the older one closes as superseded.
3. Someone works through the checklist in a PR and runs `node scripts/openapi-diff.mjs --write`
   there, so the snapshot matches the new spec and the next run is quiet again.

It needs no secrets: the built-in `GITHUB_TOKEN` opens the issues.

## Running it by hand

```bash
# Report the diff in the run summary without opening an issue
gh workflow run openapi-sync.yml -f dry_run=true

# Compare against another spec, e.g. the testing host
gh workflow run openapi-sync.yml -f spec_url=https://developers.testing.manasija.in/openapi.json
```

Locally:

```bash
node scripts/openapi-diff.mjs                      # exit 0: no change, 10: changes, 1: error
node scripts/openapi-diff.mjs --spec-file spec.json --report report.md
node scripts/openapi-diff.mjs --write              # accept the live spec as the new snapshot
bash scripts/run-sdk-tests.sh all                  # or: python js php java dotnet tooling
```

When you change an SDK to match the API, run `node scripts/openapi-diff.mjs --write` in the same
PR and close the `spec-sync` issue with it.
