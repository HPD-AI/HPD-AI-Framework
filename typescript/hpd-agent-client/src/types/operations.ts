import type { FunctionInvocationSnapshot } from './events.js';

export type AgentOperationSourceKind =
  | 'LocalTool'
  | 'McpTask'
  | 'SubAgent'
  | 'Workflow'
  | 'MultiAgent'
  | 'ProviderOperation'
  | string;

export type AgentOperationProviderStatus =
  | 'Accepted'
  | 'Running'
  | 'InputRequired'
  | 'Completed'
  | 'Failed'
  | 'CancellationRequested'
  | 'Cancelled'
  | string;

export type AgentOperationObservationStatus =
  | 'Attached'
  | 'Detaching'
  | 'Detached'
  | 'Reconciling'
  | 'Stopped'
  | string;

export type AgentOperationKind =
  | 'Task'
  | 'Process'
  | 'Session'
  | 'Workflow'
  | 'Provider'
  | string;

/** Flags may arrive as a named JSON string or numeric flags from non-HPD peers. */
export type AgentOperationCapabilities =
  | 'None'
  | 'Cancel'
  | 'Update'
  | 'Detach'
  | 'Reconcile'
  | string
  | number;

export interface AgentExecutionAddress {
  agentId: string;
  sessionId: string;
  threadId: string;
}

export interface AgentOperationControl {
  handleId?: string | null;
  kind: AgentOperationKind;
  capabilities: AgentOperationCapabilities;
}

export interface AgentOperationNotificationPolicy {
  includeProgress: boolean;
  includeTerminal: boolean;
  deduplicationKey?: string | null;
  minimumInterval: string;
}

export interface AgentOperationCompletion {
  summary?: string | null;
  artifactReferences?: string[] | null;
}

export interface AgentOperationFailure {
  code: string;
  message: string;
}

export interface AgentOperationRecoveryReference {
  kind: string;
  protectedReference: string;
}

export interface AgentOperationSnapshot {
  operationId: string;
  providerOperationId?: string | null;
  sourceKind: AgentOperationSourceKind;
  name: string;
  address: AgentExecutionAddress;
  originatingThreadExecutionId?: string | null;
  invocation?: FunctionInvocationSnapshot | null;
  providerStatus: AgentOperationProviderStatus;
  observationStatus: AgentOperationObservationStatus;
  control: AgentOperationControl;
  notification: AgentOperationNotificationPolicy;
  registeredAt: string;
  startedAt?: string | null;
  updatedAt: string;
  finishedAt?: string | null;
  completion?: AgentOperationCompletion | null;
  failure?: AgentOperationFailure | null;
  recovery?: AgentOperationRecoveryReference | null;
  version: number;
  metadata?: Record<string, string> | null;
}

export interface AgentOperationReceipt {
  operationId: string;
  providerOperationId?: string | null;
  sourceKind: AgentOperationSourceKind;
  name: string;
  address: AgentExecutionAddress;
  providerStatus: AgentOperationProviderStatus;
  observationStatus: AgentOperationObservationStatus;
  message?: string | null;
  control: AgentOperationControl;
  metadata?: Record<string, string> | null;
}
