import { mkdir, readFile, rm } from "node:fs/promises";
import { join } from "node:path";
import { describe, expect, it } from "vitest";
import { defaultGeneratorConfig } from "../src/config.js";
import { createGenerationPlan } from "../src/normalize.js";
import { renderGeneratedFiles } from "../src/render.js";
import { loadSnapshot } from "../src/input.js";
import { writeGeneratedFiles } from "../src/emit.js";

describe("emitter", () => {
  it("emits deterministic files that import only @hpd/base-client as an external runtime package", async () => {
    const snapshot = await loadSnapshot({ snapshot: "fixtures/base-client-snapshot.json", out: "fixtures/generated/base" });
    const plan = createGenerationPlan(snapshot, defaultGeneratorConfig);
    const first = renderGeneratedFiles(plan);
    const second = renderGeneratedFiles(plan);
    expect(second).toEqual(first);
    const generated = first.map(file => file.content).join("\n");
    expect(generated).toContain("createGeneratedBaseClient");
    expect(generated).toContain("readonly userProfiles");
    expect(generated).not.toContain("from \"zod\"");
    expect(generated).not.toContain("from \"openapi");
  });

  it("writes generated files", async () => {
    const out = "fixtures/generated/emit-base";
    await rm(out, { recursive: true, force: true });
    await mkdir("fixtures/generated", { recursive: true });
    const snapshot = await loadSnapshot({ snapshot: "fixtures/base-client-snapshot.json", out });
    await writeGeneratedFiles(createGenerationPlan(snapshot, defaultGeneratorConfig), out, true);
    expect(await readFile(join(out, "client.ts"), "utf8")).toContain("collection(id: \"user-profiles\")");
  });

  it("honors result-method and exact-collection-map config flags", async () => {
    const snapshot = await loadSnapshot({ snapshot: "fixtures/base-client-snapshot.json", out: "fixtures/generated/base" });
    const plan = createGenerationPlan(snapshot, {
      ...defaultGeneratorConfig,
      emitResultMethods: false,
      emitExactCollectionsMap: false
    });
    const client = renderGeneratedFiles(plan).find(file => file.path === "client.ts")?.content ?? "";
    expect(client).not.toContain("listResult");
    expect(client).not.toContain("readonly collections");
    expect(client).toContain("collection(id: \"user-profiles\")");
  });

  it("emits runtime-error methods when configured", async () => {
    const snapshot = await loadSnapshot({ snapshot: "fixtures/base-client-snapshot.json", out: "fixtures/generated/base" });
    const plan = createGenerationPlan(snapshot, {
      ...defaultGeneratorConfig,
      unsupportedMethods: "runtime-errors"
    });
    const client = renderGeneratedFiles(plan).find(file => file.path === "client.ts")?.content ?? "";
    const metadata = renderGeneratedFiles(plan).find(file => file.path === "metadata.ts")?.content ?? "";
    expect(client).toContain("delete(id: string");
    expect(client).toContain("unsupportedMethod(\"delete\")");
    expect(metadata).toContain("routes");
    expect(metadata).toContain("ListRecords");
    expect(metadata).toContain("records.list");
  });

  it("narrows generated query input when query capabilities are disabled", async () => {
    const snapshot = await loadSnapshot({ snapshot: "fixtures/base-client-snapshot.json", out: "fixtures/generated/base" });
    const disabledFilterSnapshot = {
      ...snapshot,
      capabilities: {
        descriptorVersion: "1.0",
        runtimeId: "fixture",
        families: [{ familyId: "records", familyVersion: "1.0", features: [{ featureId: "records.query.filter", version: "1.0", status: "disabled" as const }] }]
      }
    };
    const query = renderGeneratedFiles(createGenerationPlan(disabledFilterSnapshot, defaultGeneratorConfig)).find(file => file.path === "query.ts")?.content ?? "";
    expect(query).toContain("PostsQueryInput = TypedRecordQueryInput<PostsFieldPath, false");
  });
});
