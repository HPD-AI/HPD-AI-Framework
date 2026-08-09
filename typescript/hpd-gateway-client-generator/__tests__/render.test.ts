import { describe, expect, it } from "vitest";
import { render } from "../src/render.js";
import type { GenerationPlan } from "../src/types.js";

describe("contract rendering", () => {
  it("emits the dependency-free contract and runtime files and preserves CAS semantics", () => {
    const plan: GenerationPlan = {
      sourceSha256: "a".repeat(64), openApiSha256: "b".repeat(64), manifestSha256: "c".repeat(64), outputPlanSha256: "d".repeat(64),
      schemas: {
        HPD_Gateway_Admin_Request: { type: "object", required: ["revisionId"], properties: { revisionId: { type: "string" } } },
        HPD_Gateway_Admin_Response: { type: "object", properties: { desiredStateToken: { type: "string" } } },
        HPD_Gateway_Admin_GatewayAdminError: { type: "object", properties: { code: { type: "string" } } },
      },
      schemaConstraints: [{
        schemaRef: "#/components/schemas/HPD_Gateway_Admin_Request", propertyPointer: "/properties/revisionId",
        appliesTo: "value", brand: "revision-id", rules: rules,
      }],
      parameterKinds: {
        "activate\0header\0Idempotency-Key": "string",
        "activate\0header\0If-Match": "string",
      },
      operations: [{
        operation: "activate", openApiOperationId: "HpdGatewayAdmin.activate", method: "POST", path: "/management/gateway/v1/activate",
        capability: "activate", resourcePolicy: "target", resourceKind: "target", mutation: true,
        idempotency: "required", desiredPrecondition: "create-or-replace", protectedNotFound: true,
        success: { status: 202, schemaRef: "#/components/schemas/HPD_Gateway_Admin_Response", meaning: "accepted-not-active" },
        documentedErrors: [400, 409], requestBody: { presence: "required", schemaRef: "#/components/schemas/HPD_Gateway_Admin_Request", mediaTypes: ["application/json"] },
        pagination: { kind: "none", defaultMaximum: null, minimumMaximum: null, maximumMaximum: null },
        parameterConstraints: [
          { location: "header", name: "Idempotency-Key", required: true, brand: "idempotency-key", rules },
          { location: "header", name: "If-Match", required: false, brand: "desired-state-token", rules },
        ],
      }],
    };
    const files = render(plan);
    expect(Object.keys(files).sort()).toEqual(["contract.ts", "index.ts", "operations.ts", "result.ts", "runtime.ts", "schemas.ts", "snapshot.json"]);
    expect(files["operations.ts"]).toContain("readonly desiredPrecondition: S.GatewayDesiredPrecondition");
    expect(files["operations.ts"]).toContain("GatewayOperationResult<S.Response, 202, 400 | 409>");
    expect(files["schemas.ts"]).toContain("readonly revisionId: GatewayRevisionId");
    expect(Object.values(files).every(value => value.endsWith("\n"))).toBe(true);
  });
});

const rules = {
  minimumUtf8Bytes: 1, maximumUtf8Bytes: 128, normalization: "NFC" as const, characterSet: "unicode" as const,
  rejectUnicodeControls: true, collectionMinimum: null, collectionMaximum: null, uniqueness: "none" as const,
  ordering: "none" as const, cardinality: "single" as const,
};
