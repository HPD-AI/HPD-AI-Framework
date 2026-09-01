/** Closed language-neutral type nodes emitted by the BASE client generator. */
export type BaseTypeNode =
  | { readonly kind: "selection-query"; readonly maximumNodes: number; readonly maximumDepth: number; readonly maximumLiterals: number; readonly maximumTake: number }
  | { readonly kind: "selection-previous-state"; readonly maximumFields: number }
  | { readonly kind: "selection-identity" }
  | { readonly kind: "selection-patch"; readonly patchTypeId: string }
  | { readonly kind: "module-generation" }
  | { readonly kind: "boolean" }
  | { readonly kind: "string"; readonly minLength: number; readonly maxLength: number; readonly format: string }
  | { readonly kind: "integer"; readonly minimum: string; readonly maximum: string; readonly wire: "number" | "decimal-string" }
  | { readonly kind: "decimal"; readonly wire: "decimal-string" }
  | { readonly kind: "floating"; readonly precision: "binary32" | "binary64"; readonly finiteOnly: true }
  | { readonly kind: "bytes"; readonly wire: "base64"; readonly maxBytes: number }
  | { readonly kind: "canonicalJson"; readonly canonicalJsonShape: BaseCanonicalJsonShape }
  | { readonly kind: "redacted" }
  | { readonly kind: "subjectReference"; readonly contractId: string; readonly contractVersion: number; readonly subjectIdKind: "ordinalString" | "guid" | "uint64"; readonly maximumSubjectIdUtf8Bytes: number; readonly authorityEpochBytes: 16; readonly incarnationBytes: 24 }
  | { readonly kind: "literal"; readonly value: string | boolean | null }
  | { readonly kind: "enum"; readonly values: readonly string[] }
  | { readonly kind: "array"; readonly elementTypeId: string; readonly minItems: number; readonly maxItems: number }
  | { readonly kind: "object"; readonly properties: readonly { readonly name: string; readonly wireName: string; readonly typeId: string; readonly required: boolean; readonly nullable: boolean; readonly disclosureShape: "none" | "omission" | "fixed-marker" }[]; readonly additionalProperties: false }
  | { readonly kind: "union"; readonly discriminator: string; readonly variants: readonly { readonly tag: string; readonly typeId: string }[] };

/** Public bounded canonical-JSON authority carried by one generated type node. */
export interface BaseCanonicalJsonShape {
  readonly jsonShape: "object" | "array" | "objectOrArray" | 0 | 1 | 2;
  readonly maximumCanonicalJsonBytes: number;
  readonly maximumJsonDepth: number;
  readonly maximumJsonArrayItems: number;
  readonly maximumJsonObjectProperties: number;
  readonly maximumJsonTotalNodes: number;
  readonly maximumJsonTotalStringUtf8Bytes: number;
  readonly maximumJsonTotalNameUtf8Bytes: number;
  readonly checksum: string;
}

/** Maps stable DTO type IDs to closed graph nodes. */
export type BaseTypeGraph = Readonly<Record<string, BaseTypeNode>>;

/** Constructs one deeply owned closed dynamic L41 graph for Studio/runtime interpretation. */
export function createBaseTypeGraph(types: readonly Readonly<{ readonly id: string; readonly node: BaseTypeNode }>[],
  maximumNodes = 2_048, maximumDepth = 32): BaseTypeGraph {
  if (!Array.isArray(types) || types.length < 1 || types.length > maximumNodes) invalid();
  const result: Record<string, BaseTypeNode> = {}; let previous = "";
  for (const entry of types) {
    exactTypeNode(entry, ["id", "node"]); if (!dynamicTypeId(entry.id) || entry.id <= previous) invalid(); previous = entry.id;
    result[entry.id] = ownTypeNode(entry.node);
  }
  const graph = Object.freeze(result); const visiting = new Set<string>(); const complete = new Set<string>();
  const visit = (id: string, depth: number): void => {
    if (depth > maximumDepth || graph[id] === undefined) invalid(); if (complete.has(id)) return; if (visiting.has(id)) invalid(); visiting.add(id);
    const node = graph[id]!; for (const child of referencedTypes(node)) visit(child, depth + 1); visiting.delete(id); complete.add(id);
  };
  for (const id of Object.keys(graph)) visit(id, 1); return graph;
}

