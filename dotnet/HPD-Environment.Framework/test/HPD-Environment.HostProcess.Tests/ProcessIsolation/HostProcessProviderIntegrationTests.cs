namespace HPD.Environment.HostProcess.Tests.ProcessIsolation;

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using FluentAssertions;
using HPD.Environment.HostProcess;
using HPD.Environment.HostProcess.ProcessIsolation;
using HPD.Environment.Contracts;
using HPD.Environment.Runtime;
using HPD.Environment.Runtime.Workspaces;
using HPD.Environment.Search.Ripgrep;
using Xunit;

public sealed class HostProcessProviderIntegrationTests
{
    [Theory]
    [InlineData("Darwin", "arm64", "osx-arm64")]
    [InlineData("Darwin", "x86_64", "osx-x64")]
    [InlineData("Linux", "x86_64", "linux-x64")]
    [InlineData("Linux", "aarch64", "linux-arm64-musl")]
    public void Bundled_ripgrep_target_selection_is_explicit(
        string operatingSystem,
        string architecture,
        string expected)
    {
        BundledRipgrepToolProvisioner.ResolveTarget(
                operatingSystem,
                architecture)
            .Should()
            .Be(expected);
    }

    [Fact]
    public void Bundled_ripgrep_command_uses_absolute_path_instead_of_path_lookup()
    {
        var tool = new WorkspaceRipgrepTool(
            "/tmp/hpd-ripgrep-test",
            "/tmp/hpd-ripgrep-test/rg",
            [],
            "15.1.0",
            "osx-arm64",
            "digest",
            "bundled");

        string command = WorkspaceRipgrep.BuildCommand(
            tool,
            ["--version"]);

        command.Should().Be(
            "exec '/tmp/hpd-ripgrep-test/rg' '--version'");
        command.Should().NotContain("command -v");
    }

