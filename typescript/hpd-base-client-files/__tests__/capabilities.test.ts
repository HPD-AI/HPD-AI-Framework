import { createBaseClient } from "@hpd/base-client";
import { describe, expect, it } from "vitest";
import { createBaseFilesClient } from "../src/index.js";
import { createFetch, createJsonResponse, filesCapabilities, filesManifest } from "./helpers.js";

describe("capabilities", () => {
  it("reports route and degraded capability support after metadata is hydrated", async () => {
    const fake = createFetch([
      createJsonResponse({ manifest: filesManifest, capabilities: filesCapabilities, collections: [], health: [] })
    ]);
    const base = createBaseClient({ baseUrl: "/base", fetch: fake.fetch });
    await base.bootstrap();
    const files = createBaseFilesClient(base);

    expect(files.supports("upload")).toBe(true);
    expect(files.supports("upload", { allowDegraded: false })).toBe(false);
    expect(files.supports("upload", { requireRoute: true })).toBe(true);
    expect(files.route("metadata", { bucketId: "a/b", objectId: "o 1" })).toBe("/files/a%2Fb/objects/o%201/metadata");
    expect(files.bucket("avatars").supports("download")).toBe(true);
  });

  it("returns undefined support before metadata is hydrated", () => {
    const base = createBaseClient({ baseUrl: "/base", fetch: createFetch([]).fetch });
    const files = createBaseFilesClient(base);
    expect(files.supports("upload")).toBeUndefined();
    expect(files.route("upload", { bucketId: "avatars" })).toBeUndefined();
  });

  it("returns false when hydrated route descriptors omit the operation", async () => {
    const manifest = {
      ...filesManifest,
      projections: [{
        ...filesManifest.projections[0],
        routes: filesManifest.projections[0]?.routes?.filter(route => route.operationId !== "base.files.objects.delete")
      }]
    };
    const fake = createFetch([
      createJsonResponse({ manifest, capabilities: filesCapabilities, collections: [], health: [] })
    ]);
    const base = createBaseClient({ baseUrl: "/base", fetch: fake.fetch });
    await base.bootstrap();
    const files = createBaseFilesClient(base);

    expect(files.supports("delete")).toBe(false);
    expect(files.route("delete", { bucketId: "avatars", objectId: "obj-1" })).toBeUndefined();
  });
});
