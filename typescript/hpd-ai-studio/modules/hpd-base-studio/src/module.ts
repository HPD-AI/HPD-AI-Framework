import BaseRegisteredPage from './BaseRegisteredPage.svelte';
import BaseSemanticActivations from './BaseSemanticActivations.svelte';
import {
  computeStudioFrontendAbiChecksum,
  defineStudioModuleDescriptor,
  studioClientId,
  studioPageId,
  studioSha256,
  validateStudioModuleActivation,
  type StudioModuleActivation,
  type StudioModuleActivationContext,
  type StudioPageComponentBinding,
  type StudioPageId
} from '@hpd-research/hpd-studio-core';

const runtimeAbiChecksum = studioSha256('948ef656610dd748aa677b641e3b78802998eec58cd8cf5c249f48621f497088');
const componentAbiChecksum = studioSha256('e1a32da3c6dea7d690c8380873ddf4225687df4a220de4038ad1c0137e53f0a3');
const pageIds = [
  'base.activation.detail',
  'base.automation',
  'base.backup.detail',
  'base.collection.detail',
  'base.collection.records',
  'base.data',
  'base.diagnostic.detail',
  'base.diagnostics',
  'base.effect.detail',
  'base.executor.detail',
  'base.file.detail',
  'base.fileBucket.detail',
  'base.grant.detail',
  'base.health.detail',
  'base.infrastructure',
  'base.lifecycleConsumer.detail',
  'base.maintenance.detail',
  'base.migration.detail',
  'base.module.detail',
  'base.occurrence.detail',
  'base.operation.definition',
  'base.operation.execution',
  'base.operations',
  'base.overview',
  'base.policy.detail',
  'base.policy.explain',
  'base.provider.detail',
  'base.rebuild.detail',
  'base.receipt.detail',
  'base.record.detail',
  'base.restore.detail',
  'base.retirementBarrier.detail',
  'base.schedule.detail',
  'base.schema.detail',
  'base.search',
  'base.search.query',
  'base.security',
  'base.semanticActivations',
  'base.store.detail',
  'base.subject.detail',
  'base.subjectContract.detail',
  'base.subjects',
  'base.textIndex.detail',
  'base.vectorIndex.detail'
] as const;

const pageComponents = Object.freeze(Object.fromEntries(pageIds.map(id => {
  const pageId = studioPageId(id);
  const binding: StudioPageComponentBinding = Object.freeze({
    componentExportId: `component.${id}`,
    componentAbiChecksum,
    component: id === 'base.semanticActivations' ? BaseSemanticActivations : BaseRegisteredPage
  });
  return [pageId, binding] as const;
}))) as Readonly<Record<StudioPageId, StudioPageComponentBinding>>;

const clientSlots = Object.freeze([Object.freeze({
  clientId: studioClientId('base.control-plane'),
  version: 1,
  staticRuntimeAbiChecksum: runtimeAbiChecksum, protocol: 'baseL41DynamicMap' as const,
  generatedContractChecksum: studioSha256('c859fc38903585d37d039f373786971e12d5e96131be7cb4b99ea01aac0d7ec9'),
  operationInventoryChecksum: studioSha256('6013a23aa9795036b8b500d2e493dbeb8ca4aa30551b3fd315f0f95f50717c1b'),
  endpointSurfaceId: 'base.studio.runtime', transportClass: 'sameOriginShellAuthenticated' as const,
  owningPageIds: Object.freeze(pageIds.map(studioPageId)), limitsChecksum: studioSha256('f35181bd0910c0afb1f6a1e51c5a10e54ee58dde4e8fd69ec5990fac6e29c742')
})]);

const frontendAbiChecksum = computeStudioFrontendAbiChecksum('base', 1, clientSlots, pageComponents);

/** Static, authorization-neutral BASE Studio frontend contribution. */
export const studioModuleDescriptor = defineStudioModuleDescriptor({
  moduleId: 'base',
  moduleVersion: 1,
  frontendAbiChecksum,
  clientSlots,
  pageComponents
});

/** Activates one principal-generation-bound BASE Studio module instance. */
export async function activateStudioModule(context: StudioModuleActivationContext): Promise<StudioModuleActivation> {
  validateStudioModuleActivation(studioModuleDescriptor, context);
  let disposed = false;
  return Object.freeze({
    moduleId: 'base',
    moduleVersion: 1,
    frontendAbiChecksum,
    async dispose(): Promise<void> {
      if (disposed) return;
      disposed = true;
    }
  });
}
