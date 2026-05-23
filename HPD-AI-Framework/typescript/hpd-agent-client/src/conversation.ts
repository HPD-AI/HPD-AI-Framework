import type { AgentEvent, ToolCallType, ToolResultPayload } from './types/events.js';
import { EventTypes } from './types/events.js';
import type { AIContent, BranchMessage } from './types/session.js';

export type ConversationHistoryItem =
  | ConversationTextHistoryItem
  | ConversationToolCallHistoryItem
  | ConversationToolResultHistoryItem
  | ConversationErrorHistoryItem
  | ConversationReasoningHistoryItem;

export interface ConversationTextHistoryItem {
  kind: 'text';
  role: string;
  text: string;
  message: BranchMessage;
}

export interface ConversationReasoningHistoryItem {
  kind: 'reasoning';
  text: string;
  message: BranchMessage;
}

export interface ConversationToolCallHistoryItem {
  kind: 'toolCall';
  callId: string;
  name: string;
  arguments: Record<string, unknown>;
  timestamp: string;
  message: BranchMessage;
}

export interface ConversationToolResultHistoryItem {
  kind: 'toolResult';
  callId: string;
  result: unknown;
  timestamp: string;
  message: BranchMessage;
}

export interface ConversationErrorHistoryItem {
  kind: 'error';
  messageText: string;
  message: BranchMessage;
}

export type ConversationSource = 'event' | 'history' | 'local';
export type ConversationItemStatus = 'pending' | 'streaming' | 'complete' | 'error';

export type ConversationItem =
  | ConversationMessageItem
  | ConversationReasoningItem
  | ConversationToolItem
  | ConversationErrorItem;

export interface ConversationMessageItem {
  kind: 'message';
  id: string;
  role: string;
  text: string;
  status: ConversationItemStatus;
  source: ConversationSource;
  timestamp?: string;
  message?: BranchMessage;
}

export interface ConversationReasoningItem {
  kind: 'reasoning';
  id: string;
  text: string;
  status: ConversationItemStatus;
  source: ConversationSource;
  timestamp?: string;
  message?: BranchMessage;
}

export interface ConversationToolItem {
  kind: 'tool';
  id: string;
  callId: string;
  name: string;
  argsJson?: string;
  args?: Record<string, unknown>;
  result?: ToolResultPayload | unknown;
  status: ConversationItemStatus;
  source: ConversationSource;
  timestamp?: string;
  messageId?: string;
  harnessName?: string;
  callType?: ToolCallType;
  message?: BranchMessage;
}

export interface ConversationErrorItem {
  kind: 'error';
  id: string;
  message: string;
  status: 'error';
  source: ConversationSource;
  timestamp?: string;
  raw?: unknown;
}

export type ConversationChange =
  | { type: 'added'; item: ConversationItem }
  | { type: 'updated'; item: ConversationItem }
  | { type: 'reset' };

export interface ConversationSubscription {
  dispose(): void;
}

/**
 * UI-neutral transcript reducer for chat state.
 *
 * ConversationState intentionally handles only events that become durable transcript
 * items: text, reasoning, tool calls/results, message-turn errors, and stored
 * branch messages. It is not a complete protocol event bus; apps should continue
 * using AgentClient.on/onAny for permissions, clarifications, continuations,
 * middleware/state events, audio, lifecycle, observability, or custom events.
 */
export class ConversationState {
  private readonly itemsValue: ConversationItem[] = [];
  private readonly messagesById = new Map<string, ConversationMessageItem>();
  private readonly reasoningById = new Map<string, ConversationReasoningItem>();
  private readonly toolsByCallId = new Map<string, ConversationToolItem>();
  private readonly listeners = new Set<(changes: ConversationChange[]) => void>();
  private localId = 0;

  get items(): readonly ConversationItem[] {
    return this.itemsValue;
  }

  get hasAssistantMessage(): boolean {
    return this.itemsValue.some((item) => item.kind === 'message' && item.role === 'assistant' && item.text.trim());
  }

