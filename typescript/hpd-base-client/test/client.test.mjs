import assert from "node:assert/strict";
import test from "node:test";
import { baseRedacted, collection, createBaseClient, decodeBaseJson, decodeBaseValue, encodeBaseJson, executeModuleMutation, executeSelectionMutation, field, isBaseRedacted, moduleMutation, parseBaseJson, read, selectionMutation } from "../dist/index.js";
import { BaseRealtimeManager } from "../dist/realtime.js";

const basicGraph = Object.freeze({
  title: { kind: "string", minLength: 1, maxLength: 100, format: "plain" },
  record: { kind: "object", additionalProperties: false, properties: [{ name: "title", wireName: "stored_title", typeId: "title", required: true, nullable: false, disclosureShape: "none" }] },
  create: { kind: "object", additionalProperties: false, properties: [{ name: "title", wireName: "stored_title", typeId: "title", required: true, nullable: false, disclosureShape: "none" }] },
  replace: { kind: "object", additionalProperties: false, properties: [{ name: "title", wireName: "stored_title", typeId: "title", required: true, nullable: false, disclosureShape: "none" }] },
  patch: { kind: "object", additionalProperties: false, properties: [{ name: "title", wireName: "stored_title", typeId: "title", required: false, nullable: false, disclosureShape: "none" }] }
});
const schema = Object.freeze({ protocolMajor: 2, schemaGeneration: "1", digest: `sha256:${"0".repeat(64)}`, audience: "application", features: { files: false, realtime: true, batch: true, controlOperations: [] }, typeGraph: basicGraph, reads: {}, collections: { documents: collection({ id: "documents", recordTypeId: "record", createTypeId: "create", replaceTypeId: "replace", patchTypeId: "patch", fields: { title: field("stable-title", "stored_title", ["equal", "notEqual"], "title") }, operations: ["get", "query", "create", "patch", "replace", "batch", "watch", "realtime"], pagination: "seek", maxPageSize: 100, vectorIndexes: {} }) } });

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

test("selection mutation uses the closed graph and collection patch wire codec", async () => {
  const graph = Object.freeze({ ...basicGraph,
    selectionPatch: { kind: "selection-patch", patchTypeId: "patch" },
    selectionQuery: { kind: "selection-query", maximumNodes: 8, maximumDepth: 4, maximumLiterals: 8, maximumTake: 10 },
    selectionPrevious: { kind: "selection-previous-state", maximumFields: 4 },
    count: { kind: "integer", minimum: "0", maximum: "10", wire: "number" },
    outcome: { kind: "enum", values: ["committed"] }, disposition: { kind: "enum", values: ["committed", "duplicate"] },
    selectionResult: { kind: "object", additionalProperties: false, properties: [{ name: "selectedCount", wireName: "selectedCount", typeId: "count", required: true, nullable: false, disclosureShape: "none" }, { name: "mutatedCount", wireName: "mutatedCount", typeId: "count", required: true, nullable: false, disclosureShape: "none" }, { name: "outcome", wireName: "outcome", typeId: "outcome", required: true, nullable: false, disclosureShape: "none" }, { name: "requestDisposition", wireName: "requestDisposition", typeId: "disposition", required: true, nullable: false, disclosureShape: "none" }] },
    selectionRequest: { kind: "object", additionalProperties: false, properties: [{ name: "query", wireName: "query", typeId: "selectionQuery", required: true, nullable: false, disclosureShape: "none" }, { name: "patch", wireName: "patch", typeId: "selectionPatch", required: true, nullable: false, disclosureShape: "none" }, { name: "previousState", wireName: "previousState", typeId: "selectionPrevious", required: true, nullable: false, disclosureShape: "none" }] }
  });
  let body; const operation = selectionMutation({ route: "/selection", mutationKind: "mergePatch", maximumRequestBodyBytes: 4096, requestTypeId: "selectionRequest", resultTypeId: "selectionResult", typeGraph: graph });
  const transport = { jsonDocument: async (_method, _route, bytes) => { body = new TextDecoder().decode(bytes); return { ok: true, value: { selectedCount: 1, mutatedCount: 1, outcome: "committed", requestDisposition: "committed" }, correlationId: "c" }; } };
  const request = { query: { filter: { kind: "compare", field: "stable-title", operator: "equal", value: { kind: "string", string: "old" } }, sort: [{ field: "stable-title", direction: "asc" }, { field: "id", direction: "asc" }], take: 1 }, patch: { title: "new" }, previousState: { revision: { kind: "none" }, fields: [] } };
  const result = await executeSelectionMutation(transport, operation, request);
  assert.equal(result.ok, true); assert.equal(body, '{"query":{"filter":{"field":"stable-title","kind":"compare","operator":"equal","value":{"kind":"string","string":"old"}},"sort":[{"direction":"asc","field":"stable-title"},{"direction":"asc","field":"id"}],"take":1},"patch":{"patch":{"kind":"fieldMap","fields":{"stored_title":"new"}}},"previousState":{"fields":[],"revision":{"kind":"none"}}}');
  await assert.rejects(() => executeSelectionMutation(transport, operation, { query: { filter: { kind: "extension" }, sort: [{ field: "id", direction: "asc" }], take: 1 }, patch: { title: "new" }, previousState: { revision: { kind: "none" }, fields: [] } }), /responseInvalid/);
});

