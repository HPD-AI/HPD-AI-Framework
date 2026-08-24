import assert from 'node:assert/strict';
import test from 'node:test';
import { studioBootstrapChecksum, studioContractMapChecksum, studioResponseAuthorityChecksum,
  studioShellLimitsChecksum, validateStudioBootstrap, type StudioBootstrapSnapshot } from '../src/bootstrap.ts';
import { studioClientId, studioPageId, studioSha256 } from '../src/module-abi.ts';
import { studioCanonicalHash } from '../src/canonical.ts';
import { sha256 } from '@noble/hashes/sha2.js';
import { bytesToHex } from '@noble/hashes/utils.js';

const sha = studioSha256('a'.repeat(64));
const now = new Date('2026-08-22T12:00:00.000Z');

function snapshot(mode: 'inspect' | 'operate' = 'inspect'): StudioBootstrapSnapshot {
  const authority = {
      principalGeneration: '1', authenticatedSessionChecksum: sha, protectedScopeChecksum: sha,
      applicationGraphGeneration: '1', applicationGraphChecksum: sha, studioOwnerGeneration: '1',
      studioOwnerChecksum: sha, policyOwnerGeneration: '1', policyOwnerChecksum: sha, stores: [],
      authorizedThroughUtc: '2026-08-22T12:10:00.0000000Z', checksum: sha
  };
  authority.checksum = studioResponseAuthorityChecksum(authority);
  const descriptor = new TextEncoder().encode('{"kind":"object","properties":[],"additionalProperties":false}');
  const nodeChecksum = studioSha256(bytesToHex(sha256(descriptor)));
  const namedChecksum = studioSha256(studioCanonicalHash('base.studio.named-type.v1', writer => {
    writer.string('base.empty'); writer.bytes(descriptor); writer.checksum(nodeChecksum);
  }));
  const endpointChecksum = studioSha256(studioCanonicalHash('base.studio.endpoint-contract.v1', writer => {
    writer.string('base.page'); writer.int32(1); writer.discriminator(2); writer.string('/base/page'); writer.discriminator(1); writer.discriminator(1);
    writer.string('base.empty'); writer.checksum(nodeChecksum); writer.string('base.empty'); writer.checksum(nodeChecksum);
    writer.string('base.empty'); writer.checksum(nodeChecksum); writer.int64('1024'); writer.int64('1024'); writer.int64('1000');
  }));
  const bindingChecksum = studioSha256(studioCanonicalHash('base.studio.method-binding.v1', writer => {
    writer.string('base.page.read'); writer.discriminator(2); writer.string('base'); writer.string('base.overview'); writer.string('base.page');
    writer.string('base.empty'); writer.string('base.empty');
  }));
  const resolverBindingChecksum = studioSha256(studioCanonicalHash('base.studio.method-binding.v1', writer => {
    writer.string('base.record.resolve'); writer.discriminator(1); writer.string('base'); writer.string('base.record.resolve'); writer.string('base.page');
    writer.string('base.empty'); writer.string('base.empty');
  }));
  const contractMap = { protocolVersion: 'base.protocol', serializationProfile: 'base.json', errorTaxonomy: 'base.error',
    realtimeProtocol: 'base.realtime', runtimeAbiChecksum: sha, interpreterVectorChecksum: sha,
    types: [{ typeId: 'base.empty', canonicalDescriptor: Buffer.from(descriptor).toString('base64url'), nodeChecksum, checksum: namedChecksum }],
    endpoints: [{ endpointId: 'base.page', version: 1, method: 'POST' as const, relativeRoute: '/base/page', audience: 'controlPlane' as const,
      transport: 'sameOriginHttp' as const, requestNodeId: 'base.empty', requestNodeChecksum: nodeChecksum, resultNodeId: 'base.empty', resultNodeChecksum: nodeChecksum,
      errorNodeId: 'base.empty', errorNodeChecksum: nodeChecksum, maximumRequestBytes: '1024', maximumResultBytes: '1024', deadlineMilliseconds: '1000', checksum: endpointChecksum }],
    methods: [{ registeredMethodId: 'base.page.read', kind: 'page' as const, owningModuleId: 'base', owningPageOrCommandId: 'base.overview',
      endpointId: 'base.page', requestTypeId: 'base.empty', resultTypeId: 'base.empty', bindingChecksum },
      { registeredMethodId: 'base.record.resolve', kind: 'resolve' as const, owningModuleId: 'base', owningPageOrCommandId: 'base.record.resolve',
        endpointId: 'base.page', requestTypeId: 'base.empty', resultTypeId: 'base.empty', bindingChecksum: resolverBindingChecksum }], checksum: sha };
  contractMap.checksum = studioContractMapChecksum(contractMap);
  const limits = { maximumModules: 64, maximumPages: 512, maximumCommands: 256, maximumResolvers: 128,
    maximumClients: 32, maximumBootstrapBytes: '1000000', maximumRetainedBytes: '1000000',
    bootstrapDeadlineMilliseconds: '5000', checksum: sha };
  limits.checksum = studioShellLimitsChecksum(limits);
  const clientLimits = { maximumOperations: 1, maximumRequestBytes: '1024', maximumResponseBytes: '1024', maximumConcurrentRequests: 1,
    acquisitionDeadlineMilliseconds: '1000', operationDeadlineMilliseconds: '1000', disposalDeadlineMilliseconds: '1000', checksum: sha };
  clientLimits.checksum = studioSha256(studioCanonicalHash('base.studio.frameworkClient.limits.v1', writer => { writer.int32(1);
    writer.int64('1024'); writer.int64('1024'); writer.int32(1); writer.int64('1000'); writer.int64('1000'); writer.int64('1000'); }));
  const sectionChecksum=studioSha256(studioCanonicalHash('base.studio.section.v1',writer=>{writer.string('summary');writer.string('studio.section.summary');writer.int32(0);writer.discriminator(1);writer.count(1);writer.string('base.overview.view');writer.count(0)}));
  const activityChecksum=studioSha256(studioCanonicalHash('base.studio.activity.v1',writer=>{writer.discriminator(1);writer.int32(1);writer.int32(1);writer.int32(1)}));
  const preferencesChecksum=studioSha256(studioCanonicalHash('base.studio.preference.v1',writer=>{writer.string('base.overview.preferences');writer.int32(1);writer.count(0);writer.int64('1');writer.int64('1')}));
  const viewPresentationChecksum=studioSha256(studioCanonicalHash('base.studio.view-presentation.v1',writer=>{writer.string('base.overview.view');writer.discriminator(0);writer.discriminator(0);writer.discriminator(1);writer.checksum(activityChecksum);writer.checksum(preferencesChecksum)}));
  const pagePresentationChecksum=studioSha256(studioCanonicalHash('base.studio.presentation.v1',writer=>{writer.string('base.overview');writer.int32(1);writer.discriminator(1);writer.discriminator(1);writer.count(1);writer.checksum(sectionChecksum);writer.discriminator(0);writer.discriminator(0);writer.discriminator(1)}));
  const value: StudioBootstrapSnapshot = {
    applicationId: 'sample.application', mode,
    authority,
    modules: [{ moduleId: 'base', version: 1, displayNameMessageId: 'studio.module.base', necessity: 'required',
      registrationChecksum: sha, frontendAbiChecksum: sha, assetGraphChecksum: sha }],
    pages: [{ moduleId: 'base', pageId: studioPageId('base.overview'), version: 1, area: 'overview',
      navigationRole: 'areaLanding', route: { id: 'base.overview.route', segments: [{ kind: 'literal', value: 'overview' },
        { kind: 'parameter', name: 'resource', codec: 'resource' }], query: [] },
      initialResource: null, acceptedResources: ['record'], observationMethodIds: ['base.page.read'], resolverMethodIds: ['base.record.resolve'],
      presentation: { pageId: 'base.overview', pageVersion: 1, navigationRole: 'areaLanding', workspace: 'landing',
        sections: [{ sectionId: 'summary', labelMessageId: 'studio.section.summary', order: 0, kind: 'summary', viewIds: ['base.overview.view'], commandIds: [], checksum: sectionChecksum }],
        resourceRail: null, contextualDetail: null, draftRetention: 'none', checksum: pagePresentationChecksum },
      views: [{ viewId: 'base.overview.view', version: 1, observationMethodId: 'base.page.read', itemKind: 'record', itemNodeId: 'base.empty', itemNodeChecksum: sha,
        presentation: { viewId: 'base.overview.view', grid: null, chart: null, emptyState: 'noItems', activity: { kind: 'explicitRefreshOnly', maximumHintsPerRollingSecond: 1, maximumSupersededRefreshes: 1, maximumCoalescedKeys: 1, checksum: activityChecksum },
          preferences: { schemaId: 'base.overview.preferences', version: 1, allowed: [], maximumBytes: '1', maximumLifetimeMilliseconds: '1', checksum: preferencesChecksum }, checksum: viewPresentationChecksum }, registrationChecksum: sha }],
      registrationChecksum: sha }],
    commands: mode === 'inspect' ? [] : [{ moduleId: 'base', commandId: 'base.record.update', version: 1,
      actionClass: 'routine', owningPageIds: [studioPageId('base.overview')], acceptedResources: ['record'], registrationChecksum: sha }],
    resolvers: [{ moduleId: 'base', kind: 'record', resolverId: 'base.record.resolve', registrationChecksum: sha }],
    linkResolvers: [],
    clients: [{ moduleId: 'base', clientId: studioClientId('base.control-plane'), version: 1,
      protocol: 'baseL41DynamicMap', staticRuntimeAbiChecksum: sha, generatedContractChecksum: sha, operationInventoryChecksum: sha,
      endpointSurfaceId: 'base.runtime', transportClass: 'sameOriginShellAuthenticated', owningPageIds: [studioPageId('base.overview')], limits: clientLimits, operations: [] }],
    contractMap, limits,
    capturedAtUtc: '2026-08-22T12:00:00.0000000Z', expiresAtUtc: '2026-08-22T12:05:00.0000000Z', snapshotChecksum: sha
  };
  return { ...value, snapshotChecksum: studioBootstrapChecksum(value) };
}

