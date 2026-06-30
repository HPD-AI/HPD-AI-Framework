import { describe, expect, it } from "vitest";
import { createFilesTestClient, createJsonResponse, metadata } from "./helpers.js";

describe("upload", () => {
  it("sends raw body and file metadata headers", async () => {
    const body = new Blob(["hello"], { type: "text/plain" });
    const { files, calls } = createFilesTestClient([createJsonResponse({ metadata, created: true }, { status: 201 })]);

    const result = await files.bucket("avatars").upload(body, {
      key: "users/u1/avatar.txt",
      name: "avatar.txt",
      checksum: "sha256:abc"
    });

    expect(result.metadata.objectId).toBe("obj-1");
    expect(calls[0]?.url).toBe("/base/files/avatars/objects");
    expect(calls[0]?.init?.method).toBe("POST");
    expect(calls[0]?.init?.body).toBe(body);
    const headers = new Headers(calls[0]?.init?.headers);
    expect(headers.get("x-hpd-file-key")).toBe("users/u1/avatar.txt");
    expect(headers.get("x-hpd-file-name")).toBe("avatar.txt");
    expect(headers.get("x-hpd-file-checksum")).toBe("sha256:abc");
    expect(headers.get("content-type")).toBe("text/plain");
    expect(headers.get("authorization")).toBe("Bearer token");
  });

  it("infers File name and content type when available", async () => {
    const file = new File(["hello"], "hello.txt", { type: "text/plain" });
    const { files, calls } = createFilesTestClient([createJsonResponse({ metadata })]);

    await files.bucket("avatars").upload(file, { key: "hello.txt" });

    const headers = new Headers(calls[0]?.init?.headers);
    expect(headers.get("x-hpd-file-name")).toBe("hello.txt");
    expect(headers.get("content-type")).toBe("text/plain");
  });
});
