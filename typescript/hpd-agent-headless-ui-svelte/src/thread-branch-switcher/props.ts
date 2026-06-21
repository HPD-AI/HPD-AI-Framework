import {
  getThreadBranchChoiceControlLabel,
  type ThreadBranchChoiceControl,
} from '@hpd-research/hpd-agent-headless-ui';
import type {
  ThreadBranchSwitcherActionProps,
  ThreadBranchSwitcherDirection,
  ThreadBranchSwitcherElementProps,
  ThreadBranchSwitcherSelectDetails,
} from './types.js';

export function createThreadBranchSwitcherElementProps(
  control: ThreadBranchChoiceControl,
  restProps: Record<string, unknown> = {},
): ThreadBranchSwitcherElementProps {
  return {
    ...restProps,
    'data-hpd-thread-branch-switcher': '',
    'data-group-id': control.groupId,
    'data-current': String(control.position.current),
    'data-total': String(control.position.total),
  } as ThreadBranchSwitcherElementProps;
}

export function createThreadBranchSwitcherActionProps(
  direction: ThreadBranchSwitcherDirection,
  disabled: boolean,
): ThreadBranchSwitcherActionProps {
  return {
    type: 'button',
    disabled,
    'aria-label': direction === 'previous' ? 'Previous branch' : 'Next branch',
    'data-hpd-thread-branch-switcher-action': '',
    'data-direction': direction,
  };
}

export function getThreadBranchSwitcherLabel(control: ThreadBranchChoiceControl): string {
  return getThreadBranchChoiceControlLabel(control);
}

export function getThreadBranchSwitcherNumber(control: ThreadBranchChoiceControl): string {
  return String(control.position.current);
}

export function getThreadBranchSwitcherCount(control: ThreadBranchChoiceControl): string {
  return String(control.position.total);
}

export function getThreadBranchSwitcherMember(
  control: ThreadBranchChoiceControl,
  direction: ThreadBranchSwitcherDirection,
) {
  return direction === 'previous' ? control.previous : control.next;
}

export function createThreadBranchSwitcherSelectDetails(
  control: ThreadBranchChoiceControl,
  direction: ThreadBranchSwitcherDirection,
): ThreadBranchSwitcherSelectDetails | null {
  const member = getThreadBranchSwitcherMember(control, direction);
  if (!member) return null;

  return {
    control,
    direction,
    member,
    threadId: member.threadId,
  };
}
