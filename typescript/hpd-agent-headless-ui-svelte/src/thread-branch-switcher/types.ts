import type { Snippet } from 'svelte';
import type { SvelteHTMLElements } from 'svelte/elements';
import type {
  ThreadBranchChoiceControl,
} from '@hpd-research/hpd-agent-headless-ui';
import type { ThreadForkGroupMember } from '@hpd-research/hpd-agent-client';

type DivProps = Omit<SvelteHTMLElements['div'], 'children'>;
type ButtonProps = Omit<SvelteHTMLElements['button'], 'children'>;
type SpanProps = Omit<SvelteHTMLElements['span'], 'children'>;

export type ThreadBranchSwitcherDirection = 'previous' | 'next';

export interface ThreadBranchSwitcherSelectDetails {
  control: ThreadBranchChoiceControl;
  direction: ThreadBranchSwitcherDirection;
  member: ThreadForkGroupMember;
  threadId: string;
}

export interface ThreadBranchSwitcherActionProps extends ButtonProps {
  'data-hpd-thread-branch-switcher-action': '';
  'data-direction': ThreadBranchSwitcherDirection;
}

export interface ThreadBranchSwitcherElementProps extends DivProps {
  'data-hpd-thread-branch-switcher': '';
  'data-group-id': string;
  'data-current': string;
  'data-total': string;
}

export interface ThreadBranchSwitcherChildProps {
  control: ThreadBranchChoiceControl;
  current: number;
  label: string;
  next: ThreadForkGroupMember | null;
  nextProps: ThreadBranchSwitcherActionProps;
  previous: ThreadForkGroupMember | null;
  previousProps: ThreadBranchSwitcherActionProps;
  props: ThreadBranchSwitcherElementProps;
  selectNext: () => void;
  selectPrevious: () => void;
  total: number;
}

export interface ThreadBranchSwitcherActionComponentProps extends ButtonProps {
  control: ThreadBranchChoiceControl;
  direction: ThreadBranchSwitcherDirection;
  onSelect?: (details: ThreadBranchSwitcherSelectDetails) => void;
}

export interface ThreadBranchSwitcherPreviousProps extends Omit<ThreadBranchSwitcherActionComponentProps, 'direction'> {}

export interface ThreadBranchSwitcherNextProps extends Omit<ThreadBranchSwitcherActionComponentProps, 'direction'> {}

export interface ThreadBranchSwitcherLabelProps extends SpanProps {
  control: ThreadBranchChoiceControl;
}

export interface ThreadBranchSwitcherNumberProps extends SpanProps {
  control: ThreadBranchChoiceControl;
}

export interface ThreadBranchSwitcherCountProps extends SpanProps {
  control: ThreadBranchChoiceControl;
}

export interface ThreadBranchSwitcherProps extends DivProps {
  children?: Snippet<[ThreadBranchSwitcherChildProps]>;
  control: ThreadBranchChoiceControl;
  onSelect?: (details: ThreadBranchSwitcherSelectDetails) => void;
}
