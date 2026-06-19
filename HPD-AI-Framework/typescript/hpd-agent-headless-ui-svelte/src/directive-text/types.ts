import type { Snippet } from 'svelte';
import type { SvelteHTMLElements } from 'svelte/elements';
import type {
  DirectiveTextPart,
  Message,
  MessageDirective,
} from '@hpd-research/hpd-agent-headless-ui';

type SpanProps = Omit<SvelteHTMLElements['span'], 'children' | 'part'>;

export interface DirectiveTextRootElementProps extends SpanProps {
  'data-hpd-directive-text': '';
}

export interface DirectiveTextPlainElementProps extends SpanProps {
  'data-hpd-directive-text-part': '';
  'data-part-type': 'text';
}

export interface DirectiveTextChipElementProps extends SpanProps {
  'aria-label': string;
  'data-directive-id': string;
  'data-directive-trigger': string;
  'data-directive-type': string;
  'data-hpd-directive-text-chip': '';
  'data-hpd-directive-text-part': '';
  'data-part-type': 'directive';
}

export interface DirectiveTextPartChildProps {
  message?: Message;
  part: DirectiveTextPart;
  props: DirectiveTextPlainElementProps | DirectiveTextChipElementProps;
}

export interface DirectiveTextDirectiveChildProps {
  directive: MessageDirective;
  message?: Message;
  part: Extract<DirectiveTextPart, { type: 'directive' }>;
  props: DirectiveTextChipElementProps;
}

export interface DirectiveTextTextChildProps {
  message?: Message;
  part: Extract<DirectiveTextPart, { type: 'text' }>;
  props: DirectiveTextPlainElementProps;
}

export interface DirectiveTextChildrenProps {
  message?: Message;
  parts: DirectiveTextPart[];
  props: DirectiveTextRootElementProps;
}

export interface DirectiveTextProps extends SpanProps {
  directive?: Snippet<[DirectiveTextDirectiveChildProps]>;
  directives?: readonly MessageDirective[];
  message?: Message;
  part?: Snippet<[DirectiveTextPartChildProps]>;
  text: string;
  textPart?: Snippet<[DirectiveTextTextChildProps]>;
  children?: Snippet<[DirectiveTextChildrenProps]>;
}
