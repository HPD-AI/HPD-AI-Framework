import { describe, expect, it, vi } from 'vitest';
import { gatewayOperations, type GatewayClient } from '@hpd/gateway-client';
import {
  studioClientId, studioPageId, studioSha256,
  type StudioModuleActivationContext
} from '@hpd-research/hpd-studio-core';
import { activateStudioModule, studioModuleDescriptor } from '../src/module.ts';
import { requireGatewayRuntimeContext } from '../src/runtime-context.ts';

describe('Gateway L52 module activation', () => {
  it('locks the cross-language frontend ABI checksum', () => {
    expect(studioModuleDescriptor.frontendAbiChecksum).toBe(
      '0fbbcdb6092c657371b74781d6064ef257a99f51fd3a7aaf5012f2a2db3c3e81');
  });

  it('accepts only the sealed generated-client slot and destroys typed context on disposal', async () => {
    const disposers: Array<() => void | Promise<void>> = [];
    const client = Object.freeze(Object.fromEntries(gatewayOperations.map(operation =>
      [operation.operation, vi.fn(async () => ({ ok: false, kind: 'canceled' }))]))) as GatewayClient;
    const context: StudioModuleActivationContext = Object.freeze({
      moduleId: 'gateway', moduleVersion: 1,
      frontendAbiChecksum: studioModuleDescriptor.frontendAbiChecksum,
      disclosedPageIds: Object.freeze([
        studioPageId('gateway.configure'), studioPageId('gateway.diagnose'),
        studioPageId('gateway.operate'), studioPageId('gateway.overview')
      ]),
      clients: new Map([[studioClientId('gateway.admin'), Object.freeze({
        clientId: studioClientId('gateway.admin'), version: 1,
        protocol: 'frameworkGeneratedContractV1' as const,
        staticRuntimeAbiChecksum: studioModuleDescriptor.clientSlots[0]!.staticRuntimeAbiChecksum,
        generatedContractChecksum: studioSha256('02c406f8c49752d24278f14e4db91694c8e84bf8ff2ef37b2e3feed81cdb21f7'),
        operationInventoryChecksum: studioSha256('b577087395ac45ad1cd9ce74ca577ab6797591c5e7d5a564f31b826878f3b8bc'),
        endpointSurfaceId: 'gateway.admin.v1', transportClass: 'sameOriginShellAuthenticated' as const,
        owningPageIds: studioModuleDescriptor.clientSlots[0]!.owningPageIds,
        limitsChecksum: studioModuleDescriptor.clientSlots[0]!.limitsChecksum, client
      })]]),
      navigation: Object.freeze({ navigate: vi.fn() }),
      lifecycle: Object.freeze({ signal: new AbortController().signal,
        defer(dispose: () => void | Promise<void>): void { disposers.push(dispose); } })
    });

    const activation = await activateStudioModule(context);
    expect(requireGatewayRuntimeContext().controller).toBeDefined();
    await activation.dispose();
    expect(() => requireGatewayRuntimeContext()).toThrow(/unavailable/u);
    await Promise.all(disposers.map(dispose => dispose()));
  });

  it('rejects a substituted client object', async () => {
    const context = {
      moduleId: 'gateway', moduleVersion: 1, frontendAbiChecksum: studioModuleDescriptor.frontendAbiChecksum,
      disclosedPageIds: Object.freeze([]),
      clients: new Map([[studioClientId('gateway.admin'), Object.freeze({ clientId: studioClientId('gateway.admin'), version: 1,
        protocol: 'frameworkGeneratedContractV1' as const,
        staticRuntimeAbiChecksum: studioModuleDescriptor.clientSlots[0]!.staticRuntimeAbiChecksum,
        generatedContractChecksum: studioSha256('02c406f8c49752d24278f14e4db91694c8e84bf8ff2ef37b2e3feed81cdb21f7'),
        operationInventoryChecksum: studioSha256('b577087395ac45ad1cd9ce74ca577ab6797591c5e7d5a564f31b826878f3b8bc'),
        endpointSurfaceId: 'gateway.admin.v1', transportClass: 'sameOriginShellAuthenticated' as const,
        owningPageIds: studioModuleDescriptor.clientSlots[0]!.owningPageIds,
        limitsChecksum: studioModuleDescriptor.clientSlots[0]!.limitsChecksum, client: Object.freeze({}) })]]),
      navigation: Object.freeze({ navigate: vi.fn() }), lifecycle: Object.freeze({ signal: new AbortController().signal, defer: vi.fn() })
    } satisfies StudioModuleActivationContext;
    await expect(activateStudioModule(context)).rejects.toThrow(/authority/u);
  });
});
