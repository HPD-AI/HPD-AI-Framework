import assert from 'node:assert/strict';
import test from 'node:test';
import { createStudioNavigationDraftStore } from '../src/navigation-draft.ts';

const key = Object.freeze({
  principalGeneration: 1, resourceIdentity: 'resource', pageId: 'base.record.detail',
  commandId: 'base.record.update', schemaChecksum: 'c'.repeat(64), ordinal: 0
});
const admission = Object.freeze({
  key, graphId: 'nonsecret-input', retentionClass: 'currentDocumentNavigation' as const,
  pageRegistrationChecksum: 'a'.repeat(64), admissionChecksum: 'b'.repeat(64)
});

test('navigation drafts are deeply owned, bounded, validated, and removable', () => {
  const store = createStudioNavigationDraftStore({
    maximumAggregateBytes: 8, maximumEntries: 1, lifetimeMilliseconds: 1_000,
    validateAdmission: (value, bytes) => value.graphId === 'nonsecret-input' && bytes[0] === 1
  });
  const source = new Uint8Array([1, 2, 3]);
  store.retain(admission, source);
  source[0] = 9;
  const first = store.read(key)!;
  assert.deepEqual([...first], [1, 2, 3]);
  first[0] = 8;
  assert.deepEqual([...store.read(key)!], [1, 2, 3]);
  assert.throws(() => store.retain({ ...admission, key: { ...key, ordinal: 1 } }, new Uint8Array([1])));
  assert.throws(() => store.retain({ ...admission, graphId: 'secret-input' }, new Uint8Array([1])));
  store.remove(key);
  assert.equal(store.read(key), null);
});

test('expiry and lifecycle clearing destroy retained drafts', () => {
  let now = 1_000;
  const store = createStudioNavigationDraftStore({
    maximumAggregateBytes: 8, maximumEntries: 2, lifetimeMilliseconds: 1_000,
    validateAdmission: () => true, now: () => now
  });
  store.retain(admission, new Uint8Array([1]));
  now = 2_000;
  assert.equal(store.read(key), null);
  store.retain(admission, new Uint8Array([1]));
  store.clear();
  assert.equal(store.read(key), null);
  store.dispose();
  assert.throws(() => store.retain(admission, new Uint8Array([1])));
});
