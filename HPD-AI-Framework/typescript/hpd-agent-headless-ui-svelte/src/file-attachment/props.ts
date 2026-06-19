import type {
  FileAttachmentActions,
  FileAttachmentDropzoneActions,
  FileAttachmentDropzoneElementProps,
  FileAttachmentDropzoneState,
  FileAttachmentElementProps,
  FileAttachmentSnapshot,
  PendingFileAttachment,
} from './types.js';
import type { Attachment } from 'svelte/attachments';
import type { AIContent } from '@hpd-research/hpd-agent-client';
import { mergeProps } from '../thread-composer/props.js';

export interface CreateFileAttachmentElementPropsOptions {
  accept?: string;
  actions: FileAttachmentActions;
  attachments: PendingFileAttachment[];
  disabled: boolean;
  inputAttachment: Attachment<HTMLInputElement>;
  inputProps?: Record<string, unknown>;
  multiple: boolean;
  onInputChange: (event: Event) => void;
  rootProps: Record<string, unknown>;
  triggerLabel: string;
}

export interface CreateFileAttachmentSnapshotOptions {
  attachments: PendingFileAttachment[];
  canSubmit: boolean;
  disabled: boolean;
  inputRef: HTMLInputElement | null;
  readyContents: AIContent[];
}

export interface CreateFileAttachmentDropzoneStateOptions {
  disabled: boolean;
  dragging: boolean;
}

export interface CreateFileAttachmentDropzoneActionsOptions {
  add(files: FileList | File[]): Promise<void>;
  setDragging(value: boolean): void;
}

export interface CreateFileAttachmentDropzoneElementPropsOptions {
  disabled: boolean;
  dragging: boolean;
  onDragEnter: (event: DragEvent) => void;
  onDragLeave: (event: DragEvent) => void;
  onDragOver: (event: DragEvent) => void;
  onDrop: (event: DragEvent) => void;
  rootProps: Record<string, unknown>;
}

export function createFileAttachmentElementProps(
  options: CreateFileAttachmentElementPropsOptions,
): FileAttachmentElementProps {
  const hasAttachments = options.attachments.length > 0;
  const isUploading = options.attachments.some((attachment) => attachment.status === 'uploading');
  const hasError = options.attachments.some((attachment) => attachment.status === 'error');
  const hasReady = options.attachments.some((attachment) => attachment.status === 'ready');

  return {
    root: mergeProps(options.rootProps, {
      'data-hpd-file-attachment': '',
      'data-disabled': options.disabled ? '' : undefined,
      'data-empty': hasAttachments ? undefined : '',
      'data-uploading': isUploading ? '' : undefined,
      'data-error': hasError ? '' : undefined,
      'data-ready': hasReady ? '' : undefined,
    }),
    input: {
      ...(options.inputProps ?? {}),
      'data-hpd-file-attachment-input': '',
      accept: options.accept,
      disabled: options.disabled,
      multiple: options.multiple,
      onchange: options.onInputChange,
      type: 'file',
    },
    inputAttachment: options.inputAttachment,
    trigger: {
      'aria-disabled': options.disabled,
      'aria-label': options.triggerLabel,
      'data-hpd-file-attachment-trigger': '',
      disabled: options.disabled,
      onclick: (event: MouseEvent) => {
        event.preventDefault();
        options.actions.open();
      },
      type: 'button',
    },
  } as unknown as FileAttachmentElementProps;
}

export function createFileAttachmentSnapshot(
  options: CreateFileAttachmentSnapshotOptions,
): FileAttachmentSnapshot {
  const empty = options.attachments.length === 0;
  const uploading = options.attachments.some((attachment) => attachment.status === 'uploading');
  const errored = options.attachments.some((attachment) => attachment.status === 'error');
  const ready = options.attachments.some((attachment) => attachment.status === 'ready');

  return {
    attachments: options.attachments,
    canSubmit: options.canSubmit,
    disabled: options.disabled,
    empty,
    errored,
    inputRef: options.inputRef,
    ready,
    readyContents: options.readyContents,
    uploading,
  };
}

export function createFileAttachmentDropzoneState(
  options: CreateFileAttachmentDropzoneStateOptions,
): FileAttachmentDropzoneState {
  return {
    disabled: options.disabled,
    dragging: options.dragging,
  };
}

export function createFileAttachmentDropzoneActions(
  options: CreateFileAttachmentDropzoneActionsOptions,
): FileAttachmentDropzoneActions {
  return {
    async drop(event) {
      if (!event.dataTransfer?.files?.length) return;
      options.setDragging(false);
      await options.add(event.dataTransfer.files);
    },
  };
}

export function createFileAttachmentDropzoneElementProps(
  options: CreateFileAttachmentDropzoneElementPropsOptions,
): FileAttachmentDropzoneElementProps {
  return {
    root: mergeProps(options.rootProps, {
      'data-hpd-file-attachment-dropzone': '',
      'data-disabled': options.disabled ? '' : undefined,
      'data-dragging': options.dragging ? '' : undefined,
      ondragenter: options.onDragEnter,
      ondragleave: options.onDragLeave,
      ondragover: options.onDragOver,
      ondrop: options.onDrop,
    }),
  } as FileAttachmentDropzoneElementProps;
}
