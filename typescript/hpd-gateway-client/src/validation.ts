import { gatewayRuntimeSchemaConstraints, gatewayRuntimeSchemas } from "./generated/runtime.js";

type Schema = Readonly<Record<string, unknown>>;
const prefix = "#/components/schemas/";
const schemas = gatewayRuntimeSchemas as unknown as Readonly<Record<string, Schema>>;
const encoder = new TextEncoder();

export function validateWireValue(reference: string, value: unknown): boolean {
  const seen = new Set<string>();
  return validate(resolve(reference), value, reference, seen) && validateConstraints(reference, value);
}

function resolve(reference: string): Schema {
  if (!reference.startsWith(prefix)) return {};
  return schemas[reference.slice(prefix.length)] ?? {};
}

function validate(schema: Schema, value: unknown, root: string, seen: Set<string>): boolean {
  if (typeof schema.$ref === "string") {
    if (seen.has(schema.$ref)) return false;
    const next = new Set(seen); next.add(schema.$ref);
    return validate(resolve(schema.$ref), value, schema.$ref, next) && validateConstraints(schema.$ref, value);
  }
  const types = Array.isArray(schema.type) ? schema.type : schema.type === undefined ? [] : [schema.type];
  if (value === null) return types.includes("null");
  const type = types.find(item => item !== "null");
  if (Array.isArray(schema.enum) && !schema.enum.some(item => Object.is(item, value))) return false;
  if (schema.const !== undefined && !Object.is(schema.const, value)) return false;
  if (Array.isArray(schema.oneOf)) {
    return schema.oneOf.filter(item => validate(asSchema(item), value, root, new Set(seen))).length === 1;
  }
  if (type === "string") return typeof value === "string" && stringShape(schema, value);
  if (type === "integer") return typeof value === "number" && Number.isSafeInteger(value) && numericShape(schema, value);
  if (type === "number") return typeof value === "number" && Number.isFinite(value) && numericShape(schema, value);
  if (type === "boolean") return typeof value === "boolean";
  if (type === "array") {
    if (!Array.isArray(value)) return false;
    if (typeof schema.minItems === "number" && value.length < schema.minItems) return false;
    if (typeof schema.maxItems === "number" && value.length > schema.maxItems) return false;
    if (schema.uniqueItems === true && new Set(value.map(item => JSON.stringify(item))).size !== value.length) return false;
    return value.every(item => validate(asSchema(schema.items), item, root, new Set(seen)));
  }
  if (type === "object" || schema.properties !== undefined) {
    if (!isRecord(value)) return false;
    const properties = isRecord(schema.properties) ? schema.properties : {};
    const required = new Set(Array.isArray(schema.required) ? schema.required.filter(item => typeof item === "string") : []);
    if ([...required].some(key => !(key in value))) return false;
    return Object.entries(value).every(([key, child]) => {
      if (key in properties) return validate(asSchema(properties[key]), child, root, new Set(seen));
      return isRecord(schema.additionalProperties) && validate(schema.additionalProperties, child, root, new Set(seen));
    });
  }
  return schema.const !== undefined || schema.enum !== undefined;
}

function validateConstraints(reference: string, value: unknown): boolean {
  const constraints = gatewayRuntimeSchemaConstraints.filter(item => item.schemaRef === reference);
  for (const constraint of constraints) {
    const instancePath = instancePathFromSchemaPointer(constraint.propertyPointer);
    if (instancePath === null) return false;
    const target = pointer(value, instancePath);
    if (!target.found) continue;
    if (constraint.appliesTo === "collection") {
      if (!Array.isArray(target.value)) return false;
      const minimum = constraint.rules.collectionMinimum;
      const maximum = constraint.rules.collectionMaximum;
      if (minimum !== null && target.value.length < minimum || maximum !== null && target.value.length > maximum) return false;
      if (constraint.rules.uniqueness === "ordinal" && new Set(target.value).size !== target.value.length) return false;
      if (constraint.rules.ordering === "ordinal-ascending" && !ascending(target.value)) return false;
      continue;
    }
    if (constraint.appliesTo === "items" && !Array.isArray(target.value)) return false;
    const targets = constraint.appliesTo === "items" ? target.value as readonly unknown[] : [target.value];
    if (!targets.every(item => typeof item !== "string" || constrainedString(item, constraint.rules))) return false;
  }
  return true;
}

