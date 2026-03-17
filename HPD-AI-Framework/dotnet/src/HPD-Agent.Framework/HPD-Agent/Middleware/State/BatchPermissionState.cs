namespace HPD.Agent;

/// <summary>
/// State for batch permission tracking in parallel execution scenarios.
/// Stores permission decisions to avoid duplicate prompts for parallel tools.
/// </summary>
/// <remarks>
/// <para><b>Thread Safety:</b></para>
/// <para>
/// This state is immutable and flows through the context.
/// It is NOT stored in middleware instance fields, preserving thread safety
/// for concurrent RunAsync() calls.
/// </para>
///
/// <para><b>Usage:</b></para>
/// <code>
/// // Read state
/// var batchState = context.State.MiddlewareState.BatchPermission ?? new();
/// var isApproved = batchState.ApprovedFunctions.Contains(functionName);
///
/// // Update state
/// context.UpdateState(s => s with
/// {
///     MiddlewareState = s.MiddlewareState.WithBatchPermission(batchState.RecordApproval(functionName))
/// });
/// </code>
/// </remarks>
[MiddlewareState]
public sealed record BatchPermissionStateData
{

    /// <summary>
    /// Set of function names that have been approved in the current batch.
    /// Used to skip individual permission checks for already-approved functions.
    /// </summary>
    public HashSet<string> ApprovedFunctions { get; init; } = new();

    /// <summary>
    /// Set of function names that have been denied in the current batch.
    /// </summary>
    public HashSet<string> DeniedFunctions { get; init; } = new();

    /// <summary>
    /// Dictionary mapping denied function names to their denial reasons.
    /// </summary>
    public Dictionary<string, string> DenialReasons { get; init; } = new();

    /// <summary>
    /// Indicates whether batch permission check has been performed for this iteration.
    /// Reset at the start of each iteration.
    /// </summary>
    public bool BatchCheckPerformed { get; init; }

    /// <summary>
    /// Records an approved function in the batch.
    /// </summary>
    public BatchPermissionStateData RecordApproval(string functionName)
    {
        var newApproved = new HashSet<string>(ApprovedFunctions) { functionName };
        return this with { ApprovedFunctions = newApproved };
    }

    /// <summary>
    /// Records a denied function in the batch with its denial reason.
    /// </summary>
    public BatchPermissionStateData RecordDenial(string functionName, string reason)
    {
        var newDenied = new HashSet<string>(DeniedFunctions) { functionName };
        var newReasons = new Dictionary<string, string>(DenialReasons) { [functionName] = reason };
        return this with
        {
            DeniedFunctions = newDenied,
            DenialReasons = newReasons
        };
    }

    /// <summary>
    /// Marks batch check as performed.
    /// </summary>
    public BatchPermissionStateData MarkBatchCheckPerformed()
    {
        return this with { BatchCheckPerformed = true };
    }

    /// <summary>
    /// Resets the batch state for a new iteration.
    /// Called at the start of BeforeIterationAsync.
    /// </summary>
    public BatchPermissionStateData Reset()
    {
        return new BatchPermissionStateData();
    }
}
