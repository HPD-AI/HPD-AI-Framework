import type {
  GatewayActivationHistoryResponse, GatewayActivationIntentId, GatewayActivationProjection,
  GatewayClient, GatewayContinuationToken, GatewayCorrelationId, GatewayDesiredPrecondition, GatewayDesiredProjection,
  GatewayDesiredStateToken, GatewayExportResponse, GatewayIdempotencyKey, GatewayNamespaceId,
  GatewayOperationId, GatewayOperationProjection, GatewayOutcomeProjection, GatewayRevisionComparison,
  GatewayRevisionId, GatewayRevisionProjection, GatewayRevisionResponse, GatewayTargetNodeId
} from '@hpd/gateway-client';
import type { StudioAuthenticationService } from '@hpd-research/hpd-studio-core';
import type { GatewayDeclarationController } from './declaration-state.ts';
import type { GatewayStudioController, GatewayStudioSnapshot } from './state.ts';
import { parseGatewayJson } from './authored-json.ts';
import { validateGatewaySchema } from './schema-validation.ts';
import { diffGatewayDocuments } from './declaration-projections.ts';

const encoder = new TextEncoder();
const maximumConfigurationBytes = 4_194_304;
const maximumRetainedRows = 4_096;
const pageMaximum = 64;
const maximumResults = 64;
const rapidTrackingMilliseconds = 60_000;
const maximumTrackingMilliseconds = 300_000;
const rapidTrackingIntervalMilliseconds = 2_000;
const ordinaryTrackingIntervalMilliseconds = 30_000;

export type GatewayMutationKind = 'submit' | 'submit-and-activate' | 'import' | 'import-and-activate' | 'activate' | 'rollback';
export type GatewayWorkflowPhase = 'Closed' | 'Reviewing' | 'Confirming' | 'Executing' | 'Accepted' | 'Failed' | 'Stale';
type GatewayActivationsResult = Awaited<ReturnType<GatewayClient['activations']>>;

export interface GatewayWorkflowResult {
  readonly kind: 'success' | 'http' | 'protocol' | 'transport' | 'canceled';
  readonly status: number | null;
  readonly code: string;
  readonly correlationId: string | null;
  readonly operationId: GatewayOperationId | null;
  readonly revisionId: GatewayRevisionId | null;
  readonly activationIntentId: GatewayActivationIntentId | null;
  readonly desiredStateToken: GatewayDesiredStateToken | null;
  readonly duplicate: boolean;
}

export interface GatewayWorkflowReview {
  readonly phase: GatewayWorkflowPhase;
  readonly kind: GatewayMutationKind;
  readonly workflowId: string;
  readonly namespaceId: GatewayNamespaceId;
  readonly targetNodeId: GatewayTargetNodeId;
  readonly revisionId: GatewayRevisionId | null;
  readonly description: string | null;
  readonly configurationJson: string | null;
  readonly sourceId: string | null;
  readonly idempotencyKey: GatewayIdempotencyKey;
  readonly commandCorrelationId: GatewayCorrelationId;
  readonly desiredPrecondition: GatewayDesiredPrecondition | null;
  readonly baseEditGeneration: bigint | null;
  readonly baseContextGeneration: number;
  readonly basePrincipalGeneration: number;
  readonly baseSourceSha256: string | null;
  readonly baseValidationTransportSha256: string | null;
  readonly baseCapabilitySnapshotIdentity: string | null;
  readonly baseDesiredRevisionId: string | null;
  readonly baseDesiredCandidateId: string | null;
  readonly baseDesiredActivationIntentId: string | null;
  readonly proposedIdentity: string;
  readonly semanticDiffSource: string;
  readonly semanticDifferenceCount: number;
  readonly semanticDiffTruncated: number;
  readonly expectedSuccessStatus: 201 | 202;
  readonly result: GatewayWorkflowResult | null;
}

export interface GatewayManagedWorkflowSnapshot {
  readonly context: Readonly<{ namespaceId: string; targetNodeId: string }> | null;
  readonly selectedRevision: GatewayRevisionProjection | null;
  readonly selectedRevisionState: 'None' | 'Loading' | 'Loaded' | 'Stale' | 'Unavailable' | 'Failed';
  readonly revisions: readonly GatewayRevisionProjection[];
  readonly revisionCursor: GatewayContinuationToken | null;
  readonly revisionsHaveMore: boolean;
  readonly activationIntents: readonly GatewayActivationProjection[];
  readonly activationOutcomes: readonly GatewayOutcomeProjection[];
  readonly activationObservedAt: string | null;
  readonly intentCursor: GatewayContinuationToken | null;
  readonly outcomeCursor: GatewayContinuationToken | null;
  readonly intentsHaveMore: boolean;
  readonly outcomesHaveMore: boolean;
  readonly comparison: GatewayRevisionComparison | null;
  readonly exported: GatewayExportResponse | null;
  readonly workflow: GatewayWorkflowReview | null;
  readonly results: readonly GatewayWorkflowResult[];
  readonly operation: GatewayOperationProjection | null;
  readonly stale: boolean;
}

