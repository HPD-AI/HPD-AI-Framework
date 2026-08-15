import { afterEach, describe, expect, it } from 'vitest';
import {
  compiledShellContractIdentity,
  readRuntimeConfig,
  readRuntimeModuleIds
} from './runtimeConfig';

function validConfiguration(): NonNullable<typeof globalThis.HPD_STUDIO_CONFIG> {
  return {
    apiBasePath: '/management/gateway/v1',
    routePrefix: '/studio',
    productTitle: 'HPD Gateway Studio',
    mode: 'development',
    assetContractVersion: '1',
    assetIdentity: 'a'.repeat(64),
    shellContractIdentity: compiledShellContractIdentity,
    capabilities: ['gateway.management.status.read'],
    studioModules: [{ id: 'gateway', label: 'Gateway', title: 'HPD Gateway Studio', status: 'active' }]
  };
}

afterEach(() => {
  globalThis.HPD_STUDIO_CONFIG = undefined;
});

describe('governed runtime configuration', () => {
  it('accepts the complete correlated configuration', () => {
    globalThis.HPD_STUDIO_CONFIG = validConfiguration();
    expect(readRuntimeConfig()).toMatchObject({
      apiBasePath: '/management/gateway/v1',
      routePrefix: '/studio',
      productTitle: 'HPD Gateway Studio',
      mode: 'development',
      assetContractVersion: '1',
      assetIdentity: 'a'.repeat(64)
    });
    expect([...readRuntimeModuleIds()!]).toEqual(['gateway']);
  });

  it.each([
    ['missing configuration', undefined],
    ['missing identity', { ...validConfiguration(), assetIdentity: undefined }],
    ['wrong shell', { ...validConfiguration(), shellContractIdentity: 'b'.repeat(64) }],
    ['unknown mode', { ...validConfiguration(), mode: 'production' }],
    ['relative API base', { ...validConfiguration(), apiBasePath: 'management' }],
    ['backslash route', { ...validConfiguration(), routePrefix: '/studio\\bad' }],
    ['oversized title', { ...validConfiguration(), productTitle: 'x'.repeat(257) }],
    ['unknown member', { ...validConfiguration(), authorityToken: 'forbidden' }]
  ])('rejects %s', (_name, configuration) => {
    globalThis.HPD_STUDIO_CONFIG = configuration as typeof globalThis.HPD_STUDIO_CONFIG;
    expect(() => readRuntimeConfig()).toThrow();
  });

  it('rejects incomplete and unknown-member module records', () => {
    globalThis.HPD_STUDIO_CONFIG = {
      ...validConfiguration(),
      studioModules: [{ id: 'gateway', label: 'Gateway', title: 'Gateway', status: 'active', extra: true }]
    } as typeof globalThis.HPD_STUDIO_CONFIG;
    expect(() => readRuntimeModuleIds()).toThrow();
  });
});
