import { describe, expect, it } from "vitest";
import { createBaseClient } from "../src/index.js";
import { capabilities, createFetch, createJsonResponse, schema } from "./helpers.js";

describe("client", () => {
  it("supports descriptor-driven feature lookup after bootstrap", async () => {
    const fake = createFetch([createJsonResponse({ manifest: { manifestVersion: "1", contractVersion: "1", runtime: { runtimeId: "runtime" }, compatibility: {}, visibility: "public", generatedAt: "2026-01-01T00:00:00Z" }, schema, capabilities, health: [], collections: schema.collections })]);
    const client = createBaseClient({ baseUrl: "/base", fetch: fake.fetch });
    await client.bootstrap();
    expect(client.supports("base.records.crud")).toBe(true);
    expect(client.requireFeature("base.records.crud").featureId).toBe("base.records.crud");
    expect(client.collection("items").supports("create")).toBe(true);
  });

  it("exposes a narrow extension context that reuses transport configuration", async () => {
    const signal = new AbortController().signal;
    const fake = createFetch([createJsonResponse({ ok: true })]);
    const client = createBaseClient({
      baseUrl: "/base/",
      fetch: fake.fetch,
      headers: async () => ({ Authorization: "Bearer token" }),
      credentials: "include",
      clientName: "test-client",
      clientVersion: "1.2.3",
      defaultSignal: signal
    });

    const extension = client.extension();
    const headers = await extension.headers({
      hasBody: true,
      contentType: "text/plain",
      accept: "application/octet-stream",
      correlationId: "corr-1",
      headers: { "X-Test": "yes" }
    });

    expect(extension.baseUrl).toBe("/base");
    expect(extension.fetch).toBe(fake.fetch);
    expect(extension.credentials).toBe("include");
    expect(extension.defaultSignal).toBe(signal);
    expect(extension.url("/files/b/objects")).toBe("/base/files/b/objects");
    expect(headers.get("authorization")).toBe("Bearer token");
    expect(headers.get("content-type")).toBe("text/plain");
    expect(headers.get("accept")).toBe("application/octet-stream");
    expect(headers.get("x-correlation-id")).toBe("corr-1");
    expect(headers.get("x-hpd-client")).toBe("test-client");
    expect(headers.get("x-hpd-client-version")).toBe("1.2.3");
    expect(headers.get("x-test")).toBe("yes");
  });

  it("allows extension callers to suppress json content and accept defaults", async () => {
    const client = createBaseClient({ baseUrl: "/base", fetch: createFetch([]).fetch });
    const headers = await client.extension().headers({ hasBody: true, contentType: false, accept: false });

    expect(headers.has("content-type")).toBe(false);
    expect(headers.has("accept")).toBe(false);
  });
});
