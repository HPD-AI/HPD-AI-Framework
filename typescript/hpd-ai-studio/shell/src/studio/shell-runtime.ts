import {
  hydrateStudioRuntimeMap, validateStudioBootstrap, validateStudioModuleActivation,
  studioSha256,
  type StudioBootstrapSnapshot, type StudioClientBinding, type StudioClientId,
  type StudioModuleActivation, type StudioModuleActivationContext, type StudioModuleDescriptor,
  type StudioFrameworkClientActivator, type StudioFrameworkClientTransportRequest,
  type StudioNavigationHandle, type StudioPageComponent, type StudioRuntimeMethodMap, type StudioRuntimeTransport, type StudioRuntimeTransportRequest
} from '@hpd-research/hpd-studio-core';
import { StudioHistoryRouter } from './history-router.ts';
import type { StudioHistoryRoute } from './history-router.ts';
import { StudioHostAuthentication, type StudioSessionSnapshot } from './authentication.ts';
import type { StudioEditionModuleAsset, StudioHostContract } from './host-contract.ts';
import { createStudioPageRuntime, type StudioPageRuntime } from './page-runtime.ts';

export interface StudioActivePage { readonly route: StudioHistoryRoute; readonly component: StudioPageComponent; readonly runtime: StudioPageRuntime; }
export interface StudioShellState {
  readonly kind: 'authenticationRequired' | 'loading' | 'ready' | 'failed';
  readonly session: StudioSessionSnapshot;
  readonly bootstrap: StudioBootstrapSnapshot | null;
  readonly route: StudioActivePage | null;
  readonly quarantinedModuleIds: readonly string[];
  readonly failure?: string;
}
interface StudioModuleExports { readonly studioModuleDescriptor: StudioModuleDescriptor; readonly studioFrameworkClientActivators?: readonly StudioFrameworkClientActivator[];
  activateStudioModule(context: StudioModuleActivationContext): Promise<StudioModuleActivation>; }
interface ActiveModule { readonly descriptor: StudioModuleDescriptor; readonly activation: StudioModuleActivation; readonly lifecycle: ShellLifecycle; }

