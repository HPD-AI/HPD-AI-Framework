export type FileUploadBody =
  | BodyInit
  | ArrayBuffer
  | ArrayBufferView
  | Blob
  | File
  | ReadableStream<Uint8Array>
  | string;

export interface FileRequestOptions {
  signal?: AbortSignal;
  headers?: HeadersInit;
  correlationId?: string;
}

export interface FileUploadOptions extends FileRequestOptions {
  key: string;
  name?: string;
  contentType?: string;
  checksum?: string;
}

export interface FileListOptions extends FileRequestOptions {
  prefix?: string;
  limit?: number;
  cursor?: string;
}

export interface FileDownloadOptions extends FileRequestOptions {
  accept?: string;
}

export type FileCapabilityMode = "check" | "check-allow-degraded" | "route-presence" | "off";

export interface BaseFilesClientOptions {
  routePrefix?: string;
  capabilities?: FileCapabilityMode;
}

export type FileOperation = "upload" | "download" | "head" | "metadata" | "delete" | "list";

export interface FileSupportsOptions {
  allowDegraded?: boolean;
  requireRoute?: boolean;
}

export interface FileRouteOptions {
  bucketId?: string;
  objectId?: string;
}
