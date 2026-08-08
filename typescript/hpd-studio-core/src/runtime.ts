import type {
  ComposeStudioOptions,
  StudioJson,
  StudioLifecycle,
  StudioModuleRegistration,
  StudioQuarantinedModule,
  StudioRouteObservation,
  StudioRuntime,
  StudioRuntimeModule,
  StudioRuntimeRoute
} from './contracts.ts';
import { createModuleContexts } from './context-internal.ts';

const MAXIMUM_MODULES = 64;
const MAXIMUM_ROUTES = 64;
const MAXIMUM_NAV_ITEMS = 64;
const MAXIMUM_RESOURCES = 128;
const MAXIMUM_TEXT_BYTES = 128;
const MAXIMUM_SUMMARY_BYTES = 512;
const MAXIMUM_CONFIGURATION_BYTES = 65_536;
const ID = /^[a-z0-9]+(?:-[a-z0-9]+)*$/;
const SEGMENT = ID;

export class StudioCompositionError extends Error {
  readonly code: string;
  readonly moduleId?: string;
  constructor(code: string, moduleId?: string) {
    super(moduleId ? `${code}: ${moduleId}` : code);
    this.name = 'StudioCompositionError';
    this.code = code;
    this.moduleId = moduleId;
  }
}

interface PreparedRegistration {
  registration: StudioModuleRegistration;
  module: StudioRuntimeModule;
  configuration: Readonly<Record<string, StudioJson>>;
}

interface ActiveModule {
  module: StudioRuntimeModule;
  scope: LifecycleScope;
  clearContext(): void;
}

export async function composeStudio(options: ComposeStudioOptions): Promise<StudioRuntime> {
  validateShell(options);
  const authentication = sealAuthentication(options.authentication);
  const registrations = materializeBounded(options.modules, MAXIMUM_MODULES, 'studio.modules.capacityExceeded');
  const prepared = prepare(registrations);
  const active: ActiveModule[] = [];
  const quarantined: StudioQuarantinedModule[] = [];

  for (const item of prepared) {
    const scope = new LifecycleScope();
    const contexts = createModuleContexts(item.module.id);
    try {
      const activation = await item.registration.module.initialize?.({
        moduleId: item.module.id,
        mode: options.configuration.mode,
        configuration: item.configuration,
        authentication,
        contexts: contexts.writer,
        lifecycle: scope
      });
      if (activation !== undefined) {
        if (!activation || typeof activation !== 'object' ||
            (activation.dispose !== undefined && typeof activation.dispose !== 'function')) {
          throw new StudioCompositionError('studio.module.activationInvalid', item.module.id);
        }
        if (activation.dispose) scope.defer(() => activation.dispose!());
      }
      active.push({ module: withContexts(item.module, contexts.handle), scope, clearContext: contexts.clear });
    } catch {
      await scope.dispose();
      contexts.clear();
      if (item.registration.requirement === 'optional') {
        quarantined.push(Object.freeze({ id: item.module.id, code: 'studio.module.initializationFailed' }));
        continue;
      }
      await disposeActive(active);
      throw new StudioCompositionError('studio.module.requiredInitializationFailed', item.module.id);
    }
  }

  return createRuntime(options, authentication, active, Object.freeze(quarantined));
}

