import { gatewayDeclarationEditorContract } from '@hpd/gateway-client';
import { parseGatewayJson, serializeGatewayJson, type GatewayJsonDiagnostic, type GatewayJsonNode, type GatewayJsonObject } from './authored-json.ts';
import { framedSha256 } from './sha256.ts';
import type { GatewayClient, GatewayConfiguration, GatewayCorrelationId, GatewayValidationResponse } from '@hpd/gateway-client';

const encoder = new TextEncoder();
export const initialGatewayDocument = '{"schemaVersion":{"major":1,"minor":0},"canonicalizationVersion":1}';
export type GatewayLocalValidationState = 'RawMalformed' | 'RawOnlyIncompatible' | 'LocallyIncomplete' | 'LocallyValidNotServerValidated' | 'ServerRejected' | 'ServerValid' | 'ServerValidationStale';
export interface AuthoredGatewayDocument {
  readonly utf8Text: string;
  readonly editGeneration: bigint;
  readonly sourceSha256: string;
  readonly graph: GatewayJsonObject | null;
  readonly compatibleGraph: GatewayJsonObject | null;
  readonly state: GatewayLocalValidationState;
  readonly diagnostics: readonly GatewayJsonDiagnostic[];
  readonly truncatedDiagnostics: number;
  readonly validation: GatewayServerValidationEvidence | null;
}
export interface GatewayServerValidationEvidence { readonly editGeneration: bigint; readonly sourceSha256: string; readonly validationTransportSha256: string; readonly contentHashAlgorithm: string | null; readonly contentHashValue: string | null; readonly hostCapabilitySnapshotAlgorithm: string; readonly hostCapabilitySnapshotValue: string; readonly correlationId: string; readonly observedAt: string; readonly isValid: boolean; readonly transferredFromProposalId: string | null; }
export interface GatewayAuthoredBaseline { readonly utf8Text: string; readonly editGeneration: bigint; readonly sourceSha256: string; readonly capturedAt: string; readonly graph: GatewayJsonObject; }
export interface GatewayDeclarationSnapshot { readonly document: AuthoredGatewayDocument; readonly lastCompatibleHistory: AuthoredGatewayDocument | null; readonly baseline: GatewayAuthoredBaseline | null; }
export interface GatewayDeclarationController {
  snapshot(): GatewayDeclarationSnapshot;
  subscribe(listener: (value: GatewayDeclarationSnapshot) => void): () => void;
  replaceRaw(source: string): AuthoredGatewayDocument;
  setAtPointer(pointer: string, value: GatewayJsonNode): boolean;
  captureBaseline(now?: Date): boolean;
  clearBaseline(): void;
  validate(): Promise<boolean>;
  invalidateHostCapabilities(): void;
  commitValidatedProposal(baseGeneration:bigint,baseSourceSha256:string,proposedUtf8:string,evidence:GatewayServerValidationEvidence,proposalId:string):boolean;
  reset(): void;
  clearPrincipal(): void;
  dispose(): void;
}
interface GatewayDeclarationControllerOptions { readonly client?: GatewayClient; readonly hostCapabilityIdentity?: () => Readonly<{ algorithm: string; value: string }> | null; readonly correlationId?: () => string | undefined; }

