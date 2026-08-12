import { BaseQueryOperation, createFieldHandle, toWireQuery, type BaseQueryExecutor, type BaseQueryInput, type BaseQuerySnapshot, type BaseRecord, type BaseRecordPage, type BaseSubscription, type FieldHandles } from "./query.js";
import type { BaseResult } from "./result.js";
import type { BaseCollectionDefinition, BaseGeneratedSchema, BaseReadDefinition } from "./schema.js";
import { BaseHttpTransport, type BaseTransportOptions } from "./transport.js";
import { BaseRealtimeManager, type BaseConnectivityState, type BaseRecordFeedDelivery, type BaseRecordFeedFilter, type BaseWebSocketFactory } from "./realtime.js";
import { BaseFilesClient } from "./files.js";
import { BaseVectorIndexQuery } from "./vector.js";
import { createControlPlaneClient, type BaseControlPlaneClient } from "./control.js";
import { decodeBaseValue, decodeBaseWireValue, encodeBaseJson, materializeBaseJsonValue } from "./codec.js";
import { executeSelectionMutation, type BaseSelectionMutationDefinition, type BaseSelectionMutationOptions, type BaseSelectionMutationResult } from "./selection.js";

type RecordOf<T> = T extends BaseCollectionDefinition<infer TRecord, unknown, unknown, unknown> ? TRecord : never;
type CreateOf<T> = T extends BaseCollectionDefinition<unknown, infer TCreate, unknown, unknown> ? TCreate : never;
type ReplaceOf<T> = T extends BaseCollectionDefinition<unknown, unknown, infer TReplace, unknown> ? TReplace : never;
type PatchOf<T> = T extends BaseCollectionDefinition<unknown, unknown, unknown, infer TPatch> ? TPatch : never;
type CollectionRecordOf<T> = BaseRecord<RecordOf<T>>;

declare const mutationBrand: unique symbol;
export type MutationId = string & { readonly [mutationBrand]: true };

interface BaseMutationOptions { readonly mutationId?: MutationId; readonly recordId?: string; readonly expectedRevision?: string; readonly optimistic?: boolean; readonly retry?: "never" | "safe"; readonly signal?: AbortSignal; }
export type BaseCreateOptions = Omit<BaseMutationOptions, "expectedRevision">;
export type BaseExistingMutationOptions = Omit<BaseMutationOptions, "recordId">;
export interface BaseUpsertRequest<TCreate, TUpdate> { readonly create: TCreate; readonly update: TUpdate; readonly updateMode: "patch" | "replace"; readonly condition?: "any" | "createOnly" | "updateOnly"; }
export interface BaseUpsertResult<T> { readonly outcome: "created" | "updated"; readonly record: T; }

interface BaseCollectionClientSurface<T extends BaseCollectionDefinition> {
  readonly id: string;
  readonly fields: FieldHandles<T>;
  readonly mutations: BaseCollectionMutations<T>;
  get(id: string, signal?: AbortSignal): Promise<BaseResult<CollectionRecordOf<T>>>;
  create(value: CreateOf<T>, options?: BaseCreateOptions): Promise<BaseResult<CollectionRecordOf<T>>>;
  patch(id: string, value: PatchOf<T>, options?: BaseExistingMutationOptions): Promise<BaseResult<CollectionRecordOf<T>>>;
  replace(id: string, value: ReplaceOf<T>, options?: BaseExistingMutationOptions): Promise<BaseResult<CollectionRecordOf<T>>>;
  upsert(id: string, request: BaseUpsertRequest<CreateOf<T>, PatchOf<T> | ReplaceOf<T>>, options?: BaseExistingMutationOptions): Promise<BaseResult<BaseUpsertResult<CollectionRecordOf<T>>>>;
  delete(id: string, options?: BaseExistingMutationOptions): Promise<BaseResult<{ readonly deleted: true; readonly id: string }>>;
  query(input: BaseQueryInput): BaseQueryOperation<CollectionRecordOf<T>>;
  buildQuery(): BaseQueryBuilder<CollectionRecordOf<T>>;
  watch(input: BaseQueryInput, observer: (snapshot: BaseQuerySnapshot<CollectionRecordOf<T>>) => void): BaseSubscription;
  readonly events: BaseRecordFeedClient;
  readonly vectorIndexes: T["vectorIndexes"];
  vector(index: import("./schema.js").BaseVectorIndexDefinition): BaseVectorIndexQuery<CollectionRecordOf<T>>;
}

interface BaseCollectionMutationSurface<T extends BaseCollectionDefinition> {
  create(value: CreateOf<T>, options?: BaseCreateOptions): Promise<BaseResult<CollectionRecordOf<T>>>;
  patch(id: string, value: PatchOf<T>, options?: BaseExistingMutationOptions): Promise<BaseResult<CollectionRecordOf<T>>>;
  replace(id: string, value: ReplaceOf<T>, options?: BaseExistingMutationOptions): Promise<BaseResult<CollectionRecordOf<T>>>;
  upsert(id: string, request: BaseUpsertRequest<CreateOf<T>, PatchOf<T> | ReplaceOf<T>>, options?: BaseExistingMutationOptions): Promise<BaseResult<BaseUpsertResult<CollectionRecordOf<T>>>>;
  delete(id: string, options?: BaseExistingMutationOptions): Promise<BaseResult<{ readonly deleted: true; readonly id: string }>>;
}

type OperationsOf<T> = T extends BaseCollectionDefinition<unknown, unknown, unknown, unknown, Readonly<Record<string, import("./schema.js").BaseFieldDefinition>>, infer TOperations> ? TOperations[number] : never;
export type BaseCollectionMutations<T extends BaseCollectionDefinition> = Pick<BaseCollectionMutationSurface<T>, Extract<OperationsOf<T>, keyof BaseCollectionMutationSurface<T>>>;
type OperationMethodMap = {
  readonly get: "get";
  readonly create: "create";
  readonly patch: "patch";
  readonly replace: "replace";
  readonly upsert: "upsert";
  readonly delete: "delete";
  readonly query: "query" | "buildQuery";
  readonly watch: "watch";
  readonly realtime: "events";
  readonly vector: "vector";
};

