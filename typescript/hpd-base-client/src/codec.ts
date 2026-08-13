/** Closed language-neutral type nodes emitted by the BASE client generator. */
export type BaseTypeNode =
  | { readonly kind: "selection-query"; readonly maximumNodes: number; readonly maximumDepth: number; readonly maximumLiterals: number; readonly maximumTake: number }
  | { readonly kind: "selection-previous-state"; readonly maximumFields: number }
  | { readonly kind: "selection-identity" }
  | { readonly kind: "selection-patch"; readonly patchTypeId: string }
  | { readonly kind: "boolean" }
  | { readonly kind: "string"; readonly minLength: number; readonly maxLength: number; readonly format: string }
  | { readonly kind: "integer"; readonly minimum: string; readonly maximum: string; readonly wire: "number" | "decimal-string" }
  | { readonly kind: "decimal"; readonly wire: "decimal-string" }
  | { readonly kind: "floating"; readonly precision: "binary32" | "binary64"; readonly finiteOnly: true }
  | { readonly kind: "bytes"; readonly wire: "base64"; readonly maxBytes: number }
  | { readonly kind: "redacted" }
  | { readonly kind: "subjectReference"; readonly contractId: string; readonly contractVersion: number; readonly subjectIdKind: "ordinalString" | "guid" | "uint64"; readonly maximumSubjectIdUtf8Bytes: number; readonly authorityEpochBytes: 16; readonly incarnationBytes: 16 }
  | { readonly kind: "literal"; readonly value: string | boolean | null }
  | { readonly kind: "enum"; readonly values: readonly string[] }
  | { readonly kind: "array"; readonly elementTypeId: string; readonly minItems: number; readonly maxItems: number }
  | { readonly kind: "object"; readonly properties: readonly { readonly name: string; readonly wireName: string; readonly typeId: string; readonly required: boolean; readonly nullable: boolean; readonly disclosureShape: "none" | "omission" | "fixed-marker" }[]; readonly additionalProperties: false }
  | { readonly kind: "union"; readonly discriminator: string; readonly variants: readonly { readonly tag: string; readonly typeId: string }[] };

/** Maps stable DTO type IDs to closed graph nodes. */
export type BaseTypeGraph = Readonly<Record<string, BaseTypeNode>>;

/** The sole structural value emitted for fixed-marker disclosure. */
export interface BaseRedacted { readonly $base: "redacted"; }
/** The frozen canonical redaction marker. */
export const baseRedacted: BaseRedacted = Object.freeze({ $base: "redacted" });
declare const subjectReferenceBrand: unique symbol;
/** An opaque exported-subject lifetime reference bound to one installed contract. */
export type BaseSubjectReference<TContractId extends string = string> = Readonly<{
  readonly subjectId: string;
  readonly authorityEpoch: string;
  readonly incarnation: string;
  readonly [subjectReferenceBrand]: TContractId;
}>;
/** Returns whether a value is the exact canonical redaction marker shape. */
export function isBaseRedacted(value: unknown): value is BaseRedacted {
  return object(value) && Object.keys(value).length === 1 && value.$base === "redacted";
}

interface RawNumber { readonly rawNumber: true; readonly token: string; }
const rawNumber = (token: string): RawNumber => Object.freeze({ rawNumber: true, token });
const isRawNumber = (value: unknown): value is RawNumber => typeof value === "object" && value !== null && !Array.isArray(value) && (value as Partial<RawNumber>).rawNumber === true && typeof (value as Partial<RawNumber>).token === "string";

/** Parses strict BASE JSON and materializes only lossless general-purpose JSON numbers. */
export function parseBaseJson(json: string): unknown { return materialize(parseBaseJsonDocument(json)); }

/** Decodes one complete JSON document against a generated graph node. */
export function decodeBaseJson<T>(json: string, typeId: string, graph: BaseTypeGraph): T { return decodeNode(parseBaseJsonDocument(json), typeId, graph, "wire") as T; }

