using HPD.Agent.Providers;

namespace HPD.Agent.Tests.Core;

public sealed class ProviderClientManagerTests
{
    private static readonly ProviderClientCacheKey Key = new()
    {
        ProviderKey = "openai",
        BackendKey = "platform",
        Family = ProviderClientFamily.Chat,
        Credential = new ProviderClientCredentialCacheIdentity.ConstructionTime(
            "registration:work", new ProviderCredentialGeneration("generation-1")),
        AuthorizationScopeIdentity = "scope-1",
        EffectiveConfigurationFingerprint = "config-v1",
        ProviderManifestRevision = "manifest-v1"
    };

    [Fact]
    public async Task ConcurrentAcquisition_ConstructsOnceAndDisposesAfterAllLeasesDrain()
    {
        await using var manager = new ProviderClientManager<TestClient>();
        var constructions = 0;
        async ValueTask<ProviderClientConstruction<TestClient>> Factory(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref constructions);
            await Task.Delay(20, cancellationToken);
            var client = new TestClient();
            return Construction(client);
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
    public async Task RunClientSet_DisposalReleasesLeaseWithoutDisposingCachedClient()
    {
        await using var manager = new ProviderClientManager<TestClient>();
        var constructions = 0;
        ValueTask<ProviderClientConstruction<TestClient>> Factory(CancellationToken _)
        {
            constructions++;
            return ValueTask.FromResult(Construction(new TestClient()));
        }

        var firstLease = await manager.AcquireAsync(Key, Factory);
        var firstClient = firstLease.Client;
        var runClients = new AgentClientSet { TextToSpeech = firstClient };
        runClients.SetOwnedClients(new HashSet<object>(ReferenceEqualityComparer.Instance));
        runClients.SetLeases([firstLease]);

        await runClients.DisposeAsync();

        Assert.False(firstClient.Disposed);
        await using var secondLease = await manager.AcquireAsync(Key, Factory);
        Assert.Same(firstClient, secondLease.Client);
        Assert.Equal(1, constructions);
    }

    [Fact]
    public async Task CallerCancellation_DoesNotCancelSharedConstruction()
    {
        await using var manager = new ProviderClientManager<TestClient>();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async ValueTask<ProviderClientConstruction<TestClient>> Factory(CancellationToken cancellationToken)
        {
            await release.Task.WaitAsync(cancellationToken);
            var client = new TestClient();
            return Construction(client);
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
        var lease = await manager.AcquireAsync(Key, static _ => ValueTask.FromResult(Construction(new TestClient())));
        var client = lease.Client;

        var shutdown = manager.DisposeAsync().AsTask();
        Assert.False(shutdown.IsCompleted);
        await lease.DisposeAsync();
        await shutdown;

        Assert.True(client.Disposed);
        Assert.Equal(1, client.DisposeCount);
        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            manager.AcquireAsync(Key, static _ => ValueTask.FromResult(Construction(new TestClient()))).AsTask());
    }

    [Fact]
    public async Task CardinalityAndKeyBoundsFailBeforeConstruction()
    {
        await using var manager = new ProviderClientManager<TestClient>(1);
        await using var first = await manager.AcquireAsync(Key, static _ => ValueTask.FromResult(Construction(new TestClient())));
        var constructions = 0;
        var other = Key with { ProviderKey = "anthropic" };

        await Assert.ThrowsAsync<InvalidOperationException>(() => manager.AcquireAsync(other, _ =>
        {
            constructions++;
            return ValueTask.FromResult(Construction(new TestClient()));
        }).AsTask());
        await Assert.ThrowsAsync<ArgumentException>(() => manager.AcquireAsync(
            Key with { AuthorizationScopeIdentity = "\u0001" }, static _ => ValueTask.FromResult(Construction(new TestClient()))).AsTask());
        Assert.Equal(0, constructions);
    }

    private static ProviderClientConstruction<TestClient> Construction(TestClient client) => new()
    {
        Client = client,
        Owner = ProviderClientConstructionUtilities.Own(client)
    };

    private sealed class TestClient : Microsoft.Extensions.AI.ITextToSpeechClient
    {
        public bool Disposed { get; private set; }
        public int DisposeCount { get; private set; }
        public void Dispose()
        {
            Disposed = true;
            DisposeCount++;
        }

        public Task<Microsoft.Extensions.AI.TextToSpeechResponse> GetAudioAsync(
            string text,
            Microsoft.Extensions.AI.TextToSpeechOptions? options = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public async IAsyncEnumerable<Microsoft.Extensions.AI.TextToSpeechResponseUpdate> GetStreamingAudioAsync(
            string text,
            Microsoft.Extensions.AI.TextToSpeechOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
    }
}
