namespace HPD.Environment.AppleVirtualization.Tests;

using FluentAssertions;
using HPD.Environment.AppleVirtualization.ExecutionUnits;
using HPD.Environment.AppleVirtualization.GuestAgent;
using HPD.Environment.AppleVirtualization.Hosts;
using HPD.Environment.AppleVirtualization.Processes;
using HPD.Environment.AppleVirtualization.Projections;
using HPD.Environment.AppleVirtualization.Protocol;
using HPD.Environment.AppleVirtualization.State;
using HPD.Environment.AppleVirtualization.Tests.Fixtures;
using HPD.Environment.Contracts;
using HPD.Environment.Runtime;
using Xunit;

public sealed class AppleVirtualizationVerticalSliceAcceptanceTests : IDisposable
{
    private readonly string _workspacePath;

    public AppleVirtualizationVerticalSliceAcceptanceTests()
    {
        _workspacePath = Path.Combine(Path.GetTempPath(), "hpd-applevz-l9-vertical-slice", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workspacePath);
        File.WriteAllText(Path.Combine(_workspacePath, "README.md"), "vertical slice\n");
    }

    [Fact]
    public async Task Fake_full_stack_vertical_slice_reaches_ready_projects_workspace_runs_commands_and_cleans_up()
    {
        var helper = new FakeAppleVirtualizationHelperClient();
        var ledger = new AppleVirtualizationProviderStateLedger();
        var hostProvider = new AppleVirtualizationRuntimeHostProvider(
            helper,
            ledger,
            new PlatformSpec("macos", "arm64"));
        var projectionProvider = new AppleVirtualizationContentProjectionProvider(helper, ledger);
        var unitProvider = new AppleVirtualizationExecutionUnitProvider(ledger, helper);
        var processProvider = new AppleVirtualizationProcessProvider(ledger, helper);

        EnqueueReadyHostFlow(helper);
        RuntimeHostStatus host = await hostProvider.EnsureAsync(
            AppleVirtualizationContractFixtures.Metadata<RuntimeHost>("runtime-host-1", "runtime-host"),
            AppleVirtualizationContractFixtures.RuntimeHostSpec(),
            observed: null);

        host.HostPhase.Should().Be(RuntimeHostPhase.Ready);
        host.Phase.Should().Be(ResourcePhase.Ready);
        host.Readiness.Should().NotBeNull();
        host.Readiness!.Ready.Should().BeTrue();

        EnqueueVerifiedProjectionFlow(helper, "projection-1", "/workspace");
        ContentProjectionStatus projection = await projectionProvider.ProjectAsync(
            AppleVirtualizationContractFixtures.Metadata<ContentProjection>("projection-1", "content-projection"),
            ProjectionSpec(_workspacePath),
            host.Handle,
            unit: null);

        projection.ProjectionPhase.Should().Be(ContentProjectionPhase.Projected);
        projection.Phase.Should().Be(ResourcePhase.Ready);
        projection.Views.Should().ContainSingle(view =>
            view.GuestPath == new GuestPath("/workspace") &&
            view.EffectiveAccess == AccessMode.ReadOnly &&
            view.EffectiveRealization == ProjectionRealizationKind.LiveProjection);

        helper.EnqueueResponse(UnitResponse(AppleVirtualizationHelperOperation.UnitEnsure, ExecutionUnitPhase.Ready, "/workspace"));
        ExecutionUnitStatus unit = await unitProvider.EnsureAsync(
            AppleVirtualizationContractFixtures.Metadata<ExecutionUnit>("unit-1", "execution-unit"),
            AppleVirtualizationContractFixtures.ExecutionUnitSpec(),
            observed: null);

        unit.UnitPhase.Should().Be(ExecutionUnitPhase.Ready);
        unit.RealizedContentProjections.Should().ContainSingle().Which.Id.Value.Should().Be("projection-1");

        var outputSink = new RecordingProcessOutputSink();
        EnqueueProcessRun(
            helper,
            processId: "process-1",
            stdout: "Linux hpd-vm 6.8.0 #1 SMP arm64 GNU/Linux\n"u8.ToArray(),
            stderr: ReadOnlyMemory<byte>.Empty,
            exitCode: 0);

        ProcessInvocationResult uname = await processProvider.RunAsync(
            AppleVirtualizationContractFixtures.ProcessInvocationSpec(unit.Handle, fileName: "uname"),
            outputSink);

        uname.CompletionKind.Should().Be(ProcessCompletionKind.Exited);
        uname.ExitCode.Should().Be(0);
        uname.Output.Stdout.CapturedBytes.ToArray().Should().ContainInOrder("Linux hpd-vm"u8.ToArray());
        uname.Output.Stdout.BytesObserved.Should().Be(uname.Output.Stdout.BytesCaptured);
        uname.Output.Stdout.BytesDiscarded.Should().Be(0);

        EnqueueProcessRun(
            helper,
            processId: "process-2",
            stdout: "/workspace\nREADME.md\n"u8.ToArray(),
            stderr: "listed projected workspace\n"u8.ToArray(),
            exitCode: 0);

        ProcessInvocationSpec pwdLsSpec = AppleVirtualizationContractFixtures.ProcessInvocationSpec(unit.Handle, fileName: "sh") with
        {
            Command = new ProcessCommandSpec
            {
                FileName = "sh",
                Arguments = ["-lc", "pwd && ls"],
                WorkingDirectory = "/workspace",
                Environment = new Dictionary<string, string?>
                {
                    ["HPD_ACCEPTANCE"] = "1",
                },
            },
        };

        ProcessInvocationResult pwdLs = await processProvider.RunAsync(pwdLsSpec, outputSink);

        pwdLs.CompletionKind.Should().Be(ProcessCompletionKind.Exited);
        pwdLs.ExitCode.Should().Be(0);
        pwdLs.Output.Stdout.CapturedBytes.ToArray().Should().ContainInOrder("/workspace"u8.ToArray());
        pwdLs.Output.Stdout.CapturedBytes.ToArray().Should().ContainInOrder("README.md"u8.ToArray());
        pwdLs.Output.Stdout.BytesObserved.Should().Be(pwdLs.Output.Stdout.BytesCaptured);
        pwdLs.Output.Stdout.Truncated.Should().BeFalse();
        pwdLs.Output.Stderr.CapturedBytes.ToArray().Should().ContainInOrder("listed projected workspace"u8.ToArray());

        outputSink.Chunks.Should().HaveCount(3);
        outputSink.Chunks.Should().OnlyContain(chunk => chunk.Bytes.Length > 0);
        outputSink.Chunks.Select(chunk => chunk.Stream).Should().Contain(ProcessOutputStream.Stdout);
        outputSink.Chunks.Select(chunk => chunk.Stream).Should().Contain(ProcessOutputStream.Stderr);
        outputSink.Chunks.Should().OnlyContain(chunk => chunk.Process.Route.ProviderId == AppleVirtualizationProviderDescriptor.ProviderId);

        helper.EnqueueResponse(OkResponse(AppleVirtualizationHelperOperation.ProjectionRelease));
        helper.EnqueueResponse(UnitResponse(AppleVirtualizationHelperOperation.UnitStop, ExecutionUnitPhase.Stopped, "/workspace"));
        helper.EnqueueResponse(HostResponse(AppleVirtualizationHelperOperation.HostStop, RuntimeHostPhase.Stopped));

        ExecutionUnitStatus stoppedUnit = await unitProvider.StopAsync(unit.Handle!.Value, StopPolicy.Default);
        await projectionProvider.ReleaseAsync(projection.Views.Single().ProviderHandle is null
            ? AppleVirtualizationContractFixtures.ContentProjectionHandle()
            : new TargetHandle<ContentProjection>(
                new TargetRoute
                {
                    Kind = new TargetKind(nameof(ContentProjection)),
                    Scope = AppleVirtualizationContractFixtures.RuntimeScope,
                    Segments = [new TargetRouteSegment(TargetRouteSegmentKind.ContentProjection, "projection-1")],
                    BackingResourceKind = new ResourceKind("content-projection"),
                    BackingResourceId = "projection-1",
                    ProviderId = AppleVirtualizationProviderDescriptor.ProviderId,
                    ProviderHandle = projection.ProviderHandle,
                },
                TargetHandleLifetime.LiveCapability,
                TargetHandleAuthority.Observe | TargetHandleAuthority.Control | TargetHandleAuthority.Read,
                1));
        RuntimeHostStatus stoppedHost = await hostProvider.StopAsync(
            host.Handle!.Value,
            StopPolicy.Default with { Kind = StopKind.Kill });

        stoppedUnit.UnitPhase.Should().Be(ExecutionUnitPhase.Stopped);
        stoppedHost.HostPhase.Should().Be(RuntimeHostPhase.Stopped);
        helper.Requests.Select(request => request.Operation).Should().ContainInOrder(
            AppleVirtualizationHelperOperation.HostEnsure,
            AppleVirtualizationHelperOperation.HostStart,
            AppleVirtualizationHelperOperation.HostStatus,
            AppleVirtualizationHelperOperation.GuestAgentReadinessProbe,
            AppleVirtualizationHelperOperation.ProjectionConfigure,
            AppleVirtualizationHelperOperation.ProjectionMount,
            AppleVirtualizationHelperOperation.UnitEnsure,
            AppleVirtualizationHelperOperation.ProcessStart,
            AppleVirtualizationHelperOperation.ProcessReadOutput,
            AppleVirtualizationHelperOperation.ProcessWait,
            AppleVirtualizationHelperOperation.ProcessStart,
            AppleVirtualizationHelperOperation.ProcessReadOutput,
            AppleVirtualizationHelperOperation.ProcessWait,
            AppleVirtualizationHelperOperation.ProjectionRelease,
            AppleVirtualizationHelperOperation.UnitStop,
            AppleVirtualizationHelperOperation.HostStop);
        helper.Requests.Should().NotContain(request => request.Operation == AppleVirtualizationHelperOperation.ProcessResize);
    }

