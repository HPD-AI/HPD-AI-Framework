import type {
  BackgroundHandleKind,
  BackgroundHandleOperation,
  ClientToolAugmentation,
  ClientToolBackgroundOperationState,
  ClientToolHarnessDefinition,
  ClientToolInvokeOutcomeKind,
  ToolResultContent,
} from './client-tools.js';

export type ClientAppProviderBindingPolicy =
  | 'Exclusive'
  | 'Optional'
  | 'IfAvailable'
  | string;

export interface ClientAppProviderReference {
  name: string;
  providerSelector?: ClientProviderSelector;
  harnesses?: ClientToolHarnessSelector[];
  tools?: string[];
  required?: boolean;
  bindingPolicy?: ClientAppProviderBindingPolicy;
}

export interface ClientProviderSelector {
  clientRuntimeId?: string;
  appKind?: string;
  workspaceId?: string;
  documentId?: string;
  projectId?: string;
  userId?: string;
  tags?: string[];
  current?: boolean;
}

export interface ClientToolHarnessSelector {
  name: string;
  tools?: string[];
  expanded?: boolean;
  required?: boolean;
}

export interface ClientAppProviderDescriptor {
  name: string;
  displayName?: string;
  description?: string;
  version?: string;
  tags?: string[];
  metadata?: Record<string, unknown>;
}

export interface ClientToolProviderIdentity {
  providerName: string;
  appKind: string;
  instanceId?: string;
  installationId?: string;
  userHint?: string;
  origin?: string;
  version?: string;
}

export interface ClientToolProviderContext {
  workspaceId?: string;
  documentId?: string;
  documentName?: string;
  pageId?: string;
  fileId?: string;
  projectId?: string;
  sceneId?: string;
  activeView?: string;
  selectionSummary?: string;
  appStateVersion?: string;
  metadata?: Record<string, unknown>;
}

export type ClientToolProviderReadiness =
  | 'Initializing'
  | 'Ready'
  | 'Degraded'
  | 'Revoked'
  | string;

export type ClientToolProviderConnectionState =
  | 'Connected'
  | 'Registered'
  | 'Ready'
  | 'Bound'
  | 'Disconnected'
  | 'Revoked'
  | string;

export type ClientToolProviderBindingLeaseStatus =
  | 'Active'
  | 'Released'
  | 'Expired'
  | 'Disconnected'
  | 'Revoked'
  | 'Broken'
  | string;

export interface ClientToolProviderBindingScope {
  ownerRuntimeId?: string;
  agentId?: string;
  sessionId?: string;
  threadId?: string;
  threadExecutionId?: string;
  leaseDuration?: string;
}

export interface ClientToolProviderBindingLease {
  bindingId: string;
  clientRuntimeId: string;
  connectionId: string;
  ownerRuntimeId?: string;
  agentId?: string;
  sessionId?: string;
  threadId?: string;
  threadExecutionId?: string;
  boundAt?: string;
  expiresAt?: string;
  heartbeatInterval?: string;
  status?: ClientToolProviderBindingLeaseStatus;
  releasedAt?: string;
  releaseReason?: string;
}

export interface ClientToolProviderSnapshot {
  clientRuntimeId: string;
  connectionId: string;
  manifest?: ClientToolProviderManifest | null;
  state?: ClientToolProviderConnectionState;
  connectedAt?: string;
  lastHeartbeatAt?: string | null;
  disconnectedAt?: string | null;
  bindingLease?: ClientToolProviderBindingLease | null;
}

export interface ClientToolProviderQuery {
  appProviderName?: string;
  appKind?: string;
  includeDisconnected?: boolean;
}

export interface ClientToolProviderManifest {
  protocolVersion?: '1' | string;
  identity: ClientToolProviderIdentity;
  appProvider: ClientAppProviderDescriptor;
  context?: ClientToolProviderContext;
  readiness?: ClientToolProviderReadiness;
  clientToolHarnesses?: ClientToolHarnessDefinition[];
  metadata?: Record<string, unknown>;
}

export interface ClientToolProviderHelloMessage {
  type: 'provider.hello';
  protocolVersion?: '1' | string;
  identity: ClientToolProviderIdentity;
}

export interface ClientToolProviderWelcomeMessage {
  type: 'provider.welcome';
  clientRuntimeId: string;
  connectionId: string;
  heartbeatIntervalMs: number;
}

export interface ClientToolProviderManifestMessage {
  type: 'provider.manifest';
  protocolVersion?: '1' | string;
  appProvider: ClientAppProviderDescriptor;
  context?: ClientToolProviderContext;
  readiness?: ClientToolProviderReadiness;
  clientToolHarnesses?: ClientToolHarnessDefinition[];
  metadata?: Record<string, unknown>;
}

export interface ClientToolProviderHeartbeatMessage {
  type: 'provider.heartbeat';
}

export interface ClientToolProviderReleaseMessage {
  type: 'provider.release';
  bindingId?: string;
  reason?: string;
}

export interface ClientToolProviderInvokeToolMessage {
  type: 'provider.invoke';
  clientRuntimeId: string;
  connectionId: string;
  bindingId: string;
  invocationId: string;
  requestId: string;
  toolName: string;
  visibleToolName: string;
  callId: string;
  arguments: Record<string, unknown>;
  requestedInvocationMode?: 'Synchronous' | 'Background' | string;
  deadline?: string;
}

export interface ClientToolProviderInvokeOutcomeMessage {
  type: 'provider.invokeOutcome';
  bindingId: string;
  invocationId: string;
  requestId: string;
  outcome: ClientToolInvokeOutcomeKind;
  content?: ToolResultContent[];
  errorMessage?: string;
  clientOperationId?: string;
  handleKind?: BackgroundHandleKind;
  supportedOperations?: BackgroundHandleOperation;
  augmentation?: ClientToolAugmentation;
}

export interface ClientToolProviderBackgroundOperationOutcomeMessage {
  type: 'provider.backgroundOperationOutcome';
  bindingId: string;
  clientOperationId: string;
  state: ClientToolBackgroundOperationState;
  content?: ToolResultContent[];
  augmentation?: ClientToolAugmentation;
  errorMessage?: string | null;
  errorType?: string | null;
  cancellationReason?: string | null;
  metadata?: Record<string, string> | null;
}

export interface ClientToolProviderErrorMessage {
  type: 'provider.error';
  code: string;
  message: string;
}

export type ClientToolProviderToServerMessage =
  | ClientToolProviderHelloMessage
  | ClientToolProviderManifestMessage
  | ClientToolProviderInvokeOutcomeMessage
  | ClientToolProviderBackgroundOperationOutcomeMessage
  | ClientToolProviderHeartbeatMessage
  | ClientToolProviderReleaseMessage;

export type ServerToClientToolProviderMessage =
  | ClientToolProviderWelcomeMessage
  | ClientToolProviderInvokeToolMessage
  | ClientToolProviderErrorMessage;

export interface ClientToolProviderRoutes {
  connect: string;
}

export function hpdClientToolProviderRoutes(
  overrides: Partial<ClientToolProviderRoutes> = {},
): ClientToolProviderRoutes {
  return {
    connect: '/client-tool-providers/connect',
    ...overrides,
  };
}
