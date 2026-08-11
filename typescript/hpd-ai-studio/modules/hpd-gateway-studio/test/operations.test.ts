import {describe,expect,it,vi} from 'vitest';
import {readFileSync} from 'node:fs';
import type {GatewayClient} from '@hpd/gateway-client';
import type {StudioAuthenticationService} from '@hpd-research/hpd-studio-core';
import type {GatewayStudioController,GatewayStudioSnapshot} from '../src/state.ts';
import type {GatewayManagedWorkflowController} from '../src/managed-workflows.ts';
import {createGatewayOperationsController} from '../src/operations.ts';
import {validateGatewayObservabilityLinks} from '../src/observability-links.ts';

const ok=(value:unknown,status=200)=>({ok:true,status,value,headers:{}});

function harness(overrides:Record<string,unknown>={},configuration:{studio?:GatewayStudioSnapshot;managed?:unknown;controller?:Record<string,unknown>}={}){
 let studioValue=configuration.studio??studioSnapshot(),authValue={isAuthenticated:true,subjectHint:'principal'};const studioListeners=new Set<(value:GatewayStudioSnapshot)=>void>(),authListeners=new Set<(value:typeof authValue)=>void>();
 const studio={snapshot:()=>studioValue,subscribe(listener:(value:GatewayStudioSnapshot)=>void){studioListeners.add(listener);listener(studioValue);return()=>studioListeners.delete(listener);}} as unknown as GatewayStudioController;
 const authentication={snapshot:()=>authValue,subscribe(listener){authListeners.add(listener);listener(authValue);return()=>authListeners.delete(listener);}} satisfies StudioAuthenticationService;
 const managedValue=configuration.managed??{activationIntents:[],activationOutcomes:[],activationObservedAt:null,intentsHaveMore:false,outcomesHaveMore:false,stale:false};
 const managed={snapshot:()=>managedValue,subscribe(){return()=>{};}} as unknown as GatewayManagedWorkflowController;
 const client=Object.assign({audit:vi.fn(async()=>ok({items:[],continuationToken:null,hasMore:false})),provision:vi.fn(async()=>ok({operationId:'provision-op',duplicate:false},201)),backup:vi.fn(async()=>ok({operationId:'backup-op',state:'IndeterminatePending',code:'pending',artifactReference:null},202)),purge:vi.fn(async()=>ok({operationId:'purge-op',state:'Completed',code:'done',artifactReference:null},202)),operation:vi.fn(async()=>ok({kind:'administration',operationId:'backup-op',operation:'Backup',state:'Completed',code:'done',artifactReference:'artifact',observedAt:'2026-08-08T00:00:00Z'}))},overrides) as unknown as GatewayClient;
 let random=0;const controller=createGatewayOperationsController({client,studio,managed,authentication,randomValues(bytes){bytes.fill(++random);return bytes;},now:()=>new Date('2026-08-08T01:02:03Z'),...configuration.controller});
 return{controller,client,setAuth(value:typeof authValue){authValue=value;for(const listener of authListeners)listener(value);},setStudio(value:GatewayStudioSnapshot){studioValue=value;for(const listener of studioListeners)listener(value);}};
}