    [Fact]
    public void Vertical_slice_toolharness_keeps_terminal_resize_and_deferred_lanes_out_of_scope()
    {
        var registry = new EnvironmentProviderRegistry();
        registry.RegisterAppleVirtualizationProvider();

        registry.RuntimeHostProviders.Should().ContainSingle();
        registry.ExecutionUnitProviders.Should().ContainSingle();
        registry.ContentProjectionProviders.Should().ContainSingle();
        registry.ProcessProviders.Should().ContainSingle();
        registry.NetworkProviders.Should().ContainSingle();
        registry.NetworkMembershipProviders.Should().ContainSingle();
        registry.ServiceDiscoveryProviders.Should().ContainSingle();
        registry.EndpointPublicationProviders.Should().ContainSingle();
        registry.AuthorityBindingProviders.Should().ContainSingle();
        registry.EngineControlPlaneProviders.Should().BeEmpty();
        registry.ArtifactProviders.Should().BeEmpty();
        registry.RootFilesystemProviders.Should().BeEmpty();
        registry.FunctionSandboxProviders.Should().BeEmpty();
        registry.FunctionSnapshotProviders.Should().BeEmpty();
    }

    private ContentProjectionSpec ProjectionSpec(string hostPath) =>
        AppleVirtualizationContractFixtures.ReadOnlyWorkspaceProjection() with
        {
            Source = new ContentSelector
            {
                Kind = ContentSelectorKind.HostPath,
                HostPath = new HostPathSelection(new HostPath(hostPath), HostPathKind.Directory),
            },
        };

