import type { Snippet } from 'svelte';
import type { SvelteHTMLElements } from 'svelte/elements';

type SectionProps = Omit<SvelteHTMLElements['section'], 'children'>;

export type ReasoningStatus = 'complete' | 'streaming';

export interface ReasoningElementProps extends SectionProps {
  'data-hpd-reasoning': '';
  'data-empty'?: '';
  'data-status': ReasoningStatus;
  'aria-busy': boolean;
  'aria-label': string;
  'aria-live': 'off' | 'polite';
}

export interface ReasoningChildProps {
  label: string;
  props: ReasoningElementProps;
  status: ReasoningStatus;
  text: string;
}

export interface ReasoningProps extends SectionProps {
  children?: Snippet<[ReasoningChildProps]>;
  label?: string;
  status?: ReasoningStatus;
  text: string;
}
