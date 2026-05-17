using FluentAssertions;
using HPD.Agent.Sandbox;
using HPD.Sandbox.Local.Policy;
using Xunit;

namespace HPD.Sandbox.Local.Tests.Policy;

public sealed class SandboxPolicyBuilderTests
{
    [Fact]
    public void FromConfig_MapsUnrestrictedNetworkMode()
    {
        var config = SandboxConfig.CreateDefault() with
        {
            NetworkMode = SandboxNetworkMode.Unrestricted,
        };

        var policy = SandboxPolicyBuilder.FromConfig(config);

        policy.Network.Mode.Should().Be(SandboxNetworkMode.Unrestricted);
    }

    [Fact]
    public void FromConfig_MapsBlockedNetworkMode()
    {
        var config = SandboxConfig.CreateDefault() with
        {
            NetworkMode = SandboxNetworkMode.Blocked,
            AllowedDomains = [],
        };

        var policy = SandboxPolicyBuilder.FromConfig(config);

        policy.Network.Mode.Should().Be(SandboxNetworkMode.Blocked);
    }

    [Fact]
    public void FromConfig_MapsFilteredNetworkMode()
    {
        var config = SandboxConfig.CreateDefault() with
        {
            NetworkMode = SandboxNetworkMode.Filtered,
            AllowedDomains = ["*.example.com"],
            DeniedDomains = ["api.example.com"],
        };

        var policy = SandboxPolicyBuilder.FromConfig(config);

        policy.Network.Mode.Should().Be(SandboxNetworkMode.Filtered);
        policy.Network.AllowedDomains.Should().ContainSingle()
            .Which.Kind.Should().Be(DomainPatternKind.WildcardSubdomain);
        policy.Network.DeniedDomains.Should().ContainSingle()
            .Which.Canonical.Should().Be("api.example.com");
    }

    [Fact]
    public void FromConfig_BlockedNetworkIgnoresAllowedDomainList()
    {
        var config = SandboxConfig.CreateDefault() with
        {
            NetworkMode = SandboxNetworkMode.Blocked,
            AllowedDomains = ["api.example.com"],
        };

        var policy = SandboxPolicyBuilder.FromConfig(config);

        policy.Network.Mode.Should().Be(SandboxNetworkMode.Blocked);
    }

    [Fact]
    public void FromConfig_MapsExplicitFilteredNetwork()
    {
        var config = SandboxConfig.CreateDefault() with
        {
            NetworkMode = SandboxNetworkMode.Filtered,
            AllowedDomains = ["api.example.com"],
        };

        var policy = SandboxPolicyBuilder.FromConfig(config);

        policy.Network.Mode.Should().Be(SandboxNetworkMode.Filtered);
        policy.Network.AllowedDomains.Should().ContainSingle()
            .Which.Canonical.Should().Be("api.example.com");
    }

    [Fact]
    public void FromConfig_PreservesFilesystemReadAllowBacks()
    {
        var config = SandboxConfig.CreateDefault() with
        {
            DenyRead = ["~"],
            AllowRead = ["~/work/project"],
            AllowWrite = ["."],
            DenyWrite = [".git/hooks"],
            AllowGitConfig = true,
        };

        var policy = SandboxPolicyBuilder.FromConfig(config);

        policy.Filesystem.DenyRead.Should().BeEquivalentTo(["~"]);
        policy.Filesystem.AllowRead.Should().BeEquivalentTo(["~/work/project"]);
        policy.Filesystem.AllowWrite.Should().BeEquivalentTo(["."]);
        policy.Filesystem.DenyWrite.Should().BeEquivalentTo([".git/hooks"]);
        policy.Filesystem.AllowGitConfig.Should().BeTrue();
    }

    [Fact]
    public void FromConfig_PreservesUnixSocketPolicy()
    {
        var config = SandboxConfig.CreateDefault() with
        {
            AllowAllUnixSockets = true,
            AllowUnixSockets = ["/var/run/docker.sock"],
        };

        var policy = SandboxPolicyBuilder.FromConfig(config);

        policy.UnixSockets.AllowAll.Should().BeTrue();
        policy.UnixSockets.AllowedPaths.Should().BeEquivalentTo(["/var/run/docker.sock"]);
    }

    [Fact]
    public void FromConfig_PreservesMacOSTrustdLookupFlag()
    {
        var config = SandboxConfig.CreateDefault() with
        {
            AllowMacOSTrustdLookup = true,
        };

        var policy = SandboxPolicyBuilder.FromConfig(config);

        policy.AllowMacOSTrustdLookup.Should().BeTrue();
    }

    [Fact]
    public void FromConfig_RejectsInvalidDomainPatterns()
    {
        var config = SandboxConfig.CreateDefault() with
        {
            NetworkMode = SandboxNetworkMode.Filtered,
            AllowedDomains = ["*.com"],
        };

        var act = () => SandboxPolicyBuilder.FromConfig(config);

        act.Should().Throw<ArgumentException>();
    }
}
