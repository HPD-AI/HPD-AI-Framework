namespace HPD.Environment.AppleVirtualization.Tests;

using FluentAssertions;
using HPD.Environment.AppleVirtualization.Handles;
using HPD.Environment.AppleVirtualization.GuestAgent;
using HPD.Environment.AppleVirtualization.State;
using HPD.Environment.Contracts;
using Xunit;

public sealed class AppleVirtualizationProviderStateLedgerTests
{
    [Fact]
    public void Host_handle_creation_and_lookup_preserves_resource_identity()
    {
        var ledger = new AppleVirtualizationProviderStateLedger();
        ResourceMetadata<RuntimeHost> metadata = Metadata<RuntimeHost>("runtime-host", "host-1");

        var entry = ledger.UpsertRuntimeHost(metadata, new RuntimeHostStatus
        {
            Phase = ResourcePhase.Pending,
            ObservedGeneration = metadata.Generation,
            HostPhase = RuntimeHostPhase.Declared,
        });

        entry.Resource.Id.Should().Be(metadata.Id);
        entry.TargetHandle.Route.ProviderId.Should().Be(AppleVirtualizationProviderDescriptor.ProviderId);
        entry.TargetHandle.Route.ProviderHandle.Should().Be(entry.ProviderHandle);
        entry.TargetHandle.ProviderGeneration.Should().Be(ledger.ProviderGeneration);
        entry.Status.Handle.Should().Be(entry.TargetHandle);
        entry.Status.ProviderHandle.Should().Be(entry.ProviderHandle);

        ledger.TryGetRuntimeHost(entry.Resource).Succeeded.Should().BeTrue();
        AppleVirtualizationLedgerLookup<AppleVirtualizationLedgerEntry<RuntimeHost, RuntimeHostStatus>> lookup = ledger.TryGetRuntimeHost(entry.TargetHandle);
        lookup.Succeeded.Should().BeTrue();
        lookup.Entry!.Resource.Should().Be(entry.Resource);
    }

    [Fact]
    public void Execution_unit_handle_creation_and_lookup_preserves_namespace_handle()
    {
        var ledger = new AppleVirtualizationProviderStateLedger();
        ResourceMetadata<ExecutionUnit> metadata = Metadata<ExecutionUnit>("execution-unit", "unit-1");

        var entry = ledger.UpsertExecutionUnit(metadata, new ExecutionUnitStatus
        {
            Phase = ResourcePhase.Pending,
            ObservedGeneration = metadata.Generation,
            UnitPhase = ExecutionUnitPhase.Declared,
        });

        entry.TargetHandle.Route.Segments.Should().ContainSingle(segment =>
            segment.Kind == TargetRouteSegmentKind.ExecutionUnit &&
            segment.Value == metadata.Id.Value);
        entry.Status.Handle.Should().Be(entry.TargetHandle);
        entry.Status.NamespaceHandle.Should().Be(entry.ProviderHandle);

        ledger.TryGetExecutionUnit(entry.Resource).Succeeded.Should().BeTrue();
        ledger.TryGetExecutionUnit(entry.TargetHandle).Entry!.Resource.Should().Be(entry.Resource);
    }

    [Fact]
    public void Projection_handle_creation_and_lookup_keeps_target_handle_outside_status_snapshot()
    {
        var ledger = new AppleVirtualizationProviderStateLedger();
        ResourceMetadata<ContentProjection> metadata = Metadata<ContentProjection>("content-projection", "projection-1");

        var entry = ledger.UpsertContentProjection(metadata, new ContentProjectionStatus
        {
            Phase = ResourcePhase.Pending,
            ObservedGeneration = metadata.Generation,
            ProjectionPhase = ContentProjectionPhase.Projecting,
        });

        entry.TargetHandle.Route.Segments.Should().ContainSingle(segment =>
            segment.Kind == TargetRouteSegmentKind.ContentProjection &&
            segment.Value == metadata.Id.Value);
        entry.Status.ProviderHandle.Should().Be(entry.ProviderHandle);

        ledger.TryGetContentProjection(entry.Resource).Succeeded.Should().BeTrue();
        ledger.TryGetContentProjection(entry.TargetHandle).Entry!.ProviderHandle.Should().Be(entry.ProviderHandle);
    }

