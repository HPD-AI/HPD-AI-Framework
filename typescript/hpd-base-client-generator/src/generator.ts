import { createHash } from "node:crypto";
import { mkdir, readFile, rename, rm, writeFile } from "node:fs/promises";
import { dirname, join, resolve } from "node:path";
import { spawn } from "node:child_process";
import { createRequire } from "node:module";
import type { CollectionDescriptor, GenerationSnapshot, NamedTypeDescriptor, VectorDescriptor } from "./types.js";

export interface GenerateOptions { readonly snapshot: GenerationSnapshot; readonly out: string; readonly expectedAudience?: "application" | "controlPlane"; }

export async function generate(options: GenerateOptions): Promise<void> {
  validate(options.snapshot, options.expectedAudience);
  const destination = resolve(options.out);
  const staging = `${destination}.staging-${process.pid}-${crypto.randomUUID()}`;
  await mkdir(staging, { recursive: false });
  try {
    const files = render(options.snapshot);
    for (const [name, contents] of Object.entries(files).sort(([left], [right]) => left.localeCompare(right))) await writeFile(join(staging, name), contents, { encoding: "utf8", flag: "wx" });
    await writeFile(join(staging, "tsconfig.json"), verificationConfig(), { encoding: "utf8", flag: "wx" });
    await validateTypeScript(staging);
    await rm(join(staging, "tsconfig.json"));
    const previous = `${destination}.previous-${process.pid}-${crypto.randomUUID()}`;
    await mkdir(dirname(destination), { recursive: true });
    let movedPrevious = false;
    try { await rename(destination, previous); movedPrevious = true; } catch (error: unknown) { if (!isMissing(error)) throw error; }
    try { await rename(staging, destination); }
    catch (error: unknown) { if (movedPrevious) await rename(previous, destination); throw error; }
    await rm(previous, { recursive: true, force: true });
  } catch (error: unknown) {
    await rm(staging, { recursive: true, force: true });
    throw error;
  }
}

