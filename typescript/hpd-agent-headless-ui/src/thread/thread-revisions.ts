import {
  EventTypes,
  mapThreadMessages,
  type AgentClient,
  type ThreadMessageReadModel,
} from '@hpd-research/hpd-agent-client';
import type {
  ThreadRevisionController,
  ThreadRevisionControllerOptions,
  ThreadRevisionErrorCode,
  ThreadRevisionForkDetails,
  ThreadRevisionForkOptions,
  ThreadRevisionKind,
  ThreadRevisionOptions,
  ThreadRevisionResult,
  ThreadScope,
} from './types.js';

interface ResolvedRevision {
  requested: ThreadMessageReadModel;
  input: ThreadMessageReadModel;
  boundary: ThreadMessageReadModel | null;
  sentText: string;
}

export class ThreadRevisionError extends Error {
  constructor(
    readonly code: ThreadRevisionErrorCode,
    message: string,
  ) {
    super(message);
    this.name = 'ThreadRevisionError';
  }
}

export function createThreadRevisionController(
  options: ThreadRevisionControllerOptions,
): ThreadRevisionController {
  return new ThreadRevisionControllerImpl(options);
}

export function canEditThreadMessage(message: {
  role: string;
  placement?: string;
  content?: string;
  text?: string;
}): boolean {
  return isRevisionMessagePlacement(message.placement) &&
    message.role === 'user' &&
    hasRevisionMessageText(message);
}

export function canRetryThreadMessage(message: {
  role: string;
  placement?: string;
  content?: string;
  text?: string;
  toolCalls?: readonly unknown[];
}): boolean {
  return isRevisionMessagePlacement(message.placement) &&
    (message.role === 'user' || message.role === 'assistant') &&
    hasRevisionMessageText(message) &&
    !isToolOnlyRevisionMessage(message);
}

function isRevisionMessagePlacement(placement: string | undefined): boolean {
  return placement === undefined || placement === 'transcript' || placement === 'final';
}

function hasRevisionMessageText(message: { content?: string; text?: string }): boolean {
  return (message.content ?? message.text ?? '').trim().length > 0;
}

function isToolOnlyRevisionMessage(message: {
  content?: string;
  text?: string;
  toolCalls?: readonly unknown[];
}): boolean {
  return (message.toolCalls?.length ?? 0) > 0 && !hasRevisionMessageText(message);
}

class ThreadRevisionControllerImpl implements ThreadRevisionController {
  readonly scope: ThreadScope;
  private readonly client: AgentClient;

  constructor(options: ThreadRevisionControllerOptions) {
    this.client = options.client;
    this.scope = {
      agentId: options.agentId,
      sessionId: options.sessionId,
      threadId: options.threadId,
    };
  }

  async forkAndRetryMessage(
    messageId: string,
    options: ThreadRevisionOptions = {},
  ): Promise<ThreadRevisionResult> {
    const messages = await this.loadMessages();
    const revision = resolveRetry(messages, messageId);
    return this.forkAndSend('retry', revision, options);
  }

  async forkAndEditMessage(
    messageId: string,
    text: string,
    options: ThreadRevisionOptions = {},
  ): Promise<ThreadRevisionResult> {
    const messages = await this.loadMessages();
    const revision = resolveEdit(messages, messageId, text);
    return this.forkAndSend('edit', revision, options);
  }

  private async loadMessages(): Promise<ThreadMessageReadModel[]> {
    const messages = await this.client.getThreadMessages(this.scope.sessionId, this.scope.threadId);
    return mapThreadMessages(messages);
  }

