import { readFileSync } from "node:fs";
import { describe, expect, it } from "vitest";
import { canonicalJson, framedHash, hex } from "../src/canonical.js";
import { parseSnapshot } from "../src/input.js";
import { createGenerationPlan } from "../src/normalize.js";
import { render } from "../src/render.js";
import type { JsonValue } from "../src/types.js";

const fixture = readFileSync(new URL("../fixtures/gateway-client-snapshot.json", import.meta.url));

describe("real Gateway snapshot", () => {
  it("verifies and normalizes all 23 operations", () => {
    const snapshot = parseSnapshot(fixture);
    expect(snapshot.manifest.operations).toHaveLength(23);
    expect(createGenerationPlan(snapshot).outputPlanSha256).toMatch(/^[0-9a-f]{64}$/u);
  });

  it("rejects a recomputed operation mismatch", () => {
    const value = JSON.parse(fixture.toString("utf8")) as Record<string, any>;
    value.manifest.operations[0].openApiOperationId = "HpdGatewayAdmin.drift";
    rehash(value);
    expect(() => parseSnapshot(new TextEncoder().encode(JSON.stringify(value)))).toThrow(/Operation ID drift/u);
  });

  it("rejects unknown manifest members before generation", () => {
    const value = JSON.parse(fixture.toString("utf8")) as Record<string, any>;
    value.manifest.operations[0].invented = true;
    rehash(value);
    expect(() => parseSnapshot(new TextEncoder().encode(JSON.stringify(value)))).toThrow(/unknown members/u);
  });

  it("rejects empty, unsupported, and cyclic schemas before generation", () => {
    const empty = JSON.parse(fixture.toString("utf8")) as Record<string, any>;
    empty.openApi.components.schemas.HPD_Gateway_Admin_GatewayAdminError = {};
    rehash(empty);
    expect(() => parseSnapshot(new TextEncoder().encode(JSON.stringify(empty)))).toThrow(/Empty schema/u);

    const unsupported = JSON.parse(fixture.toString("utf8")) as Record<string, any>;
    unsupported.openApi.components.schemas.HPD_Gateway_Admin_GatewayAdminError.invented = true;
    rehash(unsupported);
    expect(() => parseSnapshot(new TextEncoder().encode(JSON.stringify(unsupported)))).toThrow(/unknown members/u);

    const cyclic = JSON.parse(fixture.toString("utf8")) as Record<string, any>;
    cyclic.openApi.components.schemas.HPD_Gateway_Admin_GatewayAdminError = { $ref: "#/components/schemas/HPD_Gateway_Admin_GatewayAdminError" };
    rehash(cyclic);
    expect(() => parseSnapshot(new TextEncoder().encode(JSON.stringify(cyclic)))).toThrow(/Cyclic schema reference/u);
  });

  it("rejects security, parameter, response, and source-identity drift", () => {
    const security = clone();
    const firstOperation = operation(security, 0);
    firstOperation.security = [{ wrong: [] }];
    rehash(security);
    expect(() => parse(security)).toThrow(/unknown members/u);

    const scopes = clone();
    operation(scopes, 0).security[0].test = ["invented"];
    rehash(scopes);
    expect(() => parse(scopes)).toThrow(/scopes must be empty/u);

    const parameter = clone();
    const constrained = parameter.manifest.operations.find((value: any) => value.parameterConstraints.length > 0)!;
    const wire = parameter.openApi.paths[constrained.path][constrained.method.toLowerCase()];
    wire.parameters[0].schema.maxLength = 127;
    rehash(parameter);
    expect(() => parse(parameter)).toThrow(/constraint drift/u);

    const response = clone();
    const responseOperation = response.manifest.operations[0];
    const responseWire = response.openApi.paths[responseOperation.path][responseOperation.method.toLowerCase()];
    responseWire.responses[String(responseOperation.success.status)].content["application/json"].schema.$ref =
      "#/components/schemas/HPD_Gateway_Admin_GatewayAdminError";
    rehash(response);
    expect(() => parse(response)).toThrow(/Schema reference drift/u);

    const identity = clone();
    identity.sourceSha256 = "0".repeat(64);
    expect(() => parse(identity)).toThrow(/source hash mismatch/u);
  });

  it("renders every operation deterministically from semantically reordered input", () => {
    const original = clone();
    const reordered = clone();
    reordered.openApi = reverseObjects(reordered.openApi);
    reordered.manifest = reverseObjects(reordered.manifest);
    rehash(reordered);
    const originalPlan = createGenerationPlan(parse(original));
    const reorderedPlan = createGenerationPlan(parse(reordered));
    const originalFiles = render(originalPlan);
    expect(render(reorderedPlan)).toEqual(originalFiles);
    const operations = originalFiles["operations.ts"]!;
    for (const operation of original.manifest.operations) {
      const name = operation.operation.split(/[^A-Za-z0-9]+/u).filter(Boolean)
        .map((part: string) => part[0]!.toUpperCase() + part.slice(1)).join("");
      expect(operations).toContain(`export interface ${name}Input`);
      expect(operations).toContain(`export type ${name}Result`);
    }
    expect(operations).toContain("readonly maximum?: number");
    expect(operations).not.toContain("readonly maximum?: string");
    const schemas = originalFiles["schemas.ts"]!;
    expect(schemas).toContain('export type GatewayActivationIntentId = GatewayBrand<"activation-intent-id">;');
    expect(schemas).toContain("readonly activationIntentId: GatewayActivationIntentId | null");
    expect(schemas).toContain("readonly desiredStateToken: GatewayDesiredStateToken | null");
    expect(schemas).toContain("readonly revisionId: GatewayRevisionId | null");
    expect(schemas).toContain("readonly items: readonly (GatewayOutcomeProjection)[]");
    expect(originalFiles["result.ts"]!).toContain('"request-too-large"');
  });

  it("enforces the exact schema and aggregate-property bounds", () => {
    for (const count of [257, 512]) {
      const value = withSchemaCount(count);
      rehash(value);
      expect(() => parse(value)).not.toThrow();
    }
    expect(() => rehash(withSchemaCount(513))).toThrow(/512 properties/u);

    const atPropertyLimit = withAggregatePropertyCount(4_096);
    rehash(atPropertyLimit);
    expect(() => parse(atPropertyLimit)).not.toThrow();
    const beyondPropertyLimit = withAggregatePropertyCount(4_097);
    rehash(beyondPropertyLimit);
    expect(() => parse(beyondPropertyLimit)).toThrow(/Aggregate schema property bound/u);
  });

  it("preserves non-NFC OpenAPI presentation while rejecting it in the manifest", () => {
    const presentation = clone();
    const decomposed = "Gate\u0301way presentation";
    presentation.openApi.info.title = decomposed;
    rehash(presentation);
    const parsed = parse(presentation);
    expect((parsed.openApi.info as Record<string, unknown>).title).toBe(decomposed);
    expect(parsed.sourceSha256).toBe("73fc639a960d48ad46e3a8a49d9ef009b1e608b163d6eb5b45786801745d7ffa");

    const semantic = clone();
    semantic.manifest.operations[0].capability = "gate\u0301way.capability";
    expect(() => parse(semantic)).toThrow(/Invalid capability/u);
  });
});

