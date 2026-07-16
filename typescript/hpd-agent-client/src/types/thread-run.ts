export type ThreadRunStatus = "active" | "completed" | "cancelled" | "failed" | "interrupted";

export type ModelBackgroundOperationStatus =
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

export interface ThreadRunModelBackgroundOperation {
  status: ModelBackgroundOperationStatus;
  operationId?: string | null;
  statusMessage?: string | null;
  continuationToken?: string | null;
}

export interface ThreadRunBackgroundTaskNotification {
  kind: string;
  strategyName?: string | null;
}

export interface ThreadRunBackgroundTask {
  taskId: string;
  name: string;
  sourceKind: string;
  sourceId?: string | null;
  notification: ThreadRunBackgroundTaskNotification;
  status: "started" | "completed" | "cancelled" | "faulted" | string;
  startedAt?: string | null;
  completedAt?: string | null;
  cancelledAt?: string | null;
  faultedAt?: string | null;
  errorType?: string | null;
  errorMessage?: string | null;
}

export interface ThreadRunBackgroundHandle {
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

export interface ThreadRun {
  runtimeRunId: string;
  agentId: string;
  sessionId: string;
  threadId: string;
  status: ThreadRunStatus;
  startedAt: string;
  completedAt?: string | null;
  error?: ThreadRunError | null;
  modelBackgroundOperation?: ThreadRunModelBackgroundOperation | null;
  backgroundTasks: ThreadRunBackgroundTask[];
  backgroundHandles: ThreadRunBackgroundHandle[];
}

export interface ThreadRuntimeState {
  observedHead: number;
  activeRun: ThreadRun | null;
}
