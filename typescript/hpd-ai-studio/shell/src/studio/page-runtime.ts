import {
  createStudioRefreshController, formatStudioRoute, studioCanonicalHash, studioSha256, STUDIO_LINK_RELATIONS, validateStudioOutwardResource,
  type StudioBootstrapSnapshot, type StudioCommandHandle, type StudioLinkProjection,
  type StudioLinkRelation, type StudioNavigationHandle, type StudioObservation, type StudioResourceProjection,
  type StudioRuntimeJsonMethod, type StudioRuntimeMethodMap
} from '@hpd-research/hpd-studio-core';
import type { StudioHistoryRoute } from './history-router.ts';

type PageValue = Readonly<{ resource: StudioResourceProjection; links: readonly StudioLinkProjection[];
  views: Readonly<Record<string, unknown>> }>;
export interface StudioPageRuntime { readonly resource: StudioResourceProjection | null; readonly navigation: StudioNavigationHandle;
  readonly commands: StudioCommandHandle; snapshot(): StudioObservation<PageValue>; subscribe(listener: () => void): () => void;
  refresh(signal?: AbortSignal): Promise<void>; dispose(): void; }

/** Creates one route-generation-owned page from server-issued resources and exact disclosed methods. */
export function createStudioPageRuntime(snapshot: StudioBootstrapSnapshot, route: StudioHistoryRoute,
  runtime: StudioRuntimeMethodMap, navigate: (url: string) => void,
  acquireFreshAuthentication: (request: Readonly<{requestIdentity:string;commandId:string;targetToken:string;previewChecksum:string}>, signal:AbortSignal) => Promise<import('./authentication.ts').StudioFreshAuthenticationResult>): StudioPageRuntime {
  const observations = route.page.observationMethodIds.map(id => requiredJson(runtime, id, 'page', route.page.moduleId, route.page.pageId));
  const controller = createStudioRefreshController<PageValue>({ read: async signal => {
    const resolved = route.page.initialResource === null
      ? await resolveRoute(snapshot, route, runtime, signal)
      : Object.freeze({ resource: validateStudioOutwardResource(route.page.initialResource), links: Object.freeze([]) });
    if (!route.page.acceptedResources.includes(resolved.resource.kind) || resolved.resource.applicationId !== snapshot.applicationId)
      throw new TypeError('base.studio.resourceKindMismatch');
    const observationsResult = await Promise.all(observations.map(async method =>
      [method.binding.registeredMethodId, validatePageObservation(await method.invoke(Object.freeze({ resource: resolved.resource }), signal), resolved.resource,
        snapshot)] as const));
    const entries = observationsResult.map(([id, result]) => {
      const view = route.page.views.find(candidate => candidate.observationMethodId === id);
      if (!view) throw new TypeError('base.studio.viewMethodMismatch');
      return [view.viewId, result.value] as const;
    });
    const links = observationsResult.flatMap(([, result]) => result.links);
    return Object.freeze({ kind: 'value' as const, value: Object.freeze({ resource: resolved.resource, links: Object.freeze(links.length === 0 ? [...resolved.links] : links),
      views: Object.freeze(Object.fromEntries(entries)) }), authority: Object.freeze({ coherence: snapshot.authority.checksum,
      authorizedThroughUtc: snapshot.authority.authorizedThroughUtc }) });
  } });
  const navigation: StudioNavigationHandle = Object.freeze({ async navigate(target: Parameters<StudioNavigationHandle['navigate']>[0]): Promise<void> {
    const link = validateLink(target.link, snapshot.applicationId); const source = currentResource(controller.snapshot());
    if (source === null) throw new Error('base.studio.linkUnavailable');
    const resolver = linkResolverFor(snapshot, runtime, route.page.moduleId, source.kind, link.relation, link.target.kind);
    const raw = await resolver.invoke(Object.freeze({ source, target: link.target, relation: link.relation }));
    const resolution = validateResolution(raw, snapshot);
    if (resolution.resource.authorityChecksum !== link.target.authorityChecksum) throw new TypeError('base.studio.linkTargetMismatch');
    if (target.viewId !== undefined && target.viewId !== resolution.pageId) throw new TypeError('base.studio.linkViewMismatch');
    navigate(resolution.url);
  } });
  const commands = createCommandHandle(snapshot, route.page.pageId, runtime, acquireFreshAuthentication);
  return Object.freeze({ get resource(): StudioResourceProjection | null { return currentResource(controller.snapshot()); }, navigation, commands,
    snapshot: controller.snapshot, subscribe(listener: () => void): () => void { return controller.subscribe(() => { try { listener(); } catch { /* isolated */ } }); },
    refresh: controller.refresh, dispose(): void { controller.dispose(); commands.close(); } });
}

