using System.Diagnostics;
using System.Runtime.InteropServices;
using FluentAssertions;
using HPD.Agent.Sandbox;
using HPD.Sandbox.Local.Network;
using HPD.Sandbox.Local.Platforms;
using HPD.Sandbox.Local.Platforms.Linux;
using HPD.Sandbox.Local.Platforms.MacOS;
using Xunit;
using Xunit.Abstractions;

namespace HPD.Sandbox.Local.Tests;

/// <summary>
/// Integration tests that execute actual sandboxed commands.
/// These tests require platform-specific sandbox tools to be installed:
/// - macOS: sandbox-exec (built-in)
/// - Linux: bwrap (bubblewrap)
/// - Windows: Tests verify unsupported behavior
/// </summary>
[Collection("SandboxIntegration")]
public class SandboxIntegrationTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private IPlatformSandbox? _sandbox;
    private readonly SandboxConfig _config;

    public SandboxIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
        _config = SandboxConfig.CreateDefault();
    }

    public async Task InitializeAsync()
    {
        var platform = PlatformDetector.Current;
        _sandbox = platform switch
        {
            PlatformType.Linux => new LinuxSandbox(_config, null, null),
            PlatformType.MacOS => new MacOSSandbox(_config, null, null),
            PlatformType.Windows => new WindowsSandbox(_config),
            _ => null
        };

        if (_sandbox != null && platform != PlatformType.Windows)
        {
            var hasDeps = await _sandbox.CheckDependenciesAsync(CancellationToken.None);
            if (!hasDeps)
            {
                _output.WriteLine($"Sandbox dependencies not available on {platform}. Integration tests will be skipped.");
                _sandbox = null;
            }
        }
    }

    public async Task DisposeAsync()
    {
        if (_sandbox != null)
            await _sandbox.DisposeAsync();
    }

    [SkippableFact]
    public async Task SandboxedCommand_CanExecuteBasicCommand()
    {
        Skip.If(_sandbox == null, "Sandbox not available on this platform");
        Skip.If(RuntimeInformation.IsOSPlatform(OSPlatform.Windows), "Windows sandboxing not supported");

        var wrappedCommand = await _sandbox!.WrapCommandAsync("echo hello", CancellationToken.None);
        _output.WriteLine($"Wrapped command: {wrappedCommand}");

        var result = await ExecuteCommandAsync(wrappedCommand);

        result.ExitCode.Should().Be(0);
        result.Output.Trim().Should().Be("hello");
    }

    [SkippableFact]
    public async Task SandboxedCommand_CanReadCurrentDirectory()
    {
        Skip.If(_sandbox == null, "Sandbox not available on this platform");
        Skip.If(RuntimeInformation.IsOSPlatform(OSPlatform.Windows), "Windows sandboxing not supported");

        var wrappedCommand = await _sandbox!.WrapCommandAsync("ls -la", CancellationToken.None);

        var result = await ExecuteCommandAsync(wrappedCommand);

        result.ExitCode.Should().Be(0);
        result.Output.Should().NotBeEmpty();
    }

    [SkippableFact]
    public async Task SandboxedCommand_CanWriteToTmp()
    {
        Skip.If(_sandbox == null, "Sandbox not available on this platform");
        Skip.If(RuntimeInformation.IsOSPlatform(OSPlatform.Windows), "Windows sandboxing not supported");

        var testFile = $"/tmp/sandbox_test_{Guid.NewGuid()}.txt";
        var wrappedCommand = await _sandbox!.WrapCommandAsync(
            $"echo 'test content' > {testFile} && cat {testFile}",
            CancellationToken.None);

        var result = await ExecuteCommandAsync(wrappedCommand);

        result.ExitCode.Should().Be(0);
        result.Output.Trim().Should().Be("test content");
    }

    [SkippableFact]
    public async Task SandboxedCommand_BlocksAccessToSshDirectory()
    {
        Skip.If(_sandbox == null, "Sandbox not available on this platform");
        Skip.If(RuntimeInformation.IsOSPlatform(OSPlatform.Windows), "Windows sandboxing not supported");

        var sshDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh");
        Skip.IfNot(Directory.Exists(sshDir), "~/.ssh directory does not exist");

        var wrappedCommand = await _sandbox!.WrapCommandAsync("ls ~/.ssh", CancellationToken.None);
        _output.WriteLine($"Testing blocked access to: {sshDir}");

        var result = await ExecuteCommandAsync(wrappedCommand);

        // Should either fail or show empty directory (depending on platform implementation)
        // On macOS with sandbox-exec, it may return an error
        // On Linux with bwrap --tmpfs, it returns empty
        var accessBlocked = result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.Output);
        accessBlocked.Should().BeTrue("SSH directory should be blocked or hidden");
    }

    [SkippableFact]
    public async Task SandboxedCommand_CanAccessEnvironmentVariables()
    {
        Skip.If(_sandbox == null, "Sandbox not available on this platform");
        Skip.If(RuntimeInformation.IsOSPlatform(OSPlatform.Windows), "Windows sandboxing not supported");

        var wrappedCommand = await _sandbox!.WrapCommandAsync("echo $PATH", CancellationToken.None);

        var result = await ExecuteCommandAsync(wrappedCommand);

        result.ExitCode.Should().Be(0);
        result.Output.Should().NotBeEmpty("PATH environment variable should be accessible");
    }

    [SkippableFact]
    public async Task SandboxedCommand_HandlesSpecialCharacters()
    {
        Skip.If(_sandbox == null, "Sandbox not available on this platform");
        Skip.If(RuntimeInformation.IsOSPlatform(OSPlatform.Windows), "Windows sandboxing not supported");

        // Test that special characters don't cause command injection
        var wrappedCommand = await _sandbox!.WrapCommandAsync(
            "echo 'test with $VAR and `backticks` and $(subshell)'",
            CancellationToken.None);

        var result = await ExecuteCommandAsync(wrappedCommand);

        result.ExitCode.Should().Be(0);
        // The special characters should be preserved literally, not interpreted
        result.Output.Should().Contain("$VAR");
    }

    [SkippableFact]
    public async Task SandboxedCommand_ProcessIsolation_CantSeeHostProcesses()
    {
        Skip.If(_sandbox == null, "Sandbox not available on this platform");
        Skip.If(!RuntimeInformation.IsOSPlatform(OSPlatform.Linux), "Process isolation test only relevant on Linux");

        var wrappedCommand = await _sandbox!.WrapCommandAsync("ps aux | wc -l", CancellationToken.None);

        var result = await ExecuteCommandAsync(wrappedCommand);

        // In an isolated PID namespace, should see very few processes
        if (result.ExitCode == 0 && int.TryParse(result.Output.Trim(), out var processCount))
        {
            _output.WriteLine($"Sandboxed process sees {processCount} processes");
            // Should see significantly fewer processes than the host
            // Typically just the shell and ps command (< 10 processes)
            processCount.Should().BeLessThan(20, "Sandboxed process should see isolated process namespace");
        }
    }

    private async Task<CommandResult> ExecuteCommandAsync(string command)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "/bin/sh",
                Arguments = $"-c \"{command.Replace("\"", "\\\"")}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            }
        };

        process.Start();
        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (!string.IsNullOrEmpty(error))
            _output.WriteLine($"stderr: {error}");

        return new CommandResult(process.ExitCode, output, error);
    }

    private record CommandResult(int ExitCode, string Output, string Error);
}

