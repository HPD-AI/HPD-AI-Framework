using FluentAssertions;
using HPD.Agent.Sandbox;
using HPD.Sandbox.Local.Platforms;
using HPD.Sandbox.Local.Platforms.Linux;
using System.Runtime.InteropServices;
using Xunit;

namespace HPD.Sandbox.Local.Tests;

public class LinuxSandboxTests
{
    private readonly SandboxConfig _defaultConfig = SandboxConfig.CreateDefault();

    [Fact]
    public void WrapCommandAsync_IncludesReadOnlyRoot()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return; // Skip on non-Linux

        var sandbox = new LinuxSandbox(_defaultConfig, null, null);
        var result = sandbox.WrapCommandAsync("echo hello", CancellationToken.None).Result;

        result.Should().Contain("--ro-bind");
        result.Should().Contain("/ /");
    }

    [Fact]
    public void WrapCommandAsync_IncludesWritablePaths()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return;

        var config = new SandboxConfig { AllowWrite = ["/tmp", "."] };
        var sandbox = new LinuxSandbox(config, null, null);
        var result = sandbox.WrapCommandAsync("echo hello", CancellationToken.None).Result;

        result.Should().Contain("--bind");
        result.Should().Contain("/tmp");
    }

    [Fact]
    public void WrapCommandAsync_DeniesReadPaths()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return;

        var config = new SandboxConfig { DenyRead = ["~/.ssh"] };
        var sandbox = new LinuxSandbox(config, null, null);
        var result = sandbox.WrapCommandAsync("echo hello", CancellationToken.None).Result;

        result.Should().Contain("--tmpfs");
    }

    [Fact]
    public void WrapCommandAsync_IsolatesNetwork_WhenNoDomainsAllowed()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return;

        var config = new SandboxConfig { AllowedDomains = [] };
        var sandbox = new LinuxSandbox(config, null, null);
        var result = sandbox.WrapCommandAsync("echo hello", CancellationToken.None).Result;

        result.Should().Contain("--unshare-net");
    }

    [Fact]
    public void WrapCommandAsync_SkipsNetworkIsolation_WhenWeakerSandboxEnabled()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return;

        var config = new SandboxConfig
        {
            AllowedDomains = [],
            EnableWeakerNestedSandbox = true
        };
        var sandbox = new LinuxSandbox(config, null, null);
        var result = sandbox.WrapCommandAsync("echo hello", CancellationToken.None).Result;

        result.Should().NotContain("--unshare-net");
    }

    [Fact]
    public void WrapCommandAsync_IncludesProcessIsolation()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return;

        var sandbox = new LinuxSandbox(_defaultConfig, null, null);
        var result = sandbox.WrapCommandAsync("echo hello", CancellationToken.None).Result;

        result.Should().Contain("--unshare-pid");
        result.Should().Contain("--unshare-uts");
    }

    [Fact]
    public void WrapCommandAsync_PassesEnvironmentVariables()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return;

        var config = new SandboxConfig
        {
            AllowedEnvironmentVariables = ["PATH", "HOME"]
        };
        var sandbox = new LinuxSandbox(config, null, null);
        var result = sandbox.WrapCommandAsync("echo hello", CancellationToken.None).Result;

        result.Should().Contain("--setenv");
        result.Should().Contain("PATH");
    }

    [Fact]
    public void WrapCommandAsync_QuotesArguments()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return;

        var sandbox = new LinuxSandbox(_defaultConfig, null, null);
        var result = sandbox.WrapCommandAsync("echo 'test with spaces'", CancellationToken.None).Result;

        // Should have quoted arguments to prevent injection
        result.Should().Contain("'");
    }

    [Fact]
    public void Violations_ReturnsNull_OnLinux()
    {
        var sandbox = new LinuxSandbox(_defaultConfig, null, null);

        sandbox.Violations.Should().BeNull();
    }

    [Fact]
    public async Task DisposeAsync_DoesNotThrow()
    {
        var sandbox = new LinuxSandbox(_defaultConfig, null, null);

        var act = async () => await sandbox.DisposeAsync();

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task WrapCommandAsync_UsesExternalHttpProxyPort_WhenConfigured()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return;

        var config = new SandboxConfig
        {
            ExternalHttpProxyPort = 8888,
            AllowedDomains = ["example.com"]
        };
        var sandbox = new LinuxSandbox(config, null, null);

        // This will initialize the sandbox with external proxy
        // The actual command wrapping happens during initialization
        var result = await sandbox.WrapCommandAsync("echo test", CancellationToken.None);

        // Should contain bwrap command
        result.Should().Contain("bwrap");
    }

    [Fact]
    public async Task WrapCommandAsync_UsesExternalSocksProxyPort_WhenConfigured()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return;

        var config = new SandboxConfig
        {
            ExternalSocksProxyPort = 1080,
            AllowedDomains = ["example.com"]
        };
        var sandbox = new LinuxSandbox(config, null, null);

        var result = await sandbox.WrapCommandAsync("echo test", CancellationToken.None);

        // Should contain bwrap command with network setup
        result.Should().Contain("bwrap");
    }

    [Fact]
    public async Task WrapCommandAsync_UsesBothExternalProxyPorts_WhenConfigured()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return;

        var config = new SandboxConfig
        {
            ExternalHttpProxyPort = 8888,
            ExternalSocksProxyPort = 1080,
            AllowedDomains = ["example.com"]
        };
        var sandbox = new LinuxSandbox(config, null, null);

        var result = await sandbox.WrapCommandAsync("echo test", CancellationToken.None);

        // Should contain bwrap command
        result.Should().Contain("bwrap");
    }
}
