import {
  canEditThreadMessage,
  canRetryThreadMessage,
  getMessageStatus,
  type Message,
  type ThreadRevisionResult,
} from '@hpd-research/hpd-agent-headless-ui';
import { mergeProps } from '../thread-composer/index.js';
import type {
  MessageActionBarActions,
  MessageActionBarAutohide,
  MessageActionBarElementProps,
  MessageActionBarFloat,
  MessageActionBarProps,
  MessageActionBarState,
  MessageCopyText,
} from './types.js';

type EventHandler<TEvent extends Event> = (event: TEvent) => void;

export interface CreateMessageActionBarStateOptions {
  autohide?: MessageActionBarAutohide;
  copied?: boolean;
  copyText?: string;
  float?: MessageActionBarFloat;
  focused?: boolean;
  hideWhenBusy?: boolean;
  hovered?: boolean;
  interactionCount?: number;
  branchCount?: number;
  isLast?: boolean;
  message: Message;
  onEditRequest?: MessageActionBarProps['onEditRequest'];
  onRetryRequest?: MessageActionBarProps['onRetryRequest'];
  pending?: boolean;
  revisions?: MessageActionBarProps['revisions'];
  status?: MessageActionBarProps['status'];
}

export interface CreateMessageActionBarElementPropsOptions {
  copyLabel?: string;
  editLabel?: string;
  onCopyClick: EventHandler<MouseEvent>;
  onEditClick: EventHandler<MouseEvent>;
  onRetryClick: EventHandler<MouseEvent>;
  restProps?: Record<string, unknown>;
  retryLabel?: string;
  state: MessageActionBarState;
}

export interface CreateMessageActionBarActionsOptions {
  clearCopiedTimer?: () => void;
  copyText?: MessageCopyText;
  message: Message;
  onCopy?: MessageActionBarProps['onCopy'];
  onEditRequest?: MessageActionBarProps['onEditRequest'];
  onRetryRequest?: MessageActionBarProps['onRetryRequest'];
  onRevisionCreated?: MessageActionBarProps['onRevisionCreated'];
  revisions?: MessageActionBarProps['revisions'];
  setCopied?: (value: boolean) => void;
  setCopiedTimer?: (timer: ReturnType<typeof setTimeout> | null) => void;
  setInteractionCount?: (update: (value: number) => number) => void;
  setPending?: (value: boolean) => void;
  state: MessageActionBarState;
  copiedDuration?: number;
}

export function getDefaultMessageCopyText(message: Message): string {
  return message.content;
}

export function createMessageActionBarState(
  options: CreateMessageActionBarStateOptions,
): MessageActionBarState {
  const status = options.status ?? getMessageStatus(options.message);
  const copied = options.copied ?? false;
  const pending = options.pending ?? false;
  const hovered = options.hovered ?? false;
  const focused = options.focused ?? false;
  const interactionCount = options.interactionCount ?? 0;
  const canCopy = Boolean(options.copyText?.length);
  const canEdit = Boolean(options.onEditRequest) && canEditThreadMessage(options.message);
  const canRetry = (
    Boolean(options.onRetryRequest) || Boolean(options.revisions)
  ) && canRetryThreadMessage(options.message);
  const visible = getMessageActionBarVisible({
    autohide: options.autohide,
    focused,
    hideWhenBusy: options.hideWhenBusy,
    hovered,
    interactionCount,
    isLast: options.isLast,
    message: options.message,
    pending,
  });
  const floating = getMessageActionBarFloating({
    float: options.float,
    branchCount: options.branchCount,
    message: options.message,
    visible,
  });

  return {
    canCopy,
    canEdit,
    canRetry,
    copied,
    floating,
    focused,
    hovered,
    pending,
    status,
    visible,
  };
}

