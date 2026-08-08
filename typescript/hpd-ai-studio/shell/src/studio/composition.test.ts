import { describe, expect, it } from 'vitest';
import { composeStudio, type StudioAuthenticationService, type StudioModule, type StudioModuleRegistration } from '@hpd-research/hpd-studio-core';
import { agentStudioModule } from '@hpd-research/hpd-agent-studio';
import { authStudioModule } from '@hpd-research/hpd-auth-studio';
import { baseStudioModule } from '@hpd-research/hpd-base-studio';
import { graphStudioModule } from '@hpd-research/hpd-graph-studio';
import { mlStudioModule } from '@hpd-research/hpd-ml-studio';
import { ragStudioModule } from '@hpd-research/hpd-rag-studio';

const modules: readonly StudioModule[] = [
  agentStudioModule,
  authStudioModule,
  baseStudioModule,
  graphStudioModule,
  mlStudioModule,
  ragStudioModule
];
const authentication: StudioAuthenticationService = {
  snapshot: () => ({ isAuthenticated: false }),
  subscribe: () => () => {}
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
});
