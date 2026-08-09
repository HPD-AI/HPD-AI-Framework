import type {
  GatewayCapabilityCatalog,
  GatewayClient,
  GatewayDesiredProjection,
  GatewayEffectiveSnapshot,
  GatewayHostCapabilitySnapshotResponse,
  GatewayTargetStatusResponse,
  GatewayNamespaceId,
  GatewayTargetNodeId
} from '@hpd/gateway-client';
import type {
  StudioAuthenticationService,
  StudioAuthenticationSnapshot,
  StudioLifecycle
} from '@hpd-research/hpd-studio-core';

const encoder = new TextEncoder();
const contextTextMaximum = 128;
const refreshMilliseconds = 30_000;

export interface GatewayStudioContext {
  readonly namespaceId: string;
  readonly targetId: string;
}

export type GatewayStudioVerdict = 'Serving Ready' | 'Not Ready' | 'Serving Truth Unknown';
export type GatewayObservationState = 'value' | 'not-observed' | 'denied' | 'failed';

export interface GatewayLifecycleStage {
  readonly id: 'authored' | 'validated' | 'desired' | 'delivered' | 'active' | 'effective';
  readonly label: string;
  readonly state: string;
  readonly identity?: string;
  readonly source: 'Local' | 'Management' | 'Node' | 'Effective';
}

interface Observation<T> {
  readonly state: GatewayObservationState;
  readonly value?: T;
}

interface GatewayObservationBundle {
  readonly catalog: Observation<GatewayCapabilityCatalog>;
  readonly hostCapabilities: Observation<GatewayHostCapabilitySnapshotResponse>;
  readonly status: GatewayTargetStatusResponse;
  readonly desired: Observation<GatewayDesiredProjection>;
  readonly effective: Observation<GatewayEffectiveSnapshot>;
  readonly observedAt: string;
}

export interface GatewayStudioSnapshot {
  readonly authentication: StudioAuthenticationSnapshot;
  readonly draft: GatewayStudioContext;
  readonly context: GatewayStudioContext | null;
  readonly phase: 'signed-out' | 'context-required' | 'loading' | 'ready' | 'unavailable' | 'denied' | 'failed';
  readonly verdict: GatewayStudioVerdict;
  readonly lifecycle: readonly GatewayLifecycleStage[];
  readonly observation: GatewayObservationBundle | null;
  readonly refreshing: boolean;
  readonly stale: boolean;
  readonly lastSuccessfulAt: string | null;
  readonly failureCode: string | null;
}

export interface GatewayStudioController {
  snapshot(): GatewayStudioSnapshot;
  subscribe(listener: (snapshot: GatewayStudioSnapshot) => void): () => void;
  setDraft(context: GatewayStudioContext): void;
  selectDraft(): boolean;
  clearContext(): void;
  refresh(): Promise<void>;
  dispose(): void;
}

interface ControllerOptions {
  readonly client: GatewayClient;
  readonly authentication: StudioAuthenticationService;
  readonly lifecycle: StudioLifecycle;
  readonly now?: () => Date;
  readonly isVisible?: () => boolean;
}