export function createMessageActionBarElementProps(
  options: CreateMessageActionBarElementPropsOptions,
): MessageActionBarElementProps {
  const { state } = options;

  return {
    root: mergeProps(options.restProps ?? {}, {
      'data-hpd-message-action-bar': '',
      'data-copied': state.copied ? '' : undefined,
      'data-floating': state.floating ? '' : undefined,
      'data-pending': state.pending ? '' : undefined,
      'data-status': state.status,
      'data-visible': state.visible ? '' : undefined,
    }) as MessageActionBarElementProps['root'],
    copy: {
      type: 'button',
      'data-hpd-message-action': 'copy',
      disabled: !state.canCopy || state.pending,
      'aria-label': options.copyLabel ?? 'Copy message',
      onclick: options.onCopyClick,
    },
    edit: {
      type: 'button',
      'data-hpd-message-action': 'edit',
      disabled: !state.canEdit || state.pending,
      'aria-label': options.editLabel ?? 'Edit message',
      onclick: options.onEditClick,
    },
    retry: {
      type: 'button',
      'data-hpd-message-action': 'retry',
      disabled: !state.canRetry || state.pending,
      'aria-label': options.retryLabel ?? 'Retry from message',
      onclick: options.onRetryClick,
    },
  };
}

export function createMessageActionBarActions(
  options: CreateMessageActionBarActionsOptions,
): MessageActionBarActions {
  const readCopyText = options.copyText ?? getDefaultMessageCopyText;
  const copiedDuration = options.copiedDuration ?? 1600;

  return {
    acquireInteractionLock() {
      let released = false;
      options.setInteractionCount?.((value) => value + 1);
      return () => {
        if (released) return;
        released = true;
        options.setInteractionCount?.((value) => Math.max(0, value - 1));
      };
    },
    async copy() {
      if (!options.state.canCopy || options.state.pending) return;
      const text = readCopyText(options.message);
      await writeClipboardText(text);
      options.clearCopiedTimer?.();
      options.setCopied?.(true);
      const timer = setTimeout(() => {
        options.setCopied?.(false);
        options.setCopiedTimer?.(null);
      }, copiedDuration);
      options.setCopiedTimer?.(timer);
      await options.onCopy?.({ message: options.message, text });
    },
    requestEdit() {
      if (!options.state.canEdit || options.state.pending) return;
      options.onEditRequest?.({ message: options.message });
    },
    async retry(): Promise<ThreadRevisionResult | undefined> {
      if (!options.state.canRetry || options.state.pending) return undefined;

      options.setPending?.(true);
      try {
        if (options.revisions) {
          const revision = await options.revisions.forkAndRetryMessage(options.message.id);
          await options.onRevisionCreated?.({
            message: options.message,
            revision,
          });
          return revision;
        }

        await options.onRetryRequest?.({ message: options.message });
        return undefined;
      } finally {
        options.setPending?.(false);
      }
    },
  };
}

export function getMessageActionBarVisible(options: {
  autohide?: MessageActionBarAutohide;
  focused: boolean;
  hideWhenBusy?: boolean;
  hovered: boolean;
  interactionCount: number;
  isLast?: boolean;
  message: Message;
  pending: boolean;
}): boolean {
  if (options.hideWhenBusy && (options.message.streaming || options.message.thinking)) return false;
  if (options.pending) return true;

  const autohide = options.autohide ?? 'never';
  const isLast = options.isLast ?? true;
  const autohideEnabled = autohide === 'always'
    || (autohide === 'not-last' && !isLast);

  if (!autohideEnabled) return true;
  return options.hovered || options.focused || options.interactionCount > 0;
}

export function getMessageActionBarFloating(options: {
  float?: MessageActionBarFloat;
  branchCount?: number;
  message: Message;
  visible: boolean;
}): boolean {
  if (!options.visible) return false;
  const float = options.float ?? 'never';
  if (float === 'always') return true;
  if (float === 'single-branch') {
    return (options.branchCount ?? 1) <= 1;
  }
  return false;
}

async function writeClipboardText(text: string): Promise<void> {
  const clipboard = globalThis.navigator?.clipboard;
  if (!clipboard?.writeText) return;
  await clipboard.writeText(text);
}