  onChange(listener: (changes: ConversationChange[]) => void): ConversationSubscription {
    this.listeners.add(listener);
    return { dispose: () => this.listeners.delete(listener) };
  }

  reset(): ConversationChange[] {
    this.itemsValue.length = 0;
    this.messagesById.clear();
    this.reasoningById.clear();
    this.toolsByCallId.clear();
    return this.emit([{ type: 'reset' }]);
  }

  addUserText(text: string, options: { id?: string; timestamp?: string } = {}): ConversationChange[] {
    const item: ConversationMessageItem = {
      kind: 'message',
      id: options.id ?? `local-user-${++this.localId}`,
      role: 'user',
      text,
      status: 'complete',
      source: 'local',
      timestamp: options.timestamp,
    };
    this.itemsValue.push(item);
    this.messagesById.set(item.id, item);
    return this.emit([{ type: 'added', item }]);
  }

  applyEvent(event: AgentEvent): ConversationChange[] {
    switch (event.type) {
      case EventTypes.TEXT_MESSAGE_START:
        return this.ensureMessage(event.messageId, event.role || 'assistant', 'event', 'streaming');

      case EventTypes.TEXT_DELTA:
        {
          const [item, added] = this.message(event.messageId, 'assistant', 'event', 'streaming');
          item.text += event.text;
          return this.emit([{ type: added ? 'added' : 'updated', item }]);
        }

      case EventTypes.TEXT_MESSAGE_END:
        {
          const item = this.messagesById.get(event.messageId);
          if (!item) return [];
          item.status = 'complete';
          return this.emit([{ type: 'updated', item }]);
        }

      case EventTypes.REASONING_MESSAGE_START:
        return this.ensureReasoning(event.messageId, 'event', 'streaming');

      case EventTypes.REASONING_DELTA:
        {
          const [item, added] = this.reasoning(event.messageId, 'event', 'streaming');
          item.text += event.text;
          return this.emit([{ type: added ? 'added' : 'updated', item }]);
        }

      case EventTypes.REASONING_MESSAGE_END:
        {
          const item = this.reasoningById.get(event.messageId);
          if (!item) return [];
          item.status = 'complete';
          return this.emit([{ type: 'updated', item }]);
        }

      case EventTypes.TOOL_CALL_START:
        {
          const [item, added] = this.tool(event.callId, 'event');
          item.name = event.name;
          item.messageId = event.messageId;
          item.harnessName = event.harnessName;
          item.callType = event.callType;
          item.status = 'streaming';
          return this.emit([{ type: added ? 'added' : 'updated', item }]);
        }

      case EventTypes.TOOL_CALL_ARGS:
        {
          const [item, added] = this.tool(event.callId, 'event');
          item.argsJson = event.argsJson;
          item.args = parseJsonObject(event.argsJson);
          return this.emit([{ type: added ? 'added' : 'updated', item }]);
        }

      case EventTypes.TOOL_CALL_RESULT:
        {
          const [item, added] = this.tool(event.callId, 'event');
          item.result = event.result;
          item.harnessName = event.harnessName ?? item.harnessName;
          item.callType = event.callType ?? item.callType;
          item.status = 'complete';
          return this.emit([{ type: added ? 'added' : 'updated', item }]);
        }

      case EventTypes.TOOL_CALL_END:
        {
          const item = this.toolsByCallId.get(event.callId);
          if (!item) return [];
          item.status = item.status === 'pending' ? 'complete' : item.status;
          return this.emit([{ type: 'updated', item }]);
        }

      case EventTypes.MESSAGE_TURN_ERROR:
        return this.addError(event.message || 'Message turn failed.', event);

      default:
        return [];
    }
  }

  applyBranchMessages(messages: BranchMessage[]): ConversationChange[] {
    const changes: ConversationChange[] = [];
    for (const message of messages) changes.push(...this.applyBranchMessage(message, false));
    return this.emit(changes);
  }

