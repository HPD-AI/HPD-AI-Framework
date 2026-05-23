import { AgentError, parseErrorResponse } from '../errors.js';
import { SseParser } from '../parser.js';
import type { AgentEvent, AgentRunInputEvent } from '../types/events.js';
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
  }

  async run(input: AgentRunInputEvent, options?: RunTransportOptions): Promise<void> {
    const sessionId = 'sessionId' in input ? input.sessionId : undefined;
    const branchId = 'branchId' in input ? input.branchId : undefined;
    const agentId = 'agentId' in input ? input.agentId : undefined;

    this.sessionId = sessionId ?? this.sessionId;
    this.branchId = branchId ?? this.branchId ?? 'main';
    this.agentId = agentId ?? this.agentId;

    if (this.isResponseInput(input)) {
      await this.postResponse(input);
      return;
    }

    if (this._connected) {
      throw new Error('Already connected. Call disconnect() first.');
    }

    if (!this.sessionId) {
      throw new Error('Input event must include sessionId for SSE run()');
    }

    if (!this.agentId) {
      throw new Error('Input event must include agentId for SSE run()');
    }

    this.abortController = new AbortController();
    const signal = options?.signal
      ? this.combineSignals(options.signal, this.abortController.signal)
      : this.abortController.signal;

    const isTextInput = input.type === EventTypes.USER_TEXT_INPUT;
    const endpoint = isTextInput
      ? `/agents/${this.agentId}/sessions/${this.sessionId}/branches/${this.branchId}/stream`
      : `/agents/${this.agentId}/sessions/${this.sessionId}/branches/${this.branchId}/events/stream`;

    const response = await this.fetch(`${this.baseUrl}${endpoint}`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        Accept: 'text/event-stream',
      },
      body: JSON.stringify(isTextInput
        ? { text: input.text, runConfig: input.runConfig }
        : input),
      signal,
    });

    if (!response.ok) {
      const text = await response.text().catch(() => 'Unknown error');
      throw new Error(`HTTP ${response.status}: ${text}`);
    }

    if (!response.body) {
      throw new Error('No response body');
    }

    this._connected = true;
    await this.processStream(response.body);
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

  private async postResponse(input: AgentRunInputEvent): Promise<void> {
    if (!this.agentId || !this.sessionId || !this.branchId) {
      throw new Error('Not connected');
    }

    const response = await this.fetch(`${this.baseUrl}${this.endpointForResponse(input)}`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(input),
    });

    if (!response.ok) {
      if (response.status === 409) {
        throw new AgentError(
          'Response was not accepted because the request is no longer pending',
          'STALE_RESPONSE',
          { statusCode: response.status },
        );
      }

      const body = await response.json().catch(() => null);
      throw parseErrorResponse(response, body);
    }
  }

  private endpointForResponse(input: AgentRunInputEvent): string {
    switch (input.type) {
      case EventTypes.PERMISSION_RESPONSE:
        return `/agents/${this.agentId}/sessions/${this.sessionId}/branches/${this.branchId}/permissions/respond`;
      case EventTypes.CONTINUATION_RESPONSE:
        return `/agents/${this.agentId}/sessions/${this.sessionId}/branches/${this.branchId}/continuation/respond`;
      case EventTypes.CLARIFICATION_RESPONSE:
        return `/agents/${this.agentId}/sessions/${this.sessionId}/branches/${this.branchId}/clarifications/respond`;
      case EventTypes.CLIENT_TOOL_INVOKE_RESPONSE:
        return `/agents/${this.agentId}/sessions/${this.sessionId}/branches/${this.branchId}/client-tools/respond`;
      default:
        throw new Error(`Unknown response type: ${(input as { type: string }).type}`);
    }
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
