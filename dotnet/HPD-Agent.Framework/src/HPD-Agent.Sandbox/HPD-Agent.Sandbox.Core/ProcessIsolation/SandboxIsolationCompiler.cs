namespace HPD.Agent.Sandbox.ProcessIsolation;

using HPD.Environment.Contracts;
using HPD.Agent.Sandbox.Policy;

public sealed class SandboxIsolationPlanner : ISandboxPlanner
{
    public ValueTask<SandboxPlanEnvelope> PlanAsync(
        ProcessInvocationSpec invocation,
        SandboxExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        SandboxIsolationPlan plan = SandboxIsolationCompiler.Compile(invocation.Isolation);
        Diagnostic[] diagnostics =
        [
            new Diagnostic
            {
                Code = new DiagnosticCode("hpd.agent.sandbox.plan.created"),
                Severity = DiagnosticSeverity.Info,
                Message = $"Sandbox plan created for {context.ExecutionPlatform.OperatingSystem}/{context.ExecutionPlatform.Architecture} at {context.EnforcementLocation}.",
            },
        ];

        return ValueTask.FromResult(new SandboxPlanEnvelope
        {
            SchemaId = SandboxPlanEnvelope.DefaultSchemaId,
            ExecutionPlatform = context.ExecutionPlatform,
            EnforcementLocation = context.EnforcementLocation,
            Plan = plan,
            Diagnostics = diagnostics,
            ProviderExtensions = plan.ProviderExtensions,
        });
    }
}

public static class SandboxIsolationCompiler
{
    public static SandboxIsolationPlan Compile(ProcessInvocationSpec invocation)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        return Compile(invocation.Isolation);
    }

    public static SandboxIsolationPlan Compile(ProcessIsolationPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);

        if (policy.Violations.ObservationTailLimit < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(policy),
                policy.Violations.ObservationTailLimit,
                "Violation observation tail limit cannot be negative.");
        }

        return new SandboxIsolationPlan
        {
            Filesystem = CompileFilesystem(policy.Filesystem),
            Network = CompileNetwork(policy.Network),
            UnixSockets = new SandboxUnixSocketIsolationPlan
            {
                AllowAll = policy.UnixSockets.AllowAll,
                AllowedSockets = policy.UnixSockets.AllowedSockets,
            },
            Environment = new SandboxEnvironmentIsolationPlan
            {
                AllowedVariables = policy.Environment.AllowedVariables,
                InjectedVariables = policy.Environment.InjectedVariables,
                StripUnlistedVariables = policy.Environment.StripUnlistedVariables,
            },
            Tls = new SandboxTlsIsolationPlan
            {
                Mode = policy.TlsTrust.Mode,
                TrustAuthority = policy.TlsTrust.TrustAuthority,
                InjectTrustEnvironmentVariables = policy.TlsTrust.InjectTrustEnvironmentVariables,
            },
            Interactive = new SandboxInteractiveIsolationPlan
            {
                AllowPty = policy.Interactive.AllowPty,
                AllowStdin = policy.Interactive.AllowStdin,
                AllowLocalBinding = policy.Interactive.AllowLocalBinding,
                AllowedMachLookups = policy.Interactive.AllowedMachLookups,
            },
            Violations = new SandboxViolationIsolationPlan
            {
                Action = policy.Violations.Action,
                IgnorePatterns = policy.Violations.IgnorePatterns,
                ObservationTailLimit = policy.Violations.ObservationTailLimit,
            },
            Degradation = new SandboxIsolationDegradationPlan
            {
                Mode = policy.Degradation.Mode,
                AllowDegradedFeatures = policy.Degradation.AllowDegradedFeatures,
            },
            AuthorityBindings = policy.AuthorityBindings,
            ProviderExtensions = policy.ProviderExtensions,
        };
    }

    private static SandboxFilesystemIsolationPlan CompileFilesystem(FilesystemAccessPolicy policy)
    {
        return new SandboxFilesystemIsolationPlan
        {
            Rules = policy.Rules.Select(rule => new SandboxPathAccessRule(
                rule.Kind,
                rule.Path,
                rule.PatternKind,
                rule.Reason)).ToArray(),
            DangerousPaths = policy.DangerousPaths,
            Symlinks = policy.Symlinks,
            MoveProtection = policy.MoveProtection,
        };
    }

    private static SandboxNetworkIsolationPlan CompileNetwork(NetworkEgressPolicy policy)
    {
        return new SandboxNetworkIsolationPlan
        {
            Mode = policy.Mode,
            AllowedDomains = policy.AllowedDomains.Select(CompileDomainRule).ToArray(),
            DeniedDomains = policy.DeniedDomains.Select(CompileDomainRule).ToArray(),
            ParentProxy = policy.ParentProxy,
            RequestFilter = policy.RequestFilter,
            RequireProxyMediation = policy.RequireProxyMediation,
        };
    }

    private static SandboxDomainRule CompileDomainRule(DomainRule rule)
    {
        DomainPattern pattern = DomainPattern.Parse(rule.Pattern);
        EnsureDomainKind(rule, pattern);
        return new SandboxDomainRule(rule, pattern.Canonical);
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
