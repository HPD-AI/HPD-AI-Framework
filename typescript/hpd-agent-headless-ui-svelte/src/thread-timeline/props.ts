import type {
  ThreadTimelineElementProps,
} from './types.js';

export function createThreadTimelineElementProps(
  isEmpty: boolean,
  restProps: Record<string, unknown> = {},
): ThreadTimelineElementProps {
  return {
    ...restProps,
    'data-hpd-thread-timeline': '',
    'data-empty': isEmpty ? '' : undefined,
  } as ThreadTimelineElementProps;
}
