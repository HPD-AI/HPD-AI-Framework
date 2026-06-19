import { flushSync, mount, unmount } from 'svelte';
import { describe, expect, it, vi } from 'vitest';
import type { ThreadProjectionSnapshot } from '@hpd-research/hpd-agent-headless-ui';
import {
  applyThreadComposerAutosize,
  type ThreadComposerAutosizeMetrics,
  type ThreadState,
  type ThreadStateSnapshot,
} from '../src/index.js';
import ThreadComposer from '../src/thread-composer/thread-composer.svelte';
import ThreadComposerBindHarness from './fixtures/thread-composer-bind-harness.svelte';
import ThreadComposerChildHarness from './fixtures/thread-composer-child-harness.svelte';

function mountTarget(): HTMLElement {
  const target = document.createElement('div');
  document.body.append(target);
  return target;
}

function snapshot(overrides: Partial<ThreadStateSnapshot> = {}): ThreadStateSnapshot {
  const projection = {
    thread: null,
    timeline: [],
    workGroups: [],
    transcriptMessages: [],
    activeTools: [],
    pendingRuntimeRequests: [],
    threadRun: null,
    activity: {
      status: 'idle' as const,
      streaming: false,
      reasoning: false,
      activeToolCount: 0,
      pendingRequestCount: 0,
    },
    currentTurnId: null,
    currentConversationId: null,
    currentRunId: null,
    error: null,
    canSend: true,
  } satisfies ThreadProjectionSnapshot;

  return {
    projection,
    timeline: [],
    workGroups: [],
    transcriptMessages: [],
    activity: projection.activity,
    activeTools: [],
    pendingRuntimeRequests: [],
    textSubmissionState: { canSubmit: true, reason: null },
    canSubmitText: true,
    loading: false,
    connected: true,
    error: null,
    ...overrides,
  };
}

