import { studioClientId, studioPageId, studioSha256, STUDIO_LINK_RELATIONS, type StudioClientId, type StudioLinkRelation, type StudioPageId, type StudioSha256 } from './module-abi.ts';
import { defineStudioRoute, type StudioRouteDefinition } from './route.ts';
import { studioRouteChecksum } from './route.ts';
import { isStudioResourceKind, STUDIO_RESOURCE_KINDS, validateStudioOutwardResource, type StudioOutwardResourceAuthority, type StudioResourceKind } from './resource.ts';
import { studioCanonicalHash, type StudioCanonicalWriter } from './canonical.ts';

export type StudioOperatingMode = 'inspect' | 'operate';
export type StudioNavigationRole = 'areaLanding' | 'contextual' | 'hiddenResolver';
export type StudioArea = 'overview' | 'data' | 'operations' | 'automations' | 'subjects' |
  'search' | 'security' | 'infrastructure' | 'diagnostics';

export interface StudioStoreAuthority {
  readonly storeIdentity: string;
  readonly providerGeneration: string;
  readonly restoreEpoch: string;
  readonly schemaGeneration: string;
  readonly capabilityChecksum: StudioSha256;
  readonly checksum: StudioSha256;
}

export interface StudioResponseAuthority {
  readonly principalGeneration: string;
  readonly authenticatedSessionChecksum: StudioSha256;
  readonly protectedScopeChecksum: StudioSha256;
  readonly applicationGraphGeneration: string;
  readonly applicationGraphChecksum: StudioSha256;
  readonly studioOwnerGeneration: string;
  readonly studioOwnerChecksum: StudioSha256;
  readonly policyOwnerGeneration: string;
  readonly policyOwnerChecksum: StudioSha256;
  readonly stores: readonly StudioStoreAuthority[];
  readonly authorizedThroughUtc: string;
  readonly checksum: StudioSha256;
}

export interface StudioVisibleModule {
  readonly moduleId: string;
  readonly version: number;
  readonly displayNameMessageId: string;
  readonly necessity: 'required' | 'optional';
  readonly registrationChecksum: StudioSha256;
  readonly frontendAbiChecksum: StudioSha256;
  readonly assetGraphChecksum: StudioSha256;
}

export interface StudioVisiblePage {
  readonly moduleId: string;
  readonly pageId: StudioPageId;
  readonly version: number;
  readonly area: StudioArea;
  readonly navigationRole: StudioNavigationRole;
  readonly route: StudioRouteDefinition;
  readonly initialResource: StudioOutwardResourceAuthority | null;
  readonly acceptedResources: readonly StudioResourceKind[];
  readonly observationMethodIds: readonly string[];
  readonly resolverMethodIds: readonly string[];
  readonly presentation: StudioPagePresentation;
  readonly views: readonly StudioVisibleView[];
  readonly registrationChecksum: StudioSha256;
}

export type StudioWorkspace = 'landing'|'resourceMasterDetail'|'detail'|'timeline'|'queryTool'|'diagnostics';
export type StudioSectionKind = 'summary'|'configuration'|'evidence'|'history'|'actions'|'customSemantic';
export interface StudioPageSection { readonly sectionId: string; readonly labelMessageId: string; readonly order: number;
  readonly kind: StudioSectionKind; readonly viewIds: readonly string[]; readonly commandIds: readonly string[]; readonly checksum: StudioSha256; }
export interface StudioPagePresentation { readonly pageId: string; readonly pageVersion: number; readonly navigationRole: StudioNavigationRole;
  readonly workspace: StudioWorkspace; readonly sections: readonly StudioPageSection[]; readonly resourceRail: StudioResourceRail|null;
  readonly contextualDetail: StudioContextualDetail|null; readonly draftRetention: 'none'|'currentDocumentNavigation'; readonly checksum: StudioSha256; }
export interface StudioResourceRail {readonly railId:string;readonly viewId:string;readonly itemKind:StudioResourceKind;readonly search:'none'|'currentFinitePage'|'registeredView';readonly pinning:'none'|'nonsecretIdentityAndSafeLabel';readonly initialWidthCssPixels:number;readonly minimumWidthCssPixels:number;readonly maximumWidthCssPixels:number;readonly checksum:StudioSha256}
export interface StudioContextualDetail {readonly acceptedKinds:readonly StudioResourceKind[];readonly detailPageIds:readonly string[];readonly fullScreenBelowCssPixels:number;readonly closeBehavior:'navigateToParent'|'restoreReturnTarget';readonly dirtyState:'none'|'confirmDiscardOrStay';readonly checksum:StudioSha256}
export interface StudioViewPresentation { readonly viewId: string; readonly grid: StudioGrid|null;
  readonly chart: StudioChart|null; readonly emptyState: 'noItems'|'noMatches'|'notConfigured'|'historicalUnavailable';
  readonly activity: Readonly<{kind:'explicitRefreshOnly'|'governedInvalidationRefresh';maximumHintsPerRollingSecond:number;maximumSupersededRefreshes:number;maximumCoalescedKeys:number;checksum:StudioSha256}>;
  readonly preferences: Readonly<{schemaId:string;version:number;allowed:readonly string[];maximumBytes:string;maximumLifetimeMilliseconds:string;checksum:StudioSha256}>;
  readonly checksum: StudioSha256; }
export interface StudioGridColumn {readonly columnId:string;readonly stablePropertyOrEdgeId:string;readonly renderer:string;readonly disclosure:string;readonly labelMessageId:string;readonly initiallyVisible:boolean;readonly initialOrder:number;readonly initialWidthCssPixels:number;readonly minimumWidthCssPixels:number;readonly maximumWidthCssPixels:number;readonly filterId:string|null;readonly sortId:string|null;readonly checksum:StudioSha256}
export interface StudioGrid {readonly gridId:string;readonly version:number;readonly rowKind:StudioResourceKind;readonly rowNodeId:string;readonly rowNodeChecksum:StudioSha256;readonly columns:readonly StudioGridColumn[];readonly selection:'none'|'single'|'multipleLocal';readonly rowCommandIds:readonly string[];readonly virtualizationThreshold:number;readonly accessiblePageSize:number;readonly maximumRows:number;readonly maximumBytes:string;readonly checksum:StudioSha256}
export interface StudioChart {readonly chartId:string;readonly kind:'timeBuckets'|'categoryBuckets'|'statusBuckets';readonly bucketViewId:string;readonly equivalentTableViewId:string;readonly maximumBuckets:number;readonly disclosureChannelChecksum:StudioSha256;readonly checksum:StudioSha256}
export interface StudioVisibleView { readonly viewId:string; readonly version:number; readonly observationMethodId:string;
  readonly itemKind:StudioResourceKind; readonly itemNodeId:string; readonly itemNodeChecksum:StudioSha256;
  readonly presentation:StudioViewPresentation; readonly registrationChecksum:StudioSha256; }

export interface StudioVisibleClient {
  readonly moduleId: string;
  readonly clientId: StudioClientId;
  readonly version: number;
  readonly protocol: 'baseL41DynamicMap' | 'frameworkGeneratedContractV1';
  readonly staticRuntimeAbiChecksum: StudioSha256;
  readonly generatedContractChecksum: StudioSha256;
  readonly operationInventoryChecksum: StudioSha256;
  readonly endpointSurfaceId: string;
  readonly transportClass: 'sameOriginShellAuthenticated';
  readonly owningPageIds: readonly StudioPageId[];
  readonly limits: StudioFrameworkClientLimits;
  readonly operations: readonly StudioFrameworkOperation[];
}
export interface StudioFrameworkOperation { readonly operationId: string; readonly method: 'GET'|'POST'|'PUT'|'DELETE';
  readonly relativePathTemplate: string; readonly purpose: 'observation'|'commandPreview'|'commandExecution'|'receiptResolution'|'artifactStaging';
  readonly requiredCapability: string; readonly maximumRequestBytes: string; readonly maximumResponseBytes: string;
  readonly deadlineMilliseconds: string; readonly requestMediaTypes: readonly string[]; readonly responseMediaTypes: readonly string[];
  readonly requestHeaderNames: readonly string[]; readonly responseHeaderNames: readonly string[]; }
export interface StudioFrameworkClientLimits { readonly maximumOperations: number; readonly maximumRequestBytes: string;
  readonly maximumResponseBytes: string; readonly maximumConcurrentRequests: number; readonly acquisitionDeadlineMilliseconds: string;
  readonly operationDeadlineMilliseconds: string; readonly disposalDeadlineMilliseconds: string; readonly checksum: StudioSha256; }

export type StudioActionClass = 'routine' | 'operationalTransition' | 'maintenance' | 'destructive' | 'disasterOrRecoveryDomain';
export interface StudioVisibleCommand {
  readonly moduleId: string;
  readonly commandId: string;
  readonly version: number;
  readonly actionClass: StudioActionClass;
  readonly owningPageIds: readonly StudioPageId[];
  readonly acceptedResources: readonly StudioResourceKind[];
  readonly registrationChecksum: StudioSha256;
}
export interface StudioVisibleResourceResolver {
  readonly moduleId: string;
  readonly kind: StudioResourceKind;
  readonly resolverId: string;
  readonly registrationChecksum: StudioSha256;
}
export interface StudioVisibleLinkResolver { readonly moduleId: string; readonly sourceKind: StudioResourceKind;
  readonly relation: StudioLinkRelation; readonly targetKind: StudioResourceKind; readonly resolverId: string;
  readonly methodId: string; readonly registrationChecksum: StudioSha256; }
