namespace HPD.Agent.Sandbox.ProcessIsolation;

using HPD.Execution.Contracts;
using HPD.Agent.Sandbox.Policy;

internal sealed record SandboxIsolationPlan
{
    public required SandboxFilesystemIsolationPlan Filesystem { get; init; }
    public required SandboxNetworkIsolationPlan Network { get; init; }
    public required SandboxUnixSocketIsolationPlan UnixSockets { get; init; }
    public required SandboxEnvironmentIsolationPlan Environment { get; init; }
    public required SandboxTlsIsolationPlan Tls { get; init; }
    public required SandboxInteractiveIsolationPlan Interactive { get; init; }
    public required SandboxViolationIsolationPlan Violations { get; init; }
    public required SandboxIsolationDegradationPlan Degradation { get; init; }
    public IReadOnlyList<ResourceRef<AuthorityBinding>> AuthorityBindings { get; init; } = [];
    public IReadOnlyList<ProviderExtensionData> ProviderExtensions { get; init; } = [];
}

internal sealed record SandboxFilesystemIsolationPlan
{
    public IReadOnlyList<SandboxPathAccessRule> Rules { get; init; } = [];
    public DangerousPathPolicy DangerousPaths { get; init; } = DangerousPathPolicy.Default;
    public SymlinkEvaluationPolicy Symlinks { get; init; } = SymlinkEvaluationPolicy.ResolveExistingPaths;
    public MoveProtectionPolicy MoveProtection { get; init; } = MoveProtectionPolicy.ProtectDeniedPaths;
}

internal sealed record SandboxPathAccessRule(
    PathAccessRuleKind Kind,
    HostPath Path,
    PathPatternKind PatternKind,
    string? Reason);

internal sealed record SandboxNetworkIsolationPlan
{
    public required NetworkEgressMode Mode { get; init; }
    public IReadOnlyList<SandboxDomainRule> AllowedDomains { get; init; } = [];
    public IReadOnlyList<SandboxDomainRule> DeniedDomains { get; init; } = [];
    public ParentProxyPolicy? ParentProxy { get; init; }
    public RequestFilterPolicy? RequestFilter { get; init; }
    public bool RequireProxyMediation { get; init; }
}

internal sealed record SandboxDomainRule(
    DomainRule Source,
    DomainPattern Pattern);

internal sealed record SandboxUnixSocketIsolationPlan
{
    public bool AllowAll { get; init; }
    public IReadOnlyList<UnixSocketAccessRule> AllowedSockets { get; init; } = [];
}

internal sealed record SandboxEnvironmentIsolationPlan
{
    public IReadOnlyList<string> AllowedVariables { get; init; } = [];
    public IReadOnlyDictionary<string, string> InjectedVariables { get; init; } = new Dictionary<string, string>(0, StringComparer.Ordinal);
    public bool StripUnlistedVariables { get; init; } = true;
}

internal sealed record SandboxTlsIsolationPlan
{
    public TlsTrustMode Mode { get; init; } = TlsTrustMode.None;
    public ResourceRef<AuthorityBinding>? TrustAuthority { get; init; }
    public bool InjectTrustEnvironmentVariables { get; init; }
}

internal sealed record SandboxInteractiveIsolationPlan
{
    public bool AllowPty { get; init; }
    public bool AllowStdin { get; init; } = true;
    public bool AllowLocalBinding { get; init; }
    public IReadOnlyList<string> AllowedMachLookups { get; init; } = [];
}

internal sealed record SandboxViolationIsolationPlan
{
    public ProcessViolationAction Action { get; init; } = ProcessViolationAction.ObserveAndFailInvocation;
    public IReadOnlyList<string> IgnorePatterns { get; init; } = [];
    public int ObservationTailLimit { get; init; } = 100;
}

internal sealed record SandboxIsolationDegradationPlan
{
    public ProcessIsolationDegradationMode Mode { get; init; } = ProcessIsolationDegradationMode.FailClosed;
    public IReadOnlyList<string> AllowDegradedFeatures { get; init; } = [];
}
