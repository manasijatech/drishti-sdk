// Runs the shared cross-SDK conformance fixtures in ../../conformance against the JS SDK.
// Fixture format is documented in conformance/README.md. The Python SDK runs the same fixtures
// from python/tests/test_conformance.py, so a behaviour change must land in every SDK.
import assert from "node:assert/strict";
import { readFileSync, readdirSync } from "node:fs";
import { describe, test } from "node:test";

import {
  ALERT_TYPES,
  BATCH_JOB_STATUSES,
  BATCH_JOB_TERMINAL_STATUSES,
  DEFAULT_BASE_URL,
  DRISHTI_WS_PRODUCTS,
  DrishtiApiError,
  DrishtiClient,
  buildBatchInputJsonl,
  buildWebSocketUrl,
  parseBatchResultJsonl,
  parseWebSocketMessage,
} from "../dist/index.js";
import * as sdk from "../dist/index.js";
import { PERMANENT_SUBSCRIBE_ERROR_CODES } from "../dist/websocket.js";

const CONFORMANCE_DIR = new URL("../../conformance/", import.meta.url);
const TEST_API_KEY = "test-key";
const TEST_BASE_URL = "https://api.test";

function readJson(relativePath) {
  return JSON.parse(readFileSync(new URL(relativePath, CONFORMANCE_DIR), "utf8"));
}

function fixtureFiles(subdir) {
  return readdirSync(new URL(`${subdir}/`, CONFORMANCE_DIR))
    .filter((name) => name.endsWith(".json"))
    .sort()
    .map((name) => ({ name, cases: readJson(`${subdir}/${name}`).cases }));
}

const camel = (name) => name.replace(/_([a-z])/g, (_whole, letter) => letter.toUpperCase());

// --- REST ----------------------------------------------------------------------------------

/** Translate canonical fixture arguments to this SDK's call shape. */
function jsArgs(args) {
  const out = {};
  for (const [key, value] of Object.entries(args)) {
    if (key === "file_name") {
      out.filename = value;
    } else if (key === "file_content") {
      out.file = new Blob([value], { type: "application/jsonl" });
    } else if (key === "content_retention") {
      out.contentRetention = value;
    } else if (key === "poll_interval_ms") {
      out.pollIntervalMs = value;
    } else {
      out[key] = value;
    }
  }
  return out;
}

function buildResponse(spec) {
  if (spec.network_error) {
    throw new TypeError("simulated network error");
  }
  const status = spec.status ?? 200;
  if (status === 204) {
    return new Response(null, { status });
  }
  const body = "json" in spec ? JSON.stringify(spec.json) : (spec.text ?? "");
  return new Response(body, { status, headers: { "content-type": spec.content_type ?? "application/json" } });
}

async function runRestCase(testCase) {
  const responses = testCase.responses ?? [testCase.response ?? { status: 200, json: {} }];
  const seen = [];
  const fetchImpl = async (url, init = {}) => {
    seen.push({ url: new URL(url), method: init.method, headers: new Headers(init.headers), body: init.body });
    if (seen.length > responses.length) {
      throw new Error(`unexpected extra request #${seen.length}: ${init.method} ${url}`);
    }
    return buildResponse(responses[seen.length - 1]);
  };
  const clientConfig = testCase.client ?? {};
  let result;
  let error;
  try {
    const client = new DrishtiClient({
      apiKey: clientConfig.api_key ?? TEST_API_KEY,
      baseUrl: clientConfig.base_url ?? TEST_BASE_URL,
      headers: clientConfig.headers,
      fetchImpl,
      retry: { maxRetries: clientConfig.max_retries ?? 2, initialDelayMs: 0, maxDelayMs: 0 },
    });
    const { method, positional = [], args = {} } = testCase.call;
    const fn = client[camel(method)];
    assert.equal(typeof fn, "function", `DrishtiClient.${camel(method)} is missing`);
    result = positional.length > 0
      ? await fn.call(client, ...positional, ...Object.values(args))
      : await fn.call(client, jsArgs(args));
  } catch (caught) {
    error = caught;
  }
  return { seen, result, error };
}

function queryMultimap(url) {
  const out = {};
  for (const [key, value] of url.searchParams) {
    (out[key] ??= []).push(value);
  }
  return out;
}

async function multipart(body) {
  assert.ok(body instanceof FormData, "expected a multipart/form-data body");
  const fields = {};
  const files = {};
  for (const [name, value] of body.entries()) {
    if (typeof value === "string") {
      fields[name] = value;
    } else {
      files[name] = { filename: value.name, content: await value.text() };
    }
  }
  return { fields, files };
}

