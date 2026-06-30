import { describe, expect, it } from "vitest";
import { safePropertyName, safeTypeName, uniqueName } from "../src/names.js";

describe("name planning", () => {
  it("normalizes hyphenated and spaced names", () => {
    expect(safePropertyName("user-profiles")).toBe("userProfiles");
    expect(safePropertyName("audit log")).toBe("auditLog");
    expect(safeTypeName("user-profiles")).toBe("UserProfiles");
  });

  it("handles reserved names and collisions deterministically", () => {
    expect(safePropertyName("collection")).toBe("collection_");
    const used = new Set<string>();
    expect(uniqueName("posts", used)).toBe("posts");
    expect(uniqueName("posts", used)).toBe("posts_2");
  });
});
