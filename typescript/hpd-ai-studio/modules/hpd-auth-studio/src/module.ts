import AuthModulePlaceholder from './AuthModulePlaceholder.svelte';
import type { StudioModule } from '@hpd-research/hpd-studio-core';

export const authStudioModule: StudioModule = {
  id: 'auth',
  label: 'Auth',
  title: 'HPD Auth Studio',
  description: 'Identity and access surface. The internal page structure is intentionally unset.',
  navItems: [{ path: '/auth', label: 'Auth' }],
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
