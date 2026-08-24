import GraphRegisteredPage from './GraphRegisteredPage.svelte';
import {
  computeStudioFrontendAbiChecksum,
  defineStudioModuleDescriptor,
  studioClientId,
  studioPageId,
  studioSha256,
  validateStudioModuleActivation,
  validateStudioOutwardResource,
  type StudioOutwardResourceAuthority,
  type StudioModuleActivation,
  type StudioModuleActivationContext,
  type StudioFrameworkClientHostContext,
  type StudioPageComponentBinding,
  type StudioPageId
} from '@hpd-research/hpd-studio-core';

const runtimeAbiChecksum = studioSha256('75e580b84bd3267ad7177ace62def3be7c611a9df94f6bab2f95d69e729d8304');
const generatedContractChecksum = studioSha256('20dcd5dd905fb5f1f00d9ff04e9d18406903badcfcb43fcea24ab090cfb2a792');
const operationInventoryChecksum = studioSha256('acb04f05700c7af25c0ea6cc76c536334f89998249298a169abf743f8d6fc992');
const componentAbiChecksum = studioSha256('0e356515536da0406864966c232dd2c25c3e48fd5eb779cb0f969eb78432919d');
const pageIds = [
  'graph.checkpoint.detail', 'graph.definition.detail', 'graph.execution.detail',
  'graph.executions', 'graph.overview', 'graph.topology.detail'
] as const;

type GraphDefinitionResource = StudioOutwardResourceAuthority & Readonly<{ kind: 'graphDefinition'; graphId: string; graphVersion: string }>;
type GraphExecutionResource = StudioOutwardResourceAuthority & Readonly<{ kind: 'graphExecution'; graphId: string; graphVersion: string; executionId: string }>;
type GraphCheckpointResource = StudioOutwardResourceAuthority & Readonly<{ kind: 'graphCheckpoint'; graphId: string; graphVersion: string; executionId: string; checkpointId: string }>;
function definition(value: GraphDefinitionResource): GraphDefinitionResource { const owned = validateStudioOutwardResource(value); if (owned.kind !== 'graphDefinition') throw new TypeError('Graph definition authority is required.'); return owned as GraphDefinitionResource; }
function execution(value: GraphExecutionResource): GraphExecutionResource { const owned = validateStudioOutwardResource(value); if (owned.kind !== 'graphExecution') throw new TypeError('Graph execution authority is required.'); return owned as GraphExecutionResource; }
function checkpoint(value: GraphCheckpointResource): GraphCheckpointResource { const owned = validateStudioOutwardResource(value); if (owned.kind !== 'graphCheckpoint') throw new TypeError('Graph checkpoint authority is required.'); return owned as GraphCheckpointResource; }

const pageComponents = Object.freeze(Object.fromEntries(pageIds.map(id => {
  const pageId = studioPageId(id);
  const binding: StudioPageComponentBinding = Object.freeze({
    componentExportId: `component.${id}`,
    componentAbiChecksum,
    component: GraphRegisteredPage
  });
  return [pageId, binding] as const;
}))) as Readonly<Record<StudioPageId, StudioPageComponentBinding>>;

const clientSlots = Object.freeze([Object.freeze({
  clientId: studioClientId('graph.control-plane'),
  version: 1,
  staticRuntimeAbiChecksum: runtimeAbiChecksum, protocol: 'frameworkGeneratedContractV1' as const,
  generatedContractChecksum, operationInventoryChecksum,
  endpointSurfaceId: 'graph.control-plane.v1', transportClass: 'sameOriginShellAuthenticated' as const,
  owningPageIds: Object.freeze(pageIds.map(studioPageId)), limitsChecksum: studioSha256('34e2556104951a2ac458ba0e4c553f210fab9a0b0158227fb432a2bcd448d1a8')
})]);

const frontendAbiChecksum = computeStudioFrontendAbiChecksum('graph', 1, clientSlots, pageComponents);

/** Static, authorization-neutral HPD Graph Studio frontend contribution. */
export const studioModuleDescriptor = defineStudioModuleDescriptor({
  moduleId: 'graph', moduleVersion: 1, frontendAbiChecksum, clientSlots, pageComponents
});

/** Host-only activator for the sealed generated Graph ControlPlane client. */
export const studioFrameworkClientActivators = Object.freeze([Object.freeze({
  clientId: studioClientId('graph.control-plane'), version: 1, runtimeAbiChecksum, generatedContractChecksum, operationInventoryChecksum,
  async create(context: StudioFrameworkClientHostContext) {
    const get = async (operation: string, path: string, signal = context.signal): Promise<unknown> => {
      const response = await context.transport.execute({ operation, method: 'GET', relativePathAndQuery: path,
        headers: Object.freeze({}), body: undefined, maximumResponseBytes: context.limits.maximumResponseBytes,
        purpose: 'observation', deadlineMilliseconds: context.limits.operationDeadlineMilliseconds, signal });
      if (!response.ok) throw new TypeError('Graph Studio observation is unavailable.');
      return response.json();
    };
    const client = Object.freeze({
      listDefinitions: (signal?: AbortSignal) => get('graph.definition.list', 'definitions', signal),
      getDefinition: (input: GraphDefinitionResource, signal?: AbortSignal) => { const value = definition(input); return get('graph.definition.get', `definitions/${encodeURIComponent(value.graphId)}`, signal); },
      listExecutions: (input: GraphDefinitionResource, signal?: AbortSignal) => { const value = definition(input); return get('graph.execution.list', `definitions/${encodeURIComponent(value.graphId)}/executions`, signal); },
      getExecution: (input: GraphExecutionResource, signal?: AbortSignal) => { const value = execution(input); return get('graph.execution.get', `definitions/${encodeURIComponent(value.graphId)}/executions/${encodeURIComponent(value.executionId)}`, signal); },
      getSuspendedNodes: (input: GraphExecutionResource, signal?: AbortSignal) => { const value = execution(input); return get('graph.execution.suspendedNodes', `definitions/${encodeURIComponent(value.graphId)}/executions/${encodeURIComponent(value.executionId)}/suspended-nodes`, signal); },
      getCheckpoint: (input: GraphCheckpointResource, signal?: AbortSignal) => { const value = checkpoint(input); return get('graph.checkpoint.get', `definitions/${encodeURIComponent(value.graphId)}/executions/${encodeURIComponent(value.executionId)}/checkpoints/${encodeURIComponent(value.checkpointId)}`, signal); }
    });
    return Object.freeze({ client, dispose(): void {} });
  }
})]);

/** Activates one principal-generation-bound Graph Studio module instance. */
export async function activateStudioModule(context: StudioModuleActivationContext): Promise<StudioModuleActivation> {
  validateStudioModuleActivation(studioModuleDescriptor, context);
  let disposed = false;
  return Object.freeze({
    moduleId: 'graph', moduleVersion: 1, frontendAbiChecksum,
    async dispose(): Promise<void> { if (!disposed) disposed = true; }
  });
}