export interface GatewayManagedWorkflowController {
  snapshot(): GatewayManagedWorkflowSnapshot;
  subscribe(listener: (snapshot: GatewayManagedWorkflowSnapshot) => void): () => void;
  loadRevisions(more?: boolean): Promise<boolean>;
  selectRevision(revisionId: GatewayRevisionId): Promise<boolean>;
  compare(left: GatewayRevisionId, right: GatewayRevisionId): Promise<boolean>;
  exportRevision(revisionId: GatewayRevisionId): Promise<boolean>;
  openExportAsAuthored(): boolean;
  loadActivationHistory(): Promise<boolean>;
  openSubmit(kind: 'submit' | 'submit-and-activate', description?: string): boolean;
  openImport(kind: 'import' | 'import-and-activate', configurationJson: string, sourceId: string, description?: string): boolean;
  openActivation(kind: 'activate' | 'rollback', revisionId: GatewayRevisionId, description?: string): boolean;
  requestConfirmation(): boolean;
  execute(): Promise<boolean>;
  retryExact(): Promise<boolean>;
  cancelExecution(): boolean;
  refreshOperation(): Promise<boolean>;
  closeWorkflow(): void;
  dispose(): void;
}

interface Options {
  readonly client: GatewayClient;
  readonly studio: GatewayStudioController;
  readonly declaration: GatewayDeclarationController;
  readonly authentication: StudioAuthenticationService;
  readonly randomValues?: (buffer: Uint8Array) => Uint8Array;
  readonly setTimeout?: (callback: () => void, milliseconds: number) => ReturnType<typeof globalThis.setTimeout>;
  readonly clearTimeout?: (handle: ReturnType<typeof globalThis.setTimeout>) => void;
  readonly monotonicNow?: () => number;
}

interface TrackingFence {
  readonly generation: number;
  readonly workflowId: string;
  readonly kind: GatewayMutationKind;
  readonly operationId: GatewayOperationId;
  readonly desiredStateToken: GatewayDesiredStateToken;
  readonly activationIntentId: GatewayActivationIntentId;
  readonly deadline: number;
  readonly signal: AbortSignal;
}

