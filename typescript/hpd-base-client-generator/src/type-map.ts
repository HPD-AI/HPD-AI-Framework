import type { FieldDefinition } from "@hpd/base-client";
import type { GeneratorConfig, PlannedField } from "./types.js";
import { safePropertyName } from "./names.js";

export function planField(field: FieldDefinition, config: GeneratorConfig): PlannedField {
  const baseType = baseFieldType(field, config);
  const type = applyCardinality(baseType, field.cardinality);
  return {
    source: field,
    name: field.name,
    propertyName: config.fieldNameOverrides[field.name] ?? sdkPropertyName(field) ?? safePropertyName(field.name),
    type,
    baseType,
    required: field.required === true,
    nullable: field.nullable === true,
    readOnly: field.readOnly === true,
    hidden: field.hidden === true,
    outputVisible: isOutputVisible(field),
    createWritable: isCreateWritable(field),
    updateWritable: isUpdateWritable(field),
    generatedOnCreate: isGeneratedOnCreate(field.generated),
    hasDefault: field.default !== undefined,
    comparable: isComparable(baseType),
    scalar: !isCollectionCardinality(field.cardinality)
  };
}

export function outputType(field: PlannedField): string {
  return field.nullable ? `${field.type} | null` : field.type;
}

export function inputType(field: PlannedField): string {
  return outputType(field);
}

function baseFieldType(field: FieldDefinition, config: GeneratorConfig): string {
  const alias = config.typeAliases[field.type] ?? (field.format ? config.typeAliases[`${field.type}.${field.format}`] : undefined);
  if (alias) return alias;
  const type = field.type;
  const format = field.format;
  if (type === "string" && format === "dateTime") return "IsoDateTimeString";
  switch (type) {
    case "string": return "string";
    case "bool":
    case "boolean": return "boolean";
    case "integer":
    case "int":
    case "long":
    case "number":
    case "double":
    case "float": return "number";
    case "decimal": return "DecimalString";
    case "dateTime": return "IsoDateTimeString";
    case "id":
    case "reference": return "RecordId";
    case "json": return "JsonValue";
    case "object": return "{ [key: string]: JsonValue }";
    default: return config.unknownFieldType === "json" ? "JsonValue" : "unknown";
  }
}

function applyCardinality(type: string, cardinality: Record<string, unknown> | undefined): string {
  const kind = typeof cardinality?.kind === "string" ? cardinality.kind : typeof cardinality?.type === "string" ? cardinality.type : "single";
  switch (kind) {
    case "single": return type;
    case "array":
    case "set": return `${type}[]`;
    case "map": return `Record<string, ${type}>`;
    default: return "unknown";
  }
}

function isCollectionCardinality(cardinality: Record<string, unknown> | undefined): boolean {
  const kind = typeof cardinality?.kind === "string" ? cardinality.kind : typeof cardinality?.type === "string" ? cardinality.type : "single";
  return kind === "array" || kind === "set" || kind === "map";
}

function isComparable(baseType: string): boolean {
  return ["string", "number", "DecimalString", "IsoDateTimeString", "RecordId"].includes(baseType);
}

function sdkPropertyName(field: FieldDefinition): string | undefined {
  const sdk = field.sdk;
  if (sdk && typeof sdk.propertyName === "string" && sdk.propertyName) return sdk.propertyName;
  return undefined;
}

function isGeneratedOnCreate(input: Record<string, unknown> | undefined): boolean {
  return input?.onCreate === true || input?.create === true;
}

function isOutputVisible(field: FieldDefinition): boolean {
  if (field.hidden === true) return false;
  if (visibilityFlag(field.visibility, ["public", "read", "output"]) === false) return false;
  if (visibilityFlag(field.visibility, ["writeOnly", "write-only"]) === true) return false;
  return true;
}

function isCreateWritable(field: FieldDefinition): boolean {
  if (field.hidden === true || field.readOnly === true || isGeneratedOnCreate(field.generated)) return false;
  if (field.system === true && visibilityFlag(field.visibility, ["create", "write", "input"]) !== true) return false;
  if (visibilityFlag(field.visibility, ["public", "create", "write", "input"]) === false) return false;
  return true;
}

function isUpdateWritable(field: FieldDefinition): boolean {
  if (field.hidden === true || field.readOnly === true) return false;
  if (field.system === true && visibilityFlag(field.visibility, ["update", "write", "input"]) !== true) return false;
  if (visibilityFlag(field.visibility, ["public", "update", "write", "input"]) === false) return false;
  return true;
}

function visibilityFlag(input: Record<string, unknown> | undefined, keys: string[]): boolean | undefined {
  if (!input) return undefined;
  for (const key of keys) {
    const value = input[key];
    if (typeof value === "boolean") return value;
  }
  return undefined;
}