/// <summary>
/// Tests for Windows sandbox stub behavior.
/// </summary>
public class WindowsSandboxTests
{
    [Fact]
    public async Task CheckDependenciesAsync_ReturnsFalse()
    {
        var config = SandboxConfig.CreateDefault();
        var sandbox = new WindowsSandbox(config);

        var result = await sandbox.CheckDependenciesAsync(CancellationToken.None);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task WrapCommandAsync_WithBlockBehavior_Throws()
    {
        var config = SandboxConfig.CreateDefault() with
        {
            OnInitializationFailure = SandboxFailureBehavior.Block
        };
        var sandbox = new WindowsSandbox(config);

        var act = async () => await sandbox.WrapCommandAsync("echo hello", CancellationToken.None);

        await act.Should().ThrowAsync<PlatformNotSupportedException>()
            .WithMessage("*Windows*");
    }

    [Fact]
    public async Task WrapCommandAsync_WithWarnBehavior_ReturnsOriginalCommand()
    {
        var config = SandboxConfig.CreateDefault() with
        {
            OnInitializationFailure = SandboxFailureBehavior.Warn
        };
        var sandbox = new WindowsSandbox(config);

        var result = await sandbox.WrapCommandAsync("echo hello", CancellationToken.None);

        result.Should().Be("echo hello");
    }

    [Fact]
    public async Task WrapCommandAsync_WithIgnoreBehavior_ReturnsOriginalCommand()
    {
        var config = SandboxConfig.CreateDefault() with
        {
            OnInitializationFailure = SandboxFailureBehavior.Ignore
        };
        var sandbox = new WindowsSandbox(config);

        var result = await sandbox.WrapCommandAsync("echo hello", CancellationToken.None);

        result.Should().Be("echo hello");
    }

    [Fact]
    public void Violations_ReturnsNull()
    {
        var config = SandboxConfig.CreateDefault();
        var sandbox = new WindowsSandbox(config);

        sandbox.Violations.Should().BeNull();
    }

    [Fact]
    public async Task DisposeAsync_DoesNotThrow()
    {
        var config = SandboxConfig.CreateDefault();
        var sandbox = new WindowsSandbox(config);

        var act = async () => await sandbox.DisposeAsync();

        await act.Should().NotThrowAsync();
    }
}

/// <summary>
/// HTTP proxy integration tests.
/// </summary>
[Collection("SandboxIntegration")]
public class HttpProxyIntegrationTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private HttpProxyServer? _proxy;

    public HttpProxyIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    public async Task InitializeAsync()
    {
        try
        {
            _proxy = new HttpProxyServer(["api.github.com"], []);
            await _proxy.StartAsync(CancellationToken.None);
            _output.WriteLine($"HTTP proxy started on port {_proxy.Port}");
        }
        catch (Exception ex)
        {
            _output.WriteLine($"Failed to start proxy: {ex.Message}");
            _proxy = null;
        }
    }

