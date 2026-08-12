import type { BaseCollectionDefinition, BaseFieldDefinition, BaseFieldOperator } from "./schema.js";
import type { BaseResult } from "./result.js";
import { BaseClientException } from "./result.js";

export type FieldValue<TField> = TField extends BaseFieldDefinition<infer TValue, readonly BaseFieldOperator[]> ? TValue : never;

export type BaseWhere =
  | { readonly kind: "compare"; readonly field: string; readonly operator: string; readonly value?: BaseQueryValue }
  | { readonly kind: "in"; readonly field: string; readonly values: readonly BaseQueryValue[] }
  | { readonly kind: "isNull" | "isDefined"; readonly field: string }
  | { readonly kind: "and" | "or" | "not"; readonly children: readonly BaseWhere[] };
export type BaseQueryValue = { readonly kind: "null" } | { readonly kind: "string"; readonly string: string } | { readonly kind: "boolean"; readonly boolean: boolean } | { readonly kind: "integer"; readonly integer: number } | { readonly kind: "number"; readonly number: number } | { readonly kind: "dateTime"; readonly dateTime: string } | { readonly kind: "id"; readonly id: string };

class BaseFieldHandleSurface<T> {
  public constructor(public readonly id: string) {}
  public eq(value: T): BaseWhere { return { kind: "compare", field: this.id, operator: "equal", value: queryValue(value) }; }
  public ne(value: T): BaseWhere { return { kind: "compare", field: this.id, operator: "notEqual", value: queryValue(value) }; }
  public lt(value: T): BaseWhere { return { kind: "compare", field: this.id, operator: "lessThan", value: queryValue(value) }; }
  public lte(value: T): BaseWhere { return { kind: "compare", field: this.id, operator: "lessThanOrEqual", value: queryValue(value) }; }
  public gt(value: T): BaseWhere { return { kind: "compare", field: this.id, operator: "greaterThan", value: queryValue(value) }; }
  public gte(value: T): BaseWhere { return { kind: "compare", field: this.id, operator: "greaterThanOrEqual", value: queryValue(value) }; }
  public in(values: readonly T[]): BaseWhere { if (values.length === 0) throw new TypeError("base.query.invalid"); return { kind: "in", field: this.id, values: values.map(queryValue) }; }
  public between(lower: T, upper: T): BaseWhere { return { kind: "and", children: [this.gte(lower), this.lte(upper)] }; }
  public contains(value: T): BaseWhere { return { kind: "compare", field: this.id, operator: "contains", value: queryValue(value) }; }
  public notContains(value: T): BaseWhere { return { kind: "compare", field: this.id, operator: "notContains", value: queryValue(value) }; }
  public startsWith(value: T): BaseWhere { return { kind: "compare", field: this.id, operator: "startsWith", value: queryValue(value) }; }
  public endsWith(value: T): BaseWhere { return { kind: "compare", field: this.id, operator: "endsWith", value: queryValue(value) }; }
  public like(value: T): BaseWhere { return { kind: "compare", field: this.id, operator: "like", value: queryValue(value) }; }
  public notLike(value: T): BaseWhere { return { kind: "compare", field: this.id, operator: "notLike", value: queryValue(value) }; }
  public isNull(): BaseWhere { return { kind: "isNull", field: this.id }; }
  public isDefined(): BaseWhere { return { kind: "isDefined", field: this.id }; }
  public asc(): BaseOrder { return { field: this.id, direction: "asc" }; }
  public desc(): BaseOrder { return { field: this.id, direction: "desc" }; }
}
type FieldOperatorMethod = { equal: "eq"; notEqual: "ne"; lessThan: "lt"; lessThanOrEqual: "lte"; greaterThan: "gt"; greaterThanOrEqual: "gte"; in: "in"; isNull: "isNull"; isDefined: "isDefined"; between: "between"; contains: "contains"; notContains: "notContains"; startsWith: "startsWith"; endsWith: "endsWith"; like: "like"; notLike: "notLike" };
type EnabledFieldMethod<TOperators extends readonly BaseFieldOperator[]> = { [K in keyof FieldOperatorMethod]: K extends TOperators[number] ? FieldOperatorMethod[K] : never }[keyof FieldOperatorMethod];
export type BaseFieldHandle<T, TOperators extends readonly BaseFieldOperator[] = readonly BaseFieldOperator[]> = Pick<BaseFieldHandleSurface<T>, "id" | "asc" | "desc" | EnabledFieldMethod<TOperators>>;
export function createFieldHandle<T, TOperators extends readonly BaseFieldOperator[]>(definition: BaseFieldDefinition<T, TOperators>): BaseFieldHandle<T, TOperators> { return new BaseFieldHandleSurface<T>(definition.id); }

export interface BaseOrder { readonly field: string; readonly direction: "asc" | "desc"; }
export type BaseQueryCountMode = "none" | "ifAvailable" | "exact" | "estimated" | "limited";
export interface BaseQueryInput {
  readonly where?: BaseWhere;
  readonly orderBy?: BaseOrder | readonly BaseOrder[];
  readonly select?: readonly string[];
  readonly include?: readonly string[];
  readonly count?: BaseQueryCountMode;
  readonly take: number;
  readonly cursor?: string;
}
export interface BaseRecord<T> { readonly collectionId: string; readonly id: string; readonly payload: { readonly kind: "json"; readonly json: T } | { readonly kind: "fieldMap"; readonly fields: Partial<T> }; readonly metadata: { readonly createdAt?: string; readonly updatedAt?: string; readonly revision?: string; readonly eTag?: string; readonly storeId?: string }; readonly policy?: { readonly redacted?: boolean; readonly omittedFields?: readonly string[]; readonly readOnlyFields?: readonly string[] }; }
export interface BasePageInfo { readonly page?: number; readonly perPage?: number; readonly offset?: number; readonly limit?: number; readonly cursor?: string; readonly nextCursor?: string; readonly hasMore: boolean; }
export interface BaseRecordPage<T> { readonly items: readonly T[]; readonly page: BasePageInfo; readonly count?: { readonly mode: string; readonly total?: number; readonly isExact: boolean }; }

