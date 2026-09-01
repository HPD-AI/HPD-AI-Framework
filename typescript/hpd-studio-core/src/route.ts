export type StudioRouteCodecId = 'boundedId' | 'positiveLong' | 'nonnegativeLong' |
  'sha256' | 'resource' | 'cursor' | 'enum' | 'tab';

export type StudioRouteSegment =
  | Readonly<{ readonly kind: 'literal'; readonly value: string }>
  | Readonly<{ readonly kind: 'parameter'; readonly name: string; readonly codec: StudioRouteCodecId; readonly allowed?: readonly string[] }>;

export interface StudioQueryParameterDefinition {
  readonly name: string;
  readonly codec: StudioRouteCodecId;
  readonly required: boolean;
  readonly allowed?: readonly string[];
}

export interface StudioRouteDefinition {
  readonly id: string;
  readonly segments: readonly StudioRouteSegment[];
  readonly query: readonly StudioQueryParameterDefinition[];
}

export interface StudioRouteMatch {
  readonly routeId: string;
  readonly parameters: Readonly<Record<string, string>>;
  readonly query: Readonly<Record<string, string>>;
  readonly canonicalUrl: string;
}

const NAME = /^[a-z][a-zA-Z0-9.]{0,127}$/u;
const BOUNDED_ID = /^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$/u;
const BASE64URL = /^[A-Za-z0-9_-]{1,684}$/u;
const SHA256 = /^[a-f0-9]{64}$/u;
const MAXIMUM_URL_BYTES = 512;

/** Validates and deeply owns one immutable route definition. */
export function defineStudioRoute(definition: StudioRouteDefinition): StudioRouteDefinition {
  if (!definition || !NAME.test(definition.id) || !Array.isArray(definition.segments) ||
      definition.segments.length > 8 ||
      !Array.isArray(definition.query) || definition.query.length > 16) {
    throw new TypeError('Studio route definition is invalid.');
  }
  const names = new Set<string>();
  const segments = definition.segments.map((segment) => {
    if (segment.kind === 'literal') {
      if (!BOUNDED_ID.test(segment.value)) throw new TypeError('Studio route literal is invalid.');
      return Object.freeze({ kind: 'literal', value: segment.value }) as StudioRouteSegment;
    }
    validateParameter(segment.name, segment.codec, segment.allowed, names);
    return freezeParameter(segment);
  });
  const query = definition.query.map((parameter) => {
    validateParameter(parameter.name, parameter.codec, parameter.allowed, names);
    return Object.freeze({ ...freezeParameter(parameter), required: parameter.required });
  });
  if (!isCanonical(query.map(item => item.name))) throw new TypeError('Studio route query members are not canonical.');
  return deepFreeze({ id: definition.id, segments, query });
}

/** Computes the exact .NET-compatible route registration checksum. */
export function studioRouteChecksum(definition: StudioRouteDefinition): string {
  const route = defineStudioRoute(definition);
  const segments = route.segments.map(segment => studioCanonicalHash('base.studio.route-segment.v1', writer => {
    writer.discriminator(segment.kind === 'literal' ? 1 : 2);
    writer.string(segment.kind === 'literal' ? segment.value : segment.name);
    writer.boolean(segment.kind === 'parameter');
    if (segment.kind === 'parameter') writer.discriminator(codecNumber(segment.codec));
  }));
  const query = route.query.map(member => studioCanonicalHash('base.studio.route-query.v1', writer => {
    writer.string(member.name); writer.discriminator(codecNumber(member.codec)); writer.boolean(member.required);
    writer.count(member.allowed?.length ?? 0); for (const value of member.allowed ?? []) writer.string(value);
  }));
  return studioCanonicalHash('base.studio.route.v1', writer => {
    writer.string(route.id); writer.count(segments.length); for (const checksum of segments) writer.checksum(checksum);
    writer.count(query.length); for (const checksum of query) writer.checksum(checksum);
  });
}

function codecNumber(codec: StudioRouteCodecId): number {
  return ['boundedId', 'positiveLong', 'nonnegativeLong', 'sha256', 'resource', 'cursor', 'enum', 'tab'].indexOf(codec) + 1;
}

function isCanonical(values: readonly string[]): boolean {
  if (new Set(values).size !== values.length) return false;
  for (let index = 1; index < values.length; index++) if (values[index - 1]! >= values[index]!) return false;
  return true;
}

/** Formats only values accepted by the route's closed codecs. */
export function formatStudioRoute(
  definition: StudioRouteDefinition,
  parameters: Readonly<Record<string, string>>,
  query: Readonly<Record<string, string>> = Object.freeze({})
): string {
  const route = defineStudioRoute(definition);
  const acceptedParameters = new Set(route.segments.filter((item) => item.kind === 'parameter').map((item) => item.name));
  requireExactKeys(parameters, acceptedParameters);
  const parts = route.segments.map((segment) => segment.kind === 'literal'
    ? segment.value
    : encode(validateValue(segment.codec, parameters[segment.name]!, segment.allowed)));
  const acceptedQuery = new Set(route.query.map((item) => item.name));
  requireExactKeys(query, acceptedQuery, true);
  const queryParts: string[] = [];
  for (const member of route.query) {
    const value = query[member.name];
    if (value === undefined) {
      if (member.required) throw new TypeError('A required Studio route query member is missing.');
      continue;
    }
    queryParts.push(`${encode(member.name)}=${encode(validateValue(member.codec, value, member.allowed))}`);
  }
  const result = `/${parts.join('/')}${queryParts.length === 0 ? '' : `?${queryParts.join('&')}`}`;
  if (new TextEncoder().encode(result).length > MAXIMUM_URL_BYTES) throw new RangeError('Studio route URL is too large.');
  return result;
}

