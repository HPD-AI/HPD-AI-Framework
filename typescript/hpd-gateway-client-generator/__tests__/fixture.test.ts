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
});

function rehash(value: Record<string, any>): void {
  const openApi = framedHash("HPD.Gateway.OpenApi.v1\0", canonicalJson(value.openApi as JsonValue));
  const manifest = framedHash("HPD.Gateway.ClientManifest.v1\0", canonicalJson(value.manifest as JsonValue));
  value.openApiSha256 = hex(openApi);
  value.manifestSha256 = hex(manifest);
  value.sourceSha256 = hex(framedHash("HPD.Gateway.ClientSnapshot.v1\0", openApi, manifest));
}