function linkResolverFor(snapshot: StudioBootstrapSnapshot, runtime: StudioRuntimeMethodMap, moduleId: string,
  sourceKind: StudioResourceProjection['kind'], relation: StudioLinkProjection['relation'], targetKind: StudioResourceProjection['kind']): StudioRuntimeJsonMethod {
  const matches = snapshot.linkResolvers.filter(item => item.moduleId === moduleId && item.sourceKind === sourceKind && item.relation === relation && item.targetKind === targetKind);
  if (matches.length !== 1) throw new Error('base.studio.linkResolverUnavailable');
  const match = matches[0]!; return requiredJson(runtime, match.methodId, 'resolve', moduleId, match.resolverId);
}

async function resolveRoute(snapshot: StudioBootstrapSnapshot, route: StudioHistoryRoute, runtime: StudioRuntimeMethodMap,
  signal: AbortSignal): Promise<Readonly<{ resource: StudioResourceProjection; links: readonly StudioLinkProjection[] }>> {
  const kind = route.page.acceptedResources.length === 1 ? route.page.acceptedResources[0] : route.match.parameters.resourceKind;
  if (!kind || !route.page.acceptedResources.includes(kind as never)) throw new TypeError('base.studio.routeResourceKindMissing');
  const method = resolverFor(snapshot, runtime, route.page, kind as StudioResourceProjection['kind']);
  const resourceToken = route.match.parameters.resource;
  if (typeof resourceToken !== 'string' || resourceToken.length === 0) throw new TypeError('base.studio.routeResourceTokenMissing');
  const raw = await method.invoke(Object.freeze({ resourceToken }), signal);
  const resolution = validateResolution(raw, snapshot); return Object.freeze({ resource: resolution.resource, links: resolution.links });
}

function resolverFor(snapshot: StudioBootstrapSnapshot, runtime: StudioRuntimeMethodMap, page: StudioBootstrapSnapshot['pages'][number],
  kind: StudioResourceProjection['kind']): StudioRuntimeJsonMethod {
  const registered = snapshot.resolvers.find(value => value.moduleId === page.moduleId && value.kind === kind);
  if (!registered) throw new Error('base.studio.resolverUnavailable');
  const id = page.resolverMethodIds.find(value => snapshot.contractMap.methods.find(method => method.registeredMethodId === value)?.owningPageOrCommandId === registered.resolverId);
  if (!id) throw new Error('base.studio.resolverUnavailable'); return requiredJson(runtime, id, 'resolve', page.moduleId, registered.resolverId);
}

function validateResolution(value: unknown, snapshot: StudioBootstrapSnapshot): Readonly<{ resource: StudioResourceProjection;
  links: readonly StudioLinkProjection[]; pageId: string; url: string }> {
  if (!record(value) || value.kind !== 'resolved' || !record(value.route) || typeof value.route.pageId !== 'string' ||
      !record(value.route.parameters) || !record(value.route.query) || !Array.isArray(value.links)) throw new TypeError('base.studio.resolutionInvalid');
  const resource = validateStudioOutwardResource(value.resource as StudioResourceProjection);
  if (resource.applicationId !== snapshot.applicationId) throw new TypeError('base.studio.resolutionInvalid');
  const links = Object.freeze(value.links.map(link => validateLink(link, snapshot.applicationId)));
  const route = value.route as Record<string, unknown>;
  const page = snapshot.pages.find(candidate => candidate.pageId === route.pageId && candidate.acceptedResources.includes(resource.kind));
  if (!page) throw new TypeError('base.studio.resolutionInvalid');
  const parameters = stringRecord(route.parameters as object); const query = stringRecord(route.query as object);
  return Object.freeze({ resource, links, pageId: page.pageId, url: formatStudioRoute(page.route, parameters, query) });
}
function validateLink(value: unknown, applicationId: string): StudioLinkProjection {
  if (!record(value) || Object.keys(value).sort().join('\0') !== 'label\0relation\0target' || typeof value.relation !== 'string' ||
      !(STUDIO_LINK_RELATIONS as readonly string[]).includes(value.relation) ||
      typeof value.label !== 'string' || value.label.length < 1 || value.label.length > 256) throw new TypeError('base.studio.linkInvalid');
  const target = validateStudioOutwardResource(value.target as StudioResourceProjection); if (target.applicationId !== applicationId) throw new TypeError('base.studio.linkInvalid');
  return Object.freeze({ target, relation: value.relation as StudioLinkRelation, label: value.label });
}

