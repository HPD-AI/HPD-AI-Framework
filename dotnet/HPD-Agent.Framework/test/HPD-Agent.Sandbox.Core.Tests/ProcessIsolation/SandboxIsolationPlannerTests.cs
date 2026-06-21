namespace HPD.Agent.Sandbox.Tests.ProcessIsolation;

using FluentAssertions;
using HPD.Agent.Sandbox.ProcessIsolation;
using HPD.Environment.Contracts;
using Xunit;

public sealed class SandboxIsolationPlannerTests
{
    [Fact]
    public async Task Planner_compiles_policy_into_portable_plan_envelope()
    {
        var planner = new SandboxIsolationPlanner();
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

        SandboxPlanEnvelope envelope = await planner.PlanAsync(
            invocation,
            new SandboxExecutionContext
            {
                HostPlatform = new PlatformSpec("macos", "arm64"),
                ExecutionPlatform = new PlatformSpec("linux", "arm64"),
                EnforcementLocation = SandboxEnforcementLocation.Guest,
                Scope = new ResourceScope("test-runtime"),
            });

        envelope.SchemaId.Value.Should().Be("hpd.agent.sandbox.plan");
        envelope.ExecutionPlatform.OperatingSystem.Should().Be("linux");
        envelope.EnforcementLocation.Should().Be(SandboxEnforcementLocation.Guest);
        envelope.Plan.Filesystem.Rules.Should().HaveCount(2);
        envelope.Plan.Network.AllowedDomains.Single().CanonicalPattern.Should().Be("registry.npmjs.org");
        envelope.Diagnostics.Should().ContainSingle(diagnostic => diagnostic.Code.Value == "hpd.agent.sandbox.plan.created");
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
