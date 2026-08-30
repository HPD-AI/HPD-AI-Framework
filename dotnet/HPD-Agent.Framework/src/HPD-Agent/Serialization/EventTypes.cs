namespace HPD.Agent.Serialization;

/// <summary>
/// SCREAMING_SNAKE_CASE constants for all agent event types.
/// Used as type discriminators in JSON serialization.
/// Organized hierarchically by category for better discoverability.
/// </summary>
/// <remarks>
/// These constants define stable wire discriminators used by generated event and input codecs.
/// </remarks>
public static partial class EventTypes
{
    #region Input Events

    /// <summary>
    /// Events accepted as agent input.
    /// </summary>
    public static class Input
    {
        public const string USER_MESSAGES_INPUT = "USER_MESSAGES_INPUT";
        public const string AUDIO_SESSION_INPUT = "AUDIO_SESSION_INPUT";
        public const string COMPACT_THREAD_INPUT = "COMPACT_THREAD_INPUT";
        public const string AGENT_OPERATION_NOTIFICATION_INPUT = "AGENT_OPERATION_NOTIFICATION_INPUT";
    }

    #endregion

    #region Message Turn Events

    /// <summary>
    /// Message turn lifecycle events (entire user interaction).
    /// </summary>
    public static class MessageTurn
    {
        public const string MESSAGE_TURN_STARTED = "MESSAGE_TURN_STARTED";
        public const string MESSAGE_TURN_FINISHED = "MESSAGE_TURN_FINISHED";
        public const string MESSAGE_TURN_ERROR = "MESSAGE_TURN_ERROR";
    }

    #endregion

    #region Agent Turn Events

    /// <summary>
    /// Agent turn lifecycle events (single LLM call within message turn).
    /// </summary>
    public static class AgentTurn
    {
        public const string AGENT_TURN_STARTED = "AGENT_TURN_STARTED";
        public const string AGENT_TURN_FINISHED = "AGENT_TURN_FINISHED";
        public const string PROVIDER_OPERATION_USAGE = "PROVIDER_OPERATION_USAGE";
        public const string PROVIDER_VALUATION_OBSERVATION = "PROVIDER_VALUATION_OBSERVATION";
        public const string STATE_SNAPSHOT = "STATE_SNAPSHOT";
        public const string THREAD_EXECUTION_STARTED = "THREAD_EXECUTION_STARTED";
        public const string THREAD_EXECUTION_FINISHED = "THREAD_EXECUTION_FINISHED";
        public const string SUBAGENT_INVOCATION_STARTED = "SUBAGENT_INVOCATION_STARTED";
        public const string SUBAGENT_INVOCATION_COMPLETED = "SUBAGENT_INVOCATION_COMPLETED";
        public const string SUBAGENT_INVOCATION_FAILED = "SUBAGENT_INVOCATION_FAILED";
        public const string SUBAGENT_INVOCATION_CANCELLED = "SUBAGENT_INVOCATION_CANCELLED";
    }

    #endregion

    #region Content Events

    /// <summary>
    /// Text content streaming events.
    /// </summary>
    public static class Content
    {
        public const string TEXT_MESSAGE_START = "TEXT_MESSAGE_START";
        public const string TEXT_DELTA = "TEXT_DELTA";
        public const string TEXT_MESSAGE_END = "TEXT_MESSAGE_END";
        public const string THREAD_MESSAGE_REPLACED = "THREAD_MESSAGE_REPLACED";
        public const string USER_MESSAGE = "USER_MESSAGE";
        public const string USER_AUDIO_TRANSCRIPT_DELTA = "USER_AUDIO_TRANSCRIPT_DELTA";
        public const string USER_AUDIO_TRANSCRIPT_COMPLETED = "USER_AUDIO_TRANSCRIPT_COMPLETED";
        public const string USER_AUDIO_TRANSCRIPT_FAILED = "USER_AUDIO_TRANSCRIPT_FAILED";
    }

    #endregion

    #region Reasoning Events

    /// <summary>
    /// Reasoning events for models like o1, DeepSeek-R1.
    /// </summary>
    public static class Reasoning
    {
        public const string REASONING_MESSAGE_START = "REASONING_MESSAGE_START";
        public const string REASONING_DELTA = "REASONING_DELTA";
        public const string REASONING_MESSAGE_END = "REASONING_MESSAGE_END";
    }

