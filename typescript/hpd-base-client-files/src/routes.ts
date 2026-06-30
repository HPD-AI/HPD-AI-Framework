import type { FileOperation, FileRouteOptions } from "./types/options.js";

export const fileRouteOperationIds = {
  upload: "base.files.objects.upload",
  download: "base.files.objects.download",
  head: "base.files.objects.head",
  metadata: "base.files.objects.metadata.get",
  delete: "base.files.objects.delete",
  list: "base.files.objects.list"
} as const satisfies Record<FileOperation, string>;

export function normalizeRoutePrefix(prefix: string | undefined): string {
  const value = prefix?.trim() || "/files";
  const withSlash = value.startsWith("/") ? value : `/${value}`;
  return withSlash.replace(/\/+$/u, "");
}

export function encodePathSegment(value: string): string {
  return encodeURIComponent(value);
}

export function fileRoutePath(routePrefix: string, operation: FileOperation, options: FileRouteOptions = {}): string {
  const bucketId = options.bucketId ? encodePathSegment(options.bucketId) : "{bucketId}";
  const objectId = options.objectId ? encodePathSegment(options.objectId) : "{objectId}";
  const objects = `${routePrefix}/${bucketId}/objects`;
  switch (operation) {
    case "upload":
    case "list":
      return objects;
    case "download":
    case "head":
    case "delete":
      return `${objects}/${objectId}`;
    case "metadata":
      return `${objects}/${objectId}/metadata`;
  }
}
