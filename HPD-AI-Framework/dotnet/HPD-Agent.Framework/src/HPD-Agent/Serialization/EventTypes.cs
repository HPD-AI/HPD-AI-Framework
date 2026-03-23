namespace HPD.Agent.Serialization;

/// <summary>
/// SCREAMING_SNAKE_CASE constants for all agent event types.
/// Used as type discriminators in JSON serialization.
/// Organized hierarchically by category for better discoverability.
/// </summary>
/// <remarks>
/// These constants are used by AgentEventSerializer for type discrimination.
/// </remarks>
public static partial class EventTypes
{
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
        public const string STATE_SNAPSHOT = "STATE_SNAPSHOT";
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

    #region Permission Events

    /// <summary>
    /// Permission workflow events.
    /// </summary>
    public static class Permission
    {
        public const string PERMISSION_REQUEST = "PERMISSION_REQUEST";
        public const string PERMISSION_RESPONSE = "PERMISSION_RESPONSE";
        public const string PERMISSION_APPROVED = "PERMISSION_APPROVED";
        public const string PERMISSION_DENIED = "PERMISSION_DENIED";
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
        public const string HISTORY_REDUCTION = "HISTORY_REDUCTION";
        public const string MAX_CONSECUTIVE_ERRORS_EXCEEDED = "MAX_CONSECUTIVE_ERRORS_EXCEEDED";
        public const string TOTAL_ERROR_THRESHOLD_EXCEEDED = "TOTAL_ERROR_THRESHOLD_EXCEEDED";
        public const string PII_DETECTED = "PII_DETECTED";
    }

    #endregion

    #region Client Tool Events

    /// <summary>
    /// Client tool bidirectional events.
    /// </summary>
    public static class ClientTool
    {
        public const string CLIENT_TOOL_INVOKE_REQUEST = "CLIENT_TOOL_INVOKE_REQUEST";
        public const string CLIENT_TOOL_INVOKE_RESPONSE = "CLIENT_TOOL_INVOKE_RESPONSE";
        public const string CLIENT_TOOL_GROUPS_REGISTERED = "CLIENT_TOOL_GROUPS_REGISTERED";
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
        public const string HISTORY_REDUCTION_CACHE = "HISTORY_REDUCTION_CACHE";
        public const string CHECKPOINT = "CHECKPOINT";
        public const string INTERNAL_PARALLEL_TOOL_EXECUTION = "INTERNAL_PARALLEL_TOOL_EXECUTION";
        public const string INTERNAL_RETRY = "INTERNAL_RETRY";
        public const string FUNCTION_RETRY = "FUNCTION_RETRY";
        public const string MODEL_CALL_RETRY = "MODEL_CALL_RETRY";
        public const string DELTA_SENDING_ACTIVATED = "DELTA_SENDING_ACTIVATED";
        public const string PLAN_MODE_ACTIVATED = "PLAN_MODE_ACTIVATED";
        public const string PLAN_UPDATED = "PLAN_UPDATED";
        public const string NESTED_AGENT_INVOKED = "NESTED_AGENT_INVOKED";
        public const string DOCUMENT_PROCESSED = "DOCUMENT_PROCESSED";
        public const string INTERNAL_MESSAGE_PREPARED = "INTERNAL_MESSAGE_PREPARED";
        public const string BIDIRECTIONAL_EVENT_PROCESSED = "BIDIRECTIONAL_EVENT_PROCESSED";
        public const string AGENT_DECISION = "AGENT_DECISION";
        public const string AGENT_COMPLETION = "AGENT_COMPLETION";
        public const string ITERATION_MESSAGES = "ITERATION_MESSAGES";
        public const string SCHEMA_CHANGED = "SCHEMA_CHANGED";
        public const string COLLAPSING_STATE = "COLLAPSING_STATE";
        public const string EVENT_DROPPED = "EVENT_DROPPED";
        public const string BACKGROUND_OPERATION_STARTED = "BACKGROUND_OPERATION_STARTED";
        public const string BACKGROUND_OPERATION_STATUS = "BACKGROUND_OPERATION_STATUS";
        public const string STRUCTURED_OUTPUT_ERROR = "STRUCTURED_OUTPUT_ERROR";
        public const string STRUCTURED_OUTPUT_START = "STRUCTURED_OUTPUT_START";
        public const string STRUCTURED_OUTPUT_PARTIAL = "STRUCTURED_OUTPUT_PARTIAL";
        public const string STRUCTURED_OUTPUT_COMPLETE = "STRUCTURED_OUTPUT_COMPLETE";
        public const string ASSET_UPLOADED = "ASSET_UPLOADED";
        public const string ASSET_UPLOAD_FAILED = "ASSET_UPLOAD_FAILED";
    }

    #endregion

    #region Streaming Events

    /// <summary>
    /// Priority streaming and interruption events.
    /// </summary>
    public static class Streaming
    {
        public const string INTERRUPTION_REQUEST = "INTERRUPTION_REQUEST";
    }

    #endregion
}