function ownTypeNode(node: BaseTypeNode): BaseTypeNode {
  if (!object(node) || typeof node.kind !== "string") invalid();
  const own = <T extends object>(keys: readonly string[], value: T = node as T): T => { exactTypeNode(value, keys); return Object.freeze(value); };
  switch (node.kind) {
    case "selection-query": if (![node.maximumNodes, node.maximumDepth, node.maximumLiterals, node.maximumTake].every(positiveInt)) invalid(); return own(["kind", "maximumNodes", "maximumDepth", "maximumLiterals", "maximumTake"], { ...node });
    case "selection-previous-state": if (!positiveInt(node.maximumFields)) invalid(); return own(["kind", "maximumFields"], { ...node });
    case "selection-identity": case "module-generation": case "boolean": case "decimal": case "redacted": return own(["kind"], { ...node });
    case "selection-patch": if (!dynamicTypeId(node.patchTypeId)) invalid(); return own(["kind", "patchTypeId"], { ...node });
    case "string": if (!nonnegativeInt(node.minLength) || !nonnegativeInt(node.maxLength) || node.minLength > node.maxLength || !boundedText(node.format, 128)) invalid(); return own(["kind", "minLength", "maxLength", "format"], { ...node });
    case "integer": if (!integerText(node.minimum) || !integerText(node.maximum) || BigInt(node.minimum) > BigInt(node.maximum) || !["number", "decimal-string"].includes(node.wire)) invalid(); return own(["kind", "minimum", "maximum", "wire"], { ...node });
    case "floating": if (!["binary32", "binary64"].includes(node.precision) || node.finiteOnly !== true) invalid(); return own(["kind", "precision", "finiteOnly"], { ...node });
    case "bytes": if (node.wire !== "base64" || !positiveInt(node.maxBytes)) invalid(); return own(["kind", "wire", "maxBytes"], { ...node });
    case "canonicalJson": return own(["kind", "canonicalJsonShape"], { ...node, canonicalJsonShape: ownCanonicalJsonShape(node.canonicalJsonShape) });
    case "subjectReference": if (!dynamicTypeId(node.contractId) || !positiveInt(node.contractVersion) || !["ordinalString", "guid", "uint64"].includes(node.subjectIdKind) || !positiveInt(node.maximumSubjectIdUtf8Bytes) || node.authorityEpochBytes !== 16 || node.incarnationBytes !== 24) invalid(); return own(["kind", "contractId", "contractVersion", "subjectIdKind", "maximumSubjectIdUtf8Bytes", "authorityEpochBytes", "incarnationBytes"], { ...node });
    case "literal": if (node.value !== null && typeof node.value !== "string" && typeof node.value !== "boolean") invalid(); return own(["kind", "value"], { ...node });
    case "enum": if (!Array.isArray(node.values) || node.values.length < 1 || node.values.length > 256 || node.values.some(value => !boundedText(value, 256)) || !canonicalStrings(node.values)) invalid(); return own(["kind", "values"], { ...node, values: Object.freeze([...node.values]) });
    case "array": if (!dynamicTypeId(node.elementTypeId) || !nonnegativeInt(node.minItems) || !nonnegativeInt(node.maxItems) || node.minItems > node.maxItems) invalid(); return own(["kind", "elementTypeId", "minItems", "maxItems"], { ...node });
    case "object": {
      if (node.additionalProperties !== false || !Array.isArray(node.properties) || node.properties.length > 256) invalid();
      const properties = node.properties.map(property => { if (!boundedText(property.name, 128) || !boundedText(property.wireName, 128) || !dynamicTypeId(property.typeId) || typeof property.required !== "boolean" || typeof property.nullable !== "boolean" || !["none", "omission", "fixed-marker"].includes(property.disclosureShape)) invalid(); return own(["name", "wireName", "typeId", "required", "nullable", "disclosureShape"], { ...property }); });
      if (!canonicalStrings(properties.map(property => property.name)) || new Set(properties.map(property => property.wireName)).size !== properties.length) invalid();
      return own(["kind", "properties", "additionalProperties"], { ...node, properties: Object.freeze(properties) });
    }
    case "union": {
      if (!boundedText(node.discriminator, 128) || !Array.isArray(node.variants) || node.variants.length < 1 || node.variants.length > 64) invalid();
      const variants = node.variants.map(variant => { if (!boundedText(variant.tag, 128) || !dynamicTypeId(variant.typeId)) invalid(); return own(["tag", "typeId"], { ...variant }); });
      if (!canonicalStrings(variants.map(variant => variant.tag))) invalid(); return own(["kind", "discriminator", "variants"], { ...node, variants: Object.freeze(variants) });
    }
    default: return invalid();
  }
}