export function validate(snapshot: GenerationSnapshot, expectedAudience?: "application" | "controlPlane"): void {
  exactKeys(snapshot as unknown as Record<string, unknown>, ["protocol", "application", "schema", "endpoints", "capabilities", "registeredReads", "dependencyTemplates", "vectorIndexes", "errors", "digest"]);
  exactKeys(snapshot.protocol as unknown as Record<string, unknown>, ["protocolMajor", "protocolMinor", "minimumClientMinor", "snapshotSchemaVersion", "applicationId", "schemaGeneration", "endpointInventoryDigest", "errorTaxonomyVersion", "realtimeProtocolVersion", "liveQueryProtocolVersion", "serializationProfile", "generatedAt"]);
  exactKeys(snapshot.application as unknown as Record<string, unknown>, ["applicationId", "audience", "basePath"]);
  if (snapshot.protocol.protocolMajor !== 2 || snapshot.protocol.snapshotSchemaVersion !== 2 || snapshot.protocol.realtimeProtocolVersion !== 2 || snapshot.protocol.liveQueryProtocolVersion !== 1 || snapshot.protocol.serializationProfile !== "base-json-v1" || snapshot.protocol.applicationId !== snapshot.application.applicationId || snapshot.protocol.schemaGeneration !== snapshot.schema.generation) throw new Error("base.client.protocolMismatch");
  if (expectedAudience !== undefined && snapshot.application.audience !== expectedAudience) throw new Error("base.client.endpointMismatch");
  if (!/^sha256:[0-9a-f]{64}$/u.test(snapshot.digest) || structuralDigest(digestInput(snapshot)) !== snapshot.digest) throw new Error("base.client.snapshotInvalid");
  const names = new Set<string>(["reads", "files", "close", "collection", "connectivity", "$control", "$dynamic"]);
  if (snapshot.schema.collections.length > 256 || snapshot.schema.types.length > 512 || snapshot.endpoints.length > 256 || snapshot.registeredReads.length > 256 || snapshot.vectorIndexes.length > 256 || snapshot.dependencyTemplates.length > 512) throw new Error("base.client.snapshotTooLarge");
  const typeIds = unique(snapshot.schema.types.map(type => type.id), "base.clientGeneration.typeCollision");
  unique(snapshot.endpoints.map(endpoint => endpoint.id), "base.clientGeneration.endpointCollision");
  exactKeys(snapshot.schema as unknown as Record<string, unknown>, ["generation", "collections", "types"]);
  for (const endpoint of snapshot.endpoints) exactKeys(endpoint as unknown as Record<string, unknown>, ["id", "method", "route", "audience", "operation", "capability", "requestTypeId", "responseTypeId", "successStatuses", "errorCodes", "maximumRequestBodyBytes", "responseMode", "replay", "resume", "cache"]);
  for (const capability of snapshot.capabilities) exactKeys(capability as unknown as Record<string, unknown>, ["id", "available"]);
  for (const read of snapshot.registeredReads) exactKeys(read as unknown as Record<string, unknown>, ["id", "generatedName", "endpointId", "parameterTypeId", "rowTypeId", "maxPageSize", "watchable"]);
  for (const dependency of snapshot.dependencyTemplates) exactKeys(dependency as unknown as Record<string, unknown>, ["id", "kind", "visibility", "parameterTypeIds"]);
  for (const vector of snapshot.vectorIndexes) exactKeys(vector as unknown as Record<string, unknown>, ["collectionId", "id", "generatedName", "dimensions", "measure", "filterFieldIds"]);
  for (const error of snapshot.errors) exactKeys(error as unknown as Record<string, unknown>, ["code", "category", "retryable"]);
  for (const collection of snapshot.schema.collections) {
    exactKeys(collection as unknown as Record<string, unknown>, ["id", "generatedName", "recordTypeId", "createTypeId", "replaceTypeId", "patchTypeId", "fields", "operations", "pagination", "maxPageSize"]);
    if (!names.add(collection.generatedName) || collection.generatedName.startsWith("$")) throw new Error("base.clientGeneration.nameCollision");
    for (const field of collection.fields) exactKeys(field as unknown as Record<string, unknown>, ["id", "wireName", "generatedName", "valueTypeId", "serverGenerated", "mutable", "redactionOptional", "operators"]);
    for (const id of [collection.recordTypeId, collection.createTypeId, collection.replaceTypeId, collection.patchTypeId, ...collection.fields.map(field => field.valueTypeId)]) if (!typeIds.has(id)) throw new Error("base.clientGeneration.typeMissing");
  }
  for (const read of snapshot.registeredReads) for (const id of [read.parameterTypeId, read.rowTypeId]) if (!typeIds.has(id)) throw new Error("base.clientGeneration.typeMissing");
  for (const endpoint of snapshot.endpoints) for (const id of [endpoint.requestTypeId, endpoint.responseTypeId]) if (id !== undefined && !typeIds.has(id) && !wellKnownWireTypeIds.has(id)) throw new Error("base.clientGeneration.typeMissing");
  validateTypeGraph(snapshot.schema.types, typeIds);
}

const wellKnownWireTypeIds = new Set([
  "application/octet-stream", "base.clientGeneration.snapshot.v2", "base.recordEnvelope", "base.recordPage", "base.deleteResult",
  "base.recordCreateRequest", "base.recordPatchRequest", "base.recordReplaceRequest", "base.recordDeleteRequest", "base.recordBatchRequest", "base.recordBatchResult",
  "base.recordUpsertRequest", "base.recordUpsertResult", "base.recordQuery", "base.realtime.v2.clientMessage", "base.realtime.v2.serverMessage",
  "base.vector.query.request", "base.vector.query.result", "base.vector.indexStatus", "base.vector.indexStatus.array", "base.vector.rebuild.request", "base.vector.rebuild.result",
  "base.admin.purge.request", "base.admin.purge.result", "base.admin.backup.create.request", "base.admin.backup.validate.request", "base.admin.backup.restore.request", "base.admin.backup.manifest", "base.admin.backup.restore.result",
  "base.manifest", "base.capabilityDescriptor", "base.schemaMetadata", "base.collectionDefinition", "base.collectionDefinitionArray", "base.healthDescriptorArray", "base.diagnosticDescriptorArray", "base.policyExplainRequest", "base.policyExplainResponse"
]);

