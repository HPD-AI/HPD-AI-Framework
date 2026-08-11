import { describe, expect, it, vi } from 'vitest';
import type { GatewayClient, GatewayTargetStatusResponse } from '@hpd/gateway-client';
import type { StudioAuthenticationService, StudioLifecycle } from '@hpd-research/hpd-studio-core';
import { createGatewayStudioController } from '../src/state.ts';

const ok = <T>(value: T) => ({ ok: true, status: 200, value, headers: {} });
const http = (status: number) => ({ ok: false, kind: 'http', status, error: { code: 'safe', title: 'Safe' }, headers: {} });
const transport = () => ({ ok: false, kind: 'transport', reason: 'network-failure' });

function status(serving: 'Ready' | 'NotReady' = 'Ready', publication = 'ActiveAcknowledged'): GatewayTargetStatusResponse {
  return {
    observedAt: '2026-08-08T00:00:00Z', isTruncated: false, nodeObservation: 'Observed',
    management: { authorityReady: true, code: 'Ready', durability: 'RestartDurable', indeterminateDeliveryCount: 0, latestNodeActivationIntentId: 'intent' as never, latestNodeOutcome: 'ActiveAcknowledged', nodeAttemptStarted: true, pendingDeliveryCount: 0, servingReadinessAffected: false },
    node: {
      conditions: [], detailsTruncated: false, generatedAt: '2026-08-08T00:00:00Z', processInstanceId: 'process', snapshotSequence: '1', upstreams: [],
      host: { desiredConfigurationHash: null, runningConfigurationHash: null, reasons: [], state: 'Ready', stamp: stamp() },
      intent: { state: 'NotManaged', stamp: stamp() }, preparation: { candidateId: 'candidate' as never, state: 'Prepared', stamp: stamp() },
      publication: { active: active(), attemptedCandidateId: 'candidate' as never, lastKnownGood: active(), reasons: [], state: publication as never, stamp: stamp() },
      readiness: { configuration: 'Ready', serving, reasons: [], stamp: stamp() }
    }
  };
}
function stamp() { return { authorityId: 'a', authorityKind: 'node', observationSequence: '1', observedAt: '2026-08-08T00:00:00Z', observedIdentity: null, processInstanceId: 'p' }; }
function active() { return { acknowledgedAt: '2026-08-08T00:00:00Z', applicationId: '0123456789abcdef0123456789abcdef', candidateId: 'candidate' as never, contentHash: 'hash', nativeRevisionId: 'native', symbolicPlanIdentity: { algorithm: 'sha-256', value: 'a'.repeat(64) } }; }

function client(overrides: Partial<Record<'status' | 'desired' | 'effective' | 'capabilities' | 'host-capabilities', unknown>> = {}) {
  const defaults = {
    capabilities: vi.fn(async () => ok({ apiVersion: 'v1', capabilities: [] })),
    'host-capabilities': vi.fn(async () => ok({ schemaVersion: '1', snapshotAlgorithm: 'sha-256', snapshotValue: 'snapshot', capabilities: {} })),
    status: vi.fn(async () => ok(status())),
    desired: vi.fn(async () => ok({ activationIntentId: 'intent', candidateId: 'candidate', desiredStateToken: 'token', namespaceId: 'namespace', observedAt: '2026-08-08T00:00:00Z', revisionId: 'revision', targetNodeId: 'node' })),
    effective: vi.fn(async () => ok({ candidateContentHash: {}, candidateId: 'candidate', isTruncated: false, records: [], schemaVersion: 1 }))
  };
  return Object.assign(defaults, overrides) as unknown as GatewayClient;
}

