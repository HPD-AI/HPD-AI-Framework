import type { GatewayClient } from '@hpd/gateway-client';
import { gatewayOperationInventoryChecksum, gatewayOperations } from '@hpd/gateway-client';
import { createGatewayStudioClient, type GatewayStudioClientHostContext } from '@hpd/gateway-client/studio-host';
import {
  computeStudioFrontendAbiChecksum,
  defineStudioModuleDescriptor,
  studioClientId,
  studioPageId,
  studioSha256,
  validateStudioModuleActivation,
  type StudioAuthenticationService,
  type StudioLifecycle,
  type StudioModuleActivation,
  type StudioModuleActivationContext,
  type StudioPageComponentBinding,
  type StudioPageId
} from '@hpd-research/hpd-studio-core';
import GatewayOverview from './GatewayOverview.svelte';
import GatewayConfigure from './GatewayConfigure.svelte';
import GatewayOperate from './GatewayOperate.svelte';
import GatewayDiagnose from './GatewayDiagnose.svelte';
import { createGatewayStudioController } from './state.ts';
import { createGatewayDeclarationController } from './declaration-state.ts';
import { createGatewayQuickRouteCoordinator } from './quick-route.ts';
import { createGatewayManagedWorkflowController } from './managed-workflows.ts';
import { createGatewayOperationsController } from './operations.ts';
import { installGatewayRuntimeContext, type GatewayRuntimeContext } from './runtime-context.ts';

const clientId = studioClientId('gateway.admin');
const runtimeAbiChecksum = studioSha256('da2abd35244cc5ceebe99d49ee50941100b09939c556515661a6942981879c8f');
const generatedContractChecksum = studioSha256('02c406f8c49752d24278f14e4db91694c8e84bf8ff2ef37b2e3feed81cdb21f7');
const operationInventoryChecksum = studioSha256(gatewayOperationInventoryChecksum);
const componentAbiChecksum = studioSha256('e1a32da3c6dea7d690c8380873ddf4225687df4a220de4038ad1c0137e53f0a3');
const pageDefinitions = [
  ['gateway.configure', 'component.gateway.configure', GatewayConfigure],
  ['gateway.diagnose', 'component.gateway.diagnose', GatewayDiagnose],
  ['gateway.operate', 'component.gateway.operate', GatewayOperate],
  ['gateway.overview', 'component.gateway.overview', GatewayOverview]
] as const;
const pageComponents = Object.freeze(Object.fromEntries(pageDefinitions.map(([id, exportId, component]) => {
  const binding: StudioPageComponentBinding = Object.freeze({
    componentExportId: exportId as string,
    componentAbiChecksum,
    component: component as unknown as StudioPageComponentBinding['component']
  });
  return [studioPageId(id as string), binding] as const;
}))) as Readonly<Record<StudioPageId, StudioPageComponentBinding>>;
const clientSlots = Object.freeze([Object.freeze({ clientId, version: 1, staticRuntimeAbiChecksum: runtimeAbiChecksum,
  protocol: 'frameworkGeneratedContractV1' as const, generatedContractChecksum, operationInventoryChecksum,
  endpointSurfaceId: 'gateway.admin.v1', transportClass: 'sameOriginShellAuthenticated' as const,
  owningPageIds: Object.freeze(pageDefinitions.map(([id]) => studioPageId(id))),
  limitsChecksum: studioSha256('99fc0b8491cf94f0c171fbfa337821f84f456f349cdf12b1ff571d8c60227e75') })]);
const frontendAbiChecksum = computeStudioFrontendAbiChecksum('gateway', 1, clientSlots, pageComponents);

/** Static, authorization-neutral Gateway Studio frontend contribution. */
export const studioModuleDescriptor = defineStudioModuleDescriptor({
  moduleId: 'gateway',
  moduleVersion: 1,
  frontendAbiChecksum,
  clientSlots,
  pageComponents
});

/** Host-only generated-client activator consumed by the trusted Studio shell before module activation. */
export const studioFrameworkClientActivators = Object.freeze([Object.freeze({
  clientId,
  version: 1,
  runtimeAbiChecksum,
  generatedContractChecksum,
  operationInventoryChecksum,
  async create(context: GatewayStudioClientHostContext): Promise<Readonly<{ readonly client: GatewayClient; dispose(): void }>> {
    const client = createGatewayStudioClient(context);
    let disposed = false;
    return Object.freeze({ client, dispose(): void { disposed = true; void disposed; } });
  }
})]);

