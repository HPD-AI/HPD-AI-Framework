import { describe, expect, it } from 'vitest';
import { composeStudio, type StudioAuthenticationService, type StudioModule, type StudioModuleRegistration } from '@hpd-research/hpd-studio-core';
import { agentStudioModule } from '@hpd-research/hpd-agent-studio';
import { authStudioModule } from '@hpd-research/hpd-auth-studio';
import { baseStudioModule } from '@hpd-research/hpd-base-studio';
import { graphStudioModule } from '@hpd-research/hpd-graph-studio';
import { createGatewayStudioModule } from '@hpd-research/hpd-gateway-studio';
import { createGatewayClient } from '@hpd/gateway-client';
import { mlStudioModule } from '@hpd-research/hpd-ml-studio';
import { ragStudioModule } from '@hpd-research/hpd-rag-studio';
import { resolveModuleContext } from '../../../../hpd-studio-core/src/context-internal.ts';

const modules: readonly StudioModule[] = [
  agentStudioModule,
  authStudioModule,
  baseStudioModule,
  graphStudioModule,
  mlStudioModule,
  ragStudioModule,
  createGatewayStudioModule({
    client: createGatewayClient({
      baseUrl: 'https://gateway.example',
      authentication: { getAccessToken: () => null },
      fetch: async () => { throw new Error('Unauthenticated conformance fixture must not fetch.'); }
    })
  })
];
const authentication: StudioAuthenticationService = {
  snapshot: () => ({ isAuthenticated: false }),
  subscribe: (listener) => {
    listener({ isAuthenticated: false });
    return () => {};
  }
};

async function compose(selected: readonly StudioModule[]) {
  const registrations: StudioModuleRegistration[] = selected.map((module) => ({ module, requirement: 'optional' }));
  return composeStudio({
    configuration: { productTitle: 'Fixture Studio', mode: 'development' },
    authentication,
    modules: registrations
  });
}

describe('placeholder module composition fixtures', () => {
  it('loads every module alone and permits every module to be omitted', async () => {
    for (const candidate of modules) {
      const alone = await compose([candidate]);
      expect(alone.modules.map((module) => module.id)).toEqual([candidate.id]);
      await alone.dispose();

      const omitted = await compose(modules.filter((module) => module.id !== candidate.id));
      expect(omitted.modules.some((module) => module.id === candidate.id)).toBe(false);
      await omitted.dispose();
    }
  });

  it('composes representative arbitrary subsets independent of registration order', async () => {
    const subsets = [
      [],
      [agentStudioModule, baseStudioModule],
      [authStudioModule, mlStudioModule, ragStudioModule],
      [...modules]
    ];
    for (const subset of subsets) {
      const forward = await compose(subset);
      const reverse = await compose([...subset].reverse());
      expect(forward.modules.map((module) => module.id)).toEqual(reverse.modules.map((module) => module.id));
      expect(forward.routes.map((route) => route.path)).toEqual(reverse.routes.map((route) => route.path));
      await forward.dispose();
      await reverse.dispose();
    }
  });

  it('retains the Gateway module-owned controller until disposal', async () => {
    const runtime = await compose([modules.find((module) => module.id === 'gateway')!]);
    const context = resolveModuleContext(runtime.routes[0]!.context);
    expect(context.get('gateway-controller')).toBeDefined();
    await runtime.dispose();
    expect(() => resolveModuleContext(runtime.routes[0]!.context)).toThrow();
  });
});
