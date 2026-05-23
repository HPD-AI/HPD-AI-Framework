namespace HPD.Execution.AppleVirtualization.Tests;

using FluentAssertions;
using HPD.Execution.AppleVirtualization.ExecutionUnits;
using HPD.Execution.AppleVirtualization.GuestAgent;
using HPD.Execution.AppleVirtualization.Hosts;
using HPD.Execution.AppleVirtualization.Processes;
using HPD.Execution.AppleVirtualization.Projections;
using HPD.Execution.AppleVirtualization.Protocol;
using HPD.Execution.AppleVirtualization.State;
using HPD.Execution.AppleVirtualization.Tests.Fixtures;
using HPD.Execution.Contracts;
using HPD.Execution.Runtime;
using Xunit;

public sealed class AppleVirtualizationRuntimeWorkflowTests : IDisposable
{
    private readonly string _workspacePath;

    public AppleVirtualizationRuntimeWorkflowTests()
    {
        _workspacePath = Path.Combine(Path.GetTempPath(), "hpd-applevz-l9-runtime-workflow", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workspacePath);
        File.WriteAllText(Path.Combine(_workspacePath, "README.md"), "runtime workflow\n");
    }

    [Fact]
    public async Task Runtime_facade_workflow_composes_host_unit_projection_process_and_safe_finalization()
    {
        RuntimeWorkflowFixture fixture = CreateRuntimeFixture();

        RuntimePlan plan = await fixture.Runtime.PlanAsync(new RuntimePlanRequest
        {
            TopologyPolicy = new RuntimeTopologyPolicy(),
            RequestedPlatform = new PlatformSpec("linux", "arm64"),
            RequiredContracts = AppleVirtualizationProviderDescriptor.FirstSliceContracts,
        });
        RuntimePlanValidationResult validation = await fixture.Runtime.ValidateAsync(plan);

        validation.IsSupported.Should().BeTrue();
        plan.Providers.Select(provider => provider.ProviderId).Should().OnlyContain(id => id == AppleVirtualizationProviderDescriptor.ProviderId);

        EnqueueReadyHostFlow(fixture.Helper);
        ResourceSnapshot<RuntimeHost, RuntimeHostSpec, RuntimeHostStatus> host = await fixture.Runtime.EnsureHostAsync(
            AppleVirtualizationContractFixtures.RuntimeHostSpec());

        host.Status.HostPhase.Should().Be(RuntimeHostPhase.Ready);
        host.Status.Readiness.Should().NotBeNull();
        host.Status.Readiness!.Ready.Should().BeTrue();

        ResourceMetadata<ContentProjection> projectionMetadata =
            AppleVirtualizationContractFixtures.Metadata<ContentProjection>("runtime-projection-1", "content-projection");
        EnqueueVerifiedProjectionFlow(fixture.Helper, projectionMetadata.Id.Value, "/workspace");
        ContentProjectionStatus projection = await fixture.ProjectionProvider.ProjectAsync(
            projectionMetadata,
            ProjectionSpec(host.Metadata, _workspacePath),
            host.Status.Handle,
            unit: null);

        projection.ProjectionPhase.Should().Be(ContentProjectionPhase.Projected);

        fixture.Helper.EnqueueResponse(UnitResponse(AppleVirtualizationHelperOperation.UnitEnsure, "runtime-unit", ExecutionUnitPhase.Ready, "/workspace"));
        ResourceSnapshot<ExecutionUnit, ExecutionUnitSpec, ExecutionUnitStatus> unit = await fixture.Runtime.EnsureExecutionUnitAsync(
            AppleVirtualizationContractFixtures.ExecutionUnitSpec(new ResourceRef<RuntimeHost>(
                host.Metadata.Id,
                host.Metadata.Scope,
                host.Metadata.Generation)) with
            {
                ContentProjections =
                [
                    new ResourceRef<ContentProjection>(
                        projectionMetadata.Id,
                        projectionMetadata.Scope,
                        projectionMetadata.Generation),
                ],
            });

        unit.Status.UnitPhase.Should().Be(ExecutionUnitPhase.Ready);
        unit.Status.AssignedHost.Should().Be(new ResourceRef<RuntimeHost>(
            host.Metadata.Id,
            host.Metadata.Scope,
            host.Metadata.Generation));
        unit.Status.RealizedContentProjections.Should().ContainSingle().Which.Id.Should().Be(projectionMetadata.Id);

        var sink = new RecordingProcessOutputSink();
        EnqueueProcessRun(
            fixture.Helper,
            processId: "process-1",
            stdout: "/workspace\nREADME.md\n"u8.ToArray(),
            stderr: ReadOnlyMemory<byte>.Empty,
            exitCode: 0);

        ProcessInvocationResult result = await fixture.Runtime.RunProcessAsync(
            AppleVirtualizationContractFixtures.ProcessInvocationSpec(unit.Status.Handle, fileName: "sh") with
            {
                Command = new ProcessCommandSpec
                {
                    FileName = "sh",
                    Arguments = ["-lc", "pwd && ls"],
                    WorkingDirectory = "/workspace",
                },
            },
            sink);

        result.CompletionKind.Should().Be(ProcessCompletionKind.Exited);
        result.ExitCode.Should().Be(0);
        result.Output.Stdout.CapturedBytes.ToArray().Should().ContainInOrder("/workspace"u8.ToArray());
        result.Output.Stdout.BytesObserved.Should().Be(result.Output.Stdout.BytesCaptured);
        sink.Chunks.Should().ContainSingle(chunk => chunk.Stream == ProcessOutputStream.Stdout);

        RuntimeFinalizationResult finalized = await fixture.Runtime.FinalizeRuntimeAsync(
            new RuntimeFinalizationRequest(host.Metadata.Scope, PromoteMemory: false, CleanupPolicy.Default));

        finalized.RuntimeScope.Should().Be(host.Metadata.Scope);
        finalized.ContentProjections.Should().BeEmpty();
        finalized.Diagnostics.Should().ContainSingle(diagnostic =>
            diagnostic.Code == new DiagnosticCode("hpd.execution.runtime.finalized"));
        fixture.Helper.Requests.Should().NotContain(request => request.Operation == AppleVirtualizationHelperOperation.ProcessResize);
        DeferredLanesRemainUnregistered(fixture.Registry);
    }

