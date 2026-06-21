import { flushSync, mount, unmount } from 'svelte';
import { describe, expect, it, vi } from 'vitest';
import type {
  Message,
  ThreadRevisionForkDetails,
  ThreadRevisionResult,
} from '@hpd-research/hpd-agent-headless-ui';
import MessageEdit from '../src/message-edit/message-edit.svelte';
import MessageEditSnippetHarness from './fixtures/message-edit-snippet-harness.svelte';
import type { ThreadRevisionState } from '../src/index.js';

function message(overrides: Partial<Message> = {}): Message {
  return {
    id: 'user-1',
    role: 'user',
    content: 'Original prompt',
    streaming: false,
    thinking: false,
    timestamp: new Date('2026-01-01T00:00:00.000Z'),
    toolCalls: [],
    turnId: null,
    conversationId: null,
    runId: null,
    placement: 'transcript',
    ...overrides,
  };
}

function revision(overrides: Partial<ThreadRevisionResult> = {}): ThreadRevisionResult {
  return {
    kind: 'edit',
    thread: {
      id: 'fork-1',
      sessionId: 's1',
      name: 'fork-1',
      createdAt: '2026-01-01T00:00:00.000Z',
      lastActivity: '2026-01-01T00:00:00.000Z',
      messageCount: 1,
      kind: 'MainAgent',
      visibility: 'Visible',
      childThreads: [],
      totalForks: 0,
    },
    threadId: 'fork-1',
    clickedMessageId: 'user-1',
    inputMessageId: 'user-1',
    forkBoundaryMessageId: null,
    sentText: 'Replacement prompt',
    ...overrides,
  };
}

function revisions(result: ThreadRevisionResult = revision()): Pick<ThreadRevisionState, 'forkAndEditMessage'> {
  return {
    forkAndEditMessage: vi.fn(async () => result),
  };
}

