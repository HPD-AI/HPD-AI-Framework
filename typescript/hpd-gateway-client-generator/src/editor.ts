import { readFile } from "node:fs/promises";
import { canonicalJson, framedHash, hex, scalarOrdinal } from "./canonical.js";
import { rejectDuplicateObjectNames } from "./input.js";
import type { GatewayClientGenerationSnapshot, GatewayDeclarationEditorContract, GatewayEditorLedgerExport, GatewaySchemaConstraint, JsonValue } from "./types.js";

const hashPattern = /^[0-9a-f]{64}$/u;
const rootRef = "#/components/schemas/HPD_Gateway_Abstractions_GatewayConfiguration";
const exportKeys = ["envelope", "envelopeSha256", "exportVersion", "hashAlgorithm"] as const;
const envelopeKeys = ["declarationSchemaRef", "records", "schemaVersion"] as const;
const recordKeys = ["capability", "compositionScope", "disposition", "family", "helpCode", "inheritance", "inheritanceSourceOccurrencePath", "omittedValueJson", "omittedValueKind", "presentationGroup", "quickRouteStep", "structuralReason", "target"] as const;
const targetKeys = ["componentSchemaPointer", "componentSchemaRef", "constraintTargets", "occurrencePath"] as const;
const stepKeys = ["kind", "secondaryValue", "value"] as const;
const constraintTargetKeys = ["appliesTo", "propertyPointer", "schemaRef"] as const;
const capabilityKeys = ["kind", "relativeValuePointers"] as const;
const dispositions = ["editable", "structural-only"] as const;
const scopes = ["document", "root-defaults", "route", "route-match", "upstream", "endpoint-source", "destination", "definition", "metadata", "transform"] as const;
const omittedKinds = ["absent", "canonical-json"] as const;
const inheritances = ["none", "root-inherited-and-route-replaced"] as const;
const families = ["none", "routing", "authorization", "cors", "traffic-admission", "request-timeout", "output-cache", "telemetry", "inspection", "credential-disposition", "request-transform", "response-transform", "discovery", "secret", "tls", "resilience", "active-health", "passive-health", "session-affinity", "listener", "transport", "metadata"] as const;
const capabilityKinds = ["none", "installed-family", "listener", "discovery-provider", "secret-provider", "authorization-policy", "cors-policy", "traffic-admission-policy", "request-timeout-policy", "output-cache-profile", "resilience-profile", "request-inspector", "inspection-spill", "active-health-policy", "passive-health-policy", "session-affinity-policy", "session-affinity-failure-policy"] as const;
const groups = ["document", "identity", "match", "endpoint", "policies", "reliability", "security", "transport", "metadata", "advanced"] as const;
const quickSteps = ["none", "request-match", "upstream", "destination", "optional-policy"] as const;
const structuralReasons = ["none", "container", "collection", "collection-item", "identity-wrapper", "union-boundary"] as const;
const stepKinds = ["property", "items", "union-branch", "reference"] as const;
const appliesToKinds = ["value", "collection", "items"] as const;

export async function loadEditorLedger(path: string): Promise<GatewayEditorLedgerExport> {
  return parseEditorLedger(await readFile(path));
}

export function parseEditorLedger(bytes: Uint8Array): GatewayEditorLedgerExport {
  if (bytes.byteLength === 0 || bytes.byteLength > 8 * 1024 * 1024) fail("Editor ledger byte bound exceeded.");
  const text = new TextDecoder("utf-8", { fatal: true }).decode(bytes);
  rejectDuplicateObjectNames(text);
  const root = object(JSON.parse(text), "editor export");
  exact(root, exportKeys);
  if (root.exportVersion !== 1 || root.hashAlgorithm !== "sha-256") fail("Unsupported editor export identity.");
  const envelopeSha256 = digest(root.envelopeSha256, "envelopeSha256");
  const envelope = validateEnvelope(root.envelope);
  const envelopeBytes = canonicalJson(envelope, true);
  if (hex(framedHash("hpd.gateway.editor-ledger.v1\0", envelopeBytes)) !== envelopeSha256) fail("Editor envelope hash mismatch.");
  return { exportVersion: 1, hashAlgorithm: "sha-256", envelopeSha256, envelope };
}

