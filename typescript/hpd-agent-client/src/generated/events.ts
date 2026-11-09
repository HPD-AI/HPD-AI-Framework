/* eslint-disable */
import type {
  HPDAgentAgentCapabilityRefreshPublishedEvent,
  HPDAgentAgentCapabilityRefreshRejectedEvent,
  HPDAgentAgentCapabilityRefreshStartedEvent,
  HPDAgentAgentCompletionEvent,
  HPDAgentAgentDecisionEvent,
  HPDAgentAgentOperationNotificationDeliveredEvent,
  HPDAgentAgentOperationNotificationInputEvent,
  HPDAgentAgentOperationNotificationQueuedEvent,
  HPDAgentAgentOperationNotificationSuppressedEvent,
  HPDAgentAgentOperationRegisteredEvent,
  HPDAgentAgentOperationTombstoneEvictedEvent,
  HPDAgentAgentOperationTombstonedEvent,
  HPDAgentAgentOperationTransitionedEvent,
  HPDAgentAgentRequestTerminatedEvent,
  HPDAgentAgentTurnCapabilitiesPinnedEvent,
  HPDAgentAgentTurnFinishedEvent,
  HPDAgentAgentTurnStartedEvent,
  HPDAgentAudioSessionInputEvent,
  HPDAgentCircuitBreakerTriggeredEvent,
  HPDAgentClientToolsClientToolInvokeOutcomeEvent,
  HPDAgentClientToolsClientToolInvokeRequestEvent,
  HPDAgentClientToolsClientToolOperationOutcomeEvent,
  HPDAgentCollapsedToolsVisibleEvent,
  HPDAgentCollapsingStateEvent,
  HPDAgentCompactThreadInputEvent,
  HPDAgentCompactionEvent,
  HPDAgentContainerExpandedEvent,
  HPDAgentContentAddedEvent,
  HPDAgentContentCaptureFinalizedEvent,
  HPDAgentContentReadEvent,
  HPDAgentContentScopeCleanedEvent,
  HPDAgentContinuationRequestEvent,
  HPDAgentContinuationResponseEvent,
  HPDAgentDeltaSendingActivatedEvent,
  HPDAgentDocumentProcessedEvent,
  HPDAgentEventDroppedEvent,
  HPDAgentFunctionInvocationAuditedEvent,
  HPDAgentFunctionRetryEvent,
  HPDAgentGoalsCreateGoalInputEvent,
  HPDAgentGoalsGoalAwaitingInputEvent,
  HPDAgentGoalsGoalBlockedEvent,
  HPDAgentGoalsGoalBlockerRejectedEvent,
  HPDAgentGoalsGoalBlockerReportedEvent,
  HPDAgentGoalsGoalClearedEvent,
  HPDAgentGoalsGoalCompletedEvent,
  HPDAgentGoalsGoalCompletionProposedEvent,
  HPDAgentGoalsGoalCompletionRejectedEvent,
  HPDAgentGoalsGoalContinuationScheduledEvent,
  HPDAgentGoalsGoalContinuationSkippedEvent,
  HPDAgentGoalsGoalContinuationStartedEvent,
  HPDAgentGoalsGoalEditedEvent,
  HPDAgentGoalsGoalFaultedEvent,
  HPDAgentGoalsGoalPausedEvent,
  HPDAgentGoalsGoalProgressAccountedEvent,
  HPDAgentGoalsGoalResumedEvent,
  HPDAgentGoalsGoalStartedEvent,
  HPDAgentGoalsGoalUpdatedEvent,
  HPDAgentGoalsGoalUsageLimitedEvent,
  HPDAgentInternalMessagePreparedEvent,
  HPDAgentInternalParallelToolExecutionEvent,
  HPDAgentInterruptionHandledEvent,
  HPDAgentIterationStartEvent,
  HPDAgentMaxConsecutiveErrorsExceededEvent,
  HPDAgentMessageTurnErrorEvent,
  HPDAgentMessageTurnFinishedEvent,
  HPDAgentMessageTurnStartedEvent,
  HPDAgentMiddlewareContentReferenceResolutionFailedEvent,
  HPDAgentMiddlewareContentReferenceResolvedEvent,
  HPDAgentMiddlewareContentUploadFailedEvent,
  HPDAgentMiddlewareContentUploadedEvent,
  HPDAgentMiddlewareErrorEvent,
  HPDAgentMiddlewareHostedFileUploadFailedEvent,
  HPDAgentMiddlewareHostedFileUploadedEvent,
  HPDAgentMiddlewareStateChangedEvent,
  HPDAgentMiddlewareStateSnapshotEvent,
  HPDAgentModelCallRetryEvent,
  HPDAgentNestedAgentInvokedEvent,
  HPDAgentOperationExecutionOwnerCleanupFailedEvent,
  HPDAgentPIIDetectedEvent,
  HPDAgentParentQuestionRequestEvent,
  HPDAgentPermissionEvaluatedEvent,
  HPDAgentPermissionRequestEvent,
  HPDAgentPermissionResolvedEvent,
  HPDAgentPermissionResponseEvent,
  HPDAgentPlanUpdatedEvent,
  HPDAgentProviderOperationUsageEvent,
  HPDAgentProviderValuationObservationEvent,
  HPDAgentQuestionResponseEvent,
  HPDAgentReasoningDeltaEvent,
  HPDAgentReasoningMessageEndEvent,
  HPDAgentReasoningMessageStartEvent,
  HPDAgentRequestEventProcessedEvent,
  HPDAgentRuntimeAgentDurableEvent,
  HPDAgentRuntimeAgentInputEvent,
  HPDAgentRuntimeAgentLiveEvent,
  HPDAgentSkillActivatedEvent,
  HPDAgentSkillActivationFailedEvent,
  HPDAgentSkillActivationStartedEvent,
  HPDAgentSkillResourceReadCompletedEvent,
  HPDAgentSkillResourceReadFailedEvent,
  HPDAgentSkillResourceReadStartedEvent,
  HPDAgentSkillScriptCompletedEvent,
  HPDAgentSkillScriptFailedEvent,
  HPDAgentSkillScriptStartedEvent,
  HPDAgentSkillScriptTimedOutEvent,
  HPDAgentStateSnapshotEvent,
  HPDAgentStructuredOutputCompleteEvent,
  HPDAgentStructuredOutputErrorEvent,
  HPDAgentStructuredOutputPartialEvent,
  HPDAgentStructuredOutputStartEvent,
  HPDAgentSubAgentChildControllerAuthorityEvent,
  HPDAgentSubAgentChildDetachedEvent,
  HPDAgentSubAgentChildRegisteredEvent,
  HPDAgentSubAgentChildRemappedEvent,
  HPDAgentSubAgentChildUnavailableEvent,
  HPDAgentSubAgentContextReceivedEvent,
  HPDAgentSubAgentControllerGrantedEvent,
  HPDAgentSubAgentCreationAdvancedEvent,
  HPDAgentSubAgentCreationReservedEvent,
  HPDAgentSubAgentExecutionControllerEvent,
  HPDAgentSubAgentInvocationCancelledEvent,
  HPDAgentSubAgentInvocationCompletedEvent,
  HPDAgentSubAgentInvocationFailedEvent,
  HPDAgentSubAgentInvocationStartedEvent,
  HPDAgentSubAgentQuestionRaisedEvent,
  HPDAgentSubAgentRegistrySeedEvent,
  HPDAgentSubAgentResultSubmittedEvent,
  HPDAgentTextDeltaEvent,
  HPDAgentTextMessageEndEvent,
  HPDAgentTextMessageStartEvent,
  HPDAgentThreadCreatedEvent,
  HPDAgentThreadExecutionFinishedEvent,
  HPDAgentThreadExecutionStartedEvent,
  HPDAgentThreadForkOperationChangedEvent,
  HPDAgentThreadHistoryCompactionCheckpointEvent,
  HPDAgentThreadMessageReplacedEvent,
  HPDAgentThreadMiddlewareStateCommittedEvent,
  HPDAgentThreadUpdatedEvent,
  HPDAgentToolBodyOperationCommittedFailureEvent,
  HPDAgentToolCallArgsEvent,
  HPDAgentToolCallEndEvent,
  HPDAgentToolCallResultEvent,
  HPDAgentToolCallStartEvent,
  HPDAgentToolOutputProjectedEvent,
  HPDAgentToolOutputProjectionFailedEvent,
  HPDAgentTotalErrorThresholdExceededEvent,
  HPDAgentUserAudioTranscriptCompletedEvent,
  HPDAgentUserAudioTranscriptDeltaEvent,
  HPDAgentUserAudioTranscriptFailedEvent,
  HPDAgentUserMessageEvent,
  HPDAgentUserMessagesInputEvent,
  HPDAgentUserQuestionRequestEvent
} from './models.js';

