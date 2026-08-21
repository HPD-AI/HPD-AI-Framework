import type { BaseResult } from "./result.js";
import type { BaseTextIndexDefinition } from "./schema.js";
import { encodeBaseJson, type BaseTypeGraph } from "./codec.js";
import { BaseHttpTransport } from "./transport.js";

export type BaseTextQuery<TField extends string = string> =
  | { readonly kind: "term" | "prefix"; readonly value: string }
  | { readonly kind: "phrase"; readonly terms: readonly string[] }
  | { readonly kind: "field"; readonly field: TField; readonly child: BaseTextQuery<TField> }
  | { readonly kind: "and" | "or"; readonly children: readonly BaseTextQuery<TField>[] }
  | { readonly kind: "not"; readonly child: BaseTextQuery<TField> };
export type BaseTextFilterValue = { readonly kind: "string" | "id"; readonly text: string } | { readonly kind: "boolean"; readonly boolean: boolean } | { readonly kind: "integer"; readonly integer: number };
export type BaseTextFilter<TField extends string = string> = { readonly kind: "missing" | "null"; readonly field: TField } | { readonly kind: "equal"; readonly field: TField; readonly value: BaseTextFilterValue } | { readonly kind: "in"; readonly field: TField; readonly values: readonly BaseTextFilterValue[] } | { readonly kind: "and" | "or"; readonly children: readonly BaseTextFilter<TField>[] };
export interface BaseTextOrder<TField extends string = string> { readonly field: TField; readonly direction: "asc" | "desc"; readonly nullOrder: "unspecified" | "first" | "last"; }
export type BaseTextConsistency = "current" | "available" | "atLeast" | "boundedStaleness";
export interface BaseTextQueryRequest<TSearchField extends string = string, TFilterField extends string = string> { readonly query: BaseTextQuery<TSearchField>; readonly filter?: BaseTextFilter<TFilterField>; readonly order?: readonly BaseTextOrder<TFilterField>[]; readonly take: number; readonly cursor?: string; readonly consistency?: BaseTextConsistency; readonly consistencyToken?: string; readonly maximumAgeMilliseconds?: number; }
export interface BaseTextMatch<T> { readonly record: T; readonly revision: string; readonly scoreUnits: string; }
export interface BaseTextResult<T> { readonly matches: readonly BaseTextMatch<T>[]; readonly next?: string | null; readonly consistencyToken: string; }

type SearchFieldOf<T extends BaseTextIndexDefinition> = T["fields"][keyof T["fields"]]["id" | "wireName"];
type FilterFieldOf<T extends BaseTextIndexDefinition> = T["filterFields"][keyof T["filterFields"]]["id" | "wireName"];
export class BaseTextIndexQuery<T, TIndex extends BaseTextIndexDefinition = BaseTextIndexDefinition> {
  public constructor(private readonly transport: BaseHttpTransport, private readonly collectionId: string, private readonly index: TIndex, private readonly project: (value: unknown) => T) {}
  public async search(request: BaseTextQueryRequest<SearchFieldOf<TIndex>, FilterFieldOf<TIndex>>, signal?: AbortSignal): Promise<BaseResult<BaseTextResult<T>>> {
    validateRequest(request, this.index);
    const value = { indexId: this.index.id, query: request.query, ...(request.filter === undefined ? {} : { filter: request.filter }), order: request.order ?? [], take: request.take, ...(request.cursor === undefined ? {} : { cursor: request.cursor }), consistency: request.consistency ?? "current", ...(request.consistencyToken === undefined ? {} : { consistencyToken: request.consistencyToken }), ...(request.maximumAgeMilliseconds === undefined ? {} : { maximumAgeMilliseconds: request.maximumAgeMilliseconds }) };
    const body = new TextEncoder().encode(encodeBaseJson(value, "text.request", requestGraph(this.index)));
    const result = await this.transport.jsonDocument("POST", `text/${encodeURIComponent(this.collectionId)}/${encodeURIComponent(this.index.id)}/query`, body, signal);
    if (!result.ok) return result;
    const decoded = decode<T>(result.value, request.take, this.project);
    return decoded === undefined ? { ok: false, error: { code: "base.client.responseInvalid", category: "unexpected", message: "The BASE response was invalid." }, correlationId: result.correlationId, retry: "never" } : { ...result, value: decoded };
  }
}

