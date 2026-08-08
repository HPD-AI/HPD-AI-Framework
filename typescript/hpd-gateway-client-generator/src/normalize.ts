import { canonicalJson, framedHash, hex } from "./canonical.js";
import type { GatewayClientGenerationSnapshot, GenerationPlan, JsonValue } from "./types.js";

export function createGenerationPlan(snapshot: GatewayClientGenerationSnapshot): GenerationPlan {
  const components = record(snapshot.openApi.components, "components");
  const schemas = record(components.schemas, "components.schemas");
  if (Object.keys(schemas).length === 0 || Object.keys(schemas).length > 512)
    throw new Error("Schema count is outside 1-512.");
  const operations = [...snapshot.manifest.operations];
  for (const operation of operations) {
    requireLocal(operation.success.schemaRef, schemas);
    if (operation.requestBody.schemaRef !== null) requireLocal(operation.requestBody.schemaRef, schemas);
  }
  for (const constraint of snapshot.manifest.schemaConstraints) requireLocal(constraint.schemaRef, schemas);
  const behavior = {
    version: 1,
    operations,
    schemas,
    schemaConstraints: snapshot.manifest.schemaConstraints,
  } as unknown as JsonValue;
  return {
    sourceSha256: snapshot.sourceSha256,
    openApiSha256: snapshot.openApiSha256,
    manifestSha256: snapshot.manifestSha256,
    outputPlanSha256: hex(framedHash("HPD.Gateway.TypeScriptPlan.v1\0", canonicalJson(behavior))),
    operations,
    schemas,
    schemaConstraints: snapshot.manifest.schemaConstraints,
  };
}

function requireLocal(reference: string, schemas: Readonly<Record<string, JsonValue>>): void {
  const prefix = "#/components/schemas/";
  if (!reference.startsWith(prefix) || schemas[reference.slice(prefix.length)] === undefined)
    throw new Error(`Unresolved or non-local schema reference '${reference}'.`);
}

function record(value: JsonValue | undefined, name: string): Readonly<Record<string, JsonValue>> {
  if (value === null || typeof value !== "object" || Array.isArray(value)) throw new Error(`${name} must be an object.`);
  return value as Readonly<Record<string, JsonValue>>;
}
