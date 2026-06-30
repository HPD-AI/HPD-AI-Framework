import { describe, expect, it } from "vitest";
import { createFilesTestClient } from "./helpers.js";

describe("download", () => {
  it("returns a raw response without consuming the body", async () => {
    const response = new Response("hello", { headers: { "content-type": "text/plain" } });
    const { files, calls } = createFilesTestClient([response]);

    const downloaded = await files.bucket("avatars").download("obj-1");

    expect(downloaded).toBe(response);
    expect(downloaded.bodyUsed).toBe(false);
    expect(await downloaded.text()).toBe("hello");
    expect(calls[0]?.url).toBe("/base/files/avatars/objects/obj-1");
    expect(new Headers(calls[0]?.init?.headers).get("accept")).toBe("application/octet-stream");
  });

  it("provides blob and array buffer convenience helpers", async () => {
    const { files } = createFilesTestClient([
      new Response("blob-body", { headers: { "content-type": "text/plain" } }),
      new Response("buffer-body")
    ]);

    expect(await (await files.bucket("avatars").downloadBlob("obj-1")).text()).toBe("blob-body");
    expect(new TextDecoder().decode(await files.bucket("avatars").downloadArrayBuffer("obj-1"))).toBe("buffer-body");
  });
});
