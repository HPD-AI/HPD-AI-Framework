namespace HPD.Environment.AppleVirtualization.Tests;

using FluentAssertions;
using HPD.Environment.AppleVirtualization.ExecutionUnits;
using HPD.Environment.AppleVirtualization.Protocol;
using HPD.Environment.AppleVirtualization.State;
using HPD.Environment.AppleVirtualization.Tests.Fixtures;
using HPD.Environment.Contracts;
using Xunit;

public sealed class AppleVirtualizationRuntimeHostIdlePolicyTests
{
    [Fact]
    public async Task Host_tracks_active_execution_units()
    {
        var fixture = CreateFixture();
        fixture.Helper.EnqueueResponse(UnitResponse(ExecutionUnitPhase.Ready));
        fixture.Helper.EnqueueResponse(UnitResponse(ExecutionUnitPhase.Ready));

        ExecutionUnitStatus unit1 = await fixture.UnitProvider.EnsureAsync(Metadata("unit-1"), AppleVirtualizationContractFixtures.ExecutionUnitSpec(), null);
        ExecutionUnitStatus unit2 = await fixture.UnitProvider.EnsureAsync(Metadata("unit-2"), AppleVirtualizationContractFixtures.ExecutionUnitSpec(), null);

        RuntimeHostStatus host = fixture.Ledger.TryGetRuntimeHost(AppleVirtualizationContractFixtures.RuntimeHostRef()).Entry!.Status;
        host.ExecutionUnits.Select(unit => unit.Id.Value).Should().BeEquivalentTo(
            unit1.Handle!.Value.Route.BackingResourceId,
            unit2.Handle!.Value.Route.BackingResourceId);
    }

    [Fact]
    public async Task Stopping_and_deleting_units_updates_host_active_unit_list()
    {
        var fixture = CreateFixture();
        fixture.Helper.EnqueueResponse(UnitResponse(ExecutionUnitPhase.Ready));
        fixture.Helper.EnqueueResponse(UnitResponse(ExecutionUnitPhase.Ready));
        fixture.Helper.EnqueueResponse(UnitResponse(ExecutionUnitPhase.Stopped));
        fixture.Helper.EnqueueResponse(UnitResponse(ExecutionUnitPhase.Deleted));
        ExecutionUnitStatus unit1 = await fixture.UnitProvider.EnsureAsync(Metadata("unit-1"), AppleVirtualizationContractFixtures.ExecutionUnitSpec(), null);
        ExecutionUnitStatus unit2 = await fixture.UnitProvider.EnsureAsync(Metadata("unit-2"), AppleVirtualizationContractFixtures.ExecutionUnitSpec(), null);

        await fixture.UnitProvider.StopAsync(unit1.Handle!.Value, StopPolicy.Default);

        fixture.Ledger.TryGetRuntimeHost(AppleVirtualizationContractFixtures.RuntimeHostRef()).Entry!.Status.ExecutionUnits
            .Should().ContainSingle().Which.Id.Value.Should().Be("unit-2");

        await fixture.UnitProvider.DeleteAsync(AppleVirtualizationContractFixtures.ExecutionUnitRef("unit-2"));

        RuntimeHostStatus host = fixture.Ledger.TryGetRuntimeHost(AppleVirtualizationContractFixtures.RuntimeHostRef()).Entry!.Status;
        host.ExecutionUnits.Should().BeEmpty();
        host.Diagnostics.Should().Contain(diagnostic => diagnostic.Code.Value == "AppleVirtualization.RuntimeHostEmptyRetained");
    }

    [Fact]
    public async Task Default_policy_retains_host_when_empty()
    {
        var fixture = CreateFixture();
        fixture.Helper.EnqueueResponse(UnitResponse(ExecutionUnitPhase.Ready));
        fixture.Helper.EnqueueResponse(UnitResponse(ExecutionUnitPhase.Stopped));
        ExecutionUnitStatus unit = await fixture.UnitProvider.EnsureAsync(Metadata("unit-1"), AppleVirtualizationContractFixtures.ExecutionUnitSpec(), null);

        await fixture.UnitProvider.StopAsync(unit.Handle!.Value, StopPolicy.Default);

        RuntimeHostStatus host = fixture.Ledger.TryGetRuntimeHost(AppleVirtualizationContractFixtures.RuntimeHostRef()).Entry!.Status;
        host.HostPhase.Should().Be(RuntimeHostPhase.Ready);
        host.ExecutionUnits.Should().BeEmpty();
        host.Diagnostics.Should().Contain(diagnostic => diagnostic.Code.Value == "AppleVirtualization.RuntimeHostEmptyRetained");
        fixture.Helper.Requests.Select(request => request.Operation).Should().NotContain(AppleVirtualizationHelperOperation.HostRequestStop);
    }

