export type StudioDisplayPreference =
  | Readonly<{ readonly kind: 'theme'; readonly value: 'light' | 'dark' | 'system' }>
  | Readonly<{ readonly kind: 'density'; readonly value: 'compact' | 'comfortable' }>
  | Readonly<{ readonly kind: 'railWidth'; readonly value: number }>
  | Readonly<{ readonly kind: 'detailWidth'; readonly value: number }>
  | Readonly<{ readonly kind: 'visibleColumns'; readonly value: readonly string[] }>
  | Readonly<{ readonly kind: 'columnOrder'; readonly value: readonly string[] }>
  | Readonly<{ readonly kind: 'columnWidths'; readonly value: Readonly<Record<string, number>> }>
  | Readonly<{ readonly kind: 'nonsecretPins'; readonly value: readonly string[] }>
  | Readonly<{ readonly kind: 'preferredTab'; readonly value: string }>;

export interface StudioPreferenceContext {
  readonly applicationId: string;
  readonly principalPreferenceKey: string;
  readonly studioGraphChecksum: string;
  readonly pageId: string;
  readonly viewId: string;
  readonly viewContractChecksum: string;
  readonly schemaChecksum: string;
}

export interface StudioPreferenceSchema {
  readonly version: number;
  readonly allowed: readonly StudioDisplayPreference['kind'][];
  readonly maximumEntries: number;
  readonly maximumBytes: number;
  readonly lifetimeMilliseconds: number;
  readonly columns: readonly Readonly<{ readonly id: string; readonly minimumWidth: number; readonly maximumWidth: number }>[];
  readonly tabs: readonly string[];
  readonly safePins: readonly string[];
  readonly minimumRailWidth: number;
  readonly maximumRailWidth: number;
  readonly minimumDetailWidth: number;
  readonly maximumDetailWidth: number;
}

export interface StudioPreferenceStorage {
  get(key: string): string | null;
  set(key: string, value: string): void;
  remove(key: string): void;
  keys(prefix: string): readonly string[];
}

export interface StudioPreferenceStore {
  load(context: StudioPreferenceContext, schema: StudioPreferenceSchema): Promise<readonly StudioDisplayPreference[]>;
  save(context: StudioPreferenceContext, schema: StudioPreferenceSchema, values: readonly StudioDisplayPreference[]): Promise<void>;
  clearPrincipal(applicationId: string, principalPreferenceKey: string): Promise<void>;
}

/** Creates the shell-owned preference store; modules never receive its storage adapter. */
export function createStudioPreferenceStore(storage: StudioPreferenceStorage, now: () => number = Date.now): StudioPreferenceStore {
  if (!storage || typeof storage.get !== 'function' || typeof storage.set !== 'function' ||
      typeof storage.remove !== 'function' || typeof storage.keys !== 'function') throw new TypeError('Studio preference storage is invalid.');

  const keyFor = async (context: StudioPreferenceContext): Promise<string> => {
    validateContext(context);
    const namespace = await digest(`${context.applicationId}\0${context.principalPreferenceKey}`);
    const identity = await digest([
      context.applicationId, context.principalPreferenceKey, context.studioGraphChecksum,
      context.pageId, context.viewId, context.viewContractChecksum, context.schemaChecksum
    ].join('\0'));
    return `hpd.studio.preference.v1.${namespace}.${identity}`;
  };

  return Object.freeze({
    async load(context: StudioPreferenceContext, schema: StudioPreferenceSchema) {
      validateSchema(schema);
      const key = await keyFor(context);
      try {
        const encoded = storage.get(key);
        if (encoded === null || new TextEncoder().encode(encoded).length > schema.maximumBytes) return Object.freeze([]);
        const parsed: unknown = JSON.parse(encoded);
        if (!isPlainObject(parsed) || exactKeys(parsed, ['checksum', 'expiresAt', 'schemaVersion', 'values']) === false ||
            parsed.schemaVersion !== schema.version || typeof parsed.expiresAt !== 'number' || parsed.expiresAt <= now() ||
            !Array.isArray(parsed.values) || parsed.values.length > schema.maximumEntries) {
          storage.remove(key);
          return Object.freeze([]);
        }
        const values = validateValues(parsed.values, schema);
        const expected = await digest(canonicalJson({ expiresAt: parsed.expiresAt, schemaVersion: schema.version, values }));
        if (parsed.checksum !== expected) {
          storage.remove(key);
          return Object.freeze([]);
        }
        return deepFreeze(structuredClone(values));
      } catch {
        try { storage.remove(key); } catch { /* storage failure is nonfatal */ }
        return Object.freeze([]);
      }
    },
    async save(context: StudioPreferenceContext, schema: StudioPreferenceSchema, values: readonly StudioDisplayPreference[]) {
      validateSchema(schema);
      const key = await keyFor(context);
      const owned = validateValues(values, schema);
      const body = { expiresAt: now() + schema.lifetimeMilliseconds, schemaVersion: schema.version, values: owned };
      const encoded = canonicalJson({ ...body, checksum: await digest(canonicalJson(body)) });
      if (new TextEncoder().encode(encoded).length > schema.maximumBytes) throw new RangeError('Studio preferences exceed their byte limit.');
      try { storage.set(key, encoded); } catch { /* storage failure is nonfatal */ }
    },
    async clearPrincipal(applicationId: string, principalPreferenceKey: string) {
      requireIdentity(applicationId);
      requireIdentity(principalPreferenceKey);
      const namespace = await digest(`${applicationId}\0${principalPreferenceKey}`);
      const prefix = `hpd.studio.preference.v1.${namespace}.`;
      try { for (const key of storage.keys(prefix)) if (key.startsWith(prefix)) storage.remove(key); } catch { /* nonfatal */ }
    }
  });
}

