import { mount, unmount } from 'svelte';
import { describe, expect, it } from 'vitest';
import type { Message } from '@hpd-research/hpd-agent-headless-ui';
import MessageQuote from '../src/message-quote/message-quote.svelte';
import {
  createMessageQuoteElementProps,
  readMessageQuote,
} from '../src/message-quote/index.js';
import MessageQuoteChildHarness from './fixtures/message-quote-child-harness.svelte';

function message(overrides: Partial<Message> = {}): Message {
  return {
    id: 'user-1',
    role: 'user',
    content: 'Can you explain this?',
    contents: [{ $type: 'text', text: 'Can you explain this?' }],
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

describe('MessageQuote', () => {
  it('renders nothing when no quote is present', async () => {
    const target = mountTarget();
    const component = mount(MessageQuote, {
      target,
      props: { message: message() },
    });

    expect(target.querySelector('[data-hpd-message-quote]')).toBeNull();

    await unmount(component);
    target.remove();
  });

  it('reads and renders quote metadata from message additionalProperties', async () => {
    const target = mountTarget();
    const component = mount(MessageQuote, {
      target,
      props: {
        message: message({
          additionalProperties: {
            quote: {
              text: 'Selected assistant text',
              messageId: 'assistant-1',
              threadId: 'main',
              source: 'selection',
            },
          },
        }),
      },
    });

    const quote = target.querySelector('[data-hpd-message-quote]');
    expect(quote?.tagName).toBe('BLOCKQUOTE');
    expect(quote?.getAttribute('data-message-id')).toBe('assistant-1');
    expect(quote?.textContent).toBe('Selected assistant text');

    await unmount(component);
    target.remove();
  });

  it('reads quote-shaped content when message metadata is absent', () => {
    const parsed = readMessageQuote(message({
      contents: [
        {
          $type: 'quote',
          text: 'Content quote',
          messageId: 'source-1',
        } as never,
        { $type: 'text', text: 'Reply' },
      ],
    }));

    expect(parsed).toEqual({
      messageId: 'source-1',
      source: undefined,
      text: 'Content quote',
      threadId: undefined,
    });
  });

  it('lets an explicit quote prop override the message quote', async () => {
    const target = mountTarget();
    const component = mount(MessageQuote, {
      target,
      props: {
        message: message({
          additionalProperties: {
            quote: {
              text: 'Message quote',
              messageId: 'message-source',
            },
          },
        }),
        quote: {
          text: 'Explicit quote',
          messageId: 'explicit-source',
        },
      },
    });

    const quote = target.querySelector('[data-hpd-message-quote]');
    expect(quote?.getAttribute('data-message-id')).toBe('explicit-source');
    expect(quote?.textContent).toBe('Explicit quote');

    await unmount(component);
    target.remove();
  });

  it('passes quote, message, and generated props to the children snippet', async () => {
    const target = mountTarget();
    const component = mount(MessageQuoteChildHarness, {
      target,
      props: {
        message: message(),
        quote: {
          text: 'Custom quote body',
          messageId: 'custom-source',
        },
      },
    });

    const custom = target.querySelector('[data-testid="custom-message-quote"]');
    expect(custom?.getAttribute('data-hpd-message-quote')).toBe('');
    expect(custom?.getAttribute('data-message-id')).toBe('custom-source');
    expect(custom?.textContent).toContain('custom-source');
    expect(custom?.textContent).toContain('Custom quote body');

    await unmount(component);
    target.remove();
  });

  it('creates stable element props for custom renderers', () => {
    const props = createMessageQuoteElementProps(
      { text: 'Quoted text', messageId: 'source-1' },
      { class: 'quote' },
    );

    expect(props['data-hpd-message-quote']).toBe('');
    expect(props['data-message-id']).toBe('source-1');
    expect(props.class).toContain('quote');
  });
});
