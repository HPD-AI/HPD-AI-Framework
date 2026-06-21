import type { Snippet } from 'svelte';
import type { SvelteHTMLElements } from 'svelte/elements';
import type {
  Message,
  RuntimeRequest,
  ThreadTimelineItem,
  ThreadTimelineMessageItem,
  ThreadTimelineProgressItem,
  ThreadTimelineRuntimeRequestItem,
  ThreadTimelineWarningItem,
  ThreadTimelineWorkItem,
  ThreadWorkGroup,
} from '@hpd-research/hpd-agent-headless-ui';
import type {
  RuntimeRequestActions,
  RuntimeRequestElementProps,
} from '../runtime-request/index.js';
import type { ThreadState } from '../thread-state.js';
import type { ThreadWorkGroupElementProps } from '../thread-work-group/index.js';

type DivProps = Omit<SvelteHTMLElements['div'], 'children'>;

export interface ThreadTimelineElementProps extends DivProps {
  'data-hpd-thread-timeline': '';
  'data-empty'?: '';
}

export interface ThreadTimelineMessageSnippetProps {
  index: number;
  item: ThreadTimelineMessageItem;
  message: Message;
}

export interface ThreadTimelineWorkSnippetProps {
  index: number;
  item: ThreadTimelineWorkItem;
  props: ThreadWorkGroupElementProps;
  work: ThreadWorkGroup;
}

export interface ThreadTimelineRuntimeRequestSnippetProps {
  actions: RuntimeRequestActions;
  index: number;
  item: ThreadTimelineRuntimeRequestItem;
  props: RuntimeRequestElementProps;
  request: RuntimeRequest;
}

export interface ThreadTimelineProgressSnippetProps {
  index: number;
  item: ThreadTimelineProgressItem;
  label: string;
}

export interface ThreadTimelineWarningSnippetProps {
  index: number;
  item: ThreadTimelineWarningItem;
  message: string;
}

export interface ThreadTimelineEmptySnippetProps {
  props: ThreadTimelineElementProps;
}

export interface ThreadTimelineProps extends DivProps {
  empty?: Snippet<[ThreadTimelineEmptySnippetProps]>;
  message?: Snippet<[ThreadTimelineMessageSnippetProps]>;
  progress?: Snippet<[ThreadTimelineProgressSnippetProps]>;
  runtimeRequest?: Snippet<[ThreadTimelineRuntimeRequestSnippetProps]>;
  thread?: ThreadState;
  timeline?: ThreadTimelineItem[];
  warning?: Snippet<[ThreadTimelineWarningSnippetProps]>;
  work?: Snippet<[ThreadTimelineWorkSnippetProps]>;
}