function validateValues(values: readonly unknown[], schema: StudioPreferenceSchema): readonly StudioDisplayPreference[] {
  if (!Array.isArray(values) || values.length > schema.maximumEntries) throw new TypeError('Studio preferences are invalid.');
  const kinds = new Set<string>();
  const allowed = new Set(schema.allowed);
  return values.map((candidate) => {
    if (!isPlainObject(candidate) || exactKeys(candidate, ['kind', 'value']) === false ||
        typeof candidate.kind !== 'string' || !allowed.has(candidate.kind as StudioDisplayPreference['kind']) || kinds.has(candidate.kind)) {
      throw new TypeError('Studio preference is not allowed by the registered schema.');
    }
    kinds.add(candidate.kind);
    const value = candidate.value;
    switch (candidate.kind) {
      case 'theme': if (!['light', 'dark', 'system'].includes(value as string)) throw new TypeError(); break;
      case 'density': if (!['compact', 'comfortable'].includes(value as string)) throw new TypeError(); break;
      case 'railWidth': if (!Number.isInteger(value) || (value as number) < schema.minimumRailWidth || (value as number) > schema.maximumRailWidth) throw new TypeError(); break;
      case 'detailWidth': if (!Number.isInteger(value) || (value as number) < schema.minimumDetailWidth || (value as number) > schema.maximumDetailWidth) throw new TypeError(); break;
      case 'visibleColumns':
      case 'columnOrder': if (!validRegisteredValues(value, schema.columns.map((item) => item.id))) throw new TypeError(); break;
      case 'nonsecretPins': if (!validRegisteredValues(value, schema.safePins)) throw new TypeError(); break;
      case 'preferredTab': if (typeof value !== 'string' || !schema.tabs.includes(value)) throw new TypeError(); break;
      case 'columnWidths':
        if (!isPlainObject(value) || Object.keys(value).length > schema.columns.length || Object.entries(value).some(([id, width]) => {
          const column = schema.columns.find((item) => item.id === id);
          return column === undefined || !Number.isInteger(width) || (width as number) < column.minimumWidth || (width as number) > column.maximumWidth;
        })) throw new TypeError();
        break;
      default: throw new TypeError();
    }
    return deepFreeze(structuredClone(candidate)) as StudioDisplayPreference;
  }).sort((left, right) => left.kind < right.kind ? -1 : left.kind > right.kind ? 1 : 0);
}

