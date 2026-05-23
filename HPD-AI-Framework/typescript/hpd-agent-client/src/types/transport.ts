import type { AgentEvent, AgentRunInputEvent } from './events.js';

/**
 * Runtime connection scope for long-lived transports such as WebSocket.
 */
export interface RuntimeScope {
  /** Session ID for scoped transports */
  sessionId?: string;
  /** Branch ID for scoped transports (default: 'main') */
  branchId?: string;
  /** Optional AbortSignal for cancellation */
  signal?: AbortSignal;
  /** Agent definition ID to run when the input event omits agentId */
  agentId?: string;
}

export interface RunTransportOptions {
  signal?: AbortSignal;
}

/**
 * Abstract runtime transport interface.
 * Implementations handle only event streaming and bidirectional runtime input.
 * HTTP resources such as sessions, branches, agents, evals, and assets are owned
 * by AgentHttpApi rather than duplicated by every transport.
 */
export interface AgentTransport {
  /** Connect/start a long-lived runtime transport. SSE transports may no-op. */
  connect(scope?: RuntimeScope): Promise<void>;

  /** Send an agent input event. */
  run(event: AgentRunInputEvent, options?: RunTransportOptions): Promise<void>;

  /** Register event handler */
  onEvent(handler: (event: AgentEvent) => void): void;

  /** Register error handler */
  onError(handler: (error: Error) => void): void;

  /** Register close handler */
  onClose(handler: () => void): void;

  /** Disconnect */
  disconnect(): void;

  /** Connection state */
  readonly connected: boolean;
}
