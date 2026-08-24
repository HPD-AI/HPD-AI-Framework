import assert from "node:assert/strict";
import test from "node:test";
import {
  decodeEvidence,
  PaymentsApiWireVersion,
  PaymentsBrowserClient,
  PaymentsBrowserProtocolError,
} from "../src/index.js";

function jsonResponse(value, status = 200) {
  return new Response(JSON.stringify(value), { status, headers: { "content-type": "application/json" } });
}

test("uses only the closed route and exact wire header", async () => {
  const calls = [];
  const client = new PaymentsBrowserClient({
    baseUrl: "https://payments.example.test",
    fetch: async (url, init) => {
      calls.push({ url, init });
      return jsonResponse({ status: "ready", version: PaymentsApiWireVersion });
    },
  });
  assert.deepEqual(await client.health(), { status: "ready", version: PaymentsApiWireVersion });
  assert.equal(calls.length, 1);
  assert.equal(calls[0].url, "https://payments.example.test/hpd/payments/v1/health");
  assert.equal(calls[0].init.headers["x-hpd-payments-version"], PaymentsApiWireVersion);
  assert.equal(calls[0].init.credentials, "omit");
  assert.equal(calls[0].init.redirect, "error");
});

test("rejects version mismatch without retry", async () => {
  let attempts = 0;
  const client = new PaymentsBrowserClient({
    baseUrl: "https://payments.example.test",
    fetch: async () => {
      attempts += 1;
      return jsonResponse({ version: "hpd.payments.api.v2", authorityLogic: false });
    },
  });
  await assert.rejects(client.manifest(), (error) => error instanceof PaymentsBrowserProtocolError && error.code === "payments.browser.versionUnsupported");
  assert.equal(attempts, 1);
});

test("rejects authority claims and unexpected response fields", async () => {
  const authority = new PaymentsBrowserClient({
    baseUrl: "https://payments.example.test",
    fetch: async () => jsonResponse({ version: PaymentsApiWireVersion, authorityLogic: true }),
  });
  await assert.rejects(authority.manifest(), { code: "payments.browser.authorityBoundaryInvalid" });

  const expanded = new PaymentsBrowserClient({
    baseUrl: "https://payments.example.test",
    fetch: async () => jsonResponse({ status: "ready", version: PaymentsApiWireVersion, secret: "must-not-surface" }),
  });
  await assert.rejects(expanded.health(), (error) => error.code === "payments.browser.healthInvalid" && !error.message.includes("must-not-surface"));
});

test("preserves PossibleDispatch and redacted external reference exactly", () => {
  const evidence = decodeEvidence({
    operationId: "op-1",
    state: "PossibleDispatch",
    externalReference: null,
    wireVersion: PaymentsApiWireVersion,
  });
  assert.deepEqual(evidence, {
    operationId: "op-1",
    state: "PossibleDispatch",
    externalReference: null,
    wireVersion: PaymentsApiWireVersion,
  });
});

test("allows HTTP only for loopback and forwards abort signals", async () => {
  const controller = new AbortController();
  const client = new PaymentsBrowserClient({
    baseUrl: "http://127.0.0.1:8080",
    fetch: async (_url, init) => {
      assert.equal(init.signal, controller.signal);
      return jsonResponse({ status: "ready", version: PaymentsApiWireVersion });
    },
  });
  await client.health({ signal: controller.signal });
  assert.throws(() => new PaymentsBrowserClient({ baseUrl: "http://payments.example.test" }), { code: "payments.browser.baseUrlUnsupported" });
  assert.throws(() => new PaymentsBrowserClient({ baseUrl: "https://user:secret@payments.example.test" }), { code: "payments.browser.baseUrlInvalid" });
});
