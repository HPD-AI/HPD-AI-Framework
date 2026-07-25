namespace HPD.Environment.AppleVirtualization.Tests;

using FluentAssertions;
using HPD.Environment.AppleVirtualization.ExecutionUnits;
using HPD.Environment.AppleVirtualization.Handles;
using HPD.Environment.AppleVirtualization.Hosts;
using HPD.Environment.AppleVirtualization.Processes;
using HPD.Environment.AppleVirtualization.Protocol;
using HPD.Environment.AppleVirtualization.State;
using HPD.Environment.AppleVirtualization.Tests.Fixtures;
using HPD.Environment.Contracts;
using Xunit;

public sealed class AppleVirtualizationExecutionUnitConcurrencyTests
{
    private static readonly PlatformSpec SupportedHost = new("macos", "arm64");

    [Fact]
    public async Task Two_units_can_be_ready_on_same_host()
    {
        var fixture = CreateFixture();
        fixture.Helper.EnqueueResponse(UnitResponse(ExecutionUnitPhase.Ready));
        fixture.Helper.EnqueueResponse(UnitResponse(ExecutionUnitPhase.Ready));

        ExecutionUnitStatus unit1 = await fixture.UnitProvider.EnsureAsync(
            Metadata("unit-1"),
            AppleVirtualizationContractFixtures.ExecutionUnitSpec(),
            observed: null);
        ExecutionUnitStatus unit2 = await fixture.UnitProvider.EnsureAsync(
            Metadata("unit-2"),
            AppleVirtualizationContractFixtures.ExecutionUnitSpec(),
            observed: null);

        unit1.UnitPhase.Should().Be(ExecutionUnitPhase.Ready);
        unit2.UnitPhase.Should().Be(ExecutionUnitPhase.Ready);
        unit1.AssignedHost.Should().Be(unit2.AssignedHost);
        unit1.Handle.Should().NotBe(unit2.Handle);
        fixture.Ledger.TryGetExecutionUnit(unit1.Handle!.Value).Succeeded.Should().BeTrue();
        fixture.Ledger.TryGetExecutionUnit(unit2.Handle!.Value).Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task Concurrent_processes_in_same_unit_are_tracked_independently()
    {
        var fixture = CreateFixture();
        fixture.Helper.EnqueueResponse(UnitResponse(ExecutionUnitPhase.Ready));
        fixture.Helper.EnqueueResponse(ProcessStatus("process-1", ProcessInvocationPhase.Running));
        fixture.Helper.EnqueueResponse(ProcessStatus("process-2", ProcessInvocationPhase.Running));
        ExecutionUnitStatus unit = await fixture.UnitProvider.EnsureAsync(
            Metadata("unit-1"),
            AppleVirtualizationContractFixtures.ExecutionUnitSpec(),
            observed: null);

        IProcessInvocationHandle process1 = await fixture.ProcessProvider.StartAsync(
            AppleVirtualizationContractFixtures.ProcessInvocationSpec(unit.Handle!.Value, "sleep"));
        IProcessInvocationHandle process2 = await fixture.ProcessProvider.StartAsync(
            AppleVirtualizationContractFixtures.ProcessInvocationSpec(unit.Handle.Value, "uname"));

        ExecutionUnitStatus tracked = fixture.Ledger.TryGetExecutionUnit(unit.Handle.Value).Entry!.Status;
        tracked.UnitPhase.Should().Be(ExecutionUnitPhase.Running);
        tracked.ActiveProcesses.Select(process => process.Id.Value).Should().BeEquivalentTo(
            process1.Resource!.Value.Id.Value,
            process2.Resource!.Value.Id.Value);
    }

    [Fact]
    public async Task Concurrent_processes_across_units_are_tracked_independently()
    {
        var fixture = CreateFixture();
        fixture.Helper.EnqueueResponse(UnitResponse(ExecutionUnitPhase.Ready));
        fixture.Helper.EnqueueResponse(UnitResponse(ExecutionUnitPhase.Ready));
        fixture.Helper.EnqueueResponse(ProcessStatus("process-1", ProcessInvocationPhase.Running));
        fixture.Helper.EnqueueResponse(ProcessStatus("process-2", ProcessInvocationPhase.Running));
        ExecutionUnitStatus unit1 = await fixture.UnitProvider.EnsureAsync(Metadata("unit-1"), AppleVirtualizationContractFixtures.ExecutionUnitSpec(), null);
        ExecutionUnitStatus unit2 = await fixture.UnitProvider.EnsureAsync(Metadata("unit-2"), AppleVirtualizationContractFixtures.ExecutionUnitSpec(), null);

        IProcessInvocationHandle process1 = await fixture.ProcessProvider.StartAsync(
            AppleVirtualizationContractFixtures.ProcessInvocationSpec(unit1.Handle!.Value, "sleep"));
        IProcessInvocationHandle process2 = await fixture.ProcessProvider.StartAsync(
            AppleVirtualizationContractFixtures.ProcessInvocationSpec(unit2.Handle!.Value, "uname"));

        fixture.Ledger.TryGetExecutionUnit(unit1.Handle.Value).Entry!.Status.ActiveProcesses
            .Should().ContainSingle().Which.Id.Value.Should().Be(process1.Resource!.Value.Id.Value);
        fixture.Ledger.TryGetExecutionUnit(unit2.Handle.Value).Entry!.Status.ActiveProcesses
            .Should().ContainSingle().Which.Id.Value.Should().Be(process2.Resource!.Value.Id.Value);
    }

    [Fact]
    public async Task Stopping_one_unit_does_not_stop_another_units_processes()
    {
        var fixture = CreateFixture();
        fixture.Helper.EnqueueResponse(UnitResponse(ExecutionUnitPhase.Ready));
        fixture.Helper.EnqueueResponse(UnitResponse(ExecutionUnitPhase.Ready));
        fixture.Helper.EnqueueResponse(ProcessStatus("process-1", ProcessInvocationPhase.Running));
        fixture.Helper.EnqueueResponse(ProcessStatus("process-2", ProcessInvocationPhase.Running));
        fixture.Helper.EnqueueResponse(ProcessStatus("process-1", ProcessInvocationPhase.Stopped));
        fixture.Helper.EnqueueResponse(UnitResponse(ExecutionUnitPhase.Stopped));
        ExecutionUnitStatus unit1 = await fixture.UnitProvider.EnsureAsync(Metadata("unit-1"), AppleVirtualizationContractFixtures.ExecutionUnitSpec(), null);
        ExecutionUnitStatus unit2 = await fixture.UnitProvider.EnsureAsync(Metadata("unit-2"), AppleVirtualizationContractFixtures.ExecutionUnitSpec(), null);
        IProcessInvocationHandle process1 = await fixture.ProcessProvider.StartAsync(
            AppleVirtualizationContractFixtures.ProcessInvocationSpec(unit1.Handle!.Value, "sleep"));
        IProcessInvocationHandle process2 = await fixture.ProcessProvider.StartAsync(
            AppleVirtualizationContractFixtures.ProcessInvocationSpec(unit2.Handle!.Value, "sleep"));

        ExecutionUnitStatus stopped = await fixture.UnitProvider.StopAsync(unit1.Handle.Value, StopPolicy.Default);

        stopped.ActiveProcesses.Should().BeEmpty();
        fixture.Ledger.TryGetProcessInvocation(process1.Resource!.Value).Entry!.Status.ProcessPhase.Should().Be(ProcessInvocationPhase.Stopped);
        fixture.Ledger.TryGetProcessInvocation(process2.Resource!.Value).Entry!.Status.ProcessPhase.Should().Be(ProcessInvocationPhase.Running);
        fixture.Ledger.TryGetExecutionUnit(unit2.Handle.Value).Entry!.Status.ActiveProcesses
            .Should().ContainSingle().Which.Id.Value.Should().Be(process2.Resource.Value.Id.Value);
        fixture.Helper.Requests
            .Where(request => request.Operation == AppleVirtualizationHelperOperation.ProcessStop)
            .Should().ContainSingle().Which.ProcessStopRequest!.ProcessId.Should().Be(process1.Resource.Value.Id.Value);
    }

    [Fact]
    public async Task Host_stop_invalidates_all_units_and_active_processes_on_that_host()
    {
        var fixture = CreateFixture();
        fixture.Helper.EnqueueResponse(UnitResponse(ExecutionUnitPhase.Ready));
        fixture.Helper.EnqueueResponse(UnitResponse(ExecutionUnitPhase.Ready));
        fixture.Helper.EnqueueResponse(ProcessStatus("process-1", ProcessInvocationPhase.Running));
        fixture.Helper.EnqueueResponse(ProcessStatus("process-2", ProcessInvocationPhase.Running));
        fixture.Helper.EnqueueResponse(HostResponse(RuntimeHostPhase.Stopped));
        ExecutionUnitStatus unit1 = await fixture.UnitProvider.EnsureAsync(Metadata("unit-1"), AppleVirtualizationContractFixtures.ExecutionUnitSpec(), null);
        ExecutionUnitStatus unit2 = await fixture.UnitProvider.EnsureAsync(Metadata("unit-2"), AppleVirtualizationContractFixtures.ExecutionUnitSpec(), null);
        IProcessInvocationHandle process1 = await fixture.ProcessProvider.StartAsync(AppleVirtualizationContractFixtures.ProcessInvocationSpec(unit1.Handle!.Value, "sleep"));
        IProcessInvocationHandle process2 = await fixture.ProcessProvider.StartAsync(AppleVirtualizationContractFixtures.ProcessInvocationSpec(unit2.Handle!.Value, "sleep"));

        RuntimeHostStatus stopped = await fixture.HostProvider.StopAsync(fixture.Host.Handle!.Value, StopPolicy.Default with { Kind = StopKind.Graceful });

        stopped.HostPhase.Should().Be(RuntimeHostPhase.Stopped);
        stopped.ExecutionUnits.Should().BeEmpty();
        ExecutionUnitStatus stoppedUnit1 = fixture.Ledger.TryGetExecutionUnit(unit1.Handle.Value).Entry!.Status;
        ExecutionUnitStatus stoppedUnit2 = fixture.Ledger.TryGetExecutionUnit(unit2.Handle.Value).Entry!.Status;
        stoppedUnit1.UnitPhase.Should().Be(ExecutionUnitPhase.Stopped);
        stoppedUnit2.UnitPhase.Should().Be(ExecutionUnitPhase.Stopped);
        stoppedUnit1.ActiveProcesses.Should().BeEmpty();
        stoppedUnit2.ActiveProcesses.Should().BeEmpty();
        stoppedUnit1.Diagnostics.Should().Contain(diagnostic => diagnostic.Code.Value == "AppleVirtualization.ExecutionUnitHostInvalidated");
        fixture.Ledger.TryGetProcessInvocation(process1.Resource!.Value).Entry!.Status.ProcessPhase.Should().Be(ProcessInvocationPhase.Stopped);
        fixture.Ledger.TryGetProcessInvocation(process2.Resource!.Value).Entry!.Status.ProcessPhase.Should().Be(ProcessInvocationPhase.Stopped);
    }

    [Fact]
    public async Task Host_delete_marks_affected_units_deleted_without_removing_unrelated_units()
    {
        var fixture = CreateFixture();
        fixture.Helper.EnqueueResponse(UnitResponse(ExecutionUnitPhase.Ready));
        fixture.Helper.EnqueueResponse(UnitResponse(ExecutionUnitPhase.Ready));
        fixture.Helper.EnqueueResponse(HostResponse(
            RuntimeHostPhase.Deleted,
            AppleVirtualizationHelperOperation.HostDelete));
        ExecutionUnitStatus unit1 = await fixture.UnitProvider.EnsureAsync(Metadata("unit-1"), AppleVirtualizationContractFixtures.ExecutionUnitSpec(), null);
        ExecutionUnitStatus unit2 = await fixture.UnitProvider.EnsureAsync(Metadata("unit-2"), AppleVirtualizationContractFixtures.ExecutionUnitSpec(), null);

        await fixture.HostProvider.DeleteAsync(AppleVirtualizationContractFixtures.RuntimeHostRef());

        fixture.Ledger.TryGetRuntimeHost(AppleVirtualizationContractFixtures.RuntimeHostRef()).Succeeded.Should().BeFalse();
        fixture.Ledger.TryGetExecutionUnit(unit1.Handle!.Value).Entry!.Status.UnitPhase.Should().Be(ExecutionUnitPhase.Deleted);
        fixture.Ledger.TryGetExecutionUnit(unit2.Handle!.Value).Entry!.Status.UnitPhase.Should().Be(ExecutionUnitPhase.Deleted);
    }

    [Fact]
    public async Task Stale_unit_and_process_handles_fail_deterministically_after_provider_generation_change()
    {
        var fixture = CreateFixture();
        fixture.Helper.EnqueueResponse(UnitResponse(ExecutionUnitPhase.Ready));
        fixture.Helper.EnqueueResponse(ProcessStatus("process-1", ProcessInvocationPhase.Running));
        ExecutionUnitStatus unit = await fixture.UnitProvider.EnsureAsync(Metadata("unit-1"), AppleVirtualizationContractFixtures.ExecutionUnitSpec(), null);
        IProcessInvocationHandle process = await fixture.ProcessProvider.StartAsync(
            AppleVirtualizationContractFixtures.ProcessInvocationSpec(unit.Handle!.Value, "sleep"));

        fixture.Ledger.AdvanceProviderGeneration();

        ExecutionUnitStatus unitStatus = await fixture.UnitProvider.GetStatusAsync(unit.Handle.Value);
        Func<Task> wait = async () => await process.WaitAsync();
        unitStatus.Diagnostics.Should().ContainSingle().Which.Code.Should().Be(AppleVirtualizationHandleDiagnostics.StaleHandle);
        await wait.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("AppleVirtualization.StaleHandle:*");
    }

    private static ExecutionUnitConcurrencyFixture CreateFixture()
    {
        var ledger = new AppleVirtualizationProviderStateLedger();
        RuntimeHostStatus host = SeedHost(ledger);
        SeedProjectedProjection(ledger);
        var helper = new FakeAppleVirtualizationHelperClient();
        return new ExecutionUnitConcurrencyFixture(
            ledger,
            helper,
            new AppleVirtualizationRuntimeHostProvider(helper, ledger, SupportedHost),
            new AppleVirtualizationExecutionUnitProvider(ledger, helper),
            new AppleVirtualizationProcessProvider(ledger, helper),
            host);
    }

    private static ResourceMetadata<ExecutionUnit> Metadata(string id) =>
        AppleVirtualizationContractFixtures.Metadata<ExecutionUnit>(id, "execution-unit");

    private static RuntimeHostStatus SeedHost(AppleVirtualizationProviderStateLedger ledger)
    {
        ResourceMetadata<RuntimeHost> metadata =
            AppleVirtualizationContractFixtures.Metadata<RuntimeHost>("runtime-host-1", "runtime-host");
        return ledger.UpsertRuntimeHost(metadata, new RuntimeHostStatus
        {
            Phase = ResourcePhase.Ready,
            ObservedGeneration = metadata.Generation,
            HostPhase = RuntimeHostPhase.Ready,
            GuestControl = new GuestControlStatus(
                Expected: true,
                Installed: true,
                Reachable: true,
                Transport: ProviderTransportKind.Vsock),
            Readiness = new RuntimeHostReadinessStatus(Ready: true),
        }).Status;
    }

    private static void SeedProjectedProjection(AppleVirtualizationProviderStateLedger ledger)
    {
        ResourceMetadata<ContentProjection> metadata =
            AppleVirtualizationContractFixtures.Metadata<ContentProjection>("projection-1", "content-projection");
        ledger.UpsertContentProjection(metadata, new ContentProjectionStatus
        {
            Phase = ResourcePhase.Ready,
            ObservedGeneration = metadata.Generation,
            ProjectionPhase = ContentProjectionPhase.Projected,
            Views =
            [
                new RealizedProjectionView
                {
                    Kind = ProjectionViewKind.FilesystemTree,
                    GuestPath = new GuestPath("/workspace"),
                    EffectiveAccess = AccessMode.ReadOnly,
                    EffectiveRealization = ProjectionRealizationKind.LiveProjection,
                    EffectiveWriteEffect = ProjectionWriteEffect.NoWrites,
                    EffectiveCoherence = CoherenceClass.CloseToOpen,
                },
            ],
        });
    }

    private static AppleVirtualizationHelperEnvelope UnitResponse(ExecutionUnitPhase phase) =>
        new()
        {
            MessageType = AppleVirtualizationHelperMessageType.Response,
            Operation = AppleVirtualizationHelperOperation.UnitEnsure,
            RequestId = "unit-response",
            ResponseStatus = AppleVirtualizationHelperResponseStatus.Ok,
            SequenceNumber = 1,
            ProviderGeneration = 1,
            UnitStatusResponse = new AppleVirtualizationUnitStatusResponse
            {
                UnitId = "unit-response",
                UnitPhase = phase,
                WorkingDirectory = "/workspace",
            },
        };

    private static AppleVirtualizationHelperEnvelope ProcessStatus(string processId, ProcessInvocationPhase phase) =>
        new()
        {
            MessageType = AppleVirtualizationHelperMessageType.Response,
            Operation = AppleVirtualizationHelperOperation.ProcessStart,
            ResponseStatus = AppleVirtualizationHelperResponseStatus.Ok,
            SequenceNumber = 1,
            ProviderGeneration = 1,
            ProcessStatusResponse = new AppleVirtualizationProcessStatusResponse
            {
                ProcessId = processId,
                ProcessPhase = phase,
                IoState = phase == ProcessInvocationPhase.Stopped ? ProcessIoState.Closed : ProcessIoState.Open,
                ProviderProcessId = "guest-" + processId,
                Result = phase == ProcessInvocationPhase.Stopped
                    ? new ProcessInvocationResult
                    {
                        ProcessId = new ResourceId<ProcessInvocation>(processId),
                        CompletionKind = ProcessCompletionKind.Stopped,
                        ExitedAt = DateTimeOffset.UtcNow,
                        Output = new ProcessCapturedOutput
                        {
                            Stdout = new ProcessStreamOutput(),
                            Stderr = new ProcessStreamOutput(),
                        },
                    }
                    : null,
            },
        };

    private static AppleVirtualizationHelperEnvelope HostResponse(
        RuntimeHostPhase phase,
        AppleVirtualizationHelperOperation operation = AppleVirtualizationHelperOperation.HostRequestStop) =>
        new()
        {
            MessageType = AppleVirtualizationHelperMessageType.Response,
            Operation = operation,
            ResponseStatus = AppleVirtualizationHelperResponseStatus.Ok,
            SequenceNumber = 1,
            ProviderGeneration = 1,
            HostStatusResponse = new AppleVirtualizationHostStatusResponse
            {
                HostId = "runtime-host-1",
                HostPhase = phase,
                Phase = phase == RuntimeHostPhase.Deleted ? ResourcePhase.Deleted : ResourcePhase.Ready,
            },
        };

    private sealed record ExecutionUnitConcurrencyFixture(
        AppleVirtualizationProviderStateLedger Ledger,
        FakeAppleVirtualizationHelperClient Helper,
        AppleVirtualizationRuntimeHostProvider HostProvider,
        AppleVirtualizationExecutionUnitProvider UnitProvider,
        AppleVirtualizationProcessProvider ProcessProvider,
        RuntimeHostStatus Host);
}
