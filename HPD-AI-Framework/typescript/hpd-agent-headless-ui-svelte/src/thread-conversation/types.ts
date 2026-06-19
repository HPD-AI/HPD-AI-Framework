import type { Snippet } from 'svelte';
import type { SvelteHTMLElements } from 'svelte/elements';
import type { ThreadState, ThreadStateSnapshot } from '../thread-state.js';
import type { ThreadComposerProps } from '../thread-composer/index.js';
import type { ThreadTimelineViewportProps } from '../thread-timeline-viewport/index.js';

type DivProps = Omit<SvelteHTMLElements['div'], 'children'>;

export interface ThreadConversationElementProps extends DivProps {
  'data-busy'?: '';
  'data-empty'?: '';
  'data-hpd-thread-conversation': '';
  'data-requesting'?: '';
}

export interface ThreadConversationRegionProps {
  snapshot: ThreadStateSnapshot;
  thread: ThreadState;
}

export interface ThreadConversationRootSnippetProps extends ThreadConversationRegionProps {
  props: ThreadConversationElementProps;
}

export type ThreadConversationRuntimeRequestPlacement = 'composer-panel' | 'timeline' | 'none';

export interface ThreadConversationProps extends DivProps {
  child?: Snippet<[ThreadConversationRootSnippetProps]>;
  children?: Snippet<[ThreadConversationRegionProps]>;
  composer?: Snippet<[ThreadConversationRegionProps]>;
  composerProps?: Partial<Omit<ThreadComposerProps, 'thread'>>;
  footer?: Snippet<[ThreadConversationRegionProps]>;
  header?: Snippet<[ThreadConversationRegionProps]>;
  requests?: Snippet<[ThreadConversationRegionProps]>;
  runtimeRequestPlacement?: ThreadConversationRuntimeRequestPlacement;
  thread: ThreadState;
  timeline?: Snippet<[ThreadConversationRegionProps]>;
  viewport?: Snippet<[ThreadConversationRegionProps]>;
  viewportProps?: Partial<Omit<ThreadTimelineViewportProps, 'children' | 'thread'>>;
}
