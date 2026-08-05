namespace HPD.Environment.AppleVirtualization.Tests;

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using HPD.Environment.AppleVirtualization.Protocol;
using HPD.Environment.AppleVirtualization.Tests.Fixtures;
using HPD.Environment.AppleVirtualization.Tests.TestDoubles;
using HPD.Environment.Contracts;
using Xunit;

public sealed class AppleVirtualizationToolHarnessAcceptanceTests
{
    private static readonly TimeSpan RealBootTimeout = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan RealGuestReadyTimeout = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan RealProcessTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan RealCleanupTimeout = TimeSpan.FromSeconds(20);
    private const string RealProjectionId = "projection-real-workspace";
    private const string RealGuestWorkspacePath = "/workspace";
    private const int SerialLogTailBytes = 64 * 1024;
    private const int RealOutputTailBytes = 64 * 1024;

    [Fact]
    public async Task Fake_helper_first_slice_scenario_replays_status_and_output_without_real_virtualization()
    {
        AppleVirtualizationScenario scenario = AppleVirtualizationScenarioBuilder.FirstSliceSuccess().Build();

        AppleVirtualizationHelperEnvelope hello = await scenario.Client.SendAsync(
            AppleVirtualizationHelperEnvelope.Request(AppleVirtualizationHelperOperation.Hello, "hello", 1));
        AppleVirtualizationHelperEnvelope host = await scenario.Client.SendAsync(
            AppleVirtualizationHelperEnvelope.Request(AppleVirtualizationHelperOperation.HostStatus, "host", 2));
        AppleVirtualizationHelperEnvelope projection = await scenario.Client.SendAsync(
            AppleVirtualizationHelperEnvelope.Request(AppleVirtualizationHelperOperation.ProjectionStatus, "projection", 3));
        AppleVirtualizationHelperEnvelope unit = await scenario.Client.SendAsync(
            AppleVirtualizationHelperEnvelope.Request(AppleVirtualizationHelperOperation.UnitStatus, "unit", 4));
        AppleVirtualizationHelperEnvelope process = await scenario.Client.SendAsync(
            AppleVirtualizationHelperEnvelope.Request(AppleVirtualizationHelperOperation.ProcessWait, "process", 5));

        var events = new List<AppleVirtualizationHelperEnvelope>();
        await foreach (AppleVirtualizationHelperEnvelope helperEvent in scenario.Client.ReadEventsAsync())
        {
            events.Add(helperEvent);
        }

        hello.HelloResponse.Should().NotBeNull();
        hello.HelloResponse!.ProtocolCompatible.Should().BeTrue();
        host.ShouldRepresentHostPhase(RuntimeHostPhase.Running, expectedGuestControlReachable: false);
        projection.ShouldRepresentProjection(ContentProjectionPhase.Projected, ProjectionRealizationKind.LiveProjection);
        unit.ShouldRepresentUnitPhase(ExecutionUnitPhase.Ready);
        process.ShouldRepresentProcessExit(ProcessCompletionKind.Exited, exitCode: 0);
        events.Any(helperEvent => helperEvent.HostStatusResponse is { GuestControlReachable: true }).Should().BeTrue();
        events.Any(helperEvent => helperEvent.ProcessStatusResponse is { ProcessPhase: ProcessInvocationPhase.Running }).Should().BeTrue();
        events.Any(helperEvent => helperEvent.ProcessOutputEvent is { Stream: ProcessOutputStream.Stdout }).Should().BeTrue();
    }

    [Fact]
    public void Fixtures_create_valid_first_slice_specs_with_provider_handles()
    {
        RuntimeHostSpec hostSpec = AppleVirtualizationContractFixtures.RuntimeHostSpec();
        ExecutionUnitSpec unitSpec = AppleVirtualizationContractFixtures.ExecutionUnitSpec();
        ContentProjectionSpec projectionSpec = AppleVirtualizationContractFixtures.ReadOnlyWorkspaceProjection();
        ProcessInvocationSpec processSpec = AppleVirtualizationContractFixtures.ProcessInvocationSpec();

        hostSpec.Platform.OperatingSystem.Should().Be("linux");
        hostSpec.Bootstrap!.GuestComponents.Should().Contain(component => component.Kind == GuestComponentKind.GuestAgent);
        hostSpec.Bootstrap.ReadinessGates.Should().Contain(gate => gate.Kind == ReadinessGateKind.GuestControlReachable);
        unitSpec.PreferredHost.Should().Be(AppleVirtualizationContractFixtures.RuntimeHostRef());
        projectionSpec.SecurityPolicy.AllowHostPathSource.Should().BeTrue();
        projectionSpec.SecurityPolicy.AllowDirectSourceMutation.Should().BeFalse();
        projectionSpec.AccessMode.Should().Be(AccessMode.ReadOnly);
        processSpec.Target.ShouldUseAppleProviderHandle(TargetRouteSegmentKind.ExecutionUnit, expectedProviderGeneration: 1);
        processSpec.Command.FileName.Should().Be("uname");
        processSpec.Io.StandardOutput.MaxCapturedBytes.Should().Be(64 * 1024);
    }

    [Fact]
    public void Assertion_helpers_validate_output_bytes_without_text_decoding()
    {
        ReadOnlyMemory<byte> bytes = "ready\n"u8.ToArray();
        AppleVirtualizationScenario scenario = new AppleVirtualizationScenarioBuilder()
            .WithProcessOutput("process-1", ProcessOutputStream.Stdout, bytes, final: true)
            .Build();

        AppleVirtualizationHelperEnvelope output = scenario.Events.Single();

        output.ShouldRepresentOutput(ProcessOutputStream.Stdout, bytes, ProcessOutputChunkFlags.Final);
    }

