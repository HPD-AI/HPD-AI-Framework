import { readFile } from "node:fs/promises";
import { canonicalJson, framedHash, hex } from "./canonical.js";
import type { GatewayClientGenerationManifest, GatewayClientGenerationSnapshot, GatewayClientOperation, GatewayConstraintRules, GatewayParameterConstraint, GatewaySchemaConstraint, JsonValue } from "./types.js";

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
  validateOpenApi(openApi, manifest);
  const openApiBytes = canonicalJson(openApi);
  const manifestBytes = canonicalJson(manifest as unknown as JsonValue);
  const openApiDigest = framedHash("HPD.Gateway.OpenApi.v1\0", openApiBytes);
  const manifestDigest = framedHash("HPD.Gateway.ClientManifest.v1\0", manifestBytes);
  if (hex(openApiDigest) !== openApiSha256) fail("OpenAPI payload hash mismatch.");
  if (hex(manifestDigest) !== manifestSha256) fail("Manifest payload hash mismatch.");
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
  const operations = value.operations.map((operation) => {
    const item = validateOperation(operation);
    if (item.operation <= prior) fail("Operations are not unique canonical ordinal entries.");
    prior = item.operation;
    return item;
  });
  let priorConstraint = "";
  const schemaConstraints = value.schemaConstraints.map((constraint) => {
    const item = validateSchemaConstraint(constraint);
    const key = `${item.schemaRef}\0${item.propertyPointer}\0${item.appliesTo}`;
    if (key <= priorConstraint) fail("Schema constraints are not unique canonical ordinal entries.");
    priorConstraint = key;
    return item;
  });
  return {
    schemaVersion: 1, apiVersion: "1.0.0", openApiDocumentName: "hpd-gateway-v1",
    securityScheme: value.securityScheme, operations, schemaConstraints,
  } as GatewayClientGenerationManifest;
}

function validateOperation(input: unknown): GatewayClientOperation {
  const value = object(input, "manifest operation");
  exact(value, ["operation", "openApiOperationId", "method", "path", "capability", "resourcePolicy", "resourceKind", "mutation", "idempotency", "desiredPrecondition", "protectedNotFound", "success", "documentedErrors", "requestBody", "pagination", "parameterConstraints"]);
  const operation = boundedString(value.operation, "operation", 128);
  const openApiOperationId = boundedString(value.openApiOperationId, "openApiOperationId", 256);
  const method = one(value.method, ["GET", "POST"] as const, "method");
  const path = boundedString(value.path, "path", 1024);
  const capability = boundedString(value.capability, "capability", 128);
  const resourcePolicy = value.resourcePolicy === null ? null : boundedString(value.resourcePolicy, "resourcePolicy", 128);
  const resourceKind = one(value.resourceKind, ["none", "namespace", "target", "administration"] as const, "resourceKind");
  if (typeof value.mutation !== "boolean" || typeof value.protectedNotFound !== "boolean") fail("Invalid operation booleans.");
  const idempotency = one(value.idempotency, ["required", "forbidden"] as const, "idempotency");
  const desiredPrecondition = one(value.desiredPrecondition, ["create-or-replace", "forbidden"] as const, "desiredPrecondition");
  const successValue = object(value.success, "success");
  exact(successValue, ["status", "schemaRef", "meaning"]);
  const successStatus = one(successValue.status, [200, 201, 202] as const, "success status");
  const success = { status: successStatus, schemaRef: localRef(successValue.schemaRef), meaning: one(successValue.meaning, ["completed-read", "created", "accepted-not-active"] as const, "success meaning") };
  const documentedErrors = integerArray(value.documentedErrors, "documentedErrors", 32, 400, 599);
  requireAscending(documentedErrors, "documentedErrors");
  const bodyValue = object(value.requestBody, "requestBody");
  exact(bodyValue, ["presence", "schemaRef", "mediaTypes"]);
  const presence = one(bodyValue.presence, ["none", "required", "optional"] as const, "body presence");
  const schemaRef = bodyValue.schemaRef === null ? null : localRef(bodyValue.schemaRef);
  const mediaTypes = stringArray(bodyValue.mediaTypes, "mediaTypes", 2);
  requireAscending(mediaTypes, "mediaTypes");
  if ((presence === "none") !== (schemaRef === null) || (presence === "none") !== (mediaTypes.length === 0)) fail("Request-body identity is inconsistent.");
  const pageValue = object(value.pagination, "pagination");
  exact(pageValue, ["kind", "defaultMaximum", "minimumMaximum", "maximumMaximum"]);
  const kind = one(pageValue.kind, ["none", "opaque-cursor"] as const, "pagination kind");
  const pagination = { kind, defaultMaximum: nullableInteger(pageValue.defaultMaximum), minimumMaximum: nullableInteger(pageValue.minimumMaximum), maximumMaximum: nullableInteger(pageValue.maximumMaximum) };
  if (kind === "none" ? Object.values(pagination).slice(1).some(v => v !== null) : !(pagination.minimumMaximum === 1 && pagination.defaultMaximum === 64 && pagination.maximumMaximum === 256)) fail("Invalid pagination specification.");
  if (!Array.isArray(value.parameterConstraints) || value.parameterConstraints.length > 32) fail("Invalid parameter constraints.");
  const parameterConstraints = value.parameterConstraints.map(validateParameterConstraint);
  requireAscending(parameterConstraints.map(parameterKey), "parameterConstraints");
  if (value.mutation !== (idempotency === "required")) fail("Mutation/idempotency semantics disagree.");
  if (success.status === 202 !== (success.meaning === "accepted-not-active")) fail("202 meaning is inconsistent.");
  return { operation, openApiOperationId, method, path, capability, resourcePolicy, resourceKind, mutation: value.mutation, idempotency, desiredPrecondition, protectedNotFound: value.protectedNotFound, success, documentedErrors, requestBody: { presence, schemaRef, mediaTypes }, pagination, parameterConstraints };
}

