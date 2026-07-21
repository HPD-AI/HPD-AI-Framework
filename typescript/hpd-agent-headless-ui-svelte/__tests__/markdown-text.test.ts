import { flushSync, mount, unmount } from 'svelte';
import { describe, expect, it } from 'vitest';
import type { Message as ThreadMessage } from '@hpd-research/hpd-agent-headless-ui';
import MarkdownText from '../src/markdown-text/markdown-text.svelte';
import MessageParts from '../src/message/message-parts.svelte';
import {
  createMarkdownTextExtensions,
  createMarkdownTextModel,
} from '../src/markdown-text/index.js';

function message(overrides: Partial<ThreadMessage> = {}): ThreadMessage {
  return {
    id: 'm1',
    role: 'assistant',
    content: '**Hello** from HPD.',
    contents: [
      { $type: 'text', text: '**Hello** ' },
      { $type: 'text', text: 'from HPD.' },
    ],
    streaming: false,
    thinking: false,
    timestamp: new Date('2026-01-01T00:00:00.000Z'),
    toolCalls: [],
    turnId: null,
    conversationId: null,
    executionId: null,
    placement: 'transcript',
    ...overrides,
  };
}

function mountTarget(): HTMLElement {
  const target = document.createElement('div');
  document.body.append(target);
  return target;
}

async function tick(): Promise<void> {
  await Promise.resolve();
  await Promise.resolve();
  flushSync();
}

describe('MarkdownText', () => {
  it('renders assistant markdown from accumulated message content', async () => {
    const target = mountTarget();
    const component = mount(MessageParts, {
      target,
      props: {
        message: message(),
      },
    });
    await tick();

    expect(target.querySelector('[data-hpd-markdown-text]')).not.toBeNull();
    expect(target.querySelector('strong')?.textContent).toBe('Hello');
    expect(target.querySelectorAll('[data-part-type="text"]')).toHaveLength(1);

    await unmount(component);
    target.remove();
  });

  it('does not enable Mermaid extensions while streaming by default', () => {
    const model = createMarkdownTextModel({
      features: { mermaid: true },
      streaming: true,
      text: '```mermaid\ngraph TD\n  A --> B\n```',
    });

    expect(model.mermaidEnabled).toBe(false);
    expect(createMarkdownTextExtensions({ mermaid: true }, true)).toHaveLength(0);
    expect(createMarkdownTextExtensions({ mermaid: true }, false)).toHaveLength(1);
  });

  it('supports standalone markdown rendering', async () => {
    const target = mountTarget();
    const component = mount(MarkdownText, {
      target,
      props: {
        text: 'A [link](https://example.com)',
      },
    });
    await tick();

    const link = target.querySelector('a');
    expect(link?.getAttribute('href')).toBe('https://example.com');
    expect(link?.getAttribute('target')).toBe('_blank');

    await unmount(component);
    target.remove();
  });

  it('renders text while the wrapper is marked streaming', async () => {
    const target = mountTarget();
    const component = mount(MarkdownText, {
      target,
      props: {
        text: '**Streaming** text',
        streaming: true,
        features: { katex: true, mermaid: true },
      },
    });
    await tick();

    expect(target.textContent).toContain('Streaming');

    await unmount(component);
    target.remove();
  });
});