/** Parses one URL only when it is already in canonical form. */
export function matchStudioRoute(definition: StudioRouteDefinition, url: string): StudioRouteMatch | null {
  if (typeof url !== 'string' || new TextEncoder().encode(url).length > MAXIMUM_URL_BYTES || url.includes('#')) return null;
  const route = defineStudioRoute(definition);
  const question = url.indexOf('?');
  const path = question < 0 ? url : url.slice(0, question);
  const rawQuery = question < 0 ? '' : url.slice(question + 1);
  if (!path.startsWith('/') || path !== '/' && path.endsWith('/') || path.includes('//')) return null;
  const parts = path === '/' ? [] : path.slice(1).split('/');
  if (parts.length !== route.segments.length) return null;
  const parameters: Record<string, string> = {};
  try {
    for (let index = 0; index < parts.length; index++) {
      const segment = route.segments[index]!;
      const decoded = decodeCanonical(parts[index]!);
      if (segment.kind === 'literal') {
        if (decoded !== segment.value) return null;
      } else parameters[segment.name] = validateValue(segment.codec, decoded, segment.allowed);
    }
    const query: Record<string, string> = {};
    if (rawQuery.length > 0) {
      for (const pair of rawQuery.split('&')) {
        const separator = pair.indexOf('=');
        if (separator <= 0) return null;
        const name = decodeCanonical(pair.slice(0, separator));
        const value = decodeCanonical(pair.slice(separator + 1));
        if (Object.hasOwn(query, name)) return null;
        const member = route.query.find((item) => item.name === name);
        if (!member) return null;
        query[name] = validateValue(member.codec, value, member.allowed);
      }
    }
    if (route.query.some((item) => item.required && query[item.name] === undefined)) return null;
    const canonicalUrl = formatStudioRoute(route, parameters, query);
    if (canonicalUrl !== url) return null;
    return deepFreeze({ routeId: route.id, parameters, query, canonicalUrl });
  } catch {
    return null;
  }
}

function validateParameter(name: string, codec: StudioRouteCodecId, allowed: readonly string[] | undefined, names: Set<string>): void {
  if (!NAME.test(name) || names.has(name) || !['boundedId', 'positiveLong', 'nonnegativeLong', 'sha256', 'resource', 'cursor', 'enum', 'tab'].includes(codec)) {
    throw new TypeError('Studio route parameter is invalid.');
  }
  names.add(name);
  if (codec === 'enum' || codec === 'tab') {
    if (!Array.isArray(allowed) || allowed.length < 1 || allowed.length > 64 ||
        allowed.some((item) => !BOUNDED_ID.test(item)) || new Set(allowed).size !== allowed.length) {
      throw new TypeError('Studio route enum is invalid.');
    }
  } else if (allowed !== undefined) throw new TypeError('Studio route codec does not accept enum values.');
}

function freezeParameter<T extends { readonly name: string; readonly codec: StudioRouteCodecId; readonly allowed?: readonly string[] }>(value: T): T {
  return Object.freeze({ ...value, ...(value.allowed === undefined ? {} : { allowed: Object.freeze([...value.allowed]) }) });
}

function validateValue(codec: StudioRouteCodecId, value: string, allowed?: readonly string[]): string {
  if (typeof value !== 'string' || value.normalize('NFC') !== value) throw new TypeError('Studio route value is invalid.');
  switch (codec) {
    case 'boundedId': if (!BOUNDED_ID.test(value)) throw new TypeError(); return value;
    case 'positiveLong': if (!/^[1-9][0-9]{0,18}$/u.test(value) || BigInt(value) > 9_223_372_036_854_775_807n) throw new TypeError(); return value;
    case 'nonnegativeLong': if (!/^(0|[1-9][0-9]{0,18})$/u.test(value) || BigInt(value) > 9_223_372_036_854_775_807n) throw new TypeError(); return value;
    case 'sha256': if (!SHA256.test(value)) throw new TypeError(); return value;
    case 'resource':
    case 'cursor': if (!BASE64URL.test(value)) throw new TypeError(); return value;
    case 'enum':
    case 'tab': if (!allowed?.includes(value)) throw new TypeError(); return value;
  }
}

function encode(value: string): string {
  return encodeURIComponent(value).replace(/[!'()*]/gu, (character) => `%${character.charCodeAt(0).toString(16).toUpperCase()}`);
}

function decodeCanonical(value: string): string {
  let decoded: string;
  try { decoded = decodeURIComponent(value); } catch { throw new TypeError(); }
  if (encode(decoded) !== value) throw new TypeError();
  return decoded;
}

function requireExactKeys(value: Readonly<Record<string, string>>, accepted: Set<string>, optional = false): void {
  if (!value || typeof value !== 'object' || Array.isArray(value)) throw new TypeError('Studio route values are invalid.');
  const keys = Object.keys(value);
  if (keys.some((key) => !accepted.has(key)) || (!optional && keys.length !== accepted.size)) {
    throw new TypeError('Studio route values contain unknown or missing members.');
  }
}

function deepFreeze<T>(value: T): T {
  if (value === null || typeof value !== 'object') return value;
  for (const child of Object.values(value)) deepFreeze(child);
  return Object.freeze(value);
}
import { studioCanonicalHash } from './canonical.ts';