test("module mutation uses graph-owned request and result codecs with identified replay", async () => {
  const graph = Object.freeze({ generation: { kind: "module-generation" }, request: { kind: "object", additionalProperties: false, properties: [] }, result: { kind: "object", additionalProperties: false, properties: [{ name: "generation", wireName: "generation", typeId: "generation", required: true, nullable: false, disclosureShape: "none" }] } });
  const operation = moduleMutation({ route: "/base/module-mutations/v1/payments.apply:execute", maximumRequestBytes: 1024, audience: "system", requestTypeId: "request", resultTypeId: "result", typeGraph: graph });
  let key; let body;
  const transport = { jsonDocument: async (_method, _route, bytes, _signal, idempotencyKey) => { key = idempotencyKey; body = new TextDecoder().decode(bytes); return { ok: true, value: { disposition: "new", outcome: "committed", result: { generation: "1" } }, correlationId: "c" }; } };
  const result = await executeModuleMutation(transport, operation, {}, { idempotencyKey: "payment-1" });
  assert.equal(result.ok, true); assert.equal(result.value.result.generation, "1"); assert.equal(key, "payment-1"); assert.equal(body, "{}");
  await assert.rejects(() => executeModuleMutation(transport, operation, { extra: true }, { idempotencyKey: "payment-2" }), /responseInvalid/u);
});