function validateRequest(request: BaseTextQueryRequest, index: BaseTextIndexDefinition): void {
  if (!record(request) || !only(request, ["query", "filter", "order", "take", "cursor", "consistency", "consistencyToken", "maximumAgeMilliseconds"]) || !Number.isInteger(request.take) || request.take < 1 || request.take > index.maximumResults) budget();
  const searchable = new Set(Object.values(index.fields).flatMap(field => [field.id, field.wireName])); const filters = new Map(Object.values(index.filterFields).flatMap(field => [[field.id, field], [field.wireName, field]]));
  const query = queryShape(request.query, searchable, index, 1); if (query.nodes > index.maximumQueryNodes || query.depth > index.maximumQueryDepth || query.bytes > index.maximumQueryBytes) budget();
  if (request.filter !== undefined) { const filter = filterShape(request.filter, filters, index, 1); if (filter.nodes > index.maximumFilterNodes || filter.depth > index.maximumFilterDepth || filter.literals > index.maximumFilterLiterals) budget(); }
  if (request.order !== undefined && (!Array.isArray(request.order) || request.order.length > index.maximumSecondaryOrderFields || new Set(request.order.map(value => value.field)).size !== request.order.length || request.order.some(value => !validOrder(value, filters)))) invalid();
  if (request.cursor !== undefined && (typeof request.cursor !== "string" || !/^[A-Za-z0-9_-]+$/u.test(request.cursor) || new TextEncoder().encode(request.cursor).length > index.maximumCursorBytes)) invalid();
  const consistency = request.consistency ?? "current"; if (!["current", "available", "atLeast", "boundedStaleness"].includes(consistency)) invalid();
  if ((consistency === "atLeast") !== (typeof request.consistencyToken === "string") || consistency === "boundedStaleness" !== (Number.isInteger(request.maximumAgeMilliseconds) && request.maximumAgeMilliseconds! > 0) || consistency !== "boundedStaleness" && request.maximumAgeMilliseconds !== undefined) invalid();
}

