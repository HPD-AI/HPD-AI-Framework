import type { BaseResult } from "./result.js";
import { BaseHttpTransport, parseBaseJson } from "./transport.js";

export interface BaseFileMetadata {
  readonly bucketId: string;
  readonly objectId: string;
  readonly key?: string;
  readonly name?: string;
  readonly contentType?: string;
  readonly sizeBytes: number;
  readonly checksum?: string;
  readonly revision?: string;
}
export interface BaseFilePage { readonly items: readonly BaseFileMetadata[]; readonly cursor?: string; }
export interface BaseFileUploadOptions { readonly key?: string; readonly name?: string; readonly contentType?: string; readonly checksum?: string; readonly signal?: AbortSignal; }

export class BaseFileBucket {
  public constructor(private readonly transport: BaseHttpTransport, public readonly id: string) {}
  public upload(content: BodyInit, options: BaseFileUploadOptions = {}): Promise<BaseResult<BaseFileMetadata>> {
    const headers: Record<string, string> = {};
    if (options.key !== undefined) headers["X-HPD-File-Key"] = options.key;
    if (options.name !== undefined) headers["X-HPD-File-Name"] = options.name;
    if (options.checksum !== undefined) headers["X-HPD-File-Checksum"] = options.checksum;
    return this.transport.binary("POST", this.route("objects"), content, options.contentType ?? "application/octet-stream", options.signal, headers)
      .then(result => decodeJson<BaseFileMetadata>(result));
  }
  public list(input: { readonly prefix?: string; readonly limit?: number; readonly cursor?: string; readonly signal?: AbortSignal } = {}): Promise<BaseResult<BaseFilePage>> {
    const query = new URLSearchParams();
    if (input.prefix !== undefined) query.set("prefix", input.prefix);
    if (input.limit !== undefined) query.set("limit", String(input.limit));
    if (input.cursor !== undefined) query.set("cursor", input.cursor);
    return this.transport.json("GET", `${this.route("objects")}${query.size === 0 ? "" : `?${query}`}`, undefined, input.signal);
  }
  public metadata(objectId: string, signal?: AbortSignal): Promise<BaseResult<BaseFileMetadata>> { return this.transport.json("GET", this.route(`objects/${encodeURIComponent(objectId)}/metadata`), undefined, signal); }
  public download(objectId: string, signal?: AbortSignal): Promise<BaseResult<ReadableStream<Uint8Array>>> { return this.transport.stream("GET", this.route(`objects/${encodeURIComponent(objectId)}`), signal); }
  public async delete(objectId: string, signal?: AbortSignal): Promise<BaseResult<{ readonly deleted: true }>> { const result = await this.transport.empty("DELETE", this.route(`objects/${encodeURIComponent(objectId)}`), signal); return result.ok ? { ...result, value: { deleted: true } } : result; }
  private route(suffix: string): string { return `files/${encodeURIComponent(this.id)}/${suffix}`; }
}

export class BaseFilesClient {
  public constructor(private readonly transport: BaseHttpTransport) {}
  public bucket(id: string): BaseFileBucket { if (id.length === 0) throw new TypeError("base.client.configurationInvalid"); return new BaseFileBucket(this.transport, id); }
}

function decodeJson<T>(result: BaseResult<Uint8Array>): BaseResult<T> {
  if (!result.ok) return result;
  try { return { ...result, value: parseBaseJson(new TextDecoder("utf-8", { fatal: true }).decode(result.value)) as T }; }
  catch { return { ok: false, error: { code: "base.client.responseInvalid", category: "unexpected", message: "The BASE response was invalid." }, correlationId: result.correlationId, retry: "never" }; }
}
