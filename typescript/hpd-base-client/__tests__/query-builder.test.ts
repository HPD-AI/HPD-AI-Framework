import { describe, expect, it } from "vitest";
import { q } from "../src/index.js";

describe("query builder", () => {
  it("emits DTOs without guessing typed strings", () => {
    expect(q.value("2026-01-01")).toEqual({ kind: "string", string: "2026-01-01" });
    expect(q.decimal("1.20")).toEqual({ kind: "decimal", decimal: "1.20" });
    expect(q.id("abc")).toEqual({ kind: "id", id: "abc" });
    expect(q.and(q.eq("title", "a"), q.isDefined("title"))).toEqual({
      kind: "and",
      children: [
        { kind: "compare", field: "title", operator: "equal", value: { kind: "string", string: "a" } },
        { kind: "isDefined", field: "title" }
      ]
    });
  });
});