function prepare(registrations: readonly StudioModuleRegistration[]): PreparedRegistration[] {
  const ids = new Set<string>();
  const owners = new Map<string, string>();
  const ordered = registrations.map((registration) => {
    if (!registration || !registration.module || !['required', 'optional'].includes(registration.requirement)) {
      throw new StudioCompositionError('studio.module.registrationInvalid');
    }
    requireId(registration.module.id, 'studio.module.idInvalid');
    return registration;
  }).sort((left, right) => left.module.id.localeCompare(right.module.id));
  const prepared = ordered.map((registration) => {
    const module = registration.module;
    if (ids.has(module.id)) throw new StudioCompositionError('studio.module.idDuplicate', module.id);
    ids.add(module.id);
    requireText(module.label, MAXIMUM_TEXT_BYTES, 'studio.module.labelInvalid', module.id);
    requireText(module.title, MAXIMUM_TEXT_BYTES, 'studio.module.titleInvalid', module.id);
    if (module.description !== undefined) requireText(module.description, MAXIMUM_SUMMARY_BYTES, 'studio.module.descriptionInvalid', module.id);
    if (!Array.isArray(module.routes) || module.routes.length === 0 || module.routes.length > MAXIMUM_ROUTES) {
      throw new StudioCompositionError('studio.module.routesInvalid', module.id);
    }
    if (module.navItems !== undefined && (!Array.isArray(module.navItems) || module.navItems.length > MAXIMUM_NAV_ITEMS)) {
      throw new StudioCompositionError('studio.module.navigationInvalid', module.id);
    }
    const routes = module.routes.map((route) => {
      const path = normalizeRoute(route.path);
      if (owners.has(path)) throw new StudioCompositionError('studio.route.ownershipConflict', path);
      owners.set(path, module.id);
      if (!route.component) throw new StudioCompositionError('studio.route.componentMissing', module.id);
      requireText(route.title, MAXIMUM_TEXT_BYTES, 'studio.route.titleInvalid', module.id);
      requireText(route.summary, MAXIMUM_SUMMARY_BYTES, 'studio.route.summaryInvalid', module.id);
      if (route.eyebrow !== undefined) requireText(route.eyebrow, MAXIMUM_TEXT_BYTES, 'studio.route.eyebrowInvalid', module.id);
      return Object.freeze({ ...route, path, moduleId: module.id, context: null! }) as StudioRuntimeRoute;
    }).sort((left, right) => left.path.localeCompare(right.path));
    const routePaths = new Set(routes.map((route) => route.path));
    const navPaths = new Set<string>();
    const navItems = (module.navItems ?? []).map((item) => {
      const path = normalizeRoute(item.path);
      if (!routePaths.has(path)) throw new StudioCompositionError('studio.navigation.routeNotOwned', module.id);
      if (navPaths.has(path)) throw new StudioCompositionError('studio.navigation.pathDuplicate', module.id);
      navPaths.add(path);
      requireText(item.label, MAXIMUM_TEXT_BYTES, 'studio.navigation.labelInvalid', module.id);
      if (item.summary !== undefined) requireText(item.summary, MAXIMUM_SUMMARY_BYTES, 'studio.navigation.summaryInvalid', module.id);
      return Object.freeze({ ...item, path });
    }).sort((left, right) => left.path.localeCompare(right.path));
    const runtimeModule: StudioRuntimeModule = Object.freeze({
      id: module.id,
      label: module.label,
      title: module.title,
      description: module.description,
      requirement: registration.requirement,
      routes: Object.freeze(routes),
      navItems: Object.freeze(navItems)
    });
    return { registration, module: runtimeModule, configuration: cloneConfiguration(registration.configuration) };
  });
  return prepared;
}

function withContexts(module: StudioRuntimeModule, context: StudioRuntimeRoute['context']): StudioRuntimeModule {
  return Object.freeze({
    ...module,
    routes: Object.freeze(module.routes.map((route) => Object.freeze({ ...route, context })))
  });
}

function createRuntime(
  options: ComposeStudioOptions,
  authentication: ComposeStudioOptions['authentication'],
  active: ActiveModule[],
  quarantined: readonly StudioQuarantinedModule[]
): StudioRuntime {
  const modules = Object.freeze(active.map((item) => item.module));
  const routes = Object.freeze(modules.flatMap((module) => module.routes).sort((a, b) => a.path.localeCompare(b.path)));
  const byPath = new Map(routes.map((route) => [route.path, route]));
  const fallback = Object.freeze({ route: null, requestedPath: '/', isFallback: true });
  let current: StudioRouteObservation = routes[0]
    ? Object.freeze({ route: routes[0], requestedPath: routes[0].path, isFallback: false })
    : fallback;
  let disposed = false;
  const listeners = new Set<(value: StudioRouteObservation) => void>();
  const runtime: StudioRuntime = {
    configuration: Object.freeze({ ...options.configuration }),
    authentication,
    modules,
    quarantinedModules: quarantined,
    routes,
    get current() { return current; },
    navigate(path: string) {
      if (disposed) return fallback;
      let normalized: string | null = null;
      try { normalized = normalizeRoute(path); } catch { /* safe fallback */ }
      const route = normalized ? byPath.get(normalized) : undefined;
      current = route
        ? Object.freeze({ route, requestedPath: route.path, isFallback: false })
        : Object.freeze({ route: null, requestedPath: '/', isFallback: true });
      for (const listener of listeners) listener(current);
      return current;
    },
    subscribe(listener) {
      if (disposed) return () => {};
      listeners.add(listener);
      listener(current);
      let subscribed = true;
      return () => {
        if (!subscribed) return;
        subscribed = false;
        listeners.delete(listener);
      };
    },
    async dispose() {
      if (disposed) return;
      disposed = true;
      listeners.clear();
      await disposeActive(active);
      current = fallback;
    }
  };
  return Object.freeze(runtime);
}

