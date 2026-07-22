namespace HPD.Agent.ToolHarness.Coding.Debugging;

[Flags]
public enum DebugTreeGrant
{
    None = 0,
    Inspect = 1 << 0,
    RoutineExecutionControl = 1 << 1,
    SourceBreakpoints = 1 << 2,
    FunctionBreakpoints = 1 << 3,
    ExceptionBreakpoints = 1 << 4,
    InstructionBreakpoints = 1 << 5,
    DataBreakpoints = 1 << 6,
    ChildSessions = 1 << 7,
    TerminalProcesses = 1 << 8,
    ShellInterpretation = 1 << 9,
    Evaluate = 1 << 10,
    MutateVariables = 1 << 11,
    WriteMemory = 1 << 12,

    StandardBreakpoints = SourceBreakpoints | FunctionBreakpoints |
        ExceptionBreakpoints | InstructionBreakpoints,
    Routine = Inspect | RoutineExecutionControl | StandardBreakpoints
}

public sealed record DebugTreeAuthorizationOptions
{
    public DebugTreeGrant Grants { get; init; } = DebugTreeGrant.Routine;
    public string? WorkingDirectoryScope { get; init; }
}

/// <summary>
/// Immutable, tree-scoped proof of the launch/attach boundary approved by the host.
/// It deliberately retains no invocation context or mutable capability registry.
/// </summary>
public sealed record DebugTreeAuthorization
{
    public required string AgentRuntimeRegistrationId { get; init; }
    public required string SessionId { get; init; }
    public required string ThreadId { get; init; }
    public required string DebugTreeId { get; init; }
    public required string AdapterId { get; init; }
    public required string PackageId { get; init; }
    public required string PackageVersion { get; init; }
    public required string TrustPolicyRevision { get; init; }
    public required string EnvironmentId { get; init; }
    public required long EnvironmentRevision { get; init; }
    public required long PolicyRevision { get; init; }
    public required long EndpointCatalogRevision { get; init; }
    public required string CanonicalWorkingDirectory { get; init; }
    public required string WorkingDirectoryScope { get; init; }
    public string? ToolLocationIdentity { get; init; }
    public string? ProcessProviderId { get; init; }
    public string? EndpointId { get; init; }
    public required string AuthorizationScope { get; init; }
    public required bool IsAttach { get; init; }
    public required DebugTreeGrant Grants { get; init; }

    public bool Allows(DebugTreeGrant grant) => (Grants & grant) == grant;

    public void Demand(DebugTreeGrant grant)
    {
        if (!Allows(grant))
            throw new UnauthorizedAccessException($"Debug tree authorization does not grant '{grant}'.");
    }

    public void ValidateCurrent(DebugRuntimeBinding runtime, DebugAdapterLaunchPlan plan)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(plan);
        runtime.State.ThrowIfUnavailable();

        if (!string.Equals(AgentRuntimeRegistrationId, runtime.AgentRuntimeRegistrationId, StringComparison.Ordinal) ||
            !string.Equals(SessionId, runtime.SessionId, StringComparison.Ordinal) ||
            !string.Equals(ThreadId, runtime.ThreadId, StringComparison.Ordinal) ||
            !string.Equals(AdapterId, plan.AdapterId, StringComparison.Ordinal) ||
            !string.Equals(PackageId, plan.PackageProvenance.PackageId, StringComparison.Ordinal) ||
            !string.Equals(PackageVersion, plan.PackageProvenance.PackageVersion, StringComparison.Ordinal) ||
            !string.Equals(TrustPolicyRevision, plan.TrustDecision.PolicyRevision, StringComparison.Ordinal) ||
            !string.Equals(EnvironmentId, plan.EnvironmentId, StringComparison.Ordinal) ||
            EnvironmentRevision != plan.EnvironmentRevision ||
            PolicyRevision != plan.PolicyRevision ||
            EndpointCatalogRevision != plan.EndpointCatalogRevision ||
            !IsWithinWorkingDirectoryScope(plan.CanonicalWorkingDirectory, WorkingDirectoryScope) ||
            !string.Equals(ToolLocationIdentity, plan.ToolProvenance?.LocationIdentity, StringComparison.Ordinal) ||
            !string.Equals(ProcessProviderId, plan.ProcessProviderId, StringComparison.Ordinal) ||
            !string.Equals(EndpointId, plan.Transport.EndpointId, StringComparison.Ordinal) ||
            !string.Equals(AuthorizationScope, plan.AuthorizationScope, StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException("The debug tree authorization does not match the current runtime or launch plan.");
        }
    }

    internal static DebugTreeAuthorization Create(
        DebugRuntimeBinding runtime,
        DebugTreeOwnership ownership,
        DebugAdapterLaunchPlan plan,
        bool isAttach,
        DebugTreeAuthorizationOptions options)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(ownership);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(options);
        if (plan.TrustDecision.TrustLevel != DebugAdapterTrustLevel.Trusted)
            throw new UnauthorizedAccessException("A debug tree requires a trusted adapter launch plan.");

        return new()
        {
            AgentRuntimeRegistrationId = runtime.AgentRuntimeRegistrationId,
            SessionId = runtime.SessionId,
            ThreadId = runtime.ThreadId,
            DebugTreeId = ownership.DebugTreeId,
            AdapterId = plan.AdapterId,
            PackageId = plan.PackageProvenance.PackageId,
            PackageVersion = plan.PackageProvenance.PackageVersion,
            TrustPolicyRevision = plan.TrustDecision.PolicyRevision,
            EnvironmentId = plan.EnvironmentId,
            EnvironmentRevision = plan.EnvironmentRevision,
            PolicyRevision = plan.PolicyRevision,
            EndpointCatalogRevision = plan.EndpointCatalogRevision,
            CanonicalWorkingDirectory = plan.CanonicalWorkingDirectory,
            WorkingDirectoryScope = options.WorkingDirectoryScope ?? plan.CanonicalWorkingDirectory,
            ToolLocationIdentity = plan.ToolProvenance?.LocationIdentity,
            ProcessProviderId = plan.ProcessProviderId,
            EndpointId = plan.Transport.EndpointId,
            AuthorizationScope = plan.AuthorizationScope,
            IsAttach = isAttach,
            Grants = options.Grants
        };
    }

    private static bool IsWithinWorkingDirectoryScope(string candidate, string scope)
    {
        static string Normalize(string value) => value.Replace('\\', '/').TrimEnd('/');
        var normalizedCandidate = Normalize(candidate);
        var normalizedScope = Normalize(scope);
        return string.Equals(normalizedCandidate, normalizedScope, StringComparison.Ordinal) ||
            normalizedCandidate.StartsWith(normalizedScope + "/", StringComparison.Ordinal);
    }
}