    [Fact]
    public void Process_handle_creation_and_lookup_uses_live_capability_lifetime()
    {
        var ledger = new AppleVirtualizationProviderStateLedger();
        ResourceMetadata<ProcessInvocation> metadata = Metadata<ProcessInvocation>("process-invocation", "process-1");

        var entry = ledger.UpsertProcessInvocation(metadata, new ProcessInvocationStatus
        {
            Phase = ResourcePhase.Pending,
            ObservedGeneration = metadata.Generation,
            ProcessPhase = ProcessInvocationPhase.Created,
        });

        entry.TargetHandle.Lifetime.Should().Be(TargetHandleLifetime.LiveCapability);
        entry.TargetHandle.Route.Segments.Should().ContainSingle(segment =>
            segment.Kind == TargetRouteSegmentKind.ProcessInvocation &&
            segment.Value == metadata.Id.Value);
        entry.Status.Handle.Should().Be(entry.TargetHandle);

        ledger.TryGetProcessInvocation(entry.Resource).Succeeded.Should().BeTrue();
        ledger.TryGetProcessInvocation(entry.TargetHandle).Entry!.Resource.Should().Be(entry.Resource);
    }

    [Fact]
    public void Stale_generation_rejection_returns_structured_diagnostic()
    {
        var ledger = new AppleVirtualizationProviderStateLedger();
        ResourceMetadata<RuntimeHost> metadata = Metadata<RuntimeHost>("runtime-host", "host-1");
        var entry = ledger.UpsertRuntimeHost(metadata, new RuntimeHostStatus
        {
            Phase = ResourcePhase.Ready,
            ObservedGeneration = metadata.Generation,
            HostPhase = RuntimeHostPhase.Running,
        });

        ledger.AdvanceProviderGeneration();

        AppleVirtualizationLedgerLookup<AppleVirtualizationLedgerEntry<RuntimeHost, RuntimeHostStatus>> lookup = ledger.TryGetRuntimeHost(entry.TargetHandle);

        lookup.Succeeded.Should().BeFalse();
        lookup.Entry.Should().BeNull();
        lookup.Diagnostic.Should().NotBeNull();
        lookup.Diagnostic!.Code.Should().Be(AppleVirtualizationHandleDiagnostics.StaleHandle);
        lookup.Diagnostic.ProviderId.Should().Be(AppleVirtualizationProviderDescriptor.ProviderId);
    }

    [Fact]
    public void Wrong_kind_handle_rejection_returns_structured_diagnostic()
    {
        var ledger = new AppleVirtualizationProviderStateLedger();
        ResourceMetadata<ProcessInvocation> metadata = Metadata<ProcessInvocation>("process-invocation", "process-1");
        var process = ledger.UpsertProcessInvocation(metadata, new ProcessInvocationStatus
        {
            Phase = ResourcePhase.Ready,
            ObservedGeneration = metadata.Generation,
            ProcessPhase = ProcessInvocationPhase.Running,
        });

        AppleVirtualizationLedgerLookup<AppleVirtualizationLedgerEntry<RuntimeHost, RuntimeHostStatus>> lookup =
            ledger.TryGetRuntimeHost(process.TargetHandle.Route, process.TargetHandle.ProviderGeneration);

        lookup.Succeeded.Should().BeFalse();
        lookup.Entry.Should().BeNull();
        lookup.Diagnostic.Should().NotBeNull();
        lookup.Diagnostic!.Code.Should().Be(AppleVirtualizationHandleDiagnostics.WrongHandleKind);
    }

