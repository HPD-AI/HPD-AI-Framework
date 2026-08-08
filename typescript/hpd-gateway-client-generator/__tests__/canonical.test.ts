import { describe, expect, it } from "vitest";
import { canonicalJson, framedHash, hex, scalarOrdinal } from "../src/canonical.js";
import { parseSnapshot } from "../src/input.js";

describe("canonical generation input", () => {
  it("is independent of object insertion order", () => {
    const left = canonicalJson({ z: 1, a: { y: true, x: null } });
    const right = canonicalJson({ a: { x: null, y: true }, z: 1 });
    expect(left).toEqual(right);
  });

  it("uses domain and length framed hashes", () => {
    const one = hex(framedHash("one\0", new Uint8Array([1, 2]), new Uint8Array([3])));
    const regrouped = hex(framedHash("one\0", new Uint8Array([1]), new Uint8Array([2, 3])));
    const anotherDomain = hex(framedHash("two\0", new Uint8Array([1, 2]), new Uint8Array([3])));
    expect(one).toMatch(/^[0-9a-f]{64}$/u);
    expect(one).not.toBe(regrouped);
    expect(one).not.toBe(anotherDomain);
  });

  it("rejects duplicate JSON properties before materialization", () => {
    const bytes = new TextEncoder().encode('{"snapshotVersion":1,"snapshotVersion":1}');
    expect(() => parseSnapshot(bytes)).toThrow(/Duplicate JSON property/u);
  });

  it("rejects unsafe numbers and non-NFC manifest strings", () => {
    expect(() => canonicalJson(9_007_199_254_740_992)).toThrow(/safe integers/u);
    expect(new TextDecoder().decode(canonicalJson("e\u0301"))).toBe('"é"');
    expect(() => canonicalJson("e\u0301", true)).toThrow(/NFC/u);
  });

  it("orders property names by Unicode scalar value", () => {
    const bmp = "\uE000";
    const supplementary = "\u{10000}";
    expect(scalarOrdinal(bmp, supplementary)).toBeLessThan(0);
    const text = new TextDecoder().decode(canonicalJson({ [supplementary]: 2, [bmp]: 1 }));
    expect(text.indexOf(bmp)).toBeLessThan(text.indexOf(supplementary));
  });

  it("rejects lone high and low surrogates in names and values", () => {
    for (const invalid of ["\uD800", "\uDC00"]) {
      expect(() => canonicalJson({ [invalid]: "value" })).toThrow(/lone UTF-16 surrogates/u);
      expect(() => canonicalJson({ key: invalid })).toThrow(/lone UTF-16 surrogates/u);
    }
    expect(() => canonicalJson({ key: "\uD83D\uDE00" })).not.toThrow();
  });
});
