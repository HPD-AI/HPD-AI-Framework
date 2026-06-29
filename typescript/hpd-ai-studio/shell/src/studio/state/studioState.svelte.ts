import type {
  StudioController,
  StudioModule,
  StudioModuleCatalogItem,
  StudioNavItemWithModule,
  StudioRouteWithModule,
  StudioRuntimeConfig
} from '../types';

interface CreateStudioStateOptions {
  config: StudioRuntimeConfig;
  modules: StudioModule[];
}

export function createStudioState({ config, modules }: CreateStudioStateOptions): StudioController {
  const enabledModules = getEnabledModules(config, modules);
  const defaultModule = enabledModules.find((module) => module.status !== 'planned') ?? enabledModules[0];
  const defaultRoute = defaultModule?.routes[0];

  if (!defaultModule || !defaultRoute) {
    throw new Error('HPD AI Studio requires at least one module with one route.');
  }

  const defaultRouteWithModule: StudioRouteWithModule = { ...defaultRoute, module: defaultModule };

  const state = $state({
    config,
    modules: enabledModules,
    activeRoute: defaultRoute?.path ?? '/',
    apiStatus: 'configured',
    selection: {
      agentId: '',
      sessionId: '',
      threadId: '',
      runId: '',
      eventId: ''
    }
  });

  const routes: StudioRouteWithModule[] = $derived(
    enabledModules.flatMap((module) => module.routes.map((route) => ({ ...route, module })))
  );
  const currentRoute = $derived(resolveRoute(routes, state.activeRoute) ?? defaultRouteWithModule);
  const activeModule = $derived(currentRoute?.module ?? defaultModule);
  const moduleCatalog = $derived(createModuleCatalog(state.config, modules, activeModule));
  const navItems: StudioNavItemWithModule[] = $derived(
    (activeModule?.navItems ?? []).map((item) => ({ ...item, module: activeModule }))
  );

  return {
    state,
    get routes() {
      return routes;
    },
    get moduleCatalog() {
      return moduleCatalog;
    },
    get activeModule() {
      return activeModule;
    },
    get navItems() {
      return navItems;
    },
    get currentRoute() {
      return currentRoute;
    },
    get defaultRoute() {
      return defaultRoute;
    },
    navigate(path: string) {
      state.activeRoute = path;
      if (globalThis.location?.hash !== `#${path}`) {
        globalThis.location.hash = path;
      }
    },
    selectModule(moduleId: string) {
      const module = enabledModules.find((item) => item.id === moduleId);
      const route = module?.routes[0];
      if (route) {
        this.navigate(route.path);
      }
    },
    syncRouteFromLocation() {
      const path = normalizeHash(globalThis.location?.hash) ?? defaultRoute?.path ?? '/';
      state.activeRoute = resolveRoute(routes, path)?.path ?? defaultRoute?.path ?? '/';
    },
    selectAgent(agentId: string | null | undefined) {
      state.selection.agentId = agentId ?? '';
    },
    selectSession(sessionId: string | null | undefined) {
      state.selection.sessionId = sessionId ?? '';
    },
    selectThread(threadId: string | null | undefined) {
      state.selection.threadId = threadId ?? '';
    },
    can(capability: string) {
      return state.config.capabilities.includes(capability);
    },
    isReadOnly() {
      return state.config.mode === 'read-only';
    }
  };
}

function getEnabledModules(config: StudioRuntimeConfig, liveModules: StudioModule[]): StudioModule[] {
  const configuredModules = Array.isArray(config.studioModules) ? config.studioModules : [];
  const configuredIds = new Set(configuredModules.map((module) => module.id));
  return liveModules.filter((module) => configuredIds.has(module.id));
}

function createModuleCatalog(
  config: StudioRuntimeConfig,
  liveModules: StudioModule[],
  activeModule: StudioModule
): StudioModuleCatalogItem[] {
  const liveById = new Map(liveModules.map((module) => [module.id, module]));
  const configuredModules = Array.isArray(config.studioModules) ? config.studioModules : [];
  return configuredModules.map((module) => {
    const liveModule = liveById.get(module.id);

    return {
      ...module,
      ...(liveModule ?? null),
      status: liveModule?.status ?? module.status ?? 'planned',
      isActive: activeModule?.id === module.id,
      isLive: Boolean(liveModule)
    };
  });
}

function normalizeHash(hash: string | undefined): string | null {
  if (!hash || hash === '#') return null;
  const path = hash.replace(/^#/, '');
  return path.startsWith('/') ? path : `/${path}`;
}

function resolveRoute(routes: StudioRouteWithModule[], path: string): StudioRouteWithModule | undefined {
  return routes.find((route) => route.path === path);
}
