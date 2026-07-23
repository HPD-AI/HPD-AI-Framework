namespace HPD.Agent.Sandbox.Local.Tests.ProcessIsolation;

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using FluentAssertions;
using HPD.Agent.Sandbox;
using HPD.Agent.Sandbox.Local;
using HPD.Agent.Sandbox.ProcessIsolation;
using HPD.Environment.Contracts;
using HPD.Environment.Runtime;
using Xunit;

public sealed class LocalSandboxProviderIntegrationTests
{
    [SkippableFact]
    [Trait("Category", "RealAdapter")]
    public async Task Netcoredbg_initializes_through_production_sidecar_isolation()
    {
        var adapter = System.Environment.GetEnvironmentVariable("HPD_NETCOREDBG");
        Skip.If(string.IsNullOrWhiteSpace(adapter) || !File.Exists(adapter),
            "Set HPD_NETCOREDBG to a netcoredbg executable to run this qualification.");
        Skip.If(RuntimeInformation.IsOSPlatform(OSPlatform.Windows),
            "Windows local OS sandboxing is unsupported.");
        await SkipIfLocalSandboxUnavailableAsync();

        await using var manager = new SandboxIsolationManager();
        var provider = new LocalProcessProvider(
            new SandboxIsolationPlanner(),
            new HostSandboxApplicator(manager));
        await using var handle = await provider.StartAsync(new ProcessInvocationSpec
        {
            Target = Handle<ExecutionUnit>(TargetRouteSegmentKind.ExecutionUnit, "netcoredbg"),
            Role = ProcessRole.Sidecar,
            Command = new ProcessCommandSpec
            {
                FileName = adapter!,
                Arguments = ["--interpreter=vscode"],
                WorkingDirectory = Path.GetTempPath(),
                Environment = new Dictionary<string, string?>()
            },
            Io = new ProcessIoSpec
            {
                StandardInput = new ProcessInputSpec { Kind = ProcessInputKind.Stream },
                StandardOutput = new ProcessOutputSpec { Capture = false, Stream = true },
                StandardError = new ProcessOutputSpec { Capture = false, Stream = true },
                MergeStandardError = false,
                LogPolicy = new ProcessLogPolicy { RetainOutputEvents = false }
            },
            Policy = new ProcessInvocationPolicy
            {
                AllowBackground = true,
                StopProcessTree = true,
                StopOnRunCancellation = false,
                OutputDrainTimeout = TimeSpan.FromSeconds(2)
            },
            Isolation = new ProcessIsolationPolicy
            {
                Mode = ProcessIsolationMode.Isolated,
                Network = NetworkEgressPolicy.Blocked,
                Interactive = new ProcessInteractivePolicy { AllowStdin = true },
                Environment = new EnvironmentAccessPolicy
                {
                    AllowedVariables = [],
                    StripUnlistedVariables = true
                },
                Degradation = ProcessIsolationDegradationPolicy.FailClosed
            },
            PersistResource = false,
            ObservationRetention = ObservationRetentionPolicy.ResultAndDiagnostics
        });

        const string body =
            """{"seq":1,"type":"request","command":"initialize","arguments":{"adapterID":"netcoredbg","clientID":"hpd","clientName":"HPD","columnsStartAt1":true,"linesStartAt1":true,"pathFormat":"path"}}""";
        await handle.WriteStdinAsync(
            Encoding.UTF8.GetBytes($"Content-Length: {Encoding.UTF8.GetByteCount(body)}\r\n\r\n{body}"));

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        try
        {
            await foreach (var chunk in handle.ReadOutputAsync(timeout.Token))
            {
                var text = Encoding.UTF8.GetString(chunk.Bytes.Span);
                if (chunk.Stream == ProcessOutputStream.Stdout)
                    stdout.Append(text);
                else
                    stderr.Append(text);
                if (stdout.ToString().Contains("\"command\":\"initialize\"", StringComparison.Ordinal))
                    break;
            }
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
        }

        stdout.ToString().Should().Contain(
            "\"command\":\"initialize\"",
            $"netcoredbg stderr was: {stderr}");

        var program = System.Environment.GetEnvironmentVariable("HPD_DEBUG_PROGRAM");
        if (string.IsNullOrWhiteSpace(program))
            return;

        var launchBody =
            """{"seq":2,"type":"request","command":"launch","arguments":{"request":"launch","program":""" +
            System.Text.Json.JsonSerializer.Serialize(program) +
            ""","cwd":""" +
            System.Text.Json.JsonSerializer.Serialize(Path.GetDirectoryName(program)) +
            ""","stopAtEntry":true,"noDebug":false}}""";
        await handle.WriteStdinAsync(
            Encoding.UTF8.GetBytes($"Content-Length: {Encoding.UTF8.GetByteCount(launchBody)}\r\n\r\n{launchBody}"));

        using var launchTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        try
        {
            await foreach (var chunk in handle.ReadOutputAsync(launchTimeout.Token))
            {
                var text = Encoding.UTF8.GetString(chunk.Bytes.Span);
                if (chunk.Stream == ProcessOutputStream.Stdout)
                    stdout.Append(text);
                else
                    stderr.Append(text);
                if (stdout.ToString().Contains("\"event\":\"initialized\"", StringComparison.Ordinal))
                    break;
            }
        }
        catch (OperationCanceledException) when (launchTimeout.IsCancellationRequested)
        {
        }

        stdout.ToString().Should().Contain(
            "\"event\":\"initialized\"",
            $"netcoredbg stderr was: {stderr}");

        var source = System.Environment.GetEnvironmentVariable("HPD_DEBUG_SOURCE");
        var nextSequence = 3;
        if (!string.IsNullOrWhiteSpace(source))
        {
            var setBreakpoints =
                """{"seq":3,"type":"request","command":"setBreakpoints","arguments":{"source":{"path":""" +
                System.Text.Json.JsonSerializer.Serialize(source) +
                """},"breakpoints":[{"line":7,"column":8}]}}""";
            await handle.WriteStdinAsync(
                Encoding.UTF8.GetBytes($"Content-Length: {Encoding.UTF8.GetByteCount(setBreakpoints)}\r\n\r\n{setBreakpoints}"));
            nextSequence++;
        }

        var exceptionFilter = System.Environment.GetEnvironmentVariable("HPD_DEBUG_EXCEPTION_FILTER");
        if (!string.IsNullOrWhiteSpace(exceptionFilter))
        {
            var setExceptionBreakpoints =
                """{"seq":""" + nextSequence +
                ""","type":"request","command":"setExceptionBreakpoints","arguments":{"filters":[""" +
                System.Text.Json.JsonSerializer.Serialize(exceptionFilter) +
                """]}}""";
            await handle.WriteStdinAsync(
                Encoding.UTF8.GetBytes($"Content-Length: {Encoding.UTF8.GetByteCount(setExceptionBreakpoints)}\r\n\r\n{setExceptionBreakpoints}"));
            nextSequence++;
        }

        var configurationDone =
            """{"seq":""" + nextSequence +
            ""","type":"request","command":"configurationDone","arguments":{}}""";
        await handle.WriteStdinAsync(
            Encoding.UTF8.GetBytes($"Content-Length: {Encoding.UTF8.GetByteCount(configurationDone)}\r\n\r\n{configurationDone}"));

        using var stopTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        try
        {
            await foreach (var chunk in handle.ReadOutputAsync(stopTimeout.Token))
            {
                var text = Encoding.UTF8.GetString(chunk.Bytes.Span);
                if (chunk.Stream == ProcessOutputStream.Stdout)
                    stdout.Append(text);
                else
                    stderr.Append(text);
                if (stdout.ToString().Contains("\"event\":\"stopped\"", StringComparison.Ordinal))
                    break;
            }
        }
        catch (OperationCanceledException) when (stopTimeout.IsCancellationRequested)
        {
        }

        stdout.ToString().Should().Contain(
            "\"event\":\"stopped\"",
            $"netcoredbg stderr was: {stderr}");
    }