function exactKeys(value: Record<string, unknown>, allowed: readonly string[]): void { const accepted = new Set(allowed); for (const key of Object.keys(value)) if (!accepted.has(key)) throw new Error("base.client.snapshotInvalid"); }

export function parseSnapshot(json: string): GenerationSnapshot {
  if (hasDuplicateProperties(json)) throw new Error("base.client.snapshotInvalid");
  const value = JSON.parse(json) as unknown;
  rejectInvalidStrings(value);
  return value as GenerationSnapshot;
}

function rejectInvalidStrings(value: unknown): void {
  if (typeof value === "string") { for (let index = 0; index < value.length; index++) { const unit = value.charCodeAt(index); if (unit >= 0xd800 && unit <= 0xdbff) { const next = value.charCodeAt(++index); if (!(next >= 0xdc00 && next <= 0xdfff)) throw new Error("base.client.snapshotInvalid"); } else if (unit >= 0xdc00 && unit <= 0xdfff) throw new Error("base.client.snapshotInvalid"); } return; }
  if (Array.isArray(value)) { for (const item of value) rejectInvalidStrings(item); return; }
  if (typeof value === "object" && value !== null) for (const [key, item] of Object.entries(value)) { rejectInvalidStrings(key); rejectInvalidStrings(item); }
}

function hasDuplicateProperties(json: string): boolean {
  let index = 0; let duplicate = false;
  const whitespace = (): void => { while (index < json.length && /[\t\n\r ]/u.test(json[index]!)) index++; };
  const string = (): string => { const start = index++; while (index < json.length) { const character = json[index++]!; if (character === "\\") index++; else if (character === '"') return JSON.parse(json.slice(start, index)) as string; } throw new SyntaxError(); };
  const value = (): void => { whitespace(); const character = json[index]; if (character === "{") { index++; whitespace(); const keys = new Set<string>(); if (json[index] === "}") { index++; return; } while (true) { whitespace(); if (json[index] !== '"') throw new SyntaxError(); const key = string(); if (keys.has(key)) duplicate = true; else keys.add(key); whitespace(); if (json[index++] !== ":") throw new SyntaxError(); value(); whitespace(); const separator = json[index++]; if (separator === "}") return; if (separator !== ",") throw new SyntaxError(); } } if (character === "[") { index++; whitespace(); if (json[index] === "]") { index++; return; } while (true) { value(); whitespace(); const separator = json[index++]; if (separator === "]") return; if (separator !== ",") throw new SyntaxError(); } } if (character === '"') { string(); return; } const match = /^(?:true|false|null|-?(?:0|[1-9]\d*)(?:\.\d+)?(?:[eE][+-]?\d+)?)/u.exec(json.slice(index)); if (match === null) throw new SyntaxError(); index += match[0].length; };
  value(); whitespace(); if (index !== json.length) throw new SyntaxError(); return duplicate;
}