    [Fact]
    public void Failure_scenarios_keep_structured_diagnostic_codes()
    {
        AppleVirtualizationScenario scenario = new AppleVirtualizationScenarioBuilder()
            .WithHelperFailure(
                AppleVirtualizationHelperOperation.HostStart,
                "AppleVirtualization.BootTimedOut",
                "The VM did not reach the boot readiness marker.",
                retryable: true)
            .WithStaleHandle(
                AppleVirtualizationHelperOperation.ProcessWait,
                "process-1",
                staleGeneration: 1)
            .Build();

        scenario.Responses[0].ShouldHaveStableDiagnostic("AppleVirtualization.BootTimedOut", retryable: true);
        scenario.Responses[1].ShouldHaveStableDiagnostic("AppleVirtualization.StaleHandle", retryable: false);
    }

    [Fact]
    public void Acceptance_matrix_names_required_contract_expectations_and_apple_api_surfaces()
    {
        AppleVirtualizationAcceptanceMatrix.Cases.Should().Contain(matrixCase =>
            matrixCase.Scenario == "boot-linux-vm-through-fake-helper" &&
            matrixCase.ContractKind == ProviderContractKind.RuntimeHost &&
            matrixCase.AppleApiSurface.Contains("VZVirtualMachine", StringComparison.Ordinal));
        AppleVirtualizationAcceptanceMatrix.Cases.Should().Contain(matrixCase =>
            matrixCase.Scenario == "projection-success" &&
            matrixCase.AppleApiSurface.Contains("VZVirtioFileSystemDeviceConfiguration", StringComparison.Ordinal));
        AppleVirtualizationAcceptanceMatrix.Cases.Should().Contain(matrixCase =>
            matrixCase.Scenario == "stdout-stderr-streaming" &&
            matrixCase.ContractKind == ProviderContractKind.ProcessInvocation);
        AppleVirtualizationAcceptanceMatrix.Cases.Where(matrixCase => matrixCase.Required)
            .Should()
            .Contain(matrixCase => matrixCase.Scenario == "unsupported-host-platform");
    }

    [Fact]
    public void Real_acceptance_env_parser_reports_missing_required_variables_without_failing_normal_tests()
    {
        AppleVirtualizationRealAcceptanceEnvironment environment =
            AppleVirtualizationRealAcceptanceEnvironment.Parse(_ => null, hostSupported: true);

        environment.CanAttemptRealBoot.Should().BeFalse();
        environment.SkipReason.Should().Contain("HPD_APPLEVZ_REAL_HELPER_PATH");
        environment.SkipReason.Should().Contain("HPD_APPLEVZ_GUEST_KERNEL");
        environment.SkipReason.Should().Contain("HPD_APPLEVZ_GUEST_INITRD");
        environment.SkipReason.Should().Contain("HPD_APPLEVZ_GUEST_DISK");
        environment.SkipReason.Should().Contain("HPD_APPLEVZ_GUEST_SERIAL_LOG");
        environment.SkipReason.Should().Contain("HPD_APPLEVZ_EXPECTED_GUEST_AGENT_VERSION");
        environment.SkipReason.Should().Contain("HPD_APPLEVZ_VIRTIOFS_HOST_PATH");
        environment.SkipReason.Should().Contain("HPD_APPLEVZ_VIRTIOFS_TAG");
    }

    [Fact]
    public void Real_acceptance_env_parser_reports_unsupported_host_without_attempting_boot()
    {
        using RealAcceptanceFiles files = RealAcceptanceFiles.Create();
        AppleVirtualizationRealAcceptanceEnvironment environment =
            AppleVirtualizationRealAcceptanceEnvironment.Parse(files.GetEnvironmentValue, hostSupported: false);

        environment.CanAttemptRealBoot.Should().BeFalse();
        environment.SkipReason.Should().Contain("host capability");
    }

    [Fact]
    public void Real_acceptance_env_parser_validates_required_and_optional_variables()
    {
        using RealAcceptanceFiles files = RealAcceptanceFiles.Create(includeOptional: true);
        AppleVirtualizationRealAcceptanceEnvironment environment =
            AppleVirtualizationRealAcceptanceEnvironment.Parse(files.GetEnvironmentValue, hostSupported: true);

        environment.CanAttemptRealBoot.Should().BeTrue();
        environment.HelperPath.Should().Be(files.HelperPath);
        environment.GuestImage.KernelPath.Should().Be(files.KernelPath);
        environment.GuestImage.InitrdPath.Should().Be(files.InitrdPath);
        AppleVirtualizationTestDiskSet.Path(environment.GuestImage, AppleVirtualizationDiskRole.System)
            .Should().Be(files.DiskPath);
        environment.GuestImage.SerialLogPath.Should().Be(files.SerialLogPath);
        environment.GuestImage.ExpectedGuestAgentVersion.Should().Be("0.1.0");
        environment.GuestImage.KernelCommandLine.Should().Be("console=hvc0 hpd.acceptance=1");
        environment.SharedDirectories.Should().ContainSingle().Which.Tag.Should().Be("hpd.share");
    }

