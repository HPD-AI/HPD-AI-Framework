import { mergeProps } from '../thread-composer/props.js';
import type {
  MessageEditActionProps,
  MessageEditElementProps,
} from './types.js';

type EventHandler<TEvent extends Event> = (event: TEvent) => void;

export interface CreateMessageEditElementPropsOptions {
  canSave: boolean;
  cancelLabel: string;
  draft: string;
  editing: boolean;
  error: Error | null;
  pending: boolean;
  placeholder: string;
  restProps: Record<string, unknown>;
  saveLabel: string;
  onCancelClick: EventHandler<MouseEvent>;
  onInput: EventHandler<Event>;
  onKeydown: EventHandler<KeyboardEvent>;
  onSaveClick: EventHandler<MouseEvent>;
}

export function createMessageEditElementProps(
  options: CreateMessageEditElementPropsOptions,
): MessageEditElementProps {
  const isEmpty = options.draft.trim().length === 0;

  return {
    root: mergeProps(options.restProps, {
      'data-hpd-message-edit': '',
      'data-editing': options.editing ? '' : undefined,
      'data-pending': options.pending ? '' : undefined,
      'data-empty': isEmpty ? '' : undefined,
      'data-can-save': options.canSave ? '' : undefined,
      'data-error': options.error ? '' : undefined,
    }) as MessageEditElementProps['root'],
    textarea: {
      'aria-invalid': Boolean(options.error),
      'aria-multiline': 'true',
      'data-hpd-message-edit-textarea': '',
      disabled: options.pending,
      oninput: options.onInput,
      onkeydown: options.onKeydown,
      placeholder: options.placeholder,
      rows: 1,
      value: options.draft,
    },
  };
}

export function createMessageEditActionProps(
  options: CreateMessageEditElementPropsOptions,
): MessageEditActionProps {
  return {
    save: {
      'aria-disabled': !options.canSave,
      'aria-label': options.saveLabel,
      'data-hpd-message-edit-save': '',
      disabled: !options.canSave,
      onclick: options.onSaveClick,
      type: 'button',
    },
    cancel: {
      'aria-disabled': options.pending,
      'aria-label': options.cancelLabel,
      'data-hpd-message-edit-cancel': '',
      disabled: options.pending,
      onclick: options.onCancelClick,
      type: 'button',
    },
  };
}
