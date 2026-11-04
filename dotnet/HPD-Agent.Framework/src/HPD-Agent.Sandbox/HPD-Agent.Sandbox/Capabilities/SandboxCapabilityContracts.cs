namespace HPD.Agent.Sandbox;

using HPD.Environment.Contracts;

/// <summary>Capability query built from a prospective sandbox session request.</summary>
public sealed record SandboxCapabilitiesQuery
{
    public ProcessIsolationPolicy Isolation { get; init; } =
        ProcessIsolationPolicy.Default;
    public SandboxProcessRequirements Process { get; init; } =
        SandboxProcessRequirements.Default;
    public IReadOnlyList<SandboxWorkspaceRequest> Workspaces { get; init; } = [];
    public ResourceQuotaPolicy Resources { get; init; } =
        ResourceQuotaPolicy.Default;
    public ProviderId? PreferredProvider { get; init; }
}

/// <summary>
/// A provider capability fact annotated with the component that actually
/// enforces it.
/// </summary>
public sealed record SandboxCapabilityFact(
    CapabilityFact Fact,
    SandboxEnforcementLocation EnforcementLocation);

/// <summary>
/// Provider capability data plus the sandbox-specific enforcement assessment.
/// </summary>
public sealed record SandboxBackendCapabilities(
    ProviderCapabilityReport ProviderReport,
    IReadOnlyList<SandboxCapabilityFact> SandboxCapabilities);

/// <summary>Reason that a required sandbox capability was not admitted.</summary>
public sealed record SandboxCapabilityIssue(
    CapabilityId Capability,
    CapabilityState State,
    string Reason,
    SandboxEnforcementLocation? EnforcementLocation = null);

/// <summary>Result of checking a backend against all required capabilities.</summary>
public sealed record SandboxCapabilityAssessment(
    bool IsAdmitted,
    IReadOnlyList<SandboxCapabilityIssue> Issues);

/// <summary>
/// Raised before session creation when requested behavior cannot be enforced.
/// </summary>
public sealed class SandboxCapabilityAdmissionException(
    SandboxCapabilityAssessment assessment)
    : InvalidOperationException(
        "Sandbox capabilities do not satisfy the session request: " +
        string.Join(
            " | ",
            assessment.Issues.Select(issue =>
                $"{issue.Capability.Value}: {issue.Reason}")))
{
    public SandboxCapabilityAssessment Assessment { get; } = assessment;
}

/// <summary>
/// Maps portable sandbox requests to capability requirements and fails closed
/// when a backend does not prove support.
/// </summary>
public static class SandboxCapabilityAdmission
{
    /// <summary>
    /// Gets the complete set of capabilities a request requires. The caller
    /// receives extra requirements when policy enables a feature, including
    /// features that have a default value.
    /// </summary>
    public static IReadOnlyList<CapabilityId> RequiredFor(
        SandboxSessionRequest request)
    {
        HashSet<CapabilityId> required =
        [
            StandardSandboxEnvironmentCapabilities.ProcessExecution,
            StandardSandboxEnvironmentCapabilities.ProcessOutput,
            StandardSandboxEnvironmentCapabilities.ProcessCancellation,
            StandardSandboxEnvironmentCapabilities.SessionCleanup,
        ];

        AddNetworkRequirements(request.Isolation.Network, required);
        AddInteractiveRequirements(request.Isolation.Interactive, required);
        AddIsolationRequirements(request.Isolation, required);

        if (request.Workspaces.Count > 0)
        {
            required.Add(StandardSandboxEnvironmentCapabilities.ContentProjection);
            foreach (SandboxWorkspaceRequest workspace in
                     request.Workspaces)
            {
                required.UnionWith(
                    StandardWorkspaceEnvironmentCapabilities.RequiredFor(
                        workspace.Root.Access,
                        includeSearch: true));
            }
        }
        if (request.Process.RequireRetainedObservation)
        {
            required.Add(StandardSandboxEnvironmentCapabilities.RetainedProcessObservation);
        }
        if (request.Process.RequireBackgroundOwnership)
        {
            required.Add(StandardSandboxEnvironmentCapabilities.BackgroundProcessOwnership);
        }
        if (request.Process.RequireProcessOutputReplay)
        {
            required.Add(StandardSandboxEnvironmentCapabilities.ProcessOutputReplay);
        }
        if (HasResourceRequirements(request.Resources))
        {
            required.Add(StandardSandboxEnvironmentCapabilities.ResourceLimits);
        }

        return required.ToArray();
    }

