using System.Text.Json.Serialization.Metadata;
using HPD.Agent.Middleware;

namespace HPD.Agent.Serialization;

/// <summary>Source-generated immutable event module for contracts owned by HPD Agent core.</summary>
public static class CoreAgentEventModule
{
    /// <summary>Gets the core module fragment.</summary>
    public static AgentEventModuleFragment Fragment { get; } = new()
    {
        ModuleId = "hpd.agent.core",
        Events = Array.AsReadOnly<AgentEventDescriptor>(
        [
        Create(typeof(ThreadCreatedEvent), ThreadEventTypes.ThreadCreated),
        Create(typeof(ThreadUpdatedEvent), ThreadEventTypes.ThreadUpdated),
        Create(typeof(ContentAddedEvent), ThreadEventTypes.ContentAdded),
        Create(typeof(ThreadMiddlewareStateCommittedEvent), ThreadEventTypes.ThreadMiddlewareStateCommitted),
        Create(typeof(ThreadHistoryCompactionCheckpointEvent), ThreadEventTypes.ThreadHistoryCompactionCheckpoint),
        Create(typeof(MessageTurnStartedEvent), EventTypes.MessageTurn.MESSAGE_TURN_STARTED),
        Create(typeof(MessageTurnFinishedEvent), EventTypes.MessageTurn.MESSAGE_TURN_FINISHED),
        Create(typeof(MessageTurnErrorEvent), EventTypes.MessageTurn.MESSAGE_TURN_ERROR),
        Create(typeof(AgentTurnStartedEvent), EventTypes.AgentTurn.AGENT_TURN_STARTED),
        Create(typeof(AgentTurnFinishedEvent), EventTypes.AgentTurn.AGENT_TURN_FINISHED),
        Create(typeof(ProviderOperationUsageEvent), EventTypes.AgentTurn.PROVIDER_OPERATION_USAGE),
        Create(typeof(ProviderValuationObservationEvent), EventTypes.AgentTurn.PROVIDER_VALUATION_OBSERVATION),
        Create(typeof(StateSnapshotEvent), EventTypes.AgentTurn.STATE_SNAPSHOT),
        Create(typeof(ThreadExecutionStartedEvent), EventTypes.AgentTurn.THREAD_EXECUTION_STARTED),
        Create(typeof(ThreadExecutionFinishedEvent), EventTypes.AgentTurn.THREAD_EXECUTION_FINISHED),
        Create(typeof(SubAgentInvocationStartedEvent), EventTypes.AgentTurn.SUBAGENT_INVOCATION_STARTED),
        Create(typeof(SubAgentInvocationCompletedEvent), EventTypes.AgentTurn.SUBAGENT_INVOCATION_COMPLETED),
        Create(typeof(SubAgentInvocationFailedEvent), EventTypes.AgentTurn.SUBAGENT_INVOCATION_FAILED),
        Create(typeof(SubAgentInvocationCancelledEvent), EventTypes.AgentTurn.SUBAGENT_INVOCATION_CANCELLED),
        Create(typeof(TextMessageStartEvent), EventTypes.Content.TEXT_MESSAGE_START),
        Create(typeof(TextDeltaEvent), EventTypes.Content.TEXT_DELTA),
        Create(typeof(TextMessageEndEvent), EventTypes.Content.TEXT_MESSAGE_END),
        Create(typeof(ThreadMessageReplacedEvent), EventTypes.Content.THREAD_MESSAGE_REPLACED),
        Create(typeof(UserMessageEvent), EventTypes.Content.USER_MESSAGE),
        Create(typeof(UserAudioTranscriptDeltaEvent), EventTypes.Content.USER_AUDIO_TRANSCRIPT_DELTA),
        Create(typeof(UserAudioTranscriptCompletedEvent), EventTypes.Content.USER_AUDIO_TRANSCRIPT_COMPLETED),
        Create(typeof(UserAudioTranscriptFailedEvent), EventTypes.Content.USER_AUDIO_TRANSCRIPT_FAILED),
        Create(typeof(ReasoningMessageStartEvent), EventTypes.Reasoning.REASONING_MESSAGE_START),
        Create(typeof(ReasoningDeltaEvent), EventTypes.Reasoning.REASONING_DELTA),
        Create(typeof(ReasoningMessageEndEvent), EventTypes.Reasoning.REASONING_MESSAGE_END),
        Create(typeof(ToolCallStartEvent), EventTypes.Tool.TOOL_CALL_START),
        Create(typeof(ToolCallArgsEvent), EventTypes.Tool.TOOL_CALL_ARGS),
        Create(typeof(ToolCallEndEvent), EventTypes.Tool.TOOL_CALL_END),
        Create(typeof(ToolCallResultEvent), EventTypes.Tool.TOOL_CALL_RESULT),
        Create(typeof(AgentOperationRegisteredEvent), EventTypes.Operation.AGENT_OPERATION_REGISTERED),
        Create(typeof(AgentOperationTransitionedEvent), EventTypes.Operation.AGENT_OPERATION_TRANSITIONED),
        Create(typeof(AgentOperationNotificationQueuedEvent), EventTypes.Operation.AGENT_OPERATION_NOTIFICATION_QUEUED),
        Create(typeof(AgentOperationNotificationDeliveredEvent), EventTypes.Operation.AGENT_OPERATION_NOTIFICATION_DELIVERED),
        Create(typeof(AgentOperationNotificationSuppressedEvent), EventTypes.Operation.AGENT_OPERATION_NOTIFICATION_SUPPRESSED),
        Create(typeof(AgentOperationTombstonedEvent), EventTypes.Operation.AGENT_OPERATION_TOMBSTONED),
        Create(typeof(AgentOperationTombstoneEvictedEvent), EventTypes.Operation.AGENT_OPERATION_TOMBSTONE_EVICTED),
        Create(typeof(AgentCapabilityRefreshStartedEvent), EventTypes.Capability.AGENT_CAPABILITY_REFRESH_STARTED),
        Create(typeof(AgentCapabilityRefreshPublishedEvent), EventTypes.Capability.AGENT_CAPABILITY_REFRESH_PUBLISHED),
        Create(typeof(AgentCapabilityRefreshRejectedEvent), EventTypes.Capability.AGENT_CAPABILITY_REFRESH_REJECTED),
        Create(typeof(AgentTurnCapabilitiesPinnedEvent), EventTypes.Capability.AGENT_TURN_CAPABILITIES_PINNED),
        Create(typeof(SkillActivationStartedEvent), "SKILL_ACTIVATION_STARTED"),
        Create(typeof(SkillActivatedEvent), "SKILL_ACTIVATED"),
        Create(typeof(SkillActivationFailedEvent), "SKILL_ACTIVATION_FAILED"),
        Create(typeof(SkillResourceReadStartedEvent), "SKILL_RESOURCE_READ_STARTED"),
        Create(typeof(SkillResourceReadCompletedEvent), "SKILL_RESOURCE_READ_COMPLETED"),
        Create(typeof(SkillResourceReadFailedEvent), "SKILL_RESOURCE_READ_FAILED"),
        Create(typeof(SkillScriptStartedEvent), "SKILL_SCRIPT_STARTED"),
        Create(typeof(SkillScriptCompletedEvent), "SKILL_SCRIPT_COMPLETED"),
        Create(typeof(SkillScriptFailedEvent), "SKILL_SCRIPT_FAILED"),
        Create(typeof(SkillScriptTimedOutEvent), "SKILL_SCRIPT_TIMED_OUT"),
        Create(typeof(PermissionRequestEvent), EventTypes.Permission.PERMISSION_REQUEST),
        Create(typeof(PermissionResponseEvent), EventTypes.Permission.PERMISSION_RESPONSE),
        Create(typeof(ContinuationRequestEvent), EventTypes.Permission.CONTINUATION_REQUEST),
        Create(typeof(ContinuationResponseEvent), EventTypes.Permission.CONTINUATION_RESPONSE),
        Create(typeof(Security.AgentCapabilityRequestEvent), "AGENT_CAPABILITY_REQUEST"),
        Create(typeof(Security.AgentCapabilityResponseEvent), "AGENT_CAPABILITY_RESPONSE"),
        Create(typeof(ClarificationRequestEvent), EventTypes.Clarification.CLARIFICATION_REQUEST),
        Create(typeof(ClarificationResponseEvent), EventTypes.Clarification.CLARIFICATION_RESPONSE),
        Create(typeof(ClientTools.ClientToolInvokeRequestEvent), EventTypes.ClientTool.CLIENT_TOOL_INVOKE_REQUEST),
        Create(typeof(ClientTools.ClientToolInvokeOutcomeEvent), EventTypes.ClientTool.CLIENT_TOOL_INVOKE_OUTCOME),
        Create(typeof(AgentRequestTerminatedEvent), "AGENT_REQUEST_TERMINATED"),
        Create(typeof(MiddlewareErrorEvent), EventTypes.Middleware.MIDDLEWARE_ERROR),
        Create(typeof(CompactionEvent), EventTypes.Middleware.COMPACTION),
        Create(typeof(MaxConsecutiveErrorsExceededEvent), EventTypes.Middleware.MAX_CONSECUTIVE_ERRORS_EXCEEDED),
        Create(typeof(TotalErrorThresholdExceededEvent), EventTypes.Middleware.TOTAL_ERROR_THRESHOLD_EXCEEDED),
        Create(typeof(PIIDetectedEvent), EventTypes.Middleware.PII_DETECTED),
        Create(typeof(CollapsedToolsVisibleEvent), EventTypes.Observability.COLLAPSED_TOOLS_VISIBLE),
        Create(typeof(ContainerExpandedEvent), EventTypes.Observability.CONTAINER_EXPANDED),
        Create(typeof(PermissionCheckEvent), EventTypes.Observability.PERMISSION_CHECK),
        Create(typeof(IterationStartEvent), EventTypes.Observability.ITERATION_START),
        Create(typeof(CircuitBreakerTriggeredEvent), EventTypes.Observability.CIRCUIT_BREAKER_TRIGGERED),
        Create(typeof(InternalParallelToolExecutionEvent), EventTypes.Observability.INTERNAL_PARALLEL_TOOL_EXECUTION),
        Create(typeof(FunctionRetryEvent), EventTypes.Observability.FUNCTION_RETRY),
        Create(typeof(ModelCallRetryEvent), EventTypes.Observability.MODEL_CALL_RETRY),
        Create(typeof(DeltaSendingActivatedEvent), EventTypes.Observability.DELTA_SENDING_ACTIVATED),
        Create(typeof(PlanUpdatedEvent), EventTypes.Observability.PLAN_UPDATED),
        Create(typeof(NestedAgentInvokedEvent), EventTypes.Observability.NESTED_AGENT_INVOKED),
        Create(typeof(DocumentProcessedEvent), EventTypes.Observability.DOCUMENT_PROCESSED),
        Create(typeof(InternalMessagePreparedEvent), EventTypes.Observability.INTERNAL_MESSAGE_PREPARED),
        Create(typeof(RequestEventProcessedEvent), EventTypes.Observability.REQUEST_EVENT_PROCESSED),
        Create(typeof(AgentDecisionEvent), EventTypes.Observability.AGENT_DECISION),
        Create(typeof(AgentCompletionEvent), EventTypes.Observability.AGENT_COMPLETION),
        Create(typeof(IterationContextSnapshotEvent), EventTypes.Observability.ITERATION_CONTEXT_SNAPSHOT),
        Create(typeof(MiddlewareStateSnapshotEvent), EventTypes.Observability.MIDDLEWARE_STATE_SNAPSHOT),
        Create(typeof(MiddlewareStateChangedEvent), EventTypes.Observability.MIDDLEWARE_STATE_CHANGED),
        Create(typeof(CollapsingStateEvent), EventTypes.Observability.COLLAPSING_STATE),
        Create(typeof(EventDroppedEvent), EventTypes.Observability.EVENT_DROPPED),
        Create(typeof(StructuredOutputErrorEvent), EventTypes.Observability.STRUCTURED_OUTPUT_ERROR),
        Create(typeof(StructuredOutputStartEvent), EventTypes.Observability.STRUCTURED_OUTPUT_START),
        Create(typeof(StructuredOutputPartialEvent), EventTypes.Observability.STRUCTURED_OUTPUT_PARTIAL),
        Create(typeof(StructuredOutputCompleteEvent), EventTypes.Observability.STRUCTURED_OUTPUT_COMPLETE),
        Create(typeof(ContentUploadedEvent), EventTypes.Observability.CONTENT_UPLOADED),
        Create(typeof(ContentUploadFailedEvent), EventTypes.Observability.CONTENT_UPLOAD_FAILED),
        Create(typeof(HostedFileUploadedEvent), EventTypes.Observability.HOSTED_FILE_UPLOADED),
        Create(typeof(HostedFileUploadFailedEvent), EventTypes.Observability.HOSTED_FILE_UPLOAD_FAILED),
        Create(typeof(ContentReferenceResolvedEvent), EventTypes.Observability.CONTENT_REFERENCE_RESOLVED),
        Create(typeof(ContentReferenceResolutionFailedEvent), EventTypes.Observability.CONTENT_REFERENCE_RESOLUTION_FAILED),
        Create(typeof(InterruptionHandledEvent), EventTypes.Streaming.INTERRUPTION_HANDLED),
        ])
    };

    private static AgentEventDescriptor Create(Type eventType, string discriminator)
    {
        var typeInfo = HpdGeneratedAgentEventJsonContext_1c74a3cb93ce.Default.GetTypeInfo(eventType)
            ?? throw new InvalidOperationException($"Core event '{eventType.FullName}' has no source-generated JSON metadata.");
        return new AgentEventDescriptor
        {
            Discriminator = discriminator,
            EventType = eventType,
            JsonTypeInfo = typeInfo,
            Durability = AgentEventDurability.Durable,
            ModuleId = "hpd.agent.core"
        };
    }
}

/// <summary>Explicit composition for applications whose event surface is intentionally core-only.</summary>
public static class CoreAgentEventComposition
{
    /// <summary>Gets the immutable core-only composition.</summary>
    public static AgentEventComposition Instance { get; } =
        AgentEventComposition.Create([CoreAgentEventModule.Fragment]);
}
