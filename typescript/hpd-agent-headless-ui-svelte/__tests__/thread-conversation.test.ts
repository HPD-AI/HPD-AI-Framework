import { flushSync, mount, unmount } from 'svelte';
import { describe, expect, it, vi } from 'vitest';
import type {
  Message,
  RuntimeRequest,
  ThreadProjectionSnapshot,
} from '@hpd-research/hpd-agent-headless-ui';
import type { ThreadState, ThreadStateSnapshot } from '../src/index.js';
import ThreadConversationHarness from './fixtures/thread-conversation-harness.svelte';

function mountTarget(): HTMLElement {
  const target = document.createElement('div');
  document.body.append(target);
  return target;
}

function message(id: string, role: Message['role'], content: string): Message {
  return {
    id,
    role,
    content,
    streaming: false,
    thinking: false,
    timestamp: new Date(),
    toolCalls: [],
    turnId: null,
    conversationId: null,
    executionId: null,
    placement: 'transcript',
  };
}

function permissionRequest(): RuntimeRequest {
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
      description: 'Run the test command',
      callId: 'call-1',
      arguments: { command: 'npm test' },
    },
  };
}

function snapshot(
  messages: Message[] = [],
  requests: RuntimeRequest[] = [],
): ThreadStateSnapshot {
  const blocked = requests.length > 0;
  const activity = {
    status: blocked ? 'requesting' as const : 'idle' as const,
    streaming: blocked,
    reasoning: false,
    activeToolCount: 0,
    pendingRequestCount: requests.length,
  };
  const projection: ThreadProjectionSnapshot = {
    thread: null,
    timeline: [
      ...messages.map((item) => ({
        type: 'message' as const,
        id: `message:${item.id}`,
        message: item,
        turnId: item.turnId,
        conversationId: item.conversationId,
        executionId: item.executionId,
      })),
      ...requests.map((request) => ({
        type: 'runtime-request' as const,
        id: `request:${request.id}`,
        request,
        turnId: null,
        conversationId: null,
        executionId: blocked ? 'test-run' : null,
      })),
    ],
    workGroups: [],
    transcriptMessages: messages,
    activeTools: [],
    pendingRuntimeRequests: requests,
    threadExecution: blocked
      ? {
          threadExecutionId: 'test-run',
          agentId: 'agent',
          status: 'active',
        }
      : null,
    activity,
    currentTurnId: null,
    currentConversationId: null,
    currentExecutionId: blocked ? 'test-run' : null,
    error: null,
    canSend: !blocked,
  };

  return {
    projection,
    timeline: projection.timeline,
    workGroups: [],
    transcriptMessages: messages,
    activity,
    activeTools: [],
    pendingRuntimeRequests: requests,
    textSubmissionState: blocked
      ? { canSubmit: false, reason: 'busy' }
      : { canSubmit: true, reason: null },
    canSubmitText: !blocked,
    loading: false,
    connected: true,
    error: null,
  };
}

function fakeThread(initialSnapshot: ThreadStateSnapshot): ThreadState & {
  approve: ReturnType<typeof vi.fn>;
  sendMessage: ReturnType<typeof vi.fn>;
} {
  let current = initialSnapshot;
  const subscribers = new Set<(value: ThreadStateSnapshot) => void>();

  function emit(nextSnapshot: ThreadStateSnapshot): void {
    current = nextSnapshot;
    for (const subscriber of subscribers) subscriber(current);
  }

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
    sendMessage: vi.fn(async (input: { contents: Array<{ $type: string; text?: string }> }) => {
      const text = input.contents
        .filter((content) => content.$type === 'text')
        .map((content) => content.text ?? '')
        .join('');
      emit(snapshot([
        ...current.transcriptMessages,
        message(`user-${current.transcriptMessages.length}`, 'user', text),
      ]));
    }),
    run: vi.fn(async () => undefined),
    respond: vi.fn(async () => undefined),
    interrupt: vi.fn(async () => {}),
    approve: vi.fn(async () => {
      emit(snapshot(current.transcriptMessages));
    }),
    deny: vi.fn(async () => undefined),
    clarify: vi.fn(async () => undefined),
    answerClientToolRequest: vi.fn(async () => undefined),
  };

  return thread as ThreadState & {
    approve: ReturnType<typeof vi.fn>;
    sendMessage: ReturnType<typeof vi.fn>;
  };
}

function input(textarea: HTMLTextAreaElement, value: string): void {
  textarea.value = value;
  textarea.dispatchEvent(new InputEvent('input', { bubbles: true }));
  flushSync();
}

async function tick(): Promise<void> {
  await Promise.resolve();
  await Promise.resolve();
  flushSync();
}

describe('Thread conversation composition', () => {
  it('renders messages, blocks composer for runtime requests, and unblocks after response', async () => {
    const target = mountTarget();
    const thread = fakeThread(snapshot([
      message('assistant-1', 'assistant', 'Ready after approval.'),
    ], [permissionRequest()]));
    const component = mount(ThreadConversationHarness, {
      target,
      props: { thread },
    });
    flushSync();

    expect(target.querySelector('[data-hpd-message]')?.textContent)
      .toContain('Ready after approval.');
    expect(target.querySelectorAll('[data-hpd-runtime-request]')).toHaveLength(1);
    expect(target.querySelector('[data-hpd-thread-runtime-requests]')).not.toBeNull();

    const submit = target.querySelector('[data-hpd-thread-composer-submit]') as HTMLButtonElement;
    expect(submit.disabled).toBe(true);

    (target.querySelector('[data-hpd-runtime-request-approve]') as HTMLButtonElement).click();
    await tick();

    expect(thread.approve).toHaveBeenCalledWith('perm-1', undefined);
    expect(target.querySelectorAll('[data-hpd-runtime-request]')).toHaveLength(0);
    expect(target.querySelector('[data-hpd-thread-composer]')?.getAttribute('data-empty')).toBe('');

    const textarea = target.querySelector('textarea') as HTMLTextAreaElement;
    input(textarea, 'continue please');
    expect(submit.disabled).toBe(false);
    (target.querySelector('form') as HTMLFormElement)
      .dispatchEvent(new SubmitEvent('submit', { bubbles: true, cancelable: true }));
    await tick();

    expect(thread.sendMessage).toHaveBeenCalledWith(
      { contents: [{ $type: 'text', text: 'continue please' }] },
      { runConfig: { modelId: 'story-test' } },
    );
    expect(target.textContent).toContain('continue please');

    await unmount(component);
    target.remove();
  });

  it('renders runtime requests inline when conversation placement is timeline', async () => {
    const target = mountTarget();
    const thread = fakeThread(snapshot([
      message('assistant-1', 'assistant', 'Ready after approval.'),
    ], [permissionRequest()]));
    const component = mount(ThreadConversationHarness, {
      target,
      props: {
        runtimeRequestPlacement: 'timeline',
        thread,
      },
    });
    flushSync();

    expect(target.querySelectorAll('[data-hpd-runtime-request]')).toHaveLength(1);
    expect(target.querySelector('[data-hpd-thread-runtime-requests]')).toBeNull();

    await unmount(component);
    target.remove();
  });
});
