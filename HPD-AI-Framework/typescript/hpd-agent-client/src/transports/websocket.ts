import type { AgentEvent, AgentRunInputEvent } from '../types/events.js';
import type { AgentTransport, RuntimeScope } from '../types/transport.js';

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
    this.baseUrl = baseUrl
      .replace(/^http:/, 'ws:')
      .replace(/^https:/, 'wss:')
      .replace(/\/$/, '');
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
      const branchId = scope.branchId || 'main';
      const url = `${this.baseUrl}/agents/${scope.agentId}/sessions/${scope.sessionId}/branches/${branchId}/ws`;

      try {
        this.ws = new WebSocket(url);
      } catch (error) {
        reject(new Error(`Failed to create WebSocket: ${error}`));
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

  async run(input: AgentRunInputEvent): Promise<void> {
    if (this.ws?.readyState !== WebSocket.OPEN) {
      throw new Error('WebSocket not connected');
    }

    this.ws.send(JSON.stringify({
      ...input,
      sessionId: 'sessionId' in input ? input.sessionId ?? this.scope?.sessionId : this.scope?.sessionId,
      branchId: 'branchId' in input ? input.branchId ?? this.scope?.branchId ?? 'main' : this.scope?.branchId ?? 'main',
      agentId: 'agentId' in input ? input.agentId ?? this.scope?.agentId : this.scope?.agentId,
    }));
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
