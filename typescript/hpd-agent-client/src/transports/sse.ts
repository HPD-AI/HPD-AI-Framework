import { parseErrorResponse } from '../errors.js';
import { SseParser } from '../parser.js';
import type { AgentEvent, AgentRunInputEvent, RespondResult, RespondStatus } from '../types/events.js';
import type {
  InputSubmissionResult,
  SubmitInputResult,
} from '../types/transport.js';
import type { ThreadJournalCursor } from '../types/thread-execution.js';
import type { SseMessage } from '../parser.js';
import { EventTypes } from '../types/events.js';
import type {
  AgentTransport,
  RunTransportOptions,
  RuntimeScope,
} from '../types/transport.js';
import type { TransportRequestOptions } from './options.js';

/**
 * Runtime-only SSE transport.
 * HTTP resources are intentionally handled by AgentHttpApi.
 */
export class SseTransport implements AgentTransport {
  private readonly baseUrl: string;
  private agentId?: string;
  private sessionId?: string;
  private threadId?: string;
  private abortController?: AbortController;
  private eventHandler?: (event: AgentEvent) => void | Promise<void>;
  private errorHandler?: (error: Error) => void;
  private closeHandler?: () => void;
  private _observing = false;
  private cursor: ThreadJournalCursor = { generation: 1, sequenceNumber: 0 };

  constructor(baseUrl: string, private readonly requestOptions: TransportRequestOptions = {}) {
    this.baseUrl = baseUrl.replace(/\/$/, '');
  }

  get connected(): boolean {
    return this._observing;
  }

  async connect(scope?: RuntimeScope): Promise<void> {
    if (this._observing) {
      throw new Error('Already connected. Call disconnect() first.');
    }

    this.sessionId = scope?.sessionId;
    this.threadId = scope?.threadId || 'main';
    this.agentId = scope?.agentId;

    if (!this.sessionId) {
      throw new Error('SSE connect() requires sessionId');
    }

    if (!this.agentId) {
      throw new Error('SSE connect() requires agentId');
    }

    if (!this.eventHandler) {
      throw new Error('SSE connect() requires an event handler');
    }

    const after = scope?.after ?? { generation: 1, sequenceNumber: 0 };
    if (!isCursor(after)) {
      throw new Error('SSE after cursor must contain a positive generation and non-negative sequence');
    }
    this.cursor = after;

    this.abortController = new AbortController();
    const signal = scope?.signal
      ? this.combineSignals(scope.signal, this.abortController.signal)
      : this.abortController.signal;

    this._observing = true;
    let body: ReadableStream<Uint8Array>;
    try {
      body = await this.openStream(signal);
    } catch (error) {
      this._observing = false;
      throw error;
    }
    void this.observeUntilCancelled(body, signal);
  }