export type HpdAgentResponseInput =
  | HPDAgentClientToolsClientToolInvokeOutcomeEvent
  | HPDAgentContinuationResponseEvent
  | HPDAgentPermissionResponseEvent
  | HPDAgentQuestionResponseEvent;

export type HpdAgentEvent =
  | HPDAgentAgentCapabilityRefreshPublishedEvent
  | HPDAgentAgentCapabilityRefreshRejectedEvent
  | HPDAgentAgentCapabilityRefreshStartedEvent
  | HPDAgentAgentCompletionEvent
  | HPDAgentAgentDecisionEvent
  | HPDAgentAgentOperationNotificationDeliveredEvent
  | HPDAgentAgentOperationNotificationQueuedEvent
  | HPDAgentAgentOperationNotificationSuppressedEvent
  | HPDAgentAgentOperationRegisteredEvent
  | HPDAgentAgentOperationTombstoneEvictedEvent
  | HPDAgentAgentOperationTombstonedEvent
  | HPDAgentAgentOperationTransitionedEvent
  | HPDAgentAgentRequestTerminatedEvent
  | HPDAgentAgentTurnCapabilitiesPinnedEvent
  | HPDAgentAgentTurnFinishedEvent
  | HPDAgentAgentTurnStartedEvent
  | HPDAgentCircuitBreakerTriggeredEvent
  | HPDAgentClientToolsClientToolInvokeOutcomeEvent
  | HPDAgentClientToolsClientToolInvokeRequestEvent
  | HPDAgentCollapsedToolsVisibleEvent
  | HPDAgentCollapsingStateEvent
  | HPDAgentCompactionEvent
  | HPDAgentContainerExpandedEvent
  | HPDAgentContentAddedEvent
  | HPDAgentContentCaptureFinalizedEvent
  | HPDAgentContentReadEvent
  | HPDAgentContentScopeCleanedEvent
  | HPDAgentContinuationRequestEvent
  | HPDAgentContinuationResponseEvent
  | HPDAgentDeltaSendingActivatedEvent
  | HPDAgentDocumentProcessedEvent
  | HPDAgentEventDroppedEvent
  | HPDAgentFunctionInvocationAuditedEvent
  | HPDAgentFunctionRetryEvent
  | HPDAgentGoalsGoalAwaitingInputEvent
  | HPDAgentGoalsGoalBlockedEvent
  | HPDAgentGoalsGoalBlockerRejectedEvent
  | HPDAgentGoalsGoalBlockerReportedEvent
  | HPDAgentGoalsGoalClearedEvent
  | HPDAgentGoalsGoalCompletedEvent
  | HPDAgentGoalsGoalCompletionProposedEvent
  | HPDAgentGoalsGoalCompletionRejectedEvent
  | HPDAgentGoalsGoalContinuationScheduledEvent
  | HPDAgentGoalsGoalContinuationSkippedEvent
  | HPDAgentGoalsGoalContinuationStartedEvent
  | HPDAgentGoalsGoalEditedEvent
  | HPDAgentGoalsGoalFaultedEvent
  | HPDAgentGoalsGoalPausedEvent
  | HPDAgentGoalsGoalProgressAccountedEvent
  | HPDAgentGoalsGoalResumedEvent
  | HPDAgentGoalsGoalStartedEvent
  | HPDAgentGoalsGoalUpdatedEvent
  | HPDAgentGoalsGoalUsageLimitedEvent
  | HPDAgentInternalMessagePreparedEvent
  | HPDAgentInternalParallelToolExecutionEvent
  | HPDAgentInterruptionHandledEvent
  | HPDAgentIterationStartEvent
  | HPDAgentMaxConsecutiveErrorsExceededEvent
  | HPDAgentMessageTurnErrorEvent
  | HPDAgentMessageTurnFinishedEvent
  | HPDAgentMessageTurnStartedEvent
  | HPDAgentMiddlewareContentReferenceResolutionFailedEvent
  | HPDAgentMiddlewareContentReferenceResolvedEvent
  | HPDAgentMiddlewareContentUploadFailedEvent
  | HPDAgentMiddlewareContentUploadedEvent
  | HPDAgentMiddlewareErrorEvent
  | HPDAgentMiddlewareHostedFileUploadFailedEvent
  | HPDAgentMiddlewareHostedFileUploadedEvent
  | HPDAgentMiddlewareStateChangedEvent
  | HPDAgentMiddlewareStateSnapshotEvent
  | HPDAgentModelCallRetryEvent
  | HPDAgentNestedAgentInvokedEvent
  | HPDAgentOperationExecutionOwnerCleanupFailedEvent
  | HPDAgentPIIDetectedEvent
  | HPDAgentParentQuestionRequestEvent
  | HPDAgentPermissionEvaluatedEvent
  | HPDAgentPermissionRequestEvent
  | HPDAgentPermissionResolvedEvent
  | HPDAgentPermissionResponseEvent
  | HPDAgentPlanUpdatedEvent
  | HPDAgentProviderOperationUsageEvent
  | HPDAgentProviderValuationObservationEvent
  | HPDAgentQuestionResponseEvent
  | HPDAgentReasoningDeltaEvent
  | HPDAgentReasoningMessageEndEvent
  | HPDAgentReasoningMessageStartEvent
  | HPDAgentRequestEventProcessedEvent
  | HPDAgentRuntimeAgentDurableEvent
  | HPDAgentRuntimeAgentLiveEvent
  | HPDAgentSkillActivatedEvent
  | HPDAgentSkillActivationFailedEvent
  | HPDAgentSkillActivationStartedEvent
  | HPDAgentSkillResourceReadCompletedEvent
  | HPDAgentSkillResourceReadFailedEvent
  | HPDAgentSkillResourceReadStartedEvent
  | HPDAgentSkillScriptCompletedEvent
  | HPDAgentSkillScriptFailedEvent
  | HPDAgentSkillScriptStartedEvent
  | HPDAgentSkillScriptTimedOutEvent
  | HPDAgentStateSnapshotEvent
  | HPDAgentStructuredOutputCompleteEvent
  | HPDAgentStructuredOutputErrorEvent
  | HPDAgentStructuredOutputPartialEvent
  | HPDAgentStructuredOutputStartEvent
  | HPDAgentSubAgentChildControllerAuthorityEvent
  | HPDAgentSubAgentChildDetachedEvent
  | HPDAgentSubAgentChildRegisteredEvent
  | HPDAgentSubAgentChildRemappedEvent
  | HPDAgentSubAgentChildUnavailableEvent
  | HPDAgentSubAgentContextReceivedEvent
  | HPDAgentSubAgentControllerGrantedEvent
  | HPDAgentSubAgentCreationAdvancedEvent
  | HPDAgentSubAgentCreationReservedEvent
  | HPDAgentSubAgentExecutionControllerEvent
  | HPDAgentSubAgentInvocationCancelledEvent
  | HPDAgentSubAgentInvocationCompletedEvent
  | HPDAgentSubAgentInvocationFailedEvent
  | HPDAgentSubAgentInvocationStartedEvent
  | HPDAgentSubAgentQuestionRaisedEvent
  | HPDAgentSubAgentRegistrySeedEvent
  | HPDAgentSubAgentResultSubmittedEvent
  | HPDAgentTextDeltaEvent
  | HPDAgentTextMessageEndEvent
  | HPDAgentTextMessageStartEvent
  | HPDAgentThreadCreatedEvent
  | HPDAgentThreadExecutionFinishedEvent
  | HPDAgentThreadExecutionStartedEvent
  | HPDAgentThreadForkOperationChangedEvent
  | HPDAgentThreadHistoryCompactionCheckpointEvent
  | HPDAgentThreadMessageReplacedEvent
  | HPDAgentThreadMiddlewareStateCommittedEvent
  | HPDAgentThreadUpdatedEvent
  | HPDAgentToolBodyOperationCommittedFailureEvent
  | HPDAgentToolCallArgsEvent
  | HPDAgentToolCallEndEvent
  | HPDAgentToolCallResultEvent
  | HPDAgentToolCallStartEvent
  | HPDAgentToolOutputProjectedEvent
  | HPDAgentToolOutputProjectionFailedEvent
  | HPDAgentTotalErrorThresholdExceededEvent
  | HPDAgentUserAudioTranscriptCompletedEvent
  | HPDAgentUserAudioTranscriptDeltaEvent
  | HPDAgentUserAudioTranscriptFailedEvent
  | HPDAgentUserMessageEvent
  | HPDAgentUserQuestionRequestEvent;