function unique(values: readonly string[], code: string): Set<string> { const result = new Set<string>(); for (const value of values) { if (value.length === 0 || !result.add(value)) throw new Error(code); } return result; }
function validateTypeGraph(types: readonly NamedTypeDescriptor[], ids: ReadonlySet<string>): void {
  for (const type of types) {
    exactKeys(type as unknown as Record<string, unknown>, ["id", "node"]);
    const node = type.node;
    exactKeys(node as unknown as Record<string, unknown>, ["kind", "format", "precision", "finiteOnly", "minimum", "maximum", "wire", "value", "values", "elementTypeId", "maxBytes", "maxItems", "discriminator", "variants", "minLength", "maxLength", "properties", "additionalProperties"]);
    if (node.kind === "floating" && (node.finiteOnly !== true || (node.precision !== "binary32" && node.precision !== "binary64"))) throw new Error("base.clientGeneration.typeInvalid");
    if (node.kind === "array" && (node.elementTypeId === undefined || !ids.has(node.elementTypeId))) throw new Error("base.clientGeneration.typeMissing");
    for (const property of node.properties ?? []) { exactKeys(property as unknown as Record<string, unknown>, ["name", "typeId", "required", "nullable", "redactionOptional"]); if (!ids.has(property.typeId)) throw new Error("base.clientGeneration.typeMissing"); }
    for (const variant of node.variants ?? []) { exactKeys(variant as unknown as Record<string, unknown>, ["tag", "typeId"]); if (!ids.has(variant.typeId)) throw new Error("base.clientGeneration.typeMissing"); }
  }
}

function render(snapshot: GenerationSnapshot): Record<string, string> {
  const types = new Map(snapshot.schema.types.map(type => [type.id, type]));
  const records = snapshot.schema.collections.map(collection => renderCollectionTypes(collection, types)).join("\n");
  const collectionValues = snapshot.schema.collections.map(collection => renderCollectionValue(collection, types, snapshot.vectorIndexes.filter(index => index.collectionId === collection.id))).join(",\n");
  const vectors = snapshot.vectorIndexes.map(item => `export const ${safe(item.generatedName)} = ${JSON.stringify({ id: item.id, dimensions: item.dimensions, measure: item.measure, direction: item.measure === "euclideanDistance" ? "lowerIsNearer" : "higherIsNearer" })} as const;`).join("\n");
  const readTypes = snapshot.registeredReads.map(item => `${renderNamedObject(`${pascal(item.generatedName)}Parameters`, types.get(item.parameterTypeId), types)}\n${renderNamedObject(`${pascal(item.generatedName)}Row`, types.get(item.rowTypeId), types)}`).join("\n");
  const reads = snapshot.registeredReads.map(item => `${safe(item.generatedName)}: read<${pascal(item.generatedName)}Parameters, ${pascal(item.generatedName)}Row, ${item.watchable}>(${JSON.stringify({ id: item.id, maxPageSize: item.maxPageSize, watchable: item.watchable })})`).join(",\n");
  const features = { files: snapshot.endpoints.some(endpoint => endpoint.operation.startsWith("File")), realtime: snapshot.endpoints.some(endpoint => endpoint.operation === "RealtimeSubscribe"), batch: snapshot.schema.collections.some(collection => collection.operations.includes("batch")), controlOperations: snapshot.application.audience === "controlPlane" ? snapshot.endpoints.map(endpoint => endpoint.id).filter(id => id.startsWith("base.admin.") || id.startsWith("hpd.base.vector.")).sort() : [] };
  return {
    "collections.ts": `import { collection, field } from "@hpd/base-client";\nimport type { BaseFieldDefinition } from "@hpd/base-client";\n${records}\nexport const collections = {\n${collectionValues}\n} as const;\n`,
    "protocol.ts": `export const protocol = ${JSON.stringify({ protocolMajor: 2, schemaGeneration: snapshot.schema.generation, digest: snapshot.digest, audience: snapshot.application.audience, features })} as const;\n`,
    "fields.ts": `export { collections } from "./collections.js";\n`,
    "reads.ts": `import { read } from "@hpd/base-client";\n${readTypes}\nexport const reads = {\n${reads}\n} as const;\n`,
    "vectors.ts": `${vectors}\nexport const vectorIndexes = ${JSON.stringify(snapshot.vectorIndexes)} as const;\n`,
    "dependencies.ts": `export const dependencyTemplates = ${JSON.stringify(snapshot.dependencyTemplates)} as const;\n`,
    "errors.ts": `export const errors = ${JSON.stringify(snapshot.errors)} as const;\n`,
    "schema.ts": `import { collections } from "./collections.js";\nimport { reads } from "./reads.js";\nimport { protocol } from "./protocol.js";\nexport const schema = Object.freeze({ ...protocol, collections, reads });\n`,
    "index.ts": `export { schema } from "./schema.js";\nexport { collections } from "./collections.js";\nexport * from "./protocol.js";\nexport * from "./reads.js";\nexport * from "./vectors.js";\nexport * from "./dependencies.js";\nexport * from "./errors.js";\n`
  };
}