/** Validates and deeply copies a materialized value against a generated graph node. */
export function decodeBaseValue<T>(value: unknown, typeId: string, graph: BaseTypeGraph): T { return decodeNode(value, typeId, graph, "application") as T; }

/** Decodes one parsed wire value and translates serialized property names to application names. */
export function decodeBaseWireValue<T>(value: unknown, typeId: string, graph: BaseTypeGraph): T { return decodeNode(value, typeId, graph, "wire") as T; }

/** Produces canonical base-json-v1 for one generated graph value. */
export function encodeBaseJson(value: unknown, typeId: string, graph: BaseTypeGraph): string { return encodeNode(value, typeId, graph, new Set()); }

export function parseBaseJsonDocument(json: string): unknown {
  let index = 0;
  const whitespace = (): void => { while (index < json.length && /[\t\n\r ]/u.test(json[index]!)) index++; };
  const string = (): string => { const start = index++; while (index < json.length) { const character = json[index++]!; if (character === "\\") { if (index >= json.length) throw new SyntaxError(); index++; } else if (character === '"') { const result = JSON.parse(json.slice(start, index)) as string; validUnicode(result); return result; } } throw new SyntaxError(); };
  const value = (): unknown => {
    whitespace(); const character = json[index];
    if (character === "{") { index++; whitespace(); const result: Record<string, unknown> = {}; const keys = new Set<string>(); if (json[index] === "}") { index++; return result; } while (true) { whitespace(); if (json[index] !== '"') throw new SyntaxError(); const key = string(); if (keys.has(key)) throw new SyntaxError(); keys.add(key); whitespace(); if (json[index++] !== ":") throw new SyntaxError(); result[key] = value(); whitespace(); const separator = json[index++]; if (separator === "}") return result; if (separator !== ",") throw new SyntaxError(); } }
    if (character === "[") { index++; whitespace(); const result: unknown[] = []; if (json[index] === "]") { index++; return result; } while (true) { result.push(value()); whitespace(); const separator = json[index++]; if (separator === "]") return result; if (separator !== ",") throw new SyntaxError(); } }
    if (character === '"') return string();
    const match = /^(?:true|false|null|-?(?:0|[1-9]\d*)(?:\.\d+)?(?:[eE][+-]?\d+)?)/u.exec(json.slice(index)); if (match === null) throw new SyntaxError(); const token = match[0]; index += token.length;
    if (token === "true") return true; if (token === "false") return false; if (token === "null") return null; return rawNumber(token);
  };
  const result = value(); whitespace(); if (index !== json.length) throw new SyntaxError(); return result;
}

/** Materializes a strict parsed document after no graph-specific numeric nodes remain. */
export function materializeBaseJsonValue(value: unknown): unknown { return materialize(value); }

