export type JsonScalar = string | number | boolean | null;
export type JsonValue = JsonScalar | readonly JsonValue[] | { readonly [key: string]: JsonValue };

export interface GatewayClientGenerationSnapshot {
  readonly snapshotVersion: 1;
  readonly hashAlgorithm: "sha-256";
  readonly openApiSha256: string;
  readonly manifestSha256: string;
  readonly sourceSha256: string;
  readonly openApi: Readonly<Record<string, JsonValue>>;
  readonly manifest: GatewayClientGenerationManifest;
}

export interface GatewayClientGenerationManifest {
  readonly schemaVersion: 1;
  readonly apiVersion: "1.0.0";
  readonly openApiDocumentName: "hpd-gateway-v1";
  readonly securityScheme: string;
  readonly operations: readonly GatewayClientOperation[];
  readonly schemaConstraints: readonly GatewaySchemaConstraint[];
}

export interface GatewayClientOperation {
  readonly operation: string;
  readonly openApiOperationId: string;
  readonly method: "GET" | "POST";
  readonly path: string;
  readonly capability: string;
  readonly resourcePolicy: string | null;
  readonly resourceKind: "none" | "namespace" | "target" | "administration";
  readonly mutation: boolean;
  readonly idempotency: "required" | "forbidden";
  readonly desiredPrecondition: "create-or-replace" | "forbidden";
  readonly protectedNotFound: boolean;
  readonly success: Readonly<{ status: 200 | 201 | 202; schemaRef: string; meaning: "completed-read" | "created" | "accepted-not-active" }>;
  readonly documentedErrors: readonly number[];
  readonly requestBody: Readonly<{ presence: "none" | "required" | "optional"; schemaRef: string | null; mediaTypes: readonly string[] }>;
  readonly pagination: Readonly<{ kind: "none" | "opaque-cursor"; defaultMaximum: number | null; minimumMaximum: number | null; maximumMaximum: number | null }>;
  readonly parameterConstraints: readonly GatewayParameterConstraint[];
}

export interface GatewayConstraintRules {
  readonly minimumUtf8Bytes: number | null;
  readonly maximumUtf8Bytes: number | null;
  readonly normalization: "none" | "NFC";
  readonly characterSet: "unicode" | "visible-ascii" | "lowercase-ascii-name" | "ascii-artifact-label" | "strong-entity-tag";
  readonly rejectUnicodeControls: boolean;
  readonly collectionMinimum: number | null;
  readonly collectionMaximum: number | null;
  readonly uniqueness: "none" | "ordinal" | "ordinal-ignore-case";
  readonly ordering: "none" | "ordinal-ascending" | "numeric-ascending";
  readonly cardinality: "single" | "multiple";
}

export type GatewayStringBrand = "none" | "namespace-id" | "target-node-id" | "revision-id" |
  "validation-id" | "operation-id" | "candidate-id" | "continuation-token" | "desired-state-token" |
  "idempotency-key" | "correlation-id";

export interface GatewayParameterConstraint {
  readonly location: "path" | "query" | "header";
  readonly name: string;
  readonly required: boolean;
  readonly brand: GatewayStringBrand;
  readonly rules: GatewayConstraintRules;
}

export interface GatewaySchemaConstraint {
  readonly schemaRef: string;
  readonly propertyPointer: string;
  readonly appliesTo: "value" | "collection" | "items";
  readonly brand: GatewayStringBrand;
  readonly rules: GatewayConstraintRules;
}
