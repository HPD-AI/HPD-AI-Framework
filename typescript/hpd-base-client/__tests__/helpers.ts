export interface FetchCall {
  url: string;
  init?: RequestInit;
}

export function createJsonResponse(body: unknown, init: ResponseInit = {}): Response {
  return new Response(JSON.stringify(body), {
    status: init.status ?? 200,
    headers: {
      "content-type": init.headers instanceof Headers ? init.headers.get("content-type") ?? "application/json" : "application/json",
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

export async function readJsonBody(init?: RequestInit): Promise<unknown> {
  if (!init?.body) return undefined;
  if (typeof init.body === "string") return JSON.parse(init.body);
  throw new Error("Expected string body.");
}

export const manifest = {
  manifestVersion: "1",
  contractVersion: "1",
  runtime: { runtimeId: "runtime" },
  compatibility: {},
  visibility: "public",
  generatedAt: "2026-01-01T00:00:00Z"
};

export const schema = {
  runtimeId: "runtime",
  contractVersion: "1",
  visibility: "public",
  collections: [
    {
      id: "items",
      name: "items",
      kind: "document",
      schemaMode: "loose",
      unknownFields: "preserve",
      operations: { list: true, get: true, create: true, patch: true, replace: true, delete: true },
      fields: [
        { id: "title", name: "title", type: "string" },
        { id: "publishedAt", name: "publishedAt", type: "dateTime", format: "dateTime" }
      ]
    }
  ]
};

export const capabilities = {
  descriptorVersion: "1",
  runtimeId: "runtime",
  families: [
    {
      familyId: "records",
      familyVersion: "1",
      status: "available",
      features: [{ featureId: "base.records.crud", version: "1", status: "available", supportLevel: "required", scope: "runtime" }]
    }
  ]
};
