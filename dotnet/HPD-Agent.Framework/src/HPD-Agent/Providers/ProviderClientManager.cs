namespace HPD.Agent.Providers;

/// <summary>Identifies one reusable provider client without including operation options.</summary>
public sealed record ProviderClientCacheKey
{
    /// <summary>Gets the canonical provider key.</summary>
    public required string ProviderKey { get; init; }

    /// <summary>Gets the client family.</summary>
    public required ProviderClientFamily Family { get; init; }

    /// <summary>Gets the opaque credential identity.</summary>
    public required string AuthenticationIdentity { get; init; }

    /// <summary>Gets the credential generation.</summary>
    public long AuthenticationGeneration { get; init; }

    /// <summary>Gets the normalized endpoint.</summary>
    public string? Endpoint { get; init; }

    /// <summary>Gets the generated provider-configuration fingerprint.</summary>
    public string? ProviderConfigFingerprint { get; init; }

    /// <summary>Gets the model only when the provider binds it during client construction.</summary>
    public string? ClientBoundModel { get; init; }
}

/// <summary>Leases a reusable provider client from its owner.</summary>
public interface IProviderClientLease<out TClient> : IAsyncDisposable where TClient : class
{
    /// <summary>Gets the leased client.</summary>
    TClient Client { get; }
}

/// <summary>Coordinates shared construction, leasing, eviction, and shutdown of provider clients.</summary>
public sealed class ProviderClientManager<TClient> : IAsyncDisposable where TClient : class
{
    private readonly object _gate = new();
    private readonly Dictionary<ProviderClientCacheKey, Entry> _entries = [];
    private readonly CancellationTokenSource _shutdown = new();
    private bool _stopping;

    /// <summary>Acquires a shared cached client.</summary>
    public async ValueTask<IProviderClientLease<TClient>> AcquireAsync(
        ProviderClientCacheKey key,
        Func<CancellationToken, ValueTask<TClient>> factory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(factory);
        ValidateKey(key);

        Entry entry;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_stopping, this);
            if (!_entries.TryGetValue(key, out entry!))
            {
                entry = new Entry(key, factory, _shutdown.Token);
                _entries.Add(key, entry);
            }
        }

        TClient client;
        try
        {
            client = await entry.Construction.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            lock (_gate)
            {
                if ((entry.Construction.IsFaulted || entry.Construction.IsCanceled) &&
                    _entries.TryGetValue(key, out var current) && ReferenceEquals(current, entry))
                    _entries.Remove(key);
            }
            throw;
        }

        lock (_gate)
        {
            if (_stopping || entry.Draining)
                throw new ObjectDisposedException(nameof(ProviderClientManager<TClient>));
            entry.LeaseCount++;
            return new Lease(this, entry, client);
        }
    }

    /// <summary>Evicts an entry and disposes its owned client after active leases drain.</summary>
    public async ValueTask<bool> EvictAsync(ProviderClientCacheKey key)
    {
        Entry? entry;
        lock (_gate)
        {
            if (!_entries.Remove(key, out entry))
                return false;
            entry.Draining = true;
        }
        await DisposeWhenDrainedAsync(entry).ConfigureAwait(false);
        return true;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        Entry[] entries;
        lock (_gate)
        {
            if (_stopping)
                return;
            _stopping = true;
            entries = _entries.Values.ToArray();
            _entries.Clear();
            foreach (var entry in entries)
                entry.Draining = true;
            _shutdown.Cancel();
        }

        await Task.WhenAll(entries.Select(entry => DisposeWhenDrainedAsync(entry).AsTask())).ConfigureAwait(false);
        _shutdown.Dispose();
    }

    private async ValueTask DisposeWhenDrainedAsync(Entry entry)
    {
        Task wait;
        lock (_gate)
            wait = entry.LeaseCount == 0 ? Task.CompletedTask : entry.Drained.Task;
        await wait.ConfigureAwait(false);
        await entry.DisposeClientAsync().ConfigureAwait(false);
    }

    private async ValueTask ReleaseAsync(Entry entry)
    {
        var dispose = false;
        lock (_gate)
        {
            if (entry.LeaseCount > 0)
                entry.LeaseCount--;
            if (entry.LeaseCount == 0 && entry.Draining)
            {
                entry.Drained.TrySetResult();
                dispose = true;
            }
        }
        if (dispose)
            await entry.DisposeClientAsync().ConfigureAwait(false);
    }

    private static void ValidateKey(ProviderClientCacheKey key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key.ProviderKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(key.AuthenticationIdentity);
    }

    private sealed class Entry
    {
        private int _disposed;
        public Entry(ProviderClientCacheKey key, Func<CancellationToken, ValueTask<TClient>> factory, CancellationToken cancellationToken)
        {
            Key = key;
            Construction = ConstructAsync(factory, cancellationToken);
        }
        public ProviderClientCacheKey Key { get; }
        public Task<TClient> Construction { get; }
        public int LeaseCount { get; set; }
        public bool Draining { get; set; }
        public TaskCompletionSource Drained { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask DisposeClientAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;
            TClient client;
            try { client = await Construction.ConfigureAwait(false); }
            catch { return; }
            if (client is IAsyncDisposable asyncDisposable)
                await asyncDisposable.DisposeAsync().ConfigureAwait(false);
            else if (client is IDisposable disposable)
                disposable.Dispose();
        }

        private static async Task<TClient> ConstructAsync(Func<CancellationToken, ValueTask<TClient>> factory, CancellationToken cancellationToken) =>
            await factory(cancellationToken).ConfigureAwait(false) ?? throw new InvalidOperationException("A provider client factory returned null.");
    }

    private sealed class Lease : IProviderClientLease<TClient>
    {
        private ProviderClientManager<TClient>? _owner;
        private Entry? _entry;
        public Lease(ProviderClientManager<TClient> owner, Entry entry, TClient client)
        {
            _owner = owner;
            _entry = entry;
            Client = client;
        }
        public TClient Client { get; }
        public ValueTask DisposeAsync()
        {
            var owner = Interlocked.Exchange(ref _owner, null);
            var entry = Interlocked.Exchange(ref _entry, null);
            return owner is null || entry is null ? ValueTask.CompletedTask : owner.ReleaseAsync(entry);
        }
    }
}
