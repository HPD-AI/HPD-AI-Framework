import { createHash } from "node:crypto";
import type { JsonValue } from "./types.js";

export function canonicalJson(value: JsonValue): Uint8Array {
  return new TextEncoder().encode(write(value, 0, ""));
}

export function framedHash(frame: string, ...values: readonly Uint8Array[]): Uint8Array {
  const hash = createHash("sha256");
  hash.update(frame, "utf8");
  for (const value of values) {
    const size = Buffer.allocUnsafe(8);
    size.writeBigUInt64BE(BigInt(value.byteLength));
    hash.update(size);
    hash.update(value);
  }
  return hash.digest();
}

export function hex(value: Uint8Array): string { return Buffer.from(value).toString("hex"); }

function write(value: JsonValue, depth: number, path: string): string {
  if (depth > 64) throw new Error("JSON depth exceeds 64.");
  if (value === null || typeof value === "boolean") return String(value);
  if (typeof value === "string") {
    if (value !== value.normalize("NFC")) throw new Error("Manifest strings must be NFC.");
    return JSON.stringify(value);
  }
  if (typeof value === "number") {
    if (!Number.isSafeInteger(value)) throw new Error("Canonical JSON permits safe integers only.");
    return String(value);
  }
  if (Array.isArray(value)) {
    if (value.length > 10_000) throw new Error("JSON array exceeds 10,000 items.");
    return `[${value.map(item => write(item, depth + 1, path)).join(",")}]`;
  }
  const object = value as Readonly<Record<string, JsonValue>>;
  const keys = Object.keys(object).sort(scalarOrdinal);
  const maximum = path === "/components/schemas" ? 512 : 256;
  if (keys.length > maximum) throw new Error(`JSON object exceeds ${maximum} properties.`);
  return `{${keys.map(key => `${JSON.stringify(key)}:${write(object[key]!, depth + 1, `${path}/${escapePointer(key)}`)}`).join(",")}}`;
}

export function scalarOrdinal(left: string, right: string): number {
  const leftValues = [...left].map(value => value.codePointAt(0)!);
  const rightValues = [...right].map(value => value.codePointAt(0)!);
  const length = Math.min(leftValues.length, rightValues.length);
  for (let index = 0; index < length; index++)
    if (leftValues[index] !== rightValues[index]) return leftValues[index]! < rightValues[index]! ? -1 : 1;
  return leftValues.length < rightValues.length ? -1 : leftValues.length > rightValues.length ? 1 : 0;
}
function escapePointer(value: string): string { return value.replaceAll("~", "~0").replaceAll("/", "~1"); }