export function createEditorContract(snapshot: GatewayClientGenerationSnapshot, editor: GatewayEditorLedgerExport): GatewayDeclarationEditorContract {
  const envelope = editor.envelope;
  if (envelope.declarationSchemaRef !== rootRef) fail("Editor declaration schema drift.");
  const schemas = object(object(snapshot.openApi.components, "components").schemas, "schemas");
  if (schemas[rootRef.slice(21)] === undefined) fail("Gateway declaration schema is absent.");
  const constraints = new Map(snapshot.manifest.schemaConstraints.map(value => [constraintKey(value), value]));
  const claimed = new Set<string>();
  const declarationSchemas = reachableSchemas(rootRef, schemas);
  const fields = (envelope.records as readonly JsonValue[]).map((input, index) => correlateField(object(input, `field ${index}`), schemas, constraints, claimed));
  for (const constraint of snapshot.manifest.schemaConstraints) {
    if (declarationSchemas.has(constraint.schemaRef) && !claimed.has(constraintKey(constraint))) fail(`Unclaimed declaration constraint '${constraintKey(constraint)}'.`);
  }
  const sourceSha256 = hex(framedHash("hpd.gateway.editor-contract-source.v1\0", decodeHash(editor.envelopeSha256), decodeHash(snapshot.openApiSha256), decodeHash(snapshot.manifestSha256)));
  return {
    editorContractVersion: 1,
    declarationSchemaRef: rootRef,
    editorLedgerSha256: editor.envelopeSha256,
    openApiSha256: snapshot.openApiSha256,
    manifestSha256: snapshot.manifestSha256,
    sourceSha256,
    fields,
  };
}

export function renderEditorContract(contract: GatewayDeclarationEditorContract): Readonly<Record<string, string>> {
  const json = new TextDecoder().decode(canonicalJson(contract as unknown as JsonValue, true));
  const types = `export type GatewayEditorOccurrenceStepV1 = { readonly kind: "property" | "items" | "union-branch" | "reference"; readonly value: string | null; readonly secondaryValue: string | null };\nexport type GatewayDeclarationEditorContractV1 = typeof gatewayDeclarationEditorContract;\n`;
  return {
    "editor-contract.json": `${json}\n`,
    "editor-contract.ts": `// Generated by @hpd/gateway-client-generator 0.1.0. DO NOT EDIT.\n${types}export const gatewayDeclarationEditorContract = ${json} as const;\n`,
  };
}

function validateEnvelope(input: unknown): Readonly<Record<string, JsonValue>> {
  const value = object(input, "editor envelope");
  exact(value, envelopeKeys);
  if (value.schemaVersion !== 1 || value.declarationSchemaRef !== rootRef) fail("Unsupported editor envelope identity.");
  const records = array(value.records, "editor records", 50_000).map(validateRecord);
  if (records.length !== 365) fail("Editor occurrence catalog must contain exactly 365 records.");
  let prior: readonly unknown[] | null = null;
  const seen = new Set<string>();
  for (const record of records) {
    const path = object(object(record, "record").target, "target").occurrencePath as readonly unknown[];
    const key = JSON.stringify(path);
    if (seen.has(key)) fail("Duplicate editor occurrence path.");
    if (prior !== null && comparePaths(prior, path) >= 0) fail("Editor records are not canonically ordered.");
    seen.add(key); prior = path;
  }
  return { declarationSchemaRef: rootRef, records, schemaVersion: 1 };
}