    [Fact]
    public async Task Local_process_provider_registers_process_invocation_capability()
    {
        var registry = new EnvironmentProviderRegistry();

        registry.RegisterLocalProcessProvider();

        IReadOnlyList<ProviderDescriptor> providers = await registry.ListAsync();
        ProviderCapabilityReport report = await registry.GetCapabilitiesAsync(LocalProcessProvider.LocalProviderId);

        registry.ProcessProviders.Should().ContainSingle();
        providers.Should().ContainSingle(provider => provider.ContractKinds == ProviderContractKind.ProcessInvocation);
        report.Capabilities.Should().Contain(fact =>
            fact.AppliesTo == ProviderContractKind.ProcessInvocation &&
            fact.State == CapabilityState.Supported);
    }

    [Fact]
    public async Task Local_process_provider_runs_prepared_process_invocation()
    {
        var registry = new EnvironmentProviderRegistry();
        registry.RegisterLocalProcessProvider();
        IProcessProvider provider = registry.ProcessProviders.Single();

        ProcessInvocationSpec invocation = new()
        {
            Target = Handle<ExecutionUnit>(TargetRouteSegmentKind.ExecutionUnit, "unit-1"),
            Command = ShellInvocation("local-provider-ok"),
            Isolation = ProcessIsolationPolicy.Default with { Mode = ProcessIsolationMode.Disabled },
        };

        ProcessInvocationResult result = await provider.RunAsync(invocation);

        result.CompletionKind.Should().Be(ProcessCompletionKind.Completed);
        result.ExitCode.Should().Be(0);
        System.Text.Encoding.UTF8.GetString(result.Output.Stdout.CapturedBytes.ToArray())
            .Trim()
            .Should()
            .Be("local-provider-ok");
    }

