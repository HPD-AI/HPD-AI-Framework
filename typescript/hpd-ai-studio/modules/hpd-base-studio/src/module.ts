import BaseModulePlaceholder from './BaseModulePlaceholder.svelte';
import BaseSemanticActivations from './BaseSemanticActivations.svelte';
import type { StudioModule } from '@hpd-research/hpd-studio-core';
import type { BaseSemanticActivationDefinitionShape } from '@hpd/base-client';
import { createBaseSemanticStudioController, type BaseSemanticInspectionClient } from './semantic-state.ts';

export interface BaseStudioModuleOptions {
  readonly client?: BaseSemanticInspectionClient;
  readonly semanticDefinitions?: Readonly<Record<string, BaseSemanticActivationDefinitionShape>>;
}

export function createBaseStudioModule(options: BaseStudioModuleOptions = {}): StudioModule {
  const semantic = options.client;
  const definitions = options.semanticDefinitions;
  const authority = semantic !== undefined && definitions !== undefined && Object.keys(definitions).length !== 0
    ? Object.freeze({ semantic, definitions }) : undefined;
  const module: StudioModule = {
  id: 'base',
  label: 'BASE',
  title: 'HPD BASE Studio',
  description: 'Backend data, storage, realtime, policy, and diagnostics surface.',
  navItems: [{ path: '/base', label: 'BASE' }, ...(authority === undefined ? [] : [{ path: '/base/semantic-activations', label: 'Semantic activations' }])],
  initialize({ contexts, lifecycle }) {
    if (authority === undefined) return;
    const controller = createBaseSemanticStudioController(authority.semantic, authority.definitions);
    contexts.set('base-semantic-controller', controller);
    lifecycle.defer(() => contexts.delete('base-semantic-controller'));
    return { dispose: () => controller.dispose() };
  },
  routes: [
    {
      path: '/base',
      component: BaseModulePlaceholder,
      title: 'BASE',
      eyebrow: 'HPD BASE Studio',
      summary: 'BASE module is active; record, storage, realtime, policy, and diagnostic surfaces are ready to be shaped.'
    },
    ...(authority === undefined ? [] : [{ path: '/base/semantic-activations', component: BaseSemanticActivations, title: 'Semantic activations', eyebrow: 'HPD BASE Studio', summary: 'Authorized bounded semantic activation inspection without raw key or provider-row disclosure.' }])
  ]
  };
  return Object.freeze(module);
}
