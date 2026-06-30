import { describe, expect, it } from "vitest";
import { planOperations, planQueryFeatures } from "../src/capabilities.js";

describe("capability planning", () => {
  it("omits mutations for read-only collections", () => {
    const operations = planOperations({ id: "audit", name: "audit", kind: "record", schemaMode: "typed", unknownFields: "reject", readOnly: true });
    expect(operations).toEqual(["list", "query", "get"]);
  });

  it("honors operation matrix disables", () => {
    const operations = planOperations({ id: "posts", name: "posts", kind: "record", schemaMode: "typed", unknownFields: "reject", operations: { delete: false } });
    expect(operations).not.toContain("delete");
  });

  it("honors unavailable capability features", () => {
    const operations = planOperations(
      { id: "posts", name: "posts", kind: "record", schemaMode: "typed", unknownFields: "reject" },
      { descriptorVersion: "1", runtimeId: "fixture", families: [{ familyId: "records", familyVersion: "1", features: [{ featureId: "records.create", version: "1", status: "disabled" }] }] }
    );
    expect(operations).not.toContain("create");
  });

  it("uses supplied OpenAPI paths as live route evidence", () => {
    const operations = planOperations(
      { id: "posts", name: "posts", kind: "record", schemaMode: "typed", unknownFields: "reject" },
      undefined,
      { paths: { "/collections/{collectionId}/records": { get: {} } } }
    );
    expect(operations).toEqual(["list"]);
  });

  it("omits all callable operations when collection required capabilities are unavailable", () => {
    const operations = planOperations(
      { id: "posts", name: "posts", kind: "record", schemaMode: "typed", unknownFields: "reject", requiredCapabilities: ["records.private"] },
      { descriptorVersion: "1", runtimeId: "fixture", families: [{ familyId: "records", familyVersion: "1", features: [{ featureId: "records.private", version: "1", status: "disabled" }] }] }
    );
    expect(operations).toEqual([]);
  });

  it("narrows query feature flags when capability metadata disables them", () => {
    const queryFeatures = planQueryFeatures(
      "posts",
      { descriptorVersion: "1", runtimeId: "fixture", families: [{ familyId: "records", familyVersion: "1", features: [{ featureId: "records.query.filter", version: "1", status: "disabled" }] }] }
    );
    expect(queryFeatures.filter).toBe(false);
    expect(queryFeatures.sort).toBe(true);
  });
});
