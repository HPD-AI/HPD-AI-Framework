import { describe, expect, it } from "vitest";
import { createSchemaMetadataIndex, hydrateRecord, recordCreatedAtDate } from "../src/index.js";
import { schema } from "./helpers.js";

describe("hydration", () => {
  it("preserves raw strings unless explicit schema-aware date hydration is requested", () => {
    const record = {
      collectionId: "items",
      id: "1",
      payload: { kind: "json" as const, json: { title: "a", publishedAt: "2026-01-01T00:00:00Z" } },
      metadata: { createdAt: "2026-01-01T00:00:00Z" }
    };

    expect(hydrateRecord(record, schema).payload.json.publishedAt).toBe("2026-01-01T00:00:00Z");
    const hydrated = hydrateRecord(record, schema, { dates: "date" });
    expect(hydrated.payload.json.publishedAt).toBeInstanceOf(Date);
    expect(record.payload.json.publishedAt).toBe("2026-01-01T00:00:00Z");
    expect(recordCreatedAtDate(record)).toBeInstanceOf(Date);
    expect(createSchemaMetadataIndex(schema).isDateLikeField("items", "publishedAt")).toBe(true);
  });
});
