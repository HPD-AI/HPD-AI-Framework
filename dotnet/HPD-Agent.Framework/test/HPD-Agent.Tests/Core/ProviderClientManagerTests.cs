using HPD.Agent.Providers;

namespace HPD.Agent.Tests.Core;

public sealed class ProviderClientManagerTests
{
    private static readonly ProviderClientCacheKey Key = new()
    {
        ProviderKey = "openai",
        Family = ProviderClientFamily.Chat,
        AuthenticationIdentity = "registration:work",
        AuthenticationGeneration = 1,
        Endpoint = "https://example.test",
        ProviderConfigFingerprint = "config-v1"
    };

    [Fact]
    public async Task ConcurrentAcquisition_ConstructsOnceAndDisposesAfterAllLeasesDrain()
    {
        await using var manager = new ProviderClientManager<TestClient>();
        var constructions = 0;
        async ValueTask<TestClient> Factory(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref constructions);
            await Task.Delay(20, cancellationToken);
            return new TestClient();
        }

        var leases = await Task.WhenAll(Enumerable.Range(0, 20)
            .Select(async _ => await manager.AcquireAsync(Key, Factory)));

        Assert.Equal(1, constructions);
        Assert.All(leases, lease => Assert.Same(leases[0].Client, lease.Client));
        var client = leases[0].Client;
        foreach (var lease in leases)
            await lease.DisposeAsync();
        Assert.False(client.Disposed);

        Assert.True(await manager.EvictAsync(Key));
        Assert.True(client.Disposed);
        Assert.Equal(1, client.DisposeCount);
    }

    [Fact]
    public async Task CallerCancellation_DoesNotCancelSharedConstruction()
    {
        await using var manager = new ProviderClientManager<TestClient>();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async ValueTask<TestClient> Factory(CancellationToken cancellationToken)
        {
            await release.Task.WaitAsync(cancellationToken);
            return new TestClient();
        }

        using var canceled = new CancellationTokenSource();
        var canceledWait = manager.AcquireAsync(Key, Factory, canceled.Token).AsTask();
        var survivingWait = manager.AcquireAsync(Key, Factory).AsTask();
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceledWait);
        release.SetResult();
        await using var lease = await survivingWait;
        Assert.NotNull(lease.Client);
    }

    [Fact]
    public async Task Shutdown_DrainsLeaseAndDisposesExactlyOnce()
    {
        var manager = new ProviderClientManager<TestClient>();
        var lease = await manager.AcquireAsync(Key, static _ => ValueTask.FromResult(new TestClient()));
        var client = lease.Client;

        var shutdown = manager.DisposeAsync().AsTask();
        Assert.False(shutdown.IsCompleted);
        await lease.DisposeAsync();
        await shutdown;

        Assert.True(client.Disposed);
        Assert.Equal(1, client.DisposeCount);
        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            manager.AcquireAsync(Key, static _ => ValueTask.FromResult(new TestClient())).AsTask());
    }

    [Fact]
    public async Task CardinalityAndKeyBoundsFailBeforeConstruction()
    {
        await using var manager = new ProviderClientManager<TestClient>(1);
        await using var first = await manager.AcquireAsync(Key, static _ => ValueTask.FromResult(new TestClient()));
        var constructions = 0;
        var other = Key with { ProviderKey = "anthropic" };

        await Assert.ThrowsAsync<InvalidOperationException>(() => manager.AcquireAsync(other, _ =>
        {
            constructions++;
            return ValueTask.FromResult(new TestClient());
        }).AsTask());
        await Assert.ThrowsAsync<ArgumentException>(() => manager.AcquireAsync(
            Key with { AuthenticationIdentity = new string('x', 257) }, static _ => ValueTask.FromResult(new TestClient())).AsTask());
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => manager.AcquireAsync(
            Key with { AuthenticationGeneration = -1 }, static _ => ValueTask.FromResult(new TestClient())).AsTask());
        Assert.Equal(0, constructions);
    }

    private sealed class TestClient : IDisposable
    {
        public bool Disposed { get; private set; }
        public int DisposeCount { get; private set; }
        public void Dispose()
        {
            Disposed = true;
            DisposeCount++;
        }
    }
}
