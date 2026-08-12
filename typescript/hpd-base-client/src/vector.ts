import type { BaseResult } from "./result.js";
import type { BaseVectorIndexDefinition } from "./schema.js";
import { BaseHttpTransport } from "./transport.js";
import { decodeBaseValue, encodeBaseJson, materializeBaseJsonValue, type BaseTypeGraph } from "./codec.js";

export type BaseVectorConsistency = { readonly kind: "current" } | { readonly kind: "available" } | { readonly kind: "atLeast"; readonly token: string } | { readonly kind: "boundedStaleness"; readonly maximumAgeMs: number };
export type BaseVectorMeasureDisclosure = "omit" | "include";
export interface BaseVectorMeasure { readonly function: "cosineSimilarity" | "dotProductSimilarity" | "euclideanDistance"; readonly value: number; readonly direction: "higherIsNearer" | "lowerIsNearer"; readonly normalizedRelevance?: number; }
export interface BaseVectorMatch<T> { readonly record: T; readonly rank: number; readonly measure?: BaseVectorMeasure; }
export interface BaseVectorResult<T> { readonly matches: readonly BaseVectorMatch<T>[]; readonly vectorIndexId: string; readonly vectorIndexGeneration: string; readonly providerId: string; readonly consistencyToken: string; }

export class BaseVectorIndexQuery<T> {
  public constructor(private readonly transport: BaseHttpTransport, private readonly collectionId: string, private readonly index: BaseVectorIndexDefinition, private readonly project: (value: unknown) => T) {}
  public nearest(vector: readonly number[]): BaseVectorQuery<T> { return new BaseVectorQuery<T>(this.transport, this.collectionId, this.index, vector, [], 10, { kind: "current" }, "omit", this.project); }
}

