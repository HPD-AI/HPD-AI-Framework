import { parseErrorResponse } from '../errors.js';
import { SseParser } from '../parser.js';
import type { AgentEvent, AgentRunInputEvent, RespondResult, RespondStatus } from '../types/events.js';
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
  private branchId?: string;
  private abortController?: AbortController;
  private eventHandler?: (event: AgentEvent) => void;
  private errorHandler?: (error: Error) => void;
  private closeHandler?: () => void;
  private _connected = false;

  constructor(baseUrl: string, private readonly requestOptions: TransportRequestOptions = {}) {
    this.baseUrl = baseUrl.replace(/\/$/, '');
  }

  get connected(): boolean {
    return this._connected;
  }

  async connect(scope?: RuntimeScope): Promise<void> {
    if (this._connected) {
      throw new Error('Already connected. Call disconnect() first.');
    }

    this.sessionId = scope?.sessionId;
    this.branchId = scope?.branchId || 'main';
    this.agentId = scope?.agentId;

    if (!this.sessionId) {
      throw new Error('SSE connect() requires sessionId');
    }

    if (!this.agentId) {
      throw new Error('SSE connect() requires agentId');
    }

    this.abortController = new AbortController();
    const signal = scope?.signal
      ? this.combineSignals(scope.signal, this.abortController.signal)
      : this.abortController.signal;

    const response = await this.fetch(
      `${this.baseUrl}/agents/${this.agentId}/sessions/${this.sessionId}/branches/${this.branchId}/events/live`,
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
      throw new Error('No response body');
    }

    this._connected = true;
    void this.processStream(response.body);
  }

  async submitInput(input: AgentRunInputEvent, options?: RunTransportOptions): Promise<RespondResult | undefined> {
    const sessionId = 'sessionId' in input ? input.sessionId : undefined;
    const branchId = 'branchId' in input ? input.branchId : undefined;
    const agentId = 'agentId' in input ? input.agentId : undefined;

    this.sessionId = sessionId ?? this.sessionId;
    this.branchId = branchId ?? this.branchId ?? 'main';
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
      ? `/agents/${this.agentId}/sessions/${this.sessionId}/branches/${this.branchId}/interrupt`
      : `/agents/${this.agentId}/sessions/${this.sessionId}/branches/${this.branchId}/inputs`;

    const response = await this.fetch(`${this.baseUrl}${endpoint}`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
      },
      body: JSON.stringify(input.type === EventTypes.USER_TEXT_INPUT
        ? { text: input.text, runConfig: input.runConfig }
        : input),
      signal: options?.signal,
    });

    if (!response.ok) {
      const text = await response.text().catch(() => 'Unknown error');
      throw new Error(`HTTP ${response.status}: ${text}`);
    }

    await response.body?.cancel().catch(() => undefined);
    return undefined;
  }

  onEvent(handler: (event: AgentEvent) => void): void {
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
    this._connected = false;
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

  private async processStream(body: ReadableStream<Uint8Array>): Promise<void> {
    const reader = body.getReader();
    const parser = new SseParser();

    try {
      while (true) {
        const { done, value } = await reader.read();
        if (done) {
          for (const event of parser.flush()) this.eventHandler?.(event);
          break;
        }

        for (const event of parser.processChunk(value)) this.eventHandler?.(event);
      }
    } catch (error) {
      if ((error as DOMException)?.name !== 'AbortError') {
        this.errorHandler?.(error as Error);
      }
    } finally {
      reader.releaseLock();
      this._connected = false;
      this.closeHandler?.();
    }
  }

  private isResponseInput(input: AgentRunInputEvent): boolean {
    return input.type === EventTypes.PERMISSION_RESPONSE ||
      input.type === EventTypes.CONTINUATION_RESPONSE ||
      input.type === EventTypes.CLARIFICATION_RESPONSE ||
      input.type === EventTypes.CLIENT_TOOL_INVOKE_RESPONSE;
  }

  private async postResponse(input: AgentRunInputEvent): Promise<RespondResult> {
    if (!this.agentId || !this.sessionId || !this.branchId) {
      throw new Error('Not connected');
    }

    const response = await this.fetch(`${this.baseUrl}${this.endpointForResponse(input)}`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(input),
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

    return `/agents/${this.agentId}/sessions/${this.sessionId}/branches/${this.branchId}/responses`;
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

function requestIdForResponse(input: AgentRunInputEvent): string {
  if ('requestId' in input && typeof input.requestId === 'string') return input.requestId;
  if ('permissionId' in input && typeof input.permissionId === 'string') return input.permissionId;
  if ('continuationId' in input && typeof input.continuationId === 'string') return input.continuationId;
  return '';
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
