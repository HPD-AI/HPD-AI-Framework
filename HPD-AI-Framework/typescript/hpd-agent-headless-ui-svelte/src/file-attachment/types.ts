import type { Snippet } from 'svelte';
import type { Attachment } from 'svelte/attachments';
import type { SvelteHTMLElements } from 'svelte/elements';
import type { AIContent, ContentReference } from '@hpd-research/hpd-agent-client';
import type { FileAttachmentState } from './file-attachment-state.svelte.js';

type DivProps = Omit<SvelteHTMLElements['div'], 'children'>;
type InputProps = Omit<SvelteHTMLElements['input'], 'children'>;
type ButtonProps = Omit<SvelteHTMLElements['button'], 'children'>;

export type FileAttachmentStatus = 'uploading' | 'ready' | 'error';

export interface PendingFileAttachment {
  id: string;
  file: File;
  status: FileAttachmentStatus;
  content?: ContentReference;
  error?: string;
}

export interface FileAttachmentUploadDetails {
  sessionId: string;
  threadId: string;
  file: File;
}

export type FileAttachmentUpload = (details: FileAttachmentUploadDetails) => Promise<ContentReference>;

export interface FileAttachmentClient {
  uploadContent(sessionId: string, threadId: string, file: File | Blob, name?: string): Promise<ContentReference>;
}

export interface FileAttachmentStateOptions {
  client?: FileAttachmentClient;
  upload?: FileAttachmentUpload;
  sessionId: string;
  threadId: string;
  disabled?: boolean;
}

export interface FileAttachmentElementProps {
  root: DivProps & {
    'data-hpd-file-attachment': '';
    'data-disabled'?: '';
    'data-empty'?: '';
    'data-uploading'?: '';
    'data-error'?: '';
    'data-ready'?: '';
  };
  input: InputProps & {
    'data-hpd-file-attachment-input': '';
  };
  inputAttachment: Attachment<HTMLInputElement>;
  trigger: ButtonProps & {
    'data-hpd-file-attachment-trigger': '';
  };
}

export interface FileAttachmentDropzoneElementProps {
  root: DivProps & {
    'data-hpd-file-attachment-dropzone': '';
    'data-disabled'?: '';
    'data-dragging'?: '';
  };
}

export interface FileAttachmentActions {
  add(files: FileList | File[]): Promise<void>;
  clear(): void;
  open(): void;
  remove(id: string): void;
  retry(id: string): Promise<void>;
}

export interface FileAttachmentSnapshot {
  attachments: PendingFileAttachment[];
  canSubmit: boolean;
  disabled: boolean;
  empty: boolean;
  errored: boolean;
  inputRef: HTMLInputElement | null;
  readyContents: AIContent[];
  ready: boolean;
  uploading: boolean;
}

export interface FileAttachmentApi {
  actions: FileAttachmentActions;
  props: FileAttachmentElementProps;
  state: FileAttachmentSnapshot;
}

export interface FileAttachmentDropzoneState {
  disabled: boolean;
  dragging: boolean;
}

export interface FileAttachmentDropzoneActions {
  drop(event: DragEvent): Promise<void>;
}

export interface FileAttachmentDropzoneApi {
  actions: FileAttachmentDropzoneActions;
  props: FileAttachmentDropzoneElementProps;
  state: FileAttachmentDropzoneState;
}

export type FileAttachmentDropzoneChildProps = FileAttachmentDropzoneApi;
export type FileAttachmentDropzoneChildrenProps = FileAttachmentDropzoneApi;

export interface FileAttachmentDropzoneProps extends DivProps {
  child?: Snippet<[FileAttachmentDropzoneChildProps]>;
  children?: Snippet<[FileAttachmentDropzoneChildrenProps]>;
  disabled?: boolean;
  state: FileAttachmentState;
}

export type FileAttachmentChildProps = FileAttachmentApi;
export type FileAttachmentChildrenProps = FileAttachmentApi;

export interface FileAttachmentProps extends DivProps {
  accept?: string;
  child?: Snippet<[FileAttachmentChildProps]>;
  children?: Snippet<[FileAttachmentChildrenProps]>;
  client?: FileAttachmentClient;
  disabled?: boolean;
  multiple?: boolean;
  sessionId?: string;
  state?: FileAttachmentState;
  threadId?: string;
  triggerLabel?: string;
  upload?: FileAttachmentUpload;
}