function queryShape(value: unknown, fields: ReadonlySet<string>, index: BaseTextIndexDefinition, depth: number): { nodes: number; depth: number; bytes: number } {
  if (!record(value) || typeof value.kind !== "string") invalid(); const bytes = new TextEncoder().encode(JSON.stringify(value)).length;
  if (value.kind === "term" || value.kind === "prefix") { if (!only(value, ["kind", "value"]) || typeof value.value !== "string" || value.value.length === 0 || value.value !== value.value.normalize("NFC")) invalid(); return { nodes: 1, depth, bytes }; }
  if (value.kind === "phrase") { if (!only(value, ["kind", "terms"]) || !Array.isArray(value.terms) || value.terms.length < 1 || value.terms.length > index.maximumPhraseTerms || value.terms.some(term => typeof term !== "string" || term.length === 0 || term !== term.normalize("NFC"))) invalid(); return { nodes: 1, depth, bytes }; }
  if (value.kind === "field") { if (!only(value, ["kind", "field", "child"]) || typeof value.field !== "string" || !fields.has(value.field)) invalid(); const child = queryShape(value.child, fields, index, depth + 1); return { nodes: child.nodes + 1, depth: child.depth, bytes }; }
  if (value.kind === "not") { if (!only(value, ["kind", "child"])) invalid(); const child = queryShape(value.child, fields, index, depth + 1); return { nodes: child.nodes + 1, depth: child.depth, bytes }; }
  if (value.kind === "and" || value.kind === "or") { if (!only(value, ["kind", "children"]) || !Array.isArray(value.children) || value.children.length < 2 || value.children.length > index.maximumQueryNodes) invalid(); const children = value.children.map(child => queryShape(child, fields, index, depth + 1)); return { nodes: 1 + children.reduce((sum, child) => sum + child.nodes, 0), depth: Math.max(...children.map(child => child.depth)), bytes }; }
  return invalid();
}
function filterShape(value: unknown, fields: ReadonlyMap<string, { readonly valueKind: "String" | "Boolean" | "Integer" | "Id" }>, index: BaseTextIndexDefinition, depth: number): { nodes: number; depth: number; literals: number } {
  if (!record(value) || typeof value.kind !== "string") invalid();
  if (value.kind === "missing" || value.kind === "null") { if (!only(value, ["kind", "field"]) || typeof value.field !== "string" || !fields.has(value.field)) invalid(); return { nodes: 1, depth, literals: 0 }; }
  if (value.kind === "equal") { if (!only(value, ["kind", "field", "value"]) || typeof value.field !== "string" || !filterValue(value.value, fields.get(value.field))) invalid(); return { nodes: 1, depth, literals: 1 }; }
  if (value.kind === "in") { if (!only(value, ["kind", "field", "values"]) || typeof value.field !== "string" || !Array.isArray(value.values) || value.values.length < 1 || value.values.length > index.maximumInValues) invalid(); const field = fields.get(value.field); if (value.values.some(item => !filterValue(item, field))) invalid(); return { nodes: 1, depth, literals: value.values.length }; }
  if (value.kind === "and" || value.kind === "or") { if (!only(value, ["kind", "children"]) || !Array.isArray(value.children) || value.children.length < 2 || value.children.length > index.maximumFilterNodes) invalid(); const children = value.children.map(child => filterShape(child, fields, index, depth + 1)); return { nodes: 1 + children.reduce((sum, child) => sum + child.nodes, 0), depth: Math.max(...children.map(child => child.depth)), literals: children.reduce((sum, child) => sum + child.literals, 0) }; }
  return invalid();
}
function filterValue(value: unknown, field: { readonly valueKind: "String" | "Boolean" | "Integer" | "Id" } | undefined): boolean {
  if (!record(value) || field === undefined) return false;
  if (value.kind === "string" || value.kind === "id") return only(value, ["kind", "text"]) && typeof value.text === "string" && (field.valueKind === "String" && value.kind === "string" || field.valueKind === "Id" && value.kind === "id");
  if (value.kind === "boolean") return only(value, ["kind", "boolean"]) && typeof value.boolean === "boolean" && field.valueKind === "Boolean";
  return value.kind === "integer" && only(value, ["kind", "integer"]) && Number.isSafeInteger(value.integer) && field.valueKind === "Integer";
}
function validOrder(value: unknown, fields: ReadonlyMap<string, unknown>): boolean { return record(value) && only(value, ["field", "direction", "nullOrder"]) && typeof value.field === "string" && fields.has(value.field) && typeof value.direction === "string" && ["asc", "desc"].includes(value.direction) && typeof value.nullOrder === "string" && ["unspecified", "first", "last"].includes(value.nullOrder); }

