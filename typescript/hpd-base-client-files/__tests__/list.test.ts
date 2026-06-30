import { describe, expect, it } from "vitest";
import { createFilesTestClient, createJsonResponse, metadata } from "./helpers.js";

describe("list", () => {
  it("serializes list query parameters", async () => {
    const { files, calls } = createFilesTestClient([createJsonResponse({ items: [metadata], nextCursor: "next" })]);
    const result = await files.bucket("avatars").list({ prefix: "users/", limit: 10, cursor: "c1" });

    expect(result.items).toHaveLength(1);
    expect(result.nextCursor).toBe("next");
    expect(calls[0]?.url).toBe("/base/files/avatars/objects?prefix=users%2F&limit=10&cursor=c1");
  });
});