    private static void EnqueueReadyHostFlow(FakeAppleVirtualizationHelperClient helper)
    {
        helper.EnqueueResponse(HostResponse(AppleVirtualizationHelperOperation.HostEnsure, RuntimeHostPhase.Preparing));
        helper.EnqueueResponse(HostResponse(AppleVirtualizationHelperOperation.HostStart, RuntimeHostPhase.Running));
        helper.EnqueueResponse(HostResponse(AppleVirtualizationHelperOperation.HostStatus, RuntimeHostPhase.Running, ResourcePhase.Ready));
        helper.EnqueueResponse(new AppleVirtualizationHelperEnvelope
        {
            MessageType = AppleVirtualizationHelperMessageType.Response,
            Operation = AppleVirtualizationHelperOperation.GuestAgentReadinessProbe,
            ResponseStatus = AppleVirtualizationHelperResponseStatus.Ok,
            SequenceNumber = 1,
            ProviderGeneration = 1,
            PayloadSchema = AppleVirtualizationHelperProtocol.GuestAgentReadinessResponseSchema,
            GuestAgentReadinessProbeResponse = new AppleVirtualizationGuestAgentReadinessProbeResponse
            {
                HostId = "runtime-host-1",
                State = AppleVirtualizationGuestAgentReadinessState.Ready,
                VerifiedReady = true,
                TransportConnected = true,
                ProtocolVersion = AppleVirtualizationHelperProtocol.CurrentVersion,
                AgentVersion = "0.1.0-test",
                GuestBootId = "boot-l9",
                GuestBootGeneration = 1,
                GuestAgentGeneration = 1,
                Capabilities = new AppleVirtualizationGuestAgentCapabilities
                {
                    ProjectionMount = true,
                    ProcessStart = true,
                    ProcessReadOutput = true,
                },
            },
        });
    }

