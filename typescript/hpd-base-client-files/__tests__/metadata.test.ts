import { describe, expect, it } from "vitest";
import { createFilesTestClient, createJsonResponse, metadata } from "./helpers.js";

describe("metadata and head", () => {
  it("reads metadata", async () => {
    const { files, calls } = createFilesTestClient([createJsonResponse(metadata)]);
    const result = await files.bucket("avatars").metadata("obj-1");

    expect(result.key).toBe("users/u1/avatar.png");
    expect(calls[0]?.url).toBe("/base/files/avatars/objects/obj-1/metadata");
  });

  it("maps HEAD response headers", async () => {
    const { files } = createFilesTestClient([
      new Response(null, {
        status: 204,
        headers: {
          "content-type": "image/png",
          "content-length": "123",
          etag: "\"rev-1\"",
          "last-modified": "Tue, 01 Jan 2026 00:00:00 GMT",
          "cache-control": "no-store",
          "x-correlation-id": "corr-1"
        }
      })
    ]);

    const headers = await files.bucket("avatars").head("obj-1");
    expect(headers.contentType).toBe("image/png");
    expect(headers.contentLength).toBe(123);
    expect(headers.etag).toBe("\"rev-1\"");
    expect(headers.correlationId).toBe("corr-1");
  });
});