function authentication(initial: boolean, initialSubject?: string) {
  let value: { isAuthenticated: boolean; subjectHint?: string } = { isAuthenticated: initial, ...(initialSubject === undefined ? {} : { subjectHint: initialSubject }) };
  const listeners = new Set<(snapshot: { isAuthenticated: boolean; subjectHint?: string }) => void>();
  return {
    service: { snapshot: () => value, subscribe(listener) { listeners.add(listener); listener(value); return () => listeners.delete(listener); }, beginSignOut() { value = { isAuthenticated: false }; for (const listener of listeners) listener(value); } } satisfies StudioAuthenticationService,
    set(next: boolean, subjectHint?: string) { value = { isAuthenticated: next, ...(subjectHint === undefined ? {} : { subjectHint }) }; for (const listener of listeners) listener(value); }
  };
}

function lifecycle(): StudioLifecycle {
  const controller = new AbortController();
  const disposers: Array<() => void | Promise<void>> = [];
  return {
    signal: controller.signal, defer(dispose) { disposers.push(dispose); }, trackAbortController(value = new AbortController()) { disposers.push(() => value.abort()); return value; },
    setInterval() { return 1; }, listen() {},
    // test-only disposal is intentionally not exposed through the production interface
  };
}

describe('Gateway Studio state', () => {
  it('performs no discovery or Gateway call while signed out or without context', async () => {
    const auth = authentication(false); const gateway = client();
    const controller = createGatewayStudioController({ client: gateway, authentication: auth.service, lifecycle: lifecycle() });
    controller.setDraft({ namespaceId: 'namespace', targetId: 'node' });
    expect(controller.selectDraft()).toBe(true);
    await controller.refresh();
    expect(controller.snapshot().phase).toBe('signed-out');
    expect(gateway.status).not.toHaveBeenCalled();
  });

  it('observes one explicit target and derives the truthful lifecycle', async () => {
    const auth = authentication(true); const gateway = client();
    const controller = createGatewayStudioController({ client: gateway, authentication: auth.service, lifecycle: lifecycle(), now: () => new Date('2026-08-08T01:00:00Z') });
    controller.setDraft({ namespaceId: 'namespace', targetId: 'node' });
    expect(controller.selectDraft()).toBe(true);
    await controller.refresh();
    const snapshot = controller.snapshot();
    expect(snapshot.phase).toBe('ready');
    expect(snapshot.verdict).toBe('Serving Ready');
    expect(snapshot.lifecycle.map((stage) => stage.id)).toEqual(['authored', 'validated', 'desired', 'delivered', 'active', 'effective']);
    expect(snapshot.lifecycle.find((stage) => stage.id === 'active')).toMatchObject({ state: 'ActiveAcknowledged', identity: 'candidate' });
    expect(gateway.status).toHaveBeenCalledOnce();
  });

  it('preserves protected 404 language and never infers target absence', async () => {
    const controller = createGatewayStudioController({ client: client({ capabilities: vi.fn(async()=>ok({apiVersion:'v1',capabilities:['gateway.management.target.provision']})), status: vi.fn(async () => http(404)) }), authentication: authentication(true).service, lifecycle: lifecycle() });
    controller.setDraft({ namespaceId: 'namespace', targetId: 'node' }); controller.selectDraft(); await controller.refresh();
    expect(controller.snapshot()).toMatchObject({ phase: 'unavailable', verdict: 'Serving Truth Unknown', failureCode: 'gateway.studio.targetUnavailable' });
    expect(controller.snapshot().capabilities).toMatchObject({state:'value',value:{capabilities:['gateway.management.target.provision']}});
    expect(controller.snapshot().observation).toBeNull();
  });

  it('projects authorization denial and invalidates the authentication session on 401', async () => {
    const deniedAuth = authentication(true, 'principal');
    const denied = createGatewayStudioController({ client: client({ status: vi.fn(async () => http(403)) }), authentication: deniedAuth.service, lifecycle: lifecycle() });
    denied.setDraft({ namespaceId: 'namespace', targetId: 'node' }); denied.selectDraft(); await denied.refresh();
    expect(denied.snapshot()).toMatchObject({ phase: 'denied', authentication: { isAuthenticated: true }, context: { namespaceId: 'namespace', targetId: 'node' } });

    const expiredAuth = authentication(true, 'principal');
    const expired = createGatewayStudioController({ client: client({ status: vi.fn(async () => http(401)) }), authentication: expiredAuth.service, lifecycle: lifecycle() });
    expired.setDraft({ namespaceId: 'namespace', targetId: 'node' }); expired.selectDraft(); await expired.refresh();
    expect(expired.snapshot()).toMatchObject({ phase: 'signed-out', authentication: { isAuthenticated: false }, context: null, observation: null });
  });

  it('distinguishes not-ready and indeterminate serving truth', async () => {
    const notReady = createGatewayStudioController({ client: client({ status: vi.fn(async () => ok(status('NotReady'))) }), authentication: authentication(true).service, lifecycle: lifecycle() });
    notReady.setDraft({ namespaceId: 'namespace', targetId: 'node' }); notReady.selectDraft(); await notReady.refresh();
    expect(notReady.snapshot().verdict).toBe('Not Ready');

    const indeterminate = createGatewayStudioController({ client: client({ status: vi.fn(async () => ok(status('Ready', 'PublicationIndeterminate'))) }), authentication: authentication(true).service, lifecycle: lifecycle() });
    indeterminate.setDraft({ namespaceId: 'namespace', targetId: 'node' }); indeterminate.selectDraft(); await indeterminate.refresh();
    expect(indeterminate.snapshot().verdict).toBe('Serving Truth Unknown');
    expect(indeterminate.snapshot().lifecycle.find((stage) => stage.id === 'active')).toMatchObject({ state: 'PublicationIndeterminate', identity: undefined });
  });

  it('represents no desired and no effective state without inventing identity', async () => {
    const controller = createGatewayStudioController({ client: client({ desired: vi.fn(async () => http(404)), effective: vi.fn(async () => http(404)) }), authentication: authentication(true).service, lifecycle: lifecycle() });
    controller.setDraft({ namespaceId: 'namespace', targetId: 'node' }); controller.selectDraft(); await controller.refresh();
    expect(controller.snapshot().lifecycle.find((stage) => stage.id === 'desired')).toMatchObject({ state: 'Not observed', identity: undefined });
    expect(controller.snapshot().lifecycle.find((stage) => stage.id === 'effective')).toMatchObject({ state: 'Not observed', identity: undefined });
  });

  it('retains previous truth as stale when a later status refresh fails', async () => {
    const statusCall = vi.fn().mockResolvedValueOnce(ok(status())).mockResolvedValueOnce(transport()).mockResolvedValueOnce(ok(status('NotReady')));
    const controller = createGatewayStudioController({ client: client({ status: statusCall }), authentication: authentication(true).service, lifecycle: lifecycle() });
    controller.setDraft({ namespaceId: 'namespace', targetId: 'node' }); controller.selectDraft(); await controller.refresh();
    await controller.refresh();
    expect(controller.snapshot()).toMatchObject({ phase: 'failed', stale: true, verdict: 'Serving Truth Unknown' });
    expect(controller.snapshot().observation?.status.node?.readiness.serving).toBe('Ready');
    await controller.refresh();
    expect(controller.snapshot()).toMatchObject({ phase: 'ready', stale: false, verdict: 'Not Ready' });
  });

  it('joins concurrent refresh and rejects invalid or non-normalized context', async () => {
    let release!: (value: unknown) => void;
    const pending = new Promise((resolve) => release = resolve);
    const statusCall = vi.fn(async () => pending as never);
    const controller = createGatewayStudioController({ client: client({ status: statusCall }), authentication: authentication(true).service, lifecycle: lifecycle() });
    controller.setDraft({ namespaceId: 'e\u0301', targetId: 'node' }); expect(controller.selectDraft()).toBe(false);
    controller.setDraft({ namespaceId: 'namespace', targetId: 'node' }); expect(controller.selectDraft()).toBe(true);
    const first = controller.refresh(); const second = controller.refresh();
    await Promise.resolve();
    expect(statusCall).toHaveBeenCalledOnce();
    release(ok(status())); await Promise.all([first, second]);
    expect(controller.snapshot().phase).toBe('ready');
  });

  it('enforces exact UTF-8 context bounds and suppresses a replaced target generation', async () => {
    let release!: (value: unknown) => void;
    const pending = new Promise((resolve) => release = resolve);
    const statusCall = vi.fn().mockImplementationOnce(async () => pending as never).mockResolvedValue(ok(status('NotReady')));
    const controller = createGatewayStudioController({ client: client({ status: statusCall }), authentication: authentication(true).service, lifecycle: lifecycle() });
    controller.setDraft({ namespaceId: 'a'.repeat(128), targetId: 'node' }); expect(controller.selectDraft()).toBe(true);
    controller.setDraft({ namespaceId: 'a'.repeat(129), targetId: 'node' }); expect(controller.selectDraft()).toBe(false);
    controller.setDraft({ namespaceId: 'namespace', targetId: 'replacement' }); expect(controller.selectDraft()).toBe(true);
    await controller.refresh();
    release(ok(status()));
    await Promise.resolve();
    expect(controller.snapshot()).toMatchObject({ context: { targetId: 'replacement' }, verdict: 'Not Ready' });
  });

  it('removes authorized truth and target context on sign-out or principal replacement', async () => {
    const auth = authentication(true, 'principal-a');
    const controller = createGatewayStudioController({ client: client(), authentication: auth.service, lifecycle: lifecycle() });
    controller.setDraft({ namespaceId: 'namespace', targetId: 'node' }); controller.selectDraft(); await controller.refresh();
    expect(controller.snapshot().observation).not.toBeNull();

    auth.set(false);
    expect(controller.snapshot()).toMatchObject({ phase: 'signed-out', draft: { namespaceId: '', targetId: '' }, context: null, observation: null, stale: false, lastSuccessfulAt: null });

    auth.set(true, 'principal-a');
    controller.setDraft({ namespaceId: 'namespace', targetId: 'node' }); controller.selectDraft(); await controller.refresh();
    auth.set(true, 'principal-b');
    expect(controller.snapshot()).toMatchObject({ phase: 'context-required', draft: { namespaceId: '', targetId: '' }, context: null, observation: null, stale: false, lastSuccessfulAt: null });
  });

  it('admits one refresh before observable notification and joins subscriber reentrancy', async () => {
    const gateway = client();
    const controller = createGatewayStudioController({ client: gateway, authentication: authentication(true, 'principal').service, lifecycle: lifecycle() });
    let reentrant: Promise<void> | undefined;
    controller.subscribe((snapshot) => { if (snapshot.refreshing && reentrant === undefined) reentrant = controller.refresh(); });
    controller.setDraft({ namespaceId: 'namespace', targetId: 'node' });
    expect(controller.selectDraft()).toBe(true);
    const joined = controller.refresh();
    expect(reentrant).toBe(joined);
    await joined;
    for (const operation of ['capabilities', 'host-capabilities', 'status', 'desired', 'effective'] as const) expect(gateway[operation]).toHaveBeenCalledOnce();
  });

  it('publishes deeply immutable observations and lifecycle records', async () => {
    const controller = createGatewayStudioController({ client: client(), authentication: authentication(true, 'principal').service, lifecycle: lifecycle() });
    controller.setDraft({ namespaceId: 'namespace', targetId: 'node' }); controller.selectDraft(); await controller.refresh();
    const snapshot = controller.snapshot();
    expect(() => { (snapshot.observation!.status.management as { code: string }).code = 'corrupted'; }).toThrow(TypeError);
    expect(() => { (snapshot.observation!.desired.value as { candidateId: string }).candidateId = 'corrupted'; }).toThrow(TypeError);
    expect(() => { (snapshot.lifecycle[0] as { state: string }).state = 'corrupted'; }).toThrow(TypeError);
    expect(controller.snapshot().observation!.status.management.code).toBe('Ready');
    expect(controller.snapshot().observation!.desired.value?.candidateId).toBe('candidate');
    expect(controller.snapshot().lifecycle[0]!.state).toBe('Not started');
  });
});