    private static void EnqueueVerifiedProjectionFlow(
        FakeAppleVirtualizationHelperClient helper,
        string projectionId,
        string guestPath)
    {
        helper.EnqueueResponse(OkResponse(AppleVirtualizationHelperOperation.ProjectionConfigure));
        helper.EnqueueResponse(new AppleVirtualizationHelperEnvelope
        {
            MessageType = AppleVirtualizationHelperMessageType.Response,
            Operation = AppleVirtualizationHelperOperation.ProjectionMount,
            ResponseStatus = AppleVirtualizationHelperResponseStatus.Ok,
            SequenceNumber = 1,
            ProviderGeneration = 1,
            PayloadSchema = AppleVirtualizationHelperProtocol.ProjectionResponseSchema,
            ProjectionStatusResponse = new AppleVirtualizationProjectionStatusResponse
            {
                ProjectionId = projectionId,
                ProjectionPhase = ContentProjectionPhase.Projected,
                EffectiveRealization = ProjectionRealizationKind.LiveProjection,
                EffectiveWriteEffect = ProjectionWriteEffect.NoWrites,
                EffectiveCoherence = CoherenceClass.CloseToOpen,
                GuestAgentReady = true,
                HostShareConfigured = true,
                FrameworkShareAccepted = true,
                VerifiedByGuestAgent = true,
                GuestProjectionStatus = new AppleVirtualizationGuestAgentProjectionStatus
                {
                    ProjectionId = projectionId,
                    GuestPath = guestPath,
                    Tag = "hpdprojection",
                    Mounted = true,
                    GuestMountVerified = true,
                    HostShareState = AppleVirtualizationGuestAgentProjectionHostShareState.HostShareConfigured,
                    FrameworkShareState = AppleVirtualizationGuestAgentProjectionFrameworkShareState.Accepted,
                    VerificationState = AppleVirtualizationGuestAgentProjectionVerificationState.ReadyForHpdUse,
                    ExpectedGuestPath = guestPath,
                    ActualGuestPath = guestPath,
                    RequestedAccessMode = AccessMode.ReadOnly,
                    EffectiveAccessMode = AccessMode.ReadOnly,
                    ProjectionPhase = ContentProjectionPhase.Projected,
                    EffectiveRealization = ProjectionRealizationKind.LiveProjection,
                    EffectiveWriteEffect = ProjectionWriteEffect.NoWrites,
                    EffectiveCoherence = CoherenceClass.CloseToOpen,
                    EffectiveCache = CacheBehavior.ReadCache,
                    Generation = new AppleVirtualizationGuestAgentProjectionGenerationStamp(
                        ProviderGeneration: 1,
                        HostStartGeneration: 1,
                        GuestBootId: "boot-l9",
                        GuestBootGeneration: 1,
                        GuestAgentGeneration: 1,
                        ProjectionGeneration: 1),
                    Conditions =
                    [
                        Condition("AppleVirtualization.GuestMountVerified", ConditionStatus.True, "Mounted", "Guest mount verified.", new ResourceGeneration(1)),
                    ],
                },
                Conditions =
                [
                    Condition("AppleVirtualization.GuestMountVerified", ConditionStatus.True, "Mounted", "Guest mount verified.", new ResourceGeneration(1)),
                ],
            },
        });
    }