function fakeThread(initialSnapshot: ThreadStateSnapshot = snapshot()): ThreadState & {
  emit(nextSnapshot: ThreadStateSnapshot): void;
  sendMessage: ReturnType<typeof vi.fn>;
  interrupt: ReturnType<typeof vi.fn>;
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
    emit(nextSnapshot: ThreadStateSnapshot): void;
    sendMessage: ReturnType<typeof vi.fn>;
    interrupt: ReturnType<typeof vi.fn>;
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

describe('ThreadComposer', () => {
  it('renders the default form and generated attributes', async () => {
    const target = mountTarget();
    const thread = fakeThread();
    const component = mount(ThreadComposer, {
      target,
      props: { thread, autosize: false },
    });

    expect(target.querySelector('[data-hpd-thread-composer]')).not.toBeNull();
    expect(target.querySelector('[data-hpd-thread-composer-textarea]')).not.toBeNull();
    expect(target.querySelector('[data-hpd-thread-composer-submit]')).not.toBeNull();
    expect(target.querySelector('[data-hpd-thread-composer]')?.getAttribute('data-empty')).toBe('');

    await unmount(component);
    target.remove();
  });

  it('submits trimmed text through ThreadState.sendMessage', async () => {
    const target = mountTarget();
    const thread = fakeThread();
    const runConfig = { skipTools: true };
    const component = mount(ThreadComposer, {
      target,
      props: { thread, autosize: false, runConfig },
    });

    const textarea = target.querySelector('textarea') as HTMLTextAreaElement;
    const form = target.querySelector('form') as HTMLFormElement;
    input(textarea, '  hello HPD  ');
    form.dispatchEvent(new SubmitEvent('submit', { bubbles: true, cancelable: true }));
    await tick();

    expect(thread.sendMessage).toHaveBeenCalledWith(
      { contents: [{ $type: 'text', text: 'hello HPD' }] },
      { runConfig },
    );
    await tick();
    expect(textarea.value).toBe('');

    await unmount(component);
    target.remove();
  });

  it('submits quote as message additionalProperties', async () => {
    const target = mountTarget();
    const thread = fakeThread();
    const quote = {
      text: 'selected context',
      messageId: 'message-1',
      source: 'selection',
    };
    const component = mount(ThreadComposer, {
      target,
      props: {
        thread,
        autosize: false,
        quote,
        additionalProperties: {
          app: 'test',
        },
      },
    });

    const textarea = target.querySelector('textarea') as HTMLTextAreaElement;
    const form = target.querySelector('form') as HTMLFormElement;
    input(textarea, 'reply to this');
    form.dispatchEvent(new SubmitEvent('submit', { bubbles: true, cancelable: true }));
    await tick();

    expect(thread.sendMessage).toHaveBeenCalledWith(
      {
        contents: [{ $type: 'text', text: 'reply to this' }],
        additionalProperties: {
          app: 'test',
          quote,
        },
      },
      { runConfig: undefined },
    );

    await unmount(component);
    target.remove();
  });

  it('blocks empty and busy submissions', async () => {
    const target = mountTarget();
    const thread = fakeThread(snapshot({
      textSubmissionState: { canSubmit: false, reason: 'busy' },
      canSubmitText: false,
    }));
    const component = mount(ThreadComposer, {
      target,
      props: { thread, autosize: false },
    });

    const textarea = target.querySelector('textarea') as HTMLTextAreaElement;
    const form = target.querySelector('form') as HTMLFormElement;
    form.dispatchEvent(new SubmitEvent('submit', { bubbles: true, cancelable: true }));
    input(textarea, 'still busy');
    form.dispatchEvent(new SubmitEvent('submit', { bubbles: true, cancelable: true }));
    await tick();

    expect(thread.sendMessage).not.toHaveBeenCalled();
    expect(target.querySelector('[data-hpd-thread-composer]')?.getAttribute('data-blocked-reason'))
      .toBe('busy');

    await unmount(component);
    target.remove();
  });

  it('exposes runtime request blocking distinctly from busy work', async () => {
    const target = mountTarget();
    const thread = fakeThread(snapshot({
      textSubmissionState: { canSubmit: false, reason: 'runtime-request' },
      canSubmitText: false,
    }));
    const component = mount(ThreadComposerChildHarness, {
      target,
      props: { thread, autosize: false },
    });

    const textarea = target.querySelector('[data-testid="custom-textarea"]') as HTMLTextAreaElement;
    const form = target.querySelector('[data-testid="custom-composer"]') as HTMLFormElement;
    input(textarea, 'answer request first');
    form.dispatchEvent(new SubmitEvent('submit', { bubbles: true, cancelable: true }));
    await tick();

    expect(thread.sendMessage).not.toHaveBeenCalled();
    expect(form.getAttribute('data-blocked-reason')).toBe('runtime-request');
    expect(target.querySelector('[data-testid="custom-submit"]')?.textContent?.trim())
      .toBe('runtime-request');

    await unmount(component);
    target.remove();
  });

  it('submits on Enter and preserves Shift+Enter for newline', async () => {
    const target = mountTarget();
    const thread = fakeThread();
    const component = mount(ThreadComposer, {
      target,
      props: { thread, autosize: false },
    });

    const textarea = target.querySelector('textarea') as HTMLTextAreaElement;
    input(textarea, 'line one');
    textarea.dispatchEvent(new KeyboardEvent('keydown', {
      bubbles: true,
      cancelable: true,
      key: 'Enter',
      shiftKey: true,
    }));
    await tick();
    expect(thread.sendMessage).not.toHaveBeenCalled();

    textarea.dispatchEvent(new KeyboardEvent('keydown', {
      bubbles: true,
      cancelable: true,
      key: 'Enter',
    }));
    await tick();
    expect(thread.sendMessage).toHaveBeenCalledWith(
      { contents: [{ $type: 'text', text: 'line one' }] },
      { runConfig: undefined },
    );

    await unmount(component);
    target.remove();
  });

  it('supports explicit keyboard submit modes', async () => {
    const target = mountTarget();
    const thread = fakeThread();
    const component = mount(ThreadComposer, {
      target,
      props: { thread, autosize: false, submitMode: 'mod-enter' },
    });

    const textarea = target.querySelector('textarea') as HTMLTextAreaElement;
    input(textarea, 'mod submit');
    textarea.dispatchEvent(new KeyboardEvent('keydown', {
      bubbles: true,
      cancelable: true,
      key: 'Enter',
    }));
    await tick();
    expect(thread.sendMessage).not.toHaveBeenCalled();

    textarea.dispatchEvent(new KeyboardEvent('keydown', {
      bubbles: true,
      cancelable: true,
      ctrlKey: true,
      key: 'Enter',
    }));
    await tick();
    expect(thread.sendMessage).toHaveBeenCalledWith(
      { contents: [{ $type: 'text', text: 'mod submit' }] },
      { runConfig: undefined },
    );

    await unmount(component);
    target.remove();
  });

  it('can keep the draft after successful submit', async () => {
    const target = mountTarget();
    const thread = fakeThread();
    const component = mount(ThreadComposer, {
      target,
      props: { thread, autosize: false, clear: 'never' },
    });

    const textarea = target.querySelector('textarea') as HTMLTextAreaElement;
    const form = target.querySelector('form') as HTMLFormElement;
    input(textarea, 'keep me');
    form.dispatchEvent(new SubmitEvent('submit', { bubbles: true, cancelable: true }));
    await tick();

    expect(thread.sendMessage).toHaveBeenCalledWith(
      { contents: [{ $type: 'text', text: 'keep me' }] },
      { runConfig: undefined },
    );
    expect(textarea.value).toBe('keep me');

    await unmount(component);
    target.remove();
  });

  it('supports bind:value and bind:textareaRef', async () => {
    const target = mountTarget();
    const thread = fakeThread();
    const component = mount(ThreadComposerBindHarness, {
      target,
      props: { thread },
    });

    const textarea = target.querySelector('textarea') as HTMLTextAreaElement;
    input(textarea, 'bound value');

    expect(component.getValue()).toBe('bound value');
    expect(component.getTextareaRef()).toBe(textarea);

    component.setValue('from parent');
    flushSync();
    expect(textarea.value).toBe('from parent');

    await unmount(component);
    target.remove();
  });

  it('renders custom child snippets with generated props and attachment', async () => {
    const target = mountTarget();
    const thread = fakeThread();
    const component = mount(ThreadComposerChildHarness, {
      target,
      props: { thread },
    });

    const textarea = target.querySelector('[data-testid="custom-textarea"]') as HTMLTextAreaElement;
    input(textarea, 'custom render');

    expect(target.querySelector('[data-testid="custom-composer"]')).not.toBeNull();
    expect(target.querySelector('[data-testid="custom-submit"]')?.textContent?.trim()).toBe('ready');

    await unmount(component);
    target.remove();
  });

  it('delegates interrupt to ThreadState.interrupt', async () => {
    const target = mountTarget();
    const thread = fakeThread(snapshot({
      streaming: true,
      textSubmissionState: { canSubmit: false, reason: 'busy' },
      canSubmitText: false,
    }));
    const component = mount(ThreadComposerChildHarness, {
      target,
      props: { thread, autosize: false },
    });

    const apiButton = target.querySelector('[data-testid="custom-interrupt"]') as HTMLButtonElement;
    apiButton.click();
    await tick();

    expect(thread.interrupt).toHaveBeenCalled();

    await unmount(component);
    target.remove();
  });
});

describe('ThreadComposer autosize', () => {
  it('applies custom autosize strategies and clamps helper inputs', () => {
    const textarea = document.createElement('textarea');
    const metrics: ThreadComposerAutosizeMetrics = {
      borderBlock: 2,
      contentWidth: 240,
      font: '16px Inter',
      letterSpacing: 0,
      lineHeight: 20,
      paddingBlock: 8,
    };

    const result = applyThreadComposerAutosize(
      textarea,
      'hello',
      ({ maxRows, metrics }) => maxRows * metrics.lineHeight + metrics.paddingBlock + metrics.borderBlock,
      metrics,
      2,
      4,
    );

    expect(result?.height).toBe(90);
    expect(textarea.style.height).toBe('90px');
  });

  it('leaves height untouched when autosize is disabled', () => {
    const textarea = document.createElement('textarea');
    const result = applyThreadComposerAutosize(
      textarea,
      'hello',
      false,
      null,
      1,
      4,
    );

    expect(result).toBeNull();
    expect(textarea.style.height).toBe('');
  });
});