    [Fact]
    public void Real_acceptance_env_parser_requires_projection_variables_for_l9_vertical_slice()
    {
        using RealAcceptanceFiles files = RealAcceptanceFiles.Create(includeProjection: false);
        AppleVirtualizationRealAcceptanceEnvironment environment =
            AppleVirtualizationRealAcceptanceEnvironment.Parse(files.GetEnvironmentValue, hostSupported: true);

        environment.CanAttemptRealBoot.Should().BeFalse();
        environment.SkipReason.Should().Contain("HPD_APPLEVZ_VIRTIOFS_HOST_PATH");
        environment.SkipReason.Should().Contain("HPD_APPLEVZ_VIRTIOFS_TAG");
    }

    [Fact]
    public void Serial_log_tail_capture_is_safe_and_bounded()
    {
        using RealAcceptanceFiles files = RealAcceptanceFiles.Create();
        Directory.CreateDirectory(Path.GetDirectoryName(files.SerialLogPath)!);
        File.WriteAllBytes(files.SerialLogPath, Enumerable.Repeat((byte)'x', SerialLogTailBytes + 128).ToArray());

        byte[] tail = AppleVirtualizationRealAcceptanceEnvironment.ReadSerialLogTail(files.SerialLogPath, SerialLogTailBytes);

        tail.Length.Should().Be(SerialLogTailBytes);
    }

    [Fact]
    public void Real_vertical_slice_toolharness_uses_bounded_timeout_and_cleanup_operations()
    {
        RealBootTimeout.Should().BePositive();
        RealBootTimeout.Should().BeLessThan(TimeSpan.FromMinutes(5));
        RealGuestReadyTimeout.Should().BePositive();
        RealGuestReadyTimeout.Should().BeLessThan(TimeSpan.FromMinutes(2));
        RealProcessTimeout.Should().BePositive();
        RealProcessTimeout.Should().BeLessThan(TimeSpan.FromMinutes(1));
        RealCleanupTimeout.Should().BePositive();
        RealCleanupTimeout.Should().BeLessThan(TimeSpan.FromMinutes(1));

        AppleVirtualizationHelperEnvelope requestStop = RealHostLifecycleRequest(
            AppleVirtualizationHelperOperation.HostRequestStop,
            "runtime-host-real",
            sequenceNumber: 8,
            vmConfiguration: null,
            gracePeriodMilliseconds: (int)RealCleanupTimeout.TotalMilliseconds);
        AppleVirtualizationHelperEnvelope forceStop = RealHostLifecycleRequest(
            AppleVirtualizationHelperOperation.HostStop,
            "runtime-host-real",
            sequenceNumber: 9);
        AppleVirtualizationHelperEnvelope delete = RealHostLifecycleRequest(
            AppleVirtualizationHelperOperation.HostDelete,
            "runtime-host-real",
            sequenceNumber: 10);

        requestStop.HostLifecycleRequest!.ExplicitRealMode.Should().BeTrue();
        requestStop.HostLifecycleRequest.GracePeriodMilliseconds.Should().Be(20000);
        forceStop.HostLifecycleRequest!.ExplicitRealMode.Should().BeTrue();
        delete.HostLifecycleRequest!.ExplicitRealMode.Should().BeTrue();
    }

    [Fact]
    public void Real_vertical_slice_success_assertions_are_limited_to_l9_claims()
    {
        AppleVirtualizationHelperEnvelope readiness = RealGuestReadinessRequest(
            "runtime-host-real",
            expectedGuestAgentVersion: "0.1.0",
            sequenceNumber: 11);
        AppleVirtualizationHelperEnvelope projection = RealProjectionMountRequest(
            "runtime-host-real",
            new AppleVirtualizationVmConfigurationSharedDirectory
            {
                HostPath = "/tmp/hpd-workspace",
                Tag = "hpd.share",
                ReadOnly = true,
            },
            sequenceNumber: 12);
        AppleVirtualizationHelperEnvelope uname = RealProcessStartRequest(
            "runtime-host-real",
            "process-uname",
            new ProcessCommandSpec
            {
                FileName = "uname",
                Arguments = ["-a"],
            },
            sequenceNumber: 13,
            requireProjection: false);
        AppleVirtualizationHelperEnvelope pwdAndLs = RealProcessStartRequest(
            "runtime-host-real",
            "process-pwd-ls",
            new ProcessCommandSpec
            {
                FileName = "sh",
                Arguments = ["-lc", "pwd && ls"],
                WorkingDirectory = RealGuestWorkspacePath,
            },
            sequenceNumber: 14,
            requireProjection: true);

        readiness.GuestAgentReadinessProbeRequest!.RequiredCapabilities.Should().Contain(
            ["projection.mount", "process.start", "process.readOutput"]);
        projection.ProjectionMountRequest!.GuestPath.Should().Be(RealGuestWorkspacePath);
        projection.ProjectionMountRequest.AccessMode.Should().Be(AccessMode.ReadOnly);
        uname.ProcessStartRequest!.RequireVerifiedProjection.Should().BeFalse();
        pwdAndLs.ProcessStartRequest!.RequireVerifiedProjection.Should().BeTrue();
        pwdAndLs.ProcessStartRequest.RequiredProjectionId.Should().Be(RealProjectionId);
        pwdAndLs.ProcessStartRequest.RequiredProjectionGuestPath.Should().Be(RealGuestWorkspacePath);
        new[]
        {
            readiness.Operation,
            projection.Operation,
            uname.Operation,
            pwdAndLs.Operation,
        }.Should().NotContain(AppleVirtualizationHelperOperation.ProcessResize);
    }

