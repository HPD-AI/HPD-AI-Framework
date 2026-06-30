import { describe, expect, it } from "vitest";
import { createBaseClient } from "../src/index.js";
import { createFetch, createJsonResponse, manifest } from "./helpers.js";

describe("anti scope", () => {
  it("does not expose deferred APIs", () => {
    const client = createBaseClient({ baseUrl: "/base", fetch: createFetch([createJsonResponse(manifest)]).fetch });
    const collection = client.collection("items") as Record<string, unknown>;
    const clientObject = client as unknown as Record<string, unknown>;

    for (const name of ["upsert", "batch", "files", "realtime", "liveQuery", "stream", "search", "vector", "transactions", "schemaWrite", "policyExplain", "graphql", "openApi", "login", "logout"]) {
      expect(clientObject[name]).toBeUndefined();
      expect(collection[name]).toBeUndefined();
    }
  });
});