export function createGatewayDeclarationController(options: GatewayDeclarationControllerOptions = {}): GatewayDeclarationController {
  let document = materialize(initialGatewayDocument, 0n);
  let history: AuthoredGatewayDocument | null = document.compatibleGraph === null ? null : document;
  let baseline: GatewayAuthoredBaseline | null = null;
  let disposed = false;
  let validationGeneration = 0;
  let validationController: AbortController | null = null;
  const listeners = new Set<(value: GatewayDeclarationSnapshot) => void>();
  const project = (): GatewayDeclarationSnapshot => Object.freeze({ document, lastCompatibleHistory: history, baseline });
  const emit = (): void => { const value = project(); for (const listener of listeners) listener(value); };
  const replace = (source: string): AuthoredGatewayDocument => {
    if (disposed) return document;
    validationGeneration++; validationController?.abort(); validationController=null;
    const next = materialize(String(source), document.editGeneration + 1n);
    if (document.compatibleGraph !== null) history = document;
    document = next; emit(); return document;
  };
  return Object.freeze({
    snapshot: project,
    subscribe(listener: (value: GatewayDeclarationSnapshot) => void) { if (disposed) return () => {}; listeners.add(listener); listener(project()); let active=true; return()=>{if(active){active=false;listeners.delete(listener);}}; },
    replaceRaw: replace,
    setAtPointer(pointer: string, value: GatewayJsonNode) {
      if (disposed || document.compatibleGraph === null) return false;
      const updated = replaceNode(document.compatibleGraph, pointer, value);
      if (updated === null) return false;
      const source=serializeGatewayJson(updated);const candidate=materialize(source,document.editGeneration+1n);if(candidate.compatibleGraph===null)return false;validationGeneration++;validationController?.abort();validationController=null;if(document.compatibleGraph!==null)history=document;document=candidate;emit();return true;
    },
    captureBaseline(now = new Date()) { if (disposed || document.compatibleGraph === null) return false; baseline = Object.freeze({ utf8Text: document.utf8Text, editGeneration: document.editGeneration, sourceSha256: document.sourceSha256, capturedAt: now.toISOString(), graph: document.compatibleGraph }); emit(); return true; },
    clearBaseline() { if (!disposed) { baseline=null; emit(); } },
    async validate() {
      if (disposed || options.client === undefined || document.compatibleGraph === null || document.state === 'LocallyIncomplete') return false;
      const captured=document; const capturedGraph=document.compatibleGraph; const identity=options.hostCapabilityIdentity?.() ?? null; const current=++validationGeneration; validationController?.abort(); const abort=new AbortController(); validationController=abort;
      let plain: unknown; try { plain=toPlain(capturedGraph); } catch { return false; }
      const validationText=JSON.stringify(plain); const validationBytes=encoder.encode(validationText); const reparsed=parseGatewayJson(validationText);
      if(!reparsed.ok||!gatewayJsonEquivalent(capturedGraph,reparsed.graph))return false;
      const transportHash=framedSha256('hpd.gateway.validation-transport.v1\0',validationBytes);
      const correlation=options.correlationId?.();
      const result=await options.client.validate({path:{},...(correlation===undefined?{}:{headers:{correlationId:correlation as GatewayCorrelationId}}),body:plain as GatewayConfiguration},{signal:abort.signal});
      if(disposed||abort.signal.aborted||current!==validationGeneration||document!==captured)return false;
      if(!result.ok){document=Object.freeze({...document,state:'ServerRejected',validation:null});emit();return false;}
      const value:GatewayValidationResponse=result.value; const currentIdentity=options.hostCapabilityIdentity?.()??null;
      if(identity!==null&&(currentIdentity===null||currentIdentity.algorithm!==identity.algorithm||currentIdentity.value!==identity.value))return false;
      if(currentIdentity!==null&&(value.hostCapabilitySnapshotAlgorithm!==currentIdentity.algorithm||value.hostCapabilitySnapshotValue!==currentIdentity.value))return false;
      const evidence:GatewayServerValidationEvidence=Object.freeze({editGeneration:captured.editGeneration,sourceSha256:captured.sourceSha256,validationTransportSha256:transportHash,contentHashAlgorithm:value.contentHashAlgorithm,contentHashValue:value.contentHashValue,hostCapabilitySnapshotAlgorithm:value.hostCapabilitySnapshotAlgorithm,hostCapabilitySnapshotValue:value.hostCapabilitySnapshotValue,correlationId:value.correlationId,observedAt:value.observedAt,isValid:value.isValid,transferredFromProposalId:null});
      document=Object.freeze({...document,state:value.isValid?'ServerValid':'ServerRejected',validation:evidence});emit();return value.isValid;
    },
    invalidateHostCapabilities(){if(!disposed&&document.validation!==null){document=Object.freeze({...document,state:'ServerValidationStale',validation:document.validation});emit();}},
    commitValidatedProposal(baseGeneration:bigint,baseSourceSha256:string,proposedUtf8:string,evidence:GatewayServerValidationEvidence,proposalId:string){if(disposed||document.editGeneration!==baseGeneration||document.sourceSha256!==baseSourceSha256||!evidence.isValid)return false;const next=materialize(proposedUtf8,baseGeneration+1n);if(next.compatibleGraph===null||next.state!=='LocallyValidNotServerValidated')return false;document=Object.freeze({...next,state:'ServerValid',validation:Object.freeze({...evidence,editGeneration:next.editGeneration,sourceSha256:next.sourceSha256,transferredFromProposalId:proposalId})});history=null;emit();return true;},
    reset() { if (!disposed) { baseline=null; history=null; replace(initialGatewayDocument); } },
    clearPrincipal() { if (!disposed) { baseline=null; history=null; replace(initialGatewayDocument); } },
    dispose() { if (!disposed) { disposed=true; validationGeneration++;validationController?.abort();validationController=null;baseline=null; history=null; listeners.clear(); } }
  });
}

