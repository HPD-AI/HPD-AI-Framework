import type { GeneratorConfig } from "./types.js";

export const defaultGeneratorConfig: GeneratorConfig = {
  clientName: "GeneratedBaseClient",
  typeAliases: {},
  collectionNameOverrides: {},
  fieldNameOverrides: {},
  unknownFieldType: "unknown",
  emitResultMethods: true,
  emitExactCollectionsMap: true,
  unsupportedMethods: "omit"
};

export function parseGeneratorConfig(input: unknown = {}): GeneratorConfig {
  if (!isObject(input)) return { ...defaultGeneratorConfig };
  const unknownFieldType = input.unknownFieldType === "json" ? "json" : "unknown";
  const unsupportedMethods = input.unsupportedMethods === "runtime-errors" ? "runtime-errors" : "omit";
  return {
    ...defaultGeneratorConfig,
    clientName: typeof input.clientName === "string" && input.clientName ? input.clientName : defaultGeneratorConfig.clientName,
    typeAliases: stringRecord(input.typeAliases),
    collectionNameOverrides: stringRecord(input.collectionNameOverrides),
    fieldNameOverrides: stringRecord(input.fieldNameOverrides),
    unknownFieldType,
    emitResultMethods: input.emitResultMethods !== false,
    emitExactCollectionsMap: input.emitExactCollectionsMap !== false,
    unsupportedMethods,
    banner: typeof input.banner === "string" ? input.banner : undefined
  };
}

function stringRecord(input: unknown): Record<string, string> {
  if (!isObject(input)) return {};
  return Object.fromEntries(Object.entries(input).filter(([, value]) => typeof value === "string")) as Record<string, string>;
}

function isObject(input: unknown): input is Record<string, unknown> {
  return typeof input === "object" && input !== null && !Array.isArray(input);
}