test("realtime v2 joins after welcome and resumes from the last delivered durable cursor", async () => {
  const sockets = [];
  const manager = new BaseRealtimeManager("https://example.test/base", () => { const socket = new FakeSocket(); sockets.push(socket); return socket; });
  const delivered = [];
  manager.subscribeFeed("documents", { kind: "durable", filter: {} }, async item => { delivered.push(item.cursor); }, value => value);
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

test("subject-authority controls invalidate live state and advance durable cursors only after processing", async () => {
  const subjectGraph = Object.freeze({ ...basicGraph, subject: { kind: "subjectReference", contractId: "hpd.auth.user-subject", contractVersion: 1, subjectIdKind: "guid", maximumSubjectIdUtf8Bytes: 36, authorityEpochBytes: 16, incarnationBytes: 16 } });
  const subjectCollections = { documents: collection({ ...schema.collections.documents, fields: { ...schema.collections.documents.fields, owner: field("owner", "owner", ["equal"], "subject") } }) };
  const sockets = [];
  const manager = new BaseRealtimeManager("https://example.test/base", () => { const socket = new FakeSocket(); sockets.push(socket); return socket; }, undefined, undefined, subjectGraph, subjectCollections);
  manager.subscribeFeed("documents", { kind: "durable", filter: {} }, () => undefined, value => value);
  await waitUntil(() => sockets.length === 1 && sockets[0].onmessage !== null); const first = sockets[0];
  first.receive(JSON.stringify({ protocol: 2, kind: "welcome", connectionId: "c1", connectionEpoch: "e1", heartbeatIntervalMs: 1000, maxInboundBytes: 2048, maxChannels: 8 }));
  const join = JSON.parse(first.sent[0]);
  first.receive(JSON.stringify({ protocol: 2, kind: "joined", connectionId: "c1", connectionEpoch: "e1", ref: join.ref, channelEpoch: "ch1", delivery: "durable-at-least-once" }));
  first.receive(JSON.stringify({ protocol: 2, kind: "durableSubjectAuthorityChanged", connectionId: "c1", connectionEpoch: "e1", ref: join.ref, channelEpoch: "ch1", contractId: "hpd.auth.user-subject", contractVersion: 1, stateGeneration: "2", cursor: "control-2" }));
  await new Promise(resolve => setTimeout(resolve, 0)); first.close();
  await waitUntil(() => sockets.length === 2 && sockets[1].onmessage !== null, 1000); const second = sockets[1];
  second.receive(JSON.stringify({ protocol: 2, kind: "welcome", connectionId: "c2", connectionEpoch: "e2", heartbeatIntervalMs: 1000, maxInboundBytes: 2048, maxChannels: 8 }));
  const resumed = JSON.parse(second.sent.at(-1)); assert.equal(resumed.channel.kind, "resume"); assert.equal(resumed.channel.cursor, "control-2");
  second.receive(JSON.stringify({ protocol: 2, kind: "joined", connectionId: "c2", connectionEpoch: "e2", ref: join.ref, channelEpoch: "ch2", delivery: "durable-at-least-once" }));
  second.receive(JSON.stringify({ protocol: 2, kind: "durableSubjectAuthorityChanged", connectionId: "c2", connectionEpoch: "e2", ref: join.ref, channelEpoch: "ch2", contractId: "unknown", contractVersion: 1, stateGeneration: "3", cursor: "control-3" }));
  await waitUntil(() => second.closedCode === 1008); manager.close();
});

test("durable subject controls reject conflicting cursor replay and generation regression", async () => {
  const subjectGraph = Object.freeze({ ...basicGraph, subject: { kind: "subjectReference", contractId: "hpd.auth.user-subject", contractVersion: 1, subjectIdKind: "guid", maximumSubjectIdUtf8Bytes: 36, authorityEpochBytes: 16, incarnationBytes: 16 } });
  const subjectCollections = { documents: collection({ ...schema.collections.documents, fields: { ...schema.collections.documents.fields, owner: field("owner", "owner", ["equal"], "subject") } }) };
  const open = async () => {
    const socket = new FakeSocket();
    const manager = new BaseRealtimeManager("https://example.test/base", () => socket, undefined, undefined, subjectGraph, subjectCollections);
    manager.subscribeFeed("documents", { kind: "durable", filter: {} }, () => undefined, value => value);
    await waitUntil(() => socket.onmessage !== null);
    socket.receive(JSON.stringify({ protocol: 2, kind: "welcome", connectionId: "c", connectionEpoch: "e", heartbeatIntervalMs: 1000, maxInboundBytes: 2048, maxChannels: 8 }));
    const join = JSON.parse(socket.sent[0]);
    socket.receive(JSON.stringify({ protocol: 2, kind: "joined", connectionId: "c", connectionEpoch: "e", ref: join.ref, channelEpoch: "ch", delivery: "durable-at-least-once" }));
    return { manager, socket, ref: join.ref };
  };
  const conflict = await open();
  conflict.socket.receive(JSON.stringify({ protocol: 2, kind: "durableSubjectAuthorityChanged", connectionId: "c", connectionEpoch: "e", ref: conflict.ref, channelEpoch: "ch", contractId: "hpd.auth.user-subject", contractVersion: 1, stateGeneration: "2", cursor: "cursor-2" }));
  await new Promise(resolve => setTimeout(resolve, 0));
  conflict.socket.receive(JSON.stringify({ protocol: 2, kind: "durableSubjectAuthorityChanged", connectionId: "c", connectionEpoch: "e", ref: conflict.ref, channelEpoch: "ch", contractId: "hpd.auth.user-subject", contractVersion: 1, stateGeneration: "3", cursor: "cursor-2" }));
  await waitUntil(() => conflict.socket.closedCode === 1008); conflict.manager.close();

  const regression = await open();
  regression.socket.receive(JSON.stringify({ protocol: 2, kind: "durableSubjectAuthorityChanged", connectionId: "c", connectionEpoch: "e", ref: regression.ref, channelEpoch: "ch", contractId: "hpd.auth.user-subject", contractVersion: 1, stateGeneration: "3", cursor: "cursor-3" }));
  await new Promise(resolve => setTimeout(resolve, 0));
  regression.socket.receive(JSON.stringify({ protocol: 2, kind: "durableSubjectAuthorityChanged", connectionId: "c", connectionEpoch: "e", ref: regression.ref, channelEpoch: "ch", contractId: "hpd.auth.user-subject", contractVersion: 1, stateGeneration: "2", cursor: "cursor-4" }));
  await waitUntil(() => regression.socket.closedCode === 1008); regression.manager.close();
});

test("subject controls mark the addressed registered read stale before its rerun", async () => {
  const subjectGraph = Object.freeze({ ...basicGraph, subject: { kind: "subjectReference", contractId: "hpd.auth.user-subject", contractVersion: 1, subjectIdKind: "guid", maximumSubjectIdUtf8Bytes: 36, authorityEpochBytes: 16, incarnationBytes: 16 } });
  const socket = new FakeSocket();
  const snapshots = [];
  const manager = new BaseRealtimeManager("https://example.test/base", () => socket, undefined, undefined, subjectGraph, schema.collections);
  manager.subscribeRead("current-user", {}, snapshot => snapshots.push(snapshot), value => value);
  await waitUntil(() => socket.onmessage !== null);
  socket.receive(JSON.stringify({ protocol: 2, kind: "welcome", connectionId: "c", connectionEpoch: "e", heartbeatIntervalMs: 1000, maxInboundBytes: 2048, maxChannels: 8 }));
  const join = JSON.parse(socket.sent[0]);
  socket.receive(JSON.stringify({ protocol: 2, kind: "joined", connectionId: "c", connectionEpoch: "e", ref: join.ref, channelEpoch: "ch", delivery: "live-query-snapshots" }));
  socket.receive(JSON.stringify({ protocol: 2, kind: "liveQuerySnapshot", connectionId: "c", connectionEpoch: "e", ref: join.ref, channelEpoch: "ch", version: "1", source: "initial", value: { items: [{ id: "u1" }], page: { hasMore: false } } }));
  await waitUntil(() => snapshots.length === 1);
  assert.equal(snapshots[0].stale, false);
  socket.receive(JSON.stringify({ protocol: 2, kind: "liveQuerySubjectAuthorityChanged", connectionId: "c", connectionEpoch: "e", ref: join.ref, channelEpoch: "ch", contractId: "hpd.auth.user-subject", contractVersion: 1, stateGeneration: "2" }));
  await waitUntil(() => snapshots.length === 2);
  assert.equal(snapshots[1].stale, true);
  manager.close();
});

test("configured control client validates and canonically sends subject epoch rotation", async () => {
  let route; let wire;
  const controlSchema = Object.freeze({ ...schema, audience: "controlPlane", features: { ...schema.features, controlOperations: ["base.admin.subject.epoch.rotate"] } });
  const base = createBaseClient({ schema: controlSchema, url: "https://base.test/base/", fetch: async (url, init) => {
    route = url; wire = new TextDecoder().decode(init.body);
    return Response.json({ contractId: "hpd.auth.user-subject", contractVersion: 1, previousStateGeneration: "1", publishedStateGeneration: "2", publicationPosition: "9", examinedRecords: "4", rewrittenReferences: "3" }, { headers: { "X-Correlation-ID": "c" } });
  } });
  const control = base.controlClient(["base.admin.subject.epoch.rotate"]);
  const result = await control.rotateSubjectEpoch({ storeId: "primary", contractId: "hpd.auth.user-subject", contractVersion: 1, expectedStateGeneration: "1", destructiveIntent: "rotate-subject-authority-epoch" });
  assert.equal(result.ok, true); assert.match(String(route), /administration\/subjects:rotate-epoch$/u);
  assert.equal(wire, '{"storeId":"primary","contractId":"hpd.auth.user-subject","contractVersion":1,"expectedStateGeneration":"1","destructiveIntent":"rotate-subject-authority-epoch"}');
  await assert.rejects(() => control.rotateSubjectEpoch({ storeId: "primary", contractId: "hpd.auth.user-subject", contractVersion: 1, expectedStateGeneration: "01", destructiveIntent: "rotate-subject-authority-epoch" }), /requestInvalid/u);
});

test("realtime rejects duplicate JSON properties before dispatch", async () => {
  const socket = new FakeSocket(); const manager = new BaseRealtimeManager("https://example.test/base", () => socket);
  manager.subscribeFeed("documents", { kind: "live", filter: {} }, () => undefined, value => value); await waitUntil(() => socket.onmessage !== null);
  socket.receive('{"protocol":2,"kind":"welcome","kind":"welcome","connectionId":"c","connectionEpoch":"e","heartbeatIntervalMs":1000,"maxInboundBytes":1024,"maxChannels":8}');
  assert.equal(socket.closedCode, 1008); manager.close();
});

test("realtime rejects frames before welcome and unknown channel references", async () => {
  const socket = new FakeSocket(); const manager = new BaseRealtimeManager("https://example.test/base", () => socket);
  manager.subscribeFeed("documents", { kind: "live", filter: {} }, () => undefined, value => value); await waitUntil(() => socket.onmessage !== null);
  socket.receive(JSON.stringify({ protocol: 2, kind: "heartbeatAck", connectionId: "c", connectionEpoch: "e", heartbeatId: "h" }));
  assert.equal(socket.closedCode, 1008); manager.close();
  const second = new FakeSocket(); const other = new BaseRealtimeManager("https://example.test/base", () => second);
  other.subscribeFeed("documents", { kind: "live", filter: {} }, () => undefined, value => value); await waitUntil(() => second.onmessage !== null);
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
  assert.deepEqual(wire.vector, [0.1, 0]); assert.equal(wire.measureDisclosure, "include"); assert.equal(wire.consistency, "current");
});

test("the bounded JSON codec rejects duplicate and lossy numeric tokens before materialization", () => {
  assert.throws(() => parseBaseJson('{"value":1,"value":2}'));
  assert.throws(() => parseBaseJson('{"value":1e400}'));
  assert.throws(() => parseBaseJson('{"value":1e-400}'));
  assert.throws(() => parseBaseJson('{"value":-0}'));
  assert.deepEqual(parseBaseJson('{"value":1.25}'), { value: 1.25 });
});

test("the closed graph codec validates every node and canonicalizes binary32", () => {
  const graph = {
    boolean: { kind: "boolean" }, plain: { kind: "string", minLength: 1, maxLength: 8, format: "plain" }, integer: { kind: "integer", minimum: "-10", maximum: "10", wire: "number" }, large: { kind: "integer", minimum: "0", maximum: "99999999999999999999", wire: "decimal-string" }, decimal: { kind: "decimal", wire: "decimal-string" }, f32: { kind: "floating", precision: "binary32", finiteOnly: true }, f64: { kind: "floating", precision: "binary64", finiteOnly: true }, bytes: { kind: "bytes", wire: "base64", maxBytes: 3 }, tagA: { kind: "literal", value: "a" }, tagB: { kind: "literal", value: "b" }, enumeration: { kind: "enum", values: ["x", "y"] }, array: { kind: "array", elementTypeId: "f32", minItems: 2, maxItems: 2 }, a: { kind: "object", additionalProperties: false, properties: [{ name: "kind", wireName: "kind", typeId: "tagA", required: true, nullable: false, disclosureShape: "none" }, { name: "value", wireName: "payload", typeId: "array", required: true, nullable: false, disclosureShape: "none" }] }, b: { kind: "object", additionalProperties: false, properties: [{ name: "kind", wireName: "kind", typeId: "tagB", required: true, nullable: false, disclosureShape: "none" }, { name: "value", wireName: "payload", typeId: "plain", required: false, nullable: true, disclosureShape: "none" }] }, union: { kind: "union", discriminator: "kind", variants: [{ tag: "a", typeId: "a" }, { tag: "b", typeId: "b" }] }
  };
  assert.deepEqual(decodeBaseJson('{"kind":"a","payload":[0.1,-0]}', "union", graph), { kind: "a", value: [Math.fround(0.1), 0] });
  assert.equal(encodeBaseJson({ kind: "a", value: [Math.fround(0.1), 0] }, "union", graph), '{"kind":"a","payload":[0.1,0]}');
  assert.equal(encodeBaseJson([Math.fround(0.1), -0], "array", graph), "[0.1,0]");
  assert.equal(decodeBaseJson("3.4028235e38", "f32", graph), Math.fround(3.4028235e38)); assert.equal(decodeBaseJson("1.17549435e-38", "f32", graph), Math.fround(1.17549435e-38)); assert.equal(decodeBaseJson("1e-45", "f32", graph), Math.fround(1e-45));
  assert.throws(() => decodeBaseJson("3.5e38", "f32", graph)); assert.throws(() => decodeBaseJson("1e-46", "f32", graph)); assert.equal(decodeBaseJson("1.0000000596046448", "f32", graph), 1);
  assert.deepEqual(decodeBaseJson('"AQID"', "bytes", graph), new Uint8Array([1, 2, 3])); assert.equal(decodeBaseJson('"99999999999999999999"', "large", graph), "99999999999999999999");
  const source = new Uint8Array([1, 2, 3]); const copied = decodeBaseValue(source, "bytes", graph); source[0] = 9;
  assert.deepEqual(copied, new Uint8Array([1, 2, 3])); assert.equal(encodeBaseJson(copied, "bytes", graph), '"AQID"');
  assert.throws(() => decodeBaseJson('{"kind":"a","payload":[],"extra":true}', "union", graph)); assert.throws(() => decodeBaseJson("[0.1]", "array", graph));
  assert.throws(() => decodeBaseJson('{"kind":"c"}', "union", graph)); assert.throws(() => decodeBaseJson('[1e-50]', "array", graph));
});

test("optimistic create materializes and malformed live snapshots close safely", async () => {
  const socket = new FakeSocket(); const manager = new BaseRealtimeManager("https://example.test/base", () => socket); const snapshots = [];
  manager.subscribe("documents", { take: 10 }, snapshot => snapshots.push(snapshot), value => value); await waitUntil(() => socket.onmessage !== null);
  socket.receive(JSON.stringify({ protocol: 2, kind: "welcome", connectionId: "c", connectionEpoch: "e", heartbeatIntervalMs: 1000, maxInboundBytes: 1024, maxChannels: 8 }));
  const join = JSON.parse(socket.sent[0]); socket.receive(JSON.stringify({ protocol: 2, kind: "joined", connectionId: "c", connectionEpoch: "e", ref: join.ref, channelEpoch: "ch", delivery: "live-query-snapshots" }));
  socket.receive(JSON.stringify({ protocol: 2, kind: "liveQuerySnapshot", connectionId: "c", connectionEpoch: "e", ref: join.ref, channelEpoch: "ch", version: "1", source: "initial", value: { items: [], page: { hasMore: false } } }));
  manager.applyOptimistic("m1", "documents", "d1", "create", { title: "draft" }, undefined, new Uint8Array([1]));
  assert.equal(snapshots.at(-1).records[0].id, "d1"); assert.equal(snapshots.at(-1).records[0].payload.json.title, "draft");
  socket.receive(JSON.stringify({ protocol: 2, kind: "liveQuerySnapshot", connectionId: "c", connectionEpoch: "e", ref: join.ref, channelEpoch: "ch", version: "2", source: "initial", value: { items: "invalid" } }));
  assert.equal(socket.closedCode, 1008); manager.close();
});

test("indeterminate mutations retain immutable bytes for explicit receipt resolution", async () => {
  const bodies = []; let calls = 0; const mutationId = "mutation-1";
  const base = createBaseClient({ schema, url: "https://base.test/base/", fetch: async (_url, init) => { bodies.push(new Uint8Array(await new Response(init.body).arrayBuffer())); calls++; if (calls <= 2) return Response.json({ code: "base.runtime.batch.indeterminate" }, { status: 500, headers: { "X-Correlation-ID": "c" } }); return Response.json({ outcome: "committed", items: [{ itemId: "mutation", index: 0, kind: "patch", disposition: "committed", record: { collectionId: "documents", id: "d1", payload: { kind: "json", json: { stored_title: "resolved" } }, metadata: {} } }] }, { headers: { "X-Correlation-ID": "c" } }); } });
  const first = await base.documents.patch("d1", { title: "resolved" }, { mutationId }); assert.equal(first.ok, false); assert.equal(first.error.code, "base.runtime.batch.indeterminate");
  const resolved = await base.resolveMutation(mutationId); assert.equal(resolved.ok, true); assert.deepEqual(bodies[0], bodies[1]); assert.deepEqual(bodies[1], bodies[2]);
  await assert.rejects(() => base.resolveMutation(mutationId), /mutationNotIndeterminate/);
});

test("generated graph codecs guard records and registered-read rows at runtime", async () => {
  const graph = { title: { kind: "string", minLength: 1, maxLength: 8, format: "plain" }, score: { kind: "floating", precision: "binary32", finiteOnly: true }, record: { kind: "object", additionalProperties: false, properties: [{ name: "title", wireName: "stored_title", typeId: "title", required: true, nullable: false, disclosureShape: "none" }, { name: "score", wireName: "score", typeId: "score", required: false, nullable: false, disclosureShape: "none" }] }, create: { kind: "object", additionalProperties: false, properties: [{ name: "title", wireName: "title", typeId: "title", required: true, nullable: false, disclosureShape: "none" }, { name: "score", wireName: "score", typeId: "score", required: false, nullable: false, disclosureShape: "none" }] }, replace: { kind: "object", additionalProperties: false, properties: [{ name: "title", wireName: "title", typeId: "title", required: true, nullable: false, disclosureShape: "none" }, { name: "score", wireName: "score", typeId: "score", required: true, nullable: false, disclosureShape: "none" }] }, patch: { kind: "object", additionalProperties: false, properties: [{ name: "title", wireName: "title", typeId: "title", required: false, nullable: false, disclosureShape: "none" }, { name: "score", wireName: "score", typeId: "score", required: false, nullable: false, disclosureShape: "none" }] }, row: { kind: "object", additionalProperties: false, properties: [{ name: "title", wireName: "title", typeId: "title", required: true, nullable: false, disclosureShape: "none" }] }, parameters: { kind: "object", additionalProperties: false, properties: [] } };
  const typedSchema = { ...schema, typeGraph: graph, reads: { titles: read({ id: "titles", parameterTypeId: "parameters", rowTypeId: "row", maxPageSize: 10, watchable: false }) }, collections: { documents: collection({ ...schema.collections.documents, fields: { title: field("stable-title", "stored_title", ["equal"], "title"), score: field("score", "score", ["equal"], "score") }, operations: ["get", "patch", "batch"] }) } };
  let mode = "record"; let mutationBody = ""; const base = createBaseClient({ schema: typedSchema, url: "https://base.test/base/", fetch: async (_url, init) => { if (mode === "record") return Response.json({ collectionId: "documents", id: "d1", payload: { kind: "json", json: { stored_title: "valid", score: 0.1, extra: true } }, metadata: {} }, { headers: { "X-Correlation-ID": "c" } }); if (mode === "read") return Response.json({ items: [{ title: "too-long-value" }], page: { hasMore: false } }, { headers: { "X-Correlation-ID": "c" } }); mutationBody = new TextDecoder().decode(init.body); return Response.json({ outcome: "committed", items: [{ itemId: "mutation", index: 0, kind: "patch", disposition: "committed", record: { collectionId: "documents", id: "d1", payload: { kind: "json", json: { stored_title: "valid", score: 0.1 } }, metadata: {} } }] }, { headers: { "X-Correlation-ID": "c" } }); } });
  const malformedRecord = await base.documents.get("d1"); assert.equal(malformedRecord.ok, false); assert.equal(malformedRecord.error.code, "base.client.responseInvalid");
  mode = "read"; const malformedRow = await base.reads.titles.execute({}); assert.equal(malformedRow.ok, false); assert.equal(malformedRow.error.code, "base.client.responseInvalid");
  mode = "mutation"; const patched = await base.documents.patch("d1", { score: Math.fround(0.1) }); assert.equal(patched.ok, true); assert.match(mutationBody, /\"score\":0\.1/); assert.doesNotMatch(mutationBody, /0\.10000000149011612/);
});

test("mutation DTOs enforce authoritative required, nullable, and extra-member rules", async () => {
  let calls = 0; let body = "";
  const nullableGraph = { ...basicGraph, patch: { kind: "object", additionalProperties: false, properties: [{ name: "title", wireName: "stored_title", typeId: "title", required: false, nullable: true, disclosureShape: "none" }] } };
  const nullableSchema = { ...schema, typeGraph: nullableGraph };
  const base = createBaseClient({ schema: nullableSchema, url: "https://base.test/base/", fetch: async (_url, init) => { calls++; body = new TextDecoder().decode(init.body); return Response.json({ outcome: "committed", items: [{ itemId: "mutation", index: 0, kind: "patch", disposition: "committed", record: { collectionId: "documents", id: "d1", payload: { kind: "json", json: { stored_title: "after" } }, metadata: {} } }] }, { headers: { "X-Correlation-ID": "dto" } }); } });
  await assert.rejects(() => base.documents.create({}), /base\.client\.requestInvalid/u);
  await assert.rejects(() => base.documents.replace("d1", {}), /base\.client\.requestInvalid/u);
  await assert.rejects(() => base.documents.patch("d1", { unexpected: true }), /base\.client\.requestInvalid/u);
  assert.equal(calls, 0);
  const nullable = await base.documents.patch("d1", { title: null }); assert.equal(nullable.ok, true); assert.match(body, /"stored_title":null/u); assert.equal(calls, 1);
});

test("HTTP graph decoding preserves raw negative-zero tokens until binary32 normalization", async () => {
  const graph = { ...basicGraph, score: { kind: "floating", precision: "binary32", finiteOnly: true }, record: { kind: "object", additionalProperties: false, properties: [{ name: "title", wireName: "stored_title", typeId: "title", required: true, nullable: false, disclosureShape: "none" }, { name: "score", wireName: "score", typeId: "score", required: true, nullable: false, disclosureShape: "none" }] } };
  const floatingSchema = { ...schema, typeGraph: graph, collections: { documents: collection({ ...schema.collections.documents, fields: { ...schema.collections.documents.fields, score: field("score", "score", ["equal"], "score") } }) } };
  const base = createBaseClient({ schema: floatingSchema, url: "https://base.test/base/", fetch: async () => new Response('{"collectionId":"documents","id":"d1","payload":{"kind":"json","json":{"stored_title":"value","score":-0}},"metadata":{}}', { headers: { "Content-Type": "application/json", "X-Correlation-ID": "raw" } }) });
  const result = await base.documents.get("d1"); assert.equal(result.ok, true); assert.equal(result.value.payload.json.score, 0); assert.equal(Object.is(result.value.payload.json.score, -0), false);
});

test("file DTOs are decoded against the complete closed HTTP contract", async () => {
  const fileSchema = { ...schema, features: { ...schema.features, files: true } };
  let mode = "upload";
  const metadata = { bucketId: "assets", objectId: "o1", sizeBytes: 3, createdAt: "2026-01-01T00:00:00Z", publicMetadata: { visibility: "public" } };
  const base = createBaseClient({ schema: fileSchema, url: "https://base.test/base/", fetch: async () => {
    const value = mode === "upload" ? { metadata, created: true } : mode === "list" ? { items: [metadata], nextCursor: "next" } : { ...metadata, unexpected: true };
    return new Response(JSON.stringify(value), { headers: { "Content-Type": "application/json", "X-Correlation-ID": "files" } });
  } });
  const upload = await base.files.bucket("assets").upload(new Uint8Array([1, 2, 3]));
  assert.equal(upload.ok, true); assert.equal(upload.value.metadata.objectId, "o1"); assert.equal(upload.value.created, true);
  mode = "list"; const page = await base.files.bucket("assets").list(); assert.equal(page.ok, true); assert.equal(page.value.nextCursor, "next");
  mode = "invalid"; const invalid = await base.files.bucket("assets").metadata("o1"); assert.equal(invalid.ok, false); assert.equal(invalid.error.code, "base.client.responseInvalid");
});

test("fixed-marker and omission are distinct closed disclosure shapes", () => {
  const graph = {
    value: { kind: "string", minLength: 0, maxLength: 16, format: "plain" },
    record: { kind: "object", additionalProperties: false, properties: [
      { name: "omitted", wireName: "omitted", typeId: "value", required: true, nullable: false, disclosureShape: "omission" },
      { name: "marked", wireName: "marked", typeId: "value", required: true, nullable: false, disclosureShape: "fixed-marker" }
    ] }
  };
  const decoded = decodeBaseJson('{"marked":{"$base":"redacted"}}', "record", graph);
  assert.equal(Object.hasOwn(decoded, "omitted"), false); assert.equal(decoded.marked, baseRedacted); assert.equal(isBaseRedacted(decoded.marked), true);
  assert.throws(() => decodeBaseJson('{"marked":{"$base":"redacted","extra":true}}', "record", graph), /responseInvalid/u);
  assert.throws(() => encodeBaseJson({ omitted: "x", marked: baseRedacted }, "record", graph), /responseInvalid/u);
});

class FakeSocket {
  readyState = 1; onopen = null; onmessage = null; onclose = null; onerror = null; sent = []; closedCode = undefined;
  send(data) { this.sent.push(data); }
  close(code = 1000) { this.closedCode = code; this.readyState = 3; this.onclose?.({ code }); }
  receive(data) { this.onmessage?.({ data }); }
}
async function waitUntil(predicate, timeout = 500) { const end = Date.now() + timeout; while (!predicate()) { if (Date.now() >= end) throw new Error("timeout"); await new Promise(resolve => setTimeout(resolve, 1)); } }
function join(...parts) { const result = new Uint8Array(parts.reduce((sum, part) => sum + part.length, 0)); let offset = 0; for (const part of parts) { result.set(part, offset); offset += part.length; } return result; }
