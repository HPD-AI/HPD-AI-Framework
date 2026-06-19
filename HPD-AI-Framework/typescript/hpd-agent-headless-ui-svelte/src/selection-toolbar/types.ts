import type { Snippet } from 'svelte';
import type { Attachment } from 'svelte/attachments';
import type { SvelteHTMLElements } from 'svelte/elements';

type DivProps = Omit<SvelteHTMLElements['div'], 'children'>;
type ButtonProps = Omit<SvelteHTMLElements['button'], 'children'>;

export type SelectionToolbarPlacement = 'above' | 'below';

export interface ThreadQuote {
  messageId?: string;
  source?: 'selection' | string;
  text: string;
  threadId?: string;
}

export interface SelectionToolbarSelection {
  anchorNode: Node | null;
  focusNode: Node | null;
  messageId: string | null;
  rect: DOMRectReadOnly;
  text: string;
}

export interface SelectionToolbarPosition {
  left: number;
  top: number;
}

export interface SelectionToolbarState {
  disabled: boolean;
  minLength: number;
  open: boolean;
  placement: SelectionToolbarPlacement;
  position: SelectionToolbarPosition | null;
  quote: ThreadQuote | null;
  selection: SelectionToolbarSelection | null;
}

export interface SelectionToolbarRootElementProps extends DivProps {
  'data-disabled'?: '';
  'data-hpd-selection-toolbar-root': '';
  'data-open'?: '';
}

export interface SelectionToolbarToolbarElementProps extends DivProps {
  'aria-label': string;
  'data-hpd-selection-toolbar': '';
  'data-open'?: '';
  'data-placement': SelectionToolbarPlacement;
  role: 'toolbar';
  style: string;
}

export interface SelectionToolbarQuoteElementProps extends ButtonProps {
  'aria-disabled': boolean;
  'data-hpd-selection-toolbar-quote': '';
  disabled: boolean;
  type: 'button';
}

export interface SelectionToolbarActions {
  clearSelection(): void;
  close(): void;
  quote(): ThreadQuote | null;
  refresh(): void;
  setQuote(quote: ThreadQuote | null): void;
}

export interface SelectionToolbarRootContext {
  actions: SelectionToolbarActions;
  props: {
    root: SelectionToolbarRootElementProps;
    toolbar: SelectionToolbarToolbarElementProps;
  };
  rootAttachment: Attachment<HTMLElement>;
  rootRef: HTMLElement | null;
  state: SelectionToolbarState;
}

export interface SelectionToolbarRootChildProps {
  actions: SelectionToolbarActions;
  props: SelectionToolbarRootContext['props'];
  rootAttachment: Attachment<HTMLElement>;
  rootRef: HTMLElement | null;
  state: SelectionToolbarState;
}

export interface SelectionToolbarRootProps extends DivProps {
  child?: Snippet<[SelectionToolbarRootChildProps]>;
  children?: Snippet<[SelectionToolbarRootChildProps]>;
  clearSelectionOnQuote?: boolean;
  closeOnQuote?: boolean;
  disabled?: boolean;
  minLength?: number;
  offset?: number;
  onQuote?: (quote: ThreadQuote, selection: SelectionToolbarSelection) => void | Promise<void>;
  placement?: SelectionToolbarPlacement;
  quote?: ThreadQuote | null;
  toolbarLabel?: string;
}

export interface SelectionToolbarQuoteChildProps {
  actions: SelectionToolbarActions;
  props: SelectionToolbarQuoteElementProps;
  quote: ThreadQuote | null;
  selection: SelectionToolbarSelection | null;
  state: SelectionToolbarState;
}

export interface SelectionToolbarQuoteProps extends ButtonProps {
  children?: Snippet<[SelectionToolbarQuoteChildProps]>;
  label?: string;
  onQuote?: (quote: ThreadQuote, selection: SelectionToolbarSelection) => void | Promise<void>;
}
