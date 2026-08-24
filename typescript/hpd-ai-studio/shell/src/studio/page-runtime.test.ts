import { describe, expect, it, vi } from 'vitest';

const noFresh = async (): Promise<{kind:'unsupported'}> => ({kind:'unsupported'});
import { studioCanonicalHash, studioOutwardResourceChecksum, studioPageId, studioSha256,
  type StudioBootstrapSnapshot, type StudioRuntimeMethodMap } from '@hpd-research/hpd-studio-core';
import { createStudioPageRuntime } from './page-runtime.ts';

const checksum = studioSha256('a'.repeat(64));
const page = Object.freeze({ moduleId: 'base', pageId: studioPageId('base.collection'), version: 1, area: 'data' as const,
  navigationRole: 'areaLanding' as const, route: { id: 'base.collection.route', segments: [{ kind: 'literal' as const, value: 'collections' },
    { kind: 'parameter' as const, name: 'resource', codec: 'resource' as const }], query: [] }, initialResource: null, acceptedResources: ['collection'] as const,
  observationMethodIds: ['base.collection.read'], resolverMethodIds: ['base.collection.resolve'], presentation: { pageId: 'base.collection', pageVersion: 1,
    navigationRole: 'areaLanding' as const, workspace: 'landing' as const, sections: [{ sectionId: 'summary', labelMessageId: 'studio.section.summary', order: 0,
      kind: 'summary' as const, viewIds: ['base.collection.view'], commandIds: [], checksum }], resourceRail: null, contextualDetail: null,
    draftRetention: 'none' as const, checksum }, views: [{ viewId: 'base.collection.view', version: 1, observationMethodId: 'base.collection.read',
      itemKind: 'collection' as const, itemNodeId: 'base.collection.item', itemNodeChecksum: checksum, presentation: { viewId: 'base.collection.view', grid: null,
        chart: null, emptyState: 'noItems' as const, activity: { kind: 'explicitRefreshOnly' as const, maximumHintsPerRollingSecond: 1,
          maximumSupersededRefreshes: 1, maximumCoalescedKeys: 1, checksum }, preferences: { schemaId: 'base.collection.preferences', version: 1,
          allowed: [], maximumBytes: '1', maximumLifetimeMilliseconds: '1', checksum }, checksum }, registrationChecksum: checksum }], registrationChecksum: checksum });
const snapshot = { applicationId: 'sample.application', pages: [page], commands: [], resolvers: [
  { moduleId: 'base', kind: 'collection', resolverId: 'base.collection.resolver', registrationChecksum: checksum }], linkResolvers: [
    { moduleId: 'base', sourceKind: 'collection', relation: 'containedBy', targetKind: 'collection', resolverId: 'base.collection.linkResolver',
      methodId: 'base.collection.link', registrationChecksum: checksum }], authority: { checksum, applicationGraphGeneration: '1', applicationGraphChecksum: checksum,
  studioOwnerGeneration: '1', studioOwnerChecksum: checksum, policyOwnerGeneration: '1', policyOwnerChecksum: checksum,
  authorizedThroughUtc: '2099-08-22T12:00:00.0000000Z' }, contractMap: { methods: [
    { registeredMethodId: 'base.collection.read', kind: 'page', owningModuleId: 'base', owningPageOrCommandId: 'base.collection' },
    { registeredMethodId: 'base.collection.resolve', kind: 'resolve', owningModuleId: 'base', owningPageOrCommandId: 'base.collection.resolver' },
    { registeredMethodId: 'base.collection.link', kind: 'resolve', owningModuleId: 'base', owningPageOrCommandId: 'base.collection.linkResolver' }
  ] } } as unknown as StudioBootstrapSnapshot;

