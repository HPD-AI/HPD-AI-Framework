import { describe, expect, it } from "vitest";
import { createFilesTestClient } from "./helpers.js";

describe("delete", () => {
  it("maps delete 204 to void", async () => {
    const { files, calls } = createFilesTestClient([new Response(null, { status: 204 })]);
    await expect(files.bucket("avatars").delete("obj-1")).resolves.toBeUndefined();
    expect(calls[0]?.url).toBe("/base/files/avatars/objects/obj-1");
    expect(calls[0]?.init?.method).toBe("DELETE");
  });
});
