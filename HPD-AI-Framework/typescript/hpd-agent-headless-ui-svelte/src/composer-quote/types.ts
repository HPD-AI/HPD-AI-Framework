import type { Snippet } from 'svelte';
import type { SvelteHTMLElements } from 'svelte/elements';
import type { ThreadQuote } from '../selection-toolbar/index.js';

type DivProps = Omit<SvelteHTMLElements['div'], 'children'>;
type SpanProps = Omit<SvelteHTMLElements['span'], 'children'>;
type ButtonProps = Omit<SvelteHTMLElements['button'], 'children'>;

export interface ComposerQuoteRootElementProps extends DivProps {
  'data-hpd-composer-quote': '';
  'data-message-id'?: string;
}

export interface ComposerQuoteTextElementProps extends SpanProps {
  'data-hpd-composer-quote-text': '';
}

export interface ComposerQuoteDismissElementProps extends ButtonProps {
  'aria-label': string;
  'data-hpd-composer-quote-dismiss': '';
  type: 'button';
}

export interface ComposerQuoteContext {
  clear(): void;
  props: {
    root: ComposerQuoteRootElementProps | null;
  };
  quote: ThreadQuote | null;
}

export interface ComposerQuoteChildProps {
  clear(): void;
  props: ComposerQuoteRootElementProps;
  quote: ThreadQuote;
}

export interface ComposerQuoteTextChildProps {
  props: ComposerQuoteTextElementProps;
  quote: ThreadQuote;
}

export interface ComposerQuoteDismissChildProps {
  clear(): void;
  props: ComposerQuoteDismissElementProps;
  quote: ThreadQuote;
}

export interface ComposerQuoteProps extends DivProps {
  children?: Snippet<[ComposerQuoteChildProps]>;
  onClear?: () => void;
  quote?: ThreadQuote | null;
}

export interface ComposerQuoteTextProps extends SpanProps {
  children?: Snippet<[ComposerQuoteTextChildProps]>;
}

export interface ComposerQuoteDismissProps extends ButtonProps {
  children?: Snippet<[ComposerQuoteDismissChildProps]>;
  label?: string;
  onClear?: () => void;
}