    [Fact]
    public async Task Direct_provider_workflow_composes_start_wait_and_run_through_shared_state()
    {
        RuntimeWorkflowFixture fixture = CreateRuntimeFixture();
        AppleVirtualizationRuntimeHostProvider hostProvider = (AppleVirtualizationRuntimeHostProvider)fixture.Registry.RuntimeHostProviders.Single();
        AppleVirtualizationExecutionUnitProvider unitProvider = (AppleVirtualizationExecutionUnitProvider)fixture.Registry.ExecutionUnitProviders.Single();
        AppleVirtualizationProcessProvider processProvider = (AppleVirtualizationProcessProvider)fixture.Registry.ProcessProviders.Single();

        EnqueueReadyHostFlow(fixture.Helper);
        ResourceMetadata<RuntimeHost> hostMetadata =
            AppleVirtualizationContractFixtures.Metadata<RuntimeHost>("direct-host", "runtime-host");
        RuntimeHostStatus host = await hostProvider.EnsureAsync(
            hostMetadata,
            AppleVirtualizationContractFixtures.RuntimeHostSpec(),
            observed: null);

        ResourceMetadata<ContentProjection> projectionMetadata =
            AppleVirtualizationContractFixtures.Metadata<ContentProjection>("direct-projection", "content-projection");
        EnqueueVerifiedProjectionFlow(fixture.Helper, projectionMetadata.Id.Value, "/workspace");
        ContentProjectionStatus projection = await fixture.ProjectionProvider.ProjectAsync(
            projectionMetadata,
            ProjectionSpec(hostMetadata, _workspacePath),
            host.Handle,
            unit: null);

        fixture.Helper.EnqueueResponse(UnitResponse(AppleVirtualizationHelperOperation.UnitEnsure, "direct-unit", ExecutionUnitPhase.Ready, "/workspace"));
        ExecutionUnitStatus unit = await unitProvider.EnsureAsync(
            AppleVirtualizationContractFixtures.Metadata<ExecutionUnit>("direct-unit", "execution-unit"),
            AppleVirtualizationContractFixtures.ExecutionUnitSpec(new ResourceRef<RuntimeHost>(
                hostMetadata.Id,
                hostMetadata.Scope,
                hostMetadata.Generation)) with
            {
                ContentProjections =
                [
                    new ResourceRef<ContentProjection>(
                        projectionMetadata.Id,
                        projectionMetadata.Scope,
                        projectionMetadata.Generation),
                ],
            },
            observed: null);

        projection.ProjectionPhase.Should().Be(ContentProjectionPhase.Projected);
        unit.UnitPhase.Should().Be(ExecutionUnitPhase.Ready);

        fixture.Helper.EnqueueResponse(ProcessStatus(AppleVirtualizationHelperOperation.ProcessStart, "process-1", ProcessInvocationPhase.Running));
        fixture.Helper.EnqueueResponse(ProcessResult("process-1", ProcessInvocationPhase.Exited, ProcessCompletionKind.Exited, exitCode: 0));
        await using IProcessInvocationHandle handle = await processProvider.StartAsync(
            AppleVirtualizationContractFixtures.ProcessInvocationSpec(unit.Handle, fileName: "uname"));

        ProcessInvocationResult waited = await handle.WaitAsync();

        waited.CompletionKind.Should().Be(ProcessCompletionKind.Exited);
        waited.ExitCode.Should().Be(0);

        var sink = new RecordingProcessOutputSink();
        EnqueueProcessRun(
            fixture.Helper,
            processId: "process-2",
            stdout: "second run\n"u8.ToArray(),
            stderr: ReadOnlyMemory<byte>.Empty,
            exitCode: 0);

        ProcessInvocationResult run = await processProvider.RunAsync(
            AppleVirtualizationContractFixtures.ProcessInvocationSpec(unit.Handle, fileName: "sh") with
            {
                Command = new ProcessCommandSpec
                {
                    FileName = "sh",
                    Arguments = ["-lc", "echo second run"],
                    WorkingDirectory = "/workspace",
                },
            },
            sink);

        run.CompletionKind.Should().Be(ProcessCompletionKind.Exited);
        run.Output.Stdout.CapturedBytes.ToArray().Should().ContainInOrder("second run"u8.ToArray());
        sink.Chunks.Should().ContainSingle(chunk => chunk.Stream == ProcessOutputStream.Stdout);
        fixture.Helper.Requests.Select(request => request.Operation).Should().ContainInOrder(
            AppleVirtualizationHelperOperation.HostEnsure,
            AppleVirtualizationHelperOperation.HostStart,
            AppleVirtualizationHelperOperation.HostStatus,
            AppleVirtualizationHelperOperation.GuestAgentReadinessProbe,
            AppleVirtualizationHelperOperation.ProjectionConfigure,
            AppleVirtualizationHelperOperation.ProjectionMount,
            AppleVirtualizationHelperOperation.UnitEnsure,
            AppleVirtualizationHelperOperation.ProcessStart,
            AppleVirtualizationHelperOperation.ProcessWait,
            AppleVirtualizationHelperOperation.ProcessStart,
            AppleVirtualizationHelperOperation.ProcessReadOutput,
            AppleVirtualizationHelperOperation.ProcessWait);
    }

