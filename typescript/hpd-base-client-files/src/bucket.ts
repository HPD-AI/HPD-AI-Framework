import { unwrapResult } from "@hpd/base-client";
import type { BaseClientExtensionContext } from "@hpd/base-client";
import type { BaseResult } from "@hpd/base-client/types";
import { fileObjectHeaders } from "./download.js";
import { fileRoutePath } from "./routes.js";
import { parseJsonResult, rawRequest, voidResult } from "./result.js";
import type { FileObjectHeaders, FileObjectListResult, FileObjectMetadata, FileObjectUploadResult } from "./types/files.js";
import type { FileDownloadOptions, FileListOptions, FileOperation, FileRequestOptions, FileSupportsOptions, FileUploadBody, FileUploadOptions } from "./types/options.js";

export interface BaseFileBucketClient {
  readonly id: string;
  upload(input: FileUploadBody, options: FileUploadOptions): Promise<FileObjectUploadResult>;
  uploadResult(input: FileUploadBody, options: FileUploadOptions): Promise<BaseResult<FileObjectUploadResult>>;
  list(options?: FileListOptions): Promise<FileObjectListResult>;
  listResult(options?: FileListOptions): Promise<BaseResult<FileObjectListResult>>;
  metadata(objectId: string, options?: FileRequestOptions): Promise<FileObjectMetadata>;
  metadataResult(objectId: string, options?: FileRequestOptions): Promise<BaseResult<FileObjectMetadata>>;
  head(objectId: string, options?: FileRequestOptions): Promise<FileObjectHeaders>;
  headResult(objectId: string, options?: FileRequestOptions): Promise<BaseResult<FileObjectHeaders>>;
  download(objectId: string, options?: FileDownloadOptions): Promise<Response>;
  downloadResult(objectId: string, options?: FileDownloadOptions): Promise<BaseResult<Response>>;
  downloadBlob(objectId: string, options?: FileDownloadOptions): Promise<Blob>;
  downloadBlobResult(objectId: string, options?: FileDownloadOptions): Promise<BaseResult<Blob>>;
  downloadArrayBuffer(objectId: string, options?: FileDownloadOptions): Promise<ArrayBuffer>;
  downloadArrayBufferResult(objectId: string, options?: FileDownloadOptions): Promise<BaseResult<ArrayBuffer>>;
  delete(objectId: string, options?: FileRequestOptions): Promise<void>;
  deleteResult(objectId: string, options?: FileRequestOptions): Promise<BaseResult<void>>;
  supports(operation: FileOperation, options?: FileSupportsOptions): boolean | undefined;
}

export class BaseFileBucketClientImpl implements BaseFileBucketClient {
  constructor(
    readonly id: string,
    private readonly extension: BaseClientExtensionContext,
    private readonly routePrefix: string,
    private readonly supportsOperation: (operation: FileOperation, options?: FileSupportsOptions) => boolean | undefined
  ) {}

  async upload(input: FileUploadBody, options: FileUploadOptions): Promise<FileObjectUploadResult> {
    return unwrapResult(await this.uploadResult(input, options));
  }

  async uploadResult(input: FileUploadBody, options: FileUploadOptions): Promise<BaseResult<FileObjectUploadResult>> {
    const headers = new Headers(options.headers);
    headers.set("X-HPD-File-Key", options.key);
    const inferredName = options.name ?? fileName(input);
    const contentType = options.contentType ?? fileContentType(input);
    if (inferredName) headers.set("X-HPD-File-Name", inferredName);
    if (options.checksum) headers.set("X-HPD-File-Checksum", options.checksum);
    const result = await rawRequest({
      extension: this.extension,
      operation: "upload",
      method: "POST",
      path: fileRoutePath(this.routePrefix, "upload", { bucketId: this.id }),
      body: input as BodyInit,
      headers,
      signal: options.signal,
      correlationId: options.correlationId,
      contentType: contentType ?? false
    });
    return parseJsonResult<FileObjectUploadResult>(result);
  }

  async list(options: FileListOptions = {}): Promise<FileObjectListResult> {
    return unwrapResult(await this.listResult(options));
  }

