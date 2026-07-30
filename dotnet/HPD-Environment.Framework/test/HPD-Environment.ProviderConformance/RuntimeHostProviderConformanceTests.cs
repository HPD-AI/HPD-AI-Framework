using HPD.Environment.Contracts;
using Xunit;

namespace HPD.Environment.ProviderConformance;

public abstract class RuntimeHostProviderConformanceTests
{
    protected abstract RuntimeHostProviderConformanceFixture CreateFixture();

    [Fact]
    public async Task Ensure_is_idempotent_and_observes_the_requested_generation()
    {
        RuntimeHostProviderConformanceFixture fixture = CreateFixture();

        RuntimeHostStatus first = await fixture.EnsureAsync();
        RuntimeHostStatus second = await fixture.EnsureAsync(first);

        Assert.Equal(fixture.Metadata.Generation, first.ObservedGeneration);
        Assert.Equal(fixture.Metadata.Generation, second.ObservedGeneration);
        Assert.Equal(first.Handle, second.Handle);
        Assert.Equal(first.ProviderHandle, second.ProviderHandle);
    }

    [Fact]
    public async Task Stop_is_idempotent()
    {
        RuntimeHostProviderConformanceFixture fixture = CreateFixture();
        RuntimeHostStatus ready = await fixture.EnsureAsync();

        RuntimeHostStatus first = await fixture.StopAsync(ready.Handle!.Value);
        RuntimeHostStatus second = await fixture.StopAsync(ready.Handle.Value);

        Assert.Equal(RuntimeHostPhase.Stopped, first.HostPhase);
        Assert.Equal(RuntimeHostPhase.Stopped, second.HostPhase);
        Assert.Equal(first.ObservedGeneration, second.ObservedGeneration);
    }

    [Fact]
    public async Task Delete_is_idempotent_and_removes_the_handle()
    {
        RuntimeHostProviderConformanceFixture fixture = CreateFixture();
        RuntimeHostStatus ready = await fixture.EnsureAsync();

        await fixture.DeleteAsync();
        await fixture.DeleteAsync();

        await AssertRejectedAsync(
            () => fixture.GetStatusAsync(ready.Handle!.Value).AsTask());
    }

    [Fact]
    public async Task Stale_or_forged_handles_are_rejected()
    {
        RuntimeHostProviderConformanceFixture fixture = CreateFixture();
        RuntimeHostStatus ready = await fixture.EnsureAsync();
        TargetHandle<RuntimeHost> handle = ready.Handle!.Value;
        TargetRoute route = handle.Route with
        {
            ProviderHandle = handle.Route.ProviderHandle!.Value with
            {
                Token = $"{handle.Route.ProviderHandle.Value.Token}-stale",
            },
        };
        TargetHandle<RuntimeHost> stale = handle with { Route = route };

        await AssertRejectedAsync(
            () => fixture.GetStatusAsync(stale).AsTask());
    }

    [Fact]
    public async Task Cancellation_before_mutation_is_honored()
    {
        RuntimeHostProviderConformanceFixture fixture = CreateFixture();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        int before = fixture.ObservedMutationCount();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => fixture.EnsureAsync(cancellationToken: cancellation.Token).AsTask());

        Assert.Equal(before, fixture.ObservedMutationCount());
    }

    private static async Task AssertRejectedAsync(
        Func<Task<RuntimeHostStatus>> operation)
    {
        try
        {
            RuntimeHostStatus status = await operation();
            Assert.True(
                status.Phase is ResourcePhase.Failed or ResourcePhase.Deleted ||
                status.ReconciliationOutcome is
                    ResourceReconciliationOutcome.Rejected,
                $"Expected a rejected status, but received phase '{status.Phase}' and outcome '{status.ReconciliationOutcome}'.");
        }
        catch (Exception exception) when (
            exception is not Xunit.Sdk.XunitException)
        {
            // Provider contracts may surface rejection as either a failed
            // observation or an exception. Both are fail-closed outcomes.
        }
    }
}

public sealed class RuntimeHostProviderConformanceFixture
{
    private readonly IRuntimeHostProvider _provider;
    private readonly Action _prepareEnsure;
    private readonly Action _prepareStop;
    private readonly Action _prepareDelete;

    public RuntimeHostProviderConformanceFixture(
        IRuntimeHostProvider provider,
        ResourceMetadata<RuntimeHost> metadata,
        RuntimeHostSpec spec,
        Action? prepareEnsure = null,
        Action? prepareStop = null,
        Action? prepareDelete = null,
        Func<int>? observedMutationCount = null)
    {
        _provider = provider;
        Metadata = metadata;
        Spec = spec;
        _prepareEnsure = prepareEnsure ?? (() => { });
        _prepareStop = prepareStop ?? (() => { });
        _prepareDelete = prepareDelete ?? (() => { });
        ObservedMutationCount = observedMutationCount ?? (() => 0);
    }

    public ResourceMetadata<RuntimeHost> Metadata { get; }
    public RuntimeHostSpec Spec { get; }
    public Func<int> ObservedMutationCount { get; }

    public ValueTask<RuntimeHostStatus> EnsureAsync(
        RuntimeHostStatus? observed = null,
        CancellationToken cancellationToken = default)
    {
        _prepareEnsure();
        return _provider.EnsureAsync(
            Metadata,
            Spec,
            observed,
            cancellationToken);
    }

    public ValueTask<RuntimeHostStatus> StopAsync(
        TargetHandle<RuntimeHost> handle,
        CancellationToken cancellationToken = default)
    {
        _prepareStop();
        return _provider.StopAsync(
            handle,
            StopPolicy.Default,
            cancellationToken);
    }

    public ValueTask DeleteAsync(CancellationToken cancellationToken = default)
    {
        _prepareDelete();
        return _provider.DeleteAsync(
            new ResourceRef<RuntimeHost>(
                Metadata.Id,
                Metadata.Scope,
                Metadata.Generation),
            cancellationToken);
    }

    public ValueTask<RuntimeHostStatus> GetStatusAsync(
        TargetHandle<RuntimeHost> handle,
        CancellationToken cancellationToken = default) =>
        _provider.GetStatusAsync(handle, cancellationToken);
}