    [Fact]
    public async Task Projection_failure_prevents_process_execution_with_structured_diagnostic()
    {
        RuntimeWorkflowFixture fixture = CreateRuntimeFixture();

        EnqueueReadyHostFlow(fixture.Helper);
        ResourceSnapshot<RuntimeHost, RuntimeHostSpec, RuntimeHostStatus> host = await fixture.Runtime.EnsureHostAsync(
            AppleVirtualizationContractFixtures.RuntimeHostSpec());

        ResourceMetadata<ContentProjection> projectionMetadata =
            AppleVirtualizationContractFixtures.Metadata<ContentProjection>("failed-projection", "content-projection");
        fixture.Helper.EnqueueResponse(OkResponse(AppleVirtualizationHelperOperation.ProjectionConfigure));
        fixture.Helper.EnqueueResponse(ErrorResponse(
            AppleVirtualizationHelperOperation.ProjectionMount,
            "AppleVirtualization.ProjectionGuestNotVisible",
            "The helper configured the share, but the guest did not report a verified mount."));
        ContentProjectionStatus projection = await fixture.ProjectionProvider.ProjectAsync(
            projectionMetadata,
            ProjectionSpec(host.Metadata, _workspacePath),
            host.Status.Handle,
            unit: null);

        projection.ProjectionPhase.Should().Be(ContentProjectionPhase.Failed);
        projection.Diagnostics.Should().ContainSingle(diagnostic =>
            diagnostic.Code == new DiagnosticCode("AppleVirtualization.ProjectionGuestNotVisible"));

        fixture.Helper.EnqueueResponse(UnitResponse(AppleVirtualizationHelperOperation.UnitEnsure, "projection-failure-unit", ExecutionUnitPhase.Ready, "/workspace"));
        ResourceSnapshot<ExecutionUnit, ExecutionUnitSpec, ExecutionUnitStatus> unit = await fixture.Runtime.EnsureExecutionUnitAsync(
            AppleVirtualizationContractFixtures.ExecutionUnitSpec(new ResourceRef<RuntimeHost>(
                host.Metadata.Id,
                host.Metadata.Scope,
                host.Metadata.Generation)) with
            {
                ContentProjections =
                [
                    new ResourceRef<ContentProjection>(
                        projectionMetadata.Id,
                        projectionMetadata.Scope,
                        projectionMetadata.Generation),
                ],
            });

        ProcessInvocationResult result = await fixture.Runtime.RunProcessAsync(
            AppleVirtualizationContractFixtures.ProcessInvocationSpec(unit.Status.Handle, fileName: "sh") with
            {
                Command = new ProcessCommandSpec
                {
                    FileName = "sh",
                    Arguments = ["-lc", "pwd && ls"],
                    WorkingDirectory = "/workspace",
                },
            });

        result.CompletionKind.Should().Be(ProcessCompletionKind.FailedToStart);
        result.Diagnostics.Should().ContainSingle(condition =>
            condition.Reason == "AppleVirtualization.ProcessGuestNotReady");
        fixture.Helper.Requests.Should().NotContain(request => request.Operation == AppleVirtualizationHelperOperation.ProcessStart);
    }