describe('Gateway operations controller',()=>{
 it('offers provision only from current principal-scoped static capability truth and retains its operation identity',async()=>{const h=harness();expect(h.controller.openProvisionReview()).toBe(true);const review=h.controller.snapshot().review!;expect(review.expectedStatus).toBe(201);expect(review.targetNodeId).toBe('target');expect(h.controller.requestConfirmation()).toBe(true);expect(await h.controller.execute()).toBe(true);expect(h.client.provision).toHaveBeenCalledOnce();expect(h.controller.snapshot().review).toMatchObject({phase:'accepted',operationId:'provision-op',duplicate:false});});
 it('tracks an administrative backup through the discriminated operation reader',async()=>{const h=harness();expect(h.controller.openBackupReview('archive','nightly')).toBe(true);h.controller.requestConfirmation();expect(await h.controller.execute()).toBe(true);expect(h.controller.snapshot().review?.operation).toMatchObject({operation:'Backup',state:'IndeterminatePending'});expect(await h.controller.refreshAdministrativeOperation()).toBe(true);expect(h.controller.snapshot().review?.operation).toMatchObject({kind:'administration',state:'Completed',artifactReference:'artifact'});});
 it('clears authorized audit and review truth on principal replacement',async()=>{const h=harness({audit:vi.fn(async()=>ok({items:[{auditId:'audit',actorId:'actor',operation:'submit',resultCode:'accepted',correlationId:'correlation',subjectId:'revision',recordedAt:null}],continuationToken:null,hasMore:false}))});await h.controller.loadAudit();h.controller.openBackupReview('archive');expect(h.controller.snapshot().audit).toHaveLength(1);h.setAuth({isAuthenticated:true,subjectHint:'other'});expect(h.controller.snapshot()).toMatchObject({audit:[],review:null});});
 it('creates a bounded redacted local diagnostic observation',()=>{const h=harness();const bundle=h.controller.createDiagnosticBundle()!;expect(bundle.filename).toBe('hpd-gateway-diagnostic-20260808T010203Z.json');const text=new TextDecoder().decode(bundle.bytes);const value=JSON.parse(text);expect(value.kind).toBe('hpd-gateway-studio-diagnostic-observation');expect(value.context).toEqual({namespaceId:'ns',targetNodeId:'target'});expect(text).not.toContain('actor');expect(text).not.toContain('token');});
 it('aborts a hanging administrative observation at the absolute lifetime deadline',async()=>{
   const callbacks:Array<()=>void>=[];let operationSignal:AbortSignal|undefined;
   const h=harness({operation:vi.fn((_input:unknown,init?:{signal?:AbortSignal})=>{operationSignal=init?.signal;return new Promise(()=>{});})});
   h.controller.dispose();
   const base=studioSnapshot(),authentication={snapshot:()=>({isAuthenticated:true,subjectHint:'principal'}),subscribe(){return()=>{};}} as StudioAuthenticationService;
   const studio={snapshot:()=>base,subscribe(){return()=>{};}} as unknown as GatewayStudioController;
   const managed={snapshot:()=>({activationIntents:[],activationOutcomes:[],activationObservedAt:null,intentsHaveMore:false,outcomesHaveMore:false,stale:false}),subscribe(){return()=>{};}} as unknown as GatewayManagedWorkflowController;
   const controller=createGatewayOperationsController({client:h.client,studio,managed,authentication,
     randomValues(bytes){bytes.fill(1);return bytes;},schedule(callback){callbacks.push(callback);return callback;},cancelSchedule(){}});
   controller.openBackupReview('archive');controller.requestConfirmation();await controller.execute();
   callbacks[1]!();
   await Promise.resolve();
   expect(operationSignal?.aborted).toBe(false);
   callbacks[0]!();
   expect(operationSignal?.aborted).toBe(true);
   controller.dispose();
 });
 it('retains exactly 4096 audit facts and fails closed on maximum plus one',async()=>{
   let page=0;const audit=vi.fn(async()=>{const current=page++;return ok({items:Array.from({length:current<64?64:1},(_,index)=>auditFact(`a-${current}-${index}`)),continuationToken:`cursor-${current}`,hasMore:current<64});});
   const h=harness({audit});
   expect(await h.controller.loadAudit(true)).toBe(true);
   for(let index=1;index<64;index++)expect(await h.controller.loadAudit(false)).toBe(true);
   expect(h.controller.snapshot().audit).toHaveLength(4096);
   expect(await h.controller.loadAudit(false)).toBe(false);
   expect(h.controller.snapshot()).toMatchObject({auditStale:true,auditHasMore:true});
 });
 it('rejects missing, repeated, and duplicate audit cursor truth',async()=>{
   for(const page of [
     {items:[auditFact('a')],continuationToken:null,hasMore:true},
     {items:[auditFact('a')],continuationToken:'same',hasMore:true},
   ]){
     const h=harness({audit:vi.fn(async()=>ok(page))});
     expect(await h.controller.loadAudit(true)).toBe(page.continuationToken!==null);
     if(page.continuationToken!==null)expect(await h.controller.loadAudit(false)).toBe(false);
     expect(h.controller.snapshot().auditStale).toBe(true);
   }
 });
 it('freezes exact provision retry identity and blocks absent capability authority',async()=>{
   const provision=vi.fn().mockResolvedValueOnce({ok:false,kind:'transport',reason:'network'}).mockResolvedValueOnce(ok({operationId:'provision-op',duplicate:true},201));
   const h=harness({provision});h.controller.openProvisionReview();h.controller.requestConfirmation();
   expect(await h.controller.execute()).toBe(false);expect(h.controller.snapshot().review).toMatchObject({phase:'failed',transportAmbiguous:true});
   expect(await h.controller.retryExact()).toBe(true);expect(provision.mock.calls[1]![0]).toEqual(provision.mock.calls[0]![0]);
   const denied=harness({}, {studio:{...studioSnapshot(),capabilities:{state:'value',value:{apiVersion:'v1',capabilities:[]}}}});
   expect(denied.controller.openProvisionReview()).toBe(false);
 });
 it('enforces backup and destructive purge review bounds before execution',()=>{
   const h=harness();
   expect(h.controller.openBackupReview('Bad Sink')).toBe(false);
   expect(h.controller.openBackupReview('archive','x'.repeat(129))).toBe(false);
   expect(h.controller.openPurgeReview('RevisionContent',[])).toBe(false);
   expect(h.controller.openPurgeReview('RevisionContent',Array.from({length:257},(_,i)=>`id-${String(i).padStart(3,'0')}`))).toBe(false);
   expect(h.controller.openPurgeReview('RevisionContent',['b','a'])).toBe(false);
   expect(h.controller.openPurgeReview('RevisionContent',['a'])).toBe(true);
   expect(h.controller.requestConfirmation()).toBe(false);
   h.controller.setConfirmationPhrase('ns');expect(h.controller.requestConfirmation()).toBe(true);
 });
 it('replays frozen backup input exactly and never retries a definite rejection',async()=>{
   const backup=vi.fn().mockResolvedValueOnce({ok:false,kind:'transport',reason:'network'}).mockResolvedValueOnce(ok({operationId:'backup-op',state:'Completed',code:'done',artifactReference:'artifact'},202));
   const h=harness({backup});h.controller.openBackupReview('archive','nightly');h.controller.requestConfirmation();
   expect(await h.controller.execute()).toBe(false);expect(await h.controller.retryExact()).toBe(true);
   expect(backup.mock.calls[1]![0]).toEqual(backup.mock.calls[0]![0]);
   const rejected=harness({backup:vi.fn(async()=>({ok:false,kind:'http',status:409,code:'conflict'}))});
   rejected.controller.openBackupReview('archive');rejected.controller.requestConfirmation();expect(await rejected.controller.execute()).toBe(false);expect(await rejected.controller.retryExact()).toBe(false);
 });
 it('sends the complete confirmed purge category and 256-resource set unchanged',async()=>{
   const purge=vi.fn(async(_input:unknown)=>ok({operationId:'purge-op',state:'Completed',code:'done',artifactReference:null},202));const h=harness({purge});
   const ids=Array.from({length:256},(_,index)=>`id-${String(index).padStart(3,'0')}`);
   expect(h.controller.openPurgeReview('AuditHistory',ids)).toBe(true);h.controller.setConfirmationPhrase('ns');h.controller.requestConfirmation();expect(await h.controller.execute()).toBe(true);
   expect((purge.mock.calls[0]![0] as {body:unknown}).body).toEqual({category:'AuditHistory',resourceIds:ids});
 });
 it('fences late audit results across target replacement and disposal',async()=>{
   let resolve!:(value:unknown)=>void;const pending=new Promise(value=>resolve=value);const h=harness({audit:vi.fn(()=>pending)});
   const load=h.controller.loadAudit(true);h.setStudio({...studioSnapshot(),context:{namespaceId:'ns',targetId:'other'},draft:{namespaceId:'ns',targetId:'other'}});
   resolve(ok({items:[auditFact('late')],continuationToken:null,hasMore:false}));expect(await load).toBe(false);expect(h.controller.snapshot().audit).toEqual([]);
 });
 it('applies collection maxima, exact omitted counts, deterministic output, and malformed-Unicode rejection',()=>{
   const records=Array.from({length:600},(_,index)=>effectiveRecord(index));
   const upstreams=Array.from({length:600},(_,index)=>appliedUpstream(index));
   const intents=Array.from({length:300},(_,index)=>activationIntent(index));
   const outcomes=Array.from({length:300},(_,index)=>activationOutcome(index));
   const studio=diagnosticStudio(records,upstreams);const managed={activationIntents:intents,activationOutcomes:outcomes,activationObservedAt:'2026-08-08T00:00:00Z',intentsHaveMore:true,outcomesHaveMore:true,stale:false};
   const h=harness({}, {studio,managed});const first=h.controller.createDiagnosticBundle()!;const second=h.controller.createDiagnosticBundle()!;
   expect(first.bytes).toEqual(second.bytes);expect(first.bytes.length).toBeLessThanOrEqual(1_048_576);
   const text=new TextDecoder().decode(first.bytes);const value=JSON.parse(text);
   expect(value.observations.effective.value.records).toHaveLength(512);
   expect(value.observations.effective.value.upstreams).toHaveLength(512);
   expect(value.observations.effective.value).toMatchObject({applicationId:'0123456789abcdef0123456789abcdef',appliedAt:'2026-08-08T00:00:00Z'});
   expect(value.retainedActivations.intents).toHaveLength(256);expect(value.retainedActivations.outcomes).toHaveLength(256);
   expect(value.truncation).toMatchObject({effectiveRecordsOmittedLocally:88,appliedUpstreamsOmittedLocally:88,activationIntentsOmittedLocally:44,activationOutcomesOmittedLocally:44,activationIntentsMayHaveMoreAtSource:true,activationOutcomesMayHaveMoreAtSource:true});
   expect(text).not.toContain('10.0.0.');
   const malformed=harness({}, {studio,managed:{...managed,activationOutcomes:[{...outcomes[0],code:'\uD800'}]}});
   expect(malformed.controller.createDiagnosticBundle()).toBeNull();
   const diagnose=readFileSync(new URL('../src/GatewayDiagnose.svelte',import.meta.url),'utf8');
   expect(diagnose).toContain('{#if exportFailure}');
   expect(diagnose).toContain('The bounded diagnostic envelope could not be produced.');
 });
 it('accepts the exact one-MiB diagnostic boundary and deterministically truncates max plus one',()=>{
   const records=Array.from({length:512},(_,index)=>effectiveRecord(index));
   const base=harness({}, {studio:diagnosticStudio(records)}).controller.createDiagnosticBundle()!;
   const gap=1_048_576-base.bytes.length;expect(gap).toBeGreaterThan(0);
   const each=Math.floor(gap/records.length),remainder=gap%records.length;
   const padded=records.map((record,index)=>({...record,contributions:[{...record.contributions[0],sourceIdentity:record.contributions[0]!.sourceIdentity+'x'.repeat(each+(index===records.length-1?remainder:0))}]}));
   const exact=harness({}, {studio:diagnosticStudio(padded)}).controller.createDiagnosticBundle()!;
   expect(exact.bytes).toHaveLength(1_048_576);
   const plusOne=padded.map((record,index)=>index===padded.length-1?({...record,contributions:[{...record.contributions[0],sourceIdentity:record.contributions[0]!.sourceIdentity+'x'}]}):record);
   const truncated=harness({}, {studio:diagnosticStudio(plusOne)}).controller.createDiagnosticBundle()!;
   expect(truncated.bytes.length).toBeLessThanOrEqual(1_048_576);
   const value=JSON.parse(new TextDecoder().decode(truncated.bytes));expect(value.observations.effective.value.records).toHaveLength(511);expect(value.truncation.effectiveRecordsOmittedLocally).toBe(1);
 });
 it('rejects unsafe, credentialed, fragmented, oversized, and duplicate observability links',()=>{
   for(const href of ['http://example.test','https://user:pass@example.test','https://example.test/#fragment','not-a-url'])
     expect(()=>validateGatewayObservabilityLinks([{id:'one',label:'One',kind:'logs',href}])).toThrow();
   expect(()=>validateGatewayObservabilityLinks([{id:'same',label:'One',kind:'logs',href:'https://a.test'},{id:'same',label:'Two',kind:'logs',href:'https://b.test'}])).toThrow();
   expect(()=>validateGatewayObservabilityLinks(Array.from({length:17},(_,i)=>({id:`id-${i}`,label:'Link',kind:'logs' as const,href:`https://example.test/${i}`})))).toThrow();
 });
});

