import type { FileObjectHeaders } from "./types/files.js";

export function fileObjectHeaders(headers: Headers): FileObjectHeaders {
  const length = headers.get("content-length");
  const parsedLength = length === null ? undefined : Number(length);
  return {
    contentType: headers.get("content-type") ?? undefined,
    contentLength: Number.isFinite(parsedLength) ? parsedLength : undefined,
    etag: headers.get("etag") ?? undefined,
    lastModified: headers.get("last-modified") ?? undefined,
    cacheControl: headers.get("cache-control") ?? undefined,
    correlationId: headers.get("x-correlation-id") ?? undefined
  };
}
