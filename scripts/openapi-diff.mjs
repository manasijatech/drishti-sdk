#!/usr/bin/env node
// Compares the live Drishti OpenAPI spec with the committed snapshot (openapi/openapi.json)
// and reports changes that matter to the SDKs: operations, parameters, request bodies,
// response schemas, and component schemas. Prose (descriptions, summaries, examples, info)
// is ignored, so documentation edits in the API don't trigger SDK work.
//
// Usage:
//   node scripts/openapi-diff.mjs [--spec-url URL | --spec-file PATH] [--snapshot PATH]
//                                 [--report PATH] [--write]
// Exit codes: 0 no SDK-relevant change, 10 changes found, 1 error.
// When GITHUB_OUTPUT is set, writes `changed`, `count`, and `hash` outputs.

import { createHash } from "node:crypto";
import { appendFileSync, readFileSync, writeFileSync } from "node:fs";
import { pathToFileURL } from "node:url";

export const DEFAULT_SPEC_URL = "https://developers.manasija.in/openapi.json";
export const DEFAULT_SNAPSHOT = "openapi/openapi.json";
export const CHANGES_EXIT_CODE = 10;

const METHODS = ["get", "post", "put", "patch", "delete"];
// Documentation-only keys; changing these never changes how an SDK calls the API.
const PROSE_KEYS = new Set([
  "description",
  "summary",
  "title",
  "example",
  "examples",
  "externalDocs",
]);

/** Recursively sorts object keys so the same spec always serialises the same way. */
export function sortKeys(value) {
  if (Array.isArray(value)) {
    return value.map(sortKeys);
  }
  if (value && typeof value === "object") {
    return Object.fromEntries(
      Object.keys(value)
        .sort()
        .map((key) => [key, sortKeys(value[key])]),
    );
  }
  return value;
}

/** The snapshot file format: sorted keys, 2-space indent, trailing newline. */
export function serializeSpec(spec) {
  return `${JSON.stringify(sortKeys(spec), null, 2)}\n`;
}

/** Drops prose keys at every level. Property names inside `properties` are kept as-is. */
export function stripProse(value, parentKey = "") {
  if (Array.isArray(value)) {
    return value.map((item) => stripProse(item));
  }
  if (value && typeof value === "object") {
    return Object.fromEntries(
      Object.entries(value)
        .filter(([key]) => parentKey === "properties" || !PROSE_KEYS.has(key))
        .map(([key, item]) => [key, stripProse(item, key)]),
    );
  }
  return value;
}

function mediaSchemas(content) {
  if (!content) {
    return null;
  }
  return Object.fromEntries(
    Object.entries(content).map(([type, media]) => [type, media?.schema ?? null]),
  );
}

/**
 * The parts of a spec an SDK depends on, keyed for comparison:
 * `operations["GET /v1/news"]` and `schemas["NewsItem"]`.
 */
export function buildModel(spec) {
  const operations = {};
  for (const [path, item] of Object.entries(spec.paths ?? {})) {
    const shared = item?.parameters ?? [];
    for (const method of METHODS) {
      const operation = item?.[method];
      if (!operation) {
        continue;
      }
      const parameters = {};
      for (const param of [...shared, ...(operation.parameters ?? [])]) {
        parameters[`${param.in}:${param.name}`] = stripProse({
          required: param.required ?? false,
          schema: param.schema ?? null,
          style: param.style,
          explode: param.explode,
          deprecated: param.deprecated,
        });
      }
      operations[`${method.toUpperCase()} ${path}`] = {
        deprecated: operation.deprecated ?? false,
        parameters,
        requestBody: operation.requestBody
          ? stripProse({
              required: operation.requestBody.required ?? false,
              content: mediaSchemas(operation.requestBody.content),
            })
          : null,
        responses: Object.fromEntries(
          Object.entries(operation.responses ?? {}).map(([status, response]) => [
            status,
            stripProse(mediaSchemas(response?.content)),
          ]),
        ),
      };
    }
  }
  const schemas = Object.fromEntries(
    Object.entries(spec.components?.schemas ?? {}).map(([name, schema]) => [
      name,
      stripProse(schema),
    ]),
  );
  return { operations, schemas };
}

