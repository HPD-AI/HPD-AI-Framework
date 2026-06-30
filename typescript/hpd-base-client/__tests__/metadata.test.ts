import { describe, expect, it } from "vitest";
import { createBaseClient } from "../src/index.js";
import { capabilities, createFetch, createJsonResponse, manifest, schema } from "./helpers.js";

describe("metadata", () => {
  it("uses public and admin metadata routes", async () => {
    const fake = createFetch(call => {
      if (call.url.includes("capabilities")) return createJsonResponse(capabilities);
      if (call.url.includes("schema")) return createJsonResponse(schema);
      if (call.url.includes("collections")) return createJsonResponse(schema.collections);
      if (call.url.includes("health") || call.url.includes("diagnostics")) return createJsonResponse([]);
      return createJsonResponse(manifest);
    });
    const client = createBaseClient({ baseUrl: "/base", fetch: fake.fetch });

    await client.manifest({ expand: ["schema", "capabilities", "health", "collections"] });
    await client.capabilities();
    await client.schema();
    await client.collections();
    await client.collectionDefinition("items");
    await client.health();
    await client.diagnostics();
    await client.admin.manifest();
    await client.admin.capabilities();
    await client.admin.schema();
    await client.admin.collections();
    await client.admin.collectionDefinition("items");
    await client.admin.health();
    await client.admin.diagnostics();

    expect(fake.calls.map(call => call.url)).toEqual([
      "/base/manifest?expand=schema%2Ccapabilities%2Chealth%2Ccollections",
      "/base/capabilities",
      "/base/schema",
      "/base/collections",
      "/base/collections/items",
      "/base/health",
      "/base/diagnostics",
      "/base/admin/manifest",
      "/base/admin/capabilities",
      "/base/admin/schema",
      "/base/admin/collections",
      "/base/admin/collections/items",
      "/base/admin/health",
      "/base/admin/diagnostics"
    ]);
  });

  it("bootstraps hydrated metadata without diagnostics by default", async () => {
    const fake = createFetch(call => {
      if (call.url.includes("manifest")) return createJsonResponse({ manifest, schema, capabilities, health: [], collections: schema.collections });
      throw new Error(`Unexpected call ${call.url}`);
    });
    const client = createBaseClient({ baseUrl: "/base", fetch: fake.fetch });

    const metadata = await client.bootstrap();

    expect(fake.calls[0]?.url).toBe("/base/manifest?expand=schema%2Ccapabilities%2Chealth%2Ccollections");
    expect(metadata.collectionsById.get("items")?.id).toBe("items");
    expect(metadata.featuresById.get("base.records.crud")?.status).toBe("available");
    expect(metadata.diagnostics).toBeUndefined();
  });

  it("honors view options, bootstrap defaults, bootstrapManifest, diagnostics fallback, and cache separation", async () => {
    const adminManifest = { ...manifest, visibility: "admin" };
    const fake = createFetch(call => {
      if (call.url === "/base/admin/manifest?expand=schema%2Ccapabilities%2Chealth%2Cdiagnostics%2Ccollections") {
        return createJsonResponse({ manifest: adminManifest, schema, capabilities, health: [], collections: schema.collections });
      }
      if (call.url === "/base/admin/diagnostics") return createJsonResponse([{ id: "diag", code: "d", message: "diagnostic", emittedAt: "2026-01-01T00:00:00Z" }]);
      if (call.url.includes("manifest")) return createJsonResponse({ manifest, schema, capabilities, health: [], collections: schema.collections });
      if (call.url.includes("capabilities")) return createJsonResponse(capabilities);
      if (call.url.includes("schema")) return createJsonResponse(schema);
      if (call.url.includes("collections")) return createJsonResponse(schema.collections);
      if (call.url.includes("health")) return createJsonResponse([]);
      if (call.url.includes("diagnostics")) return createJsonResponse([]);
      throw new Error(`Unexpected call ${call.url}`);
    });
    const client = createBaseClient({
      baseUrl: "/base",
      fetch: fake.fetch,
      bootstrap: { view: "admin", diagnostics: true },
      bootstrapManifest: { manifest, schema, capabilities, health: [], collections: schema.collections }
    });

    const preloaded = await client.bootstrap({ view: "public" });
    const admin = await client.bootstrap({ view: "admin", cache: { mode: "none" } });

    expect(preloaded.view).toBe("public");
    expect(admin.view).toBe("admin");
    expect(admin.diagnostics?.[0]?.id).toBe("diag");
    expect(fake.calls.map(call => call.url)).toContain("/base/admin/manifest?expand=schema%2Ccapabilities%2Chealth%2Ccollections%2Cdiagnostics");
  });

  it("uses contractVersion for bootstrap compatibility checks", async () => {
    const fake = createFetch([createJsonResponse({ manifest: { ...manifest, contractVersion: "2" }, schema, capabilities, health: [], collections: schema.collections })]);
    const result = await createBaseClient({ baseUrl: "/base", fetch: fake.fetch, contractVersion: "1" }).bootstrapResult();

    expect(result.ok).toBe(false);
    if (!result.ok) expect(result.error.code).toBe("base.client.contractVersionMismatch");
  });
});