export function createGatewayManagedWorkflowController(options: Options): GatewayManagedWorkflowController {
  const randomValues = options.randomValues ?? ((buffer: Uint8Array) => {
    crypto.getRandomValues(buffer as Uint8Array<ArrayBuffer>);
    return buffer;
  });
  const listeners = new Set<(snapshot: GatewayManagedWorkflowSnapshot) => void>();
  const requests = new Set<AbortController>();
  const resourceRequests = new Map<string,AbortController>();
  let studio = options.studio.snapshot();
  let declaration = options.declaration.snapshot();
  let principalGeneration = 0;
  let contextGeneration = 0;
  let disposed = false;
  let selectedRevision: GatewayRevisionProjection | null = null;
  let selectedRevisionState: GatewayManagedWorkflowSnapshot['selectedRevisionState'] = 'None';
  let revisions: readonly GatewayRevisionProjection[] = Object.freeze([]);
  let revisionCursor: GatewayContinuationToken | null = null;
  let revisionsHaveMore = false;
  let activationIntents: readonly GatewayActivationProjection[] = Object.freeze([]);
  let activationOutcomes: readonly GatewayOutcomeProjection[] = Object.freeze([]);
  let activationObservedAt: string | null = null;
  let intentCursor: GatewayContinuationToken | null = null;
  let outcomeCursor: GatewayContinuationToken | null = null;
  let intentsHaveMore = false;
  let outcomesHaveMore = false;
  let comparison: GatewayRevisionComparison | null = null;
  let exported: GatewayExportResponse | null = null;
  let workflow: GatewayWorkflowReview | null = null;
  let results: readonly GatewayWorkflowResult[] = Object.freeze([]);
  let operation: GatewayOperationProjection | null = null;
  let stale = false;
  let trackingTimer:ReturnType<typeof globalThis.setTimeout>|null=null;
  let trackingStartedAt=0;
  let trackingDeadline=0;
  let trackingDelayMilliseconds=rapidTrackingIntervalMilliseconds;
  let trackingGeneration=0;
  let trackingBusy=false;
  let trackingDeadlineTimer:ReturnType<typeof globalThis.setTimeout>|null=null;
  let trackingAbort:AbortController|null=null;
  let activeMutation:AbortController|null=null;
  const schedule=options.setTimeout??globalThis.setTimeout.bind(globalThis);
  const unschedule=options.clearTimeout??globalThis.clearTimeout.bind(globalThis);
  const monotonicNow=options.monotonicNow??(()=>performance.now());

  const contextKey = (value: GatewayStudioSnapshot) => value.context === null ? '' : `${value.context.namespaceId}\0${value.context.targetId}`;
  let priorContext = contextKey(studio);
  let priorPrincipal = options.authentication.snapshot();
  const studioUnsubscribe = options.studio.subscribe(next => {
    const nextKey = contextKey(next);
    studio = next;
    if (nextKey !== priorContext) { priorContext = nextKey; contextGeneration++; resetRemote(); }
    else markWorkflowStale();
  });
  const declarationUnsubscribe = options.declaration.subscribe(next => { declaration = next; markWorkflowStale(); });
  const authenticationUnsubscribe = options.authentication.subscribe(next => {
    const changed = !next.isAuthenticated || !priorPrincipal.isAuthenticated || next.subjectHint === undefined || priorPrincipal.subjectHint === undefined || next.subjectHint !== priorPrincipal.subjectHint;
    priorPrincipal = next;
    if (changed) { principalGeneration++; contextGeneration++; resetRemote(); }
  });

  function project(): GatewayManagedWorkflowSnapshot {
    const context = studio.context === null ? null : Object.freeze({ namespaceId: studio.context.namespaceId, targetNodeId: studio.context.targetId });
    return Object.freeze({ context, selectedRevision, selectedRevisionState, revisions, revisionCursor, revisionsHaveMore,
      activationIntents, activationOutcomes, activationObservedAt, intentCursor, outcomeCursor, intentsHaveMore, outcomesHaveMore,
      comparison, exported, workflow, results, operation, stale });
  }
  function emit(): void { const value = project(); for (const listener of listeners) listener(value); }
  function abortRequests(): void { for (const request of requests) request.abort(); requests.clear(); resourceRequests.clear(); }
  function resetRemote(): void {
    stopTracking();abortRequests(); selectedRevision=null; selectedRevisionState='None'; revisions=Object.freeze([]); revisionCursor=null; revisionsHaveMore=false;
    activationIntents=Object.freeze([]); activationOutcomes=Object.freeze([]); activationObservedAt=null; intentCursor=null; outcomeCursor=null; intentsHaveMore=false; outcomesHaveMore=false;
    comparison=null; exported=null; workflow=null; operation=null;results=Object.freeze([]); stale=false; emit();
  }
  function markWorkflowStale(): void {
    if (workflow === null || workflow.phase === 'Executing' || workflow.phase === 'Accepted' || workflow.phase === 'Failed') return;
    if (!workflowCurrent(workflow)) { workflow=freeze({ ...workflow, phase:'Stale' }); stale=true; emit(); }
  }
  function workflowCurrent(value: GatewayWorkflowReview): boolean {
    if (value.baseContextGeneration !== contextGeneration || value.basePrincipalGeneration !== principalGeneration) return false;
    const current = studio.context;
    if (current === null || current.namespaceId !== value.namespaceId || current.targetId !== value.targetNodeId) return false;
    if (value.baseEditGeneration !== null && (declaration.document.editGeneration !== value.baseEditGeneration || declaration.document.sourceSha256 !== value.baseSourceSha256)) return false;
    const currentCapability = capabilityIdentity(studio);
    if (value.baseCapabilitySnapshotIdentity !== null && value.baseCapabilitySnapshotIdentity !== currentCapability) return false;
    if (value.desiredPrecondition !== null) {
      const desired=desiredTruth(studio,true);if(desired===null)return false;
      if(value.desiredPrecondition.kind==='create-only'&&desired.kind!=='absent')return false;
      if(value.desiredPrecondition.kind==='replace'&&(desired.kind!=='present'||desired.value.desiredStateToken!==value.desiredPrecondition.token))return false;
    }
    return !(value.kind==='activate'||value.kind==='rollback') || selectedRevision?.revisionId===value.revisionId;
  }
  function capture(): { ns: GatewayNamespaceId; target: GatewayTargetNodeId; contextGeneration: number; principalGeneration: number } | null {
    if (disposed || !studio.authentication.isAuthenticated || studio.context === null) return null;
    return { ns: studio.context.namespaceId as GatewayNamespaceId, target: studio.context.targetId as GatewayTargetNodeId, contextGeneration, principalGeneration };
  }
  function live(captured: { contextGeneration: number; principalGeneration: number }, abort: AbortController): boolean {
    return !disposed && !abort.signal.aborted && captured.contextGeneration === contextGeneration && captured.principalGeneration === principalGeneration;
  }
  async function request<T>(resource:string,work: (signal: AbortSignal) => Promise<T>,lifetime?:AbortSignal): Promise<{ value: T; abort: AbortController } | null> {
    resourceRequests.get(resource)?.abort();const abort=new AbortController();resourceRequests.set(resource,abort);requests.add(abort);
    const cancel=()=>abort.abort();if(lifetime?.aborted)abort.abort();else lifetime?.addEventListener('abort',cancel,{once:true});
    try { const observed=await settleRequestOnAbort(work(abort.signal),abort.signal);return observed.completed?{value:observed.value,abort}:null; } catch { return null; } finally { lifetime?.removeEventListener('abort',cancel);requests.delete(abort);if(resourceRequests.get(resource)===abort)resourceRequests.delete(resource); }
  }

  async function loadRevisions(more=false): Promise<boolean> {
    const captured=capture(); if(captured===null)return false;
    const cursor=more?revisionCursor:null; if(more&&(!revisionsHaveMore||cursor===null))return false;
    const outcome=await request('revisions',signal=>options.client.revisions({path:{ns:captured.ns,target:captured.target},query:{maximum:pageMaximum,...(cursor===null?{}:{cursor})}},{signal}));
    if(outcome===null||!live(captured,outcome.abort)||!outcome.value.ok)return false;
    const page=outcome.value.value; if(more&&page.continuationToken===cursor&&page.hasMore)return false;
    const combined=more?[...revisions,...page.items]:[...page.items];
    if(combined.length>maximumRetainedRows||new Set(combined.map(item=>item.revisionId)).size!==combined.length)return false;
    revisions=freeze(combined);revisionCursor=page.continuationToken;revisionsHaveMore=page.hasMore;emit();return true;
  }
  async function selectRevisionById(revisionId:GatewayRevisionId):Promise<boolean>{const captured=capture();if(captured===null)return false;selectedRevisionState='Loading';emit();const outcome=await request('revision',signal=>options.client.revision({path:{ns:captured.ns,target:captured.target,revision:revisionId}},{signal}));if(outcome===null||!live(captured,outcome.abort))return false;if(!outcome.value.ok||outcome.value.value.revisionId!==revisionId){selectedRevision=null;selectedRevisionState=outcome.value.ok?'Failed':outcome.value.kind==='http'&&outcome.value.status===404?'Unavailable':'Failed';emit();return false;}selectedRevision=freeze(outcome.value.value);selectedRevisionState='Loaded';emit();return true;}
  async function compare(left:GatewayRevisionId,right:GatewayRevisionId):Promise<boolean>{const captured=capture();if(captured===null)return false;const outcome=await request('comparison',signal=>options.client.compare({path:{ns:captured.ns,target:captured.target},body:{leftRevisionId:left,rightRevisionId:right}},{signal}));if(outcome===null||!live(captured,outcome.abort)||!outcome.value.ok||outcome.value.value.leftRevisionId!==left||outcome.value.value.rightRevisionId!==right)return false;comparison=freeze(outcome.value.value);emit();return true;}
  async function exportRevision(revisionId:GatewayRevisionId):Promise<boolean>{const captured=capture();if(captured===null)return false;const outcome=await request('export',signal=>options.client.export({path:{ns:captured.ns,target:captured.target,revision:revisionId}},{signal}));if(outcome===null||!live(captured,outcome.abort)||!outcome.value.ok||outcome.value.value.revisionId!==revisionId)return false;exported=freeze(outcome.value.value);emit();return true;}
  async function loadActivationHistory():Promise<boolean>{const captured=capture();if(captured===null)return false;const first=activationIntents.length===0&&activationOutcomes.length===0;const advanceIntents=first||intentsHaveMore,advanceOutcomes=first||outcomesHaveMore;if(!advanceIntents&&!advanceOutcomes)return false;const outcome=await request('activations',signal=>options.client.activations({path:{ns:captured.ns,target:captured.target},query:{maximum:pageMaximum,...(advanceIntents&&intentCursor!==null?{intentCursor}:{}),...(advanceOutcomes&&outcomeCursor!==null?{outcomeCursor}:{})}},{signal}));if(outcome===null||!live(captured,outcome.abort)||!outcome.value.ok)return false;return applyHistory(outcome.value.value,advanceIntents,advanceOutcomes);}
  function applyHistory(value:GatewayActivationHistoryResponse,advanceIntents:boolean,advanceOutcomes:boolean):boolean{
    const nextIntents=advanceIntents?[...activationIntents,...value.intents.items]:[...activationIntents],nextOutcomes=advanceOutcomes?[...activationOutcomes,...value.outcomes.items]:[...activationOutcomes];
    if(nextIntents.length>maximumRetainedRows||nextOutcomes.length>maximumRetainedRows||new Set(nextIntents.map(v=>v.intentId)).size!==nextIntents.length||new Set(nextOutcomes.map(v=>v.outcomeId)).size!==nextOutcomes.length)return false;
    if(advanceIntents&&value.intents.hasMore&&value.intents.continuationToken===intentCursor||advanceOutcomes&&value.outcomes.hasMore&&value.outcomes.continuationToken===outcomeCursor)return false;
    activationIntents=freeze(nextIntents);activationOutcomes=freeze(nextOutcomes);activationObservedAt=new Date().toISOString();if(advanceIntents){intentCursor=value.intents.continuationToken;intentsHaveMore=value.intents.hasMore;}if(advanceOutcomes){outcomeCursor=value.outcomes.continuationToken;outcomesHaveMore=value.outcomes.hasMore;}emit();return true;
  }

  function open(kind:GatewayMutationKind, values:{revisionId?:GatewayRevisionId;configurationJson?:string;sourceId?:string;description?:string}):boolean{
    const captured=capture();if(captured===null||workflow?.phase==='Executing')return false;
    const configurationJson=values.configurationJson??null;if(configurationJson!==null&&encoder.encode(configurationJson).byteLength>maximumConfigurationBytes)return false;
    if((kind==='import'||kind==='import-and-activate')&&(configurationJson===null||!validImported(configurationJson)||!validResource(values.sourceId??'')))return false;
    if((kind==='activate'||kind==='rollback')&&(values.revisionId===undefined||selectedRevision?.revisionId!==values.revisionId))return false;
    const description=normalizedDescription(values.description);if(description===undefined)return false;
    const capability=capabilityIdentity(studio);if(capability===null)return false;
    const authored=kind==='submit'||kind==='submit-and-activate';
    if(authored){const doc=declaration.document,validation=doc.validation,currentCapability=capabilityIdentity(studio);if(doc.state!=='ServerValid'||validation===null||!validation.isValid||validation.editGeneration!==doc.editGeneration||validation.sourceSha256!==doc.sourceSha256||currentCapability===null||`${validation.hostCapabilitySnapshotAlgorithm}:${validation.hostCapabilitySnapshotValue}`!==currentCapability)return false;}
    const activates=kind==='submit-and-activate'||kind==='import-and-activate'||kind==='activate'||kind==='rollback';
    const desired=desiredTruth(studio);if(activates&&desired===null)return false;
    const desiredPrecondition:GatewayDesiredPrecondition|null=activates?(desired!.kind==='absent'?{kind:'create-only'}:{kind:'replace',token:desired!.value.desiredStateToken}):null;
    const doc=declaration.document;
    const diff=mutationDiff(kind,configurationJson,values.revisionId??null,options.declaration,comparison);const desiredValue=desired?.kind==='present'?desired.value:null;
    workflow=freeze({phase:'Reviewing',kind,workflowId:hex(randomValues(new Uint8Array(16))),namespaceId:captured.ns,targetNodeId:captured.target,
      revisionId:values.revisionId??null,description,configurationJson:authored?doc.utf8Text:configurationJson,sourceId:authored?'studio':values.sourceId??null,
      idempotencyKey:base64url(randomValues(new Uint8Array(32))) as GatewayIdempotencyKey,commandCorrelationId:`studio-${hex(randomValues(new Uint8Array(16)))}` as GatewayCorrelationId,
      desiredPrecondition,baseEditGeneration:authored?doc.editGeneration:null,baseSourceSha256:authored?doc.sourceSha256:null,
      baseContextGeneration:captured.contextGeneration,basePrincipalGeneration:captured.principalGeneration,
      baseValidationTransportSha256:authored?doc.validation!.validationTransportSha256:null,baseCapabilitySnapshotIdentity:capability,
      baseDesiredRevisionId:desiredValue?.revisionId??null,baseDesiredCandidateId:desiredValue?.candidateId??null,baseDesiredActivationIntentId:desiredValue?.activationIntentId??null,
      proposedIdentity:values.revisionId??(authored?doc.sourceSha256:values.sourceId??'Imported candidate'),semanticDiffSource:diff.source,semanticDifferenceCount:diff.count,semanticDiffTruncated:diff.truncated,
      expectedSuccessStatus:kind==='submit'||kind==='import'?201:202,result:null});stale=false;emit();return true;
  }
  async function execute():Promise<boolean>{if(workflow===null||workflow.phase!=='Confirming'||!workflowCurrent(workflow))return false;return invoke(workflow);}
  async function invoke(capturedWorkflow:GatewayWorkflowReview):Promise<boolean>{
    workflow=freeze({...capturedWorkflow,phase:'Executing'});emit();const abort=new AbortController();activeMutation=abort;requests.add(abort);let result:any;
    try{const path={ns:capturedWorkflow.namespaceId,target:capturedWorkflow.targetNodeId};const headers={idempotencyKey:capturedWorkflow.idempotencyKey,correlationId:capturedWorkflow.commandCorrelationId};const description=capturedWorkflow.description;
      switch(capturedWorkflow.kind){
        case'submit':result=await options.client.submit({path,headers,body:{configurationJson:capturedWorkflow.configurationJson!,sourceKind:'studio',sourceId:capturedWorkflow.sourceId!,description}},{signal:abort.signal});break;
        case'submit-and-activate':result=await options.client['submit-and-activate']({path,headers:{...headers,desiredPrecondition:capturedWorkflow.desiredPrecondition!},body:{configurationJson:capturedWorkflow.configurationJson!,sourceKind:'studio',sourceId:capturedWorkflow.sourceId!,description}},{signal:abort.signal});break;
        case'import':result=await options.client.import({path,headers,body:{configurationJson:capturedWorkflow.configurationJson!,sourceId:capturedWorkflow.sourceId!,description}},{signal:abort.signal});break;
        case'import-and-activate':result=await options.client['import-and-activate']({path,headers:{...headers,desiredPrecondition:capturedWorkflow.desiredPrecondition!},body:{configurationJson:capturedWorkflow.configurationJson!,sourceId:capturedWorkflow.sourceId!,description}},{signal:abort.signal});break;
        case'activate':result=await options.client.activate({path:{...path,revision:capturedWorkflow.revisionId!},headers:{...headers,desiredPrecondition:capturedWorkflow.desiredPrecondition!},body:{description}},{signal:abort.signal});break;
        case'rollback':result=await options.client.rollback({path:{...path,revision:capturedWorkflow.revisionId!},headers:{...headers,desiredPrecondition:capturedWorkflow.desiredPrecondition!},body:{description}},{signal:abort.signal});break;
      }
    }catch{result=abort.signal.aborted?{ok:false,kind:'canceled',reason:'caller-canceled'}:{ok:false,kind:'transport',reason:'network-failure'};}finally{requests.delete(abort);if(activeMutation===abort)activeMutation=null;}
    if(disposed||workflow?.workflowId!==capturedWorkflow.workflowId)return false;if(result.ok&&!mutationResultMatches(capturedWorkflow,result.value)){result={ok:false,kind:'protocol',reason:'schema-mismatch'};}const projected=projectResult(result);results=freeze([projected,...results].slice(0,maximumResults));workflow=freeze({...capturedWorkflow,phase:result.ok?'Accepted':'Failed',result:projected});emit();if(result.ok&&projected.activationIntentId!==null)startTracking(projected.activationIntentId);return result.ok;
  }
  async function refreshOperation():Promise<boolean>{return (await readOperation()).accepted;}

  async function readOperation(fence?:TrackingFence):Promise<{readonly accepted:boolean;readonly rateLimited:boolean}>{
    const captured=capture();const capturedWorkflow=workflow;const result=capturedWorkflow?.result;const id=result?.operationId;
    if(captured===null||result==null||id===null||id===undefined)return{accepted:false,rateLimited:false};
    const workflowId=capturedWorkflow!.workflowId,kind=capturedWorkflow!.kind,desiredStateToken=result.desiredStateToken;
    if(fence!==undefined&&(fence.workflowId!==workflowId||fence.kind!==kind||fence.operationId!==id||fence.desiredStateToken!==desiredStateToken))return{accepted:false,rateLimited:false};
    if(fence!==undefined&&monotonicNow()>=fence.deadline)return{accepted:false,rateLimited:false};
    const outcome=await request('operation',signal=>options.client.operation({path:{ns:captured.ns,operation:id}},{signal}),fence?.signal);
    if(outcome===null||!live(captured,outcome.abort)||!workflowFenceCurrent(workflowId,kind,id,desiredStateToken,fence?.generation)||fence!==undefined&&monotonicNow()>=fence.deadline)return{accepted:false,rateLimited:false};
    if(!outcome.value.ok)return{accepted:false,rateLimited:isRateLimited(outcome.value)};
    const value=outcome.value.value;
    if(value.kind!=='command'||value.operationId!==id||value.operation!==expectedReceiptOperation(kind)||value.desiredStateToken!==desiredStateToken)
      return{accepted:false,rateLimited:false};
    if(!workflowFenceCurrent(workflowId,kind,id,desiredStateToken,fence?.generation))return{accepted:false,rateLimited:false};
    operation=freeze(value);emit();return{accepted:true,rateLimited:false};
  }

  async function readTrackingHistory(fence:TrackingFence):Promise<{readonly accepted:boolean;readonly rateLimited:boolean;readonly exhausted:boolean}>{
    const captured=capture();if(captured===null)return{accepted:false,rateLimited:false,exhausted:false};
    let nextIntentCursor:GatewayContinuationToken|null=null,nextOutcomeCursor:GatewayContinuationToken|null=null,advanceIntents=true,advanceOutcomes=true;
    const nextIntents:GatewayActivationProjection[]=[],nextOutcomes:GatewayOutcomeProjection[]=[];
    while(advanceIntents||advanceOutcomes){
      if(!trackingFenceCurrent(fence)||monotonicNow()>=fence.deadline)return{accepted:false,rateLimited:false,exhausted:true};
      const requestedIntentCursor:GatewayContinuationToken|null=nextIntentCursor,requestedOutcomeCursor:GatewayContinuationToken|null=nextOutcomeCursor;
      const response=await request<GatewayActivationsResult>('activations',(signal:AbortSignal)=>options.client.activations({path:{ns:captured.ns,target:captured.target},query:{maximum:pageMaximum,...(advanceIntents&&requestedIntentCursor!==null?{intentCursor:requestedIntentCursor}:{}),...(advanceOutcomes&&requestedOutcomeCursor!==null?{outcomeCursor:requestedOutcomeCursor}:{})}},{signal}),fence.signal);
      if(response===null||!live(captured,response.abort)||!trackingFenceCurrent(fence)||monotonicNow()>=fence.deadline)return{accepted:false,rateLimited:false,exhausted:monotonicNow()>=fence.deadline};
      if(!response.value.ok)return{accepted:false,rateLimited:isRateLimited(response.value),exhausted:false};
      const value:GatewayActivationHistoryResponse=response.value.value;
      if(advanceIntents){
        if(nextIntents.length+value.intents.items.length>maximumRetainedRows)return{accepted:false,rateLimited:false,exhausted:true};
        if(value.intents.hasMore&&(value.intents.continuationToken===null||value.intents.continuationToken===requestedIntentCursor))return{accepted:false,rateLimited:false,exhausted:false};
        nextIntents.push(...value.intents.items);if(value.intents.hasMore&&nextIntents.length===maximumRetainedRows)return{accepted:false,rateLimited:false,exhausted:true};nextIntentCursor=value.intents.continuationToken;advanceIntents=value.intents.hasMore;
      }
      if(advanceOutcomes){
        if(nextOutcomes.length+value.outcomes.items.length>maximumRetainedRows)return{accepted:false,rateLimited:false,exhausted:true};
        if(value.outcomes.hasMore&&(value.outcomes.continuationToken===null||value.outcomes.continuationToken===requestedOutcomeCursor))return{accepted:false,rateLimited:false,exhausted:false};
        nextOutcomes.push(...value.outcomes.items);if(value.outcomes.hasMore&&nextOutcomes.length===maximumRetainedRows)return{accepted:false,rateLimited:false,exhausted:true};nextOutcomeCursor=value.outcomes.continuationToken;advanceOutcomes=value.outcomes.hasMore;
      }
      if(nextOutcomes.some(value=>value.activationIntentId===fence.activationIntentId))break;
    }
    if(new Set(nextIntents.map(item=>item.intentId)).size!==nextIntents.length||new Set(nextOutcomes.map(item=>item.outcomeId)).size!==nextOutcomes.length||!trackingFenceCurrent(fence))return{accepted:false,rateLimited:false,exhausted:false};
    activationIntents=freeze(nextIntents);activationOutcomes=freeze(nextOutcomes);
    intentCursor=nextIntentCursor;outcomeCursor=nextOutcomeCursor;intentsHaveMore=advanceIntents;outcomesHaveMore=advanceOutcomes;emit();
    return{accepted:true,rateLimited:false,exhausted:false};
  }

  function workflowFenceCurrent(workflowId:string,kind:GatewayMutationKind,operationId:GatewayOperationId,desiredStateToken:GatewayDesiredStateToken|null,generation?:number):boolean{
    const current=workflow;return!disposed&&(generation===undefined||generation===trackingGeneration)&&current?.workflowId===workflowId&&current.kind===kind&&current.result?.operationId===operationId&&current.result.desiredStateToken===desiredStateToken;
  }
  function trackingFenceCurrent(fence:TrackingFence):boolean{return!fence.signal.aborted&&workflowFenceCurrent(fence.workflowId,fence.kind,fence.operationId,fence.desiredStateToken,fence.generation)&&workflow?.result?.activationIntentId===fence.activationIntentId;}

  function stopTracking():void{trackingGeneration++;if(trackingTimer!==null){unschedule(trackingTimer);trackingTimer=null;}if(trackingDeadlineTimer!==null){unschedule(trackingDeadlineTimer);trackingDeadlineTimer=null;}trackingAbort?.abort();trackingAbort=null;trackingStartedAt=0;trackingDeadline=0;trackingDelayMilliseconds=rapidTrackingIntervalMilliseconds;trackingBusy=false;}
  function startTracking(intent:GatewayActivationIntentId):void{
    stopTracking();activationIntents=Object.freeze([]);activationOutcomes=Object.freeze([]);activationObservedAt=null;intentCursor=null;outcomeCursor=null;intentsHaveMore=false;outcomesHaveMore=false;
    const generation=trackingGeneration;trackingStartedAt=monotonicNow();trackingDeadline=trackingStartedAt+maximumTrackingMilliseconds;trackingAbort=new AbortController();
    const current=workflow,result=current?.result;
    if(current===null||current===undefined||result===null||result===undefined||result.operationId===null||result.desiredStateToken===null||result.activationIntentId!==intent){stopTracking();return;}
    const fence:TrackingFence=Object.freeze({generation,workflowId:current.workflowId,kind:current.kind,operationId:result.operationId,desiredStateToken:result.desiredStateToken,activationIntentId:intent,deadline:trackingDeadline,signal:trackingAbort.signal});
    const scheduleNext=(delay:number):void=>{if(disposed||generation!==trackingGeneration)return;const remaining=trackingDeadline-monotonicNow();if(remaining<=0){stopTracking();return;}const bounded=Math.min(delay,remaining);trackingDelayMilliseconds=bounded;trackingTimer=schedule(()=>void tick(),bounded);};
    const tick=async()=>{if(disposed||generation!==trackingGeneration||monotonicNow()>=trackingDeadline){stopTracking();return;}trackingTimer=null;if(trackingBusy){scheduleNext(baseTrackingInterval());return;}trackingBusy=true;
      let rateLimited=false;
      try{const [,history,receipt]=await Promise.all([options.studio.refresh(fence.signal),readTrackingHistory(fence),readOperation(fence)]);rateLimited=history.rateLimited||receipt.rateLimited;if(history.exhausted||(trackingFenceCurrent(fence)&&activationOutcomes.some(value=>value.activationIntentId===intent))){stopTracking();return;}}
      finally{trackingBusy=false;}
      if(disposed||generation!==trackingGeneration)return;if(monotonicNow()>=trackingDeadline){stopTracking();return;}
      const base=baseTrackingInterval();const elapsed=monotonicNow()-trackingStartedAt,jitter=(Math.floor(elapsed/rapidTrackingIntervalMilliseconds)*137)%501;
      scheduleNext(rateLimited?Math.min(ordinaryTrackingIntervalMilliseconds,Math.max(base,trackingDelayMilliseconds*2)+jitter):base);
    };
    const baseTrackingInterval=():number=>monotonicNow()-trackingStartedAt<rapidTrackingMilliseconds?rapidTrackingIntervalMilliseconds:ordinaryTrackingIntervalMilliseconds;
    scheduleNext(rapidTrackingIntervalMilliseconds);
    trackingDeadlineTimer=schedule(()=>{if(generation===trackingGeneration)stopTracking();},maximumTrackingMilliseconds);
  }
  const controller:GatewayManagedWorkflowController=Object.freeze({snapshot:project,subscribe(listener:(snapshot:GatewayManagedWorkflowSnapshot)=>void){if(disposed)return()=>{};listeners.add(listener);listener(project());let active=true;return()=>{if(active){active=false;listeners.delete(listener);}};},loadRevisions,selectRevision:selectRevisionById,compare,exportRevision,openExportAsAuthored(){if(exported===null||disposed)return false;const value=exported.configurationJson;resetRemote();options.declaration.replaceRaw(value);return true;},loadActivationHistory,openSubmit(kind:'submit'|'submit-and-activate',description?:string){return open(kind,{description});},openImport(kind:'import'|'import-and-activate',configurationJson:string,sourceId:string,description?:string){return open(kind,{configurationJson,sourceId,description});},openActivation(kind:'activate'|'rollback',revisionId:GatewayRevisionId,description?:string){return open(kind,{revisionId,description});},requestConfirmation(){if(workflow===null||workflow.phase!=='Reviewing'||!workflowCurrent(workflow))return false;workflow=freeze({...workflow,phase:'Confirming'});emit();return true;},execute,retryExact(){if(workflow===null||workflow.phase!=='Failed'||workflow.result?.kind!=='transport')return Promise.resolve(false);return invoke(workflow);},cancelExecution(){if(workflow?.phase!=='Executing'||activeMutation===null)return false;activeMutation.abort();return true;},refreshOperation,closeWorkflow(){if(workflow?.phase==='Executing')return;stopTracking();workflow=null;operation=null;activationIntents=Object.freeze([]);activationOutcomes=Object.freeze([]);activationObservedAt=null;intentCursor=null;outcomeCursor=null;intentsHaveMore=false;outcomesHaveMore=false;stale=false;emit();},dispose(){if(disposed)return;disposed=true;stopTracking();abortRequests();studioUnsubscribe();declarationUnsubscribe();authenticationUnsubscribe();listeners.clear();}});
  return controller;
}

