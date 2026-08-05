using HPD.Environment.Contracts;
using Xunit;

namespace HPD.Environment.ProviderConformance;

public abstract class AuthorityBindingProviderConformanceTests
{
    protected abstract ValueTask<AuthorityBindingProviderConformanceFixture>
        CreateAuthorityFixtureAsync();

    [Fact]
    public async Task Ensure_is_idempotent_for_the_same_spec_and_generation()
    {
        AuthorityBindingProviderConformanceFixture fixture =
            await CreateAuthorityFixtureAsync();

        AuthorityBindingStatus first =
            await fixture.EnsureAsync();
        AuthorityBindingStatus second =
            await fixture.EnsureAsync(first);

        Assert.Equal(
            fixture.Metadata.Generation,
            first.ObservedGeneration);
        Assert.Equal(
            fixture.Metadata.Generation,
            second.ObservedGeneration);
        Assert.Equal(
            AuthorityBindingPhase.Projected,
            first.BindingPhase);
        Assert.Equal(first.BoundAuthority, second.BoundAuthority);
    }

    [Fact]
    public async Task Revoke_is_idempotent_and_verified()
    {
        AuthorityBindingProviderConformanceFixture fixture =
            await CreateAuthorityFixtureAsync();
        await fixture.EnsureAsync();

        await fixture.RevokeAsync();
        await fixture.RevokeAsync();
        AuthorityBindingStatus status =
            await fixture.GetStatusAsync();

        Assert.Equal(
            AuthorityBindingPhase.Revoked,
            status.BindingPhase);
        Assert.Equal(
            RevocationVerificationStatus.Verified,
            status.BoundAuthority?.RevocationStatus);
    }

    [Fact]
    public async Task Immutable_spec_conflict_is_rejected()
    {
        AuthorityBindingProviderConformanceFixture fixture =
            await CreateAuthorityFixtureAsync();
        await fixture.EnsureAsync();

        try
        {
            AuthorityBindingStatus conflict =
                await fixture.Provider
                    .EnsureAuthorityBindingAsync(
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
    public async Task Stale_generation_is_rejected()
    {
        AuthorityBindingProviderConformanceFixture fixture =
            await CreateAuthorityFixtureAsync();
        await fixture.EnsureAsync();
        ResourceRef<AuthorityBinding> stale = fixture.Binding with
        {
            Generation = new ResourceGeneration(
                fixture.Binding.Generation!.Value.Value + 1),
        };

        await Assert.ThrowsAnyAsync<Exception>(
            () => fixture.Provider
                .RevokeAuthorityBindingAsync(stale)
                .AsTask());
    }

    [Fact]
    public async Task Expired_lease_is_not_reported_as_projected()
    {
        AuthorityBindingProviderConformanceFixture fixture =
            await CreateAuthorityFixtureAsync();
        await fixture.Provider.EnsureAuthorityBindingAsync(
            fixture.Metadata,
            fixture.ExpiringSpec,
            observed: null);

        fixture.AdvancePastExpiry();
        AuthorityBindingStatus status =
            await fixture.GetStatusAsync();

        Assert.NotEqual(
            AuthorityBindingPhase.Projected,
            status.BindingPhase);
        Assert.True(status.Phase is
            ResourcePhase.Degraded or
            ResourcePhase.Deleting or
            ResourcePhase.Ready);
    }

    [Fact]
    public async Task Cancellation_before_mutation_is_honored()
    {
        AuthorityBindingProviderConformanceFixture fixture =
            await CreateAuthorityFixtureAsync();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        int before = fixture.ObservedMutationCount();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => fixture.EnsureAsync(
                    cancellationToken: cancellation.Token)
                .AsTask());

        Assert.Equal(
            before,
            fixture.ObservedMutationCount());
    }
}

public sealed class AuthorityBindingProviderConformanceFixture(
    IAuthorityBindingProvider provider,
    ResourceMetadata<AuthorityBinding> metadata,
    AuthorityBindingSpec spec,
    AuthorityBindingSpec conflictingSpec,
    AuthorityBindingSpec expiringSpec,
    Action? advancePastExpiry = null,
    Func<int>? observedMutationCount = null)
{
    public IAuthorityBindingProvider Provider { get; } = provider;
    public ResourceMetadata<AuthorityBinding> Metadata { get; } =
        metadata;
    public AuthorityBindingSpec Spec { get; } = spec;
    public AuthorityBindingSpec ConflictingSpec { get; } =
        conflictingSpec;
    public AuthorityBindingSpec ExpiringSpec { get; } =
        expiringSpec;
    public Action AdvancePastExpiry { get; } =
        advancePastExpiry ?? (() => { });
    public Func<int> ObservedMutationCount { get; } =
        observedMutationCount ?? (() => 0);
    public ResourceRef<AuthorityBinding> Binding { get; } =
        new(metadata.Id, metadata.Scope, metadata.Generation);

    public ValueTask<AuthorityBindingStatus> EnsureAsync(
        AuthorityBindingStatus? observed = null,
        CancellationToken cancellationToken = default) =>
        Provider.EnsureAuthorityBindingAsync(
            Metadata,
            Spec,
            observed,
            cancellationToken);

    public ValueTask<AuthorityBindingStatus> GetStatusAsync(
        CancellationToken cancellationToken = default) =>
        Provider.GetStatusAsync(Binding, cancellationToken);

    public ValueTask RevokeAsync(
        CancellationToken cancellationToken = default) =>
        Provider.RevokeAuthorityBindingAsync(
            Binding,
            cancellationToken);
}
