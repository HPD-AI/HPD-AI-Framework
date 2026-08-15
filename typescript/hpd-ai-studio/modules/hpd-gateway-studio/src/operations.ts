import type {
  GatewayAdministrativeOperationProjection,
  GatewayAuditProjection,
  GatewayClient,
  GatewayCorrelationId,
  GatewayContinuationToken,
  GatewayIdempotencyKey,
  GatewayNamespaceId,
  GatewayOperationId,
  GatewayTargetNodeId,
} from '@hpd/gateway-client';
import type { StudioAuthenticationService } from '@hpd-research/hpd-studio-core';
import type { GatewayManagedWorkflowController } from './managed-workflows.ts';
import type { GatewayStudioController } from './state.ts';

const pageMaximum = 64;
const retainedMaximum = 4_096;
const operationLifetimeMilliseconds = 300_000;

export type GatewayAdministrativeReview = Readonly<{
  phase: 'reviewing' | 'confirming' | 'executing' | 'accepted' | 'failed';
  kind: 'provision' | 'backup' | 'purge';
  reviewId: string;
  namespaceId: GatewayNamespaceId;
  targetNodeId: GatewayTargetNodeId | null;
  idempotencyKey: GatewayIdempotencyKey;
  correlationId: string;
  expectedStatus: 201 | 202;
  sinkName: string | null;
  artifactLabel: string | null;
  purgeCategory: 'RevisionContent' | 'ValidationContent' | 'ActivationOutcomeHistory' | 'AuditHistory' | null;
  resourceIds: readonly string[];
  confirmationPhrase: string;
  operationId: GatewayOperationId | null;
  operation: GatewayAdministrativeOperationProjection | null;
  code: string | null;
  duplicate: boolean;
  transportAmbiguous: boolean;
}>;

export interface GatewayOperationsSnapshot {
  readonly audit: readonly GatewayAuditProjection[];
  readonly auditHasMore: boolean;
  readonly auditLoading: boolean;
  readonly auditStale: boolean;
  readonly auditObservedAt: string | null;
  readonly review: GatewayAdministrativeReview | null;
}

export interface GatewayDiagnosticBundle {
  readonly bytes: Uint8Array;
  readonly filename: string;
}

export interface GatewayOperationsController {
  snapshot(): GatewayOperationsSnapshot;
  subscribe(listener: (snapshot: GatewayOperationsSnapshot) => void): () => void;
  loadAudit(reset?: boolean): Promise<boolean>;
  openProvisionReview(): boolean;
  openBackupReview(sinkName: string, artifactLabel?: string): boolean;
  openPurgeReview(category: NonNullable<GatewayAdministrativeReview['purgeCategory']>, resourceIds: readonly string[]): boolean;
  setConfirmationPhrase(value: string): void;
  requestConfirmation(): boolean;
  execute(): Promise<boolean>;
  retryExact(): Promise<boolean>;
  refreshAdministrativeOperation(): Promise<boolean>;
  closeReview(): void;
  createDiagnosticBundle(): GatewayDiagnosticBundle | null;
  dispose(): void;
}

export interface GatewayOperationsControllerOptions {
  readonly client: GatewayClient;
  readonly studio: GatewayStudioController;
  readonly managed: GatewayManagedWorkflowController;
  readonly authentication: StudioAuthenticationService;
  readonly now?: () => Date;
  readonly monotonicNow?: () => number;
  readonly randomValues?: (bytes: Uint8Array) => Uint8Array;
  readonly schedule?: (callback: () => void, milliseconds: number) => unknown;
  readonly cancelSchedule?: (handle: unknown) => void;
}