describe('page runtime authority', () => {
  it('dispatches only the exact page method and publishes its immutable observation', async () => {
    const invoke = vi.fn(async request => current((request as { resource: ReturnType<typeof collection> }).resource));
    const runtime = map([{ id: 'base.collection.read', kind: 'page', owner: 'base.collection', invoke }, resolver('users')]);
    const route = Object.freeze({ page, match: Object.freeze({ routeId: page.route.id, parameters: Object.freeze({ resource: 'users' }),
      query: Object.freeze({}), canonicalUrl: '/collections/users' }) });
    const value = createStudioPageRuntime(snapshot, route, runtime, vi.fn(), noFresh); await value.refresh();
    expect(invoke).toHaveBeenCalledWith({ resource: collection('users') }, expect.any(AbortSignal));
    expect(value.snapshot().state).toBe('value'); expect(Object.isFrozen(value.snapshot())).toBe(true);
    expect(value.snapshot()).toMatchObject({ value: { views: { 'base.collection.view': { rows: [] } } } });
  });

  it('accepts checksum-bound navigation and rejects substituted outward authority', async () => {
    const navigate = vi.fn(); const runtime = map([{ id: 'base.collection.read', kind: 'page', owner: 'base.collection', invoke: async request => current((request as { resource: ReturnType<typeof collection> }).resource) }, resolver('users'), linkResolver('roles')]);
    const route = Object.freeze({ page, match: Object.freeze({ routeId: page.route.id, parameters: Object.freeze({ resource: 'users' }),
      query: Object.freeze({}), canonicalUrl: '/collections/users' }) });
    const value = createStudioPageRuntime(snapshot, route, runtime, navigate, noFresh);
    await value.refresh(); const resource = collection('roles'); const link = { target: resource, relation: 'containedBy' as const, label: 'Roles' };
    await value.navigation.navigate({ link }); expect(navigate).toHaveBeenCalledWith('/collections/roles');
    await expect(value.navigation.navigate({ link: { ...link, target: { ...resource, collectionId: 'admin' } } })).rejects.toThrow();
  });

  it('rejects a method whose registered owner was substituted', () => {
    const route = Object.freeze({ page, match: Object.freeze({ routeId: page.route.id, parameters: Object.freeze({ resource: 'users' }),
      query: Object.freeze({}), canonicalUrl: '/collections/users' }) });
    expect(() => createStudioPageRuntime(snapshot, route,
      map([{ id: 'base.collection.read', kind: 'page', owner: 'base.other', invoke: async () => ({}) }, resolver('users')]), vi.fn(), noFresh)).toThrow();
  });

  it('rejects a link resolver that substitutes the disclosed target', async () => {
    const route = Object.freeze({ page, match: Object.freeze({ routeId: page.route.id, parameters: Object.freeze({ resource: 'users' }),
      query: Object.freeze({}), canonicalUrl: '/collections/users' }) });
    const runtime = map([{ id: 'base.collection.read', kind: 'page', owner: 'base.collection', invoke: async request => current((request as { resource: ReturnType<typeof collection> }).resource) },
      resolver('users'), linkResolver('admin')]);
    const value = createStudioPageRuntime(snapshot, route, runtime, vi.fn(), noFresh); await value.refresh();
    await expect(value.navigation.navigate({ link: { target: collection('roles'), relation: 'containedBy', label: 'Roles' } })).rejects.toThrow('base.studio.linkTargetMismatch');
  });

  it('rejects Current authority substitution before publishing typed truth', async () => {
    const route = Object.freeze({ page, match: Object.freeze({ routeId: page.route.id, parameters: Object.freeze({ resource: 'users' }),
      query: Object.freeze({}), canonicalUrl: '/collections/users' }) });
    const runtime = map([{ id: 'base.collection.read', kind: 'page', owner: 'base.collection', invoke: async request => {
      const result = current((request as { resource: ReturnType<typeof collection> }).resource);
      return { ...result, observationAuthority: { ...result.observationAuthority, policyOwnerGeneration: '2' } };
    } }, resolver('users')]);
    const value = createStudioPageRuntime(snapshot, route, runtime, vi.fn(), noFresh); await value.refresh();
    expect(value.snapshot().state).toBe('failed');
  });

  it('reuses the shell-owned request identity only after proved no influence', async () => {
    const execute = vi.fn().mockRejectedValueOnce(Object.assign(new Error('transport'), { code: 'base.studio.failedBeforeInfluence' }))
      .mockResolvedValueOnce({ kind: 'completed', receipt: 'receipt-1' });
    const value = commandRuntime(execute);
    value.commands.open('base.collection.delete', collection('users'));
    await value.commands.preview();
    value.commands.acknowledge('delete\0irreversible', true);
    await value.commands.execute();
    expect(value.commands.snapshot()).toMatchObject({ kind: 'retryable' });
    await value.commands.execute();
    expect(execute).toHaveBeenCalledTimes(2);
    const first = execute.mock.calls[0]![0] as { requestIdentity: string };
    const second = execute.mock.calls[1]![0] as { requestIdentity: string };
    expect(first.requestIdentity).toMatch(/^[0-9a-f-]{36}$/u);
    expect(second.requestIdentity).toBe(first.requestIdentity);
  });

  it('never re-executes an indeterminate request and resolves it only through receipt authority', async () => {
    const execute = vi.fn().mockRejectedValue(new Error('response lost'));
    const resolve = vi.fn().mockResolvedValue({ kind: 'completed', receipt: 'receipt-1' });
    const value = commandRuntime(execute, resolve);
    value.commands.open('base.collection.delete', collection('users'));
    await value.commands.preview();
    value.commands.acknowledge('delete\0irreversible', true);
    await value.commands.execute();
    expect(value.commands.snapshot()).toMatchObject({ kind: 'indeterminate' });
    value.commands.close();
    expect(value.commands.snapshot()).toMatchObject({ kind: 'unresolved' });
    await value.commands.execute();
    expect(execute).toHaveBeenCalledTimes(1);
    await value.commands.resolve();
    expect(resolve).toHaveBeenCalledTimes(1);
    expect(value.commands.snapshot()).toEqual({ kind: 'completed', receipt: 'receipt-1' });
  });

  it('requires the exact acknowledgement set and binds its evidence to the preview checksum', async () => {
    const execute = vi.fn().mockResolvedValue({ kind: 'completed' });
    const value = commandRuntime(execute);
    value.commands.open('base.collection.delete', collection('users'));
    await value.commands.preview();
    await expect(value.commands.execute()).rejects.toThrow('base.studio.acknowledgementsRequired');
    expect(() => value.commands.acknowledge('delete\0substituted', true)).toThrow('base.studio.acknowledgementInvalid');
    value.commands.acknowledge('delete\0irreversible', true);
    await value.commands.execute();
    expect(execute.mock.calls[0]![0]).toMatchObject({ acknowledgements: [{ purposeId: 'delete', impactId: 'irreversible', previewChecksum: checksum }] });
  });
  it('retains the same identity when fresh authentication fails before command dispatch', async () => {
    const execute=vi.fn().mockResolvedValue({kind:'completed'});const fresh=vi.fn().mockRejectedValueOnce(new Error('auth unavailable'))
      .mockResolvedValueOnce({kind:'satisfied',authority:'A'.repeat(32),expiresAtUtc:'2099-08-22T12:00:00.0000000Z'});
    const value=commandRuntime(execute,vi.fn(async()=>undefined),'destructive',fresh);value.commands.open('base.collection.delete',collection('users'));
    await value.commands.preview();value.commands.acknowledge('delete\0irreversible',true);await value.commands.execute();
    const identity=(value.commands.snapshot() as {requestIdentity:string}).requestIdentity;expect(value.commands.snapshot()).toMatchObject({kind:'retryable'});
    await value.commands.execute();expect(execute).toHaveBeenCalledTimes(1);expect((execute.mock.calls[0]![0] as {requestIdentity:string}).requestIdentity).toBe(identity);
  });
});