export function createGatewayStudioController(options: ControllerOptions): GatewayStudioController {
  const now = options.now ?? (() => new Date());
  const isVisible = options.isVisible ?? (() => globalThis.document?.visibilityState !== 'hidden');
  const listeners = new Set<(value: GatewayStudioSnapshot) => void>();
  let authentication = sealAuthentication(options.authentication.snapshot());
  let draft: GatewayStudioContext = Object.freeze({ namespaceId: '', targetId: '' });
  let context: GatewayStudioContext | null = null;
  let phase: GatewayStudioSnapshot['phase'] = authentication.isAuthenticated ? 'context-required' : 'signed-out';
  let observation: GatewayObservationBundle | null = null;
  let refreshing = false;
  let stale = false;
  let lastSuccessfulAt: string | null = null;
  let failureCode: string | null = null;
  let activeController: AbortController | null = null;
  let activeRefresh: Promise<void> | null = null;
  let generation = 0;
  let disposed = false;

  const emit = () => {
    const value = project();
    for (const listener of listeners) listener(value);
  };

  const unsubscribeAuthentication = options.authentication.subscribe((nextValue) => {
    const next = sealAuthentication(nextValue);
    const principalChanged = authentication.isAuthenticated && next.isAuthenticated &&
      (authentication.subjectHint === undefined || next.subjectHint === undefined ||
        authentication.subjectHint !== next.subjectHint || authentication.displayName !== next.displayName);
    authentication = next;
    if (!next.isAuthenticated || principalChanged) {
      cancelGeneration();
      clearAuthorizedState();
      phase = next.isAuthenticated ? 'context-required' : 'signed-out';
      refreshing = false;
    } else if (context === null) {
      phase = 'context-required';
    } else {
      void refresh();
    }
    emit();
  });
  options.lifecycle.defer(unsubscribeAuthentication);
  options.lifecycle.defer(() => cancelGeneration());
  options.lifecycle.setInterval(() => {
    if (authentication.isAuthenticated && context !== null && isVisible()) void refresh();
  }, refreshMilliseconds);

  function project(): GatewayStudioSnapshot {
    return Object.freeze({
      authentication,
      draft,
      context,
      phase,
      verdict: deriveVerdict(phase, observation, stale),
      lifecycle: Object.freeze(deriveLifecycle(observation)),
      observation,
      refreshing,
      stale,
      lastSuccessfulAt,
      failureCode
    });
  }

  function cancelGeneration(): void {
    generation++;
    activeController?.abort();
    activeController = null;
    activeRefresh = null;
  }

  function clearAuthorizedState(): void {
    draft = Object.freeze({ namespaceId: '', targetId: '' });
    context = null;
    observation = null;
    stale = false;
    lastSuccessfulAt = null;
    failureCode = null;
  }

  function refresh(): Promise<void> {
    if (disposed || !authentication.isAuthenticated || context === null) return Promise.resolve();
    if (activeRefresh !== null) return activeRefresh;
    const selected = context;
    const currentGeneration = ++generation;
    const controller = new AbortController();
    activeController = controller;
    refreshing = true;
    if (observation === null) phase = 'loading';
    failureCode = null;
    const work = Promise.resolve().then(async () => {
      const path = { ns: selected.namespaceId as GatewayNamespaceId, target: selected.targetId as GatewayTargetNodeId };
      const [catalogResult, hostResult, statusResult, desiredResult, effectiveResult] = await Promise.all([
        options.client.capabilities({ path: {} }, { signal: controller.signal }),
        options.client['host-capabilities']({ path: {} }, { signal: controller.signal }),
        options.client.status({ path }, { signal: controller.signal }),
        options.client.desired({ path }, { signal: controller.signal }),
        options.client.effective({ path }, { signal: controller.signal })
      ]);
      if (disposed || controller.signal.aborted || currentGeneration !== generation) return;
      if (!statusResult.ok) {
        const failure = classifyFailure(statusResult);
        phase = failure.phase;
        failureCode = failure.code;
        stale = observation !== null;
        return;
      }
      const observedAt = now().toISOString();
      observation = cloneAndFreeze({
        catalog: observe(catalogResult),
        hostCapabilities: observe(hostResult),
        status: statusResult.value,
        desired: observe(desiredResult),
        effective: observe(effectiveResult),
        observedAt
      });
      phase = 'ready';
      stale = false;
      lastSuccessfulAt = observedAt;
      failureCode = null;
    }).catch(() => {
      if (disposed || controller.signal.aborted || currentGeneration !== generation) return;
      phase = 'failed';
      failureCode = 'gateway.studio.refreshFailed';
      stale = observation !== null;
    }).finally(() => {
      if (currentGeneration !== generation) return;
      activeController = null;
      activeRefresh = null;
      refreshing = false;
      emit();
    });
    activeRefresh = work;
    emit();
    return work;
  }

  const controller: GatewayStudioController = Object.freeze({
    snapshot: project,
    subscribe(listener: (snapshot: GatewayStudioSnapshot) => void) {
      if (disposed) return () => {};
      listeners.add(listener);
      listener(project());
      let active = true;
      return () => { if (active) { active = false; listeners.delete(listener); } };
    },
    setDraft(next: GatewayStudioContext) {
      if (disposed) return;
      draft = Object.freeze({ namespaceId: String(next.namespaceId), targetId: String(next.targetId) });
      emit();
    },
    selectDraft() {
      if (disposed || !validContextText(draft.namespaceId) || !validContextText(draft.targetId)) return false;
      cancelGeneration();
      context = Object.freeze({ ...draft });
      observation = null;
      stale = false;
      lastSuccessfulAt = null;
      failureCode = null;
      phase = authentication.isAuthenticated ? 'loading' : 'signed-out';
      emit();
      if (authentication.isAuthenticated) void refresh();
      return true;
    },
    clearContext() {
      if (disposed) return;
      cancelGeneration();
      context = null;
      observation = null;
      stale = false;
      lastSuccessfulAt = null;
      failureCode = null;
      phase = authentication.isAuthenticated ? 'context-required' : 'signed-out';
      emit();
    },
    refresh,
    dispose() {
      if (disposed) return;
      disposed = true;
      cancelGeneration();
      listeners.clear();
    }
  });
  return controller;
}

