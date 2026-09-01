import { createBaseTypeGraph, decodeBaseJson, encodeBaseJson, type BaseTypeGraph, type BaseTypeNode } from '@hpd/base-client';
import { sha256 } from '@noble/hashes/sha2.js';
import { bytesToHex } from '@noble/hashes/utils.js';
import type { StudioContractMap, StudioEndpointContract, StudioMethodBinding } from './bootstrap.ts';

export interface StudioRuntimeTransportRequest {
  readonly endpointId: string; readonly method: StudioEndpointContract['method']; readonly relativeRoute: string;
  readonly registeredMethodId: string;
  readonly registeredKind: StudioMethodBinding['kind'];
  readonly body: string; readonly maximumResultBytes: bigint; readonly deadlineMilliseconds: bigint; readonly signal?: AbortSignal;
}
export type StudioRuntimeTransportResult = Readonly<{ readonly ok: true; readonly body: string }> |
  Readonly<{ readonly ok: false; readonly body: string; readonly failureCode?: 'base.studio.failedBeforeInfluence'|'base.studio.commandIndeterminate' }>;
export interface StudioRuntimeTransport {
  executeJson(request: StudioRuntimeTransportRequest): Promise<StudioRuntimeTransportResult>;
  subscribe(request: StudioRuntimeTransportRequest): AsyncIterable<StudioRuntimeTransportResult>;
  upload(request: Omit<StudioRuntimeTransportRequest, 'body'> & { readonly metadataBody: string; readonly content: ReadableStream<Uint8Array> }): Promise<StudioRuntimeTransportResult>;
}
export class StudioRuntimeEndpointError extends Error {
  readonly endpointId: string; readonly value: unknown; readonly code: string|undefined;
  constructor(endpointId: string, value: unknown, code?: string) { super('base.studio.endpointFailed'); this.endpointId = endpointId; this.value = value; this.code = code; }
}
export interface StudioRuntimeJsonMethod { readonly kind: 'json'; readonly binding: StudioMethodBinding; invoke(request: unknown, signal?: AbortSignal): Promise<unknown>; }
export interface StudioRuntimeSubscriptionMethod { readonly kind: 'subscription'; readonly binding: StudioMethodBinding; subscribe(request: unknown, signal?: AbortSignal): AsyncIterable<unknown>; }
export interface StudioRuntimeUploadMethod { readonly kind: 'upload'; readonly binding: StudioMethodBinding;
  upload(metadata: unknown, content: ReadableStream<Uint8Array>, signal?: AbortSignal): Promise<unknown>; }
export type StudioRuntimeMethod = StudioRuntimeJsonMethod | StudioRuntimeSubscriptionMethod | StudioRuntimeUploadMethod;
export interface StudioRuntimeMethodRegistry { readonly ids: readonly string[]; has(id: string): boolean; get(id: string): StudioRuntimeMethod | undefined; }
export interface StudioRuntimeMethodMap { readonly graph: BaseTypeGraph; readonly methods: StudioRuntimeMethodRegistry; }