  applyBranchMessage(message: BranchMessage, shouldEmit = true): ConversationChange[] {
    const changes: ConversationChange[] = [];
    for (const history of readBranchMessage(message)) {
      if (history.kind === 'text') {
        const id = history.message.id;
        if (this.messagesById.has(id)) continue;
        const item: ConversationMessageItem = {
          kind: 'message',
          id,
          role: history.role,
          text: history.text,
          status: 'complete',
          source: 'history',
          timestamp: message.timestamp,
          message,
        };
        this.itemsValue.push(item);
        this.messagesById.set(id, item);
        changes.push({ type: 'added', item });
      } else if (history.kind === 'reasoning') {
        const id = `${history.message.id}:reasoning`;
        if (this.reasoningById.has(id)) continue;
        const item: ConversationReasoningItem = {
          kind: 'reasoning',
          id,
          text: history.text,
          status: 'complete',
          source: 'history',
          timestamp: message.timestamp,
          message,
        };
        this.itemsValue.push(item);
        this.reasoningById.set(id, item);
        changes.push({ type: 'added', item });
      } else if (history.kind === 'toolCall') {
        const [item, added] = this.tool(history.callId, 'history');
        item.name = history.name;
        item.args = history.arguments;
        item.argsJson = JSON.stringify(history.arguments, null, 2);
        item.status = item.result ? 'complete' : 'pending';
        item.timestamp = history.timestamp;
        item.message = message;
        changes.push({ type: added ? 'added' : 'updated', item });
      } else if (history.kind === 'toolResult') {
        const [item, added] = this.tool(history.callId, 'history');
        item.result = history.result;
        item.status = 'complete';
        item.timestamp = history.timestamp;
        item.message = message;
        changes.push({ type: added ? 'added' : 'updated', item });
      } else if (history.kind === 'error') {
        const item: ConversationErrorItem = {
          kind: 'error',
          id: `${message.id}:error`,
          message: history.messageText,
          status: 'error',
          source: 'history',
          timestamp: message.timestamp,
          raw: message,
        };
        this.itemsValue.push(item);
        changes.push({ type: 'added', item });
      }
    }

    return shouldEmit ? this.emit(changes) : changes;
  }

  private ensureMessage(id: string, role: string, source: ConversationSource, status: ConversationItemStatus): ConversationChange[] {
    const [item, added] = this.message(id, role, source, status);
    return this.emit([{ type: added ? 'added' : 'updated', item }]);
  }

  private ensureReasoning(id: string, source: ConversationSource, status: ConversationItemStatus): ConversationChange[] {
    const [item, added] = this.reasoning(id, source, status);
    return this.emit([{ type: added ? 'added' : 'updated', item }]);
  }

  private message(id: string, role: string, source: ConversationSource, status: ConversationItemStatus): [ConversationMessageItem, boolean] {
    const existing = this.messagesById.get(id);
    if (existing) {
      existing.status = status;
      return [existing, false];
    }

    const item: ConversationMessageItem = { kind: 'message', id, role, text: '', status, source };
    this.itemsValue.push(item);
    this.messagesById.set(id, item);
    return [item, true];
  }

  private reasoning(id: string, source: ConversationSource, status: ConversationItemStatus): [ConversationReasoningItem, boolean] {
    const existing = this.reasoningById.get(id);
    if (existing) {
      existing.status = status;
      return [existing, false];
    }

    const item: ConversationReasoningItem = { kind: 'reasoning', id, text: '', status, source };
    this.itemsValue.push(item);
    this.reasoningById.set(id, item);
    return [item, true];
  }

  private tool(callId: string, source: ConversationSource): [ConversationToolItem, boolean] {
    const existing = this.toolsByCallId.get(callId);
    if (existing) return [existing, false];

    const item: ConversationToolItem = {
      kind: 'tool',
      id: `tool:${callId}`,
      callId,
      name: 'tool',
      status: 'pending',
      source,
    };
    this.itemsValue.push(item);
    this.toolsByCallId.set(callId, item);
    return [item, true];
  }

  private addError(message: string, raw?: unknown): ConversationChange[] {
    const item: ConversationErrorItem = {
      kind: 'error',
      id: `error:${++this.localId}`,
      message,
      status: 'error',
      source: 'event',
      raw,
    };
    this.itemsValue.push(item);
    return this.emit([{ type: 'added', item }]);
  }