function validatePageObservation(value: unknown, expected: StudioResourceProjection, snapshot: StudioBootstrapSnapshot): Readonly<{ value: unknown; links: readonly StudioLinkProjection[] }> {
  if (!record(value) || typeof value.kind !== 'string') throw new TypeError('base.studio.pageObservationInvalid');
  if (value.kind !== 'current') throw new Error(value.kind === 'unsupported' ? 'base.studio.pageUnsupported' :
    value.kind === 'unavailable' ? 'base.studio.pageUnavailable' : 'base.studio.pageFailed');
  if (Object.keys(value).sort().join('\0') !== 'accounting\0evidence\0kind\0links\0observationAuthority\0resource\0value' ||
      !Array.isArray(value.links) || value.links.length > 128 || !Array.isArray(value.evidence) || value.evidence.length > 256 ||
      value.evidence.some(item => !record(item)) || !record(value.accounting) || !record(value.observationAuthority))
    throw new TypeError('base.studio.pageObservationInvalid');
  const resource = validateStudioOutwardResource(value.resource as StudioResourceProjection);
  if (resource.applicationId !== snapshot.applicationId || resource.authorityChecksum !== expected.authorityChecksum ||
      !validObservationAuthority(value.observationAuthority, snapshot)) throw new TypeError('base.studio.pageObservationInvalid');
  return Object.freeze({ value: deepFreeze(structuredClone(value.value)), links: Object.freeze(value.links.map(link => validateLink(link, snapshot.applicationId))) });
}

function validObservationAuthority(value: Record<string, unknown>, snapshot: StudioBootstrapSnapshot): boolean {
  const keys = ['applicationGraphChecksum','applicationGraphGeneration','authorityChecksum','kind','policyOwnerChecksum','policyOwnerGeneration','studioOwnerChecksum','studioOwnerGeneration'].sort();
  if (Object.keys(value).sort().some((key, index) => key !== keys[index]) || Object.keys(value).length !== keys.length || value.kind !== 'graph' ||
      value.applicationGraphGeneration !== snapshot.authority.applicationGraphGeneration || value.applicationGraphChecksum !== snapshot.authority.applicationGraphChecksum ||
      value.studioOwnerGeneration !== snapshot.authority.studioOwnerGeneration || value.studioOwnerChecksum !== snapshot.authority.studioOwnerChecksum ||
      value.policyOwnerGeneration !== snapshot.authority.policyOwnerGeneration || value.policyOwnerChecksum !== snapshot.authority.policyOwnerChecksum ||
      typeof value.authorityChecksum !== 'string') return false;
  const expected = studioSha256(studioCanonicalHash('base.studio.observation-authority.graph.v1', writer => {
    writer.int64(value.applicationGraphGeneration as string); writer.checksum(value.applicationGraphChecksum as string);
    writer.int64(value.studioOwnerGeneration as string); writer.checksum(value.studioOwnerChecksum as string);
    writer.int64(value.policyOwnerGeneration as string); writer.checksum(value.policyOwnerChecksum as string);
  }));
  return expected === value.authorityChecksum;
}