function positiveInt(value: unknown): value is number { return Number.isInteger(value) && (value as number) > 0 && (value as number) <= 2_147_483_647; }
function nonnegativeInt(value: unknown): value is number { return Number.isInteger(value) && (value as number) >= 0 && (value as number) <= 2_147_483_647; }
function integerText(value: unknown): value is string { return typeof value === "string" && /^-?(?:0|[1-9][0-9]*)$/u.test(value) && value !== "-0"; }
function dynamicTypeId(value: unknown): value is string { return typeof value === "string" && new TextEncoder().encode(value).length <= 128 && /^[a-z][a-z0-9]*(?:[.-][a-z0-9]+)*$/u.test(value); }
function canonicalStrings(values: readonly string[]): boolean { return new Set(values).size === values.length && values.every((value, index) => index === 0 || values[index - 1]! < value); }
function referencedTypes(node: BaseTypeNode): readonly string[] { switch (node.kind) { case "selection-patch": return [node.patchTypeId]; case "array": return [node.elementTypeId]; case "object": return node.properties.map(property => property.typeId); case "union": return node.variants.map(variant => variant.typeId); default: return []; } }
function exactTypeNode(value: unknown, keys: readonly string[]): void { if (!object(value)) invalid(); const actual = Object.keys(value).sort(); const expected = [...keys].sort(); if (actual.length !== expected.length || actual.some((key, index) => key !== expected[index])) invalid(); }

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

declare const canonicalJsonNumberBrand: unique symbol;
/** An exact BASE canonical-JSON number that is not losslessly representable as a JavaScript number. */
export type BaseCanonicalJsonNumber = Readonly<{
  readonly canonical: string;
  readonly [canonicalJsonNumberBrand]: true;
}>;
const canonicalJsonNumberMarker = Symbol("BaseCanonicalJsonNumber");
/** Creates an exact canonical-JSON number from one canonical L44 number token. */
export function baseCanonicalJsonNumber(canonical: string): BaseCanonicalJsonNumber {
  if (!canonicalBaseJsonNumber(canonical)) invalid();
  return Object.freeze({ canonical, [canonicalJsonNumberMarker]: true }) as unknown as BaseCanonicalJsonNumber;
}
/** Returns whether a value is an exact BASE canonical-JSON number. */
export function isBaseCanonicalJsonNumber(value: unknown): value is BaseCanonicalJsonNumber {
  return typeof value === "object" && value !== null && !Array.isArray(value)
    && (value as Record<PropertyKey, unknown>)[canonicalJsonNumberMarker] === true
    && typeof (value as { readonly canonical?: unknown }).canonical === "string"
    && Object.getOwnPropertySymbols(value).length === 1
    && Object.keys(value).length === 1
    && canonicalBaseJsonNumber((value as { readonly canonical: string }).canonical);
}

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
    case "module-generation": if (typeof value !== "string" || !/^[1-9][0-9]{0,18}$/u.test(value) || BigInt(value) > 9223372036854775807n) invalid(); return value;
    case "boolean": if (typeof value !== "boolean") invalid(); return value;
    case "string": if (typeof value !== "string" || scalarLength(value) < node.minLength || scalarLength(value) > node.maxLength || !format(value, node.format)) invalid(); return value;
    case "integer": return integer(value, node);
    case "decimal": if (typeof value !== "string" || !/^-?(?:0|[1-9][0-9]*)(?:\.[0-9]+)?$/u.test(value) || value === "-0") invalid(); return value;
    case "floating": return floating(value, node.precision);
    case "bytes": return decodeBytes(value, node.maxBytes, shape);
    case "canonicalJson": return canonicalJsonValue(value, node.canonicalJsonShape, shape);
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
      case "module-generation": return JSON.stringify(decodeNode(value, typeId, graph, "application"));
      case "boolean": case "string": case "decimal": case "literal": case "enum": return JSON.stringify(decodeNode(value, typeId, graph, "application"));
      case "bytes": return JSON.stringify(encodeBytes(value, node.maxBytes));
      case "canonicalJson": return canonicalJsonText(value, node.canonicalJsonShape, "application");
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

