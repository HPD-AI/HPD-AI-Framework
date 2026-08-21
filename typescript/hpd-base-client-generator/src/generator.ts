import { createHash } from "node:crypto";
import { mkdir, readFile, rename, rm, writeFile } from "node:fs/promises";
import { dirname, join, resolve } from "node:path";
import { spawn } from "node:child_process";
import { createRequire } from "node:module";
import type { CollectionDescriptor, GenerationSnapshot, NamedTypeDescriptor, TextIndexDescriptor, TypeNode, VectorDescriptor } from "./types.js";

export interface GenerateOptions { readonly snapshot: GenerationSnapshot; readonly out: string; readonly expectedAudience?: "application" | "controlPlane" | "service" | "system"; }

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

export function validate(snapshot: GenerationSnapshot, expectedAudience?: "application" | "controlPlane" | "service" | "system"): void {
  exactKeys(snapshot as unknown as Record<string, unknown>, ["protocol", "application", "schema", "endpoints", "capabilities", "registeredReads", "dependencyTemplates", "vectorIndexes", "textIndexes", "selectionMutations", "moduleMutations", "subjectLifecycleConsumers", "errors", "digest"]);
  exactKeys(snapshot.protocol as unknown as Record<string, unknown>, ["protocolMajor", "protocolMinor", "minimumClientMinor", "snapshotSchemaVersion", "applicationId", "schemaGeneration", "endpointInventoryDigest", "errorTaxonomyVersion", "realtimeProtocolVersion", "liveQueryProtocolVersion", "serializationProfile", "generatedAt"]);
  exactKeys(snapshot.application as unknown as Record<string, unknown>, ["applicationId", "audience", "basePath"]);
  if (snapshot.protocol.protocolMajor !== 2 || snapshot.protocol.snapshotSchemaVersion !== 6 || snapshot.protocol.realtimeProtocolVersion !== 2 || snapshot.protocol.liveQueryProtocolVersion !== 1 || snapshot.protocol.serializationProfile !== "base-json-v1" || snapshot.protocol.applicationId !== snapshot.application.applicationId || snapshot.protocol.schemaGeneration !== snapshot.schema.generation) throw new Error("base.client.protocolMismatch");
  if (expectedAudience !== undefined && snapshot.application.audience !== expectedAudience) throw new Error("base.client.endpointMismatch");
  if (!/^sha256:[0-9a-f]{64}$/u.test(snapshot.digest) || structuralDigest(digestInput(snapshot)) !== snapshot.digest) throw new Error("base.client.snapshotInvalid");
  const names = new Set<string>(["reads", "files", "close", "collection", "connectivity", "$control", "$dynamic"]);
  if (snapshot.schema.collections.length > 256 || snapshot.schema.types.length > 512 || snapshot.endpoints.length > 256 || snapshot.registeredReads.length > 256 || snapshot.vectorIndexes.length > 256 || snapshot.textIndexes.length > 256 || snapshot.selectionMutations.length > 256 || snapshot.moduleMutations.length > 256 || snapshot.subjectLifecycleConsumers.length > 32 || snapshot.dependencyTemplates.length > 512) throw new Error("base.client.snapshotTooLarge");
  if (snapshot.application.audience === "application" || snapshot.application.audience === "controlPlane") {
    if (snapshot.subjectLifecycleConsumers.length !== 0) throw new Error("base.client.audienceMismatch");
  } else {
    const names = unique(snapshot.subjectLifecycleConsumers.map(value => value.generatedName), "base.client.nameCollision");
    if (names.size !== snapshot.subjectLifecycleConsumers.length) throw new Error("base.client.nameCollision");
    for (const consumer of snapshot.subjectLifecycleConsumers) {
      exactKeys(consumer as unknown as Record<string, unknown>, ["id", "version", "checksum", "generatedName", "audience", "contractId", "contractVersion", "observedStates", "readRoute", "checkpointRoute", "reconciliationRoute", "retirementParticipation", "acknowledgementRoute", "retirementChecksum", "maximumFactsPerPage", "maximumResultBytes"]);
      if (!/^[0-9a-f]{64}$/u.test(consumer.checksum)) throw new Error("base.client.snapshotInvalid");
      if (!stableId(consumer.id) || !stableId(consumer.contractId) || !stableProperty(consumer.generatedName) || !safeBound(consumer.version, 1, 2147483647) || !safeBound(consumer.contractVersion, 1, 2147483647)
        || snapshot.application.audience === "service" && consumer.audience !== "service" || consumer.observedStates.length === 0 || consumer.observedStates.some(state => !["active", "inactive", "tombstoned", "retired"].includes(state))
        || !consumer.readRoute.startsWith("/") || !consumer.checkpointRoute.startsWith("/") || !["observeOnly", "advisory", "required"].includes(consumer.retirementParticipation)
        || consumer.retirementParticipation === "observeOnly" && (consumer.acknowledgementRoute != null || consumer.retirementChecksum != null)
        || consumer.retirementParticipation !== "observeOnly" && (typeof consumer.acknowledgementRoute !== "string" || !consumer.acknowledgementRoute.startsWith("/") || typeof consumer.retirementChecksum !== "string" || !/^[0-9a-f]{64}$/u.test(consumer.retirementChecksum))
        || !safeBound(consumer.maximumFactsPerPage, 1, 256) || !safeBound(consumer.maximumResultBytes, 1, 1_048_576)) throw new Error("base.client.contractInvalid");
    }
  }
  const typeIds = unique(snapshot.schema.types.map(type => type.id), "base.clientGeneration.typeCollision");
  unique(snapshot.endpoints.map(endpoint => endpoint.id), "base.clientGeneration.endpointCollision");
  exactKeys(snapshot.schema as unknown as Record<string, unknown>, ["generation", "collections", "types"]);
  for (const endpoint of snapshot.endpoints) exactKeys(endpoint as unknown as Record<string, unknown>, ["id", "method", "route", "audience", "operation", "capability", "requestTypeId", "responseTypeId", "successStatuses", "errorCodes", "maximumRequestBodyBytes", "responseMode", "replay", "resume", "cache"]);
  for (const capability of snapshot.capabilities) exactKeys(capability as unknown as Record<string, unknown>, ["id", "available"]);
  for (const read of snapshot.registeredReads) exactKeys(read as unknown as Record<string, unknown>, ["id", "generatedName", "endpointId", "parameterTypeId", "rowTypeId", "maxPageSize", "watchable"]);
  for (const selection of snapshot.selectionMutations) exactKeys(selection as unknown as Record<string, unknown>, ["id", "version", "checksum", "collectionId", "generatedName", "mutationKind", "endpointId", "route", "maximumSelectedRecords", "maximumRequestBodyBytes", "requestTypeId", "resultTypeId"]);
  for (const operation of snapshot.moduleMutations) {
    exactKeys(operation as unknown as Record<string, unknown>, ["id", "version", "generatedName", "audience", "requestTypeId", "resultTypeId", "route", "maximumRequestBytes"]);
    if (snapshot.application.audience !== "controlPlane" || !stableId(operation.id) || !safeBound(operation.version, 1, 2_147_483_647) || !safeBound(operation.maximumRequestBytes, 1, 1_048_576) || !typeIds.has(operation.requestTypeId) || !typeIds.has(operation.resultTypeId)) throw new Error("base.client.snapshotInvalid");
  }
  for (const dependency of snapshot.dependencyTemplates) exactKeys(dependency as unknown as Record<string, unknown>, ["id", "kind", "visibility", "parameterTypeIds"]);
  for (const vector of snapshot.vectorIndexes) exactKeys(vector as unknown as Record<string, unknown>, ["collectionId", "id", "generatedName", "dimensions", "measure", "filterFieldIds"]);
  for (const index of snapshot.textIndexes) {
    exactKeys(index as unknown as Record<string, unknown>, ["collectionId", "id", "version", "generatedName", "analyzerId", "scoringId", "audience", "fields", "filterFields", "maximumResults"]);
    if (!stableId(index.collectionId) || !stableId(index.id) || !safeBound(index.version, 1, 2_147_483_647) || !safeBound(index.maximumResults, 1, 256) || index.fields.length === 0 || index.fields.length > 16 || index.filterFields.length > 16) throw new Error("base.client.snapshotInvalid");
    for (const field of index.fields) { exactKeys(field as unknown as Record<string, unknown>, ["id", "generatedName", "wireName", "weight"]); if (!stableId(field.id) || !stableProperty(field.generatedName) || !stableProperty(field.wireName) || !safeBound(field.weight, 1, 64)) throw new Error("base.client.snapshotInvalid"); }
    for (const field of index.filterFields) { exactKeys(field as unknown as Record<string, unknown>, ["id", "generatedName", "wireName", "valueKind"]); if (!stableId(field.id) || !stableProperty(field.generatedName) || !stableProperty(field.wireName) || !["String", "Boolean", "Integer", "Id"].includes(field.valueKind)) throw new Error("base.client.snapshotInvalid"); }
  }
  for (const error of snapshot.errors) exactKeys(error as unknown as Record<string, unknown>, ["code", "category", "retryable"]);
  for (const collection of snapshot.schema.collections) {
    exactKeys(collection as unknown as Record<string, unknown>, ["id", "generatedName", "recordTypeId", "createTypeId", "replaceTypeId", "patchTypeId", "fields", "operations", "pagination", "maxPageSize"]);
    if (!names.add(collection.generatedName) || collection.generatedName.startsWith("$")) throw new Error("base.clientGeneration.nameCollision");
    for (const field of collection.fields) exactKeys(field as unknown as Record<string, unknown>, ["id", "wireName", "generatedName", "valueTypeId", "serverGenerated", "mutable", "disclosureShape", "operators"]);
    for (const id of [collection.recordTypeId, collection.createTypeId, collection.replaceTypeId, collection.patchTypeId, ...collection.fields.map(field => field.valueTypeId)]) if (!typeIds.has(id)) throw new Error("base.clientGeneration.typeMissing");
  }
  for (const read of snapshot.registeredReads) for (const id of [read.parameterTypeId, read.rowTypeId]) if (!typeIds.has(id)) throw new Error("base.clientGeneration.typeMissing");
  for (const endpoint of snapshot.endpoints) for (const id of [endpoint.requestTypeId, endpoint.responseTypeId]) if (id !== undefined && !typeIds.has(id) && !wellKnownWireTypeIds.has(id)) throw new Error("base.clientGeneration.typeMissing");
  validateTypeGraph(snapshot.schema.types, typeIds);
  if (snapshot.subjectLifecycleConsumers.length !== 0) {
    const byId = new Map(snapshot.schema.types.map(type => [type.id, type.node.kind]));
    for (const [id, kind] of Object.entries(requiredLifecycleTypeKinds))
      if (byId.get(id) !== kind) throw new Error("base.clientGeneration.typeMissing");
  }
  validateCollectionDtoBindings(snapshot.schema.collections, snapshot.schema.types);
}

