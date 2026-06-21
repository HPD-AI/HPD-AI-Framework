import { EventTypes, type ToolResultPayload } from './types/events.js';
import type {
  AIContent,
  AiFunctionCallContent,
  AiFunctionResultContent,
  AiTextContent,
  AiTextReasoningContent,
  ThreadEvent,
  ThreadMessage,
} from './types/session.js';

export interface ThreadToolCallReadModel {
  callId: string;
  name: string;
  messageId: string;
  args?: unknown;
  resultText?: string;
  informationalOnly?: boolean;
}

export interface ThreadMessageReadModel {
  id: string;
  role: ThreadMessage['role'];
  text: string;
  contents: AIContent[];
  additionalProperties?: Record<string, unknown>;
  reasoningText?: string;
  timestamp: string;
  toolCalls: ThreadToolCallReadModel[];
  authorName?: string;
}

export function projectThreadEventsToMessages(events: readonly ThreadEvent[]): ThreadMessage[] {
  const byId = new Map<string, ThreadMessage>();
  const ensureMessage = (event: ThreadEvent, fallbackRole: ThreadMessage['role'] = 'assistant'): ThreadMessage | null => {
    const messageId = getStringProperty(event, 'messageId');
    if (!messageId) return null;

    const existing = byId.get(messageId);
    if (existing) {
      const role = getStringProperty(event, 'role');
      if (role) existing.role = role;
      const additionalProperties = getRecordProperty(event, 'additionalProperties');
      if (additionalProperties) existing.additionalProperties = additionalProperties;
      return existing;
    }

    const additionalProperties = getRecordProperty(event, 'additionalProperties');
    const message: ThreadMessage = {
      id: messageId,
      role: getStringProperty(event, 'role') ?? fallbackRole,
      contents: [],
      additionalProperties,
      timestamp: getStringProperty(event, 'createdAt') ??
        getStringProperty(event, 'timestamp') ??
        new Date().toISOString(),
      authorName: getStringProperty(event, 'authorName'),
    };
    byId.set(messageId, message);
    return message;
  };

  for (const event of events) {
    if (event.type === EventTypes.MESSAGE_STARTED) {
      ensureMessage(event);
    } else if (event.type === EventTypes.TEXT_MESSAGE_START) {
      ensureMessage(event);
    } else if (event.type === EventTypes.TEXT_DELTA) {
      const message = ensureMessage(event);
      const text = getStringProperty(event, 'text');
      if (message && text) {
        message.contents.push({ $type: 'text', text });
      }
    } else if (event.type === EventTypes.REASONING_MESSAGE_START) {
      ensureMessage(event);
    } else if (event.type === EventTypes.REASONING_DELTA) {
      const message = ensureMessage(event);
      const text = getStringProperty(event, 'text');
      if (message && text) {
        message.contents.push({ $type: 'reasoning', text });
      }
    } else if (event.type === EventTypes.CONTENT_ADDED) {
      if (!isAIContent(event.content)) continue;

      const message = ensureMessage(event);
      if (message) message.contents.push(event.content);
    }
  }

  return [...byId.values()];
}

export function mapThreadMessages(messages: readonly ThreadMessage[]): ThreadMessageReadModel[] {
  return messages
    .filter((message) => message.role !== 'tool')
    .map(mapThreadMessage);
}

export function mapThreadMessage(message: ThreadMessage): ThreadMessageReadModel {
  let text = '';
  let reasoningText: string | undefined;
  const toolCalls: ThreadToolCallReadModel[] = [];

  for (const item of message.contents) {
    switch (item.$type) {
      case 'text':
        text += (item as AiTextContent).text;
        break;
      case 'reasoning':
        reasoningText = (reasoningText ?? '') + (item as AiTextReasoningContent).text;
        break;
      case 'functionCall': {
        const call = item as AiFunctionCallContent;
        toolCalls.push({
          callId: call.callId,
          name: call.name,
          messageId: message.id,
          args: call.arguments,
          informationalOnly: call.informationalOnly,
        });
        break;
      }
      case 'functionResult': {
        const result = item as AiFunctionResultContent;
        const match = toolCalls.find((tool) => tool.callId === result.callId);
        if (match) {
          match.resultText = formatUnknownPayload(result.result);
        }
        break;
      }
      default:
        break;
    }
  }

  return {
    id: message.id,
    role: message.role,
    text,
    contents: message.contents.map(cloneAIContent),
    additionalProperties: message.additionalProperties
      ? { ...message.additionalProperties }
      : undefined,
    reasoningText,
    timestamp: message.timestamp,
    toolCalls,
    authorName: message.authorName,
  };
}

function cloneAIContent(content: AIContent): AIContent {
  if (typeof structuredClone === 'function') {
    return structuredClone(content);
  }
  return JSON.parse(JSON.stringify(content)) as AIContent;
}

export function formatToolResultPayload(result: ToolResultPayload): string {
  if (result.text) return result.text;
  if (result.json !== undefined) return JSON.stringify(result.json);
  if (result.content && result.content.length > 0) return JSON.stringify(result.content);
  return '';
}

function formatUnknownPayload(value: unknown): string | undefined {
  if (value === undefined) return undefined;
  return typeof value === 'string' ? value : JSON.stringify(value);
}

function isAIContent(value: unknown): value is AIContent {
  return typeof value === 'object' &&
    value !== null &&
    '$type' in value &&
    typeof (value as { $type?: unknown }).$type === 'string';
}

function getStringProperty(value: object, key: string): string | undefined {
  const property = (value as Record<string, unknown>)[key];
  return typeof property === 'string' ? property : undefined;
}

function getRecordProperty(value: object, key: string): Record<string, unknown> | undefined {
  const property = (value as Record<string, unknown>)[key];
  return property && typeof property === 'object' && !Array.isArray(property)
    ? property as Record<string, unknown>
    : undefined;
}
