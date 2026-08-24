import type { Component } from 'svelte';
import type { StudioRouteMatch } from './route.ts';
import type { StudioOutwardResourceAuthority } from './resource.ts';
import type { StudioVisiblePage } from './bootstrap.ts';
import { studioCanonicalHash } from './canonical.ts';

export type StudioSha256 = string & { readonly __studioSha256: unique symbol };
export type StudioPageId = string & { readonly __studioPageId: unique symbol };
export type StudioClientId = string & { readonly __studioClientId: unique symbol };

export interface StudioFrontendClientSlot {
  readonly clientId: StudioClientId;
  readonly version: number;
  readonly staticRuntimeAbiChecksum: StudioSha256;
  readonly protocol: 'baseL41DynamicMap' | 'frameworkGeneratedContractV1';
  readonly generatedContractChecksum: StudioSha256;
  readonly operationInventoryChecksum: StudioSha256;
  readonly endpointSurfaceId: string;
  readonly transportClass: 'sameOriginShellAuthenticated';
  readonly owningPageIds: readonly StudioPageId[];
  readonly limitsChecksum: StudioSha256;
}

export type StudioResourceProjection = StudioOutwardResourceAuthority;

export const STUDIO_LINK_RELATIONS = Object.freeze(['owns', 'containedBy', 'affected', 'producedBy', 'receiptFor', 'scheduledBy',
  'occurrenceOf', 'attemptOf', 'childOf', 'references', 'lifecycleOf', 'blocks', 'acknowledgedBy', 'indexedBy', 'storedBy',
  'authorizedBy', 'diagnoses', 'remediates'] as const);
export type StudioLinkRelation = typeof STUDIO_LINK_RELATIONS[number];
export interface StudioLinkProjection { readonly target: StudioResourceProjection; readonly relation: StudioLinkRelation; readonly label: string; }

export interface StudioPageProps {
  readonly page: StudioVisiblePage;
  readonly route: StudioRouteMatch;
  readonly resource: StudioResourceProjection | null;
  readonly observation: unknown;
  readonly navigation: StudioNavigationHandle;
  readonly commands: StudioCommandHandle;
}

export type StudioPageComponent = Component<StudioPageProps>;

export interface StudioPageComponentBinding {
  readonly componentExportId: string;
  readonly componentAbiChecksum: StudioSha256;
  readonly component: StudioPageComponent;
}

export interface StudioClientBinding {
  readonly clientId: StudioClientId;
  readonly version: number;
  readonly staticRuntimeAbiChecksum: StudioSha256;
  readonly protocol: StudioFrontendClientSlot['protocol'];
  readonly generatedContractChecksum: StudioSha256;
  readonly operationInventoryChecksum: StudioSha256;
  readonly endpointSurfaceId: string;
  readonly transportClass: StudioFrontendClientSlot['transportClass'];
  readonly owningPageIds: readonly StudioPageId[];
  readonly limitsChecksum: StudioSha256;
  readonly client: object;
}

export interface StudioFrameworkClientActivator {
  readonly clientId: StudioClientId; readonly version: number; readonly runtimeAbiChecksum: StudioSha256;
  readonly generatedContractChecksum: StudioSha256; readonly operationInventoryChecksum: StudioSha256;
  create(context: StudioFrameworkClientHostContext): Promise<StudioFrameworkClientLease>;
}
export interface StudioFrameworkClientLease { readonly client: object; dispose(): void | Promise<void>; }
export interface StudioFrameworkClientTransportRequest { readonly operation: string; readonly method: string; readonly relativePathAndQuery: string;
  readonly headers: Readonly<Record<string, string>>; readonly body: string | undefined; readonly maximumResponseBytes: number;
  readonly purpose: 'observation' | 'commandPreview' | 'commandExecution' | 'receiptResolution' | 'artifactStaging';
  readonly deadlineMilliseconds: number; readonly signal: AbortSignal; }
export interface StudioFrameworkClientHostContext { readonly endpointSurfaceId: string; readonly principalGeneration: bigint;
  readonly authenticationSessionChecksum: StudioSha256; readonly signal: AbortSignal;
  readonly transport: Readonly<{ execute(request: StudioFrameworkClientTransportRequest): Promise<Response> }>;
  readonly limits: Readonly<{ maximumOperations: number; maximumRequestBytes: number; maximumResponseBytes: number;
    maximumConcurrentRequests: number; acquisitionDeadlineMilliseconds: number; operationDeadlineMilliseconds: number;
    disposalDeadlineMilliseconds: number }> }

export interface StudioModuleDescriptor {
  readonly moduleId: string;
  readonly moduleVersion: number;
  readonly frontendAbiChecksum: StudioSha256;
  readonly clientSlots: readonly StudioFrontendClientSlot[];
  readonly pageComponents: Readonly<Record<StudioPageId, StudioPageComponentBinding>>;
}

export interface StudioNavigationHandle {
  navigate(target: Readonly<{ readonly link: StudioLinkProjection; readonly viewId?: string }>): Promise<void>;
}

