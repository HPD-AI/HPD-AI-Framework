export type ThreadRunStatus = "active" | "completed" | "cancelled" | "failed" | "interrupted";

export type BackgroundOperationStatus =
  | "Queued"
  | "InProgress"
  | "Completed"
  | "Failed"
  | "Cancelled"
  | string;

export interface ThreadRunError {
  type?: string | null;
  message?: string | null;
}

export interface ThreadRunBackgroundOperation {
  status: BackgroundOperationStatus;
  operationId?: string | null;
  statusMessage?: string | null;
  continuationToken?: string | null;
}

export interface ThreadRunBackgroundTask {
  taskId: string;
  name: string;
  status: "started" | "completed" | "cancelled" | "faulted" | string;
  startedAt?: string | null;
  completedAt?: string | null;
  cancelledAt?: string | null;
  faultedAt?: string | null;
  errorType?: string | null;
  errorMessage?: string | null;
}

export interface ThreadRun {
  runtimeRunId: string;
  agentId: string;
  sessionId: string;
  threadId: string;
  status: ThreadRunStatus;
  startedAt: string;
  completedAt?: string | null;
  error?: ThreadRunError | null;
  backgroundOperation?: ThreadRunBackgroundOperation | null;
  backgroundTasks: ThreadRunBackgroundTask[];
}