    private static void EnqueueProcessRun(
        FakeAppleVirtualizationHelperClient helper,
        string processId,
        ReadOnlyMemory<byte> stdout,
        ReadOnlyMemory<byte> stderr,
        int exitCode)
    {
        helper.EnqueueResponse(ProcessStatus(AppleVirtualizationHelperOperation.ProcessStart, processId, ProcessInvocationPhase.Running));
        helper.EnqueueResponse(ProcessStatus(AppleVirtualizationHelperOperation.ProcessReadOutput, processId, ProcessInvocationPhase.Running));
        if (!stdout.IsEmpty)
        {
            helper.EnqueueEvent(ProcessOutput(processId, ProcessOutputStream.Stdout, stdout, sequence: 1, final: stderr.IsEmpty));
        }

        if (!stderr.IsEmpty)
        {
            helper.EnqueueEvent(ProcessOutput(processId, ProcessOutputStream.Stderr, stderr, sequence: 2, final: true));
        }

        helper.EnqueueResponse(ProcessResult(processId, ProcessInvocationPhase.Exited, ProcessCompletionKind.Exited, exitCode));
    }

    private static AppleVirtualizationHelperEnvelope HostResponse(
        AppleVirtualizationHelperOperation operation,
        RuntimeHostPhase hostPhase,
        ResourcePhase? phase = null) =>
        new()
        {
            MessageType = AppleVirtualizationHelperMessageType.Response,
            Operation = operation,
            ResponseStatus = AppleVirtualizationHelperResponseStatus.Ok,
            SequenceNumber = 1,
            ProviderGeneration = 1,
            PayloadSchema = AppleVirtualizationHelperProtocol.HostResponseSchema,
            HostStatusResponse = new AppleVirtualizationHostStatusResponse
            {
                HostId = "runtime-host-1",
                HostPhase = hostPhase,
                Phase = phase ?? (hostPhase == RuntimeHostPhase.Ready ? ResourcePhase.Ready : ResourcePhase.Reconciling),
                ProviderHandle = new ProviderOpaqueHandle(AppleVirtualizationProviderDescriptor.ProviderId, "host:runtime-host-1", Generation: 1),
                GuestControlReachable = hostPhase == RuntimeHostPhase.Ready,
            },
        };

    private static AppleVirtualizationHelperEnvelope UnitResponse(
        AppleVirtualizationHelperOperation operation,
        ExecutionUnitPhase phase,
        string? workingDirectory = null) =>
        new()
        {
            MessageType = AppleVirtualizationHelperMessageType.Response,
            Operation = operation,
            ResponseStatus = AppleVirtualizationHelperResponseStatus.Ok,
            SequenceNumber = 1,
            ProviderGeneration = 1,
            PayloadSchema = AppleVirtualizationHelperProtocol.UnitResponseSchema,
            UnitStatusResponse = new AppleVirtualizationUnitStatusResponse
            {
                UnitId = "unit-1",
                UnitPhase = phase,
                WorkingDirectory = workingDirectory,
            },
        };

    private static AppleVirtualizationHelperEnvelope ProcessStatus(
        AppleVirtualizationHelperOperation operation,
        string processId,
        ProcessInvocationPhase phase) =>
        new()
        {
            MessageType = AppleVirtualizationHelperMessageType.Response,
            Operation = operation,
            ResponseStatus = AppleVirtualizationHelperResponseStatus.Ok,
            SequenceNumber = 1,
            ProviderGeneration = 1,
            PayloadSchema = AppleVirtualizationHelperProtocol.ProcessResponseSchema,
            ProcessStatusResponse = new AppleVirtualizationProcessStatusResponse
            {
                ProcessId = processId,
                ProcessPhase = phase,
                IoState = ProcessIoState.Open,
                ProviderProcessId = "guest-" + processId,
            },
        };

