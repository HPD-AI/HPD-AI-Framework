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
});