/** Principal-generation-owned shell runtime. Bootstrap replacement destroys modules, routes, and dynamic clients. */
export class StudioShellRuntime {
  readonly authentication: StudioHostAuthentication;
  readonly #host: StudioHostContract;
  readonly #listeners = new Set<(state: StudioShellState) => void>();
  #state: StudioShellState;
  #router: StudioHistoryRouter | null = null;
  #active: ActiveModule[] = [];
  #pageRuntime: StudioPageRuntime | null = null;
  #runtimeMap: StudioRuntimeMethodMap | null = null;
  #renewalTimer: ReturnType<typeof setTimeout> | null = null;
  #generation = 0;
  #disposed = false;
  constructor(host: StudioHostContract) {
    this.#host = host; this.authentication = new StudioHostAuthentication(host);
    this.#state = freezeState('loading', this.authentication.current, null, null, []);
    this.authentication.subscribe(session => { if (session.kind === 'unauthenticated' && this.#state.kind === 'ready') void this.#invalidate(session); });
  }
  get current(): StudioShellState { return this.#state; }
  subscribe(listener: (state: StudioShellState) => void): () => void {
    this.#listeners.add(listener); try { listener(this.#state); } catch { /* initial observers are isolated */ }
    return () => this.#listeners.delete(listener);
  }
  async start(signal?: AbortSignal): Promise<void> {
    const session = await this.authentication.observe(signal);
    if (session.kind === 'unauthenticated') { this.#publish(freezeState('authenticationRequired', session, null, null, [])); return; }
    await this.#bootstrap(session, signal);
  }
  navigate(url: string): void { this.#router?.navigate(url); }
  async refresh(signal?: AbortSignal): Promise<void> {
    const session = await this.authentication.observe(signal); if (session.kind === 'authenticated') await this.#bootstrap(session, signal, true);
  }
  async dispose(): Promise<void> {
    if (this.#disposed) return; this.#disposed = true; this.#generation++; await this.#destroy(); this.#listeners.clear();
  }
  async #bootstrap(session: Extract<StudioSessionSnapshot, { kind: 'authenticated' }>, signal?: AbortSignal, retainVisibleState = false): Promise<void> {
    const generation = ++this.#generation; await this.#destroy();
    if (!retainVisibleState) this.#publish(freezeState('loading', session, null, null, []));
    try {
      const response = await this.authentication.authorize(new URL(this.#host.bootstrapRoute, globalThis.location.origin), {
        method: 'POST', headers: { Accept: 'application/json', 'Content-Type': 'application/json', 'X-Requested-With': 'HPD-Studio' },
        body: JSON.stringify({ shellContractChecksum: this.#host.shellContractChecksum,
          editionAssetGraphChecksum: this.#host.editionAssetGraphChecksum, runtimeClientChecksum: this.#host.runtimeClientChecksum,
          locale: canonicalLocale(), clientCapabilities: [1, 2, 3, 4, 5] }), signal
      }, 'bootstrap');
      if (generation !== this.#generation || this.#disposed) return;
      if (response.status === 401) { this.#publish(freezeState('authenticationRequired', this.authentication.current, null, null, [])); return; }
      if (!response.ok) throw new Error(`base.studio.bootstrap.${response.status}`);
      const snapshot = validateStudioBootstrap(await response.json() as StudioBootstrapSnapshot);
      const transport = createTransport(this.authentication, snapshot.snapshotChecksum, snapshot.authority.checksum);
      const runtimeMap = hydrateStudioRuntimeMap(snapshot.contractMap, transport);
      this.#runtimeMap = runtimeMap;
      const quarantined: string[] = [];
      for (const visible of snapshot.modules) {
        const asset = this.#host.modules.find(item => item.moduleId === visible.moduleId && item.moduleVersion === visible.version);
        if (!asset || asset.assetGraphChecksum !== visible.assetGraphChecksum) { if (visible.necessity === 'required') throw new Error('base.studio.assetMismatch'); quarantined.push(visible.moduleId); continue; }
        try { this.#active.push(await activate(asset, visible.frontendAbiChecksum, snapshot,
          runtimeMap, this.authentication, session, async (target: Parameters<StudioNavigationHandle['navigate']>[0]) => this.#navigateResource(snapshot, target),
          () => this.#generation === generation)); }
        catch (error) {
          console.error(`HPD Studio module '${visible.moduleId}' activation failed.`, error);
          if (visible.necessity === 'required') throw new Error('base.studio.moduleUnavailable');
          quarantined.push(visible.moduleId);
        }
      }
      if (generation !== this.#generation || this.#disposed) return;
      this.#router = new StudioHistoryRouter(snapshot.pages);
      this.#router.subscribe(() => this.#publishRoute(snapshot, quarantined));
      if (!this.#router.current) {
        const landing = snapshot.pages.find(page => page.navigationRole === 'areaLanding');
        if (landing) this.#router.navigate(routePath(landing), true);
      }
      this.#scheduleRenewal(snapshot);
    } catch (error) {
      if (generation !== this.#generation || this.#disposed) return;
      await this.#destroy(); this.#publish(freezeState('failed', session, null, null, [], safeFailure(error)));
    }
  }
  #publishRoute(snapshot = this.#state.bootstrap, quarantined = [...this.#state.quarantinedModuleIds]): void {
    if (!snapshot) return; const matched = this.#router?.current ?? null; let route: StudioActivePage | null = null;
    this.#pageRuntime?.dispose(); this.#pageRuntime = null;
    if (matched) {
      const module = this.#active.find(item => item.descriptor.moduleId === matched.page.moduleId);
      const binding = module?.descriptor.pageComponents[matched.page.pageId];
      if (binding && this.#runtimeMap) {
        const pageRuntime = createStudioPageRuntime(snapshot, matched, this.#runtimeMap, url => this.navigate(url),
          async (request,signal)=>{const result=await this.authentication.acquireFreshAuthentication(request,signal);
            return result.kind==='challenge'?this.authentication.completeFreshAuthentication(result,signal):result;});
        this.#pageRuntime = pageRuntime; pageRuntime.subscribe(() => this.#publishPageObservation(snapshot, quarantined));
        route = Object.freeze({ route: matched, component: binding.component, runtime: pageRuntime });
        void pageRuntime.refresh().then(() => {
          if (this.#pageRuntime !== pageRuntime || this.#disposed) return;
          this.#publish(freezeState('ready', this.authentication.current, snapshot, route, quarantined));
        });
        return;
      }
    }
    this.#publish(freezeState('ready', this.authentication.current, snapshot, route, quarantined));
  }
  #publishPageObservation(snapshot: StudioBootstrapSnapshot, quarantined: readonly string[]): void {
    const route = this.#state.route; if (!route || route.runtime !== this.#pageRuntime) return;
    this.#publish(freezeState('ready', this.authentication.current, snapshot,
      Object.freeze({ route: route.route, component: route.component, runtime: route.runtime }), quarantined));
  }
  async #navigateResource(snapshot: StudioBootstrapSnapshot, target: Parameters<StudioNavigationHandle['navigate']>[0]): Promise<void> {
    const runtime = this.#pageRuntime; if (!runtime) throw new Error('base.studio.navigationUnavailable');
    await runtime.navigation.navigate(target); void snapshot;
  }
  async #invalidate(session: StudioSessionSnapshot): Promise<void> { this.#generation++; await this.#destroy(); this.#publish(freezeState('authenticationRequired', session, null, null, [])); }
  async #destroy(): Promise<void> {
    if (this.#renewalTimer !== null) clearTimeout(this.#renewalTimer); this.#renewalTimer = null;
    this.#pageRuntime?.dispose(); this.#pageRuntime = null; this.#runtimeMap = null;
    this.#router?.dispose(); this.#router = null; const active = this.#active.splice(0).reverse();
    for (const item of active) item.lifecycle.abort();
    await settleStudioTeardown(active.flatMap(item => [Promise.resolve().then(() => item.activation.dispose()), item.lifecycle.dispose()]), 5_000);
  }
  #scheduleRenewal(snapshot: StudioBootstrapSnapshot): void {
    if (this.#renewalTimer !== null) clearTimeout(this.#renewalTimer);
    const remaining = Date.parse(snapshot.authority.authorizedThroughUtc) - Date.now();
    if (!Number.isFinite(remaining) || remaining <= 0 || this.#disposed) return;
    const lead = Math.min(30_000, Math.max(1_000, Math.floor(remaining / 10)));
    this.#renewalTimer = setTimeout(() => { this.#renewalTimer = null; void this.refresh(); }, Math.max(0, remaining - lead));
  }
  #publish(state: StudioShellState): void {
    this.#state = state; for (const listener of this.#listeners) try { listener(state); } catch { /* shell observers are isolated */ }
  }
}

async function activate(asset: StudioEditionModuleAsset, checksum: string, snapshot: StudioBootstrapSnapshot,
  runtimeMap: StudioRuntimeMethodMap, authentication: StudioHostAuthentication,
  session: Extract<StudioSessionSnapshot, { kind: 'authenticated' }>, navigate: StudioNavigationHandle['navigate'], current: () => boolean): Promise<ActiveModule> {
  const loaded = await within(import(/* @vite-ignore */ new URL(asset.entryModulePath, globalThis.location.origin).href) as Promise<StudioModuleExports>, 10_000,
    'base.studio.moduleImportTimeout');
  if (!current() || !loaded || typeof loaded.activateStudioModule !== 'function' || !loaded.studioModuleDescriptor) throw new TypeError();
  const descriptor = loaded.studioModuleDescriptor; if (descriptor.frontendAbiChecksum !== checksum) throw new TypeError();
  const lifecycle = new ShellLifecycle();
  const clients = new Map<StudioClientId, StudioClientBinding>();
  for (const visible of snapshot.clients.filter(client => client.moduleId === asset.moduleId)) {
    let client: object;
    if (visible.protocol === 'baseL41DynamicMap') client = runtimeMap;
    else {
      const activators = loaded.studioFrameworkClientActivators ?? [];
      const activator = activators.find(value => value.clientId === visible.clientId && value.version === visible.version);
      if (!activator || activators.filter(value => value.clientId === visible.clientId && value.version === visible.version).length !== 1 ||
          activator.runtimeAbiChecksum !== visible.staticRuntimeAbiChecksum || activator.generatedContractChecksum !== visible.generatedContractChecksum ||
          activator.operationInventoryChecksum !== visible.operationInventoryChecksum) throw new TypeError('base.studio.frameworkClientMismatch');
      const lease = await within(activator.create(Object.freeze({ endpointSurfaceId: visible.endpointSurfaceId,
        principalGeneration: BigInt(session.principalGeneration), authenticationSessionChecksum: studioSha256(session.sessionChecksum),
        signal: lifecycle.signal, transport: createStudioFrameworkTransport(authentication, visible, snapshot.snapshotChecksum, snapshot.authority.checksum),
        limits: numericClientLimits(visible.limits) })), Number(visible.limits.acquisitionDeadlineMilliseconds), 'base.studio.frameworkClientAcquireTimeout');
      if (!lease || typeof lease.client !== 'object' || typeof lease.dispose !== 'function') throw new TypeError('base.studio.frameworkClientLeaseInvalid');
      lifecycle.defer(() => within(Promise.resolve(lease.dispose()), Number(visible.limits.disposalDeadlineMilliseconds), 'base.studio.frameworkClientDisposeTimeout'));
      client = lease.client;
    }
    clients.set(visible.clientId, Object.freeze({ clientId: visible.clientId, version: visible.version, protocol: visible.protocol,
      staticRuntimeAbiChecksum: visible.staticRuntimeAbiChecksum, generatedContractChecksum: visible.generatedContractChecksum,
      operationInventoryChecksum: visible.operationInventoryChecksum, endpointSurfaceId: visible.endpointSurfaceId,
      transportClass: visible.transportClass, owningPageIds: visible.owningPageIds, limitsChecksum: visible.limits.checksum, client }));
  }
  const navigation: StudioNavigationHandle = Object.freeze({ navigate });
  const context: StudioModuleActivationContext = Object.freeze({ moduleId: asset.moduleId, moduleVersion: asset.moduleVersion,
    frontendAbiChecksum: descriptor.frontendAbiChecksum, disclosedPageIds: Object.freeze(snapshot.pages.filter(page => page.moduleId === asset.moduleId).map(page => page.pageId)),
    clients, navigation, lifecycle });
  validateStudioModuleActivation(descriptor, context);
  const pending = loaded.activateStudioModule(context);
  pending.then(value => { if (!current()) void within(value.dispose(), 5_000, 'base.studio.moduleDisposeTimeout').catch(() => {}); }, () => {});
  const activation = await within(pending, 10_000, 'base.studio.moduleActivationTimeout');
  if (!current() || activation.moduleId !== asset.moduleId || activation.moduleVersion !== asset.moduleVersion || activation.frontendAbiChecksum !== checksum)
    throw new TypeError('base.studio.moduleActivationInvalid');
  return Object.freeze({ descriptor, activation, lifecycle });
}

function numericClientLimits(value: StudioBootstrapSnapshot['clients'][number]['limits']): import('@hpd-research/hpd-studio-core').StudioFrameworkClientHostContext['limits'] {
  return Object.freeze({ maximumOperations: value.maximumOperations, maximumRequestBytes: Number(value.maximumRequestBytes),
    maximumResponseBytes: Number(value.maximumResponseBytes), maximumConcurrentRequests: value.maximumConcurrentRequests,
    acquisitionDeadlineMilliseconds: Number(value.acquisitionDeadlineMilliseconds), operationDeadlineMilliseconds: Number(value.operationDeadlineMilliseconds),
    disposalDeadlineMilliseconds: Number(value.disposalDeadlineMilliseconds) });
}

/** @internal Creates the sealed snapshot-bound framework-client bridge. */
export function createStudioFrameworkTransport(authentication: StudioHostAuthentication, client: StudioBootstrapSnapshot['clients'][number], snapshotChecksum: string,
  authorityChecksum: string): Readonly<{ execute(request: StudioFrameworkClientTransportRequest): Promise<Response> }> {
  return Object.freeze({ async execute(request: StudioFrameworkClientTransportRequest): Promise<Response> {
    const operation = client.operations.find(item => item.operationId === request.operation);
    if (!/^[a-z][a-zA-Z0-9]*(?:[.-][a-zA-Z0-9]+)*$/u.test(request.operation) || !/^\/[A-Za-z0-9._~!$&'()*+,;=:@%/?-]{1,2048}$/u.test(request.relativePathAndQuery) ||
        request.relativePathAndQuery.includes('//') || /(?:^|\/)\.\.?(?:\/|\?|$)/u.test(request.relativePathAndQuery) ||
        !['GET','POST','PUT','PATCH','DELETE'].includes(request.method) ||
        !['observation','commandPreview','commandExecution','receiptResolution','artifactStaging'].includes(request.purpose) ||
        !validFrameworkHeaders(request.headers) || !operation || operation.method !== request.method || operation.purpose !== request.purpose ||
        !matchesFrameworkPath(operation.relativePathTemplate, request.relativePathAndQuery.split('?', 1)[0]!.slice(1)) ||
        request.maximumResponseBytes > Math.min(Number(operation.maximumResponseBytes), Number(client.limits.maximumResponseBytes)) ||
        request.deadlineMilliseconds > Math.min(Number(operation.deadlineMilliseconds), Number(client.limits.operationDeadlineMilliseconds)) ||
        new TextEncoder().encode(request.body ?? '').byteLength > Math.min(Number(operation.maximumRequestBytes), Number(client.limits.maximumRequestBytes)) ||
        request.body !== undefined && !operation.requestMediaTypes.some(media => media.toLowerCase() === request.headers['Content-Type']?.toLowerCase()) ||
        Object.keys(request.headers).some(name => !operation.requestHeaderNames.some(allowed => allowed.toLowerCase() === name.toLowerCase()) &&
          !['accept','content-type'].includes(name.toLowerCase())))
      throw new TypeError('base.studio.frameworkClientRequestInvalid');
    const route = `/base/studio/framework-clients/${encodeURIComponent(client.endpointSurfaceId)}${request.relativePathAndQuery}`;
    const controller = new AbortController(); const timeout = globalThis.setTimeout(() => controller.abort(), request.deadlineMilliseconds);
    request.signal.addEventListener('abort', () => controller.abort(), { once: true });
    try {
      const response = await authentication.authorize(new URL(route.slice(1), document.baseURI), { method: request.method,
        headers: { ...request.headers, 'X-HPD-Studio-Operation': request.operation, 'X-HPD-Studio-Snapshot': snapshotChecksum }, body: request.body, signal: controller.signal },
      request.purpose);
      requireStudioResponseAuthority(response, authorityChecksum);
      return response;
    } finally { globalThis.clearTimeout(timeout); }
  } });
}
function matchesFrameworkPath(template: string, path: string): boolean { const expected = template.split('/'); const actual = path.split('/');
  return expected.length === actual.length && expected.every((segment, index) => { const value = actual[index]!; const open = segment.indexOf('{');
    if (open < 0) return segment === value; const close = segment.indexOf('}', open + 1); const prefix = segment.slice(0, open); const suffix = segment.slice(close + 1);
    return value.startsWith(prefix) && value.endsWith(suffix) && value.length > prefix.length + suffix.length &&
      value.length - prefix.length - suffix.length <= 512 && !/[\u0000-\u001f\u007f/]/u.test(value.slice(prefix.length, value.length - suffix.length)); }); }
function validFrameworkHeaders(headers: Readonly<Record<string, string>>): boolean {
  const allowed = new Set(['accept','content-type','x-correlation-id','idempotency-key','if-match']);
  const entries = Object.entries(headers); return entries.length <= allowed.size && entries.every(([name, value]) =>
    allowed.has(name.toLowerCase()) && typeof value === 'string' && new TextEncoder().encode(value).length <= 1024 && !/[\r\n]/u.test(value));
}

class ShellLifecycle {
  readonly #controller = new AbortController(); readonly #disposers: Array<() => void | Promise<void>> = []; #disposed = false;
  get signal(): AbortSignal { return this.#controller.signal; }
  defer(dispose: () => void | Promise<void>): void { if (this.#disposed || this.#disposers.length >= 128) throw new Error('base.studio.lifecycleUnavailable'); this.#disposers.push(dispose); }
  abort(): void { this.#controller.abort(); }
  async dispose(): Promise<void> {
    if (this.#disposed) return; this.#disposed = true; this.abort();
    await settleStudioTeardown(this.#disposers.reverse().map(dispose => Promise.resolve().then(dispose)), 5_000);
  }
}

function createTransport(authentication: StudioHostAuthentication, snapshotChecksum: string, responseAuthorityChecksum: string): StudioRuntimeTransport {
  const execute = async (request: StudioRuntimeTransportRequest): Promise<Readonly<{ ok: boolean; body: string }>> => {
    const controller = new AbortController(); const deadline = Number(request.deadlineMilliseconds);
    const timeout = globalThis.setTimeout(() => controller.abort(), Math.min(deadline, 60_000));
    request.signal?.addEventListener('abort', () => controller.abort(), { once: true });
    try { const response = await authentication.authorize(new URL(request.relativeRoute.replace(/^\//u, ''), document.baseURI), {
      method: request.method, headers: { Accept: 'application/json', 'Content-Type': 'application/json',
        'X-HPD-Studio-Method': request.registeredMethodId, 'X-HPD-Studio-Snapshot': snapshotChecksum }, body: request.body, signal: controller.signal }, purpose(request.registeredKind));
      requireStudioResponseAuthority(response, responseAuthorityChecksum);
      const body = await readStudioResponseBody(response, request.maximumResultBytes, controller.signal);
      if(response.ok)return Object.freeze({ok:true as const,body});const header=response.headers.get('X-HPD-Studio-Error');
      const failureCode=header==='base.studio.failedBeforeInfluence'||header==='base.studio.commandIndeterminate'?header:undefined;
      return Object.freeze({ok:false as const,body,...(failureCode===undefined?{}:{failureCode})});
    } finally { globalThis.clearTimeout(timeout); }
  };
  return Object.freeze({ executeJson: execute,
    async *subscribe(): AsyncIterable<never> { throw new Error('base.studio.realtimeTransportUnavailable'); },
    async upload(): Promise<never> { throw new Error('base.studio.uploadTransportUnavailable'); } });
}

function routePath(page: StudioBootstrapSnapshot['pages'][number]): string {
  if (page.route.segments.some(segment => segment.kind === 'parameter') || page.route.query.some(member => member.required)) return '/';
  return `/${page.route.segments.map(segment => segment.kind === 'literal' ? segment.value : '').join('/')}`;
}
function canonicalLocale(): string { const value = navigator.language; return /^[A-Za-z0-9-]{1,35}$/u.test(value) ? value : 'en-US'; }
function purpose(kind: StudioRuntimeTransportRequest['registeredKind']): import('./authentication.ts').StudioAuthorizationPurpose {
  switch (kind) {
    case 'resolve': case 'page': case 'invalidationSubscribe': return 'observation';
    case 'preview': return 'commandPreview';
    case 'execute': return 'commandExecution';
    case 'receiptQuery': case 'receiptResolve': return 'receiptResolution';
    case 'stageCreate': case 'stageUpload': case 'stageFinalize': case 'stageDispose': return 'artifactStaging';
  }
}
function safeFailure(error: unknown): string { return error instanceof Error && /^base\.studio\.[A-Za-z0-9.]+$/u.test(error.message) ? error.message : 'base.studio.moduleUnavailable'; }
function freezeState(kind: StudioShellState['kind'], session: StudioSessionSnapshot, bootstrap: StudioBootstrapSnapshot | null,
  route: StudioActivePage | null, quarantinedModuleIds: readonly string[], failure?: string): StudioShellState {
  return Object.freeze({ kind, session, bootstrap, route, quarantinedModuleIds: Object.freeze([...quarantinedModuleIds]), ...(failure ? { failure } : {}) });
}

/** @internal Reads a response without allocating beyond its admitted byte authority. */
export async function readStudioResponseBody(response: Response, maximumBytes: bigint, signal: AbortSignal): Promise<string> {
  if (response.body === null) return '';
  const reader = response.body.getReader(); const chunks: Uint8Array[] = []; let total = 0n;
  const abort = (): void => { void reader.cancel(); }; signal.addEventListener('abort', abort, { once: true });
  try {
    while (true) {
      const value = await reader.read(); if (value.done) break;
      total += BigInt(value.value.byteLength); if (total > maximumBytes) { await reader.cancel(); throw new RangeError('base.studio.resultTooLarge'); }
      chunks.push(value.value);
    }
  } finally { signal.removeEventListener('abort', abort); reader.releaseLock(); }
  const bytes = new Uint8Array(Number(total)); let offset = 0;
  for (const chunk of chunks) { bytes.set(chunk, offset); offset += chunk.byteLength; }
  return new TextDecoder('utf-8', { fatal: true }).decode(bytes);
}

/** @internal Validates the principal/snapshot lease on every protected response, independent of outcome status. */
export function requireStudioResponseAuthority(response: Response, expected: string): void {
  if (response.headers.get('X-HPD-Studio-Response-Authority') !== expected)
    throw new TypeError('base.studio.responseAuthorityMismatch');
}

function within<T>(promise: Promise<T>, milliseconds: number, code: string): Promise<T> {
  return new Promise<T>((resolve, reject) => {
    const timeout = globalThis.setTimeout(() => reject(new Error(code)), milliseconds);
    promise.then(value => { globalThis.clearTimeout(timeout); resolve(value); }, error => { globalThis.clearTimeout(timeout); reject(error); });
  });
}

/** @internal Settles a finite teardown set under one aggregate deadline and detaches late work. */
export async function settleStudioTeardown(tasks: readonly Promise<unknown>[], maximumMilliseconds: number): Promise<void> {
  if (!Number.isInteger(maximumMilliseconds) || maximumMilliseconds < 1 || maximumMilliseconds > 30_000 || tasks.length > 256)
    throw new RangeError('base.studio.teardownBoundsInvalid');
  try { await within(Promise.allSettled(tasks), maximumMilliseconds, 'base.studio.moduleDisposeTimeout'); } catch { /* bounded quarantine */ }
}
