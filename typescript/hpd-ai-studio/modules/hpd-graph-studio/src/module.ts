import GraphModulePlaceholder from './GraphModulePlaceholder.svelte';
import type { StudioModule } from '@hpd-research/hpd-studio-core';

export const graphStudioModule: StudioModule = {
  id: 'workflows',
  label: 'Workflows',
  title: 'HPD Graph Studio',
  description: 'Graph and workflow runtime surface. The internal page structure is intentionally unset.',
  navItems: [{ path: '/workflows', label: 'Workflows' }],
  routes: [
    {
      path: '/workflows',
      component: GraphModulePlaceholder,
      title: 'Workflows',
      eyebrow: 'HPD Graph Studio',
      summary: 'Workflow module is active; page scaffolding is intentionally paused.'
    }
  ]
};
