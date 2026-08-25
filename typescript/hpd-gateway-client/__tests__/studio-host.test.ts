import { describe, expect, it, vi } from 'vitest';
import { gatewayOperationInventoryChecksum, gatewayOperations } from '../src/generated/contract.js';
import { createGatewayStudioClient, type GatewayStudioTransportRequest } from '../src/studio-host.js';

describe('Gateway Studio host client', () => {
  it('locks the generated operation inventory', () => {
    expect(gatewayOperations).toHaveLength(23);
    expect(gatewayOperationInventoryChecksum).toBe('b577087395ac45ad1cd9ce74ca577ab6797591c5e7d5a564f31b826878f3b8bc');
  });

  it('dispatches only an operation-scoped relative request without credentials or origin', async () => {
    let captured: GatewayStudioTransportRequest | undefined;
    const execute = vi.fn(async (request: GatewayStudioTransportRequest) => {
      captured = request;
      return new Response('{', { status: 200, headers: { 'Content-Type': 'application/json' } });
    });
    const client = createGatewayStudioClient({ endpointSurfaceId: 'gateway.admin.v1', principalGeneration: 7n,
      authenticationSessionChecksum: 'a'.repeat(64), signal: new AbortController().signal,
      transport: Object.freeze({ execute }), limits: Object.freeze({ maximumOperations: 23,
        maximumRequestBytes: 8_388_608, maximumResponseBytes: 8_388_608, maximumConcurrentRequests: 8,
        acquisitionDeadlineMilliseconds: 5_000, operationDeadlineMilliseconds: 30_000,
        disposalDeadlineMilliseconds: 5_000 }) });

    await client.capabilities({ path: {} });

    expect(execute).toHaveBeenCalledOnce();
    expect(captured).toMatchObject({ operation: 'capabilities', purpose: 'observation', method: 'GET', relativePathAndQuery: '/capabilities' });
    expect(Object.keys(captured!)).not.toContain('baseUrl');
    expect(Object.keys(captured!)).not.toContain('authentication');
    expect(Object.values(captured!.headers).join(' ')).not.toMatch(/bearer/iu);
  });

  it('rejects substituted endpoint, session, and capacity authority before creating a client', () => {
    const valid = { endpointSurfaceId: 'gateway.admin.v1', principalGeneration: 1n,
      authenticationSessionChecksum: 'b'.repeat(64), signal: new AbortController().signal,
      transport: Object.freeze({ execute: vi.fn() }), limits: Object.freeze({ maximumOperations: 23,
        maximumRequestBytes: 1, maximumResponseBytes: 1, maximumConcurrentRequests: 1,
        acquisitionDeadlineMilliseconds: 1, operationDeadlineMilliseconds: 1, disposalDeadlineMilliseconds: 1 }) };
    expect(() => createGatewayStudioClient({ ...valid, endpointSurfaceId: 'gateway.other' })).toThrow(/authority/u);
    expect(() => createGatewayStudioClient({ ...valid, authenticationSessionChecksum: 'not-a-checksum' })).toThrow(/authority/u);
    expect(() => createGatewayStudioClient({ ...valid, limits: { ...valid.limits, maximumOperations: 22 } })).toThrow(/authority/u);
  });

  it('projects generated mutation purpose without transport-method inference', async () => {
    let captured: GatewayStudioTransportRequest | undefined;
    const client = createGatewayStudioClient({ endpointSurfaceId: 'gateway.admin.v1', principalGeneration: 1n,
      authenticationSessionChecksum: 'b'.repeat(64), signal: new AbortController().signal,
      transport: { execute: async request => { captured = request; return new Response('{', { status: 202, headers: { 'Content-Type': 'application/json' } }); } },
      limits: { maximumOperations: 23, maximumRequestBytes: 8_388_608, maximumResponseBytes: 8_388_608, maximumConcurrentRequests: 1,
        acquisitionDeadlineMilliseconds: 1_000, operationDeadlineMilliseconds: 1_000, disposalDeadlineMilliseconds: 1_000 } });
    await client.activate({ path: { ns: 'namespace', target: 'node', revision: 'revision' } as never,
      headers: { idempotencyKey: 'attempt', desiredPrecondition: { kind: 'create-only' } } as never });
    expect(captured).toMatchObject({ operation: 'activate', purpose: 'commandExecution', method: 'POST' });
  });
});