  async submitInput(input: AgentRunInputEvent, options?: RunTransportOptions): Promise<SubmitInputResult> {
    const sessionId = 'sessionId' in input ? input.sessionId : undefined;
    const threadId = 'threadId' in input ? input.threadId : undefined;
    const agentId = 'agentId' in input ? input.agentId : undefined;

    this.sessionId = sessionId ?? this.sessionId;
    this.threadId = threadId ?? this.threadId ?? 'main';
    this.agentId = agentId ?? this.agentId;

    if (this.isResponseInput(input)) {
      return this.postResponse(input);
    }

    if (!this.sessionId) {
      throw new Error('Input event must include sessionId for SSE submitInput()');
    }

    if (!this.agentId) {
      throw new Error('Input event must include agentId for SSE submitInput()');
    }

    const endpoint = `/agents/${this.agentId}/sessions/${this.sessionId}/threads/${this.threadId}/inputs`;

    const response = await this.fetch(`${this.baseUrl}${endpoint}`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
      },
      body: JSON.stringify(input),
      signal: options?.signal,
    });

    if (!response.ok) {
      const text = await response.text().catch(() => 'Unknown error');
      throw new Error(`HTTP ${response.status}: ${text}`);
    }

    return readLifecycleResult(response);
  }

  onEvent(handler: (event: AgentEvent) => void | Promise<void>): void {
    this.eventHandler = handler;
  }

  onError(handler: (error: Error) => void): void {
    this.errorHandler = handler;
  }

  onClose(handler: () => void): void {
    this.closeHandler = handler;
  }

  disconnect(): void {
    this.abortController?.abort();
  }

  private fetch(input: RequestInfo | URL, init: RequestInit = {}): Promise<Response> {
    const headers = {
      ...(this.requestOptions.headers ?? {}),
      ...((init.headers as Record<string, string> | undefined) ?? {}),
    };

    return globalThis.fetch(input, {
      ...init,
      credentials: this.requestOptions.credentials,
      headers,
    });
  }

  private async openStream(signal: AbortSignal): Promise<ReadableStream<Uint8Array>> {
    const response = await this.fetch(
      `${this.baseUrl}/agents/${encodeURIComponent(this.agentId!)}` +
      `/sessions/${encodeURIComponent(this.sessionId!)}` +
      `/threads/${encodeURIComponent(this.threadId!)}` +
      `/events?after=${this.cursor.generation}:${this.cursor.sequenceNumber}`,
      {
        method: 'GET',
        headers: { Accept: 'text/event-stream' },
        signal,
      },
    );

    if (!response.ok) {
      const text = await response.text().catch(() => 'Unknown error');
      throw new Error(`HTTP ${response.status}: ${text}`);
    }

    if (!response.body) {
      throw new Error('SSE response did not include a body');
    }

    return response.body;
  }

  private async observeUntilCancelled(
    initialBody: ReadableStream<Uint8Array>,
    signal: AbortSignal,
  ): Promise<void> {
    let body: ReadableStream<Uint8Array> | undefined = initialBody;
    let retryDelayMs = 250;

    try {
      while (!signal.aborted) {
        try {
          body ??= await this.openStream(signal);
          await this.processStream(body, signal);
          body = undefined;
          retryDelayMs = 250;
        } catch (error) {
          body = undefined;
          if (signal.aborted || (error as DOMException)?.name === 'AbortError') {
            break;
          }

          this.errorHandler?.(error instanceof Error ? error : new Error(String(error)));
          if (error instanceof ThreadJournalRebasedError) {
            break;
          }
          retryDelayMs = Math.min(retryDelayMs * 2, 5_000);
        }

        if (!signal.aborted) {
          await delay(retryDelayMs, signal);
        }
      }
    } catch (error) {
      if (!signal.aborted && (error as DOMException)?.name !== 'AbortError') {
        this.errorHandler?.(error instanceof Error ? error : new Error(String(error)));
      }
    } finally {
      this._observing = false;
      this.closeHandler?.();
    }
  }

  private async processStream(
    body: ReadableStream<Uint8Array>,
    signal: AbortSignal,
  ): Promise<void> {
    const reader = body.getReader();
    const parser = new SseParser();

    try {
      while (!signal.aborted) {
        const { done, value } = await reader.read();
        if (done) {
          for (const message of parser.flush()) await this.dispatchMessage(message);
          break;
        }

        for (const message of parser.processChunk(value)) {
          await this.dispatchMessage(message);
        }
      }
    } finally {
      reader.releaseLock();
    }
  }

  private async dispatchMessage(message: SseMessage): Promise<void> {
    if (message.kind === 'control') {
      if (message.eventName === 'thread-journal-rebased') {
        const data = message.data as { previousGeneration?: unknown; currentGeneration?: unknown };
        if (!Number.isSafeInteger(data.previousGeneration) || !Number.isSafeInteger(data.currentGeneration)) {
          throw new Error('Invalid thread-journal-rebased control payload');
        }
        throw new ThreadJournalRebasedError(
          Number(data.previousGeneration),
          Number(data.currentGeneration));
      }
      return;
    }

    if (message.kind === 'live-agent-event') {
      await this.eventHandler!(message.event);
      return;
    }

    await this.dispatchCommitted(message.id, message.event);
  }

  private async dispatchCommitted(id: string | null, event: AgentEvent): Promise<void> {
    const cursor = parseCommittedCursor(id, event.threadSequenceNumber);
    if (cursor.generation !== this.cursor.generation) {
      throw new ThreadJournalRebasedError(this.cursor.generation, cursor.generation);
    }
    if (cursor.sequenceNumber <= this.cursor.sequenceNumber) return;

    await this.eventHandler!(event);
    this.cursor = cursor;
  }

  private isResponseInput(input: AgentRunInputEvent): boolean {
    return input.type === EventTypes.PERMISSION_RESPONSE ||
      input.type === EventTypes.CONTINUATION_RESPONSE ||
      input.type === EventTypes.CLARIFICATION_RESPONSE ||
      input.type === EventTypes.CLIENT_TOOL_INVOKE_OUTCOME;
  }

  private async postResponse(input: AgentRunInputEvent): Promise<RespondResult> {
    if (!this.agentId || !this.sessionId || !this.threadId) {
      throw new Error('Not connected');
    }

    const response = await this.fetch(`${this.baseUrl}${this.endpointForResponse(input)}`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: serializeResponseInput(input),
    });

    const body = await response.json?.().catch(() => null) ?? null;
    const requestId = requestIdForResponse(input);

    if (response.ok) {
      return normalizeRespondResult(body, requestId);
    }

    if (response.status === 409) {
      if (body?.result) {
        return normalizeRespondResult(body.result, requestId);
      }

      const details = body?.errors as Record<string, string[]> | undefined;
      const serverCode = details ? Object.keys(details)[0] : undefined;

      if (details && serverCode) {
        const messages = details[serverCode];
        const status = normalizeRespondStatus(serverCode);
        return {
          status,
          requestId,
          message: messages?.[0] ?? body?.title ?? 'Response was not accepted',
          accepted: status === 'accepted',
        };
      }

      return {
        status: 'alreadyResolved',
        requestId,
        message: 'Response was not accepted because the request is no longer pending',
        accepted: false,
      };
    }

    throw parseErrorResponse(response, body);
  }

  private endpointForResponse(input: AgentRunInputEvent): string {
    if (!this.isResponseInput(input)) {
      throw new Error(`Unknown response type: ${(input as { type: string }).type}`);
    }

    return `/agents/${this.agentId}/sessions/${this.sessionId}/threads/${this.threadId}/responses`;
  }

  private combineSignals(...signals: AbortSignal[]): AbortSignal {
    const controller = new AbortController();

    for (const signal of signals) {
      if (signal.aborted) {
        controller.abort(signal.reason);
        return controller.signal;
      }
      signal.addEventListener('abort', () => controller.abort(signal.reason), { once: true });
    }

    return controller.signal;
  }
}

