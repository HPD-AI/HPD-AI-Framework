import { describe, expect, it, vi } from "vitest";
import { createGatewayClient, gatewayOperations } from "../src/index.js";
import { gatewayRuntimeSchemaConstraints, gatewayRuntimeSchemas } from "../src/generated/runtime.js";

const auth = { getAccessToken: vi.fn(async () => ({ value: "token" })) };
const json = (value: unknown, status = 200, headers: Record<string, string> = {}) =>
  new Response(JSON.stringify(value), { status, headers: { "content-type": "application/json", ...headers } });
const cancelableResponse = (headers: Record<string, string>) => {
  const canceled = vi.fn();
  const body = new ReadableStream<Uint8Array>({
    start(controller) { controller.enqueue(new TextEncoder().encode("{}")); },
    cancel: canceled,
  });
  return { value: new Response(body, { status: 200, headers }), canceled };
};

type RuntimeOperation = (typeof gatewayOperations)[number];
type RuntimeSchema = Readonly<Record<string, unknown>>;
const schemaPrefix = "#/components/schemas/";
const runtimeSchemas = gatewayRuntimeSchemas as unknown as Readonly<Record<string, RuntimeSchema>>;

function operationInput(operation: RuntimeOperation): Record<string, unknown> {
  const input: Record<string, unknown> = { path: {} };
  for (const constraint of operation.parameterConstraints) {
    const containerName = constraint.location === "path" ? "path" : constraint.location === "query" ? "query" : "headers";
    const container = input[containerName] as Record<string, unknown> | undefined ?? (input[containerName] = {}) as Record<string, unknown>;
    const property = constraint.name === "X-Correlation-ID" ? "correlationId" : constraint.name === "Idempotency-Key" ? "idempotencyKey" : constraint.name === "If-Match" ? "desiredPrecondition" : constraint.name;
    if (!constraint.required && constraint.name !== "maximum" && constraint.name !== "cursor") continue;
    container[property] = constraint.name === "maximum" ? operation.pagination.defaultMaximum : constraint.name === "If-Match" ? { kind: "create-only" } : "value";
  }
  if (operation.requestBody.presence === "required") input.body = sampleReference(operation.requestBody.schemaRef!);
  return input;
}

function sampleReference(reference: string): unknown {
  return sampleSchema(runtimeSchemas[reference.slice(schemaPrefix.length)]!, reference, new Set());
}

function sampleSchema(schema: RuntimeSchema, reference: string, seen: Set<string>): unknown {
  if (typeof schema.$ref === "string") {
    if (seen.has(schema.$ref)) return null;
    const next = new Set(seen); next.add(schema.$ref);
    return sampleSchema(runtimeSchemas[schema.$ref.slice(schemaPrefix.length)]!, schema.$ref, next);
  }
  if (schema.const !== undefined) return schema.const;
  if (Array.isArray(schema.enum)) return schema.enum[0];
  if (Array.isArray(schema.oneOf)) return sampleSchema(schema.oneOf[0] as RuntimeSchema, reference, new Set(seen));
  const types = Array.isArray(schema.type) ? schema.type : [schema.type];
  const type = types.find(value => value !== "null");
  if (type === "string") return sampleString(schema);
  if (type === "integer" || type === "number") return typeof schema.minimum === "number" ? schema.minimum : 0;
  if (type === "boolean") return false;
  if (type === "array") return Array.from({ length: typeof schema.minItems === "number" ? schema.minItems : 0 }, () => sampleSchema(schema.items as RuntimeSchema, reference, new Set(seen)));
  if (type === "object" || schema.properties !== undefined) {
    const result: Record<string, unknown> = {};
    const properties = schema.properties as Record<string, RuntimeSchema> | undefined ?? {};
    const required = Array.isArray(schema.required) ? schema.required.filter((value): value is string => typeof value === "string") : [];
    for (const property of required) result[property] = sampleSchema(properties[property]!, reference, new Set(seen));
    return result;
  }
  throw new Error(`Unsupported sample schema: ${reference}`);
}

