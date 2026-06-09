namespace HPDAgent.Graph.Abstractions.Execution;

/// <summary>
/// Represents all possible outcomes of a node execution.
/// C# discriminated union via sealed record hierarchy.
/// Universal primitive for ANY workflow (not specific to any domain).
/// </summary>
public abstract record NodeExecutionResult
{
    /// <summary>
    /// Node executed successfully and produced outputs.
    /// Port-based output routing (all nodes use ports, single-output = port 0).
    /// </summary>
    public sealed record Success(
        IReadOnlyDictionary<int, Dictionary<string, object>> PortOutputs,
        TimeSpan Duration,
        NodeExecutionMetadata Metadata
    ) : NodeExecutionResult
    {
        /// <summary>
        /// Convenience factory for single-output nodes (port 0).
        /// Most common case: single output.
        /// </summary>
        public static Success Single(
            Dictionary<string, object> output,
            TimeSpan duration,
            NodeExecutionMetadata metadata)
            => new Success(
                PortOutputs: new Dictionary<int, Dictionary<string, object>> { [0] = output },
                Duration: duration,
                Metadata: metadata
            );

        /// <summary>
        /// Convenience factory for multi-port output.
        /// Builder pattern for complex routing.
        /// </summary>
        public static Success WithPorts(
            PortOutputs ports,
            TimeSpan duration,
            NodeExecutionMetadata metadata)
            => new Success(
                PortOutputs: ports.Build(),
                Duration: duration,
                Metadata: metadata
            );
    }

    /// <summary>
    /// Node failed with an error.
    /// </summary>
    public sealed record Failure(
        Exception Exception,
        ErrorSeverity Severity,
        bool IsTransient,
        TimeSpan Duration,
        string? ErrorCode = null,
        Dictionary<string, object>? PartialOutputs = null,
        NodeExecutionMetadata? Metadata = null
    ) : NodeExecutionResult;

    /// <summary>
    /// Node was skipped (dependency failed, condition not met, etc.).
    /// </summary>
    public sealed record Skipped(
        SkipReason Reason,
        string? Message = null,
        string? UpstreamFailedNode = null
    ) : NodeExecutionResult;

    /// <summary>
    /// Node execution suspended for external input or polling.
    /// Use factory methods for type-safe construction.
    /// </summary>
    public sealed record Suspended : NodeExecutionResult
    {
        public required string SuspendToken { get; init; }
        public required SuspendReason Reason { get; init; }
        public object? ResumeValue { get; init; }
        public string? Message { get; init; }
        public TimeSpan? RetryAfter { get; init; }
        public TimeSpan? MaxWaitTime { get; init; }
        public int? MaxRetries { get; init; }

        /// <summary>
        /// Create a suspension for polling (sensor pattern).
        /// </summary>
        public static Suspended ForPolling(
            string suspendToken,
            TimeSpan retryAfter,
            TimeSpan? maxWaitTime = null,
            int? maxRetries = null,
            string? message = null)
        {
            return new Suspended
            {
                SuspendToken = suspendToken,
                Reason = SuspendReason.PollingCondition,
                RetryAfter = retryAfter,
                MaxWaitTime = maxWaitTime,
                MaxRetries = maxRetries,
                Message = message
            };
        }

        /// <summary>
        /// Create a suspension for human approval (HITL pattern).
        /// </summary>
        public static Suspended ForHumanApproval(
            string suspendToken,
            object? resumeValue = null,
            string? message = null)
        {
            return new Suspended
            {
                SuspendToken = suspendToken,
                Reason = SuspendReason.HumanApproval,
                ResumeValue = resumeValue,
                Message = message
            };
        }

        /// <summary>
        /// Create a suspension for external task wait (webhook, callback).
        /// </summary>
        public static Suspended ForExternalTask(
            string suspendToken,
            TimeSpan? maxWaitTime = null,
            string? message = null)
        {
            return new Suspended
            {
                SuspendToken = suspendToken,
                Reason = SuspendReason.ExternalTaskWait,
                MaxWaitTime = maxWaitTime,
                Message = message
            };
        }

        /// <summary>
        /// Create a suspension for resource availability wait.
        /// </summary>
        public static Suspended ForResourceWait(
            string suspendToken,
            TimeSpan retryAfter,
            TimeSpan? maxWaitTime = null,
            int? maxRetries = null,
            string? message = null)
        {
            return new Suspended
            {
                SuspendToken = suspendToken,
                Reason = SuspendReason.ResourceWait,
                RetryAfter = retryAfter,
                MaxWaitTime = maxWaitTime,
                MaxRetries = maxRetries,
                Message = message
            };
        }

        /// <summary>
        /// Create a general suspension (Phase 4: Temporal Operators).
        /// Used for schedule-based suspension and other custom scenarios.
        /// </summary>
        public static Suspended Create(
            string suspendToken,
            SuspendReason reason,
            TimeSpan? retryAfter = null,
            object? resumeValue = null,
            string? message = null,
            TimeSpan? maxWaitTime = null,
            int? maxRetries = null)
        {
            return new Suspended
            {
                SuspendToken = suspendToken,
                Reason = reason,
                RetryAfter = retryAfter,
                ResumeValue = resumeValue,
                Message = message,
                MaxWaitTime = maxWaitTime,
                MaxRetries = maxRetries
            };
        }

    }

    /// <summary>
    /// Node was cancelled before or during execution.
    /// </summary>
    public sealed record Cancelled(
        CancellationReason Reason,
        string? Message = null
    ) : NodeExecutionResult;

    // Prevent external inheritance - only the nested records above
    private NodeExecutionResult() { }
}
