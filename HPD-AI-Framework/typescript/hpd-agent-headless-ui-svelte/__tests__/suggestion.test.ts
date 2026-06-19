import { flushSync, mount, unmount } from 'svelte';
import { describe, expect, it, vi } from 'vitest';
import type { ThreadProjectionSnapshot } from '@hpd-research/hpd-agent-headless-ui';
import {
  Suggestion,
  SuggestionList,
  createSuggestionModel,
  type SuggestionSelectDetails,
  type ThreadState,
  type ThreadStateSnapshot,
} from '../src/index.js';
import SuggestionBindHarness from './fixtures/suggestion-bind-harness.svelte';
import SuggestionChildHarness from './fixtures/suggestion-child-harness.svelte';

function mountTarget(): HTMLElement {
  const target = document.createElement('div');
  document.body.append(target);
  return target;
}

function snapshot(options: {
  canSubmitText?: boolean;
  reason?: ThreadStateSnapshot['textSubmissionState']['reason'];
} = {}): ThreadStateSnapshot {
  const canSubmitText = options.canSubmitText ?? true;
  const reason = canSubmitText ? null : options.reason ?? 'busy';
  const projection: ThreadProjectionSnapshot = {
    thread: null,
    timeline: [],
    workGroups: [],
    transcriptMessages: [],
    activeTools: [],
    pendingRuntimeRequests: [],
    threadRun: canSubmitText ? null : {
      runtimeRunId: 'run-1',
      agentId: 'agent',
      status: 'active',
    },
    activity: {
      status: canSubmitText ? 'idle' : 'working',
      streaming: !canSubmitText,
      reasoning: false,
      activeToolCount: 0,
      pendingRequestCount: 0,
    },
    currentTurnId: null,
    currentConversationId: null,
    currentRunId: canSubmitText ? null : 'run-1',
    error: null,
    canSend: canSubmitText,
  };

  return {
    projection,
    timeline: [],
    workGroups: [],
    transcriptMessages: [],
    activity: projection.activity,
    activeTools: [],
    pendingRuntimeRequests: [],
    textSubmissionState: {
      canSubmit: canSubmitText,
      reason,
    },
    canSubmitText,
    loading: false,
    connected: true,
    error: null,
  };
}

function fakeThread(initialSnapshot = snapshot()): ThreadState {
  let current = initialSnapshot;
  const subscribers = new Set<(value: ThreadStateSnapshot) => void>();
  return {
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
  };
}

