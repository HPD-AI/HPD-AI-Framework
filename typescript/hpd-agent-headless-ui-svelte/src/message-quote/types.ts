import type { Snippet } from 'svelte';
import type { SvelteHTMLElements } from 'svelte/elements';
import type { Message } from '@hpd-research/hpd-agent-headless-ui';
import type { ThreadQuote } from '../selection-toolbar/index.js';

type BlockquoteProps = Omit<SvelteHTMLElements['blockquote'], 'children'>;

export interface MessageQuoteElementProps extends BlockquoteProps {
  'data-hpd-message-quote': '';
  'data-message-id'?: string;
}

export interface MessageQuoteChildProps {
  message?: Message;
  props: MessageQuoteElementProps;
  quote: ThreadQuote;
}

export interface MessageQuoteProps extends BlockquoteProps {
  children?: Snippet<[MessageQuoteChildProps]>;
  message?: Message;
  quote?: ThreadQuote | null;
}