function validateRecord(input: unknown): JsonValue {
  const value = object(input, "editor record"); exact(value, recordKeys);
  const target = validateTarget(value.target);
  const capability = object(value.capability, "capability"); exact(capability, capabilityKeys);
  const capabilityKind = one(capability.kind, capabilityKinds, "capability kind");
  const relativeValuePointers = strings(capability.relativeValuePointers, "relative pointers", 2, 1024, true);
  const disposition = one(value.disposition, dispositions, "disposition");
  const omittedValueKind = one(value.omittedValueKind, omittedKinds, "omitted value kind");
  const omittedValueJson = nullableString(value.omittedValueJson, "omitted value", 16_384, false);
  if ((omittedValueKind === "absent") !== (omittedValueJson === null)) fail("Omitted value representation drift.");
  if (omittedValueJson !== null) canonicalScalarOrValue(omittedValueJson);
  const structuralReason = one(value.structuralReason, structuralReasons, "structural reason");
  if ((disposition === "editable") !== (structuralReason === "none")) fail("Disposition and structural reason drift.");
  return {
    capability: { kind: capabilityKind, relativeValuePointers },
    compositionScope: one(value.compositionScope, scopes, "composition scope"),
    disposition,
    family: one(value.family, families, "family"),
    helpCode: bounded(value.helpCode, "help code", 128, true),
    inheritance: one(value.inheritance, inheritances, "inheritance"),
    inheritanceSourceOccurrencePath: validatePath(value.inheritanceSourceOccurrencePath),
    omittedValueJson, omittedValueKind,
    presentationGroup: one(value.presentationGroup, groups, "presentation group"),
    quickRouteStep: one(value.quickRouteStep, quickSteps, "quick route step"),
    structuralReason, target,
  };
}

function validateTarget(input: unknown): JsonValue {
  const value = object(input, "field target"); exact(value, targetKeys);
  const constraintTargets = array(value.constraintTargets, "constraint targets", 3).map(item => {
    const target = object(item, "constraint target"); exact(target, constraintTargetKeys);
    return { appliesTo: one(target.appliesTo, appliesToKinds, "constraint appliesTo"), propertyPointer: pointer(target.propertyPointer), schemaRef: ref(target.schemaRef) };
  });
  return { componentSchemaPointer: pointer(value.componentSchemaPointer), componentSchemaRef: ref(value.componentSchemaRef), constraintTargets, occurrencePath: validatePath(value.occurrencePath) };
}

function validatePath(input: unknown): readonly JsonValue[] {
  return array(input, "occurrence path", 64).map(item => {
    const step = object(item, "occurrence step"); exact(step, stepKeys);
    const kind = one(step.kind, stepKinds, "occurrence kind");
    const value = nullableString(step.value, "occurrence value", 1024, true);
    const secondaryValue = nullableString(step.secondaryValue, "occurrence secondary value", 1024, true);
    if ((kind === "property" || kind === "union-branch" || kind === "reference") && value === null) fail("Occurrence step requires a value.");
    if (kind === "items" && (value !== null || secondaryValue !== null)) fail("Items occurrence step cannot carry values.");
    return { kind, secondaryValue, value };
  });
}

function correlateField(record: Record<string, unknown>, schemas: Record<string, unknown>, constraints: Map<string, GatewaySchemaConstraint>, claimed: Set<string>): Readonly<Record<string, JsonValue>> {
  const target = object(record.target, "field target");
  const occurrencePath = target.occurrencePath as readonly JsonValue[];
  const occurrenceSchema = followOccurrence(object(schemas[rootRef.slice(21)], "root schema"), occurrencePath, schemas);
  const componentRef = target.componentSchemaRef as string;
  const componentPointer = target.componentSchemaPointer as string;
  const componentSchema = object(resolvePointer(object(schemas[componentRef.slice(21)], "component schema"), componentPointer), "component target");
  if (!equivalent(occurrenceSchema, componentSchema)) fail("Editor occurrence/component correlation drift.");
  const correlated = (target.constraintTargets as readonly unknown[]).map(item => {
    const declared = object(item, "constraint target");
    const key = `${declared.schemaRef}\0${declared.propertyPointer}\0${declared.appliesTo}`;
    const constraint = constraints.get(key);
    if (constraint === undefined) fail(`Missing manifest constraint '${key}'.`);
    claimed.add(key);
    return { target: declared as unknown as JsonValue, brand: constraint.brand, rules: constraint.rules as unknown as JsonValue };
  });
  const schema = dereference(componentSchema, schemas);
  const required = requiredAtPointer(object(schemas[componentRef.slice(21)], "component schema"), componentPointer);
  const type = schema.type;
  const types = Array.isArray(type) ? type : [type];
  const valueKind = types.find(value => value !== "null");
  if (!["string", "integer", "number", "boolean", "object", "array"].includes(valueKind as string)) fail("Unsupported editor wire value kind.");
  const scalar = (name: string): string | null => schema[name] === undefined ? null : new TextDecoder().decode(canonicalJson(schema[name] as JsonValue));
  const wire: Record<string, JsonValue> = {
    constJson: scalar("const"),
    constraints: correlated,
    enumJson: schema.enum === undefined ? [] : (schema.enum as readonly JsonValue[]).map(value => new TextDecoder().decode(canonicalJson(value))),
    format: (schema.format ?? null) as JsonValue,
    maximumItems: (schema.maxItems ?? null) as JsonValue,
    maximumJson: scalar("maximum"),
    maximumLength: (schema.maxLength ?? null) as JsonValue,
    minimumItems: (schema.minItems ?? null) as JsonValue,
    minimumJson: scalar("minimum"),
    minimumLength: (schema.minLength ?? null) as JsonValue,
    nullable: types.includes("null"),
    pattern: (schema.pattern ?? null) as JsonValue,
    required,
    uniqueItems: schema.uniqueItems === true,
    valueKind: valueKind as JsonValue,
  };
  return { ...record as unknown as Readonly<Record<string, JsonValue>>, wire };
}

