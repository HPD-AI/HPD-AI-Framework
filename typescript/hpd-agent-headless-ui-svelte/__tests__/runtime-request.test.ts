import { flushSync, mount, unmount } from 'svelte';
import { describe, expect, it, vi } from 'vitest';
import type {
  RuntimeRequest,
  ThreadProjectionSnapshot,
} from '@hpd-research/hpd-agent-headless-ui';
import {
  RuntimeRequest as RuntimeRequestComponent,
  ThreadRuntimeRequests,
  type ThreadState,
  type ThreadStateSnapshot,
} from '../src/index.js';
import RuntimeRequestSnippetHarness from './fixtures/runtime-request-snippet-harness.svelte';

function mountTarget(): HTMLElement {
  const target = document.createElement('div');
  document.body.append(target);
  return target;
}

function projection(requests: RuntimeRequest[] = []): ThreadProjectionSnapshot {
  const activity = {
    status: requests.length === 0 ? 'idle' as const : 'requesting' as const,
    streaming: requests.length > 0,
    reasoning: false,
    activeToolCount: 0,
    pendingRequestCount: requests.length,
  };
  return {
    thread: null,
    timeline: [],
    workGroups: [],
    transcriptMessages: [],
    activeTools: [],
    pendingRuntimeRequests: requests,
    threadRun: null,
    activity,
    currentTurnId: null,
    currentConversationId: null,
    currentRunId: null,
    error: null,
    canSend: requests.length === 0,
  };
}

function snapshot(requests: RuntimeRequest[] = []): ThreadStateSnapshot {
  const projectionSnapshot = projection(requests);
  return {
    projection: projectionSnapshot,
    timeline: [],
    workGroups: [],
    transcriptMessages: [],
    activity: projectionSnapshot.activity,
    activeTools: [],
    pendingRuntimeRequests: requests,
    textSubmissionState: requests.length === 0
      ? { canSubmit: true, reason: null }
      : { canSubmit: false, reason: 'busy' },
    canSubmitText: requests.length === 0,
    loading: false,
    connected: true,
    error: null,
  };
}

function fakeThread(initialSnapshot: ThreadStateSnapshot = snapshot()): ThreadState & {
  approve: ReturnType<typeof vi.fn>;
  clarify: ReturnType<typeof vi.fn>;
  deny: ReturnType<typeof vi.fn>;
  emit(nextSnapshot: ThreadStateSnapshot): void;
  respond: ReturnType<typeof vi.fn>;
  respondToClientTool: ReturnType<typeof vi.fn>;
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
    respondToClientTool: vi.fn(async () => undefined),
    emit(nextSnapshot: ThreadStateSnapshot) {
      current = nextSnapshot;
      for (const subscriber of subscribers) subscriber(current);
    },
  };
  return thread as ThreadState & {
    approve: ReturnType<typeof vi.fn>;
    clarify: ReturnType<typeof vi.fn>;
    deny: ReturnType<typeof vi.fn>;
    emit(nextSnapshot: ThreadStateSnapshot): void;
    respond: ReturnType<typeof vi.fn>;
    respondToClientTool: ReturnType<typeof vi.fn>;
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
      description: 'Run a command',
      callId: 'call-1',
      arguments: { command: 'npm test' },
    },
  };
}

function clarificationRequest(): RuntimeRequest {
  return {
    id: 'clarify-1',
    kind: 'clarification',
    sourceName: 'ClarificationFunction',
    requestEventType: 'CLARIFICATION_REQUEST',
    expectedResponseEventType: 'CLARIFICATION_RESPONSE',
    request: {
      requestId: 'clarify-1',
      sourceName: 'ClarificationFunction',
      question: 'Which tenant?',
      options: ['dev', 'prod'],
    },
  };
}

function clientToolRequest(): RuntimeRequest {
  return {
    id: 'tool-1',
    kind: 'client-tool',
    sourceName: 'HPD.Agent.ClientTools',
    requestEventType: 'CLIENT_TOOL_INVOKE_REQUEST',
    expectedResponseEventType: 'CLIENT_TOOL_INVOKE_RESPONSE',
    responsePolicy: 'targetedResponder',
    visibility: 'allObservers',
    request: {
      requestId: 'tool-1',
      sourceName: 'HPD.Agent.ClientTools',
      toolName: 'pickFile',
      callId: 'call-2',
      description: 'Pick a file',
      arguments: { accept: 'image/*' },
    },
  };
}

function customRequest(): RuntimeRequest {
  return {
    id: 'custom-1',
    kind: 'custom',
    sourceName: 'custom-source',
    requestEventType: 'CUSTOM_REVIEW_REQUEST',
    expectedResponseEventType: 'CUSTOM_REVIEW_RESPONSE',
    responsePolicy: 'firstValidResponseWins',
    visibility: 'allObservers',
    event: {
      type: 'CUSTOM_REVIEW_REQUEST',
      requestId: 'custom-1',
      sourceName: 'custom-source',
      prompt: 'Review this custom request',
    },
  };
}

function setValue(control: HTMLInputElement | HTMLTextAreaElement, value: string): void {
  control.value = value;
  control.dispatchEvent(new InputEvent('input', { bubbles: true }));
  flushSync();
}

async function tick(): Promise<void> {
  await Promise.resolve();
  await Promise.resolve();
  flushSync();
}

