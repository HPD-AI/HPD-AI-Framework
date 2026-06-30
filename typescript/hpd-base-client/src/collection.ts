import { unwrapResult } from "./result.js";
import { encodePathSegment, type HttpTransport } from "./transport/http.js";
import { serializeRecordQueryForGet } from "./query/serialize.js";
import { toRecordQuery } from "./query/builder.js";
import type {
  CreateInput,
  DeleteResult,
  JsonObject,
  PatchInput,
  RecordCreateRequest,
  RecordDeleteRequest,
  RecordEnvelope,
  RecordPage,
  RecordPatchRequest,
  RecordReplaceRequest,
  ReplaceInput
} from "./types/records.js";
import type { CollectionDefinition } from "./types/schema.js";
import type { BaseResult } from "./types/results.js";
import type { RecordQueryInput } from "./types/query.js";
import type { CollectionClient, CreateOptions, DeleteOptions, ListRequestOptions, MutationOptions, RequestOptions, SupportsOptions } from "./client.js";

export class BaseCollectionClient<TRecord extends JsonObject = JsonObject> implements CollectionClient<TRecord> {
  constructor(
    readonly id: string,
    private readonly transport: HttpTransport,
    private readonly definitionLoader: (id: string, options?: RequestOptions) => Promise<CollectionDefinition>,
    private readonly definitionResultLoader: (id: string, options?: RequestOptions) => Promise<BaseResult<CollectionDefinition>>,
    private readonly supportsOperation: (id: string, operation: string, options?: SupportsOptions) => boolean | undefined
  ) {}

  async list(query?: RecordQueryInput<TRecord>, options: ListRequestOptions = {}): Promise<RecordPage<TRecord>> {
    return unwrapResult(await this.listResult(query, options));
  }

  async listResult(query?: RecordQueryInput<TRecord>, options: ListRequestOptions = {}): Promise<BaseResult<RecordPage<TRecord>>> {
    const recordQuery = toRecordQuery(query);
    const method = options.method ?? "auto";
    if (method !== "post") {
      const serialized = serializeRecordQueryForGet(recordQuery, options.maxUrlLength);
      if (serialized.ok) {
        return this.transport.request<RecordPage<TRecord>>({ path: this.recordsPath(), query: serialized.search, headers: options.headers, signal: options.signal, correlationId: options.correlationId });
      }
      if (method === "get") {
        return {
          ok: false,
          status: "validationFailed",
          error: { status: "validationFailed", code: "base.client.query.notGetSafe", message: serialized.reason ?? "Query cannot be represented as GET." }
        };
      }
    }
    return this.queryResult(recordQuery as RecordQueryInput<TRecord>, options);
  }

  async query(query?: RecordQueryInput<TRecord>, options?: RequestOptions): Promise<RecordPage<TRecord>> {
    return unwrapResult(await this.queryResult(query, options));
  }

  async queryResult(query?: RecordQueryInput<TRecord>, options: RequestOptions = {}): Promise<BaseResult<RecordPage<TRecord>>> {
    return this.transport.request<RecordPage<TRecord>>({
      method: "POST",
      path: `/collections/${encodePathSegment(this.id)}/query`,
      body: toRecordQuery(query),
      headers: options.headers,
      signal: options.signal,
      correlationId: options.correlationId
    });
  }

  async get(id: string, options?: RequestOptions): Promise<RecordEnvelope<TRecord>> {
    return unwrapResult(await this.getResult(id, options));
  }

  async getResult(id: string, options: RequestOptions = {}): Promise<BaseResult<RecordEnvelope<TRecord>>> {
    return this.transport.request<RecordEnvelope<TRecord>>({ path: `${this.recordsPath()}/${encodePathSegment(id)}`, headers: options.headers, signal: options.signal, correlationId: options.correlationId });
  }

  async create(input: CreateInput<TRecord>, options?: CreateOptions): Promise<RecordEnvelope<TRecord>> {
    return unwrapResult(await this.createResult(input, options));
  }

  async createResult(input: CreateInput<TRecord>, options: CreateOptions = {}): Promise<BaseResult<RecordEnvelope<TRecord>>> {
    const conflict = createConflict(input, options);
    if (conflict) return validationFailure(conflict);
    const body = normalizeCreate(input, options);
    const headers = new Headers(options.headers);
    if (body.idempotencyKey) headers.set("Idempotency-Key", body.idempotencyKey);
    return this.transport.request<RecordEnvelope<TRecord>>({
      method: "POST",
      path: this.recordsPath(),
      body,
      headers,
      signal: options.signal,
      correlationId: options.correlationId,
      context: "create"
    });
  }

  async patch(id: string, input: PatchInput<TRecord>, options?: MutationOptions): Promise<RecordEnvelope<TRecord>> {
    return unwrapResult(await this.patchResult(id, input, options));
  }

  async patchResult(id: string, input: PatchInput<TRecord>, options: MutationOptions = {}): Promise<BaseResult<RecordEnvelope<TRecord>>> {
    const conflict = revisionConflict(input, options);
    if (conflict) return validationFailure(conflict);
    const body = normalizePatch(input, options);
    return this.mutate<RecordPatchRequest<TRecord>, RecordEnvelope<TRecord>>("PATCH", id, body, options, "patch");
  }

