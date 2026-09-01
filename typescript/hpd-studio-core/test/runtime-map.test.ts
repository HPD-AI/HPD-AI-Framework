import assert from 'node:assert/strict';
import test from 'node:test';
import { sha256 } from '@noble/hashes/sha2.js';
import { bytesToHex } from '@noble/hashes/utils.js';
import { hydrateStudioRuntimeMap } from '../src/runtime-map.ts';
import { studioSha256 } from '../src/module-abi.ts';
import type { StudioContractMap, StudioNamedTypeContract } from '../src/bootstrap.ts';

const text = new TextEncoder(); const sha = studioSha256('a'.repeat(64));
function type(typeId: string, node: object): StudioNamedTypeContract {
  const bytes = text.encode(JSON.stringify(node)); const canonicalDescriptor = Buffer.from(bytes).toString('base64url');
  return { typeId, canonicalDescriptor, nodeChecksum: studioSha256(bytesToHex(sha256(bytes))), checksum: sha };
}
function map(): StudioContractMap {
  const types = [
    type('error', { kind: 'string', minLength: 1, maxLength: 64, format: 'plain' }),
    type('message', { kind: 'string', minLength: 1, maxLength: 64, format: 'plain' }),
    type('request', { kind: 'object', properties: [{ name: 'message', wireName: 'message', typeId: 'message', required: true, nullable: false, disclosureShape: 'none' }], additionalProperties: false }),
    type('result', { kind: 'object', properties: [{ name: 'message', wireName: 'message', typeId: 'message', required: true, nullable: false, disclosureShape: 'none' }], additionalProperties: false })
  ];
  return { protocolVersion: 'base.protocol', serializationProfile: 'base.json', errorTaxonomy: 'base.error', realtimeProtocol: 'base.realtime',
    runtimeAbiChecksum: sha, interpreterVectorChecksum: sha, types,
    endpoints: [{ endpointId: 'base.echo', version: 1, method: 'POST', relativeRoute: '/echo', audience: 'controlPlane', transport: 'sameOriginHttp',
      requestNodeId: 'request', requestNodeChecksum: types[2]!.nodeChecksum, resultNodeId: 'result', resultNodeChecksum: types[3]!.nodeChecksum,
      errorNodeId: 'error', errorNodeChecksum: types[0]!.nodeChecksum, maximumRequestBytes: '1024', maximumResultBytes: '1024', deadlineMilliseconds: '1000', checksum: sha }],
    methods: [{ registeredMethodId: 'base.echo.invoke', kind: 'execute', owningModuleId: 'base', owningPageOrCommandId: 'base.echo', endpointId: 'base.echo',
      requestTypeId: 'request', resultTypeId: 'result', bindingChecksum: sha }], checksum: sha };
}

test('runtime map hydrates through the BASE graph codec and seals methods', async () => {
  const runtime = hydrateStudioRuntimeMap(map(), { async executeJson(request) { assert.equal(request.body, '{"message":"hello"}'); return { ok: true, body: '{"message":"world"}' }; },
    async *subscribe() {}, async upload() { return { ok: true, body: '{"message":"world"}' }; } });
  const method = runtime.methods.get('base.echo.invoke'); assert.equal(method?.kind, 'json');
  assert.deepEqual(await (method as Extract<typeof method, { kind: 'json' }>).invoke({ message: 'hello' }), { message: 'world' });
  assert.ok(Object.isFrozen(runtime));
  assert.equal((runtime.methods as unknown as { set?: unknown }).set, undefined);
});

test('runtime map rejects malformed, substituted, and unreachable type authority', () => {
  const original = map(); const substituted = { ...original, endpoints: [{ ...original.endpoints[0]!, requestNodeChecksum: sha }] };
  const transport = { async executeJson() { return { ok: true as const, body: '{}' }; }, async *subscribe() {}, async upload() { return { ok: true as const, body: '{}' }; } };
  assert.throws(() => hydrateStudioRuntimeMap(substituted, transport));
  const current = map(); const unreachable = { ...current, types: [...current.types, type('unused', { kind: 'boolean' })] };
  assert.throws(() => hydrateStudioRuntimeMap(unreachable, transport));
  const oversizedBytes = text.encode(`${' '.repeat(65_536)}{\"kind\":\"boolean\"}`);
  const oversized = { ...current, types: current.types.map((value, index) => index === 0 ? {
    ...value, canonicalDescriptor: Buffer.from(oversizedBytes).toString('base64url'),
    nodeChecksum: studioSha256(bytesToHex(sha256(oversizedBytes)))
  } : value) };
  assert.throws(() => hydrateStudioRuntimeMap(oversized, transport));
});

test('runtime map separates realtime, streaming, and typed error transports', async () => {
  const original = map(); const binding = original.methods[0]!;
  const subscription = hydrateStudioRuntimeMap({ ...original,
    endpoints: [{ ...original.endpoints[0]!, method: 'WEBSOCKET', transport: 'sameOriginRealtime' }],
    methods: [{ ...binding, kind: 'invalidationSubscribe' }] }, {
    async executeJson() { throw new Error(); }, async *subscribe() { yield { ok: true as const, body: '{"message":"hint"}' }; },
    async upload() { throw new Error(); }
  }).methods.get('base.echo.invoke');
  assert.equal(subscription?.kind, 'subscription'); const values: unknown[] = [];
  for await (const value of (subscription as Extract<typeof subscription, { kind: 'subscription' }>).subscribe({ message: 'join' })) values.push(value);
  assert.deepEqual(values, [{ message: 'hint' }]);

  const upload = hydrateStudioRuntimeMap({ ...original, endpoints: [{ ...original.endpoints[0]!, method: 'PUT' }],
    methods: [{ ...binding, kind: 'stageUpload' }] }, {
    async executeJson() { throw new Error(); }, async *subscribe() {}, async upload(request) {
      assert.equal(request.metadataBody, '{"message":"metadata"}'); return { ok: true, body: '{"message":"stored"}' };
    }
  }).methods.get('base.echo.invoke');
  assert.equal(upload?.kind, 'upload');
  assert.deepEqual(await (upload as Extract<typeof upload, { kind: 'upload' }>).upload({ message: 'metadata' }, new ReadableStream()), { message: 'stored' });

  const failed = hydrateStudioRuntimeMap(original, { async executeJson() { return { ok: false, body: '"denied"' }; }, async *subscribe() {}, async upload() { throw new Error(); } });
  await assert.rejects(() => (failed.methods.get('base.echo.invoke') as Extract<ReturnType<typeof failed.methods.get>, { kind: 'json' }>).invoke({ message: 'hello' }),
    (error: unknown) => error instanceof Error && error.message === 'base.studio.endpointFailed');
  const beforeInfluence = hydrateStudioRuntimeMap(original, { async executeJson() { return { ok: false as const, body: '', failureCode: 'base.studio.failedBeforeInfluence' as const }; },
    async *subscribe() {}, async upload() { throw new Error(); } });
  await assert.rejects(() => (beforeInfluence.methods.get('base.echo.invoke') as Extract<ReturnType<typeof beforeInfluence.methods.get>, { kind: 'json' }>).invoke({ message: 'hello' }),
    (error: unknown) => error instanceof Error && (error as Error & {code?:string}).code === 'base.studio.failedBeforeInfluence');
  assert.throws(() => hydrateStudioRuntimeMap({ ...original, methods: [{ ...binding, kind: 'invalidationSubscribe' }] }, {
    async executeJson() { throw new Error(); }, async *subscribe() {}, async upload() { throw new Error(); }
  }));
});
