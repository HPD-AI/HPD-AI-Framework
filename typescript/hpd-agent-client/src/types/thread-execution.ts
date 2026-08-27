export type ThreadExecutionStatus = "active" | "succeeded" | "cancelled" | "failed" | "interrupted";

export interface ThreadExecutionError {
  type?: string | null;
  message?: string | null;
}

export interface ThreadExecutionOperation {
  operationId: string;
  providerOperationId?: string | null;
  name: string;
  sourceKind: string;
  providerStatus: string;
  observationStatus: string;
  controlKind: string;
  controlCapabilities: string;
  controlHandleId?: string | null;
  version: number;
  registeredAt: string;
  startedAt?: string | null;
  updatedAt: string;
  finishedAt?: string | null;
  completionSummary?: string | null;
  artifactReferences?: string[] | null;
  failureCode?: string | null;
  failureMessage?: string | null;
  metadata?: Record<string, string> | null;
}

export interface ThreadExecution {
  threadExecutionId: string;
  agentId: string;
  sessionId: string;
  threadId: string;
  status: ThreadExecutionStatus;
  startedAt: string;
  finishedAt?: string | null;
  error?: ThreadExecutionError | null;
  operations: ThreadExecutionOperation[];
}

export interface PendingAgentRequest {
  request: import('./events.js').AgentEvent;
  createdAt: string;
}

export interface ThreadRuntimeState {
  observedCursor: ThreadJournalCursor;
  activeExecution: ThreadExecution | null;
  pendingRequests: PendingAgentRequest[];
}

export interface ThreadJournalCursor {
  generation: number;
  sequenceNumber: number;
}