function createCommandHandle(snapshot: StudioBootstrapSnapshot, pageId: string, runtime: StudioRuntimeMethodMap,
  acquireFreshAuthentication: (request: Readonly<{requestIdentity:string;commandId:string;targetToken:string;previewChecksum:string}>, signal:AbortSignal) => Promise<import('./authentication.ts').StudioFreshAuthenticationResult>): StudioCommandHandle {
  let state: unknown = Object.freeze({ kind: 'closed' }); let commandId = ''; let target: StudioResourceProjection | null = null;
  let input: unknown; let preview: unknown; let resolution: unknown; let active: AbortController | null = null; const accepted = new Set<string>();
  let requestIdentity:string|null=null;let freshAuthentication:Extract<import('./authentication.ts').StudioFreshAuthenticationResult,{kind:'satisfied'}>|null=null;
  const listeners = new Set<(value: unknown) => void>(); const publish = (value: unknown): void => { state = deepFreeze(structuredClone(value)); for (const listener of listeners) try { listener(state); } catch { /* isolated */ } };
  return Object.freeze({ snapshot: () => state, subscribe(listener: (value: unknown) => void): () => void { listeners.add(listener); try { listener(state); } catch { /* isolated */ } return () => listeners.delete(listener); },
    open(id: string, resource: StudioResourceProjection, value: unknown = Object.freeze({})): void {
      const owned = validateStudioOutwardResource(resource); const command = snapshot.commands.find(candidate => candidate.commandId === id &&
        candidate.owningPageIds.includes(pageId as never) && candidate.acceptedResources.includes(owned.kind));
      if (!command || owned.applicationId !== snapshot.applicationId) throw new Error('base.studio.commandUnavailable');
      active?.abort(); commandId = id; target = owned; input = structuredClone(value); preview = undefined; resolution = undefined; accepted.clear();requestIdentity=null;freshAuthentication=null;
      publish({ kind: 'draft', commandId, target, input });
    }, async preview(signal?: AbortSignal): Promise<void> { if (target === null) return; active?.abort(); active = linked(signal); publish({ kind: 'previewing', commandId, target, input });
      try { preview = await commandMethod(snapshot, runtime, commandId, 'preview').invoke(Object.freeze({ commandId, pageId, target, input,
        responseAuthorityChecksum: snapshot.authority.checksum }), active.signal); if (!active.signal.aborted) publish({ kind: 'review', commandId, target, preview }); }
      catch { if (!active.signal.aborted) publish({ kind: 'failed', code: 'base.studio.previewFailed' }); } },
    acknowledge(id:string,value:boolean):void { const required=readAcknowledgements(preview).map(acknowledgementKey);if(!required.includes(id))throw new Error('base.studio.acknowledgementInvalid');
      if(value)accepted.add(id);else accepted.delete(id);publish({kind:'review',commandId,target,preview,acceptedAcknowledgements:Object.freeze([...accepted].sort())}); },
    async execute(signal?: AbortSignal): Promise<void> { if (preview === undefined || target === null || resolution !== undefined) return;
      const command=snapshot.commands.find(candidate=>candidate.commandId===commandId);if(!command)throw new Error('base.studio.commandUnavailable');
      const requiredAcknowledgements=readAcknowledgements(preview);if(requiredAcknowledgements.some(value=>!accepted.has(acknowledgementKey(value)))||accepted.size!==requiredAcknowledgements.length)
        throw new Error('base.studio.acknowledgementsRequired');
      const previewExpiry=readPreviewExpiry(preview);if(Date.parse(previewExpiry)<=Date.now()){if(requestIdentity===null){publish({kind:'failed',code:'base.studio.previewExpired'});return;}
        resolution={kind:'indeterminate',requestIdentity,commandId,target};publish({kind:'unresolved',requestIdentity,resolution});return;}
      requestIdentity??=crypto.randomUUID();active?.abort(); active = linked(signal);let executionDispatched=false;try {
        const previewChecksum=readPreviewChecksum(preview);let authAuthority:string|null=null;if(command.actionClass==='destructive'||command.actionClass==='disasterOrRecoveryDomain'){
          if(freshAuthentication!==null&&Date.parse(freshAuthentication.expiresAtUtc)<=Date.now()){resolution={kind:'indeterminate',requestIdentity,commandId,target};publish({kind:'unresolved',requestIdentity,resolution});return;}
          const fresh=freshAuthentication??await acquireFreshAuthentication(Object.freeze({requestIdentity,commandId,targetToken:resourceToken(target),previewChecksum}),active.signal);
          if(fresh.kind==='unsupported'){publish({kind:'freshAuthenticationUnsupported',requestIdentity});return;}if(fresh.kind==='challenge'){publish({kind:'freshAuthenticationRequired',requestIdentity,challenge:fresh});return;}
          freshAuthentication=fresh;authAuthority=fresh.authority;}
        publish({ kind: 'executing', requestIdentity });
        executionDispatched=true;const result = await commandMethod(snapshot, runtime, commandId, 'execute').invoke(Object.freeze({ commandId, pageId, target, preview, requestIdentity,
          acknowledgements:requiredAcknowledgements.map(value=>Object.freeze({...value,previewChecksum})),freshAuthentication:authAuthority,responseAuthorityChecksum: snapshot.authority.checksum }), active.signal); if (!active.signal.aborted) {
            resolution=record(result)&&result.kind==='indeterminate'?result:undefined;publish(result); }
      } catch(error) { if (!active.signal.aborted) {if(!executionDispatched||(record(error)&&error.code==='base.studio.failedBeforeInfluence'))publish({kind:'retryable',requestIdentity});
        else{resolution={kind:'indeterminate',requestIdentity,commandId,target};publish(resolution);}} } },
    async resolve(signal?: AbortSignal): Promise<void> { if (resolution === undefined) return; const method = snapshot.contractMap.methods.find(value => value.kind === 'receiptResolve');
      if (!method) throw new Error('base.studio.receiptResolutionUnavailable'); active?.abort(); active = linked(signal);
      const result = await requiredJson(runtime, method.registeredMethodId, 'receiptResolve', method.owningModuleId, method.owningPageOrCommandId).invoke(resolution, active.signal);
      if (!active.signal.aborted) publish(result); }, close(): void { active?.abort(); active = null;if(resolution!==undefined){publish({kind:'unresolved',requestIdentity,resolution});return;}
      commandId = ''; target = null; preview = undefined; accepted.clear();requestIdentity=null;freshAuthentication=null;publish({ kind: 'closed' }); } });
}
function readPreviewChecksum(value:unknown):string{if(!record(value)||typeof value.previewChecksum!=='string'||!/^[a-f0-9]{64}$/u.test(value.previewChecksum))throw new TypeError('base.studio.previewInvalid');return value.previewChecksum}
function readPreviewExpiry(value:unknown):string{if(!record(value)||typeof value.expiresAtUtc!=='string'||!/^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}\.[0-9]{7}Z$/u.test(value.expiresAtUtc))throw new TypeError('base.studio.previewInvalid');return value.expiresAtUtc}
type PreviewAcknowledgement=Readonly<{purposeId:string;impactId:string}>;
function readAcknowledgements(value:unknown):readonly PreviewAcknowledgement[]{if(!record(value)||!Array.isArray(value.acknowledgements))throw new TypeError('base.studio.previewInvalid');
  const result=value.acknowledgements.map(item=>{if(!record(item)||Object.keys(item).sort().join('\0')!=='impactId\0purposeId'||typeof item.purposeId!=='string'||typeof item.impactId!=='string'||
      !/^[a-z][a-zA-Z0-9.-]{0,127}$/u.test(item.purposeId)||!/^[a-z][a-zA-Z0-9.-]{0,127}$/u.test(item.impactId))throw new TypeError('base.studio.previewInvalid');
    return Object.freeze({purposeId:item.purposeId,impactId:item.impactId});});if(result.some((item,index)=>index>0&&acknowledgementKey(result[index-1]!)>=acknowledgementKey(item)))throw new TypeError('base.studio.previewInvalid');return Object.freeze(result)}
