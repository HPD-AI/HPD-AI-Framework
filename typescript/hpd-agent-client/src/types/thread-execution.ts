export type ThreadExecutionStatus = "active" | "succeeded" | "cancelled" | "failed" | "interrupted";

export type ModelBackgroundOperationStatus =
  | "Queued"
  | "InProgress"
  | "Completed"
  | "Failed"
  | "Cancelled"
  | string;

export interface ThreadExecutionError {
  type: string;
  message: string;
}

export interface ThreadExecutionModelBackgroundOperation {
  status: ModelBackgroundOperationStatus;
  operationId?: string | null;
  statusMessage?: string | null;
  continuationToken?: string | null;
}

export interface ThreadExecutionBackgroundTaskNotification {
  kind: string;
  strategyName?: string | null;
}

export interface ThreadExecutionBackgroundTask {
  taskId: string;
  name: string;
  sourceKind: string;
  sourceId?: string | null;
  notification: ThreadExecutionBackgroundTaskNotification;
  status: "started" | "completed" | "cancelled" | "faulted" | string;
  startedAt?: string | null;
  completedAt?: string | null;
  cancelledAt?: string | null;
  faultedAt?: string | null;
  errorType?: string | null;
  errorMessage?: string | null;
}

export interface ThreadExecutionBackgroundHandle {
  handleId: string;
  name: string;
  handleKind: string;
  sourceKind: string;
  sourceId?: string | null;
  status: string;
  supportedOperations: string;
  registeredAt: string;
  updatedAt?: string | null;
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
  modelBackgroundOperation?: ThreadExecutionModelBackgroundOperation | null;
  backgroundTasks: ThreadExecutionBackgroundTask[];
  backgroundHandles: ThreadExecutionBackgroundHandle[];
}

export interface ThreadRuntimeState {
  observedCursor: ThreadJournalCursor;
  activeExecution: ThreadExecution | null;
}

export interface ThreadJournalCursor {
  generation: number;
  sequenceNumber: number;
}
