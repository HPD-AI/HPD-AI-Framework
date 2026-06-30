import { describe, expect, it } from "vitest";
import { q } from "../src/index.js";
import { serializeRecordQueryForGet } from "../src/query/serialize.js";

describe("query serialize", () => {
  it("serializes only the implemented GET grammar", () => {
    const result = serializeRecordQueryForGet(q.query({ where: q.eq("title", "alpha"), sort: q.sortDesc("createdAt", "last"), count: "exact", dependencyToken: true }));
    expect(result.ok).toBe(true);
    expect(result.search?.toString()).toBe("where%5Btitle%5D=alpha&sort=-createdAt&nulls%5BcreatedAt%5D=last&count=exact&dependencyToken=true");
  });

  it("rejects typed values for GET", () => {
    const result = serializeRecordQueryForGet(q.query({ where: q.eq("createdAt", q.dateTime("2026-01-01T00:00:00Z")) }));
    expect(result.ok).toBe(false);
  });
});