function auditFact(id:string){return{auditId:id,actorId:`actor-${id}`,operation:'submit',resultCode:'accepted',correlationId:`correlation-${id}`,subjectId:`subject-${id}`,recordedAt:'2026-08-08T00:00:00Z'};}
function effectiveRecord(index:number){return{compilerPackage:'HPD.Gateway',compilerVersion:'1',composition:'ReplaceMoreSpecific',contributions:[{contentHash:{algorithm:'sha256',value:`content-${index}`},definition:null,deterministicOrder:0,disposition:'Selected',scope:'RouteLocal',sourceIdentity:`route-${index}`,sourceKind:'Inline'}],diagnostics:[],disposition:'Materialized',effectiveContentHash:{algorithm:'sha256',value:`effective-${index}`},family:'authorization',nativeProjection:{owner:'YARP',seam:'AuthorizationPolicy'},schemaVersion:1,targetId:`route-${index}`,targetKind:'Route'};}
function activationIntent(index:number){return{acceptedAt:'2026-08-08T00:00:00Z',authorityVersion:String(index),candidateId:`candidate-${index}`,contentHashValue:`hash-${index}`,intentId:`intent-${index}`,revisionId:`revision-${index}`};}
function activationOutcome(index:number){return{activationIntentId:`intent-${index}`,authorityVersion:String(index),code:'accepted',kind:'ActiveAcknowledged',observedAt:'2026-08-08T00:00:00Z',outcomeId:`outcome-${index}`};}
function appliedUpstream(index:number){return{upstreamId:`upstream-${String(index).padStart(3,'0')}`,kind:'serviceDiscovery',discoveryProfile:'aspire',service:`service-${index}`,endpoint:'https',membershipGeneration:String(index),membershipIdentity:{algorithm:'sha-256',value:`membership-${index}`},destinationCount:2,disposition:'fresh',safeDiagnostic:'membership applied'};}
function diagnosticStudio(records:unknown[],upstreams:unknown[]=[]):GatewayStudioSnapshot{const value=studioSnapshot();return{...value,observation:{...value.observation!,effective:{state:'value',value:{appliedAt:'2026-08-08T00:00:00Z',applicationId:'0123456789abcdef0123456789abcdef',candidateContentHash:{algorithm:'sha256',value:'candidate-hash'},candidateId:'candidate',isComplete:true,isTruncated:false,routes:records.map((record,index)=>({routeId:`route-${index}`,contributions:[record]})),schemaVersion:1,symbolicPlanIdentity:{algorithm:'sha-256',value:'a'.repeat(64)},upstreams}}}} as unknown as GatewayStudioSnapshot;}

function studioSnapshot():GatewayStudioSnapshot{return{authentication:{isAuthenticated:true,subjectHint:'principal'},draft:{namespaceId:'ns',targetId:'target'},context:{namespaceId:'ns',targetId:'target'},phase:'ready',verdict:'Serving Ready',lifecycle:[],capabilities:{state:'value',value:{apiVersion:'v1',capabilities:['gateway.management.target.provision']}},capabilitiesObservedAt:'2026-08-08T00:00:00Z',refreshing:false,stale:false,lastSuccessfulAt:'2026-08-08T00:00:00Z',failureCode:null,observation:{hostCapabilities:{state:'not-observed'},status:{observedAt:'2026-08-08T00:00:00Z',isTruncated:false,nodeObservation:'Observed',management:{} as never,node:{} as never},desired:{state:'not-observed'},effective:{state:'not-observed'},observedAt:'2026-08-08T00:00:00Z'}};}