export type HpdAgentInputEvent =
  | HPDAgentAgentOperationNotificationInputEvent
  | HPDAgentAudioSessionInputEvent
  | HPDAgentClientToolsClientToolOperationOutcomeEvent
  | HPDAgentCompactThreadInputEvent
  | HPDAgentGoalsCreateGoalInputEvent
  | HPDAgentRuntimeAgentInputEvent
  | HPDAgentUserMessagesInputEvent;

export const EventTypes = {
  AGENT_CAPABILITY_REFRESH_PUBLISHED: "AGENT_CAPABILITY_REFRESH_PUBLISHED",
  AGENT_CAPABILITY_REFRESH_REJECTED: "AGENT_CAPABILITY_REFRESH_REJECTED",
  AGENT_CAPABILITY_REFRESH_STARTED: "AGENT_CAPABILITY_REFRESH_STARTED",
  AGENT_COMPLETION: "AGENT_COMPLETION",
  AGENT_DECISION: "AGENT_DECISION",
  AGENT_OPERATION_NOTIFICATION_DELIVERED: "AGENT_OPERATION_NOTIFICATION_DELIVERED",
  AGENT_OPERATION_NOTIFICATION_QUEUED: "AGENT_OPERATION_NOTIFICATION_QUEUED",
  AGENT_OPERATION_NOTIFICATION_SUPPRESSED: "AGENT_OPERATION_NOTIFICATION_SUPPRESSED",
  AGENT_OPERATION_REGISTERED: "AGENT_OPERATION_REGISTERED",
  AGENT_OPERATION_TOMBSTONED: "AGENT_OPERATION_TOMBSTONED",
  AGENT_OPERATION_TOMBSTONE_EVICTED: "AGENT_OPERATION_TOMBSTONE_EVICTED",
  AGENT_OPERATION_TRANSITIONED: "AGENT_OPERATION_TRANSITIONED",
  AGENT_REQUEST_TERMINATED: "AGENT_REQUEST_TERMINATED",
  AGENT_TURN_CAPABILITIES_PINNED: "AGENT_TURN_CAPABILITIES_PINNED",
  AGENT_TURN_FINISHED: "AGENT_TURN_FINISHED",
  AGENT_TURN_STARTED: "AGENT_TURN_STARTED",
  CIRCUIT_BREAKER_TRIGGERED: "CIRCUIT_BREAKER_TRIGGERED",
  CLIENT_TOOL_INVOKE_OUTCOME: "CLIENT_TOOL_INVOKE_OUTCOME",
  CLIENT_TOOL_INVOKE_REQUEST: "CLIENT_TOOL_INVOKE_REQUEST",
  COLLAPSED_TOOLS_VISIBLE: "COLLAPSED_TOOLS_VISIBLE",
  COLLAPSING_STATE: "COLLAPSING_STATE",
  COMPACTION: "COMPACTION",
  CONTAINER_EXPANDED: "CONTAINER_EXPANDED",
  CONTENT_ADDED: "CONTENT_ADDED",
  CONTENT_CAPTURE_FINALIZED: "CONTENT_CAPTURE_FINALIZED",
  CONTENT_READ: "CONTENT_READ",
  CONTENT_REFERENCE_RESOLUTION_FAILED: "CONTENT_REFERENCE_RESOLUTION_FAILED",
  CONTENT_REFERENCE_RESOLVED: "CONTENT_REFERENCE_RESOLVED",
  CONTENT_SCOPE_CLEANED: "CONTENT_SCOPE_CLEANED",
  CONTENT_UPLOADED: "CONTENT_UPLOADED",
  CONTENT_UPLOAD_FAILED: "CONTENT_UPLOAD_FAILED",
  CONTINUATION_REQUEST: "CONTINUATION_REQUEST",
  CONTINUATION_RESPONSE: "CONTINUATION_RESPONSE",
  DELTA_SENDING_ACTIVATED: "DELTA_SENDING_ACTIVATED",
  DOCUMENT_PROCESSED: "DOCUMENT_PROCESSED",
  EVENT_DROPPED: "EVENT_DROPPED",
  FUNCTION_INVOCATION_AUDITED: "FUNCTION_INVOCATION_AUDITED",
  FUNCTION_RETRY: "FUNCTION_RETRY",
  GOAL_AWAITING_INPUT: "GOAL_AWAITING_INPUT",
  GOAL_BLOCKED: "GOAL_BLOCKED",
  GOAL_BLOCKER_REJECTED: "GOAL_BLOCKER_REJECTED",
  GOAL_BLOCKER_REPORTED: "GOAL_BLOCKER_REPORTED",
  GOAL_CLEARED: "GOAL_CLEARED",
  GOAL_COMPLETED: "GOAL_COMPLETED",
  GOAL_COMPLETION_PROPOSED: "GOAL_COMPLETION_PROPOSED",
  GOAL_COMPLETION_REJECTED: "GOAL_COMPLETION_REJECTED",
  GOAL_CONTINUATION_SCHEDULED: "GOAL_CONTINUATION_SCHEDULED",
  GOAL_CONTINUATION_SKIPPED: "GOAL_CONTINUATION_SKIPPED",
  GOAL_CONTINUATION_STARTED: "GOAL_CONTINUATION_STARTED",
  GOAL_EDITED: "GOAL_EDITED",
  GOAL_FAULTED: "GOAL_FAULTED",
  GOAL_PAUSED: "GOAL_PAUSED",
  GOAL_PROGRESS_ACCOUNTED: "GOAL_PROGRESS_ACCOUNTED",
  GOAL_RESUMED: "GOAL_RESUMED",
  GOAL_STARTED: "GOAL_STARTED",
  GOAL_UPDATED: "GOAL_UPDATED",
  GOAL_USAGE_LIMITED: "GOAL_USAGE_LIMITED",
  HOSTED_FILE_UPLOADED: "HOSTED_FILE_UPLOADED",
  HOSTED_FILE_UPLOAD_FAILED: "HOSTED_FILE_UPLOAD_FAILED",
  INTERNAL_MESSAGE_PREPARED: "INTERNAL_MESSAGE_PREPARED",
  INTERNAL_PARALLEL_TOOL_EXECUTION: "INTERNAL_PARALLEL_TOOL_EXECUTION",
  INTERRUPTION_HANDLED: "INTERRUPTION_HANDLED",
  ITERATION_START: "ITERATION_START",
  MAX_CONSECUTIVE_ERRORS_EXCEEDED: "MAX_CONSECUTIVE_ERRORS_EXCEEDED",
  MESSAGE_TURN_ERROR: "MESSAGE_TURN_ERROR",
  MESSAGE_TURN_FINISHED: "MESSAGE_TURN_FINISHED",
  MESSAGE_TURN_STARTED: "MESSAGE_TURN_STARTED",
  MIDDLEWARE_ERROR: "MIDDLEWARE_ERROR",
  MIDDLEWARE_STATE_CHANGED: "MIDDLEWARE_STATE_CHANGED",
  MIDDLEWARE_STATE_SNAPSHOT: "MIDDLEWARE_STATE_SNAPSHOT",
  MODEL_CALL_RETRY: "MODEL_CALL_RETRY",
  NESTED_AGENT_INVOKED: "NESTED_AGENT_INVOKED",
  OPERATION_EXECUTION_OWNER_CLEANUP_FAILED: "OPERATION_EXECUTION_OWNER_CLEANUP_FAILED",
  PARENT_QUESTION_REQUEST: "PARENT_QUESTION_REQUEST",
  PERMISSION_EVALUATED: "PERMISSION_EVALUATED",
  PERMISSION_REQUEST: "PERMISSION_REQUEST",
  PERMISSION_RESOLVED: "PERMISSION_RESOLVED",
  PERMISSION_RESPONSE: "PERMISSION_RESPONSE",
  PII_DETECTED: "PII_DETECTED",
  PLAN_UPDATED: "PLAN_UPDATED",
  PROVIDER_OPERATION_USAGE: "PROVIDER_OPERATION_USAGE",
  PROVIDER_VALUATION_OBSERVATION: "PROVIDER_VALUATION_OBSERVATION",
  QUESTION_RESPONSE: "QUESTION_RESPONSE",
  REASONING_DELTA: "REASONING_DELTA",
  REASONING_MESSAGE_END: "REASONING_MESSAGE_END",
  REASONING_MESSAGE_START: "REASONING_MESSAGE_START",
  REQUEST_EVENT_PROCESSED: "REQUEST_EVENT_PROCESSED",
  RUNTIME_AGENT_DURABLE_EVENT: "RUNTIME_AGENT_DURABLE_EVENT",
  RUNTIME_AGENT_LIVE_EVENT: "RUNTIME_AGENT_LIVE_EVENT",
  SKILL_ACTIVATED: "SKILL_ACTIVATED",
  SKILL_ACTIVATION_FAILED: "SKILL_ACTIVATION_FAILED",
  SKILL_ACTIVATION_STARTED: "SKILL_ACTIVATION_STARTED",
  SKILL_RESOURCE_READ_COMPLETED: "SKILL_RESOURCE_READ_COMPLETED",
  SKILL_RESOURCE_READ_FAILED: "SKILL_RESOURCE_READ_FAILED",
  SKILL_RESOURCE_READ_STARTED: "SKILL_RESOURCE_READ_STARTED",
  SKILL_SCRIPT_COMPLETED: "SKILL_SCRIPT_COMPLETED",
  SKILL_SCRIPT_FAILED: "SKILL_SCRIPT_FAILED",
  SKILL_SCRIPT_STARTED: "SKILL_SCRIPT_STARTED",
  SKILL_SCRIPT_TIMED_OUT: "SKILL_SCRIPT_TIMED_OUT",
  STATE_SNAPSHOT: "STATE_SNAPSHOT",
  STRUCTURED_OUTPUT_COMPLETE: "STRUCTURED_OUTPUT_COMPLETE",
  STRUCTURED_OUTPUT_ERROR: "STRUCTURED_OUTPUT_ERROR",
  STRUCTURED_OUTPUT_PARTIAL: "STRUCTURED_OUTPUT_PARTIAL",
  STRUCTURED_OUTPUT_START: "STRUCTURED_OUTPUT_START",
  SUBAGENT_CHILD_CONTROLLER_AUTHORITY: "SUBAGENT_CHILD_CONTROLLER_AUTHORITY",
  SUBAGENT_CHILD_DETACHED: "SUBAGENT_CHILD_DETACHED",
  SUBAGENT_CHILD_REGISTERED: "SUBAGENT_CHILD_REGISTERED",
  SUBAGENT_CHILD_REMAPPED: "SUBAGENT_CHILD_REMAPPED",
  SUBAGENT_CHILD_UNAVAILABLE: "SUBAGENT_CHILD_UNAVAILABLE",
  SUBAGENT_CONTEXT_RECEIVED: "SUBAGENT_CONTEXT_RECEIVED",
  SUBAGENT_CONTROLLER_GRANTED: "SUBAGENT_CONTROLLER_GRANTED",
  SUBAGENT_CREATION_ADVANCED: "SUBAGENT_CREATION_ADVANCED",
  SUBAGENT_CREATION_RESERVED: "SUBAGENT_CREATION_RESERVED",
  SUBAGENT_EXECUTION_CONTROLLER: "SUBAGENT_EXECUTION_CONTROLLER",
  SUBAGENT_INVOCATION_CANCELLED: "SUBAGENT_INVOCATION_CANCELLED",
  SUBAGENT_INVOCATION_COMPLETED: "SUBAGENT_INVOCATION_COMPLETED",
  SUBAGENT_INVOCATION_FAILED: "SUBAGENT_INVOCATION_FAILED",
  SUBAGENT_INVOCATION_STARTED: "SUBAGENT_INVOCATION_STARTED",
  SUBAGENT_QUESTION_RAISED: "SUBAGENT_QUESTION_RAISED",
  SUBAGENT_REGISTRY_SEED: "SUBAGENT_REGISTRY_SEED",
  SUBAGENT_RESULT_SUBMITTED: "SUBAGENT_RESULT_SUBMITTED",
  TEXT_DELTA: "TEXT_DELTA",
  TEXT_MESSAGE_END: "TEXT_MESSAGE_END",
  TEXT_MESSAGE_START: "TEXT_MESSAGE_START",
  THREAD_CREATED: "THREAD_CREATED",
  THREAD_EXECUTION_FINISHED: "THREAD_EXECUTION_FINISHED",
  THREAD_EXECUTION_STARTED: "THREAD_EXECUTION_STARTED",
  THREAD_FORK_OPERATION_CHANGED: "THREAD_FORK_OPERATION_CHANGED",
  THREAD_HISTORY_COMPACTION_CHECKPOINT: "THREAD_HISTORY_COMPACTION_CHECKPOINT",
  THREAD_MESSAGE_REPLACED: "THREAD_MESSAGE_REPLACED",
  THREAD_MIDDLEWARE_STATE_COMMITTED: "THREAD_MIDDLEWARE_STATE_COMMITTED",
  THREAD_UPDATED: "THREAD_UPDATED",
  TOOL_BODY_OPERATION_COMMITTED_FAILURE: "TOOL_BODY_OPERATION_COMMITTED_FAILURE",
  TOOL_CALL_ARGS: "TOOL_CALL_ARGS",
  TOOL_CALL_END: "TOOL_CALL_END",
  TOOL_CALL_RESULT: "TOOL_CALL_RESULT",
  TOOL_CALL_START: "TOOL_CALL_START",
  TOOL_OUTPUT_PROJECTED: "TOOL_OUTPUT_PROJECTED",
  TOOL_OUTPUT_PROJECTION_FAILED: "TOOL_OUTPUT_PROJECTION_FAILED",
  TOTAL_ERROR_THRESHOLD_EXCEEDED: "TOTAL_ERROR_THRESHOLD_EXCEEDED",
  USER_AUDIO_TRANSCRIPT_COMPLETED: "USER_AUDIO_TRANSCRIPT_COMPLETED",
  USER_AUDIO_TRANSCRIPT_DELTA: "USER_AUDIO_TRANSCRIPT_DELTA",
  USER_AUDIO_TRANSCRIPT_FAILED: "USER_AUDIO_TRANSCRIPT_FAILED",
  USER_MESSAGE: "USER_MESSAGE",
  USER_QUESTION_REQUEST: "USER_QUESTION_REQUEST",
} as const;
export type EventType = typeof EventTypes[keyof typeof EventTypes];
