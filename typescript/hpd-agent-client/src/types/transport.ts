import type { AgentEvent, AgentRunInputEvent, RespondResult } from './events.js';
import type { ThreadJournalCursor, ThreadExecution } from './thread-execution.js';

export interface InputSubmissionResult {
  disposition:
    | 'completed'
    | 'queued'
    | 'accepted'
    | 'no_active_execution'
    | 'active_execution_mismatch'
    | 'active_input_not_steerable'
    | 'execution_finishing';
  threadExecutionId?: string | null;
  startedAt?: string | null;
  activeExecution?: ThreadExecution | null;
}

export type SubmitInputResult = RespondResult | InputSubmissionResult;

/**
 * Runtime connection scope for committed SSE observation.
 */
export interface RuntimeScope {
  /** Session ID for scoped transports */
  sessionId?: string;
  /** Thread ID for scoped transports (default: 'main') */
  threadId?: string;
  /** Optional AbortSignal for cancellation */
  signal?: AbortSignal;
  /** Agent definition ID to run when the input event omits agentId */
  agentId?: string;
  /** Last committed generation/sequence completely applied by the consumer. */
  after?: ThreadJournalCursor;
}

export interface RunTransportOptions {
  signal?: AbortSignal;
}

/**
 * Abstract runtime transport interface.
 * Implementations handle only event streaming and request-session runtime input.
 * HTTP resources such as sessions, threads, agents, evals, and contents are owned
 * by AgentHttpApi rather than duplicated by every transport.
 */
export interface AgentTransport {
  /** Connect/start a long-lived runtime transport. SSE transports may no-op. */
  connect(scope?: RuntimeScope): Promise<void>;

  /** Submit an agent input event to the runtime. Response events may return a structured status. */
  submitInput(event: AgentRunInputEvent, options?: RunTransportOptions): Promise<SubmitInputResult>;

  /** Register an acknowledged event handler. The cursor advances only after it resolves. */
  onEvent(handler: (event: AgentEvent) => void | Promise<void>): void;

  /** Register error handler */
  onError(handler: (error: Error) => void): void;

  /** Register close handler */
  onClose(handler: () => void): void;

  /** Disconnect */
  disconnect(): void;

  /** Connection state */
  readonly connected: boolean;
}