    [Fact]
    public async Task Guest_readiness_failure_prevents_projection_and_process_with_structured_diagnostics()
    {
        RuntimeWorkflowFixture fixture = CreateRuntimeFixture();

        EnqueueHostRunningWithoutReady(fixture.Helper);
        ResourceSnapshot<RuntimeHost, RuntimeHostSpec, RuntimeHostStatus> host = await fixture.Runtime.EnsureHostAsync(
            AppleVirtualizationContractFixtures.RuntimeHostSpec());

        host.Status.HostPhase.Should().Be(RuntimeHostPhase.Running);
        host.Status.Readiness.Should().NotBeNull();
        host.Status.Readiness!.Ready.Should().BeFalse();
        host.Status.Diagnostics.Should().Contain(diagnostic =>
            diagnostic.Code == new DiagnosticCode("AppleVirtualization.GuestAgentReadiness.Timeout"));

        ResourceMetadata<ContentProjection> projectionMetadata =
            AppleVirtualizationContractFixtures.Metadata<ContentProjection>("readiness-blocked-projection", "content-projection");
        fixture.Helper.EnqueueResponse(OkResponse(AppleVirtualizationHelperOperation.ProjectionConfigure));
        fixture.Helper.EnqueueResponse(ErrorResponse(
            AppleVirtualizationHelperOperation.ProjectionMount,
            "AppleVirtualization.ProjectionGuestNotReady",
            "Projection mount is blocked until the guest agent is ready."));
        ContentProjectionStatus projection = await fixture.ProjectionProvider.ProjectAsync(
            projectionMetadata,
            ProjectionSpec(host.Metadata, _workspacePath),
            host.Status.Handle,
            unit: null);

        projection.ProjectionPhase.Should().Be(ContentProjectionPhase.Failed);
        projection.Diagnostics.Should().ContainSingle(diagnostic =>
            diagnostic.Code == new DiagnosticCode("AppleVirtualization.ProjectionGuestNotReady"));

        fixture.Helper.EnqueueResponse(UnitResponse(AppleVirtualizationHelperOperation.UnitEnsure, "not-ready-unit", ExecutionUnitPhase.Ready, "/workspace"));
        ResourceSnapshot<ExecutionUnit, ExecutionUnitSpec, ExecutionUnitStatus> unit = await fixture.Runtime.EnsureExecutionUnitAsync(
            AppleVirtualizationContractFixtures.ExecutionUnitSpec(new ResourceRef<RuntimeHost>(
                host.Metadata.Id,
                host.Metadata.Scope,
                host.Metadata.Generation)) with
            {
                ContentProjections =
                [
                    new ResourceRef<ContentProjection>(
                        projectionMetadata.Id,
                        projectionMetadata.Scope,
                        projectionMetadata.Generation),
                ],
            });

        ProcessInvocationResult result = await fixture.Runtime.RunProcessAsync(
            AppleVirtualizationContractFixtures.ProcessInvocationSpec(unit.Status.Handle, fileName: "uname"));

        result.CompletionKind.Should().Be(ProcessCompletionKind.FailedToStart);
        result.Diagnostics.Should().ContainSingle(condition =>
            condition.Reason == "AppleVirtualization.ProcessGuestNotReady");
        fixture.Helper.Requests.Should().NotContain(request => request.Operation == AppleVirtualizationHelperOperation.ProcessStart);
    }

