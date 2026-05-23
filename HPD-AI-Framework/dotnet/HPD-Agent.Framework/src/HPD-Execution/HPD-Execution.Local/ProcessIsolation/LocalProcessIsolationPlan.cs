namespace HPD.Execution.Local.ProcessIsolation;

using HPD.Execution.Contracts;
using HPD.Execution.Local.Policy;

internal sealed record LocalProcessIsolationPlan
{
    public required LocalFilesystemIsolationPlan Filesystem { get; init; }
    public required LocalNetworkIsolationPlan Network { get; init; }
    public required LocalUnixSocketIsolationPlan UnixSockets { get; init; }
    public required LocalEnvironmentIsolationPlan Environment { get; init; }
    public required LocalTlsIsolationPlan Tls { get; init; }
    public required LocalInteractiveIsolationPlan Interactive { get; init; }
    public required LocalViolationIsolationPlan Violations { get; init; }
    public required LocalIsolationDegradationPlan Degradation { get; init; }
    public IReadOnlyList<ResourceRef<AuthorityBinding>> AuthorityBindings { get; init; } = [];
    public IReadOnlyList<ProviderExtensionData> ProviderExtensions { get; init; } = [];
}

internal sealed record LocalFilesystemIsolationPlan
{
    public IReadOnlyList<LocalPathAccessRule> Rules { get; init; } = [];
    public DangerousPathPolicy DangerousPaths { get; init; } = DangerousPathPolicy.Default;
    public SymlinkEvaluationPolicy Symlinks { get; init; } = SymlinkEvaluationPolicy.ResolveExistingPaths;
    public MoveProtectionPolicy MoveProtection { get; init; } = MoveProtectionPolicy.ProtectDeniedPaths;
}

internal sealed record LocalPathAccessRule(
    PathAccessRuleKind Kind,
    HostPath Path,
    PathPatternKind PatternKind,
    string? Reason);

internal sealed record LocalNetworkIsolationPlan
{
    public required NetworkEgressMode Mode { get; init; }
    public IReadOnlyList<LocalDomainRule> AllowedDomains { get; init; } = [];
    public IReadOnlyList<LocalDomainRule> DeniedDomains { get; init; } = [];
    public ParentProxyPolicy? ParentProxy { get; init; }
    public RequestFilterPolicy? RequestFilter { get; init; }
    public bool RequireProxyMediation { get; init; }
}

internal sealed record LocalDomainRule(
    DomainRule Source,
    DomainPattern Pattern);

internal sealed record LocalUnixSocketIsolationPlan
{
    public bool AllowAll { get; init; }
    public IReadOnlyList<UnixSocketAccessRule> AllowedSockets { get; init; } = [];
}

internal sealed record LocalEnvironmentIsolationPlan
{
    public IReadOnlyList<string> AllowedVariables { get; init; } = [];
    public IReadOnlyDictionary<string, string> InjectedVariables { get; init; } = new Dictionary<string, string>(0, StringComparer.Ordinal);
    public bool StripUnlistedVariables { get; init; } = true;
}

internal sealed record LocalTlsIsolationPlan
{
    public TlsTrustMode Mode { get; init; } = TlsTrustMode.None;
    public ResourceRef<AuthorityBinding>? TrustAuthority { get; init; }
    public bool InjectTrustEnvironmentVariables { get; init; }
}

internal sealed record LocalInteractiveIsolationPlan
{
    public bool AllowPty { get; init; }
    public bool AllowStdin { get; init; } = true;
    public bool AllowLocalBinding { get; init; }
    public IReadOnlyList<string> AllowedMachLookups { get; init; } = [];
}

internal sealed record LocalViolationIsolationPlan
{
    public ProcessViolationAction Action { get; init; } = ProcessViolationAction.ObserveAndFailInvocation;
    public IReadOnlyList<string> IgnorePatterns { get; init; } = [];
    public int ObservationTailLimit { get; init; } = 100;
}

internal sealed record LocalIsolationDegradationPlan
{
    public ProcessIsolationDegradationMode Mode { get; init; } = ProcessIsolationDegradationMode.FailClosed;
    public IReadOnlyList<string> AllowDegradedFeatures { get; init; } = [];
}
