import { describe, expect, it } from 'vitest';
import { createStudioFrameworkTransport, readStudioResponseBody, requireStudioResponseAuthority, settleStudioTeardown } from './shell-runtime.ts';

describe('Studio bounded transport', () => {
  it('carries the exact snapshot lease and generated purpose through framework dispatch', async () => {
    const snapshot = 'b'.repeat(64); const authority = 'a'.repeat(64); let request: RequestInit | undefined; let purpose: string | undefined;
    const authentication = { authorize: async (_url: URL, init: RequestInit, suppliedPurpose: string) => { request = init; purpose = suppliedPurpose;
      return new Response('{}', { status: 409, headers: { 'X-HPD-Studio-Response-Authority': authority } }); } };
    const original = globalThis.location; Object.defineProperty(globalThis, 'location', { configurable: true, value: { href: 'https://studio.example/' } });
    const client = { endpointSurfaceId: 'gateway.admin.v1', limits: { maximumRequestBytes: '1024', maximumResponseBytes: '1024', operationDeadlineMilliseconds: '1000' },
      operations: [{ operationId: 'activate', method: 'POST', relativePathTemplate: 'activate', purpose: 'commandExecution', maximumRequestBytes: '1024',
        maximumResponseBytes: '1024', deadlineMilliseconds: '1000', requestMediaTypes: ['application/json'], requestHeaderNames: [] }] };
    try { const transport = createStudioFrameworkTransport(authentication as never, client as never, snapshot, authority);
      await transport.execute({ operation: 'activate', purpose: 'commandExecution', method: 'POST', relativePathAndQuery: '/activate', headers: { 'Content-Type': 'application/json' }, body: '{}',
        maximumResponseBytes: 1024, deadlineMilliseconds: 1000, signal: new AbortController().signal });
    } finally { Object.defineProperty(globalThis, 'location', { configurable: true, value: original }); }
    expect(new Headers(request?.headers).get('X-HPD-Studio-Snapshot')).toBe(snapshot); expect(purpose).toBe('commandExecution');
  });
  it('resolves framework routes from the Studio directory without relying on document.baseURI', async () => {
    let requestedUrl = ''; const originalLocation = globalThis.location;
    Object.defineProperty(globalThis, 'location', { configurable: true, value: { href: 'https://studio.example/platform/studio/security' } });
    const authentication = { authorize: async (url: URL) => { requestedUrl = url.href;
      return new Response('{}', { status: 200, headers: { 'X-HPD-Studio-Response-Authority': 'a'.repeat(64) } }); } };
    const client = { endpointSurfaceId: 'gateway.admin.v1', limits: { maximumRequestBytes: '1024', maximumResponseBytes: '1024', operationDeadlineMilliseconds: '1000' },
      operations: [{ operationId: 'observe', method: 'POST', relativePathTemplate: 'observe', purpose: 'observation', maximumRequestBytes: '1024',
        maximumResponseBytes: '1024', deadlineMilliseconds: '1000', requestMediaTypes: ['application/json'], requestHeaderNames: [] }] };
    try {
      const transport = createStudioFrameworkTransport(authentication as never, client as never, 'b'.repeat(64), 'a'.repeat(64));
      await transport.execute({ operation: 'observe', purpose: 'observation', method: 'POST', relativePathAndQuery: '/observe',
        headers: { 'Content-Type': 'application/json' }, body: '{}', maximumResponseBytes: 1024, deadlineMilliseconds: 1000,
        signal: new AbortController().signal });
    } finally { Object.defineProperty(globalThis, 'location', { configurable: true, value: originalLocation }); }
    expect(requestedUrl).toBe('https://studio.example/platform/studio/base/studio/framework-clients/gateway.admin.v1/observe');
  });
  it('requires exact response authority on protected success and failure responses', () => {
    const authority = 'a'.repeat(64);
    expect(() => requireStudioResponseAuthority(new Response('{}', { status: 409, headers: { 'X-HPD-Studio-Response-Authority': authority } }), authority)).not.toThrow();
    expect(() => requireStudioResponseAuthority(new Response('{}', { status: 503 }), authority)).toThrow('base.studio.responseAuthorityMismatch');
    expect(() => requireStudioResponseAuthority(new Response('{}', { status: 400, headers: { 'X-HPD-Studio-Response-Authority': 'b'.repeat(64) } }), authority)).toThrow();
  });
  it('accepts the exact response-byte maximum', async () => {
    const response = new Response(new Uint8Array([0x61, 0x62, 0x63]));
    await expect(readStudioResponseBody(response, 3n, new AbortController().signal)).resolves.toBe('abc');
  });
  it('cancels before retaining maximum plus one response bytes', async () => {
    let cancelled = false;
    const body = new ReadableStream<Uint8Array>({ start(controller) { controller.enqueue(new Uint8Array([1, 2, 3])); controller.enqueue(new Uint8Array([4])); },
      cancel() { cancelled = true; } });
    await expect(readStudioResponseBody(new Response(body), 3n, new AbortController().signal)).rejects.toThrow('base.studio.resultTooLarge');
    expect(cancelled).toBe(true);
  });
  it('rejects malformed UTF-8 inside the byte bound', async () => {
    await expect(readStudioResponseBody(new Response(new Uint8Array([0xff])), 1n, new AbortController().signal)).rejects.toThrow();
  });
});

describe('Studio aggregate teardown', () => {
  it('detaches all late callbacks after one aggregate deadline', async () => {
    const clock = await import('node:timers/promises');
    const never = new Promise<void>(() => {}); const started = Date.now();
    await settleStudioTeardown(Array.from({ length: 256 }, () => never), 10);
    expect(Date.now() - started).toBeLessThan(250); void clock;
  });
  it('rejects task capacity maximum plus one before waiting', async () => {
    await expect(settleStudioTeardown(Array.from({ length: 257 }, () => Promise.resolve()), 10)).rejects.toThrow('base.studio.teardownBoundsInvalid');
  });
});