describe('Suggestion', () => {
  it('derives model state for populate and send modes', () => {
    expect(createSuggestionModel({ prompt: 'Hello' })).toMatchObject({
      blockedReason: null,
      canSelect: true,
      mode: 'populate',
      prompt: 'Hello',
      title: 'Hello',
    });

    expect(createSuggestionModel({ prompt: 'Hello', mode: 'send' })).toMatchObject({
      blockedReason: 'missing-thread',
      canSelect: false,
    });

    expect(createSuggestionModel({
      prompt: 'Hello',
      mode: 'send',
      thread: fakeThread(snapshot({ canSubmitText: false, reason: 'runtime-request' })),
    })).toMatchObject({
      blockedReason: 'runtime-request',
      canSelect: false,
    });
  });

  it('populates a bound target value and calls onSelect', async () => {
    const target = mountTarget();
    const onSelect = vi.fn<(details: SuggestionSelectDetails) => void>();
    const component = mount(SuggestionBindHarness, {
      target,
      props: {
        onSelect,
        prompt: 'Summarize the session',
      },
    });

    const button = target.querySelector<HTMLButtonElement>('[data-hpd-suggestion]');
    button?.click();
    await Promise.resolve();
    flushSync();

    expect(target.querySelector('[data-testid="draft"]')?.textContent)
      .toBe('Summarize the session');
    expect(onSelect).toHaveBeenCalledWith({
      additionalProperties: undefined,
      description: '',
      mode: 'populate',
      populateMode: 'replace',
      prompt: 'Summarize the session',
      thread: null,
      title: 'Summarize the session',
    });

    await unmount(component);
    target.remove();
  });

  it('appends prompt in populate mode when requested', async () => {
    const target = mountTarget();
    const component = mount(Suggestion, {
      target,
      props: {
        prompt: 'with tests',
        populateMode: 'append',
        targetValue: 'Review this file',
      },
    });

    target.querySelector<HTMLButtonElement>('[data-hpd-suggestion]')?.click();
    await Promise.resolve();
    flushSync();

    expect(target.querySelector<HTMLButtonElement>('[data-hpd-suggestion]')?.getAttribute('data-populate-mode'))
      .toBe('append');

    await unmount(component);
    target.remove();
  });

  it('sends text content and suggestion metadata through ThreadState in send mode', async () => {
    const target = mountTarget();
    const thread = fakeThread();
    const onSelect = vi.fn();
    const component = mount(Suggestion, {
      target,
      props: {
        additionalProperties: { source: 'welcome' },
        description: 'Plain language overview',
        mode: 'send',
        onSelect,
        prompt: 'Explain the architecture',
        thread,
        title: 'Explain',
      },
    });

    target.querySelector<HTMLButtonElement>('[data-hpd-suggestion]')?.click();
    await Promise.resolve();
    await Promise.resolve();

    expect(thread.sendMessage).toHaveBeenCalledWith({
      contents: [{ $type: 'text', text: 'Explain the architecture' }],
      additionalProperties: {
        source: 'welcome',
        suggestion: {
          description: 'Plain language overview',
          prompt: 'Explain the architecture',
          title: 'Explain',
        },
      },
    }, { runConfig: undefined });
    expect(onSelect).toHaveBeenCalledWith({
      additionalProperties: { source: 'welcome' },
      description: 'Plain language overview',
      mode: 'send',
      populateMode: 'replace',
      prompt: 'Explain the architecture',
      thread,
      title: 'Explain',
    });

    await unmount(component);
    target.remove();
  });

  it('disables send suggestions when the thread cannot submit text', async () => {
    const target = mountTarget();
    const thread = fakeThread(snapshot({ canSubmitText: false, reason: 'busy' }));
    const component = mount(Suggestion, {
      target,
      props: {
        mode: 'send',
        prompt: 'Try this',
        thread,
      },
    });

    const button = target.querySelector<HTMLButtonElement>('[data-hpd-suggestion]');
    expect(button?.disabled).toBe(true);
    expect(button?.getAttribute('data-blocked-reason')).toBe('busy');

    button?.click();
    await Promise.resolve();
    expect(thread.sendMessage).not.toHaveBeenCalled();

    await unmount(component);
    target.remove();
  });

  it('supports child snippets with full DOM control', async () => {
    const target = mountTarget();
    const thread = fakeThread();
    const component = mount(SuggestionChildHarness, {
      target,
      props: { thread },
    });

    const button = target.querySelector<HTMLButtonElement>('[data-testid="custom-suggestion"]');
    expect(button?.getAttribute('data-mode')).toBe('send');
    expect(button?.textContent).toContain('Review:ready');

    button?.click();
    await Promise.resolve();
    expect(thread.sendMessage).toHaveBeenCalled();

    await unmount(component);
    target.remove();
  });

  it('renders a suggestion list with structured defaults', async () => {
    const target = mountTarget();
    const component = mount(SuggestionList, {
      target,
      props: {
        suggestions: [
          {
            prompt: 'Summarize this thread',
            title: 'Summarize',
            description: 'List the important decisions',
          },
        ],
      },
    });

    expect(target.querySelector('[data-hpd-suggestion-list]')).toBeTruthy();
    expect(target.querySelector('[data-hpd-suggestion]')?.textContent)
      .toContain('Summarize');
    expect(target.querySelector('[data-hpd-suggestion]')?.textContent)
      .toContain('List the important decisions');

    await unmount(component);
    target.remove();
  });
});
