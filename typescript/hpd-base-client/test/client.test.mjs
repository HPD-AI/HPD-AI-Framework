import assert from "node:assert/strict";
import test from "node:test";
import { collection, createBaseClient, field, parseBaseJson } from "../dist/index.js";
import { BaseRealtimeManager } from "../dist/realtime.js";

const schema = Object.freeze({ protocolMajor: 2, schemaGeneration: "1", digest: `sha256:${"0".repeat(64)}`, audience: "application", features: { files: false, realtime: true, batch: true, controlOperations: [] }, reads: {}, collections: { documents: collection({ id: "documents", fields: { title: field("stable-title", "stored_title", ["equal", "notEqual"]) }, operations: ["get", "query", "patch", "batch", "watch", "realtime"], pagination: "seek", maxPageSize: 100, vectorIndexes: {} }) } });

test("query uses the canonical RecordQuery wire shape", async () => {
  let wire;
  const base = createBaseClient({ schema, url: "https://base.test/base/", fetch: async (_url, init) => { wire = JSON.parse(new TextDecoder().decode(init.body)); return Response.json({ items: [], page: { hasMore: false } }, { headers: { "X-Correlation-ID": "c" } }); } });
  const result = await base.documents.query({ where: base.documents.fields.title.eq("hello"), orderBy: base.documents.fields.title.asc(), select: ["stable-title"], include: ["owner"], count: "exact", take: 10 }).execute();
  assert.equal(result.ok, true);
  assert.deepEqual(wire, { filter: { kind: "compare", field: "stable-title", operator: "equal", value: { kind: "string", string: "hello" } }, sort: [{ field: "stable-title", direction: "asc" }], select: ["stable-title"], include: [{ navigationId: "owner" }], count: "exact", page: { mode: "cursor", limit: 10 } });
});

test("identified mutation retries byte-identically with one logical correlation", async () => {
  const attempts = [];
  const base = createBaseClient({ schema, url: "https://base.test/base/", fetch: async (_url, init) => {
    attempts.push({ body: new Uint8Array(await new Response(init.body).arrayBuffer()), correlation: new Headers(init.headers).get("X-Correlation-ID"), mutation: new Headers(init.headers).get("Idempotency-Key") });
    if (attempts.length === 1) throw new TypeError("network");
    return Response.json({
      outcome: "committed",
      items: [{ itemId: "mutation", index: 0, kind: "patch", disposition: "committed", record: { collectionId: "documents", id: "d1", payload: { kind: "json", json: { stored_title: "new" } }, metadata: {} } }]
    }, { headers: { "X-Correlation-ID": attempts[0].correlation } });
  } });
  const result = await base.documents.patch("d1", { title: "new" });
  assert.equal(result.ok, true); assert.equal(result.value.payload.json.title, "new"); assert.equal(attempts.length, 2);
  assert.deepEqual(attempts[0].body, attempts[1].body); assert.equal(attempts[0].correlation, attempts[1].correlation); assert.equal(attempts[0].mutation, attempts[1].mutation);
  assert.equal(new TextDecoder().decode(attempts[0].body), '{"mode":"atomic","operations":[{"itemId":"mutation","collectionId":"documents","kind":"patch","recordId":"d1","patch":{"patch":{"kind":"json","json":{"stored_title":"new"}}}}]}');
});