function materialize(source: string, generation: bigint): AuthoredGatewayDocument {
  const bytes = encoder.encode(source);
  const sourceSha256 = framedSha256('hpd.gateway.authored-source.v1\0', bytes);
  const parsed = parseGatewayJson(source);
  if (!parsed.ok) return seal(source, generation, sourceSha256, null, null, 'RawMalformed', [parsed.diagnostic]);
  const compatibility = validateCompatibility(parsed.graph);
  if (compatibility.length > 0) return seal(source, generation, sourceSha256, parsed.graph, null, 'RawOnlyIncompatible', compatibility);
  const completeness = validateMinimum(parsed.graph);
  return seal(source, generation, sourceSha256, parsed.graph, parsed.graph, completeness.length === 0 ? 'LocallyValidNotServerValidated' : 'LocallyIncomplete', completeness);
}
function seal(source:string,generation:bigint,hash:string,graph:GatewayJsonObject|null,compatible:GatewayJsonObject|null,state:GatewayLocalValidationState,diagnostics:readonly GatewayJsonDiagnostic[]):AuthoredGatewayDocument {
  const kept=diagnostics.slice(0,1024).map(value=>Object.freeze(value)); return Object.freeze({utf8Text:source,editGeneration:generation,sourceSha256:hash,graph,compatibleGraph:compatible,state,diagnostics:Object.freeze(kept),truncatedDiagnostics:Math.max(0,diagnostics.length-kept.length),validation:null});
}

