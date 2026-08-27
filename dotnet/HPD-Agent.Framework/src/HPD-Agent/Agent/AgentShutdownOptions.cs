namespace HPD.Agent;

/// <summary>Controls how remote provider operations behave during agent shutdown.</summary>
public enum AgentRemoteOperationShutdownPolicy
{
    /// <summary>Detach local observation and preserve durable recovery references.</summary>
    DetachObservation,
    /// <summary>Request provider cancellation before detaching unresolved observation.</summary>
    RequestCancellation
}

/// <summary>Controls how resources pinned by leaked capability leases are handled.</summary>
public enum AgentLeaseLeakPolicy
{
    /// <summary>Report leaks and leave resources for process-level cleanup.</summary>
    ReportAndAbandonResources,
    /// <summary>Fault outstanding turns, wait for release, and then dispose resources.</summary>
    ReportAndForceDispose
}

/// <summary>Configures bounded, dependency-ordered asynchronous agent shutdown.</summary>
public sealed record AgentShutdownOptions
{
    /// <summary>Gets the initial deadline for accepted work and leases to drain.</summary>
    public TimeSpan GracefulDrainTimeout { get; init; } = TimeSpan.FromSeconds(30);
    /// <summary>Gets the deadline after locally owned work receives cancellation.</summary>
    public TimeSpan CancellationDrainTimeout { get; init; } = TimeSpan.FromSeconds(10);
    /// <summary>Gets the policy for non-terminal remote provider work.</summary>
    public AgentRemoteOperationShutdownPolicy RemoteOperations { get; init; } =
        AgentRemoteOperationShutdownPolicy.DetachObservation;
    /// <summary>Gets the policy for leases still held after both drain deadlines.</summary>
    public AgentLeaseLeakPolicy LeaseLeaks { get; init; } =
        AgentLeaseLeakPolicy.ReportAndAbandonResources;

    /// <summary>Validates shutdown deadlines.</summary>
    internal void Validate()
    {
        if (GracefulDrainTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(GracefulDrainTimeout));
        if (CancellationDrainTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(CancellationDrainTimeout));
    }
}
