import type { BaseResult } from "./result.js";
import type { BaseTextIndexDefinition } from "./schema.js";
import { BaseHttpTransport } from "./transport.js";

export type BaseTextQuery =
  | { readonly kind: "term" | "prefix"; readonly value: string }
  | { readonly kind: "phrase"; readonly terms: readonly string[] }
  | { readonly kind: "field"; readonly field: string; readonly child: BaseTextQuery }
  | { readonly kind: "and" | "or"; readonly children: readonly BaseTextQuery[] }
  | { readonly kind: "not"; readonly child: BaseTextQuery };
export type BaseTextFilterValue = { readonly kind: "string" | "id"; readonly text: string } | { readonly kind: "boolean"; readonly boolean: boolean } | { readonly kind: "integer"; readonly integer: number };
export type BaseTextFilter = { readonly kind: "missing" | "null"; readonly field: string } | { readonly kind: "equal"; readonly field: string; readonly value: BaseTextFilterValue } | { readonly kind: "in"; readonly field: string; readonly values: readonly BaseTextFilterValue[] } | { readonly kind: "and" | "or"; readonly children: readonly BaseTextFilter[] };
export interface BaseTextOrder { readonly field: string; readonly direction: "asc" | "desc"; readonly nullOrder: "unspecified" | "first" | "last"; }
export type BaseTextConsistency = "current" | "available" | "atLeast" | "boundedStaleness";
export interface BaseTextQueryRequest { readonly query: BaseTextQuery; readonly filter?: BaseTextFilter; readonly order?: readonly BaseTextOrder[]; readonly take: number; readonly cursor?: string; readonly consistency?: BaseTextConsistency; readonly consistencyToken?: string; readonly maximumAgeMilliseconds?: number; }
export interface BaseTextMatch<T> { readonly record: T; readonly revision: string; readonly scoreUnits: string; }
export interface BaseTextResult<T> { readonly matches: readonly BaseTextMatch<T>[]; readonly next?: string | null; readonly consistencyToken: string; }

export class BaseTextIndexQuery<T> {
  public constructor(private readonly transport: BaseHttpTransport, private readonly collectionId: string, private readonly index: BaseTextIndexDefinition, private readonly project: (value: unknown) => T) {}
  public async search(request: BaseTextQueryRequest, signal?: AbortSignal): Promise<BaseResult<BaseTextResult<T>>> {
    validateRequest(request, this.index);
    const body = new TextEncoder().encode(JSON.stringify({ indexId: this.index.id, ...request, order: request.order ?? [], consistency: request.consistency ?? "current" }));
    const result = await this.transport.jsonDocument("POST", `text/${encodeURIComponent(this.collectionId)}/${encodeURIComponent(this.index.id)}/query`, body, signal);
    if (!result.ok) return result;
    const decoded = decode<T>(result.value, request.take, this.project);
    return decoded === undefined ? { ok: false, error: { code: "base.client.responseInvalid", category: "unexpected", message: "The BASE response was invalid." }, correlationId: result.correlationId, retry: "never" } : { ...result, value: decoded };
  }
}

function validateRequest(request: BaseTextQueryRequest, index: BaseTextIndexDefinition): void {
  if (!Number.isInteger(request.take) || request.take < 1 || request.take > index.maximumResults || (request.order?.length ?? 0) > 8) throw new RangeError("base.text.budgetExceeded");
  if (request.order?.some(value => !Object.values(index.filterFields).some(field => field.wireName === value.field || field.id === value.field)) === true) throw new TypeError("base.text.queryInvalid");
}
function decode<T>(value: unknown, take: number, project: (value: unknown) => T): BaseTextResult<T> | undefined {
  if (!record(value) || !only(value, ["matches", "next", "consistencyToken"]) || !Array.isArray(value.matches) || value.matches.length > take || typeof value.consistencyToken !== "string" || value.next !== undefined && value.next !== null && typeof value.next !== "string") return undefined;
  const matches: BaseTextMatch<T>[] = [];
  try { for (const item of value.matches) { if (!record(item) || !only(item, ["record", "revision", "scoreUnits"]) || !record(item.record) || typeof item.revision !== "string" || typeof item.scoreUnits !== "string" || !/^(?:0|[1-9][0-9]*)$/u.test(item.scoreUnits)) return undefined; matches.push({ record: project(item.record), revision: item.revision, scoreUnits: item.scoreUnits }); } }
  catch { return undefined; }
  return { matches, ...(value.next === undefined ? {} : { next: value.next as string | null }), consistencyToken: value.consistencyToken };
}
function record(value: unknown): value is Record<string, unknown> { return typeof value === "object" && value !== null && !Array.isArray(value); }
function only(value: Record<string, unknown>, keys: readonly string[]): boolean { return Object.keys(value).every(key => keys.includes(key)); }
