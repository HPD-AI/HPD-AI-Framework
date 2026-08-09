import type { GatewayClient } from '@hpd/gateway-client';
import type { StudioModule, StudioModuleInitialization } from '@hpd-research/hpd-studio-core';
import GatewayOverview from './GatewayOverview.svelte';
import GatewayConfigure from './GatewayConfigure.svelte';
import { createGatewayStudioController } from './state.ts';
import { createGatewayDeclarationController } from './declaration-state.ts';
import { createGatewayQuickRouteCoordinator } from './quick-route.ts';

export interface GatewayStudioModuleOptions {
  readonly client: GatewayClient;
  readonly now?: () => Date;
  readonly isVisible?: () => boolean;
}

export function createGatewayStudioModule(options: GatewayStudioModuleOptions): StudioModule {
  if (!options || !options.client) throw new TypeError('Gateway Studio requires an HPD Gateway client.');
  return Object.freeze({
    id: 'gateway',
    label: 'Gateway',
    title: 'HPD Gateway Studio',
    description: 'Governed Gateway lifecycle, configuration, operation, and diagnosis.',
    navItems: [{ path: '/gateway', label: 'Gateway', summary: 'Gateway operational truth' },{path:'/gateway/configure',label:'Configure',summary:'Author and validate one complete candidate'}],
    routes: [{
      path: '/gateway',
      component: GatewayOverview,
      title: 'Gateway Overview',
      eyebrow: 'HPD Gateway Studio',
      summary: 'Outcome-first desired, delivered, active, and effective truth.'
    },{path:'/gateway/configure',component:GatewayConfigure,title:'Gateway Configure',eyebrow:'HPD Gateway Studio',summary:'Lossless complete-document authoring and validation.'}],
    initialize(context: StudioModuleInitialization) {
      const controller = createGatewayStudioController({
        client: options.client,
        authentication: context.authentication,
        lifecycle: context.lifecycle,
        now: options.now,
        isVisible: options.isVisible
      });
      let principalGeneration=0n;
      let prior=context.authentication.snapshot();
      const declaration=createGatewayDeclarationController({client:options.client,hostCapabilityIdentity:()=>{const snapshot=controller.snapshot();const host=snapshot.observation?.hostCapabilities;return host?.state==='value'&&host.value?{algorithm:host.value.snapshotAlgorithm,value:host.value.snapshotValue}:null;}});
      const quickRoute=createGatewayQuickRouteCoordinator({declaration,client:options.client,principalGeneration:()=>principalGeneration,capabilityIdentity:()=>{const snapshot=controller.snapshot();const host=snapshot.observation?.hostCapabilities;return host?.state==='value'&&host.value?{algorithm:host.value.snapshotAlgorithm,value:host.value.snapshotValue}:null;}});
      const authUnsubscribe=context.authentication.subscribe(next=>{const changed=!next.isAuthenticated||!prior.isAuthenticated||next.subjectHint===undefined||prior.subjectHint===undefined||next.subjectHint!==prior.subjectHint;if(changed){principalGeneration++;quickRoute.cancel();declaration.clearPrincipal();}prior=next;});
      context.contexts.set('gateway-controller', controller);
      context.contexts.set('gateway-declaration-controller',declaration);
      context.contexts.set('gateway-quick-route-coordinator',quickRoute);
      context.lifecycle.defer(() => context.contexts.delete('gateway-controller'));
      context.lifecycle.defer(() => context.contexts.delete('gateway-declaration-controller'));
      context.lifecycle.defer(() => context.contexts.delete('gateway-quick-route-coordinator'));
      return { dispose: () => {authUnsubscribe();quickRoute.dispose();declaration.dispose();controller.dispose();} };
    }
  });
}