export interface StudioCommandHandle {
  open(commandId: string, target: StudioResourceProjection, input?: unknown): void;
  snapshot(): unknown;
  subscribe(listener: (state: unknown) => void): () => void;
  preview(signal?: AbortSignal): Promise<void>;
  acknowledge(acknowledgementId: string, accepted: boolean): void;
  execute(signal?: AbortSignal): Promise<void>;
  resolve(signal?: AbortSignal): Promise<void>;
  close(): void;
}

export interface StudioModuleLifecycle {
  readonly signal: AbortSignal;
  defer(dispose: () => void | Promise<void>): void;
}

export interface StudioModuleActivationContext {
  readonly moduleId: string;
  readonly moduleVersion: number;
  readonly frontendAbiChecksum: StudioSha256;
  readonly disclosedPageIds: readonly StudioPageId[];
  readonly clients: ReadonlyMap<StudioClientId, StudioClientBinding>;
  readonly navigation: StudioNavigationHandle;
  readonly lifecycle: StudioModuleLifecycle;
}

export interface StudioModuleActivation {
  readonly moduleId: string;
  readonly moduleVersion: number;
  readonly frontendAbiChecksum: StudioSha256;
  dispose(): Promise<void>;
}

// Studio graph identities preserve the proposal's fixed camelCase members. L41 type
// identities deliberately use the stricter lowercase grammar in @hpd/base-client.
const ID = /^[a-z][a-zA-Z0-9]*(?:[.-][a-zA-Z0-9]+)*$/u;
const SHA256 = /^[a-f0-9]{64}$/u;

export function studioSha256(value: string): StudioSha256 {
  if (!SHA256.test(value)) throw new TypeError('Studio SHA-256 value is invalid.');
  return value as StudioSha256;
}

export function studioPageId(value: string): StudioPageId {
  if (!ID.test(value) || new TextEncoder().encode(value).length > 128) throw new TypeError('Studio page identity is invalid.');
  return value as StudioPageId;
}

export function studioClientId(value: string): StudioClientId {
  if (!ID.test(value) || new TextEncoder().encode(value).length > 128) throw new TypeError('Studio client identity is invalid.');
  return value as StudioClientId;
}

/** Validates and deeply owns the static descriptor exported by a trusted module chunk. */
export function defineStudioModuleDescriptor(value: StudioModuleDescriptor): StudioModuleDescriptor {
  exactKeys(value, ['moduleId', 'moduleVersion', 'frontendAbiChecksum', 'clientSlots', 'pageComponents']);
  if (!value || !ID.test(value.moduleId) || value.moduleVersion < 1 || !Number.isSafeInteger(value.moduleVersion) ||
      !SHA256.test(value.frontendAbiChecksum) || !Array.isArray(value.clientSlots) ||
      value.clientSlots.length < 1 || value.clientSlots.length > 32 || !value.pageComponents ||
      typeof value.pageComponents !== 'object') throw new TypeError('Studio module descriptor is invalid.');
  const clientSlots = value.clientSlots.map((slot) => {
    exactKeys(slot, ['clientId', 'version', 'staticRuntimeAbiChecksum','protocol','generatedContractChecksum','operationInventoryChecksum',
      'endpointSurfaceId','transportClass','owningPageIds','limitsChecksum']);
    if (!slot || !ID.test(slot.clientId) || slot.version < 1 || !Number.isSafeInteger(slot.version) ||
        !SHA256.test(slot.staticRuntimeAbiChecksum) || !['baseL41DynamicMap','frameworkGeneratedContractV1'].includes(slot.protocol) ||
        !SHA256.test(slot.generatedContractChecksum) || !SHA256.test(slot.operationInventoryChecksum) || !ID.test(slot.endpointSurfaceId) ||
        slot.transportClass !== 'sameOriginShellAuthenticated' || !Array.isArray(slot.owningPageIds) || slot.owningPageIds.length < 1 ||
        !isCanonical(slot.owningPageIds) || !SHA256.test(slot.limitsChecksum)) throw new TypeError('Studio client slot is invalid.');
    return Object.freeze({ clientId: studioClientId(slot.clientId), version: slot.version,
      staticRuntimeAbiChecksum: studioSha256(slot.staticRuntimeAbiChecksum), protocol: slot.protocol,
      generatedContractChecksum: studioSha256(slot.generatedContractChecksum), operationInventoryChecksum: studioSha256(slot.operationInventoryChecksum),
      endpointSurfaceId: slot.endpointSurfaceId, transportClass: slot.transportClass,
      owningPageIds: Object.freeze(slot.owningPageIds.map(studioPageId)), limitsChecksum: studioSha256(slot.limitsChecksum) });
  });
  if (!isCanonical(clientSlots.map((slot) => `${slot.clientId}\0${slot.version}`)))
    throw new TypeError('Studio client slots are not canonical.');
  const entries = Object.entries(value.pageComponents);
  for (const [, binding] of entries) exactKeys(binding, ['componentExportId', 'componentAbiChecksum', 'component']);
  if (entries.length < 1 || entries.length > 64 || !isCanonical(entries.map(([id]) => id)) ||
      entries.some(([id, binding]) => !ID.test(id) || !binding || !ID.test(binding.componentExportId) ||
        !SHA256.test(binding.componentAbiChecksum) || !binding.component))
    throw new TypeError('Studio page components are invalid.');
  const components = Object.freeze(Object.fromEntries(entries.map(([id, binding]) =>
    [id, Object.freeze({ componentExportId: binding.componentExportId,
      componentAbiChecksum: studioSha256(binding.componentAbiChecksum), component: binding.component })]))) as
      Readonly<Record<StudioPageId, StudioPageComponentBinding>>;
  const expected = computeStudioFrontendAbiChecksum(value.moduleId, value.moduleVersion, clientSlots, components);
  if (expected !== value.frontendAbiChecksum) throw new TypeError('Studio frontend ABI checksum is invalid.');
  return Object.freeze({ moduleId: value.moduleId, moduleVersion: value.moduleVersion,
    frontendAbiChecksum: studioSha256(value.frontendAbiChecksum), clientSlots: Object.freeze(clientSlots), pageComponents: components });
}

