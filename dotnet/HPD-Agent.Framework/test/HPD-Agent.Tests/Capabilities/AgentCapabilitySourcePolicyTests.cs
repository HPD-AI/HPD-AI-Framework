using System.Collections.Immutable;
using HPD.Agent.Tests.Infrastructure;

namespace HPD.Agent.Tests.Capabilities;

public sealed class AgentCapabilitySourcePolicyTests
{
    [Fact]
    public async Task RequiredInitialFailureRejectsConstructionAndDisposesSource()
    {
        var source = new ScriptedSource("required", [LoadOutcome.Failure]);

        await Assert.ThrowsAsync<InvalidOperationException>(() => BuildAsync(
            source, CapabilitySourceInitialLoadPolicy.Required,
            CapabilitySourceRefreshFailurePolicy.RejectCandidate));

        Assert.Equal(1, source.DisposeCount);
    }

    [Fact]
    public async Task OptionalInitialTimeoutOmitsSourceAndDisposesIt()
    {
        var source = new ScriptedSource("optional-timeout", [LoadOutcome.Timeout]);

        await using var agent = await BuildAsync(
            source, CapabilitySourceInitialLoadPolicy.Optional,
            CapabilitySourceRefreshFailurePolicy.RejectCandidate,
            TimeSpan.FromMilliseconds(10));

        Assert.Equal(1, source.DisposeCount);
        Assert.Equal(0, agent.CapabilityEpoch);
    }

    [Theory]
    [InlineData(CapabilitySourceRefreshFailurePolicy.RejectCandidate, false, 0, 0)]
    [InlineData(CapabilitySourceRefreshFailurePolicy.RetainLastKnownGood, true, 1, 0)]
    [InlineData(CapabilitySourceRefreshFailurePolicy.RemoveSource, true, 1, 1)]
    public async Task RefreshFailurePolicyDeterministicallyRejectsRetainsOrRemoves(
        CapabilitySourceRefreshFailurePolicy policy,
        bool published,
        long epoch,
        int ownerDisposeCount)
    {
        var source = new ScriptedSource("refresh", [LoadOutcome.Success, LoadOutcome.Failure]);
        await using var agent = await BuildAsync(
            source, CapabilitySourceInitialLoadPolicy.Required, policy);

        var result = await agent.RefreshCapabilitiesAsync();

        Assert.Equal(published, result.Published);
        Assert.Equal(epoch, result.Epoch);
        Assert.Equal(ownerDisposeCount, source.Owners[0].DisposeCount);
    }

    [Fact]
    public async Task InvalidOwnerMetadataFollowsRetainLastKnownGoodPolicy()
    {
        var source = new ScriptedSource("metadata", [LoadOutcome.Success, LoadOutcome.InvalidOwner]);
        await using var agent = await BuildAsync(
            source,
            CapabilitySourceInitialLoadPolicy.Required,
            CapabilitySourceRefreshFailurePolicy.RetainLastKnownGood);

        var result = await agent.RefreshCapabilitiesAsync();

        Assert.True(result.Published);
        Assert.Equal(1, result.Epoch);
        Assert.Equal(1, source.Owners[1].DisposeCount);
        Assert.Equal(0, source.Owners[0].DisposeCount);
    }

    private static Task<Agent> BuildAsync(
        ScriptedSource source,
        CapabilitySourceInitialLoadPolicy initial,
        CapabilitySourceRefreshFailurePolicy refresh,
        TimeSpan? timeout = null)
    {
        var registration = new AgentCapabilitySourceRegistration(
            new SingleSourceFactory(source), initial, refresh)
        {
            LoadTimeout = timeout ?? TimeSpan.FromSeconds(1)
        };
        return new AgentBuilder(new AgentConfig { Name = "source-policy-test" })
            .WithChatClient(new FakeChatClient())
            .AddCapabilitySource(registration)
            .BuildAsync();
    }

    private enum LoadOutcome { Success, Failure, Timeout, InvalidOwner }

    private sealed class SingleSourceFactory(ScriptedSource source) : IAgentCapabilitySourceFactory
    {
        public CapabilitySourceId Id => source.Id;
        public ValueTask<IAgentCapabilitySource> CreateAsync(
            IServiceProvider? services,
            CancellationToken cancellationToken) => ValueTask.FromResult<IAgentCapabilitySource>(source);
    }

    private sealed class ScriptedSource(string id, IReadOnlyList<LoadOutcome> outcomes)
        : IAgentCapabilitySource
    {
        private int _loadIndex;
        public CapabilitySourceId Id { get; } = CapabilitySourceId.Create(id);
        internal List<TestOwner> Owners { get; } = [];
        internal int DisposeCount { get; private set; }

        public async ValueTask<CapabilitySourceLoadResult> LoadAsync(
            CapabilityLoadContext context,
            CancellationToken cancellationToken)
        {
            var outcome = outcomes[Math.Min(_loadIndex++, outcomes.Count - 1)];
            if (outcome == LoadOutcome.Timeout)
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            if (outcome == LoadOutcome.Failure)
                throw new InvalidOperationException("scripted source failure");
            var owner = new TestOwner(
                outcome == LoadOutcome.InvalidOwner ? CapabilitySourceId.Create("wrong") : Id,
                CapabilitySourceRevision.Create(context.CandidateEpoch));
            Owners.Add(owner);
            return new CapabilitySourceLoadResult(owner);
        }

        public async IAsyncEnumerable<CapabilityInvalidation> WatchAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            yield break;
        }

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class TestOwner(
        CapabilitySourceId sourceId,
        CapabilitySourceRevision revision) : ICapabilitySourceRevisionOwner
    {
        public CapabilitySourceId SourceId { get; } = sourceId;
        public CapabilitySourceRevision Revision { get; } = revision;
        public CapabilitySourceSnapshot Snapshot { get; } = new()
        {
            Functions = [],
            Descriptors = ImmutableDictionary<CapabilityId, CapabilityDescriptor>.Empty
        };
        internal int DisposeCount { get; private set; }
        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return ValueTask.CompletedTask;
        }
    }
}
