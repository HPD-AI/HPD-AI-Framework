import { createBaseClient } from "@hpd/base-client";
import { describe, expect, it } from "vitest";
import { createBaseFilesClient } from "../src/index.js";
import { createFetch, createJsonResponse } from "./helpers.js";

describe("files client", () => {
  it("creates bucket handles and reuses configured async headers", async () => {
    const fake = createFetch([createJsonResponse({ items: [] })]);
    const base = createBaseClient({
      baseUrl: "/base/",
      fetch: fake.fetch,
      headers: async () => ({ Authorization: "Bearer token" })
    });
    const files = createBaseFilesClient(base);

    await files.bucket("avatars").list({ correlationId: "corr-1" });

    expect(files.base).toBe(base);
    expect(files.routePrefix).toBe("/files");
    expect(fake.calls[0]?.url).toBe("/base/files/avatars/objects");
    const headers = new Headers(fake.calls[0]?.init?.headers);
    expect(headers.get("authorization")).toBe("Bearer token");
    expect(headers.get("x-correlation-id")).toBe("corr-1");
  });

});