function renderCollectionTypes(collection: CollectionDescriptor, types: Map<string, NamedTypeDescriptor>): string {
  const output = collection.fields.map(field => `  readonly ${safe(field.generatedName)}${field.redactionOptional ? "?" : ""}: ${tsTypeWithGraph(types.get(field.valueTypeId), types)};`).join("\n");
  const create = collection.fields.filter(field => field.mutable && !field.serverGenerated).map(field => `  readonly ${safe(field.generatedName)}: ${tsTypeWithGraph(types.get(field.valueTypeId), types)};`).join("\n");
  const patch = collection.fields.filter(field => field.mutable && !field.serverGenerated).map(field => `  readonly ${safe(field.generatedName)}?: ${tsTypeWithGraph(types.get(field.valueTypeId), types)};`).join("\n");
  const name = pascal(collection.generatedName);
  return `export interface ${name} {\n${output}\n}\nexport interface ${name}Create {\n${create}\n}\nexport type ${name}Replace = ${name}Create;\nexport interface ${name}Patch {\n${patch}\n}\n`;
}

function renderCollectionValue(collection: CollectionDescriptor, types: Map<string, NamedTypeDescriptor>, vectors: readonly VectorDescriptor[]): string {
  const name = pascal(collection.generatedName);
  const fieldShape = collection.fields.map(item => `    readonly ${safe(item.generatedName)}: BaseFieldDefinition<${tsTypeWithGraph(types.get(item.valueTypeId), types)}, readonly [${item.operators.map(value => JSON.stringify(value)).join(", ")}]>;`).join("\n");
  const fields = collection.fields.map(item => `      ${safe(item.generatedName)}: field<${tsTypeWithGraph(types.get(item.valueTypeId), types)}, readonly [${item.operators.map(value => JSON.stringify(value)).join(", ")}]>(${JSON.stringify(item.id)}, ${JSON.stringify(item.wireName)}, ${JSON.stringify(item.operators)})`).join(",\n");
  const operationType = `readonly [${collection.operations.map(value => JSON.stringify(value)).join(", ")}]`;
  const vectorValues = vectors.map(item => `${safe(item.generatedName)}: ${JSON.stringify({ id: item.id, dimensions: item.dimensions, measure: item.measure, direction: item.measure === "euclideanDistance" ? "lowerIsNearer" : "higherIsNearer" })}`).join(", ");
  return `  ${safe(collection.generatedName)}: collection<${name}, ${name}Create, ${name}Replace, ${name}Patch, {\n${fieldShape}\n  }, ${operationType}>({ id: ${JSON.stringify(collection.id)}, fields: {\n${fields}\n  }, operations: ${JSON.stringify(collection.operations)}, pagination: ${JSON.stringify(collection.pagination)}, maxPageSize: ${collection.maxPageSize}, vectorIndexes: { ${vectorValues} } })`;
}