async function assertRequest(request, expected) {
  assert.equal(request.method, expected.method);
  assert.equal(request.url.pathname, expected.path);
  assert.deepEqual(queryMultimap(request.url), expected.query ?? {});
  for (const [name, value] of Object.entries(expected.headers ?? {})) {
    assert.equal(request.headers.get(name), value, `header ${name}`);
  }
  if (expected.json_body !== undefined && expected.json_body !== null) {
    assert.deepEqual(JSON.parse(request.body), expected.json_body);
  }
  if (expected.multipart !== undefined) {
    assert.deepEqual(await multipart(request.body), expected.multipart);
  }
}

describe("REST conformance", () => {
  for (const file of fixtureFiles("rest")) {
    describe(file.name, () => {
      for (const testCase of file.cases) {
        test(testCase.id, async () => {
          const { seen, result, error } = await runRestCase(testCase);

          const expectedRequests = testCase.expect_requests
            ?? (testCase.expect_request ? [testCase.expect_request] : undefined);
          if (expectedRequests !== undefined) {
            assert.equal(seen.length, expectedRequests.length, "request count");
            for (let i = 0; i < expectedRequests.length; i += 1) {
              await assertRequest(seen[i], expectedRequests[i]);
            }
          }
          if (testCase.expect_request_count !== undefined) {
            assert.equal(seen.length, testCase.expect_request_count, "request count");
          }

          if (testCase.expect_error) {
            const expectedError = testCase.expect_error;
            assert.ok(error, `expected ${expectedError.type}, got result ${JSON.stringify(result)}`);
            if (expectedError.type === "api_error") {
              assert.ok(error instanceof DrishtiApiError, `expected DrishtiApiError, got ${error}`);
              assert.equal(error.statusCode, expectedError.status);
              assert.deepEqual(error.body, expectedError.body);
            } else {
              assert.ok(error instanceof Error && !(error instanceof DrishtiApiError), `got ${error}`);
            }
            return;
          }
          if (error) {
            throw error;
          }
          if ("expect_result" in testCase) {
            assert.deepEqual(result, testCase.expect_result);
          }
        });
      }
    });
  }
});

// --- Helpers -------------------------------------------------------------------------------

describe("helper conformance", () => {
  for (const file of fixtureFiles("helpers")) {
    for (const testCase of file.cases) {
      test(testCase.id, () => {
        if (testCase.function === "build_batch_input_jsonl") {
          if (testCase.expect_error) {
            assert.throws(() => buildBatchInputJsonl(testCase.input));
            return;
          }
          const text = buildBatchInputJsonl(testCase.input);
          assert.ok(text.endsWith("\n"));
          assert.deepEqual(text.trimEnd().split("\n").map((line) => JSON.parse(line)), testCase.expect_lines);
        } else if (testCase.function === "parse_batch_result_jsonl") {
          assert.deepEqual(parseBatchResultJsonl(testCase.input), testCase.expect_result);
        } else {
          assert.fail(`unknown helper ${testCase.function}`);
        }
      });
    }
  }
});

// --- WebSocket -----------------------------------------------------------------------------

/** Convert a parsed event to the canonical snake_case shape used by the fixtures. */
function canonicalEvent(event) {
  const out = {};
  for (const [key, value] of Object.entries(event)) {
    out[key.replace(/[A-Z]/g, (letter) => `_${letter.toLowerCase()}`)] = value === undefined ? null : value;
  }
  if (out.kind === "error" && !("code" in out)) {
    out.code = null;
  }
  return out;
}

describe("WebSocket conformance", () => {
  for (const testCase of readJson("websocket/parse.json").cases) {
    test(testCase.id, () => {
      assert.deepEqual(canonicalEvent(parseWebSocketMessage(testCase.raw)), testCase.expect);
    });
  }
  for (const testCase of readJson("websocket/subscribe.json").cases) {
    test(testCase.id, () => {
      assert.equal(typeof sdk.buildSubscribeMessage, "function", "buildSubscribeMessage is not exported");
      assert.deepEqual(sdk.buildSubscribeMessage(testCase.options), testCase.expect);
    });
  }
  for (const testCase of readJson("websocket/url.json").cases) {
    test(testCase.id, () => {
      assert.equal(buildWebSocketUrl(testCase.base_url, testCase.api_key), testCase.expect);
    });
  }
});

// --- Constants -----------------------------------------------------------------------------

test("constants conformance", () => {
  const expected = readJson("constants.json");
  assert.equal(DEFAULT_BASE_URL, expected.default_base_url);
  assert.deepEqual([...DRISHTI_WS_PRODUCTS], expected.ws_products);
  assert.deepEqual([...PERMANENT_SUBSCRIBE_ERROR_CODES].sort(), expected.permanent_subscribe_error_codes);
  assert.deepEqual([...ALERT_TYPES], expected.alert_types);
  assert.deepEqual([...BATCH_JOB_STATUSES], expected.batch_job_statuses);
  assert.deepEqual([...BATCH_JOB_TERMINAL_STATUSES], expected.batch_job_terminal_statuses);
});
