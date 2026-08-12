import assert from "node:assert/strict";
import { access, readFile, rm, writeFile } from "node:fs/promises";
import { execFile } from "node:child_process";
import { promisify } from "node:util";
import test from "node:test";
import { generate, parseSnapshot, structuralDigest, validate } from "../dist/index.js";

test("generation validates its digest, emits the complete surface, and typechecks with TypeScript 7", async () => {
  const output = new URL("../test-output", import.meta.url).pathname;
  await rm(output, { recursive: true, force: true });
  const base = {
    protocol: { protocolMajor: 2, protocolMinor: 0, minimumClientMinor: 0, snapshotSchemaVersion: 2, applicationId: "test", schemaGeneration: "1", endpointInventoryDigest: `sha256:${"1".repeat(64)}`, errorTaxonomyVersion: 1, realtimeProtocolVersion: 2, liveQueryProtocolVersion: 1, serializationProfile: "base-json-v1", generatedAt: "" },
    application: { audience: "application", applicationId: "test", basePath: "/base" },
    schema: {
      generation: "1",
      collections: [{ id: "documents", generatedName: "documents", recordTypeId: "collection.documents.record", createTypeId: "collection.documents.create", replaceTypeId: "collection.documents.replace", patchTypeId: "collection.documents.patch", fields: [{ id: "title", wireName: "title", generatedName: "title", valueTypeId: "field.documents.title", serverGenerated: false, mutable: true, redactionOptional: false, operators: ["equal"] }], operations: ["get", "query"], pagination: "seek", maxPageSize: 100 }],
      types: [
        { id: "field.documents.title", node: { kind: "string", format: "plain", minLength: 0, maxLength: 100 } },
        { id: "collection.documents.record", node: { kind: "object", additionalProperties: false, properties: [{ name: "title", typeId: "field.documents.title", required: true, nullable: false, redactionOptional: false }] } },
        { id: "collection.documents.create", node: { kind: "object", additionalProperties: false, properties: [{ name: "title", typeId: "field.documents.title", required: true, nullable: false, redactionOptional: false }] } },
        { id: "collection.documents.replace", node: { kind: "object", additionalProperties: false, properties: [{ name: "title", typeId: "field.documents.title", required: true, nullable: false, redactionOptional: false }] } },
        { id: "collection.documents.patch", node: { kind: "object", additionalProperties: false, properties: [{ name: "title", typeId: "field.documents.title", required: false, nullable: false, redactionOptional: false }] } },
        { id: "read.by-title.parameters.title", node: { kind: "string", format: "plain", minLength: 0, maxLength: 100 } },
        { id: "read.by-title.row.title", node: { kind: "string", format: "plain", minLength: 0, maxLength: 100 } },
        { id: "read.by-title.parameters", node: { kind: "object", additionalProperties: false, properties: [{ name: "title", typeId: "read.by-title.parameters.title", required: true, nullable: false, redactionOptional: false }] } },
        { id: "read.by-title.row", node: { kind: "object", additionalProperties: false, properties: [{ name: "title", typeId: "read.by-title.row.title", required: true, nullable: false, redactionOptional: false }] } },
        { id: "test.boolean", node: { kind: "boolean" } }, { id: "test.integer", node: { kind: "integer", minimum: "-5", maximum: "5", wire: "number" } }, { id: "test.large", node: { kind: "integer", minimum: "0", maximum: "99999999999999999999", wire: "decimal-string" } }, { id: "test.decimal", node: { kind: "decimal", wire: "decimal-string" } }, { id: "test.f32", node: { kind: "floating", precision: "binary32", finiteOnly: true } }, { id: "test.f64", node: { kind: "floating", precision: "binary64", finiteOnly: true } }, { id: "test.bytes", node: { kind: "bytes", wire: "base64", maxBytes: 16 } }, { id: "test.enum", node: { kind: "enum", values: ["one", "two"] } }, { id: "test.tag.a", node: { kind: "literal", value: "a" } }, { id: "test.tag.b", node: { kind: "literal", value: "b" } }, { id: "test.variant.a", node: { kind: "object", additionalProperties: false, properties: [{ name: "kind", typeId: "test.tag.a", required: true, nullable: false, redactionOptional: false }] } }, { id: "test.variant.b", node: { kind: "object", additionalProperties: false, properties: [{ name: "kind", typeId: "test.tag.b", required: true, nullable: false, redactionOptional: false }] } }, { id: "test.union", node: { kind: "union", discriminator: "kind", variants: [{ tag: "a", typeId: "test.variant.a" }, { tag: "b", typeId: "test.variant.b" }] } }, { id: "test.array", node: { kind: "array", elementTypeId: "test.union", maxItems: 4 } }
      ]
    },
    endpoints: [], capabilities: [],
    registeredReads: [{ id: "by-title", generatedName: "byTitle", endpointId: "base.reads.public.by-title", parameterTypeId: "read.by-title.parameters", rowTypeId: "read.by-title.row", maxPageSize: 100, watchable: false }],
    dependencyTemplates: [], vectorIndexes: [], errors: []
  };
  const snapshot = { ...base, protocol: { ...base.protocol, generatedAt: "2026-01-01T00:00:00Z" }, digest: structuralDigest(base) };
  await generate({ snapshot, out: output, expectedAudience: "application" });
  for (const name of ["index.ts", "protocol.ts", "schema.ts", "collections.ts", "fields.ts", "reads.ts", "vectors.ts", "dependencies.ts", "errors.ts", "types.ts"]) await access(`${output}/${name}`);
  assert.match(await readFile(`${output}/reads.ts`, "utf8"), /byTitle/);
  const generatedTypes = await readFile(`${output}/types.ts`, "utf8"); assert.match(generatedTypes, /number/); assert.match(generatedTypes, /"one" \| "two"/); assert.match(generatedTypes, /readonly Type\d+\[\]/); assert.match(generatedTypes, /Type\d+ \| Type\d+/);
  const generatedCollections = await readFile(`${output}/collections.ts`, "utf8");
  assert.match(generatedCollections, /export type Documents = GeneratedTypes\.Type\d+;/u);
  assert.match(generatedCollections, /export type DocumentsCreate = GeneratedTypes\.Type\d+;/u);
  assert.match(generatedCollections, /export type DocumentsReplace = GeneratedTypes\.Type\d+;/u);
  assert.match(generatedCollections, /export type DocumentsPatch = GeneratedTypes\.Type\d+;/u);
  assert.doesNotMatch(generatedCollections, /interface Documents/u);
  await writeFile(`${output}/consumer.ts`, 'import { createBaseClient } from "@hpd/base-client"; import { schema } from "./schema.js"; const base = createBaseClient({ url: "https://example.test/base", schema }); base.documents.get("id"); base.$dynamic.collection(schema.collections.documents); // @ts-expect-error unsupported mutation\nbase.documents.create({ title: "x" }); // @ts-expect-error application artifacts have no control plane\nbase.$control;\n');
  await writeFile(`${output}/tsconfig.json`, JSON.stringify({ compilerOptions: { target: "ES2024", module: "NodeNext", moduleResolution: "NodeNext", strict: true, noUncheckedIndexedAccess: true, exactOptionalPropertyTypes: true, noEmit: true }, include: ["./*.ts"] }));
  const typeScriptRoot = new URL("../node_modules/typescript/", import.meta.url).pathname;
  await promisify(execFile)(process.execPath, [`${typeScriptRoot}bin/tsc`, "--project", `${output}/tsconfig.json`, "--noEmit", "--pretty", "false"]);
  const second = new URL("../test-output-second", import.meta.url).pathname; await rm(second, { recursive: true, force: true }); await generate({ snapshot, out: second, expectedAudience: "application" });
  for (const name of ["index.ts", "protocol.ts", "schema.ts", "collections.ts", "fields.ts", "reads.ts", "vectors.ts", "dependencies.ts", "errors.ts", "types.ts"]) assert.equal(await readFile(`${output}/${name}`, "utf8"), await readFile(`${second}/${name}`, "utf8"));
  await rm(second, { recursive: true, force: true });
  await rm(output, { recursive: true, force: true });
});