    [SkippableFact]
    public async Task Real_macos_acceptance_runs_full_vertical_slice_only_with_explicit_env_and_cleans_up()
    {
        AppleVirtualizationRealAcceptanceEnvironment environment =
            AppleVirtualizationRealAcceptanceEnvironment.Parse(
                System.Environment.GetEnvironmentVariable,
                hostSupported: RuntimeInformation.IsOSPlatform(OSPlatform.OSX));
        Skip.IfNot(environment.CanAttemptRealBoot, environment.SkipReason);

        await using var helper = await RealHelperProcess.StartAsync(environment.HelperPath);
        AppleVirtualizationHelperEnvelope hello = await helper.SendAsync(
            AppleVirtualizationHelperEnvelope.Request(AppleVirtualizationHelperOperation.Hello, "real-hello", 1),
            RealCleanupTimeout);
        AppleVirtualizationPreflightFact? hostSupportedFact = hello.HelloResponse?.PreflightFacts.FirstOrDefault(fact =>
            string.Equals(fact.Name, "vzvirtualmachine-supported", StringComparison.Ordinal));
        Skip.If(
            hostSupportedFact?.State == AppleVirtualizationPreflightFactState.Unsupported,
            hostSupportedFact.Message ?? "VZVirtualMachine.isSupported is false on this host.");

        string hostId = "hpd-real-acceptance-" + Guid.NewGuid().ToString("N");
        AppleVirtualizationVmConfigurationValidationRequest vmConfiguration = environment.CreateVmConfiguration(hostId);
        AppleVirtualizationHelperEnvelope start = RealHostLifecycleRequest(
            AppleVirtualizationHelperOperation.HostStart,
            hostId,
            sequenceNumber: 2,
            vmConfiguration);

        try
        {
            AppleVirtualizationHelperEnvelope startResponse = await helper.SendAsync(start, RealCleanupTimeout);
            startResponse.Error.Should().BeNull("real boot acceptance should fail only as a test failure after env opt-in");

            AppleVirtualizationHelperEnvelope running = await PollForHostPhaseAsync(
                helper,
                hostId,
                RuntimeHostPhase.Running,
                RealBootTimeout);

            running.ShouldRepresentHostPhase(RuntimeHostPhase.Running, expectedGuestControlReachable: false);

            AppleVirtualizationHelperEnvelope ready = await helper.SendAsync(
                RealGuestReadinessRequest(
                    hostId,
                    environment.GuestImage.ExpectedGuestAgentVersion!,
                    sequenceNumber: 30),
                RealGuestReadyTimeout).ConfigureAwait(false);
            ready.Error.Should().BeNull("explicit real L9 acceptance requires a baked compatible HPD guest agent");
            ready.GuestAgentReadinessProbeResponse!.VerifiedReady.Should().BeTrue();
            ready.GuestAgentReadinessProbeResponse.State.Should().Be(AppleVirtualizationGuestAgentReadinessState.Ready);
            ready.GuestAgentReadinessProbeResponse.Capabilities!.ProcessResize.Should().BeFalse();

            AppleVirtualizationHelperEnvelope projection = await helper.SendAsync(
                RealProjectionMountRequest(hostId, environment.SharedDirectories[0], sequenceNumber: 31),
                RealGuestReadyTimeout).ConfigureAwait(false);
            projection.Error.Should().BeNull("real L9 acceptance requires guest-agent projection verification");
            projection.ProjectionStatusResponse!.ReadyForHpdUse.Should().BeTrue();
            projection.ProjectionStatusResponse.ProjectionPhase.Should().Be(ContentProjectionPhase.Projected);

            AppleVirtualizationProcessStatusResponse uname = await RunRealProcessAsync(
                helper,
                hostId,
                processId: "process-uname-" + Guid.NewGuid().ToString("N"),
                new ProcessCommandSpec
                {
                    FileName = "uname",
                    Arguments = ["-a"],
                },
                requireProjection: false,
                startSequence: 40).ConfigureAwait(false);
            uname.Result!.ExitCode.Should().Be(0);
            CapturedBytes(uname.Result.Output.Stdout).Length.Should().BeGreaterThan(0);

            AppleVirtualizationProcessStatusResponse pwdAndLs = await RunRealProcessAsync(
                helper,
                hostId,
                processId: "process-pwd-ls-" + Guid.NewGuid().ToString("N"),
                new ProcessCommandSpec
                {
                    FileName = "sh",
                    Arguments = ["-lc", "pwd && ls"],
                    WorkingDirectory = RealGuestWorkspacePath,
                },
                requireProjection: true,
                startSequence: 50).ConfigureAwait(false);
            pwdAndLs.Result!.ExitCode.Should().Be(0);
            string stdout = Encoding.UTF8.GetString(CapturedBytes(pwdAndLs.Result.Output.Stdout).Span);
            stdout.Should().Contain(RealGuestWorkspacePath);
        }
        finally
        {
            await helper.TrySendAsync(
                RealProjectionLifecycleRequest(
                    AppleVirtualizationHelperOperation.ProjectionUnmount,
                    hostId,
                    sequenceNumber: 80),
                RealCleanupTimeout).ConfigureAwait(false);
            await helper.TrySendAsync(
                RealHostLifecycleRequest(
                    AppleVirtualizationHelperOperation.HostRequestStop,
                    hostId,
                    sequenceNumber: 90,
                    vmConfiguration: null,
                    gracePeriodMilliseconds: (int)RealCleanupTimeout.TotalMilliseconds),
                RealCleanupTimeout).ConfigureAwait(false);
            await helper.TrySendAsync(
                RealHostLifecycleRequest(AppleVirtualizationHelperOperation.HostStop, hostId, sequenceNumber: 91),
                RealCleanupTimeout).ConfigureAwait(false);
            await helper.TrySendAsync(
                RealHostLifecycleRequest(AppleVirtualizationHelperOperation.HostDelete, hostId, sequenceNumber: 92),
                RealCleanupTimeout).ConfigureAwait(false);
            _ = AppleVirtualizationRealAcceptanceEnvironment.ReadSerialLogTail(environment.GuestImage.SerialLogPath!, SerialLogTailBytes);
        }
    }