export interface BaseRecordFeedClient {
  live(filter: BaseRecordFeedFilter, observer: (delivery: BaseRecordFeedDelivery) => void | Promise<void>): BaseSubscription;
  durable(filter: BaseRecordFeedFilter, observer: (delivery: BaseRecordFeedDelivery) => void | Promise<void>): BaseSubscription;
  resume(cursor: string, filter: BaseRecordFeedFilter, observer: (delivery: BaseRecordFeedDelivery) => void | Promise<void>): BaseSubscription;
}
type EnabledMethod<T extends BaseCollectionDefinition> = {
  [K in keyof OperationMethodMap]: K extends OperationsOf<T> ? OperationMethodMap[K] : never
}[keyof OperationMethodMap];
export type BaseCollectionClient<T extends BaseCollectionDefinition> =
  Pick<BaseCollectionClientSurface<T>, "id" | "fields" | "mutations" | "vectorIndexes" | EnabledMethod<T>>;

export interface BaseDynamicClient {
  collection<T extends BaseCollectionDefinition>(definition: T): BaseCollectionClient<T>;
}

export interface BaseClientCommon {
  readonly connectivity: { readonly getSnapshot: () => BaseConnectivityState; readonly subscribe: (observer: () => void) => () => void };
  readonly $dynamic: BaseDynamicClient;
  resolveMutation(mutationId: MutationId, signal?: AbortSignal): Promise<BaseResult<unknown>>;
  close(): void;
}

export type BaseClient<TSchema extends BaseGeneratedSchema> = BaseClientCommon & {
  readonly [K in keyof TSchema["collections"]]: BaseCollectionClient<TSchema["collections"][K]>
} & { readonly reads: { readonly [K in keyof TSchema["reads"]]: BaseReadClient<TSchema["reads"][K]> } }
  & { readonly selectionMutations: { readonly [K in keyof NonNullable<TSchema["selectionMutations"]>]: NonNullable<TSchema["selectionMutations"]>[K] extends BaseSelectionMutationDefinition<infer TRequest> ? (request: TRequest, options?: BaseSelectionMutationOptions) => Promise<BaseResult<BaseSelectionMutationResult>> : never } }
  & (TSchema["features"]["files"] extends true ? { readonly files: BaseFilesClient } : {})
  & (TSchema["features"]["batch"] extends true ? { readonly batch: BaseBatchClient<TSchema> } : {})
  & (TSchema["audience"] extends "controlPlane" ? { readonly $control: BaseControlPlaneClient<TSchema["features"]["controlOperations"]> } : {});

type CollectionName<TSchema extends BaseGeneratedSchema> = Extract<keyof TSchema["collections"], string>;
export type BaseBatchOperation<TSchema extends BaseGeneratedSchema> = { [K in CollectionName<TSchema>]:
  | ("create" extends OperationsOf<TSchema["collections"][K]> ? { readonly itemId: string; readonly collection: K; readonly kind: "create"; readonly value: CreateOf<TSchema["collections"][K]>; readonly recordId?: string } : never)
  | ("patch" extends OperationsOf<TSchema["collections"][K]> ? { readonly itemId: string; readonly collection: K; readonly kind: "patch"; readonly id: string; readonly value: PatchOf<TSchema["collections"][K]>; readonly expectedRevision?: string } : never)
  | ("replace" extends OperationsOf<TSchema["collections"][K]> ? { readonly itemId: string; readonly collection: K; readonly kind: "replace"; readonly id: string; readonly value: ReplaceOf<TSchema["collections"][K]>; readonly expectedRevision?: string } : never)
  | ("upsert" extends OperationsOf<TSchema["collections"][K]> ? { readonly itemId: string; readonly collection: K; readonly kind: "upsert"; readonly id: string; readonly value: BaseUpsertRequest<CreateOf<TSchema["collections"][K]>, PatchOf<TSchema["collections"][K]> | ReplaceOf<TSchema["collections"][K]>>; readonly expectedRevision?: string } : never)
  | ("delete" extends OperationsOf<TSchema["collections"][K]> ? { readonly itemId: string; readonly collection: K; readonly kind: "delete"; readonly id: string; readonly expectedRevision?: string } : never)
}[CollectionName<TSchema>];
export interface BaseBatchResult { readonly outcome: string; readonly items: readonly unknown[]; }
export interface BaseBatchClient<TSchema extends BaseGeneratedSchema> { execute(mode: "orderedIndependent" | "orderedStopOnFailure" | "atomic", operations: readonly BaseBatchOperation<TSchema>[], options?: { readonly mutationId?: MutationId; readonly signal?: AbortSignal }): Promise<BaseResult<BaseBatchResult>>; }

type ReadParameters<T> = T extends BaseReadDefinition<infer TParameters, unknown, boolean> ? TParameters : never;
type ReadRow<T> = T extends BaseReadDefinition<unknown, infer TRow, boolean> ? TRow : never;
interface BaseReadClientSurface<T extends BaseReadDefinition> {
  readonly id: string;
  execute(parameters: ReadParameters<T>, page?: { readonly page?: number; readonly perPage?: number; readonly signal?: AbortSignal }): Promise<BaseResult<BaseRecordPage<ReadRow<T>>>>;
  watch(parameters: ReadParameters<T>, observer: (snapshot: BaseQuerySnapshot<ReadRow<T>>) => void): BaseSubscription;
}
export type BaseReadClient<T extends BaseReadDefinition> = T extends BaseReadDefinition<unknown, unknown, true> ? BaseReadClientSurface<T> : Pick<BaseReadClientSurface<T>, "id" | "execute">;

export interface BaseClientOptions<TSchema extends BaseGeneratedSchema> extends BaseTransportOptions {
  readonly schema: TSchema;
  readonly webSocketFactory?: BaseWebSocketFactory;
}

