import type { StudioMode, StudioShellConfiguration } from '@hpd-research/hpd-studio-core';

declare global {
  var HPD_STUDIO_CONFIG: Partial<StudioShellConfiguration> | undefined;
}

export function readRuntimeConfig(): StudioShellConfiguration {
  const supplied = globalThis.HPD_STUDIO_CONFIG ?? {};
  const mode: StudioMode = supplied.mode === 'read-only' ? 'read-only' : 'development';
  return Object.freeze({
    productTitle: typeof supplied.productTitle === 'string' && supplied.productTitle.trim()
      ? supplied.productTitle
      : 'HPD AI Platform',
    apiBasePath: typeof supplied.apiBasePath === 'string' ? supplied.apiBasePath : '/api/hpd',
    mode
  });
}