function ownCanonicalJsonShape(value: BaseCanonicalJsonShape): BaseCanonicalJsonShape {
  if (!object(value)) invalid(); exactTypeNode(value, ["jsonShape", "maximumCanonicalJsonBytes", "maximumJsonDepth",
    "maximumJsonArrayItems", "maximumJsonObjectProperties", "maximumJsonTotalNodes",
    "maximumJsonTotalStringUtf8Bytes", "maximumJsonTotalNameUtf8Bytes", "checksum"]);
  if (![0, 1, 2, "object", "array", "objectOrArray"].includes(value.jsonShape)
    || ![value.maximumCanonicalJsonBytes, value.maximumJsonDepth, value.maximumJsonArrayItems,
      value.maximumJsonObjectProperties, value.maximumJsonTotalNodes, value.maximumJsonTotalStringUtf8Bytes,
      value.maximumJsonTotalNameUtf8Bytes].every(positiveInt)
    || typeof value.checksum !== "string" || !/^[0-9a-f]{64}$/u.test(value.checksum)) invalid();
  return Object.freeze({ ...value });
}

function canonicalJsonValue(value: unknown, limits: BaseCanonicalJsonShape, shape: "application" | "wire"): unknown {
  canonicalJsonText(value, limits, shape);
  return deepOwnJson(materializeCanonicalJson(value));
}

function canonicalJsonText(value: unknown, limits: BaseCanonicalJsonShape, shape: "application" | "wire"): string {
  let nodes = 0; let strings = 0; let names = 0; const encoder = new TextEncoder();
  const visit = (item: unknown, depth: number): string => {
    if (++nodes > limits.maximumJsonTotalNodes || depth > limits.maximumJsonDepth) invalid();
    if (item === null) return "null";
    if (typeof item === "boolean") return item ? "true" : "false";
    if (typeof item === "string") { validUnicode(item); strings += encoder.encode(item).length; if (strings > limits.maximumJsonTotalStringUtf8Bytes) invalid(); return JSON.stringify(item); }
    if (isRawNumber(item)) { if (shape !== "wire" || !canonicalBaseJsonNumber(item.token)) invalid(); return item.token; }
    if (isBaseCanonicalJsonNumber(item)) { if (shape !== "application") invalid(); return item.canonical; }
    if (typeof item === "number") { const token = Object.is(item, -0) ? "0" : item.toString(); if (shape !== "application" || !canonicalBaseJsonNumber(token)) invalid(); return token; }
    if (Array.isArray(item)) { if (item.length > limits.maximumJsonArrayItems) invalid(); return `[${item.map(child => visit(child, depth + 1)).join(",")}]`; }
    if (!object(item)) invalid(); const keys = Object.keys(item); if (keys.length > limits.maximumJsonObjectProperties) invalid();
    const sorted = [...keys].sort(); return `{${sorted.map(key => { validUnicode(key); nodes++; names += encoder.encode(key).length; if (nodes > limits.maximumJsonTotalNodes || names > limits.maximumJsonTotalNameUtf8Bytes) invalid(); return `${JSON.stringify(key)}:${visit(item[key], depth + 1)}`; }).join(",")}}`;
  };
  const objectShape = object(value); const arrayShape = Array.isArray(value); const admitted = limits.jsonShape === 0 || limits.jsonShape === "object" ? objectShape
    : limits.jsonShape === 1 || limits.jsonShape === "array" ? arrayShape : objectShape || arrayShape;
  if (!admitted) invalid(); const text = visit(value, 1); if (encoder.encode(text).length > limits.maximumCanonicalJsonBytes) invalid(); return text;
}