const same = (a, b) =>
  JSON.stringify(sortKeys(a ?? null)) === JSON.stringify(sortKeys(b ?? null));

function compareKeyed(before, after, onChange) {
  const changes = [];
  for (const key of Object.keys(after)) {
    if (!(key in before)) {
      changes.push({ kind: "added", key });
    } else if (!same(before[key], after[key])) {
      changes.push(...onChange(key, before[key], after[key]));
    }
  }
  for (const key of Object.keys(before)) {
    if (!(key in after)) {
      changes.push({ kind: "removed", key });
    }
  }
  return changes;
}

/** A flat list of SDK-relevant changes between two specs. */
export function diffSpecs(beforeSpec, afterSpec) {
  const before = buildModel(beforeSpec);
  const after = buildModel(afterSpec);
  const changes = [];

  for (const change of compareKeyed(
    before.operations,
    after.operations,
    (key, old, next) => {
      const nested = [];
      for (const param of compareKeyed(
        old.parameters,
        next.parameters,
        (name, a, b) => [{ kind: "changed", key: name, before: a, after: b }],
      )) {
        nested.push({ area: "parameter", operation: key, ...param });
      }
      if (!same(old.requestBody, next.requestBody)) {
        nested.push({
          area: "requestBody",
          operation: key,
          kind: "changed",
          before: old.requestBody,
          after: next.requestBody,
        });
      }
      if (!same(old.responses, next.responses)) {
        nested.push({
          area: "response",
          operation: key,
          kind: "changed",
          before: old.responses,
          after: next.responses,
        });
      }
      if (old.deprecated !== next.deprecated) {
        nested.push({
          area: "operation",
          kind: "changed",
          key,
          before: { deprecated: old.deprecated },
          after: { deprecated: next.deprecated },
        });
      }
      return nested;
    },
  )) {
    changes.push(change.area ? change : { area: "operation", ...change });
  }

  for (const change of compareKeyed(before.schemas, after.schemas, (name, old, next) => {
    const nested = compareKeyed(
      old.properties ?? {},
      next.properties ?? {},
      (property, a, b) => [{ kind: "changed", key: property, before: a, after: b }],
    ).map((property) => ({ area: "property", schema: name, ...property }));
    const { properties: _old, ...oldRest } = old;
    const { properties: _next, ...nextRest } = next;
    if (!same(oldRest, nextRest)) {
      nested.push({
        area: "schema",
        kind: "changed",
        key: name,
        before: oldRest,
        after: nextRest,
      });
    }
    return nested;
  })) {
    changes.push(change.area ? change : { area: "schema", ...change });
  }

  return changes;
}

/** Stable id for a spec's SDK-relevant shape; prose-only edits keep the same hash. */
export function modelHash(spec) {
  return createHash("sha256")
    .update(JSON.stringify(sortKeys(buildModel(spec))))
    .digest("hex")
    .slice(0, 12);
}

const compact = (value) => `\`${JSON.stringify(sortKeys(value ?? null))}\``;

function describe(change) {
  switch (change.area) {
    case "operation":
      return change.kind === "changed"
        ? `Operation \`${change.key}\` changed: ${compact(change.before)} → ${compact(change.after)}`
        : `Operation \`${change.key}\` ${change.kind}`;
    case "parameter": {
      const [location, name] = change.key.split(":");
      const label = `${location} param \`${name}\` on \`${change.operation}\``;
      return change.kind === "changed"
        ? `${label} changed: ${compact(change.before)} → ${compact(change.after)}`
        : `${label[0].toUpperCase()}${label.slice(1)} ${change.kind}`;
    }
    case "requestBody":
      return `Request body of \`${change.operation}\` changed: ${compact(change.before)} → ${compact(change.after)}`;
    case "response":
      return `Responses of \`${change.operation}\` changed: ${compact(change.before)} → ${compact(change.after)}`;
    case "schema":
      return change.kind === "changed"
        ? `Schema \`${change.key}\` changed: ${compact(change.before)} → ${compact(change.after)}`
        : `Schema \`${change.key}\` ${change.kind}`;
    case "property":
      return change.kind === "changed"
        ? `Field \`${change.schema}.${change.key}\` changed: ${compact(change.before)} → ${compact(change.after)}`
        : `Field \`${change.schema}.${change.key}\` ${change.kind}`;
    default:
      return JSON.stringify(change);
  }
}

