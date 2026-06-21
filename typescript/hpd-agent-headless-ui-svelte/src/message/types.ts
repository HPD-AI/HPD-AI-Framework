import type { Snippet } from 'svelte';
import type { SvelteHTMLElements } from 'svelte/elements';
import type {
  Message,
  MessageStatus,
  ToolCall,
} from '@hpd-research/hpd-agent-headless-ui';
import type {
  MessageActionBarActions,
  MessageActionBarChildProps,
  MessageActionBarElementProps,
  MessageCopyText,
  MessageCopyDetails,
  MessageActionDetails,
} from '../message-action-bar/index.js';

type DivProps = Omit<SvelteHTMLElements['div'], 'children'>;
type MessagePartsDivProps = Omit<DivProps, 'part'>;
type SpanProps = Omit<SvelteHTMLElements['span'], 'children'>;
type MessageContent = Message['contents'][number];

export type MessageElementProps = DivProps & {
  'data-hpd-message': '';
  'data-message-id': string;
  'data-role': string;
  'data-status': MessageStatus;
  'data-streaming'?: '';
  'data-thinking'?: '';
  'data-has-tools'?: '';
  'data-has-reasoning'?: '';
  'aria-live': 'polite' | 'off';
  'aria-busy': boolean;
  'aria-label': string;
};

export interface MessageSnippetProps {
  message: Message;
  parts: MessageRenderPart[];
  status: MessageStatus;
}

export type MessageActionBarSnippetProps = MessageActionBarChildProps;

export interface MessageChildProps extends MessageSnippetProps {
  actionProps: MessageActionBarElementProps;
  actions: MessageActionBarActions;
  props: MessageElementProps;
}

export type MessageRenderPart =
  | MessageThinkingPart
  | MessageReasoningPart
  | MessageTextPart
  | MessageContentPart
  | MessageToolPart
  | MessageCursorPart;

export interface MessageThinkingPart {
  type: 'thinking';
  id: string;
  message: Message;
}

export interface MessageReasoningPart {
  type: 'reasoning';
  id: string;
  message: Message;
  status: 'streaming' | 'complete';
  text: string;
}

export interface MessageTextPart {
  type: 'text';
  id: string;
  content: Extract<MessageContent, { $type: 'text' }> | null;
  message: Message;
  streaming: boolean;
  text: string;
}

export interface MessageContentPart {
  type: 'content';
  id: string;
  content: MessageContent;
  message: Message;
}

export interface MessageToolPart {
  type: 'tool';
  id: string;
  message: Message;
  tool: ToolCall;
}

export interface MessageCursorPart {
  type: 'cursor';
  id: string;
  message: Message;
}

export type MessagePartElementProps = SpanProps & {
  'data-hpd-message-part': '';
  'data-part-type': MessageRenderPart['type'];
  'data-content-type'?: string;
  'data-tool-id'?: string;
  'data-tool-status'?: string;
};

export interface MessagePartsState {
  empty: boolean;
  message: Message;
  parts: MessageRenderPart[];
}

export interface MessagePartsChildProps {
  message: Message;
  part: MessageRenderPart;
  props: MessagePartElementProps;
}

export interface MessagePartsChildrenProps {
  message: Message;
  parts: MessageRenderPart[];
  state: MessagePartsState;
}

export interface MessagePartsProps extends MessagePartsDivProps {
  message: Message;
  part?: Snippet<[MessagePartsChildProps]>;
  children?: Snippet<[MessagePartsChildrenProps]>;
}

export interface MessageProps extends DivProps {
  message: Message;
  showActions?: boolean;
  copyText?: MessageCopyText;
  onCopy?: (details: MessageCopyDetails) => void | Promise<void>;
  onEditRequest?: (details: MessageActionDetails) => void;
  onRetryRequest?: (details: MessageActionDetails) => void;
  child?: Snippet<[MessageChildProps]>;
  children?: Snippet<[MessageSnippetProps]>;
  actionBar?: Snippet<[MessageActionBarSnippetProps]>;
}
