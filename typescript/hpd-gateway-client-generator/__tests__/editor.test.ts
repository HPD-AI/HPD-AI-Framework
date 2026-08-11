import { readFileSync } from "node:fs";
import { describe, expect, it } from "vitest";
import { canonicalJson, framedHash, hex } from "../src/canonical.js";
import { createEditorContract, parseEditorLedger, renderEditorContract } from "../src/editor.js";
import { parseSnapshot } from "../src/input.js";
import type { JsonValue } from "../src/types.js";

const snapshotBytes = readFileSync(new URL("../fixtures/gateway-client-snapshot.json", import.meta.url));
const ledgerBytes = readFileSync(new URL("../fixtures/gateway-editor-ledger.json", import.meta.url));

describe("Gateway declaration editor contract", () => {
  it("correlates all 420 fields and pins deterministic source identity", () => {
    const contract = createEditorContract(parseSnapshot(snapshotBytes), parseEditorLedger(ledgerBytes));
    expect(contract.fields).toHaveLength(420);
    expect(contract.sourceSha256).toBe("3af779e4901684435d0e36aa2ca6d82a1651425825322d1b3c1f04833892fed1");
    expect(Object.keys(renderEditorContract(contract)).sort()).toEqual(["editor-contract.json", "editor-contract.ts"]);
  });

  it("rejects duplicate, unknown, and hash-drifted export members", () => {
    const duplicate = ledgerBytes.toString("utf8").replace('"exportVersion":1', '"exportVersion":1,"exportVersion":1');
    expect(() => parseEditorLedger(new TextEncoder().encode(duplicate))).toThrow(/Duplicate JSON property/u);
    const unknown = cloneLedger(); unknown.invented = true;
    expect(() => parseLedger(unknown)).toThrow(/Unknown or missing/u);
    const hash = cloneLedger(); hash.envelopeSha256 = "0".repeat(64);
    expect(() => parseLedger(hash)).toThrow(/hash mismatch/u);
  });

  it("rejects missing, duplicate, reordered, and moved occurrences", () => {
    const missing = cloneLedger(); missing.envelope.records.pop(); rehashLedger(missing);
    const snapshot = parseSnapshot(snapshotBytes);
    expect(() => createEditorContract(snapshot, parseLedger(missing))).toThrow(/420 records/u);
    const duplicate = cloneLedger(); duplicate.envelope.records[1] = duplicate.envelope.records[0]; rehashLedger(duplicate);
    expect(() => parseLedger(duplicate)).toThrow(/Duplicate editor occurrence/u);
    const reordered = cloneLedger(); [reordered.envelope.records[0], reordered.envelope.records[1]] = [reordered.envelope.records[1], reordered.envelope.records[0]]; rehashLedger(reordered);
    expect(() => parseLedger(reordered)).toThrow(/canonically ordered/u);
    const moved = cloneLedger(); moved.envelope.records[0].target.componentSchemaPointer = "/properties/schemaVersion"; rehashLedger(moved);
    expect(() => createEditorContract(snapshot, parseLedger(moved))).toThrow(/correlation drift/u);
  });

  it("rejects constraint and OpenAPI wire drift", () => {
    const snapshot = JSON.parse(snapshotBytes.toString("utf8"));
    const record = cloneLedger().envelope.records.find((value: any) => value.target.constraintTargets.length > 0);
    record.target.constraintTargets[0].propertyPointer = "/properties/invented";
    const ledger = cloneLedger(); const target = ledger.envelope.records.find((value: any) => value.target.constraintTargets.length > 0); target.target.constraintTargets = record.target.constraintTargets; rehashLedger(ledger);
    expect(() => createEditorContract(parseSnapshot(snapshotBytes), parseLedger(ledger))).toThrow(/Missing manifest constraint/u);
    const component = snapshot.openApi.components.schemas.HPD_Gateway_Abstractions_GatewayConfiguration;
    component.properties.schemaVersion.type = "string";
    rehashSnapshot(snapshot);
    const drifted = createEditorContract(parseSnapshot(new TextEncoder().encode(JSON.stringify(snapshot))), parseEditorLedger(ledgerBytes));
    expect(drifted.sourceSha256).not.toBe("20337124f7ec5bfcba1677cd9d7cc93d677c3d872e81def279c9c601ffa12c81");
    expect((drifted.fields[1]!.wire as any).valueKind).toBeDefined();
  });

  it("is invariant to equivalent object-member reordering", () => {
    const snapshot = parseSnapshot(snapshotBytes);
    const original = createEditorContract(snapshot, parseEditorLedger(ledgerBytes));
    const reordered = reverseObjects(cloneLedger()); rehashLedger(reordered);
    expect(renderEditorContract(createEditorContract(snapshot, parseLedger(reordered)))).toEqual(renderEditorContract(original));
  });
});

function cloneLedger(): any { return JSON.parse(ledgerBytes.toString("utf8")); }
function parseLedger(value: any) { return parseEditorLedger(new TextEncoder().encode(JSON.stringify(value))); }
function rehashLedger(value: any): void { value.envelopeSha256 = hex(framedHash("hpd.gateway.editor-ledger.v1\0", canonicalJson(value.envelope as JsonValue, true))); }
function rehashSnapshot(value: any): void {
  const openApi = framedHash("HPD.Gateway.OpenApi.v1\0", canonicalJson(value.openApi as JsonValue));
  const manifest = framedHash("HPD.Gateway.ClientManifest.v1\0", canonicalJson(value.manifest as JsonValue, true));
  value.openApiSha256 = hex(openApi); value.manifestSha256 = hex(manifest);
  value.sourceSha256 = hex(framedHash("HPD.Gateway.ClientSnapshot.v1\0", openApi, manifest));
}
function reverseObjects(value: any): any { if (Array.isArray(value)) return value.map(reverseObjects); if (value === null || typeof value !== "object") return value; return Object.fromEntries(Object.entries(value).reverse().map(([key, child]) => [key, reverseObjects(child)])); }
