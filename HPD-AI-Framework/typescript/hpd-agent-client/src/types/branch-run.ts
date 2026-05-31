export type BranchRunStatus = "active" | "completed" | "cancelled" | "failed";

export type BackgroundOperationStatus =
  | "Queued"
  | "InProgress"
  | "Completed"
  | "Failed"
  | "Cancelled"
  | string;

export interface BranchRunError {
  type?: string | null;
  message?: string | null;
}

export interface BranchRunBackgroundOperation {
  status: BackgroundOperationStatus;
  operationId?: string | null;
  statusMessage?: string | null;
  continuationToken?: string | null;
}

export interface BranchRunBackgroundTask {
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

export interface BranchRun {
  runtimeRunId: string;
  agentId: string;
  sessionId: string;
  branchId: string;
  status: BranchRunStatus;
  startedAt: string;
  completedAt?: string | null;
  error?: BranchRunError | null;
  backgroundOperation?: BranchRunBackgroundOperation | null;
  backgroundTasks: BranchRunBackgroundTask[];
}
