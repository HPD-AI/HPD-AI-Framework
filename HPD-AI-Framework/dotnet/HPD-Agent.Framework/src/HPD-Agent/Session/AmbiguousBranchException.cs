namespace HPD.Agent;

/// <summary>
/// Thrown when branchId is not specified but the session has multiple branches.
/// Once a session has been forked (via ForkBranchAsync), the caller must specify
/// branchId explicitly to avoid accidentally writing to the wrong branch.
/// </summary>
public class AmbiguousBranchException : InvalidOperationException
{
    /// <summary>The session that has multiple branches.</summary>
    public string SessionId { get; }

    /// <summary>The branch IDs available in the session.</summary>
    public List<string> AvailableBranches { get; }

    public AmbiguousBranchException(string sessionId, List<string> branches)
        : base($"Session '{sessionId}' has {branches.Count} branches ({string.Join(", ", branches)}). " +
               $"Specify branchId explicitly when a session has multiple branches.")
    {
        SessionId = sessionId;
        AvailableBranches = branches;
    }
}
