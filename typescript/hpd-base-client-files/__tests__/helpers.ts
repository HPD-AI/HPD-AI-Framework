import { createBaseClient } from "@hpd/base-client";
import { createBaseFilesClient } from "../src/index.js";
import type { BaseFilesClient } from "../src/index.js";

export interface FetchCall {
  url: string;
  init?: RequestInit;
}

export function createJsonResponse(body: unknown, init: ResponseInit = {}): Response {
  return new Response(JSON.stringify(body), {
    status: init.status ?? 200,
    headers: {
      "content-type": "application/json",
      ...(init.headers && !(init.headers instanceof Headers) ? init.headers as Record<string, string> : {})
    }
  });
}

export function createFetch(responses: Response[] | ((call: FetchCall) => Response | Promise<Response>)) {
  const calls: FetchCall[] = [];
  const fetch = async (input: RequestInfo | URL, init?: RequestInit) => {
    const call = { url: String(input), init };
    calls.push(call);
    if (Array.isArray(responses)) {
      const response = responses.shift();
      if (!response) throw new Error("No fake response queued.");
      return response;
    }
    return responses(call);
  };
  return { fetch: fetch as typeof globalThis.fetch, calls };
}

export function createFilesTestClient(responses: Response[] | ((call: FetchCall) => Response | Promise<Response>)): { files: BaseFilesClient; calls: FetchCall[] } {
  const fake = createFetch(responses);
  const base = createBaseClient({
    baseUrl: "/base",
    fetch: fake.fetch,
    headers: async () => ({ Authorization: "Bearer token" }),
    clientName: "files-test"
  });
  return { files: createBaseFilesClient(base), calls: fake.calls };
}

export const metadata = {
  bucketId: "avatars",
  objectId: "obj-1",
  key: "users/u1/avatar.png",
  name: "avatar.png",
  contentType: "image/png",
  sizeBytes: 5,
  checksum: "sha256:abc",
  revision: "rev-1",
  createdAt: "2026-01-01T00:00:00Z",
  updatedAt: "2026-01-01T00:00:00Z",
  publicMetadata: { alt: "avatar" }
};

export const filesManifest = {
  manifestVersion: "1",
  contractVersion: "1",
  runtime: { runtimeId: "runtime" },
  compatibility: {},
  visibility: "public",
  generatedAt: "2026-01-01T00:00:00Z",
  projections: [
    {
      id: "files",
      kind: "aspnet",
      packageId: "HPD.Base.Files.AspNetCore",
      packageVersion: "0.1.0",
      contractVersionRange: "1",
      routes: [
        { operationId: "base.files.objects.upload", method: "post", path: "/base/files/{bucketId}/objects", responseDtoId: "FileObjectUploadResult" },
        { operationId: "base.files.objects.list", method: "get", path: "/base/files/{bucketId}/objects", responseDtoId: "FileObjectListResult" },
        { operationId: "base.files.objects.download", method: "get", path: "/base/files/{bucketId}/objects/{objectId}", responseDtoId: "stream" },
        { operationId: "base.files.objects.head", method: "head", path: "/base/files/{bucketId}/objects/{objectId}", responseDtoId: "void" },
        { operationId: "base.files.objects.metadata.get", method: "get", path: "/base/files/{bucketId}/objects/{objectId}/metadata", responseDtoId: "FileObjectMetadata" },
        { operationId: "base.files.objects.delete", method: "delete", path: "/base/files/{bucketId}/objects/{objectId}", responseDtoId: "void" }
      ]
    }
  ]
};

export const filesCapabilities = {
  descriptorVersion: "1",
  runtimeId: "runtime",
  families: [
    {
      familyId: "files",
      familyVersion: "1",
      status: "degraded",
      features: [
        { featureId: "files.object.upload", version: "1", status: "degraded", supportLevel: "optional", scope: "runtime" },
        { featureId: "files.object.download", version: "1", status: "degraded", supportLevel: "optional", scope: "runtime" },
        { featureId: "files.object.metadata.read", version: "1", status: "degraded", supportLevel: "optional", scope: "runtime" },
        { featureId: "files.object.delete", version: "1", status: "degraded", supportLevel: "optional", scope: "runtime" },
        { featureId: "files.object.list", version: "1", status: "degraded", supportLevel: "optional", scope: "runtime" }
      ]
    }
  ]
};