    private static async Task<AppleVirtualizationProcessStatusResponse> RunRealProcessAsync(
        RealHelperProcess helper,
        string hostId,
        string processId,
        ProcessCommandSpec command,
        bool requireProjection,
        long startSequence)
    {
        AppleVirtualizationHelperEnvelope start = await helper.SendAsync(
            RealProcessStartRequest(hostId, processId, command, startSequence, requireProjection),
            RealProcessTimeout).ConfigureAwait(false);
        start.Error.Should().BeNull("process.start should succeed after guest readiness and verified projection");
        start.ProcessStatusResponse!.ProcessPhase.Should().Be(ProcessInvocationPhase.Running);

        AppleVirtualizationHelperEnvelope output = await helper.SendAsync(
            RealProcessLifecycleRequest(
                AppleVirtualizationHelperOperation.ProcessReadOutput,
                processId,
                startSequence + 1,
                timeout: null),
            RealProcessTimeout).ConfigureAwait(false);
        output.Error.Should().BeNull("process.readOutput should be supported by the baked guest agent");
        if (output.ProcessOutputEvent is not null)
        {
            output.ProcessOutputEvent.Bytes.Length.Should().BeLessThanOrEqualTo(RealOutputTailBytes);
        }

        AppleVirtualizationHelperEnvelope wait = await helper.SendAsync(
            RealProcessLifecycleRequest(
                AppleVirtualizationHelperOperation.ProcessWait,
                processId,
                startSequence + 2,
                timeout: RealProcessTimeout),
            RealProcessTimeout + TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        wait.Error.Should().BeNull("process.wait should return a structured result");
        wait.ProcessStatusResponse!.Result.Should().NotBeNull();
        wait.ProcessStatusResponse.Result!.Output.Stdout.BytesObserved.Should().BeLessThanOrEqualTo(RealOutputTailBytes);
        wait.ProcessStatusResponse.Result.Output.Stderr.BytesObserved.Should().BeLessThanOrEqualTo(RealOutputTailBytes);
        return wait.ProcessStatusResponse;
    }

    private static async Task<AppleVirtualizationHelperEnvelope> PollForHostPhaseAsync(
        RealHelperProcess helper,
        string hostId,
        RuntimeHostPhase expectedPhase,
        TimeSpan timeout)
    {
        using var cancellation = new CancellationTokenSource(timeout);
        long sequence = 3;
        AppleVirtualizationHelperEnvelope? last = null;
        while (!cancellation.IsCancellationRequested)
        {
            last = await helper.SendAsync(
                RealHostLifecycleRequest(AppleVirtualizationHelperOperation.HostStatus, hostId, sequence++),
                TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            if (last.HostStatusResponse?.HostPhase == expectedPhase)
            {
                return last;
            }

            if (last.ResponseStatus == AppleVirtualizationHelperResponseStatus.Error ||
                last.HostStatusResponse?.HostPhase == RuntimeHostPhase.Failed)
            {
                string code = last.Error?.Code ?? last.HostStatusResponse?.Diagnostics.FirstOrDefault()?.Code.Value ?? "unknown";
                throw new InvalidOperationException("Real VM host lifecycle failed before reaching running state: " + code);
            }

            await Task.Delay(TimeSpan.FromSeconds(1), cancellation.Token).ConfigureAwait(false);
        }

        throw new TimeoutException(
            "Timed out waiting for real VM running state. Last phase: " +
            (last?.HostStatusResponse?.HostPhase.ToString() ?? "none"));
    }

    private static AppleVirtualizationHelperEnvelope RealHostLifecycleRequest(
        AppleVirtualizationHelperOperation operation,
        string hostId,
        long sequenceNumber,
        AppleVirtualizationVmConfigurationValidationRequest? vmConfiguration = null,
        int? gracePeriodMilliseconds = null) =>
        AppleVirtualizationHelperEnvelope.Request(
            operation,
            "real-" + sequenceNumber.ToString(System.Globalization.CultureInfo.InvariantCulture),
            sequenceNumber,
            AppleVirtualizationHelperProtocol.HostRequestSchema) with
        {
            HostLifecycleRequest = new AppleVirtualizationHostLifecycleRequest
            {
                HostId = hostId,
                ExplicitRealMode = true,
                VmConfigurationValidationRequest = vmConfiguration,
                GracePeriodMilliseconds = gracePeriodMilliseconds,
                Reason = "opt-in-real-acceptance",
            },
        };

    private static AppleVirtualizationHelperEnvelope RealGuestReadinessRequest(
        string hostId,
        string expectedGuestAgentVersion,
        long sequenceNumber) =>
        AppleVirtualizationHelperEnvelope.Request(
            AppleVirtualizationHelperOperation.GuestAgentReadinessProbe,
            "real-readiness-" + sequenceNumber.ToString(System.Globalization.CultureInfo.InvariantCulture),
            sequenceNumber,
            AppleVirtualizationHelperProtocol.GuestAgentReadinessRequestSchema) with
        {
            GuestAgentReadinessProbeRequest = new AppleVirtualizationGuestAgentReadinessProbeRequest
            {
                HostId = hostId,
                ExplicitRealMode = true,
                TimeoutMilliseconds = (int)RealGuestReadyTimeout.TotalMilliseconds,
                ExpectedAgentVersion = expectedGuestAgentVersion,
                RequiredCapabilities =
                [
                    "projection.mount",
                    "process.start",
                    "process.readOutput",
                ],
            },
        };

    private static AppleVirtualizationHelperEnvelope RealProjectionMountRequest(
        string hostId,
        AppleVirtualizationVmConfigurationSharedDirectory share,
        long sequenceNumber) =>
        AppleVirtualizationHelperEnvelope.Request(
            AppleVirtualizationHelperOperation.ProjectionMount,
            "real-projection-" + sequenceNumber.ToString(System.Globalization.CultureInfo.InvariantCulture),
            sequenceNumber,
            AppleVirtualizationHelperProtocol.ProjectionRequestSchema) with
        {
            ProjectionMountRequest = new AppleVirtualizationProjectionMountRequest
            {
                ProjectionId = RealProjectionId,
                HostId = hostId,
                HostPath = share.HostPath,
                Tag = share.Tag,
                GuestPath = RealGuestWorkspacePath,
                AccessMode = share.ReadOnly ? AccessMode.ReadOnly : AccessMode.ReadWrite,
                Realization = ProjectionRealizationKind.LiveProjection,
                RequestedWriteEffect = share.ReadOnly ? ProjectionWriteEffect.NoWrites : ProjectionWriteEffect.DirectSourceMutation,
                RequestedCoherence = CoherenceClass.CloseToOpen,
            },
        };

    private static AppleVirtualizationHelperEnvelope RealProjectionLifecycleRequest(
        AppleVirtualizationHelperOperation operation,
        string hostId,
        long sequenceNumber) =>
        AppleVirtualizationHelperEnvelope.Request(
            operation,
            "real-projection-cleanup-" + sequenceNumber.ToString(System.Globalization.CultureInfo.InvariantCulture),
            sequenceNumber,
            AppleVirtualizationHelperProtocol.ProjectionRequestSchema) with
        {
            ProjectionUnmountRequest = new AppleVirtualizationProjectionUnmountRequest
            {
                ProjectionId = RealProjectionId,
                HostId = hostId,
                GuestPath = RealGuestWorkspacePath,
                Force = true,
            },
        };

    private static AppleVirtualizationHelperEnvelope RealProcessStartRequest(
        string hostId,
        string processId,
        ProcessCommandSpec command,
        long sequenceNumber,
        bool requireProjection) =>
        AppleVirtualizationHelperEnvelope.Request(
            AppleVirtualizationHelperOperation.ProcessStart,
            "real-process-start-" + sequenceNumber.ToString(System.Globalization.CultureInfo.InvariantCulture),
            sequenceNumber,
            AppleVirtualizationHelperProtocol.ProcessRequestSchema) with
        {
            ProcessStartRequest = new AppleVirtualizationProcessStartRequest
            {
                ProcessId = processId,
                UnitId = "unit-" + hostId,
                Command = command,
                Io = ProcessIoSpec.Default with
                {
                    StandardOutput = ProcessOutputSpec.CaptureAndStream with { MaxCapturedBytes = RealOutputTailBytes },
                    StandardError = ProcessOutputSpec.CaptureAndStream with { MaxCapturedBytes = RealOutputTailBytes },
                },
                Policy = ProcessInvocationPolicy.Default with
                {
                    Timeout = RealProcessTimeout,
                    OutputDrainTimeout = TimeSpan.FromSeconds(2),
                },
                RequiredProjectionId = requireProjection ? RealProjectionId : null,
                RequiredProjectionGuestPath = requireProjection ? RealGuestWorkspacePath : null,
                RequireVerifiedProjection = requireProjection,
            },
        };

    private static AppleVirtualizationHelperEnvelope RealProcessLifecycleRequest(
        AppleVirtualizationHelperOperation operation,
        string processId,
        long sequenceNumber,
        TimeSpan? timeout) =>
        AppleVirtualizationHelperEnvelope.Request(
            operation,
            "real-process-lifecycle-" + sequenceNumber.ToString(System.Globalization.CultureInfo.InvariantCulture),
            sequenceNumber,
            AppleVirtualizationHelperProtocol.ProcessRequestSchema) with
        {
            ProcessLifecycleRequest = new AppleVirtualizationProcessLifecycleRequest
            {
                ProcessId = processId,
                Timeout = timeout,
                AfterOutputSequence = 0,
                OutputLimit = 64,
            },
        };

    private static ReadOnlyMemory<byte> CapturedBytes(ProcessStreamOutput output) =>
        output.CapturedBytes.Length > RealOutputTailBytes
            ? output.CapturedBytes[..RealOutputTailBytes]
            : output.CapturedBytes;

    private static AppleVirtualizationHelperEnvelope HostLifecycleResponse(
        AppleVirtualizationHelperOperation operation,
        RuntimeHostPhase hostPhase,
        ResourcePhase phase,
        bool reachable) =>
        new()
        {
            MessageType = AppleVirtualizationHelperMessageType.Response,
            Operation = operation,
            RequestId = "response-1",
            ResponseStatus = AppleVirtualizationHelperResponseStatus.Ok,
            SequenceNumber = 1,
            PayloadSchema = AppleVirtualizationHelperProtocol.HostResponseSchema,
            HostStatusResponse = new AppleVirtualizationHostStatusResponse
            {
                HostId = "runtime-host-real",
                HostPhase = hostPhase,
                Phase = phase,
                GuestControlReachable = reachable,
            },
        };

    private sealed class AppleVirtualizationRealAcceptanceEnvironment
    {
        private static readonly string[] RequiredVariables =
        [
            "HPD_APPLEVZ_REAL_HELPER_PATH",
            "HPD_APPLEVZ_GUEST_KERNEL",
            "HPD_APPLEVZ_GUEST_INITRD",
            "HPD_APPLEVZ_GUEST_DISK",
            "HPD_APPLEVZ_GUEST_SERIAL_LOG",
            "HPD_APPLEVZ_EXPECTED_GUEST_AGENT_VERSION",
            "HPD_APPLEVZ_VIRTIOFS_HOST_PATH",
            "HPD_APPLEVZ_VIRTIOFS_TAG",
        ];

        private AppleVirtualizationRealAcceptanceEnvironment(
            string helperPath,
            AppleVirtualizationGuestImageOptions guestImage,
            IReadOnlyList<AppleVirtualizationVmConfigurationSharedDirectory> sharedDirectories,
            string skipReason)
        {
            HelperPath = helperPath;
            GuestImage = guestImage;
            SharedDirectories = sharedDirectories;
            SkipReason = skipReason;
        }

        public string HelperPath { get; }
        public AppleVirtualizationGuestImageOptions GuestImage { get; }
        public IReadOnlyList<AppleVirtualizationVmConfigurationSharedDirectory> SharedDirectories { get; }
        public string SkipReason { get; }
        public bool CanAttemptRealBoot => string.IsNullOrEmpty(SkipReason);

        public static AppleVirtualizationRealAcceptanceEnvironment Parse(
            Func<string, string?> getEnvironmentVariable,
            bool hostSupported)
        {
            ArgumentNullException.ThrowIfNull(getEnvironmentVariable);
            string[] missing = RequiredVariables
                .Where(name => string.IsNullOrWhiteSpace(getEnvironmentVariable(name)))
                .ToArray();
            if (missing.Length > 0)
            {
                return Skipped("Missing required Apple Virtualization real acceptance env vars: " + string.Join(", ", missing));
            }

            if (!hostSupported)
            {
                return Skipped("Apple Virtualization real acceptance skipped because host capability is unsupported.");
            }

            string helper = getEnvironmentVariable("HPD_APPLEVZ_REAL_HELPER_PATH")!;
            string serialLog = getEnvironmentVariable("HPD_APPLEVZ_GUEST_SERIAL_LOG")!;
            EnsureSafeSerialParent(serialLog);

            var guestImage = new AppleVirtualizationGuestImageOptions
            {
                BundleRoot = NullIfWhiteSpace(getEnvironmentVariable("HPD_APPLEVZ_GUEST_BUNDLE_ROOT")),
                BootLoader = AppleVirtualizationGuestBootLoaderKind.LinuxBootLoader,
                KernelPath = getEnvironmentVariable("HPD_APPLEVZ_GUEST_KERNEL"),
                InitrdPath = getEnvironmentVariable("HPD_APPLEVZ_GUEST_INITRD"),
                KernelCommandLine = NullIfWhiteSpace(getEnvironmentVariable("HPD_APPLEVZ_GUEST_KERNEL_CMDLINE")),
                DiskAttachments = AppleVirtualizationTestDiskSet.Create(getEnvironmentVariable("HPD_APPLEVZ_GUEST_DISK")),
                SerialLogPath = serialLog,
                Architecture = AppleVirtualizationGuestArchitectureExpectation.HostNative,
                ExpectVirtiofsSupport = true,
                ExpectedGuestAgentVersion = getEnvironmentVariable("HPD_APPLEVZ_EXPECTED_GUEST_AGENT_VERSION"),
            };

            IReadOnlyList<AppleVirtualizationVmConfigurationSharedDirectory> sharedDirectories =
                CreateSharedDirectories(getEnvironmentVariable);
            return new AppleVirtualizationRealAcceptanceEnvironment(helper, guestImage, sharedDirectories, skipReason: string.Empty);
        }

        public AppleVirtualizationVmConfigurationValidationRequest CreateVmConfiguration(string hostId) =>
            new()
            {
                HostId = hostId,
                CpuCount = 2,
                MemorySizeBytes = 2L * 1024 * 1024 * 1024,
                GuestImage = GuestImage,
                SharedDirectories = SharedDirectories,
                IncludeSerialConsole = true,
                IncludeVirtioSocketPlaceholder = true,
            };

        public static byte[] ReadSerialLogTail(string path, int maxBytes)
        {
            if (string.IsNullOrWhiteSpace(path) || maxBytes <= 0 || !File.Exists(path))
            {
                return [];
            }

            using FileStream stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            int bytesToRead = (int)Math.Min(maxBytes, stream.Length);
            byte[] buffer = new byte[bytesToRead];
            stream.Seek(-bytesToRead, SeekOrigin.End);
            int read = stream.Read(buffer, 0, bytesToRead);
            return read == buffer.Length ? buffer : buffer[..read];
        }

        private static AppleVirtualizationRealAcceptanceEnvironment Skipped(string reason) =>
            new(
                helperPath: string.Empty,
                guestImage: new AppleVirtualizationGuestImageOptions(),
                sharedDirectories: Array.Empty<AppleVirtualizationVmConfigurationSharedDirectory>(),
                skipReason: reason);

        private static IReadOnlyList<AppleVirtualizationVmConfigurationSharedDirectory> CreateSharedDirectories(
            Func<string, string?> getEnvironmentVariable)
        {
            return
            [
                new AppleVirtualizationVmConfigurationSharedDirectory
                {
                    HostPath = getEnvironmentVariable("HPD_APPLEVZ_VIRTIOFS_HOST_PATH")!,
                    Tag = getEnvironmentVariable("HPD_APPLEVZ_VIRTIOFS_TAG")!,
                    ReadOnly = true,
                },
            ];
        }

        private static void EnsureSafeSerialParent(string serialLogPath)
        {
            string? parent = Path.GetDirectoryName(serialLogPath);
            if (!string.IsNullOrWhiteSpace(parent))
            {
                Directory.CreateDirectory(parent);
            }
        }

        private static string? NullIfWhiteSpace(string? value) =>
            string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private sealed class RealHelperProcess : IAsyncDisposable
    {
        private readonly Process _process;

        private RealHelperProcess(Process process)
        {
            _process = process;
        }

        public static async Task<RealHelperProcess> StartAsync(string helperPath)
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo(helperPath)
                {
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                },
            };
            process.Start().Should().BeTrue();
            await Task.Yield();
            return new RealHelperProcess(process);
        }

        public async Task<AppleVirtualizationHelperEnvelope> SendAsync(
            AppleVirtualizationHelperEnvelope envelope,
            TimeSpan timeout)
        {
            using var cancellation = new CancellationTokenSource(timeout);
            string json = JsonSerializer.Serialize(
                envelope,
                AppleVirtualizationJsonContext.Default.AppleVirtualizationHelperEnvelope);
            await _process.StandardInput.WriteLineAsync(json).WaitAsync(cancellation.Token).ConfigureAwait(false);
            await _process.StandardInput.FlushAsync(cancellation.Token).ConfigureAwait(false);
            string? line = await _process.StandardOutput.ReadLineAsync().WaitAsync(cancellation.Token).ConfigureAwait(false);
            if (line is null)
            {
                string stderr = await _process.StandardError.ReadToEndAsync(cancellation.Token).ConfigureAwait(false);
                throw new InvalidOperationException("hpd-vz exited before writing a response. stderr: " + stderr);
            }

            return JsonSerializer.Deserialize(
                line,
                AppleVirtualizationJsonContext.Default.AppleVirtualizationHelperEnvelope)
                ?? throw new JsonException("Swift helper response was not a helper envelope.");
        }

        public async Task TrySendAsync(AppleVirtualizationHelperEnvelope envelope, TimeSpan timeout)
        {
            try
            {
                await SendAsync(envelope, timeout).ConfigureAwait(false);
            }
            catch
            {
            }
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                _process.StandardInput.Close();
                using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await _process.WaitForExitAsync(cancellation.Token).ConfigureAwait(false);
            }
            catch
            {
                if (!_process.HasExited)
                {
                    _process.Kill(entireProcessTree: true);
                }
            }
            finally
            {
                _process.Dispose();
            }
        }
    }