    [Fact]
    public async Task StopHostWhenEmpty_stops_host_only_when_last_unit_is_gone()
    {
        var fixture = CreateFixture(RuntimeHostSpec(stopHostWhenEmpty: true, retainEmptyHost: false));
        fixture.Helper.EnqueueResponse(UnitResponse(ExecutionUnitPhase.Ready));
        fixture.Helper.EnqueueResponse(UnitResponse(ExecutionUnitPhase.Ready));
        fixture.Helper.EnqueueResponse(UnitResponse(ExecutionUnitPhase.Stopped));
        fixture.Helper.EnqueueResponse(UnitResponse(ExecutionUnitPhase.Stopped));
        fixture.Helper.EnqueueResponse(HostResponse(RuntimeHostPhase.Stopped));
        ExecutionUnitStatus unit1 = await fixture.UnitProvider.EnsureAsync(Metadata("unit-1"), AppleVirtualizationContractFixtures.ExecutionUnitSpec(), null);
        ExecutionUnitStatus unit2 = await fixture.UnitProvider.EnsureAsync(Metadata("unit-2"), AppleVirtualizationContractFixtures.ExecutionUnitSpec(), null);

        await fixture.UnitProvider.StopAsync(unit1.Handle!.Value, StopPolicy.Default);
        fixture.Helper.Requests.Select(request => request.Operation).Should().NotContain(AppleVirtualizationHelperOperation.HostRequestStop);

        await fixture.UnitProvider.StopAsync(unit2.Handle!.Value, StopPolicy.Default);

        RuntimeHostStatus host = fixture.Ledger.TryGetRuntimeHost(AppleVirtualizationContractFixtures.RuntimeHostRef()).Entry!.Status;
        host.HostPhase.Should().Be(RuntimeHostPhase.Stopped);
        host.ExecutionUnits.Should().BeEmpty();
        host.Diagnostics.Should().Contain(diagnostic => diagnostic.Code.Value == "AppleVirtualization.RuntimeHostStopWhenEmpty");
        fixture.Helper.Requests.Last().Operation.Should().Be(AppleVirtualizationHelperOperation.HostRequestStop);
        fixture.Helper.Requests.Last().HostLifecycleRequest!.Reason.Should().Be("empty-host");
    }

    [Fact]
    public async Task IdleRetention_is_represented_conservatively_without_background_stop()
    {
        var fixture = CreateFixture(RuntimeHostSpec(stopHostWhenEmpty: true, retainEmptyHost: false, idleRetention: TimeSpan.FromMinutes(5)));
        fixture.Helper.EnqueueResponse(UnitResponse(ExecutionUnitPhase.Ready));
        fixture.Helper.EnqueueResponse(UnitResponse(ExecutionUnitPhase.Stopped));
        ExecutionUnitStatus unit = await fixture.UnitProvider.EnsureAsync(Metadata("unit-1"), AppleVirtualizationContractFixtures.ExecutionUnitSpec(), null);

        await fixture.UnitProvider.StopAsync(unit.Handle!.Value, StopPolicy.Default);

        RuntimeHostStatus host = fixture.Ledger.TryGetRuntimeHost(AppleVirtualizationContractFixtures.RuntimeHostRef()).Entry!.Status;
        host.HostPhase.Should().Be(RuntimeHostPhase.Ready);
        host.ExecutionUnits.Should().BeEmpty();
        host.Diagnostics.Should().Contain(diagnostic => diagnostic.Code.Value == "AppleVirtualization.RuntimeHostIdleRetentionPending");
        fixture.Helper.Requests.Select(request => request.Operation).Should().NotContain(AppleVirtualizationHelperOperation.HostRequestStop);
    }

    private static RuntimeHostIdlePolicyFixture CreateFixture(RuntimeHostSpec? spec = null)
    {
        var ledger = new AppleVirtualizationProviderStateLedger();
        SeedHost(ledger, spec ?? RuntimeHostSpec());
        SeedProjectedProjection(ledger);
        var helper = new FakeAppleVirtualizationHelperClient();
        return new RuntimeHostIdlePolicyFixture(
            ledger,
            helper,
            new AppleVirtualizationExecutionUnitProvider(ledger, helper));
    }

    private static ResourceMetadata<ExecutionUnit> Metadata(string id) =>
        AppleVirtualizationContractFixtures.Metadata<ExecutionUnit>(id, "execution-unit");

    private static RuntimeHostSpec RuntimeHostSpec(
        bool stopHostWhenEmpty = false,
        bool retainEmptyHost = true,
        TimeSpan? idleRetention = null) =>
        AppleVirtualizationContractFixtures.RuntimeHostSpec() with
        {
            LifecyclePolicy = LifecyclePolicy.Default with
            {
                StopHostWhenEmpty = stopHostWhenEmpty,
                IdleRetention = idleRetention,
            },
            TopologyPolicy = new RuntimeTopologyPolicy
            {
                Mode = RuntimeTopologyMode.OneHostPerRuntime,
                RetainEmptyHost = retainEmptyHost,
            },
        };

    private static void SeedHost(AppleVirtualizationProviderStateLedger ledger, RuntimeHostSpec spec)
    {
        ResourceMetadata<RuntimeHost> metadata =
            AppleVirtualizationContractFixtures.Metadata<RuntimeHost>("runtime-host-1", "runtime-host");
        ledger.UpsertRuntimeHost(metadata, new RuntimeHostStatus
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
        }, spec);
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

    private static AppleVirtualizationHelperEnvelope HostResponse(RuntimeHostPhase phase) =>
        new()
        {
            MessageType = AppleVirtualizationHelperMessageType.Response,
            Operation = AppleVirtualizationHelperOperation.HostRequestStop,
            ResponseStatus = AppleVirtualizationHelperResponseStatus.Ok,
            SequenceNumber = 1,
            ProviderGeneration = 1,
            HostStatusResponse = new AppleVirtualizationHostStatusResponse
            {
                HostId = "runtime-host-1",
                HostPhase = phase,
                Phase = ResourcePhase.Ready,
            },
        };

    private sealed record RuntimeHostIdlePolicyFixture(
        AppleVirtualizationProviderStateLedger Ledger,
        FakeAppleVirtualizationHelperClient Helper,
        AppleVirtualizationExecutionUnitProvider UnitProvider);
}
