import { parseErrorResponse } from '../errors.js';
import { SseParser } from '../parser.js';
import type { AgentEvent, AgentRunInputEvent, RespondResult, RespondStatus } from '../types/events.js';
import type {
  InputSubmissionResult,
  InterruptionResult,
  InterruptionStatus,
  SubmitInputResult,
} from '../types/transport.js';
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
  private cursor = 0;

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

    const afterSequenceNumber = scope?.afterSequenceNumber ?? 0;
    if (!Number.isSafeInteger(afterSequenceNumber) || afterSequenceNumber < 0) {
      throw new Error('SSE afterSequenceNumber must be a non-negative safe integer');
    }
    this.cursor = afterSequenceNumber;

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

    const endpoint = input.type === EventTypes.INTERRUPTION_REQUEST
      ? `/agents/${this.agentId}/sessions/${this.sessionId}/threads/${this.threadId}/interrupt`
      : `/agents/${this.agentId}/sessions/${this.sessionId}/threads/${this.threadId}/inputs`;

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

    return readLifecycleResult(response, input.type === EventTypes.INTERRUPTION_REQUEST);
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
      `/events/live?after=${this.cursor}`,
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
          for (const message of parser.flush()) await this.dispatchCommitted(message.id, message.event);
          break;
        }

        for (const message of parser.processChunk(value)) {
          await this.dispatchCommitted(message.id, message.event);
        }
      }
    } finally {
      reader.releaseLock();
    }
  }

  private async dispatchCommitted(id: string | null, event: AgentEvent): Promise<void> {
    const sequenceNumber = parseCommittedSequence(id, event.sequenceNumber);
    if (sequenceNumber <= this.cursor) return;

    await this.eventHandler!(event);
    this.cursor = sequenceNumber;
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
  interruption: boolean,
): Promise<InputSubmissionResult | InterruptionResult> {
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
  if (interruption) {
    const status = parseInterruptionStatus(record.status);
    return {
      status,
      activeRun: record.activeRun && typeof record.activeRun === 'object'
        ? record.activeRun as InterruptionResult['activeRun']
        : null,
    };
  }

  if (typeof record.runtimeRunId !== 'string' || !record.runtimeRunId.trim()) {
    throw new Error('Input submission did not return runtimeRunId');
  }
  if (typeof record.startedAt !== 'string' || !record.startedAt.trim()) {
    throw new Error('Input submission did not return startedAt');
  }

  return {
    runtimeRunId: record.runtimeRunId,
    startedAt: record.startedAt,
  };
}

function parseInterruptionStatus(value: unknown): InterruptionStatus {
  switch (value) {
    case 'accepted':
    case 'already_terminal':
    case 'no_active_run':
    case 'active_run_mismatch':
      return value;
    default:
      throw new Error(`Unknown interruption status: ${String(value)}`);
  }
}

function parseCommittedSequence(id: string | null, eventSequenceNumber: number | undefined): number {
  const idSequenceNumber = id !== null && id !== '' ? Number(id) : undefined;
  if (idSequenceNumber !== undefined &&
      eventSequenceNumber !== undefined &&
      idSequenceNumber !== eventSequenceNumber) {
    throw new Error('SSE id did not match the event sequenceNumber');
  }

  const sequenceNumber = idSequenceNumber ?? eventSequenceNumber;
  if (!Number.isSafeInteger(sequenceNumber) || sequenceNumber! <= 0) {
    throw new Error('SSE event did not include a valid committed sequence');
  }

  return sequenceNumber!;
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
