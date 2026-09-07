import type { BaseEvent } from './events.js';

export interface AgentInputCancellation {
  cause: 'Unknown' | 'Caller' | 'Explicit' | 'RuntimeShutdown' | 'Middleware';
  reason?: string | null;
  source: string;
}
export type GoalStatus = 'active' | 'paused' | 'awaitingInput' | 'completed' | 'blocked' | 'usageLimited' | 'faulted';
export type GoalUsageQuality = 'exact' | 'partial' | 'unavailable';
export type GoalBlockerCategory = 'userDecision' | 'authority' | 'missingArtifact' | 'conflictingRequirements' | 'externalSystem' | 'environment';
export interface GoalEvidenceItem { kind: string; description: string; reference?: string | null }
export interface GoalCompletionProposal {
  summary: string; evidence: GoalEvidenceItem[]; proposedAt: string; executionId: string; remainingWork: string[];
}
export interface GoalBlockerEvidence {
  category: GoalBlockerCategory; fingerprint: string; description: string; requiredChange: string;
  evidence: string[]; consecutiveExecutions: number; firstObservedAt: string; lastObservedAt: string;
  lastExecutionId: string; lastExecutionOrdinal: number;
}
export interface GoalAccounting {
  tokensUsed: number; usageQuality: GoalUsageQuality;
  /** .NET TimeSpan in invariant constant format. */
  executionTime: string;
  executionCount: number; lastAccountedExecutionId?: string | null; lastAccountedMessageTurnId?: string | null;
}
export interface GoalData {
  goalId: string; objective: string; status: GoalStatus; revision: number; continuationGeneration: number;
  continuation?: { generation: number; expectedRevision: number; reservedAt: string; sourceExecutionId: string; activationOwner?: string | null } | null;
  accounting: GoalAccounting; consecutiveNoProgressExecutions: number;
  completionProposal?: GoalCompletionProposal | null; blocker?: GoalBlockerEvidence | null;
  createdAt: string; updatedAt: string;
}

export interface GoalStartedEvent extends BaseEvent {
  type: 'GOAL_STARTED';
  goal: GoalData;
  reason: string;
}
export interface GoalUpdatedEvent extends BaseEvent {
  type: 'GOAL_UPDATED';
  goal: GoalData;
  reason: string;
}
export interface GoalPausedEvent extends BaseEvent {
  type: 'GOAL_PAUSED';
  cancellation?: AgentInputCancellation | null;
  goal: GoalData;
  reason: string;
}
export interface GoalResumedEvent extends BaseEvent {
  type: 'GOAL_RESUMED';
  goal: GoalData;
  reason: string;
}
export interface GoalEditedEvent extends BaseEvent {
  type: 'GOAL_EDITED';
  goal: GoalData;
  reason: string;
}
export interface GoalClearedEvent extends BaseEvent {
  type: 'GOAL_CLEARED';
  goal: GoalData;
  reason: string;
}
export interface GoalContinuationScheduledEvent extends BaseEvent {
  type: 'GOAL_CONTINUATION_SCHEDULED';
  goal: GoalData;
  reason: string;
}
export interface GoalContinuationStartedEvent extends BaseEvent {
  type: 'GOAL_CONTINUATION_STARTED';
  goal: GoalData;
  reason: string;
}
export interface GoalContinuationSkippedEvent extends BaseEvent {
  type: 'GOAL_CONTINUATION_SKIPPED';
  goal: GoalData;
  reason: string;
}
export interface GoalProgressAccountedEvent extends BaseEvent {
  type: 'GOAL_PROGRESS_ACCOUNTED';
  goal: GoalData;
  reason: string;
}
export interface GoalCompletionProposedEvent extends BaseEvent {
  type: 'GOAL_COMPLETION_PROPOSED';
  goal: GoalData;
  reason: string;
}
export interface GoalCompletionRejectedEvent extends BaseEvent {
  type: 'GOAL_COMPLETION_REJECTED';
  goal: GoalData;
  reason: string;
}
export interface GoalCompletedEvent extends BaseEvent {
  type: 'GOAL_COMPLETED';
  goal: GoalData;
  reason: string;
  acceptedProposal?: GoalCompletionProposal | null;
}
export interface GoalBlockerReportedEvent extends BaseEvent {
  type: 'GOAL_BLOCKER_REPORTED';
  goal: GoalData;
  reason: string;
}
export interface GoalBlockerRejectedEvent extends BaseEvent {
  type: 'GOAL_BLOCKER_REJECTED';
  goal: GoalData;
  reason: string;
}
export interface GoalAwaitingInputEvent extends BaseEvent {
  type: 'GOAL_AWAITING_INPUT';
  goal: GoalData;
  reason: string;
}
export interface GoalBlockedEvent extends BaseEvent {
  type: 'GOAL_BLOCKED';
  goal: GoalData;
  reason: string;
}
export interface GoalUsageLimitedEvent extends BaseEvent {
  type: 'GOAL_USAGE_LIMITED';
  goal: GoalData;
  reason: string;
}
export interface GoalFaultedEvent extends BaseEvent {
  type: 'GOAL_FAULTED';
  goal: GoalData;
  reason: string;
}

export type GoalLifecycleEvent =
  GoalStartedEvent
  | GoalUpdatedEvent
  | GoalPausedEvent
  | GoalResumedEvent
  | GoalEditedEvent
  | GoalClearedEvent
  | GoalContinuationScheduledEvent
  | GoalContinuationStartedEvent
  | GoalContinuationSkippedEvent
  | GoalProgressAccountedEvent
  | GoalCompletionProposedEvent
  | GoalCompletionRejectedEvent
  | GoalCompletedEvent
  | GoalBlockerReportedEvent
  | GoalBlockerRejectedEvent
  | GoalAwaitingInputEvent
  | GoalBlockedEvent
  | GoalUsageLimitedEvent
  | GoalFaultedEvent;
