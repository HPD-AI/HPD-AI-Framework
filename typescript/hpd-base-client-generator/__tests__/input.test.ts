import { describe, expect, it } from "vitest";
import { loadSnapshot, parseSnapshot } from "../src/input.js";

describe("input loading", () => {
  it("reads a combined v1 fixture snapshot", async () => {
    const snapshot = await loadSnapshot({ snapshot: "fixtures/base-client-snapshot.json", out: "fixtures/generated/base" });
    expect(snapshot.snapshotVersion).toBe("1");
    expect(snapshot.schema.collections?.map(collection => collection.id)).toContain("posts");
  });

  it("builds a snapshot from separate manifest/schema/capability inputs", async () => {
    const snapshot = await loadSnapshot({
      manifest: "fixtures/manifest.json",
      schema: "fixtures/schema.json",
      capabilities: "fixtures/capabilities.json",
      openapi: "fixtures/openapi.base-public.json",
      out: "fixtures/generated/base"
    });
    expect(snapshot.manifest.contractVersion).toBe("1.0");
    expect(snapshot.schema.collections).toHaveLength(1);
    expect(snapshot.openApi).toBeTruthy();
  });

  it("rejects missing schema", () => {
    expect(() => parseSnapshot({ snapshotVersion: "1", manifest: {} })).toThrow(/schema/);
  });

  it("rejects unknown snapshot versions", () => {
    expect(() => parseSnapshot({ snapshotVersion: "2", manifest: {}, schema: { collections: [] } })).toThrow(/Unsupported/);
  });
});
