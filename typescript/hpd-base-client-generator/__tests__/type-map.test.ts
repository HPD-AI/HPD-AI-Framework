import { describe, expect, it } from "vitest";
import { defaultGeneratorConfig } from "../src/config.js";
import { inputType, outputType, planField } from "../src/type-map.js";

describe("type mapping", () => {
  it("maps decimal/date/reference/custom fields conservatively", () => {
    expect(planField({ id: "price", name: "price", type: "decimal" }, defaultGeneratorConfig).type).toBe("DecimalString");
    expect(planField({ id: "createdAt", name: "createdAt", type: "dateTime" }, defaultGeneratorConfig).type).toBe("IsoDateTimeString");
    expect(planField({ id: "authorId", name: "authorId", type: "reference" }, defaultGeneratorConfig).type).toBe("RecordId");
    expect(planField({ id: "embedding", name: "embedding", type: "custom.embedding" }, defaultGeneratorConfig).type).toBe("unknown");
  });

  it("applies nullable and cardinality metadata", () => {
    const field = planField({ id: "tags", name: "tags", type: "string", nullable: true, cardinality: { kind: "array" } }, defaultGeneratorConfig);
    expect(outputType(field)).toBe("string[] | null");
    expect(inputType(field)).toBe("string[] | null");
  });

  it("supports configured aliases", () => {
    const field = planField({ id: "embedding", name: "embedding", type: "custom.embedding" }, {
      ...defaultGeneratorConfig,
      typeAliases: { "custom.embedding": "number[]" }
    });
    expect(field.type).toBe("number[]");
  });

  it("tracks read/write visibility for public generated surfaces", () => {
    const writeOnly = planField({ id: "password", name: "password", type: "string", visibility: { writeOnly: true } }, defaultGeneratorConfig);
    expect(writeOnly.outputVisible).toBe(false);
    expect(writeOnly.createWritable).toBe(true);
    const createHidden = planField({ id: "slug", name: "slug", type: "string", visibility: { create: false } }, defaultGeneratorConfig);
    expect(createHidden.outputVisible).toBe(true);
    expect(createHidden.createWritable).toBe(false);
    expect(createHidden.updateWritable).toBe(true);
    const systemField = planField({ id: "tenant", name: "tenant", type: "string", system: true }, defaultGeneratorConfig);
    expect(systemField.createWritable).toBe(false);
    expect(systemField.updateWritable).toBe(false);
  });
});