    [SkippableFact]
    [Trait("Category", "RealAdapter")]
    public async Task Netcoredbg_initializes_through_production_sidecar_isolation()
    {
        var adapter = System.Environment.GetEnvironmentVariable("HPD_NETCOREDBG");
        Skip.If(string.IsNullOrWhiteSpace(adapter) || !File.Exists(adapter),
            "Set HPD_NETCOREDBG to a netcoredbg executable to run this qualification.");
        Skip.If(RuntimeInformation.IsOSPlatform(OSPlatform.Windows),
            "This test requires an accepted Windows OS confinement backend. Windows Host Process has separate explicit degraded-profile coverage.");
        await SkipIfLocalSandboxUnavailableAsync();

        await using HostProcessTestEnvironment environment =
            await HostProcessTestEnvironment.CreateAsync();
        await using var handle = await environment.ProcessProvider.StartAsync(new ProcessInvocationSpec
        {
            Target = environment.ExecutionTarget,
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
    public async Task Host_process_module_registers_environment_execution_capabilities()
    {
        await using var module = new HostProcessProviderModule();
        var registry = new EnvironmentProviderRegistry();

        registry.RegisterModule(module);

        IReadOnlyList<ProviderDescriptor> providers = await registry.ListAsync();
        ProviderCapabilityReport report = await registry.GetCapabilitiesAsync(
            HostProcessProviderDescriptor.ProviderId);

        registry.ProcessProviders.Should().ContainSingle();
        providers.Should().ContainSingle(provider =>
            provider.Id == HostProcessProviderDescriptor.ProviderId);
        report.Capabilities.Should().Contain(fact =>
            fact.Id == StandardSandboxEnvironmentCapabilities.ProcessExecution &&
            fact.State == CapabilityState.Supported);
    }

    [Fact]
    public async Task Host_process_provider_runs_prepared_process_invocation()
    {
        await using HostProcessTestEnvironment environment =
            await HostProcessTestEnvironment.CreateAsync();

        ProcessInvocationSpec invocation = new()
        {
            Target = environment.ExecutionTarget,
            Command = ShellInvocation("local-provider-ok"),
            Isolation = ProcessIsolationPolicy.Default with { Mode = ProcessIsolationMode.Disabled },
        };

        ProcessInvocationResult result =
            await environment.ProcessProvider.RunAsync(invocation);

        result.CompletionKind.Should().Be(ProcessCompletionKind.Completed);
        result.ExitCode.Should().Be(0);
        System.Text.Encoding.UTF8.GetString(result.Output.Stdout.CapturedBytes.ToArray())
            .Trim()
            .Should()
            .Be("local-provider-ok");
    }

    [SkippableFact]
    public async Task Bundled_ripgrep_is_provisioned_through_environment_process_lane()
    {
        Skip.If(
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows),
            "The portable bundled ripgrep test currently requires a POSIX Environment.");
        Skip.If(
            RipgrepBundledBinaries.Manifest.Count == 0,
            "Run prepare-ripgrep-binaries.cs before this acceptance test.");
        await SkipIfLocalSandboxUnavailableAsync();

        await using HostProcessTestEnvironment environment =
            await HostProcessTestEnvironment.CreateAsync();
        var provisioner = new BundledRipgrepToolProvisioner(
            environment.ProcessProvider,
            environment.ExecutionTarget);
        WorkspaceRipgrepTool? tool =
            await provisioner.EnsureAsync();
        tool.Should().NotBeNull(provisioner.FailureReason);
        tool!.Source.Should().Be("bundled");
        tool.ExecutablePath.Should().StartWith("/tmp/hpd-ripgrep-");

        ProcessInvocationResult result =
            await environment.ProcessProvider.RunAsync(
                new ProcessInvocationSpec
                {
                    Target = environment.ExecutionTarget,
                    Command = new ProcessCommandSpec
                    {
                        FileName = tool.ExecutablePath,
                        Arguments =
                        [
                            .. tool.ArgumentsPrefix,
                            "--version",
                        ],
                        WorkingDirectory = "/tmp",
                    },
                    Io = new ProcessIoSpec
                    {
                        StandardInput = ProcessInputSpec.None,
                        StandardOutput = new ProcessOutputSpec
                        {
                            Capture = true,
                            Stream = false,
                        },
                        StandardError = new ProcessOutputSpec
                        {
                            Capture = true,
                            Stream = false,
                        },
                        MergeStandardError = false,
                    },
                    Isolation = MinimalExecutableIsolation(),
                });

        result.CompletionKind.Should().Be(ProcessCompletionKind.Completed);
        result.ExitCode.Should().Be(0);
        Encoding.UTF8.GetString(
                result.Output.Stdout.CapturedBytes.Span)
            .Should()
            .StartWith("ripgrep ");
    }

    [SkippableFact]
    public async Task Environment_runtime_runs_through_host_process_enforcement()
    {
        Skip.If(RuntimeInformation.IsOSPlatform(OSPlatform.Windows), "This isolated execution test requires an accepted Windows OS confinement backend.");
        await SkipIfLocalSandboxUnavailableAsync();

        await using HostProcessTestEnvironment environment =
            await HostProcessTestEnvironment.CreateAsync();

        ProcessInvocationSpec invocation = new()
        {
            Target = environment.ExecutionTarget,
            Command = ShellInvocation("runtime-host-process-isolated-ok"),
            Isolation = MinimalExecutableIsolation(),
        };

        ProcessInvocationResult result =
            await environment.Runtime.RunProcessAsync(invocation);

        result.CompletionKind.Should().Be(ProcessCompletionKind.Completed);
        result.ExitCode.Should().Be(0);
        System.Text.Encoding.UTF8.GetString(result.Output.Stdout.CapturedBytes.ToArray())
            .Trim()
            .Should()
            .Be("runtime-host-process-isolated-ok");
    }

    [SkippableFact]
    public async Task Host_process_provider_enforces_filesystem_read_denials()
    {
        Skip.If(RuntimeInformation.IsOSPlatform(OSPlatform.Windows), "This filesystem-enforcement test requires an accepted Windows OS confinement backend.");
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
            await using HostProcessTestEnvironment environment =
                await HostProcessTestEnvironment.CreateAsync();

            ProcessInvocationResult allowed =
                await environment.ProcessProvider.RunAsync(new ProcessInvocationSpec
            {
                Target = environment.ExecutionTarget,
                Command = ReadFileInvocation(allowedFile),
                Isolation = FilesystemReadDenyIsolation(secretDirectory),
            });

            allowed.CompletionKind.Should().Be(ProcessCompletionKind.Completed);
            allowed.ExitCode.Should().Be(0);
            System.Text.Encoding.UTF8.GetString(allowed.Output.Stdout.CapturedBytes.ToArray())
                .Trim()
                .Should()
                .Be("public");

            ProcessInvocationResult denied =
                await environment.ProcessProvider.RunAsync(new ProcessInvocationSpec
            {
                Target = environment.ExecutionTarget,
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

    private sealed class HostProcessTestEnvironment : IAsyncDisposable
    {
        private readonly HostProcessProviderModule _module;
        private readonly ResourceRef<ExecutionUnit> _unit;

        private HostProcessTestEnvironment(
            HostProcessProviderModule module,
            InMemoryEnvironmentRuntime runtime,
            ResourceRef<ExecutionUnit> unit,
            TargetHandle<ExecutionUnit> executionTarget)
        {
            _module = module;
            Runtime = runtime;
            _unit = unit;
            ExecutionTarget = executionTarget;
            ProcessProvider = module.ProcessProvider;
        }

        public InMemoryEnvironmentRuntime Runtime { get; }

        public IProcessProvider ProcessProvider { get; }

        public TargetHandle<ExecutionUnit> ExecutionTarget { get; }

        public static async Task<HostProcessTestEnvironment> CreateAsync()
        {
            var module = new HostProcessProviderModule();
            var registry = new EnvironmentProviderRegistry();
            registry.RegisterModule(module);
            var runtime = new InMemoryEnvironmentRuntime(registry);

            ResourceSnapshot<
                RuntimeHost,
                RuntimeHostSpec,
                RuntimeHostStatus> host =
                await runtime.EnsureHostAsync(new RuntimeHostSpec
                {
                    PreferredProvider =
                        HostProcessProviderDescriptor.ProviderId,
                    Platform =
                        HostProcessProviderDescriptor.CurrentPlatform(),
                });
            host.Status.HostPhase.Should().Be(RuntimeHostPhase.Ready);
            var hostRef = new ResourceRef<RuntimeHost>(
                host.Metadata.Id,
                host.Metadata.Scope,
                host.Metadata.Generation);

            ResourceSnapshot<
                ExecutionUnit,
                ExecutionUnitSpec,
                ExecutionUnitStatus> unit =
                await runtime.EnsureExecutionUnitAsync(
                    new ExecutionUnitSpec
                    {
                        PreferredHost = hostRef,
                    });
            unit.Status.UnitPhase.Should().Be(ExecutionUnitPhase.Ready);
            var unitRef = new ResourceRef<ExecutionUnit>(
                unit.Metadata.Id,
                unit.Metadata.Scope,
                unit.Metadata.Generation);
            return new HostProcessTestEnvironment(
                module,
                runtime,
                unitRef,
                unit.Status.Handle!.Value);
        }

        public async ValueTask DisposeAsync()
        {
            await Runtime.DeleteExecutionUnitAsync(_unit);
            await Runtime.DeleteHostAsync();
            await _module.DisposeAsync();
        }
    }

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
