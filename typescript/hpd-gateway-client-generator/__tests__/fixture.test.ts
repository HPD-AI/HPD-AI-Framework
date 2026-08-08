import { readFileSync } from "node:fs";
import { describe, expect, it } from "vitest";
import { canonicalJson, framedHash, hex } from "../src/canonical.js";
import { parseSnapshot } from "../src/input.js";
import { createGenerationPlan } from "../src/normalize.js";
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
});

function clone(): Record<string, any> { return JSON.parse(fixture.toString("utf8")) as Record<string, any>; }
function parse(value: Record<string, any>) { return parseSnapshot(new TextEncoder().encode(JSON.stringify(value))); }
function operation(value: Record<string, any>, index: number): Record<string, any> {
  const semantic = value.manifest.operations[index];
  return value.openApi.paths[semantic.path][semantic.method.toLowerCase()] as Record<string, any>;
}

function rehash(value: Record<string, any>): void {
  const openApi = framedHash("HPD.Gateway.OpenApi.v1\0", canonicalJson(value.openApi as JsonValue));
  const manifest = framedHash("HPD.Gateway.ClientManifest.v1\0", canonicalJson(value.manifest as JsonValue));
  value.openApiSha256 = hex(openApi);
  value.manifestSha256 = hex(manifest);
  value.sourceSha256 = hex(framedHash("HPD.Gateway.ClientSnapshot.v1\0", openApi, manifest));
}