function clone(): Record<string, any> { return JSON.parse(fixture.toString("utf8")) as Record<string, any>; }
function parse(value: Record<string, any>) { return parseSnapshot(new TextEncoder().encode(JSON.stringify(value))); }
function operation(value: Record<string, any>, index: number): Record<string, any> {
  const semantic = value.manifest.operations[index];
  return value.openApi.paths[semantic.path][semantic.method.toLowerCase()] as Record<string, any>;
}
function reverseObjects(value: any): any {
  if (Array.isArray(value)) return value.map(reverseObjects);
  if (value === null || typeof value !== "object") return value;
  return Object.fromEntries(Object.entries(value).reverse().map(([key, child]) => [key, reverseObjects(child)]));
}
function withSchemaCount(count: number): Record<string, any> {
  const value = clone();
  const schemas = value.openApi.components.schemas as Record<string, any>;
  for (let index = Object.keys(schemas).length; index < count; index++) schemas[`Synthetic_${index.toString().padStart(3, "0")}`] = { type: "string" };
  return value;
}
function withAggregatePropertyCount(target: number): Record<string, any> {
  const value = clone();
  const schemas = value.openApi.components.schemas as Record<string, any>;
  let remaining = target - aggregatePropertyCount(schemas);
  let schemaIndex = 0;
  while (remaining > 0) {
    const count = Math.min(remaining, 256);
    schemas[`PropertyBound_${schemaIndex++}`] = {
      type: "object",
      properties: Object.fromEntries(Array.from({ length: count }, (_, index) => [`p${index.toString().padStart(3, "0")}`, { type: "string" }])),
    };
    remaining -= count;
  }
  return value;
}
function aggregatePropertyCount(schemas: Record<string, any>): number {
  const count = (schema: any): number => {
    if (!schema || typeof schema !== "object" || schema.$ref) return 0;
    let result = schema.properties ? Object.keys(schema.properties).length + Object.values(schema.properties).reduce((sum: number, child) => sum + count(child), 0) : 0;
    if (schema.items) result += count(schema.items);
    if (schema.oneOf) result += schema.oneOf.reduce((sum: number, child: any) => sum + count(child), 0);
    return result;
  };
  return Object.values(schemas).reduce((sum: number, schema) => sum + count(schema), 0);
}

function rehash(value: Record<string, any>): void {
  const openApi = framedHash("HPD.Gateway.OpenApi.v1\0", canonicalJson(value.openApi as JsonValue));
  const manifest = framedHash("HPD.Gateway.ClientManifest.v1\0", canonicalJson(value.manifest as JsonValue, true));
  value.openApiSha256 = hex(openApi);
  value.manifestSha256 = hex(manifest);
  value.sourceSha256 = hex(framedHash("HPD.Gateway.ClientSnapshot.v1\0", openApi, manifest));
}