function observe<T>(result: { readonly ok: boolean; readonly value?: T; readonly kind?: string; readonly status?: number }): Observation<T> {
  if (result.ok) return Object.freeze({ state: 'value', value: cloneAndFreeze(result.value!) });
  if (result.kind === 'http' && result.status === 404) return Object.freeze({ state: 'not-observed' });
  if (result.kind === 'http' && (result.status === 401 || result.status === 403)) return Object.freeze({ state: 'denied' });
  return Object.freeze({ state: 'failed' });
}

function classifyFailure(result: { readonly kind?: string; readonly status?: number }): { phase: GatewayStudioSnapshot['phase']; code: string } {
  if (result.kind === 'http' && result.status === 404) return { phase: 'unavailable', code: 'gateway.studio.targetUnavailable' };
  if (result.kind === 'http' && (result.status === 401 || result.status === 403)) return { phase: 'denied', code: 'gateway.studio.accessDenied' };
  return { phase: 'failed', code: 'gateway.studio.refreshFailed' };
}

function deriveVerdict(phase: GatewayStudioSnapshot['phase'], bundle: GatewayObservationBundle | null, stale: boolean): GatewayStudioVerdict {
  if (phase !== 'ready' || bundle === null || stale) return 'Serving Truth Unknown';
  if (bundle.status.nodeObservation !== 'Observed') return 'Serving Truth Unknown';
  if (bundle.status.node.readiness.serving === 'NotReady') return 'Not Ready';
  return bundle.status.node.publication.state === 'PublicationIndeterminate' ? 'Serving Truth Unknown' : 'Serving Ready';
}

function deriveLifecycle(bundle: GatewayObservationBundle | null): GatewayLifecycleStage[] {
  const desired = bundle?.desired.state === 'value' ? bundle.desired.value : undefined;
  const status = bundle?.status;
  const effective = bundle?.effective.state === 'value' ? bundle.effective.value : undefined;
  const publication = status?.node.publication;
  const delivered = status === undefined ? 'Not observed' : status.nodeObservation === 'Observed' ? status.management.latestNodeOutcome : status.nodeObservation;
  return [
    { id: 'authored', label: 'Authored', state: 'Not started', source: 'Local' },
    { id: 'validated', label: 'Validated', state: 'Not validated', source: 'Local' },
    { id: 'desired', label: 'Desired', state: desired ? 'Observed' : 'Not observed', identity: desired?.revisionId, source: 'Management' },
    { id: 'delivered', label: 'Delivered', state: delivered ?? 'Not observed', identity: status?.management.latestNodeActivationIntentId ?? undefined, source: 'Management' },
    { id: 'active', label: 'Active', state: publication?.state ?? 'Not observed', identity: publication?.state === 'ActiveAcknowledged' ? publication.active.candidateId : undefined, source: 'Node' },
    { id: 'effective', label: 'Effective', state: effective ? 'Observed' : 'Not observed', identity: effective?.candidateId, source: 'Effective' }
  ].map((stage) => Object.freeze(stage)) as GatewayLifecycleStage[];
}

function sealAuthentication(value: StudioAuthenticationSnapshot): StudioAuthenticationSnapshot {
  return Object.freeze({
    isAuthenticated: value.isAuthenticated,
    ...(value.displayName === undefined ? {} : { displayName: value.displayName }),
    ...(value.subjectHint === undefined ? {} : { subjectHint: value.subjectHint })
  });
}

function cloneAndFreeze<T>(value: T): T {
  return deepFreeze(structuredClone(value));
}

function deepFreeze<T>(value: T): T {
  if (value === null || typeof value !== 'object' || Object.isFrozen(value)) return value;
  for (const child of Object.values(value)) deepFreeze(child);
  return Object.freeze(value);
}

function validContextText(value: string): boolean {
  if (typeof value !== 'string' || value.normalize('NFC') !== value || encoder.encode(value).byteLength < 1 || encoder.encode(value).byteLength > contextTextMaximum || /[\u0000-\u001f\u007f-\u009f]/u.test(value)) return false;
  for (let index = 0; index < value.length; index++) {
    const code = value.charCodeAt(index);
    if (code >= 0xd800 && code <= 0xdbff) { const next = value.charCodeAt(++index); if (!(next >= 0xdc00 && next <= 0xdfff)) return false; }
    else if (code >= 0xdc00 && code <= 0xdfff) return false;
  }
  return true;
}
