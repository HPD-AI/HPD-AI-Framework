import type { HpdAgentEvent } from './generated/events.js';
import type { HpdSseAppliedCursor } from './runtime.js';
import { isRecord } from './event-schema.js';

export type ThreadHistoryMessageSource =
  | 'Unspecified'
  | 'UserInput'
  | 'AssistantOutput'
  | 'SystemInstruction'
  | 'RuntimeContext'
  | 'BackgroundNotification'
  | 'ToolResult'
  | 'PermissionResponse'
  | 'Steering'
  | 'Internal';

export type ThreadHistoryMessageVisibility = 'Transcript' | 'Hidden' | 'Diagnostic';
export type ThreadHistoryMessagePersistence = 'ThreadHistory' | 'ModelContextOnly' | 'None';
export type ThreadHistoryToolCallStatus = 'pending' | 'complete' | 'error';

export interface ThreadHistoryToolCall {
  callId: string;
  name: string;
  messageId: string;
  status: ThreadHistoryToolCallStatus;
  arguments?: unknown;
  result?: unknown;
  error?: string;
  toolHarnessName?: string;
  callType?: string;
}

export interface ThreadHistoryMessage {
  id: string;
  role: string;
  text: string;
  reasoning: string;
  contents: ReadonlyArray<Record<string, unknown>>;
  toolCalls: ThreadHistoryToolCall[];
  source?: ThreadHistoryMessageSource;
  visibility?: ThreadHistoryMessageVisibility;
  persistence?: ThreadHistoryMessagePersistence;
  authorName?: string;
  clientInputId?: string;
  createdAt?: string;
  streaming: boolean;
}

export interface ThreadHistory {
  messages: ThreadHistoryMessage[];
  appliedCursor: Readonly<HpdSseAppliedCursor>;
}

const sources: ThreadHistoryMessageSource[] = [
  'Unspecified',
  'UserInput',
  'AssistantOutput',
  'SystemInstruction',
  'RuntimeContext',
  'BackgroundNotification',
  'ToolResult',
  'PermissionResponse',
  'Steering',
  'Internal',
];

const visibilities: ThreadHistoryMessageVisibility[] = ['Transcript', 'Hidden', 'Diagnostic'];
const persistences: ThreadHistoryMessagePersistence[] = ['ThreadHistory', 'ModelContextOnly', 'None'];

export class ThreadHistoryProjection {
  private readonly messages = new Map<string, ThreadHistoryMessage>();

  project(event: HpdAgentEvent | object): void {
    const value = event as Record<string, unknown>;
    switch (value.type) {
      case 'USER_MESSAGE':
        this.startMessage(value, {
          source: 'UserInput',
          visibility: 'Transcript',
        }, false);
        this.appendText(requiredString(value, 'messageId'), requiredString(value, 'text'));
        break;
      case 'TEXT_MESSAGE_START':
        this.startMessage(value);
        break;
      case 'TEXT_DELTA':
        this.appendText(requiredString(value, 'messageId'), requiredString(value, 'text'));
        break;
      case 'TEXT_MESSAGE_END':
        this.update(requiredString(value, 'messageId'), message => ({ ...message, streaming: false }));
        break;
      case 'CONTENT_ADDED':
        this.projectContentAdded(value);
        break;
      case 'REASONING_MESSAGE_START':
        this.startMessage(value);
        break;
      case 'REASONING_DELTA':
        this.appendReasoning(requiredString(value, 'messageId'), requiredString(value, 'text'));
        break;
      case 'REASONING_MESSAGE_END':
        this.update(requiredString(value, 'messageId'), message => ({ ...message, streaming: false }));
        break;
      case 'TOOL_CALL_START':
        this.projectToolStart(value);
        break;
      case 'TOOL_CALL_ARGS':
        this.projectToolArguments(value);
        break;
      case 'TOOL_CALL_RESULT':
        this.projectToolResult(value);
        break;
      case 'THREAD_MESSAGE_REPLACED':
        this.projectReplacement(value);
        break;
    }
  }

