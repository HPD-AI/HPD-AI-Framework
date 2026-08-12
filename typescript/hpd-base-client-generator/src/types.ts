export interface GenerationSnapshot {
  readonly protocol: { readonly protocolMajor: 2; readonly protocolMinor: number; readonly minimumClientMinor: number; readonly snapshotSchemaVersion: 2; readonly applicationId: string; readonly schemaGeneration: string; readonly endpointInventoryDigest: string; readonly errorTaxonomyVersion: number; readonly realtimeProtocolVersion: 2; readonly liveQueryProtocolVersion: 1; readonly serializationProfile: "base-json-v1"; readonly generatedAt: string };
  readonly application: { readonly audience: "application" | "controlPlane"; readonly applicationId: string; readonly basePath: string };
  readonly schema: { readonly generation: string; readonly collections: readonly CollectionDescriptor[]; readonly types: readonly NamedTypeDescriptor[] };
  readonly endpoints: readonly EndpointDescriptor[];
  readonly capabilities: readonly CapabilityDescriptor[];
  readonly registeredReads: readonly ReadDescriptor[];
  readonly dependencyTemplates: readonly DependencyDescriptor[];
  readonly vectorIndexes: readonly VectorDescriptor[];
  readonly errors: readonly ErrorDescriptor[];
  readonly digest: string;
}
export interface CollectionDescriptor {
  readonly id: string; readonly generatedName: string; readonly recordTypeId: string; readonly createTypeId: string; readonly replaceTypeId: string; readonly patchTypeId: string;
  readonly fields: readonly FieldDescriptor[]; readonly operations: readonly string[]; readonly pagination: "none" | "seek" | "stableHistory"; readonly maxPageSize: number;
}
export interface FieldDescriptor { readonly id: string; readonly wireName: string; readonly generatedName: string; readonly valueTypeId: string; readonly serverGenerated: boolean; readonly mutable: boolean; readonly redactionOptional: boolean; readonly operators: readonly string[]; }
export interface NamedTypeDescriptor { readonly id: string; readonly node: TypeNode; }
export interface TypeNode { readonly kind: string; readonly format?: string; readonly precision?: string; readonly finiteOnly?: boolean; readonly wire?: string; readonly minimum?: string; readonly maximum?: string; readonly minLength?: number; readonly maxLength?: number; readonly elementTypeId?: string; readonly maxItems?: number; readonly maxBytes?: number; readonly value?: unknown; readonly properties?: readonly PropertyDescriptor[]; readonly additionalProperties?: false; readonly values?: readonly string[]; readonly discriminator?: string; readonly variants?: readonly { readonly tag: string; readonly typeId: string }[]; }
export interface PropertyDescriptor { readonly name: string; readonly typeId: string; readonly required: boolean; readonly nullable: boolean; readonly redactionOptional: boolean; }
export interface EndpointDescriptor { readonly id: string; readonly audience: "application" | "controlPlane"; readonly operation: string; readonly method: string; readonly route: string; readonly capability?: string; readonly requestTypeId?: string; readonly responseTypeId?: string; readonly successStatuses: readonly number[]; readonly errorCodes: readonly string[]; readonly maximumRequestBodyBytes: number; readonly responseMode: "json" | "bytes" | "stream" | "webSocket" | "empty"; readonly replay: "none" | "channelDependent"; readonly resume: "none" | "durableCursor"; readonly cache: "none" | "structuralDigest"; }
export interface CapabilityDescriptor { readonly id: string; readonly available: boolean; }
export interface ReadDescriptor { readonly id: string; readonly generatedName: string; readonly endpointId: string; readonly parameterTypeId: string; readonly rowTypeId: string; readonly maxPageSize: number; readonly watchable: boolean; }
export interface DependencyDescriptor { readonly id: string; readonly kind: string; readonly visibility: string; readonly parameterTypeIds: readonly string[]; }
export interface ErrorDescriptor { readonly code: string; readonly category: string; readonly retryable: boolean; }
export interface VectorDescriptor { readonly collectionId: string; readonly id: string; readonly generatedName: string; readonly dimensions: number; readonly measure: "cosineSimilarity" | "dotProductSimilarity" | "euclideanDistance"; readonly filterFieldIds: readonly string[]; }