function acknowledgementKey(value:PreviewAcknowledgement):string{return `${value.purposeId}\0${value.impactId}`}
function resourceToken(value:StudioResourceProjection):string{const bytes=new TextEncoder().encode(JSON.stringify(value));let binary='';for(const byte of bytes)binary+=String.fromCharCode(byte);
  return btoa(binary).replace(/\+/gu,'-').replace(/\//gu,'_').replace(/=+$/gu,'')}
function commandMethod(snapshot: StudioBootstrapSnapshot, runtime: StudioRuntimeMethodMap, commandId: string, kind: 'preview' | 'execute'): StudioRuntimeJsonMethod {
  const owner = snapshot.commands.find(command => command.commandId === commandId); if (!owner) throw new Error('base.studio.commandUnavailable');
  const methods = snapshot.contractMap.methods.filter(method => method.kind === kind && method.owningModuleId === owner.moduleId && method.owningPageOrCommandId === commandId);
  if (methods.length !== 1) throw new Error('base.studio.commandMethodUnavailable'); return requiredJson(runtime, methods[0]!.registeredMethodId, kind, owner.moduleId, commandId);
}
function requiredJson(runtime: StudioRuntimeMethodMap, id: string, kind: string, moduleId: string, owner: string): StudioRuntimeJsonMethod { const method = runtime.methods.get(id);
  if (!method || method.kind !== 'json' || method.binding.kind !== kind || method.binding.owningModuleId !== moduleId || method.binding.owningPageOrCommandId !== owner)
    throw new TypeError('base.studio.methodAuthorityMismatch'); return method; }
function currentResource(value: StudioObservation<PageValue>): StudioResourceProjection | null { return value.state === 'value' || value.state === 'stale' ? value.value.resource : value.state === 'loading' ? value.previous?.resource ?? null : null; }
function stringRecord(value: object): Readonly<Record<string,string>> { const result: Record<string,string> = {}; for (const [key, member] of Object.entries(value)) {
  if (typeof member !== 'string') throw new TypeError('base.studio.resolutionInvalid'); result[key] = member; } return Object.freeze(result); }
function record(value: unknown): value is Record<string, unknown> { return value !== null && typeof value === 'object' && !Array.isArray(value); }
function linked(signal?: AbortSignal): AbortController { const controller = new AbortController(); if (signal?.aborted) controller.abort(); else signal?.addEventListener('abort', () => controller.abort(), { once: true }); return controller; }
function deepFreeze<T>(value: T): T { if (value && typeof value === 'object') for (const child of Object.values(value)) deepFreeze(child); return value && typeof value === 'object' ? Object.freeze(value) : value; }