/** Activates Gateway semantics over the shell-owned, principal-generation-bound generated client. */
export async function activateStudioModule(context: StudioModuleActivationContext): Promise<StudioModuleActivation> {
  validateStudioModuleActivation(studioModuleDescriptor, context);
  const binding = context.clients.get(clientId);
  if (!binding || !isGatewayClient(binding.client)) throw new TypeError('Gateway Studio generated-client authority is unavailable.');
  const lifecycle = createLifecycle(context);
  const authentication = createActivationAuthentication(binding.generatedContractChecksum);
  const controller = createGatewayStudioController({ client: binding.client, authentication, lifecycle });
  let principalGeneration = 1n;
  const declaration = createGatewayDeclarationController({ client: binding.client, hostCapabilityIdentity: () => {
    const host = controller.snapshot().observation?.hostCapabilities;
    return host?.state === 'value' && host.value ? { algorithm: host.value.snapshotAlgorithm, value: host.value.snapshotValue } : null;
  }});
  const workflows = createGatewayManagedWorkflowController({ client: binding.client, studio: controller, declaration, authentication });
  const operations = createGatewayOperationsController({ client: binding.client, studio: controller, managed: workflows, authentication });
  const quickRoute = createGatewayQuickRouteCoordinator({ declaration, client: binding.client,
    principalGeneration: () => principalGeneration, capabilityIdentity: () => {
      const host = controller.snapshot().observation?.hostCapabilities;
      return host?.state === 'value' && host.value ? { algorithm: host.value.snapshotAlgorithm, value: host.value.snapshotValue } : null;
    }});
  const runtime: GatewayRuntimeContext = Object.freeze({ controller, declaration, quickRoute, workflows, operations, observabilityLinks: Object.freeze([]) });
  const uninstall = installGatewayRuntimeContext(runtime);
  let disposed = false;
  return Object.freeze({
    moduleId: 'gateway', moduleVersion: 1, frontendAbiChecksum,
    async dispose(): Promise<void> {
      if (disposed) return;
      disposed = true; principalGeneration++; uninstall(); operations.dispose(); workflows.dispose();
      quickRoute.dispose(); declaration.dispose(); controller.dispose(); lifecycle.dispose();
    }
  });
}

function isGatewayClient(value: object): value is GatewayClient {
  const candidate = value as Record<string, unknown>;
  return gatewayOperations.length > 0 && gatewayOperations.every(operation => typeof candidate[operation.operation] === 'function');
}

function createActivationAuthentication(subjectHint: string): StudioAuthenticationService {
  const snapshot = Object.freeze({ isAuthenticated: true, subjectHint });
  return Object.freeze({ snapshot: () => snapshot, subscribe(listener: (value: typeof snapshot) => void): () => void {
    listener(snapshot); return () => undefined;
  }});
}

function createLifecycle(context: StudioModuleActivationContext): StudioLifecycle & { dispose(): void } {
  const disposers: Array<() => void> = [];
  const defer = (dispose: () => void | Promise<void>): void => {
    context.lifecycle.defer(dispose); disposers.push(() => { void dispose(); });
  };
  return Object.freeze({ signal: context.lifecycle.signal, defer,
    trackAbortController(controller = new AbortController()): AbortController {
      const abort = (): void => controller.abort(); context.lifecycle.signal.addEventListener('abort', abort, { once: true });
      defer(() => { context.lifecycle.signal.removeEventListener('abort', abort); controller.abort(); }); return controller;
    },
    setInterval(callback: () => void, milliseconds: number): number {
      const handle = globalThis.setInterval(callback, milliseconds); defer(() => globalThis.clearInterval(handle)); return handle;
    },
    listen(target: Pick<EventTarget, 'addEventListener' | 'removeEventListener'>, type: string, listener: EventListener): void {
      target.addEventListener(type, listener); defer(() => target.removeEventListener(type, listener));
    },
    dispose(): void { for (const dispose of disposers.splice(0).reverse()) dispose(); }
  });
}
