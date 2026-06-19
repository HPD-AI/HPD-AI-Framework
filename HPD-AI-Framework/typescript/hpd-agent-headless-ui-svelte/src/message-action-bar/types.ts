import type { Snippet } from 'svelte';
import type { Attachment } from 'svelte/attachments';
import type { SvelteHTMLElements } from 'svelte/elements';
import type {
  Message,
  MessageStatus,
  ThreadRevisionResult,
} from '@hpd-research/hpd-agent-headless-ui';
import type { ThreadRevisionState } from '../thread-revisions.js';

type DivProps = Omit<SvelteHTMLElements['div'], 'children'>;
type ButtonProps = Omit<SvelteHTMLElements['button'], 'children'>;

export type MessageActionBarAutohide = 'always' | 'not-last' | 'never';
export type MessageActionBarFloat = 'always' | 'single-branch' | 'never';
export type MessageActionBarAction = 'copy' | 'edit' | 'retry';

export interface MessageActionDetails {
  message: Message;
}

export interface MessageCopyDetails extends MessageActionDetails {
  text: string;
}

export interface MessageActionRevisionDetails extends MessageActionDetails {
  revision: ThreadRevisionResult;
}

export type MessageCopyText = (message: Message) => string;

export interface MessageActionBarState {
  canCopy: boolean;
  canEdit: boolean;
  canRetry: boolean;
  copied: boolean;
  floating: boolean;
  focused: boolean;
  hovered: boolean;
  pending: boolean;
  status: MessageStatus;
  visible: boolean;
}

export interface MessageActionBarActions {
  acquireInteractionLock(): () => void;
  copy(): Promise<void>;
  requestEdit(): void;
  retry(): Promise<ThreadRevisionResult | undefined>;
}

export type MessageActionBarRootProps = DivProps & {
  'data-hpd-message-action-bar': '';
  'data-copied'?: '';
  'data-floating'?: '';
  'data-pending'?: '';
  'data-status': MessageStatus;
  'data-visible'?: '';
};

export type MessageActionBarButtonProps = ButtonProps & {
  'data-hpd-message-action': MessageActionBarAction;
};

export interface MessageActionBarElementProps {
  root: MessageActionBarRootProps;
  copy: MessageActionBarButtonProps;
  edit: MessageActionBarButtonProps;
  retry: MessageActionBarButtonProps;
}

export interface MessageActionBarChildProps {
  actions: MessageActionBarActions;
  message: Message;
  props: MessageActionBarElementProps;
  rootAttachment: Attachment<HTMLElement>;
  state: MessageActionBarState;
}

export interface MessageActionBarProps extends DivProps {
  autohide?: MessageActionBarAutohide;
  children?: Snippet<[MessageActionBarChildProps]>;
  copiedDuration?: number;
  copyLabel?: string;
  copyText?: MessageCopyText;
  editLabel?: string;
  float?: MessageActionBarFloat;
  hideWhenBusy?: boolean;
  isLast?: boolean;
  branchCount?: number;
  message: Message;
  onCopy?: (details: MessageCopyDetails) => void | Promise<void>;
  onEditRequest?: (details: MessageActionDetails) => void;
  onRetryRequest?: (details: MessageActionDetails) => void | Promise<void>;
  onRevisionCreated?: (details: MessageActionRevisionDetails) => void | Promise<void>;
  retryLabel?: string;
  revisions?: Pick<ThreadRevisionState, 'forkAndRetryMessage'>;
  status?: MessageStatus;
}
