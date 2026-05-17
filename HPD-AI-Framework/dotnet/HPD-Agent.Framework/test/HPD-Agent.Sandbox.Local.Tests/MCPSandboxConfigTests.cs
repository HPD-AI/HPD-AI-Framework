using FluentAssertions;
using HPD.Agent.MCP;
using HPD.Agent.Sandbox;
using Xunit;

namespace HPD.Sandbox.Local.Tests;

public class MCPSandboxConfigTests
{
    [Fact]
    public void Default_IsEnabled()
    {
        var config = new MCPSandboxConfig();

        config.Enabled.Should().BeTrue();
    }

    [Fact]
    public void Default_HasNoNetworkAccess()
    {
        var config = new MCPSandboxConfig();

        config.AllowedDomains.Should().BeEmpty();
    }

    [Fact]
    public void ToSandboxConfig_WithNoProfile_UsesDefaults()
    {
        var mcpConfig = new MCPSandboxConfig();
        var sandboxConfig = mcpConfig.ToSandboxConfig();

        sandboxConfig.NetworkMode.Should().Be(SandboxNetworkMode.Blocked);
        sandboxConfig.AllowedDomains.Should().BeEmpty();
        sandboxConfig.AllowWrite.Should().BeEquivalentTo([".", "/tmp"]);
        sandboxConfig.DenyRead.Should().Contain("~/.ssh");
    }

    [Fact]
    public void ToSandboxConfig_RestrictiveProfile_AppliesDefaults()
    {
        var mcpConfig = new MCPSandboxConfig { Profile = "restrictive" };
        var sandboxConfig = mcpConfig.ToSandboxConfig();

        sandboxConfig.NetworkMode.Should().Be(SandboxNetworkMode.Blocked);
        sandboxConfig.AllowedDomains.Should().BeEmpty();
        sandboxConfig.DenyRead.Should().Contain("~/.ssh");
    }

    [Fact]
    public void ToSandboxConfig_PermissiveProfile_AllowsNetwork()
    {
        var mcpConfig = new MCPSandboxConfig { Profile = "permissive" };
        var sandboxConfig = mcpConfig.ToSandboxConfig();

        sandboxConfig.NetworkMode.Should().Be(SandboxNetworkMode.Unrestricted);
        sandboxConfig.AllowedDomains.Should().BeEmpty();
        sandboxConfig.DenyRead.Should().BeEmpty();
    }

    [Fact]
    public void ToSandboxConfig_NetworkOnlyProfile_AllowsNetworkDeniesFiles()
    {
        var mcpConfig = new MCPSandboxConfig { Profile = "network-only" };
        var sandboxConfig = mcpConfig.ToSandboxConfig();

        sandboxConfig.NetworkMode.Should().Be(SandboxNetworkMode.Unrestricted);
        sandboxConfig.AllowedDomains.Should().BeEmpty();
        sandboxConfig.DenyRead.Should().Contain("~/.ssh");
    }

    [Fact]
    public void ToSandboxConfig_FilesystemOnlyProfile_DeniesNetworkAllowsFiles()
    {
        var mcpConfig = new MCPSandboxConfig { Profile = "filesystem-only" };
        var sandboxConfig = mcpConfig.ToSandboxConfig();

        sandboxConfig.NetworkMode.Should().Be(SandboxNetworkMode.Blocked);
        sandboxConfig.AllowedDomains.Should().BeEmpty();
        sandboxConfig.DenyRead.Should().BeEmpty(); // Allow all reads
    }

    [Fact]
    public void ToSandboxConfig_ExplicitSettings_OverrideProfile()
    {
        var mcpConfig = new MCPSandboxConfig
        {
            Profile = "restrictive",
            NetworkMode = SandboxNetworkMode.Filtered,
            AllowedDomains = ["api.github.com"],
            EnableViolationMonitoring = true
        };
        var sandboxConfig = mcpConfig.ToSandboxConfig();

        sandboxConfig.NetworkMode.Should().Be(SandboxNetworkMode.Filtered);
        sandboxConfig.AllowedDomains.Should().BeEquivalentTo(["api.github.com"]);
        sandboxConfig.EnableViolationMonitoring.Should().BeTrue();
    }

    [Fact]
    public void ToSandboxConfig_AllowedDomainsWithoutNetworkMode_DoesNotEnableNetwork()
    {
        var mcpConfig = new MCPSandboxConfig
        {
            AllowedDomains = ["api.github.com"]
        };
        var sandboxConfig = mcpConfig.ToSandboxConfig();

        sandboxConfig.NetworkMode.Should().Be(SandboxNetworkMode.Blocked);
        sandboxConfig.AllowedDomains.Should().BeEquivalentTo(["api.github.com"]);
    }

    [Fact]
    public void ToSandboxConfig_ProfileIsCaseInsensitive()
    {
        var mcpConfig = new MCPSandboxConfig { Profile = "PERMISSIVE" };
        var sandboxConfig = mcpConfig.ToSandboxConfig();

        sandboxConfig.NetworkMode.Should().Be(SandboxNetworkMode.Unrestricted);
        sandboxConfig.AllowedDomains.Should().BeEmpty();
    }

    [Fact]
    public void ToSandboxConfig_UnknownProfile_UsesDefaults()
    {
        var mcpConfig = new MCPSandboxConfig { Profile = "unknown-profile" };
        var sandboxConfig = mcpConfig.ToSandboxConfig();

        // Falls through to default case which uses CreateDefault()
        sandboxConfig.NetworkMode.Should().Be(SandboxNetworkMode.Blocked);
        sandboxConfig.AllowedDomains.Should().BeEmpty();
    }

    [Fact]
    public void AllowAllUnixSockets_DefaultsFalse()
    {
        var config = new MCPSandboxConfig();

        config.AllowAllUnixSockets.Should().BeFalse();
    }

    [Fact]
    public void AllowLocalBinding_DefaultsFalse()
    {
        var config = new MCPSandboxConfig();

        config.AllowLocalBinding.Should().BeFalse();
    }

    [Fact]
    public void EnableViolationMonitoring_DefaultsFalse()
    {
        var config = new MCPSandboxConfig();

        config.EnableViolationMonitoring.Should().BeFalse();
    }

    [Fact]
    public void DeniedDomains_TakesPrecedenceOverAllowed()
    {
        var mcpConfig = new MCPSandboxConfig
        {
            NetworkMode = SandboxNetworkMode.Filtered,
            AllowedDomains = ["*.github.com"],
            DeniedDomains = ["malicious.github.com"]
        };
        var sandboxConfig = mcpConfig.ToSandboxConfig();

        sandboxConfig.NetworkMode.Should().Be(SandboxNetworkMode.Filtered);
        sandboxConfig.AllowedDomains.Should().Contain("*.github.com");
        sandboxConfig.DeniedDomains.Should().Contain("malicious.github.com");
    }
}
