using System.Collections.Immutable;
using HPD.Agent.Tests.TestToolHarnesses;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Tests.Capabilities;

public sealed class AgentCapabilityCatalogTests
{
    [Fact]
    public async Task PublicationPinsOldRevisionUntilFinalLeaseReleases()
    {
        var first = Owner("native", 1);
        var second = Owner("native", 2);
        await using var catalog = new AgentCapabilityCatalog(1, [first]);
        var oldLease = catalog.Acquire();

        await catalog.PublishAsync(2, [second]);

        Assert.Equal(1, oldLease.Snapshot.Epoch);
        Assert.Equal(0, first.DisposeCount);
        await oldLease.DisposeAsync();
        Assert.Equal(1, first.DisposeCount);

        await using var current = catalog.Acquire();
        Assert.Equal(2, current.Snapshot.Epoch);
    }

    [Fact]
    public async Task ReusedOwnerIsDisposedOnlyAfterEveryPublishedSnapshotReleases()
    {
        var owner = Owner("native", 1);
        var catalog = new AgentCapabilityCatalog(1, [owner]);
        var oldLease = catalog.Acquire();

        await catalog.PublishAsync(2, [owner]);
        await catalog.DisposeAsync();
        Assert.Equal(0, owner.DisposeCount);

        await oldLease.DisposeAsync();
        Assert.Equal(1, owner.DisposeCount);
    }

    [Fact]
    public async Task ForceDisposePolicyReleasesOwnerPinnedByLeakedLeaseExactlyOnce()
    {
        var owner = Owner("native", 1);
        var catalog = new AgentCapabilityCatalog(1, [owner]);
        var leakedLease = catalog.Acquire();

        var leakCount = await catalog.ShutdownAsync(AgentLeaseLeakPolicy.ReportAndForceDispose);

        Assert.Equal(1, leakCount);
        Assert.Equal(1, owner.DisposeCount);
        await leakedLease.DisposeAsync();
        Assert.Equal(1, owner.DisposeCount);
    }

    [Fact]
    public async Task RejectedCandidateDoesNotRetirePublishedRevision()
    {
        var current = Owner("native", 1);
        var duplicate = Owner("native", 2);
        await using var catalog = new AgentCapabilityCatalog(1, [current]);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => catalog.PublishAsync(2, [current, duplicate]).AsTask());

