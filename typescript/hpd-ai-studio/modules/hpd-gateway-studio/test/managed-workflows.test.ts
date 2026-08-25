import { describe, expect, it, vi } from 'vitest';
import type { GatewayClient } from '@hpd/gateway-client';
import { createGatewayManagedWorkflowController } from '../src/managed-workflows.ts';
import type { GatewayDeclarationController, GatewayDeclarationSnapshot } from '../src/declaration-state.ts';
import type { GatewayStudioController, GatewayStudioSnapshot } from '../src/state.ts';
import type { StudioAuthenticationService } from '@hpd-research/hpd-studio-core';

const ok=(value:unknown,status=200)=>({ok:true,status,value,headers:{}});
const transport=()=>({ok:false,kind:'transport',reason:'network-failure'});

function harness(overrides:Record<string,unknown>={},runtime:Record<string,unknown>={},studioRefresh:(signal?:AbortSignal)=>Promise<void>=async()=>{}){
  let studioValue=studioSnapshot();const studioListeners=new Set<(value:GatewayStudioSnapshot)=>void>();
  const studio={snapshot:()=>studioValue,refresh:vi.fn(studioRefresh),subscribe(listener:(value:GatewayStudioSnapshot)=>void){studioListeners.add(listener);listener(studioValue);return()=>{studioListeners.delete(listener);};}} as unknown as GatewayStudioController;
  let declarationValue=declarationSnapshot();const declarationListeners=new Set<(value:GatewayDeclarationSnapshot)=>void>();
  const declaration={snapshot:()=>declarationValue,subscribe(listener:(value:GatewayDeclarationSnapshot)=>void){declarationListeners.add(listener);listener(declarationValue);return()=>{declarationListeners.delete(listener);};},replaceRaw(source:string){declarationValue={...declarationValue,document:{...declarationValue.document,utf8Text:source,editGeneration:declarationValue.document.editGeneration+1n,sourceSha256:'replacement',state:'LocallyValidNotServerValidated',validation:null}} as GatewayDeclarationSnapshot;for(const listener of declarationListeners)listener(declarationValue);return declarationValue.document;}} as unknown as GatewayDeclarationController;
  let authValue:{isAuthenticated:boolean;subjectHint?:string}={isAuthenticated:true,subjectHint:'principal'};const authListeners=new Set<(value:typeof authValue)=>void>();
  const authentication={snapshot:()=>authValue,subscribe(listener){authListeners.add(listener);listener(authValue);return()=>authListeners.delete(listener);}} satisfies StudioAuthenticationService;
  const client=Object.assign({
    revisions:vi.fn(async()=>ok({items:[revision('r1')],continuationToken:null,hasMore:false})),
    revision:vi.fn(async()=>ok(revision('r1'))), compare:vi.fn(async()=>ok({leftRevisionId:'r1',rightRevisionId:'r2',equivalent:false,isTruncated:false,differences:[]})),
    export:vi.fn(async()=>ok({revisionId:'r1',configurationJson:'{}',contentHashAlgorithm:'sha-256',contentHashValue:'hash',schemaVersion:'1.0'})),
    activations:vi.fn(async()=>ok({intents:{items:[],continuationToken:null,hasMore:false},outcomes:{items:[],continuationToken:null,hasMore:false}})),
    submit:vi.fn(async()=>ok(response(false),201)), 'submit-and-activate':vi.fn(async()=>ok(response(true),202)),
    import:vi.fn(async()=>ok(response(false),201)), 'import-and-activate':vi.fn(async()=>ok(response(true),202)),
    activate:vi.fn(async(input:any)=>ok({...response(true),revisionId:input.path.revision},202)), rollback:vi.fn(async(input:any)=>ok({...response(true),revisionId:input.path.revision},202)), operation:vi.fn(async()=>ok({kind:'command',operationId:'op',operation:'submit',resultCode:'Accepted',acceptedAt:null,desiredStateToken:null}))
  },overrides) as unknown as GatewayClient;
  let random=0;const controller=createGatewayManagedWorkflowController({client,studio,declaration,authentication,randomValues(buffer){buffer.fill(++random);return buffer;},...runtime});
  return{controller,client,declaration,setStudio(next:GatewayStudioSnapshot){studioValue=next;for(const listener of studioListeners)listener(next);},setAuth(next:{isAuthenticated:boolean;subjectHint?:string}){authValue=next;for(const listener of authListeners)listener(next);},edit(){declarationValue={...declarationValue,document:{...declarationValue.document,editGeneration:2n,sourceSha256:'changed'}};for(const listener of declarationListeners)listener(declarationValue);}};
}