export interface StudioEndpointContract {
  readonly endpointId: string;
  readonly version: number;
  readonly method: 'GET' | 'POST' | 'PUT' | 'DELETE' | 'WEBSOCKET';
  readonly relativeRoute: string;
  readonly audience: 'controlPlane';
  readonly transport: 'sameOriginHttp' | 'sameOriginRealtime';
  readonly requestNodeId: string;
  readonly requestNodeChecksum: StudioSha256;
  readonly resultNodeId: string;
  readonly resultNodeChecksum: StudioSha256;
  readonly errorNodeId: string;
  readonly errorNodeChecksum: StudioSha256;
  readonly maximumRequestBytes: string;
  readonly maximumResultBytes: string;
  readonly deadlineMilliseconds: string;
  readonly checksum: StudioSha256;
}
export interface StudioNamedTypeContract { readonly typeId: string; readonly canonicalDescriptor: string; readonly nodeChecksum: StudioSha256; readonly checksum: StudioSha256; }
export type StudioMethodKind = 'resolve' | 'page' | 'preview' | 'execute' | 'receiptQuery' | 'receiptResolve' |
  'invalidationSubscribe' | 'stageCreate' | 'stageUpload' | 'stageFinalize' | 'stageDispose';
export interface StudioMethodBinding { readonly registeredMethodId: string; readonly kind: StudioMethodKind; readonly owningModuleId: string;
  readonly owningPageOrCommandId: string; readonly endpointId: string; readonly requestTypeId: string; readonly resultTypeId: string; readonly bindingChecksum: StudioSha256; }
export interface StudioContractMap {
  readonly protocolVersion: string;
  readonly serializationProfile: string;
  readonly errorTaxonomy: string;
  readonly realtimeProtocol: string;
  readonly runtimeAbiChecksum: StudioSha256;
  readonly interpreterVectorChecksum: StudioSha256;
  readonly types: readonly StudioNamedTypeContract[];
  readonly endpoints: readonly StudioEndpointContract[];
  readonly methods: readonly StudioMethodBinding[];
  readonly checksum: StudioSha256;
}
export interface StudioShellLimits {
  readonly maximumModules: number;
  readonly maximumPages: number;
  readonly maximumCommands: number;
  readonly maximumResolvers: number;
  readonly maximumClients: number;
  readonly maximumBootstrapBytes: string;
  readonly maximumRetainedBytes: string;
  readonly bootstrapDeadlineMilliseconds: string;
  readonly checksum: StudioSha256;
}

export interface StudioBootstrapSnapshot {
  readonly applicationId: string;
  readonly mode: StudioOperatingMode;
  readonly authority: StudioResponseAuthority;
  readonly modules: readonly StudioVisibleModule[];
  readonly pages: readonly StudioVisiblePage[];
  readonly commands: readonly StudioVisibleCommand[];
  readonly resolvers: readonly StudioVisibleResourceResolver[];
  readonly linkResolvers: readonly StudioVisibleLinkResolver[];
  readonly clients: readonly StudioVisibleClient[];
  readonly contractMap: StudioContractMap;
  readonly limits: StudioShellLimits;
  readonly capturedAtUtc: string;
  readonly expiresAtUtc: string;
  readonly snapshotChecksum: StudioSha256;
}

const ID = /^[a-z][a-zA-Z0-9]*(?:[.-][a-zA-Z0-9]+)*$/u;
const UINT = /^(?:0|[1-9][0-9]{0,18})$/u;
const POSITIVE = /^[1-9][0-9]{0,18}$/u;
const UTC = /^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}\.[0-9]{7}Z$/u;
const AREAS: readonly StudioArea[] = Object.freeze([
  'overview', 'data', 'operations', 'automations', 'subjects', 'search', 'security', 'infrastructure', 'diagnostics'
]);

/** Validates and deeply owns a principal-filtered bootstrap snapshot before module activation. */
export function validateStudioBootstrap(value: StudioBootstrapSnapshot, now = new Date()): StudioBootstrapSnapshot {
  exactKeys(value, ['applicationId', 'mode', 'authority', 'modules', 'pages', 'commands', 'resolvers', 'linkResolvers', 'clients',
    'contractMap', 'limits', 'capturedAtUtc', 'expiresAtUtc', 'snapshotChecksum']);
  if (!value || !ID.test(value.applicationId) || !['inspect', 'operate'].includes(value.mode) ||
      !canonicalUtc(value.capturedAtUtc) || !canonicalUtc(value.expiresAtUtc) ||
      Date.parse(value.expiresAtUtc) <= now.getTime() || Date.parse(value.capturedAtUtc) > now.getTime())
    throw new TypeError('Studio bootstrap envelope is invalid.');
  const authority = ownAuthority(value.authority, now);
  if (authority.applicationGraphChecksum !== value.authority.applicationGraphChecksum ||
      Date.parse(value.expiresAtUtc) > Date.parse(authority.authorizedThroughUtc))
    throw new TypeError('Studio bootstrap exceeds current response authority.');
  const modules = value.modules.map(ownModule);
  if (!canonical(modules.map((item) => `${item.moduleId}\0${item.version}`))) throw new TypeError('Studio modules are not canonical.');
  const moduleIds = new Set(modules.map((item) => item.moduleId));
  const pages = value.pages.map(ownPage);
  if (!canonical(pages.map((item) => `${AREAS.indexOf(item.area).toString().padStart(2, '0')}\0${item.moduleId}\0${item.pageId}\0${item.version}`)) ||
      pages.some((page) => !moduleIds.has(page.moduleId))) throw new TypeError('Studio pages are not canonical.');
  const pageIds = new Set(pages.map((page) => page.pageId));
  const disclosedAreas = new Set(pages.filter((page) => page.navigationRole === 'areaLanding').map((page) => page.area));
  if (disclosedAreas.size !== pages.filter((page) => page.navigationRole === 'areaLanding').length ||
      [...disclosedAreas].some((area) => !pageIds.has(pages.find((page) => page.area === area && page.navigationRole === 'areaLanding')!.pageId)))
    throw new TypeError('Studio area landing authority is invalid.');
  const clients = value.clients.map(ownClient);
  if (!canonical(clients.map((item) => `${item.moduleId}\0${item.clientId}\0${item.version}`)) ||
      clients.some((client) => !moduleIds.has(client.moduleId) || client.owningPageIds.some(page => !pageIds.has(page) ||
        pages.find(value => value.pageId === page)?.moduleId !== client.moduleId))) throw new TypeError('Studio clients are not canonical.');
  const commands = value.commands.map(ownCommand);
  if (!canonical(commands.map((item) => `${item.moduleId}\0${item.commandId}\0${item.version}`)) ||
      commands.some((command) => !moduleIds.has(command.moduleId) || command.owningPageIds.some(page => !pageIds.has(page) ||
        pages.find(value => value.pageId === page)?.moduleId !== command.moduleId)) || value.mode === 'inspect' && commands.length !== 0)
    throw new TypeError('Studio commands are not canonical.');
  const resolvers = value.resolvers.map(ownResolver);
  if (!canonical(resolvers.map((item) => `${item.moduleId}\0${resourceKindNumber(item.kind).toString().padStart(3, '0')}\0${item.resolverId}`)) ||
      resolvers.some((resolver) => !moduleIds.has(resolver.moduleId))) throw new TypeError('Studio resolvers are not canonical.');
  const linkResolvers = value.linkResolvers.map(ownLinkResolver);
  if (!canonical(linkResolvers.map(item => `${item.moduleId}\0${resourceKindNumber(item.sourceKind).toString().padStart(3,'0')}\0${STUDIO_LINK_RELATIONS.indexOf(item.relation).toString().padStart(2,'0')}\0${resourceKindNumber(item.targetKind).toString().padStart(3,'0')}\0${item.resolverId}\0${item.methodId}`)) ||
      linkResolvers.some(item => !moduleIds.has(item.moduleId))) throw new TypeError('Studio link resolvers are not canonical.');
  const contractMap = ownContractMap(value.contractMap,
    new Set([...pages.map(x => `${x.moduleId}\0${x.pageId}`), ...commands.map(x => `${x.moduleId}\0${x.commandId}`),
      ...resolvers.map(x => `${x.moduleId}\0${x.resolverId}`), ...linkResolvers.map(x => `${x.moduleId}\0${x.resolverId}`)]));
  validateGridProperties(pages, contractMap);
  const methods = new Map(contractMap.methods.map(method => [method.registeredMethodId, method]));
  for (const page of pages) {
    if (page.observationMethodIds.some(id => methods.get(id)?.kind !== 'page' ||
        methods.get(id)?.owningModuleId !== page.moduleId || methods.get(id)?.owningPageOrCommandId !== page.pageId) ||
        page.resolverMethodIds.some(id => {
          const method = methods.get(id); return method?.kind !== 'resolve' || method.owningModuleId !== page.moduleId ||
            !resolvers.some(resolver => resolver.moduleId === page.moduleId && resolver.resolverId === method.owningPageOrCommandId &&
              page.acceptedResources.includes(resolver.kind));
        }))
      throw new TypeError('Studio page method authority is invalid.');
  }
  if (linkResolvers.some(link => { const method = methods.get(link.methodId); return method?.kind !== 'resolve' ||
      method.owningModuleId !== link.moduleId || method.owningPageOrCommandId !== link.resolverId; }))
    throw new TypeError('Studio link resolver method authority is invalid.');
  const limits = ownLimits(value.limits);
  const owned = freeze({ applicationId: value.applicationId, mode: value.mode, authority, modules, pages, commands, resolvers, linkResolvers, clients,
    contractMap, limits, capturedAtUtc: value.capturedAtUtc, expiresAtUtc: value.expiresAtUtc,
    snapshotChecksum: studioSha256(value.snapshotChecksum) });
  if (studioBootstrapChecksum(owned) !== owned.snapshotChecksum) throw new TypeError('Studio bootstrap checksum is invalid.');
  return owned;
}