        await using var lease = catalog.Acquire();
        Assert.Equal(1, lease.Snapshot.Epoch);
        Assert.Equal(0, current.DisposeCount);
        Assert.Equal(1, duplicate.DisposeCount);
    }

    [Fact]
    public async Task RecoveryReattachesOperationAndPinsOwningRevision()
    {
        var owner = new RecoveryRevisionOwner();
        await using var catalog = new AgentCapabilityCatalog(1, [owner]);
        await using var operation = new AgentOperation(
            CreateDetachedSnapshot(), new TestEventSink());

        await catalog.ReconcileAsync([operation], default);
        Assert.Equal(AgentOperationObservationStatus.Attached, operation.Snapshot.ObservationStatus);

        await catalog.PublishAsync(2, [Owner("replacement", 1)]);
        Assert.Equal(0, owner.DisposeCount);
        await operation.DisposeAsync();
        Assert.Equal(1, owner.DisposeCount);
    }

    [Fact]
    public void TurnOverlayHasStableIdentityAndSourceRevisions()
    {
        var owner = Owner("native", 7);
        var snapshot = new AgentCapabilitySnapshot
        {
            Epoch = 11,
            Functions = owner.Snapshot.Functions,
            Graph = CapabilityGraph.CreateFromFunctions(owner.Snapshot.Functions),
            Descriptors = owner.Snapshot.Descriptors,
            Revisions = ImmutableDictionary<CapabilitySourceId, ICapabilitySourceRevisionOwner>.Empty
                .Add(owner.SourceId, owner)
        };

        var first = AgentTurnCapabilityOverlay.Compose(snapshot, owner.Snapshot.Functions, null, null);
        var second = AgentTurnCapabilityOverlay.Compose(snapshot, owner.Snapshot.Functions.Reverse(), null, null);

        Assert.Equal(first.Identity.OverlayRevision, second.Identity.OverlayRevision);
        Assert.Equal(first.Identity.EffectiveSnapshotId, second.Identity.EffectiveSnapshotId);
        Assert.Equal(11, first.Identity.AgentEpoch);
        Assert.Equal(new AgentCapabilitySourceRevision("native", 7),
            Assert.Single(first.Identity.SourceRevisions));
        Assert.Equal(first.Tools.Select(static tool => ((AIFunction)tool).Name).Order(StringComparer.Ordinal),
            first.Tools.Select(static tool => ((AIFunction)tool).Name));
    }

    [Fact]
    public void TurnOverlayRejectsModelNameCollision()
    {
        var owner = Owner("native", 1);
        var function = owner.Snapshot.Functions[0];

        var error = Assert.Throws<InvalidOperationException>(() =>
            AgentTurnCapabilityOverlay.Compose(null, [function], [function], null));

        Assert.Contains("Turn capability collision", error.Message, StringComparison.Ordinal);
        Assert.Contains(function.Name, error.Message, StringComparison.Ordinal);
    }

    private static TestRevisionOwner Owner(string source, long revision)
    {
        var builder = new AgentBuilder().WithToolHarness<CombinedCapabilitiesTools>();
        var factory = Assert.Single(
            builder._selectedToolHarnessFactories,
            candidate => candidate.Name == nameof(CombinedCapabilitiesTools));
        var functions = factory.CreateFunctions(new CombinedCapabilitiesTools(), null, null)
            .Where(function => function.AdditionalProperties?.ContainsKey(
                HPDCapabilityMetadata.AdditionalPropertiesKey) == true)
            .ToImmutableArray();
        var sourceId = CapabilitySourceId.Create(source);
        var sourceRevision = CapabilitySourceRevision.Create(revision);
        var descriptors = functions.ToImmutableDictionary(
            function => ((HPDCapabilityMetadata)function.AdditionalProperties![
                HPDCapabilityMetadata.AdditionalPropertiesKey]!).Id,
            function =>
            {
                var metadata = (HPDCapabilityMetadata)function.AdditionalProperties![
                    HPDCapabilityMetadata.AdditionalPropertiesKey]!;
                return new CapabilityDescriptor
                {
                    Id = metadata.Id,
                    SourceId = sourceId,
                    SourceRevision = sourceRevision,
                    ModelName = function.Name,
                    Kind = metadata.Kind
                };
            });
        return new TestRevisionOwner(sourceId, sourceRevision, new CapabilitySourceSnapshot
        {
            Functions = functions,
            Descriptors = descriptors
        });
    }

    private sealed class TestRevisionOwner(
        CapabilitySourceId sourceId,
        CapabilitySourceRevision revision,
        CapabilitySourceSnapshot snapshot) : ICapabilitySourceRevisionOwner
    {
        public CapabilitySourceId SourceId { get; } = sourceId;
        public CapabilitySourceRevision Revision { get; } = revision;
        public CapabilitySourceSnapshot Snapshot { get; } = snapshot;
        public int DisposeCount { get; private set; }

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return ValueTask.CompletedTask;
        }
    }

    private static AgentOperationSnapshot CreateDetachedSnapshot()
    {
        var now = DateTimeOffset.UtcNow;
        return new AgentOperationSnapshot
        {
            OperationId = "recoverable",
            ProviderOperationId = "remote",
            SourceKind = AgentOperationSourceKind.McpTask,
            Name = "remote",
            Address = new AgentExecutionAddress("agent", "session", "thread"),
            ProviderStatus = AgentOperationProviderStatus.Running,
            ObservationStatus = AgentOperationObservationStatus.Detached,
            Control = new AgentOperationControl("remote", AgentOperationKind.Provider,
                AgentOperationCapabilities.Reconcile),
            Notification = new AgentOperationNotificationPolicy(),
            RegisteredAt = now,
            UpdatedAt = now,
            Recovery = new AgentOperationRecoveryReference("test-v1", "protected"),
            Version = 1
        };
    }

    private sealed class RecoveryRevisionOwner : ICapabilitySourceRevisionOwner, IAgentOperationRecoveryProvider
    {
        public CapabilitySourceId SourceId { get; } = CapabilitySourceId.Create("recovery");
        public CapabilitySourceRevision Revision { get; } = CapabilitySourceRevision.Create(1);
        public CapabilitySourceSnapshot Snapshot { get; } = new()
        {
            Functions = [],
            Descriptors = ImmutableDictionary<CapabilityId, CapabilityDescriptor>.Empty
        };
        public int DisposeCount { get; private set; }
        public bool CanRecover(AgentOperationRecoveryReference recoveryReference) =>
            recoveryReference.Kind == "test-v1";
        public async ValueTask<bool> TryRecoverAsync(
            AgentOperation operation,
            AgentCapabilityLease revisionLease,
            CancellationToken cancellationToken)
        {
            await operation.AttachLiveResourcesAsync(
                new NoopController(), new LeaseObserver(revisionLease), cancellationToken);
            return true;
        }
        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class LeaseObserver(AgentCapabilityLease lease) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => lease.DisposeAsync();
    }

    private sealed class NoopController : IAgentOperationController
    {
        public ValueTask RequestCancellationAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask SupplyInputAsync(AgentOperationInput input, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class TestEventSink : IAgentOperationEventSink
    {
        public ValueTask AppendAsync(AgentEvent operationEvent, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
    }
}