    private sealed class RealAcceptanceFiles : IDisposable
    {
        private readonly string _root;
        private readonly Dictionary<string, string> _environment = new(StringComparer.Ordinal);

        private RealAcceptanceFiles(string root)
        {
            _root = root;
            HelperPath = Path.Combine(root, "hpd-vz");
            KernelPath = Path.Combine(root, "vmlinuz");
            InitrdPath = Path.Combine(root, "initrd.img");
            DiskPath = Path.Combine(root, "root.raw");
            SerialLogPath = Path.Combine(root, "logs", "serial.log");
        }

        public string HelperPath { get; }
        public string KernelPath { get; }
        public string InitrdPath { get; }
        public string DiskPath { get; }
        public string SerialLogPath { get; }

        public static RealAcceptanceFiles Create(bool includeOptional = false, bool includeProjection = true)
        {
            string root = Path.Combine(Path.GetTempPath(), "hpd-applevz-real-acceptance-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var files = new RealAcceptanceFiles(root);
            Directory.CreateDirectory(Path.GetDirectoryName(files.SerialLogPath)!);
            File.WriteAllText(files.HelperPath, "#!/bin/sh\nexit 0\n");
            File.WriteAllBytes(files.KernelPath, [0x48, 0x50, 0x44]);
            File.WriteAllBytes(files.InitrdPath, [0x48, 0x50, 0x44]);
            File.WriteAllBytes(files.DiskPath, new byte[4096]);
            files._environment["HPD_APPLEVZ_REAL_HELPER_PATH"] = files.HelperPath;
            files._environment["HPD_APPLEVZ_GUEST_KERNEL"] = files.KernelPath;
            files._environment["HPD_APPLEVZ_GUEST_INITRD"] = files.InitrdPath;
            files._environment["HPD_APPLEVZ_GUEST_DISK"] = files.DiskPath;
            files._environment["HPD_APPLEVZ_GUEST_SERIAL_LOG"] = files.SerialLogPath;
            files._environment["HPD_APPLEVZ_EXPECTED_GUEST_AGENT_VERSION"] = "0.1.0";
            if (includeProjection)
            {
                files._environment["HPD_APPLEVZ_VIRTIOFS_HOST_PATH"] = root;
                files._environment["HPD_APPLEVZ_VIRTIOFS_TAG"] = "hpd.share";
            }

            if (includeOptional)
            {
                files._environment["HPD_APPLEVZ_GUEST_BUNDLE_ROOT"] = root;
                files._environment["HPD_APPLEVZ_GUEST_KERNEL_CMDLINE"] = "console=hvc0 hpd.acceptance=1";
            }

            return files;
        }

        public string? GetEnvironmentValue(string name) =>
            _environment.TryGetValue(name, out string? value) ? value : null;

        public void Dispose()
        {
            try
            {
                Directory.Delete(_root, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
