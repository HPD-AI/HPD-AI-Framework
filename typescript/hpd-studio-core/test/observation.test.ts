import assert from 'node:assert/strict';
import test from 'node:test';
import { createStudioRefreshController, type StudioReadResult } from '../src/index.ts';

const authority = Object.freeze({ coherence: 'authority-one', authorizedThroughUtc: '2027-08-22T12:00:00.000Z' });

test('refresh is single-flight and publishes one deeply immutable value', async () => {
  let calls = 0;
  let release!: (value: StudioReadResult<{ nested: { value: number } }>) => void;
  const pending = new Promise<StudioReadResult<{ nested: { value: number } }>>((resolve) => { release = resolve; });
  const controller = createStudioRefreshController({
    read: async () => { calls++; return pending; },
    now: () => new Date('2026-08-22T12:00:00.000Z')
  });

  const first = controller.refresh();
  const second = controller.refresh();
  assert.equal(calls, 0);
  await Promise.resolve();
  assert.equal(calls, 1);
  assert.equal(controller.snapshot().state, 'loading');
  release({ kind: 'value', value: { nested: { value: 7 } }, authority });
  await Promise.all([first, second]);

  const snapshot = controller.snapshot();
  assert.equal(snapshot.state, 'value');
  if (snapshot.state !== 'value') return;
  assert.equal(snapshot.observedAt, '2026-08-22T12:00:00.000Z');
  assert.equal(snapshot.value.nested.value, 7);
  assert.equal(Object.isFrozen(snapshot.value), true);
  assert.equal(Object.isFrozen(snapshot.value.nested), true);
});

test('failed refresh retains prior truth only as stale', async () => {
  let attempt = 0;
  const controller = createStudioRefreshController({
    read: async () => ++attempt === 1
      ? { kind: 'value', value: { revision: 1 }, authority }
      : { kind: 'failed', code: 'base.studio.deadlineExceeded' },
    now: () => new Date('2026-08-22T12:00:00.000Z')
  });

  await controller.refresh();
  await controller.refresh();
  assert.deepEqual(controller.snapshot(), {
    state: 'stale',
    value: { revision: 1 },
    observedAt: '2026-08-22T12:00:00.000Z',
    code: 'base.studio.deadlineExceeded'
  });
});

test('authority invalidation prevents late work from publishing', async () => {
  let release!: (value: StudioReadResult<{ secret: string }>) => void;
  const controller = createStudioRefreshController<{ secret: string }>({
    read: async () => new Promise<StudioReadResult<{ secret: string }>>((resolve) => { release = resolve; })
  });

  const refresh = controller.refresh();
  await Promise.resolve();
  controller.invalidate('principalChanged');
  release({ kind: 'value', value: { secret: 'old-principal' }, authority });
  await refresh;
  assert.deepEqual(controller.snapshot(), { state: 'unobserved' });
});

test('denied and unavailable never retain an earlier value', async () => {
  const outcomes: StudioReadResult<number>[] = [
    { kind: 'value', value: 1, authority },
    { kind: 'denied', code: 'base.studio.unauthorized' },
    { kind: 'unavailable', code: 'base.studio.unavailable' }
  ];
  const controller = createStudioRefreshController({ read: async () => outcomes.shift()! });
  await controller.refresh();
  await controller.refresh();
  assert.deepEqual(controller.snapshot(), { state: 'denied', code: 'base.studio.unauthorized' });
  await controller.refresh();
  assert.deepEqual(controller.snapshot(), { state: 'unavailable', code: 'base.studio.unavailable' });
});

test('caller cancellation only stops waiting and does not abort shared work', async () => {
  let release!: (value: StudioReadResult<number>) => void;
  const controller = createStudioRefreshController({
    read: async () => new Promise<StudioReadResult<number>>((resolve) => { release = resolve; })
  });
  const cancellation = new AbortController();
  const refresh = controller.refresh(cancellation.signal);
  await Promise.resolve();
  cancellation.abort();
  await refresh;
  assert.equal(controller.snapshot().state, 'loading');
  release({ kind: 'value', value: 1, authority });
  await new Promise((resolve) => setTimeout(resolve, 0));
  assert.equal(controller.snapshot().state, 'value');
});

test('data invalidation marks current truth stale while authority invalidation clears it', async () => {
  const controller = createStudioRefreshController({ read: async () => ({ kind: 'value', value: 1, authority }) });
  await controller.refresh();
  controller.invalidate('dataChanged');
  assert.equal(controller.snapshot().state, 'stale');
  controller.invalidate('policyChanged');
  assert.deepEqual(controller.snapshot(), { state: 'unobserved' });
});

test('authorization expiry clears protected truth and hostile observers are isolated', async () => {
  const expiring = { coherence: 'short', authorizedThroughUtc: new Date(Date.now() + 15).toISOString() };
  const controller = createStudioRefreshController({ read: async () => ({ kind: 'value', value: 1, authority: expiring }) });
  controller.subscribe(() => { throw new Error('hostile observer'); });
  let observed = 0;
  controller.subscribe(() => { observed++; });
  await controller.refresh();
  assert.equal(controller.snapshot().state, 'value');
  await new Promise((resolve) => setTimeout(resolve, 25));
  assert.deepEqual(controller.snapshot(), { state: 'unobserved' });
  assert.ok(observed >= 3);
});
