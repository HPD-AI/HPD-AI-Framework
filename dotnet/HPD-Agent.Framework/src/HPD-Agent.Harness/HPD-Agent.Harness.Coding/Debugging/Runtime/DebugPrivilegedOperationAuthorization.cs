namespace HPD.Agent.ToolHarness.Coding.Debugging;


internal enum DebugPrivilegedOperation { SetVariable, SetExpression, WriteMemory, PrivilegedEvaluate, HostExtension }

/// <summary>Narrow invocation proof minted after normal HPD permission middleware approves an operation.</summary>
internal sealed record DebugPrivilegedOperationAuthorization
{
    public required string AgentRuntimeRegistrationId { get; init; }
    public required string DebugTreeId { get; init; }
    public required string DebugSessionId { get; init; }
    public required DebugPrivilegedOperation Operation { get; init; }
    public required long PolicyRevision { get; init; }
    public required long EnvironmentRevision { get; init; }
    public required long EndpointCatalogRevision { get; init; }
    public required DateTimeOffset ExpiresAt { get; init; }

    public void Validate(DebugSessionTree tree, DebugSession session, DebugPrivilegedOperation expected)
    {
        ArgumentNullException.ThrowIfNull(tree);
        ArgumentNullException.ThrowIfNull(session);
        if (DateTimeOffset.UtcNow >= ExpiresAt || Operation != expected ||
            !string.Equals(AgentRuntimeRegistrationId, tree.Ownership.AgentRuntimeRegistrationId, StringComparison.Ordinal) ||
            !string.Equals(DebugTreeId, tree.Ownership.DebugTreeId, StringComparison.Ordinal) ||
            !string.Equals(DebugSessionId, session.SessionId, StringComparison.Ordinal) ||
            PolicyRevision != tree.Authorization.PolicyRevision ||
            EnvironmentRevision != tree.Authorization.EnvironmentRevision ||
            EndpointCatalogRevision != tree.Authorization.EndpointCatalogRevision)
            throw new DebugSemanticException(DebugSemanticFailureReason.PermissionDenied,
                "The privileged debugger authorization is absent, expired, or belongs to another operation.");
    }

    internal static DebugPrivilegedOperationAuthorization Create(
        DebugSessionTree tree, DebugSession session, DebugPrivilegedOperation operation,
        TimeSpan? lifetime = null)
    {
        var duration = lifetime ?? TimeSpan.FromMinutes(1);
        if (duration <= TimeSpan.Zero || duration > TimeSpan.FromMinutes(5))
            throw new ArgumentOutOfRangeException(nameof(lifetime));
        return new()
        {
            AgentRuntimeRegistrationId = tree.Ownership.AgentRuntimeRegistrationId,
            DebugTreeId = tree.Ownership.DebugTreeId,
            DebugSessionId = session.SessionId,
            Operation = operation,
            PolicyRevision = tree.Authorization.PolicyRevision,
            EnvironmentRevision = tree.Authorization.EnvironmentRevision,
            EndpointCatalogRevision = tree.Authorization.EndpointCatalogRevision,
            ExpiresAt = DateTimeOffset.UtcNow + duration
        };
    }

}
