import type { BaseManifest, CapabilityDescriptor } from "@hpd/base-client";
import type { CollectionDefinition, FieldDefinition, SchemaMetadata } from "@hpd/base-client";

export interface BaseClientGenerationSnapshot {
  snapshotVersion: "1";
  generatedAt?: string;
  source?: {
    baseUrl?: string;
    openApiDocumentName?: string;
    view?: "public" | "admin";
  };
  manifest: BaseManifest;
  schema: SchemaMetadata;
  capabilities?: CapabilityDescriptor;
  collections?: CollectionDefinition[];
  openApi?: unknown;
}

export interface GeneratorConfig {
  clientName: string;
  typeAliases: Record<string, string>;
  collectionNameOverrides: Record<string, string>;
  fieldNameOverrides: Record<string, string>;
  unknownFieldType: "unknown" | "json";
  emitResultMethods: boolean;
  emitExactCollectionsMap: boolean;
  unsupportedMethods: "omit" | "runtime-errors";
  banner?: string;
}

export interface GenerateOptions {
  snapshot?: string;
  manifest?: string;
  schema?: string;
  capabilities?: string;
  collections?: string;
  openapi?: string;
  config?: string;
  out: string;
  clean?: boolean;
  banner?: string;
}

export type RecordOperation = "list" | "query" | "get" | "create" | "patch" | "replace" | "delete";

export interface PlannedField {
  source: FieldDefinition;
  name: string;
  propertyName: string;
  type: string;
  baseType: string;
  required: boolean;
  nullable: boolean;
  readOnly: boolean;
  hidden: boolean;
  outputVisible: boolean;
  createWritable: boolean;
  updateWritable: boolean;
  generatedOnCreate: boolean;
  hasDefault: boolean;
  comparable: boolean;
  scalar: boolean;
}

export interface PlannedCollection {
  source: CollectionDefinition;
  id: string;
  propertyName: string;
  typeName: string;
  variableName: string;
  fields: PlannedField[];
  availableOperations: RecordOperation[];
  operations: RecordOperation[];
  routes: Partial<Record<RecordOperation, {
    method: string;
    path: string;
    operationId?: string;
    requiredFeatureIds?: string[];
    visibility?: string;
  }>>;
  queryFeatures: {
    filter: boolean;
    sort: boolean;
    select: boolean;
    include: boolean;
  };
}

export interface GenerationPlan {
  snapshot: BaseClientGenerationSnapshot;
  config: GeneratorConfig;
  collections: PlannedCollection[];
  warnings: string[];
}

export interface GeneratedFile {
  path: string;
  content: string;
}