function canonicalBaseJsonNumber(value: string): boolean {
  if (!/^-?(?:0|[1-9][0-9]*)(?:\.[0-9]{1,28})?$/u.test(value) || value === "-0" || value.endsWith("0") && value.includes(".")) return false;
  const digits = value.replace(/[-.]/gu, "").replace(/^0+/u, "") || "0";
  try {
    const coefficient = BigInt(digits);
    return value.startsWith("-")
      ? coefficient <= 170141183460469231731687303715884105728n
      : coefficient <= 170141183460469231731687303715884105727n;
  } catch { return false; }
}

function deepOwnJson(value: unknown): unknown {
  if (isBaseCanonicalJsonNumber(value)) return value;
  if (Array.isArray(value)) return Object.freeze(value.map(deepOwnJson));
  if (object(value)) return Object.freeze(Object.fromEntries(Object.entries(value).map(([key, item]) => [key, deepOwnJson(item)])));
  return value;
}

function materializeCanonicalJson(value: unknown): unknown {
  if (isRawNumber(value)) {
    if (!canonicalBaseJsonNumber(value.token)) invalid();
    const numeric = Number(value.token);
    if (Number.isFinite(numeric) && !Object.is(numeric, -0)
      && numeric.toString() === value.token
      && (value.token.includes(".") || Number.isSafeInteger(numeric))) return numeric;
    return baseCanonicalJsonNumber(value.token);
  }
  if (Array.isArray(value)) return value.map(materializeCanonicalJson);
  if (object(value)) return Object.fromEntries(Object.entries(value).map(([key, item]) => [key, materializeCanonicalJson(item)]));
  return value;
}

function subjectReference(value: unknown, node: Extract<BaseTypeNode, { kind: "subjectReference" }>): BaseSubjectReference {
  if (!object(value)) invalid(); exact(value, ["subjectId", "authorityEpoch", "incarnation"]);
  if (typeof value.subjectId !== "string" || new TextEncoder().encode(value.subjectId).length < 1
    || new TextEncoder().encode(value.subjectId).length > node.maximumSubjectIdUtf8Bytes
    || typeof value.authorityEpoch !== "string" || typeof value.incarnation !== "string"
    || !/^[A-Za-z0-9_-]{22}$/u.test(value.authorityEpoch) || !/^[A-Za-z0-9_-]{32}$/u.test(value.incarnation)
    || !canonicalBase64Url(value.authorityEpoch, 16) || !canonicalBase64Url(value.incarnation, 24)
    || node.authorityEpochBytes !== 16 || node.incarnationBytes !== 24 || !Number.isSafeInteger(node.contractVersion) || node.contractVersion < 1)
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
function canonicalBase64Url(value: string, expectedBytes: number): boolean {
  try {
    const padding = "=".repeat((4 - value.length % 4) % 4);
    const base64 = value.replaceAll("-", "+").replaceAll("_", "/") + padding;
    const bytes = Uint8Array.from(atob(base64), character => character.charCodeAt(0));
    if (bytes.length !== expectedBytes) return false;
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
function format(value: string, kind: string): boolean {
  if (/^(?:record-id|collection-id|field-id|revision|cursor|consistency-token|mutation-id|dependency-reference)$/u.test(kind))
    return value.length > 0 && !/[\u0000-\u001f\u007f]/u.test(value);
  if (kind === "utc-instant") return !Number.isNaN(Date.parse(value)) && /(?:Z|[+-]\d\d:\d\d)$/u.test(value);
  if (kind === "sha256") return /^[0-9a-f]{64}$/u.test(value);
  if (kind === "optional-sha256") return value.length === 0 || /^[0-9a-f]{64}$/u.test(value);
  if (kind === "nfc-text" || kind === "nfc-search" || kind === "studio-resource-summary")
    return unicodeScalarText(value) && value.normalize("NFC") === value && !/[\p{Cc}]/u.test(value);
  if (kind === "safe-error-code") return /^[a-z][a-z0-9]*(?:[.-][a-z0-9]+)*$/u.test(value);
  if (kind === "opaque-search-cursor" || kind === "studio-resource-token") return /^[A-Za-z0-9_-]*$/u.test(value);
  if (kind === "forward-reference") return false;
  return kind === "plain";
}
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
function object(value: unknown): value is Record<string, unknown> { return typeof value === "object" && value !== null && !Array.isArray(value) && !isRawNumber(value) && !isBaseCanonicalJsonNumber(value); }
function invalid(): never { throw new TypeError("base.client.responseInvalid"); }
