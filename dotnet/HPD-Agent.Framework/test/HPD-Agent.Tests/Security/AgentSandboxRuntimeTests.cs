using HPD.Agent.Security;
using HPD.Environment.Contracts;

namespace HPD.Agent.Tests.Security;

public sealed class AgentSandboxRuntimeTests
{
    [Fact]
    public void Capture_ClonesSecurityAndCapabilities()
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "hpd-sandbox-root"));
        var runConfig = new AgentRunConfig
        {
            Security = new AgentSecurityProfile
            {
                Approval = AgentApprovalPolicy.AutoApprove,
                Sandbox = AgentSandboxPolicy.Disabled,
                SandboxEscape = AgentSandboxEscapePolicy.Deny
            },
            Sandbox = new AgentSandboxConfiguration
            {
                Filesystem =
                [
                    new AgentSandboxPathGrant
                    {
                        Access = AgentSandboxPathAccess.Read,
                        Path = root
                    }
                ],
                Network = new NetworkEgressPolicy
                {
                    Mode = NetworkEgressMode.Unrestricted
                }
            }
        };

        var runtime = AgentSandboxRuntime.Capture(runConfig);
        runConfig.Sandbox = new AgentSandboxConfiguration();

        Assert.False(runtime.IsEnforced);
        Assert.Equal(AgentApprovalPolicy.AutoApprove, runtime.Security.Approval);
        Assert.Equal(AgentSandboxEscapePolicy.Deny, runtime.Security.SandboxEscape);
        Assert.Single(runtime.Filesystem);
        Assert.Equal(root, runtime.Filesystem[0].Path);
        Assert.Equal(NetworkEgressMode.Unrestricted, runtime.Network.Mode);
    }

    [Fact]
    public void ProcessPolicy_ResolvesRelativeGrantsAgainstWorkingDirectory()
    {
        var workingDirectory = Path.GetFullPath(
            Path.Combine(Path.GetTempPath(), "hpd-sandbox-working"));
        var runtime = AgentSandboxRuntime.Capture(new AgentRunConfig
        {
            Sandbox = new AgentSandboxConfiguration
            {
                Filesystem =
                [
                    new AgentSandboxPathGrant
                    {
                        Access = AgentSandboxPathAccess.Write,
                        Path = "artifacts"
                    }
                ]
            }
        });

        var policy = runtime.ToProcessIsolationPolicy(workingDirectory);

        Assert.Equal(ProcessIsolationMode.Isolated, policy.Mode);
        var rule = Assert.Single(policy.Filesystem.Rules);
        Assert.Equal(PathAccessRuleKind.AllowWrite, rule.Kind);
        Assert.Equal(
            Path.Combine(workingDirectory, "artifacts"),
            rule.Path.Value);
    }

    [Fact]
    public void WithPathGrant_ReplacesOnlyAnEquivalentGrant()
    {
        var path = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "hpd-extra-grant"));
        var runtime = AgentSandboxRuntime.Default
            .WithPathGrant(AgentSandboxPathAccess.Read, path)
            .WithPathGrant(AgentSandboxPathAccess.Read, path)
            .WithPathGrant(AgentSandboxPathAccess.Write, path);

        Assert.Equal(2, runtime.Filesystem.Count);
        Assert.Contains(runtime.Filesystem, grant =>
            grant.Access == AgentSandboxPathAccess.Read && grant.Path == path);
        Assert.Contains(runtime.Filesystem, grant =>
            grant.Access == AgentSandboxPathAccess.Write && grant.Path == path);
    }
}