  async replace(id: string, input: ReplaceInput<TRecord>, options?: MutationOptions): Promise<RecordEnvelope<TRecord>> {
    return unwrapResult(await this.replaceResult(id, input, options));
  }

  async replaceResult(id: string, input: ReplaceInput<TRecord>, options: MutationOptions = {}): Promise<BaseResult<RecordEnvelope<TRecord>>> {
    const conflict = revisionConflict(input, options);
    if (conflict) return validationFailure(conflict);
    const body = normalizeReplace(input, options);
    return this.mutate<RecordReplaceRequest<TRecord>, RecordEnvelope<TRecord>>("PUT", id, body, options, "replace");
  }

  async delete(id: string, options?: DeleteOptions): Promise<DeleteResult<TRecord>> {
    return unwrapResult(await this.deleteResult(id, options));
  }

  async deleteResult(id: string, options: DeleteOptions = {}): Promise<BaseResult<DeleteResult<TRecord>>> {
    const body: RecordDeleteRequest | undefined = options.expectedRevision || options.returnPrevious
      ? { expectedRevision: options.expectedRevision, returnPrevious: options.returnPrevious }
      : undefined;
    return this.mutate<RecordDeleteRequest | undefined, DeleteResult<TRecord>>("DELETE", id, body, options, "delete");
  }

  async definition(options?: RequestOptions): Promise<CollectionDefinition> {
    return this.definitionLoader(this.id, options);
  }

  async definitionResult(options?: RequestOptions): Promise<BaseResult<CollectionDefinition>> {
    return this.definitionResultLoader(this.id, options);
  }

  supports(operation: import("./client.js").CollectionOperation, options?: SupportsOptions): boolean | undefined {
    return this.supportsOperation(this.id, operation, options);
  }

  private recordsPath(): string {
    return `/collections/${encodePathSegment(this.id)}/records`;
  }

  private mutate<TBody, TValue>(method: string, id: string, body: TBody, options: MutationOptions, context: "patch" | "replace" | "delete"): Promise<BaseResult<TValue>> {
    const headers = new Headers(options.headers);
    if (options.expectedRevision) headers.set("If-Match", options.expectedRevision);
    return this.transport.request<TValue>({
      method,
      path: `${this.recordsPath()}/${encodePathSegment(id)}`,
      body,
      headers,
      signal: options.signal,
      correlationId: options.correlationId,
      context
    });
  }
}

function normalizeCreate<TRecord extends JsonObject>(input: CreateInput<TRecord>, options: CreateOptions): RecordCreateRequest<TRecord> {
  if (isCreateRequest(input)) return { ...input, requestedId: input.requestedId ?? options.requestedId, idempotencyKey: input.idempotencyKey ?? options.idempotencyKey };
  return { payload: { kind: "json", json: input }, requestedId: options.requestedId, idempotencyKey: options.idempotencyKey };
}

function normalizePatch<TRecord extends JsonObject>(input: PatchInput<TRecord>, options: MutationOptions): RecordPatchRequest<TRecord> {
  if (isPatchRequest(input)) return { ...input, expectedRevision: input.expectedRevision ?? options.expectedRevision };
  return { patch: { kind: "fieldMap", fields: input }, expectedRevision: options.expectedRevision };
}

function normalizeReplace<TRecord extends JsonObject>(input: ReplaceInput<TRecord>, options: MutationOptions): RecordReplaceRequest<TRecord> {
  if (isReplaceRequest(input)) return { ...input, expectedRevision: input.expectedRevision ?? options.expectedRevision };
  return { payload: { kind: "json", json: input }, expectedRevision: options.expectedRevision };
}

function isCreateRequest<TRecord extends JsonObject>(input: CreateInput<TRecord>): input is RecordCreateRequest<TRecord> {
  return typeof input === "object" && input !== null && "payload" in input;
}

function isPatchRequest<TRecord extends JsonObject>(input: PatchInput<TRecord>): input is RecordPatchRequest<TRecord> {
  return typeof input === "object" && input !== null && "patch" in input;
}

function isReplaceRequest<TRecord extends JsonObject>(input: ReplaceInput<TRecord>): input is RecordReplaceRequest<TRecord> {
  return typeof input === "object" && input !== null && "payload" in input;
}

function createConflict<TRecord extends JsonObject>(input: CreateInput<TRecord>, options: CreateOptions): string | undefined {
  if (!isCreateRequest(input)) return undefined;
  if (input.idempotencyKey && options.idempotencyKey && input.idempotencyKey !== options.idempotencyKey) {
    return "idempotencyKey option conflicts with request body.";
  }
  if (input.requestedId && options.requestedId && input.requestedId !== options.requestedId) {
    return "requestedId option conflicts with request body.";
  }
  return undefined;
}

function revisionConflict(input: unknown, options: MutationOptions): string | undefined {
  const expectedRevision = typeof input === "object" && input !== null && "expectedRevision" in input && typeof input.expectedRevision === "string"
    ? input.expectedRevision
    : undefined;
  return expectedRevision && options.expectedRevision && expectedRevision !== options.expectedRevision
    ? "expectedRevision option conflicts with request body."
    : undefined;
}

function validationFailure<T>(message: string): BaseResult<T> {
  return {
    ok: false,
    status: "validationFailed",
    error: {
      status: "validationFailed",
      code: "base.client.optionConflict",
      message,
      category: "validation"
    }
  };
}