/** Markdown summary grouped by area, for the PR body and the Claude prompt. */
export function formatReport(changes, { specUrl, hash } = {}) {
  if (changes.length === 0) {
    return "No SDK-relevant changes in the OpenAPI spec.\n";
  }
  const sections = [
    ["Operations", ["operation"]],
    ["Parameters", ["parameter"]],
    ["Request bodies", ["requestBody"]],
    ["Responses", ["response"]],
    ["Schemas and fields", ["schema", "property"]],
  ];
  const lines = [
    "## OpenAPI spec changes",
    "",
    `${changes.length} SDK-relevant change${changes.length === 1 ? "" : "s"}${specUrl ? ` in ${specUrl}` : ""}${hash ? ` (shape \`${hash}\`)` : ""}.`,
  ];
  for (const [title, areas] of sections) {
    const items = changes.filter((change) => areas.includes(change.area));
    if (items.length > 0) {
      lines.push("", `### ${title}`, "", ...items.map((change) => `- ${describe(change)}`));
    }
  }
  return `${lines.join("\n")}\n`;
}

function parseArgs(argv) {
  const args = { snapshot: DEFAULT_SNAPSHOT, specUrl: DEFAULT_SPEC_URL };
  for (let index = 0; index < argv.length; index++) {
    const flag = argv[index];
    const next = () => {
      const value = argv[++index];
      if (value === undefined) {
        throw new Error(`${flag} needs a value`);
      }
      return value;
    };
    if (flag === "--spec-url") args.specUrl = next();
    else if (flag === "--spec-file") args.specFile = next();
    else if (flag === "--snapshot") args.snapshot = next();
    else if (flag === "--report") args.report = next();
    else if (flag === "--write") args.write = true;
    else throw new Error(`Unknown flag ${flag}`);
  }
  return args;
}

async function loadSpec({ specFile, specUrl }) {
  if (specFile) {
    return JSON.parse(readFileSync(specFile, "utf8"));
  }
  const response = await fetch(specUrl, {
    headers: { accept: "application/json" },
    signal: AbortSignal.timeout(30_000),
  });
  if (!response.ok) {
    throw new Error(`GET ${specUrl} failed: ${response.status}`);
  }
  const spec = await response.json();
  if (!spec?.openapi || !spec?.paths) {
    throw new Error(`${specUrl} did not return an OpenAPI document`);
  }
  return spec;
}

async function main() {
  const args = parseArgs(process.argv.slice(2));
  const live = await loadSpec(args);
  const snapshot = JSON.parse(readFileSync(args.snapshot, "utf8"));
  const changes = diffSpecs(snapshot, live);
  const hash = modelHash(live);
  const report = formatReport(changes, {
    hash,
    specUrl: args.specFile ?? args.specUrl,
  });

  process.stdout.write(report);
  if (args.report) {
    writeFileSync(args.report, report);
  }
  if (args.write) {
    writeFileSync(args.snapshot, serializeSpec(live));
  }
  if (process.env.GITHUB_OUTPUT) {
    appendFileSync(
      process.env.GITHUB_OUTPUT,
      `changed=${changes.length > 0}\ncount=${changes.length}\nhash=${hash}\n`,
    );
  }
  return changes.length > 0 ? CHANGES_EXIT_CODE : 0;
}

if (import.meta.url === pathToFileURL(process.argv[1] ?? "").href) {
  main().then(
    (code) => process.exit(code),
    (error) => {
      console.error(error instanceof Error ? error.message : error);
      process.exit(1);
    },
  );
}
