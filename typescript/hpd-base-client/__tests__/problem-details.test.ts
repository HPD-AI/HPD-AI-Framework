import { describe, expect, it } from "vitest";
import { parseFailureResponse } from "../src/transport/problem-details.js";

describe("problem details", () => {
  it("synthesizes safe errors for non-json failures", async () => {
    const response = new Response("plain failure", { status: 503 });
    const parsed = await parseFailureResponse(response, {});
    expect(parsed.error.status).toBe("storeError");
    expect(parsed.error.message).toBe("plain failure");
  });
});
