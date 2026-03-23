using FluentAssertions;
using HPD.Agent.Sandbox;
using Xunit;

namespace HPD.Sandbox.Local.Tests;

public class SandboxConfigTests
{
    [Fact]
    public void CreateDefault_ReturnsRestrictiveConfig()
    {
        var config = SandboxConfig.CreateDefault();

        config.AllowWrite.Should().BeEquivalentTo([".", "/tmp"]);
        config.DenyRead.Should().BeEquivalentTo(["~/.ssh", "~/.aws", "~/.gnupg"]);
        config.AllowedDomains.Should().BeEmpty();
        config.OnInitializationFailure.Should().Be(SandboxFailureBehavior.Block);
        config.OnViolation.Should().Be(SandboxViolationBehavior.EmitEvent);
    }

    [Fact]
    public void CreatePermissive_AllowsNetworkAndMinimalRestrictions()
    {
        var config = SandboxConfig.CreatePermissive();

        config.DenyRead.Should().BeEmpty();
        config.AllowedDomains.Should().BeNull(); // null = no filtering
    }

    [Fact]
    public void CreateForMCP_HasMcpOptimizedDefaults()
    {
        var config = SandboxConfig.CreateForMCP();

        config.AllowedDomains.Should().Contain("*.npmjs.org");
        config.AllowedDomains.Should().Contain("*.pypi.org");
        config.DenyRead.Should().Contain("~/.ssh");
        config.DenyRead.Should().Contain("~/.config");
        config.EnableViolationMonitoring.Should().BeTrue();
    }

    [Fact]
    public void Validate_ThrowsWhenNoWritablePaths()
    {
        var config = new SandboxConfig { AllowWrite = [] };

        var act = () => config.Validate();

        act.Should().Throw<ArgumentException>()
            .WithMessage("*writable path*");
    }

    [Fact]
    public void Validate_ThrowsWhenPathIsEmpty()
    {
        var config = new SandboxConfig { AllowWrite = [".", ""] };

        var act = () => config.Validate();

        act.Should().Throw<ArgumentException>()
            .WithMessage("*empty*");
    }

    [Fact]
    public void Validate_ThrowsWhenDomainPatternIsEmpty()
    {
        var config = new SandboxConfig { AllowedDomains = ["github.com", ""] };

        var act = () => config.Validate();

        act.Should().Throw<ArgumentException>()
            .WithMessage("*Domain*empty*");
    }

    [Fact]
    public void Validate_PassesForValidConfig()
    {
        var config = SandboxConfig.CreateDefault();

        var act = () => config.Validate();

        act.Should().NotThrow();
    }

    [Fact]
    public void Config_CanBeModifiedWithWith()
    {
        var config = SandboxConfig.CreateDefault() with
        {
            AllowedDomains = ["api.github.com"],
            EnableViolationMonitoring = true
        };

        config.AllowedDomains.Should().BeEquivalentTo(["api.github.com"]);
        config.EnableViolationMonitoring.Should().BeTrue();
        // Original defaults preserved
        config.AllowWrite.Should().BeEquivalentTo([".", "/tmp"]);
    }

    [Fact]
    public void AllowedEnvironmentVariables_HasSafeDefaults()
    {
        var config = SandboxConfig.CreateDefault();

        config.AllowedEnvironmentVariables.Should().Contain("PATH");
        config.AllowedEnvironmentVariables.Should().Contain("HOME");
        config.AllowedEnvironmentVariables.Should().Contain("TERM");
        config.AllowedEnvironmentVariables.Should().Contain("LANG");
    }

    [Fact]
    public void EnableWeakerNestedSandbox_DefaultsFalse()
    {
        var config = SandboxConfig.CreateDefault();

        config.EnableWeakerNestedSandbox.Should().BeFalse();
    }

    [Fact]
    public void ExternalHttpProxyPort_DefaultsToNull()
    {
        var config = SandboxConfig.CreateDefault();

        config.ExternalHttpProxyPort.Should().BeNull();
    }

    [Fact]
    public void ExternalSocksProxyPort_DefaultsToNull()
    {
        var config = SandboxConfig.CreateDefault();

        config.ExternalSocksProxyPort.Should().BeNull();
    }

    [Fact]
    public void ExternalProxyPorts_CanBeSet()
    {
        var config = SandboxConfig.CreateDefault() with
        {
            ExternalHttpProxyPort = 8080,
            ExternalSocksProxyPort = 1080
        };

        config.ExternalHttpProxyPort.Should().Be(8080);
        config.ExternalSocksProxyPort.Should().Be(1080);
    }

    [Fact]
    public void AllowUnixSockets_DefaultsToNull()
    {
        var config = SandboxConfig.CreateDefault();

        config.AllowUnixSockets.Should().BeNull();
    }

    [Fact]
    public void AllowUnixSockets_CanBeSet()
    {
        var config = SandboxConfig.CreateDefault() with
        {
            AllowUnixSockets = ["/var/run/docker.sock", "/tmp/ssh-agent.sock"]
        };

        config.AllowUnixSockets.Should().NotBeNull();
        config.AllowUnixSockets.Should().HaveCount(2);
        config.AllowUnixSockets.Should().Contain("/var/run/docker.sock");
        config.AllowUnixSockets.Should().Contain("/tmp/ssh-agent.sock");
    }

    [Fact]
    public void AllowAllUnixSockets_DefaultsFalse()
    {
        var config = SandboxConfig.CreateDefault();

        config.AllowAllUnixSockets.Should().BeFalse();
    }

    [Fact]
    public void AllowAllUnixSockets_CanBeEnabled()
    {
        var config = SandboxConfig.CreateDefault() with
        {
            AllowAllUnixSockets = true
        };

        config.AllowAllUnixSockets.Should().BeTrue();
    }
}