    private static AppleVirtualizationHelperEnvelope ProcessResult(
        string processId,
        ProcessInvocationPhase phase,
        ProcessCompletionKind completionKind,
        int? exitCode) =>
        new()
        {
            MessageType = AppleVirtualizationHelperMessageType.Response,
            Operation = AppleVirtualizationHelperOperation.ProcessWait,
            ResponseStatus = AppleVirtualizationHelperResponseStatus.Ok,
            SequenceNumber = 1,
            ProviderGeneration = 1,
            PayloadSchema = AppleVirtualizationHelperProtocol.ProcessResponseSchema,
            ProcessStatusResponse = new AppleVirtualizationProcessStatusResponse
            {
                ProcessId = processId,
                ProcessPhase = phase,
                IoState = ProcessIoState.Closed,
                ProviderProcessId = "guest-" + processId,
                Result = new ProcessInvocationResult
                {
                    ProcessId = new ResourceId<ProcessInvocation>(processId),
                    ProviderProcessId = "guest-" + processId,
                    ExitCode = exitCode,
                    CompletionKind = completionKind,
                    StartedAt = new DateTimeOffset(2026, 5, 20, 12, 0, 0, TimeSpan.Zero),
                    ExitedAt = new DateTimeOffset(2026, 5, 20, 12, 0, 1, TimeSpan.Zero),
                    Duration = TimeSpan.FromSeconds(1),
                    Output = new ProcessCapturedOutput
                    {
                        Stdout = new ProcessStreamOutput(),
                        Stderr = new ProcessStreamOutput(),
                        OutputDrainTimeout = ProcessInvocationPolicy.Default.OutputDrainTimeout,
                    },
                },
            },
        };

    private static AppleVirtualizationHelperEnvelope ProcessOutput(
        string processId,
        ProcessOutputStream stream,
        ReadOnlyMemory<byte> bytes,
        long sequence,
        bool final) =>
        new()
        {
            MessageType = AppleVirtualizationHelperMessageType.Event,
            Operation = AppleVirtualizationHelperOperation.ProcessReadOutput,
            EventKind = AppleVirtualizationHelperEventKind.ProcessOutput,
            ResponseStatus = AppleVirtualizationHelperResponseStatus.Ok,
            SequenceNumber = sequence,
            ProviderGeneration = 1,
            PayloadSchema = AppleVirtualizationHelperProtocol.ProcessOutputEventSchema,
            ProcessOutputEvent = new AppleVirtualizationProcessOutputEvent
            {
                ProcessId = processId,
                Stream = stream,
                Sequence = sequence,
                ObservedAt = new DateTimeOffset(2026, 5, 20, 12, 0, 0, TimeSpan.Zero),
                Bytes = bytes,
                Flags = final ? ProcessOutputChunkFlags.Final : ProcessOutputChunkFlags.None,
            },
        };

    private static AppleVirtualizationHelperEnvelope OkResponse(AppleVirtualizationHelperOperation operation) =>
        new()
        {
            MessageType = AppleVirtualizationHelperMessageType.Response,
            Operation = operation,
            ResponseStatus = AppleVirtualizationHelperResponseStatus.Ok,
            SequenceNumber = 1,
            ProviderGeneration = 1,
        };

    private static Condition Condition(
        string type,
        ConditionStatus status,
        string reason,
        string message,
        ResourceGeneration generation) =>
        new(type, status, reason, message, new DateTimeOffset(2026, 5, 20, 12, 0, 0, TimeSpan.Zero), generation);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_workspacePath, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed class RecordingProcessOutputSink : IProcessOutputSink
    {
        public List<ProcessOutputChunk> Chunks { get; } = [];

        public ValueTask OnOutputAsync(ProcessOutputChunk chunk, CancellationToken cancellationToken = default)
        {
            Chunks.Add(chunk);
            return ValueTask.CompletedTask;
        }
    }
}
