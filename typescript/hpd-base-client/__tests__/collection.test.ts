import { describe, expect, it } from "vitest";
import { createBaseClient, q } from "../src/index.js";
import { createFetch, createJsonResponse, readJsonBody } from "./helpers.js";

const envelope = {
  collectionId: "items",
  id: "item 1",
  payload: { kind: "json", json: { title: "alpha" } },
  metadata: { revision: "r1" }
};

describe("collection", () => {
  it("maps CRUD/query routes and normalizes inputs", async () => {
    const fake = createFetch([createJsonResponse({ items: [], page: {} }), createJsonResponse({ items: [], page: {} }), createJsonResponse(envelope), createJsonResponse(envelope, { status: 201 }), createJsonResponse(envelope), createJsonResponse(envelope), createJsonResponse({ id: "item 1", deleted: true })]);
    const collection = createBaseClient({ baseUrl: "/base", fetch: fake.fetch }).collection("my items");

    await collection.list({ where: q.eq("title", "alpha") });
    await collection.query({ select: ["title"] });
    await collection.get("item 1");
    await collection.create({ title: "alpha" }, { requestedId: "item 1", idempotencyKey: "idem" });
    await collection.patch("item 1", { title: "beta" }, { expectedRevision: "r1" });
    await collection.replace("item 1", { title: "gamma" }, { expectedRevision: "r2" });
    await collection.delete("item 1", { expectedRevision: "r3", returnPrevious: true });

    expect(fake.calls.map(call => [call.init?.method ?? "GET", call.url])).toEqual([
      ["GET", "/base/collections/my%20items/records?where%5Btitle%5D=alpha"],
      ["POST", "/base/collections/my%20items/query"],
      ["GET", "/base/collections/my%20items/records/item%201"],
      ["POST", "/base/collections/my%20items/records"],
      ["PATCH", "/base/collections/my%20items/records/item%201"],
      ["PUT", "/base/collections/my%20items/records/item%201"],
      ["DELETE", "/base/collections/my%20items/records/item%201"]
    ]);
    expect(await readJsonBody(fake.calls[3]?.init)).toEqual({ payload: { kind: "json", json: { title: "alpha" } }, requestedId: "item 1", idempotencyKey: "idem" });
    expect(new Headers(fake.calls[3]?.init?.headers).get("idempotency-key")).toBe("idem");
    expect(await readJsonBody(fake.calls[4]?.init)).toEqual({ patch: { kind: "fieldMap", fields: { title: "beta" } }, expectedRevision: "r1" });
    expect(new Headers(fake.calls[4]?.init?.headers).get("if-match")).toBe("r1");
    expect(await readJsonBody(fake.calls[6]?.init)).toEqual({ expectedRevision: "r3", returnPrevious: true });
  });

  it("falls back to POST for non-GET-safe list queries", async () => {
    const fake = createFetch([createJsonResponse({ items: [], page: {} })]);
    const collection = createBaseClient({ baseUrl: "/base", fetch: fake.fetch }).collection("items");

    await collection.list({ where: q.between("rank", 1, 3) });

    expect(fake.calls[0]?.init?.method).toBe("POST");
    expect(fake.calls[0]?.url).toBe("/base/collections/items/query");
  });

  it("returns local validation failures for conflicting option/body values", async () => {
    const fake = createFetch([]);
    const collection = createBaseClient({ baseUrl: "/base", fetch: fake.fetch }).collection("items");

    const create = await collection.createResult({ payload: { kind: "json", json: {} }, idempotencyKey: "body" }, { idempotencyKey: "option" });
    const patch = await collection.patchResult("1", { patch: { kind: "fieldMap", fields: { title: "x" } }, expectedRevision: "r1" }, { expectedRevision: "r2" });
    const replace = await collection.replaceResult("1", { payload: { kind: "json", json: {} }, expectedRevision: "r1" }, { expectedRevision: "r2" });

    expect(create.ok).toBe(false);
    expect(patch.ok).toBe(false);
    expect(replace.ok).toBe(false);
    expect(fake.calls).toHaveLength(0);
  });

  it("preserves collection definition result status", async () => {
    const problem = {
      title: "Not found",
      status: 404,
      detail: "Missing",
      "hpd.status": "notFound",
      "hpd.error.code": "base.collection.notFound"
    };
    const fake = createFetch([createJsonResponse(problem, { status: 404, headers: { "content-type": "application/problem+json" } })]);
    const result = await createBaseClient({ baseUrl: "/base", fetch: fake.fetch }).collection("missing").definitionResult();

    expect(result.ok).toBe(false);
    if (!result.ok) {
      expect(result.status).toBe("notFound");
      expect(result.error.code).toBe("base.collection.notFound");
    }
  });
});