  private emit(changes: ConversationChange[]): ConversationChange[] {
    if (!changes.length) return changes;
    for (const listener of this.listeners) listener(changes);
    return changes;
  }
}

export function readBranchMessage(message: BranchMessage): ConversationHistoryItem[] {
  const items: ConversationHistoryItem[] = [];
  const role = String(message.role || '').toLowerCase();
  const text = (message.contents || [])
    .filter(isTextContent)
    .map((content) => content.text || '')
    .filter(Boolean)
    .join('\n');
  const reasoning = (message.contents || [])
    .filter(isReasoningContent)
    .map((content) => content.text || '')
    .filter(Boolean)
    .join('\n');

  if (text && (role === 'user' || role === 'assistant' || role === 'system' || role === 'tool')) {
    items.push({ kind: 'text', role, text, message });
  }

  if (reasoning) {
    items.push({ kind: 'reasoning', text: reasoning, message });
  }

  for (const content of message.contents || []) {
    if (isFunctionCallContent(content)) {
      const callId = stringProperty(content, 'callId') ?? stringProperty(content, 'call_id') ?? stringProperty(content, 'id');
      const name = stringProperty(content, 'name') ?? stringProperty(content, 'functionName') ?? stringProperty(content, 'function_name');
      if (callId && name) {
        items.push({
          kind: 'toolCall',
          callId,
          name,
          arguments: objectProperty(content, 'arguments') ?? objectProperty(content, 'args') ?? {},
          timestamp: message.timestamp,
          message,
        });
      }
    } else if (isFunctionResultContent(content)) {
      const callId = stringProperty(content, 'callId') ?? stringProperty(content, 'call_id') ?? stringProperty(content, 'id');
      if (callId) {
        items.push({
          kind: 'toolResult',
          callId,
          result: valueProperty(content, 'result') ?? valueProperty(content, 'value'),
          timestamp: message.timestamp,
          message,
        });
      }
    } else if (isErrorContent(content)) {
      items.push({ kind: 'error', messageText: content.message, message });
    }
  }

  return items;
}

export function isTextContent(content: AIContent): content is Extract<AIContent, { $type: 'text' }> {
  return contentKind(content) === 'text';
}

export function isReasoningContent(content: AIContent): content is Extract<AIContent, { $type: 'reasoning' }> {
  return contentKind(content) === 'reasoning';
}

export function isFunctionCallContent(content: AIContent): content is Extract<AIContent, { $type: 'functionCall' }> {
  return contentKind(content) === 'functioncall';
}

export function isFunctionResultContent(content: AIContent): content is Extract<AIContent, { $type: 'functionResult' }> {
  return contentKind(content) === 'functionresult';
}

export function isErrorContent(content: AIContent): content is Extract<AIContent, { $type: 'error' }> {
  return contentKind(content) === 'error';
}

function contentKind(content: AIContent): string {
  const raw = stringProperty(content, '$type') ?? stringProperty(content, 'type') ?? '';
  return raw.toLowerCase().replace(/[_-]/g, '');
}

function valueProperty(content: AIContent, key: string): unknown {
  return (content as Record<string, unknown>)[key];
}

function stringProperty(content: AIContent, key: string): string | undefined {
  const value = valueProperty(content, key);
  return typeof value === 'string' && value ? value : undefined;
}

function objectProperty(content: AIContent, key: string): Record<string, unknown> | undefined {
  const value = valueProperty(content, key);
  if (value && typeof value === 'object' && !Array.isArray(value)) return value as Record<string, unknown>;
  if (typeof value === 'string') return parseJsonObject(value);
  return undefined;
}

function parseJsonObject(value: string): Record<string, unknown> | undefined {
  try {
    const parsed = JSON.parse(value) as unknown;
    return parsed && typeof parsed === 'object' && !Array.isArray(parsed)
      ? parsed as Record<string, unknown>
      : undefined;
  } catch {
    return undefined;
  }
}