const requiredLifecycleTypeKinds = {
  "base.subjectLifecycle.authorityEpoch": "subject-lifecycle-authority-epoch",
  "base.subjectLifecycle.incarnation": "subject-lifecycle-incarnation",
  "base.subjectLifecycle.cursor": "subject-lifecycle-cursor",
  "base.subjectLifecycle.checkpoint": "subject-lifecycle-checkpoint",
  "base.subjectLifecycle.fact": "object",
  "base.subjectLifecycle.page": "object",
} as const;

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

function unique(values: readonly string[], code: string): Set<string> { const result = new Set<string>(); for (const value of values) { if (value.length === 0 || result.has(value)) throw new Error(code); result.add(value); } return result; }
function validateTypeGraph(types: readonly NamedTypeDescriptor[], ids: ReadonlySet<string>): void {
  const byId = new Map(types.map(type => [type.id, type]));
  for (const type of types) {
    exactKeys(type as unknown as Record<string, unknown>, ["id", "node"]);
    if (!stableId(type.id)) invalidType();
    const node = type.node;
    const keys: Record<TypeNode["kind"], readonly string[]> = {
      "selection-query": ["kind", "maximumNodes", "maximumDepth", "maximumLiterals", "maximumTake"], "selection-previous-state": ["kind", "maximumFields"], "selection-identity": ["kind"], "selection-patch": ["kind", "patchTypeId"], "module-generation": ["kind"], "subject-lifecycle-cursor": ["kind"], "subject-lifecycle-checkpoint": ["kind"], "subject-lifecycle-authority-epoch": ["kind"], "subject-lifecycle-incarnation": ["kind"], boolean: ["kind"], string: ["kind", "minLength", "maxLength", "format"], integer: ["kind", "minimum", "maximum", "wire"], decimal: ["kind", "wire"], floating: ["kind", "precision", "finiteOnly"], bytes: ["kind", "wire", "maxBytes"], redacted: ["kind"], subjectReference: ["kind", "contractId", "contractVersion", "subjectIdKind", "maximumSubjectIdUtf8Bytes", "authorityEpochBytes", "incarnationBytes"], literal: ["kind", "value"], enum: ["kind", "values"], array: ["kind", "elementTypeId", "minItems", "maxItems"], object: ["kind", "properties", "additionalProperties"], union: ["kind", "discriminator", "variants"]
    };
    if (!Object.hasOwn(keys, node.kind)) invalidType();
    exactKeys(node as unknown as Record<string, unknown>, keys[node.kind]);
    if (node.kind === "selection-query" && (!safeBound(node.maximumNodes, 1, 4096) || !safeBound(node.maximumDepth, 1, 64) || !safeBound(node.maximumLiterals, 0, 16_384) || !safeBound(node.maximumTake, 1, 100_000))) invalidType();
    if (node.kind === "selection-previous-state" && !safeBound(node.maximumFields, 0, 4096)) invalidType();
    if (node.kind === "selection-patch" && !ids.has(node.patchTypeId)) throw new Error("base.clientGeneration.typeMissing");
    if (node.kind === "string" && (!safeBound(node.minLength, 0, 1_048_576) || !safeBound(node.maxLength, node.minLength, 1_048_576) || !["plain", "record-id", "collection-id", "field-id", "utc-instant", "revision", "cursor", "consistency-token", "mutation-id", "dependency-reference"].includes(node.format))) invalidType();
    if (node.kind === "integer" && (!integerText(node.minimum) || !integerText(node.maximum) || !["number", "decimal-string"].includes(node.wire) || BigInt(node.minimum) > BigInt(node.maximum) || node.wire === "number" && (BigInt(node.minimum) < BigInt(Number.MIN_SAFE_INTEGER) || BigInt(node.maximum) > BigInt(Number.MAX_SAFE_INTEGER)))) invalidType();
    if (node.kind === "decimal" && node.wire !== "decimal-string") invalidType();
    if (node.kind === "floating" && (node.finiteOnly !== true || !["binary32", "binary64"].includes(node.precision))) invalidType();
    if (node.kind === "bytes" && (node.wire !== "base64" || !safeBound(node.maxBytes, 0, 16 * 1024 * 1024))) invalidType();
    if (node.kind === "subjectReference" && (!stableId(node.contractId) || !safeBound(node.contractVersion, 1, 2147483647)
      || !["ordinalString", "guid", "uint64"].includes(node.subjectIdKind)
      || !safeBound(node.maximumSubjectIdUtf8Bytes, 1, 256) || node.authorityEpochBytes !== 16 || node.incarnationBytes !== 24)) invalidType();
    if (node.kind === "literal" && node.value !== null && typeof node.value !== "string" && typeof node.value !== "boolean") invalidType();
    if (node.kind === "enum" && (node.values.length === 0 || node.values.length > 256 || node.values.some(value => typeof value !== "string" || value.length === 0 || value.length > 256) || unique(node.values, "base.clientGeneration.typeInvalid").size !== node.values.length)) invalidType();
    if (node.kind === "array" && (!ids.has(node.elementTypeId) || !safeBound(node.minItems, 0, 1_048_576) || !safeBound(node.maxItems, node.minItems, 1_048_576))) throw new Error(!ids.has(node.elementTypeId) ? "base.clientGeneration.typeMissing" : "base.clientGeneration.typeInvalid");
    if (node.kind === "object") {
      if (node.additionalProperties !== false || node.properties.length > 256) invalidType();
      unique(node.properties.map(property => property.name), "base.clientGeneration.typeInvalid"); unique(node.properties.map(property => property.wireName), "base.clientGeneration.typeInvalid");
      for (const property of node.properties) { exactKeys(property as unknown as Record<string, unknown>, ["name", "wireName", "typeId", "required", "nullable", "disclosureShape"]); if (!stableProperty(property.name) || !stableProperty(property.wireName) || typeof property.required !== "boolean" || typeof property.nullable !== "boolean" || !["none", "omission", "fixed-marker"].includes(property.disclosureShape)) invalidType(); if (!ids.has(property.typeId)) throw new Error("base.clientGeneration.typeMissing"); }
    }
    if (node.kind === "union") {
      if (!/^[A-Za-z_$][A-Za-z0-9_$]*$/u.test(node.discriminator) || node.variants.length < 2 || node.variants.length > 64) invalidType();
      unique(node.variants.map(variant => variant.tag), "base.clientGeneration.typeInvalid"); unique(node.variants.map(variant => variant.typeId), "base.clientGeneration.typeInvalid");
      for (const variant of node.variants) {
        if (typeof variant.tag !== "string" || variant.tag.length === 0 || variant.tag.length > 128) invalidType();
        exactKeys(variant as unknown as Record<string, unknown>, ["tag", "typeId"]); const target = byId.get(variant.typeId); if (target === undefined) throw new Error("base.clientGeneration.typeMissing");
        if (target.node.kind !== "object") invalidType(); const discriminator = target.node.properties.find(property => property.name === node.discriminator); const literal = discriminator === undefined ? undefined : byId.get(discriminator.typeId)?.node;
        if (discriminator?.required !== true || discriminator.nullable || literal?.kind !== "literal" || literal.value !== variant.tag) invalidType();
      }
    }
  }
  const direct = (id: string): readonly string[] => { const node = byId.get(id)!.node; return node.kind === "union" ? node.variants.map(variant => variant.typeId) : []; };
  const visiting = new Set<string>(); const visited = new Set<string>(); const visit = (id: string): void => { if (visiting.has(id)) invalidType(); if (visited.has(id)) return; visiting.add(id); for (const child of direct(id)) visit(child); visiting.delete(id); visited.add(id); }; for (const id of ids) visit(id);
}