function validateSchema(schema: StudioPreferenceSchema): void {
  if (!schema || !Number.isInteger(schema.version) || schema.version < 1 ||
      !Array.isArray(schema.allowed) || new Set(schema.allowed).size !== schema.allowed.length ||
      schema.allowed.some((kind) => !['theme', 'density', 'railWidth', 'detailWidth', 'visibleColumns', 'columnOrder', 'columnWidths', 'nonsecretPins', 'preferredTab'].includes(kind)) ||
      !Number.isInteger(schema.maximumEntries) || schema.maximumEntries < 0 || schema.maximumEntries > 9 ||
      !Number.isInteger(schema.maximumBytes) || schema.maximumBytes < 1 || schema.maximumBytes > 64_000 ||
      !Number.isInteger(schema.lifetimeMilliseconds) || schema.lifetimeMilliseconds < 60_000 || schema.lifetimeMilliseconds > 15_552_000_000 ||
      !Array.isArray(schema.columns) || schema.columns.length > 128 || new Set(schema.columns.map((item) => item.id)).size !== schema.columns.length ||
      schema.columns.some((item) => !validIdentity(item.id) || !Number.isInteger(item.minimumWidth) || !Number.isInteger(item.maximumWidth) ||
        item.minimumWidth < 1 || item.maximumWidth > 1_600 || item.minimumWidth > item.maximumWidth) ||
      !validIdentityCatalog(schema.tabs) || !validIdentityCatalog(schema.safePins, 64) ||
      !validWidthRange(schema.minimumRailWidth, schema.maximumRailWidth) ||
      !validWidthRange(schema.minimumDetailWidth, schema.maximumDetailWidth)) {
    throw new TypeError('Studio preference schema is invalid.');
  }
}

function validateContext(context: StudioPreferenceContext): void {
  if (!context || !validIdentity(context.applicationId) || !validIdentity(context.principalPreferenceKey) ||
      !validChecksum(context.studioGraphChecksum) || !validIdentity(context.pageId) || !validIdentity(context.viewId) ||
      !validChecksum(context.viewContractChecksum) || !validChecksum(context.schemaChecksum))
    throw new TypeError('Studio preference context is invalid.');
}

function validRegisteredValues(value: unknown, registered: readonly string[]): value is readonly string[] {
  const allowed = new Set(registered);
  return Array.isArray(value) && value.length <= registered.length && value.every((item) => typeof item === 'string' && allowed.has(item)) && new Set(value).size === value.length;
}
function validIdentityCatalog(value: unknown, maximum = 128): value is readonly string[] { return Array.isArray(value) && value.length <= maximum && value.every(validIdentity) && new Set(value).size === value.length; }
function validWidthRange(minimum: number, maximum: number): boolean { return Number.isInteger(minimum) && Number.isInteger(maximum) && minimum >= 1 && maximum <= 1_600 && minimum <= maximum; }
function validIdentity(value: unknown): value is string { return typeof value === 'string' && /^[A-Za-z0-9][A-Za-z0-9._:-]{0,255}$/u.test(value); }
function validChecksum(value: unknown): value is string { return typeof value === 'string' && /^[a-f0-9]{64}$/u.test(value); }
function requireIdentity(value: string): void { if (!validIdentity(value)) throw new TypeError('Studio preference identity is invalid.'); }
function isPlainObject(value: unknown): value is Record<string, unknown> { return value !== null && typeof value === 'object' && !Array.isArray(value) && Object.getPrototypeOf(value) === Object.prototype; }
function exactKeys(value: Record<string, unknown>, expected: readonly string[]): boolean { const keys = Object.keys(value).sort(); return keys.length === expected.length && keys.every((key, index) => key === [...expected].sort()[index]); }

async function digest(value: string): Promise<string> {
  const bytes = await crypto.subtle.digest('SHA-256', new TextEncoder().encode(value));
  return Array.from(new Uint8Array(bytes), (item) => item.toString(16).padStart(2, '0')).join('');
}

function canonicalJson(value: unknown): string {
  if (value === null || typeof value === 'string' || typeof value === 'boolean') return JSON.stringify(value);
  if (typeof value === 'number') { if (!Number.isSafeInteger(value)) throw new TypeError(); return String(value); }
  if (Array.isArray(value)) return `[${value.map(canonicalJson).join(',')}]`;
  if (!isPlainObject(value)) throw new TypeError();
  return `{${Object.keys(value).sort().map((key) => `${JSON.stringify(key)}:${canonicalJson(value[key])}`).join(',')}}`;
}

function deepFreeze<T>(value: T): T {
  if (value === null || typeof value !== 'object') return value;
  for (const child of Object.values(value)) deepFreeze(child);
  return Object.freeze(value);
}
