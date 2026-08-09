import { gatewayDeclarationEditorContract, gatewayRuntimeSchemas } from '@hpd/gateway-client';
import { serializeGatewayJson, type GatewayJsonDiagnostic, type GatewayJsonNode, type GatewayJsonObject } from './authored-json.ts';

type Schema = Readonly<Record<string, unknown>>;
const schemas = gatewayRuntimeSchemas as unknown as Readonly<Record<string, Schema>>;
const rootSchema = schemas.HPD_Gateway_Abstractions_GatewayConfiguration;

export function validateGatewaySchema(root: GatewayJsonObject): readonly GatewayJsonDiagnostic[] {
  const diagnostics: GatewayJsonDiagnostic[] = [];
  if (rootSchema === undefined) return [diagnostic('schema-authority-unavailable', '')];
  validate(root, rootSchema, '', diagnostics, 0);
  for(const field of gatewayDeclarationEditorContract.fields)validateEditorField(field,root,'',0,diagnostics);
  return Object.freeze(diagnostics.slice(0, 1024));
}

function validateEditorField(field:(typeof gatewayDeclarationEditorContract.fields)[number],node:GatewayJsonNode,path:string,index:number,output:GatewayJsonDiagnostic[]):void{if(output.length>=1024)return;const steps=field.target.occurrencePath;if(index===steps.length){validateWire(node,field.wire,path,output);return;}const step=steps[index]!;if(step.kind==='reference'){validateEditorField(field,node,path,index+1,output);return;}if(step.kind==='union-branch'){if(node.kind==='object'){const tag=node.entries.find(entry=>entry.name===step.value)?.value;if(tag?.kind==='string'&&tag.value===step.secondaryValue)validateEditorField(field,node,path,index+1,output);}return;}if(step.kind==='items'){if(node.kind==='array')node.items.forEach((item,itemIndex)=>validateEditorField(field,item,`${path}/${itemIndex}`,index+1,output));return;}if(step.kind==='property'&&node.kind==='object'&&step.value!==null){const child=node.entries.find(entry=>entry.name===step.value)?.value;if(child!==undefined)validateEditorField(field,child,`${path}/${escapePointer(step.value)}`,index+1,output);}}
function validateWire(node:GatewayJsonNode,wire:(typeof gatewayDeclarationEditorContract.fields)[number]['wire'],path:string,output:GatewayJsonDiagnostic[]):void{if(node.kind==='null'){if(!wire.nullable)output.push(diagnostic('wire-constraint-mismatch',path));return;}const kind=node.kind==='number'?'integer':node.kind;if(kind!==wire.valueKind){output.push(diagnostic('wire-constraint-mismatch',path));return;}if(node.kind==='string'){if(wire.minimumLength!==null&&[...node.value].length<wire.minimumLength||wire.maximumLength!==null&&[...node.value].length>wire.maximumLength)output.push(diagnostic('string-bound-mismatch',path));if(wire.enumJson.length&&!wire.enumJson.some(value=>JSON.parse(value)===node.value))output.push(diagnostic('enum-value-unknown',path));for(const constraint of wire.constraints){const rules=constraint.rules;const bytes=new TextEncoder().encode(node.value).byteLength;if(rules.minimumUtf8Bytes!==null&&bytes<rules.minimumUtf8Bytes||rules.maximumUtf8Bytes!==null&&bytes>rules.maximumUtf8Bytes)output.push(diagnostic('utf8-bound-mismatch',path));if(String(rules.normalization).toLowerCase()==='nfc'&&node.value!==node.value.normalize('NFC'))output.push(diagnostic('normalization-mismatch',path));if(rules.rejectUnicodeControls&&/[\u0000-\u001f\u007f-\u009f]/u.test(node.value))output.push(diagnostic('unicode-control-rejected',path));}}if(node.kind==='array'&&(wire.minimumItems!==null&&node.items.length<wire.minimumItems||wire.maximumItems!==null&&node.items.length>wire.maximumItems))output.push(diagnostic('collection-bound-mismatch',path));}

