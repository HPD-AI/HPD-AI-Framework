namespace HPD.Execution.Local.ProcessIsolation;

using HPD.Execution.Contracts;
using HPD.Execution.Local.Policy;

internal static class LocalProcessIsolationCompiler
{
    public static LocalProcessIsolationPlan Compile(ProcessInvocationSpec invocation)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        return Compile(invocation.Isolation);
    }

    public static LocalProcessIsolationPlan Compile(ProcessIsolationPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);

        if (policy.Violations.ObservationTailLimit < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(policy),
                policy.Violations.ObservationTailLimit,
                "Violation observation tail limit cannot be negative.");
        }

        return new LocalProcessIsolationPlan
        {
            Filesystem = CompileFilesystem(policy.Filesystem),
            Network = CompileNetwork(policy.Network),
            UnixSockets = new LocalUnixSocketIsolationPlan
            {
                AllowAll = policy.UnixSockets.AllowAll,
                AllowedSockets = policy.UnixSockets.AllowedSockets,
            },
            Environment = new LocalEnvironmentIsolationPlan
            {
                AllowedVariables = policy.Environment.AllowedVariables,
                InjectedVariables = policy.Environment.InjectedVariables,
                StripUnlistedVariables = policy.Environment.StripUnlistedVariables,
            },
            Tls = new LocalTlsIsolationPlan
            {
                Mode = policy.TlsTrust.Mode,
                TrustAuthority = policy.TlsTrust.TrustAuthority,
                InjectTrustEnvironmentVariables = policy.TlsTrust.InjectTrustEnvironmentVariables,
            },
            Interactive = new LocalInteractiveIsolationPlan
            {
                AllowPty = policy.Interactive.AllowPty,
                AllowStdin = policy.Interactive.AllowStdin,
                AllowLocalBinding = policy.Interactive.AllowLocalBinding,
                AllowedMachLookups = policy.Interactive.AllowedMachLookups,
            },
            Violations = new LocalViolationIsolationPlan
            {
                Action = policy.Violations.Action,
                IgnorePatterns = policy.Violations.IgnorePatterns,
                ObservationTailLimit = policy.Violations.ObservationTailLimit,
            },
            Degradation = new LocalIsolationDegradationPlan
            {
                Mode = policy.Degradation.Mode,
                AllowDegradedFeatures = policy.Degradation.AllowDegradedFeatures,
            },
            AuthorityBindings = policy.AuthorityBindings,
            ProviderExtensions = policy.ProviderExtensions,
        };
    }

    private static LocalFilesystemIsolationPlan CompileFilesystem(FilesystemAccessPolicy policy)
    {
        return new LocalFilesystemIsolationPlan
        {
            Rules = policy.Rules.Select(rule => new LocalPathAccessRule(
                rule.Kind,
                rule.Path,
                rule.PatternKind,
                rule.Reason)).ToArray(),
            DangerousPaths = policy.DangerousPaths,
            Symlinks = policy.Symlinks,
            MoveProtection = policy.MoveProtection,
        };
    }

    private static LocalNetworkIsolationPlan CompileNetwork(NetworkEgressPolicy policy)
    {
        return new LocalNetworkIsolationPlan
        {
            Mode = policy.Mode,
            AllowedDomains = policy.AllowedDomains.Select(CompileDomainRule).ToArray(),
            DeniedDomains = policy.DeniedDomains.Select(CompileDomainRule).ToArray(),
            ParentProxy = policy.ParentProxy,
            RequestFilter = policy.RequestFilter,
            RequireProxyMediation = policy.RequireProxyMediation,
        };
    }

    private static LocalDomainRule CompileDomainRule(DomainRule rule)
    {
        DomainPattern pattern = DomainPattern.Parse(rule.Pattern);
        EnsureDomainKind(rule, pattern);
        return new LocalDomainRule(rule, pattern);
    }

    private static void EnsureDomainKind(DomainRule rule, DomainPattern pattern)
    {
        bool isValid = rule.Kind switch
        {
            DomainRuleKind.ProviderValidate => true,
            DomainRuleKind.ExactHost => pattern.Kind == DomainPatternKind.ExactHost,
            DomainRuleKind.WildcardSubdomain => pattern.Kind == DomainPatternKind.WildcardSubdomain,
            DomainRuleKind.IpLiteral => pattern.Kind == DomainPatternKind.IpLiteral,
            DomainRuleKind.Localhost => pattern.Kind == DomainPatternKind.Localhost,
            _ => false,
        };

        if (!isValid)
        {
            throw new ArgumentException(
                $"Domain rule '{rule.Pattern}' was declared as {rule.Kind}, but validated as {pattern.Kind}.",
                nameof(rule));
        }
    }
}
