import type {
  ReasoningElementProps,
  ReasoningStatus,
} from './types.js';

export interface CreateReasoningElementPropsOptions {
  label: string;
  restProps?: Record<string, unknown>;
  status: ReasoningStatus;
  text: string;
}

export function createReasoningElementProps(
  options: CreateReasoningElementPropsOptions,
): ReasoningElementProps {
  const isStreaming = options.status === 'streaming';

  return {
    ...options.restProps,
    'data-hpd-reasoning': '',
    'data-empty': options.text.trim().length === 0 ? '' : undefined,
    'data-status': options.status,
    'aria-busy': isStreaming,
    'aria-label': options.label,
    'aria-live': isStreaming ? 'polite' : 'off',
  } as ReasoningElementProps;
}