    [Fact]
    public void Engine_generation_ledger_rejects_stale_guest_agent_and_engine_generations()
    {
        var ledger = new AppleVirtualizationProviderStateLedger();
        ResourceMetadata<RuntimeHost> metadata = Metadata<RuntimeHost>("runtime-host", "host-1");
        var accepted = new AppleVirtualizationGuestAgentEngineGenerationStamp(
            ProviderGeneration: 1,
            HostStartGeneration: 4,
            GuestBootId: "boot-a",
            GuestBootGeneration: 8,
            GuestAgentGeneration: 6,
            EngineGeneration: 9);

        ledger.TryAcceptRuntimeHostEngineGeneration(
            metadata.Id,
            metadata.Scope,
            "docker",
            accepted,
            1,
            4,
            "boot-a",
            8,
            requireEngineGeneration: true,
            out _).Should().BeTrue();

        ledger.TryAcceptRuntimeHostEngineGeneration(
            metadata.Id,
            metadata.Scope,
            "docker",
            accepted with { GuestAgentGeneration = 5 },
            1,
            4,
            "boot-a",
            8,
            requireEngineGeneration: true,
            out string staleAgentReason).Should().BeFalse();
        staleAgentReason.Should().Contain("guest-agent generation is stale");

        ledger.TryAcceptRuntimeHostEngineGeneration(
            metadata.Id,
            metadata.Scope,
            "docker",
            accepted with { EngineGeneration = 8 },
            1,
            4,
            "boot-a",
            8,
            requireEngineGeneration: true,
            out string staleEngineReason).Should().BeFalse();
        staleEngineReason.Should().Contain("engine generation is stale");
    }

    [Fact]
    public void Engine_generation_ledger_rejects_wrong_boot_and_zero_ready_engine_generation()
    {
        var ledger = new AppleVirtualizationProviderStateLedger();
        ResourceMetadata<RuntimeHost> metadata = Metadata<RuntimeHost>("runtime-host", "host-1");
        var generation = new AppleVirtualizationGuestAgentEngineGenerationStamp(
            ProviderGeneration: 1,
            HostStartGeneration: 4,
            GuestBootId: "boot-old",
            GuestBootGeneration: 7,
            GuestAgentGeneration: 2,
            EngineGeneration: 1);

        ledger.TryAcceptRuntimeHostEngineGeneration(
            metadata.Id,
            metadata.Scope,
            "docker",
            generation,
            1,
            4,
            "boot-current",
            8,
            requireEngineGeneration: true,
            out _).Should().BeFalse();

        ledger.TryAcceptRuntimeHostEngineGeneration(
            metadata.Id,
            metadata.Scope,
            "docker",
            generation with
            {
                GuestBootId = "boot-current",
                GuestBootGeneration = 8,
                EngineGeneration = 0,
            },
            1,
            4,
            "boot-current",
            8,
            requireEngineGeneration: true,
            out string zeroEngineReason).Should().BeFalse();
        zeroEngineReason.Should().Contain("positive engine generation");
    }

    [Fact]
    public void Rejected_provider_generation_does_not_poison_last_accepted_engine_tuple()
    {
        var ledger = new AppleVirtualizationProviderStateLedger();
        ResourceMetadata<RuntimeHost> metadata = Metadata<RuntimeHost>("runtime-host", "host-1");
        var invalid = new AppleVirtualizationGuestAgentEngineGenerationStamp(
            ProviderGeneration: 99,
            HostStartGeneration: 4,
            GuestBootId: "boot-a",
            GuestBootGeneration: 8,
            GuestAgentGeneration: 99,
            EngineGeneration: 99);

        ledger.TryAcceptRuntimeHostEngineGeneration(
            metadata.Id,
            metadata.Scope,
            "docker",
            invalid,
            expectedProviderGeneration: 1,
            expectedHostStartGeneration: 4,
            expectedGuestBootId: "boot-a",
            expectedGuestBootGeneration: 8,
            requireEngineGeneration: true,
            out string rejectedReason).Should().BeFalse();
        rejectedReason.Should().Contain("provider generation");

        ledger.TryAcceptRuntimeHostEngineGeneration(
            metadata.Id,
            metadata.Scope,
            "docker",
            invalid with
            {
                ProviderGeneration = 1,
                GuestAgentGeneration = 2,
                EngineGeneration = 2,
            },
            expectedProviderGeneration: 1,
            expectedHostStartGeneration: 4,
            expectedGuestBootId: "boot-a",
            expectedGuestBootGeneration: 8,
            requireEngineGeneration: true,
            out _).Should().BeTrue();
    }