    #endregion

    #region Tool Events

    /// <summary>
    /// Tool execution lifecycle events.
    /// </summary>
    public static class Tool
    {
        public const string TOOL_CALL_START = "TOOL_CALL_START";
        public const string TOOL_CALL_ARGS = "TOOL_CALL_ARGS";
        public const string TOOL_CALL_END = "TOOL_CALL_END";
        public const string TOOL_CALL_RESULT = "TOOL_CALL_RESULT";
    }

    #endregion

    #region Operation Events

    /// <summary>Unified operation registration and transition events.</summary>
    public static class Operation
    {
        /// <summary>The canonical operation-registration discriminator.</summary>
        public const string AGENT_OPERATION_REGISTERED = "AGENT_OPERATION_REGISTERED";
        /// <summary>The canonical operation-transition discriminator.</summary>
        public const string AGENT_OPERATION_TRANSITIONED = "AGENT_OPERATION_TRANSITIONED";
        public const string AGENT_OPERATION_NOTIFICATION_QUEUED = "AGENT_OPERATION_NOTIFICATION_QUEUED";
        public const string AGENT_OPERATION_NOTIFICATION_DELIVERED = "AGENT_OPERATION_NOTIFICATION_DELIVERED";
        public const string AGENT_OPERATION_NOTIFICATION_SUPPRESSED = "AGENT_OPERATION_NOTIFICATION_SUPPRESSED";
        public const string AGENT_OPERATION_TOMBSTONED = "AGENT_OPERATION_TOMBSTONED";
        public const string AGENT_OPERATION_TOMBSTONE_EVICTED = "AGENT_OPERATION_TOMBSTONE_EVICTED";
    }

    /// <summary>Complete capability-catalog refresh events.</summary>
    public static class Capability
    {
        /// <summary>The refresh-start discriminator.</summary>
        public const string AGENT_CAPABILITY_REFRESH_STARTED = "AGENT_CAPABILITY_REFRESH_STARTED";
        /// <summary>The successful-publication discriminator.</summary>
        public const string AGENT_CAPABILITY_REFRESH_PUBLISHED = "AGENT_CAPABILITY_REFRESH_PUBLISHED";
        /// <summary>The rejected-candidate discriminator.</summary>
        public const string AGENT_CAPABILITY_REFRESH_REJECTED = "AGENT_CAPABILITY_REFRESH_REJECTED";
        /// <summary>An immutable effective turn capability surface was pinned.</summary>
        public const string AGENT_TURN_CAPABILITIES_PINNED = "AGENT_TURN_CAPABILITIES_PINNED";
    }

    #endregion

    #region Permission Events

    /// <summary>
    /// Permission workflow events.
    /// </summary>
    public static class Permission
    {
        public const string PERMISSION_REQUEST = "PERMISSION_REQUEST";
        public const string PERMISSION_RESPONSE = "PERMISSION_RESPONSE";
        public const string CONTINUATION_REQUEST = "CONTINUATION_REQUEST";
        public const string CONTINUATION_RESPONSE = "CONTINUATION_RESPONSE";
    }

    #endregion

    #region Clarification Events

    /// <summary>
    /// Human-in-the-loop clarification events.
    /// </summary>
    public static class Clarification
    {
        public const string CLARIFICATION_REQUEST = "CLARIFICATION_REQUEST";
        public const string CLARIFICATION_RESPONSE = "CLARIFICATION_RESPONSE";
    }

    #endregion

    #region Middleware Events

    /// <summary>
    /// Middleware error events.
    /// </summary>
    public static class Middleware
    {
        public const string MIDDLEWARE_ERROR = "MIDDLEWARE_ERROR";
        public const string COMPACTION = "COMPACTION";
        public const string MAX_CONSECUTIVE_ERRORS_EXCEEDED = "MAX_CONSECUTIVE_ERRORS_EXCEEDED";
        public const string TOTAL_ERROR_THRESHOLD_EXCEEDED = "TOTAL_ERROR_THRESHOLD_EXCEEDED";
        public const string PII_DETECTED = "PII_DETECTED";
    }

    #endregion

    #region Client Tool Events

