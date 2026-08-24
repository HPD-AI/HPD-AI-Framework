import assert from 'node:assert/strict';
import test from 'node:test';
import { createStudioActivityController } from '../src/activity.ts';

test('hints coalesce and overload pauses automatic activity', () => {
  let now = 100;
  const activity = createStudioActivityController({
    refresh: async () => {}, policy: { kind: 'governedInvalidationRefresh', maximumHintsPerRollingSecond: 2,
      maximumSupersededRefreshes: 3, maximumCoalescedKeys: 4 }, now: () => now
  });
  activity.observeHint('record.one');
  assert.deepEqual(activity.snapshot(), { kind: 'updatesAvailable', count: 'one' });
  activity.observeHint('record.two');
  assert.deepEqual(activity.snapshot(), { kind: 'updatesAvailable', count: 'several' });
  activity.observeHint('record.three');
  assert.deepEqual(activity.snapshot(), { kind: 'pausedForActivity' });
  now += 1_000;
  activity.observeHint('record.four');
  assert.deepEqual(activity.snapshot(), { kind: 'pausedForActivity' });
});

test('duplicate keys coalesce and repeated superseded refreshes pause', async () => {
  const finishes: Array<() => void> = [];
  const activity = createStudioActivityController({
    refresh: async () => new Promise<void>((resolve) => { finishes.push(resolve); }),
    policy: { kind: 'governedInvalidationRefresh', maximumHintsPerRollingSecond: 20,
      maximumSupersededRefreshes: 2, maximumCoalescedKeys: 2 }
  });
  activity.observeHint('same');
  activity.observeHint('same');
  assert.deepEqual(activity.snapshot(), { kind: 'updatesAvailable', count: 'one' });
  for (let index = 0; index < 2; index++) {
    const refresh = activity.refresh();
    await Promise.resolve();
    activity.observeHint(`new-${index}`);
    finishes.shift()!();
    await refresh;
  }
  assert.deepEqual(activity.snapshot(), { kind: 'pausedForActivity' });
});

test('refresh is single flight and hints during refresh remain pending', async () => {
  let calls = 0;
  let finish!: () => void;
  const activity = createStudioActivityController({
    refresh: async () => { calls++; await new Promise<void>((resolve) => { finish = resolve; }); },
    policy: { kind: 'governedInvalidationRefresh', maximumHintsPerRollingSecond: 10,
      maximumSupersededRefreshes: 3, maximumCoalescedKeys: 10 }
  });
  activity.observeHint('record.one');
  const first = activity.refresh();
  const second = activity.refresh();
  await Promise.resolve();
  activity.observeHint('record.two');
  finish();
  await Promise.all([first, second]);
  assert.equal(calls, 1);
  assert.deepEqual(activity.snapshot(), { kind: 'updatesAvailable', count: 'one' });
});

test('invalidation fences late refresh completion', async () => {
  let finish!: () => void;
  const activity = createStudioActivityController({
    refresh: async () => new Promise<void>((resolve) => { finish = resolve; }),
    policy: { kind: 'governedInvalidationRefresh', maximumHintsPerRollingSecond: 10,
      maximumSupersededRefreshes: 3, maximumCoalescedKeys: 10 }
  });
  const pending = activity.refresh();
  await Promise.resolve();
  activity.invalidate();
  finish();
  await pending;
  assert.deepEqual(activity.snapshot(), { kind: 'quiet' });
});
