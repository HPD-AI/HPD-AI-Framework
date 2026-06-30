import { describe, expect, it } from "vitest";
import { createFilesTestClient } from "./helpers.js";

describe("anti scope", () => {
  it("does not expose deferred APIs", () => {
    const { files } = createFilesTestClient([]);
    const bucket = files.bucket("avatars") as unknown as Record<string, unknown>;
    const filesObject = files as unknown as Record<string, unknown>;

    for (const name of ["signedUrl", "createSignedUrl", "multipart", "resumable", "provider", "login", "logout", "createBucket", "updateBucket", "deleteBucket", "transforms", "thumbnail", "scan", "cdn", "graphql", "search", "vector", "upsert", "batch", "transactions"]) {
      expect(filesObject[name]).toBeUndefined();
      expect(bucket[name]).toBeUndefined();
    }
  });
});