export function createGatewayOperationsController(options: GatewayOperationsControllerOptions): GatewayOperationsController {
  const now = options.now ?? (() => new Date());
  const monotonicNow = options.monotonicNow ?? (() => performance.now());
  const randomValues = options.randomValues ?? ((bytes: Uint8Array) => crypto.getRandomValues(bytes));
  const schedule = options.schedule ?? ((callback, milliseconds) => setTimeout(callback, milliseconds));
  const cancelSchedule = options.cancelSchedule ?? ((handle) => clearTimeout(handle as ReturnType<typeof setTimeout>));
  const listeners = new Set<(snapshot: GatewayOperationsSnapshot) => void>();
  let audit: readonly GatewayAuditProjection[] = Object.freeze([]);
  let auditCursor: GatewayContinuationToken | null = null;
  let auditHasMore = false;
  let auditLoading = false;
  let auditStale = false;
  let auditObservedAt: string | null = null;
  let review: GatewayAdministrativeReview | null = null;
  let request: AbortController | null = null;
  let tracking: AbortController | null = null;
  let trackingTimer: unknown = null;
  let trackingDeadlineTimer: unknown = null;
  let principalGeneration = 0;
  let contextGeneration = 0;
  let disposed = false;
  let priorAuthentication = options.authentication.snapshot();
  let priorContext = contextKey(options.studio.snapshot());

  const authenticationUnsubscribe = options.authentication.subscribe(next => {
    const changed = !next.isAuthenticated || !priorAuthentication.isAuthenticated ||
      next.subjectHint === undefined || priorAuthentication.subjectHint === undefined ||
      next.subjectHint !== priorAuthentication.subjectHint;
    priorAuthentication = next;
    if (changed) {
      principalGeneration++;
      clearAll();
      emit();
    }
  });
  const studioUnsubscribe = options.studio.subscribe(next => {
    const key = contextKey(next);
    if (key !== priorContext) {
      priorContext = key;
      contextGeneration++;
      clearTarget();
      emit();
    }
  });

  function project(): GatewayOperationsSnapshot {
    return Object.freeze({ audit, auditHasMore, auditLoading, auditStale, auditObservedAt, review });
  }
  function emit(): void { const value = project(); for (const listener of listeners) listener(value); }
  function currentContext(): { ns: GatewayNamespaceId; target: GatewayTargetNodeId } | null {
    const value = options.studio.snapshot();
    return value.authentication.isAuthenticated && value.context !== null
      ? { ns: value.context.namespaceId as GatewayNamespaceId, target: value.context.targetId as GatewayTargetNodeId }
      : null;
  }
  function fence(): { principal: number; context: number } { return { principal: principalGeneration, context: contextGeneration }; }
  function live(value: { principal: number; context: number }, signal?: AbortSignal): boolean {
    return !disposed && !signal?.aborted && value.principal === principalGeneration && value.context === contextGeneration;
  }
  function clearAll(): void { clearTarget(); audit = Object.freeze([]); auditCursor = null; auditHasMore = false; auditStale = false; auditObservedAt = null; }
  function clearTarget(): void { request?.abort(); request = null; stopTracking(); review = null; }
  function stopTracking(): void {
    tracking?.abort(); tracking = null;
    if (trackingTimer !== null) cancelSchedule(trackingTimer);
    if (trackingDeadlineTimer !== null) cancelSchedule(trackingDeadlineTimer);
    trackingTimer = null; trackingDeadlineTimer = null;
  }

  async function loadAudit(reset = false): Promise<boolean> {
    const context = currentContext();
    if (context === null || auditLoading || (!reset && audit.length > 0 && !auditHasMore)) return false;
    const captured = fence();
    const controller = new AbortController();
    request?.abort(); request = controller; auditLoading = true; emit();
    const result = await options.client.audit({ path: { ns: context.ns }, query: {
      maximum: pageMaximum,
      ...(!reset && auditCursor !== null ? { cursor: auditCursor } : {}),
    } }, { signal: controller.signal });
    if (!live(captured, controller.signal)) return false;
    request = null; auditLoading = false;
    if (!result.ok) { auditStale = audit.length > 0; emit(); return false; }
    const existing = reset ? [] : [...audit];
    const ids = new Set(existing.map(value => value.auditId));
    for (const item of result.value.items) {
      if (ids.has(item.auditId) || existing.length >= retainedMaximum) { auditStale = true; emit(); return false; }
      ids.add(item.auditId); existing.push(deepFreeze(structuredClone(item)));
    }
    if (result.value.hasMore && (result.value.continuationToken === null || result.value.continuationToken === auditCursor)) {
      auditStale = true; emit(); return false;
    }
    audit = Object.freeze(existing); auditCursor = result.value.continuationToken;
    auditHasMore = result.value.hasMore; auditStale = false; auditObservedAt = now().toISOString(); emit(); return true;
  }

  function baseReview(kind: GatewayAdministrativeReview['kind']): GatewayAdministrativeReview | null {
    const context = currentContext(); if (context === null) return null;
    const bytes = randomValues(new Uint8Array(32));
    const identity = base64Url(bytes) as GatewayIdempotencyKey;
    return deepFreeze({ phase: 'reviewing', kind, reviewId: base64Url(randomValues(new Uint8Array(16))),
      namespaceId: context.ns, targetNodeId: kind === 'provision' ? context.target : null,
      idempotencyKey: identity, correlationId: `gateway-studio-${identity}`,
      expectedStatus: kind === 'provision' ? 201 : 202, sinkName: null, artifactLabel: null,
      purgeCategory: null, resourceIds: Object.freeze([]), confirmationPhrase: '', operationId: null, operation: null,
      code: null, duplicate: false, transportAmbiguous: false } satisfies GatewayAdministrativeReview);
  }
  function openProvisionReview(): boolean {
    const capability = options.studio.snapshot().capabilities;
    if (review !== null || capability.state !== 'value' ||
      !capability.value?.capabilities.includes('gateway.management.target.provision')) return false;
    review = baseReview('provision'); emit(); return review !== null;
  }
  function openBackupReview(sinkName: string, artifactLabel?: string): boolean {
    if (review !== null || !validSink(sinkName) || artifactLabel !== undefined && !validLabel(artifactLabel)) return false;
    const value = baseReview('backup'); if (value === null) return false;
    review = deepFreeze({ ...value, sinkName, artifactLabel: artifactLabel ?? null }); emit(); return true;
  }
  function openPurgeReview(category: NonNullable<GatewayAdministrativeReview['purgeCategory']>, resourceIds: readonly string[]): boolean {
    if (review !== null || !['RevisionContent','ValidationContent','ActivationOutcomeHistory','AuditHistory'].includes(category)) return false;
    const values = [...resourceIds];
    if (values.length < 1 || values.length > 256 || values.some(value => !validResource(value)) ||
      values.some((value,index) => index > 0 && value <= values[index - 1]!)) return false;
    const value = baseReview('purge'); if (value === null) return false;
    review = deepFreeze({ ...value, purgeCategory: category, resourceIds: Object.freeze(values) }); emit(); return true;
  }
  function setConfirmationPhrase(value: string): void {
    if (review === null || review.kind !== 'purge' || review.phase !== 'reviewing') return;
    review = deepFreeze({ ...review, confirmationPhrase: value }); emit();
  }
  function requestConfirmation(): boolean {
    if (review === null || review.phase !== 'reviewing' ||
      review.kind === 'purge' && review.confirmationPhrase !== review.namespaceId) return false;
    review = deepFreeze({ ...review, phase: 'confirming' }); emit(); return true;
  }

  async function execute(): Promise<boolean> {
    if (review === null || review.phase !== 'confirming') return false;
    return invoke(review);
  }
  async function retryExact(): Promise<boolean> {
    if (review === null || review.phase !== 'failed' || !review.transportAmbiguous) return false;
    return invoke(review);
  }
  async function invoke(frozen: GatewayAdministrativeReview): Promise<boolean> {
    const captured = fence(); const controller = new AbortController(); request?.abort(); request = controller;
    review = deepFreeze({ ...frozen, phase: 'executing', transportAmbiguous: false }); emit();
    const headers = { idempotencyKey: frozen.idempotencyKey, correlationId: frozen.correlationId as GatewayCorrelationId };
    if (frozen.kind === 'provision') {
      const result = await options.client.provision({ path: { ns: frozen.namespaceId, target: frozen.targetNodeId! }, headers }, { signal: controller.signal });
      if (!live(captured, controller.signal) || review?.reviewId !== frozen.reviewId) return false;
      request = null;
      if (!result.ok) { fail(frozen, result); return false; }
      if (result.value.operationId.length === 0) { failSchema(frozen); return false; }
      review = deepFreeze({ ...frozen, phase: 'accepted', operationId: result.value.operationId,
        code: 'management.target.provisioned', duplicate: result.value.duplicate }); emit(); return true;
    }
    const result = frozen.kind === 'backup'
      ? await options.client.backup({ path: { ns: frozen.namespaceId }, headers,
          body: { sinkName: frozen.sinkName!, artifactLabel: frozen.artifactLabel } }, { signal: controller.signal })
      : await options.client.purge({ path: { ns: frozen.namespaceId }, headers,
          body: { category: frozen.purgeCategory!, resourceIds: frozen.resourceIds } }, { signal: controller.signal });
    if (!live(captured, controller.signal) || review?.reviewId !== frozen.reviewId) return false;
    request = null;
    if (!result.ok) {
      fail(frozen, result); return false;
    }
    if (result.value.operationId.length === 0) { review = deepFreeze({ ...frozen, phase: 'failed', code: 'schema-mismatch', transportAmbiguous: false }); emit(); return false; }
    const expected = frozen.kind === 'backup' ? 'Backup' : 'Purge';
    review = deepFreeze({ ...frozen, phase: 'accepted', operationId: result.value.operationId,
      code: result.value.code, operation: deepFreeze({
      kind: 'administration', operationId: result.value.operationId, operation: expected,
      state: result.value.state, code: result.value.code, artifactReference: result.value.artifactReference,
      observedAt: null,
    } as GatewayAdministrativeOperationProjection), duplicate: false }); emit();
    if (result.value.state !== 'Completed' && result.value.state !== 'Failed') startTracking(frozen.reviewId, result.value.operationId, expected);
    return true;
  }
  function fail(frozen: GatewayAdministrativeReview, result: { readonly kind: string; readonly reason?: string; readonly status?: number }): void {
    review = deepFreeze({ ...frozen, phase: 'failed', code: result.kind === 'transport' ? result.reason ?? 'transport' : result.kind === 'http' ? `http-${result.status}` : result.kind,
      transportAmbiguous: result.kind === 'transport' }); emit();
  }
  function failSchema(frozen: GatewayAdministrativeReview): void { review = deepFreeze({ ...frozen, phase: 'failed', code: 'schema-mismatch', transportAmbiguous: false }); emit(); }

  async function refreshAdministrativeOperation(): Promise<boolean> {
    if (review?.operation === null || review?.operation === undefined) return false;
    return readOperation(review.reviewId, review.operation.operationId, review.operation.operation);
  }
  async function readOperation(reviewId: string, operationId: GatewayOperationId, expected: 'Backup'|'Purge', signal?: AbortSignal): Promise<boolean> {
    const current = review;
    if (current === null || current.reviewId !== reviewId) return false;
    const namespaceId = current.namespaceId;
    const captured = fence();
    const result = await options.client.operation({ path: { ns: namespaceId, operation: operationId } }, { signal });
    if (!live(captured, signal) || review?.reviewId !== reviewId || !result.ok || result.value.kind !== 'administration' ||
      result.value.operationId !== operationId || result.value.operation !== expected) return false;
    review = deepFreeze({ ...review, operation: deepFreeze(structuredClone(result.value)), code: result.value.code }); emit();
    return result.value.state === 'Completed' || result.value.state === 'Failed';
  }
  function startTracking(reviewId: string, operationId: GatewayOperationId, expected: 'Backup'|'Purge'): void {
    stopTracking(); const controller = new AbortController(); tracking = controller; const deadline = monotonicNow() + operationLifetimeMilliseconds;
    trackingDeadlineTimer = schedule(() => {
      if (tracking === controller) stopTracking();
    }, operationLifetimeMilliseconds);
    const tick = async () => {
      if (controller.signal.aborted || review?.reviewId !== reviewId || monotonicNow() >= deadline) { stopTracking(); return; }
      const terminal = await readOperation(reviewId, operationId, expected, controller.signal);
      if (terminal || controller.signal.aborted) { stopTracking(); return; }
      trackingTimer = schedule(() => { void tick(); }, Math.min(30_000, Math.max(0, deadline - monotonicNow())));
    };
    trackingTimer = schedule(() => { void tick(); }, 30_000);
  }
  function closeReview(): void { if (review?.phase === 'executing') return; stopTracking(); review = null; emit(); }

  function createDiagnosticBundle(): GatewayDiagnosticBundle | null {
    try { return buildDiagnosticBundle(); }
    catch { return null; }
  }

  function buildDiagnosticBundle(): GatewayDiagnosticBundle | null {
    const studio = options.studio.snapshot(); const managed = options.managed.snapshot();
    const context = studio.context; const observation = studio.observation;
    if (context === null || observation === null) return null;
    const effective = observation.effective;
    const allRecords=effective.state==='value'&&effective.value
      ? effective.value.routes.flatMap(route=>route.contributions):[];
    const allAdmissionPlans=effective.state==='value'&&effective.value
      ? effective.value.routes.filter(route=>route.trafficAdmission!=null).map(route=>({routeId:route.routeId,trafficAdmission:route.trafficAdmission!})):[];
    const allUpstreams=effective.state==='value'&&effective.value?[...effective.value.upstreams]:[];
    const consideredRecords=[...allRecords.slice(0,512)],consideredAdmissionPlans=[...allAdmissionPlans.slice(0,512)],consideredUpstreams=[...allUpstreams.slice(0,512)],consideredIntents=[...managed.activationIntents.slice(0,256)],consideredOutcomes=[...managed.activationOutcomes.slice(0,256)];
    let recordCount=consideredRecords.length,admissionPlanCount=consideredAdmissionPlans.length,upstreamCount=consideredUpstreams.length,intentCount=consideredIntents.length,outcomeCount=consideredOutcomes.length;
    const build=()=>({
      context: { namespaceId: context.namespaceId, targetNodeId: context.targetId },
      exportVersion: '1', generatedAt: now().toISOString(), kind: 'hpd-gateway-studio-diagnostic-observation',
      observations: {
        desired: diagnosticObservation(observation.desired),
        effective: effective.state === 'value' && effective.value ? { state:'value', value: {
          applicationId: effective.value.applicationId, appliedAt: effective.value.appliedAt,
          candidateContentHash: effective.value.candidateContentHash, candidateId: effective.value.candidateId,
          admissionPlans: consideredAdmissionPlans.slice(0,admissionPlanCount), isTruncated: effective.value.isTruncated,
          records: consideredRecords.slice(0,recordCount), schemaVersion: effective.value.schemaVersion,
          symbolicPlanIdentity: effective.value.symbolicPlanIdentity, upstreams: consideredUpstreams.slice(0,upstreamCount),
        }} : { state: effective.state, value: null },
        status: { state:'value', value: observation.status },
      },
      retainedActivations: { observedAt: managed.activationObservedAt, stale: managed.stale,
        intents: consideredIntents.slice(0,intentCount), outcomes: consideredOutcomes.slice(0,outcomeCount) },
      source: { lastSuccessfulAt: studio.lastSuccessfulAt, stale: studio.stale },
      truncation: { statusSourceTruncated: observation.status.isTruncated,
        effectiveSourceTruncated: effective.state === 'value' ? Boolean(effective.value?.isTruncated) : false,
        effectiveRecordsOmittedLocally: Math.max(0,allRecords.length-recordCount),
        admissionPlansOmittedLocally: Math.max(0,allAdmissionPlans.length-admissionPlanCount),
        appliedUpstreamsOmittedLocally: Math.max(0,allUpstreams.length-upstreamCount),
        activationIntentsOmittedLocally: Math.max(0,managed.activationIntents.length-intentCount),
        activationOutcomesOmittedLocally: Math.max(0,managed.activationOutcomes.length-outcomeCount),
        activationIntentsMayHaveMoreAtSource: managed.intentsHaveMore,
        activationOutcomesMayHaveMoreAtSource: managed.outcomesHaveMore },
    });
    const size=()=>new TextEncoder().encode(canonicalJson(build())).length;
    const saved=[recordCount,admissionPlanCount,upstreamCount,intentCount,outcomeCount];recordCount=0;admissionPlanCount=0;upstreamCount=0;intentCount=0;outcomeCount=0;
    if(size()>262_144)return null;[recordCount,admissionPlanCount,upstreamCount,intentCount,outcomeCount]=saved;
    if(size()>1_048_576){intentCount=largestFittingPrefix(intentCount,value=>{intentCount=value;return size()<=1_048_576;});}
    if(size()>1_048_576){outcomeCount=largestFittingPrefix(outcomeCount,value=>{outcomeCount=value;return size()<=1_048_576;});}
    if(size()>1_048_576){recordCount=largestFittingPrefix(recordCount,value=>{recordCount=value;return size()<=1_048_576;});}
    if(size()>1_048_576){admissionPlanCount=largestFittingPrefix(admissionPlanCount,value=>{admissionPlanCount=value;return size()<=1_048_576;});}
    if(size()>1_048_576){upstreamCount=largestFittingPrefix(upstreamCount,value=>{upstreamCount=value;return size()<=1_048_576;});}
    const bytes = new TextEncoder().encode(canonicalJson(build()));
    if (bytes.length > 1_048_576) return null;
    return Object.freeze({ bytes, filename: `hpd-gateway-diagnostic-${basicUtc(now())}.json` });
  }

  return Object.freeze({ snapshot: project, subscribe(listener: (snapshot: GatewayOperationsSnapshot) => void) { if (disposed) return () => {}; listeners.add(listener); listener(project()); return () => listeners.delete(listener); },
    loadAudit, openProvisionReview, openBackupReview, openPurgeReview, setConfirmationPhrase, requestConfirmation,
    execute, retryExact, refreshAdministrativeOperation, closeReview, createDiagnosticBundle,
    dispose() { if (disposed) return; disposed = true; clearAll(); authenticationUnsubscribe(); studioUnsubscribe(); listeners.clear(); } });
}