export class BaseVectorQuery<T> {
  readonly #vector: readonly number[];
  readonly #filters: readonly unknown[];
  readonly #take: number;
  readonly #consistency: BaseVectorConsistency;
  readonly #disclosure: BaseVectorMeasureDisclosure;
  readonly #project: (value: unknown) => T;
  public constructor(private readonly transport: BaseHttpTransport, private readonly collectionId: string, private readonly index: BaseVectorIndexDefinition, vector: readonly number[], filters: readonly unknown[] = [], take = 10, consistency: BaseVectorConsistency = { kind: "current" }, disclosure: BaseVectorMeasureDisclosure = "omit", project?: (value: unknown) => T) {
    if (project === undefined) throw new TypeError("base.client.configurationInvalid");
    this.#project = project;
    if (vector.length !== index.dimensions || vector.some(value => !Number.isFinite(value) || (value !== 0 && Math.fround(value) === 0) || !Number.isFinite(Math.fround(value)))) throw new TypeError("base.vector.invalid");
    this.#vector = Object.freeze(vector.map(value => Object.is(value, -0) ? 0 : Math.fround(value)));
    this.#filters = filters; this.#take = take; this.#consistency = consistency; this.#disclosure = disclosure;
  }
  public where(filter: { readonly kind: "compare"; readonly field: string; readonly operator: string; readonly value?: unknown }): BaseVectorQuery<T> {
    if (filter.operator !== "equal") throw new TypeError("base.vector.filterUnsupported");
    return new BaseVectorQuery(this.transport, this.collectionId, this.index, this.#vector, [...this.#filters, encodeFilter(filter)], this.#take, this.#consistency, this.#disclosure, this.#project);
  }
  public take(value: number): BaseVectorQuery<T> { if (!Number.isInteger(value) || value < 1) throw new RangeError("base.vector.limitExceeded"); return new BaseVectorQuery(this.transport, this.collectionId, this.index, this.#vector, this.#filters, value, this.#consistency, this.#disclosure, this.#project); }
  public consistency(value: BaseVectorConsistency): BaseVectorQuery<T> { return new BaseVectorQuery(this.transport, this.collectionId, this.index, this.#vector, this.#filters, this.#take, value, this.#disclosure, this.#project); }
  public measures(value: BaseVectorMeasureDisclosure): BaseVectorQuery<T> { return new BaseVectorQuery(this.transport, this.collectionId, this.index, this.#vector, this.#filters, this.#take, this.#consistency, value, this.#project); }
  public async execute(signal?: AbortSignal): Promise<BaseResult<BaseVectorResult<T>>> {
    const consistency = this.#consistency.kind === "atLeast" ? "atLeast" : this.#consistency.kind;
    const body = { vector: this.#vector, filters: this.#filters, take: this.#take, consistency, ...(this.#consistency.kind === "atLeast" ? { consistencyToken: this.#consistency.token } : {}), ...(this.#consistency.kind === "boundedStaleness" ? { maximumAgeMilliseconds: this.#consistency.maximumAgeMs } : {}), measureDisclosure: this.#disclosure };
    const vectorGraph: BaseTypeGraph = { element: { kind: "floating", precision: "binary32", finiteOnly: true }, vector: { kind: "array", elementTypeId: "element", maxItems: this.index.dimensions } };
    const bodyJson = JSON.stringify({ ...body, vector: undefined }).replace(/,"vector"(?::undefined)?|"vector":undefined,?/u, "");
    const encoded = `{\"vector\":${encodeBaseJson(this.#vector, "vector", vectorGraph)}${bodyJson === "{}" ? "" : `,${bodyJson.slice(1, -1)}`}}`;
    const result = await this.transport.jsonDocument("POST", `vector/${encodeURIComponent(this.collectionId)}/${encodeURIComponent(this.index.id)}/query`, new TextEncoder().encode(encoded), signal);
    if (!result.ok) return result;
    const decoded = decodeVectorResult(result.value, this.index, this.#disclosure, this.#take);
    if (decoded === undefined) return { ok: false, error: { code: "base.client.responseInvalid", category: "unexpected", message: "The BASE response was invalid." }, correlationId: result.correlationId, retry: "never" };
    try { return { ...result, value: { ...decoded, matches: decoded.matches.map(match => ({ ...match, record: this.#project(match.record) })) } }; }
    catch { return { ok: false, error: { code: "base.client.responseInvalid", category: "unexpected", message: "The BASE response was invalid." }, correlationId: result.correlationId, retry: "never" }; }
  }
}

function decodeVectorResult(value: unknown, index: BaseVectorIndexDefinition, disclosure: BaseVectorMeasureDisclosure, take: number): BaseVectorResult<unknown> | undefined {
  if (!record(value) || !only(value, ["matches", "vectorIndexId", "vectorIndexGeneration", "providerId", "consistencyToken"]) || value.vectorIndexId !== index.id || typeof value.vectorIndexGeneration !== "string" || !/^(?:0|[1-9][0-9]*)$/u.test(value.vectorIndexGeneration) || !text(value.providerId) || !text(value.consistencyToken) || !Array.isArray(value.matches) || value.matches.length > take) return undefined;
  const floatGraph: BaseTypeGraph = { f64: { kind: "floating", precision: "binary64", finiteOnly: true } }; const matches: BaseVectorMatch<unknown>[] = [];
  try {
    for (const [offset, match] of value.matches.entries()) {
      if (!record(match) || !only(match, ["record", "rank", "measure"]) || materializeBaseJsonValue(match.rank) !== offset + 1 || !record(match.record)) return undefined;
      if (disclosure === "omit") { if (match.measure !== undefined) return undefined; matches.push({ record: match.record, rank: offset + 1 }); continue; }
      const measure = match.measure; if (!record(measure) || !only(measure, ["function", "value", "direction", "normalizedRelevance"]) || measure.function !== index.measure || measure.direction !== index.direction) return undefined;
      const numeric = decodeBaseValue<number>(measure.value, "f64", floatGraph); const normalized = measure.normalizedRelevance === undefined ? undefined : decodeBaseValue<number>(measure.normalizedRelevance, "f64", floatGraph);
      if (normalized !== undefined && (normalized < 0 || normalized > 1)) return undefined;
      matches.push({ record: match.record, rank: offset + 1, measure: { function: index.measure, value: numeric, direction: index.direction, ...(normalized === undefined ? {} : { normalizedRelevance: normalized }) } });
    }
  } catch { return undefined; }
  return { matches, vectorIndexId: index.id, vectorIndexGeneration: value.vectorIndexGeneration, providerId: value.providerId, consistencyToken: value.consistencyToken };
}

function record(value: unknown): value is Record<string, unknown> { return typeof value === "object" && value !== null && !Array.isArray(value); }
function only(value: Record<string, unknown>, keys: readonly string[]): boolean { return Object.keys(value).every(key => keys.includes(key)); }
function text(value: unknown): value is string { return typeof value === "string" && value.length > 0 && value.length <= 4096 && !/[\u0000-\u001f\u007f]/u.test(value); }

function encodeFilter(filter: { readonly field: string; readonly value?: unknown }): object {
  const raw = filter.value as { readonly kind?: string; readonly string?: string; readonly boolean?: boolean; readonly integer?: number } | undefined;
  const value = raw?.kind === "string" ? raw.string : raw?.kind === "boolean" ? raw.boolean : raw?.kind === "integer" ? raw.integer : raw?.kind === "null" ? null : undefined;
  const wire = value === null ? { kind: "null" }
    : typeof value === "string" ? { kind: "string", text: value }
    : typeof value === "boolean" ? { kind: "boolean", boolean: value }
    : typeof value === "number" && Number.isSafeInteger(value) ? { kind: "integer", integer: value }
    : (() => { throw new TypeError("base.vector.filterUnsupported"); })();
  return { fieldId: filter.field, value: wire };
}
