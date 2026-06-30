import { HpdBaseError } from "@hpd/base-client";
import { describe, expect, it } from "vitest";
import { createFilesTestClient } from "./helpers.js";

describe("ProblemDetails", () => {
  it("preserves generic BASE error shape for result and throwing variants", async () => {
    const problem = {
      type: "https://example.test/problem",
      title: "Not found",
      detail: "Object not found.",
      status: 404,
      "hpd.status": "notFound",
      "hpd.error.code": "files.object.notFound",
      "hpd.error.category": "storage"
    };
    const { files } = createFilesTestClient([
      new Response(JSON.stringify(problem), { status: 404, headers: { "content-type": "application/problem+json" } }),
      new Response(JSON.stringify(problem), { status: 404, headers: { "content-type": "application/problem+json" } })
    ]);

    const result = await files.bucket("avatars").metadataResult("missing");
    expect(result.ok).toBe(false);
    if (!result.ok) {
      expect(result.status).toBe("notFound");
      expect(result.error.code).toBe("files.object.notFound");
      expect(result.problem).toEqual(problem);
    }

    await expect(files.bucket("avatars").metadata("missing")).rejects.toBeInstanceOf(HpdBaseError);
  });
});