  async listResult(options: FileListOptions = {}): Promise<BaseResult<FileObjectListResult>> {
    const query = new URLSearchParams();
    if (options.prefix !== undefined) query.set("prefix", options.prefix);
    if (options.limit !== undefined) query.set("limit", String(options.limit));
    if (options.cursor !== undefined) query.set("cursor", options.cursor);
    const result = await rawRequest({
      extension: this.extension,
      operation: "list",
      method: "GET",
      path: fileRoutePath(this.routePrefix, "list", { bucketId: this.id }),
      query: query.size ? query : undefined,
      headers: options.headers,
      signal: options.signal,
      correlationId: options.correlationId
    });
    return parseJsonResult<FileObjectListResult>(result);
  }

  async metadata(objectId: string, options: FileRequestOptions = {}): Promise<FileObjectMetadata> {
    return unwrapResult(await this.metadataResult(objectId, options));
  }

  async metadataResult(objectId: string, options: FileRequestOptions = {}): Promise<BaseResult<FileObjectMetadata>> {
    const result = await rawRequest({
      extension: this.extension,
      operation: "metadata",
      method: "GET",
      path: fileRoutePath(this.routePrefix, "metadata", { bucketId: this.id, objectId }),
      headers: options.headers,
      signal: options.signal,
      correlationId: options.correlationId
    });
    return parseJsonResult<FileObjectMetadata>(result);
  }

  async head(objectId: string, options: FileRequestOptions = {}): Promise<FileObjectHeaders> {
    return unwrapResult(await this.headResult(objectId, options));
  }

  async headResult(objectId: string, options: FileRequestOptions = {}): Promise<BaseResult<FileObjectHeaders>> {
    const result = await rawRequest({
      extension: this.extension,
      operation: "head",
      method: "HEAD",
      path: fileRoutePath(this.routePrefix, "head", { bucketId: this.id, objectId }),
      headers: options.headers,
      signal: options.signal,
      correlationId: options.correlationId,
      accept: false
    });
    if (!result.ok) return result;
    return { ...result, value: fileObjectHeaders(result.value.headers) };
  }

  async download(objectId: string, options: FileDownloadOptions = {}): Promise<Response> {
    return unwrapResult(await this.downloadResult(objectId, options));
  }

  downloadResult(objectId: string, options: FileDownloadOptions = {}): Promise<BaseResult<Response>> {
    return rawRequest({
      extension: this.extension,
      operation: "download",
      method: "GET",
      path: fileRoutePath(this.routePrefix, "download", { bucketId: this.id, objectId }),
      headers: options.headers,
      signal: options.signal,
      correlationId: options.correlationId,
      accept: options.accept ?? "application/octet-stream"
    });
  }

  async downloadBlob(objectId: string, options: FileDownloadOptions = {}): Promise<Blob> {
    return unwrapResult(await this.downloadBlobResult(objectId, options));
  }

  async downloadBlobResult(objectId: string, options: FileDownloadOptions = {}): Promise<BaseResult<Blob>> {
    const result = await this.downloadResult(objectId, options);
    if (!result.ok) return result;
    return { ...result, value: await result.value.blob() };
  }

  async downloadArrayBuffer(objectId: string, options: FileDownloadOptions = {}): Promise<ArrayBuffer> {
    return unwrapResult(await this.downloadArrayBufferResult(objectId, options));
  }

  async downloadArrayBufferResult(objectId: string, options: FileDownloadOptions = {}): Promise<BaseResult<ArrayBuffer>> {
    const result = await this.downloadResult(objectId, options);
    if (!result.ok) return result;
    return { ...result, value: await result.value.arrayBuffer() };
  }

  async delete(objectId: string, options: FileRequestOptions = {}): Promise<void> {
    return unwrapResult(await this.deleteResult(objectId, options));
  }

  async deleteResult(objectId: string, options: FileRequestOptions = {}): Promise<BaseResult<void>> {
    return voidResult(await rawRequest({
      extension: this.extension,
      operation: "delete",
      method: "DELETE",
      path: fileRoutePath(this.routePrefix, "delete", { bucketId: this.id, objectId }),
      headers: options.headers,
      signal: options.signal,
      correlationId: options.correlationId
    }));
  }

  supports(operation: FileOperation, options?: FileSupportsOptions): boolean | undefined {
    return this.supportsOperation(operation, options);
  }
}

function fileName(input: FileUploadBody): string | undefined {
  return typeof File !== "undefined" && input instanceof File ? input.name : undefined;
}

function fileContentType(input: FileUploadBody): string | undefined {
  return typeof Blob !== "undefined" && input instanceof Blob && input.type ? input.type : undefined;
}
