import type { Snippet } from 'svelte';
import type { SvelteHTMLElements } from 'svelte/elements';
import type { ThreadTimelineItem } from '@hpd-research/hpd-agent-headless-ui';
import type { ThreadState } from '../thread-state.js';

type DivProps = Omit<SvelteHTMLElements['div'], 'children'>;
type ButtonProps = Omit<SvelteHTMLElements['button'], 'children'>;

export type ThreadTimelineViewportScrollContainer = 'all' | 'nearest';
export type ThreadTimelineViewportTurnAnchor = 'bottom' | 'top';

export interface ThreadTimelineViewportTopAnchorClamp {
  tallerThan?: string;
  visibleHeight?: string;
}

export interface ThreadTimelineViewportScrollToBottomOptions {
  behavior?: ScrollBehavior;
}

export interface ThreadTimelineViewportScrollToItemOptions {
  behavior?: ScrollBehavior;
  block?: ScrollLogicalPosition;
  container?: ThreadTimelineViewportScrollContainer;
  inline?: ScrollLogicalPosition;
}

export interface ThreadTimelineViewportApi {
  readonly autoScrollSuppressed: boolean;
  readonly contentInset: number;
  readonly isAtBottom: boolean;
  registerContentInset(id: string, height: number): void;
  registerItem(id: string, element: HTMLElement): void;
  scrollToBottom(options?: ThreadTimelineViewportScrollToBottomOptions): void;
  scrollToItem(id: string, options?: ThreadTimelineViewportScrollToItemOptions): void;
  unregisterContentInset(id: string): void;
  unregisterItem(id: string): void;
}

export interface ThreadTimelineViewportElementProps extends DivProps {
  'data-hpd-thread-timeline-viewport': '';
  'data-at-bottom'?: '';
  'data-auto-scroll-suppressed'?: '';
  'data-empty'?: '';
  'data-auto-scroll': 'true' | 'false';
  'data-turn-anchor': ThreadTimelineViewportTurnAnchor;
  'aria-atomic': 'false';
  'aria-label': string;
  'aria-live': 'polite';
  role: 'log';
}

export interface ThreadTimelineViewportFooterElementProps extends DivProps {
  'data-hpd-thread-timeline-viewport-footer': '';
}

export interface ThreadScrollToBottomElementProps extends ButtonProps {
  'aria-disabled': boolean;
  'data-hpd-thread-scroll-to-bottom': '';
  'data-at-bottom'?: '';
  disabled: boolean;
  type: 'button';
}

export interface ThreadTimelineViewportChildProps {
  props: ThreadTimelineViewportElementProps;
  timeline: ThreadTimelineItem[];
  viewport: ThreadTimelineViewportApi;
}

export interface ThreadTimelineViewportProps extends DivProps {
  ariaLabel?: string;
  anchorBlock?: ScrollLogicalPosition;
  anchorInline?: ScrollLogicalPosition;
  atBottomThreshold?: number;
  autoScroll?: boolean;
  children?: Snippet<[ThreadTimelineViewportChildProps]>;
  scrollBehavior?: ScrollBehavior;
  scrollContainer?: ThreadTimelineViewportScrollContainer;
  scrollToBottomOnInitialize?: boolean;
  scrollToBottomOnExecutionStart?: boolean;
  thread?: ThreadState;
  timeline?: ThreadTimelineItem[];
  topAnchorMessageClamp?: ThreadTimelineViewportTopAnchorClamp;
  turnAnchor?: ThreadTimelineViewportTurnAnchor;
}

export interface ThreadTimelineViewportFooterChildProps {
  props: ThreadTimelineViewportFooterElementProps;
}

export interface ThreadTimelineViewportFooterProps extends DivProps {
  children?: Snippet<[ThreadTimelineViewportFooterChildProps]>;
}

export interface ThreadScrollToBottomChildProps {
  props: ThreadScrollToBottomElementProps;
  viewport: ThreadTimelineViewportApi | null;
}

export interface ThreadScrollToBottomProps extends ButtonProps {
  behavior?: ScrollBehavior;
  child?: Snippet<[ThreadScrollToBottomChildProps]>;
  children?: Snippet<[ThreadScrollToBottomChildProps]>;
  disabled?: boolean;
  thread?: ThreadState;
}