test('bootstrap validates and deeply owns current principal inventory', () => {
  const source = snapshot();
  const value = validateStudioBootstrap(source, now);
  (source.modules as unknown as Array<{ moduleId: string }>)[0]!.moduleId = 'changed';
  assert.equal(value.modules[0]!.moduleId, 'base');
  assert.ok(Object.isFrozen(value));
  assert.ok(Object.isFrozen(value.modules));
});

test('inspect mode categorically rejects command inventory', () => {
  const value = snapshot('inspect');
  (value as unknown as { commands: StudioBootstrapSnapshot['commands'] }).commands = [{ moduleId: 'base', commandId: 'base.record.update', version: 1,
    actionClass: 'routine', owningPageIds: [studioPageId('base.overview')], acceptedResources: ['record'], registrationChecksum: sha }];
  assert.throws(() => validateStudioBootstrap(value, now));
});

test('expired, dangling, and noncanonical bootstrap inventories fail closed', () => {
  assert.throws(() => validateStudioBootstrap(snapshot(), new Date('2026-08-22T12:11:00.000Z')));
  assert.throws(() => validateStudioBootstrap({ ...snapshot(), pages: [{ ...snapshot().pages[0]!, moduleId: 'missing' }] }, now));
  assert.throws(() => validateStudioBootstrap({ ...snapshot(), modules: [
    { ...snapshot().modules[0]!, moduleId: 'z' }, { ...snapshot().modules[0]!, moduleId: 'a' }
  ] }, now));
});