test("realtime v2 joins after welcome and resumes from the last delivered durable cursor", async () => {
  const sockets = [];
  const manager = new BaseRealtimeManager("https://example.test/base", () => { const socket = new FakeSocket(); sockets.push(socket); return socket; });
  const delivered = [];
  manager.subscribeFeed("documents", { kind: "durable", filter: {} }, async item => { delivered.push(item.cursor); });
  await waitUntil(() => sockets.length === 1 && sockets[0].onmessage !== null); const first = sockets[0]; assert.equal(first.sent.length, 0);
  first.receive(JSON.stringify({ protocol: 2, kind: "welcome", connectionId: "connection", connectionEpoch: "epoch-1", heartbeatIntervalMs: 1000, maxInboundBytes: 1024, maxChannels: 8 }));
  const firstJoin = JSON.parse(first.sent[0]); assert.equal(firstJoin.channel.kind, "durable");
  first.receive(JSON.stringify({ protocol: 2, kind: "joined", connectionId: "connection", connectionEpoch: "epoch-1", ref: firstJoin.ref, channelEpoch: "channel-1", delivery: "durable-at-least-once" }));
  first.receive(JSON.stringify({ protocol: 2, kind: "durableRecordEvent", connectionId: "connection", connectionEpoch: "epoch-1", ref: firstJoin.ref, channelEpoch: "channel-1", cursor: "cursor-1", event: { eventId: "event", collectionId: "documents", recordId: "record", operation: "patch", occurredAt: "2026-01-01T00:00:00Z" } }));
  await new Promise(resolve => setTimeout(resolve, 0)); assert.deepEqual(delivered, ["cursor-1"]);
  first.close(); await waitUntil(() => sockets.length === 2 && sockets[1].onmessage !== null, 1000); const second = sockets[1];
  second.receive(JSON.stringify({ protocol: 2, kind: "welcome", connectionId: "connection-2", connectionEpoch: "epoch-2", heartbeatIntervalMs: 1000, maxInboundBytes: 1024, maxChannels: 8 }));
  const resumed = JSON.parse(second.sent.at(-1)); assert.equal(resumed.channel.kind, "resume"); assert.equal(resumed.channel.cursor, "cursor-1"); manager.close();
});

test("realtime rejects duplicate JSON properties before dispatch", async () => {
  const socket = new FakeSocket(); const manager = new BaseRealtimeManager("https://example.test/base", () => socket);
  manager.subscribeFeed("documents", { kind: "live", filter: {} }, () => undefined); await waitUntil(() => socket.onmessage !== null);
  socket.receive('{"protocol":2,"kind":"welcome","kind":"welcome","connectionId":"c","connectionEpoch":"e","heartbeatIntervalMs":1000,"maxInboundBytes":1024,"maxChannels":8}');
  assert.equal(socket.closedCode, 1008); manager.close();
});

test("realtime rejects frames before welcome and unknown channel references", async () => {
  const socket = new FakeSocket(); const manager = new BaseRealtimeManager("https://example.test/base", () => socket);
  manager.subscribeFeed("documents", { kind: "live", filter: {} }, () => undefined); await waitUntil(() => socket.onmessage !== null);
  socket.receive(JSON.stringify({ protocol: 2, kind: "heartbeatAck", connectionId: "c", connectionEpoch: "e", heartbeatId: "h" }));
  assert.equal(socket.closedCode, 1008); manager.close();
  const second = new FakeSocket(); const other = new BaseRealtimeManager("https://example.test/base", () => second);
  other.subscribeFeed("documents", { kind: "live", filter: {} }, () => undefined); await waitUntil(() => second.onmessage !== null);
  second.receive(JSON.stringify({ protocol: 2, kind: "welcome", connectionId: "c", connectionEpoch: "e", heartbeatIntervalMs: 1000, maxInboundBytes: 1024, maxChannels: 8 }));
  second.receive(JSON.stringify({ protocol: 2, kind: "joined", connectionId: "c", connectionEpoch: "e", ref: "unknown", channelEpoch: "channel", delivery: "live-at-most-once" }));
  assert.equal(second.closedCode, 1008); other.close();
});

