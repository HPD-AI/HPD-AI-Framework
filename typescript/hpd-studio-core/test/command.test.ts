import assert from 'node:assert/strict';
import test from 'node:test';
import { createStudioIdentifiedCommandController } from '../src/command.ts';

const target = Object.freeze({ kind: 'record', id: 'one' });
const input = Object.freeze({ enabled: true });
const previewAuthority = () => ({ coherence: 'preview-one', authorizedThroughUtc: '2027-08-22T12:00:00.000Z' });

test('identified command previews, executes, and deeply owns results', async () => {
  const result = { revision: 2 };
  const controller = createStudioIdentifiedCommandController({
    preview: async () => ({ checksum: 'preview' }),
    execute: async () => ({ kind: 'confirmed', result }),
    resolve: async () => { throw new Error('unused'); },
    failure: () => ({ code: 'failed' }),
    resolutionRequestIdentity: (value: any) => value.requestIdentity,
    previewAuthority
  });
  controller.open(target, 'base.record.update', input);
  await controller.preview();
  assert.equal(controller.snapshot().kind, 'review');
  await controller.execute('request-one');
  result.revision = 3;
  assert.deepEqual(controller.snapshot(), { kind: 'confirmed', result: { revision: 2 } });
});

test('indeterminate execution resolves without executing again', async () => {
  let executions = 0;
  let resolutions = 0;
  const controller = createStudioIdentifiedCommandController({
    preview: async () => ({ checksum: 'preview' }),
    execute: async ({ requestIdentity }) => {
      executions++;
      return { kind: 'indeterminate', resolution: { requestIdentity, receipt: 'receipt-one' } } as const;
    },
    resolve: async () => {
      resolutions++;
      return { kind: 'duplicate', result: { revision: 2 } } as const;
    },
    failure: () => ({ code: 'failed' }),
    resolutionRequestIdentity: (value) => value.requestIdentity,
    previewAuthority
  });
  controller.open(target, 'base.record.update', input);
  await controller.preview();
  await controller.execute('request-one');
  assert.equal(controller.snapshot().kind, 'indeterminate');
  await controller.resolve();
  assert.equal(controller.snapshot().kind, 'duplicate');
  assert.equal(executions, 1);
  assert.equal(resolutions, 1);
});

test('principal invalidation suppresses late preview and clears protected state', async () => {
  let complete!: (value: { checksum: string }) => void;
  const controller = createStudioIdentifiedCommandController({
    preview: () => new Promise((resolve) => { complete = resolve; }),
    execute: async () => { throw new Error('unused'); },
    resolve: async () => { throw new Error('unused'); },
    failure: () => ({ code: 'failed' }),
    resolutionRequestIdentity: () => 'unused',
    previewAuthority
  });
  controller.open(target, 'base.record.update', input);
  const work = controller.preview();
  await Promise.resolve();
  controller.invalidate();
  complete({ checksum: 'late' });
  await work;
  assert.deepEqual(controller.snapshot(), { kind: 'closed' });
});

test('single flight prevents a second execution while the first is active', async () => {
  let finish!: () => void;
  let executions = 0;
  const controller = createStudioIdentifiedCommandController({
    preview: async () => ({ checksum: 'preview' }),
    execute: async () => {
      executions++;
      await new Promise<void>((resolve) => { finish = resolve; });
      return { kind: 'confirmed', result: { ok: true } } as const;
    },
    resolve: async () => { throw new Error('unused'); },
    failure: () => ({ code: 'failed' }),
    resolutionRequestIdentity: () => 'unused',
    previewAuthority
  });
  controller.open(target, 'base.record.update', input);
  await controller.preview();
  const first = controller.execute('request-one');
  const second = controller.execute('request-two');
  await Promise.resolve();
  finish();
  await Promise.all([first, second]);
  assert.equal(executions, 1);
});

test('closing indeterminate work preserves its only resolution authority', async () => {
  const controller = createStudioIdentifiedCommandController({
    preview: async () => ({ checksum: 'preview' }),
    execute: async ({ requestIdentity }) => ({ kind: 'indeterminate', resolution: { requestIdentity } } as const),
    resolve: async () => ({ kind: 'confirmed', result: { ok: true } } as const),
    failure: () => ({ code: 'failed' }),
    resolutionRequestIdentity: (value) => value.requestIdentity,
    previewAuthority
  });
  controller.open(target, 'base.record.update', input);
  await controller.preview();
  await controller.execute('request-one');
  controller.close();
  assert.equal(controller.snapshot().kind, 'unresolved');
  assert.throws(() => controller.open(target, 'base.record.update', input));
  await controller.resolve();
  assert.equal(controller.snapshot().kind, 'confirmed');
});

test('conflict preserves the nonsecret draft and discards preview authority', async () => {
  const controller = createStudioIdentifiedCommandController({
    preview: async () => ({ checksum: 'preview' }),
    execute: async () => ({ kind: 'conflict', error: { code: 'conflict' } } as const),
    resolve: async () => { throw new Error('unused'); },
    failure: () => ({ code: 'failed' }),
    resolutionRequestIdentity: () => 'unused',
    previewAuthority
  });
  controller.open(target, 'base.record.update', input);
  await controller.preview();
  await controller.execute('request-one');
  const snapshot = controller.snapshot();
  assert.equal(snapshot.kind, 'conflict');
  if (snapshot.kind === 'conflict') assert.deepEqual(snapshot.draft.input, input);
});

test('expired preview returns to draft and pre-aborted calls do not enter transitional states', async () => {
  const controller = createStudioIdentifiedCommandController({
    preview: async () => ({ checksum: 'preview' }),
    execute: async () => ({ kind: 'confirmed', result: { ok: true } } as const),
    resolve: async () => { throw new Error('unused'); },
    failure: () => ({ code: 'failed' }),
    resolutionRequestIdentity: () => 'unused',
    previewAuthority: () => ({ coherence: 'preview-short', authorizedThroughUtc: new Date(Date.now() + 100).toISOString() })
  });
  controller.open(target, 'base.record.update', input);
  const cancelled = new AbortController();
  cancelled.abort();
  await controller.preview(cancelled.signal);
  assert.equal(controller.snapshot().kind, 'draft');
  await controller.preview();
  assert.equal(controller.snapshot().kind, 'review');
  await new Promise((resolve) => setTimeout(resolve, 120));
  assert.equal(controller.snapshot().kind, 'draft');
});

test('executing command cannot be closed or replaced before its outcome is classified', async () => {
  let finish!: () => void;
  const controller = createStudioIdentifiedCommandController({
    preview: async () => ({ checksum: 'preview' }),
    execute: async () => {
      await new Promise<void>((resolve) => { finish = resolve; });
      return { kind: 'confirmed', result: { ok: true } } as const;
    },
    resolve: async () => { throw new Error('unused'); },
    failure: () => ({ code: 'failed' }),
    resolutionRequestIdentity: () => 'unused',
    previewAuthority
  });
  controller.open(target, 'base.record.update', input);
  await controller.preview();
  const executing = controller.execute('request-one');
  await Promise.resolve();
  controller.close();
  assert.equal(controller.snapshot().kind, 'executing');
  assert.throws(() => controller.open(target, 'base.record.update', input));
  finish();
  await executing;
  assert.equal(controller.snapshot().kind, 'confirmed');
});
