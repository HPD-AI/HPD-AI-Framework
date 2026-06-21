import type { ThreadComposerAutosizeStrategy } from './autosize.js';
import type {
  ThreadComposerBlockedReason,
  ThreadComposerActions,
  ThreadComposerElementProps,
  ThreadComposerState,
  ThreadComposerSubmitMode,
} from './types.js';
import type { Attachment } from 'svelte/attachments';
import type { AIContent } from '@hpd-research/hpd-agent-client';
import type { PendingFileAttachment } from '../file-attachment/index.js';
import type { TextSubmissionState } from '@hpd-research/hpd-agent-headless-ui';

type EventHandler<TEvent extends Event> = (event: TEvent) => void;

export interface CreateThreadComposerElementPropsOptions {
  autosize: ThreadComposerAutosizeStrategy;
  blockedReason: ThreadComposerBlockedReason;
  canInterrupt: boolean;
  canSubmit: boolean;
  disabled: boolean;
  focused: boolean;
  formProps: Record<string, unknown>;
  isBusy: boolean;
  isEmpty: boolean;
  isSubmitting: boolean;
  inputAttachment: Attachment<HTMLTextAreaElement>;
  onBlur: EventHandler<FocusEvent>;
  onFocus: EventHandler<FocusEvent>;
  onInput: EventHandler<InputEvent>;
  onInterruptClick: EventHandler<MouseEvent>;
  onKeydown: EventHandler<KeyboardEvent>;
  onSubmit: EventHandler<SubmitEvent>;
  placeholder: string;
  value: string;
}

export interface CreateThreadComposerStateOptions {
  attachments: PendingFileAttachment[];
  blockedReason: ThreadComposerBlockedReason;
  canInterrupt: boolean;
  canSubmit: boolean;
  disabled: boolean;
  focused: boolean;
  isBusy: boolean;
  isEmpty: boolean;
  isSubmitting: boolean;
  readyContents: AIContent[];
  textSubmissionState: TextSubmissionState;
  value: string;
}

export interface CreateThreadComposerActionsOptions {
  clear: () => void;
  focus: (options?: FocusOptions) => void;
  interrupt: () => Promise<void>;
  setValue: (value: string) => void;
  submit: () => Promise<void>;
}

export function createThreadComposerElementProps(
  options: CreateThreadComposerElementPropsOptions,
): ThreadComposerElementProps {
  const {
    autosize,
    blockedReason,
    canInterrupt,
    canSubmit,
    disabled,
    focused,
    formProps,
    inputAttachment,
    isBusy,
    isEmpty,
    isSubmitting,
    onBlur,
    onFocus,
    onInput,
    onInterruptClick,
    onKeydown,
    onSubmit,
    placeholder,
    value,
  } = options;

  return {
    root: mergeProps(formProps, {
      'data-hpd-thread-composer': '',
      'data-autosize': autosize === false ? undefined : autosize === 'pretext' ? 'pretext' : 'custom',
      'data-busy': isBusy ? '' : undefined,
      'data-can-submit': canSubmit ? '' : undefined,
      'data-disabled': disabled ? '' : undefined,
      'data-empty': isEmpty ? '' : undefined,
      'data-blocked-reason': blockedReason ?? undefined,
      onsubmit: onSubmit,
    }),
    input: {
      'aria-disabled': disabled,
      'aria-multiline': 'true',
      'data-hpd-thread-composer-textarea': '',
      'data-empty': isEmpty ? '' : undefined,
      'data-focused': focused ? '' : undefined,
      disabled,
      onblur: onBlur,
      onfocus: onFocus,
      oninput: onInput,
      onkeydown: onKeydown,
      placeholder,
      rows: 1,
      value,
    },
    inputAttachment,
    submit: {
      'aria-disabled': !canSubmit,
      'data-hpd-thread-composer-submit': '',
      disabled: !canSubmit,
      type: 'submit',
    },
    interrupt: {
      'aria-disabled': !canInterrupt,
      'data-hpd-thread-composer-interrupt': '',
      disabled: !canInterrupt || isSubmitting,
      onclick: onInterruptClick,
      type: 'button',
    },
  } as unknown as ThreadComposerElementProps;
}

export function createThreadComposerState(options: CreateThreadComposerStateOptions): ThreadComposerState {
  return {
    attachmentCount: options.attachments.length,
    attachments: options.attachments,
    blockedReason: options.blockedReason,
    busy: options.isBusy,
    canInterrupt: options.canInterrupt,
    canSubmit: options.canSubmit,
    disabled: options.disabled,
    empty: options.isEmpty,
    focused: options.focused,
    readyAttachmentCount: options.readyContents.length,
    readyContents: options.readyContents,
    submitting: options.isSubmitting,
    textSubmissionState: options.textSubmissionState,
    value: options.value,
  };
}

export function createThreadComposerActions(
  options: CreateThreadComposerActionsOptions,
): ThreadComposerActions {
  return {
    clear: options.clear,
    focus: options.focus,
    interrupt: options.interrupt,
    setValue: options.setValue,
    submit: options.submit,
  };
}

export function shouldSubmitForKeyboardEvent(
  event: KeyboardEvent,
  submitMode: ThreadComposerSubmitMode,
): boolean {
  if (event.key !== 'Enter' || event.shiftKey || event.isComposing) return false;
  if (submitMode === 'none') return false;
  if (submitMode === 'mod-enter') return event.metaKey || event.ctrlKey;
  return true;
}

export function mergeProps(
  restProps: Record<string, unknown>,
  internalProps: Record<string, unknown>,
): Record<string, unknown> {
  const merged: Record<string, unknown> = { ...restProps };

  for (const [key, value] of Object.entries(internalProps)) {
    if (value === undefined) continue;

    const existing = merged[key];
    if (isEventHandlerKey(key) && typeof existing === 'function' && typeof value === 'function') {
      merged[key] = composeEventHandlers(existing as EventHandler<Event>, value as EventHandler<Event>);
      continue;
    }

    if (key === 'class' && existing !== undefined && value !== undefined) {
      merged[key] = `${existing as string} ${value as string}`;
      continue;
    }

    merged[key] = value;
  }

  return merged;
}

function composeEventHandlers<TEvent extends Event>(
  userHandler: EventHandler<TEvent>,
  internalHandler: EventHandler<TEvent>,
): EventHandler<TEvent> {
  return (event) => {
    userHandler(event);
    if (!event.defaultPrevented) internalHandler(event);
  };
}

function isEventHandlerKey(key: string): boolean {
  return key.startsWith('on') && key.length > 2;
}
