import assert from "node:assert/strict";
import { test } from "node:test";

import {
  diffSpecs,
  formatReport,
  modelHash,
  serializeSpec,
} from "./openapi-diff.mjs";

function baseSpec() {
  return {
    openapi: "3.1.0",
    info: { title: "Drishti API", version: "1.0.0", description: "Intro" },
    paths: {
      "/v1/announcements": {
        get: {
          summary: "List announcements",
          description: "Paginated announcements.",
          parameters: [
            { in: "query", name: "symbols", schema: { type: "array", items: { type: "string" } } },
            { in: "query", name: "detailed", schema: { type: "boolean", default: false } },
            { in: "query", name: "important", schema: { type: "boolean", default: false } },
          ],
          responses: {
            200: {
              description: "OK",
              content: {
                "application/json": {
                  schema: { $ref: "#/components/schemas/AnnouncementPage" },
                },
              },
            },
          },
        },
      },
      "/v1/news": {
        get: {
          parameters: [
            {
              in: "query",
              name: "sentiment",
              schema: { type: "string", enum: ["positive", "negative"] },
            },
          ],
          responses: { 200: { description: "OK" } },
        },
      },
    },
    components: {
      schemas: {
        Announcement: {
          type: "object",
          description: "One filing.",
          properties: {
            id: { type: "string" },
            important: { type: "boolean", description: "Flag." },
          },
          required: ["id"],
        },
      },
    },
  };
}

test("no changes between identical specs", () => {
  assert.deepEqual(diffSpecs(baseSpec(), baseSpec()), []);
});

test("prose-only edits are ignored and keep the same hash", () => {
  const next = baseSpec();
  next.info.description = "A completely rewritten intro with billing tables.";
  next.paths["/v1/announcements"].get.summary = "Fetch announcements";
  next.paths["/v1/announcements"].get.parameters[1].description = "New help text";
  next.components.schemas.Announcement.properties.important.description = "Reworded.";
  next.components.schemas.Announcement.properties.id.examples = ["abc"];

  assert.deepEqual(diffSpecs(baseSpec(), next), []);
  assert.equal(modelHash(next), modelHash(baseSpec()));
});

test("a removed query param and field are reported", () => {
  const next = baseSpec();
  next.paths["/v1/announcements"].get.parameters.pop();
  delete next.components.schemas.Announcement.properties.important;

  const changes = diffSpecs(baseSpec(), next);
  assert.deepEqual(
    changes.map(({ area, kind, key, operation, schema }) => ({ area, kind, key, operation, schema })),
    [
      {
        area: "parameter",
        kind: "removed",
        key: "query:important",
        operation: "GET /v1/announcements",
        schema: undefined,
      },
      {
        area: "property",
        kind: "removed",
        key: "important",
        operation: undefined,
        schema: "Announcement",
      },
    ],
  );
  assert.notEqual(modelHash(next), modelHash(baseSpec()));

  const report = formatReport(changes);
  assert.match(report, /Query param `important` on `GET \/v1\/announcements` removed/);
  assert.match(report, /Field `Announcement.important` removed/);
});

test("an enum change is reported as a changed param", () => {
  const next = baseSpec();
  next.paths["/v1/news"].get.parameters[0].schema.enum.push("neutral");

  const [change] = diffSpecs(baseSpec(), next);
  assert.equal(change.area, "parameter");
  assert.equal(change.kind, "changed");
  assert.equal(change.key, "query:sentiment");
  assert.match(formatReport([change]), /neutral/);
});

test("new endpoints, schemas, and required fields are reported", () => {
  const next = baseSpec();
  next.paths["/v1/block-deals"] = { get: { responses: { 200: { description: "OK" } } } };
  next.components.schemas.BlockDeal = { type: "object", properties: { id: { type: "string" } } };
  next.components.schemas.Announcement.required.push("important");

  const summary = diffSpecs(baseSpec(), next).map(({ area, kind, key }) => `${area} ${kind} ${key}`);
  assert.deepEqual(summary.sort(), [
    "operation added GET /v1/block-deals",
    "schema added BlockDeal",
    "schema changed Announcement",
  ]);
});

test("snapshots serialise with sorted keys", () => {
  assert.equal(serializeSpec({ b: 1, a: { d: 2, c: 3 } }), '{\n  "a": {\n    "c": 3,\n    "d": 2\n  },\n  "b": 1\n}\n');
});