function requestGraph(index: BaseTextIndexDefinition): BaseTypeGraph {
  const s = (maxLength: number) => ({ kind: "string" as const, minLength: 0, maxLength, format: "plain" }); const property = (name: string, typeId: string, required = true, nullable = false) => ({ name, wireName: name, typeId, required, nullable, disclosureShape: "none" as const });
  const graph: Record<string, any> = { "text.string": s(4096), "text.field": { kind: "enum", values: [...new Set(Object.values(index.fields).flatMap(field => [field.id, field.wireName]))] }, "text.filterField": { kind: "enum", values: [...new Set(Object.values(index.filterFields).flatMap(field => [field.id, field.wireName]))] }, "text.query.children": { kind: "array", elementTypeId: "text.query", minItems: 2, maxItems: index.maximumQueryNodes }, "text.phrase.terms": { kind: "array", elementTypeId: "text.string", minItems: 1, maxItems: index.maximumPhraseTerms }, "text.filter.children": { kind: "array", elementTypeId: "text.filter", minItems: 2, maxItems: index.maximumFilterNodes }, "text.filter.values": { kind: "array", elementTypeId: "text.filterValue", minItems: 1, maxItems: index.maximumInValues }, "text.orders": { kind: "array", elementTypeId: "text.order", minItems: 0, maxItems: index.maximumSecondaryOrderFields }, "text.take": { kind: "integer", minimum: "1", maximum: String(index.maximumResults), wire: "number" }, "text.age": { kind: "integer", minimum: "1", maximum: "2592000000", wire: "number" }, "text.cursor": s(index.maximumCursorBytes), "text.consistency": { kind: "enum", values: ["current", "available", "atLeast", "boundedStaleness"] }, "text.direction": { kind: "enum", values: ["asc", "desc"] }, "text.nullOrder": { kind: "enum", values: ["unspecified", "first", "last"] }, "text.bool": { kind: "boolean" }, "text.integer": { kind: "integer", minimum: "-9007199254740991", maximum: "9007199254740991", wire: "number" } };
  const object = (properties: any[]) => ({ kind: "object", properties, additionalProperties: false }); const variant = (id: string, kind: string, properties: any[]) => { graph[id] = object([property("kind", `${id}.kind`), ...properties]); graph[`${id}.kind`] = { kind: "literal", value: kind }; return { tag: kind, typeId: id }; };
  graph["text.query"] = { kind: "union", discriminator: "kind", variants: [variant("text.query.term", "term", [property("value", "text.string")]), variant("text.query.prefix", "prefix", [property("value", "text.string")]), variant("text.query.phrase", "phrase", [property("terms", "text.phrase.terms")]), variant("text.query.field", "field", [property("field", "text.field"), property("child", "text.query")]), variant("text.query.and", "and", [property("children", "text.query.children")]), variant("text.query.or", "or", [property("children", "text.query.children")]), variant("text.query.not", "not", [property("child", "text.query")])] };
  graph["text.filterValue"] = { kind: "union", discriminator: "kind", variants: [variant("text.value.string", "string", [property("text", "text.string")]), variant("text.value.id", "id", [property("text", "text.string")]), variant("text.value.boolean", "boolean", [property("boolean", "text.bool")]), variant("text.value.integer", "integer", [property("integer", "text.integer")])] };
  graph["text.filter"] = { kind: "union", discriminator: "kind", variants: [variant("text.filter.missing", "missing", [property("field", "text.filterField")]), variant("text.filter.null", "null", [property("field", "text.filterField")]), variant("text.filter.equal", "equal", [property("field", "text.filterField"), property("value", "text.filterValue")]), variant("text.filter.in", "in", [property("field", "text.filterField"), property("values", "text.filter.values")]), variant("text.filter.and", "and", [property("children", "text.filter.children")]), variant("text.filter.or", "or", [property("children", "text.filter.children")])] };
  graph["text.order"] = object([property("field", "text.filterField"), property("direction", "text.direction"), property("nullOrder", "text.nullOrder")]); graph["text.indexId"] = { kind: "literal", value: index.id };
  graph["text.request"] = object([property("indexId", "text.indexId"), property("query", "text.query"), property("filter", "text.filter", false), property("order", "text.orders"), property("take", "text.take"), property("cursor", "text.cursor", false), property("consistency", "text.consistency"), property("consistencyToken", "text.cursor", false), property("maximumAgeMilliseconds", "text.age", false)]); return graph as BaseTypeGraph;
}
function invalid(): never { throw new TypeError("base.text.queryInvalid"); }
function budget(): never { throw new RangeError("base.text.budgetExceeded"); }
function decode<T>(value: unknown, take: number, project: (value: unknown) => T): BaseTextResult<T> | undefined {
  if (!record(value) || !only(value, ["matches", "next", "consistencyToken"]) || !Array.isArray(value.matches) || value.matches.length > take || typeof value.consistencyToken !== "string" || value.next !== undefined && value.next !== null && typeof value.next !== "string") return undefined;
  const matches: BaseTextMatch<T>[] = [];
  try { for (const item of value.matches) { if (!record(item) || !only(item, ["record", "revision", "scoreUnits"]) || !record(item.record) || typeof item.revision !== "string" || typeof item.scoreUnits !== "string" || !/^(?:0|[1-9][0-9]*)$/u.test(item.scoreUnits)) return undefined; matches.push({ record: project(item.record), revision: item.revision, scoreUnits: item.scoreUnits }); } }
  catch { return undefined; }
  return { matches, ...(value.next === undefined ? {} : { next: value.next as string | null }), consistencyToken: value.consistencyToken };
}
function record(value: unknown): value is Record<string, unknown> { return typeof value === "object" && value !== null && !Array.isArray(value); }
function only(value: Record<string, unknown>, keys: readonly string[]): boolean { return Object.keys(value).every(key => keys.includes(key)); }
