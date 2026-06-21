import { flushSync, mount, unmount } from 'svelte';
import { describe, expect, it, vi } from 'vitest';
import type {
  Message as ThreadMessage,
  ToolCall as ToolCallModel,
} from '@hpd-research/hpd-agent-headless-ui';
import {
  createMessageElementProps,
  createMessageParts,
} from '../src/message/index.js';
import {
  createToolCallState,
  ToolCall,
} from '../src/tool-call/index.js';
import {
  createMessageActionBarActions,
  createMessageActionBarState,
} from '../src/message-action-bar/index.js';
import Message from '../src/message/message.svelte';
import MessageParts from '../src/message/message-parts.svelte';
import MessageActionBar from '../src/message-action-bar/message-action-bar.svelte';
import Reasoning from '../src/reasoning/reasoning.svelte';
import {
  FileAttachmentState,
  FileAttachment,
  FileAttachmentDropzone,
} from '../src/file-attachment/index.js';
import MessageActionBarHarness from './fixtures/message-action-bar-harness.svelte';
import MessageChildHarness from './fixtures/message-child-harness.svelte';
import MessageChildrenHarness from './fixtures/message-children-harness.svelte';
import MessageRerenderHarness from './fixtures/message-rerender-harness.svelte';

function message(overrides: Partial<ThreadMessage> = {}): ThreadMessage {
  return {
    id: 'm1',
    role: 'assistant',
    content: 'Hello from HPD.',
    contents: [{ $type: 'text', text: 'Hello from HPD.' }],
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

function mountTarget(): HTMLElement {
  const target = document.createElement('div');
  document.body.append(target);
  return target;
}

function toolCall(overrides: Partial<ToolCallModel> = {}): ToolCallModel {
  return {
    callId: 'tool-1',
    name: 'read_file',
    messageId: 'm1',
    status: 'complete',
    startTime: new Date('2026-01-01T00:00:00.000Z'),
    endTime: new Date('2026-01-01T00:00:01.250Z'),
    args: { path: 'README.md' },
    resultText: 'File contents',
    toolharnessName: 'workspace',
    callType: 'Function',
    turnId: 'turn-1',
    conversationId: 'conversation-1',
    runId: 'run-1',
    ...overrides,
  };
}

async function tick(): Promise<void> {
  await Promise.resolve();
  await Promise.resolve();
  flushSync();
}

describe('FileAttachmentState', () => {
  it('uploads files and exposes ready content references', async () => {
    const upload = vi.fn(async ({ file }: { file: File }) => ({
      contentId: `content-${file.name}`,
      version: 'v1',
      contentType: file.type || 'application/octet-stream',
      name: file.name,
      sizeBytes: file.size,
    }));
    const state = new FileAttachmentState({
      sessionId: 's1',
      threadId: 'main',
      upload,
    });

    await state.add([
      new File(['hello'], 'note.txt', { type: 'text/plain' }),
    ]);

    expect(upload).toHaveBeenCalledOnce();
    expect(state.attachments).toEqual([
      expect.objectContaining({
        file: expect.objectContaining({ name: 'note.txt' }),
        status: 'ready',
        content: expect.objectContaining({
          contentId: 'content-note.txt',
          contentType: 'text/plain',
        }),
      }),
    ]);
    expect(state.readyContents).toEqual([
      expect.objectContaining({
        $type: 'uri',
        uri: 'hpd-content://content-note.txt',
        mediaType: 'text/plain',
      }),
    ]);
    expect(state.canSubmit).toBe(true);
  });

  it('blocks submit while an upload failed', async () => {
    const state = new FileAttachmentState({
      sessionId: 's1',
      threadId: 'main',
      upload: async () => {
        throw new Error('upload failed');
      },
    });

    await state.add([
      new File(['hello'], 'note.txt', { type: 'text/plain' }),
    ]);

    expect(state.attachments[0]).toEqual(expect.objectContaining({
      status: 'error',
      error: 'upload failed',
    }));
    expect(state.canSubmit).toBe(false);
  });

  it('renders the picker with state/actions/props and supports dropzone add', async () => {
    const target = mountTarget();
    const state = new FileAttachmentState({
      sessionId: 's1',
      threadId: 'main',
      upload: vi.fn(async ({ file }: { file: File }) => ({
        contentId: `content-${file.name}`,
        version: 'v1',
        contentType: file.type || 'application/octet-stream',
        name: file.name,
        sizeBytes: file.size,
      })),
    });
    const picker = mount(FileAttachment, {
      target,
      props: { state },
    });
    const dropzone = mount(FileAttachmentDropzone, {
      target,
      props: { state },
    });

    expect(target.querySelector('[data-hpd-file-attachment]')).not.toBeNull();
    expect(target.querySelector('[data-hpd-file-attachment-input]')).not.toBeNull();
    expect(target.querySelector('[data-hpd-file-attachment-dropzone]')).not.toBeNull();

    const drop = new Event('drop', { bubbles: true, cancelable: true }) as DragEvent;
    Object.defineProperty(drop, 'dataTransfer', {
      value: {
        files: [
          new File(['hello'], 'dropped.txt', { type: 'text/plain' }),
        ],
      },
    });
    target.querySelector('[data-hpd-file-attachment-dropzone]')?.dispatchEvent(drop);
    await tick();

    expect(state.attachments).toEqual([
      expect.objectContaining({
        file: expect.objectContaining({ name: 'dropped.txt' }),
        status: 'ready',
      }),
    ]);
    expect(state.readyContents).toEqual([
      expect.objectContaining({
        uri: 'hpd-content://content-dropped.txt',
      }),
    ]);

    await unmount(dropzone);
    await unmount(picker);
    target.remove();
  });
});

describe('ToolCall', () => {
  it('creates state from the projected tool call envelope', () => {
    const state = createToolCallState({
      tool: toolCall(),
    });

    expect(state.active).toBe(false);
    expect(state.argsText).toContain('README.md');
    expect(state.durationMs).toBe(1250);
    expect(state.expanded).toBe(false);
    expect(state.hasArgs).toBe(true);
    expect(state.hasError).toBe(false);
    expect(state.hasResult).toBe(true);
    expect(state.inspectable).toBe(false);
    expect(state.label).toBe('read_file');
    expect(state.resultText).toBe('File contents');
    expect(state.statusLabel).toBe('complete');
  });

  it('renders default tool metadata, args, result, and generated attributes', async () => {
    const target = mountTarget();
    const component = mount(ToolCall, {
      target,
      props: {
        tool: toolCall(),
      },
    });

    const element = target.querySelector('[data-hpd-tool-call]');
    const trigger = target.querySelector('[data-hpd-tool-call-trigger]');
    const content = target.querySelector('[data-hpd-tool-call-content]');
    expect(element?.getAttribute('data-tool-id')).toBe('tool-1');
    expect(element?.getAttribute('data-tool-name')).toBe('read_file');
    expect(element?.getAttribute('data-tool-status')).toBe('complete');
    expect(element?.getAttribute('data-tool-harness')).toBe('workspace');
    expect(element?.hasAttribute('data-expanded')).toBe(false);
    expect(trigger?.getAttribute('aria-expanded')).toBe('false');
    expect(trigger?.getAttribute('aria-controls')).toBe(content?.getAttribute('id'));
    expect(content?.getAttribute('aria-labelledby')).toBe(trigger?.getAttribute('id'));
    expect(content?.hasAttribute('hidden')).toBe(true);
    expect(element?.textContent).toContain('read_file');
    expect(element?.textContent).toContain('complete');
    expect(element?.textContent).toContain('1.3s');
    expect(element?.textContent).toContain('README.md');
    expect(element?.textContent).toContain('File contents');

    await unmount(component);
    target.remove();
  });

  it('marks active tool calls as busy', async () => {
    const target = mountTarget();
    const component = mount(ToolCall, {
      target,
      props: {
        tool: toolCall({
          endTime: undefined,
          resultText: undefined,
          status: 'executing',
        }),
      },
    });

    const element = target.querySelector('[data-hpd-tool-call]');
    const trigger = target.querySelector('[data-hpd-tool-call-trigger]');
    const content = target.querySelector('[data-hpd-tool-call-content]');
    expect(element?.getAttribute('data-tool-active')).toBe('');
    expect(element?.getAttribute('aria-busy')).toBe('true');
    expect(element?.getAttribute('aria-live')).toBe('polite');
    expect(element?.hasAttribute('data-expanded')).toBe(true);
    expect(trigger?.getAttribute('aria-expanded')).toBe('true');
    expect(content?.hasAttribute('hidden')).toBe(false);

    await unmount(component);
    target.remove();
  });

  it('toggles disclosure through the generated trigger and reports callback details', async () => {
    const target = mountTarget();
    const onExpandedChange = vi.fn();
    const component = mount(ToolCall, {
      target,
      props: {
        onExpandedChange,
        tool: toolCall(),
      },
    });

    const element = target.querySelector('[data-hpd-tool-call]');
    const trigger = target.querySelector('[data-hpd-tool-call-trigger]');
    const content = target.querySelector('[data-hpd-tool-call-content]');
    expect(trigger?.getAttribute('aria-expanded')).toBe('false');
    expect(content?.hasAttribute('hidden')).toBe(true);

    trigger?.dispatchEvent(new MouseEvent('click', { bubbles: true }));
    await tick();

    expect(element?.hasAttribute('data-expanded')).toBe(true);
    expect(trigger?.getAttribute('aria-expanded')).toBe('true');
    expect(content?.hasAttribute('hidden')).toBe(false);
    expect(onExpandedChange).toHaveBeenCalledWith(
      true,
      expect.objectContaining({
        event: expect.any(MouseEvent),
        reason: 'trigger-press',
        trigger,
      }),
    );

    await unmount(component);
    target.remove();
  });

  it('renders an inspect affordance when the app opts in and reports callback details', async () => {
    const target = mountTarget();
    const onInspect = vi.fn();
    const component = mount(ToolCall, {
      target,
      props: {
        inspectable: true,
        inspectLabel: 'Open diff',
        onInspect,
        tool: toolCall({
          name: 'edit_file',
        }),
      },
    });

    const inspect = target.querySelector('[data-hpd-tool-call-inspect]');
    expect(inspect?.textContent).toBe('Open diff');
    expect(inspect?.getAttribute('aria-label')).toBe('Open diff');

    inspect?.dispatchEvent(new MouseEvent('click', { bubbles: true }));
    await tick();

    expect(onInspect).toHaveBeenCalledWith(expect.objectContaining({
      event: expect.any(MouseEvent),
      reason: 'inspect-press',
      trigger: inspect,
      state: expect.objectContaining({
        inspectable: true,
        label: 'edit_file',
      }),
      tool: expect.objectContaining({
        name: 'edit_file',
      }),
    }));

    await unmount(component);
    target.remove();
  });

  it('does not render inspect affordance without an inspect callback', async () => {
    const target = mountTarget();
    const component = mount(ToolCall, {
      target,
      props: {
        inspectable: true,
        tool: toolCall(),
      },
    });

    expect(target.querySelector('[data-hpd-tool-call-inspect]')).toBeNull();

    await unmount(component);
    target.remove();
  });
});

describe('Message', () => {
  it('generates conditional data and ARIA props from message state', () => {
    const props = createMessageElementProps(message({
      streaming: true,
      reasoning: 'Reasoning...',
      toolCalls: [{
        callId: 'tool-1',
        name: 'search',
        messageId: 'm1',
        status: 'pending',
        startTime: new Date('2026-01-01T00:00:00.000Z'),
        turnId: null,
        conversationId: null,
        runId: null,
      }],
    }));

    expect(props['data-message-id']).toBe('m1');
    expect(props['data-role']).toBe('assistant');
    expect(props['data-status']).toBe('streaming');
    expect(props['data-streaming']).toBe('');
    expect(props['data-has-reasoning']).toBe('');
    expect(props['data-has-tools']).toBe('');
    expect(props['aria-live']).toBe('polite');
    expect(props['aria-busy']).toBe(true);
  });

  it('marks thinking messages as busy without live announcements', () => {
    const props = createMessageElementProps(message({
      thinking: true,
    }));

    expect(props['data-status']).toBe('thinking');
    expect(props['data-thinking']).toBe('');
    expect(props['aria-live']).toBe('off');
    expect(props['aria-busy']).toBe(true);
  });

  it('marks active tools as executing and completed tools as complete', () => {
    const executing = createMessageElementProps(message({
      toolCalls: [{
        callId: 'tool-1',
        name: 'search',
        messageId: 'm1',
        status: 'executing',
        startTime: new Date('2026-01-01T00:00:00.000Z'),
        turnId: null,
        conversationId: null,
        runId: null,
      }],
    }));
    const complete = createMessageElementProps(message({
      toolCalls: [{
        callId: 'tool-1',
        name: 'search',
        messageId: 'm1',
        status: 'complete',
        startTime: new Date('2026-01-01T00:00:00.000Z'),
        endTime: new Date('2026-01-01T00:00:01.000Z'),
        turnId: null,
        conversationId: null,
        runId: null,
      }],
    }));

    expect(executing['data-status']).toBe('executing');
    expect(executing['data-has-tools']).toBe('');
    expect(complete['data-status']).toBe('complete');
    expect(complete['data-has-tools']).toBe('');
  });

  it('renders default content and generated attributes', async () => {
    const target = mountTarget();
    const component = mount(Message, {
      target,
      props: {
        message: message({ streaming: true }),
      },
    });
    await tick();

    const element = target.querySelector('[data-hpd-message]');
    expect(element?.textContent).toContain('Hello from HPD.');
    expect(element?.getAttribute('data-message-id')).toBe('m1');
    expect(element?.getAttribute('data-role')).toBe('assistant');
    expect(element?.getAttribute('data-status')).toBe('streaming');
    expect(element?.getAttribute('aria-live')).toBe('polite');
    expect(element?.getAttribute('aria-busy')).toBe('true');

    await unmount(component);
    target.remove();
  });

  it('projects one accumulated text part from flattened message content', () => {
    const parts = createMessageParts(message({
      content: 'Hello from accumulated text.',
      contents: [
        { $type: 'reasoning', text: 'Structured reasoning' },
        { $type: 'text', text: 'Hello from ' },
        { $type: 'text', text: 'delta chunks.' },
        {
          $type: 'uri',
          uri: 'hpd-content://asset-1',
          mediaType: 'text/plain',
        },
      ],
      reasoning: 'fallback reasoning',
      streaming: true,
      toolCalls: [{
        callId: 'tool-1',
        name: 'search',
        messageId: 'm1',
        status: 'executing',
        startTime: new Date('2026-01-01T00:00:00.000Z'),
        turnId: null,
        conversationId: null,
        runId: null,
      }],
    }));

    expect(parts.map((part) => part.type)).toEqual([
      'reasoning',
      'text',
      'content',
      'tool',
      'cursor',
    ]);
    expect(parts.find((part) => part.type === 'text')).toEqual(expect.objectContaining({
      id: 'm1:text',
      text: 'Hello from accumulated text.',
    }));
    expect(parts.find((part) => part.type === 'reasoning')).toEqual(expect.objectContaining({
      text: 'Structured reasoning',
    }));
  });

  it('renders structured message parts with stable data attributes', async () => {
    const target = mountTarget();
    const component = mount(MessageParts, {
      target,
      props: {
        message: message({
          content: 'Structured text',
          contents: [
            { $type: 'text', text: 'Structured text' },
            {
              $type: 'uri',
              uri: 'hpd-content://asset-1',
              mediaType: 'text/plain',
            },
          ],
          streaming: true,
        }),
      },
    });
    await tick();

    expect(target.querySelector('[data-hpd-message-parts]')).not.toBeNull();
    expect(target.querySelector('[data-part-type="text"]')?.textContent).toContain('Structured text');
    expect(target.querySelector('[data-part-type="content"]')?.textContent).toContain('hpd-content://asset-1');
    expect(target.querySelector('[data-part-type="cursor"]')?.textContent).toBe('|');

    await unmount(component);
    target.remove();
  });

  it('renders reasoning as a reusable leaf', async () => {
    const target = mountTarget();
    const component = mount(Reasoning, {
      target,
      props: {
        text: 'Checking the context.',
        status: 'streaming',
      },
    });

    const element = target.querySelector('[data-hpd-reasoning]');
    expect(element?.getAttribute('data-status')).toBe('streaming');
    expect(element?.getAttribute('aria-live')).toBe('polite');
    expect(element?.getAttribute('aria-busy')).toBe('true');
    expect(element?.textContent).toContain('Checking the context.');

    await unmount(component);
    target.remove();
  });

  it('does not render default actions unless requested', async () => {
    const target = mountTarget();
    const component = mount(Message, {
      target,
      props: {
        message: message(),
      },
    });

    expect(target.querySelector('[data-hpd-message-action-bar]')).toBeNull();

    await unmount(component);
    target.remove();
  });

  it('renders requested default actions and delegates callbacks', async () => {
    const onCopy = vi.fn();
    const onEditRequest = vi.fn();
    const onRetryRequest = vi.fn();
    const target = mountTarget();
    const component = mount(Message, {
      target,
      props: {
        message: message({ role: 'user' }),
        showActions: true,
        onCopy,
        onEditRequest,
        onRetryRequest,
      },
    });

    const copyButton = target.querySelector<HTMLButtonElement>('[data-hpd-message-action="copy"]');
    const editButton = target.querySelector<HTMLButtonElement>('[data-hpd-message-action="edit"]');
    const retryButton = target.querySelector<HTMLButtonElement>('[data-hpd-message-action="retry"]');

    expect(target.querySelector('[data-hpd-message-action-bar]')).not.toBeNull();
    expect(copyButton?.textContent).toBe('Copy');
    expect(editButton?.textContent).toBe('Edit');
    expect(retryButton?.textContent).toBe('Retry');

    copyButton?.click();
    editButton?.click();
    retryButton?.click();
    await Promise.resolve();

    expect(onCopy).toHaveBeenCalledWith({
      message: expect.objectContaining({ id: 'm1' }),
      text: 'Hello from HPD.',
    });
    expect(onEditRequest).toHaveBeenCalledWith({
      message: expect.objectContaining({ id: 'm1' }),
    });
    expect(onRetryRequest).toHaveBeenCalledWith({
      message: expect.objectContaining({ id: 'm1' }),
    });

    await unmount(component);
    target.remove();
  });

  it('allows custom message action bar snippets', async () => {
    const onCopy = vi.fn();
    const target = mountTarget();
    const component = mount(MessageActionBarHarness, {
      target,
      props: {
        message: message(),
        onCopy,
      },
    });

    const actions = target.querySelector('[data-testid="custom-action-bar"]');
    const copyButton = target.querySelector<HTMLButtonElement>('[data-hpd-message-action="copy"]');

    expect(actions).not.toBeNull();
    expect(copyButton?.textContent?.trim()).toBe('Copy assistant');

    copyButton?.click();
    await Promise.resolve();

    expect(onCopy).toHaveBeenCalledWith({
      message: expect.objectContaining({ id: 'm1' }),
      text: 'Hello from HPD.',
    });

    await unmount(component);
    target.remove();
  });

  it('renders standalone message action bar', async () => {
    const onCopy = vi.fn();
    const onEditRequest = vi.fn();
    const onRetryRequest = vi.fn();
    const target = mountTarget();
    const component = mount(MessageActionBar, {
      target,
      props: {
        message: message({ role: 'user' }),
        onCopy,
        onEditRequest,
        onRetryRequest,
      },
    });

    const copyButton = target.querySelector<HTMLButtonElement>('[data-hpd-message-action="copy"]');
    const editButton = target.querySelector<HTMLButtonElement>('[data-hpd-message-action="edit"]');
    const retryButton = target.querySelector<HTMLButtonElement>('[data-hpd-message-action="retry"]');

    expect(target.querySelector('[data-hpd-message-action-bar]')).not.toBeNull();
    expect(copyButton?.textContent).toBe('Copy');
    expect(editButton?.textContent).toBe('Edit');
    expect(retryButton?.textContent).toBe('Retry');

    copyButton?.click();
    editButton?.click();
    retryButton?.click();
    await Promise.resolve();

    expect(onCopy).toHaveBeenCalledWith({
      message: expect.objectContaining({ id: 'm1' }),
      text: 'Hello from HPD.',
    });
    expect(onEditRequest).toHaveBeenCalledWith({
      message: expect.objectContaining({ id: 'm1' }),
    });
    expect(onRetryRequest).toHaveBeenCalledWith({
      message: expect.objectContaining({ id: 'm1' }),
    });

    await unmount(component);
    target.remove();
  });

  it('creates standalone message action handlers', async () => {
    const onCopy = vi.fn();
    const onEditRequest = vi.fn();
    const onRetryRequest = vi.fn();
    const item = message({ role: 'user' });
    const state = createMessageActionBarState({
      copyText: 'user: Hello from HPD.',
      message: item,
      onEditRequest,
      onRetryRequest,
    });
    const actions = createMessageActionBarActions({
      message: item,
      copyText: (item) => `${item.role}: ${item.content}`,
      onCopy,
      onEditRequest,
      onRetryRequest,
      state,
    });

    await actions.copy();
    actions.requestEdit();
    await actions.retry();

    expect(onCopy).toHaveBeenCalledWith({
      message: expect.objectContaining({ id: 'm1' }),
      text: 'user: Hello from HPD.',
    });
    expect(onEditRequest).toHaveBeenCalledWith({
      message: expect.objectContaining({ id: 'm1' }),
    });
    expect(onRetryRequest).toHaveBeenCalledWith({
      message: expect.objectContaining({ id: 'm1' }),
    });
  });

  it('does not fire revision actions for ineligible message roles', () => {
    const onEditRequest = vi.fn();
    const onRetryRequest = vi.fn();
    const item = message({ role: 'tool' });
    const state = createMessageActionBarState({
      copyText: item.content,
      message: item,
      onEditRequest,
      onRetryRequest,
    });
    const actions = createMessageActionBarActions({
      message: item,
      onEditRequest,
      onRetryRequest,
      state,
    });

    actions.requestEdit();
    void actions.retry();

    expect(onEditRequest).not.toHaveBeenCalled();
    expect(onRetryRequest).not.toHaveBeenCalled();
  });

  it('renders children snippets inside the default wrapper', async () => {
    const target = mountTarget();
    const component = mount(MessageChildrenHarness, {
      target,
      props: {
        message: message(),
      },
    });

    expect(target.querySelector('[data-hpd-message]')).not.toBeNull();
    expect(target.querySelector('[data-testid="custom-content"]')?.textContent)
      .toBe('assistant:complete:Hello from HPD.');

    await unmount(component);
    target.remove();
  });

  it('renders child snippets with full element control', async () => {
    const target = mountTarget();
    const component = mount(MessageChildHarness, {
      target,
      props: {
        message: message({ thinking: true }),
      },
    });

    const element = target.querySelector('[data-testid="custom-message"]');
    expect(element?.tagName).toBe('ARTICLE');
    expect(element?.getAttribute('data-status')).toBe('thinking');

    await unmount(component);
    target.remove();
  });

  it('updates snippet content when the message prop changes', async () => {
    const target = mountTarget();
    const component = mount(MessageRerenderHarness, {
      target,
      props: {
        message: message({ content: 'First content', streaming: false }),
      },
    });

    expect(target.querySelector('[data-testid="content-output"]')?.textContent)
      .toBe('First content');
    expect(target.querySelector('[data-testid="streaming-output"]')?.textContent)
      .toBe('false');

    component.setMessage(message({ content: 'Updated content', streaming: true }));
    flushSync();

    expect(target.querySelector('[data-testid="content-output"]')?.textContent)
      .toBe('Updated content');
    expect(target.querySelector('[data-testid="streaming-output"]')?.textContent)
      .toBe('true');
    expect(target.querySelector('[data-testid="status-output"]')?.textContent)
      .toBe('streaming');

    await unmount(component);
    target.remove();
  });
});