function validateGridProperties(pages:readonly StudioVisiblePage[],map:StudioContractMap):void {const types=new Map(map.types.map(type=>[type.typeId,type]));
  for(const page of pages)for(const view of page.views){const grid=view.presentation.grid;if(grid===null)continue;if(grid.rowNodeId!==view.itemNodeId)
      throw new TypeError('Studio grid row node authority is invalid.');const node=types.get(grid.rowNodeId);
    if(!node||node.nodeChecksum!==grid.rowNodeChecksum||node.nodeChecksum!==view.itemNodeChecksum)throw new TypeError('Studio grid row node authority is invalid.');
    const descriptor=JSON.parse(new TextDecoder('utf-8',{fatal:true}).decode(decodeBase64Url(node.canonicalDescriptor))) as unknown;
    if(!descriptor||typeof descriptor!=='object'||Array.isArray(descriptor)||(descriptor as {kind?:unknown}).kind!=='object'||
      !Array.isArray((descriptor as {properties?:unknown}).properties))throw new TypeError('Studio grid row node is not a closed object.');
    const properties=new Set((descriptor as {properties:unknown[]}).properties.map(property=>property&&typeof property==='object'&&!Array.isArray(property)
      ? (property as {name?:unknown}).name:null));if(grid.columns.some(column=>!properties.has(column.stablePropertyOrEdgeId)))
      throw new TypeError('Studio grid column does not correspond to its L41 row node.');}}

function ownAuthority(value: StudioResponseAuthority, now: Date): StudioResponseAuthority {
  exactKeys(value, ['principalGeneration', 'authenticatedSessionChecksum', 'protectedScopeChecksum',
    'applicationGraphGeneration', 'applicationGraphChecksum', 'studioOwnerGeneration', 'studioOwnerChecksum',
    'policyOwnerGeneration', 'policyOwnerChecksum', 'stores', 'authorizedThroughUtc', 'checksum']);
  if (!value || !wireLong(value.principalGeneration, true) || !wireLong(value.applicationGraphGeneration, true) ||
      !wireLong(value.studioOwnerGeneration, true) || !wireLong(value.policyOwnerGeneration, true) ||
      !canonicalUtc(value.authorizedThroughUtc) || Date.parse(value.authorizedThroughUtc) <= now.getTime())
    throw new TypeError('Studio response authority is invalid.');
  const stores = value.stores.map((store) => {
    exactKeys(store, ['storeIdentity', 'providerGeneration', 'restoreEpoch', 'schemaGeneration', 'capabilityChecksum', 'checksum']);
    if (!ID.test(store.storeIdentity) || !wireLong(store.providerGeneration, true) || !wireLong(store.restoreEpoch, false) || !wireLong(store.schemaGeneration, false))
      throw new TypeError('Studio store authority is invalid.');
    const owned = Object.freeze({ storeIdentity: store.storeIdentity, providerGeneration: store.providerGeneration,
      restoreEpoch: store.restoreEpoch, schemaGeneration: store.schemaGeneration,
      capabilityChecksum: studioSha256(store.capabilityChecksum), checksum: studioSha256(store.checksum) });
    if (studioStoreAuthorityChecksum(owned) !== owned.checksum) throw new TypeError('Studio store-authority checksum is invalid.');
    return owned;
  });
  if (!canonical(stores.map((item) => item.storeIdentity))) throw new TypeError('Studio store authorities are not canonical.');
  const owned = freeze({ principalGeneration: value.principalGeneration,
    authenticatedSessionChecksum: studioSha256(value.authenticatedSessionChecksum), protectedScopeChecksum: studioSha256(value.protectedScopeChecksum),
    applicationGraphGeneration: value.applicationGraphGeneration, applicationGraphChecksum: studioSha256(value.applicationGraphChecksum),
    studioOwnerGeneration: value.studioOwnerGeneration, studioOwnerChecksum: studioSha256(value.studioOwnerChecksum),
    policyOwnerGeneration: value.policyOwnerGeneration, policyOwnerChecksum: studioSha256(value.policyOwnerChecksum), stores,
    authorizedThroughUtc: value.authorizedThroughUtc, checksum: studioSha256(value.checksum) });
  if (studioResponseAuthorityChecksum(owned) !== owned.checksum) throw new TypeError('Studio response-authority checksum is invalid.');
  return owned;
}

function ownModule(value: StudioVisibleModule): StudioVisibleModule {
  exactKeys(value, ['moduleId', 'version', 'displayNameMessageId', 'necessity', 'registrationChecksum', 'frontendAbiChecksum', 'assetGraphChecksum']);
  if (!value || !ID.test(value.moduleId) || !ID.test(value.displayNameMessageId) || !Number.isSafeInteger(value.version) || value.version < 1 ||
      !['required', 'optional'].includes(value.necessity)) throw new TypeError('Studio module projection is invalid.');
  return Object.freeze({ moduleId: value.moduleId, version: value.version, displayNameMessageId: value.displayNameMessageId,
    necessity: value.necessity, registrationChecksum: studioSha256(value.registrationChecksum),
    frontendAbiChecksum: studioSha256(value.frontendAbiChecksum), assetGraphChecksum: studioSha256(value.assetGraphChecksum) });
}

function ownPage(value: StudioVisiblePage): StudioVisiblePage {
  exactKeys(value, ['moduleId', 'pageId', 'version', 'area', 'navigationRole', 'route', 'initialResource', 'acceptedResources',
    'observationMethodIds', 'resolverMethodIds', 'presentation', 'views', 'registrationChecksum']);
  if (!value || !ID.test(value.moduleId) || !Number.isSafeInteger(value.version) || value.version < 1 ||
      !AREAS.includes(value.area) || !['areaLanding', 'contextual', 'hiddenResolver'].includes(value.navigationRole))
    throw new TypeError('Studio page projection is invalid.');
  const acceptedResources = value.acceptedResources.map(resource => {
    if (!isStudioResourceKind(resource)) throw new TypeError('Studio page resource is invalid.'); return resource;
  });
  if (!canonical(acceptedResources.map(resource => resourceKindNumber(resource).toString().padStart(3, '0'))) ||
      acceptedResources.length > 64) throw new TypeError('Studio page resources are not canonical.');
  const observationMethodIds = ownIds(value.observationMethodIds, 256, true);
  const resolverMethodIds = ownIds(value.resolverMethodIds, 64, acceptedResources.length === 0);
  const initialResource = value.initialResource === null ? null : validateStudioOutwardResource(value.initialResource);
  const hasResourceParameter = value.route.segments.some(segment => segment.kind === 'parameter' && segment.codec === 'resource');
  if (hasResourceParameter && initialResource !== null || !hasResourceParameter && acceptedResources.length !== 0 && initialResource === null || initialResource !== null && !acceptedResources.includes(initialResource.kind))
    throw new TypeError('Studio initial resource kind is invalid.');
  const presentation = ownPagePresentation(value.presentation); const views = Object.freeze(value.views.map(ownVisibleView));
  const sectionViews = presentation.sections.flatMap(section => section.viewIds);
  if (presentation.pageId !== value.pageId || presentation.pageVersion !== value.version || presentation.navigationRole !== value.navigationRole ||
      sectionViews.length !== views.length || sectionViews.some(id => !views.some(view => view.viewId === id)) ||
      views.map(view => view.observationMethodId).sort().join('\0') !== [...observationMethodIds].sort().join('\0'))
    throw new TypeError('Studio page presentation and executable views differ.');
  return Object.freeze({ moduleId: value.moduleId, pageId: studioPageId(value.pageId), version: value.version,
    area: value.area, navigationRole: value.navigationRole, route: defineStudioRoute(value.route), initialResource, acceptedResources,
    observationMethodIds, resolverMethodIds, presentation, views,
    registrationChecksum: studioSha256(value.registrationChecksum) });
}

