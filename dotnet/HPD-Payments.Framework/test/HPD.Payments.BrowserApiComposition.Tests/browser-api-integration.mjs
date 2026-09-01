import assert from "node:assert/strict";
import { PaymentsBrowserClient } from "../../clients/typescript/hpd-payments-browser/src/index.js";

const [baseUrl, expectedProfile] = process.argv.slice(2);
assert.ok(baseUrl);
assert.ok(expectedProfile);

const client = new PaymentsBrowserClient({ baseUrl });
assert.deepEqual(await client.health(), { status: "ready", version: "hpd.payments.api.v1" });
assert.deepEqual(await client.manifest(), { version: "hpd.payments.api.v1", authorityLogic: false });

const wrongVersion = await fetch(`${baseUrl}/hpd/payments/v1/manifest`, {
  headers: { "x-hpd-payments-version": "hpd.payments.api.v0" },
  redirect: "error",
  credentials: "omit",
});
assert.equal(wrongVersion.status, 426);
assert.equal(wrongVersion.headers.get("x-hpd-payments-profile"), expectedProfile);
assert.deepEqual(await wrongVersion.json(), {
  error: "payments.api.versionUnsupported",
  version: "hpd.payments.api.v1",
});

console.log(`PASS Browser/API process composition profile=${expectedProfile}`);
