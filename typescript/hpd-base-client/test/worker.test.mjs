import assert from "node:assert/strict";
import test from "node:test";
import { createBaseActivationWorkerClient } from "../dist/worker.js";

const graph = Object.freeze({
  text: { kind: "string", minLength: 1, maxLength: 64, format: "plain" },
  input: { kind: "object", additionalProperties: false, properties: [{ name: "text", wireName: "text", typeId: "text", required: true, nullable: false, disclosureShape: "none" }] },
  result: { kind: "object", additionalProperties: false, properties: [{ name: "text", wireName: "text", typeId: "text", required: true, nullable: false, disclosureShape: "none" }] },
});
const definition = Object.freeze({ id: "graph.execute", version: 1, inputTypeId: "input", resultTypeId: "result", typeGraph: graph });

test("worker subpath creates graph-validated identified activation requests", async () => {
  let request;
  const worker = createBaseActivationWorkerClient({
    url: "https://base.test/base/",
    fetch: async (_url, init) => {
      request = JSON.parse(new TextDecoder().decode(init.body));
      return Response.json({ activationId: "activation-1", state: "pending", disposition: "committed" },
        { headers: { "X-Correlation-ID": "c" } });
    },
  }, definition);

  const result = await worker.enqueue({ text: "hello" }, { idempotencyKey: "enqueue-1" });

  assert.equal(result.ok, true);
  assert.equal(result.value.activationId, "activation-1");
  assert.deepEqual(request.payload, { text: "hello" });
  assert.equal(request.definitionId, "graph.execute");
  assert.equal(request.definitionVersion, 1);
  assert.equal(request.identity.idempotencyKey, "enqueue-1");
  assert.equal(request.identity.fingerprint.length, 32);
  await assert.rejects(
    () => worker.enqueue({ text: "", extra: true }, { idempotencyKey: "enqueue-2" }),
    /base\.activation\.invalid/u);
});

test("worker authority is absent from the root client and fails closed in browser globals", async () => {
  const root = await import("../dist/index.js");
  assert.equal("createBaseActivationWorkerClient" in root, false);

  Object.defineProperty(globalThis, "document", { value: {}, configurable: true });
  try {
    assert.throws(
      () => createBaseActivationWorkerClient({ url: "https://base.test/base/", fetch: globalThis.fetch }, definition),
      /base\.activation\.browserForbidden/u);
  } finally {
    delete globalThis.document;
  }
});