function ownPagePresentation(value: StudioPagePresentation): StudioPagePresentation {
  exactKeys(value, ['pageId','pageVersion','navigationRole','workspace','sections','resourceRail','contextualDetail','draftRetention','checksum']);
  if (!ID.test(value.pageId) || !Number.isSafeInteger(value.pageVersion) || value.pageVersion < 1 ||
      !['areaLanding','contextual','hiddenResolver'].includes(value.navigationRole) ||
      !['landing','resourceMasterDetail','detail','timeline','queryTool','diagnostics'].includes(value.workspace) ||
      !['none','currentDocumentNavigation'].includes(value.draftRetention)) throw new TypeError('Studio page presentation is invalid.');
  const sections = Object.freeze(value.sections.map((section, index) => { exactKeys(section,['sectionId','labelMessageId','order','kind','viewIds','commandIds','checksum']);
    if (!ID.test(section.sectionId)||!ID.test(section.labelMessageId)||section.order!==index||!['summary','configuration','evidence','history','actions','customSemantic'].includes(section.kind)) throw new TypeError('Studio section is invalid.');
    const viewIds=ownIds(section.viewIds,64,true),commandIds=ownIds(section.commandIds,64,true),checksum=studioSha256(section.checksum);
    const expected=studioCanonicalHash('base.studio.section.v1',writer=>{writer.string(section.sectionId);writer.string(section.labelMessageId);writer.int32(section.order);
      writer.discriminator(['summary','configuration','evidence','history','actions','customSemantic'].indexOf(section.kind)+1);writer.count(viewIds.length);for(const id of viewIds)writer.string(id);
      writer.count(commandIds.length);for(const id of commandIds)writer.string(id);});if(expected!==checksum)throw new TypeError('Studio section checksum is invalid.');
    return Object.freeze({...section,viewIds,commandIds,checksum}); }));
  const resourceRail=value.resourceRail===null?null:ownResourceRail(value.resourceRail),contextualDetail=value.contextualDetail===null?null:ownContextualDetail(value.contextualDetail);
  if(value.workspace==='resourceMasterDetail'&&(resourceRail===null||contextualDetail===null)||value.workspace!=='resourceMasterDetail'&&resourceRail!==null)throw new TypeError('Studio workspace mechanics are invalid.');
  const checksum=studioSha256(value.checksum),expected=studioCanonicalHash('base.studio.presentation.v1',writer=>{writer.string(value.pageId);writer.int32(value.pageVersion);
    writer.discriminator(['areaLanding','contextual','hiddenResolver'].indexOf(value.navigationRole)+1);writer.discriminator(['landing','resourceMasterDetail','detail','timeline','queryTool','diagnostics'].indexOf(value.workspace)+1);
    writer.count(sections.length);for(const section of sections)writer.checksum(section.checksum);optionalChecksum(writer,resourceRail?.checksum);optionalChecksum(writer,contextualDetail?.checksum);
    writer.discriminator(['none','currentDocumentNavigation'].indexOf(value.draftRetention)+1);});if(expected!==checksum)throw new TypeError('Studio page presentation checksum is invalid.');
  return Object.freeze({...value,sections,resourceRail,contextualDetail,checksum});
}
function ownVisibleView(value: StudioVisibleView): StudioVisibleView { exactKeys(value,['viewId','version','observationMethodId','itemKind','itemNodeId','itemNodeChecksum','presentation','registrationChecksum']);
  if(!ID.test(value.viewId)||!ID.test(value.observationMethodId)||!ID.test(value.itemNodeId)||!Number.isSafeInteger(value.version)||value.version<1||!isStudioResourceKind(value.itemKind)) throw new TypeError('Studio visible view is invalid.');
  const presentation=ownViewPresentation(value.presentation); if(presentation.viewId!==value.viewId) throw new TypeError('Studio view presentation differs.');
  return Object.freeze({...value,presentation,itemNodeChecksum:studioSha256(value.itemNodeChecksum),registrationChecksum:studioSha256(value.registrationChecksum)}); }
function ownViewPresentation(value:StudioViewPresentation):StudioViewPresentation { exactKeys(value,['viewId','grid','chart','emptyState','activity','preferences','checksum']);
  if(!ID.test(value.viewId)||!['noItems','noMatches','notConfigured','historicalUnavailable'].includes(value.emptyState)) throw new TypeError('Studio view presentation is invalid.');
  exactKeys(value.activity,['kind','maximumHintsPerRollingSecond','maximumSupersededRefreshes','maximumCoalescedKeys','checksum']);
  if(!['explicitRefreshOnly','governedInvalidationRefresh'].includes(value.activity.kind)||!boundedInt(value.activity.maximumHintsPerRollingSecond,1,1000)||!boundedInt(value.activity.maximumSupersededRefreshes,1,100)||!boundedInt(value.activity.maximumCoalescedKeys,1,2048))throw new TypeError('Studio activity is invalid.');
  const activityChecksum=studioSha256(value.activity.checksum),activityExpected=studioCanonicalHash('base.studio.activity.v1',writer=>{writer.discriminator(['explicitRefreshOnly','governedInvalidationRefresh'].indexOf(value.activity.kind)+1);writer.int32(value.activity.maximumHintsPerRollingSecond);writer.int32(value.activity.maximumSupersededRefreshes);writer.int32(value.activity.maximumCoalescedKeys);});if(activityExpected!==activityChecksum)throw new TypeError('Studio activity checksum is invalid.');
  exactKeys(value.preferences,['schemaId','version','allowed','maximumBytes','maximumLifetimeMilliseconds','checksum']);const preferenceKinds=['theme','density','railWidth','detailWidth','visibleColumns','columnOrder','columnWidths','nonsecretPins','preferredTab'] as const;
  if(!ID.test(value.preferences.schemaId)||!boundedInt(value.preferences.version,1,2147483647)||!wireLong(value.preferences.maximumBytes,true)||BigInt(value.preferences.maximumBytes)>64000n||!wireLong(value.preferences.maximumLifetimeMilliseconds,true)||BigInt(value.preferences.maximumLifetimeMilliseconds)>15552000000n||value.preferences.allowed.length>9||!canonical(value.preferences.allowed.map(kind=>String(preferenceKinds.indexOf(kind as never)+1).padStart(2,'0')))||value.preferences.allowed.some(kind=>!preferenceKinds.includes(kind as never)))throw new TypeError('Studio preferences are invalid.');
  const allowed=Object.freeze([...value.preferences.allowed]),preferencesChecksum=studioSha256(value.preferences.checksum),preferencesExpected=studioCanonicalHash('base.studio.preference.v1',writer=>{writer.string(value.preferences.schemaId);writer.int32(value.preferences.version);writer.count(allowed.length);for(const kind of allowed)writer.discriminator(preferenceKinds.indexOf(kind as never)+1);writer.int64(value.preferences.maximumBytes);writer.int64(value.preferences.maximumLifetimeMilliseconds);});if(preferencesExpected!==preferencesChecksum)throw new TypeError('Studio preferences checksum is invalid.');
  const grid=value.grid===null?null:ownGrid(value.grid),chart=value.chart===null?null:ownChart(value.chart),checksum=studioSha256(value.checksum),expected=studioCanonicalHash('base.studio.view-presentation.v1',writer=>{writer.string(value.viewId);optionalChecksum(writer,grid?.checksum);optionalChecksum(writer,chart?.checksum);writer.discriminator(['noItems','noMatches','notConfigured','historicalUnavailable'].indexOf(value.emptyState)+1);writer.checksum(activityChecksum);writer.checksum(preferencesChecksum);});if(expected!==checksum)throw new TypeError('Studio view presentation checksum is invalid.');
  return Object.freeze({...value,grid,chart,activity:Object.freeze({...value.activity,checksum:activityChecksum}),preferences:Object.freeze({...value.preferences,allowed,checksum:preferencesChecksum}),checksum}); }
function ownResourceRail(value:StudioResourceRail):StudioResourceRail { exactKeys(value,['railId','viewId','itemKind','search','pinning','initialWidthCssPixels','minimumWidthCssPixels','maximumWidthCssPixels','checksum']);
  if(!ID.test(value.railId)||!ID.test(value.viewId)||!isStudioResourceKind(value.itemKind)||!['none','currentFinitePage','registeredView'].includes(value.search)||!['none','nonsecretIdentityAndSafeLabel'].includes(value.pinning)||!validWidths(value.minimumWidthCssPixels,value.initialWidthCssPixels,value.maximumWidthCssPixels))throw new TypeError('Studio rail is invalid.');
  const checksum=studioSha256(value.checksum),expected=studioCanonicalHash('base.studio.rail.v1',writer=>{writer.string(value.railId);writer.string(value.viewId);writer.discriminator(resourceKindNumber(value.itemKind));writer.discriminator(['none','currentFinitePage','registeredView'].indexOf(value.search)+1);writer.discriminator(['none','nonsecretIdentityAndSafeLabel'].indexOf(value.pinning)+1);writer.int32(value.initialWidthCssPixels);writer.int32(value.minimumWidthCssPixels);writer.int32(value.maximumWidthCssPixels);});if(expected!==checksum)throw new TypeError('Studio rail checksum is invalid.');return Object.freeze({...value,checksum}); }