test('tampered nested and outer bootstrap authority fails closed', () => {
  const changed = snapshot();
  assert.throws(() => validateStudioBootstrap({ ...changed, applicationId: 'other.application' }, now));
  assert.throws(() => validateStudioBootstrap({ ...changed, limits: { ...changed.limits, maximumPages: 511 } }, now));
  assert.throws(() => validateStudioBootstrap({ ...changed, authority: { ...changed.authority, policyOwnerGeneration: '2' } }, now));
  const page=changed.pages[0]!,section=page.presentation.sections[0]!,view=page.views[0]!;
  assert.throws(()=>validateStudioBootstrap({...changed,pages:[{...page,presentation:{...page.presentation,sections:[{...section,labelMessageId:'studio.section.changed'}]}}]},now));
  assert.throws(()=>validateStudioBootstrap({...changed,pages:[{...page,views:[{...view,presentation:{...view.presentation,activity:{...view.presentation.activity,maximumCoalescedKeys:2}}}]}]},now));
});

test('grid properties must correspond to the exact L41 row node', () => {
  const value=snapshot(),page=value.pages[0]!,view=page.views[0]!,node=value.contractMap.types[0]!,activity=view.presentation.activity,preferences=view.presentation.preferences;
  const columnChecksum=studioSha256(studioCanonicalHash('base.studio.grid-column.v1',writer=>{writer.string('missing');writer.string('missing');writer.discriminator(1);writer.discriminator(1);
    writer.string('studio.column.missing');writer.boolean(true);writer.int32(0);writer.int32(200);writer.int32(160);writer.int32(300);writer.discriminator(0);writer.discriminator(0)}));
  const column={columnId:'missing',stablePropertyOrEdgeId:'missing',renderer:'text',disclosure:'projectedValue',labelMessageId:'studio.column.missing',initiallyVisible:true,initialOrder:0,
    initialWidthCssPixels:200,minimumWidthCssPixels:160,maximumWidthCssPixels:300,filterId:null,sortId:null,checksum:columnChecksum};
  const gridChecksum=studioSha256(studioCanonicalHash('base.studio.grid.v1',writer=>{writer.string('base.grid');writer.int32(1);writer.discriminator(4);writer.string(node.typeId);writer.checksum(node.nodeChecksum);
    writer.count(1);writer.checksum(columnChecksum);writer.discriminator(1);writer.count(0);writer.int32(10);writer.int32(10);writer.int32(10);writer.int64('1024')}));
  const grid={gridId:'base.grid',version:1,rowKind:'record' as const,rowNodeId:node.typeId,rowNodeChecksum:node.nodeChecksum,columns:[column],selection:'none' as const,rowCommandIds:[],virtualizationThreshold:10,accessiblePageSize:10,maximumRows:10,maximumBytes:'1024',checksum:gridChecksum};
  const presentationChecksum=studioSha256(studioCanonicalHash('base.studio.view-presentation.v1',writer=>{writer.string(view.viewId);writer.discriminator(1);writer.checksum(gridChecksum);writer.discriminator(0);writer.discriminator(1);writer.checksum(activity.checksum);writer.checksum(preferences.checksum)}));
  const hostile={...value,pages:[{...page,views:[{...view,itemNodeChecksum:node.nodeChecksum,presentation:{...view.presentation,grid,checksum:presentationChecksum}}]}]};
  hostile.snapshotChecksum=studioBootstrapChecksum(hostile);assert.throws(()=>validateStudioBootstrap(hostile,now),/does not correspond/);
});

