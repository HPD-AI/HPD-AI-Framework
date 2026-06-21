import { mergeProps } from '../thread-composer/index.js';
import type {
  ComposerQuoteDismissElementProps,
  ComposerQuoteRootElementProps,
  ComposerQuoteTextElementProps,
} from './types.js';
import type { ThreadQuote } from '../selection-toolbar/index.js';

export function createComposerQuoteRootElementProps(
  quote: ThreadQuote,
  restProps: Record<string, unknown> = {},
): ComposerQuoteRootElementProps {
  return mergeProps(restProps, {
    'data-hpd-composer-quote': '',
    'data-message-id': quote.messageId,
  }) as unknown as ComposerQuoteRootElementProps;
}

export function createComposerQuoteTextElementProps(
  restProps: Record<string, unknown> = {},
): ComposerQuoteTextElementProps {
  return mergeProps(restProps, {
    'data-hpd-composer-quote-text': '',
  }) as unknown as ComposerQuoteTextElementProps;
}

export function createComposerQuoteDismissElementProps(options: {
  label?: string;
  onClick: (event: MouseEvent) => void;
  restProps?: Record<string, unknown>;
}): ComposerQuoteDismissElementProps {
  return mergeProps(options.restProps ?? {}, {
    'aria-label': options.label ?? 'Dismiss quote',
    'data-hpd-composer-quote-dismiss': '',
    onclick: options.onClick,
    type: 'button',
  }) as unknown as ComposerQuoteDismissElementProps;
}
