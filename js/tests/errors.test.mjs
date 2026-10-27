import assert from "node:assert/strict";
import { test } from "node:test";

import { DrishtiApiError } from "../dist/index.js";

test("error message uses the API's error.message instead of [object Object]", () => {
  const error = new DrishtiApiError(401, { error: { code: "unauthorized", message: "Invalid API key" } });
  assert.equal(error.message, "Drishti API error 401: Invalid API key");
  assert.deepEqual(error.body, { error: { code: "unauthorized", message: "Invalid API key" } });
});

test("other JSON bodies are rendered as JSON, text bodies as is", () => {
  assert.equal(new DrishtiApiError(404, { detail: "Not found" }).message, 'Drishti API error 404: {"detail":"Not found"}');
  assert.equal(new DrishtiApiError(502, "bad gateway").message, "Drishti API error 502: bad gateway");
  assert.equal(new DrishtiApiError(204, null).message, "Drishti API error 204: null");
});
