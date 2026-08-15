import type { BaseResult } from "./result.js";
import { BaseHttpTransport, parseBaseJson } from "./transport.js";

export interface BaseFileMetadata {
  readonly bucketId: string;
  readonly objectId: string;
  readonly key?: string;
  readonly name?: string;
  readonly contentType?: string;
  readonly sizeBytes?: number;
  readonly checksum?: string;
  readonly revision?: string;
  readonly createdAt?: string;
  readonly updatedAt?: string;
  readonly ownerSubjectId?: string;
  readonly tenantId?: string;
  readonly publicMetadata?: Readonly<Record<string, string>>;
}
export interface BaseFileUploadResult { readonly metadata: BaseFileMetadata; readonly created: boolean; }
export interface BaseFilePage { readonly items: readonly BaseFileMetadata[]; readonly nextCursor?: string; }
export interface BaseFileUploadOptions { readonly key?: string; readonly name?: string; readonly contentType?: string; readonly checksum?: string; readonly signal?: AbortSignal; }

export class BaseFileBucket {
  public constructor(private readonly transport: BaseHttpTransport, public readonly id: string) {}
  public upload(content: BodyInit, options: BaseFileUploadOptions = {}): Promise<BaseResult<BaseFileUploadResult>> {
    const headers: Record<string, string> = {};
    if (options.key !== undefined) headers["X-HPD-File-Key"] = options.key;
    if (options.name !== undefined) headers["X-HPD-File-Name"] = options.name;
    if (options.checksum !== undefined) headers["X-HPD-File-Checksum"] = options.checksum;
    return this.transport.binary("POST", this.route("objects"), content, options.contentType ?? "application/octet-stream", options.signal, headers)
      .then(result => decodeJson(result, isUploadResult));
  }
  public list(input: { readonly prefix?: string; readonly limit?: number; readonly cursor?: string; readonly signal?: AbortSignal } = {}): Promise<BaseResult<BaseFilePage>> {
    const query = new URLSearchParams();
    if (input.prefix !== undefined) query.set("prefix", input.prefix);
    if (input.limit !== undefined) query.set("limit", String(input.limit));
    if (input.cursor !== undefined) query.set("cursor", input.cursor);
    return this.transport.json("GET", `${this.route("objects")}${query.size === 0 ? "" : `?${query}`}`, undefined, input.signal).then(result => validateResult(result, isPage));
  }
  public metadata(objectId: string, signal?: AbortSignal): Promise<BaseResult<BaseFileMetadata>> { return this.transport.json("GET", this.route(`objects/${encodeURIComponent(objectId)}/metadata`), undefined, signal).then(result => validateResult(result, isMetadata)); }
  public download(objectId: string, signal?: AbortSignal): Promise<BaseResult<ReadableStream<Uint8Array>>> { return this.transport.stream("GET", this.route(`objects/${encodeURIComponent(objectId)}`), signal); }
  public async delete(objectId: string, signal?: AbortSignal): Promise<BaseResult<{ readonly deleted: true }>> { const result = await this.transport.empty("DELETE", this.route(`objects/${encodeURIComponent(objectId)}`), signal); return result.ok ? { ...result, value: { deleted: true } } : result; }
  private route(suffix: string): string { return `files/${encodeURIComponent(this.id)}/${suffix}`; }
}

export class BaseFilesClient {
  public constructor(private readonly transport: BaseHttpTransport) {}
  public bucket(id: string): BaseFileBucket { if (id.length === 0) throw new TypeError("base.client.configurationInvalid"); return new BaseFileBucket(this.transport, id); }
}

function decodeJson<T>(result: BaseResult<Uint8Array>, validate: (value: unknown) => value is T): BaseResult<T> {
  if (!result.ok) return result;
  try { const value = parseBaseJson(new TextDecoder("utf-8", { fatal: true }).decode(result.value)); return validate(value) ? { ...result, value } : invalid(result.correlationId); }
  catch { return { ok: false, error: { code: "base.client.responseInvalid", category: "unexpected", message: "The BASE response was invalid." }, correlationId: result.correlationId, retry: "never" }; }
}
function validateResult<T>(result: BaseResult<unknown>, validate: (value: unknown) => value is T): BaseResult<T> { return !result.ok ? result : validate(result.value) ? { ...result, value: result.value } : invalid(result.correlationId); }
function invalid<T>(correlationId: string): BaseResult<T> { return { ok: false, error: { code: "base.client.responseInvalid", category: "unexpected", message: "The BASE response was invalid." }, correlationId, retry: "never" }; }
function isMetadata(value: unknown): value is BaseFileMetadata {
  if (!record(value) || !only(value, ["bucketId", "objectId", "key", "name", "contentType", "sizeBytes", "checksum", "revision", "createdAt", "updatedAt", "ownerSubjectId", "tenantId", "publicMetadata"]) || !text(value.bucketId) || !text(value.objectId)) return false;
  if (value.sizeBytes !== undefined && (!Number.isSafeInteger(value.sizeBytes) || (value.sizeBytes as number) < 0)) return false;
  if (!["key", "name", "contentType", "checksum", "revision", "ownerSubjectId", "tenantId"].every(key => value[key] === undefined || text(value[key]))) return false;
  if (!["createdAt", "updatedAt"].every(key => value[key] === undefined || utc(value[key]))) return false;
  return value.publicMetadata === undefined || stringMap(value.publicMetadata);
}
function isUploadResult(value: unknown): value is BaseFileUploadResult { return record(value) && only(value, ["metadata", "created"]) && isMetadata(value.metadata) && typeof value.created === "boolean"; }
function isPage(value: unknown): value is BaseFilePage { return record(value) && only(value, ["items", "nextCursor"]) && Array.isArray(value.items) && value.items.length <= 1024 && value.items.every(isMetadata) && (value.nextCursor === undefined || text(value.nextCursor)); }
function record(value: unknown): value is Record<string, unknown> { return typeof value === "object" && value !== null && !Array.isArray(value); }
function only(value: Record<string, unknown>, keys: readonly string[]): boolean { return Object.keys(value).every(key => keys.includes(key)); }
function text(value: unknown): value is string { return typeof value === "string" && value.length > 0 && value.length <= 4096 && !/[\u0000-\u001f\u007f]/u.test(value); }
function utc(value: unknown): value is string { return text(value) && /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?Z$/u.test(value) && !Number.isNaN(Date.parse(value)); }
function stringMap(value: unknown): value is Readonly<Record<string, string>> { return record(value) && Object.keys(value).length <= 128 && Object.entries(value).every(([key, item]) => text(key) && text(item)); }