test("control-plane backup succeeds only after the complete length-framed multipart body", async () => {
  const boundary = `hpd-base-${"a".repeat(32)}`; const artifact = new Uint8Array([1, 2, 3, 4]);
  const manifest = { envelopeVersion: 1, providerKind: "test", providerVersion: "1", nativeSqliteVersion: "", baseContractVersion: "1", storeIdentityDigest: "sha256:test", schemaGeneration: 1, schemaBaselineId: "baseline", schemaChecksum: "sha256:schema", restoreEpoch: 1, createdAt: "2026-01-01T00:00:00Z", providerPayloadLength: 4, providerPayloadSha256: "sha256:payload", logicalPartitions: [], receiptFormatVersion: 1, journalFormatVersion: 1, collectionHistoryFormatVersion: 1, payloadEncryptedAtRest: true, externalKeyReferenceKind: null };
  const json = new TextEncoder().encode(JSON.stringify(manifest)); const text = value => new TextEncoder().encode(value);
  const body = join(text(`--${boundary}\r\nContent-Type: application/json\r\nContent-Length: ${json.length}\r\n\r\n`), json, text(`\r\n--${boundary}\r\nContent-Type: application/octet-stream\r\nContent-Length: ${artifact.length}\r\n\r\n`), artifact, text(`\r\n--${boundary}--\r\n`));
  const controlSchema = { ...schema, audience: "controlPlane", features: { ...schema.features, controlOperations: ["base.admin.backup.create"] } };
  const client = createBaseClient({ schema: controlSchema, url: "https://base.test/base/", fetch: async () => new Response(body, { headers: { "Content-Type": `multipart/mixed; boundary=${boundary}`, "Content-Length": String(body.length), "X-Correlation-ID": "c" } }) });
  const written = []; const destination = new WritableStream({ write(chunk) { written.push(chunk); } });
  const result = await client.$control.createBackup({ storeId: "primary" }, destination);
  assert.equal(result.ok, true); assert.deepEqual(join(...written), artifact);
  const truncatedClient = createBaseClient({ schema: controlSchema, url: "https://base.test/base/", fetch: async () => new Response(body.slice(0, -3), { headers: { "Content-Type": `multipart/mixed; boundary=${boundary}`, "Content-Length": String(body.length) } }) });
  const truncated = await truncatedClient.$control.createBackup({ storeId: "primary" }, new WritableStream());
  assert.equal(truncated.ok, false); assert.equal(truncated.error.code, "base.client.responseInvalid");
});

test("vector search preserves binary32 inputs and validates disclosed dot-product measures", async () => {
  let wire;
  const vectorSchema = { ...schema, collections: { documents: collection({ ...schema.collections.documents, operations: ["vector"], vectorIndexes: { semantic: { id: "semantic", dimensions: 2, measure: "dotProductSimilarity", direction: "higherIsNearer" } } }) } };
  const base = createBaseClient({ schema: vectorSchema, url: "https://base.test/base/", fetch: async (_url, init) => { wire = JSON.parse(new TextDecoder().decode(init.body)); return Response.json({ matches: [{ record: { collectionId: "documents", id: "d1", payload: { kind: "json", json: { stored_title: "match" } }, metadata: {} }, rank: 1, measure: { function: "dotProductSimilarity", value: 0.75, direction: "higherIsNearer", normalizedRelevance: 0.875 } }], vectorIndexId: "semantic", vectorIndexGeneration: "42", providerId: "inMemory", consistencyToken: "opaque" }, { headers: { "X-Correlation-ID": "v" } }); } });
  const result = await base.documents.vector(base.documents.vectorIndexes.semantic).nearest([0.1, -0]).measures("include").execute();
  assert.equal(result.ok, true); assert.equal(result.value.matches[0].record.payload.json.title, "match");
  assert.deepEqual(wire.vector, [Math.fround(0.1), 0]); assert.equal(wire.measureDisclosure, "include"); assert.equal(wire.consistency, "current");
});

test("the bounded JSON codec rejects duplicate and lossy numeric tokens before materialization", () => {
  assert.throws(() => parseBaseJson('{"value":1,"value":2}'));
  assert.throws(() => parseBaseJson('{"value":1e400}'));
  assert.throws(() => parseBaseJson('{"value":1e-400}'));
  assert.throws(() => parseBaseJson('{"value":-0}'));
  assert.deepEqual(parseBaseJson('{"value":1.25}'), { value: 1.25 });
});

class FakeSocket {
  readyState = 1; onopen = null; onmessage = null; onclose = null; onerror = null; sent = []; closedCode = undefined;
  send(data) { this.sent.push(data); }
  close(code = 1000) { this.closedCode = code; this.readyState = 3; this.onclose?.({ code }); }
  receive(data) { this.onmessage?.({ data }); }
}
async function waitUntil(predicate, timeout = 500) { const end = Date.now() + timeout; while (!predicate()) { if (Date.now() >= end) throw new Error("timeout"); await new Promise(resolve => setTimeout(resolve, 1)); } }
function join(...parts) { const result = new Uint8Array(parts.reduce((sum, part) => sum + part.length, 0)); let offset = 0; for (const part of parts) { result.set(part, offset); offset += part.length; } return result; }