    /// <summary>
    /// Client tool request/response events.
    /// </summary>
    public static class ClientTool
    {
        public const string CLIENT_TOOL_INVOKE_REQUEST = "CLIENT_TOOL_INVOKE_REQUEST";
        public const string CLIENT_TOOL_INVOKE_OUTCOME = "CLIENT_TOOL_INVOKE_OUTCOME";
        public const string CLIENT_TOOL_BACKGROUND_OPERATION_OUTCOME = "CLIENT_TOOL_BACKGROUND_OPERATION_OUTCOME";
    }

    #endregion

    #region Observability Events

    /// <summary>
    /// Observability and diagnostic events.
    /// </summary>
    public static class Observability
    {
        public const string COLLAPSED_TOOLS_VISIBLE = "COLLAPSED_TOOLS_VISIBLE";
        public const string CONTAINER_EXPANDED = "CONTAINER_EXPANDED";
        public const string PERMISSION_CHECK = "PERMISSION_CHECK";
        public const string ITERATION_START = "ITERATION_START";
        public const string CIRCUIT_BREAKER_TRIGGERED = "CIRCUIT_BREAKER_TRIGGERED";
        public const string INTERNAL_PARALLEL_TOOL_EXECUTION = "INTERNAL_PARALLEL_TOOL_EXECUTION";
        public const string FUNCTION_RETRY = "FUNCTION_RETRY";
        public const string MODEL_CALL_RETRY = "MODEL_CALL_RETRY";
        public const string DELTA_SENDING_ACTIVATED = "DELTA_SENDING_ACTIVATED";
        public const string PLAN_UPDATED = "PLAN_UPDATED";
        public const string NESTED_AGENT_INVOKED = "NESTED_AGENT_INVOKED";
        public const string DOCUMENT_PROCESSED = "DOCUMENT_PROCESSED";
        public const string INTERNAL_MESSAGE_PREPARED = "INTERNAL_MESSAGE_PREPARED";
        public const string REQUEST_EVENT_PROCESSED = "REQUEST_EVENT_PROCESSED";
        public const string AGENT_DECISION = "AGENT_DECISION";
        public const string AGENT_COMPLETION = "AGENT_COMPLETION";
        public const string ITERATION_CONTEXT_SNAPSHOT = "ITERATION_CONTEXT_SNAPSHOT";
        public const string MIDDLEWARE_STATE_SNAPSHOT = "MIDDLEWARE_STATE_SNAPSHOT";
        public const string MIDDLEWARE_STATE_CHANGED = "MIDDLEWARE_STATE_CHANGED";
        public const string COLLAPSING_STATE = "COLLAPSING_STATE";
        public const string EVENT_DROPPED = "EVENT_DROPPED";
        public const string MODEL_BACKGROUND_OPERATION_STARTED = "MODEL_BACKGROUND_OPERATION_STARTED";
        public const string MODEL_BACKGROUND_OPERATION_STATUS = "MODEL_BACKGROUND_OPERATION_STATUS";
        public const string STRUCTURED_OUTPUT_ERROR = "STRUCTURED_OUTPUT_ERROR";
        public const string STRUCTURED_OUTPUT_START = "STRUCTURED_OUTPUT_START";
        public const string STRUCTURED_OUTPUT_PARTIAL = "STRUCTURED_OUTPUT_PARTIAL";
        public const string STRUCTURED_OUTPUT_COMPLETE = "STRUCTURED_OUTPUT_COMPLETE";
        public const string CONTENT_UPLOADED = "CONTENT_UPLOADED";
        public const string CONTENT_UPLOAD_FAILED = "CONTENT_UPLOAD_FAILED";
        public const string HOSTED_FILE_UPLOADED = "HOSTED_FILE_UPLOADED";
        public const string HOSTED_FILE_UPLOAD_FAILED = "HOSTED_FILE_UPLOAD_FAILED";
        public const string CONTENT_REFERENCE_RESOLVED = "CONTENT_REFERENCE_RESOLVED";
        public const string CONTENT_REFERENCE_RESOLUTION_FAILED = "CONTENT_REFERENCE_RESOLUTION_FAILED";
    }

    #endregion

    #region Streaming Events

    /// <summary>
    /// Priority streaming and interruption events.
    /// </summary>
    public static class Streaming
    {
        public const string INTERRUPTION_HANDLED = "INTERRUPTION_HANDLED";
    }

    #endregion
}