  private async forkAndSend(
    kind: ThreadRevisionKind,
    revision: ResolvedRevision,
    options: ThreadRevisionOptions,
  ): Promise<ThreadRevisionResult> {
    const details = createRevisionForkDetails(kind, revision);
    const forkOptions = resolveForkOptions(options.fork, details);
    const thread = await this.client.forkThread(this.scope.sessionId, this.scope.threadId, {
      ...forkOptions,
      metadata: {
        ...forkOptions?.metadata,
        revisionKind: kind,
        clickedMessageId: details.clickedMessageId,
        inputMessageId: details.inputMessageId,
        forkBoundaryMessageId: details.forkBoundaryMessageId,
      },
      agentId: this.scope.agentId,
      fromMessageId: revision.boundary?.id ?? null,
    });

    await this.client.run({
      type: EventTypes.USER_MESSAGES_INPUT,
      agentId: this.scope.agentId,
      sessionId: this.scope.sessionId,
      threadId: thread.id,
      messages: [{
        role: 'user',
        contents: [{ $type: 'text', text: revision.sentText }],
      }],
      runConfig: options.runConfig,
    });

    return {
      kind,
      thread,
      threadId: thread.id,
      clickedMessageId: details.clickedMessageId,
      inputMessageId: details.inputMessageId,
      forkBoundaryMessageId: details.forkBoundaryMessageId,
      sentText: revision.sentText,
    };
  }
}

function createRevisionForkDetails(
  kind: ThreadRevisionKind,
  revision: ResolvedRevision,
): ThreadRevisionForkDetails {
  return {
    kind,
    clickedMessageId: revision.requested.id,
    inputMessageId: revision.input.id,
    forkBoundaryMessageId: revision.boundary?.id ?? null,
    sentText: revision.sentText,
  };
}

function resolveForkOptions(
  fork: ThreadRevisionOptions['fork'],
  details: ThreadRevisionForkDetails,
): ThreadRevisionForkOptions | undefined {
  return typeof fork === 'function' ? fork(details) : fork;
}

function resolveRetry(messages: ThreadMessageReadModel[], messageId: string): ResolvedRevision {
  const targetIndex = findMessageIndex(messages, messageId);
  const target = messages[targetIndex];

  if (!canRetryThreadMessage(target)) {
    throw new ThreadRevisionError(
      'unsupported-message-role',
      `Cannot retry message '${messageId}' because only user and assistant messages can be retried.`,
    );
  }

  const input = target.role === 'user'
    ? target
    : findPreviousUserMessage(messages, targetIndex);

  if (!input) {
    throw new ThreadRevisionError(
      'no-user-message',
      `Cannot retry message '${messageId}' because no previous user message was found.`,
    );
  }

  return resolveFromUserMessage(messages, target, input, input.text);
}

function resolveEdit(
  messages: ThreadMessageReadModel[],
  messageId: string,
  text: string,
): ResolvedRevision {
  const targetIndex = findMessageIndex(messages, messageId);
  const target = messages[targetIndex];

  if (target.role !== 'user') {
    throw new ThreadRevisionError(
      'unsupported-message-role',
      `Cannot edit message '${messageId}' because only user messages can be edited.`,
    );
  }

  return resolveFromUserMessage(messages, target, target, text);
}

function resolveFromUserMessage(
  messages: ThreadMessageReadModel[],
  requested: ThreadMessageReadModel,
  input: ThreadMessageReadModel,
  sentText: string,
): ResolvedRevision {
  const normalizedText = sentText.trim();
  if (!normalizedText) {
    throw new ThreadRevisionError(
      'empty-message',
      `Cannot revise message '${input.id}' with empty text.`,
    );
  }

  const inputIndex = messages.findIndex((message) => message.id === input.id);
  const boundary = inputIndex > 0 ? messages[inputIndex - 1] : null;

  return {
    requested,
    input,
    boundary,
    sentText: normalizedText,
  };
}

function findMessageIndex(messages: ThreadMessageReadModel[], messageId: string): number {
  const index = messages.findIndex((message) => message.id === messageId);
  if (index < 0) {
    throw new ThreadRevisionError(
      'message-not-found',
      `Cannot revise missing message '${messageId}'.`,
    );
  }
  return index;
}

function findPreviousUserMessage(
  messages: ThreadMessageReadModel[],
  beforeIndex: number,
): ThreadMessageReadModel | null {
  for (let index = beforeIndex - 1; index >= 0; index -= 1) {
    if (messages[index].role === 'user') return messages[index];
  }
  return null;
}
