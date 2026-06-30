import type { DiagnosticDescriptor, HealthDescriptor } from "./health.js";
import type { CollectionDefinition, FieldDefinition, SchemaMetadata } from "./schema.js";

export type VisibilityLevel = "public" | "admin" | "internal" | "system";
export type CapabilityStatus = "available" | "unavailable" | "degraded" | "disabled" | "planned";
export type CapabilityScope = "runtime" | "collection" | "field" | "store" | "projection" | "admin";
export type SupportLevel = "required" | "optional" | "experimental" | "preview" | "deprecated";
export type HttpMethodKind = "get" | "post" | "put" | "patch" | "delete" | "head" | "options";

export interface BaseManifest {
  manifestVersion: string;
  contractVersion: string;
  runtime: RuntimeDescriptor;
  compatibility: CompatibilityDescriptor;
  collections?: CollectionSummaryDescriptor[];
  capabilities?: CapabilitySummaryDescriptor;
  modules?: BaseModuleDescriptor[];
  projections?: ProjectionDescriptor[];
  dtoContracts?: DtoContractDescriptor[];
  eventTypes?: EventTypeDescriptor[];
  healthRefs?: HealthRefDescriptor[];
  diagnosticRefs?: DiagnosticRefDescriptor[];
  links?: ManifestLinkDescriptor[];
  visibility: VisibilityLevel;
  eTag?: string;
  generatedAt: string;
}

export interface ExpandedBaseManifest {
  manifest: BaseManifest;
  schema?: SchemaMetadata;
  capabilities?: CapabilityDescriptor;
  health?: HealthDescriptor[];
  diagnostics?: DiagnosticDescriptor[];
  collections?: CollectionDefinition[];
  eTag?: string;
}

export interface RuntimeDescriptor {
  runtimeId: string;
  name?: string;
  version?: string;
  mode?: string;
  environment?: string;
  extensions?: Record<string, unknown>;
}

export interface CompatibilityDescriptor {
  minClientContractVersion?: string;
  maxClientContractVersion?: string;
  compatibleContractVersions?: string[];
  deprecatedContractVersions?: string[];
  extensions?: Record<string, unknown>;
}

export interface CollectionSummaryDescriptor {
  id: string;
  name: string;
  displayName?: string;
  kind: string;
  enabled?: boolean;
  exposed?: boolean;
  schemaRef?: string;
  requiredFeatureIds?: string[];
  visibility?: VisibilityLevel;
}

export interface CapabilitySummaryDescriptor {
  descriptorVersion: string;
  runtimeId: string;
  familyIds?: string[];
  featureIds?: string[];
}

export interface BaseModuleDescriptor {
  id: string;
  name: string;
  kind: string;
  version: string;
  status?: string;
  compatibility?: Record<string, unknown>;
  dependencies?: Record<string, unknown>[];
  contributedCapabilities?: string[];
  contributedDtoIds?: string[];
  contributedRouteIds?: string[];
  contributedEventTypes?: string[];
  contributedFieldAnnotationIds?: string[];
  contributedHealthRefIds?: string[];
  contributedDiagnosticIds?: string[];
  publicConfig?: Record<string, unknown>;
  adminConfigSummary?: Record<string, unknown>;
  visibility?: VisibilityLevel;
}

export interface ProjectionDescriptor {
  id: string;
  kind: string;
  packageId: string;
  packageVersion: string;
  contractVersionRange: string;
  status?: string;
  visibility?: VisibilityLevel;
  requiredCapabilities?: string[];
  providedCapabilities?: string[];
  routes?: RouteDescriptor[];
  dtoContracts?: DtoContractDescriptor[];
  entrypoints?: ProjectionEntrypointDescriptor[];
  healthRefs?: string[];
  diagnosticRefs?: string[];
}

export interface ProjectionEntrypointDescriptor {
  id: string;
  name: string;
  kind: string;
  visibility?: VisibilityLevel;
  requiredFeatureIds?: string[];
  routeRefs?: string[];
}

export interface DtoContractDescriptor {
  dtoId: string;
  name?: string;
  version?: string;
  schemaRef?: string;
  visibility?: VisibilityLevel;
  extensions?: Record<string, unknown>;
}

export interface EventTypeDescriptor {
  id: string;
  name?: string;
  resourceKind?: string;
  visibility?: VisibilityLevel;
}

export interface HealthRefDescriptor {
  id: string;
  targetRef?: string;
  visibility?: VisibilityLevel;
}

export interface DiagnosticRefDescriptor {
  id: string;
  code?: string;
  visibility?: VisibilityLevel;
}

export interface ManifestLinkDescriptor {
  rel: string;
  href: string;
  kind?: string;
  title?: string;
}

export interface CapabilityDescriptor {
  descriptorVersion: string;
  runtimeId: string;
  families: CapabilityFamilyDescriptor[];
}

export interface CapabilityFamilyDescriptor {
  familyId: string;
  familyVersion: string;
  status?: CapabilityStatus;
  ownerModuleId?: string;
  scopes?: CapabilityScope[];
  features?: CapabilityFeatureDescriptor[];
  limits?: CapabilityLimitDescriptor[];
  dependencies?: CapabilityDependencyDescriptor[];
  visibility?: VisibilityLevel;
}

export interface CapabilityFeatureDescriptor {
  featureId: string;
  version: string;
  status?: CapabilityStatus;
  supportLevel?: SupportLevel;
  scope?: CapabilityScope;
  appliesTo?: string[];
  constraints?: Record<string, unknown>;
  dtoContracts?: string[];
  routeRefs?: string[];
  eventTypeRefs?: string[];
  healthRef?: string;
  diagnosticRefs?: string[];
  visibility?: VisibilityLevel;
}

export interface CapabilityLimitDescriptor {
  name: string;
  value: string;
  unit?: string;
}

export interface CapabilityDependencyDescriptor {
  moduleId?: string;
  featureId?: string;
  versionRange?: string;
  required?: boolean;
}

export interface RouteDescriptor {
  operationId: string;
  method: HttpMethodKind;
  path: string;
  visibility?: VisibilityLevel;
  authRequirement?: string;
  requestDtoId?: string;
  responseDtoId: string;
  errorDtoId?: string;
  resultDtoId?: string;
  requiredFeatureIds?: string[];
}

export interface HydratedBaseMetadata {
  view: "public" | "admin";
  manifest: BaseManifest;
  schema?: SchemaMetadata;
  capabilities?: CapabilityDescriptor;
  health?: HealthDescriptor[];
  diagnostics?: DiagnosticDescriptor[];
  collectionsById: ReadonlyMap<string, CollectionDefinition>;
  featuresById: ReadonlyMap<string, CapabilityFeatureDescriptor>;
  familiesById: ReadonlyMap<string, CapabilityFamilyDescriptor>;
  routesByOperationId: ReadonlyMap<string, RouteDescriptor>;
  fieldsByCollectionAndName: ReadonlyMap<string, ReadonlyMap<string, FieldDefinition>>;
  etagBySection: ReadonlyMap<string, string>;
}
