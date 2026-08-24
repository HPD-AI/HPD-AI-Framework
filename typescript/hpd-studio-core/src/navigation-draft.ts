export interface StudioNavigationDraftKey {
  readonly principalGeneration: number;
  readonly resourceIdentity: string;
  readonly pageId: string;
  readonly commandId: string;
  readonly schemaChecksum: string;
  readonly ordinal: number;
}

export interface StudioNavigationDraftStore {
  retain(admission: StudioNavigationDraftAdmission, canonicalBytes: Uint8Array): void;
  read(key: StudioNavigationDraftKey): Uint8Array | null;
  remove(key: StudioNavigationDraftKey): void;
  clear(): void;
  dispose(): void;
}

export interface StudioNavigationDraftStoreOptions {
  readonly maximumAggregateBytes: number;
  readonly maximumEntries: number;
  readonly lifetimeMilliseconds: number;
  readonly validateAdmission: (admission: StudioNavigationDraftAdmission, canonicalBytes: Uint8Array) => boolean;
  readonly now?: () => number;
}

export interface StudioNavigationDraftAdmission {
  readonly key: StudioNavigationDraftKey;
  readonly graphId: string;
  readonly pageRegistrationChecksum: string;
  readonly retentionClass: 'currentDocumentNavigation';
  readonly admissionChecksum: string;
}

interface Entry { readonly bytes: Uint8Array; readonly expiresAt: number; }

/** Creates the memory-only, current-document draft store. */
export function createStudioNavigationDraftStore(options: StudioNavigationDraftStoreOptions): StudioNavigationDraftStore {
  if (!options || !Number.isInteger(options.maximumAggregateBytes) || options.maximumAggregateBytes < 1 || options.maximumAggregateBytes > 1_048_576 ||
      options.maximumAggregateBytes > 256_000 ||
      !Number.isInteger(options.maximumEntries) || options.maximumEntries < 1 || options.maximumEntries > 128 ||
      !Number.isInteger(options.lifetimeMilliseconds) || options.lifetimeMilliseconds < 1_000 || options.lifetimeMilliseconds > 3_600_000 ||
      typeof options.validateAdmission !== 'function') throw new TypeError('Studio navigation draft options are invalid.');
  const now = options.now ?? Date.now;
  const entries = new Map<string, Entry>();
  let bytes = 0;
  let disposed = false;

  const prune = (): void => {
    const observedAt = now();
    for (const [key, entry] of entries) if (entry.expiresAt <= observedAt) {
      bytes -= entry.bytes.byteLength;
      entry.bytes.fill(0);
      entries.delete(key);
    }
  };

  return Object.freeze({
    retain(admission: StudioNavigationDraftAdmission, canonicalBytes: Uint8Array) {
      if (disposed) throw new Error('Studio navigation draft store is disposed.');
      if (!admission || admission.retentionClass !== 'currentDocumentNavigation' || !validIdentity(admission.graphId) ||
          !validChecksum(admission.pageRegistrationChecksum) || !validChecksum(admission.admissionChecksum)) {
        throw new TypeError('Studio navigation draft admission is invalid.');
      }
      const identity = keyIdentity(admission.key);
      if (!(canonicalBytes instanceof Uint8Array) || canonicalBytes.byteLength < 1 ||
          canonicalBytes.byteLength > options.maximumAggregateBytes || !options.validateAdmission(admission, canonicalBytes)) {
        throw new TypeError('Studio navigation draft is invalid or classified as confidential.');
      }
      prune();
      const prior = entries.get(identity);
      const nextTotal = bytes - (prior?.bytes.byteLength ?? 0) + canonicalBytes.byteLength;
      if ((prior === undefined && entries.size >= options.maximumEntries) || nextTotal > options.maximumAggregateBytes) {
        throw new RangeError('Studio navigation draft capacity is exceeded.');
      }
      const owned = canonicalBytes.slice();
      if (prior) prior.bytes.fill(0);
      entries.set(identity, { bytes: owned, expiresAt: now() + options.lifetimeMilliseconds });
      bytes = nextTotal;
    },
    read(key: StudioNavigationDraftKey) {
      if (disposed) return null;
      prune();
      return entries.get(keyIdentity(key))?.bytes.slice() ?? null;
    },
    remove(key: StudioNavigationDraftKey) {
      if (disposed) return;
      const identity = keyIdentity(key);
      const entry = entries.get(identity);
      if (!entry) return;
      bytes -= entry.bytes.byteLength;
      entry.bytes.fill(0);
      entries.delete(identity);
    },
    clear() {
      for (const entry of entries.values()) entry.bytes.fill(0);
      entries.clear();
      bytes = 0;
    },
    dispose() {
      if (disposed) return;
      disposed = true;
      for (const entry of entries.values()) entry.bytes.fill(0);
      entries.clear();
      bytes = 0;
    }
  });
}

function keyIdentity(key: StudioNavigationDraftKey): string {
  if (!key || !Number.isSafeInteger(key.principalGeneration) || key.principalGeneration < 1 ||
      !validIdentity(key.resourceIdentity) || !validIdentity(key.pageId) || !validIdentity(key.commandId) ||
      !validChecksum(key.schemaChecksum) || !Number.isSafeInteger(key.ordinal) || key.ordinal < 0) {
    throw new TypeError('Studio navigation draft key is invalid.');
  }
  return `${key.principalGeneration}\0${key.resourceIdentity}\0${key.pageId}\0${key.commandId}\0${key.schemaChecksum}\0${key.ordinal}`;
}

function validIdentity(value: unknown): value is string {
  return typeof value === 'string' && value.length >= 1 && value.length <= 512 && value.normalize('NFC') === value && !/[\u0000-\u001f\u007f-\u009f]/u.test(value);
}
function validChecksum(value: unknown): value is string { return typeof value === 'string' && /^[a-f0-9]{64}$/u.test(value); }
