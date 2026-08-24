import assert from 'node:assert/strict';
import test from 'node:test';
import { createStudioPreferenceStore, type StudioPreferenceStorage } from '../src/preference.ts';

function memoryStorage() {
  const values = new Map<string, string>();
  const storage: StudioPreferenceStorage = {
    get: (key) => values.get(key) ?? null,
    set: (key, value) => { values.set(key, value); },
    remove: (key) => { values.delete(key); },
    keys: (prefix) => [...values.keys()].filter((key) => key.startsWith(prefix))
  };
  return { storage, values };
}

const context = Object.freeze({
  applicationId: 'app', principalPreferenceKey: 'principal', studioGraphChecksum: 'a'.repeat(64),
  pageId: 'base.data', viewId: 'records', viewContractChecksum: 'b'.repeat(64), schemaChecksum: 'c'.repeat(64)
});
const schema = Object.freeze({
  version: 1, allowed: ['theme', 'visibleColumns'] as const, maximumEntries: 2,
  maximumBytes: 4_096, lifetimeMilliseconds: 60_000,
  columns: [{ id: 'name', minimumWidth: 80, maximumWidth: 400 }, { id: 'revision', minimumWidth: 60, maximumWidth: 200 }],
  tabs: ['summary'], safePins: ['cmVjb3JkLTE'],
  minimumRailWidth: 160, maximumRailWidth: 1_600, minimumDetailWidth: 240, maximumDetailWidth: 1_600
});

test('display preferences round trip through a digested principal and graph key', async () => {
  const memory = memoryStorage();
  const store = createStudioPreferenceStore(memory.storage, () => 1_000);
  await store.save(context, schema, [
    { kind: 'visibleColumns', value: ['name', 'revision'] },
    { kind: 'theme', value: 'dark' }
  ]);
  assert.equal(memory.values.size, 1);
  const key = [...memory.values.keys()][0]!;
  assert.equal(key.includes('principal'), false);
  assert.deepEqual(await store.load(context, schema), [
    { kind: 'theme', value: 'dark' },
    { kind: 'visibleColumns', value: ['name', 'revision'] }
  ]);
});

test('unknown, protected-shaped, tampered, and expired preferences fail closed', async () => {
  const memory = memoryStorage();
  const store = createStudioPreferenceStore(memory.storage, () => 1_000);
  await assert.rejects(() => store.save(context, schema, [{ kind: 'filter', value: 'secret' } as never]));
  await store.save(context, schema, [{ kind: 'theme', value: 'dark' }]);
  const key = [...memory.values.keys()][0]!;
  memory.values.set(key, memory.values.get(key)!.replace('dark', 'light'));
  assert.deepEqual(await store.load(context, schema), []);

  await store.save(context, schema, [{ kind: 'theme', value: 'dark' }]);
  const expired = createStudioPreferenceStore(memory.storage, () => 100_000);
  assert.deepEqual(await expired.load(context, schema), []);
});

test('principal clearing removes only the exact digested namespace', async () => {
  const memory = memoryStorage();
  const store = createStudioPreferenceStore(memory.storage, () => 1_000);
  await store.save(context, schema, [{ kind: 'theme', value: 'dark' }]);
  await store.save({ ...context, principalPreferenceKey: 'other' }, schema, [{ kind: 'theme', value: 'light' }]);
  await store.clearPrincipal('app', 'principal');
  assert.deepEqual(await store.load(context, schema), []);
  assert.deepEqual(await store.load({ ...context, principalPreferenceKey: 'other' }, schema), [{ kind: 'theme', value: 'light' }]);
});