function commandRuntime(execute: (request: unknown, signal?: AbortSignal) => Promise<unknown>,
  resolve: (request: unknown, signal?: AbortSignal) => Promise<unknown> = vi.fn(async () => undefined), actionClass='reversible',
  fresh: Parameters<typeof createStudioPageRuntime>[4]=noFresh): ReturnType<typeof createStudioPageRuntime> {
  const command = { moduleId: 'base', commandId: 'base.collection.delete', actionClass, owningPageIds: ['base.collection'],
    acceptedResources: ['collection'] };
  const commandSnapshot = { ...snapshot, commands: [command], contractMap: { methods: [...snapshot.contractMap.methods,
    { registeredMethodId: 'base.collection.delete.preview', kind: 'preview', owningModuleId: 'base', owningPageOrCommandId: command.commandId },
    { registeredMethodId: 'base.collection.delete.execute', kind: 'execute', owningModuleId: 'base', owningPageOrCommandId: command.commandId },
    { registeredMethodId: 'base.receipt.resolve', kind: 'receiptResolve', owningModuleId: 'base', owningPageOrCommandId: command.commandId }] } } as unknown as StudioBootstrapSnapshot;
  const preview = { previewChecksum: checksum, expiresAtUtc: '2099-08-22T12:00:00.0000000Z', acknowledgements: [{ purposeId: 'delete', impactId: 'irreversible' }] };
  const runtime = map([{ id: 'base.collection.read', kind: 'page', owner: 'base.collection', invoke: async request => current((request as { resource: ReturnType<typeof collection> }).resource) },
    resolver('users'), { id: 'base.collection.delete.preview', kind: 'preview', owner: command.commandId, invoke: async () => preview },
    { id: 'base.collection.delete.execute', kind: 'execute', owner: command.commandId, invoke: execute },
    { id: 'base.receipt.resolve', kind: 'receiptResolve', owner: command.commandId, invoke: resolve }]);
  const route = Object.freeze({ page, match: Object.freeze({ routeId: page.route.id, parameters: Object.freeze({ resource: 'users' }), query: Object.freeze({}), canonicalUrl: '/collections/users' }) });
  return createStudioPageRuntime(commandSnapshot, route, runtime, vi.fn(), fresh);
}

