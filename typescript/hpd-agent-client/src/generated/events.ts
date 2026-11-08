/* eslint-disable */
import type {
  ExecuteCommandAutoBackgroundedEvent,
  ExecuteCommandOutputChunkEvent,
  ExecuteCommandProcessExitedEvent,
  ExecuteCommandProcessStartedEvent,
  ExecuteCommandProgressEvent,
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
  HPDAgentEvaluationsIntegrationAnnotationRequestedEvent,
  HPDAgentEvaluationsIntegrationAnnotationResponseEvent,
  HPDAgentEvaluationsIntegrationEvalFailedEvent,
  HPDAgentEvaluationsIntegrationEvalPolicyViolationEvent,
  HPDAgentEvaluationsIntegrationEvalScoreEvent,
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
  HPDAgentIterationContextSnapshotEvent,
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
  HPDAgentSandboxHostProcessEventsLocalProcessInvocationCancelledEvent,
  HPDAgentSandboxHostProcessEventsLocalProcessInvocationCompletedEvent,
  HPDAgentSandboxHostProcessEventsLocalProcessInvocationFailedEvent,
  HPDAgentSandboxHostProcessEventsLocalProcessInvocationKilledEvent,
  HPDAgentSandboxHostProcessEventsLocalProcessInvocationStartedEvent,
  HPDAgentSandboxHostProcessEventsLocalProcessInvocationStartingEvent,
  HPDAgentSandboxHostProcessEventsLocalProcessInvocationTimedOutEvent,
  HPDAgentSandboxHostProcessEventsProcessIsolationInitializedEvent,
  HPDAgentSandboxHostProcessEventsProcessIsolationViolationEvent,
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
  HPDAgentToolHarnessCodingDebuggingDebugRunInTerminalRequestEvent,
  HPDAgentToolHarnessCodingDebuggingDebugRunInTerminalResponseEvent,
  HPDAgentToolOutputProjectedEvent,
  HPDAgentToolOutputProjectionFailedEvent,
  HPDAgentTotalErrorThresholdExceededEvent,
  HPDAgentUserAudioTranscriptCompletedEvent,
  HPDAgentUserAudioTranscriptDeltaEvent,
  HPDAgentUserAudioTranscriptFailedEvent,
  HPDAgentUserMessageEvent,
  HPDAgentUserMessagesInputEvent,
  HPDAgentUserQuestionRequestEvent,
  HPDOSToolHarnessesMiddlewareDebugBreakpointChangedEvent,
  HPDOSToolHarnessesMiddlewareDebugBreakpointSelectionAppliedEvent,
  HPDOSToolHarnessesMiddlewareDebugCapabilitiesChangedEvent,
  HPDOSToolHarnessesMiddlewareDebugChildSessionStartedEvent,
  HPDOSToolHarnessesMiddlewareDebugExecutionActivatingEvent,
  HPDOSToolHarnessesMiddlewareDebugExecutionActivationFailedEvent,
  HPDOSToolHarnessesMiddlewareDebugExecutionCommandAppliedEvent,
  HPDOSToolHarnessesMiddlewareDebugExecutionPlannedEvent,
  HPDOSToolHarnessesMiddlewareDebugHostProcessExitedEvent,
  HPDOSToolHarnessesMiddlewareDebugHostProcessStartedEvent,
  HPDOSToolHarnessesMiddlewareDebugHostReadyEvent,
  HPDOSToolHarnessesMiddlewareDebugLoadedSourceChangedEvent,
  HPDOSToolHarnessesMiddlewareDebugMemoryChangedEvent,
  HPDOSToolHarnessesMiddlewareDebugModuleChangedEvent,
  HPDOSToolHarnessesMiddlewareDebugOutputAvailableEvent,
  HPDOSToolHarnessesMiddlewareDebugOwnedResourceCleanupFailedEvent,
  HPDOSToolHarnessesMiddlewareDebugPrimaryStopAvailableEvent,
  HPDOSToolHarnessesMiddlewareDebugProcessChangedEvent,
  HPDOSToolHarnessesMiddlewareDebugProgressCompletedEvent,
  HPDOSToolHarnessesMiddlewareDebugProgressStartedEvent,
  HPDOSToolHarnessesMiddlewareDebugProgressUpdatedEvent,
  HPDOSToolHarnessesMiddlewareDebugRestartTransitionEvent,
  HPDOSToolHarnessesMiddlewareDebugSessionContinuedEvent,
  HPDOSToolHarnessesMiddlewareDebugSessionExitedEvent,
  HPDOSToolHarnessesMiddlewareDebugSessionFailedEvent,
  HPDOSToolHarnessesMiddlewareDebugSessionStateChangedEvent,
  HPDOSToolHarnessesMiddlewareDebugSessionStoppedEvent,
  HPDOSToolHarnessesMiddlewareDebugSessionTerminatedEvent,
  HPDOSToolHarnessesMiddlewareDebugStateInvalidatedEvent,
  HPDOSToolHarnessesMiddlewareDebugStateMutationAppliedEvent,
  HPDOSToolHarnessesMiddlewareDebugTerminalRecordEvictedEvent,
  HPDOSToolHarnessesMiddlewareDebugTerminalRecordRetainedEvent,
  HPDOSToolHarnessesMiddlewareDebugThreadChangedEvent,
  HPDOSToolHarnessesMiddlewareDebugTreeCompletedEvent,
  HPDOSToolHarnessesMiddlewareDebugTreeFaultedEvent,
  HPDOSToolHarnessesMiddlewareDebugTreeStartedEvent,
  HPDOSToolHarnessesMiddlewareDebugTreeTerminatedEvent,
  HPDOSToolHarnessesMiddlewareFileEditAppliedEvent,
  HPDOSToolHarnessesMiddlewareFileWriteAppliedEvent,
  HPDOSToolHarnessesMiddlewareLanguageServerDiagnosticsReceivedEvent,
  HPDOSToolHarnessesMiddlewareLanguageServerDocumentChangedEvent,
  HPDOSToolHarnessesMiddlewareLanguageServerDocumentClosedEvent,
  HPDOSToolHarnessesMiddlewareLanguageServerDocumentOpenedEvent,
  HPDOSToolHarnessesMiddlewareLanguageServerDocumentSavedEvent,
  HPDOSToolHarnessesMiddlewareLanguageServerStatusSnapshotEvent,
  HPDOSToolHarnessesMiddlewareLanguageServerWatchedFileChangedEvent
} from './models.js';

