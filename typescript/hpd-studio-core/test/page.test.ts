import assert from 'node:assert/strict';
import test from 'node:test';
import { createStudioPageController, type StudioPageSegment } from '../src/index.ts';

type Item = Readonly<{ id: string; value: number }>;
const authority = Object.freeze({ coherence: 'page-one', authorizedThroughUtc: '2027-08-22T12:00:00.000Z' });
const segment = (items: Item[], next: string | null, coherence = 'page-one'): StudioPageSegment<Item, string> => ({
  items, next, authority: { ...authority, coherence }, coverageChecksum: `coverage-${next ?? 'end'}`,
  accounting: { resultBytes: 10, transientBytes: 20 }
});

function controller(segments: StudioPageSegment<Item, string>[], maximumItems = 4, maximumPages = 2) {
  return createStudioPageController<Item, string>({
    take: 2,
    maximumItems,
    maximumPages,
    maximumResultBytes: 1_000,
    maximumTransientBytes: 1_000,
    load: async () => ({ kind: 'value', value: segments.shift()!, authority }),
    itemIdentity: (item) => item.id,
    boundaryIdentity: (boundary) => boundary,
    now: () => new Date('2026-08-22T12:00:00.000Z')
  });
}

test('bounded pages append without replacing prior rows', async () => {
  const pages = controller([
    segment([{ id: 'a', value: 1 }, { id: 'b', value: 2 }], 'b'),
    segment([{ id: 'c', value: 3 }], null)
  ]);
  await pages.loadInitial();
  await pages.loadMore();
  const snapshot = pages.snapshot();
  assert.equal(snapshot.state, 'value');
  if (snapshot.state !== 'value') return;
  assert.deepEqual(snapshot.value.items.map((item) => item.id), ['a', 'b', 'c']);
  assert.equal(snapshot.value.pages, 2);
  assert.equal(snapshot.value.next, null);
  assert.equal(Object.isFrozen(snapshot.value.items), true);
});

test('duplicate rows and repeated continuation fail closed with stale truth', async () => {
  const duplicate = controller([
    segment([{ id: 'a', value: 1 }], 'a'),
    segment([{ id: 'a', value: 2 }], null)
  ]);
  await duplicate.loadInitial();
  await duplicate.loadMore();
  const duplicateSnapshot = duplicate.snapshot();
  assert.equal(duplicateSnapshot.state, 'stale');
  if (duplicateSnapshot.state === 'stale') assert.equal(duplicateSnapshot.code, 'studio.page.duplicateItem');

  const boundary = controller([
    segment([{ id: 'a', value: 1 }], 'cursor'),
    segment([{ id: 'b', value: 2 }], 'cursor')
  ]);
  await boundary.loadInitial();
  await boundary.loadMore();
  const boundarySnapshot = boundary.snapshot();
  assert.equal(boundarySnapshot.state, 'stale');
  if (boundarySnapshot.state === 'stale') assert.equal(boundarySnapshot.code, 'studio.page.repeatedBoundary');
});

test('maximum item and page boundaries are enforced independently', async () => {
  const items = controller([
    segment([{ id: 'a', value: 1 }, { id: 'b', value: 2 }], 'b'),
    segment([{ id: 'c', value: 3 }], null)
  ], 2, 2);
  await items.loadInitial();
  await items.loadMore();
  const itemSnapshot = items.snapshot();
  assert.equal(itemSnapshot.state, 'stale');
  if (itemSnapshot.state === 'stale') assert.equal(itemSnapshot.code, 'studio.page.maximumItems');

  const pages = controller([
    segment([{ id: 'a', value: 1 }], 'a')
  ], 4, 1);
  await pages.loadInitial();
  await pages.loadMore();
  const pageSnapshot = pages.snapshot();
  assert.equal(pageSnapshot.state, 'stale');
  if (pageSnapshot.state === 'stale') assert.equal(pageSnapshot.code, 'studio.page.maximumPages');
});

test('invalidation suppresses a late page from the prior authority generation', async () => {
  let release!: (value: { kind: 'value'; value: StudioPageSegment<Item, string>; authority: typeof authority }) => void;
  const pages = createStudioPageController<Item, string>({
    take: 2,
    maximumItems: 4,
    maximumPages: 2,
    maximumResultBytes: 1_000,
    maximumTransientBytes: 1_000,
    load: async () => new Promise((resolve) => { release = resolve; }),
    itemIdentity: (item) => item.id,
    boundaryIdentity: (boundary) => boundary
  });
  const pending = pages.loadInitial();
  await Promise.resolve();
  pages.invalidate('principalChanged');
  release({ kind: 'value', value: segment([{ id: 'private', value: 1 }], null), authority });
  await pending;
  assert.deepEqual(pages.snapshot(), { state: 'unobserved' });
});

test('cross-page authority drift and non-adjacent cursor cycles fail closed', async () => {
  const drift = controller([
    segment([{ id: 'a', value: 1 }], 'a'),
    segment([{ id: 'b', value: 2 }], null, 'page-two')
  ]);
  await drift.loadInitial();
  await drift.loadMore();
  assert.equal(drift.snapshot().state, 'stale');

  const cycle = controller([
    segment([{ id: 'a', value: 1 }], 'a'),
    segment([{ id: 'b', value: 2 }], 'b'),
    segment([{ id: 'c', value: 3 }], 'a')
  ], 6, 3);
  await cycle.loadInitial();
  await cycle.loadMore();
  await cycle.loadMore();
  const snapshot = cycle.snapshot();
  assert.equal(snapshot.state, 'stale');
  if (snapshot.state === 'stale') assert.equal(snapshot.code, 'studio.page.repeatedBoundary');
});

test('initial reload retains last-good rows when replacement fails', async () => {
  let call = 0;
  const pages = createStudioPageController<Item, string>({
    take: 2, maximumItems: 4, maximumPages: 2, maximumResultBytes: 1_000, maximumTransientBytes: 1_000,
    load: async () => ++call === 1
      ? { kind: 'value', value: segment([{ id: 'a', value: 1 }], null), authority }
      : { kind: 'failed', code: 'base.studio.deadlineExceeded' },
    itemIdentity: (item) => item.id,
    boundaryIdentity: (boundary) => boundary
  });
  await pages.loadInitial();
  await pages.loadInitial();
  const snapshot = pages.snapshot();
  assert.equal(snapshot.state, 'stale');
  if (snapshot.state === 'stale') assert.equal(snapshot.value.items[0]?.id, 'a');
});

test('data-invalidated stale page is display-only until a fresh initial load', async () => {
  let calls = 0;
  const pages = createStudioPageController<Item, string>({
    take: 2, maximumItems: 4, maximumPages: 2, maximumResultBytes: 1_000, maximumTransientBytes: 1_000,
    load: async () => {
      calls++;
      return { kind: 'value', value: segment([{ id: 'a', value: 1 }], 'a'), authority };
    },
    itemIdentity: (item) => item.id,
    boundaryIdentity: (boundary) => boundary
  });
  await pages.loadInitial();
  pages.invalidate('dataChanged');
  await pages.loadMore();
  assert.equal(calls, 1);
  assert.equal(pages.snapshot().state, 'stale');
});