function serializeResponseInput(input: AgentRunInputEvent): string {
  const body: Record<string, unknown> = {
    version: input.version ?? '1.0',
    type: input.type,
  };

  for (const [key, value] of Object.entries(input)) {
    if (value === undefined || key === 'version' || key === 'type') continue;
    body[key] = key === 'choice' ? serializePermissionChoice(value) : value;
  }

  return JSON.stringify(body);
}

function serializePermissionChoice(value: unknown): unknown {
  switch (value) {
    case 'ask':
      return 0;
    case 'allow_always':
      return 1;
    case 'deny_always':
      return 2;
    default:
      return value;
  }
}

function requestIdForResponse(input: AgentRunInputEvent): string {
  if ('requestId' in input && typeof input.requestId === 'string') return input.requestId;
  if ('permissionId' in input && typeof input.permissionId === 'string') return input.permissionId;
  if ('continuationId' in input && typeof input.continuationId === 'string') return input.continuationId;
  return '';
}

async function readLifecycleResult(
  response: Response,
): Promise<InputSubmissionResult> {
  const text = await response.text().catch(() => '');
  if (!text.trim()) {
    throw new Error('Lifecycle submission returned an empty response');
  }

  let parsed: unknown;
  try {
    parsed = JSON.parse(text);
  } catch {
    throw new Error('Lifecycle submission returned invalid JSON');
  }

  if (!parsed || typeof parsed !== 'object') {
    throw new Error('Lifecycle submission returned an invalid result');
  }

  const record = parsed as Record<string, unknown>;
  const disposition = parseInputDisposition(record.disposition);

  return {
    disposition,
    threadExecutionId: typeof record.threadExecutionId === 'string' ? record.threadExecutionId : null,
    startedAt: typeof record.startedAt === 'string' ? record.startedAt : null,
    activeExecution: record.activeExecution && typeof record.activeExecution === 'object'
      ? record.activeExecution as InputSubmissionResult['activeExecution']
      : null,
  };
}