describe('RuntimeRequest', () => {
  it('renders a permission request and delegates approve and deny', async () => {
    const target = mountTarget();
    const thread = fakeThread();
    const onDeny = vi.fn();
    const component = mount(RuntimeRequestComponent, {
      target,
      props: {
        item: permissionRequest(),
        onDeny,
        thread,
      },
    });

    expect(target.querySelector('[data-hpd-runtime-request]')).not.toBeNull();
    expect(target.querySelector('[data-request-kind="permission"]')).not.toBeNull();
    expect(target.textContent).toContain('Run a command');

    const input = target.querySelector('input') as HTMLInputElement;
    setValue(input, 'too risky');
    (target.querySelector('[data-hpd-runtime-request-deny]') as HTMLButtonElement).click();
    await tick();
    expect(thread.deny).toHaveBeenCalledWith('perm-1', 'too risky');
    expect(onDeny).toHaveBeenCalledWith({ item: permissionRequest(), reason: 'too risky' });

    (target.querySelector('[data-hpd-runtime-request-approve]') as HTMLButtonElement).click();
    await tick();
    expect(thread.approve).toHaveBeenCalledWith('perm-1', undefined);

    await unmount(component);
    target.remove();
  });

  it('submits clarification answers and option clicks', async () => {
    const target = mountTarget();
    const thread = fakeThread();
    const component = mount(RuntimeRequestComponent, {
      target,
      props: {
        item: clarificationRequest(),
        thread,
      },
    });

    const option = Array.from(target.querySelectorAll('button'))
      .find((button) => button.textContent === 'prod') as HTMLButtonElement;
    option.click();
    await tick();
    expect(thread.clarify).toHaveBeenCalledWith('clarify-1', 'prod');

    const input = target.querySelector('input') as HTMLInputElement;
    setValue(input, 'staging');
    (target.querySelector('form') as HTMLFormElement)
      .dispatchEvent(new SubmitEvent('submit', { bubbles: true, cancelable: true }));
    await tick();
    expect(thread.clarify).toHaveBeenCalledWith('clarify-1', 'staging');

    await unmount(component);
    target.remove();
  });

  it('submits client-tool responses', async () => {
    const target = mountTarget();
    const thread = fakeThread();
    const component = mount(RuntimeRequestComponent, {
      target,
      props: {
        item: clientToolRequest(),
        thread,
      },
    });

    const textarea = target.querySelector('textarea') as HTMLTextAreaElement;
    setValue(textarea, 'selected file');
    (target.querySelector('form') as HTMLFormElement)
      .dispatchEvent(new SubmitEvent('submit', { bubbles: true, cancelable: true }));
    await tick();

    expect(thread.respondToClientTool).toHaveBeenCalledWith('tool-1', 'selected file', undefined);

    await unmount(component);
    target.remove();
  });

  it('submits custom request responses through the generic respond path', async () => {
    const target = mountTarget();
    const thread = fakeThread();
    const component = mount(RuntimeRequestComponent, {
      target,
      props: {
        item: customRequest(),
        thread,
      },
    });

    const textarea = target.querySelector('textarea') as HTMLTextAreaElement;
    setValue(textarea, 'approved');
    (target.querySelector('form') as HTMLFormElement)
      .dispatchEvent(new SubmitEvent('submit', { bubbles: true, cancelable: true }));
    await tick();

    expect(thread.respond).toHaveBeenCalledWith({
      type: 'CUSTOM_REVIEW_RESPONSE',
      requestId: 'custom-1',
      sourceName: 'custom-source',
      value: 'approved',
    });

    await unmount(component);
    target.remove();
  });

  it('supports named kind snippets', async () => {
    const target = mountTarget();
    const thread = fakeThread();
    const component = mount(RuntimeRequestSnippetHarness, {
      target,
      props: {
        item: permissionRequest(),
        thread,
      },
    });

    target.querySelector<HTMLButtonElement>('[data-testid="snippet-approve"]')?.click();
    await tick();

    expect(thread.approve).toHaveBeenCalledWith('perm-1', 'allow-once');
    expect(target.querySelector('[data-kind-prop="permission"]')).not.toBeNull();

    await unmount(component);
    target.remove();
  });
});

describe('ThreadRuntimeRequests', () => {
  it('renders static requests and an empty state', async () => {
    const target = mountTarget();
    const component = mount(ThreadRuntimeRequests, {
      target,
      props: {
        requests: [permissionRequest(), customRequest()],
      },
    });

    expect(target.querySelectorAll('[data-hpd-runtime-request]')).toHaveLength(2);
    expect(target.querySelector('[data-hpd-thread-runtime-requests]')).not.toBeNull();

    await unmount(component);

    const empty = mount(ThreadRuntimeRequests, {
      target,
      props: {
        requests: [],
      },
    });
    expect(target.querySelector('[data-hpd-thread-runtime-requests]')?.getAttribute('data-empty')).toBe('');

    await unmount(empty);
    target.remove();
  });

  it('subscribes to ThreadState pending runtime requests', async () => {
    const target = mountTarget();
    const thread = fakeThread(snapshot([permissionRequest()]));
    const component = mount(ThreadRuntimeRequests, {
      target,
      props: {
        thread,
      },
    });
    flushSync();

    expect(target.querySelectorAll('[data-hpd-runtime-request]')).toHaveLength(1);

    thread.emit(snapshot([permissionRequest(), clientToolRequest()]));
    flushSync();
    expect(target.querySelectorAll('[data-hpd-runtime-request]')).toHaveLength(2);

    await unmount(component);
    target.remove();
  });
});