function decodeNode(value: unknown, typeId: string, graph: BaseTypeGraph, shape: "application" | "wire"): unknown {
  const node = graph[typeId]; if (node === undefined) throw new TypeError("base.client.responseInvalid");
  switch (node.kind) {
    case "selection-query": return selectionQuery(value, node);
    case "selection-previous-state": return selectionPreviousState(value, node.maximumFields);
    case "selection-identity": return selectionIdentity(value);
    case "selection-patch": return shape === "application" ? decodeNode(value, node.patchTypeId, graph, shape) : selectionPatchWire(value, node.patchTypeId, graph);
    case "boolean": if (typeof value !== "boolean") invalid(); return value;
    case "string": if (typeof value !== "string" || scalarLength(value) < node.minLength || scalarLength(value) > node.maxLength || !format(value, node.format)) invalid(); return value;
    case "integer": return integer(value, node);
    case "decimal": if (typeof value !== "string" || !/^-?(?:0|[1-9][0-9]*)(?:\.[0-9]+)?$/u.test(value) || value === "-0") invalid(); return value;
    case "floating": return floating(value, node.precision);
    case "bytes": return decodeBytes(value, node.maxBytes, shape);
    case "redacted": if (!isBaseRedacted(value)) invalid(); return baseRedacted;
    case "subjectReference": return subjectReference(value, node);
    case "literal": if (value !== node.value) invalid(); return value;
    case "enum": if (typeof value !== "string" || !node.values.includes(value)) invalid(); return value;
    case "array": if (!Array.isArray(value) || value.length < node.minItems || value.length > node.maxItems) invalid(); return Object.freeze(value.map(item => decodeNode(item, node.elementTypeId, graph, shape)));
    case "object": {
      if (!object(value)) invalid(); const key = (property: typeof node.properties[number]): string => shape === "wire" ? property.wireName : property.name; const accepted = new Set(node.properties.map(key)); if (Object.keys(value).some(item => !accepted.has(item))) invalid(); const result: Record<string, unknown> = {};
      for (const property of node.properties) { const source = key(property); if (!Object.hasOwn(value, source)) { if (property.required && property.disclosureShape !== "omission") invalid(); continue; } const item = value[source]; if (property.disclosureShape === "fixed-marker" && isBaseRedacted(item)) result[property.name] = baseRedacted; else if (item === null) { if (!property.nullable) invalid(); result[property.name] = null; } else result[property.name] = decodeNode(item, property.typeId, graph, shape); }
      return Object.freeze(result);
    }
    case "union": { if (!object(value)) invalid(); const variant = node.variants.find(item => { const target = graph[item.typeId]; if (target?.kind !== "object") return false; const discriminator = target.properties.find(property => property.name === node.discriminator); return discriminator !== undefined && value[shape === "wire" ? discriminator.wireName : discriminator.name] === item.tag; }); if (variant === undefined) invalid(); return decodeNode(value, variant.typeId, graph, shape); }
  }
  return invalid();
}

function encodeNode(value: unknown, typeId: string, graph: BaseTypeGraph, path: Set<object>): string {
  const node = graph[typeId]; if (node === undefined) invalid();
  if (object(value) || Array.isArray(value)) { if (path.has(value as object)) invalid(); path.add(value as object); }
  try {
    switch (node.kind) {
      case "selection-query": return canonicalClosed(selectionQuery(value, node));
      case "selection-previous-state": return canonicalClosed(selectionPreviousState(value, node.maximumFields));
      case "selection-identity": return canonicalClosed(selectionIdentity(value));
      case "selection-patch": path.delete(value as object); return `{"patch":{"kind":"fieldMap","fields":${encodeNode(value, node.patchTypeId, graph, path)}}}`;
      case "boolean": case "string": case "decimal": case "literal": case "enum": return JSON.stringify(decodeNode(value, typeId, graph, "application"));
      case "bytes": return JSON.stringify(encodeBytes(value, node.maxBytes));
      case "redacted": invalid();
      case "subjectReference": { const reference = subjectReference(value, node); return `{"subjectId":${JSON.stringify(reference.subjectId)},"authorityEpoch":${JSON.stringify(reference.authorityEpoch)},"incarnation":${JSON.stringify(reference.incarnation)}}`; }
      case "integer": { const decoded = integer(value, node); return node.wire === "number" ? String(decoded) : JSON.stringify(decoded); }
      case "floating": return canonicalFloat(floating(value, node.precision), node.precision);
      case "array": { if (!Array.isArray(value) || value.length < node.minItems || value.length > node.maxItems) invalid(); return `[${value.map(item => encodeNode(item, node.elementTypeId, graph, path)).join(",")}]`; }
      case "object": { if (!object(value)) invalid(); const accepted = new Set(node.properties.map(property => property.name)); if (Object.keys(value).some(key => !accepted.has(key))) invalid(); const fields: string[] = []; for (const property of node.properties) { if (!Object.hasOwn(value, property.name)) { if (property.required) invalid(); continue; } const item = value[property.name]; if (item === null) { if (!property.nullable) invalid(); fields.push(`${JSON.stringify(property.wireName)}:null`); } else fields.push(`${JSON.stringify(property.wireName)}:${encodeNode(item, property.typeId, graph, path)}`); } return `{${fields.join(",")}}`; }
      case "union": { if (!object(value) || typeof value[node.discriminator] !== "string") invalid(); const variant = node.variants.find(item => item.tag === value[node.discriminator]); if (variant === undefined) invalid(); path.delete(value); return encodeNode(value, variant.typeId, graph, path); }
    }
  } finally { if (object(value) || Array.isArray(value)) path.delete(value as object); }
  return invalid();
}