describe('Gateway managed workflows',()=>{
  it('freezes one identified submit command and replays it exactly after ambiguity',async()=>{
    const submit=vi.fn().mockResolvedValueOnce(transport()).mockResolvedValueOnce(ok(response(false),201));const h=harness({submit});
    expect(h.controller.openSubmit('submit','release')).toBe(true);const review=h.controller.snapshot().workflow!;
    expect(review.idempotencyKey).toHaveLength(43);expect(review.commandCorrelationId).toMatch(/^studio-[0-9a-f]{32}$/u);
    expect(h.controller.requestConfirmation()).toBe(true);expect(await h.controller.execute()).toBe(false);expect(h.controller.snapshot().workflow?.result?.code).toBe('Outcome not observed');
    expect(await h.controller.retryExact()).toBe(true);expect(submit).toHaveBeenCalledTimes(2);
    expect(submit.mock.calls[1]![0]).toEqual(submit.mock.calls[0]![0]);
  });

  it('cancels only local observation of an executing command',async()=>{
    const submit=vi.fn(async(_:unknown,call:{signal:AbortSignal})=>new Promise(resolve=>call.signal.addEventListener('abort',()=>resolve({ok:false,kind:'canceled',reason:'caller-canceled'}),{once:true})));
    const h=harness({submit});expect(h.controller.openSubmit('submit')).toBe(true);h.controller.requestConfirmation();const execution=h.controller.execute();
    await Promise.resolve();expect(h.controller.snapshot().workflow?.phase).toBe('Executing');expect(h.controller.cancelExecution()).toBe(true);expect(await execution).toBe(false);expect(h.controller.snapshot().workflow?.result?.kind).toBe('canceled');
  });

  it('captures exact desired CAS and becomes stale after authored change',()=>{
    const h=harness();expect(h.controller.openSubmit('submit-and-activate')).toBe(true);
    expect(h.controller.snapshot().workflow?.desiredPrecondition).toEqual({kind:'replace',token:'desired-token'});
    h.edit();expect(h.controller.snapshot().workflow?.phase).toBe('Stale');expect(h.controller.requestConfirmation()).toBe(false);
  });

  it('retains a frozen review during refresh and invalidates it only when refreshed authority changes',()=>{
    const h=harness();expect(h.controller.openSubmit('submit-and-activate')).toBe(true);
    h.setStudio({...studioSnapshot(),refreshing:true});expect(h.controller.snapshot().workflow?.phase).toBe('Reviewing');
    h.setStudio(studioSnapshot());expect(h.controller.requestConfirmation()).toBe(true);
    const changed=studioSnapshot();h.setStudio({...changed,observation:{...changed.observation!,desired:{state:'value',value:{...changed.observation!.desired.value!,desiredStateToken:'changed-token' as never}}}});
    expect(h.controller.snapshot().workflow?.phase).toBe('Stale');expect(h.controller.requestConfirmation()).toBe(false);
  });

  it('permits create-only only from current authoritative desired absence',()=>{
    for(const desiredState of ['denied','failed'] as const){const h=harness();const current=studioSnapshot();h.setStudio({...current,observation:{...current.observation!,desired:{state:desiredState}}});expect(h.controller.openSubmit('submit-and-activate')).toBe(false);}
    const stale=harness();stale.setStudio({...studioSnapshot(),stale:true});expect(stale.controller.openSubmit('submit-and-activate')).toBe(false);
    const refreshing=harness();refreshing.setStudio({...studioSnapshot(),refreshing:true});expect(refreshing.controller.openSubmit('submit-and-activate')).toBe(false);
    const absent=harness();const current=studioSnapshot();absent.setStudio({...current,observation:{...current.observation!,desired:{state:'not-observed'}}});expect(absent.controller.openSubmit('submit-and-activate')).toBe(true);expect(absent.controller.snapshot().workflow?.desiredPrecondition).toEqual({kind:'create-only'});
  });

  it('keeps submit-only free of CAS and enforces the configuration bound',()=>{
    const h=harness();expect(h.controller.openSubmit('submit')).toBe(true);expect(h.controller.snapshot().workflow?.desiredPrecondition).toBeNull();h.controller.closeWorkflow();
    expect(h.controller.openImport('import','x'.repeat(4_194_305),'file')).toBe(false);
  });

  it('keeps import separate from local authoring and activates only through explicit review',async()=>{
    const h=harness();const source=h.declaration.snapshot().document.utf8Text;
    expect(h.controller.openImport('import',source,'selected-file')).toBe(true);expect(h.declaration.snapshot().document.utf8Text).toBe(source);
    expect(h.controller.requestConfirmation()).toBe(true);expect(await h.controller.execute()).toBe(true);expect(h.client.import).toHaveBeenCalledOnce();
    h.controller.closeWorkflow();expect(await h.controller.loadRevisions()).toBe(true);expect(await h.controller.selectRevision('r1' as never)).toBe(true);
    expect(h.controller.openActivation('rollback','r1' as never,'controlled rollback')).toBe(true);expect(h.controller.requestConfirmation()).toBe(true);expect(await h.controller.execute()).toBe(true);expect(h.client.rollback).toHaveBeenCalledOnce();h.controller.dispose();
  });

  it('pages intents and outcomes with independent opaque cursors',async()=>{
    const activations=vi.fn().mockResolvedValueOnce(ok({intents:{items:[intent('i1')],continuationToken:'ic',hasMore:true},outcomes:{items:[],continuationToken:null,hasMore:false}})).mockResolvedValueOnce(ok({intents:{items:[intent('i2')],continuationToken:null,hasMore:false},outcomes:{items:[],continuationToken:null,hasMore:false}}));
    const h=harness({activations});expect(await h.controller.loadActivationHistory()).toBe(true);expect(await h.controller.loadActivationHistory()).toBe(true);
    expect(activations.mock.calls[1]![0].query).toEqual({maximum:64,intentCursor:'ic'});expect(h.controller.snapshot().activationIntents).toHaveLength(2);
  });

  it('loads bounded revisions, selects metadata, and explicitly opens exported bytes as authored',async()=>{
    const h=harness();expect(await h.controller.loadRevisions()).toBe(true);expect(await h.controller.selectRevision('r1' as never)).toBe(true);expect(await h.controller.exportRevision('r1' as never)).toBe(true);
    expect(h.controller.openExportAsAuthored()).toBe(true);expect(h.declaration.snapshot().document.utf8Text).toBe('{}');
  });

  it('rejects mismatched revision, comparison, export, operation, and activation identities',async()=>{
    const h=harness({revision:vi.fn(async()=>ok(revision('wrong'))),compare:vi.fn(async()=>ok({leftRevisionId:'wrong',rightRevisionId:'r2',equivalent:false,isTruncated:false,differences:[]})),export:vi.fn(async()=>ok({...awaitedExport(),revisionId:'wrong'})),operation:vi.fn(async()=>ok({operationId:'wrong',operation:'submit',resultCode:'Accepted',acceptedAt:null,desiredStateToken:null}))});
    expect(await h.controller.selectRevision('r1' as never)).toBe(false);expect(await h.controller.compare('r1' as never,'r2' as never)).toBe(false);expect(await h.controller.exportRevision('r1' as never)).toBe(false);
    expect(h.controller.openSubmit('submit')).toBe(true);h.controller.requestConfirmation();expect(await h.controller.execute()).toBe(true);expect(await h.controller.refreshOperation()).toBe(false);
    h.controller.closeWorkflow();const activation=harness({activate:vi.fn(async()=>ok({...response(true),revisionId:'wrong'},202))});await activation.controller.loadRevisions();await activation.controller.selectRevision('r1' as never);expect(activation.controller.openActivation('activate','r1' as never)).toBe(true);activation.controller.requestConfirmation();expect(await activation.controller.execute()).toBe(false);expect(activation.controller.snapshot().workflow?.result?.kind).toBe('protocol');
  });

  it('admits operation receipts only when command semantics and desired identity match',async()=>{
    const accepted=harness({operation:vi.fn(async()=>ok({kind:'command',operationId:'operation',operation:'submit-only',resultCode:'accepted',acceptedAt:null,desiredStateToken:null}))});
    accepted.controller.openSubmit('submit');accepted.controller.requestConfirmation();await accepted.controller.execute();expect(await accepted.controller.refreshOperation()).toBe(true);
    const wrongKind=harness({operation:vi.fn(async()=>ok({kind:'command',operationId:'operation',operation:'activate-existing',resultCode:'accepted',acceptedAt:null,desiredStateToken:null}))});
    wrongKind.controller.openSubmit('submit');wrongKind.controller.requestConfirmation();await wrongKind.controller.execute();expect(await wrongKind.controller.refreshOperation()).toBe(false);
    const wrongToken=harness({operation:vi.fn(async()=>ok({kind:'command',operationId:'operation',operation:'submit-and-activate',resultCode:'accepted',acceptedAt:null,desiredStateToken:'other'}))});
    wrongToken.controller.openSubmit('submit-and-activate');wrongToken.controller.requestConfirmation();await wrongToken.controller.execute();expect(await wrongToken.controller.refreshOperation()).toBe(false);
  });

  it('tracks rapidly, transitions to ordinary cadence, and stops at five minutes',async()=>{
    const clock=fakeClock();const h=harness({},clock.runtime);
    h.controller.openSubmit('submit-and-activate');h.controller.requestConfirmation();await h.controller.execute();expect(clock.delays()[0]).toBe(2_000);
    for(let index=0;index<30;index++){clock.runNext();await vi.waitFor(()=>expect(clock.delays()[0]).toBe(index===29?30_000:2_000));}
    for(let index=0;index<8;index++){clock.runNext();await Promise.resolve();await Promise.resolve();if(index<7)await vi.waitFor(()=>expect(clock.delays()[0]).toBe(30_000));}
    await vi.waitFor(()=>expect(clock.delays()).toHaveLength(0));
  });

  it('backs off a rate-limited tracker and refreshes history until its exact intent appears',async()=>{
    const clock=fakeClock();
    const activations=vi.fn().mockResolvedValueOnce({ok:false,kind:'http',status:429,error:{code:'rate-limited'}}).mockResolvedValueOnce(ok({intents:{items:[],continuationToken:null,hasMore:false},outcomes:{items:[{outcomeId:'outcome',activationIntentId:'intent',authorityVersion:'1',kind:'Acknowledged',code:'ok',observedAt:null}],continuationToken:null,hasMore:false}}));
    const h=harness({activations,operation:vi.fn(async()=>ok({kind:'command',operationId:'operation',operation:'submit-and-activate',resultCode:'accepted',acceptedAt:null,desiredStateToken:'token'}))},clock.runtime);
    h.controller.openSubmit('submit-and-activate');h.controller.requestConfirmation();await h.controller.execute();clock.runNext();await vi.waitFor(()=>expect(clock.delays().some(delay=>delay>2_000&&delay<=30_000)).toBe(true));expect(clock.delays()[0]).toBeLessThanOrEqual(30_000);
    clock.runNext();await vi.waitFor(()=>expect(h.controller.snapshot().activationOutcomes).toHaveLength(1));expect(clock.delays()).toHaveLength(0);
  });

  it('traverses independent bounded history pages until the exact tracked intent is found',async()=>{
    const clock=fakeClock();
    const activations=vi.fn()
      .mockResolvedValueOnce(ok({intents:{items:[intent('older-intent')],continuationToken:'intent-page-2',hasMore:true},outcomes:{items:[outcome('older-outcome','older-intent')],continuationToken:'outcome-page-2',hasMore:true}}))
      .mockResolvedValueOnce(ok({intents:{items:[intent('intent')],continuationToken:null,hasMore:false},outcomes:{items:[outcome('matching-outcome','intent')],continuationToken:null,hasMore:false}}));
    const h=harness({activations,operation:vi.fn(async()=>ok({kind:'command',operationId:'operation',operation:'submit-and-activate',resultCode:'accepted',acceptedAt:null,desiredStateToken:'token'}))},clock.runtime);
    h.controller.openSubmit('submit-and-activate');h.controller.requestConfirmation();await h.controller.execute();clock.runNext();
    await vi.waitFor(()=>expect(h.controller.snapshot().activationOutcomes.some(value=>value.activationIntentId==='intent')).toBe(true));
    expect(activations).toHaveBeenCalledTimes(2);expect(activations.mock.calls[1]![0].query).toEqual({maximum:64,intentCursor:'intent-page-2',outcomeCursor:'outcome-page-2'});expect(clock.delays()).toHaveLength(0);
  });

  it('stops exact tracking when an independent history stream exhausts its bound',async()=>{
    const clock=fakeClock();let page=0;
    const activations=vi.fn(async()=>{const current=page++;return ok({intents:{items:[],continuationToken:null,hasMore:false},outcomes:{items:Array.from({length:64},(_,index)=>outcome(`outcome-${current}-${index}`,'other-intent')),continuationToken:`outcome-page-${current+1}`,hasMore:true}});});
    const h=harness({activations},clock.runtime);
    h.controller.openSubmit('submit-and-activate');h.controller.requestConfirmation();await h.controller.execute();clock.runNext();await vi.waitFor(()=>expect(activations).toHaveBeenCalledTimes(64));await vi.waitFor(()=>expect(clock.delays()).toHaveLength(0));expect(h.controller.snapshot().activationOutcomes).toHaveLength(0);
  });

  it('aborts hanging tracker requests at the absolute five-minute deadline',async()=>{
    const clock=fakeClock(),signals:AbortSignal[]=[];const never=(_:unknown,call:{signal:AbortSignal})=>{signals.push(call.signal);return new Promise(()=>{});};
    const refresh=vi.fn((signal?:AbortSignal)=>new Promise<void>(resolve=>{if(signal===undefined){resolve();return;}signals.push(signal);signal.addEventListener('abort',()=>resolve(),{once:true});}));
    const h=harness({activations:vi.fn(never),operation:vi.fn(never)},clock.runtime,refresh);
    h.controller.openSubmit('submit-and-activate');h.controller.requestConfirmation();await h.controller.execute();clock.runNext();await vi.waitFor(()=>expect(signals).toHaveLength(3));
    expect(clock.delays()).toEqual([298_000]);clock.runNext();expect(signals.every(signal=>signal.aborted)).toBe(true);expect(clock.delays()).toHaveLength(0);
  });

  it('counts slow multipage request duration against the absolute deadline',async()=>{
    const clock=fakeClock();let page=0;
    const activations=vi.fn(async()=>{clock.advance(50_000);const current=page++;return ok({intents:{items:[],continuationToken:null,hasMore:false},outcomes:{items:[outcome(`slow-${current}`,'other-intent')],continuationToken:`slow-page-${current+1}`,hasMore:true}});});
    const h=harness({activations},clock.runtime);h.controller.openSubmit('submit-and-activate');h.controller.requestConfirmation();await h.controller.execute();clock.runNext();
    await vi.waitFor(()=>expect(activations).toHaveBeenCalledTimes(6));await vi.waitFor(()=>expect(clock.delays()).toHaveLength(0));expect(clock.now()).toBe(302_000);expect(h.controller.snapshot().activationOutcomes).toHaveLength(0);
  });

  it('rejects late tracker history and receipt truth after every authority invalidation',async()=>{
    for(const invalidation of ['close','sign-out','context','dispose'] as const){
      const clock=fakeClock(),history=deferred<any>(),receipt=deferred<any>();
      const h=harness({activations:vi.fn(()=>history.promise),operation:vi.fn(()=>receipt.promise)},clock.runtime);
      let emissions=0;h.controller.subscribe(()=>{emissions++;});h.controller.openSubmit('submit-and-activate');h.controller.requestConfirmation();await h.controller.execute();clock.runNext();
      await Promise.resolve();const beforeInvalidation=emissions;
      if(invalidation==='close')h.controller.closeWorkflow();
      else if(invalidation==='sign-out')h.setAuth({isAuthenticated:false});
      else if(invalidation==='context')h.setStudio({...studioSnapshot(),context:{namespaceId:'ns',targetId:'other'}});
      else h.controller.dispose();
      const afterInvalidation=emissions;
      history.resolve(ok({intents:{items:[intent('intent')],continuationToken:null,hasMore:false},outcomes:{items:[outcome('late-outcome','intent')],continuationToken:null,hasMore:false}}));
      receipt.resolve(ok({kind:'command',operationId:'operation',operation:'submit-and-activate',resultCode:'accepted',acceptedAt:null,desiredStateToken:'token'}));
      await Promise.resolve();await Promise.resolve();await Promise.resolve();
      expect(h.controller.snapshot().activationOutcomes,`${invalidation} history`).toHaveLength(0);expect(h.controller.snapshot().operation,`${invalidation} receipt`).toBeNull();expect(emissions,`${invalidation} emissions`).toBe(afterInvalidation);expect(afterInvalidation).toBeGreaterThanOrEqual(beforeInvalidation);
    }
  });

  it('clears authorized result history on context and principal replacement',async()=>{
    const context=harness();context.controller.openSubmit('submit');context.controller.requestConfirmation();await context.controller.execute();expect(context.controller.snapshot().results).toHaveLength(1);context.setStudio({...studioSnapshot(),context:{namespaceId:'ns',targetId:'other'}});expect(context.controller.snapshot().results).toHaveLength(0);
    const principal=harness();principal.controller.openSubmit('submit');principal.controller.requestConfirmation();await principal.controller.execute();expect(principal.controller.snapshot().results).toHaveLength(1);principal.setAuth({isAuthenticated:true,subjectHint:'other'});expect(principal.controller.snapshot().results).toHaveLength(0);
    const signedOut=harness();signedOut.controller.openSubmit('submit');signedOut.controller.requestConfirmation();await signedOut.controller.execute();signedOut.setAuth({isAuthenticated:false});expect(signedOut.controller.snapshot().results).toHaveLength(0);
  });
});