    [SkippableFact]
    public async Task Execution_runtime_runs_through_sandbox_isolation_and_local_process_providers()
    {
        Skip.If(RuntimeInformation.IsOSPlatform(OSPlatform.Windows), "Windows local OS sandboxing is unsupported.");
        await SkipIfLocalSandboxUnavailableAsync();

        await using var manager = new SandboxIsolationManager();
        var registry = new EnvironmentProviderRegistry();
        registry.RegisterModule(new LocalProcessProviderModule(new LocalProcessProvider(
            new SandboxIsolationPlanner(),
            new HostSandboxApplicator(manager))));
        var runtime = new InMemoryEnvironmentRuntime(registry);

        ProcessInvocationSpec invocation = new()
        {
            Target = Handle<ExecutionUnit>(TargetRouteSegmentKind.ExecutionUnit, "unit-1"),
            Command = ShellInvocation("runtime-local-isolated-ok"),
            Isolation = MinimalExecutableIsolation(),
        };

        ProcessInvocationResult result = await runtime.RunProcessAsync(invocation);

        result.CompletionKind.Should().Be(ProcessCompletionKind.Completed);
        result.ExitCode.Should().Be(0);
        System.Text.Encoding.UTF8.GetString(result.Output.Stdout.CapturedBytes.ToArray())
            .Trim()
            .Should()
            .Be("runtime-local-isolated-ok");
    }