function subjectReference(value: unknown, node: Extract<BaseTypeNode, { kind: "subjectReference" }>): BaseSubjectReference {
  if (!object(value)) invalid(); exact(value, ["subjectId", "authorityEpoch", "incarnation"]);
  if (typeof value.subjectId !== "string" || new TextEncoder().encode(value.subjectId).length < 1
    || new TextEncoder().encode(value.subjectId).length > node.maximumSubjectIdUtf8Bytes
    || typeof value.authorityEpoch !== "string" || typeof value.incarnation !== "string"
    || !/^[A-Za-z0-9_-]{22}$/u.test(value.authorityEpoch) || !/^[A-Za-z0-9_-]{22}$/u.test(value.incarnation)
    || !canonicalBase64Url16(value.authorityEpoch) || !canonicalBase64Url16(value.incarnation)
    || node.authorityEpochBytes !== 16 || node.incarnationBytes !== 16 || !Number.isSafeInteger(node.contractVersion) || node.contractVersion < 1)
    invalid();
  const subject = value.subjectId;
  if (node.subjectIdKind === "ordinalString") {
    if (!unicodeScalarText(subject) || subject.normalize("NFC") !== subject || /[\p{Cc}]/u.test(subject)) invalid();
  } else if (node.subjectIdKind === "guid") {
    if (!/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/u.test(subject)) invalid();
  } else if (!/^(?:0|[1-9][0-9]*)$/u.test(subject) || BigInt(subject) > 18446744073709551615n) invalid();
  return Object.freeze({ subjectId: subject, authorityEpoch: value.authorityEpoch, incarnation: value.incarnation }) as BaseSubjectReference;
}
function unicodeScalarText(value: string): boolean {
  for (let index = 0; index < value.length; index++) {
    const code = value.charCodeAt(index);
    if (code >= 0xd800 && code <= 0xdbff) {
      const low = value.charCodeAt(++index);
      if (!(low >= 0xdc00 && low <= 0xdfff)) return false;
    } else if (code >= 0xdc00 && code <= 0xdfff) return false;
  }
  return true;
}
function canonicalBase64Url16(value: string): boolean {
  try {
    const base64 = value.replaceAll("-", "+").replaceAll("_", "/") + "==";
    const bytes = Uint8Array.from(atob(base64), character => character.charCodeAt(0));
    if (bytes.length !== 16) return false;
    const encoded = btoa(String.fromCharCode(...bytes)).replaceAll("+", "-").replaceAll("/", "_").replace(/=+$/u, "");
    return encoded === value;
  } catch { return false; }
}

