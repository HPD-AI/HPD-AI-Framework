import type { StudioHostContract } from './host-contract.ts';

export type StudioSessionSnapshot = Readonly<
  | { readonly kind: 'unauthenticated'; readonly principalGeneration: bigint }
  | { readonly kind: 'authenticated'; readonly principalGeneration: bigint; readonly sessionChecksum: string; readonly audience: string;
      readonly protectedScopeChecksum: string; readonly issuedAtUtc: string; readonly expiresAtUtc: string; readonly descriptorChecksum: string }
>;
export type StudioAuthorizationPurpose = 'bootstrap' | 'observation' | 'commandPreview' | 'commandExecution' |
  'receiptResolution' | 'artifactStaging' | 'signOut';
export type StudioFreshAuthenticationResult=Readonly<
  |{kind:'satisfied';authority:string;expiresAtUtc:string}
  |{kind:'challenge';continuation:string;browserAction:Readonly<{kind:'redirect'|'webAuthn'|'externalIdp';target:string}>;expiresAtUtc:string}
  |{kind:'unsupported'}>;

/** Host-integrated same-origin authentication; credentials never cross this boundary. */
export class StudioHostAuthentication {
  readonly #host: StudioHostContract;
  readonly #listeners = new Set<(value: StudioSessionSnapshot) => void>();
  readonly #authorizationFlights = new Map<StudioAuthorizationPurpose, Promise<Readonly<{ headerName: string; headerValue: string }>>>();
  #snapshot: StudioSessionSnapshot = Object.freeze({ kind: 'unauthenticated', principalGeneration: 0n });
  constructor(host: StudioHostContract) { this.#host = host; }
  get current(): StudioSessionSnapshot { return this.#snapshot; }
  subscribe(listener: (value: StudioSessionSnapshot) => void): () => void {
    this.#listeners.add(listener); try { listener(this.#snapshot); } catch { /* initial observers are isolated */ }
    return () => this.#listeners.delete(listener);
  }
  async observe(signal?: AbortSignal): Promise<StudioSessionSnapshot> {
    const response = await fetch(new URL(this.#host.sessionRoute, globalThis.location.origin), {
      credentials: 'same-origin', headers: { Accept: 'application/json' }, cache: 'no-store', redirect: 'error', signal
    });
    if (response.status === 401) return this.#publish({ kind: 'unauthenticated', principalGeneration: this.#snapshot.principalGeneration + 1n });
    if (!response.ok) throw new Error('base.studio.authenticationUnavailable');
    const value = await response.json() as Record<string, unknown>;
    const keys = Object.keys(value).sort();
    const expected = ['audience', 'descriptorChecksum', 'expiresAtUtc', 'issuedAtUtc', 'kind', 'principalGeneration', 'protectedScopeChecksum', 'sessionChecksum'];
    if (keys.length !== expected.length || keys.some((key, index) => key !== expected[index]) ||
        value.kind !== 'authenticated' || !/^[1-9][0-9]{0,18}$/u.test(String(value.principalGeneration)) ||
        !sha(value.sessionChecksum) || !sha(value.protectedScopeChecksum) || !sha(value.descriptorChecksum) ||
        value.descriptorChecksum !== this.#host.authentication.descriptorChecksum || typeof value.audience !== 'string' || !/^[a-z][a-zA-Z0-9.-]{0,127}$/u.test(value.audience) ||
        !utc(value.issuedAtUtc) || !utc(value.expiresAtUtc) || Date.parse(value.issuedAtUtc as string) > Date.now() || Date.parse(value.expiresAtUtc as string) <= Date.now())
      throw new TypeError('base.studio.sessionInvalid');
    return this.#publish({ kind: 'authenticated', principalGeneration: BigInt(String(value.principalGeneration)),
      sessionChecksum: value.sessionChecksum as string, audience: value.audience, protectedScopeChecksum: value.protectedScopeChecksum as string,
      issuedAtUtc: value.issuedAtUtc as string, expiresAtUtc: value.expiresAtUtc as string, descriptorChecksum: value.descriptorChecksum as string });
  }
  beginSignIn(returnPath: string): void {
    const route = new URL(this.#host.loginRoute, globalThis.location.origin); route.searchParams.set('return', returnPath);
    globalThis.location.assign(route);
  }
  async beginSignOut(signal?: AbortSignal): Promise<void> {
    this.#publish({ kind: 'unauthenticated', principalGeneration: this.#snapshot.principalGeneration + 1n });
    const authority = await this.#acquireAuthorization('signOut', signal, true);
    await fetch(new URL(this.#host.logoutRoute, globalThis.location.origin), { method: 'POST', credentials: 'same-origin',
      headers: { [authority.headerName]: authority.headerValue }, cache: 'no-store', redirect: 'error', signal });
  }
  async acquireFreshAuthentication(request: Readonly<{requestIdentity:string;commandId:string;targetToken:string;previewChecksum:string}>, signal:AbortSignal):Promise<StudioFreshAuthenticationResult>{
    const response=await this.authorize(new URL('/base/studio/auth/fresh',document.baseURI),{method:'POST',headers:{'Content-Type':'application/json'},
      body:JSON.stringify(request),signal},'commandExecution');if(!response.ok)throw new Error('base.studio.freshAuthenticationUnavailable');
    return decodeFreshResult(await response.json());
  }
  async completeFreshAuthentication(challenge:Extract<StudioFreshAuthenticationResult,{kind:'challenge'}>,signal:AbortSignal):Promise<Exclude<StudioFreshAuthenticationResult,{kind:'challenge'}>>{
    if(Date.parse(challenge.expiresAtUtc)<=Date.now())throw new Error('base.studio.freshAuthenticationExpired');let popup:Window|null=null;
    const target=new URL(challenge.browserAction.target,document.baseURI);if(target.origin!==location.origin||target.username||target.password||target.hash)
      throw new TypeError('base.studio.freshAuthenticationInvalid');
    if(target.pathname!=='/base/studio/auth/fresh/callback'||target.searchParams.getAll('continuation').length!==1||
      target.searchParams.get('continuation')!==challenge.continuation||[...target.searchParams.keys()].some(key=>key!=='continuation'))
      throw new TypeError('base.studio.freshAuthenticationInvalid');
    popup=window.open(target,'hpd-studio-fresh-auth','popup,width=640,height=720');if(!popup)throw new Error('base.studio.freshAuthenticationPopupBlocked');
    try{while(Date.now()<Date.parse(challenge.expiresAtUtc)){const response=await this.authorize(new URL('/base/studio/auth/fresh/complete',document.baseURI),{method:'POST',headers:{'Content-Type':'application/json'},
        body:JSON.stringify({continuation:challenge.continuation}),signal},'commandExecution');if(!response.ok)throw new Error('base.studio.freshAuthenticationUnavailable');const result=decodeFreshResult(await response.json());
        if(result.kind!=='challenge')return result;if(result.continuation!==challenge.continuation||result.expiresAtUtc!==challenge.expiresAtUtc)
          throw new TypeError('base.studio.freshAuthenticationInvalid');await delay(500,signal);}throw new Error('base.studio.freshAuthenticationExpired');}finally{popup?.close();}
  }
  consumeFreshAuthenticationCallback():boolean{const url=new URL(location.href);if(url.pathname!=='/base/studio/auth/fresh/callback')return false;
    const values=url.searchParams.getAll('continuation');if(values.length!==1||[...url.searchParams.keys()].some(key=>key!=='continuation')||url.hash||!protectedValue(values[0]))
      throw new TypeError('base.studio.freshAuthenticationInvalid');history.replaceState(history.state,'',url.pathname);return true;}
  async authorize(input: RequestInfo | URL, init: RequestInit = {}, purpose: StudioAuthorizationPurpose): Promise<Response> {
    const url = new URL(typeof input === 'string' ? input : input instanceof URL ? input.href : input.url, document.baseURI);
    if (url.origin !== globalThis.location.origin) throw new TypeError('base.studio.crossOriginRejected');
    const authority = await this.#acquireAuthorization(purpose, init.signal ?? undefined);
    const execute = (): Promise<Response> => { const headers = new Headers(init.headers); headers.set(authority.headerName, authority.headerValue);
      return fetch(url, { ...init, headers, credentials: 'same-origin', cache: 'no-store', redirect: 'error' }); };
    let response = await execute();
    if (response.status === 401 && this.#host.authentication.kind === 'bearer' && this.#host.authentication.refreshSupported) {
      const refreshed = await this.#acquireAuthorization(purpose, init.signal ?? undefined, true); const headers = new Headers(init.headers);
      headers.set(refreshed.headerName, refreshed.headerValue);
      response = await fetch(url, { ...init, headers, credentials: 'same-origin', cache: 'no-store', redirect: 'error' });
    }
    if (response.status === 401) this.#publish({ kind: 'unauthenticated', principalGeneration: this.#snapshot.principalGeneration + 1n });
    return response;
  }
  #publish(value: StudioSessionSnapshot): StudioSessionSnapshot {
    this.#snapshot = Object.freeze(value);
    for (const listener of this.#listeners) try { listener(this.#snapshot); } catch { /* observers cannot alter session truth */ }
    return this.#snapshot;
  }
  #acquireAuthorization(purpose: StudioAuthorizationPurpose, signal?: AbortSignal, force = false): Promise<Readonly<{ headerName: string; headerValue: string }>> {
    if (force) this.#authorizationFlights.delete(purpose);
    const existing = this.#authorizationFlights.get(purpose); if (existing) return waitForCaller(existing, signal);
    const route = new URL(this.#host.authentication.authorizationRoute, globalThis.location.origin);
    const controller = new AbortController(); const deadline = globalThis.setTimeout(() => controller.abort(), 30_000);
    const flight = (async () => {
      const response = await fetch(route, { method: 'POST', credentials: 'same-origin', cache: 'no-store', redirect: 'error',
        headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ purpose }), signal: controller.signal });
      if (!response.ok) throw new Error('base.studio.authorizationUnavailable');
      const value = await response.json() as Record<string, unknown>; const keys = Object.keys(value).sort();
      if (keys.length !== 5 || keys[0] !== 'authorizedThroughUtc' || keys[1] !== 'descriptorChecksum' || keys[2] !== 'headerName' || keys[3] !== 'headerValue' || keys[4] !== 'purpose' ||
          value.purpose !== purpose ||
          value.descriptorChecksum !== this.#host.authentication.descriptorChecksum || typeof value.headerName !== 'string' || !/^[A-Za-z][A-Za-z0-9-]{0,63}$/u.test(value.headerName) ||
          typeof value.headerValue !== 'string' || value.headerValue.length === 0 || value.headerValue.length > 4096 || /[\r\n]/u.test(value.headerValue) ||
          !utc(value.authorizedThroughUtc) || Date.parse(value.authorizedThroughUtc) <= Date.now()) throw new TypeError('base.studio.authorizationInvalid');
      return Object.freeze({ headerName: value.headerName, headerValue: value.headerValue });
    })().finally(() => { globalThis.clearTimeout(deadline); if (this.#authorizationFlights.get(purpose) === flight) this.#authorizationFlights.delete(purpose); });
    this.#authorizationFlights.set(purpose, flight); return waitForCaller(flight, signal);
  }
}

function sha(value: unknown): value is string { return typeof value === 'string' && /^[a-f0-9]{64}$/u.test(value); }
function protectedValue(value:unknown):value is string{return typeof value==='string'&&value.length>=32&&value.length<=4096&&/^[A-Za-z0-9_-]+$/u.test(value)}
function utc(value: unknown): value is string { return typeof value === 'string' && /^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}\.[0-9]{7}Z$/u.test(value); }
function decodeFreshResult(input:unknown):StudioFreshAuthenticationResult{if(input===null||typeof input!=='object'||Array.isArray(input))throw new TypeError('base.studio.freshAuthenticationInvalid');const value=input as Record<string,unknown>;
  if(value.kind==='unsupported'&&Object.keys(value).length===1)return Object.freeze({kind:'unsupported'});
  if(value.kind==='satisfied'&&Object.keys(value).sort().join('\0')==='authority\0expiresAtUtc\0kind'&&protectedValue(value.authority)&&utc(value.expiresAtUtc)&&Date.parse(value.expiresAtUtc)>Date.now())
    return Object.freeze({kind:'satisfied',authority:value.authority,expiresAtUtc:value.expiresAtUtc});
  if(value.kind==='challenge'&&Object.keys(value).sort().join('\0')==='browserAction\0continuation\0expiresAtUtc\0kind'&&protectedValue(value.continuation)&&utc(value.expiresAtUtc)&&Date.parse(value.expiresAtUtc)>Date.now()&&
    value.browserAction!==null&&typeof value.browserAction==='object'&&!Array.isArray(value.browserAction)&&Object.keys(value.browserAction).sort().join('\0')==='kind\0target'){
    const action=value.browserAction as Record<string,unknown>;if(!['redirect','webAuthn','externalIdp'].includes(String(action.kind))||typeof action.target!=='string'||action.target.length<1||action.target.length>2048)
      throw new TypeError('base.studio.freshAuthenticationInvalid');
    return Object.freeze({kind:'challenge',continuation:value.continuation,expiresAtUtc:value.expiresAtUtc,browserAction:Object.freeze({kind:action.kind as 'redirect'|'webAuthn'|'externalIdp',target:action.target})});}
  throw new TypeError('base.studio.freshAuthenticationInvalid');}
function delay(milliseconds:number,signal:AbortSignal):Promise<void>{return new Promise((resolve,reject)=>{if(signal.aborted){reject(signal.reason);return;}const timer=setTimeout(done,milliseconds);
  function done(){signal.removeEventListener('abort',abort);resolve();}function abort(){clearTimeout(timer);reject(signal.reason??new DOMException('Aborted','AbortError'));}signal.addEventListener('abort',abort,{once:true});});}
function waitForCaller<T>(shared: Promise<T>, signal?: AbortSignal): Promise<T> {
  if (!signal) return shared; if (signal.aborted) return Promise.reject(signal.reason ?? new DOMException('Aborted', 'AbortError'));
  return new Promise<T>((resolve, reject) => {
    const abort = (): void => reject(signal.reason ?? new DOMException('Aborted', 'AbortError'));
    signal.addEventListener('abort', abort, { once: true });
    shared.then(value => { signal.removeEventListener('abort', abort); resolve(value); }, error => { signal.removeEventListener('abort', abort); reject(error); });
  });
}
