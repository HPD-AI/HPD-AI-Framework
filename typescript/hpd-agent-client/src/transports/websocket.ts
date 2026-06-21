import type { AgentEvent, AgentRunInputEvent } from '../types/events.js';
import type { SubmitInputResult } from '../types/transport.js';
import type { AgentTransport, RunTransportOptions, RuntimeScope } from '../types/transport.js';

/**
 * Runtime-only WebSocket transport.
 * HTTP resources are intentionally handled by AgentHttpApi.
 */
export class WebSocketTransport implements AgentTransport {
  private readonly baseUrl: string;
  private ws?: WebSocket;
  private scope?: RuntimeScope;
  private eventHandler?: (event: AgentEvent) => void;
  private errorHandler?: (error: Error) => void;
  private closeHandler?: () => void;

  constructor(baseUrl: string) {
    this.baseUrl = toWebSocketBaseUrl(baseUrl);
  }

  get connected(): boolean {
    return this.ws?.readyState === WebSocket.OPEN;
  }

  connect(scope?: RuntimeScope): Promise<void> {
    if (this.ws?.readyState === WebSocket.OPEN || this.ws?.readyState === WebSocket.CONNECTING) {
      return Promise.reject(new Error('Already connected. Call disconnect() first.'));
    }

    return new Promise((resolve, reject) => {
      if (!scope?.sessionId) {
        reject(new Error('WebSocket connect() requires sessionId'));
        return;
      }

      if (!scope.agentId) {
        reject(new Error('WebSocket connect() requires agentId'));
        return;
      }

      this.scope = scope;
      const threadId = scope.threadId || 'main';
      const url = [
        this.baseUrl,
        'agents',
        encodeURIComponent(scope.agentId),
        'sessions',
        encodeURIComponent(scope.sessionId),
        'threads',
        encodeURIComponent(threadId),
        'ws',
      ].join('/');

      try {
        this.ws = new WebSocket(url);
      } catch (error) {
        const message = error instanceof Error ? error.message : String(error);
        reject(new Error(`Failed to create WebSocket for ${url}: ${message}`));
        return;
      }

      const cleanup = () => {
        scope.signal?.removeEventListener('abort', onAbort);
      };

      const onAbort = () => {
        cleanup();
        this.ws?.close();
        reject(new DOMException('Aborted', 'AbortError'));
      };

      if (scope.signal?.aborted) {
        reject(new DOMException('Aborted', 'AbortError'));
        return;
      }

      scope.signal?.addEventListener('abort', onAbort, { once: true });

      this.ws.onopen = () => {
        cleanup();
        resolve();
      };

      this.ws.onmessage = (event) => {
        try {
          this.eventHandler?.(JSON.parse(event.data as string) as AgentEvent);
        } catch {
          // Ignore malformed runtime messages.
        }
      };

      this.ws.onerror = () => {
        cleanup();
        const error = new Error('WebSocket error');
        this.errorHandler?.(error);
        reject(error);
      };

      this.ws.onclose = () => {
        cleanup();
        this.closeHandler?.();
      };
    });
  }

  async submitInput(input: AgentRunInputEvent, _options?: RunTransportOptions): Promise<SubmitInputResult> {
    if (this.ws?.readyState !== WebSocket.OPEN) {
      throw new Error('WebSocket not connected');
    }

    this.ws.send(JSON.stringify({
      ...input,
      sessionId: 'sessionId' in input ? input.sessionId ?? this.scope?.sessionId : this.scope?.sessionId,
      threadId: 'threadId' in input ? input.threadId ?? this.scope?.threadId ?? 'main' : this.scope?.threadId ?? 'main',
      agentId: 'agentId' in input ? input.agentId ?? this.scope?.agentId : this.scope?.agentId,
    }));
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
    this.ws?.close();
  }
}

function toWebSocketBaseUrl(baseUrl: string): string {
  const trimmed = baseUrl.replace(/\/$/, '');

  if (/^wss?:\/\//i.test(trimmed)) {
    return trimmed;
  }

  if (/^https?:\/\//i.test(trimmed)) {
    return trimmed
      .replace(/^http:/i, 'ws:')
      .replace(/^https:/i, 'wss:');
  }

  const location = globalThis.location;
  if (location?.origin) {
    const url = new URL(trimmed.startsWith('/') ? trimmed : `/${trimmed}`, location.origin);
    url.protocol = location.protocol === 'https:' ? 'wss:' : 'ws:';
    return url.toString().replace(/\/$/, '');
  }

  return trimmed;
}