test("type graph validation fails closed on malformed kinds, bounds, uniqueness, and union discriminators", () => {
  const snapshot = types => { const base = { protocol: { protocolMajor: 2, protocolMinor: 0, minimumClientMinor: 0, snapshotSchemaVersion: 2, applicationId: "test", schemaGeneration: "1", endpointInventoryDigest: `sha256:${"1".repeat(64)}`, errorTaxonomyVersion: 1, realtimeProtocolVersion: 2, liveQueryProtocolVersion: 1, serializationProfile: "base-json-v1", generatedAt: "" }, application: { audience: "application", applicationId: "test", basePath: "/base" }, schema: { generation: "1", collections: [], types }, endpoints: [], capabilities: [], registeredReads: [], dependencyTemplates: [], vectorIndexes: [], errors: [] }; return { ...base, digest: structuralDigest(base) }; };
  assert.throws(() => validate(snapshot([{ id: "x", node: { kind: "unknown" } }])), /typeInvalid/);
  assert.throws(() => validate(snapshot([{ id: "x", node: { kind: "string", format: "plain", minLength: 2, maxLength: 1 } }])), /typeInvalid/);
  assert.throws(() => validate(snapshot([{ id: "x", node: { kind: "enum", values: ["a", "a"] } }])), /typeInvalid/);
  assert.throws(() => validate(snapshot([{ id: "tag", node: { kind: "literal", value: "a" } }, { id: "variant", node: { kind: "object", additionalProperties: false, properties: [{ name: "wrong", typeId: "tag", required: true, nullable: false, redactionOptional: false }] } }, { id: "union", node: { kind: "union", discriminator: "kind", variants: [{ tag: "a", typeId: "variant" }, { tag: "b", typeId: "variant" }] } }])), /typeInvalid/);
  assert.throws(() => validate(snapshot([{ id: "x", node: { kind: "object", additionalProperties: false, properties: [{ name: "a", typeId: "x", required: true, nullable: false, redactionOptional: false }, { name: "a", typeId: "x", required: true, nullable: false, redactionOptional: false }] } }])), /typeInvalid/);
});

test("snapshot parsing rejects duplicate properties and lone surrogates", () => {
  assert.throws(() => parseSnapshot('{"protocol":{},"protocol":{}}'), /snapshotInvalid/);
  assert.throws(() => parseSnapshot('{"value":"\\ud800"}'), /snapshotInvalid/);
});
