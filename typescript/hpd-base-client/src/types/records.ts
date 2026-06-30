/** JSON object payload accepted and returned by HPD.BASE record routes. */
export type JsonObject = Record<string, unknown>;

/** Record identifier wire type. */
export type RecordId = string;

/** Record revision token wire type. */
export type RevisionToken = string;

/** ISO-8601 date/time string as returned by BASE HTTP JSON. */
export type IsoDateTimeString = string;

/** Portable record payload shape used by the ASP.NET projection. */
export type RecordPayload<TRecord extends JsonObject = JsonObject> =
  | { kind: "json"; json: TRecord; fields?: undefined }
  | { kind: "fieldMap"; fields: Partial<Record<keyof TRecord & string, unknown>>; json?: undefined };

/** Record envelope returned by get/list/query/mutation routes. */
export interface RecordEnvelope<TRecord extends JsonObject = JsonObject> {
  collectionId: string;
  id: RecordId;
  payload: RecordPayload<TRecord>;
  metadata: RecordMetadata;
  policy?: RecordPolicyMetadata;
  includes?: Record<string, RecordIncludeValue<TRecord>>;
}

export interface RecordIncludeValue<TRecord extends JsonObject = JsonObject> {
  path: string;
  kind: "none" | "one" | "many";
  record?: RecordEnvelope<TRecord>;
  records?: RecordEnvelope<TRecord>[];
  truncated?: boolean;
  reasonCode?: string;
}

export interface RecordPolicyMetadata {
  redacted?: boolean;
  omittedFields?: string[];
  readOnlyFields?: string[];
  reasonCode?: string;
}

export interface RecordMetadata {
  createdAt?: IsoDateTimeString;
  updatedAt?: IsoDateTimeString;
  revision?: RevisionToken;
  eTag?: string;
  storeId?: string;
  tags?: Record<string, string>;
}

export interface RecordPage<TRecord extends JsonObject = JsonObject> {
  items: RecordEnvelope<TRecord>[];
  page: PageInfo;
  count?: CountInfo;
  dependencyToken?: string;
}

export interface PageInfo {
  page?: number;
  perPage?: number;
  offset?: number;
  limit?: number;
  cursor?: string;
  nextCursor?: string;
  hasMore?: boolean;
}

export interface CountInfo {
  mode: import("./query.js").QueryCountMode;
  total?: number;
  isExact?: boolean;
}

export interface RecordCreateRequest<TRecord extends JsonObject = JsonObject> {
  payload: RecordPayload<TRecord>;
  requestedId?: RecordId;
  idempotencyKey?: string;
}

export interface RecordPatchRequest<TRecord extends JsonObject = JsonObject> {
  patch: RecordPayload<TRecord>;
  expectedRevision?: RevisionToken;
}

export interface RecordReplaceRequest<TRecord extends JsonObject = JsonObject> {
  payload: RecordPayload<TRecord>;
  expectedRevision?: RevisionToken;
}

export interface RecordDeleteRequest {
  expectedRevision?: RevisionToken;
  returnPrevious?: boolean;
}

export interface DeleteResult<TRecord extends JsonObject = JsonObject> {
  id: RecordId;
  deleted: boolean;
  previous?: RecordEnvelope<TRecord>;
}

export type CreateInput<TRecord extends JsonObject> = RecordCreateRequest<TRecord> | TRecord;
export type PatchInput<TRecord extends JsonObject> = RecordPatchRequest<TRecord> | Partial<TRecord>;
export type ReplaceInput<TRecord extends JsonObject> = RecordReplaceRequest<TRecord> | TRecord;