export class BaseQueryBuilder<T> {
  readonly #executor: BaseQueryExecutor<T>;
  readonly #collectionId: string;
  readonly #input: Partial<BaseQueryInput>;
  public constructor(executor: BaseQueryExecutor<T>, collectionId: string, input: Partial<BaseQueryInput> = {}) { this.#executor = executor; this.#collectionId = collectionId; this.#input = input; }
  public where(where: NonNullable<BaseQueryInput["where"]>): BaseQueryBuilder<T> { const previous = this.#input.where; return new BaseQueryBuilder(this.#executor, this.#collectionId, { ...this.#input, where: previous === undefined ? where : { kind: "and", children: [previous, where] } }); }
  public orderBy(orderBy: NonNullable<BaseQueryInput["orderBy"]>): BaseQueryBuilder<T> { return new BaseQueryBuilder(this.#executor, this.#collectionId, { ...this.#input, orderBy }); }
  public thenByRecordId(): BaseQueryBuilder<T> { const previous = this.#input.orderBy; const orderBy = previous === undefined ? [{ field: "id", direction: "asc" as const }] : [...(Array.isArray(previous) ? previous : [previous]), { field: "id", direction: "asc" as const }]; return new BaseQueryBuilder(this.#executor, this.#collectionId, { ...this.#input, orderBy }); }
  public select(...fieldIds: readonly string[]): BaseQueryBuilder<T> { if (fieldIds.length === 0) throw new TypeError("base.query.invalid"); return new BaseQueryBuilder(this.#executor, this.#collectionId, { ...this.#input, select: fieldIds }); }
  public include(...navigationIds: readonly string[]): BaseQueryBuilder<T> { if (navigationIds.length === 0) throw new TypeError("base.query.invalid"); return new BaseQueryBuilder(this.#executor, this.#collectionId, { ...this.#input, include: navigationIds }); }
  public count(mode: NonNullable<BaseQueryInput["count"]>): BaseQueryBuilder<T> { return new BaseQueryBuilder(this.#executor, this.#collectionId, { ...this.#input, count: mode }); }
  public take(take: number): BaseQueryOperation<T> { return new BaseQueryOperation(this.#executor, this.#collectionId, { ...this.#input, take }); }
}

class BaseClientRuntime implements BaseQueryExecutor<unknown> {
  readonly #transport: BaseHttpTransport;
  readonly #schema: BaseGeneratedSchema;
  readonly #collections = new Map<string, BaseCollectionClient<BaseCollectionDefinition>>();
  readonly #realtime: BaseRealtimeManager | undefined;
  readonly #files: BaseFilesClient;
  public readonly reads: Readonly<Record<string, BaseReadClient<BaseReadDefinition>>>;
  public readonly connectivity: { readonly getSnapshot: () => BaseConnectivityState; readonly subscribe: (observer: () => void) => () => void };
  #closed = false;
  readonly #indeterminate = new Map<MutationId, { readonly bytes: Uint8Array; readonly correlationId: string; readonly kind: "create" | "patch" | "replace" | "delete" | "upsert"; readonly collectionId: string; readonly optimisticId?: string }>();

  public constructor(options: BaseClientOptions<BaseGeneratedSchema>) {
    if (options.schema.protocolMajor !== 2) throw new TypeError("base.client.protocolMismatch");
    this.#schema = options.schema;
    this.#transport = new BaseHttpTransport(options);
    this.#files = new BaseFilesClient(this.#transport);
    const webSocketFactory = options.webSocketFactory ?? (options.accessToken === undefined && typeof globalThis.WebSocket === "function" ? ((url: URL) => new globalThis.WebSocket(url)) : undefined);
    this.#realtime = webSocketFactory === undefined ? undefined : new BaseRealtimeManager(options.url, webSocketFactory, options.accessToken, () => this.#indeterminate.clear());
    this.connectivity = this.#realtime?.connectivity ?? { getSnapshot: () => ({ kind: "offline" }), subscribe: () => () => undefined };
    this.reads = Object.freeze(Object.fromEntries(Object.entries(options.schema.reads).map(([name, definition]) => [name, new ReadClient(this, definition)])));
  }

  public collection<T extends BaseCollectionDefinition>(definition: T): BaseCollectionClient<T> {
    this.ensureOpen();
    const existing = this.#collections.get(definition.id);
    if (existing !== undefined) return existing as BaseCollectionClient<T>;
    const created = new CollectionClient(this, definition);
    this.#collections.set(definition.id, created);
    return created as BaseCollectionClient<T>;
  }

  public close(): void { this.#closed = true; this.#realtime?.close(); this.#collections.clear(); this.#indeterminate.clear(); }
  public selectionTransport(): BaseHttpTransport { this.ensureOpen(); return this.#transport; }
  public controlClient<const TOperations extends readonly string[]>(operations: TOperations): BaseControlPlaneClient<TOperations> { this.ensureOpen(); return createControlPlaneClient(this.#transport, operations); }
  public async resolveMutation(mutationId: MutationId, signal?: AbortSignal): Promise<BaseResult<unknown>> { this.ensureOpen(); const pending = this.#indeterminate.get(mutationId); if (pending === undefined) throw new TypeError("base.client.mutationNotIndeterminate"); const result = normalizeIdentifiedFailure(await this.#transport.jsonDocument("POST", "records/batch", pending.bytes, signal, mutationId, pending.correlationId)); const projected = projectMutation<unknown>(result, pending.kind); if (projected.ok) { this.#indeterminate.delete(mutationId); const authoritative = pending.kind === "delete" ? undefined : pending.kind === "upsert" && isObject(projected.value) && "record" in projected.value ? this.fromWireRecord(pending.collectionId, projected.value.record) : this.fromWireRecord(pending.collectionId, projected.value); const value = pending.kind === "upsert" && isObject(projected.value) ? { ...projected.value, record: authoritative } : authoritative; if (pending.optimisticId !== undefined) this.#realtime?.reconcile(mutationId, pending.collectionId, authoritative); return { ...projected, value }; } if (projected.error.code !== "base.runtime.batch.indeterminate") { this.#indeterminate.delete(mutationId); if (pending.optimisticId !== undefined) this.#realtime?.reject(mutationId, pending.collectionId); } return projected; }
  public filesClient(): BaseFilesClient { this.ensureOpen(); return this.#files; }
  public async executeBatch(mode: "orderedIndependent" | "orderedStopOnFailure" | "atomic", operations: readonly BaseBatchOperation<BaseGeneratedSchema>[], options: { readonly mutationId?: MutationId; readonly signal?: AbortSignal } = {}): Promise<BaseResult<BaseBatchResult>> {
    this.ensureOpen(); if (operations.length === 0) throw new TypeError("base.client.batchInvalid");
    const wire = operations.map(operation => toBatchItem(this.#schema, operation));
    const bytes = encodeSchemaJson({ mode, operations: wire }, this.#schema); const identity = mode === "atomic" ? options.mutationId ?? crypto.randomUUID() as MutationId : undefined; const correlation = crypto.randomUUID();
    let result = await this.#transport.jsonDocument("POST", "records/batch", bytes, options.signal, identity, correlation);
    if (mode === "atomic" && !result.ok && identifiedRetry(result.error.code)) result = await this.#transport.jsonDocument("POST", "records/batch", bytes, options.signal, identity, correlation);
    const normalized = mode === "atomic" ? normalizeIdentifiedFailure(result) : result;
    if (!normalized.ok) return normalized;
    return isBatchResult(normalized.value) ? { ...normalized, value: normalized.value } : invalid(normalized.correlationId);
  }

  public async executeQuery<T>(collectionId: string, query: BaseQueryInput, signal?: AbortSignal): Promise<BaseResult<BaseRecordPage<T>>> {
    this.ensureOpen();
    const result = await this.#transport.jsonDocument("POST", `collections/${encodeURIComponent(collectionId)}/records:query`, encodeJson(toWireQuery(query)), signal);
    if (!result.ok) return result;
    const page = materializePageEnvelope(result.value);
    if (!isRecordPage(page)) return invalid(result.correlationId);
    try { return { ...result, value: { ...page, items: page.items.map(item => this.fromWireRecord(collectionId, item)) as T[] } }; } catch { return invalid(result.correlationId); }
  }

  public watchQuery<T>(collectionId: string, query: BaseQueryInput, observer: (snapshot: BaseQuerySnapshot<T>) => void): BaseSubscription {
    this.ensureOpen();
    if (this.#realtime === undefined) throw new Error("base.client.capabilityUnavailable");
    return this.#realtime.subscribe(collectionId, query, observer, value => this.fromWireRecord(collectionId, value) as T);
  }

  public async get<T>(collectionId: string, id: string, signal?: AbortSignal): Promise<BaseResult<T>> {
    const result = await this.#transport.jsonDocument("GET", `collections/${encodeURIComponent(collectionId)}/records/${encodeURIComponent(id)}`, undefined, signal);
    if (!result.ok) return result; try { return isRecord(result.value) ? { ...result, value: this.fromWireRecord(collectionId, result.value) as T } : invalid(result.correlationId); } catch { return invalid(result.correlationId); }
  }

  public vectorQuery<T>(collectionId: string, index: import("./schema.js").BaseVectorIndexDefinition): BaseVectorIndexQuery<T> {
    this.ensureOpen();
    return new BaseVectorIndexQuery<T>(this.#transport, collectionId, index, value => this.fromWireRecord(collectionId, value) as T);
  }

  public async executeRead<T>(id: string, parameters: unknown, parameterTypeId: string | undefined, rowTypeId: string | undefined, page: { readonly page?: number; readonly perPage?: number; readonly signal?: AbortSignal } = {}): Promise<BaseResult<BaseRecordPage<T>>> {
    if (parameterTypeId === undefined || rowTypeId === undefined || this.#schema.typeGraph === undefined) throw new TypeError("base.client.configurationInvalid");
    const query = new URLSearchParams(); if (page.page !== undefined) query.set("page", String(page.page)); if (page.perPage !== undefined) query.set("perPage", String(page.perPage));
    let body: Uint8Array; try { body = new TextEncoder().encode(encodeBaseJson(parameters, parameterTypeId, this.#schema.typeGraph)); } catch { throw new TypeError("base.client.requestInvalid"); }
    const result = await this.#transport.jsonDocument("POST", `reads/${encodeURIComponent(id)}${query.size === 0 ? "" : `?${query}`}`, body, page.signal);
    if (!result.ok) return result; const decodedPage = materializePageEnvelope(result.value);
    if (!isRecordPage(decodedPage, false)) return invalid(result.correlationId);
    try { return { ...result, value: { ...decodedPage, items: decodedPage.items.map(item => decodeBaseWireValue<T>(item, rowTypeId, this.#schema.typeGraph!)) } }; } catch { return invalid(result.correlationId); }
  }
  public watchRead<T>(id: string, parameters: unknown, parameterTypeId: string | undefined, rowTypeId: string | undefined, observer: (snapshot: BaseQuerySnapshot<T>) => void): BaseSubscription { if (this.#realtime === undefined) throw new Error("base.client.capabilityUnavailable"); if (parameterTypeId === undefined || rowTypeId === undefined || this.#schema.typeGraph === undefined) throw new TypeError("base.client.configurationInvalid"); const encoded = encodeBaseJson(parameters, parameterTypeId, this.#schema.typeGraph); return this.#realtime.subscribeRead(id, parameters, observer, value => decodeBaseWireValue<T>(value, rowTypeId, this.#schema.typeGraph!), encoded); }
  public watchEvents(collectionId: string, request: import("./realtime.js").BaseRecordFeedRequest, observer: (delivery: BaseRecordFeedDelivery) => void | Promise<void>): BaseSubscription { if (this.#realtime === undefined) throw new Error("base.client.capabilityUnavailable"); return this.#realtime.subscribeFeed(collectionId, request, observer, value => this.fromWireRecord(collectionId, value)); }

  public async mutate<T>(collectionId: string, kind: "create" | "patch" | "replace" | "delete" | "upsert", id: string | undefined, value: unknown, options: BaseMutationOptions = {}): Promise<BaseResult<T>> {
    const mutationId = options.mutationId ?? crypto.randomUUID() as MutationId;
    const payload = (input: unknown): object => ({ kind: "json", json: input });
    if (kind === "create" && options.optimistic === true && options.recordId === undefined) throw new TypeError("base.client.optimisticIdRequired");
    const validatedValue = this.validateMutationInput(collectionId, kind, value);
    const wireValue = this.toWirePayload(collectionId, validatedValue);
    const request = kind === "create" ? { payload: payload(wireValue), ...(options.recordId === undefined ? {} : { requestedId: options.recordId }) }
      : kind === "patch" ? { patch: payload(wireValue), ...(options.expectedRevision === undefined ? {} : { expectedRevision: options.expectedRevision }) }
      : kind === "replace" ? { payload: payload(wireValue), ...(options.expectedRevision === undefined ? {} : { expectedRevision: options.expectedRevision }) }
      : kind === "delete" ? { ...(options.expectedRevision === undefined ? {} : { expectedRevision: options.expectedRevision }), returnPrevious: false }
      : upsertRequest(id!, this.toWireUpsert(collectionId, validatedValue as BaseUpsertRequest<unknown, unknown>), options.expectedRevision);
    const operation = {
      itemId: "mutation",
      collectionId,
      kind,
      ...(id === undefined ? {} : { recordId: id }),
      [kind]: request
    };
    const bytes = encodeSchemaJson({ mode: "atomic", operations: [operation] }, this.#schema);
    const correlationId = crypto.randomUUID();
    const optimisticId = id ?? options.recordId;
    if (options.optimistic === true && optimisticId !== undefined && this.#realtime !== undefined)
      this.#realtime.applyOptimistic(mutationId, collectionId, optimisticId, kind, this.safeOptimistic(collectionId, validatedValue), options.expectedRevision, bytes);
    let result = await this.#transport.jsonDocument("POST", "records/batch", bytes, options.signal, mutationId, correlationId);
    if (!result.ok && identifiedRetry(result.error.code) && options.retry !== "never")
      result = await this.#transport.jsonDocument("POST", "records/batch", bytes, options.signal, mutationId, correlationId);
    let projected = projectMutation<T>(normalizeIdentifiedFailure(result), kind);
    if (projected.ok && kind !== "delete") projected = { ...projected, value: (kind === "upsert" && isObject(projected.value) && "record" in projected.value ? { ...projected.value, record: this.fromWireRecord(collectionId, projected.value.record) } : this.fromWireRecord(collectionId, projected.value)) as T };
    if (!projected.ok && projected.retry === "identifiedMutationOnly") this.#indeterminate.set(mutationId, { bytes: bytes.slice(), correlationId, kind, collectionId, ...(options.optimistic === true && optimisticId !== undefined ? { optimisticId } : {}) });
    if (options.optimistic === true && optimisticId !== undefined && this.#realtime !== undefined) {
      if (projected.ok) {
        const authoritative = kind === "delete" ? undefined : kind === "upsert" && typeof projected.value === "object" && projected.value !== null && "record" in projected.value ? projected.value.record : projected.value;
        this.#realtime.reconcile(mutationId, collectionId, authoritative);
      } else if (projected.retry === "identifiedMutationOnly") this.#realtime.markIndeterminate(mutationId); else this.#realtime.reject(mutationId, collectionId);
    }
    return projected;
  }

  private ensureOpen(): void { if (this.#closed) throw new Error("base.client.closed"); }
  private validateMutationInput(collectionId: string, kind: "create" | "patch" | "replace" | "delete" | "upsert", value: unknown): unknown {
    if (kind === "delete") return value;
    const definition = Object.values(this.#schema.collections).find(item => item.id === collectionId); const graph = this.#schema.typeGraph;
    if (definition === undefined || graph === undefined) throw new TypeError("base.client.configurationInvalid");
    return validateMutationDto(definition, graph, kind, value);
  }
  private toWirePayload(collectionId: string, value: unknown): unknown { const definition = Object.values(this.#schema.collections).find(item => item.id === collectionId); if (definition === undefined || !isObject(value)) return value; return Object.fromEntries(Object.entries(value).map(([name, item]) => [definition.fields[name]?.wireName ?? name, item])); }
  private toWireUpsert(collectionId: string, value: BaseUpsertRequest<unknown, unknown>): BaseUpsertRequest<unknown, unknown> { return { ...value, create: this.toWirePayload(collectionId, value.create), update: this.toWirePayload(collectionId, value.update) }; }
  private safeOptimistic(collectionId: string, value: unknown): unknown { const definition = Object.values(this.#schema.collections).find(item => item.id === collectionId); if (definition === undefined || !isObject(value)) return value; const allowed = new Set(Object.entries(definition.fields).filter(([, field]) => field.disclosureShape === "none").map(([name]) => name)); if ("create" in value && "update" in value) return { ...value, create: isObject(value.create) ? Object.fromEntries(Object.entries(value.create).filter(([key]) => allowed.has(key))) : value.create, update: isObject(value.update) ? Object.fromEntries(Object.entries(value.update).filter(([key]) => allowed.has(key))) : value.update }; return Object.fromEntries(Object.entries(value).filter(([key]) => allowed.has(key))); }
  private fromWireRecord(collectionId: string, value: unknown): unknown {
    if (!isRecord(value) || value.collectionId !== collectionId || value.id.length === 0) throw new TypeError("base.client.responseInvalid");
    const definition = Object.values(this.#schema.collections).find(item => item.id === collectionId); if (definition === undefined) throw new TypeError("base.client.responseInvalid");
    const source = value.payload.kind === "json" ? value.payload.json : value.payload.fields; if (!isObject(source) || definition.recordTypeId === undefined || this.#schema.typeGraph === undefined) throw new TypeError("base.client.responseInvalid");
    const decoded = decodeBaseWireValue<Record<string, unknown>>(source, definition.recordTypeId, this.#schema.typeGraph);
    const accepted = new Set(Object.keys(definition.fields)); if (Object.keys(decoded).some(name => !accepted.has(name))) throw new TypeError("base.client.responseInvalid"); const mapped = decoded;
    return { ...value, payload: value.payload.kind === "json" ? { ...value.payload, json: Object.freeze(mapped) } : { ...value.payload, fields: Object.freeze(mapped) } };
  }
}

class ReadClient<T extends BaseReadDefinition> implements BaseReadClientSurface<T> {
  public readonly id: string;
  private readonly maximum: number;
  private readonly rowTypeId: string | undefined;
  private readonly parameterTypeId: string | undefined;
  public constructor(private readonly owner: BaseClientRuntime, definition: T) { this.id = definition.id; this.maximum = definition.maxPageSize; this.rowTypeId = definition.rowTypeId; this.parameterTypeId = definition.parameterTypeId; }
  public execute(parameters: ReadParameters<T>, page?: { readonly page?: number; readonly perPage?: number; readonly signal?: AbortSignal }): Promise<BaseResult<BaseRecordPage<ReadRow<T>>>> { if (page?.perPage !== undefined && (!Number.isInteger(page.perPage) || page.perPage < 1 || page.perPage > this.maximum)) throw new RangeError("base.query.limitInvalid"); return this.owner.executeRead(this.id, parameters, this.parameterTypeId, this.rowTypeId, page); }
  public watch(parameters: ReadParameters<T>, observer: (snapshot: BaseQuerySnapshot<ReadRow<T>>) => void): BaseSubscription { return this.owner.watchRead(this.id, parameters, this.parameterTypeId, this.rowTypeId, observer); }
}

class CollectionClient<T extends BaseCollectionDefinition> implements BaseCollectionClientSurface<T> {
  public readonly id: string;
  public readonly fields: FieldHandles<T>;
  public readonly mutations: BaseCollectionMutations<T>;
  public readonly vectorIndexes: T["vectorIndexes"];
  public readonly events: BaseRecordFeedClient;
  public constructor(private readonly owner: BaseClientRuntime, private readonly definition: T) {
    this.id = definition.id;
    const handles: Record<string, import("./query.js").BaseFieldHandle<unknown>> = {};
    for (const [name, field] of Object.entries(definition.fields)) handles[name] = createFieldHandle(field);
    this.fields = Object.freeze(handles) as FieldHandles<T>;
    const mutationMethods: Record<string, unknown> = {};
    if (definition.operations.includes("create")) mutationMethods["create"] = this.create.bind(this);
    if (definition.operations.includes("patch")) mutationMethods["patch"] = this.patch.bind(this);
    if (definition.operations.includes("replace")) mutationMethods["replace"] = this.replace.bind(this);
    if (definition.operations.includes("upsert")) mutationMethods["upsert"] = this.upsert.bind(this);
    if (definition.operations.includes("delete")) mutationMethods["delete"] = this.delete.bind(this);
    this.mutations = Object.freeze(mutationMethods) as BaseCollectionMutations<T>;
    this.vectorIndexes = definition.vectorIndexes;
    this.events = Object.freeze({
      live: (filter: BaseRecordFeedFilter, observer: (delivery: BaseRecordFeedDelivery) => void | Promise<void>) => this.owner.watchEvents(this.id, { kind: "live", filter }, observer),
      durable: (filter: BaseRecordFeedFilter, observer: (delivery: BaseRecordFeedDelivery) => void | Promise<void>) => this.owner.watchEvents(this.id, { kind: "durable", filter }, observer),
      resume: (cursor: string, filter: BaseRecordFeedFilter, observer: (delivery: BaseRecordFeedDelivery) => void | Promise<void>) => this.owner.watchEvents(this.id, { kind: "resume", cursor, filter }, observer)
    });
  }
  public get(id: string, signal?: AbortSignal): Promise<BaseResult<CollectionRecordOf<T>>> { return this.owner.get(this.id, id, signal); }
  public create(value: CreateOf<T>, options?: BaseCreateOptions): Promise<BaseResult<CollectionRecordOf<T>>> { return this.owner.mutate(this.id, "create", undefined, value, options); }
  public patch(id: string, value: PatchOf<T>, options?: BaseExistingMutationOptions): Promise<BaseResult<CollectionRecordOf<T>>> { return this.owner.mutate(this.id, "patch", id, value, options); }
  public replace(id: string, value: ReplaceOf<T>, options?: BaseExistingMutationOptions): Promise<BaseResult<CollectionRecordOf<T>>> { return this.owner.mutate(this.id, "replace", id, value, options); }
  public upsert(id: string, request: BaseUpsertRequest<CreateOf<T>, PatchOf<T> | ReplaceOf<T>>, options?: BaseExistingMutationOptions): Promise<BaseResult<BaseUpsertResult<CollectionRecordOf<T>>>> { return this.owner.mutate(this.id, "upsert", id, request, options); }
  public delete(id: string, options?: BaseExistingMutationOptions): Promise<BaseResult<{ readonly deleted: true; readonly id: string }>> { return this.owner.mutate(this.id, "delete", id, undefined, options); }
  public query(input: BaseQueryInput): BaseQueryOperation<CollectionRecordOf<T>> { if (input.take > this.definition.maxPageSize) throw new RangeError("base.query.limitInvalid"); return new BaseQueryOperation<CollectionRecordOf<T>>(this.owner, this.id, input); }
  public buildQuery(): BaseQueryBuilder<CollectionRecordOf<T>> { return new BaseQueryBuilder<CollectionRecordOf<T>>(this.owner, this.id); }
  public watch(input: BaseQueryInput, observer: (snapshot: BaseQuerySnapshot<CollectionRecordOf<T>>) => void): BaseSubscription { return this.query(input).watch(observer); }
  public vector(index: import("./schema.js").BaseVectorIndexDefinition): BaseVectorIndexQuery<CollectionRecordOf<T>> { return this.owner.vectorQuery(this.id, index); }
}

export function createBaseClient<TSchema extends BaseGeneratedSchema>(options: BaseClientOptions<TSchema>): BaseClient<TSchema> {
  const schema = deepFrozenClone(options.schema);
  const runtime = new BaseClientRuntime({ ...options, schema });
  const target = runtime as unknown as BaseClient<TSchema>;
  Object.defineProperty(target, "$dynamic", { value: Object.freeze({ collection: runtime.collection.bind(runtime) }), enumerable: true, configurable: false, writable: false });
  Object.defineProperty(target, "resolveMutation", { value: runtime.resolveMutation.bind(runtime), enumerable: true, configurable: false, writable: false });
  for (const [name, definition] of Object.entries(schema.collections)) {
    if (name in runtime) throw new TypeError("base.client.configurationInvalid");
    Object.defineProperty(target, name, { value: runtime.collection(definition), enumerable: true, configurable: false, writable: false });
  }
  if (schema.features.files) Object.defineProperty(target, "files", { value: runtime.filesClient(), enumerable: true, configurable: false, writable: false });
  if (schema.features.batch) Object.defineProperty(target, "batch", { value: Object.freeze({ execute: runtime.executeBatch.bind(runtime) }), enumerable: true, configurable: false, writable: false });
  const selections = Object.fromEntries(Object.entries(schema.selectionMutations ?? {}).map(([name, definition]) => [name, (request: unknown, selectionOptions?: BaseSelectionMutationOptions) => executeSelectionMutation(runtime.selectionTransport(), definition, request, selectionOptions)]));
  Object.defineProperty(target, "selectionMutations", { value: Object.freeze(selections), enumerable: true, configurable: false, writable: false });
  if (schema.audience === "controlPlane") Object.defineProperty(target, "$control", { value: runtime.controlClient(schema.features.controlOperations), enumerable: true, configurable: false, writable: false });
  return target;
}

function deepFrozenClone<T>(value: T): T {
  const clone = structuredClone(value);
  const freeze = (item: unknown): void => { if (typeof item !== "object" || item === null || Object.isFrozen(item)) return; for (const child of Object.values(item)) freeze(child); Object.freeze(item); };
  freeze(clone); return clone;
}

function encodeJson(value: unknown): Uint8Array { return new TextEncoder().encode(JSON.stringify(value)); }
function encodeSchemaJson(value: unknown, schema: BaseGeneratedSchema): Uint8Array {
  const encode = (item: unknown, collectionId?: string, payload = false): string => {
    if (item === null || typeof item === "boolean" || typeof item === "string") return JSON.stringify(item);
    if (typeof item === "number") { if (!Number.isFinite(item)) throw new TypeError("base.client.requestInvalid"); return Object.is(item, -0) ? "0" : item.toString(); }
    if (Array.isArray(item)) return `[${item.map(child => encode(child, collectionId)).join(",")}]`;
    if (!isObject(item)) throw new TypeError("base.client.requestInvalid");
    const nextCollection = typeof item.collectionId === "string" ? item.collectionId : collectionId; const definition = nextCollection === undefined ? undefined : Object.values(schema.collections).find(candidate => candidate.id === nextCollection); const byWire = definition === undefined ? undefined : new Map(Object.values(definition.fields).map(field => [field.wireName, field]));
    return `{${Object.entries(item).map(([key, child]) => {
      if (payload && byWire !== undefined) { const field = byWire.get(key); if (field === undefined) throw new TypeError("base.client.requestInvalid"); if (child === null) return `${JSON.stringify(key)}:null`; if (field.valueTypeId !== undefined && schema.typeGraph !== undefined) return `${JSON.stringify(key)}:${encodeBaseJson(child, field.valueTypeId, schema.typeGraph)}`; }
      return `${JSON.stringify(key)}:${encode(child, nextCollection, key === "json" || key === "fields")}`;
    }).join(",")}}`;
  };
  return new TextEncoder().encode(encode(value));
}

function upsertRequest(id: string, request: BaseUpsertRequest<unknown, unknown>, expectedRevision: string | undefined): object {
  return {
    id,
    createPayload: { kind: "json", json: request.create },
    updatePayload: { kind: "json", json: request.update },
    updateMode: request.updateMode,
    condition: request.condition ?? "any",
    ...(expectedRevision === undefined ? {} : { expectedRevision })
  };
}

function projectMutation<T>(result: BaseResult<unknown>, kind: string): BaseResult<T> {
  if (!result.ok) return result;
  if (typeof result.value !== "object" || result.value === null) return invalid<T>(result.correlationId);
  const aggregate = result.value as { outcome?: unknown; items?: unknown };
  if (aggregate.outcome !== "committed" || !Array.isArray(aggregate.items) || aggregate.items.length !== 1) return invalid<T>(result.correlationId);
  const item = aggregate.items[0] as { itemId?: unknown; index?: unknown; kind?: unknown; disposition?: unknown; record?: unknown; delete?: unknown; upsert?: unknown };
  if (item.itemId !== "mutation" || materializeBaseJsonValue(item.index) !== 0 || item.kind !== kind || item.disposition !== "committed") return invalid<T>(result.correlationId);
  const value = kind === "delete" ? item.delete : kind === "upsert" ? item.upsert : item.record;
  if (value === undefined) return invalid<T>(result.correlationId);
  return { ...result, value: value as T };
}

function invalid<T>(correlationId: string): BaseResult<T> {
  return { ok: false, error: { code: "base.client.responseInvalid", category: "unexpected", message: "The BASE mutation response was invalid." }, correlationId, retry: "identifiedMutationOnly" };
}
function toBatchItem(schema: BaseGeneratedSchema, operation: BaseBatchOperation<BaseGeneratedSchema>): object {
  const collectionId = schema.collections[operation.collection]?.id ?? operation.collection;
  const definition = schema.collections[operation.collection]; if (definition === undefined || schema.typeGraph === undefined) throw new TypeError("base.client.configurationInvalid");
  const map = (value: unknown): unknown => !isObject(value) ? value : Object.fromEntries(Object.entries(value).map(([name, item]) => [definition.fields[name]?.wireName ?? name, item]));
  if (operation.kind === "create") { const value = validateMutationDto(definition, schema.typeGraph, "create", operation.value); return { itemId: operation.itemId, collectionId, kind: operation.kind, create: { payload: { kind: "json", json: map(value) }, ...(operation.recordId === undefined ? {} : { requestedId: operation.recordId }) } }; }
  if (operation.kind === "patch") { const value = validateMutationDto(definition, schema.typeGraph, "patch", operation.value); return { itemId: operation.itemId, collectionId, kind: operation.kind, recordId: operation.id, patch: { patch: { kind: "json", json: map(value) }, ...(operation.expectedRevision === undefined ? {} : { expectedRevision: operation.expectedRevision }) } }; }
  if (operation.kind === "replace") { const value = validateMutationDto(definition, schema.typeGraph, "replace", operation.value); return { itemId: operation.itemId, collectionId, kind: operation.kind, recordId: operation.id, replace: { payload: { kind: "json", json: map(value) }, ...(operation.expectedRevision === undefined ? {} : { expectedRevision: operation.expectedRevision }) } }; }
  if (operation.kind === "upsert") { const value = validateMutationDto(definition, schema.typeGraph, "upsert", operation.value) as BaseUpsertRequest<unknown, unknown>; return { itemId: operation.itemId, collectionId, kind: operation.kind, recordId: operation.id, upsert: upsertRequest(operation.id, { ...value, create: map(value.create), update: map(value.update) }, operation.expectedRevision) }; }
  return { itemId: operation.itemId, collectionId, kind: operation.kind, recordId: operation.id, delete: { ...(operation.expectedRevision === undefined ? {} : { expectedRevision: operation.expectedRevision }), returnPrevious: false } };
}

function validateMutationDto(definition: BaseCollectionDefinition, graph: import("./codec.js").BaseTypeGraph, kind: "create" | "patch" | "replace" | "upsert", value: unknown): unknown {
  try {
    if (kind === "upsert") {
      if (!isObject(value) || !hasOnly(value, ["create", "update", "updateMode", "condition"]) || (value.updateMode !== "patch" && value.updateMode !== "replace") || (value.condition !== undefined && !["any", "createOnly", "updateOnly"].includes(value.condition as string))) throw new TypeError();
      const updateTypeId = value.updateMode === "patch" ? definition.patchTypeId : definition.replaceTypeId;
      if (definition.createTypeId === undefined || updateTypeId === undefined) throw new TypeError("base.client.configurationInvalid");
      return Object.freeze({ ...value, create: decodeBaseValue(value.create, definition.createTypeId, graph), update: decodeBaseValue(value.update, updateTypeId, graph) });
    }
    const typeId = kind === "create" ? definition.createTypeId : kind === "patch" ? definition.patchTypeId : definition.replaceTypeId;
    if (typeId === undefined) throw new TypeError("base.client.configurationInvalid");
    return decodeBaseValue(value, typeId, graph);
  } catch (error: unknown) { if (error instanceof TypeError && error.message === "base.client.configurationInvalid") throw error; throw new TypeError("base.client.requestInvalid"); }
}

function materializePageEnvelope(value: unknown): unknown {
  if (!isObject(value)) return value;
  return { ...value, ...(isObject(value.page) ? { page: materializeBaseJsonValue(value.page) } : {}), ...(value.count === undefined ? {} : { count: materializeBaseJsonValue(value.count) }) };
}
function identifiedRetry(code: string): boolean { return code === "base.client.transportFailed" || code === "base.runtime.batch.indeterminate" || code === "base.runtime.request.outcomeUnknown"; }
function normalizeIdentifiedFailure<T>(result: BaseResult<T>): BaseResult<T> { return !result.ok && identifiedRetry(result.error.code) ? { ...result, retry: "identifiedMutationOnly" } : result; }
function isObject(value: unknown): value is Record<string, unknown> { return typeof value === "object" && value !== null && !Array.isArray(value); }
function hasOnly(value: Record<string, unknown>, keys: readonly string[]): boolean { return Object.keys(value).every(key => keys.includes(key)); }
function isRecord(value: unknown): value is BaseRecord<unknown> {
  if (!isObject(value) || !hasOnly(value, ["collectionId", "id", "payload", "metadata", "policy"]) || typeof value.collectionId !== "string" || typeof value.id !== "string" || !isObject(value.payload) || !isObject(value.metadata)) return false;
  if (value.payload.kind === "json") { if (!hasOnly(value.payload, ["kind", "json"]) || !isObject(value.payload.json)) return false; }
  else if (value.payload.kind === "fieldMap") { if (!hasOnly(value.payload, ["kind", "fields"]) || !isObject(value.payload.fields)) return false; }
  else return false;
  if (!hasOnly(value.metadata, ["createdAt", "updatedAt", "revision", "eTag", "storeId"])) return false; for (const item of Object.values(value.metadata)) if (typeof item !== "string") return false;
  return value.policy === undefined || isObject(value.policy) && hasOnly(value.policy, ["redacted", "omittedFields", "readOnlyFields"]) && (value.policy.redacted === undefined || typeof value.policy.redacted === "boolean") && (value.policy.omittedFields === undefined || Array.isArray(value.policy.omittedFields) && value.policy.omittedFields.every(item => typeof item === "string")) && (value.policy.readOnlyFields === undefined || Array.isArray(value.policy.readOnlyFields) && value.policy.readOnlyFields.every(item => typeof item === "string"));
}
function isRecordPage(value: unknown, records = true): value is BaseRecordPage<unknown> {
  if (!isObject(value) || !hasOnly(value, ["items", "page", "count"]) || !Array.isArray(value.items) || !isObject(value.page) || !hasOnly(value.page, ["page", "perPage", "offset", "limit", "cursor", "nextCursor", "hasMore"]) || typeof value.page.hasMore !== "boolean") return false;
  if (records && !value.items.every(isRecord)) return false;
  return value.count === undefined || (isObject(value.count) && typeof value.count.mode === "string" && typeof value.count.isExact === "boolean" && (value.count.total === undefined || Number.isSafeInteger(value.count.total)));
}
function isBatchResult(value: unknown): value is BaseBatchResult { return isObject(value) && hasOnly(value, ["outcome", "items", "failureIndex", "receipt", "committedAt"]) && typeof value.outcome === "string" && Array.isArray(value.items); }