/** Hydrates a checksum-verified Studio map through the existing closed L41 BASE codec. */
export function hydrateStudioRuntimeMap(map: StudioContractMap, transport: StudioRuntimeTransport): StudioRuntimeMethodMap {
  const decoded = map.types.map(type => {
    const descriptor = decodeBase64Url(type.canonicalDescriptor);
    if (descriptor.length < 1 || descriptor.length > 65_536) mismatch();
    if (bytesToHex(sha256(descriptor)) !== type.nodeChecksum) mismatch();
    let node: BaseTypeNode; try { node = JSON.parse(new TextDecoder('utf-8', { fatal: true }).decode(descriptor)) as BaseTypeNode; }
    catch { return mismatch(); }
    return Object.freeze({ id: type.typeId, node });
  });
  const graph = createBaseTypeGraph(decoded, 2_048, 32);
  const endpoints = new Map(map.endpoints.map(endpoint => [endpoint.endpointId, endpoint]));
  for (const endpoint of map.endpoints) {
    if (graph[endpoint.requestNodeId] === undefined || graph[endpoint.resultNodeId] === undefined || graph[endpoint.errorNodeId] === undefined ||
        map.types.find(type => type.typeId === endpoint.requestNodeId)?.nodeChecksum !== endpoint.requestNodeChecksum ||
        map.types.find(type => type.typeId === endpoint.resultNodeId)?.nodeChecksum !== endpoint.resultNodeChecksum ||
        map.types.find(type => type.typeId === endpoint.errorNodeId)?.nodeChecksum !== endpoint.errorNodeChecksum) mismatch();
  }
  const reachable = new Set<string>(); const visit = (id: string): void => {
    if (reachable.has(id)) return; const node = graph[id]; if (!node) mismatch(); reachable.add(id);
    for (const child of references(node)) visit(child);
  };
  for (const endpoint of map.endpoints) { visit(endpoint.requestNodeId); visit(endpoint.resultNodeId); visit(endpoint.errorNodeId); }
  if (reachable.size !== map.types.length) mismatch();

  const owned = new Map<string, StudioRuntimeMethod>();
  for (const binding of map.methods) {
    const endpoint = endpoints.get(binding.endpointId); if (!endpoint || binding.requestTypeId !== endpoint.requestNodeId || binding.resultTypeId !== endpoint.resultNodeId || !methodTransportMatches(binding, endpoint)) mismatch();
    const request = (value: unknown): string => {
      const body = encodeBaseJson(value, binding.requestTypeId, graph);
      if (BigInt(new TextEncoder().encode(body).length) > BigInt(endpoint.maximumRequestBytes)) throw new RangeError('base.studio.requestTooLarge'); return body;
    };
    const result = (response: StudioRuntimeTransportResult): unknown => {
      if (BigInt(new TextEncoder().encode(response.body).length) > BigInt(endpoint.maximumResultBytes)) throw new RangeError('base.studio.resultTooLarge');
      if (!response.ok) {const value=response.body.length===0&&response.failureCode!==undefined?Object.freeze({code:response.failureCode}):decodeBaseJson(response.body,endpoint.errorNodeId,graph);
        throw new StudioRuntimeEndpointError(endpoint.endpointId,value,response.failureCode);}
      return decodeBaseJson(response.body, binding.resultTypeId, graph);
    };
    const common = (body: string, signal?: AbortSignal): StudioRuntimeTransportRequest => ({ endpointId: endpoint.endpointId, method: endpoint.method,
      registeredKind: binding.kind, registeredMethodId: binding.registeredMethodId,
      relativeRoute: endpoint.relativeRoute, body, maximumResultBytes: BigInt(endpoint.maximumResultBytes), deadlineMilliseconds: BigInt(endpoint.deadlineMilliseconds), signal });
    if (binding.kind === 'invalidationSubscribe') {
      owned.set(binding.registeredMethodId, Object.freeze({ kind: 'subscription' as const, binding,
        async *subscribe(value: unknown, signal?: AbortSignal): AsyncIterable<unknown> {
          for await (const item of transport.subscribe(common(request(value), signal))) yield result(item);
        } }));
    } else if (binding.kind === 'stageUpload') {
      owned.set(binding.registeredMethodId, Object.freeze({ kind: 'upload' as const, binding,
        async upload(metadata: unknown, content: ReadableStream<Uint8Array>, signal?: AbortSignal): Promise<unknown> {
          const base = common(request(metadata), signal); return result(await transport.upload({ ...base, metadataBody: base.body, content }));
        } }));
    } else {
      owned.set(binding.registeredMethodId, Object.freeze({ kind: 'json' as const, binding,
        async invoke(value: unknown, signal?: AbortSignal): Promise<unknown> { return result(await transport.executeJson(common(request(value), signal))); } }));
    }
  }
  const ids = Object.freeze([...owned.keys()]);
  const methods: StudioRuntimeMethodRegistry = Object.freeze({ ids, has: (id: string) => owned.has(id), get: (id: string) => owned.get(id) });
  return Object.freeze({ graph, methods });
}

function references(node: BaseTypeNode): readonly string[] {
  switch (node.kind) { case 'selection-patch': return [node.patchTypeId]; case 'array': return [node.elementTypeId];
    case 'object': return node.properties.map(property => property.typeId); case 'union': return node.variants.map(variant => variant.typeId); default: return []; }
}
function decodeBase64Url(value: string): Uint8Array {
  if (!/^[A-Za-z0-9_-]+$/u.test(value)) mismatch();
  const base64 = value.replace(/-/gu, '+').replace(/_/gu, '/').padEnd(Math.ceil(value.length / 4) * 4, '=');
  const bytes = Uint8Array.from(globalThis.atob(base64), character => character.charCodeAt(0));
  const encoded = globalThis.btoa(String.fromCharCode(...bytes)).replace(/=/gu, '').replace(/\+/gu, '-').replace(/\//gu, '_');
  if (encoded !== value) mismatch(); return bytes;
}
function methodTransportMatches(binding: StudioMethodBinding, endpoint: StudioEndpointContract): boolean {
  if (binding.kind === 'invalidationSubscribe') return endpoint.transport === 'sameOriginRealtime' && endpoint.method === 'WEBSOCKET';
  if (binding.kind === 'stageUpload') return endpoint.transport === 'sameOriginHttp' && endpoint.method === 'PUT';
  return endpoint.transport === 'sameOriginHttp' && endpoint.method !== 'WEBSOCKET';
}
function mismatch(): never { throw new TypeError('base.studio.contractMismatch'); }