function mountTarget(): HTMLElement {
  const target = document.createElement('div');
  document.body.append(target);
  return target;
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

describe('MessageEdit', () => {
  it('renders view mode and enters edit mode with the current message content', async () => {
    const target = mountTarget();
    const component = mount(MessageEdit, {
      target,
      props: {
        message: message(),
        revisions: revisions(),
        autosize: false,
      },
    });

    expect(target.querySelector('[data-hpd-message-edit]')).not.toBeNull();
    expect(target.querySelector('[data-hpd-message-edit-view]')?.textContent).toBe('Original prompt');

    target.querySelector<HTMLButtonElement>('button')?.click();
    await tick();

    const textarea = target.querySelector<HTMLTextAreaElement>('[data-hpd-message-edit-textarea]');
    expect(textarea?.value).toBe('Original prompt');
    expect(target.querySelector('[data-hpd-message-edit]')?.getAttribute('data-editing')).toBe('');

    await unmount(component);
    target.remove();
  });

  it('forks with replacement text and reports the saved revision', async () => {
    const target = mountTarget();
    const revisionResult = revision({ sentText: 'Replacement prompt' });
    const revisionState = revisions(revisionResult);
    const onSaved = vi.fn();
    const component = mount(MessageEdit, {
      target,
      props: {
        message: message(),
        revisions: revisionState,
        autosize: false,
        runConfig: { modelId: 'careful' },
        forkOptions: { name: 'edited prompt' },
        onSaved,
      },
    });

    target.querySelector<HTMLButtonElement>('button')?.click();
    await tick();
    input(target.querySelector('textarea') as HTMLTextAreaElement, '  Replacement prompt  ');
    target.querySelector<HTMLButtonElement>('[data-hpd-message-edit-save]')?.click();
    await tick();

    expect(revisionState.forkAndEditMessage).toHaveBeenCalledWith('user-1', 'Replacement prompt', {
      runConfig: { modelId: 'careful' },
      fork: { name: 'edited prompt' },
    });
    expect(onSaved).toHaveBeenCalledWith({
      message: expect.objectContaining({ id: 'user-1' }),
      revision: revisionResult,
      text: 'Replacement prompt',
    });
    expect(target.querySelector('[data-hpd-message-edit]')?.getAttribute('data-editing')).toBeNull();

    await unmount(component);
    target.remove();
  });

  it('passes dynamic fork metadata through to the revision controller', async () => {
    const target = mountTarget();
    const revisionState = revisions();
    const fork = vi.fn((details: ThreadRevisionForkDetails) => ({
      name: `Edit ${details.inputMessageId}`,
      metadata: {
        preview: details.sentText,
      },
    }));
    const component = mount(MessageEdit, {
      target,
      props: {
        message: message(),
        revisions: revisionState,
        autosize: false,
        forkOptions: fork,
      },
    });

    target.querySelector<HTMLButtonElement>('button')?.click();
    await tick();
    input(target.querySelector('textarea') as HTMLTextAreaElement, '  Metadata replacement  ');
    target.querySelector<HTMLButtonElement>('[data-hpd-message-edit-save]')?.click();
    await tick();

    expect(fork).not.toHaveBeenCalled();
    expect(revisionState.forkAndEditMessage).toHaveBeenCalledWith('user-1', 'Metadata replacement', {
      runConfig: undefined,
      fork,
    });

    await unmount(component);
    target.remove();
  });

  it('cancels with Escape without saving', async () => {
    const target = mountTarget();
    const revisionState = revisions();
    const onCancel = vi.fn();
    const component = mount(MessageEdit, {
      target,
      props: {
        message: message(),
        revisions: revisionState,
        autosize: false,
        onCancel,
      },
    });

    target.querySelector<HTMLButtonElement>('button')?.click();
    await tick();
    const textarea = target.querySelector('textarea') as HTMLTextAreaElement;
    input(textarea, 'Replacement prompt');
    textarea.dispatchEvent(new KeyboardEvent('keydown', {
      bubbles: true,
      cancelable: true,
      key: 'Escape',
    }));
    await tick();

    expect(revisionState.forkAndEditMessage).not.toHaveBeenCalled();
    expect(onCancel).toHaveBeenCalledWith({ message: expect.objectContaining({ id: 'user-1' }) });
    expect(target.querySelector('[data-hpd-message-edit]')?.getAttribute('data-editing')).toBeNull();

    await unmount(component);
    target.remove();
  });

  it('submits on Enter and preserves Shift+Enter', async () => {
    const target = mountTarget();
    const revisionState = revisions();
    const component = mount(MessageEdit, {
      target,
      props: {
        message: message(),
        revisions: revisionState,
        autosize: false,
      },
    });

    target.querySelector<HTMLButtonElement>('button')?.click();
    await tick();
    const textarea = target.querySelector('textarea') as HTMLTextAreaElement;
    input(textarea, 'Replacement prompt');
    textarea.dispatchEvent(new KeyboardEvent('keydown', {
      bubbles: true,
      cancelable: true,
      key: 'Enter',
      shiftKey: true,
    }));
    await tick();
    expect(revisionState.forkAndEditMessage).not.toHaveBeenCalled();

    textarea.dispatchEvent(new KeyboardEvent('keydown', {
      bubbles: true,
      cancelable: true,
      key: 'Enter',
    }));
    await tick();
    expect(revisionState.forkAndEditMessage).toHaveBeenCalledOnce();

    await unmount(component);
    target.remove();
  });

  it('keeps editing open and reports errors when fork-and-edit fails', async () => {
    const target = mountTarget();
    const error = new Error('fork failed');
    const revisionState = {
      forkAndEditMessage: vi.fn(async () => {
        throw error;
      }),
    };
    const onError = vi.fn();
    const component = mount(MessageEdit, {
      target,
      props: {
        message: message(),
        revisions: revisionState,
        autosize: false,
        onError,
      },
    });

    target.querySelector<HTMLButtonElement>('button')?.click();
    await tick();
    input(target.querySelector('textarea') as HTMLTextAreaElement, 'Replacement prompt');
    target.querySelector<HTMLButtonElement>('[data-hpd-message-edit-save]')?.click();
    await tick();

    expect(onError).toHaveBeenCalledWith({
      message: expect.objectContaining({ id: 'user-1' }),
      error,
    });
    expect(target.querySelector('[data-hpd-message-edit]')?.getAttribute('data-editing')).toBe('');
    expect(target.querySelector('[data-hpd-message-edit]')?.getAttribute('data-error')).toBe('');

    await unmount(component);
    target.remove();
  });

  it('supports custom view and edit snippets', async () => {
    const target = mountTarget();
    const revisionState = revisions();
    const component = mount(MessageEditSnippetHarness, {
      target,
      props: {
        message: message(),
        revisions: revisionState,
      },
    });

    target.querySelector<HTMLButtonElement>('[data-testid="start"]')?.click();
    await tick();
    input(target.querySelector('textarea') as HTMLTextAreaElement, 'Custom replacement');
    target.querySelector<HTMLButtonElement>('[data-testid="save"]')?.click();
    await tick();

    expect(revisionState.forkAndEditMessage).toHaveBeenCalledWith('user-1', 'Custom replacement', {
      runConfig: undefined,
      fork: undefined,
    });

    await unmount(component);
    target.remove();
  });
});
