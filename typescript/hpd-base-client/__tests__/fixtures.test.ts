import { describe, expect, it } from "vitest";
import recordEnvelope from "../fixtures/aspnet/record-envelope.json";
import validationProblem from "../fixtures/aspnet/problem-details.validation.json";

describe("fixtures", () => {
  it("preserves camelCase and lower-camel enum strings", () => {
    expect(recordEnvelope.payload.kind).toBe("json");
    expect(recordEnvelope.metadata.revision).toBe("r1");
    expect(validationProblem["hpd.status"]).toBe("validationFailed");
  });
});
