import { afterEach, describe, expect, it, vi } from 'vitest';
import { decodeStudioHostContract } from './host-contract.ts';
import { StudioHostAuthentication } from './authentication.ts';

const zero = '0'.repeat(64);
const host = decodeStudioHostContract({ shellContractChecksum: zero, editionAssetGraphChecksum: zero, runtimeClientChecksum: zero,
  bootstrapRoute: '/control/bootstrap', sessionRoute: '/auth/session', loginRoute: '/auth/login', logoutRoute: '/auth/logout',
  authentication: { kind: 'cookieBff', authorizationRoute: '/auth/authorize', descriptorChecksum: zero },
  modules: [{ moduleId: 'base', moduleVersion: 1, entryModulePath: '/modules/base/1/assets/base-studio.js', assetGraphChecksum: zero }] });
const original = { fetch: globalThis.fetch, document: globalThis.document, location: globalThis.location, history: globalThis.history };
afterEach(() => Object.assign(globalThis, original));

describe('Studio host authentication', () => {
  it('accepts only the exact authenticated session wire shape', async () => {
    Object.assign(globalThis, { document: { baseURI: 'https://studio.test/studio/' }, location: { origin: 'https://studio.test' },
      fetch: vi.fn(async () => new Response(JSON.stringify({ kind: 'authenticated', principalGeneration: '7', sessionChecksum: zero,
        audience: 'controlPlane', protectedScopeChecksum: zero, issuedAtUtc: '2026-01-01T00:00:00.0000000Z',
        expiresAtUtc: '2099-01-01T00:00:00.0000000Z', descriptorChecksum: zero }), { status: 200 })) });
    const authentication = new StudioHostAuthentication(host);
    await expect(authentication.observe()).resolves.toMatchObject({ kind: 'authenticated', principalGeneration: 7n });
  });
  it('rejects legacy or additional session members and cross-origin transport', async () => {
    Object.assign(globalThis, { document: { baseURI: 'https://studio.test/studio/' }, location: { origin: 'https://studio.test' },
      fetch: vi.fn(async () => new Response(JSON.stringify({ kind: 'authenticated', principalGeneration: '7', sessionChecksum: zero,
        audience: 'controlPlane', protectedScopeChecksum: zero, issuedAtUtc: '2026-01-01T00:00:00.0000000Z',
        expiresAtUtc: '2099-01-01T00:00:00.0000000Z', descriptorChecksum: zero, token: 'no' }), { status: 200 })) });
    const authentication = new StudioHostAuthentication(host);
    await expect(authentication.observe()).rejects.toThrow('base.studio.sessionInvalid');
    await expect(authentication.authorize('https://foreign.test/value', {}, 'observation')).rejects.toThrow('base.studio.crossOriginRejected');
  });
  it('publishes unauthenticated authority on 401 without requesting a token', async () => {
    Object.assign(globalThis, { document: { baseURI: 'https://studio.test/studio/' }, location: { origin: 'https://studio.test' },
      fetch: vi.fn(async () => new Response(null, { status: 401 })) });
    const authentication = new StudioHostAuthentication(host);
    await expect(authentication.observe()).resolves.toEqual({ kind: 'unauthenticated', principalGeneration: 1n });
  });
  it('rejects a session issued under another authentication descriptor', async () => {
    Object.assign(globalThis, { document: { baseURI: 'https://studio.test/studio/' }, location: { origin: 'https://studio.test' },
      fetch: vi.fn(async () => new Response(JSON.stringify({ kind: 'authenticated', principalGeneration: '7', sessionChecksum: zero,
        audience: 'controlPlane', protectedScopeChecksum: zero, issuedAtUtc: '2026-01-01T00:00:00.0000000Z',
        expiresAtUtc: '2099-01-01T00:00:00.0000000Z', descriptorChecksum: '1'.repeat(64) }), { status: 200 })) });
    await expect(new StudioHostAuthentication(host).observe()).rejects.toThrow('base.studio.sessionInvalid');
  });
  it('single-flights rotating request authorization and injects only the returned header', async () => {
    let authorityCalls = 0; const requests: RequestInit[] = [];
    Object.assign(globalThis, { document: { baseURI: 'https://studio.test/studio/' }, location: { origin: 'https://studio.test' },
      fetch: vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
        if (String(input).endsWith('/auth/authorize')) { authorityCalls++; await Promise.resolve(); return new Response(JSON.stringify({
          headerName: 'X-HPD-CSRF', headerValue: 'protected-value', authorizedThroughUtc: '2099-01-01T00:00:00.0000000Z', descriptorChecksum: zero,
          purpose: JSON.parse(String(init?.body)).purpose }), { status: 200 }); }
        requests.push(init ?? {}); return new Response('{}', { status: 200 });
      }) });
    const authentication = new StudioHostAuthentication(host);
    await Promise.all([authentication.authorize('/studio/control/a', {}, 'observation'), authentication.authorize('/studio/control/b', {}, 'observation')]);
    expect(authorityCalls).toBe(1); expect(new Headers(requests[0]?.headers).get('X-HPD-CSRF')).toBe('protected-value');
  });
  it('never shares or accepts request authority across purposes', async () => {
    let authorityCalls = 0;
    Object.assign(globalThis, { document: { baseURI: 'https://studio.test/studio/' }, location: { origin: 'https://studio.test' },
      fetch: vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
        if (String(input).endsWith('/auth/authorize')) { authorityCalls++; const requested = JSON.parse(String(init?.body)).purpose;
          return new Response(JSON.stringify({ headerName: 'X-HPD-CSRF', headerValue: 'protected',
            authorizedThroughUtc: '2099-01-01T00:00:00.0000000Z', descriptorChecksum: zero,
            purpose: requested === 'observation' ? 'commandExecution' : requested }), { status: 200 }); }
        return new Response('{}');
      }) });
    const authentication = new StudioHostAuthentication(host);
    const values = await Promise.allSettled([authentication.authorize('/studio/a', {}, 'observation'),
      authentication.authorize('/studio/b', {}, 'commandExecution')]);
    expect(authorityCalls).toBe(2); expect(values[0]?.status).toBe('rejected'); expect(values[1]?.status).toBe('fulfilled');
  });
  it('caller cancellation stops only that waiter on a shared authorization flight', async () => {
    let release!: () => void; const gate = new Promise<void>(resolve => release = resolve); let calls = 0;
    Object.assign(globalThis, { document: { baseURI: 'https://studio.test/studio/' }, location: { origin: 'https://studio.test' },
      fetch: vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
        if (String(input).endsWith('/auth/authorize')) { calls++; await gate; return new Response(JSON.stringify({ headerName: 'X-HPD-CSRF',
          headerValue: 'protected', authorizedThroughUtc: '2099-01-01T00:00:00.0000000Z', descriptorChecksum: zero,
          purpose: JSON.parse(String(init?.body)).purpose }), { status: 200 }); }
        return new Response('{}');
      }) });
    const authentication = new StudioHostAuthentication(host); const first = new AbortController();
    const cancelled = authentication.authorize('/studio/a', { signal: first.signal }, 'observation');
    const retained = authentication.authorize('/studio/b', {}, 'observation'); first.abort(); release();
    await expect(cancelled).rejects.toThrow(); await expect(retained).resolves.toBeInstanceOf(Response); expect(calls).toBe(1);
  });
  it('rejects substituted fresh-authentication authority shapes', async () => {
    Object.assign(globalThis, { document: { baseURI: 'https://studio.test/studio/' }, location: { origin: 'https://studio.test' },
      fetch: vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => String(input).endsWith('/auth/authorize')
        ? new Response(JSON.stringify({ headerName: 'X-HPD-CSRF', headerValue: 'protected', authorizedThroughUtc: '2099-01-01T00:00:00.0000000Z',
          descriptorChecksum: zero, purpose: JSON.parse(String(init?.body)).purpose }), { status: 200 })
        : new Response(JSON.stringify({ kind: 'satisfied', authority: 'A'.repeat(32), token: 'substituted' }), { status: 200 })) });
    await expect(new StudioHostAuthentication(host).acquireFreshAuthentication({ requestIdentity: crypto.randomUUID(), commandId: 'record.delete',
      targetToken: 'opaque', previewChecksum: zero }, new AbortController().signal)).rejects.toThrow('base.studio.freshAuthenticationInvalid');
  });
  it('decodes the exact fresh-authentication challenge without exposing continuation bytes elsewhere', async () => {
    const continuation='C'.repeat(32);Object.assign(globalThis,{document:{baseURI:'https://studio.test/studio/'},location:{origin:'https://studio.test'},
      fetch:vi.fn(async(input:RequestInfo|URL,init?:RequestInit)=>String(input).endsWith('/auth/authorize')
        ?new Response(JSON.stringify({headerName:'X-HPD-CSRF',headerValue:'protected',authorizedThroughUtc:'2099-01-01T00:00:00.0000000Z',descriptorChecksum:zero,purpose:JSON.parse(String(init?.body)).purpose}),{status:200})
        :new Response(JSON.stringify({kind:'challenge',continuation,browserAction:{kind:'redirect',target:`https://studio.test/base/studio/auth/fresh/callback?continuation=${continuation}`},expiresAtUtc:'2099-01-01T00:00:00.0000000Z'}),{status:200}))});
    await expect(new StudioHostAuthentication(host).acquireFreshAuthentication({requestIdentity:crypto.randomUUID(),commandId:'record.delete',targetToken:'opaque',previewChecksum:zero},new AbortController().signal))
      .resolves.toMatchObject({kind:'challenge',continuation,browserAction:{kind:'redirect'}});
  });
  it('consumes the fixed callback continuation once without posting or retaining it in browser history', () => {
    const continuation='C'.repeat(32);const replaceState=vi.fn();const fetch=vi.fn();Object.assign(globalThis,{document:{baseURI:'https://studio.test/studio/'},
      location:{origin:'https://studio.test',href:`https://studio.test/base/studio/auth/fresh/callback?continuation=${continuation}`},
      history:{state:null,replaceState},fetch});
    expect(new StudioHostAuthentication(host).consumeFreshAuthenticationCallback()).toBe(true);
    expect(replaceState).toHaveBeenCalledWith(null,'','/base/studio/auth/fresh/callback');expect(fetch).not.toHaveBeenCalled();
  });
  it('rejects callback continuation smuggling before history replacement', () => {
    const continuation='C'.repeat(32);const replaceState=vi.fn();Object.assign(globalThis,{document:{baseURI:'https://studio.test/studio/'},
      location:{origin:'https://studio.test',href:`https://studio.test/base/studio/auth/fresh/callback?continuation=${continuation}&return=foreign`},
      history:{state:null,replaceState}});
    expect(()=>new StudioHostAuthentication(host).consumeFreshAuthenticationCallback()).toThrow('base.studio.freshAuthenticationInvalid');
    expect(replaceState).not.toHaveBeenCalled();
  });
});
