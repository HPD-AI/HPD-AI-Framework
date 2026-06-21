import {
  getMessageStatus,
  type Message,
  type MessageStatus,
} from '@hpd-research/hpd-agent-headless-ui';
import type {
  MessageElementProps,
  MessagePartElementProps,
  MessagePartsState,
  MessageRenderPart,
} from './types.js';

type MessageContent = Message['contents'][number];
type ReasoningContent = Extract<MessageContent, { $type: 'reasoning' }>;

export function createMessageElementProps(
  message: Message,
  restProps: Record<string, unknown> = {},
  status: MessageStatus = getMessageStatus(message),
): MessageElementProps {
  return {
    ...restProps,
    'data-hpd-message': '',
    'data-message-id': message.id,
    'data-role': message.role,
    'data-status': status,
    'data-streaming': message.streaming ? '' : undefined,
    'data-thinking': message.thinking ? '' : undefined,
    'data-has-tools': message.toolCalls.length > 0 ? '' : undefined,
    'data-has-reasoning': message.reasoning ? '' : undefined,
    'aria-live': message.streaming ? 'polite' : 'off',
    'aria-busy': message.streaming || message.thinking,
    'aria-label': `${message.role} message`,
  } as MessageElementProps;
}

export function createMessageParts(message: Message): MessageRenderPart[] {
  const parts: MessageRenderPart[] = [];
  const contents = message.contents ?? [];
  const reasoningContents = contents.filter(isReasoningContent);
  const otherContents = contents.filter((content) =>
    content.$type !== 'text' && content.$type !== 'reasoning');

  if (message.thinking) {
    parts.push({
      id: `${message.id}:thinking`,
      message,
      type: 'thinking',
    });
  }

  if (reasoningContents.length > 0) {
    reasoningContents.forEach((content, index) => {
      parts.push({
        id: `${message.id}:reasoning:${index}`,
        message,
        status: message.streaming ? 'streaming' : 'complete',
        text: content.text,
        type: 'reasoning',
      });
    });
  } else if (message.reasoning) {
    parts.push({
      id: `${message.id}:reasoning`,
      message,
      status: message.streaming ? 'streaming' : 'complete',
      text: message.reasoning,
      type: 'reasoning',
    });
  }

  if (message.content.length > 0) {
    parts.push({
      content: null,
      id: `${message.id}:text`,
      message,
      streaming: message.streaming,
      text: message.content,
      type: 'text',
    });
  }

  otherContents.forEach((content, index) => {
    parts.push({
      content,
      id: `${message.id}:content:${content.$type}:${index}`,
      message,
      type: 'content',
    });
  });

  message.toolCalls.forEach((tool) => {
    parts.push({
      id: `${message.id}:tool:${tool.callId}`,
      message,
      tool,
      type: 'tool',
    });
  });

  if (message.streaming) {
    parts.push({
      id: `${message.id}:cursor`,
      message,
      type: 'cursor',
    });
  }

  return parts;
}

function isReasoningContent(content: MessageContent): content is ReasoningContent {
  return content.$type === 'reasoning' && typeof content.text === 'string' && content.text.length > 0;
}

export function createMessagePartsState(message: Message, parts: MessageRenderPart[]): MessagePartsState {
  return {
    empty: parts.length === 0,
    message,
    parts,
  };
}

export function createMessagePartElementProps(
  part: MessageRenderPart,
  restProps: Record<string, unknown> = {},
): MessagePartElementProps {
  return {
    ...restProps,
    'data-hpd-message-part': '',
    'data-part-type': part.type,
    'data-content-type': part.type === 'content' ? part.content.$type : undefined,
    'data-tool-id': part.type === 'tool' ? part.tool.callId : undefined,
    'data-tool-status': part.type === 'tool' ? part.tool.status : undefined,
  } as MessagePartElementProps;
}