function capabilityIdentity(value:GatewayStudioSnapshot):string|null{const host=value.observation?.hostCapabilities;return host?.state==='value'&&host.value!==undefined?`${host.value.snapshotAlgorithm}:${host.value.snapshotValue}`:null;}
function desiredTruth(value:GatewayStudioSnapshot,retainDuringRefresh=false):{readonly kind:'present';readonly value:GatewayDesiredProjection}|{readonly kind:'absent'}|null{if(value.phase!=='ready'||value.stale||!retainDuringRefresh&&value.refreshing||value.observation===null)return null;const desired=value.observation.desired;if(desired.state==='value'&&desired.value!==undefined)return{kind:'present',value:desired.value};return desired.state==='not-observed'?{kind:'absent'}:null;}
function normalizedDescription(value:string|undefined):string|null|undefined{if(value===undefined||value.length===0)return null;return value.length<=1_024?value:undefined;}
function validResource(value:string):boolean{return value.length>0&&value.normalize('NFC')===value&&encoder.encode(value).byteLength<=128&&![...value].some(character=>/\p{Cc}/u.test(character));}
function validImported(value:string):boolean{const parsed=parseGatewayJson(value);return parsed.ok&&validateGatewaySchema(parsed.graph).diagnostics.length===0;}
function base64url(value:Uint8Array):string{let binary='';for(const byte of value)binary+=String.fromCharCode(byte);return btoa(binary).replaceAll('+','-').replaceAll('/','_').replace(/=+$/u,'');}
function hex(value:Uint8Array):string{return [...value].map(byte=>byte.toString(16).padStart(2,'0')).join('');}
function projectResult(value:any):GatewayWorkflowResult{if(value.ok){const body=value.value as GatewayRevisionResponse;return freeze({kind:'success',status:value.status,code:value.status===202?'Accepted delivery':'Accepted revision',correlationId:value.correlationId??null,operationId:body.operationId,revisionId:body.revisionId,activationIntentId:body.activationIntentId,desiredStateToken:body.desiredStateToken,duplicate:body.duplicate});}if(value.kind==='http')return freeze({kind:'http',status:value.status,code:value.error.code,correlationId:value.correlationId??null,operationId:null,revisionId:null,activationIntentId:null,desiredStateToken:null,duplicate:false});return freeze({kind:value.kind,status:null,code:value.kind==='transport'?'Outcome not observed':value.reason,correlationId:value.correlationId??null,operationId:null,revisionId:null,activationIntentId:null,desiredStateToken:null,duplicate:false});}
function mutationResultMatches(workflow:GatewayWorkflowReview,value:GatewayRevisionResponse):boolean{return (workflow.kind!=='activate'&&workflow.kind!=='rollback')||value.revisionId===workflow.revisionId;}
function expectedReceiptOperation(kind:GatewayMutationKind):string{switch(kind){case'submit':case'import':return'submit-only';case'submit-and-activate':case'import-and-activate':return'submit-and-activate';case'activate':return'activate-existing';case'rollback':return'rollback';}}
function isRateLimited(value:any):boolean{return value?.kind==='http'&&value.status===429;}
function settleRequestOnAbort<T>(work:Promise<T>,signal:AbortSignal):Promise<{readonly completed:true;readonly value:T}|{readonly completed:false}>{if(signal.aborted)return Promise.resolve({completed:false});return new Promise((resolve,reject)=>{const canceled=()=>{resolve({completed:false});};const complete=()=>signal.removeEventListener('abort',canceled);signal.addEventListener('abort',canceled,{once:true});void work.then(value=>{complete();resolve({completed:true,value});},error=>{complete();reject(error);});});}
function mutationDiff(kind:GatewayMutationKind,configurationJson:string|null,revisionId:GatewayRevisionId|null,declaration:GatewayDeclarationController,comparison:GatewayRevisionComparison|null):{source:string;count:number;truncated:number}{const snapshot=declaration.snapshot();if(kind==='submit'||kind==='submit-and-activate'){if(snapshot.baseline===null||snapshot.document.compatibleGraph===null)return{source:'No local authored baseline',count:0,truncated:0};const value=diffGatewayDocuments(snapshot.document.compatibleGraph,snapshot.baseline.graph);return{source:'Local authored comparison',count:value.differences.length,truncated:value.truncatedCount};}if(kind==='import'||kind==='import-and-activate'){const parsed=configurationJson===null?null:parseGatewayJson(configurationJson);if(parsed===null||!parsed.ok||snapshot.document.compatibleGraph===null)return{source:'Imported preview without local comparison',count:0,truncated:0};const value=diffGatewayDocuments(parsed.graph,snapshot.document.compatibleGraph);return{source:'Imported preview versus current authored candidate',count:value.differences.length,truncated:value.truncatedCount};}if(comparison!==null&&revisionId!==null&&(comparison.leftRevisionId===revisionId||comparison.rightRevisionId===revisionId))return{source:'Server revision comparison',count:comparison.differences.length,truncated:comparison.isTruncated?1:0};return{source:'No server revision comparison selected',count:0,truncated:0};}
function freeze<T>(value:T):T{if(value!==null&&typeof value==='object'&&!Object.isFrozen(value)){for(const child of Object.values(value as Record<string,unknown>))freeze(child);Object.freeze(value);}return value;}
