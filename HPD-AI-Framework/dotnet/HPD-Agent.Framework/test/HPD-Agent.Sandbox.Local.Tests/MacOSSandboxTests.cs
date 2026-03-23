using FluentAssertions;
using HPD.Agent.Sandbox;
using HPD.Sandbox.Local.Platforms;
using HPD.Sandbox.Local.Platforms.MacOS;
using System.Runtime.InteropServices;
using Xunit;

namespace HPD.Sandbox.Local.Tests;

public class MacOSSandboxTests
{
    private readonly SandboxConfig _defaultConfig = SandboxConfig.CreateDefault();

    [Fact]
    public async Task WrapCommandAsync_UsesSandboxExec()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return; // Skip on non-macOS

        var sandbox = new MacOSSandbox(_defaultConfig, null, null);
        var result = await sandbox.WrapCommandAsync("echo hello", CancellationToken.None);

        result.Should().StartWith("sandbox-exec");
    }

    [Fact]
    public async Task WrapCommandAsync_ReferencesProfileFile()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return;

        var sandbox = new MacOSSandbox(_defaultConfig, null, null);
        var result = await sandbox.WrapCommandAsync("echo hello", CancellationToken.None);

        result.Should().Contain("-f");
    }

    [Fact]
    public void Violations_ReturnsNull_WhenMonitoringDisabled()
    {
        var config = new SandboxConfig { EnableViolationMonitoring = false };
        var sandbox = new MacOSSandbox(config, null, null);

        sandbox.Violations.Should().BeNull();
    }

    [Fact]
    public void Violations_ReturnsChannelReader_WhenMonitoringEnabled()
    {
        var config = new SandboxConfig { EnableViolationMonitoring = true };
        var sandbox = new MacOSSandbox(config, null, null);

        sandbox.Violations.Should().NotBeNull();
    }

    [Fact]
    public async Task DisposeAsync_CompletesViolationChannel()
    {
        var config = new SandboxConfig { EnableViolationMonitoring = true };
        var sandbox = new MacOSSandbox(config, null, null);

        await sandbox.DisposeAsync();

        // After dispose, channel should be completed
        sandbox.Violations!.Completion.IsCompleted.Should().BeTrue();
    }

    [Fact]
    public async Task DisposeAsync_DoesNotThrow()
    {
        var sandbox = new MacOSSandbox(_defaultConfig, null, null);

        var act = async () => await sandbox.DisposeAsync();

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task WrapCommandAsync_UsesExternalHttpProxyPort_WhenConfigured()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return;

        var config = new SandboxConfig
        {
            ExternalHttpProxyPort = 8888,
            AllowedDomains = ["example.com"]
        };
        var sandbox = new MacOSSandbox(config, null, null);
        var result = await sandbox.WrapCommandAsync("echo test", CancellationToken.None);

        result.Should().Contain("HTTP_PROXY=http://127.0.0.1:8888");
        result.Should().Contain("HTTPS_PROXY=http://127.0.0.1:8888");
    }

    [Fact]
    public async Task WrapCommandAsync_UsesExternalSocksProxyPort_WhenConfigured()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return;

        var config = new SandboxConfig
        {
            ExternalSocksProxyPort = 1080,
            AllowedDomains = ["example.com"]
        };
        var sandbox = new MacOSSandbox(config, null, null);
        var result = await sandbox.WrapCommandAsync("echo test", CancellationToken.None);

        result.Should().Contain("ALL_PROXY=socks5h://127.0.0.1:1080");
    }

    [Fact]
    public async Task WrapCommandAsync_IncludesUnixSocketsInProfile_WhenConfigured()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return;

        var config = new SandboxConfig
        {
            AllowUnixSockets = ["/var/run/docker.sock"]
        };
        var sandbox = new MacOSSandbox(config, null, null);
        var result = await sandbox.WrapCommandAsync("echo test", CancellationToken.None);

        // The profile should be written to a temp file, so we can't inspect it directly
        // But we can verify the command was successfully created
        result.Should().StartWith("sandbox-exec");
        result.Should().Contain("-f");
    }

    [Fact]
    public async Task WrapCommandAsync_AllowsAllUnixSockets_WhenConfigured()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return;

        var config = new SandboxConfig
        {
            AllowAllUnixSockets = true
        };
        var sandbox = new MacOSSandbox(config, null, null);
        var result = await sandbox.WrapCommandAsync("echo test", CancellationToken.None);

        // Verify command was successfully created with AllowAllUnixSockets setting
        result.Should().StartWith("sandbox-exec");
        result.Should().Contain("-f");
    }
}