  snapshot(): ThreadHistoryMessage[] {
    return [...this.messages.values()].map(message => compact({
      ...message,
      contents: message.contents.map(content => structuredClone(content)),
      toolCalls: message.toolCalls.map(toolCall => compact({
        ...toolCall,
        arguments: cloneValue(toolCall.arguments),
        result: cloneValue(toolCall.result),
      })),
    }));
  }

  private startMessage(
    event: object,
    inferred: Pick<ThreadHistoryMessage, 'source' | 'visibility'> = {},
    streaming = true,
  ): ThreadHistoryMessage {
    const id = requiredString(event, 'messageId');
    const existing = this.messages.get(id);
    if (existing) return existing;
    const policy = readPolicy(event);
    const source = policy.source ?? inferred.source;
    const message: ThreadHistoryMessage = {
      id,
      role: readString(event, 'role') ?? (source === 'UserInput' ? 'user' : 'assistant'),
      text: '',
      reasoning: '',
      contents: [],
      toolCalls: [],
      source,
      visibility: policy.visibility ?? inferred.visibility,
      persistence: policy.persistence,
      authorName: readString(event, 'authorName'),
      clientInputId: readString(event, 'clientInputId'),
      createdAt: readString(event, 'createdAt') ?? readString(event, 'timestamp'),
      streaming,
    };
    this.messages.set(id, message);
    return message;
  }

  private appendText(messageId: string, text: string): void {
    this.update(messageId, message => ({
      ...message,
      text: message.text + text,
      contents: [...message.contents, { $type: 'text', text }],
      streaming: true,
    }));
  }

  private appendReasoning(messageId: string, text: string): void {
    this.update(messageId, message => ({
      ...message,
      reasoning: message.reasoning + text,
      contents: [...message.contents, { $type: 'reasoning', text }],
      streaming: true,
    }));
  }

  private projectContentAdded(event: Record<string, unknown>): void {
    const messageId = requiredString(event, 'messageId');
    this.startMessage(event);
    const content = isRecord(event.content) ? event.content : { $type: 'unknown', value: event.content };
    const type = readString(content, '$type');
    if (type === 'text') {
      this.appendText(messageId, readString(content, 'text') ?? '');
      return;
    }
    if (type === 'reasoning') {
      this.appendReasoning(messageId, readString(content, 'text') ?? '');
      return;
    }
    this.update(messageId, message => ({
      ...message,
      contents: [...message.contents, structuredClone(content)],
    }));
  }

  private projectToolStart(event: Record<string, unknown>): void {
    const messageId = requiredString(event, 'messageId');
    this.startMessage(event);
    const callId = requiredString(event, 'callId');
    this.update(messageId, message => ({
      ...message,
      toolCalls: [
        ...message.toolCalls.filter(call => call.callId !== callId),
        {
          callId,
          name: readString(event, 'name') ?? '',
          messageId,
          status: 'pending',
          toolHarnessName: readString(event, 'toolHarnessName'),
          callType: readString(event, 'callType'),
        },
      ],
    }));
  }

  private projectToolArguments(event: Record<string, unknown>): void {
    const callId = requiredString(event, 'callId');
    const argsJson = readString(event, 'argsJson') ?? '';
    let args: unknown = argsJson;
    let error: string | undefined;
    try {
      args = JSON.parse(argsJson) as unknown;
    } catch {
      error = 'Invalid tool arguments';
    }
    this.updateTool(callId, toolCall => ({
      ...toolCall,
      arguments: args,
      error,
      status: error ? 'error' : toolCall.status,
    }));
  }

  private projectToolResult(event: Record<string, unknown>): void {
    const callId = requiredString(event, 'callId');
    this.updateTool(callId, toolCall => ({
      ...toolCall,
      name: readString(event, 'name') ?? toolCall.name,
      result: cloneValue(event.result),
      status: 'complete',
      toolHarnessName: readString(event, 'toolHarnessName') ?? toolCall.toolHarnessName,
      callType: readString(event, 'callType') ?? toolCall.callType,
    }));
  }

