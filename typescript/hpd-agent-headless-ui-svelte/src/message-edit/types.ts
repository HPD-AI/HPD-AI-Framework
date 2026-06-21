import type { Snippet } from 'svelte';
import type { Attachment } from 'svelte/attachments';
import type { SvelteHTMLElements } from 'svelte/elements';
import type {
  Message,
  ThreadRevisionForkDetails,
  ThreadRevisionOptions,
  ThreadRevisionResult,
} from '@hpd-research/hpd-agent-headless-ui';
import type { ThreadRevisionState } from '../thread-revisions.js';
import type {
  ThreadComposerAutosizeStrategy,
  ThreadComposerPretextOptions,
  ThreadComposerSubmitMode,
} from '../thread-composer/index.js';

type DivProps = Omit<SvelteHTMLElements['div'], 'children'>;
type TextareaProps = Omit<SvelteHTMLElements['textarea'], 'children'>;
type ButtonProps = Omit<SvelteHTMLElements['button'], 'children'>;

export type MessageEditForkDetails = ThreadRevisionForkDetails;

export type MessageEditForkOptions = ThreadRevisionOptions['fork'];

export interface MessageEditSaveDetails {
  message: Message;
  revision: ThreadRevisionResult;
  text: string;
}

export interface MessageEditCancelDetails {
  message: Message;
}

export interface MessageEditErrorDetails {
  message: Message;
  error: Error;
}

export interface MessageEditElementProps {
  root: DivProps & {
    'data-hpd-message-edit': '';
    'data-editing'?: '';
    'data-pending'?: '';
    'data-empty'?: '';
    'data-can-save'?: '';
    'data-error'?: '';
  };
  textarea: TextareaProps & {
    'data-hpd-message-edit-textarea': '';
    'aria-invalid': boolean;
    'aria-multiline': 'true';
  };
}

export interface MessageEditActionProps {
  save: ButtonProps & {
    'data-hpd-message-edit-save': '';
  };
  cancel: ButtonProps & {
    'data-hpd-message-edit-cancel': '';
  };
}

export interface MessageEditActions {
  cancel(): void;
  save(): Promise<ThreadRevisionResult | undefined>;
  setDraft(value: string): void;
  startEdit(): void;
}

export interface MessageEditApi {
  actions: MessageEditActions;
  actionProps: MessageEditActionProps;
  canSave: boolean;
  draft: string;
  editing: boolean;
  error: Error | null;
  pending: boolean;
  props: MessageEditElementProps;
  textareaAttachment: Attachment<HTMLTextAreaElement>;
  textareaRef: HTMLTextAreaElement | null;
}

export type MessageEditViewProps = MessageEditApi & {
  message: Message;
};

export type MessageEditEditProps = MessageEditApi & {
  message: Message;
};

export interface MessageEditProps extends DivProps {
  autosize?: ThreadComposerAutosizeStrategy;
  cancelLabel?: string;
  draft?: string;
  editing?: boolean;
  edit?: Snippet<[MessageEditEditProps]>;
  forkOptions?: MessageEditForkOptions;
  maxRows?: number;
  message: Message;
  minRows?: number;
  onCancel?: (details: MessageEditCancelDetails) => void;
  onError?: (details: MessageEditErrorDetails) => void;
  onSaved?: (details: MessageEditSaveDetails) => void | Promise<void>;
  onStartEdit?: (details: { message: Message }) => void;
  placeholder?: string;
  pretext?: ThreadComposerPretextOptions;
  revisions: Pick<ThreadRevisionState, 'forkAndEditMessage'>;
  runConfig?: ThreadRevisionOptions['runConfig'];
  saveLabel?: string;
  submitMode?: ThreadComposerSubmitMode;
  view?: Snippet<[MessageEditViewProps]>;
}