function invalidType(): never { throw new Error("base.clientGeneration.typeInvalid"); }
function validateCollectionDtoBindings(collections: readonly CollectionDescriptor[], types: readonly NamedTypeDescriptor[]): void {
  const byId = new Map(types.map(type => [type.id, type.node]));
  for (const collection of collections) {
    const expected = (mutableOnly: boolean): string[] => collection.fields.filter(field => !mutableOnly || field.mutable && !field.serverGenerated).map(field => `${field.generatedName}\u0000${field.wireName}\u0000${field.valueTypeId}`).sort();
    for (const [id, mutableOnly] of [[collection.recordTypeId, false], [collection.createTypeId, true], [collection.replaceTypeId, true], [collection.patchTypeId, true]] as const) {
      const node = byId.get(id); if (node?.kind !== "object") invalidType();
      const actual = node.properties.map(property => `${property.name}\u0000${property.wireName}\u0000${property.typeId}`).sort();
      if (actual.length !== expected(mutableOnly).length || actual.some((value, index) => value !== expected(mutableOnly)[index])) invalidType();
    }
  }
}
function safeBound(value: number, minimum: number, maximum: number): boolean { return Number.isSafeInteger(value) && value >= minimum && value <= maximum; }
function integerText(value: string): boolean { return /^-?(?:0|[1-9][0-9]*)$/u.test(value) && value !== "-0"; }
function stableId(value: string): boolean { return typeof value === "string" && value.length >= 1 && value.length <= 128 && /^[A-Za-z0-9][A-Za-z0-9._:-]*$/u.test(value); }
function stableProperty(value: string): boolean { return typeof value === "string" && value.length >= 1 && value.length <= 128 && !/[\u0000-\u001f\u007f]/u.test(value); }