function contextKey(value: ReturnType<GatewayStudioController['snapshot']>): string { return value.context === null ? '' : `${value.context.namespaceId}\0${value.context.targetId}`; }
function base64Url(value: Uint8Array): string { let binary=''; for(const byte of value)binary+=String.fromCharCode(byte); return btoa(binary).replace(/\+/g,'-').replace(/\//g,'_').replace(/=+$/,''); }
function validSink(value: string): boolean { return /^[a-z0-9.-]{1,128}$/.test(value); }
function validLabel(value: string): boolean { return /^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$/.test(value); }
function validResource(value: string): boolean { return value.length > 0 && new TextEncoder().encode(value).length <= 128 && value.normalize('NFC') === value && !/\p{Cc}/u.test(value); }
function diagnosticObservation(value: { readonly state: string; readonly value?: unknown }): unknown { return value.state === 'value' ? { state:'value', value:value.value } : { state:value.state, value:null }; }
function canonicalJson(value: unknown): string {if(value===null)return'null';if(typeof value==='string'){wellFormed(value);return JSON.stringify(value);}if(typeof value==='boolean')return value?'true':'false';if(typeof value==='number'){if(!Number.isSafeInteger(value))throw new TypeError('Diagnostic numbers must be safe integers.');return String(value);}if(Array.isArray(value))return`[${value.map(canonicalJson).join(',')}]`;if(typeof value!=='object'||Object.getPrototypeOf(value)!==Object.prototype)throw new TypeError('Diagnostic value is outside the closed contract.');const record=value as Record<string,unknown>;return`{${Object.keys(record).sort(ordinal).map(key=>{wellFormed(key);return`${JSON.stringify(key)}:${canonicalJson(record[key])}`;}).join(',')}}`;}
function wellFormed(value:string):void{for(let index=0;index<value.length;index++){const code=value.charCodeAt(index);if(code>=0xd800&&code<=0xdbff){if(index+1>=value.length){throw new TypeError('Malformed Unicode.');}const next=value.charCodeAt(++index);if(next<0xdc00||next>0xdfff)throw new TypeError('Malformed Unicode.');}else if(code>=0xdc00&&code<=0xdfff)throw new TypeError('Malformed Unicode.');}}
function ordinal(left:string,right:string):number{
  const leftScalars=Array.from(left,character=>character.codePointAt(0)!);
  const rightScalars=Array.from(right,character=>character.codePointAt(0)!);
  const length=Math.min(leftScalars.length,rightScalars.length);
  for(let index=0;index<length;index++){
    if(leftScalars[index]!<rightScalars[index]!)return-1;
    if(leftScalars[index]!>rightScalars[index]!)return 1;
  }
  return leftScalars.length-rightScalars.length;
}
function basicUtc(value:Date):string{return value.toISOString().replace(/[-:]/g,'').replace(/\.\d{3}Z$/,'Z');}
function largestFittingPrefix(maximum:number,fits:(value:number)=>boolean):number{if(!fits(0))return 0;let low=0,high=maximum;while(low<high){const middle=Math.ceil((low+high)/2);if(fits(middle))low=middle;else high=middle-1;}return low;}
function deepFreeze<T>(value:T):T{if(value!==null&&typeof value==='object'&&!Object.isFrozen(value)){Object.freeze(value);for(const child of Object.values(value as Record<string,unknown>))deepFreeze(child);}return value;}