    private RuntimeWorkflowFixture CreateRuntimeFixture()
    {
        var helper = new FakeAppleVirtualizationHelperClient();
        var ledger = new AppleVirtualizationProviderStateLedger();
        var registry = new ExecutionProviderRegistry();
        var options = new AppleVirtualizationProviderOptions
        {
            HelperTransportMode = AppleVirtualizationHelperTransportMode.InMemoryFake,
            FeatureGates = new AppleVirtualizationProviderFeatureGates
            {
                EnableInMemoryFakeHelper = true,
            },
        };

        registry.RegisterModule(new AppleVirtualizationProviderModule(
            options,
            helper,
            ledger,
            hostPlatformOverride: new PlatformSpec("macos", "arm64")));

        return new RuntimeWorkflowFixture(
            registry,
            helper,
            new InMemoryExecutionRuntime(registry),
            registry.ContentProjectionProviders.Single());
    }

    private ContentProjectionSpec ProjectionSpec(ResourceMetadata<RuntimeHost> host, string hostPath) =>
        AppleVirtualizationContractFixtures.ReadOnlyWorkspaceProjection() with
        {
            Source = new ContentSelector
            {
                Kind = ContentSelectorKind.HostPath,
                HostPath = new HostPathSelection(new HostPath(hostPath), HostPathKind.Directory),
            },
            Target = new ContentProjectionTarget
            {
                Host = new ResourceRef<RuntimeHost>(host.Id, host.Scope, host.Generation),
                TargetName = "workspace",
            },
        };

