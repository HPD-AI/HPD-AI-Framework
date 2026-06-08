namespace HPD.Agent.Sandbox.AppleVirtualization.Tests;

using System.Runtime.InteropServices;
using System.Text;
using FluentAssertions;
using HPD.Environment.AppleVirtualization;
using HPD.Environment.Contracts;
using HPD.Environment.Runtime;
using Xunit;

public sealed class AppleVirtualizationSandboxRealAcceptanceTests
{
    [SkippableFact]
    public async Task Real_apple_virtualization_sandbox_runtime_boots_guest_and_runs_isolated_process()
    {
        RealAppleVirtualizationEnvironment environment = RealAppleVirtualizationEnvironment.Parse(System.Environment.GetEnvironmentVariable);
        Skip.IfNot(RuntimeInformation.IsOSPlatform(OSPlatform.OSX), "Real Apple Virtualization acceptance requires macOS.");
        Skip.IfNot(environment.CanRun, environment.SkipReason);

        using RealScratchDisk scratchDisk = RealScratchDisk.Create(environment.GuestDiskPath);
        AppleVirtualizationProviderOptions options = environment.CreateProviderOptions(scratchDisk.Path);
        await using var middleware = new AppleVirtualizationSandboxMiddleware(options);
        await middleware.InitializeAsync();

        IEnvironmentRuntime runtime = middleware.Runtime!;
        TargetHandle<RuntimeHost>? hostHandle = null;
        try
        {
            ResourceSnapshot<RuntimeHost, RuntimeHostSpec, RuntimeHostStatus> host =
                await runtime.EnsureHostAsync(RuntimeHostSpec());
            IRuntimeHostProvider hostProvider = middleware.Registry!.RuntimeHostProviders.Single();
            host.Status.Handle.Should().NotBeNull();
            hostHandle = host.Status.Handle.Value;
            RuntimeHostStatus hostStatus = await WaitForHostReadyAsync(hostProvider, hostHandle.Value);
            hostStatus.HostPhase.Should().Be(RuntimeHostPhase.Ready, HostFailureDetails(hostStatus));

            ResourceSnapshot<ExecutionUnit, ExecutionUnitSpec, ExecutionUnitStatus> unit =
                await runtime.EnsureExecutionUnitAsync(new ExecutionUnitSpec
                {
                    PreferredHost = new ResourceRef<RuntimeHost>(
                        host.Metadata.Id,
                        host.Metadata.Scope,
                        host.Metadata.Generation),
                    Identity = new ExecutionUnitIdentitySpec { User = "hpd", Group = "hpd" },
                });
            unit.Status.Handle.Should().NotBeNull();
            TargetHandle<ExecutionUnit> unitHandle = unit.Status.Handle.Value;
            IExecutionUnitProvider unitProvider = middleware.Registry.ExecutionUnitProviders.Single();
            ExecutionUnitStatus unitStatus = await WaitForUnitReadyAsync(unitProvider, unitHandle);
            unitStatus.UnitPhase.Should().Be(ExecutionUnitPhase.Ready, UnitFailureDetails(unitStatus));

            ProcessInvocationResult result = await runtime.RunProcessAsync(IsolatedProcess(unitHandle));

            result.CompletionKind.Should().Be(ProcessCompletionKind.Exited);
            result.ExitCode.GetValueOrDefault().Should().Be(0, Decode(result.Output.Stderr.CapturedBytes));
            string stdout = Decode(result.Output.Stdout.CapturedBytes);
            stdout.Should().Contain("HPD_REAL_SANDBOX_OK=1");
            stdout.Should().NotContain("HPD_SHOULD_BE_STRIPPED=");
        }
        finally
        {
            if (hostHandle is not null && middleware.Registry?.RuntimeHostProviders.SingleOrDefault() is { } provider)
            {
                await provider.StopAsync(hostHandle.Value, StopPolicy.Default);
            }
        }
    }

    private static async Task<RuntimeHostStatus> WaitForHostReadyAsync(
        IRuntimeHostProvider hostProvider,
        TargetHandle<RuntimeHost> host)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + TimeSpan.FromMinutes(5);
        RuntimeHostStatus last = await hostProvider.GetStatusAsync(host, CancellationToken.None);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (last.HostPhase is RuntimeHostPhase.Ready or RuntimeHostPhase.Failed)
                return last;

