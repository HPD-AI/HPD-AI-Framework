import MlModulePlaceholder from './MlModulePlaceholder.svelte';
import type { StudioModule } from '@hpd-research/hpd-studio-core';

export const mlStudioModule: StudioModule = {
  id: 'ml',
  label: 'ML',
  title: 'HPD ML Studio',
  description: 'Machine learning workbench surface. The internal page structure is intentionally unset.',
  navItems: [{ path: '/ml', label: 'ML' }],
  routes: [
    {
      path: '/ml',
      component: MlModulePlaceholder,
      title: 'ML',
      eyebrow: 'HPD ML Studio',
      summary: 'ML module is active; page scaffolding is intentionally paused.'
    }
  ]
};