function render(snapshot: GenerationSnapshot): Record<string, string> {
  const typeNames = new Map(snapshot.schema.types.map((type, index) => [type.id, `Type${index}`]));
  const records = snapshot.schema.collections.map(collection => renderCollectionTypes(collection, typeNames)).join("\n");
  const collectionValues = snapshot.schema.collections.map(collection => renderCollectionValue(collection, typeNames, snapshot.vectorIndexes.filter(index => index.collectionId === collection.id), snapshot.textIndexes.filter(index => index.collectionId === collection.id))).join(",\n");
  const vectors = snapshot.vectorIndexes.map(item => `export const ${safe(item.generatedName)} = ${JSON.stringify({ id: item.id, dimensions: item.dimensions, measure: item.measure, direction: item.measure === "euclideanDistance" ? "lowerIsNearer" : "higherIsNearer" })} as const;`).join("\n");
  const textIndexes = snapshot.textIndexes.map(item => `export const ${safe(item.generatedName)} = ${JSON.stringify({ id: item.id, version: item.version, maximumResults: item.maximumResults, filterFields: Object.fromEntries(item.filterFields.map(field => [field.generatedName, { id: field.id, wireName: field.wireName, valueKind: field.valueKind }])) })} as const;`).join("\n");
  const readTypes = snapshot.registeredReads.map(item => `export type ${pascal(item.generatedName)}Parameters = GeneratedTypes.${typeNames.get(item.parameterTypeId)!};\nexport type ${pascal(item.generatedName)}Row = GeneratedTypes.${typeNames.get(item.rowTypeId)!};`).join("\n");
  const reads = snapshot.registeredReads.map(item => `${safe(item.generatedName)}: read<${pascal(item.generatedName)}Parameters, ${pascal(item.generatedName)}Row, ${item.watchable}>(${JSON.stringify({ id: item.id, parameterTypeId: item.parameterTypeId, rowTypeId: item.rowTypeId, maxPageSize: item.maxPageSize, watchable: item.watchable })})`).join(",\n");
  const selections = snapshot.selectionMutations.map(item => `  ${safe(item.generatedName)}: selectionMutation<${pascal(item.generatedName)}Request>({ ...${JSON.stringify({ route: item.route, mutationKind: item.mutationKind, maximumRequestBodyBytes: item.maximumRequestBodyBytes, requestTypeId: item.requestTypeId, resultTypeId: item.resultTypeId })}, typeGraph })`).join(",\n");
  const selectionTypes = snapshot.selectionMutations.map(item => `export interface ${pascal(item.generatedName)}Request { readonly query: BaseSelectionHttpQuery; ${item.mutationKind === "mergePatch" ? "readonly patch: GeneratedTypes." + typeNames.get(snapshot.schema.collections.find(collection => collection.id === item.collectionId)!.patchTypeId) + "; " : ""}readonly previousState: BaseSelectionPreviousState; readonly requestIdentity?: BaseSelectionRequestIdentity; readonly callerWaitTimeoutTicks?: number; }`).join("\n");
  const moduleTypes = snapshot.moduleMutations.map(item => `export type ${pascal(item.generatedName)}Request = GeneratedTypes.${typeNames.get(item.requestTypeId)!};\nexport type ${pascal(item.generatedName)}Result = GeneratedTypes.${typeNames.get(item.resultTypeId)!};`).join("\n");
  const moduleValues = snapshot.moduleMutations.map(item => `  ${safe(item.generatedName)}: moduleMutation<${pascal(item.generatedName)}Request, ${pascal(item.generatedName)}Result>({ ...${JSON.stringify({ route: item.route, maximumRequestBytes: item.maximumRequestBytes, audience: item.audience, requestTypeId: item.requestTypeId, resultTypeId: item.resultTypeId })}, typeGraph })`).join(",\n");
  const lifecycleValues = snapshot.subjectLifecycleConsumers.map(item => `  ${safe(item.generatedName)}: subjectLifecycleConsumer(${JSON.stringify({ id: item.id, version: item.version, checksum: item.checksum, audience: item.audience, contractId: item.contractId, contractVersion: item.contractVersion, observedStates: item.observedStates, readRoute: item.readRoute, checkpointRoute: item.checkpointRoute, reconciliationRoute: item.reconciliationRoute ?? null, retirementParticipation: item.retirementParticipation, acknowledgementRoute: item.acknowledgementRoute ?? null, retirementChecksum: item.retirementChecksum ?? null, maximumFactsPerPage: item.maximumFactsPerPage, maximumResultBytes: item.maximumResultBytes })})`).join(",\n");
  const features = { files: snapshot.endpoints.some(endpoint => endpoint.operation.startsWith("File")), realtime: snapshot.endpoints.some(endpoint => endpoint.operation === "RealtimeSubscribe"), batch: snapshot.schema.collections.some(collection => collection.operations.includes("batch")), controlOperations: snapshot.application.audience === "controlPlane" ? snapshot.endpoints.map(endpoint => endpoint.id).filter(id => id.startsWith("base.admin.") || id.startsWith("hpd.base.vector.")).sort() : [] };
  const files: Record<string, string> = {
    "collections.ts": `import { collection, field } from "@hpd/base-client";\nimport type { BaseFieldDefinition } from "@hpd/base-client";\nimport type * as GeneratedTypes from "./types.js";\n${records}\nexport const collections = {\n${collectionValues}\n} as const;\n`,
    "protocol.ts": `export const protocol = ${JSON.stringify({ protocolMajor: 2, schemaGeneration: snapshot.schema.generation, digest: snapshot.digest, audience: snapshot.application.audience, features })} as const;\n`,
    "fields.ts": `export { collections } from "./collections.js";\n`,
    "reads.ts": `import { read } from "@hpd/base-client";\nimport type * as GeneratedTypes from "./types.js";\n${readTypes}\nexport const reads = {\n${reads}\n} as const;\n`,
    "selection-mutations.ts": `import { selectionMutation } from "@hpd/base-client";\nimport type { BaseSelectionHttpQuery, BaseSelectionPreviousState, BaseSelectionRequestIdentity } from "@hpd/base-client";\nimport { typeGraph } from "./types.js";\nimport type * as GeneratedTypes from "./types.js";\n${selectionTypes}\nexport const selectionMutations = {\n${selections}\n} as const;\n`,
    "types.ts": `${snapshot.schema.types.map((type, index) => `export type Type${index} = ${renderType(type.node, typeNames)};`).join("\n")}\nexport const typeGraph = ${JSON.stringify(Object.fromEntries(snapshot.schema.types.map(type => [type.id, type.node])))} as const;\n`,
    "vectors.ts": `${vectors}\nexport const vectorIndexes = ${JSON.stringify(snapshot.vectorIndexes)} as const;\n`,
    "text-indexes.ts": `${textIndexes}\nexport const textIndexes = ${JSON.stringify(snapshot.textIndexes)} as const;\n`,
    "dependencies.ts": `export const dependencyTemplates = ${JSON.stringify(snapshot.dependencyTemplates)} as const;\n`,
    "errors.ts": `export const errors = ${JSON.stringify(snapshot.errors)} as const;\n`,
    "schema.ts": `import { collections } from "./collections.js";\nimport { reads } from "./reads.js";\nimport { selectionMutations } from "./selection-mutations.js";\nimport { protocol } from "./protocol.js";\nimport { typeGraph } from "./types.js";\nexport const schema = Object.freeze({ ...protocol, collections, reads, selectionMutations, typeGraph });\n`,
    "index.ts": `export { schema } from "./schema.js";\nexport { collections } from "./collections.js";\nexport * from "./protocol.js";\nexport * from "./reads.js";\nexport * from "./selection-mutations.js";\nexport * from "./vectors.js";\nexport * from "./text-indexes.js";\nexport * from "./dependencies.js";\nexport * from "./errors.js";\nexport type * from "./types.js";\n`
  };
  if (snapshot.application.audience === "controlPlane") {
    files["module-mutations.ts"] = `import { moduleMutation } from "@hpd/base-client";\nimport { typeGraph } from "./types.js";\nimport type * as GeneratedTypes from "./types.js";\n${moduleTypes}\nexport const moduleMutations = {\n${moduleValues}\n} as const;\n`;
    files["schema.ts"] = `import { collections } from "./collections.js";\nimport { reads } from "./reads.js";\nimport { selectionMutations } from "./selection-mutations.js";\nimport { moduleMutations } from "./module-mutations.js";\nimport { protocol } from "./protocol.js";\nimport { typeGraph } from "./types.js";\nexport const schema = Object.freeze({ ...protocol, collections, reads, selectionMutations, moduleMutations, typeGraph });\n`;
    files["index.ts"] = files["index.ts"]!.replace('export * from "./selection-mutations.js";\n', 'export * from "./selection-mutations.js";\nexport * from "./module-mutations.js";\n');
  }
  if (snapshot.application.audience === "service" || snapshot.application.audience === "system") {
    files["subject-lifecycle.ts"] = `import { acknowledgeSubjectRetirement, advanceSubjectLifecycle, iterateSubjectLifecycle, iterateSubjectRetirement, readSubjectLifecycle, reconcileSubjectLifecycle, subjectLifecycleConsumer } from "@hpd/base-client";\nimport type { BaseHttpTransport, BaseSubjectAcknowledgementDisposition, BaseSubjectAdvisoryAcknowledgementEvidence, BaseSubjectLifecycleCheckpoint, BaseSubjectLifecycleMutationIdentity, BaseSubjectLifecycleReadOptions, BaseSubjectLifecycleReconciliationOptions, BaseSubjectRequiredAcknowledgementEvidence } from "@hpd/base-client";\nexport const subjectLifecycleConsumers = {\n${lifecycleValues}\n} as const;\nexport function createSubjectLifecycleWorkers(transport: BaseHttpTransport) { return Object.freeze({ ${snapshot.subjectLifecycleConsumers.map(item => { const name=safe(item.generatedName); const deliveries=item.retirementParticipation==="observeOnly"?`iterateSubjectLifecycle(transport, subjectLifecycleConsumers.${name}, options)`: `iterateSubjectRetirement(transport, subjectLifecycleConsumers.${name}, options)`; const acknowledge=item.retirementParticipation==="observeOnly"?"":`, acknowledge: (evidence: ${item.retirementParticipation==="advisory"?"BaseSubjectAdvisoryAcknowledgementEvidence":"BaseSubjectRequiredAcknowledgementEvidence"}, disposition: BaseSubjectAcknowledgementDisposition, identity: BaseSubjectLifecycleMutationIdentity, projectId?: string, signal?: AbortSignal) => acknowledgeSubjectRetirement(transport, { id: subjectLifecycleConsumers.${name}.id, version: subjectLifecycleConsumers.${name}.version, checksum: subjectLifecycleConsumers.${name}.retirementChecksum!, participation: ${JSON.stringify(item.retirementParticipation)}, acknowledgementRoute: subjectLifecycleConsumers.${name}.acknowledgementRoute! }, evidence, disposition, identity, projectId, signal)`; return `${name}: Object.freeze({ ${item.retirementParticipation==="observeOnly"?`read: (options?: BaseSubjectLifecycleReadOptions) => readSubjectLifecycle(transport, subjectLifecycleConsumers.${name}, options), `:""}deliveries: (options?: BaseSubjectLifecycleReadOptions) => ${deliveries}, advance: (checkpoint: BaseSubjectLifecycleCheckpoint, identity: BaseSubjectLifecycleMutationIdentity, projectId?: string, signal?: AbortSignal) => advanceSubjectLifecycle(transport, subjectLifecycleConsumers.${name}, checkpoint, identity, projectId, signal)${item.reconciliationRoute ? `, reconcile: (options?: BaseSubjectLifecycleReconciliationOptions) => reconcileSubjectLifecycle(transport, subjectLifecycleConsumers.${name}, options)` : ""}${acknowledge} })`; }).join(", ")} }); }\n`;
    files["index.ts"] += `export * from "./subject-lifecycle.js";\n`;
  }
  return files;
}

