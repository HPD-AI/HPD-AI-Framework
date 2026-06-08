namespace HPD.Environment.AppleVirtualization.Tests;

using FluentAssertions;
using HPD.Environment.AppleVirtualization.Handles;
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