function studioSnapshot():GatewayStudioSnapshot{return {authentication:{isAuthenticated:true,subjectHint:'principal'},draft:{namespaceId:'ns',targetId:'target'},context:{namespaceId:'ns',targetId:'target'},phase:'ready',verdict:'Serving Ready',lifecycle:[],refreshing:false,stale:false,lastSuccessfulAt:'2026-08-08T00:00:00Z',failureCode:null,capabilities:{state:'value',value:{apiVersion:'v1',capabilities:[]}},capabilitiesObservedAt:'2026-08-08T00:00:00Z',observation:{hostCapabilities:{state:'value',value:{schemaVersion:'1',snapshotAlgorithm:'sha-256',snapshotValue:'cap',capabilities:{} as never}},status:{} as never,desired:{state:'value',value:{namespaceId:'ns' as never,targetNodeId:'target' as never,revisionId:'desired' as never,activationIntentId:'intent' as never,candidateId:'candidate' as never,desiredStateToken:'desired-token' as never,observedAt:null}},effective:{state:'not-observed'},observedAt:'2026-08-08T00:00:00Z'}};}
function declarationSnapshot():GatewayDeclarationSnapshot{return {lastCompatibleHistory:null,baseline:null,document:{utf8Text:'{"schemaVersion":{"major":1,"minor":0},"canonicalizationVersion":1}',editGeneration:1n,sourceSha256:'source',graph:{} as never,compatibleGraph:{} as never,state:'ServerValid',diagnostics:[],truncatedDiagnostics:0,validation:{editGeneration:1n,sourceSha256:'source',validationTransportSha256:'transport',contentHashAlgorithm:'sha-256',contentHashValue:'candidate',hostCapabilitySnapshotAlgorithm:'sha-256',hostCapabilitySnapshotValue:'cap',correlationId:'correlation',observedAt:'2026-08-08T00:00:00Z',isValid:true,transferredFromProposalId:null}}};}
function revision(id:string){return{revisionId:id,acceptedAt:null,canonicalizationVersion:'1',contentHashAlgorithm:'sha-256',contentHashValue:'hash',derivedFromRevisionId:null,description:null,parentRevisionId:null,schemaVersion:'1.0',sourceId:'studio',sourceKind:'studio',validationId:'validation'};}
function response(activated:boolean){return{operationId:'operation',revisionId:'revision',activationIntentId:activated?'intent':null,desiredStateToken:activated?'token':null,duplicate:false};}
function intent(id:string){return{intentId:id,revisionId:'revision',candidateId:'candidate',contentHashValue:'hash',authorityVersion:'1',acceptedAt:null};}
function outcome(id:string,activationIntentId:string){return{outcomeId:id,activationIntentId,authorityVersion:'1',kind:'Acknowledged',code:'ok',observedAt:null};}
function deferred<T>(){let resolve!:(value:T)=>void;const promise=new Promise<T>(accept=>{resolve=accept;});return{promise,resolve};}
function fakeClock(){let current=0,nextId=0;const tasks:{id:number;due:number;callback:()=>void}[]=[];const runtime={monotonicNow:()=>current,setTimeout:(callback:()=>void,milliseconds:number)=>{const id=++nextId;tasks.push({id,due:current+milliseconds,callback});return id as never;},clearTimeout:(handle:unknown)=>{const index=tasks.findIndex(task=>task.id===handle);if(index>=0)tasks.splice(index,1);}};return{runtime,now:()=>current,advance:(milliseconds:number)=>{current+=milliseconds;},delays:()=>tasks.map(task=>task.due-current).sort((left,right)=>left-right),runNext:()=>{tasks.sort((left,right)=>left.due-right.due);const task=tasks.shift();if(task===undefined)throw new Error('No scheduled task.');current=Math.max(current,task.due);task.callback();}};}
function awaitedExport(){return{revisionId:'r1',configurationJson:'{}',contentHashAlgorithm:'sha-256',contentHashValue:'hash',schemaVersion:'1.0'};}
