import type { StudioModuleConfig, StudioRuntimeConfig } from '../types';

declare global {
  var HPD_AI_PLATFORM_CONFIG: Partial<StudioRuntimeConfig> | undefined;
}

export function readRuntimeConfig(): StudioRuntimeConfig {
  const fallback: StudioRuntimeConfig = {
    apiBasePath: '/api/hpd',
    routePrefix: '/studio',
    productTitle: 'HPD AI Platform',
    mode: 'development',
    capabilities: [],
    studioModules: []
  };

  const config: StudioRuntimeConfig = {
    ...fallback,
    ...(globalThis.HPD_AI_PLATFORM_CONFIG ?? {})
  };

  config.productTitle = config.productTitle || 'HPD AI Platform';
  config.capabilities = Array.isArray(config.capabilities) ? config.capabilities : fallback.capabilities;
  config.studioModules = mergeStudioModules(fallback.studioModules, config.studioModules);

  return config;
}

function mergeStudioModules(
  fallbackModules: StudioModuleConfig[],
  configuredModules: unknown
): StudioModuleConfig[] {
  const configured = Array.isArray(configuredModules) ? configuredModules : [];
  const byId = new Map(fallbackModules.map((module) => [module.id, module]));

  for (const module of configured) {
    if (!isStudioModuleConfig(module)) continue;
    byId.set(module.id, { ...(byId.get(module.id) ?? {}), ...module });
  }

  return [...byId.values()];
}

function isStudioModuleConfig(value: unknown): value is StudioModuleConfig {
  return (
    typeof value === 'object' &&
    value !== null &&
    'id' in value &&
    typeof value.id === 'string'
  );
}
