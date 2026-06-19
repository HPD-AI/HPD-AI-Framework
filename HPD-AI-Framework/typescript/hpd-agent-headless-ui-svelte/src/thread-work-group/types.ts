import type { Snippet } from 'svelte';
import type { SvelteHTMLElements } from 'svelte/elements';
import type {
  ThreadWorkGroup,
  ThreadWorkPart,
  ThreadWorkStatus,
} from '@hpd-research/hpd-agent-headless-ui';

type DetailsProps = Omit<SvelteHTMLElements['details'], 'children'>;
type DivProps = Omit<SvelteHTMLElements['div'], 'children'>;
type SectionProps = Omit<SvelteHTMLElements['section'], 'children'>;

export interface ThreadWorkGroupElementProps extends DetailsProps {
  'data-hpd-thread-work-group': '';
  'data-work-id': string;
  'data-work-status': ThreadWorkStatus;
  'data-open-by-default'?: '';
}

export interface ThreadWorkPartElementProps extends SectionProps {
  'data-hpd-thread-work-part': '';
  'data-work-part-type': ThreadWorkPart['type'];
  'data-tool-id'?: string;
  'data-tool-status'?: string;
}

export interface ThreadWorkPartsElementProps extends DivProps {
  'data-hpd-thread-work-parts': '';
  'data-empty'?: '';
}

export interface ThreadWorkPartsState {
  empty: boolean;
  parts: ThreadWorkPart[];
  status: ThreadWorkStatus;
  work: ThreadWorkGroup;
}

export interface ThreadWorkGroupSnippetProps {
  work: ThreadWorkGroup;
  parts: ThreadWorkPart[];
  status: ThreadWorkStatus;
}

export interface ThreadWorkGroupPartSnippetProps {
  part: ThreadWorkPart;
  index: number;
  props: ThreadWorkPartElementProps;
  work: ThreadWorkGroup;
}

export interface ThreadWorkGroupChildProps extends ThreadWorkGroupSnippetProps {
  props: ThreadWorkGroupElementProps;
}

export interface ThreadWorkGroupProps extends DetailsProps {
  child?: Snippet<[ThreadWorkGroupChildProps]>;
  children?: Snippet<[ThreadWorkGroupSnippetProps]>;
  showFinalDraft?: boolean;
  workPart?: Snippet<[ThreadWorkGroupPartSnippetProps]>;
  work: ThreadWorkGroup;
}

export interface ThreadWorkPartsProps extends DivProps {
  children?: Snippet<[ThreadWorkGroupSnippetProps]>;
  showFinalDraft?: boolean;
  workPart?: Snippet<[ThreadWorkGroupPartSnippetProps]>;
  work: ThreadWorkGroup;
}
