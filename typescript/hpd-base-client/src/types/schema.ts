import type { FilterExpression, QuerySort } from "./query.js";
import type { DiagnosticDescriptor } from "./health.js";
import type { VisibilityLevel } from "./descriptors.js";

export interface SchemaMetadata {
  runtimeId: string;
  contractVersion: string;
  visibility: VisibilityLevel;
  role?: string;
  collections?: CollectionDefinition[];
  relations?: SchemaRelationSummary[];
  sources?: SchemaSourceDescriptor[];
  diagnostics?: DiagnosticDescriptor[];
  capabilities?: string[];
  eTag?: string;
  refreshedAt?: string;
}

export interface SchemaSourceDescriptor {
  id: string;
  kind: string;
  ownerModuleId?: string;
  storeId?: string;
  version?: string;
  visibility?: VisibilityLevel;
}

export interface SchemaRelationSummary {
  id: string;
  sourceCollectionId: string;
  sourceFieldPath: string;
  targetCollectionId: string;
  targetFieldPath?: string;
  kind?: string;
  cardinality?: string;
  visibility?: VisibilityLevel;
}

export interface CollectionDefinition {
  id: string;
  name: string;
  displayName?: string;
  kind: string;
  enabled?: boolean;
  exposed?: boolean;
  system?: boolean;
  readOnly?: boolean;
  readOnlyReason?: string;
  operations?: CollectionOperationMatrix;
  schemaMode: string;
  unknownFields: string;
  validationMode?: string;
  source?: SchemaSourceDescriptor;
  fields?: FieldDefinition[];
  indexes?: IndexDefinition[];
  policyRefs?: string[];
  store?: StoreAnnotation;
  visibility?: CollectionVisibility;
  requiredCapabilities?: string[];
  diagnostics?: DiagnosticDescriptor[];
  schemaVersion?: string;
  refreshedAt?: string;
  extensions?: Record<string, unknown>;
}

export interface CollectionOperationMatrix {
  list?: boolean;
  get?: boolean;
  create?: boolean;
  patch?: boolean;
  replace?: boolean;
  upsert?: boolean;
  delete?: boolean;
  batch?: boolean;
}

export interface FieldDefinition {
  id: string;
  name: string;
  displayName?: string;
  type: string;
  format?: string;
  cardinality?: Record<string, unknown>;
  required?: boolean;
  nullable?: boolean;
  system?: boolean;
  hidden?: boolean;
  readOnly?: boolean;
  default?: Record<string, unknown>;
  generated?: Record<string, unknown>;
  constraints?: Record<string, unknown>;
  validation?: Record<string, unknown>;
  relation?: Record<string, unknown>;
  file?: Record<string, unknown>;
  visibility?: Record<string, unknown>;
  ui?: Record<string, unknown>;
  sdk?: Record<string, unknown>;
  store?: StoreAnnotation;
  requiredCapabilities?: string[];
  extensions?: Record<string, unknown>;
}

export interface IndexDefinition {
  id: string;
  name: string;
  collectionId: string;
  kind: string;
  parts?: IndexPart[];
  unique?: boolean;
  primary?: boolean;
  predicate?: FilterExpression;
  nativePredicate?: string;
  status?: string;
  enforcement?: string;
  accessMethod?: string;
  nativeDefinition?: string;
  extensions?: Record<string, unknown>;
}

export interface IndexPart {
  kind: string;
  fieldPath?: string;
  expression?: string;
  direction?: QuerySort["direction"];
  nulls?: QuerySort["nulls"];
  collation?: string;
  length?: number;
  operatorClass?: string;
  extensions?: Record<string, unknown>;
}

export interface StoreAnnotation {
  storeId?: string;
  nativeName?: string;
  nativeType?: string;
  extensions?: Record<string, unknown>;
}

export interface CollectionVisibility {
  public?: boolean;
  admin?: boolean;
  reasonCode?: string;
}
