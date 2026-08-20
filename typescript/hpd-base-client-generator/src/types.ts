export interface GenerationSnapshot {
  readonly protocol: { readonly protocolMajor: 2; readonly protocolMinor: number; readonly minimumClientMinor: number; readonly snapshotSchemaVersion: 5; readonly applicationId: string; readonly schemaGeneration: string; readonly endpointInventoryDigest: string; readonly errorTaxonomyVersion: number; readonly realtimeProtocolVersion: 2; readonly liveQueryProtocolVersion: 1; readonly serializationProfile: "base-json-v1"; readonly generatedAt: string };
  readonly application: { readonly audience: "application" | "controlPlane" | "service" | "system"; readonly applicationId: string; readonly basePath: string };
  readonly schema: { readonly generation: string; readonly collections: readonly CollectionDescriptor[]; readonly types: readonly NamedTypeDescriptor[] };
  readonly endpoints: readonly EndpointDescriptor[];
  readonly capabilities: readonly CapabilityDescriptor[];
  readonly registeredReads: readonly ReadDescriptor[];
  readonly dependencyTemplates: readonly DependencyDescriptor[];
  readonly vectorIndexes: readonly VectorDescriptor[];
  readonly selectionMutations: readonly SelectionMutationDescriptor[];
  readonly moduleMutations: readonly ModuleMutationDescriptor[];
  readonly subjectLifecycleConsumers: readonly SubjectLifecycleConsumerDescriptor[];
  readonly errors: readonly ErrorDescriptor[];
  readonly digest: string;
}
export interface CollectionDescriptor {
  readonly id: string; readonly generatedName: string; readonly recordTypeId: string; readonly createTypeId: string; readonly replaceTypeId: string; readonly patchTypeId: string;
  readonly fields: readonly FieldDescriptor[]; readonly operations: readonly string[]; readonly pagination: "none" | "seek" | "stableHistory"; readonly maxPageSize: number;
}
export interface FieldDescriptor { readonly id: string; readonly wireName: string; readonly generatedName: string; readonly valueTypeId: string; readonly serverGenerated: boolean; readonly mutable: boolean; readonly disclosureShape: "none" | "omission" | "fixed-marker"; readonly operators: readonly string[]; }
export interface NamedTypeDescriptor { readonly id: string; readonly node: TypeNode; }
export type StringFormat = "plain" | "record-id" | "collection-id" | "field-id" | "utc-instant" | "revision" | "cursor" | "consistency-token" | "mutation-id" | "dependency-reference";
export type TypeNode =
  | { readonly kind: "selection-query"; readonly maximumNodes: number; readonly maximumDepth: number; readonly maximumLiterals: number; readonly maximumTake: number }
  | { readonly kind: "selection-previous-state"; readonly maximumFields: number }
  | { readonly kind: "selection-identity" }
  | { readonly kind: "selection-patch"; readonly patchTypeId: string }
  | { readonly kind: "module-generation" }
  | { readonly kind: "subject-lifecycle-cursor" }
  | { readonly kind: "subject-lifecycle-checkpoint" }
  | { readonly kind: "subject-lifecycle-authority-epoch" }
  | { readonly kind: "subject-lifecycle-incarnation" }
  | { readonly kind: "boolean" }
  | { readonly kind: "string"; readonly minLength: number; readonly maxLength: number; readonly format: StringFormat }
  | { readonly kind: "integer"; readonly minimum: string; readonly maximum: string; readonly wire: "number" | "decimal-string" }
  | { readonly kind: "decimal"; readonly wire: "decimal-string" }
  | { readonly kind: "floating"; readonly precision: "binary32" | "binary64"; readonly finiteOnly: true }
  | { readonly kind: "bytes"; readonly wire: "base64"; readonly maxBytes: number }
  | { readonly kind: "redacted" }
  | { readonly kind: "subjectReference"; readonly contractId: string; readonly contractVersion: number; readonly subjectIdKind: "ordinalString" | "guid" | "uint64"; readonly maximumSubjectIdUtf8Bytes: number; readonly authorityEpochBytes: 16; readonly incarnationBytes: 24 }
  | { readonly kind: "literal"; readonly value: string | boolean | null }
  | { readonly kind: "enum"; readonly values: readonly string[] }
  | { readonly kind: "array"; readonly elementTypeId: string; readonly minItems: number; readonly maxItems: number }
  | { readonly kind: "object"; readonly properties: readonly PropertyDescriptor[]; readonly additionalProperties: false }
  | { readonly kind: "union"; readonly discriminator: string; readonly variants: readonly { readonly tag: string; readonly typeId: string }[] };
export interface PropertyDescriptor { readonly name: string; readonly wireName: string; readonly typeId: string; readonly required: boolean; readonly nullable: boolean; readonly disclosureShape: "none" | "omission" | "fixed-marker"; }
export interface EndpointDescriptor { readonly id: string; readonly audience: "application" | "controlPlane"; readonly operation: string; readonly method: string; readonly route: string; readonly capability?: string; readonly requestTypeId?: string; readonly responseTypeId?: string; readonly successStatuses: readonly number[]; readonly errorCodes: readonly string[]; readonly maximumRequestBodyBytes: number; readonly responseMode: "json" | "bytes" | "stream" | "webSocket" | "empty"; readonly replay: "none" | "channelDependent"; readonly resume: "none" | "durableCursor"; readonly cache: "none" | "structuralDigest"; }
export interface CapabilityDescriptor { readonly id: string; readonly available: boolean; }
export interface ReadDescriptor { readonly id: string; readonly generatedName: string; readonly endpointId: string; readonly parameterTypeId: string; readonly rowTypeId: string; readonly maxPageSize: number; readonly watchable: boolean; }
export interface DependencyDescriptor { readonly id: string; readonly kind: string; readonly visibility: string; readonly parameterTypeIds: readonly string[]; }
export interface ErrorDescriptor { readonly code: string; readonly category: string; readonly retryable: boolean; }
export interface VectorDescriptor { readonly collectionId: string; readonly id: string; readonly generatedName: string; readonly dimensions: number; readonly measure: "cosineSimilarity" | "dotProductSimilarity" | "euclideanDistance"; readonly filterFieldIds: readonly string[]; }
export interface SelectionMutationDescriptor { readonly id: string; readonly version: number; readonly checksum: string; readonly collectionId: string; readonly generatedName: string; readonly mutationKind: "mergePatch" | "delete"; readonly endpointId: string; readonly route: string; readonly maximumSelectedRecords: number; readonly maximumRequestBodyBytes: number; readonly requestTypeId: string; readonly resultTypeId: string; }
export interface ModuleMutationDescriptor { readonly id: string; readonly version: number; readonly generatedName: string; readonly audience: "service" | "system"; readonly requestTypeId: string; readonly resultTypeId: string; readonly route: string; readonly maximumRequestBytes: number; }
export interface SubjectLifecycleConsumerDescriptor { readonly id: string; readonly version: number; readonly checksum: string; readonly generatedName: string; readonly audience: "service" | "system"; readonly contractId: string; readonly contractVersion: number; readonly observedStates: readonly ("active" | "inactive" | "tombstoned" | "retired")[]; readonly readRoute: string; readonly checkpointRoute: string; readonly maximumFactsPerPage: number; readonly maximumResultBytes: number; }
