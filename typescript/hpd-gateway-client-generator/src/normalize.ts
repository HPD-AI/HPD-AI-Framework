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
  const paths = record(snapshot.openApi.paths, "paths");
  const parameterKinds: Record<string, "string" | "number"> = {};
  for (const operation of operations) {
    const path = record(paths[operation.path], "operation path");
    const wire = record(path[operation.method.toLowerCase()], "operation wire shape");
    const parameters = wire.parameters as readonly JsonValue[];
    for (const constraint of operation.parameterConstraints) {
      const parameter = parameters.map(value => record(value, "parameter")).find(value =>
        value.in === constraint.location && value.name === constraint.name);
      const schema = record(parameter?.schema, "parameter schema");
      parameterKinds[parameterKindKey(operation.operation, constraint.location, constraint.name)] =
        schema.type === "integer" || schema.type === "number" ? "number" : "string";
    }
  }
  const behavior = {
    version: 1,
    operations,
    schemas,
    schemaConstraints: snapshot.manifest.schemaConstraints,
    parameterKinds,
  } as unknown as JsonValue;
  return {
    sourceSha256: snapshot.sourceSha256,
    openApiSha256: snapshot.openApiSha256,
    manifestSha256: snapshot.manifestSha256,
    outputPlanSha256: hex(framedHash("HPD.Gateway.TypeScriptPlan.v1\0", canonicalJson(behavior))),
    operations,
    schemas,
    schemaConstraints: snapshot.manifest.schemaConstraints,
    parameterKinds,
  };
}

export function parameterKindKey(operation: string, location: string, name: string): string {
  return `${operation}\0${location}\0${name}`;
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
