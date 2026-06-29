import AuthModulePlaceholder from './AuthModulePlaceholder.svelte';
import type { StudioModule } from './types';

export const authStudioModule: StudioModule = {
  id: 'auth',
  label: 'Auth',
  title: 'HPD Auth Studio',
  description: 'Identity and access surface. The internal page structure is intentionally unset.',
  status: 'active',
  capabilities: ['auth', 'identity', 'access-control'],
  navItems: [],
  routes: [
    {
      path: '/auth',
      component: AuthModulePlaceholder,
      title: 'Auth',
      eyebrow: 'HPD Auth Studio',
      summary: 'Auth module is active; page scaffolding is intentionally paused.'
    }
  ]
};