  private projectReplacement(event: Record<string, unknown>): void {
    const messageId = requiredString(event, 'messageId');
    const replacement = isRecord(event.replacement) ? event.replacement : undefined;
    if (!replacement || readString(replacement, 'messageId') !== messageId) return;
    const contents = Array.isArray(replacement.contents)
      ? replacement.contents.filter(isRecord).map(content => structuredClone(content))
      : [];
    const message: ThreadHistoryMessage = {
      id: messageId,
      role: readString(replacement, 'role') ?? 'assistant',
      text: contents
        .filter(content => readString(content, '$type') === 'text')
        .map(content => readString(content, 'text') ?? '')
        .join(''),
      reasoning: contents
        .filter(content => readString(content, '$type') === 'reasoning')
        .map(content => readString(content, 'text') ?? '')
        .join(''),
      contents,
      toolCalls: this.messages.get(messageId)?.toolCalls ?? [],
      authorName: readString(replacement, 'authorName'),
      createdAt: readString(replacement, 'createdAt'),
      streaming: false,
    };
    this.messages.set(messageId, message);
  }

  private update(messageId: string, update: (message: ThreadHistoryMessage) => ThreadHistoryMessage): void {
    let message = this.messages.get(messageId);
    if (!message) {
      message = {
        id: messageId,
        role: 'assistant',
        text: '',
        reasoning: '',
        contents: [],
        toolCalls: [],
        streaming: true,
      };
      this.messages.set(messageId, message);
    }
    this.messages.set(messageId, update(message));
  }

  private updateTool(callId: string, update: (toolCall: ThreadHistoryToolCall) => ThreadHistoryToolCall): void {
    for (const [messageId, message] of this.messages) {
      const index = message.toolCalls.findIndex(call => call.callId === callId);
      if (index < 0) continue;
      const toolCalls = [...message.toolCalls];
      toolCalls[index] = update(toolCalls[index]!);
      this.messages.set(messageId, { ...message, toolCalls });
      return;
    }
  }
}

export async function projectThreadHistory(
  events: AsyncIterable<HpdAgentEvent>,
): Promise<ThreadHistoryMessage[]> {
  const projection = new ThreadHistoryProjection();
  for await (const event of events) projection.project(event);
  return projection.snapshot();
}

function readPolicy(event: object): {
  source?: ThreadHistoryMessageSource;
  visibility?: ThreadHistoryMessageVisibility;
  persistence?: ThreadHistoryMessagePersistence;
} {
  const value = event as Record<string, unknown>;
  const additional = isRecord(value.additionalProperties) ? value.additionalProperties : undefined;
  return {
    source: readEnum(value.source, additional?.['hpd.message.source'], sources),
    visibility: readEnum(value.visibility, additional?.['hpd.message.visibility'], visibilities),
    persistence: readEnum(value.persistence, additional?.['hpd.message.persistence'], persistences),
  };
}

function readEnum<T extends string>(value: unknown, additional: unknown, values: readonly T[]): T | undefined {
  if (typeof value === 'string' && values.includes(value as T)) return value as T;
  if (typeof value === 'number' && Number.isInteger(value) && value >= 0 && value < values.length) {
    return values[value];
  }
  if (typeof additional === 'string' && values.includes(additional as T)) return additional as T;
  return undefined;
}

function requiredString(value: object, key: string): string {
  const result = readString(value, key);
  if (!result) throw new Error(`History event is missing '${key}'`);
  return result;
}

function readString(value: object, key: string): string | undefined {
  const result = (value as Record<string, unknown>)[key];
  return typeof result === 'string' ? result : undefined;
}

function cloneValue<T>(value: T): T {
  if (value === undefined) return value;
  return structuredClone(value);
}

function compact<T extends object>(value: T): T {
  return Object.fromEntries(
    Object.entries(value).filter(([, item]) => item !== undefined),
  ) as T;
}
