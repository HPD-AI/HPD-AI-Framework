import { createSchemaMetadataIndex, hydrateRecord, type RecordEnvelope } from "../src/index.js";

const record: RecordEnvelope<{ publishedAt: string }> = {
  collectionId: "items",
  id: "1",
  payload: { kind: "json", json: { publishedAt: "2026-01-01T00:00:00Z" } },
  metadata: {}
};

hydrateRecord(record, {
  runtimeId: "runtime",
  contractVersion: "1",
  visibility: "public",
  collections: [{ id: "items", name: "items", kind: "document", schemaMode: "loose", unknownFields: "preserve" }]
});
createSchemaMetadataIndex(undefined).getCollection("items");