function sampleString(schema: RuntimeSchema): string {
  if (schema.format === "uuid") return "00000000-0000-4000-8000-000000000000";
  if (schema.format === "date-time") return "2026-01-01T00:00:00Z";
  if (schema.format === "uri") return "https://gateway.example/";
  if (schema.format === "int64" || schema.format === "uint64") return "0";
  const pattern = typeof schema.pattern === "string" ? schema.pattern : "";
  if (pattern.includes("\\d{2}:\\d{2}:\\d{2}")) return "00:00:00";
  if (pattern.includes("[a-z0-9.-]")) return "value";
  const minimum = typeof schema.minLength === "number" ? schema.minLength : 1;
  return "x".repeat(Math.max(1, minimum));
}

describe("Gateway client", () => {
  it("constructs exactly one method for every generated operation", () => {
    const client = createGatewayClient({ baseUrl: "https://gateway.example", authentication: auth, fetch: vi.fn() });
    expect(Object.keys(client).sort()).toEqual(gatewayOperations.map(value => value.operation).sort());
    expect(Object.keys(client)).toHaveLength(23);
  });

  it("rejects unknown input and container members before authentication or Fetch", async () => {
    const authentication = { getAccessToken: vi.fn(async () => ({ value: "token" })) };
    const fetch = vi.fn();
    const client = createGatewayClient({ baseUrl: "https://gateway.example", authentication, fetch });
    expect(await client.capabilities({ path: {}, query: { injected: "yes" } } as never)).toMatchObject({ kind: "protocol", reason: "schema-mismatch" });
    expect(await client.capabilities({ path: {}, injected: "yes" } as never)).toMatchObject({ kind: "protocol", reason: "schema-mismatch" });
    expect(await client.revisions({ path: { ns: "n", target: "t", injected: "yes" }, query: {} } as never)).toMatchObject({ kind: "protocol", reason: "schema-mismatch" });
    expect(authentication.getAccessToken).not.toHaveBeenCalled();
    expect(fetch).not.toHaveBeenCalled();
  });

  it("projects every generated operation through its exact runtime descriptor", async () => {
    for (const operation of gatewayOperations) {
      const fetch = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
        const url = new URL(String(input));
        expect(init?.method, operation.operation).toBe(operation.method);
        const expectedPath = operation.path.replace(/\{[^}]+\}/gu, "value");
        expect(url.pathname, operation.operation).toBe(expectedPath);
        const declaredQuery = new Set(operation.parameterConstraints.filter(value => value.location === "query").map(value => value.name));
        expect([...url.searchParams.keys()].every(key => declaredQuery.has(key)), operation.operation).toBe(true);
        const headers = new Headers(init?.headers);
        expect(headers.get("authorization"), operation.operation).toBe("Bearer token");
        if (operation.parameterConstraints.some(value => value.name === "Idempotency-Key" && value.required))
          expect(headers.get("idempotency-key"), operation.operation).toBe("value");
        if (operation.requestBody.presence === "none") expect(init?.body, operation.operation).toBeUndefined();
        if (operation.requestBody.presence === "required") expect(init?.body, operation.operation).toBeDefined();
        if (init?.body !== undefined) expect(headers.get("content-type"), operation.operation).toBe(operation.requestBody.mediaTypes[0]);
        return json(sampleReference(operation.success.schemaRef), operation.success.status);
      });
      const client = createGatewayClient({ baseUrl: "https://gateway.example", authentication: auth, fetch }) as unknown as Record<string, (input: Record<string, unknown>) => Promise<{ ok: boolean }>>;
      const result = await client[operation.operation]!(operationInput(operation));
      expect(result.ok, operation.operation).toBe(true);
      expect(fetch, operation.operation).toHaveBeenCalledOnce();
    }
  });

  it("maps every documented error for every generated operation", async () => {
    for (const operation of gatewayOperations) {
      for (const status of operation.documentedErrors) {
        const client = createGatewayClient({ baseUrl: "https://gateway.example", authentication: auth,
          fetch: async () => json({ code: "denied", title: "Denied" }, status) }) as unknown as Record<string, (input: Record<string, unknown>) => Promise<Record<string, unknown>>>;
        expect(await client[operation.operation]!(operationInput(operation)), `${operation.operation}:${status}`).toMatchObject({ ok: false, kind: "http", status });
      }
    }
  });

  it("executes a typed read with bearer authentication", async () => {
    const fetch = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      expect(String(input)).toBe("https://gateway.example/management/gateway/v1/capabilities");
      expect(new Headers(init?.headers).get("authorization")).toBe("Bearer token");
      return json({ apiVersion: "1.0.0", capabilities: [] }, 200, { "x-correlation-id": "corr" });
    });
    const client = createGatewayClient({ baseUrl: "https://gateway.example/", authentication: auth, fetch });
    const result = await client.capabilities({ path: {}, headers: { correlationId: "request-corr" as never } });
    expect(result).toMatchObject({ ok: true, status: 200, correlationId: "corr" });
    expect(fetch).toHaveBeenCalledOnce();
  });

  it("encodes paths and pagination without allowing path injection", async () => {
    const fetch = vi.fn(async (input: RequestInfo | URL) => {
      const url = new URL(input.toString());
      expect(url.pathname).toContain("namespaces/a%2Fb/targets/node%20one/revisions");
      expect(url.searchParams.get("maximum")).toBe("64");
      expect(url.searchParams.get("cursor")).toBe("next");
      return json({ items: [], continuationToken: "", hasMore: false });
    });
    const client = createGatewayClient({ baseUrl: "https://gateway.example/prefix", authentication: auth, fetch });
    await client.revisions({ path: { ns: "a/b" as never, target: "node one" as never }, query: { maximum: 64, cursor: "next" as never } });
  });

  it("maps documented errors and rejects unknown statuses", async () => {
    const documented = createGatewayClient({ baseUrl: "https://gateway.example", authentication: auth,
      fetch: async () => json({ code: "denied", title: "Denied" }, 403) });
    expect(await documented.capabilities({ path: {} })).toMatchObject({ ok: false, kind: "http", status: 403 });
    const unknown = createGatewayClient({ baseUrl: "https://gateway.example", authentication: auth,
      fetch: async () => json({ code: "odd", title: "Odd" }, 418) });
    expect(await unknown.capabilities({ path: {} })).toMatchObject({ ok: false, kind: "protocol", reason: "unexpected-status" });
  });

  it("fails closed for invalid envelopes, media types, and oversized bodies", async () => {
    const invalid = createGatewayClient({ baseUrl: "https://gateway.example", authentication: auth,
      fetch: async () => json({ apiVersion: "1.0.0" }) });
    expect(await invalid.capabilities({ path: {} })).toMatchObject({ kind: "protocol", reason: "schema-mismatch" });
    const media = createGatewayClient({ baseUrl: "https://gateway.example", authentication: auth,
      fetch: async () => new Response("text", { status: 200, headers: { "content-type": "text/plain" } }) });
    expect(await media.capabilities({ path: {} })).toMatchObject({ kind: "protocol", reason: "unexpected-media-type" });
    const large = createGatewayClient({ baseUrl: "https://gateway.example", authentication: auth,
      fetch: async () => new Response("{}", { status: 200, headers: { "content-type": "application/json", "content-length": "8388609" } }) });
    expect(await large.capabilities({ path: {} })).toMatchObject({ kind: "protocol", reason: "response-too-large" });
  });

  it("enforces generated response semantic constraints on instance properties", async () => {
    const client = createGatewayClient({ baseUrl: "https://gateway.example", authentication: auth,
      fetch: async () => json({ desiredStateToken: "next-token", duplicate: false, revisionId: "e\u0301" }, 202) });
    expect(await client.activate({
      path: { ns: "namespace", target: "node", revision: "revision" } as never,
      headers: { idempotencyKey: "attempt", desiredPrecondition: { kind: "create-only" } } as never,
    })).toMatchObject({ kind: "protocol", reason: "schema-mismatch" });
  });

  it("cancels response bodies for every rejection decided before body consumption", async () => {
    for (const response of [
      cancelableResponse({ "content-type": "application/json", "content-length": "8388609" }),
      cancelableResponse({ "content-type": "text/plain" }),
      cancelableResponse({ "content-type": `application/json;${"x".repeat(241)}` }),
      cancelableResponse({ "content-type": "application/json", "x-large": "x".repeat(4_097) }),
    ]) {
      const client = createGatewayClient({ baseUrl: "https://gateway.example", authentication: auth, fetch: async () => response.value });
      expect((await client.capabilities({ path: {} })).ok).toBe(false);
      expect(response.canceled).toHaveBeenCalledOnce();
    }
  });

  it("snapshots client options and calls authentication exactly once per admitted request", async () => {
    const base = new URL("https://gateway.example/root");
    const fetch = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      expect(String(input)).toBe("https://gateway.example/root/management/gateway/v1/capabilities");
      expect(new Headers(init?.headers).get("authorization")).toBe("Bearer token");
      return json({ apiVersion: "1.0.0", capabilities: [] });
    });
    const authentication = { getAccessToken: vi.fn(async () => ({ value: "token" })) };
    const options = { baseUrl: base, authentication, fetch };
    const client = createGatewayClient(options);
    base.pathname = "/mutated";
    (options as { baseUrl: URL }).baseUrl = new URL("https://other.example");
    authentication.getAccessToken = vi.fn(async () => ({ value: "replacement" }));
    expect((await client.capabilities({ path: {} })).ok).toBe(true);
    expect(authentication.getAccessToken).not.toHaveBeenCalled();
    expect(fetch).toHaveBeenCalledOnce();
  });

  it("validates and sends one exact materialized request-body representation", async () => {
    const authentication = { getAccessToken: vi.fn(async () => ({ value: "token" })) };
    const rejectedFetch = vi.fn();
    const rejected = createGatewayClient({ baseUrl: "https://gateway.example", authentication, fetch: rejectedFetch });
    const replaced = Object.create({ toJSON: () => ({ injected: "yes" }) }) as Record<string, unknown>;
    expect(await rejected.activate({
      path: { ns: "namespace", target: "node", revision: "revision" } as never,
      headers: { idempotencyKey: "attempt" } as never,
      body: replaced as never,
    })).toMatchObject({ kind: "protocol", reason: "schema-mismatch" });
    expect(authentication.getAccessToken).not.toHaveBeenCalled();
    expect(rejectedFetch).not.toHaveBeenCalled();

    let getterCalls = 0;
    const body = Object.defineProperty({}, "description", { enumerable: true, get: () => { getterCalls++; return "safe"; } });
    const acceptedFetch = vi.fn(async (_input: RequestInfo | URL, init?: RequestInit) => {
      expect(init?.body).toBe('{"description":"safe"}');
      return json({ desiredStateToken: "next-token", duplicate: false, revisionId: "revision" }, 202);
    });
    const accepted = createGatewayClient({ baseUrl: "https://gateway.example", authentication: auth, fetch: acceptedFetch });
    expect((await accepted.activate({
      path: { ns: "namespace", target: "node", revision: "revision" } as never,
      headers: { idempotencyKey: "attempt" } as never,
      body: body as never,
    })).ok).toBe(true);
    expect(getterCalls).toBe(1);
  });

  it("prepares path, query, and header values once before authentication", async () => {
    const calls = { ns: 0, target: 0, maximum: 0, cursor: 0, correlation: 0 };
    const path = Object.defineProperties({}, {
      ns: { enumerable: true, get: () => ++calls.ns === 1 ? "namespace" : "changed" },
      target: { enumerable: true, get: () => ++calls.target === 1 ? "node" : "changed" },
    });
    const query = Object.defineProperties({}, {
      maximum: { enumerable: true, get: () => { calls.maximum++; return 64; } },
      cursor: { enumerable: true, get: () => ++calls.cursor === 1 ? "next" : "changed" },
    });
    const headers = Object.defineProperty({}, "correlationId", { enumerable: true, get: () => ++calls.correlation === 1 ? "correlation" : "changed" });
    let actualUrl = ""; let actualCorrelation: string | null = null;
    const fetch = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      actualUrl = String(input);
      actualCorrelation = new Headers(init?.headers).get("x-correlation-id");
      return json({ items: [], continuationToken: "", hasMore: false });
    });
    const client = createGatewayClient({ baseUrl: "https://gateway.example", authentication: auth, fetch });
    const preparedResult = await client.revisions({ path, query, headers } as never);
    expect(preparedResult).toMatchObject({ ok: true });
    expect(actualUrl).toBe("https://gateway.example/management/gateway/v1/namespaces/namespace/targets/node/revisions?cursor=next&maximum=64");
    expect(actualCorrelation).toBe("correlation");
    expect(calls).toEqual({ ns: 1, target: 1, maximum: 1, cursor: 1, correlation: 1 });
  });

  it("never awaits hostile cancellation during early rejection or streamed oversize", async () => {
    const behaviors = [
      () => new Promise<void>(() => undefined),
      () => { throw new Error("cancel failure"); },
    ];
    for (const cancel of behaviors) {
      const earlyStream = new ReadableStream<Uint8Array>({ start(controller) { controller.enqueue(new Uint8Array([1])); }, cancel });
      const early = createGatewayClient({ baseUrl: "https://gateway.example", authentication: auth,
        fetch: async () => new Response(earlyStream, { headers: { "content-type": "text/plain" } }) });
      expect(await settles(early.capabilities({ path: {} }))).toMatchObject({ kind: "protocol", reason: "unexpected-media-type" });

      const oversizedStream = new ReadableStream<Uint8Array>({ start(controller) { controller.enqueue(new Uint8Array(8 * 1024 * 1024 + 1)); }, cancel });
      const oversized = createGatewayClient({ baseUrl: "https://gateway.example", authentication: auth,
        fetch: async () => new Response(oversizedStream, { headers: { "content-type": "application/json" } }) });
      expect(await settles(oversized.capabilities({ path: {} }))).toMatchObject({ kind: "protocol", reason: "response-too-large" });
    }
  });

  it("bounds authentication invocation across failure and post-auth cancellation", async () => {
    const failedAuthentication = { getAccessToken: vi.fn(async () => { throw new Error("private provider failure"); }) };
    const failedFetch = vi.fn();
    const failed = createGatewayClient({ baseUrl: "https://gateway.example", authentication: failedAuthentication, fetch: failedFetch });
    expect(await failed.capabilities({ path: {} })).toMatchObject({ kind: "transport", reason: "network-failure" });
    expect(failedAuthentication.getAccessToken).toHaveBeenCalledOnce();
    expect(failedFetch).not.toHaveBeenCalled();

    const controller = new AbortController();
    const cancelingAuthentication = { getAccessToken: vi.fn(async () => { controller.abort(); return { value: "token" }; }) };
    const canceledFetch = vi.fn();
    const canceledClient = createGatewayClient({ baseUrl: "https://gateway.example", authentication: cancelingAuthentication, fetch: canceledFetch });
    expect(await canceledClient.capabilities({ path: {} }, { signal: controller.signal })).toMatchObject({ kind: "canceled" });
    expect(cancelingAuthentication.getAccessToken).toHaveBeenCalledOnce();
    expect(canceledFetch).not.toHaveBeenCalled();
  });

  it("makes authentication output total and captures its own value exactly once", async () => {
    for (const malformed of [undefined, "token", {}, { token: "token" }]) {
      const fetch = vi.fn();
      const client = createGatewayClient({ baseUrl: "https://gateway.example",
        authentication: { getAccessToken: (() => malformed) as never }, fetch });
      expect(await client.capabilities({ path: {} })).toMatchObject({ kind: "protocol", reason: "schema-mismatch" });
      expect(fetch).not.toHaveBeenCalled();
    }

    const throwing = Object.defineProperty({}, "value", { enumerable: true, get: () => { throw new Error("provider getter failed"); } });
    const throwingFetch = vi.fn();
    const throwingClient = createGatewayClient({ baseUrl: "https://gateway.example",
      authentication: { getAccessToken: (() => throwing) as never }, fetch: throwingFetch });
    expect(await throwingClient.capabilities({ path: {} })).toMatchObject({ kind: "transport", reason: "network-failure" });
    expect(throwingFetch).not.toHaveBeenCalled();

    let reads = 0;
    const changing = Object.defineProperty({}, "value", { enumerable: true, get: () => ++reads === 1 ? "captured-token" : "x".repeat(20_000) });
    const changingFetch = vi.fn(async (_input: RequestInfo | URL, init?: RequestInit) => {
      expect(new Headers(init?.headers).get("authorization")).toBe("Bearer captured-token");
      return json({ apiVersion: "1.0.0", capabilities: [] });
    });
    const changingClient = createGatewayClient({ baseUrl: "https://gateway.example",
      authentication: { getAccessToken: (() => changing) as never }, fetch: changingFetch });
    expect((await changingClient.capabilities({ path: {} })).ok).toBe(true);
    expect(reads).toBe(1);
    expect(changingFetch).toHaveBeenCalledOnce();
  });

  it("does not call authentication or Fetch when already canceled", async () => {
    const controller = new AbortController(); controller.abort();
    const authentication = { getAccessToken: vi.fn() };
    const fetch = vi.fn();
    const client = createGatewayClient({ baseUrl: "https://gateway.example", authentication, fetch });
    expect(await client.capabilities({ path: {} }, { signal: controller.signal })).toMatchObject({ kind: "canceled" });
    expect(authentication.getAccessToken).not.toHaveBeenCalled();
    expect(fetch).not.toHaveBeenCalled();
  });

  it("rejects malformed credentials before Fetch without exposing them", async () => {
    const fetch = vi.fn();
    const client = createGatewayClient({ baseUrl: "https://gateway.example",
      authentication: { getAccessToken: () => ({ value: "secret\r\nInjected: yes" }) }, fetch });
    const result = await client.capabilities({ path: {} });
    expect(result).toMatchObject({ kind: "protocol", reason: "schema-mismatch" });
    expect(JSON.stringify(result)).not.toContain("secret");
    expect(fetch).not.toHaveBeenCalled();
  });

  it("projects idempotency and desired CAS framing exactly", async () => {
    const fetch = vi.fn(async (_input: RequestInfo | URL, init?: RequestInit) => {
      const headers = new Headers(init?.headers);
      expect(headers.get("idempotency-key")).toBe("attempt-one");
      expect(headers.get("if-match")).toBe('"desired-token"');
      expect(init?.method).toBe("POST");
      return json({ desiredStateToken: "next-token", duplicate: false, revisionId: "revision-two" }, 202);
    });
    const client = createGatewayClient({ baseUrl: "https://gateway.example", authentication: auth, fetch });
    const result = await client.activate({
      path: { ns: "namespace" as never, target: "node" as never, revision: "revision" as never },
      headers: { idempotencyKey: "attempt-one" as never, desiredPrecondition: { kind: "replace", token: "desired-token" as never } },
    });
    expect(result).toMatchObject({ ok: true, status: 202 });
  });

  it("rejects duplicate JSON members and oversized streamed chunks", async () => {
    const duplicate = createGatewayClient({ baseUrl: "https://gateway.example", authentication: auth,
      fetch: async () => new Response('{"apiVersion":"1","apiVersion":"2","capabilities":[]}', { headers: { "content-type": "application/json" } }) });
    expect(await duplicate.capabilities({ path: {} })).toMatchObject({ kind: "protocol", reason: "malformed-json" });

    const stream = new ReadableStream<Uint8Array>({ start(controller) { controller.enqueue(new Uint8Array(8 * 1024 * 1024 + 1)); controller.close(); } });
    const oversized = createGatewayClient({ baseUrl: "https://gateway.example", authentication: auth,
      fetch: async () => new Response(stream, { headers: { "content-type": "application/json" } }) });
    expect(await oversized.capabilities({ path: {} })).toMatchObject({ kind: "protocol", reason: "response-too-large" });
  });
});

async function settles<T>(value: Promise<T>): Promise<T> {
  return Promise.race([value, new Promise<never>((_, reject) => setTimeout(() => reject(new Error("operation hung")), 250))]);
}