function ownContextualDetail(value:StudioContextualDetail):StudioContextualDetail { exactKeys(value,['acceptedKinds','detailPageIds','fullScreenBelowCssPixels','closeBehavior','dirtyState','checksum']);
  const acceptedKinds=Object.freeze([...value.acceptedKinds]);if(acceptedKinds.length<1||acceptedKinds.length>64||acceptedKinds.some(kind=>!isStudioResourceKind(kind))||!canonical(acceptedKinds.map(kind=>String(resourceKindNumber(kind)).padStart(3,'0')))||!boundedInt(value.fullScreenBelowCssPixels,320,1280)||!['navigateToParent','restoreReturnTarget'].includes(value.closeBehavior)||!['none','confirmDiscardOrStay'].includes(value.dirtyState))throw new TypeError('Studio contextual detail is invalid.');
  const detailPageIds=ownIds(value.detailPageIds,64,false),checksum=studioSha256(value.checksum),expected=studioCanonicalHash('base.studio.contextual-detail.v1',writer=>{writer.count(acceptedKinds.length);for(const kind of acceptedKinds)writer.discriminator(resourceKindNumber(kind));writer.count(detailPageIds.length);for(const id of detailPageIds)writer.string(id);writer.int32(value.fullScreenBelowCssPixels);writer.discriminator(['navigateToParent','restoreReturnTarget'].indexOf(value.closeBehavior)+1);writer.discriminator(['none','confirmDiscardOrStay'].indexOf(value.dirtyState)+1);});if(expected!==checksum)throw new TypeError('Studio detail checksum is invalid.');return Object.freeze({...value,acceptedKinds,detailPageIds,checksum}); }
function ownGrid(value:StudioGrid):StudioGrid { exactKeys(value,['gridId','version','rowKind','rowNodeId','rowNodeChecksum','columns','selection','rowCommandIds','virtualizationThreshold','accessiblePageSize','maximumRows','maximumBytes','checksum']);
  const renderers=['text','code','boolean','integer','decimal','utcDateTime','status','identityLink','relationExcerpt','disclosureValue'],disclosures=['projectedValue','safeLabelOnly','disclosureStateOnly'];
  if(!ID.test(value.gridId)||!boundedInt(value.version,1,2147483647)||!isStudioResourceKind(value.rowKind)||!ID.test(value.rowNodeId)||!['none','single','multipleLocal'].includes(value.selection)||!boundedInt(value.virtualizationThreshold,1,2147483647)||!boundedInt(value.accessiblePageSize,1,2147483647)||!boundedInt(value.maximumRows,value.accessiblePageSize,2147483647)||!wireLong(value.maximumBytes,true))throw new TypeError('Studio grid is invalid.');
  const columns=Object.freeze(value.columns.map((column,index)=>{exactKeys(column,['columnId','stablePropertyOrEdgeId','renderer','disclosure','labelMessageId','initiallyVisible','initialOrder','initialWidthCssPixels','minimumWidthCssPixels','maximumWidthCssPixels','filterId','sortId','checksum']);
    if(!ID.test(column.columnId)||!ID.test(column.stablePropertyOrEdgeId)||!ID.test(column.labelMessageId)||column.initialOrder!==index||!renderers.includes(column.renderer)||!disclosures.includes(column.disclosure)||!validWidths(column.minimumWidthCssPixels,column.initialWidthCssPixels,column.maximumWidthCssPixels)||(column.filterId!==null&&!ID.test(column.filterId))||(column.sortId!==null&&!ID.test(column.sortId)))throw new TypeError('Studio grid column is invalid.');
    const checksum=studioSha256(column.checksum),expected=studioCanonicalHash('base.studio.grid-column.v1',writer=>{writer.string(column.columnId);writer.string(column.stablePropertyOrEdgeId);writer.discriminator(renderers.indexOf(column.renderer)+1);writer.discriminator(disclosures.indexOf(column.disclosure)+1);writer.string(column.labelMessageId);writer.boolean(column.initiallyVisible);writer.int32(column.initialOrder);writer.int32(column.initialWidthCssPixels);writer.int32(column.minimumWidthCssPixels);writer.int32(column.maximumWidthCssPixels);optionalString(writer,column.filterId);optionalString(writer,column.sortId);});if(expected!==checksum)throw new TypeError('Studio column checksum is invalid.');return Object.freeze({...column,checksum});}));
  if(columns.length<1||columns.length>128)throw new TypeError('Studio grid columns are invalid.');const rowCommandIds=ownIds(value.rowCommandIds,128,true),rowNodeChecksum=studioSha256(value.rowNodeChecksum),checksum=studioSha256(value.checksum);
  const expected=studioCanonicalHash('base.studio.grid.v1',writer=>{writer.string(value.gridId);writer.int32(value.version);writer.discriminator(resourceKindNumber(value.rowKind));writer.string(value.rowNodeId);writer.checksum(rowNodeChecksum);writer.count(columns.length);for(const column of columns)writer.checksum(column.checksum);writer.discriminator(['none','single','multipleLocal'].indexOf(value.selection)+1);writer.count(rowCommandIds.length);for(const id of rowCommandIds)writer.string(id);writer.int32(value.virtualizationThreshold);writer.int32(value.accessiblePageSize);writer.int32(value.maximumRows);writer.int64(value.maximumBytes);});if(expected!==checksum)throw new TypeError('Studio grid checksum is invalid.');
  return Object.freeze({...value,columns,rowCommandIds,rowNodeChecksum,checksum}); }
function ownChart(value:StudioChart):StudioChart { exactKeys(value,['chartId','kind','bucketViewId','equivalentTableViewId','maximumBuckets','disclosureChannelChecksum','checksum']);
  if(!ID.test(value.chartId)||!ID.test(value.bucketViewId)||!ID.test(value.equivalentTableViewId)||!['timeBuckets','categoryBuckets','statusBuckets'].includes(value.kind)||!boundedInt(value.maximumBuckets,1,256))throw new TypeError('Studio chart is invalid.');
  const disclosureChannelChecksum=studioSha256(value.disclosureChannelChecksum),checksum=studioSha256(value.checksum),expected=studioCanonicalHash('base.studio.chart.v1',writer=>{writer.string(value.chartId);writer.discriminator(['timeBuckets','categoryBuckets','statusBuckets'].indexOf(value.kind)+1);writer.string(value.bucketViewId);writer.string(value.equivalentTableViewId);writer.int32(value.maximumBuckets);writer.checksum(disclosureChannelChecksum);});if(expected!==checksum)throw new TypeError('Studio chart checksum is invalid.');return Object.freeze({...value,disclosureChannelChecksum,checksum}); }

function ownClient(value: StudioVisibleClient): StudioVisibleClient {
  exactKeys(value, ['moduleId', 'clientId', 'version', 'protocol', 'staticRuntimeAbiChecksum', 'generatedContractChecksum',
    'operationInventoryChecksum', 'endpointSurfaceId', 'transportClass', 'owningPageIds', 'limits', 'operations']);
  if (!value || !ID.test(value.moduleId) || !Number.isSafeInteger(value.version) || value.version < 1 ||
      !['baseL41DynamicMap', 'frameworkGeneratedContractV1'].includes(value.protocol) || !ID.test(value.endpointSurfaceId) ||
      value.transportClass !== 'sameOriginShellAuthenticated')
    throw new TypeError('Studio client projection is invalid.');
  const owningPageIds = ownIds(value.owningPageIds, 64, false).map(studioPageId); const limits = ownFrameworkClientLimits(value.limits);
  const operations = value.operations.map(ownFrameworkOperation);
  if (!canonical(operations.map(item => item.operationId)) || operations.length > limits.maximumOperations ||
      value.protocol === 'baseL41DynamicMap' && operations.length !== 0 || value.protocol === 'frameworkGeneratedContractV1' && operations.length === 0)
    throw new TypeError('Studio framework operation inventory is invalid.');
  const inventory = studioCanonicalHash('base.studio.framework-operation-inventory.v1', writer => { writer.string(value.endpointSurfaceId); writer.count(operations.length);
    for (const item of operations) { writer.string(item.operationId); writer.discriminator(['GET','POST','PUT','DELETE'].indexOf(item.method) + 1);
      writer.string(item.relativePathTemplate); writer.discriminator(['bootstrap','observation','commandPreview','commandExecution','receiptResolution','artifactStaging'].indexOf(item.purpose) + 1);
      writer.string(item.requiredCapability); writer.int64(item.maximumRequestBytes); writer.int64(item.maximumResponseBytes); writer.int64(item.deadlineMilliseconds);
      writer.count(item.requestMediaTypes.length); for (const member of item.requestMediaTypes) writer.string(member); writer.count(item.responseMediaTypes.length); for (const member of item.responseMediaTypes) writer.string(member);
      writer.count(item.requestHeaderNames.length); for (const member of item.requestHeaderNames) writer.string(member); writer.count(item.responseHeaderNames.length); for (const member of item.responseHeaderNames) writer.string(member); } });
  if (value.protocol === 'frameworkGeneratedContractV1' && inventory !== value.operationInventoryChecksum) throw new TypeError('Studio framework operation inventory checksum is invalid.');
  return Object.freeze({ moduleId: value.moduleId, clientId: studioClientId(value.clientId), version: value.version,
    protocol: value.protocol, staticRuntimeAbiChecksum: studioSha256(value.staticRuntimeAbiChecksum),
    generatedContractChecksum: studioSha256(value.generatedContractChecksum), operationInventoryChecksum: studioSha256(value.operationInventoryChecksum),
    endpointSurfaceId: value.endpointSurfaceId, transportClass: value.transportClass, owningPageIds, limits, operations });
}

