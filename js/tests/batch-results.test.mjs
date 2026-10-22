// Regression tests: batch results are JSONL (application/jsonl) and must not be JSON-decoded.
import assert from "node:assert/strict";
import { test } from "node:test";

import { DrishtiClient } from "../dist/index.js";

const JSONL = '{"custom_id":"a","status":"succeeded"}\n{"custom_id":"b","status":"failed"}\n';

function client() {
  return new DrishtiClient({
    apiKey: "test-key",
    baseUrl: "https://api.test",
    fetchImpl: async (url) => {
      assert.equal(new URL(url).pathname, "/v1/batch/jobs/job_1/results");
      return new Response(JSONL, { status: 200, headers: { "content-type": "application/jsonl" } });
    },
  });
}

test("getBatchJobsJobIdResults returns the raw JSONL text", async () => {
  assert.equal(await client().getBatchJobsJobIdResults({ job_id: "job_1" }), JSONL);
});

test("getBatchJobsJobIdResultsParsed splits the JSONL lines", async () => {
  assert.deepEqual(await client().getBatchJobsJobIdResultsParsed({ job_id: "job_1" }), [
    { custom_id: "a", status: "succeeded" },
    { custom_id: "b", status: "failed" },
  ]);
});