    public async Task DisposeAsync()
    {
        if (_proxy != null)
            await _proxy.DisposeAsync();
    }

    [SkippableFact]
    public void Proxy_StartsAndListens()
    {
        Skip.If(_proxy == null, "Proxy failed to start");

        _proxy!.Port.Should().BeGreaterThan(0);
        _proxy.Port.Should().BeLessThan(65536);
    }

    [SkippableFact]
    public async Task Proxy_AllowsConfiguredDomain()
    {
        Skip.If(_proxy == null, "Proxy failed to start");
        Skip.If(!await HasCurlAsync(), "curl not available");

        // Use curl through the proxy to access an allowed domain
        var result = await RunCurlAsync(
            $"https://api.github.com/zen",
            _proxy!.Port);

        _output.WriteLine($"Response: {result.Output}");
        _output.WriteLine($"Exit code: {result.ExitCode}");

        // The request might fail due to SSL/TLS issues with MITM proxy,
        // but it should attempt the connection (not immediately rejected)
        // A status code response indicates the proxy processed the request
        result.Output.Should().NotContain("connection refused", "Proxy should accept connection");
    }

    [SkippableFact]
    public async Task Proxy_BlocksUnauthorizedDomain()
    {
        Skip.If(_proxy == null, "Proxy failed to start");
        Skip.If(!await HasCurlAsync(), "curl not available");

        // Try to access a domain not in the allowed list
        // Use HTTP (not HTTPS) so the proxy can intercept the request
        // HTTPS uses CONNECT tunneling which bypasses request filtering
        var result = await RunCurlAsync(
            "http://example.com",
            _proxy!.Port);

        _output.WriteLine($"Response: {result.Output}");
        _output.WriteLine($"Exit code: {result.ExitCode}");

        // Should be blocked - proxy returns 403 with "Access denied by sandbox"
        var wasBlocked = result.Output.Contains("403") ||
                         result.Output.Contains("Access denied") ||
                         result.Output.Contains("Forbidden");

        wasBlocked.Should().BeTrue("Unauthorized domain should be blocked with 403 Forbidden");
    }

    private static async Task<bool> HasCurlAsync()
    {
        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "which",
                    Arguments = "curl",
                    RedirectStandardOutput = true,
                    UseShellExecute = false
                }
            };
            process.Start();
            await process.WaitForExitAsync();
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<(int ExitCode, string Output)> RunCurlAsync(string url, int proxyPort)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "curl",
                Arguments = $"-x http://127.0.0.1:{proxyPort} --connect-timeout 5 -s -S {url}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            }
        };

        process.Start();
        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        return (process.ExitCode, output + error);
    }
}

/// <summary>
/// SOCKS5 proxy integration tests.
/// </summary>
[Collection("SandboxIntegration")]
public class Socks5ProxyIntegrationTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private Socks5ProxyServer? _proxy;

    public Socks5ProxyIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    public async Task InitializeAsync()
    {
        try
        {
            _proxy = new Socks5ProxyServer(["localhost"], []);
            var port = await _proxy.StartAsync();
            _output.WriteLine($"SOCKS5 proxy started on port {port}");
        }
        catch (Exception ex)
        {
            _output.WriteLine($"Failed to start SOCKS5 proxy: {ex.Message}");
            _proxy = null;
        }
    }

    public async Task DisposeAsync()
    {
        if (_proxy != null)
            await _proxy.DisposeAsync();
    }

    [SkippableFact]
    public void Proxy_StartsSuccessfully()
    {
        Skip.If(_proxy == null, "SOCKS5 proxy failed to start");

        _proxy!.Port.Should().BeGreaterThan(0);
    }

    [SkippableFact]
    public async Task Proxy_AcceptsConnections()
    {
        Skip.If(_proxy == null, "SOCKS5 proxy failed to start");

        using var client = new System.Net.Sockets.TcpClient();
        var connectTask = client.ConnectAsync(System.Net.IPAddress.Loopback, _proxy!.Port);
        var completed = await Task.WhenAny(connectTask, Task.Delay(2000));

        completed.Should().Be(connectTask, "Should connect within timeout");
        client.Connected.Should().BeTrue();
    }

    [SkippableFact]
    public async Task Proxy_CompletesSOCKS5Handshake()
    {
        Skip.If(_proxy == null, "SOCKS5 proxy failed to start");

        using var client = new System.Net.Sockets.TcpClient();
        await client.ConnectAsync(System.Net.IPAddress.Loopback, _proxy!.Port);

        await using var stream = client.GetStream();

        // Send SOCKS5 greeting
        var greeting = new byte[] { 0x05, 0x01, 0x00 }; // Version 5, 1 method, no auth
        await stream.WriteAsync(greeting);

        // Read response
        var response = new byte[2];
        var bytesRead = await stream.ReadAsync(response);

        bytesRead.Should().Be(2);
        response[0].Should().Be(0x05, "Version should be 5");
        response[1].Should().Be(0x00, "Should accept no-auth method");
    }
}