function ownFrameworkOperation(value: StudioFrameworkOperation): StudioFrameworkOperation {
  exactKeys(value, ['operationId','method','relativePathTemplate','purpose','requiredCapability','maximumRequestBytes','maximumResponseBytes','deadlineMilliseconds',
    'requestMediaTypes','responseMediaTypes','requestHeaderNames','responseHeaderNames']);
  if (!value || !ID.test(value.operationId) || !['GET','POST','PUT','DELETE'].includes(value.method) ||
      !['observation','commandPreview','commandExecution','receiptResolution','artifactStaging'].includes(value.purpose) ||
      !validFrameworkTemplate(value.relativePathTemplate) ||
      !value.requiredCapability || value.requiredCapability.length > 256 || !wireLong(value.maximumRequestBytes, false) || !wireLong(value.maximumResponseBytes, true) || !wireLong(value.deadlineMilliseconds, true))
    throw new TypeError('Studio framework operation is invalid.');
  const own = (items: readonly string[], maximum: number): readonly string[] => { if (items.length > maximum || !canonical([...items]) || items.some(item => !item || item.length > 128 || /[\r\n]/u.test(item))) throw new TypeError('Studio framework operation members are invalid.'); return Object.freeze([...items]); };
  return Object.freeze({ ...value, requestMediaTypes: own(value.requestMediaTypes, 8), responseMediaTypes: own(value.responseMediaTypes, 8),
    requestHeaderNames: own(value.requestHeaderNames, 32), responseHeaderNames: own(value.responseHeaderNames, 32) });
}
function validFrameworkTemplate(value: string): boolean { if (value.length < 1 || value.length > 2048 || value.startsWith('/')) return false;
  const names = new Set<string>(); return value.split('/').every(segment => { if (segment.length < 1 || segment.length > 512 || segment === '.' || segment === '..' || /[\u0000-\u001f\u007f]/u.test(segment)) return false;
    const open = segment.indexOf('{'); const close = segment.indexOf('}'); if (open < 0) return close < 0 && !segment.includes('{');
    if (close <= open + 1 || segment.indexOf('{', open + 1) >= 0 || segment.indexOf('}', close + 1) >= 0) return false;
    const name = segment.slice(open + 1, close); return /^[A-Za-z0-9._-]+$/u.test(name) && !names.has(name) && !!names.add(name); }); }

function ownFrameworkClientLimits(value: StudioFrameworkClientLimits): StudioFrameworkClientLimits {
  exactKeys(value, ['maximumOperations','maximumRequestBytes','maximumResponseBytes','maximumConcurrentRequests',
    'acquisitionDeadlineMilliseconds','operationDeadlineMilliseconds','disposalDeadlineMilliseconds','checksum']);
  if (!Number.isSafeInteger(value.maximumOperations) || value.maximumOperations < 1 || value.maximumOperations > 4096 ||
      !Number.isSafeInteger(value.maximumConcurrentRequests) || value.maximumConcurrentRequests < 1 || value.maximumConcurrentRequests > 256 ||
      !wireLong(value.maximumRequestBytes, true) || !wireLong(value.maximumResponseBytes, true) || !wireLong(value.acquisitionDeadlineMilliseconds, true) ||
      !wireLong(value.operationDeadlineMilliseconds, true) || !wireLong(value.disposalDeadlineMilliseconds, true)) throw new TypeError('Studio framework-client limits are invalid.');
  const owned = Object.freeze({ ...value, checksum: studioSha256(value.checksum) });
  const expected = studioCanonicalHash('base.studio.frameworkClient.limits.v1', writer => { writer.int32(owned.maximumOperations);
    writer.int64(owned.maximumRequestBytes); writer.int64(owned.maximumResponseBytes); writer.int32(owned.maximumConcurrentRequests);
    writer.int64(owned.acquisitionDeadlineMilliseconds); writer.int64(owned.operationDeadlineMilliseconds); writer.int64(owned.disposalDeadlineMilliseconds); });
  if (expected !== owned.checksum) throw new TypeError('Studio framework-client limits checksum is invalid.'); return owned;
}

function ownCommand(value: StudioVisibleCommand): StudioVisibleCommand {
  exactKeys(value, ['moduleId', 'commandId', 'version', 'actionClass', 'owningPageIds', 'acceptedResources', 'registrationChecksum']);
  if (!value || !ID.test(value.moduleId) || !ID.test(value.commandId) || !Number.isSafeInteger(value.version) || value.version < 1 ||
      !['routine', 'operationalTransition', 'maintenance', 'destructive', 'disasterOrRecoveryDomain'].includes(value.actionClass))
    throw new TypeError('Studio command projection is invalid.');
  const owningPageIds = ownIds(value.owningPageIds, 128, false).map(studioPageId);
  const acceptedResources = value.acceptedResources.map(resource => { if (!isStudioResourceKind(resource)) throw new TypeError(); return resource; });
  if (acceptedResources.length < 1 || !canonical(acceptedResources.map(value => resourceKindNumber(value).toString().padStart(3, '0')))) throw new TypeError('Studio command resources are invalid.');
  return Object.freeze({ moduleId: value.moduleId, commandId: value.commandId, version: value.version,
    actionClass: value.actionClass, owningPageIds, acceptedResources, registrationChecksum: studioSha256(value.registrationChecksum) });
}

function ownResolver(value: StudioVisibleResourceResolver): StudioVisibleResourceResolver {
  exactKeys(value, ['moduleId', 'kind', 'resolverId', 'registrationChecksum']);
  if (!value || !ID.test(value.moduleId) || !ID.test(value.resolverId) || !isStudioResourceKind(value.kind))
    throw new TypeError('Studio resolver projection is invalid.');
  return Object.freeze({ moduleId: value.moduleId, kind: value.kind, resolverId: value.resolverId,
    registrationChecksum: studioSha256(value.registrationChecksum) });
}

function ownLinkResolver(value: StudioVisibleLinkResolver): StudioVisibleLinkResolver {
  exactKeys(value, ['moduleId', 'sourceKind', 'relation', 'targetKind', 'resolverId', 'methodId', 'registrationChecksum']);
  if (!value || !ID.test(value.moduleId) || !isStudioResourceKind(value.sourceKind) ||
      !(STUDIO_LINK_RELATIONS as readonly string[]).includes(value.relation) || !isStudioResourceKind(value.targetKind) ||
      !ID.test(value.resolverId) || !ID.test(value.methodId)) throw new TypeError('Studio link resolver projection is invalid.');
  return Object.freeze({ ...value, registrationChecksum: studioSha256(value.registrationChecksum) });
}

