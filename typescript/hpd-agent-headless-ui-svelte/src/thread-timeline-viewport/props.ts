import type {
  ThreadScrollToBottomElementProps,
  ThreadTimelineViewportApi,
  ThreadTimelineViewportElementProps,
  ThreadTimelineViewportFooterElementProps,
  ThreadTimelineViewportTurnAnchor,
} from './types.js';

export interface CreateThreadTimelineViewportElementPropsOptions {
  ariaLabel: string;
  autoScrollSuppressed: boolean;
  autoScroll: boolean;
  isAtBottom: boolean;
  isEmpty: boolean;
  restProps?: Record<string, unknown>;
  turnAnchor: ThreadTimelineViewportTurnAnchor;
}

export function createThreadTimelineViewportElementProps(
  options: CreateThreadTimelineViewportElementPropsOptions,
): ThreadTimelineViewportElementProps {
  return {
    ...options.restProps,
    'data-hpd-thread-timeline-viewport': '',
    'data-at-bottom': options.isAtBottom ? '' : undefined,
    'data-auto-scroll-suppressed': options.autoScrollSuppressed ? '' : undefined,
    'data-empty': options.isEmpty ? '' : undefined,
    'data-auto-scroll': options.autoScroll ? 'true' : 'false',
    'data-turn-anchor': options.turnAnchor,
    'aria-atomic': 'false',
    'aria-label': options.ariaLabel,
    'aria-live': 'polite',
    role: 'log',
  } as ThreadTimelineViewportElementProps;
}

export function createThreadTimelineViewportFooterElementProps(
  restProps: Record<string, unknown> = {},
): ThreadTimelineViewportFooterElementProps {
  return {
    ...restProps,
    'data-hpd-thread-timeline-viewport-footer': '',
  } as ThreadTimelineViewportFooterElementProps;
}

export function createThreadScrollToBottomElementProps(options: {
  atBottom: boolean;
  disabled?: boolean;
  onclick: (event: MouseEvent) => void;
  restProps?: Record<string, unknown>;
  viewport: ThreadTimelineViewportApi | null;
}): ThreadScrollToBottomElementProps {
  const atBottom = options.atBottom;
  const disabled = options.disabled || atBottom || !options.viewport;
  return {
    ...options.restProps,
    'aria-disabled': disabled,
    'data-hpd-thread-scroll-to-bottom': '',
    'data-at-bottom': atBottom ? '' : undefined,
    disabled,
    onclick: options.onclick,
    type: 'button',
  } as ThreadScrollToBottomElementProps;
}