/** Computes the static frontend ABI checksum from the complete closed descriptor graph. */
export function computeStudioFrontendAbiChecksum(moduleId: string, moduleVersion: number,
  clientSlots: readonly StudioFrontendClientSlot[], pageComponents: Readonly<Record<StudioPageId, StudioPageComponentBinding>>): StudioSha256 {
  const clientChecksums = clientSlots.map(slot => studioCanonicalHash('base.studio.frontend-client-slot.v1', writer => {
    writer.string(slot.clientId); writer.int32(slot.version); writer.discriminator(slot.protocol === 'baseL41DynamicMap' ? 1 : 2);
    writer.checksum(slot.staticRuntimeAbiChecksum); writer.checksum(slot.generatedContractChecksum); writer.checksum(slot.operationInventoryChecksum);
    writer.string(slot.endpointSurfaceId); writer.discriminator(1); writer.count(slot.owningPageIds.length);
    for (const page of slot.owningPageIds) writer.string(page); writer.checksum(slot.limitsChecksum);
  }));
  const componentChecksums = Object.entries(pageComponents).map(([pageId, binding]) =>
    studioCanonicalHash('base.studio.page-component.v1', writer => {
      writer.string(pageId); writer.string(binding.componentExportId); writer.checksum(binding.componentAbiChecksum);
    }));
  return studioSha256(studioCanonicalHash('base.studio.frontend-abi.v1', writer => {
    writer.string(moduleId); writer.int32(moduleVersion); writer.count(clientChecksums.length);
    for (const checksum of clientChecksums) writer.checksum(checksum);
    writer.count(componentChecksums.length); for (const checksum of componentChecksums) writer.checksum(checksum);
  }));
}

/** Cross-checks one principal-filtered activation context against the static descriptor. */
export function validateStudioModuleActivation(
  descriptor: StudioModuleDescriptor,
  context: StudioModuleActivationContext
): void {
  const disclosedPages = new Set(context.disclosedPageIds);
  if (descriptor.moduleId !== context.moduleId || descriptor.moduleVersion !== context.moduleVersion ||
      descriptor.frontendAbiChecksum !== context.frontendAbiChecksum ||
      context.disclosedPageIds.some((pageId) => descriptor.pageComponents[pageId] === undefined) ||
      descriptor.clientSlots.some((slot) => {
        const binding = context.clients.get(slot.clientId);
        return !binding || binding.clientId !== slot.clientId || binding.version !== slot.version ||
          binding.staticRuntimeAbiChecksum !== slot.staticRuntimeAbiChecksum || binding.protocol !== slot.protocol ||
          binding.generatedContractChecksum !== slot.generatedContractChecksum || binding.operationInventoryChecksum !== slot.operationInventoryChecksum ||
          binding.endpointSurfaceId !== slot.endpointSurfaceId || binding.transportClass !== slot.transportClass ||
          binding.limitsChecksum !== slot.limitsChecksum ||
          binding.owningPageIds.join('\0') !== slot.owningPageIds.filter(pageId => disclosedPages.has(pageId)).join('\0');
      }) ||
      context.clients.size !== descriptor.clientSlots.length) {
    throw new TypeError('Studio module activation authority does not match its static descriptor.');
  }
}

function isCanonical(values: readonly string[]): boolean {
  if (new Set(values).size !== values.length) return false;
  for (let index = 1; index < values.length; index++) if (values[index - 1]! >= values[index]!) return false;
  return true;
}

function exactKeys(value: unknown, expected: readonly string[]): void {
  if (!value || typeof value !== 'object' || Array.isArray(value)) throw new TypeError('Studio ABI object is invalid.');
  const actual = Object.keys(value).sort(); const accepted = [...expected].sort();
  if (actual.length !== accepted.length || actual.some((key, index) => key !== accepted[index]))
    throw new TypeError('Studio ABI object members are not exact.');
}