function ownContractMap(value: StudioContractMap, disclosedOwners: ReadonlySet<string>): StudioContractMap {
  exactKeys(value, ['protocolVersion', 'serializationProfile', 'errorTaxonomy', 'realtimeProtocol', 'runtimeAbiChecksum',
    'interpreterVectorChecksum', 'types', 'endpoints', 'methods', 'checksum']);
  if (!value || !ID.test(value.protocolVersion) || !ID.test(value.serializationProfile) || !ID.test(value.errorTaxonomy) ||
      !ID.test(value.realtimeProtocol))
    throw new TypeError('Studio contract map is invalid.');
  const types = value.types.map(type => {
    exactKeys(type, ['typeId', 'canonicalDescriptor', 'nodeChecksum', 'checksum']);
    if (!ID.test(type.typeId)) throw new TypeError('Studio named type is invalid.');
    const descriptor = decodeBase64Url(type.canonicalDescriptor);
    if (descriptor.length < 1 || descriptor.length > 65_536) throw new TypeError('Studio named type descriptor is outside its byte authority.');
    const owned = Object.freeze({ typeId: type.typeId, canonicalDescriptor: type.canonicalDescriptor,
      nodeChecksum: studioSha256(type.nodeChecksum), checksum: studioSha256(type.checksum) });
    const expected = studioCanonicalHash('base.studio.named-type.v1', writer => { writer.string(owned.typeId); writer.bytes(descriptor); writer.checksum(owned.nodeChecksum); });
    if (expected !== owned.checksum) throw new TypeError('Studio named-type checksum is invalid.'); return owned;
  });
  if (!canonical(types.map(x => x.typeId))) throw new TypeError('Studio named types are not canonical.');
  const typeIds = new Set(types.map(x => x.typeId));
  const endpoints = value.endpoints.map((endpoint) => {
    exactKeys(endpoint, ['endpointId', 'version', 'method', 'relativeRoute', 'audience', 'transport', 'requestNodeId', 'requestNodeChecksum',
      'resultNodeId', 'resultNodeChecksum', 'errorNodeId', 'errorNodeChecksum', 'maximumRequestBytes',
      'maximumResultBytes', 'deadlineMilliseconds', 'checksum']);
    if (!ID.test(endpoint.endpointId) || endpoint.version < 1 || !Number.isSafeInteger(endpoint.version) ||
        !['GET', 'POST', 'PUT', 'DELETE', 'WEBSOCKET'].includes(endpoint.method) || endpoint.audience !== 'controlPlane' ||
        !['sameOriginHttp', 'sameOriginRealtime'].includes(endpoint.transport) || !endpoint.relativeRoute.startsWith('/') ||
        !ID.test(endpoint.requestNodeId) || !ID.test(endpoint.resultNodeId) || !ID.test(endpoint.errorNodeId) ||
        !wireLong(endpoint.maximumRequestBytes, true) || !wireLong(endpoint.maximumResultBytes, true) || !wireLong(endpoint.deadlineMilliseconds, true))
      throw new TypeError('Studio endpoint contract is invalid.');
    const owned = Object.freeze({ endpointId: endpoint.endpointId, version: endpoint.version, method: endpoint.method,
      relativeRoute: endpoint.relativeRoute, audience: endpoint.audience, transport: endpoint.transport,
      requestNodeId: endpoint.requestNodeId, requestNodeChecksum: studioSha256(endpoint.requestNodeChecksum),
      resultNodeId: endpoint.resultNodeId, resultNodeChecksum: studioSha256(endpoint.resultNodeChecksum),
      errorNodeId: endpoint.errorNodeId, errorNodeChecksum: studioSha256(endpoint.errorNodeChecksum),
      maximumRequestBytes: endpoint.maximumRequestBytes, maximumResultBytes: endpoint.maximumResultBytes,
      deadlineMilliseconds: endpoint.deadlineMilliseconds, checksum: studioSha256(endpoint.checksum) });
    if (![owned.requestNodeId, owned.resultNodeId, owned.errorNodeId].every(id => typeIds.has(id))) throw new TypeError('Studio endpoint references an absent type.');
    const expected = studioCanonicalHash('base.studio.endpoint-contract.v1', writer => {
      writer.string(owned.endpointId); writer.int32(owned.version); writer.discriminator(['GET','POST','PUT','DELETE','WEBSOCKET'].indexOf(owned.method) + 1);
      writer.string(owned.relativeRoute); writer.discriminator(1); writer.discriminator(owned.transport === 'sameOriginHttp' ? 1 : 2);
      writer.string(owned.requestNodeId); writer.checksum(owned.requestNodeChecksum); writer.string(owned.resultNodeId); writer.checksum(owned.resultNodeChecksum);
      writer.string(owned.errorNodeId); writer.checksum(owned.errorNodeChecksum); writer.int64(owned.maximumRequestBytes); writer.int64(owned.maximumResultBytes); writer.int64(owned.deadlineMilliseconds);
    });
    if (expected !== owned.checksum) throw new TypeError('Studio endpoint checksum is invalid.');
    return owned;
  });
  if (!canonical(endpoints.map((item) => `${item.endpointId}\0${item.version}`))) throw new TypeError('Studio endpoints are not canonical.');
  const endpointIds = new Set(endpoints.map(x => x.endpointId));
  const methods = value.methods.map(method => {
    exactKeys(method, ['registeredMethodId','kind','owningModuleId','owningPageOrCommandId','endpointId','requestTypeId','resultTypeId','bindingChecksum']);
    if (![method.registeredMethodId, method.owningModuleId, method.owningPageOrCommandId, method.endpointId, method.requestTypeId, method.resultTypeId].every(x => ID.test(x)) ||
        !['resolve','page','preview','execute','receiptQuery','receiptResolve','invalidationSubscribe','stageCreate','stageUpload','stageFinalize','stageDispose'].includes(method.kind) ||
        !endpointIds.has(method.endpointId) || !typeIds.has(method.requestTypeId) || !typeIds.has(method.resultTypeId) ||
        !disclosedOwners.has(`${method.owningModuleId}\0${method.owningPageOrCommandId}`))
      throw new TypeError('Studio method binding is invalid.');
    const endpoint = endpoints.find(value => value.endpointId === method.endpointId)!;
    if (method.requestTypeId !== endpoint.requestNodeId || method.resultTypeId !== endpoint.resultNodeId || !methodTransportMatches(method.kind, endpoint))
      throw new TypeError('Studio method transport binding is invalid.');
    const owned = Object.freeze({ registeredMethodId: method.registeredMethodId, kind: method.kind, owningModuleId: method.owningModuleId,
      owningPageOrCommandId: method.owningPageOrCommandId, endpointId: method.endpointId, requestTypeId: method.requestTypeId,
      resultTypeId: method.resultTypeId, bindingChecksum: studioSha256(method.bindingChecksum) });
    const expected = studioCanonicalHash('base.studio.method-binding.v1', writer => {
      writer.string(owned.registeredMethodId); writer.discriminator(['resolve','page','preview','execute','receiptQuery','receiptResolve','invalidationSubscribe','stageCreate','stageUpload','stageFinalize','stageDispose'].indexOf(owned.kind) + 1);
      writer.string(owned.owningModuleId); writer.string(owned.owningPageOrCommandId); writer.string(owned.endpointId); writer.string(owned.requestTypeId); writer.string(owned.resultTypeId);
    });
    if (expected !== owned.bindingChecksum) throw new TypeError('Studio method-binding checksum is invalid.'); return owned;
  });
  if (!canonical(methods.map(x => x.registeredMethodId))) throw new TypeError('Studio methods are not canonical.');
  const owned = freeze({ protocolVersion: value.protocolVersion, serializationProfile: value.serializationProfile,
    errorTaxonomy: value.errorTaxonomy, realtimeProtocol: value.realtimeProtocol,
    runtimeAbiChecksum: studioSha256(value.runtimeAbiChecksum), interpreterVectorChecksum: studioSha256(value.interpreterVectorChecksum),
    types, endpoints, methods, checksum: studioSha256(value.checksum) });
  if (studioContractMapChecksum(owned) !== owned.checksum) throw new TypeError('Studio contract-map checksum is invalid.');
  return owned;
}

function ownLimits(value: StudioShellLimits): StudioShellLimits {
  exactKeys(value, ['maximumModules', 'maximumPages', 'maximumCommands', 'maximumResolvers', 'maximumClients',
    'maximumBootstrapBytes', 'maximumRetainedBytes', 'bootstrapDeadlineMilliseconds', 'checksum']);
  if (!value || ![value.maximumModules, value.maximumPages, value.maximumCommands, value.maximumResolvers, value.maximumClients]
      .every(item => Number.isSafeInteger(item) && item >= 0) || !wireLong(value.maximumBootstrapBytes, true) ||
      !wireLong(value.maximumRetainedBytes, true) || !wireLong(value.bootstrapDeadlineMilliseconds, true))
    throw new TypeError('Studio shell limits are invalid.');
  const owned = Object.freeze({ maximumModules: value.maximumModules, maximumPages: value.maximumPages,
    maximumCommands: value.maximumCommands, maximumResolvers: value.maximumResolvers, maximumClients: value.maximumClients,
    maximumBootstrapBytes: value.maximumBootstrapBytes, maximumRetainedBytes: value.maximumRetainedBytes,
    bootstrapDeadlineMilliseconds: value.bootstrapDeadlineMilliseconds, checksum: studioSha256(value.checksum) });
  if (studioShellLimitsChecksum(owned) !== owned.checksum) throw new TypeError('Studio shell-limits checksum is invalid.');
  return owned;
}

export function studioStoreAuthorityChecksum(value: Omit<StudioStoreAuthority, 'checksum'> | StudioStoreAuthority): StudioSha256 {
  return studioSha256(studioCanonicalHash('base.studio.store-authority.v1', writer => {
    writer.string(value.storeIdentity); writer.int64(value.providerGeneration); writer.int64(value.restoreEpoch);
    writer.int64(value.schemaGeneration); writer.checksum(value.capabilityChecksum);
  }));
}

export function studioResponseAuthorityChecksum(value: Omit<StudioResponseAuthority, 'checksum'> | StudioResponseAuthority): StudioSha256 {
  return studioSha256(studioCanonicalHash('base.studio.response-authority.v1', writer => {
    writer.int64(value.principalGeneration); writer.checksum(value.authenticatedSessionChecksum); writer.checksum(value.protectedScopeChecksum);
    writer.int64(value.applicationGraphGeneration); writer.checksum(value.applicationGraphChecksum); writer.int64(value.studioOwnerGeneration);
    writer.checksum(value.studioOwnerChecksum); writer.int64(value.policyOwnerGeneration); writer.checksum(value.policyOwnerChecksum);
    writer.count(value.stores.length); for (const store of value.stores) writer.checksum(store.checksum); writer.string(value.authorizedThroughUtc);
  }));
}

export function studioShellLimitsChecksum(value: Omit<StudioShellLimits, 'checksum'> | StudioShellLimits): StudioSha256 {
  return studioSha256(studioCanonicalHash('base.studio.shell-limits.v1', writer => {
    writer.int32(value.maximumModules); writer.int32(value.maximumPages); writer.int32(value.maximumCommands);
    writer.int32(value.maximumResolvers); writer.int32(value.maximumClients); writer.int64(value.maximumBootstrapBytes);
    writer.int64(value.maximumRetainedBytes); writer.int64(value.bootstrapDeadlineMilliseconds);
  }));
}