function validateParameterConstraint(input: unknown): GatewayParameterConstraint {
  const value = object(input, "parameter constraint");
  exact(value, ["location", "name", "required", "brand", "rules"]);
  if (typeof value.required !== "boolean") fail("Invalid parameter required flag.");
  return { location: one(value.location, ["path", "query", "header"] as const, "parameter location"), name: boundedString(value.name, "parameter name", 128), required: value.required, brand: brand(value.brand), rules: rules(value.rules) };
}

function validateSchemaConstraint(input: unknown): GatewaySchemaConstraint {
  const value = object(input, "schema constraint");
  exact(value, ["schemaRef", "propertyPointer", "appliesTo", "brand", "rules"]);
  const appliesTo = one(value.appliesTo, ["value", "collection", "items"] as const, "constraint target");
  const identity = brand(value.brand);
  if (appliesTo === "collection" && identity !== "none") fail("Collection constraints cannot carry a brand.");
  return { schemaRef: localRef(value.schemaRef), propertyPointer: pointer(value.propertyPointer), appliesTo, brand: identity, rules: rules(value.rules) };
}

function rules(input: unknown): GatewayConstraintRules {
  const value = object(input, "constraint rules");
  exact(value, ["minimumUtf8Bytes", "maximumUtf8Bytes", "normalization", "characterSet", "rejectUnicodeControls", "collectionMinimum", "collectionMaximum", "uniqueness", "ordering", "cardinality"]);
  if (typeof value.rejectUnicodeControls !== "boolean") fail("Invalid control-character rule.");
  const result: GatewayConstraintRules = {
    minimumUtf8Bytes: nullableBound(value.minimumUtf8Bytes, 16 * 1024 * 1024), maximumUtf8Bytes: nullableBound(value.maximumUtf8Bytes, 16 * 1024 * 1024),
    normalization: one(value.normalization, ["none", "NFC"] as const, "normalization"), characterSet: one(value.characterSet, ["unicode", "visible-ascii", "lowercase-ascii-name", "ascii-artifact-label", "strong-entity-tag"] as const, "characterSet"),
    rejectUnicodeControls: value.rejectUnicodeControls, collectionMinimum: nullableBound(value.collectionMinimum, 10_000), collectionMaximum: nullableBound(value.collectionMaximum, 10_000),
    uniqueness: one(value.uniqueness, ["none", "ordinal", "ordinal-ignore-case"] as const, "uniqueness"), ordering: one(value.ordering, ["none", "ordinal-ascending", "numeric-ascending"] as const, "ordering"), cardinality: one(value.cardinality, ["single", "multiple"] as const, "cardinality"),
  };
  if (result.minimumUtf8Bytes !== null && result.maximumUtf8Bytes !== null && result.minimumUtf8Bytes > result.maximumUtf8Bytes) fail("Invalid byte range.");
  if (result.collectionMinimum !== null && result.collectionMaximum !== null && result.collectionMinimum > result.collectionMaximum) fail("Invalid collection range.");
  return result;
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

function only(value: Record<string, unknown>, fields: readonly string[]): void {
  const allowed = new Set(fields);
  if (Object.keys(value).some(field => !allowed.has(field))) fail("Object contains unknown members.");
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

function validateOpenApi(openApi: Readonly<Record<string, JsonValue>>, manifest: GatewayClientGenerationManifest): void {
  exact(openApi as Record<string, unknown>, ["openapi", "info", "paths", "components"]);
  if (typeof openApi.openapi !== "string" || !openApi.openapi.startsWith("3.1.")) fail("OpenAPI must be 3.1.x.");
  const components = object(openApi.components, "components");
  exact(components, ["schemas", "securitySchemes"]);
  const schemas = object(components.schemas, "schemas");
  validateSchemas(schemas);
  const securitySchemes = object(components.securitySchemes, "securitySchemes");
  if (Object.keys(securitySchemes).length !== 1) fail("Exactly one security scheme is required.");
  const security = object(securitySchemes[manifest.securityScheme], "security scheme");
  exact(security, ["type", "scheme", "bearerFormat"]);
  if (security.type !== "http" || security.scheme !== "bearer" || security.bearerFormat !== "JWT") fail("Invalid bearer security scheme.");
  const paths = object(openApi.paths, "paths");
  const observed = new Set<string>();
  for (const operation of manifest.operations) {
    const path = object(paths[operation.path], `path ${operation.path}`);
    const wire = object(path[operation.method.toLowerCase()], `operation ${operation.operation}`);
    exact(wire, ["operationId", "parameters", ...(operation.requestBody.presence === "none" ? [] : ["requestBody"]), "responses", "security"]);
    if (wire.operationId !== operation.openApiOperationId) fail(`Operation ID drift for ${operation.operation}.`);
    const key = `${operation.method} ${operation.path}`;
    if (!observed.add(key)) fail("Duplicate method/path operation.");
    const securityRequirements = array(wire.security, "operation security");
    if (securityRequirements.length !== 1) fail("Every operation requires exactly one security requirement.");
    const securityRequirement = object(securityRequirements[0], "security requirement");
    exact(securityRequirement, [manifest.securityScheme]);
    if (!Array.isArray(securityRequirement[manifest.securityScheme]) || (securityRequirement[manifest.securityScheme] as unknown[]).length !== 0)
      fail("Gateway bearer security scopes must be empty.");
    const parameters = array(wire.parameters, "parameters");
    const wireParameters = parameters.map(value => {
      const parameter = object(value, "parameter");
      exact(parameter, ["name", "in", "description", "schema", ...(parameter.required === undefined ? [] : ["required"])]);
      if (typeof parameter.name !== "string" || !["path", "query", "header"].includes(String(parameter.in))) fail("Invalid OpenAPI parameter identity.");
      if (parameter.description !== undefined && typeof parameter.description !== "string") fail("Invalid parameter description.");
      return `${String(parameter.in)}\0${String(parameter.name)}`;
    }).sort();
    const expectedParameters = operation.parameterConstraints.map(value => `${value.location}\0${value.name}`).sort();
    if (!equal(wireParameters, expectedParameters)) fail(`Parameter drift for ${operation.operation}.`);
    for (const constraint of operation.parameterConstraints) {
      const parameter = parameters.map(value => object(value, "parameter")).find(value => value.in === constraint.location && value.name === constraint.name)!;
      if ((parameter.required === true) !== constraint.required) fail(`Parameter required drift for ${operation.operation}.`);
      const parameterSchema = object(parameter.schema, "parameter schema");
      if (constraint.location === "query" && constraint.name === "maximum") {
        if (parameterSchema.type !== "integer" || parameterSchema.minimum !== operation.pagination.minimumMaximum ||
            parameterSchema.maximum !== operation.pagination.maximumMaximum || parameterSchema.default !== operation.pagination.defaultMaximum)
          fail("Pagination maximum constraint drift.");
      } else correlateConstraintSchema(parameterSchema, constraint.rules, constraint.location === "header" && constraint.name === "If-Match");
    }
    const responses = object(wire.responses, "responses");
    const statuses = Object.keys(responses).sort();
    const expectedStatuses = [String(operation.success.status), ...operation.documentedErrors.map(String)].sort();
    if (!equal(statuses, expectedStatuses)) fail(`Response drift for ${operation.operation}.`);
    for (const [status, responseValue] of Object.entries(responses)) {
      const response = object(responseValue, "response");
      exact(response, ["description", "content"]);
      if (typeof response.description !== "string") fail("Invalid response description.");
      const content = object(response.content, "response content");
      exact(content, ["application/json"]);
      const expected = status === String(operation.success.status) ? operation.success.schemaRef :
        "#/components/schemas/HPD_Gateway_Admin_GatewayAdminError";
      requireSchemaRef(object(content["application/json"], "response media").schema, expected, schemas);
    }
    if (operation.requestBody.presence !== "none") {
      const body = object(wire.requestBody, "requestBody");
      const required = body.required === true;
      if (required !== (operation.requestBody.presence === "required")) fail("Request-body presence drift.");
      const content = object(body.content, "request content");
      if (!equal(Object.keys(content).sort(), [...operation.requestBody.mediaTypes].sort())) fail("Request media-type drift.");
      requireSchemaRef(object(content["application/json"], "JSON media").schema, operation.requestBody.schemaRef!, schemas);
    }
  }
  let operationCount = 0;
  for (const value of Object.values(paths)) operationCount += Object.keys(object(value, "path")).filter(key => key === "get" || key === "post").length;
  if (operationCount !== manifest.operations.length) fail("OpenAPI contains additional operations.");
  for (const constraint of manifest.schemaConstraints) {
    const schema = object(schemas[constraint.schemaRef.slice("#/components/schemas/".length)], "constrained schema");
    const property = object(resolvePointer(schema, constraint.propertyPointer), "constrained property");
    if (constraint.appliesTo === "collection") {
      if (property.type !== "array" || property.minItems !== constraint.rules.collectionMinimum ||
          property.maxItems !== constraint.rules.collectionMaximum) fail("Schema collection constraint drift.");
    } else {
      const target = constraint.appliesTo === "items" ? object(property.items, "constrained items") : property;
      if (constraint.brand !== "none" || constraint.rules.maximumUtf8Bytes !== null)
        correlateConstraintSchema(target, constraint.rules, false);
    }
  }
}

function correlateConstraintSchema(schema: Record<string, unknown>, rules: GatewayConstraintRules, framedEntityTag: boolean): void {
  if (schema.type !== "string") fail("Constrained parameter must be a string.");
  const minimum = framedEntityTag && rules.minimumUtf8Bytes !== null ? rules.minimumUtf8Bytes + 2 :
    rules.characterSet === "unicode" && (rules.minimumUtf8Bytes ?? 0) > 0 ? 1 : rules.minimumUtf8Bytes;
  const maximum = framedEntityTag && rules.maximumUtf8Bytes !== null ? rules.maximumUtf8Bytes + 2 : rules.maximumUtf8Bytes;
  if (minimum !== null && schema.minLength !== minimum) fail("Parameter minimum constraint drift.");
  if (maximum !== null && schema.maxLength !== maximum) fail("Parameter maximum constraint drift.");
  if (rules.characterSet !== "unicode" || rules.rejectUnicodeControls)
    if (typeof schema.pattern !== "string" || schema.pattern.length === 0) fail("Parameter character-set constraint drift.");
}

const schemaFields = ["$ref", "type", "title", "format", "description", "enum", "const", "oneOf", "discriminator", "properties", "required", "additionalProperties", "items", "minItems", "maxItems", "uniqueItems", "minLength", "maxLength", "pattern", "minimum", "maximum", "default"] as const;
const schemaTypes = ["string", "integer", "number", "boolean", "object", "array", "null"] as const;
const schemaFormats = ["uri", "uuid", "uint16", "int32", "int64", "uint64", "date-time"] as const;

function validateSchemas(schemas: Record<string, unknown>): void {
  const names = Object.keys(schemas);
  if (names.length === 0 || names.length > 10_000) fail("Invalid schema component collection.");
  for (const name of names) {
    boundedString(name, "schema component name", 512);
    validateSchema(schemas[name], schemas, `#/components/schemas/${name}`);
  }
  const edges = new Map<string, Set<string>>();
  for (const name of names) {
    const found = new Set<string>();
    collectSchemaReferences(schemas[name], found);
    edges.set(name, found);
  }
  const active = new Set<string>();
  const complete = new Set<string>();
  const visit = (name: string): void => {
    if (active.has(name)) fail(`Cyclic schema reference '${name}'.`);
    if (complete.has(name)) return;
    active.add(name);
    for (const target of edges.get(name) ?? []) visit(target);
    active.delete(name);
    complete.add(name);
  };
  for (const name of names) visit(name);
}

function validateSchema(input: unknown, schemas: Record<string, unknown>, path: string): void {
  const schema = object(input, `schema ${path}`);
  if (Object.keys(schema).length === 0) fail(`Empty schema '${path}'.`);
  only(schema, schemaFields);
  if (schema.$ref !== undefined) {
    const reference = localRef(schema.$ref);
    if (schemas[reference.slice("#/components/schemas/".length)] === undefined) fail(`Unresolved reference '${reference}'.`);
    if (Object.keys(schema).length !== 1) fail("Schema references cannot have siblings.");
    return;
  }
  const types = Array.isArray(schema.type) ? schema.type.map(value => one(value, schemaTypes, "schema type")) :
    schema.type === undefined ? [] : [one(schema.type, schemaTypes, "schema type")];
  const nonNullTypes = types.filter(value => value !== "null");
  if (new Set(types).size !== types.length || nonNullTypes.length > 1) fail("Invalid schema type union.");
  if (schema.format !== undefined) one(schema.format, schemaFormats, "schema format");
  if (schema.title !== undefined) boundedString(schema.title, "schema title", 16_384);
  if (schema.description !== undefined) boundedString(schema.description, "schema description", 16_384);
  if (schema.pattern !== undefined) boundedString(schema.pattern, "schema pattern", 16_384);
  for (const field of ["minItems", "maxItems"] as const)
    if (schema[field] !== undefined && (!Number.isSafeInteger(schema[field]) || (schema[field] as number) < 0 || (schema[field] as number) > 10_000)) fail(`Invalid ${field}.`);
  for (const field of ["minLength", "maxLength"] as const)
    if (schema[field] !== undefined && (!Number.isSafeInteger(schema[field]) || (schema[field] as number) < 0 || (schema[field] as number) > 4_194_304)) fail(`Invalid ${field}.`);
  for (const field of ["minimum", "maximum"] as const)
    if (schema[field] !== undefined && typeof schema[field] !== "string") fail(`Invalid ${field}.`);
  if (schema.enum !== undefined && (!Array.isArray(schema.enum) || schema.enum.length === 0 || schema.enum.length > 10_000)) fail("Invalid schema enum.");
  if (schema.required !== undefined) {
    const required = stringArray(schema.required, "required", 10_000);
    if (new Set(required).size !== required.length) fail("Duplicate required property.");
  }
  if (schema.uniqueItems !== undefined && typeof schema.uniqueItems !== "boolean") fail("Invalid uniqueItems.");
  if (schema.additionalProperties === true) fail("Free-form additional properties are unsupported.");
  if (schema.additionalProperties !== undefined && typeof schema.additionalProperties !== "boolean")
    validateSchema(schema.additionalProperties, schemas, `${path}/additionalProperties`);
  if (schema.items !== undefined) validateSchema(schema.items, schemas, `${path}/items`);
  if (schema.properties !== undefined) {
    const properties = object(schema.properties, "schema properties");
    if (Object.keys(properties).length > 10_000) fail("Schema property bound exceeded.");
    for (const [name, value] of Object.entries(properties)) {
      boundedString(name, "schema property name", 512);
      validateSchema(value, schemas, `${path}/properties/${name}`);
    }
  }
  if (schema.oneOf !== undefined) {
    const alternatives = array(schema.oneOf, "oneOf");
    if (alternatives.length < 2 || alternatives.length > 256) fail("Invalid oneOf alternatives.");
    for (let index = 0; index < alternatives.length; index++) validateSchema(alternatives[index], schemas, `${path}/oneOf/${index}`);
  }
  if (schema.discriminator !== undefined) {
    if (schema.oneOf === undefined) fail("A discriminator requires oneOf.");
    const discriminator = object(schema.discriminator, "schema discriminator");
    exact(discriminator, ["propertyName", "mapping"]);
    boundedString(discriminator.propertyName, "discriminator property", 512);
    const mapping = object(discriminator.mapping, "discriminator mapping");
    if (Object.keys(mapping).length < 2 || Object.keys(mapping).length > 256) fail("Invalid discriminator mapping.");
    for (const [key, value] of Object.entries(mapping)) {
      boundedString(key, "discriminator value", 512);
      const reference = localRef(value);
      if (schemas[reference.slice("#/components/schemas/".length)] === undefined) fail(`Unresolved discriminator reference '${reference}'.`);
    }
    const observed = new Set<string>();
    for (const branchValue of array(schema.oneOf, "oneOf")) {
      const branch = object(branchValue, "discriminator branch");
      const reference = localRef(branch.$ref);
      const target = object(schemas[reference.slice("#/components/schemas/".length)], "discriminator target");
      const required = stringArray(target.required, "discriminator required", 10_000);
      if (!required.includes(discriminator.propertyName as string)) fail("Discriminator property must be required in every branch.");
      const tagSchema = object(object(target.properties, "discriminator properties")[discriminator.propertyName as string], "discriminator tag");
      const tag = typeof tagSchema.const === "string" ? tagSchema.const :
        Array.isArray(tagSchema.enum) && tagSchema.enum.length === 1 && typeof tagSchema.enum[0] === "string" ? tagSchema.enum[0] : null;
      if (tag === null || observed.has(tag) || mapping[tag] !== reference) fail("Discriminator branch correlation drift.");
      observed.add(tag);
    }
    if (observed.size !== Object.keys(mapping).length) fail("Discriminator mapping contains additional entries.");
  }
  if (types.length === 0 && schema.oneOf === undefined && schema.const === undefined && schema.enum === undefined && schema.properties === undefined)
    fail(`Schema '${path}' has no supported shape.`);
}

function collectSchemaReferences(input: unknown, output: Set<string>): void {
  const schema = object(input, "schema reference graph");
  if (typeof schema.$ref === "string") { output.add(schema.$ref.slice("#/components/schemas/".length)); return; }
  if (schema.items !== undefined) collectSchemaReferences(schema.items, output);
  if (schema.additionalProperties !== undefined && typeof schema.additionalProperties !== "boolean") collectSchemaReferences(schema.additionalProperties, output);
  if (schema.properties !== undefined) for (const value of Object.values(object(schema.properties, "schema properties"))) collectSchemaReferences(value, output);
  if (schema.oneOf !== undefined) for (const value of array(schema.oneOf, "oneOf")) collectSchemaReferences(value, output);
}

function responseSchema(value: unknown): unknown {
  const response = object(value, "response");
  return object(object(response.content, "response content")["application/json"], "response JSON media").schema;
}
function requireSchemaRef(value: unknown, expected: string, schemas: Record<string, unknown>): void {
  const schema = object(value, "schema reference");
  exact(schema, ["$ref"]);
  if (schema.$ref !== expected || schemas[expected.slice("#/components/schemas/".length)] === undefined) fail("Schema reference drift.");
}
function resolvePointer(root: Record<string, unknown>, pointerValue: string): unknown {
  let current: unknown = root;
  for (const segment of pointerValue.split("/").slice(1)) current = object(current, "pointer segment")[segment.replaceAll("~1", "/").replaceAll("~0", "~")];
  if (current === undefined) fail(`Unresolved schema pointer '${pointerValue}'.`);
  return current;
}
function pointer(value: unknown): string { const text = boundedString(value, "propertyPointer", 1024); if (!text.startsWith("/properties/")) fail("Invalid property pointer."); return text; }
function localRef(value: unknown): string { const text = boundedString(value, "schemaRef", 512); if (!text.startsWith("#/components/schemas/") || text.length === 21) fail("Invalid local schema reference."); return text; }
function brand(value: unknown): GatewayParameterConstraint["brand"] { return one(value, ["none", "namespace-id", "target-node-id", "revision-id", "validation-id", "operation-id", "candidate-id", "continuation-token", "desired-state-token", "idempotency-key", "correlation-id"] as const, "brand"); }
function parameterKey(value: GatewayParameterConstraint): string { const order = value.location === "path" ? "0" : value.location === "query" ? "1" : "2"; return `${order}\0${value.name}`; }
function boundedString(value: unknown, name: string, maximum: number): string { if (typeof value !== "string" || utf8(value) < 1 || utf8(value) > maximum || value !== value.normalize("NFC")) fail(`Invalid ${name}.`); return value; }
function one<const T extends readonly (string | number)[]>(value: unknown, values: T, name: string): T[number] { if (!values.includes(value as never)) fail(`Invalid ${name}.`); return value as T[number]; }
function nullableInteger(value: unknown): number | null { if (value === null) return null; if (!Number.isSafeInteger(value)) fail("Expected nullable integer."); return value as number; }
function nullableBound(value: unknown, maximum: number): number | null { const result = nullableInteger(value); if (result !== null && (result < 0 || result > maximum)) fail("Numeric bound is outside its range."); return result; }
function integerArray(value: unknown, name: string, maximumItems: number, minimum: number, maximum: number): readonly number[] { const values = array(value, name); if (values.length > maximumItems || values.some(item => !Number.isInteger(item) || (item as number) < minimum || (item as number) > maximum)) fail(`Invalid ${name}.`); return values as number[]; }
function stringArray(value: unknown, name: string, maximumItems: number): readonly string[] { const values = array(value, name); if (values.length > maximumItems || values.some(item => typeof item !== "string")) fail(`Invalid ${name}.`); return values as string[]; }
function array(value: unknown, name: string): unknown[] { if (!Array.isArray(value) || value.length > 10_000) fail(`${name} must be a bounded array.`); return value; }
function requireAscending<T extends string | number>(values: readonly T[], name: string): void { for (let index = 1; index < values.length; index++) if (values[index - 1]! >= values[index]!) fail(`${name} is not strictly ascending.`); }
function equal<T>(left: readonly T[], right: readonly T[]): boolean { return left.length === right.length && left.every((value, index) => value === right[index]); }