function followOccurrence(root: Record<string, unknown>, path: readonly JsonValue[], schemas: Record<string, unknown>): Record<string, unknown> {
  let current = root;
  for (const raw of path) {
    const step = raw as Readonly<Record<string, JsonValue>>;
    if (step.kind === "reference") {
      if (current.$ref !== step.value) fail("Occurrence reference hop drift.");
      current = object(schemas[(step.value as string).slice(21)], "referenced schema");
    } else if (step.kind === "property") {
      current = object(object(current.properties, "occurrence properties")[step.value as string], "occurrence property");
    } else if (step.kind === "items") current = object(current.items, "occurrence items");
    else {
      const branches = array(current.oneOf, "occurrence union", 256);
      const discriminator = object(current.discriminator, "occurrence discriminator");
      if (discriminator.propertyName !== step.value) fail("Occurrence discriminator property drift.");
      const expectedRef = object(discriminator.mapping, "occurrence discriminator mapping")[step.secondaryValue as string];
      current = object(branches.find(value => object(value, "occurrence branch").$ref === expectedRef), "occurrence union branch");
    }
  }
  return current;
}

function dereference(schema: Record<string, unknown>, schemas: Record<string, unknown>): Record<string, unknown> {
  return typeof schema.$ref === "string" ? object(schemas[schema.$ref.slice(21)], "dereferenced schema") : schema;
}
function requiredAtPointer(component: Record<string, unknown>, pointerValue: string): boolean {
  const parts = pointerValue.split("/").slice(1).map(unescapePointer);
  const final = parts.at(-1)!;
  const parentParts = parts.slice(0, -2);
  const parent = parentParts.length === 0 ? component : resolvePointer(component, `/${parentParts.map(escapePointer).join("/")}`);
  return Array.isArray(object(parent, "required parent").required) && (object(parent, "required parent").required as unknown[]).includes(final);
}
function reachableSchemas(start: string, schemas: Record<string, unknown>): Set<string> {
  const found = new Set<string>();
  const visit = (reference: string): void => {
    if (found.has(reference)) return;
    found.add(reference);
    const walk = (input: unknown): void => {
      const schema = object(input, "reachable schema");
      if (typeof schema.$ref === "string") { visit(schema.$ref); return; }
      if (schema.properties !== undefined) for (const value of Object.values(object(schema.properties, "reachable properties"))) walk(value);
      if (schema.items !== undefined) walk(schema.items);
      if (schema.oneOf !== undefined) for (const value of array(schema.oneOf, "reachable union", 256)) walk(value);
      if (schema.additionalProperties !== undefined && typeof schema.additionalProperties !== "boolean") walk(schema.additionalProperties);
    };
    walk(schemas[reference.slice(21)]);
  };
  visit(start);
  return found;
}
function constraintKey(value: Pick<GatewaySchemaConstraint, "schemaRef" | "propertyPointer" | "appliesTo">): string { return `${value.schemaRef}\0${value.propertyPointer}\0${value.appliesTo}`; }
function equivalent(left: Record<string, unknown>, right: Record<string, unknown>): boolean { return new TextDecoder().decode(canonicalJson(left as JsonValue)) === new TextDecoder().decode(canonicalJson(right as JsonValue)); }
function resolvePointer(root: Record<string, unknown>, value: string): unknown { let current: unknown = root; for (const segment of value.split("/").slice(1)) current = object(current, "pointer")[unescapePointer(segment)]; if (current === undefined) fail(`Unresolved pointer '${value}'.`); return current; }
function comparePaths(left: readonly unknown[], right: readonly unknown[]): number {
  const order = new Map(stepKinds.map((value, index) => [value, index]));
  const length = Math.min(left.length, right.length);
  for (let index = 0; index < length; index++) {
    const l = object(left[index], "left occurrence step");
    const r = object(right[index], "right occurrence step");
    const kind = (order.get(l.kind as typeof stepKinds[number]) ?? -1) - (order.get(r.kind as typeof stepKinds[number]) ?? -1);
    if (kind !== 0) return kind;
    for (const key of ["value", "secondaryValue"] as const) {
      const lv = l[key] as string | null;
      const rv = r[key] as string | null;
      if (lv === rv) continue;
      if (lv === null) return -1;
      if (rv === null) return 1;
      const compared = scalarOrdinal(lv, rv);
      if (compared !== 0) return compared;
    }
  }
  return left.length - right.length;
}
function exact(value: Record<string, unknown>, keys: readonly string[]): void { const actual = Object.keys(value).sort(scalarOrdinal); const expected = [...keys].sort(scalarOrdinal); if (actual.length !== expected.length || actual.some((key, index) => key !== expected[index])) fail("Unknown or missing editor member."); }
function object(value: unknown, name: string): Record<string, unknown> { if (value === null || typeof value !== "object" || Array.isArray(value)) fail(`${name} must be an object.`); return value as Record<string, unknown>; }
function array(value: unknown, name: string, maximum: number): unknown[] { if (!Array.isArray(value) || value.length > maximum) fail(`${name} must be a bounded array.`); return value; }
function strings(value: unknown, name: string, maximum: number, maxBytes: number, ascii: boolean): readonly string[] { return array(value, name, maximum).map(item => bounded(item, name, maxBytes, ascii)); }
function bounded(value: unknown, name: string, maximum: number, ascii: boolean): string { if (typeof value !== "string" || Buffer.byteLength(value) === 0 || Buffer.byteLength(value) > maximum || value !== value.normalize("NFC") || (ascii && !/^[\x20-\x7e]+$/u.test(value))) fail(`Invalid ${name}.`); return value; }
function nullableString(value: unknown, name: string, maximum: number, ascii: boolean): string | null { return value === null ? null : bounded(value, name, maximum, ascii); }
function one<const T extends readonly string[]>(value: unknown, values: T, name: string): T[number] { if (!values.includes(value as never)) fail(`Invalid ${name}.`); return value as T[number]; }
function ref(value: unknown): string { const result = bounded(value, "schema ref", 512, true); if (!result.startsWith("#/components/schemas/")) fail("Invalid schema reference."); return result; }
function pointer(value: unknown): string { const result = bounded(value, "schema pointer", 1024, true); if (!result.startsWith("/properties/")) fail("Invalid schema pointer."); return result; }
function digest(value: unknown, name: string): string { if (typeof value !== "string" || !hashPattern.test(value)) fail(`Invalid ${name}.`); return value; }
function decodeHash(value: string): Uint8Array { return Buffer.from(value, "hex"); }
function canonicalScalarOrValue(value: string): void { const parsed: unknown = JSON.parse(value); if (new TextDecoder().decode(canonicalJson(parsed as JsonValue, true)) !== value) fail("Omitted value JSON is not canonical."); }
function escapePointer(value: string): string { return value.replaceAll("~", "~0").replaceAll("/", "~1"); }
function unescapePointer(value: string): string { return value.replaceAll("~1", "/").replaceAll("~0", "~"); }
function fail(message: string): never { throw new Error(message); }