    [SkippableFact]
    public async Task Local_process_provider_enforces_filesystem_read_denials()
    {
        Skip.If(RuntimeInformation.IsOSPlatform(OSPlatform.Windows), "Windows local OS sandboxing is unsupported.");
        await SkipIfLocalSandboxUnavailableAsync();

        var root = Path.Combine(Path.GetTempPath(), "hpd-local-sandbox-test-" + Guid.NewGuid().ToString("N"));
        var publicDirectory = Path.Combine(root, "public");
        var secretDirectory = Path.Combine(root, "secret");
        var allowedFile = Path.Combine(publicDirectory, "allowed.txt");
        var secretFile = Path.Combine(secretDirectory, "secret.txt");

        Directory.CreateDirectory(publicDirectory);
        Directory.CreateDirectory(secretDirectory);
        await File.WriteAllTextAsync(allowedFile, "public");
        await File.WriteAllTextAsync(secretFile, "secret");

        try
        {
            await using var manager = new SandboxIsolationManager();
            var provider = new LocalProcessProvider(
                new SandboxIsolationPlanner(),
                new HostSandboxApplicator(manager));

            ProcessInvocationResult allowed = await provider.RunAsync(new ProcessInvocationSpec
            {
                Target = Handle<ExecutionUnit>(TargetRouteSegmentKind.ExecutionUnit, "unit-1"),
                Command = ReadFileInvocation(allowedFile),
                Isolation = FilesystemReadDenyIsolation(secretDirectory),
            });

            allowed.CompletionKind.Should().Be(ProcessCompletionKind.Completed);
            allowed.ExitCode.Should().Be(0);
            System.Text.Encoding.UTF8.GetString(allowed.Output.Stdout.CapturedBytes.ToArray())
                .Trim()
                .Should()
                .Be("public");

            ProcessInvocationResult denied = await provider.RunAsync(new ProcessInvocationSpec
            {
                Target = Handle<ExecutionUnit>(TargetRouteSegmentKind.ExecutionUnit, "unit-2"),
                Command = ReadFileInvocation(secretFile),
                Isolation = FilesystemReadDenyIsolation(secretDirectory),
            });

            denied.ExitCode.Should().NotBe(0);
            denied.CompletionKind.Should().Be(ProcessCompletionKind.Exited);
            System.Text.Encoding.UTF8.GetString(denied.Output.Stdout.CapturedBytes.ToArray())
                .Should()
                .BeEmpty();
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
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

    private static ProcessCommandSpec ShellInvocation(string message)
    {
        if (OperatingSystem.IsWindows())
        {
            return new ProcessCommandSpec
            {
                FileName = "cmd.exe",
                Arguments = ["/c", $"echo {message}"],
            };
        }

        return new ProcessCommandSpec
        {
            FileName = "/bin/sh",
            Arguments = ["-c", $"printf '%s\\n' {Quote(message)}"],
        };
    }

    private static ProcessCommandSpec ReadFileInvocation(string path) =>
        new()
        {
            FileName = "/bin/sh",
            Arguments = ["-c", $"cat {Quote(path)}"],
        };

    private static string Quote(string value) =>
        "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";

    private static ProcessIsolationPolicy MinimalExecutableIsolation() =>
        new()
        {
            Mode = ProcessIsolationMode.Isolated,
            Filesystem = new FilesystemAccessPolicy
            {
                DangerousPaths = DangerousPathPolicy.Default with
                {
                    ProtectSensitiveDefaults = false,
                },
            },
            Network = new NetworkEgressPolicy
            {
                Mode = NetworkEgressMode.Unrestricted,
            },
            Violations = ProcessViolationPolicy.Default with
            {
                Action = ProcessViolationAction.ObserveOnly,
            },
        };

    private static ProcessIsolationPolicy FilesystemReadDenyIsolation(string deniedDirectory) =>
        MinimalExecutableIsolation() with
        {
            Filesystem = new FilesystemAccessPolicy
            {
                Rules =
                [
                    new PathAccessRule
                    {
                        Kind = PathAccessRuleKind.DenyRead,
                        Path = new HostPath(deniedDirectory),
                        Reason = "test read boundary",
                    },
                ],
                DangerousPaths = DangerousPathPolicy.Default with
                {
                    ProtectSensitiveDefaults = false,
                },
            },
        };

    private static async Task SkipIfLocalSandboxUnavailableAsync()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            Skip.IfNot(await CommandExistsAsync("bwrap"), "bubblewrap is required for local Linux process isolation.");
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            Skip.IfNot(await CommandExistsAsync("sandbox-exec"), "sandbox-exec is required for local macOS process isolation.");
    }

    private static async Task<bool> CommandExistsAsync(string command)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "which",
                    Arguments = command,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                },
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
}
