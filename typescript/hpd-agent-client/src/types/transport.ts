import type { AgentEvent, AgentRunInputEvent, RespondResult } from './events.js';
import type { ThreadRun } from './thread-run.js';

export interface InputSubmissionResult {
  runtimeRunId: string;
  startedAt: string;
}

export type InterruptionStatus =
  | 'accepted'
  | 'already_terminal'
  | 'no_active_run'
  | 'active_run_mismatch';

export interface InterruptionResult {
  status: InterruptionStatus;
  activeRun?: ThreadRun | null;
}

export type SubmitInputResult = RespondResult | InputSubmissionResult | InterruptionResult;

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
  /** Last committed sequence completely applied by the consumer. */
  afterSequenceNumber?: number;
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
