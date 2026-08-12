import type { BaseResult } from "./result.js";
import type { BaseVectorIndexDefinition } from "./schema.js";
import { BaseHttpTransport } from "./transport.js";

export type BaseVectorConsistency = { readonly kind: "current" } | { readonly kind: "available" } | { readonly kind: "atLeast"; readonly token: string } | { readonly kind: "boundedStaleness"; readonly maximumAgeMs: number };
export type BaseVectorMeasureDisclosure = "omit" | "include";
export interface BaseVectorMeasure { readonly function: "cosineSimilarity" | "dotProductSimilarity" | "euclideanDistance"; readonly value: number; readonly direction: "higherIsNearer" | "lowerIsNearer"; readonly normalizedRelevance?: number; }
export interface BaseVectorMatch<T> { readonly record: T; readonly rank: number; readonly measure?: BaseVectorMeasure; }
export interface BaseVectorResult<T> { readonly matches: readonly BaseVectorMatch<T>[]; readonly vectorIndexId: string; readonly vectorIndexGeneration: string; readonly providerId: string; readonly consistencyToken: string; }

export class BaseVectorIndexQuery<T> {
  public constructor(private readonly transport: BaseHttpTransport, private readonly collectionId: string, private readonly index: BaseVectorIndexDefinition, private readonly project: (value: unknown) => T = value => value as T) {}
  public nearest(vector: readonly number[]): BaseVectorQuery<T> { return new BaseVectorQuery<T>(this.transport, this.collectionId, this.index, vector, [], 10, { kind: "current" }, "omit", this.project); }
}

export class BaseVectorQuery<T> {
  readonly #vector: readonly number[];
  readonly #filters: readonly unknown[];
  readonly #take: number;
  readonly #consistency: BaseVectorConsistency;
  readonly #disclosure: BaseVectorMeasureDisclosure;
  public constructor(private readonly transport: BaseHttpTransport, private readonly collectionId: string, private readonly index: BaseVectorIndexDefinition, vector: readonly number[], filters: readonly unknown[] = [], take = 10, consistency: BaseVectorConsistency = { kind: "current" }, disclosure: BaseVectorMeasureDisclosure = "omit", private readonly project: (value: unknown) => T = value => value as T) {
    if (vector.length !== index.dimensions || vector.some(value => !Number.isFinite(value) || (value !== 0 && Math.fround(value) === 0) || !Number.isFinite(Math.fround(value)))) throw new TypeError("base.vector.invalid");
    this.#vector = Object.freeze(vector.map(value => Object.is(value, -0) ? 0 : Math.fround(value)));
    this.#filters = filters; this.#take = take; this.#consistency = consistency; this.#disclosure = disclosure;
  }
  public where(filter: { readonly kind: "compare"; readonly field: string; readonly operator: string; readonly value?: unknown }): BaseVectorQuery<T> {
    if (filter.operator !== "equal") throw new TypeError("base.vector.filterUnsupported");
    return new BaseVectorQuery(this.transport, this.collectionId, this.index, this.#vector, [...this.#filters, encodeFilter(filter)], this.#take, this.#consistency, this.#disclosure, this.project);
  }
  public take(value: number): BaseVectorQuery<T> { if (!Number.isInteger(value) || value < 1) throw new RangeError("base.vector.limitExceeded"); return new BaseVectorQuery(this.transport, this.collectionId, this.index, this.#vector, this.#filters, value, this.#consistency, this.#disclosure, this.project); }
  public consistency(value: BaseVectorConsistency): BaseVectorQuery<T> { return new BaseVectorQuery(this.transport, this.collectionId, this.index, this.#vector, this.#filters, this.#take, value, this.#disclosure, this.project); }
  public measures(value: BaseVectorMeasureDisclosure): BaseVectorQuery<T> { return new BaseVectorQuery(this.transport, this.collectionId, this.index, this.#vector, this.#filters, this.#take, this.#consistency, value, this.project); }
  public async execute(signal?: AbortSignal): Promise<BaseResult<BaseVectorResult<T>>> {
    const consistency = this.#consistency.kind === "atLeast" ? "atLeast" : this.#consistency.kind;
    const body = { vector: this.#vector, filters: this.#filters, take: this.#take, consistency, ...(this.#consistency.kind === "atLeast" ? { consistencyToken: this.#consistency.token } : {}), ...(this.#consistency.kind === "boundedStaleness" ? { maximumAgeMilliseconds: this.#consistency.maximumAgeMs } : {}), measureDisclosure: this.#disclosure };
    const result = await this.transport.json<BaseVectorResult<T>>("POST", `vector/${encodeURIComponent(this.collectionId)}/${encodeURIComponent(this.index.id)}/query`, new TextEncoder().encode(JSON.stringify(body)), signal);
    if (!result.ok) return result;
    if (!validVectorResult(result.value, this.index, this.#disclosure)) return { ok: false, error: { code: "base.client.responseInvalid", category: "unexpected", message: "The BASE response was invalid." }, correlationId: result.correlationId, retry: "never" };
    return { ...result, value: { ...result.value, matches: result.value.matches.map(match => ({ ...match, record: this.project(match.record) })) } };
  }
}

function validVectorResult<T>(value: BaseVectorResult<T>, index: BaseVectorIndexDefinition, disclosure: BaseVectorMeasureDisclosure): boolean {
  if (typeof value !== "object" || value === null || value.vectorIndexId !== index.id || !/^(?:0|[1-9][0-9]*)$/u.test(value.vectorIndexGeneration) || typeof value.providerId !== "string" || typeof value.consistencyToken !== "string" || !Array.isArray(value.matches)) return false;
  return value.matches.every((match, offset) => {
    if (typeof match !== "object" || match === null || match.rank !== offset + 1 || typeof match.record !== "object" || match.record === null) return false;
    if (disclosure === "omit") return match.measure === undefined;
    const measure = match.measure;
    return measure !== undefined && measure.function === index.measure && measure.direction === index.direction && Number.isFinite(measure.value) && (measure.normalizedRelevance === undefined || Number.isFinite(measure.normalizedRelevance));
  });
}

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
