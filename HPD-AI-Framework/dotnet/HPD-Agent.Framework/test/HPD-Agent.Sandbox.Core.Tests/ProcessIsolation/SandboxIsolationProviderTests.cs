namespace HPD.Agent.Sandbox.Tests.ProcessIsolation;

using FluentAssertions;
using HPD.Agent.Sandbox.ProcessIsolation;
using HPD.Execution.Contracts;
using HPD.Execution.Runtime;
using Xunit;

public sealed class SandboxIsolationProviderTests
{
    [Fact]
    public async Task Module_registers_process_isolation_provider_and_reports_capability()
    {
        var registry = new ExecutionProviderRegistry();

        registry.RegisterSandboxIsolation();

        IReadOnlyList<ProviderDescriptor> providers = await registry.ListAsync();
        ProviderCapabilityReport report = await registry.GetCapabilitiesAsync(SandboxIsolationProvider.SandboxProviderId);

        registry.ProcessIsolationProviders.Should().ContainSingle();
        providers.Should().ContainSingle(provider => provider.ContractKinds == ProviderContractKind.ProcessIsolation);
        report.Capabilities.Should().Contain(fact =>
            fact.AppliesTo == ProviderContractKind.ProcessIsolation &&
            fact.State == CapabilityState.Supported);
    }

    [Fact]
    public async Task Prepare_compiles_policy_and_marks_invocation()
    {
        var provider = new SandboxIsolationProvider();
        ProcessInvocationSpec invocation = new()
        {
            Target = Handle<ExecutionUnit>(TargetRouteSegmentKind.ExecutionUnit, "unit-1"),
            Command = new ProcessCommandSpec { FileName = "/usr/bin/npm", Arguments = ["install"] },
            Isolation = new ProcessIsolationPolicy
            {
                Mode = ProcessIsolationMode.Isolated,
                Filesystem = new FilesystemAccessPolicy
                {
                    Rules =
                    [
                        new PathAccessRule { Kind = PathAccessRuleKind.AllowWrite, Path = new HostPath("/workspace") },
                        new PathAccessRule { Kind = PathAccessRuleKind.DenyRead, Path = new HostPath("/home/agent/.ssh") },
                    ],
                },
                Network = new NetworkEgressPolicy
                {
                    Mode = NetworkEgressMode.Filtered,
                    AllowedDomains =
                    [
                        new DomainRule { Pattern = "registry.npmjs.org", Kind = DomainRuleKind.ExactHost },
                    ],
                },
            },
        };

        ProcessIsolationPlan plan = await provider.PlanIsolationAsync(invocation, invocation.Isolation);
        IsolatedProcessCommand prepared = await provider.PrepareAsync(invocation, invocation.Isolation, plan);

        plan.Diagnostics.Should().ContainSingle(message => message.Contains("filesystem-rules=2", StringComparison.Ordinal));
        prepared.Invocation.ProviderExtensions.Should().Contain(extension =>
            extension.ProviderId == SandboxIsolationProvider.SandboxProviderId &&
            extension.SchemaId.Value == "hpd.agent.sandbox.plan");
        provider.LastPreparedPlan.Should().NotBeNull();
        provider.LastPreparedPlan!.Filesystem.Rules.Should().HaveCount(2);
        provider.LastPreparedPlan.Network.AllowedDomains.Single().Pattern.Canonical.Should().Be("registry.npmjs.org");
    }

    private static TargetHandle<T> Handle<T>(TargetRouteSegmentKind kind, string id)
        where T : IOperationTargetMarker =>
        new(
            new TargetRoute
            {
                Kind = new TargetKind(typeof(T).Name),
                Scope = new ResourceScope("test-runtime"),
                Segments = [new TargetRouteSegment(kind, id)],
            },
            TargetHandleLifetime.LiveCapability,
            TargetHandleAuthority.Observe | TargetHandleAuthority.Control | TargetHandleAuthority.Invoke);
}