export interface BaseQueryExecutor<T> {
  executeQuery(collectionId: string, query: BaseQueryInput, signal?: AbortSignal): Promise<BaseResult<BaseRecordPage<T>>>;
  watchQuery(collectionId: string, query: BaseQueryInput, observer: (snapshot: BaseQuerySnapshot<T>) => void): BaseSubscription;
}

export interface BaseQuerySnapshot<T> { readonly key: string; readonly connectionEpoch: string; readonly channelEpoch: string; readonly records: readonly T[]; readonly source: "initial" | "rerun" | "reconnected"; readonly stale: boolean; readonly version: string; readonly receivedAt: number; }
export interface BaseSubscription { readonly closed: boolean; close(): void; }

export class BaseQueryOperation<T> {
  public constructor(private readonly executor: BaseQueryExecutor<T>, public readonly collectionId: string, public readonly input: BaseQueryInput) {
    if (!Number.isInteger(input.take) || input.take < 1) throw new TypeError("take must be a positive integer");
    Object.freeze(this);
  }
  public execute(signal?: AbortSignal): Promise<BaseResult<BaseRecordPage<T>>> { return this.executor.executeQuery(this.collectionId, this.input, signal); }
  public page(signal?: AbortSignal): Promise<BaseResult<BaseRecordPage<T>>> { return this.execute(signal); }
  public continue(cursor: string): BaseQueryOperation<T> { if (cursor.length === 0) throw new TypeError("base.query.cursorInvalid"); return new BaseQueryOperation(this.executor, this.collectionId, { ...this.input, cursor }); }
  public async *iterate(options: { readonly maximumItems: number; readonly signal?: AbortSignal }): AsyncGenerator<T> {
    if (!Number.isInteger(options.maximumItems) || options.maximumItems < 1) throw new TypeError("base.query.limitInvalid");
    let operation: BaseQueryOperation<T> = this; let emitted = 0; const cursors = new Set<string>();
    while (emitted < options.maximumItems) {
      const result = await operation.execute(options.signal); if (!result.ok) throw new BaseClientException(result.error);
      for (const record of result.value.items) { if (emitted++ >= options.maximumItems) return; yield record; }
      const cursor = result.value.page.nextCursor; if (!result.value.page.hasMore || cursor === undefined) return;
      if (!cursors.add(cursor)) throw new BaseClientException({ code: "base.client.responseInvalid", category: "unexpected", message: "The BASE response was invalid." }); operation = operation.continue(cursor);
    }
  }
  public watch(observer: (snapshot: BaseQuerySnapshot<T>) => void): BaseSubscription { return this.executor.watchQuery(this.collectionId, this.input, observer); }
}

export function and(...expressions: readonly BaseWhere[]): BaseWhere { return { kind: "and", children: [...expressions] }; }
export function or(...expressions: readonly BaseWhere[]): BaseWhere { return { kind: "or", children: [...expressions] }; }
export function not(expression: BaseWhere): BaseWhere { return { kind: "not", children: [expression] }; }

export function toWireQuery(input: BaseQueryInput): object { return { ...(input.where === undefined ? {} : { filter: input.where }), ...(input.orderBy === undefined ? {} : { sort: Array.isArray(input.orderBy) ? input.orderBy : [input.orderBy] }), ...(input.select === undefined ? {} : { select: input.select }), ...(input.include === undefined ? {} : { include: input.include.map(navigationId => ({ navigationId })) }), count: input.count ?? "none", page: { mode: "cursor", limit: input.take, ...(input.cursor === undefined ? {} : { cursor: input.cursor, cursorDirection: "after" }) } }; }
export function toWireLiveQuery(input: BaseQueryInput): object { if (input.cursor !== undefined) throw new TypeError("base.liveQuery.pageUnsupported"); return { ...(input.where === undefined ? {} : { filter: input.where }), ...(input.orderBy === undefined ? {} : { sort: Array.isArray(input.orderBy) ? input.orderBy : [input.orderBy] }), ...(input.select === undefined ? {} : { select: input.select }), ...(input.include === undefined ? {} : { include: input.include.map(navigationId => ({ navigationId })) }) }; }
function queryValue(value: unknown): BaseQueryValue {
  if (value === null) return { kind: "null" }; if (typeof value === "string") return { kind: "string", string: value }; if (typeof value === "boolean") return { kind: "boolean", boolean: value };
  if (typeof value === "number" && Number.isSafeInteger(value)) return { kind: "integer", integer: value }; if (typeof value === "number" && Number.isFinite(value)) return { kind: "number", number: value };
  if (value instanceof Date && !Number.isNaN(value.getTime())) return { kind: "dateTime", dateTime: value.toISOString() }; throw new TypeError("base.query.valueInvalid");
}

export type FieldHandles<T extends BaseCollectionDefinition> = {
  readonly [K in keyof T["fields"]]: T["fields"][K] extends BaseFieldDefinition<infer TValue, infer TOperators> ? BaseFieldHandle<TValue, TOperators> : never
};