    /// <summary>
    /// Assesses the sandbox facts against the requirements. A degraded fact is
    /// accepted only when the caller explicitly permits provider degradation.
    /// </summary>
    public static SandboxCapabilityAssessment Assess(
        SandboxBackendCapabilities capabilities,
        IReadOnlyList<CapabilityId> required,
        bool allowDegradation = false)
    {
        // Every sandbox fact must also exist in the provider report with the
        // same state. This prevents the adapter from claiming a capability the
        // provider itself does not report.
        List<SandboxCapabilityIssue> issues = [];

        Dictionary<CapabilityId, SandboxCapabilityFact> sandboxFacts =
            ToUniqueFacts(
                capabilities.SandboxCapabilities,
                capability => capability.Fact.Id,
                issues);
        Dictionary<CapabilityId, CapabilityFact> providerFacts =
            ToUniqueFacts(
                capabilities.ProviderReport.Capabilities,
                capability => capability.Id,
                issues);

        foreach (SandboxCapabilityFact sandboxFact in
                 capabilities.SandboxCapabilities)
        {
            if (!providerFacts.TryGetValue(
                    sandboxFact.Fact.Id,
                    out CapabilityFact? providerFact) ||
                providerFact.State != sandboxFact.Fact.State)
            {
                issues.Add(new SandboxCapabilityIssue(
                    sandboxFact.Fact.Id,
                    CapabilityState.Unsupported,
                    "Sandbox capability assessment disagrees with the provider report.",
                    sandboxFact.EnforcementLocation));
            }
        }

        foreach (CapabilityId capability in required.Distinct())
        {
            if (!sandboxFacts.TryGetValue(
                    capability,
                    out SandboxCapabilityFact? fact))
            {
                issues.Add(new SandboxCapabilityIssue(
                    capability,
                    CapabilityState.Unsupported,
                    "Required capability was not reported."));
                continue;
            }

            bool admitted =
                fact.Fact.State == CapabilityState.Supported ||
                (allowDegradation &&
                 fact.Fact.State == CapabilityState.Degraded);
            if (!admitted)
            {
                issues.Add(new SandboxCapabilityIssue(
                    capability,
                    fact.Fact.State,
                    fact.Fact.Detail ??
                        "Required capability is not available.",
                    fact.EnforcementLocation));
            }
        }

        return new SandboxCapabilityAssessment(
            issues.Count == 0,
            issues);
    }

    /// <summary>
    /// Throws when the backend cannot satisfy the request. Session factories
    /// must call this before creating any environment-owned resource.
    /// </summary>
    public static void EnsureAdmitted(
        SandboxSessionRequest request,
        SandboxBackendCapabilities capabilities)
    {
        bool allowDegradation =
            request.Isolation.Degradation.Mode ==
            ProcessIsolationDegradationMode.AllowProviderDegraded;
        SandboxCapabilityAssessment assessment = Assess(
            capabilities,
            RequiredFor(request),
            allowDegradation);
        if (!assessment.IsAdmitted)
        {
            throw new SandboxCapabilityAdmissionException(assessment);
        }
    }

    private static Dictionary<TKey, TValue> ToUniqueFacts<TKey, TValue>(
        IEnumerable<TValue> facts,
        Func<TValue, TKey> keySelector,
        List<SandboxCapabilityIssue> issues)
        where TKey : notnull
    {
        Dictionary<TKey, TValue> unique = [];
        foreach (TValue fact in facts)
        {
            TKey key = keySelector(fact);
            if (!unique.TryAdd(key, fact) &&
                key is CapabilityId capability)
            {
                issues.Add(new SandboxCapabilityIssue(
                    capability,
                    CapabilityState.Unsupported,
                    "Capability was reported more than once."));
            }
        }

        return unique;
    }

    private static void AddIsolationRequirements(
        ProcessIsolationPolicy isolation,
        HashSet<CapabilityId> required)
    {
        if (isolation.Mode == ProcessIsolationMode.Isolated)
        {
            required.Add(StandardSandboxEnvironmentCapabilities.FilesystemReadRestrictions);
            required.Add(StandardSandboxEnvironmentCapabilities.FilesystemWriteRestrictions);
            required.Add(StandardSandboxEnvironmentCapabilities.SymlinkHandling);
        }

        if (isolation.UnixSockets.AllowedSockets.Count > 0)
        {
            required.Add(StandardSandboxEnvironmentCapabilities.UnixSocketRestriction);
        }

        if (isolation.Environment.StripUnlistedVariables ||
            isolation.Environment.InjectedVariables.Count > 0)
        {
            required.Add(StandardSandboxEnvironmentCapabilities.EnvironmentFiltering);
        }

        if (isolation.Violations.Action !=
            ProcessViolationAction.ObserveOnly)
        {
            required.Add(StandardSandboxEnvironmentCapabilities.ViolationObservation);
        }
    }

    private static void AddNetworkRequirements(
        NetworkEgressPolicy network,
        HashSet<CapabilityId> required)
    {
        switch (network.Mode)
        {
            case NetworkEgressMode.Blocked:
                required.Add(StandardSandboxEnvironmentCapabilities.NetworkBlock);
                break;
            case NetworkEgressMode.Filtered:
                required.Add(StandardSandboxEnvironmentCapabilities.NetworkFiltering);
                if (network.AllowedDomains.Count > 0)
                {
                    required.Add(StandardSandboxEnvironmentCapabilities.NetworkAllowlist);
                }
                break;
        }

        if (network.RequireProxyMediation &&
            network.Mode != NetworkEgressMode.Blocked)
        {
            required.Add(StandardSandboxEnvironmentCapabilities.NetworkProxyMediation);
        }
    }

    private static void AddInteractiveRequirements(
        ProcessInteractivePolicy interactive,
        HashSet<CapabilityId> required)
    {
        if (interactive.AllowPty)
        {
            required.Add(StandardSandboxEnvironmentCapabilities.Pty);
        }
        if (interactive.AllowStdin)
        {
            required.Add(StandardSandboxEnvironmentCapabilities.StandardInput);
        }
        if (interactive.AllowLocalBinding)
        {
            required.Add(StandardSandboxEnvironmentCapabilities.LocalPortBinding);
        }
    }

    private static bool HasResourceRequirements(
        ResourceQuotaPolicy resources) =>
        resources.CpuCores is not null ||
        resources.MemoryBytes is not null ||
        resources.StorageBytes is not null ||
        resources.ProcessCount is not null;
}