class LifecycleScope implements StudioLifecycle {
  readonly #controller = new AbortController();
  readonly #disposers: Array<() => void | Promise<void>> = [];
  #disposed = false;
  get signal(): AbortSignal { return this.#controller.signal; }
  defer(dispose: () => void | Promise<void>): void {
    if (this.#disposed || typeof dispose !== 'function') throw new StudioCompositionError('studio.lifecycle.unavailable');
    if (this.#disposers.length >= MAXIMUM_RESOURCES) throw new StudioCompositionError('studio.lifecycle.capacityExceeded');
    this.#disposers.push(dispose);
  }
  trackAbortController(controller = new AbortController()): AbortController {
    this.defer(() => controller.abort());
    return controller;
  }
  setInterval(callback: () => void, milliseconds: number): number {
    if (!Number.isInteger(milliseconds) || milliseconds < 100 || milliseconds > 86_400_000) {
      throw new StudioCompositionError('studio.lifecycle.intervalInvalid');
    }
    const handle = globalThis.setInterval(callback, milliseconds) as unknown as number;
    this.defer(() => globalThis.clearInterval(handle));
    return handle;
  }
  listen(target: Pick<EventTarget, 'addEventListener' | 'removeEventListener'>, type: string, listener: EventListener): void {
    if (!target || !ID.test(type) || typeof listener !== 'function') throw new StudioCompositionError('studio.lifecycle.listenerInvalid');
    target.addEventListener(type, listener);
    this.defer(() => target.removeEventListener(type, listener));
  }
  async dispose(): Promise<void> {
    if (this.#disposed) return;
    this.#disposed = true;
    this.#controller.abort();
    for (const dispose of this.#disposers.reverse()) {
      try { await dispose(); } catch { /* failure-isolated cleanup */ }
    }
    this.#disposers.length = 0;
  }
}

async function disposeActive(active: ActiveModule[]): Promise<void> {
  for (const item of [...active].reverse()) {
    await item.scope.dispose();
    item.clearContext();
  }
  active.length = 0;
}

function normalizeRoute(value: string): string {
  if (typeof value !== 'string' || value.length === 0 || value.length > 256 || !value.startsWith('/') ||
      /[?#%\\:*{}]/.test(value)) throw new StudioCompositionError('studio.route.pathInvalid');
  const segments = value.split('/').filter(Boolean);
  if (segments.length === 0 || segments.some((segment) => segment.length > 64 || !SEGMENT.test(segment))) {
    throw new StudioCompositionError('studio.route.pathInvalid');
  }
  return `/${segments.join('/')}`;
}

function requireId(value: string, code: string): void {
  if (typeof value !== 'string' || value.length > 64 || !/^[a-z0-9]+(?:-[a-z0-9]+)*$/.test(value)) {
    throw new StudioCompositionError(code, value);
  }
}

function requireText(value: string, maximumBytes: number, code: string, moduleId: string): void {
  if (typeof value !== 'string' || value.trim().length === 0 || new TextEncoder().encode(value).length > maximumBytes || /[\u0000-\u001f\u007f-\u009f]/.test(value)) {
    throw new StudioCompositionError(code, moduleId);
  }
}

function validateShell(options: ComposeStudioOptions): void {
  if (!options || !options.authentication || typeof options.authentication.snapshot !== 'function' ||
      typeof options.authentication.subscribe !== 'function' || !options.configuration ||
      !['development', 'read-only'].includes(options.configuration.mode)) {
    throw new StudioCompositionError('studio.shell.configurationInvalid');
  }
  requireText(options.configuration.productTitle, MAXIMUM_TEXT_BYTES, 'studio.shell.titleInvalid', 'shell');
  if (options.configuration.apiBasePath !== undefined && new TextEncoder().encode(options.configuration.apiBasePath).length > 256) {
    throw new StudioCompositionError('studio.shell.apiBasePathInvalid');
  }
}

function sealAuthentication(service: ComposeStudioOptions['authentication']): ComposeStudioOptions['authentication'] {
  const project = () => {
    const value = service.snapshot();
    if (!value || typeof value.isAuthenticated !== 'boolean') {
      throw new StudioCompositionError('studio.authentication.snapshotInvalid');
    }
    if (value.displayName !== undefined) requireText(value.displayName, MAXIMUM_TEXT_BYTES, 'studio.authentication.displayNameInvalid', 'authentication');
    if (value.subjectHint !== undefined) requireText(value.subjectHint, MAXIMUM_TEXT_BYTES, 'studio.authentication.subjectHintInvalid', 'authentication');
    return Object.freeze({
      isAuthenticated: value.isAuthenticated,
      ...(value.displayName === undefined ? {} : { displayName: value.displayName }),
      ...(value.subjectHint === undefined ? {} : { subjectHint: value.subjectHint })
    });
  };
  project();
  return Object.freeze({
    snapshot: project,
    subscribe(listener: Parameters<typeof service.subscribe>[0]) {
      if (typeof listener !== 'function') throw new StudioCompositionError('studio.authentication.listenerInvalid');
      const unsubscribe = service.subscribe(() => listener(project()));
      if (typeof unsubscribe !== 'function') throw new StudioCompositionError('studio.authentication.subscriptionInvalid');
      let active = true;
      return () => {
        if (!active) return;
        active = false;
        unsubscribe();
      };
    },
    ...(service.beginSignIn ? { beginSignIn: () => service.beginSignIn!() } : {}),
    ...(service.beginSignOut ? { beginSignOut: () => service.beginSignOut!() } : {})
  });
}

function materializeBounded<T>(source: Iterable<T>, maximum: number, code: string): T[] {
  if (!source || typeof source[Symbol.iterator] !== 'function') throw new StudioCompositionError(code);
  const values: T[] = [];
  for (const value of source) {
    if (values.length === maximum) throw new StudioCompositionError(code);
    values.push(value);
  }
  return values;
}

function cloneConfiguration(value: Readonly<Record<string, StudioJson>> | undefined): Readonly<Record<string, StudioJson>> {
  const remaining = { nodes: 1_024 };
  const cloned = cloneJson(value ?? {}, 0, remaining) as Record<string, StudioJson>;
  if (new TextEncoder().encode(JSON.stringify(cloned)).length > MAXIMUM_CONFIGURATION_BYTES) {
    throw new StudioCompositionError('studio.module.configurationTooLarge');
  }
  return cloned;
}

function cloneJson(value: StudioJson, depth: number, remaining: { nodes: number }): StudioJson {
  if (depth > 8 || --remaining.nodes < 0) throw new StudioCompositionError('studio.module.configurationInvalid');
  if (value === null || typeof value === 'boolean' || typeof value === 'string') return value;
  if (typeof value === 'number') {
    if (!Number.isFinite(value)) throw new StudioCompositionError('studio.module.configurationInvalid');
    return value;
  }
  if (Array.isArray(value)) return Object.freeze(value.map((item) => cloneJson(item, depth + 1, remaining)));
  if (typeof value !== 'object' || Object.getPrototypeOf(value) !== Object.prototype) {
    throw new StudioCompositionError('studio.module.configurationInvalid');
  }
  const entries = Object.entries(value);
  if (entries.length > 64) throw new StudioCompositionError('studio.module.configurationInvalid');
  const result: Record<string, StudioJson> = {};
  for (const [key, item] of entries.sort(([left], [right]) => left.localeCompare(right))) {
    requireId(key, 'studio.module.configurationKeyInvalid');
    result[key] = cloneJson(item, depth + 1, remaining);
  }
  return Object.freeze(result);
}
