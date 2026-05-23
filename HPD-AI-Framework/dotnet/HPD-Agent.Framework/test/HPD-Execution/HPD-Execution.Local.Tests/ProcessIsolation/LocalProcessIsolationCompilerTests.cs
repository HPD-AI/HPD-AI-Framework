namespace HPD.Execution.Local.Tests.ProcessIsolation;

using FluentAssertions;
using HPD.Execution.Contracts;
using HPD.Execution.Local.Policy;
using HPD.Execution.Local.ProcessIsolation;
using Xunit;

public sealed class LocalProcessIsolationCompilerTests
{
    [Fact]
    public void Compile_preserves_l3_12_policy_as_provider_native_plan()
    {
        ProcessIsolationPolicy policy = new()
        {
            Mode = ProcessIsolationMode.Isolated,
            Filesystem = new FilesystemAccessPolicy
            {
                Rules =
                [
                    new PathAccessRule { Kind = PathAccessRuleKind.AllowWrite, Path = new HostPath("/workspace"), Reason = "workspace writes" },
                    new PathAccessRule { Kind = PathAccessRuleKind.AllowWrite, Path = new HostPath("/tmp"), Reason = "scratch" },
                    new PathAccessRule { Kind = PathAccessRuleKind.DenyRead, Path = new HostPath("/home/agent/.ssh"), Reason = "credential boundary" },
                    new PathAccessRule { Kind = PathAccessRuleKind.DenyWrite, Path = new HostPath("/workspace/.git/hooks"), Reason = "hook protection" },
                ],
                Symlinks = SymlinkEvaluationPolicy.ResolveExistingPaths,
                MoveProtection = MoveProtectionPolicy.ProtectDeniedPaths,
            },
            Network = new NetworkEgressPolicy
            {
                Mode = NetworkEgressMode.Filtered,
                AllowedDomains =
                [
                    new DomainRule { Pattern = "registry.npmjs.org", Kind = DomainRuleKind.ExactHost },
                    new DomainRule { Pattern = "*.github.com", Kind = DomainRuleKind.WildcardSubdomain },
                ],
                DeniedDomains =
                [
                    new DomainRule { Pattern = "169.254.169.254", Kind = DomainRuleKind.IpLiteral },
                ],
                RequireProxyMediation = true,
            },
            UnixSockets = new UnixSocketAccessPolicy
            {
                AllowedSockets =
                [
                    new UnixSocketAccessRule
                    {
                        Path = new UnixSocketPath("/run/user/1000/docker.sock"),
                        AuthorityClass = SensitiveAuthorityClass.RootlessEngineControl,
                        Purpose = "docker compose",
                    },
                ],
            },
            Environment = new EnvironmentAccessPolicy
            {
                AllowedVariables = ["PATH", "HOME", "TMPDIR"],
                InjectedVariables = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["HTTPS_PROXY"] = "http://127.0.0.1:18443",
                },
                StripUnlistedVariables = true,
            },
            TlsTrust = new TlsTrustPolicy
            {
                Mode = TlsTrustMode.EphemeralProviderAuthority,
                InjectTrustEnvironmentVariables = true,
            },
            Interactive = new ProcessInteractivePolicy
            {
                AllowPty = true,
                AllowStdin = true,
                AllowLocalBinding = false,
                AllowedMachLookups = ["com.apple.trustd"],
            },
            Violations = new ProcessViolationPolicy
            {
                Action = ProcessViolationAction.ObserveAndFailInvocation,
                ObservationTailLimit = 50,
            },
            Degradation = new ProcessIsolationDegradationPolicy
            {
                Mode = ProcessIsolationDegradationMode.FailClosed,
            },
        };

        LocalProcessIsolationPlan plan = LocalProcessIsolationCompiler.Compile(policy);

        plan.Filesystem.Rules.Should().HaveCount(4);
        plan.Filesystem.Rules.Should().Contain(rule => rule.Kind == PathAccessRuleKind.DenyRead && rule.Path.Value == "/home/agent/.ssh");
        plan.Network.Mode.Should().Be(NetworkEgressMode.Filtered);
        plan.Network.AllowedDomains.Should().HaveCount(2);
        plan.Network.AllowedDomains[0].Pattern.Kind.Should().Be(DomainPatternKind.ExactHost);
        plan.Network.AllowedDomains[1].Pattern.Kind.Should().Be(DomainPatternKind.WildcardSubdomain);
        plan.Network.DeniedDomains.Single().Pattern.Kind.Should().Be(DomainPatternKind.IpLiteral);
        plan.Network.RequireProxyMediation.Should().BeTrue();
        plan.UnixSockets.AllowedSockets.Single().AuthorityClass.Should().Be(SensitiveAuthorityClass.RootlessEngineControl);
        plan.Environment.InjectedVariables.Should().ContainKey("HTTPS_PROXY");
        plan.Tls.Mode.Should().Be(TlsTrustMode.EphemeralProviderAuthority);
        plan.Interactive.AllowedMachLookups.Should().Contain("com.apple.trustd");
        plan.Violations.ObservationTailLimit.Should().Be(50);
        plan.Degradation.Mode.Should().Be(ProcessIsolationDegradationMode.FailClosed);
    }

    [Fact]
    public void Compile_rejects_domain_kind_mismatch()
    {
        ProcessIsolationPolicy policy = new()
        {
            Network = new NetworkEgressPolicy
            {
                Mode = NetworkEgressMode.Filtered,
                AllowedDomains =
                [
                    new DomainRule { Pattern = "*.github.com", Kind = DomainRuleKind.ExactHost },
                ],
            },
        };

        Action act = () => LocalProcessIsolationCompiler.Compile(policy);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*declared as ExactHost*validated as WildcardSubdomain*");
    }

    [Fact]
    public void Compile_rejects_negative_violation_tail_limit()
    {
        ProcessIsolationPolicy policy = new()
        {
            Violations = new ProcessViolationPolicy
            {
                ObservationTailLimit = -1,
            },
        };

        Action act = () => LocalProcessIsolationCompiler.Compile(policy);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
