import { readFile } from "node:fs/promises";
import { canonicalJson, framedHash, hex } from "./canonical.js";
import type { GatewayClientGenerationManifest, GatewayClientGenerationSnapshot, JsonValue } from "./types.js";

const maximumSnapshotBytes = 8 * 1024 * 1024;
const hashPattern = /^[0-9a-f]{64}$/u;

export async function loadSnapshot(path: string): Promise<GatewayClientGenerationSnapshot> {
  const bytes = await readFile(path);
  return parseSnapshot(bytes);
}

export function parseSnapshot(bytes: Uint8Array): GatewayClientGenerationSnapshot {
  if (bytes.byteLength === 0 || bytes.byteLength > maximumSnapshotBytes) fail("Snapshot byte bound exceeded.");
  const text = new TextDecoder("utf-8", { fatal: true }).decode(bytes);
  rejectDuplicateObjectNames(text);
  const root: unknown = JSON.parse(text);
  const snapshot = object(root, "snapshot");
  exact(snapshot, ["snapshotVersion", "hashAlgorithm", "openApiSha256", "manifestSha256", "sourceSha256", "openApi", "manifest"]);
  if (snapshot.snapshotVersion !== 1 || snapshot.hashAlgorithm !== "sha-256") fail("Unsupported snapshot version or hash algorithm.");
  const openApiSha256 = digest(snapshot.openApiSha256, "openApiSha256");
  const manifestSha256 = digest(snapshot.manifestSha256, "manifestSha256");
  const sourceSha256 = digest(snapshot.sourceSha256, "sourceSha256");
  const openApi = object(snapshot.openApi, "openApi") as Record<string, JsonValue>;
  const manifest = validateManifest(snapshot.manifest);
  const openApiBytes = canonicalJson(openApi);
  const manifestBytes = canonicalJson(manifest as unknown as JsonValue);
  const openApiDigest = framedHash("HPD.Gateway.OpenApi.v1\0", openApiBytes);
  const manifestDigest = framedHash("HPD.Gateway.ClientManifest.v1\0", manifestBytes);
  if (hex(openApiDigest) !== openApiSha256 || hex(manifestDigest) !== manifestSha256) fail("Snapshot payload hash mismatch.");
  if (hex(framedHash("HPD.Gateway.ClientSnapshot.v1\0", openApiDigest, manifestDigest)) !== sourceSha256)
    fail("Snapshot source hash mismatch.");
  return { snapshotVersion: 1, hashAlgorithm: "sha-256", openApiSha256, manifestSha256, sourceSha256, openApi, manifest };
}

function validateManifest(input: unknown): GatewayClientGenerationManifest {
  const value = object(input, "manifest");
  exact(value, ["schemaVersion", "apiVersion", "openApiDocumentName", "securityScheme", "operations", "schemaConstraints"]);
  if (value.schemaVersion !== 1 || value.apiVersion !== "1.0.0" || value.openApiDocumentName !== "hpd-gateway-v1")
    fail("Unsupported manifest identity.");
  if (typeof value.securityScheme !== "string" || utf8(value.securityScheme) < 1 || utf8(value.securityScheme) > 128)
    fail("Invalid manifest security scheme.");
  if (!Array.isArray(value.operations) || value.operations.length !== 23) fail("Manifest must contain exactly 23 operations.");
  if (!Array.isArray(value.schemaConstraints) || value.schemaConstraints.length > 10_000) fail("Invalid schema-constraint collection.");
  let prior = "";
  for (const operation of value.operations) {
    const item = object(operation, "operation");
    if (typeof item.operation !== "string" || item.operation <= prior) fail("Operations are not unique canonical ordinal entries.");
    prior = item.operation;
  }
  return value as unknown as GatewayClientGenerationManifest;
}

function object(value: unknown, scope: string): Record<string, unknown> {
  if (value === null || typeof value !== "object" || Array.isArray(value)) fail(`${scope} must be an object.`);
  const result = value as Record<string, unknown>;
  if (Object.keys(result).length > 256) fail(`${scope} exceeds 256 properties.`);
  return result;
}

function exact(value: Record<string, unknown>, fields: readonly string[]): void {
  const actual = Object.keys(value).sort();
  const expected = [...fields].sort();
  if (actual.length !== expected.length || actual.some((field, index) => field !== expected[index]))
    fail("Object contains missing or unknown members.");
}

function digest(value: unknown, name: string): string {
  if (typeof value !== "string" || !hashPattern.test(value)) fail(`Invalid ${name}.`);
  return value;
}

function utf8(value: string): number { return new TextEncoder().encode(value).byteLength; }
function fail(message: string): never { throw new Error(message); }

// JSON.parse keeps the last duplicate property. This bounded lexical pass rejects
// duplicates before materialization while respecting strings and object scopes.
function rejectDuplicateObjectNames(text: string): void {
  const stack: Array<Set<string> | null> = [];
  let index = 0;
  while (index < text.length) {
    const current = text[index]!;
    if (/\s/u.test(current)) { index++; continue; }
    if (current === "{") { stack.push(new Set()); index++; continue; }
    if (current === "[") { stack.push(null); index++; continue; }
    if (current === "}" || current === "]") { stack.pop(); index++; continue; }
    if (current !== '"') { index++; continue; }
    const start = index++;
    while (index < text.length) {
      if (text[index] === "\\") { index += 2; continue; }
      if (text[index++] === '"') break;
    }
    let probe = index;
    while (probe < text.length && /\s/u.test(text[probe]!)) probe++;
    if (text[probe] !== ":" || stack.at(-1) === null) continue;
    const name = JSON.parse(text.slice(start, index)) as string;
    const names = stack.at(-1);
    if (names?.has(name)) fail("Duplicate JSON property.");
    names?.add(name);
  }
}
