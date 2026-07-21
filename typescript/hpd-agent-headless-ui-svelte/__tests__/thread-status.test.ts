import { flushSync, mount, unmount } from 'svelte';
import { describe, expect, it, vi } from 'vitest';
import type {
  RuntimeRequest,
  ThreadProjectionSnapshot,
  ToolCall,
} from '@hpd-research/hpd-agent-headless-ui';
import {
  ThreadStatus,
  ThreadStatusMetrics,
  createThreadStatusModel,
  type ThreadState,
  type ThreadStateSnapshot,
} from '../src/index.js';
import ThreadStatusChildHarness from './fixtures/thread-status-child-harness.svelte';

function mountTarget(): HTMLElement {
  const target = document.createElement('div');
  document.body.append(target);
  return target;
}

function toolCall(): ToolCall {
  return {
    callId: 'tool-1',
    name: 'SearchDocs',
    messageId: 'message-1',
    status: 'executing',
    startTime: new Date('2026-01-01T00:00:00.000Z'),
    turnId: null,
    conversationId: null,
    executionId: null,
  };
}

function runtimeRequest(): RuntimeRequest {
  return {
    id: 'perm-1',
    kind: 'permission',
    sourceName: 'PermissionMiddleware',
    requestEventType: 'PERMISSION_REQUEST',
    expectedResponseEventType: 'PERMISSION_RESPONSE',
    request: {
      permissionId: 'perm-1',
      sourceName: 'PermissionMiddleware',
      functionName: 'Bash',
      description: 'Run tests',
      callId: 'call-1',
      arguments: { command: 'npm test' },
    },
  };
}

function snapshot(options: {
  activeTools?: ToolCall[];
  connected?: boolean;
  error?: string | null;
  loading?: boolean;
  pendingRuntimeRequests?: RuntimeRequest[];
  reasoning?: boolean;
  streaming?: boolean;
} = {}): ThreadStateSnapshot {
  const activeTools = options.activeTools ?? [];
  const pendingRuntimeRequests = options.pendingRuntimeRequests ?? [];
  const streaming = options.streaming ?? false;
  const reasoning = options.reasoning ?? false;
  const busy = streaming || reasoning || activeTools.length > 0 || pendingRuntimeRequests.length > 0;
  const activity = {
    status: options.error
      ? 'failed' as const
      : pendingRuntimeRequests.length > 0
        ? 'requesting' as const
        : busy
          ? 'working' as const
          : 'idle' as const,
    streaming: busy,
    reasoning,
    activeToolCount: activeTools.length,
    pendingRequestCount: pendingRuntimeRequests.length,
  };
  const projection: ThreadProjectionSnapshot = {
    thread: null,
    timeline: [],
    workGroups: [],
    transcriptMessages: [],
    activeTools,
    pendingRuntimeRequests,
    threadExecution: busy
      ? {
          threadExecutionId: 'run-1',
          agentId: 'agent',
          status: 'active',
        }
      : null,
    activity,
    currentTurnId: null,
    currentConversationId: null,
    currentExecutionId: busy ? 'run-1' : null,
    error: options.error ?? null,
    canSend: !busy,
  };

  return {
    projection,
    timeline: [],
    workGroups: [],
    transcriptMessages: [],
    activity,
    activeTools,
    pendingRuntimeRequests,
    textSubmissionState: busy || options.error
      ? { canSubmit: false, reason: options.error ? 'error' : 'busy' }
      : { canSubmit: true, reason: null },
    canSubmitText: !busy && !options.error,
    loading: options.loading ?? false,
    connected: options.connected ?? true,
    error: options.error ?? null,
  };
}

function fakeThread(initialSnapshot: ThreadStateSnapshot): ThreadState & {
  emit(nextSnapshot: ThreadStateSnapshot): void;
} {
  let current = initialSnapshot;
  const subscribers = new Set<(value: ThreadStateSnapshot) => void>();
  const thread = {
    controller: {} as ThreadState['controller'],
    subscribe(run: (value: ThreadStateSnapshot) => void) {
      subscribers.add(run);
      run(current);
      return () => {
        subscribers.delete(run);
      };
    },
    getSnapshot: () => current,
    clearError: () => {},
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
    answerClientToolRequest: vi.fn(async () => undefined),
    emit(nextSnapshot: ThreadStateSnapshot) {
      current = nextSnapshot;
      for (const subscriber of subscribers) subscriber(current);
    },
  };

  return thread as ThreadState & {
    emit(nextSnapshot: ThreadStateSnapshot): void;
  };
}

describe('ThreadStatus', () => {
  it('derives display state with the expected priority', () => {
    expect(createThreadStatusModel(snapshot({ loading: true, error: 'late' })).state)
      .toBe('loading');
    expect(createThreadStatusModel(snapshot({ error: 'failed', connected: false })).state)
      .toBe('error');
    expect(createThreadStatusModel(snapshot({ connected: false, streaming: true })).state)
      .toBe('disconnected');
    expect(createThreadStatusModel(snapshot({
      pendingRuntimeRequests: [runtimeRequest()],
      activeTools: [toolCall()],
    })).state).toBe('requesting');
    expect(createThreadStatusModel(snapshot({ activeTools: [toolCall()] })).state)
      .toBe('working');
    expect(createThreadStatusModel(snapshot()).state).toBe('ready');
  });

  it('renders default status attributes and updates from ThreadState', async () => {
    const target = mountTarget();
    const thread = fakeThread(snapshot({ activeTools: [toolCall()] }));
    const component = mount(ThreadStatus, {
      target,
      props: { thread },
    });

    let status = target.querySelector('[data-hpd-thread-status]');
    expect(status?.getAttribute('data-status-state')).toBe('working');
    expect(status?.getAttribute('aria-busy')).toBe('true');
    expect(status?.textContent).toContain('SearchDocs running');
    expect(target.querySelector('[data-hpd-thread-status-tools]')).toBeNull();
    expect(target.querySelector('[data-hpd-thread-status-requests]')).toBeNull();

    thread.emit(snapshot());
    flushSync();

    status = target.querySelector('[data-hpd-thread-status]');
    expect(status?.getAttribute('data-status-state')).toBe('ready');
    expect(status?.getAttribute('aria-busy')).toBe('false');
    expect(status?.textContent).toContain('Ready');

    await unmount(component);
    target.remove();
  });

  it('renders passive metrics through ThreadStatusMetrics', async () => {
    const target = mountTarget();
    const status = createThreadStatusModel(snapshot({ activeTools: [toolCall()] }));
    const component = mount(ThreadStatusMetrics, {
      target,
      props: { status },
    });

    const metrics = target.querySelector('[data-hpd-thread-status-metrics]');
    expect(metrics?.getAttribute('data-blocked-reason')).toBe('busy');
    expect(metrics?.textContent).toContain('1 tool');
    expect(metrics?.textContent).toContain('busy');

    await unmount(component);
    target.remove();
  });

  it('supports child snippets with full DOM control', async () => {
    const target = mountTarget();
    const thread = fakeThread(snapshot({
      pendingRuntimeRequests: [runtimeRequest()],
    }));
    const component = mount(ThreadStatusChildHarness, {
      target,
      props: { thread },
    });

    const status = target.querySelector('[data-testid="custom-status"]');
    expect(status?.tagName).toBe('ASIDE');
    expect(status?.getAttribute('data-status-state')).toBe('requesting');
    expect(status?.textContent).toContain('requesting:1 request pending');

    await unmount(component);
    target.remove();
  });
});
