import { beforeAll, describe, expect, it } from "vitest";
import { defaultGeneratorConfig } from "../src/config.js";
import { writeGeneratedFiles } from "../src/emit.js";
import { loadSnapshot } from "../src/input.js";
import { createGenerationPlan } from "../src/normalize.js";
import type { CollectionClient, HpdBaseClient } from "@hpd/base-client";

describe("generated runtime", () => {
  beforeAll(async () => {
    const snapshot = await loadSnapshot({ snapshot: "fixtures/base-client-snapshot.json", out: "fixtures/generated/base" });
    await writeGeneratedFiles(createGenerationPlan(snapshot, defaultGeneratorConfig), "fixtures/generated/base", true);
  });

  it("delegates to fake generic collection clients", async () => {
    const calls: string[] = [];
    const generic = (id: string): CollectionClient<any> => ({
      id,
      list: async () => ({ items: [], page: {} }),
      listResult: async () => ({ ok: true, status: "ok", value: { items: [], page: {} } }) as any,
      query: async () => ({ items: [], page: {} }),
      queryResult: async () => ({ ok: true, status: "ok", value: { items: [], page: {} } }) as any,
      get: async recordId => {
        calls.push(`${id}.get.${recordId}`);
        return { collectionId: id, id: recordId, payload: { kind: "json", json: {} }, metadata: {} };
      },
      getResult: async () => ({ ok: true, status: "ok", value: {} }) as any,
      create: async input => {
        calls.push(`${id}.create.${input.title ?? input.displayName ?? ""}`);
        return { collectionId: id, id: "new", payload: { kind: "json", json: input }, metadata: {} };
      },
      createResult: async () => ({ ok: true, status: "ok", value: {} }) as any,
      patch: async (recordId, input) => ({ collectionId: id, id: recordId, payload: { kind: "json", json: input }, metadata: {} }),
      patchResult: async () => ({ ok: true, status: "ok", value: {} }) as any,
      replace: async (recordId, input) => ({ collectionId: id, id: recordId, payload: { kind: "json", json: input }, metadata: {} }),
      replaceResult: async () => ({ ok: true, status: "ok", value: {} }) as any,
      delete: async recordId => ({ id: recordId, deleted: true }),
      deleteResult: async () => ({ ok: true, status: "ok", value: {} }) as any,
      definition: async () => ({ id, name: id, kind: "record", schemaMode: "typed", unknownFields: "reject" }),
      definitionResult: async () => ({ ok: true, status: "ok", value: {} }) as any,
      supports: () => true
    });
    const base = { collection: generic } as HpdBaseClient;
    const { createGeneratedBaseClient } = await import("../fixtures/generated/base/client.js");
    const db = createGeneratedBaseClient(base);
    await db.posts.create({ title: "Hello", authorId: "user_1" });
    await db.collection("user-profiles").get("profile_1");
    expect(db.collections["user-profiles"]).toBe(db.userProfiles);
    expect("delete" in db.posts).toBe(false);
    expect("create" in db.auditLog).toBe(false);
    expect(calls).toEqual(["posts.create.Hello", "user-profiles.get.profile_1"]);
  });
});