function collection(id: string) { const base = { kind: 'collection' as const, applicationId: 'sample.application', collectionId: id, installedCollectionChecksum: checksum };
  return Object.freeze({ ...base, authorityChecksum: studioOutwardResourceChecksum(base) }); }
function resolver(id: string): { id: string; kind: 'resolve'; owner: string; invoke(request: unknown): Promise<unknown> } {
  return { id: 'base.collection.resolve', kind: 'resolve', owner: 'base.collection.resolver', invoke: async () => ({ kind: 'resolved', resource: collection(id),
    route: { pageId: 'base.collection', parameters: { resource: id }, query: {} }, links: [] }) };
}
function linkResolver(id: string): { id: string; kind: 'resolve'; owner: string; invoke(request: unknown): Promise<unknown> } {
  return { id: 'base.collection.link', kind: 'resolve', owner: 'base.collection.linkResolver', invoke: async () => ({ kind: 'resolved', resource: collection(id),
    route: { pageId: 'base.collection', parameters: { resource: id }, query: {} }, links: [] }) };
}
function current(resource: ReturnType<typeof collection>) { const authority = { kind: 'graph', applicationGraphGeneration: '1', applicationGraphChecksum: checksum,
  studioOwnerGeneration: '1', studioOwnerChecksum: checksum, policyOwnerGeneration: '1', policyOwnerChecksum: checksum, authorityChecksum: checksum };
  authority.authorityChecksum = studioSha256(studioCanonicalHash('base.studio.observation-authority.graph.v1', writer => { writer.int64('1'); writer.checksum(checksum);
    writer.int64('1'); writer.checksum(checksum); writer.int64('1'); writer.checksum(checksum); }));
  return { kind: 'current', resource, observationAuthority: authority, value: { rows: [] }, links: [], evidence: [], accounting: {} }; }
function map(methods: Array<{ id: string; kind: 'page' | 'resolve' | 'preview' | 'execute' | 'receiptResolve'; owner: string; invoke(request: unknown, signal?: AbortSignal): Promise<unknown> }>): StudioRuntimeMethodMap {
  const values = new Map(methods.map(value => [value.id, Object.freeze({ kind: 'json' as const, binding: Object.freeze({ registeredMethodId: value.id,
    kind: value.kind, owningModuleId: 'base', owningPageOrCommandId: value.owner, endpointId: 'base.endpoint', requestTypeId: 'request',
    resultTypeId: 'result', bindingChecksum: checksum }), invoke: value.invoke })]));
  return Object.freeze({ graph: Object.freeze({}), methods: Object.freeze({ ids: Object.freeze([...values.keys()]), has: (id: string) => values.has(id), get: (id: string) => values.get(id) }) }) as StudioRuntimeMethodMap;
}