    private static void EnqueueReadyHostFlow(FakeAppleVirtualizationHelperClient helper)
    {
        helper.EnqueueResponse(HostResponse(AppleVirtualizationHelperOperation.HostEnsure, RuntimeHostPhase.Preparing));
        helper.EnqueueResponse(HostResponse(AppleVirtualizationHelperOperation.HostStart, RuntimeHostPhase.Running));
        helper.EnqueueResponse(HostResponse(AppleVirtualizationHelperOperation.HostStatus, RuntimeHostPhase.Running, ResourcePhase.Ready));
        helper.EnqueueResponse(GuestReadinessResponse(ready: true));
    }

    private static void EnqueueHostRunningWithoutReady(FakeAppleVirtualizationHelperClient helper)
    {
        helper.EnqueueResponse(HostResponse(AppleVirtualizationHelperOperation.HostEnsure, RuntimeHostPhase.Preparing));
        helper.EnqueueResponse(HostResponse(AppleVirtualizationHelperOperation.HostStart, RuntimeHostPhase.Running));
        helper.EnqueueResponse(HostResponse(AppleVirtualizationHelperOperation.HostStatus, RuntimeHostPhase.Running, ResourcePhase.Ready));
        helper.EnqueueResponse(GuestReadinessResponse(ready: false));
    }

    private static AppleVirtualizationHelperEnvelope GuestReadinessResponse(bool ready) =>
        new()
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
                State = ready
                    ? AppleVirtualizationGuestAgentReadinessState.Ready
                    : AppleVirtualizationGuestAgentReadinessState.Timeout,
                VerifiedReady = ready,
                TransportConnected = ready,
                ProtocolVersion = ready ? AppleVirtualizationHelperProtocol.CurrentVersion : null,
                AgentVersion = ready ? "0.1.0-test" : null,
                GuestBootId = ready ? "boot-l9-runtime" : null,
                GuestBootGeneration = ready ? 1UL : 0UL,
                GuestAgentGeneration = ready ? 1UL : 0UL,
                Capabilities = ready
                    ? new AppleVirtualizationGuestAgentCapabilities
                    {
                        ProjectionMount = true,
                        ProcessStart = true,
                        ProcessReadOutput = true,
                    }
                    : null,
                Message = ready ? null : "Timed out waiting for guest-agent transport.",
            },
        };

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
                        GuestBootId: "boot-l9-runtime",
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
        string unitId,
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
                UnitId = unitId,
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

    private static AppleVirtualizationHelperEnvelope ErrorResponse(
        AppleVirtualizationHelperOperation operation,
        string code,
        string message) =>
        new()
        {
            MessageType = AppleVirtualizationHelperMessageType.Response,
            Operation = operation,
            ResponseStatus = AppleVirtualizationHelperResponseStatus.Error,
            SequenceNumber = 1,
            ProviderGeneration = 1,
            Error = new AppleVirtualizationHelperError
            {
                Code = code,
                Message = message,
                Operation = AppleVirtualizationHelperOperationNames.ToWireName(operation),
                Severity = DiagnosticSeverity.Error,
            },
        };

    private static Condition Condition(
        string type,
        ConditionStatus status,
        string reason,
        string message,
        ResourceGeneration generation) =>
        new(type, status, reason, message, new DateTimeOffset(2026, 5, 20, 12, 0, 0, TimeSpan.Zero), generation);

    private static void DeferredLanesRemainUnregistered(ExecutionProviderRegistry registry)
    {
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

    private sealed record RuntimeWorkflowFixture(
        ExecutionProviderRegistry Registry,
        FakeAppleVirtualizationHelperClient Helper,
        IExecutionRuntime Runtime,
        IContentProjectionProvider ProjectionProvider);

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
