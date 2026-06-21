import type { Snippet } from 'svelte';
import type { SvelteHTMLElements } from 'svelte/elements';
import type { ThreadErrorInfo } from '@hpd-research/hpd-agent-headless-ui';
import type {
  ThreadState,
  ThreadStateSnapshot,
} from '../thread-state.js';

type DivProps = Omit<SvelteHTMLElements['div'], 'children'>;
type ButtonProps = Omit<SvelteHTMLElements['button'], 'children'>;

export interface ThreadErrorActions {
  clear(): void;
}

export interface ThreadErrorModel {
  actions: ThreadErrorActions;
  error: ThreadErrorInfo | null;
  errors: ThreadErrorInfo[];
  hasError: boolean;
  label: string;
  snapshot: ThreadStateSnapshot;
}

export interface ThreadErrorRootProps extends DivProps {
  'aria-live': 'polite';
  'data-error-kind'?: ThreadErrorInfo['kind'];
  'data-hpd-thread-error': '';
  'data-recoverable'?: '';
  role: 'alert';
}

export interface ThreadErrorClearButtonProps extends ButtonProps {
  'data-hpd-thread-error-clear': '';
  disabled?: boolean;
  type: 'button';
}

export interface ThreadErrorElementProps {
  root: ThreadErrorRootProps;
  clearButton: ThreadErrorClearButtonProps;
}

export interface ThreadErrorChildProps extends ThreadErrorModel {
  props: ThreadErrorElementProps;
}

export interface ThreadErrorProps extends DivProps {
  child?: Snippet<[ThreadErrorChildProps]>;
  children?: Snippet<[ThreadErrorModel]>;
  clearLabel?: string;
  showAll?: boolean;
  thread: ThreadState;
}