    [Fact]
    public void Engine_generation_sequences_are_independent_per_engine_on_the_same_host()
    {
        var ledger = new AppleVirtualizationProviderStateLedger();
        ResourceMetadata<RuntimeHost> metadata = Metadata<RuntimeHost>("runtime-host", "host-1");
        var generation = new AppleVirtualizationGuestAgentEngineGenerationStamp(
            ProviderGeneration: 1,
            HostStartGeneration: 4,
            GuestBootId: "boot-a",
            GuestBootGeneration: 8,
            GuestAgentGeneration: 2,
            EngineGeneration: 9);

        ledger.TryAcceptRuntimeHostEngineGeneration(
            metadata.Id, metadata.Scope, "docker", generation,
            1, 4, "boot-a", 8, true, out _).Should().BeTrue();
        ledger.TryAcceptRuntimeHostEngineGeneration(
            metadata.Id, metadata.Scope, "containerd",
            generation with { EngineGeneration = 1 },
            1, 4, "boot-a", 8, true, out _).Should().BeTrue();
        ledger.TryAcceptRuntimeHostEngineGeneration(
            metadata.Id, metadata.Scope, "docker",
            generation with { EngineGeneration = 8 },
            1, 4, "boot-a", 8, true, out string staleReason).Should().BeFalse();
        staleReason.Should().Contain("engine generation is stale");
    }

    [Fact]
    public void Removing_and_recreating_host_clears_fingerprint_and_engine_generations()
    {
        var ledger = new AppleVirtualizationProviderStateLedger();
        ResourceMetadata<RuntimeHost> metadata = Metadata<RuntimeHost>("runtime-host", "host-1");
        var host = ledger.UpsertRuntimeHost(metadata, new RuntimeHostStatus
        {
            Phase = ResourcePhase.Ready,
            ObservedGeneration = metadata.Generation,
            HostPhase = RuntimeHostPhase.Running,
        });
        ledger.SetRuntimeHostConfigurationFingerprint(metadata.Id, metadata.Scope, "old-fingerprint");
        var oldGeneration = new AppleVirtualizationGuestAgentEngineGenerationStamp(
            ProviderGeneration: 1,
            HostStartGeneration: 4,
            GuestBootId: "boot-a",
            GuestBootGeneration: 8,
            GuestAgentGeneration: 9,
            EngineGeneration: 9);
        ledger.TryAcceptRuntimeHostEngineGeneration(
            metadata.Id, metadata.Scope, "docker", oldGeneration,
            1, 4, null, null, true, out _).Should().BeTrue();

        ledger.RemoveRuntimeHost(host.Resource).Should().BeTrue();
        ledger.GetRuntimeHostConfigurationFingerprint(metadata.Id, metadata.Scope).Should().BeNull();

        ledger.UpsertRuntimeHost(metadata, new RuntimeHostStatus
        {
            Phase = ResourcePhase.Ready,
            ObservedGeneration = metadata.Generation,
            HostPhase = RuntimeHostPhase.Running,
        });
        ledger.TryAcceptRuntimeHostEngineGeneration(
            metadata.Id,
            metadata.Scope,
            "docker",
            oldGeneration with { GuestAgentGeneration = 1, EngineGeneration = 1 },
            1, 4, null, null, true, out _).Should().BeTrue();
    }

    private static ResourceMetadata<TResource> Metadata<TResource>(string kind, string id)
        where TResource : IExecutionResourceMarker =>
        new()
        {
            Id = new ResourceId<TResource>(id),
            Kind = new ResourceKind(kind),
            Scope = new ResourceScope("test-runtime"),
            Generation = new ResourceGeneration(1),
            SchemaVersion = new SchemaVersion("v1"),
            CreatedAt = DateTimeOffset.UtcNow,
        };
}