function selectionQuery(value: unknown, limits: Extract<BaseTypeNode, { kind: "selection-query" }>): unknown {
  if (!object(value) || Object.keys(value).some(key => !["filter", "sort", "take"].includes(key)) || !Array.isArray(value.sort)
    || value.sort.length < 1 || value.sort.length > limits.maximumNodes || !Number.isSafeInteger(value.take)
    || (value.take as number) < 1 || (value.take as number) > limits.maximumTake) invalid();
  const sort = value.sort.map(item => { if (!object(item) || Object.keys(item).some(key => !["field", "direction", "nulls"].includes(key)) || !boundedText(item.field, 128) || !["asc", "desc"].includes(item.direction as string) || item.nulls !== undefined && !["unspecified", "first", "last"].includes(item.nulls as string)) invalid(); return Object.freeze({ field: item.field, direction: item.direction, ...(item.nulls === undefined ? {} : { nulls: item.nulls }) }); });
  if (sort.at(-1)?.field !== "id" || sort.filter(item => item.field === "id").length !== 1 || sort.at(-1)?.nulls !== undefined && sort.at(-1)?.nulls !== "unspecified") invalid();
  let nodes = 0, literals = 0;
  const filter = (node: unknown, depth: number): unknown => {
    if (!object(node) || depth > limits.maximumDepth || ++nodes > limits.maximumNodes || typeof node.kind !== "string") invalid();
    const kind = node.kind; let result: Record<string, unknown>;
    if (kind === "true" || kind === "false") { exact(node, ["kind"]); result = { kind }; }
    else if (kind === "compare") { exact(node, ["kind", "field", "operator", "value"]); if (!boundedText(node.field, 128) || !["equal", "notEqual", "lessThan", "lessThanOrEqual", "greaterThan", "greaterThanOrEqual", "contains", "notContains", "startsWith", "endsWith", "like", "notLike"].includes(node.operator as string)) invalid(); literals++; result = { kind, field: node.field, operator: node.operator, value: queryValue(node.value, 1) }; }
    else if (kind === "in" || kind === "between") { exact(node, ["kind", "field", "values"]); if (!boundedText(node.field, 128) || !Array.isArray(node.values) || node.values.length < (kind === "between" ? 2 : 1) || kind === "between" && node.values.length !== 2) invalid(); literals += node.values.length; result = { kind, field: node.field, values: Object.freeze(node.values.map(item => queryValue(item, 1))) }; }
    else if (kind === "isNull" || kind === "isDefined") { exact(node, ["kind", "field"]); if (!boundedText(node.field, 128)) invalid(); result = { kind, field: node.field }; }
    else if (kind === "not" || kind === "and" || kind === "or") { exact(node, ["kind", "children"]); if (!Array.isArray(node.children) || node.children.length < 1 || kind === "not" && node.children.length !== 1) invalid(); result = { kind, children: Object.freeze(node.children.map(child => filter(child, depth + 1))) }; }
    else invalid();
    if (literals > limits.maximumLiterals) invalid(); return Object.freeze(result);
  };
  return Object.freeze({ ...(value.filter === undefined ? {} : { filter: filter(value.filter, 1) }), sort: Object.freeze(sort), take: value.take });
}
function selectionPreviousState(value: unknown, maximum: number): unknown {
  if (!object(value)) invalid(); exact(value, ["revision", "fields"]); if (!object(value.revision) || !Array.isArray(value.fields) || value.fields.length > maximum) invalid();
  const revision = value.revision; if (revision.kind === "exact") { exact(revision, ["kind", "exactRevision"]); if (!boundedText(revision.exactRevision, 512)) invalid(); } else { exact(revision, ["kind"]); if (revision.kind !== "none" && revision.kind !== "exists") invalid(); }
  const seen = new Set<string>(); const fields = value.fields.map(item => { if (!object(item) || !boundedText(item.fieldId, 128) || seen.has(item.fieldId)) invalid(); seen.add(item.fieldId); if (item.kind === "equal") { exact(item, ["fieldId", "kind", "value"]); return Object.freeze({ fieldId: item.fieldId, kind: item.kind, value: queryValue(item.value, 1) }); } exact(item, ["fieldId", "kind"]); if (!["isNull", "isMissing", "isDefined"].includes(item.kind as string)) invalid(); return Object.freeze({ fieldId: item.fieldId, kind: item.kind }); });
  return Object.freeze({ revision: Object.freeze({ ...revision }), fields: Object.freeze(fields) });
}
function selectionIdentity(value: unknown): unknown { if (!object(value)) invalid(); exact(value, ["scope", "operation", "idempotencyKey", "fingerprint"]); if (!boundedText(value.scope, 128) || !boundedText(value.operation, 128) || !boundedText(value.idempotencyKey, 256) || typeof value.fingerprint !== "string" || !/^[A-Za-z0-9+/]{43}=$/u.test(value.fingerprint)) invalid(); return Object.freeze({ scope: value.scope, operation: value.operation, idempotencyKey: value.idempotencyKey, fingerprint: value.fingerprint }); }
function selectionPatchWire(value: unknown, patchTypeId: string, graph: BaseTypeGraph): unknown { if (!object(value)) invalid(); exact(value, ["patch"]); if (!object(value.patch)) invalid(); exact(value.patch, ["kind", "fields"]); if (value.patch.kind !== "fieldMap") invalid(); return decodeNode(value.patch.fields, patchTypeId, graph, "wire"); }
function queryValue(value: unknown, depth: number): unknown { if (!object(value) || depth > 16 || typeof value.kind !== "string") invalid(); const kind = value.kind; if (kind === "null") { exact(value, ["kind"]); return Object.freeze({ kind }); } const member = kind === "string" ? "string" : kind === "boolean" ? "boolean" : kind === "integer" ? "integer" : kind === "number" ? "number" : kind === "decimal" ? "decimal" : kind === "dateTime" ? "dateTime" : kind === "id" ? "id" : kind === "array" ? "array" : invalid(); exact(value, ["kind", member]); const item = value[member]; if (kind === "boolean" ? typeof item !== "boolean" : kind === "integer" ? !Number.isSafeInteger(item) : kind === "number" ? typeof item !== "number" || !Number.isFinite(item) : kind === "array" ? !Array.isArray(item) || item.length > 256 : !boundedText(item, 4096)) invalid(); return Object.freeze({ kind, [member]: kind === "array" ? Object.freeze((item as unknown[]).map(child => queryValue(child, depth + 1))) : item }); }
function exact(value: Record<string, unknown>, keys: readonly string[]): void { if (Object.keys(value).length !== keys.length || Object.keys(value).some(key => !keys.includes(key))) invalid(); }
function boundedText(value: unknown, maximum: number): value is string { return typeof value === "string" && value.length > 0 && new TextEncoder().encode(value).length <= maximum && !/[\u0000-\u001f\u007f]/u.test(value); }
function closed(value: unknown): Readonly<Record<string, unknown>> { if (!object(value)) invalid(); return Object.freeze(Object.fromEntries(Object.keys(value).sort().map(key => [key, Array.isArray(value[key]) ? Object.freeze((value[key] as unknown[]).map(item => object(item) ? closed(item) : item)) : object(value[key]) ? closed(value[key]) : value[key]]))); }
function canonicalClosed(value: unknown): string { if (value === null || typeof value === "boolean" || typeof value === "string") return JSON.stringify(value); if (typeof value === "number") { if (!Number.isFinite(value)) invalid(); return Object.is(value, -0) ? "0" : value.toString(); } if (Array.isArray(value)) return `[${value.map(canonicalClosed).join(",")}]`; if (!object(value)) invalid(); return `{${Object.keys(value).sort().map(key => `${JSON.stringify(key)}:${canonicalClosed(value[key])}`).join(",")}}`; }