function tsType(type: NamedTypeDescriptor | undefined): string {
  if (type === undefined) throw new Error("base.clientGeneration.typeMissing");
  return type.node.kind === "boolean" ? "boolean"
    : type.node.kind === "integer" ? (type.node.wire === "number" ? "number" : "string")
    : type.node.kind === "decimal" ? "string"
    : type.node.kind === "floating" ? "number" : "string";
}
function tsTypeWithGraph(type: NamedTypeDescriptor | undefined, types: Map<string, NamedTypeDescriptor>): string {
  if (type?.node.kind === "array" && type.node.elementTypeId !== undefined) return `readonly ${tsTypeWithGraph(types.get(type.node.elementTypeId), types)}[]`;
  return tsType(type);
}
function renderNamedObject(name: string, type: NamedTypeDescriptor | undefined, types: Map<string, NamedTypeDescriptor>): string {
  if (type?.node.kind !== "object" || type.node.properties === undefined || type.node.additionalProperties !== false) throw new Error("base.clientGeneration.typeMissing");
  const properties = type.node.properties.map(property => `  readonly ${safe(property.name)}${property.required ? "" : "?"}: ${tsTypeWithGraph(types.get(property.typeId), types)}${property.nullable ? " | null" : ""};`).join("\n");
  return `export interface ${name} {\n${properties}\n}`;
}
function safe(value: string): string { if (!/^[A-Za-z_$][A-Za-z0-9_$]*$/u.test(value)) throw new Error("base.clientGeneration.nameInvalid"); return value; }
function pascal(value: string): string { return safe(value[0]!.toUpperCase() + value.slice(1)); }

async function validateTypeScript(staging: string): Promise<void> {
  const require = createRequire(import.meta.url);
  const manifestPath = require.resolve("typescript/package.json");
  const manifest = JSON.parse(await readFile(manifestPath, "utf8")) as { version?: unknown };
  if (manifest.version !== "7.0.2") throw new Error("base.clientGeneration.typeScriptVersionInvalid");
  const tsc = join(dirname(manifestPath), "bin", "tsc");
  const version = await execute([tsc, "--version"], dirname(manifestPath));
  if (version.code !== 0 || version.stdout.replace(/\r\n/gu, "\n") !== "Version 7.0.2\n" || version.stderr !== "") throw new Error("base.clientGeneration.typeScriptVersionInvalid");
  const checked = await execute([tsc, "--project", join(staging, "tsconfig.json"), "--noEmit", "--pretty", "false"], staging);
  if (checked.code !== 0) throw new Error(`base.clientGeneration.typeCheckFailed\n${checked.stdout.slice(0, 16_384)}${checked.stderr.slice(0, 16_384)}`);
}

function execute(arguments_: readonly string[], cwd: string): Promise<{ readonly code: number; readonly stdout: string; readonly stderr: string }> {
  return new Promise((resolvePromise, reject) => {
    const child = spawn(process.execPath, arguments_, { cwd, shell: false, stdio: ["ignore", "pipe", "pipe"] });
    let stdout = ""; let stderr = "";
    child.stdout.setEncoding("utf8"); child.stderr.setEncoding("utf8");
    child.stdout.on("data", (chunk: string) => { if (stdout.length < 32_768) stdout += chunk; });
    child.stderr.on("data", (chunk: string) => { if (stderr.length < 32_768) stderr += chunk; });
    child.once("error", reject);
    child.once("close", code => resolvePromise({ code: code ?? -1, stdout, stderr }));
  });
}

function verificationConfig(): string { return JSON.stringify({ compilerOptions: { target: "ES2024", module: "NodeNext", moduleResolution: "NodeNext", strict: true, noUncheckedIndexedAccess: true, exactOptionalPropertyTypes: true, noUncheckedSideEffectImports: true, noEmit: true }, include: ["./*.ts"] }, null, 2) + "\n"; }
function isMissing(error: unknown): boolean { return typeof error === "object" && error !== null && "code" in error && error.code === "ENOENT"; }

export function structuralDigest(value: unknown): string { return `sha256:${createHash("sha256").update(stableJson(value)).digest("hex")}`; }
function stableJson(value: unknown): string {
  if (value === null || typeof value !== "object") return JSON.stringify(value);
  if (Array.isArray(value)) return `[${value.map(stableJson).join(",")}]`;
  const record = value as Record<string, unknown>;
  return `{${Object.keys(record).sort().map(key => `${JSON.stringify(key)}:${stableJson(record[key])}`).join(",")}}`;
}

function digestInput(snapshot: GenerationSnapshot): unknown {
  const clone = structuredClone(snapshot) as unknown as Record<string, unknown> & { protocol: { generatedAt: string } };
  delete clone["digest"];
  clone.protocol.generatedAt = "";
  return clone;
}
