import MlModulePlaceholder from './MlModulePlaceholder.svelte';
import type { StudioModule } from './types';

export const mlStudioModule: StudioModule = {
  id: 'ml',
  label: 'ML',
  title: 'HPD ML Studio',
  description: 'Machine learning workbench surface. The internal page structure is intentionally unset.',
  status: 'active',
  capabilities: ['ml', 'models', 'training', 'evaluations'],
  navItems: [],
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
