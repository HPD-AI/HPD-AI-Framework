import { mergeProps } from '../thread-composer/index.js';
import type { Message } from '@hpd-research/hpd-agent-headless-ui';
import type { ThreadQuote } from '../selection-toolbar/index.js';
import type { MessageQuoteElementProps } from './types.js';

export function createMessageQuoteElementProps(
  quote: ThreadQuote,
  restProps: Record<string, unknown> = {},
): MessageQuoteElementProps {
  return mergeProps(restProps, {
    'data-hpd-message-quote': '',
    'data-message-id': quote.messageId,
  }) as unknown as MessageQuoteElementProps;
}

export function readMessageQuote(message: Message | undefined): ThreadQuote | null {
  if (!message) return null;

  const quote = message.additionalProperties?.quote;
  if (quote && typeof quote === 'object') {
    const parsed = readQuoteRecord(quote as Record<string, unknown>);
    if (parsed) return parsed;
  }

  for (const content of message.contents ?? []) {
    const contentQuote = readQuoteFromContent(content);
    if (contentQuote) return contentQuote;
  }

  return null;
}

function readQuoteFromContent(content: Message['contents'][number]): ThreadQuote | null {
  if (content.$type === 'quote') {
    const candidate = content as unknown as Record<string, unknown>;
    return readQuoteRecord(candidate);
  }

  const additionalProperties = 'additionalProperties' in content
    ? content.additionalProperties
    : undefined;

  if (!additionalProperties || typeof additionalProperties !== 'object') return null;
  const quote = (additionalProperties as Record<string, unknown>).quote;
  if (!quote || typeof quote !== 'object') return null;

  return readQuoteRecord(quote as Record<string, unknown>);
}

function readQuoteRecord(record: Record<string, unknown>): ThreadQuote | null {
  if (typeof record.text !== 'string' || record.text.length === 0) return null;

  return {
    messageId: typeof record.messageId === 'string' ? record.messageId : undefined,
    source: typeof record.source === 'string' ? record.source : undefined,
    text: record.text,
    threadId: typeof record.threadId === 'string' ? record.threadId : undefined,
  };
}