function parseInputDisposition(value: unknown): InputSubmissionResult['disposition'] {
  switch (value) {
    case 'completed':
    case 'queued':
    case 'accepted':
    case 'no_active_execution':
    case 'active_execution_mismatch':
    case 'active_input_not_steerable':
    case 'execution_finishing':
      return value;
    default:
      throw new Error(`Unknown input disposition: ${String(value)}`);
  }
}

function parseCommittedCursor(id: string | null, eventThreadSequenceNumber: number | undefined): ThreadJournalCursor {
  const parts = id?.split(':');
  const generation = parts?.length === 2 ? Number(parts[0]) : undefined;
  const idSequenceNumber = parts?.length === 2 ? Number(parts[1]) : undefined;
  if (idSequenceNumber !== undefined &&
      eventThreadSequenceNumber !== undefined &&
      idSequenceNumber !== eventThreadSequenceNumber) {
    throw new Error('SSE id did not match the event threadSequenceNumber');
  }

  const sequenceNumber = idSequenceNumber ?? eventThreadSequenceNumber;
  if (!Number.isSafeInteger(sequenceNumber) || sequenceNumber! <= 0) {
    throw new Error('SSE event did not include a valid committed sequence');
  }

  if (!Number.isSafeInteger(generation) || generation! <= 0) {
    throw new Error('SSE event did not include a valid journal generation');
  }

  return { generation: generation!, sequenceNumber: sequenceNumber! };
}

function isCursor(value: ThreadJournalCursor): boolean {
  return Number.isSafeInteger(value.generation) && value.generation > 0 &&
    Number.isSafeInteger(value.sequenceNumber) && value.sequenceNumber >= 0;
}

export class ThreadJournalRebasedError extends Error {
  constructor(
    public readonly previousGeneration: number,
    public readonly currentGeneration: number,
  ) {
    super(`Thread journal rebased from generation ${previousGeneration} to ${currentGeneration}`);
    this.name = 'ThreadJournalRebasedError';
  }
}

function delay(milliseconds: number, signal: AbortSignal): Promise<void> {
  return new Promise((resolve, reject) => {
    const onAbort = () => {
      clearTimeout(timeout);
      reject(new DOMException('Aborted', 'AbortError'));
    };
    const timeout = setTimeout(() => {
      signal.removeEventListener('abort', onAbort);
      resolve();
    }, milliseconds);
    signal.addEventListener('abort', onAbort, { once: true });
  });
}

function normalizeRespondResult(value: unknown, fallbackRequestId: string): RespondResult {
  if (value && typeof value === 'object') {
    const record = value as Record<string, unknown>;
    const status = normalizeRespondStatus(record.status);
    return {
      status,
      requestId: typeof record.requestId === 'string' ? record.requestId : fallbackRequestId,
      message: typeof record.message === 'string' ? record.message : null,
      accepted: typeof record.accepted === 'boolean' ? record.accepted : status === 'accepted',
    };
  }

  return {
    status: 'accepted',
    requestId: fallbackRequestId,
    message: null,
    accepted: true,
  };
}

function normalizeRespondStatus(value: unknown): RespondStatus {
  if (typeof value === 'number') {
    return [
      'accepted',
      'notFound',
      'alreadyResolved',
      'timedOut',
      'cancelled',
      'responseTypeMismatch',
      'targetMismatch',
      'ambiguousRequest',
    ][value] as RespondStatus | undefined ?? 'notFound';
  }

  if (typeof value === 'string') {
    const normalized = value.charAt(0).toLowerCase() + value.slice(1);
    switch (normalized) {
      case 'accepted':
      case 'notFound':
      case 'alreadyResolved':
      case 'timedOut':
      case 'cancelled':
      case 'responseTypeMismatch':
      case 'targetMismatch':
      case 'ambiguousRequest':
        return normalized;
      default:
        return value.toUpperCase() === 'STALE_RESPONSE' ? 'alreadyResolved' : 'notFound';
    }
  }

  return 'notFound';
}