            await Task.Delay(TimeSpan.FromSeconds(1));
            last = await hostProvider.GetStatusAsync(host, CancellationToken.None);
        }

        return last;
    }

    private static async Task<ExecutionUnitStatus> WaitForUnitReadyAsync(
        IExecutionUnitProvider unitProvider,
        TargetHandle<ExecutionUnit> unit)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + TimeSpan.FromMinutes(2);
        ExecutionUnitStatus last = await unitProvider.GetStatusAsync(unit, CancellationToken.None);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (last.UnitPhase is ExecutionUnitPhase.Ready or ExecutionUnitPhase.Failed)
                return last;

            await Task.Delay(TimeSpan.FromSeconds(1));
            last = await unitProvider.GetStatusAsync(unit, CancellationToken.None);
        }

        return last;
    }

    private static RuntimeHostSpec RuntimeHostSpec() =>
        new()
        {
            Platform = new PlatformSpec("linux", "arm64"),
            Capacity = new ResourceQuotaPolicy
            {
                CpuCores = 4,
                MemoryBytes = 4L * 1024 * 1024 * 1024,
                StorageBytes = 32L * 1024 * 1024 * 1024,
            },
            Bootstrap = new RuntimeHostBootstrapSpec
            {
                GuestComponents =
                [
                    new GuestComponentSpec(GuestComponentKind.GuestAgent, "hpd-guest-agent"),
                ],
                ReadinessGates =
                [
                    new ReadinessGateSpec(
                        "guest-agent-handshake",
                        ReadinessGateKind.GuestControlReachable,
                        ReadinessGateScope.GuestControl,
                        new RetryPolicy(MaxAttempts: 30, Delay: TimeSpan.FromSeconds(1)),
                        Timeout: TimeSpan.FromSeconds(30)),
                ],
            },
            TopologyPolicy = new RuntimeTopologyPolicy
            {
                Mode = RuntimeTopologyMode.OneHostPerRuntime,
            },
        };

    private static ProcessInvocationSpec IsolatedProcess(TargetHandle<ExecutionUnit> unit) =>
        new()
        {
            Target = unit,
            Command = new ProcessCommandSpec
            {
                FileName = "/usr/bin/env",
                WorkingDirectory = "/tmp",
                Environment = new Dictionary<string, string?>
                {
                    ["HPD_SHOULD_BE_STRIPPED"] = "secret",
                },
            },
            Io = new ProcessIoSpec
            {
                StandardOutput = new ProcessOutputSpec
                {
                    Capture = true,
                    Stream = true,
                    MaxCapturedBytes = 64 * 1024,
                },
                StandardError = new ProcessOutputSpec
                {
                    Capture = true,
                    Stream = true,
                    MaxCapturedBytes = 64 * 1024,
                },
            },
            Isolation = ProcessIsolationPolicy.Default with
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
                Environment = new EnvironmentAccessPolicy
                {
                    StripUnlistedVariables = true,
                    InjectedVariables = new Dictionary<string, string>
                    {
                        ["HPD_REAL_SANDBOX_OK"] = "1",
                    },
                },
            },
            Policy = ProcessInvocationPolicy.Default with
            {
                Timeout = TimeSpan.FromSeconds(30),
                OutputDrainTimeout = TimeSpan.FromSeconds(2),
                StopOnRunCancellation = true,
            },
        };

    private static string Decode(ReadOnlyMemory<byte> bytes) =>
        Encoding.UTF8.GetString(bytes.Span);

    private static string HostFailureDetails(RuntimeHostStatus status) =>
        "phase: " + status.HostPhase +
        "; diagnostics: " + string.Join(" | ", status.Diagnostics.Select(diagnostic =>
            diagnostic.Code.Value + ": " + diagnostic.Message)) +
        "; conditions: " + string.Join(" | ", status.Conditions.Select(condition =>
            condition.Type + "/" + condition.Status + ": " + condition.Message));

    private static string UnitFailureDetails(ExecutionUnitStatus status) =>
        "phase: " + status.UnitPhase +
        "; diagnostics: " + string.Join(" | ", status.Diagnostics.Select(diagnostic =>
            diagnostic.Code.Value + ": " + diagnostic.Message)) +
        "; conditions: " + string.Join(" | ", status.Conditions.Select(condition =>
            condition.Type + "/" + condition.Status + ": " + condition.Message));

    private sealed record RealAppleVirtualizationEnvironment(
        bool CanRun,
        string SkipReason,
        string HelperPath,
        string KernelPath,
        string InitrdPath,
        string GuestDiskPath,
        string? SerialLogPath,
        string? KernelCommandLine,
        string? ExpectedGuestAgentVersion)
    {
        public static RealAppleVirtualizationEnvironment Parse(Func<string, string?> getEnvironment)
        {
            string[] required =
            [
                "HPD_APPLEVZ_REAL_CONTAINER_SMOKE",
                "HPD_APPLEVZ_REAL_HELPER_PATH",
                "HPD_APPLEVZ_GUEST_KERNEL",
                "HPD_APPLEVZ_GUEST_INITRD",
                "HPD_APPLEVZ_GUEST_DISK",
                "HPD_APPLEVZ_EXPECTED_GUEST_AGENT_VERSION",
            ];

            string[] missing = required
                .Where(name => string.IsNullOrWhiteSpace(getEnvironment(name)))
                .ToArray();
            if (missing.Length > 0)
            {
                return Empty("Missing real Apple Virtualization env vars: " + string.Join(", ", missing));
            }

            if (getEnvironment("HPD_APPLEVZ_REAL_CONTAINER_SMOKE") != "1")
            {
                return Empty("Set HPD_APPLEVZ_REAL_CONTAINER_SMOKE=1 to opt into real Apple Virtualization acceptance.");
            }

            string helper = getEnvironment("HPD_APPLEVZ_REAL_HELPER_PATH")!;
            string kernel = getEnvironment("HPD_APPLEVZ_GUEST_KERNEL")!;
            string initrd = getEnvironment("HPD_APPLEVZ_GUEST_INITRD")!;
            string disk = getEnvironment("HPD_APPLEVZ_GUEST_DISK")!;
            string[] paths = [helper, kernel, initrd, disk];
            string[] missingFiles = paths
                .Where(path => !File.Exists(path))
                .ToArray();
            if (missingFiles.Length > 0)
            {
                return Empty("Real Apple Virtualization files are missing: " + string.Join(", ", missingFiles));
            }

            return new RealAppleVirtualizationEnvironment(
                true,
                string.Empty,
                helper,
                kernel,
                initrd,
                disk,
                NullIfWhiteSpace(getEnvironment("HPD_APPLEVZ_GUEST_SERIAL_LOG")),
                NullIfWhiteSpace(getEnvironment("HPD_APPLEVZ_GUEST_KERNEL_CMDLINE")),
                NullIfWhiteSpace(getEnvironment("HPD_APPLEVZ_EXPECTED_GUEST_AGENT_VERSION")));
        }

        public AppleVirtualizationProviderOptions CreateProviderOptions(string scratchDiskPath) =>
            new()
            {
                HelperPath = HelperPath,
                HelperTransportMode = AppleVirtualizationHelperTransportMode.StdIo,
                GuestImage = new AppleVirtualizationGuestImageOptions
                {
                    BootLoader = AppleVirtualizationGuestBootLoaderKind.LinuxBootLoader,
                    KernelPath = KernelPath,
                    InitrdPath = InitrdPath,
                    DiskImagePath = scratchDiskPath,
                    SerialLogPath = SerialLogPath,
                    KernelCommandLine = KernelCommandLine,
                    ExpectedGuestAgentVersion = ExpectedGuestAgentVersion,
                },
                FeatureGates = new AppleVirtualizationProviderFeatureGates
                {
                    EnableRealHelperActivation = true,
                    EnableRealVmBoot = true,
                    EnableVmConfigurationValidation = true,
                },
            };

        private static RealAppleVirtualizationEnvironment Empty(string reason) =>
            new(false, reason, string.Empty, string.Empty, string.Empty, string.Empty, null, null, null);

        private static string? NullIfWhiteSpace(string? value) =>
            string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private sealed class RealScratchDisk : IDisposable
    {
        private RealScratchDisk(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static RealScratchDisk Create(string sourceDisk)
        {
            string path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "hpd-applevz-sandbox-real-" + Guid.NewGuid().ToString("N") + ".raw");
            File.Copy(sourceDisk, path);
            return new RealScratchDisk(path);
        }

        public void Dispose()
        {
            try
            {
                File.Delete(Path);
            }
            catch
            {
                // Best effort cleanup for a temp disk copy.
            }
        }
    }
}
