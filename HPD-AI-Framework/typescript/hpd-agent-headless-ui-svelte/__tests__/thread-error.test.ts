import { mount, unmount } from 'svelte';
import { describe, expect, it, vi } from 'vitest';
import type {
  ThreadProjectionSnapshot,
  ThreadWorkGroup,
} from '@hpd-research/hpd-agent-headless-ui';
import {
  ThreadError,
  createThreadErrorModel,
  type ThreadState,
  type ThreadStateSnapshot,
} from '../src/index.js';

function mountTarget(): HTMLElement {
  const target = document.createElement('div');
  document.body.append(target);
  return target;
}

function projection(overrides: Partial<ThreadProjectionSnapshot> = {}): ThreadProjectionSnapshot {
  const activity = {
    status: overrides.error ? 'failed' as const : 'idle' as const,
    streaming: false,
    reasoning: false,
    activeToolCount: 0,
    pendingRequestCount: 0,
  };

  return {
    thread: null,
    timeline: [],
    workGroups: [],
    transcriptMessages: [],
    activeTools: [],
    pendingRuntimeRequests: [],
    threadRun: null,
    activity,
    currentTurnId: null,
    currentConversationId: null,
    currentRunId: null,
    error: null,
    canSend: true,
    ...overrides,
  };
}

function snapshot(overrides: Partial<ThreadStateSnapshot> = {}): ThreadStateSnapshot {
  const baseProjection = overrides.projection ?? projection();
  return {
    projection: baseProjection,
    timeline: [],
    workGroups: baseProjection.workGroups,
    transcriptMessages: [],
    activity: baseProjection.activity,
    activeTools: baseProjection.activeTools,
    pendingRuntimeRequests: baseProjection.pendingRuntimeRequests,
    textSubmissionState: baseProjection.error
      ? { canSubmit: false, reason: 'error' }
      : { canSubmit: true, reason: null },
    canSubmitText: !baseProjection.error,
    loading: false,
    connected: true,
    error: baseProjection.error,
    ...overrides,
  };
}

function fakeThread(state: ThreadStateSnapshot): ThreadState {
  return {
    controller: {} as ThreadState['controller'],
    subscribe(run) {
      run(state);
      return () => {};
    },
    getSnapshot: () => state,
    clearError: vi.fn(),
    start: vi.fn(async () => {}),
    rehydrate: vi.fn(async () => {}),
    connect: vi.fn(async () => {}),
    disconnect: vi.fn(async () => {}),
    dispose: vi.fn(async () => {}),
    sendMessage: vi.fn(async () => {}),
    run: vi.fn(async () => undefined),
    respond: vi.fn(async () => undefined),
    interrupt: vi.fn(async () => {}),
    approve: vi.fn(async () => undefined),
    deny: vi.fn(async () => undefined),
    clarify: vi.fn(async () => undefined),
    respondToClientTool: vi.fn(async () => undefined),
  };
}

function failedWorkGroup(): ThreadWorkGroup {
  return {
    id: 'work-1',
    turnId: 'turn-1',
    conversationId: 'conversation-1',
    runId: 'run-1',
    status: 'failed',
    label: 'Work failed',
    openByDefault: true,
    error: 'work failed',
    parts: [],
  };
}

describe('createThreadErrorModel', () => {
  it('normalizes projection errors', () => {
    const state = snapshot({
      projection: projection({
        workGroups: [failedWorkGroup()],
      }),
    });

    const model = createThreadErrorModel(fakeThread(state));

    expect(model.hasError).toBe(true);
    expect(model.error).toMatchObject({ kind: 'work', message: 'work failed' });
    expect(model.errors).toHaveLength(1);
  });

  it('includes controller errors that are not projection errors', () => {
    const state = snapshot({ error: 'connection lost' });
    const thread = fakeThread(state);
    const model = createThreadErrorModel(thread);

    expect(model.error).toMatchObject({
      kind: 'controller',
      message: 'connection lost',
    });

    model.actions.clear();
    expect(thread.clearError).toHaveBeenCalledOnce();
  });
});

describe('ThreadError', () => {
  it('renders nothing when the thread has no error', async () => {
    const target = mountTarget();
    const component = mount(ThreadError, {
      target,
      props: { thread: fakeThread(snapshot()) },
    });

    expect(target.querySelector('[data-hpd-thread-error]')).toBeNull();

    await unmount(component);
    target.remove();
  });

  it('renders the latest thread error as an accessible alert', async () => {
    const target = mountTarget();
    const component = mount(ThreadError, {
      target,
      props: {
        thread: fakeThread(snapshot({
          projection: projection({
            workGroups: [failedWorkGroup()],
          }),
        })),
      },
    });

    const error = target.querySelector('[data-hpd-thread-error]');
    expect(error).not.toBeNull();
    expect(error?.getAttribute('role')).toBe('alert');
    expect(error?.getAttribute('aria-live')).toBe('polite');
    expect(error?.getAttribute('data-error-kind')).toBe('work');
    expect(error?.textContent).toContain('work failed');

    await unmount(component);
    target.remove();
  });

  it('lists every normalized error when showAll is enabled', async () => {
    const target = mountTarget();
    const component = mount(ThreadError, {
      target,
      props: {
        showAll: true,
        thread: fakeThread(snapshot({
          error: 'connection lost',
          projection: projection({
            workGroups: [failedWorkGroup()],
          }),
        })),
      },
    });

    const items = [...target.querySelectorAll('[data-hpd-thread-error-list-item]')];
    expect(items).toHaveLength(2);
    expect(items[0]?.textContent).toContain('work failed');
    expect(items[1]?.textContent).toContain('connection lost');

    await unmount(component);
    target.remove();
  });

  it('renders a recoverable clear action for controller errors', async () => {
    const target = mountTarget();
    const thread = fakeThread(snapshot({ error: 'connection lost' }));
    const component = mount(ThreadError, {
      target,
      props: { thread },
    });

    const error = target.querySelector('[data-hpd-thread-error]');
    const clear = target.querySelector<HTMLButtonElement>('[data-hpd-thread-error-clear]');
    expect(error?.getAttribute('data-recoverable')).toBe('');
    expect(clear).not.toBeNull();

    clear?.click();
    expect(thread.clearError).toHaveBeenCalledOnce();

    await unmount(component);
    target.remove();
  });
});
