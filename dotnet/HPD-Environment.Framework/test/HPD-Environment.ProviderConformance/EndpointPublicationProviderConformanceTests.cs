using HPD.Environment.Contracts;
using Xunit;

namespace HPD.Environment.ProviderConformance;

public abstract class EndpointPublicationProviderConformanceTests
{
    protected abstract ValueTask<
        EndpointPublicationProviderConformanceFixture>
        CreateEndpointFixtureAsync();

    [Fact]
    public async Task Ensure_is_idempotent_for_the_same_spec_and_generation()
    {
        EndpointPublicationProviderConformanceFixture fixture =
            await CreateEndpointFixtureAsync();

        PublishedEndpointStatus first =
            await fixture.EnsureAsync();
        PublishedEndpointStatus second =
            await fixture.EnsureAsync(first);

        Assert.Equal(
            fixture.Metadata.Generation,
            first.ObservedGeneration);
        Assert.Equal(
            fixture.Metadata.Generation,
            second.ObservedGeneration);
        Assert.Equal(PublishedEndpointPhase.Bound, first.EndpointPhase);
        Assert.Equal(first.BoundListener, second.BoundListener);
        Assert.Equal(first.RouterHandle, second.RouterHandle);
    }

    [Fact]
    public async Task Immutable_spec_conflict_is_rejected()
    {
        EndpointPublicationProviderConformanceFixture fixture =
            await CreateEndpointFixtureAsync();
        await fixture.EnsureAsync();
        try
        {
            PublishedEndpointStatus conflict =
                await fixture.Provider
                    .EnsurePublishedEndpointAsync(
                        fixture.Metadata,
                        fixture.ConflictingSpec,
                        observed: null);
            Assert.True(
                conflict.Phase == ResourcePhase.Failed ||
                conflict.ReconciliationOutcome ==
                    ResourceReconciliationOutcome.Rejected);
        }
        catch (Exception exception) when (
            exception is not Xunit.Sdk.XunitException)
        {
            // A provider may reject immutable conflict either as a failed
            // observation or as an exception.
        }
    }

    [Fact]
    public async Task Release_is_idempotent_and_removes_live_publication()
    {
        EndpointPublicationProviderConformanceFixture fixture =
            await CreateEndpointFixtureAsync();
        await fixture.EnsureAsync();

        await fixture.ReleaseAsync();
        await fixture.ReleaseAsync();

        try
        {
            PublishedEndpointStatus status =
                await fixture.GetStatusAsync();
            Assert.True(
                status.EndpointPhase is
                    PublishedEndpointPhase.Released or
                    PublishedEndpointPhase.Failed ||
                status.Phase is
                    ResourcePhase.Deleted or
                    ResourcePhase.Failed);
        }
        catch (Exception exception) when (
            exception is not Xunit.Sdk.XunitException)
        {
            // Removing the provider-local publication handle is also a
            // valid terminal release observation.
        }
    }

    [Fact]
    public async Task Cancellation_before_mutation_is_honored()
    {
        EndpointPublicationProviderConformanceFixture fixture =
            await CreateEndpointFixtureAsync();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        int before = fixture.ObservedMutationCount();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => fixture.EnsureAsync(
                    cancellationToken: cancellation.Token)
                .AsTask());

        Assert.Equal(before, fixture.ObservedMutationCount());
    }
}

public sealed class EndpointPublicationProviderConformanceFixture
{
    private readonly Action<int> _prepareEnsure;
    private readonly Action<int> _prepareRelease;
    private int _ensureCount;
    private int _releaseCount;

    public EndpointPublicationProviderConformanceFixture(
        IEndpointPublicationProvider provider,
        ResourceMetadata<PublishedEndpoint> metadata,
        PublishedEndpointSpec spec,
        PublishedEndpointSpec conflictingSpec,
        Action<int>? prepareEnsure = null,
        Action<int>? prepareRelease = null,
        Func<int>? observedMutationCount = null)
    {
        Provider = provider;
        Metadata = metadata;
        Spec = spec;
        ConflictingSpec = conflictingSpec;
        _prepareEnsure = prepareEnsure ?? (_ => { });
        _prepareRelease = prepareRelease ?? (_ => { });
        ObservedMutationCount = observedMutationCount ?? (() => 0);
        Endpoint = new ResourceRef<PublishedEndpoint>(
            metadata.Id,
            metadata.Scope,
            metadata.Generation);
    }

    public IEndpointPublicationProvider Provider { get; }
    public ResourceMetadata<PublishedEndpoint> Metadata { get; }
    public PublishedEndpointSpec Spec { get; }
    public PublishedEndpointSpec ConflictingSpec { get; }
    public ResourceRef<PublishedEndpoint> Endpoint { get; }
    public Func<int> ObservedMutationCount { get; }

    public ValueTask<PublishedEndpointStatus> EnsureAsync(
        PublishedEndpointStatus? observed = null,
        CancellationToken cancellationToken = default)
    {
        _prepareEnsure(_ensureCount++);
        return Provider.EnsurePublishedEndpointAsync(
            Metadata,
            Spec,
            observed,
            cancellationToken);
    }

    public ValueTask<PublishedEndpointStatus> GetStatusAsync(
        CancellationToken cancellationToken = default) =>
        Provider.GetStatusAsync(Endpoint, cancellationToken);

    public ValueTask ReleaseAsync(
        CancellationToken cancellationToken = default)
    {
        _prepareRelease(_releaseCount++);
        return Provider.ReleasePublishedEndpointAsync(
            Endpoint,
            cancellationToken);
    }
}
