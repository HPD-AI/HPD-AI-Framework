import BaseModulePlaceholder from './BaseModulePlaceholder.svelte';
import type { StudioModule } from './types';

export const baseStudioModule: StudioModule = {
  id: 'base',
  label: 'BASE',
  title: 'HPD BASE Studio',
  description: 'Backend data, storage, realtime, policy, and diagnostics surface.',
  status: 'active',
  capabilities: [
    'base',
    'records',
    'collections',
    'schemas',
    'stores',
    'files',
    'realtime',
    'policy',
    'health',
    'diagnostics'
  ],
  navItems: [],
  routes: [
    {
      path: '/base',
      component: BaseModulePlaceholder,
      title: 'BASE',
      eyebrow: 'HPD BASE Studio',
      summary: 'BASE module is active; record, storage, realtime, policy, and diagnostic surfaces are ready to be shaped.'
    }
  ]
};
