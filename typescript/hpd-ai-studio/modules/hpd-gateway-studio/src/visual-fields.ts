import { gatewayDeclarationEditorContract, type GatewayHostCapabilitySnapshotResponse } from '@hpd/gateway-client';
import type { GatewayJsonNode, GatewayJsonObject } from './authored-json.ts';
import { projectGatewayEditorCapabilities } from './capability-projection.ts';

type Field = (typeof gatewayDeclarationEditorContract.fields)[number];
export interface GatewayVisualField {
  readonly key:string; readonly pointer:string; readonly label:string; readonly helpCode:string;
  readonly group:string; readonly family:string; readonly valueKind:string; readonly value:string;
  readonly present:boolean; readonly required:boolean; readonly nullable:boolean; readonly enumValues:readonly string[];
  readonly inheritance:string; readonly omitted:string; readonly capabilityState:string; readonly capabilityOptions:readonly string[];
}

export function projectGatewayVisualFields(graph:GatewayJsonObject,snapshot:GatewayHostCapabilitySnapshotResponse|null):readonly GatewayVisualField[]{
  const capabilities=new Map(projectGatewayEditorCapabilities(snapshot).map(value=>[value.helpCode,value]));
  const result:GatewayVisualField[]=[];
  for(const field of gatewayDeclarationEditorContract.fields)if(field.disposition==='editable')resolve(field,graph,'',0,result,capabilities);
  result.sort((a,b)=>ordinal(a.pointer,b.pointer)||ordinal(a.helpCode,b.helpCode));return Object.freeze(result.slice(0,50_000));
}
export function visualFieldNode(field:GatewayVisualField,value:string,checked?:boolean):GatewayJsonNode|null{
  if(field.valueKind==='boolean')return Object.freeze({kind:'boolean',value:checked??value==='true'});
  if(field.valueKind==='integer')return /^-?(?:0|[1-9]\d*)$/u.test(value)?Object.freeze({kind:'number',lexeme:value}):null;
  if(field.valueKind==='string')return Object.freeze({kind:'string',value});
  return null;
}
export function everyEditableFieldHasRenderer():boolean{return gatewayDeclarationEditorContract.fields.filter(value=>value.disposition==='editable').every(value=>value.wire.valueKind==='string'||value.wire.valueKind==='integer'||value.wire.valueKind==='boolean');}

function resolve(field:Field,node:GatewayJsonNode,pointer:string,index:number,output:GatewayVisualField[],capabilities:Map<string,ReturnType<typeof projectGatewayEditorCapabilities>[number]>):void{
  const steps=field.target.occurrencePath;if(index>=steps.length){append(field,node,pointer,true,output,capabilities);return;}const step=steps[index]!;
  if(step.kind==='reference'){resolve(field,node,pointer,index+1,output,capabilities);return;}
  if(step.kind==='union-branch'){if(node.kind!=='object')return;const discriminator=node.entries.find(entry=>entry.name===step.value)?.value;if(discriminator?.kind==='string'&&discriminator.value===step.secondaryValue)resolve(field,node,pointer,index+1,output,capabilities);return;}
  if(step.kind==='items'){if(node.kind==='array')node.items.forEach((item,itemIndex)=>resolve(field,item,`${pointer}/${itemIndex}`,index+1,output,capabilities));return;}
  if(step.kind==='property'&&node.kind==='object'&&step.value!==null){const child=node.entries.find(entry=>entry.name===step.value)?.value;const next=`${pointer}/${escapePointer(step.value)}`;if(child!==undefined)resolve(field,child,next,index+1,output,capabilities);else if(index===steps.length-1)append(field,null,next,false,output,capabilities);}
}
function append(field:Field,node:GatewayJsonNode|null,pointer:string,present:boolean,output:GatewayVisualField[],capabilities:Map<string,ReturnType<typeof projectGatewayEditorCapabilities>[number]>):void{
  const capability=capabilities.get(field.helpCode);const terminal=field.target.occurrencePath.filter(step=>step.kind==='property').at(-1)?.value??field.family;
  const enumValues=field.wire.enumJson.map(value=>String(JSON.parse(value)));
  let value='';if(node?.kind==='string')value=node.value;else if(node?.kind==='number')value=node.lexeme;else if(node?.kind==='boolean')value=String(node.value);
  output.push(Object.freeze({key:`${field.helpCode}:${pointer}`,pointer,label:terminal,helpCode:field.helpCode,group:field.presentationGroup,family:field.family,valueKind:field.wire.valueKind,value,present,required:field.wire.required,nullable:field.wire.nullable,enumValues:Object.freeze(enumValues),inheritance:field.inheritance,omitted:field.omittedValueKind==='canonical-json'?field.omittedValueJson??'absent':field.omittedValueKind,capabilityState:capability?.state??'not-required',capabilityOptions:capability?.options??Object.freeze([])}));
}
function escapePointer(value:string):string{return value.replaceAll('~','~0').replaceAll('/','~1');}
function ordinal(left:string,right:string):number{return left<right?-1:left>right?1:0;}
