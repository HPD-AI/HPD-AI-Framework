import {
  AgentMessagePolicyProperties,
  EventTypes,
  type AgentMessagePersistence,
  type AgentMessageSource,
  type AgentMessageVisibility,
  type ToolResultPayload,
  type ThreadMessageReplacedEvent,
} from './types/events.js';
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
  source?: AgentMessageSource;
  visibility?: AgentMessageVisibility;
  persistence?: AgentMessagePersistence;
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
      applyMessagePolicy(existing, event);
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
    applyMessagePolicy(message, event);
    byId.set(messageId, message);
    return message;
  };

  for (const event of events) {
    if (event.type === EventTypes.TEXT_MESSAGE_START) {
      ensureMessage(event);
    } else if (event.type === EventTypes.TEXT_DELTA) {
      const message = ensureMessage(event);
      const text = getStringProperty(event, 'text');
      if (message && text) {
        message.contents.push({ $type: 'text', text });
      }
    } else if (event.type === EventTypes.USER_MESSAGE) {
      const message = ensureMessage(event, 'user');
      const text = getStringProperty(event, 'text');
      if (message && text) {
        message.role = 'user';
        message.contents.push({ $type: 'text', text });
      }
    } else if (event.type === EventTypes.THREAD_MESSAGE_REPLACED) {
      const replaced = event as ThreadEvent & ThreadMessageReplacedEvent;
      const replacement = replaced.replacement;
      if (!replacement || replacement.messageId !== replaced.messageId || !byId.has(replaced.messageId)) continue;
      byId.set(replaced.messageId, {
        id: replaced.messageId,
        role: replacement.role,
        contents: replacement.contents,
        additionalProperties: replacement.additionalProperties ?? undefined,
        timestamp: replacement.createdAt ?? getStringProperty(event, 'timestamp') ?? new Date().toISOString(),
        authorName: replacement.authorName ?? undefined,
      });
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
    .filter((message) => message.role !== 'tool' && message.visibility !== 'Hidden')
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
    source: message.source,
    visibility: message.visibility,
    persistence: message.persistence,
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

function applyMessagePolicy(message: ThreadMessage, event: ThreadEvent): void {
  const rawSource = getStringProperty(event, 'source') ?? getStringPropertyFromRecord(
    message.additionalProperties,
    AgentMessagePolicyProperties.SOURCE,
  );
  const rawVisibility = getStringProperty(event, 'visibility') ?? getStringPropertyFromRecord(
    message.additionalProperties,
    AgentMessagePolicyProperties.VISIBILITY,
  );
  const rawPersistence = getStringProperty(event, 'persistence') ?? getStringPropertyFromRecord(
    message.additionalProperties,
    AgentMessagePolicyProperties.PERSISTENCE,
  );
  const source = isAgentMessageSource(rawSource) ? rawSource : undefined;
  const visibility = isAgentMessageVisibility(rawVisibility) ? rawVisibility : undefined;
  const persistence = isAgentMessagePersistence(rawPersistence) ? rawPersistence : undefined;

  if (source) message.source = source;
  if (visibility) message.visibility = visibility;
  if (persistence) message.persistence = persistence;

  if (message.source || message.visibility || message.persistence) {
    message.additionalProperties ??= {};
    if (message.source) message.additionalProperties[AgentMessagePolicyProperties.SOURCE] = message.source;
    if (message.visibility) message.additionalProperties[AgentMessagePolicyProperties.VISIBILITY] = message.visibility;
    if (message.persistence) message.additionalProperties[AgentMessagePolicyProperties.PERSISTENCE] = message.persistence;
  }
}

function getStringPropertyFromRecord(value: Record<string, unknown> | undefined, key: string): string | undefined {
  const property = value?.[key];
  return typeof property === 'string' ? property : undefined;
}

function isAgentMessageSource(value: string | undefined): value is AgentMessageSource {
  return value === 'Unspecified' ||
    value === 'UserInput' ||
    value === 'AssistantOutput' ||
    value === 'SystemInstruction' ||
    value === 'RuntimeContext' ||
    value === 'BackgroundNotification' ||
    value === 'ToolResult' ||
    value === 'PermissionResponse' ||
    value === 'Steering' ||
    value === 'Internal';
}

function isAgentMessageVisibility(value: string | undefined): value is AgentMessageVisibility {
  return value === 'Transcript' ||
    value === 'Hidden' ||
    value === 'Diagnostic';
}

function isAgentMessagePersistence(value: string | undefined): value is AgentMessagePersistence {
  return value === 'ThreadHistory' ||
    value === 'ModelContextOnly' ||
    value === 'None';
}
