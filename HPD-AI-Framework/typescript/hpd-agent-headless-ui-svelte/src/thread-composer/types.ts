import type { Snippet } from 'svelte';
import type { Attachment } from 'svelte/attachments';
import type { SvelteHTMLElements } from 'svelte/elements';
import type { AIContent } from '@hpd-research/hpd-agent-client';
import type { TextSubmissionState } from '@hpd-research/hpd-agent-headless-ui';
import type { ThreadState } from '../thread-state.js';
import type { ThreadQuote } from '../selection-toolbar/index.js';
import type {
  ThreadComposerAutosizeStrategy,
  ThreadComposerPretextOptions,
} from './autosize.js';
import type { FileAttachmentState, PendingFileAttachment } from '../file-attachment/index.js';

type FormProps = Omit<SvelteHTMLElements['form'], 'children'>;
type TextareaProps = Omit<SvelteHTMLElements['textarea'], 'children'>;
type ButtonProps = Omit<SvelteHTMLElements['button'], 'children'>;
type SendMessageOptions = NonNullable<Parameters<ThreadState['sendMessage']>[1]>;

export type ThreadComposerRunConfig = SendMessageOptions['runConfig'];
export type ThreadComposerClearMode = 'on-submit' | 'never';
export type ThreadComposerSubmitMode = 'enter' | 'mod-enter' | 'none';

export type ThreadComposerBlockedReason =
  | 'empty'
  | 'attachments-uploading'
  | 'attachment-error'
  | 'disabled'
  | 'error'
  | 'runtime-request'
  | 'busy'
  | 'not-sendable'
  | null;

export interface ThreadComposerElementProps {
  root: FormProps & {
    'data-hpd-thread-composer': '';
    'data-autosize'?: 'pretext' | 'custom';
    'data-busy'?: '';
    'data-can-submit'?: '';
    'data-disabled'?: '';
    'data-empty'?: '';
  };
  input: TextareaProps & {
    'data-hpd-thread-composer-textarea': '';
    'aria-disabled': boolean;
    'aria-multiline': 'true';
  };
  inputAttachment: Attachment<HTMLTextAreaElement>;
  interrupt: ButtonProps & {
    'data-hpd-thread-composer-interrupt': '';
  };
  submit: ButtonProps & {
    'data-hpd-thread-composer-submit': '';
  };
}

export interface ThreadComposerState {
  attachmentCount: number;
  attachments: PendingFileAttachment[];
  blockedReason: ThreadComposerBlockedReason;
  busy: boolean;
  canInterrupt: boolean;
  canSubmit: boolean;
  disabled: boolean;
  empty: boolean;
  focused: boolean;
  readyAttachmentCount: number;
  readyContents: AIContent[];
  submitting: boolean;
  textSubmissionState: TextSubmissionState;
  value: string;
}

export interface ThreadComposerActions {
  clear(): void;
  focus(options?: FocusOptions): void;
  interrupt(): Promise<void>;
  setValue(value: string): void;
  submit(): Promise<void>;
}

export interface ThreadComposerApi {
  actions: ThreadComposerActions;
  props: ThreadComposerElementProps;
  state: ThreadComposerState;
  textareaRef: HTMLTextAreaElement | null;
}

export type ThreadComposerChildrenProps = ThreadComposerApi;
export type ThreadComposerChildProps = ThreadComposerApi;

export interface ThreadComposerProps extends FormProps {
  additionalProperties?: Record<string, unknown>;
  attachments?: FileAttachmentState;
  autosize?: ThreadComposerAutosizeStrategy;
  child?: Snippet<[ThreadComposerChildProps]>;
  children?: Snippet<[ThreadComposerChildrenProps]>;
  clear?: ThreadComposerClearMode;
  disabled?: boolean;
  maxRows?: number;
  minRows?: number;
  placeholder?: string;
  pretext?: ThreadComposerPretextOptions;
  quote?: ThreadQuote | null;
  runConfig?: ThreadComposerRunConfig;
  submitMode?: ThreadComposerSubmitMode;
  textareaRef?: HTMLTextAreaElement | null;
  thread: ThreadState;
  value?: string;
}
