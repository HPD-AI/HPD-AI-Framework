import { describe, expect, it } from "vitest";
import { createCapabilityIndex } from "../src/index.js";
import { capabilities } from "./helpers.js";

describe("capabilities", () => {
  it("uses descriptor data without endpoint probing", () => {
    const index = createCapabilityIndex(capabilities);
    expect(index.supports("base.records.crud")).toBe(true);
    expect(index.supports("missing")).toBe(false);
    expect(index.feature("base.records.crud")?.featureId).toBe("base.records.crud");
  });

  it("honors status and appliesTo filters", () => {
    const index = createCapabilityIndex({
      descriptorVersion: "1",
      runtimeId: "runtime",
      families: [
        {
          familyId: "records",
          familyVersion: "1",
          features: [
            { featureId: "feature.available", version: "1", status: "available", appliesTo: ["items"] },
            { featureId: "feature.degraded", version: "1", status: "degraded" },
            { featureId: "feature.disabled", version: "1", status: "disabled" }
          ]
        }
      ]
    });

    expect(index.supports("feature.available", { collectionId: "items" })).toBe(true);
    expect(index.supports("feature.available", { collectionId: "other" })).toBe(false);
    expect(index.supports("feature.degraded")).toBe(false);
    expect(index.supports("feature.degraded", { allowDegraded: true })).toBe(true);
    expect(index.supports("feature.disabled")).toBe(false);
  });
});
