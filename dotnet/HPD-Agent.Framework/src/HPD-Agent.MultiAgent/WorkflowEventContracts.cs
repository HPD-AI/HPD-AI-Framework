namespace HPD.MultiAgent;

/// <summary>Describes the outcome of attempting to approve or deny a workflow request.</summary>
public enum WorkflowApprovalStatus
{
    /// <summary>The active request accepted the response.</summary>
    Accepted,

    /// <summary>No active request with the supplied identity exists.</summary>
    NotFound,

    /// <summary>A response already resolved the request.</summary>
    AlreadyResolved,

    /// <summary>The request expired before the response arrived.</summary>
    TimedOut,

    /// <summary>The request was cancelled before the response arrived.</summary>
    Cancelled,

    /// <summary>The response type does not match the request's declared response type.</summary>
    ResponseTypeMismatch,

    /// <summary>The response target does not match the request target.</summary>
    TargetMismatch,

    /// <summary>The response cannot be associated with exactly one active request.</summary>
    AmbiguousRequest
}

/// <summary>Result returned for an approval or denial attempt.</summary>
public sealed record WorkflowApprovalResult(
    WorkflowApprovalStatus Status,
    string RequestId,
    string? Message = null)
{
    /// <summary>Gets whether the active request accepted the response.</summary>
    public bool Accepted => Status == WorkflowApprovalStatus.Accepted;
}