function validate(node: GatewayJsonNode, source: Schema, path: string, output: GatewayJsonDiagnostic[], depth: number): void {
  if (output.length >= 1024 || depth > 128) return;
  const schema = resolveBranch(node, source, path, output);
  if (schema === null) return;
  const allowed = array(schema.type);
  if (!matchesType(node, allowed)) { output.push(diagnostic('wire-constraint-mismatch', path)); return; }
  if (node.kind === 'null') return;
  const enumValues = array(schema.enum);
  if (enumValues.length > 0 && !enumValues.some(value => scalarEquals(node, value))) output.push(diagnostic('enum-value-unknown', path));
  if (schema.const !== undefined && !scalarEquals(node, schema.const)) output.push(diagnostic('const-value-mismatch', path));
  if (node.kind === 'string') validateString(node.value, schema, path, output);
  else if (node.kind === 'number') validateInteger(node.lexeme, schema, path, output);
  else if (node.kind === 'array') {
    const minimum = integer(schema.minItems), maximum = integer(schema.maxItems);
    if (minimum !== null && node.items.length < minimum || maximum !== null && node.items.length > maximum) output.push(diagnostic('collection-bound-mismatch', path));
    if (schema.uniqueItems === true && new Set(node.items.map(serializeGatewayJson)).size !== node.items.length) output.push(diagnostic('collection-not-unique', path));
    if (record(schema.items)) node.items.forEach((item, index) => validate(item, schema.items as Schema, `${path}/${index}`, output, depth + 1));
  } else if (node.kind === 'object') {
    const properties = record(schema.properties) ? schema.properties as Record<string, Schema> : {};
    const required = new Set(array(schema.required).filter((value): value is string => typeof value === 'string'));
    const names = new Set(node.entries.map(entry => entry.name));
    for (const name of required) if (!names.has(name)) output.push(diagnostic('required-property-missing', `${path}/${escapePointer(name)}`));
    for (const entry of node.entries) {
      const child = properties[entry.name];
      if (!record(child)) output.push(diagnostic('unknown-property', `${path}/${escapePointer(entry.name)}`));
      else validate(entry.value, child, `${path}/${escapePointer(entry.name)}`, output, depth + 1);
    }
  }
}

function resolveBranch(node: GatewayJsonNode, schema: Schema, path: string, output: GatewayJsonDiagnostic[]): Schema | null {
  if (typeof schema.$ref === 'string') return resolveReference(schema.$ref, path, output);
  const choices = array(schema.oneOf).filter(record) as Schema[];
  if (choices.length === 0) return schema;
  if (node.kind !== 'object' || !record(schema.discriminator)) { output.push(diagnostic('union-branch-required', path)); return null; }
  const discriminator = schema.discriminator as Record<string, unknown>;
  const propertyName = typeof discriminator.propertyName === 'string' ? discriminator.propertyName : null;
  const mapping = record(discriminator.mapping) ? discriminator.mapping as Record<string, unknown> : {};
  const value = propertyName === null ? undefined : node.entries.find(entry => entry.name === propertyName)?.value;
  const tag = value?.kind === 'string' ? value.value : null;
  const reference = tag === null ? undefined : mapping[tag];
  if (typeof reference !== 'string') { output.push(diagnostic('union-discriminator-unknown', `${path}/${escapePointer(propertyName ?? 'kind')}`)); return null; }
  return resolveReference(reference, path, output);
}
function resolveReference(reference: string, path: string, output: GatewayJsonDiagnostic[]): Schema | null { const prefix = '#/components/schemas/'; if (!reference.startsWith(prefix)) { output.push(diagnostic('schema-reference-unsupported', path)); return null; } const value=schemas[reference.slice(prefix.length)]; if(value===undefined)output.push(diagnostic('schema-reference-unresolved',path)); return value??null; }
function validateString(value:string,schema:Schema,path:string,output:GatewayJsonDiagnostic[]):void{const min=integer(schema.minLength),max=integer(schema.maxLength);if(min!==null&&[...value].length<min||max!==null&&[...value].length>max)output.push(diagnostic('string-bound-mismatch',path));if(typeof schema.pattern==='string'){try{if(!new RegExp(schema.pattern,'u').test(value))output.push(diagnostic('string-pattern-mismatch',path));}catch{output.push(diagnostic('schema-pattern-invalid',path));}}}
function validateInteger(value:string,schema:Schema,path:string,output:GatewayJsonDiagnostic[]):void{if(!/^-?(?:0|[1-9]\d*)$/u.test(value)){output.push(diagnostic('integer-required',path));return;}const actual=BigInt(value);if(typeof schema.minimum==='number'&&actual<BigInt(schema.minimum)||typeof schema.maximum==='number'&&actual>BigInt(schema.maximum))output.push(diagnostic('integer-bound-mismatch',path));}
function matchesType(node:GatewayJsonNode,types:readonly unknown[]):boolean{const actual=node.kind==='number'?'integer':node.kind;return types.length===0||types.includes(actual);}
function scalarEquals(node:GatewayJsonNode,value:unknown):boolean{return node.kind==='string'&&node.value===value||node.kind==='boolean'&&node.value===value||node.kind==='null'&&value===null||node.kind==='number'&&(typeof value==='number'||typeof value==='string')&&/^-?\d+$/u.test(String(value))&&BigInt(node.lexeme)===BigInt(String(value));}
function array(value:unknown):readonly unknown[]{return Array.isArray(value)?value:value===undefined?[]:[value];}
function record(value:unknown):value is Record<string,unknown>{return typeof value==='object'&&value!==null&&!Array.isArray(value);}
function integer(value:unknown):number|null{return typeof value==='number'&&Number.isSafeInteger(value)?value:null;}
function diagnostic(code:string,path:string):GatewayJsonDiagnostic{return Object.freeze({code,path,offset:0});}
function escapePointer(value:string):string{return value.replaceAll('~','~0').replaceAll('/','~1');}
