import type { CollectionDefinition, FieldDefinition, SchemaMetadata } from "./types/schema.js";
import type { JsonObject, RecordEnvelope, RecordPayload } from "./types/records.js";

export interface SchemaMetadataIndex {
  getCollection(id: string): CollectionDefinition | undefined;
  getField(collectionId: string, fieldPath: string): FieldDefinition | undefined;
  isDateLikeField(collectionId: string, fieldPath: string): boolean;
  readonly collectionsById: ReadonlyMap<string, CollectionDefinition>;
  readonly fieldsByCollectionAndName: ReadonlyMap<string, ReadonlyMap<string, FieldDefinition>>;
}

export interface HydrateRecordOptions {
  collectionId?: string;
  dates?: "string" | "date";
  unknownFields?: "preserve" | "drop";
}

export function createSchemaMetadataIndex(schema: SchemaMetadata | undefined): SchemaMetadataIndex {
  const collectionsById = new Map<string, CollectionDefinition>();
  const fieldsByCollectionAndName = new Map<string, Map<string, FieldDefinition>>();
  for (const collection of schema?.collections ?? []) {
    collectionsById.set(collection.id, collection);
    const fields = new Map<string, FieldDefinition>();
    for (const field of collection.fields ?? []) {
      fields.set(field.name, field);
      fields.set(field.id, field);
    }
    fieldsByCollectionAndName.set(collection.id, fields);
  }

  return {
    collectionsById,
    fieldsByCollectionAndName,
    getCollection: id => collectionsById.get(id),
    getField: (collectionId, fieldPath) => fieldsByCollectionAndName.get(collectionId)?.get(fieldPath),
    isDateLikeField(collectionId, fieldPath) {
      const field = fieldsByCollectionAndName.get(collectionId)?.get(fieldPath);
      return isDateLikeField(field);
    }
  };
}

export function hydrateRecord<TRecord extends JsonObject = JsonObject>(
  record: RecordEnvelope<TRecord>,
  schema: SchemaMetadata | SchemaMetadataIndex,
  options: HydrateRecordOptions = {}
): RecordEnvelope<TRecord> {
  if (options.dates !== "date") return cloneRecord(record);
  const index = "getField" in schema ? schema : createSchemaMetadataIndex(schema);
  const collectionId = options.collectionId ?? record.collectionId;
  const payload = hydratePayload(record.payload, collectionId, index, options) as RecordPayload<TRecord>;
  return { ...record, payload, metadata: { ...record.metadata }, includes: cloneIncludes(record.includes) };
}

export function parseBaseDate(value: string): Date {
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) throw new Error(`Invalid BASE date '${value}'.`);
  return date;
}

export function recordCreatedAtDate(record: RecordEnvelope): Date | undefined {
  return record.metadata.createdAt ? parseBaseDate(record.metadata.createdAt) : undefined;
}

export function recordUpdatedAtDate(record: RecordEnvelope): Date | undefined {
  return record.metadata.updatedAt ? parseBaseDate(record.metadata.updatedAt) : undefined;
}

function hydratePayload(payload: RecordPayload, collectionId: string, index: SchemaMetadataIndex, options: HydrateRecordOptions): RecordPayload {
  if (payload.kind === "fieldMap") {
    const fields = hydrateObject(payload.fields ?? {}, collectionId, index, options);
    return { kind: "fieldMap", fields };
  }
  return { kind: "json", json: hydrateObject(payload.json, collectionId, index, options) };
}

function hydrateObject<TRecord extends JsonObject>(input: TRecord, collectionId: string, index: SchemaMetadataIndex, options: HydrateRecordOptions): TRecord {
  const output: JsonObject = {};
  for (const [key, value] of Object.entries(input)) {
    const field = index.getField(collectionId, key);
    if (!field && options.unknownFields === "drop") continue;
    output[key] = isDateLikeField(field) && typeof value === "string" ? tryParseDate(value) : value;
  }
  return output as TRecord;
}

function tryParseDate(value: string): string | Date {
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? value : date;
}

function isDateLikeField(field: FieldDefinition | undefined): boolean {
  return field?.type === "dateTime" || field?.format === "date" || field?.format === "time" || field?.format === "dateTime";
}

function cloneRecord<TRecord extends JsonObject>(record: RecordEnvelope<TRecord>): RecordEnvelope<TRecord> {
  return { ...record, metadata: { ...record.metadata }, includes: cloneIncludes(record.includes) };
}

function cloneIncludes<TRecord extends JsonObject>(includes: RecordEnvelope<TRecord>["includes"]): RecordEnvelope<TRecord>["includes"] {
  return includes ? { ...includes } : undefined;
}