export function studioContractMapChecksum(value: Omit<StudioContractMap, 'checksum'> | StudioContractMap): StudioSha256 {
  return studioSha256(studioCanonicalHash('base.studio.contract-map.v1', writer => {
    writer.string(value.protocolVersion); writer.string(value.serializationProfile); writer.string(value.errorTaxonomy);
    writer.string(value.realtimeProtocol); writer.checksum(value.runtimeAbiChecksum); writer.checksum(value.interpreterVectorChecksum);
    writer.count(value.types.length); for (const type of value.types) writer.checksum(type.checksum);
    writer.count(value.endpoints.length);
    for (const endpoint of value.endpoints) writer.checksum(endpoint.checksum);
    writer.count(value.methods.length); for (const method of value.methods) writer.checksum(method.bindingChecksum);
  }));
}

export function studioBootstrapChecksum(value: Omit<StudioBootstrapSnapshot, 'snapshotChecksum'> | StudioBootstrapSnapshot): StudioSha256 {
  return studioSha256(studioCanonicalHash('base.studio.bootstrap.v1', writer => {
    writer.string(value.applicationId); writer.discriminator(value.mode === 'inspect' ? 1 : 2); writer.checksum(value.authority.checksum);
    writer.count(value.modules.length); for (const x of value.modules) { writer.string(x.moduleId); writer.int32(x.version); writer.string(x.displayNameMessageId); writer.discriminator(x.necessity === 'required' ? 1 : 2); writer.checksum(x.registrationChecksum); writer.checksum(x.frontendAbiChecksum); writer.checksum(x.assetGraphChecksum); }
    writer.count(value.pages.length); for (const x of value.pages) { writer.string(x.moduleId); writer.string(x.pageId); writer.int32(x.version); writer.discriminator(AREAS.indexOf(x.area) + 1); writer.discriminator(['areaLanding', 'contextual', 'hiddenResolver'].indexOf(x.navigationRole) + 1); writer.checksum(studioRouteChecksum(x.route));
      writer.count(x.acceptedResources.length); for (const resource of x.acceptedResources) writer.discriminator(resourceKindNumber(resource));
      writer.count(x.observationMethodIds.length); for (const method of x.observationMethodIds) writer.string(method);
      writer.count(x.resolverMethodIds.length); for (const method of x.resolverMethodIds) writer.string(method);
      writer.boolean(x.initialResource !== null); if (x.initialResource !== null) writer.checksum(x.initialResource.authorityChecksum);
      writer.checksum(x.presentation.checksum); writer.count(x.views.length); for (const view of x.views) { writer.string(view.viewId); writer.int32(view.version);
        writer.string(view.observationMethodId); writer.discriminator(resourceKindNumber(view.itemKind)); writer.string(view.itemNodeId); writer.checksum(view.itemNodeChecksum);
        writer.checksum(view.presentation.checksum); writer.checksum(view.registrationChecksum); }
      writer.checksum(x.registrationChecksum); }
    writer.count(value.commands.length); for (const x of value.commands) { writer.string(x.moduleId); writer.string(x.commandId); writer.int32(x.version); writer.discriminator(['routine', 'operationalTransition', 'maintenance', 'destructive', 'disasterOrRecoveryDomain'].indexOf(x.actionClass) + 1);
      writer.count(x.owningPageIds.length); for (const page of x.owningPageIds) writer.string(page); writer.count(x.acceptedResources.length);
      for (const resource of x.acceptedResources) writer.discriminator(resourceKindNumber(resource)); writer.checksum(x.registrationChecksum); }
    writer.count(value.resolvers.length); for (const x of value.resolvers) { writer.string(x.moduleId); writer.discriminator(resourceKindNumber(x.kind)); writer.string(x.resolverId); writer.checksum(x.registrationChecksum); }
    writer.count(value.linkResolvers.length); for (const x of value.linkResolvers) { writer.string(x.moduleId); writer.discriminator(resourceKindNumber(x.sourceKind));
      writer.discriminator(STUDIO_LINK_RELATIONS.indexOf(x.relation) + 1); writer.discriminator(resourceKindNumber(x.targetKind)); writer.string(x.resolverId); writer.string(x.methodId); writer.checksum(x.registrationChecksum); }
    writer.count(value.clients.length); for (const x of value.clients) { writer.string(x.moduleId); writer.string(x.clientId); writer.int32(x.version);
      writer.discriminator(x.protocol === 'baseL41DynamicMap' ? 1 : 2); writer.checksum(x.staticRuntimeAbiChecksum); writer.checksum(x.generatedContractChecksum);
      writer.checksum(x.operationInventoryChecksum); writer.string(x.endpointSurfaceId); writer.discriminator(1); writer.count(x.owningPageIds.length);
      for (const page of x.owningPageIds) writer.string(page); writer.checksum(x.limits.checksum); writer.count(x.operations.length);
      for (const operation of x.operations) { writer.string(operation.operationId); writer.discriminator(['GET','POST','PUT','DELETE'].indexOf(operation.method) + 1);
        writer.string(operation.relativePathTemplate); writer.discriminator(['bootstrap','observation','commandPreview','commandExecution','receiptResolution','artifactStaging'].indexOf(operation.purpose) + 1);
        writer.string(operation.requiredCapability); writer.int64(operation.maximumRequestBytes); writer.int64(operation.maximumResponseBytes); writer.int64(operation.deadlineMilliseconds); } }
    writer.checksum(value.contractMap.checksum); writer.checksum(value.limits.checksum); writer.string(value.capturedAtUtc); writer.string(value.expiresAtUtc);
  }));
}

function decodeBase64Url(value: string): Uint8Array {
  if (!/^[A-Za-z0-9_-]+$/u.test(value)) throw new TypeError('Studio canonical bytes are invalid.');
  const base64 = value.replace(/-/gu, '+').replace(/_/gu, '/').padEnd(Math.ceil(value.length / 4) * 4, '=');
  const bytes = Uint8Array.from(globalThis.atob(base64), character => character.charCodeAt(0));
  const encoded = globalThis.btoa(String.fromCharCode(...bytes)).replace(/=/gu, '').replace(/\+/gu, '-').replace(/\//gu, '_');
  if (encoded !== value) throw new TypeError('Studio canonical bytes are invalid.'); return bytes;
}

function methodTransportMatches(kind: StudioMethodKind, endpoint: StudioEndpointContract): boolean {
  if (kind === 'invalidationSubscribe') return endpoint.transport === 'sameOriginRealtime' && endpoint.method === 'WEBSOCKET';
  if (kind === 'stageUpload') return endpoint.transport === 'sameOriginHttp' && endpoint.method === 'PUT';
  return endpoint.transport === 'sameOriginHttp' && endpoint.method !== 'WEBSOCKET';
}

function resourceKindNumber(kind: StudioResourceKind): number {
  return STUDIO_RESOURCE_KINDS.indexOf(kind) + 1;
}

function optionalChecksum(writer:StudioCanonicalWriter,value:string|undefined):void{writer.discriminator(value===undefined?0:1);if(value!==undefined)writer.checksum(value)}
function optionalString(writer:StudioCanonicalWriter,value:string|null):void{writer.discriminator(value===null?0:1);if(value!==null)writer.string(value)}
function boundedInt(value:number,minimum:number,maximum:number):boolean{return Number.isSafeInteger(value)&&value>=minimum&&value<=maximum}
function validWidths(minimum:number,initial:number,maximum:number):boolean{return boundedInt(minimum,160,1600)&&boundedInt(initial,minimum,1600)&&boundedInt(maximum,initial,1600)}

function exactKeys(value: unknown, expected: readonly string[]): void {
  if (!value || typeof value !== 'object' || Array.isArray(value)) throw new TypeError('Studio object is invalid.');
  const keys = Object.keys(value).sort();
  const accepted = [...expected].sort();
  if (keys.length !== accepted.length || keys.some((key, index) => key !== accepted[index]))
    throw new TypeError('Studio object members are not exact.');
}

function wireLong(value: string, positive: boolean): boolean {
  if (!(positive ? POSITIVE : UINT).test(value)) return false;
  try { const number = BigInt(value); return number <= 9_223_372_036_854_775_807n && (!positive || number > 0n); }
  catch { return false; }
}

function canonicalUtc(value: string): boolean {
  if (!UTC.test(value)) return false;
  const year = Number(value.slice(0, 4)); const month = Number(value.slice(5, 7)); const day = Number(value.slice(8, 10));
  const hour = Number(value.slice(11, 13)); const minute = Number(value.slice(14, 16)); const second = Number(value.slice(17, 19));
  if (year < 1 || month < 1 || month > 12 || day < 1 || hour > 23 || minute > 59 || second > 59) return false;
  const days = new Date(Date.UTC(year, month, 0)).getUTCDate();
  return day <= days && Number.isFinite(Date.parse(value));
}

function canonical(values: readonly string[]): boolean {
  if (new Set(values).size !== values.length) return false;
  for (let index = 1; index < values.length; index++) if (values[index - 1]! >= values[index]!) return false;
  return true;
}

function ownIds(values: readonly string[], maximum: number, allowEmpty: boolean): readonly string[] {
  if (!Array.isArray(values) || (!allowEmpty && values.length === 0) || values.length > maximum ||
      values.some(value => !ID.test(value)) || !canonical(values)) throw new TypeError('Studio identities are not canonical.');
  return Object.freeze([...values]);
}

function freeze<T extends object>(value: T): T {
  for (const member of Object.values(value)) if (Array.isArray(member)) Object.freeze(member);
  return Object.freeze(value);
}
