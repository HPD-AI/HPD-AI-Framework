import { describe, expect, it } from "vitest";
import { createBaseClient, HpdBaseError } from "../src/index.js";
import { createFetch, createJsonResponse, manifest } from "./helpers.js";

describe("transport", () => {
  it("normalizes baseUrl and sends default/configured headers", async () => {
    const fake = createFetch([createJsonResponse(manifest)]);
    const client = createBaseClient({
      baseUrl: "/base/",
      fetch: fake.fetch,
      clientName: "test-client",
      clientVersion: "1.2.3",
      headers: { Authorization: "Bearer token" }
    });

    await client.manifest({ correlationId: "corr-1" });

    expect(fake.calls[0]?.url).toBe("/base/manifest");
    const headers = new Headers(fake.calls[0]?.init?.headers);
    expect(headers.get("accept")).toBe("application/json");
    expect(headers.get("content-type")).toBeNull();
    expect(headers.get("authorization")).toBe("Bearer token");
    expect(headers.get("x-hpd-client")).toBe("test-client");
    expect(headers.get("x-hpd-client-version")).toBe("1.2.3");
    expect(headers.get("x-correlation-id")).toBe("corr-1");
  });

  it("passes credentials, signals, and body content headers", async () => {
    const controller = new AbortController();
    const fake = createFetch([createJsonResponse({ collectionId: "items", id: "1", payload: { kind: "json", json: {} }, metadata: {} }, { status: 201 })]);
    const client = createBaseClient({
      baseUrl: "/base",
      fetch: fake.fetch,
      credentials: "include",
      defaultSignal: controller.signal
    });

    await client.collection("items").create({});

    expect(fake.calls[0]?.init?.credentials).toBe("include");
    expect(fake.calls[0]?.init?.signal).toBe(controller.signal);
    expect(new Headers(fake.calls[0]?.init?.headers).get("content-type")).toBe("application/json");
  });

  it("fails clearly when fetch is unavailable", () => {
    const original = globalThis.fetch;
    Object.defineProperty(globalThis, "fetch", { value: undefined, configurable: true });
    try {
      expect(() => createBaseClient("/base")).toThrow(HpdBaseError);
    } finally {
      Object.defineProperty(globalThis, "fetch", { value: original, configurable: true });
    }
  });
});