function renderCollectionTypes(collection: CollectionDescriptor, typeNames: ReadonlyMap<string, string>): string {
  const name = pascal(collection.generatedName);
  return `export type ${name} = GeneratedTypes.${typeNames.get(collection.recordTypeId)!};\nexport type ${name}Create = GeneratedTypes.${typeNames.get(collection.createTypeId)!};\nexport type ${name}Replace = GeneratedTypes.${typeNames.get(collection.replaceTypeId)!};\nexport type ${name}Patch = GeneratedTypes.${typeNames.get(collection.patchTypeId)!};\n`;
}

function renderCollectionValue(collection: CollectionDescriptor, typeNames: ReadonlyMap<string, string>, vectors: readonly VectorDescriptor[], textIndexes: readonly TextIndexDescriptor[]): string {
  const name = pascal(collection.generatedName);
  const fieldShape = collection.fields.map(item => `    readonly ${safe(item.generatedName)}: BaseFieldDefinition<GeneratedTypes.${typeNames.get(item.valueTypeId)!}, readonly [${item.operators.map(value => JSON.stringify(value)).join(", ")}]>;`).join("\n");
  const fields = collection.fields.map(item => `      ${safe(item.generatedName)}: field<GeneratedTypes.${typeNames.get(item.valueTypeId)!}, readonly [${item.operators.map(value => JSON.stringify(value)).join(", ")}]>(${JSON.stringify(item.id)}, ${JSON.stringify(item.wireName)}, ${JSON.stringify(item.operators)}, ${JSON.stringify(item.valueTypeId)}, ${JSON.stringify(item.disclosureShape)})`).join(",\n");
  const operationType = `readonly [${collection.operations.map(value => JSON.stringify(value)).join(", ")}]`;
  const vectorValues = vectors.map(item => `${safe(item.generatedName)}: ${JSON.stringify({ id: item.id, dimensions: item.dimensions, measure: item.measure, direction: item.measure === "euclideanDistance" ? "lowerIsNearer" : "higherIsNearer" })}`).join(", ");
  const textValues = textIndexes.map(item => `${safe(item.generatedName)}: ${JSON.stringify({ id: item.id, version: item.version, maximumResults: item.maximumResults, filterFields: Object.fromEntries(item.filterFields.map(field => [field.generatedName, { id: field.id, wireName: field.wireName, valueKind: field.valueKind }])) })}`).join(", ");
  return `  ${safe(collection.generatedName)}: collection<${name}, ${name}Create, ${name}Replace, ${name}Patch, {\n${fieldShape}\n  }, ${operationType}>({ id: ${JSON.stringify(collection.id)}, recordTypeId: ${JSON.stringify(collection.recordTypeId)}, createTypeId: ${JSON.stringify(collection.createTypeId)}, replaceTypeId: ${JSON.stringify(collection.replaceTypeId)}, patchTypeId: ${JSON.stringify(collection.patchTypeId)}, fields: {\n${fields}\n  }, operations: ${JSON.stringify(collection.operations)}, pagination: ${JSON.stringify(collection.pagination)}, maxPageSize: ${collection.maxPageSize}, vectorIndexes: { ${vectorValues} }, textIndexes: { ${textValues} } })`;
}