function materialize(value: unknown): unknown { if (isRawNumber(value)) { validateGeneralNumber(value.token); return Number(value.token); } if (Array.isArray(value)) return value.map(materialize); if (object(value)) return Object.fromEntries(Object.entries(value).map(([key, item]) => [key, materialize(item)])); return value; }
function integer(value: unknown, node: Extract<BaseTypeNode, { kind: "integer" }>): number | string { const text = isRawNumber(value) ? value.token : node.wire === "decimal-string" && typeof value === "string" ? value : typeof value === "number" && Number.isSafeInteger(value) ? String(value) : ""; if (!/^-?(?:0|[1-9][0-9]*)$/u.test(text) || text === "-0") invalid(); const parsed = BigInt(text); if (parsed < BigInt(node.minimum) || parsed > BigInt(node.maximum)) invalid(); if (node.wire === "number") { const numeric = Number(text); if (!Number.isSafeInteger(numeric)) invalid(); return numeric; } return text; }
function floating(value: unknown, precision: "binary32" | "binary64"): number { const token = isRawNumber(value) ? value.token : typeof value === "number" ? canonicalFloat(value, "binary64") : ""; if (!numberGrammar(token)) invalid(); const binary64 = Number(token); if (!Number.isFinite(binary64)) invalid(); const negativeZero = /^-0(?:\.0*)?(?:[eE][+-]?\d+)?$/u.test(token); const lexicalNonzero = /[1-9]/u.test(token.split(/[eE]/u)[0]!); if (precision === "binary32") { const result = Math.fround(binary64); if (!Number.isFinite(result) || result === 0 && lexicalNonzero) invalid(); return Object.is(result, -0) || negativeZero ? 0 : result; } if (binary64 === 0 && lexicalNonzero) invalid(); return Object.is(binary64, -0) || negativeZero ? 0 : binary64; }
function canonicalFloat(value: number, precision: "binary32" | "binary64"): string { if (!Number.isFinite(value)) invalid(); if (Object.is(value, -0) || value === 0) return "0"; if (precision === "binary64") return value.toString(); const target = Math.fround(value); for (let digits = 1; digits <= 9; digits++) { const candidate = target.toPrecision(digits); if (Math.fround(Number(candidate)) === target) return Number(candidate).toString(); } invalid(); }
function validateGeneralNumber(token: string): void { if (!numberGrammar(token)) invalid(); const numeric = Number(token); if (!Number.isFinite(numeric) || Object.is(numeric, -0)) invalid(); if (!token.includes(".") && !/[eE]/u.test(token) && !Number.isSafeInteger(numeric)) invalid(); if (numeric === 0 && /[1-9]/u.test(token.split(/[eE]/u)[0]!)) invalid(); }
function numberGrammar(value: string): boolean { return /^-?(?:0|[1-9]\d*)(?:\.\d+)?(?:[eE][+-]?\d+)?$/u.test(value); }
function format(value: string, kind: string): boolean { if (/^(?:record-id|collection-id|field-id|revision|cursor|consistency-token|mutation-id|dependency-reference)$/u.test(kind)) return value.length > 0 && !/[\u0000-\u001f\u007f]/u.test(value); if (kind === "utc-instant") return !Number.isNaN(Date.parse(value)) && /(?:Z|[+-]\d\d:\d\d)$/u.test(value); return kind === "plain"; }
function base64(value: string): boolean { return value.length % 4 === 0 && /^(?:[A-Za-z0-9+/]{4})*(?:[A-Za-z0-9+/]{2}==|[A-Za-z0-9+/]{3}=)?$/u.test(value); }
function decodeBytes(value: unknown, maximum: number, shape: "application" | "wire"): Uint8Array {
  if (shape === "application") {
    if (!(value instanceof Uint8Array) || value.byteLength > maximum) invalid();
    return new Uint8Array(value);
  }
  if (typeof value !== "string" || !base64(value)) invalid();
  const length = Math.floor(value.length * 3 / 4) - (value.endsWith("==") ? 2 : value.endsWith("=") ? 1 : 0);
  if (length > maximum) invalid();
  const binary = atob(value); if (binary.length !== length) invalid();
  const result = new Uint8Array(length); for (let index = 0; index < length; index++) result[index] = binary.charCodeAt(index);
  if (encodeBytes(result, maximum) !== value) invalid();
  return result;
}
function encodeBytes(value: unknown, maximum: number): string {
  if (!(value instanceof Uint8Array) || value.byteLength > maximum) invalid();
  let binary = ""; for (const byte of value) binary += String.fromCharCode(byte);
  return btoa(binary);
}
function scalarLength(value: string): number { return [...value].length; }
function validUnicode(value: string): void { for (let index = 0; index < value.length; index++) { const unit = value.charCodeAt(index); if (unit >= 0xd800 && unit <= 0xdbff) { const next = value.charCodeAt(++index); if (!(next >= 0xdc00 && next <= 0xdfff)) invalid(); } else if (unit >= 0xdc00 && unit <= 0xdfff) invalid(); } }
function object(value: unknown): value is Record<string, unknown> { return typeof value === "object" && value !== null && !Array.isArray(value) && !isRawNumber(value); }
function invalid(): never { throw new TypeError("base.client.responseInvalid"); }