export type HpdAgentResponseInput =
  | HPDAgentClientToolsClientToolInvokeOutcomeEvent
  | HPDAgentContinuationResponseEvent
  | HPDAgentPermissionResponseEvent
  | HPDAgentQuestionResponseEvent;

export type HpdAgentEvent =
  | ExecuteCommandAutoBackgroundedEvent
  | ExecuteCommandOutputChunkEvent
  | ExecuteCommandProcessExitedEvent
  | ExecuteCommandProcessStartedEvent
  | ExecuteCommandProgressEvent
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
  | HPDAgentEvaluationsIntegrationAnnotationRequestedEvent
  | HPDAgentEvaluationsIntegrationAnnotationResponseEvent
  | HPDAgentEvaluationsIntegrationEvalFailedEvent
  | HPDAgentEvaluationsIntegrationEvalPolicyViolationEvent
  | HPDAgentEvaluationsIntegrationEvalScoreEvent
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
  | HPDAgentIterationContextSnapshotEvent
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
  | HPDAgentSandboxHostProcessEventsLocalProcessInvocationCancelledEvent
  | HPDAgentSandboxHostProcessEventsLocalProcessInvocationCompletedEvent
  | HPDAgentSandboxHostProcessEventsLocalProcessInvocationFailedEvent
  | HPDAgentSandboxHostProcessEventsLocalProcessInvocationKilledEvent
  | HPDAgentSandboxHostProcessEventsLocalProcessInvocationStartedEvent
  | HPDAgentSandboxHostProcessEventsLocalProcessInvocationStartingEvent
  | HPDAgentSandboxHostProcessEventsLocalProcessInvocationTimedOutEvent
  | HPDAgentSandboxHostProcessEventsProcessIsolationInitializedEvent
  | HPDAgentSandboxHostProcessEventsProcessIsolationViolationEvent
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
  | HPDAgentToolHarnessCodingDebuggingDebugRunInTerminalRequestEvent
  | HPDAgentToolHarnessCodingDebuggingDebugRunInTerminalResponseEvent
  | HPDAgentToolOutputProjectedEvent
  | HPDAgentToolOutputProjectionFailedEvent
  | HPDAgentTotalErrorThresholdExceededEvent
  | HPDAgentUserAudioTranscriptCompletedEvent
  | HPDAgentUserAudioTranscriptDeltaEvent
  | HPDAgentUserAudioTranscriptFailedEvent
  | HPDAgentUserMessageEvent
  | HPDAgentUserQuestionRequestEvent
  | HPDOSToolHarnessesMiddlewareDebugBreakpointChangedEvent
  | HPDOSToolHarnessesMiddlewareDebugBreakpointSelectionAppliedEvent
  | HPDOSToolHarnessesMiddlewareDebugCapabilitiesChangedEvent
  | HPDOSToolHarnessesMiddlewareDebugChildSessionStartedEvent
  | HPDOSToolHarnessesMiddlewareDebugExecutionActivatingEvent
  | HPDOSToolHarnessesMiddlewareDebugExecutionActivationFailedEvent
  | HPDOSToolHarnessesMiddlewareDebugExecutionCommandAppliedEvent
  | HPDOSToolHarnessesMiddlewareDebugExecutionPlannedEvent
  | HPDOSToolHarnessesMiddlewareDebugHostProcessExitedEvent
  | HPDOSToolHarnessesMiddlewareDebugHostProcessStartedEvent
  | HPDOSToolHarnessesMiddlewareDebugHostReadyEvent
  | HPDOSToolHarnessesMiddlewareDebugLoadedSourceChangedEvent
  | HPDOSToolHarnessesMiddlewareDebugMemoryChangedEvent
  | HPDOSToolHarnessesMiddlewareDebugModuleChangedEvent
  | HPDOSToolHarnessesMiddlewareDebugOutputAvailableEvent
  | HPDOSToolHarnessesMiddlewareDebugOwnedResourceCleanupFailedEvent
  | HPDOSToolHarnessesMiddlewareDebugPrimaryStopAvailableEvent
  | HPDOSToolHarnessesMiddlewareDebugProcessChangedEvent
  | HPDOSToolHarnessesMiddlewareDebugProgressCompletedEvent
  | HPDOSToolHarnessesMiddlewareDebugProgressStartedEvent
  | HPDOSToolHarnessesMiddlewareDebugProgressUpdatedEvent
  | HPDOSToolHarnessesMiddlewareDebugRestartTransitionEvent
  | HPDOSToolHarnessesMiddlewareDebugSessionContinuedEvent
  | HPDOSToolHarnessesMiddlewareDebugSessionExitedEvent
  | HPDOSToolHarnessesMiddlewareDebugSessionFailedEvent
  | HPDOSToolHarnessesMiddlewareDebugSessionStateChangedEvent
  | HPDOSToolHarnessesMiddlewareDebugSessionStoppedEvent
  | HPDOSToolHarnessesMiddlewareDebugSessionTerminatedEvent
  | HPDOSToolHarnessesMiddlewareDebugStateInvalidatedEvent
  | HPDOSToolHarnessesMiddlewareDebugStateMutationAppliedEvent
  | HPDOSToolHarnessesMiddlewareDebugTerminalRecordEvictedEvent
  | HPDOSToolHarnessesMiddlewareDebugTerminalRecordRetainedEvent
  | HPDOSToolHarnessesMiddlewareDebugThreadChangedEvent
  | HPDOSToolHarnessesMiddlewareDebugTreeCompletedEvent
  | HPDOSToolHarnessesMiddlewareDebugTreeFaultedEvent
  | HPDOSToolHarnessesMiddlewareDebugTreeStartedEvent
  | HPDOSToolHarnessesMiddlewareDebugTreeTerminatedEvent
  | HPDOSToolHarnessesMiddlewareFileEditAppliedEvent
  | HPDOSToolHarnessesMiddlewareFileWriteAppliedEvent
  | HPDOSToolHarnessesMiddlewareLanguageServerDiagnosticsReceivedEvent
  | HPDOSToolHarnessesMiddlewareLanguageServerDocumentChangedEvent
  | HPDOSToolHarnessesMiddlewareLanguageServerDocumentClosedEvent
  | HPDOSToolHarnessesMiddlewareLanguageServerDocumentOpenedEvent
  | HPDOSToolHarnessesMiddlewareLanguageServerDocumentSavedEvent
  | HPDOSToolHarnessesMiddlewareLanguageServerStatusSnapshotEvent
  | HPDOSToolHarnessesMiddlewareLanguageServerWatchedFileChangedEvent;

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
  ANNOTATION_REQUESTED: "ANNOTATION_REQUESTED",
  ANNOTATION_RESPONSE: "ANNOTATION_RESPONSE",
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
  DEBUG_BREAKPOINT_CHANGED: "DEBUG_BREAKPOINT_CHANGED",
  DEBUG_BREAKPOINT_SELECTION_APPLIED: "DEBUG_BREAKPOINT_SELECTION_APPLIED",
  DEBUG_CAPABILITIES_CHANGED: "DEBUG_CAPABILITIES_CHANGED",
  DEBUG_CHILD_SESSION_STARTED: "DEBUG_CHILD_SESSION_STARTED",
  DEBUG_EXECUTION_ACTIVATING: "DEBUG_EXECUTION_ACTIVATING",
  DEBUG_EXECUTION_ACTIVATION_FAILED: "DEBUG_EXECUTION_ACTIVATION_FAILED",
  DEBUG_EXECUTION_COMMAND_APPLIED: "DEBUG_EXECUTION_COMMAND_APPLIED",
  DEBUG_EXECUTION_PLANNED: "DEBUG_EXECUTION_PLANNED",
  DEBUG_HOST_PROCESS_EXITED: "DEBUG_HOST_PROCESS_EXITED",
  DEBUG_HOST_PROCESS_STARTED: "DEBUG_HOST_PROCESS_STARTED",
  DEBUG_HOST_READY: "DEBUG_HOST_READY",
  DEBUG_LOADED_SOURCE_CHANGED: "DEBUG_LOADED_SOURCE_CHANGED",
  DEBUG_MEMORY_CHANGED: "DEBUG_MEMORY_CHANGED",
  DEBUG_MODULE_CHANGED: "DEBUG_MODULE_CHANGED",
  DEBUG_OUTPUT_AVAILABLE: "DEBUG_OUTPUT_AVAILABLE",
  DEBUG_OWNED_RESOURCE_CLEANUP_FAILED: "DEBUG_OWNED_RESOURCE_CLEANUP_FAILED",
  DEBUG_PRIMARY_STOP_AVAILABLE: "DEBUG_PRIMARY_STOP_AVAILABLE",
  DEBUG_PROCESS_CHANGED: "DEBUG_PROCESS_CHANGED",
  DEBUG_PROGRESS_COMPLETED: "DEBUG_PROGRESS_COMPLETED",
  DEBUG_PROGRESS_STARTED: "DEBUG_PROGRESS_STARTED",
  DEBUG_PROGRESS_UPDATED: "DEBUG_PROGRESS_UPDATED",
  DEBUG_RESTART_TRANSITION: "DEBUG_RESTART_TRANSITION",
  DEBUG_RUN_IN_TERMINAL_REQUEST: "DEBUG_RUN_IN_TERMINAL_REQUEST",
  DEBUG_RUN_IN_TERMINAL_RESPONSE: "DEBUG_RUN_IN_TERMINAL_RESPONSE",
  DEBUG_SESSION_CONTINUED: "DEBUG_SESSION_CONTINUED",
  DEBUG_SESSION_EXITED: "DEBUG_SESSION_EXITED",
  DEBUG_SESSION_FAILED: "DEBUG_SESSION_FAILED",
  DEBUG_SESSION_STATE_CHANGED: "DEBUG_SESSION_STATE_CHANGED",
  DEBUG_SESSION_STOPPED: "DEBUG_SESSION_STOPPED",
  DEBUG_SESSION_TERMINATED: "DEBUG_SESSION_TERMINATED",
  DEBUG_STATE_INVALIDATED: "DEBUG_STATE_INVALIDATED",
  DEBUG_STATE_MUTATION_APPLIED: "DEBUG_STATE_MUTATION_APPLIED",
  DEBUG_TERMINAL_RECORD_EVICTED: "DEBUG_TERMINAL_RECORD_EVICTED",
  DEBUG_TERMINAL_RECORD_RETAINED: "DEBUG_TERMINAL_RECORD_RETAINED",
  DEBUG_THREAD_CHANGED: "DEBUG_THREAD_CHANGED",
  DEBUG_TREE_COMPLETED: "DEBUG_TREE_COMPLETED",
  DEBUG_TREE_FAULTED: "DEBUG_TREE_FAULTED",
  DEBUG_TREE_STARTED: "DEBUG_TREE_STARTED",
  DEBUG_TREE_TERMINATED: "DEBUG_TREE_TERMINATED",
  DELTA_SENDING_ACTIVATED: "DELTA_SENDING_ACTIVATED",
  DOCUMENT_PROCESSED: "DOCUMENT_PROCESSED",
  EVAL_FAILED: "EVAL_FAILED",
  EVAL_POLICY_VIOLATION: "EVAL_POLICY_VIOLATION",
  EVAL_SCORE: "EVAL_SCORE",
  EVENT_DROPPED: "EVENT_DROPPED",
  EXECUTE_COMMAND_AUTO_BACKGROUNDED: "EXECUTE_COMMAND_AUTO_BACKGROUNDED",
  EXECUTE_COMMAND_OUTPUT_CHUNK: "EXECUTE_COMMAND_OUTPUT_CHUNK",
  EXECUTE_COMMAND_PROCESS_EXITED: "EXECUTE_COMMAND_PROCESS_EXITED",
  EXECUTE_COMMAND_PROCESS_STARTED: "EXECUTE_COMMAND_PROCESS_STARTED",
  EXECUTE_COMMAND_PROGRESS: "EXECUTE_COMMAND_PROGRESS",
  FILE_EDIT_APPLIED: "FILE_EDIT_APPLIED",
  FILE_WRITE_APPLIED: "FILE_WRITE_APPLIED",
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
  ITERATION_CONTEXT_SNAPSHOT: "ITERATION_CONTEXT_SNAPSHOT",
  ITERATION_START: "ITERATION_START",
  LANGUAGE_SERVER_DIAGNOSTICS_RECEIVED: "LANGUAGE_SERVER_DIAGNOSTICS_RECEIVED",
  LANGUAGE_SERVER_DOCUMENT_CHANGED: "LANGUAGE_SERVER_DOCUMENT_CHANGED",
  LANGUAGE_SERVER_DOCUMENT_CLOSED: "LANGUAGE_SERVER_DOCUMENT_CLOSED",
  LANGUAGE_SERVER_DOCUMENT_OPENED: "LANGUAGE_SERVER_DOCUMENT_OPENED",
  LANGUAGE_SERVER_DOCUMENT_SAVED: "LANGUAGE_SERVER_DOCUMENT_SAVED",
  LANGUAGE_SERVER_STATUS_SNAPSHOT: "LANGUAGE_SERVER_STATUS_SNAPSHOT",
  LANGUAGE_SERVER_WATCHED_FILE_CHANGED: "LANGUAGE_SERVER_WATCHED_FILE_CHANGED",
  LOCAL_PROCESS_INVOCATION_CANCELLED: "LOCAL_PROCESS_INVOCATION_CANCELLED",
  LOCAL_PROCESS_INVOCATION_COMPLETED: "LOCAL_PROCESS_INVOCATION_COMPLETED",
  LOCAL_PROCESS_INVOCATION_FAILED: "LOCAL_PROCESS_INVOCATION_FAILED",
  LOCAL_PROCESS_INVOCATION_KILLED: "LOCAL_PROCESS_INVOCATION_KILLED",
  LOCAL_PROCESS_INVOCATION_STARTED: "LOCAL_PROCESS_INVOCATION_STARTED",
  LOCAL_PROCESS_INVOCATION_STARTING: "LOCAL_PROCESS_INVOCATION_STARTING",
  LOCAL_PROCESS_INVOCATION_TIMED_OUT: "LOCAL_PROCESS_INVOCATION_TIMED_OUT",
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
  PROCESS_ISOLATION_INITIALIZED: "PROCESS_ISOLATION_INITIALIZED",
  PROCESS_ISOLATION_VIOLATION: "PROCESS_ISOLATION_VIOLATION",
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
  SUBAGENT_INVOCATION_CANCELLED: "SUBAGENT_INVOCATION_CANCELLED",
  SUBAGENT_INVOCATION_COMPLETED: "SUBAGENT_INVOCATION_COMPLETED",
  SUBAGENT_INVOCATION_FAILED: "SUBAGENT_INVOCATION_FAILED",
  SUBAGENT_INVOCATION_STARTED: "SUBAGENT_INVOCATION_STARTED",
  SUBAGENT_REGISTRY_SEED: "SUBAGENT_REGISTRY_SEED",
  SUB_AGENT_EXECUTION_CONTROLLER: "SUB_AGENT_EXECUTION_CONTROLLER",
  SUB_AGENT_QUESTION_RAISED: "SUB_AGENT_QUESTION_RAISED",
  SUB_AGENT_RESULT_SUBMITTED: "SUB_AGENT_RESULT_SUBMITTED",
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
