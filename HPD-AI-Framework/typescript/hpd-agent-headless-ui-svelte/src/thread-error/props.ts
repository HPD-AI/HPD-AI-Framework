import {
  getThreadErrors,
  type ThreadErrorInfo,
} from '@hpd-research/hpd-agent-headless-ui';
import { mergeProps } from '../thread-composer/index.js';
import type { ThreadState, ThreadStateSnapshot } from '../thread-state.js';
import type {
  ThreadErrorActions,
  ThreadErrorElementProps,
  ThreadErrorModel,
} from './types.js';

export interface CreateThreadErrorModelOptions {
  clear?: () => void;
  snapshot?: ThreadStateSnapshot;
}

export function createThreadErrorModel(
  thread: ThreadState,
  options: CreateThreadErrorModelOptions = {},
): ThreadErrorModel {
  const snapshot = options.snapshot ?? thread.getSnapshot();
  const projectionErrors = getThreadErrors(snapshot.projection);
  const errors = includeControllerError(snapshot.error, projectionErrors);
  const error = errors.at(-1) ?? null;
  const actions: ThreadErrorActions = {
    clear: options.clear ?? (() => thread.clearError()),
  };

  return {
    actions,
    error,
    errors,
    hasError: error !== null,
    label: error?.message ?? 'No thread error',
    snapshot,
  };
}

export function createThreadErrorElementProps(
  model: ThreadErrorModel,
  restProps: Record<string, unknown> = {},
  clearLabel = 'Dismiss error',
): ThreadErrorElementProps {
  return {
    root: mergeProps(restProps, {
      'aria-live': 'polite',
      'data-error-kind': model.error?.kind,
      'data-hpd-thread-error': '',
      'data-recoverable': model.error?.recoverable ? '' : undefined,
      role: 'alert',
    }),
    clearButton: {
      'aria-label': clearLabel,
      'data-hpd-thread-error-clear': '',
      disabled: !model.error?.recoverable || undefined,
      type: 'button',
    },
  } as unknown as ThreadErrorElementProps;
}

function includeControllerError(
  message: string | null,
  errors: ThreadErrorInfo[],
): ThreadErrorInfo[] {
  if (!message) return errors;
  if (errors.some((error) => error.message === message)) return errors;

  return [
    ...errors,
    {
      id: 'controller:error',
      kind: 'controller',
      message,
      recoverable: true,
    },
  ];
}