function constrainedString(value: string, rules: (typeof gatewayRuntimeSchemaConstraints)[number]["rules"]): boolean {
  const size = encoder.encode(value).byteLength;
  if (rules.minimumUtf8Bytes !== null && size < rules.minimumUtf8Bytes) return false;
  if (rules.maximumUtf8Bytes !== null && size > rules.maximumUtf8Bytes) return false;
  if (rules.normalization === "NFC" && value !== value.normalize("NFC")) return false;
  if (rules.rejectUnicodeControls && /[\u0000-\u001F\u007F-\u009F]/u.test(value)) return false;
  if (rules.characterSet === "visible-ascii" && !/^[!-~]*$/u.test(value)) return false;
  return wellFormed(value);
}

function stringShape(schema: Schema, value: string): boolean {
  if (!wellFormed(value)) return false;
  if (typeof schema.minLength === "number" && [...value].length < schema.minLength) return false;
  if (typeof schema.maxLength === "number" && [...value].length > schema.maxLength) return false;
  if (typeof schema.pattern === "string" && !new RegExp(schema.pattern, "u").test(value)) return false;
  if (schema.format === "uuid" && !/^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/iu.test(value)) return false;
  if (schema.format === "date-time" && !Number.isFinite(Date.parse(value))) return false;
  if (schema.format === "uri") { try { new URL(value); } catch { return false; } }
  if (schema.format === "int64" && (!/^-?(?:0|[1-9][0-9]{0,18})$/u.test(value) || !integerRange(value, -9223372036854775808n, 9223372036854775807n))) return false;
  if (schema.format === "uint64" && (!/^(?:0|[1-9][0-9]{0,19})$/u.test(value) || !integerRange(value, 0n, 18446744073709551615n))) return false;
  return true;
}
function numericShape(schema: Schema, value: number): boolean {
  const minimum = typeof schema.minimum === "string" ? Number(schema.minimum) : schema.minimum;
  const maximum = typeof schema.maximum === "string" ? Number(schema.maximum) : schema.maximum;
  if (schema.format === "uint16" && (!Number.isInteger(value) || value < 0 || value > 65_535)) return false;
  if (schema.format === "int32" && (!Number.isInteger(value) || value < -2_147_483_648 || value > 2_147_483_647)) return false;
  return !(typeof minimum === "number" && value < minimum) && !(typeof maximum === "number" && value > maximum);
}
function integerRange(value: string, minimum: bigint, maximum: bigint): boolean { try { const number = BigInt(value); return number >= minimum && number <= maximum; } catch { return false; } }
function instancePathFromSchemaPointer(path: string): string[] | null {
  const parts = path.split("/").slice(1).map(unescapePointerPart);
  const result: string[] = [];
  for (let index = 0; index < parts.length;) {
    if (parts[index] !== "properties" || index + 1 >= parts.length) return null;
    result.push(parts[index + 1]!);
    index += 2;
  }
  return result;
}
function pointer(root: unknown, path: readonly string[]): { found: boolean; value?: unknown } {
  let value = root;
  for (const part of path) {
    if (!isRecord(value) || !Object.prototype.hasOwnProperty.call(value, part)) return { found: false };
    value = value[part];
  }
  return { found: true, value };
}
function unescapePointerPart(value: string): string { return value.replaceAll("~1", "/").replaceAll("~0", "~"); }
function ascending(values: readonly unknown[]): boolean { return values.every((value, index) => index === 0 || String(values[index - 1]) < String(value)); }
function wellFormed(value: string): boolean {
  for (let i = 0; i < value.length; i++) {
    const code = value.charCodeAt(i);
    if (code >= 0xD800 && code <= 0xDBFF) { const next = value.charCodeAt(++i); if (!(next >= 0xDC00 && next <= 0xDFFF)) return false; }
    else if (code >= 0xDC00 && code <= 0xDFFF) return false;
  }
  return true;
}
function isRecord(value: unknown): value is Record<string, unknown> { return value !== null && typeof value === "object" && !Array.isArray(value); }
function asSchema(value: unknown): Schema { return isRecord(value) ? value : {}; }
