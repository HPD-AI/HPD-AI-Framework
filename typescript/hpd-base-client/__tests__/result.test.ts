import { describe, expect, it } from "vitest";
import { createBaseClient, HpdBaseError } from "../src/index.js";
import { createFetch, createJsonResponse } from "./helpers.js";

describe("results", () => {
  it("reconstructs success statuses and headers", async () => {
    const fake = createFetch([createJsonResponse({ collectionId: "items", id: "1", payload: { kind: "json", json: {} }, metadata: {} }, { status: 201, headers: { "HPD-Base-Revision": "r1", Location: "/records/1" } })]);
    const result = await createBaseClient({ baseUrl: "/base", fetch: fake.fetch }).collection("items").createResult({});

    expect(result.ok).toBe(true);
    if (result.ok) {
      expect(result.status).toBe("created");
      expect(result.headers.revision).toBe("r1");
      expect(result.headers.location).toBe("/records/1");
    }
  });

  it("returns problem details for result methods and throws for convenience methods", async () => {
    const problem = {
      type: "urn:hpd:base:error:validation",
      title: "Validation failed",
      status: 400,
      detail: "Nope",
      "hpd.status": "validationFailed",
      "hpd.error.code": "base.test.validation",
      "hpd.validation": [{ path: "title", message: "Required" }]
    };
    const fake = createFetch([createJsonResponse(problem, { status: 400, headers: { "content-type": "application/problem+json" } }), createJsonResponse(problem, { status: 400, headers: { "content-type": "application/problem+json" } })]);
    const collection = createBaseClient({ baseUrl: "/base", fetch: fake.fetch }).collection("items");

    const result = await collection.getResult("missing");
    expect(result.ok).toBe(false);
    if (!result.ok) {
      expect(result.status).toBe("validationFailed");
      expect(result.error.code).toBe("base.test.validation");
      expect(result.problem?.["hpd.validation"]).toEqual([{ path: "title", message: "Required" }]);
    }
    await expect(collection.get("missing")).rejects.toThrow(HpdBaseError);
  });

  it("uses conservative fallback statuses without hpd.status", async () => {
    const fake = createFetch([createJsonResponse({ title: "Not found", status: 404 }, { status: 404, headers: { "content-type": "application/problem+json" } })]);
    const result = await createBaseClient({ baseUrl: "/base", fetch: fake.fetch }).collection("items").getResult("missing");

    expect(result.ok).toBe(false);
    if (!result.ok) expect(result.status).toBe("notFound");
  });
});