test('framework operation purpose, path, and limits are frozen by the inventory checksum', () => {
  const value = snapshot(); const operation = { operationId: 'read', method: 'GET' as const, relativePathTemplate: 'records/{record}', purpose: 'observation' as const,
    requiredCapability: 'base.records.read', maximumRequestBytes: '0', maximumResponseBytes: '1024', deadlineMilliseconds: '1000',
    requestMediaTypes: [] as string[], responseMediaTypes: ['application/json'], requestHeaderNames: [] as string[], responseHeaderNames: [] as string[] };
  const inventory = studioSha256(studioCanonicalHash('base.studio.framework-operation-inventory.v1', writer => { writer.string('base.runtime'); writer.count(1);
    writer.string('read'); writer.discriminator(1); writer.string('records/{record}'); writer.discriminator(2); writer.string('base.records.read');
    writer.int64('0'); writer.int64('1024'); writer.int64('1000'); writer.count(0); writer.count(1); writer.string('application/json'); writer.count(0); writer.count(0); }));
  const client = { ...value.clients[0]!, protocol: 'frameworkGeneratedContractV1' as const, operationInventoryChecksum: inventory, operations: [operation] };
  const admitted = { ...value, clients: [client] }; admitted.snapshotChecksum = studioBootstrapChecksum(admitted);
  assert.doesNotThrow(() => validateStudioBootstrap(admitted, now));
  const substituted = { ...admitted, clients: [{ ...client, operations: [{ ...operation, purpose: 'commandExecution' as const }] }] };
  substituted.snapshotChecksum = studioBootstrapChecksum(substituted); assert.throws(() => validateStudioBootstrap(substituted, now));
});