type PatternStep = Readonly<{ kind:string; value:string|null; secondaryValue:string|null }>;
const fieldPatterns = gatewayDeclarationEditorContract.fields.map(field => ({steps:field.target.occurrencePath.filter(step => step.kind === 'property' || step.kind === 'items') as readonly PatternStep[],wire:field.wire,union:field.target.occurrencePath.some(step=>step.kind==='union-branch')}));
const patterns = fieldPatterns.map(value=>value.steps);
function validateCompatibility(root: GatewayJsonObject): GatewayJsonDiagnostic[] {
  const diagnostics: GatewayJsonDiagnostic[]=[];
  const walk=(node:GatewayJsonNode,path:(string|number)[]):void=>{
    if(path.length>0&&!validAtPath(node,path))diagnostics.push(Object.freeze({code:'wire-constraint-mismatch',path:pointer(path),offset:0}));
    if(node.kind==='object'){const names=new Set(node.entries.map(entry=>entry.name));for(const required of requiredChildren(path))if(!names.has(required))diagnostics.push(Object.freeze({code:'required-property-missing',path:pointer([...path,required]),offset:0}));for(const entry of node.entries){const next=[...path,entry.name];if(!hasPrefix(next))diagnostics.push(Object.freeze({code:'unknown-property',path:pointer(next),offset:0}));else walk(entry.value,next);}}
    else if(node.kind==='array') node.items.forEach((value,index)=>walk(value,[...path,index]));
  }; walk(root,[]); return diagnostics;
}
function hasPrefix(path:readonly(string|number)[]):boolean{return patterns.some(pattern=>{const normalized=pattern.map(step=>step.kind==='items'?'#':step.value!);return path.every((value,index)=>normalized[index]=== (typeof value==='number'?'#':value));});}
function requiredChildren(path:readonly(string|number)[]):string[]{const normalizedPath=path.map(value=>typeof value==='number'?'#':value);return [...new Set(fieldPatterns.filter(field=>field.wire.required&&!field.union&&field.steps.length===path.length+1).filter(field=>field.steps.slice(0,-1).every((step,index)=>(step.kind==='items'?'#':step.value)===normalizedPath[index])).map(field=>field.steps.at(-1)!.value!).filter(value=>value!==null))];}
function validAtPath(node:GatewayJsonNode,path:readonly(string|number)[]):boolean{const candidates=fieldPatterns.filter(field=>{const normalized=field.steps.map(step=>step.kind==='items'?'#':step.value!);return normalized.length===path.length&&path.every((value,index)=>normalized[index]===(typeof value==='number'?'#':value));});if(candidates.length===0)return true;return candidates.some(({wire})=>{if(node.kind==='null')return wire.nullable;const kinds:Record<GatewayJsonNode['kind'],string>={null:'null',boolean:'boolean',string:'string',number:'integer',array:'array',object:'object'};if(wire.valueKind!==kinds[node.kind])return false;if(node.kind==='string'){if(wire.minimumLength!==null&&[...node.value].length<wire.minimumLength)return false;if(wire.maximumLength!==null&&[...node.value].length>wire.maximumLength)return false;if(wire.pattern!==null&&!new RegExp(wire.pattern,'u').test(node.value))return false;if(wire.enumJson.length>0&&!wire.enumJson.some(value=>JSON.parse(value)===node.value))return false;}if(node.kind==='number'){if(!/^-?(?:0|[1-9]\d*)$/u.test(node.lexeme))return false;const value=BigInt(node.lexeme);if(wire.minimumJson!==null&&value<BigInt(JSON.parse(wire.minimumJson)))return false;if(wire.maximumJson!==null&&value>BigInt(JSON.parse(wire.maximumJson)))return false;if(wire.enumJson.length>0&&!wire.enumJson.some(item=>{try{return BigInt(JSON.parse(item))===value;}catch{return false;}}))return false;}if(node.kind==='array'){if(wire.minimumItems!==null&&node.items.length<wire.minimumItems)return false;if(wire.maximumItems!==null&&node.items.length>wire.maximumItems)return false;if(wire.uniqueItems&&new Set(node.items.map(serializeGatewayJson)).size!==node.items.length)return false;}return true;});}
function validateMinimum(root:GatewayJsonObject):GatewayJsonDiagnostic[]{const map=new Map(root.entries.map(value=>[value.name,value.value]));const diagnostics:GatewayJsonDiagnostic[]=[];const version=map.get('schemaVersion');const versionMap=version?.kind==='object'?new Map(version.entries.map(value=>[value.name,value.value])):null;if(versionMap?.get('major')?.kind!=='number'||(versionMap.get('major') as any).lexeme!=='1'||versionMap.get('minor')?.kind!=='number'||(versionMap.get('minor') as any).lexeme!=='0')diagnostics.push({code:'schema-version-required',path:'/schemaVersion',offset:0});if(map.get('canonicalizationVersion')?.kind!=='number'||(map.get('canonicalizationVersion') as any).lexeme!=='1')diagnostics.push({code:'canonicalization-version-required',path:'/canonicalizationVersion',offset:0});return diagnostics;}
function replaceNode(root:GatewayJsonObject,pointerValue:string,value:GatewayJsonNode):GatewayJsonObject|null{if(!pointerValue.startsWith('/'))return null;const segments=pointerValue.split('/').slice(1).map(value=>value.replaceAll('~1','/').replaceAll('~0','~'));const recurse=(node:GatewayJsonNode,index:number):GatewayJsonNode|null=>{if(index===segments.length)return value;const segment=segments[index]!;if(node.kind==='object'){const position=node.entries.findIndex(entry=>entry.name===segment);if(position<0)return null;const child=recurse(node.entries[position]!.value,index+1);if(child===null)return null;const entries=node.entries.map((entry,i)=>i===position?Object.freeze({name:entry.name,value:child}):entry);return Object.freeze({kind:'object',entries:Object.freeze(entries)});}if(node.kind==='array'){const position=Number(segment);if(!Number.isInteger(position)||position<0||position>=node.items.length)return null;const child=recurse(node.items[position]!,index+1);if(child===null)return null;const items=node.items.map((item,i)=>i===position?child:item);return Object.freeze({kind:'array',items:Object.freeze(items)});}return null;};const result=recurse(root,0);return result?.kind==='object'?result:null;}
function pointer(path:readonly(string|number)[]):string{return path.map(value=>`/${String(value).replaceAll('~','~0').replaceAll('/','~1')}`).join('');}
function toPlain(node:GatewayJsonNode):unknown{switch(node.kind){case'null':return null;case'boolean':case'string':return node.value;case'number':{if(!/^-?(?:0|[1-9]\d*)$/u.test(node.lexeme))throw new Error('Non-integral number.');const value=Number(node.lexeme);if(!Number.isSafeInteger(value))throw new Error('Unsafe integer.');return value;}case'array':return node.items.map(toPlain);case'object':return Object.fromEntries(node.entries.map(entry=>[entry.name,toPlain(entry.value)]));}}
function gatewayJsonEquivalent(left:GatewayJsonNode,right:GatewayJsonNode):boolean{if(left.kind==='number'&&right.kind==='number')return BigInt(left.lexeme)===BigInt(right.lexeme);if(left.kind!==right.kind)return false;if(left.kind==='null')return true;if(left.kind==='boolean'||left.kind==='string')return left.value===(right as any).value;if(left.kind==='array')return left.items.length===(right as any).items.length&&left.items.every((value,index)=>gatewayJsonEquivalent(value,(right as any).items[index]));const entries=(right as GatewayJsonObject).entries;if(left.kind==='object')return left.entries.length===entries.length&&left.entries.every(entry=>{const found=entries.find(value=>value.name===entry.name);return found!==undefined&&gatewayJsonEquivalent(entry.value,found.value);});return false;}