function renderType(node: TypeNode, names: ReadonlyMap<string, string>): string {
  switch (node.kind) {
    case "boolean": return "boolean";
    case "selection-query": return "import(\"@hpd/base-client\").BaseSelectionHttpQuery";
    case "selection-previous-state": return "import(\"@hpd/base-client\").BaseSelectionPreviousState";
    case "selection-identity": return "import(\"@hpd/base-client\").BaseSelectionRequestIdentity";
    case "selection-patch": return names.get(node.patchTypeId)!;
    case "module-generation": return "import(\"@hpd/base-client\").BaseModuleGeneration";
    case "subject-lifecycle-cursor": return "import(\"@hpd/base-client\").BaseSubjectLifecycleCursor";
    case "subject-lifecycle-checkpoint": return "import(\"@hpd/base-client\").BaseSubjectLifecycleCheckpoint";
    case "subject-lifecycle-authority-epoch": return "import(\"@hpd/base-client\").BaseSubjectLifecycleAuthorityEpoch";
    case "subject-lifecycle-incarnation": return "import(\"@hpd/base-client\").BaseSubjectLifecycleIncarnation";
    case "string": case "decimal": return "string";
    case "bytes": return "Uint8Array";
    case "redacted": return "import(\"@hpd/base-client\").BaseRedacted";
    case "subjectReference": return `import("@hpd/base-client").BaseSubjectReference<${JSON.stringify(node.contractId)}>`;
    case "integer": return node.wire === "number" ? "number" : "string";
    case "floating": return "number";
    case "literal": return JSON.stringify(node.value);
    case "enum": return node.values.map(value => JSON.stringify(value)).join(" | ");
    case "array": return `readonly ${names.get(node.elementTypeId)!}[]`;
    case "object": return `{ ${node.properties.map(property => `readonly ${JSON.stringify(property.name)}${property.required && property.disclosureShape !== "omission" ? "" : "?"}: ${names.get(property.typeId)!}${property.nullable ? " | null" : ""}${property.disclosureShape === "fixed-marker" ? " | import(\"@hpd/base-client\").BaseRedacted" : ""}`).join("; ")} }`;
    case "union": return node.variants.map(variant => names.get(variant.typeId)!).join(" | ");
  }
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
