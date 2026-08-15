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
    /// <summary>The default maximum number of distinct cached clients per manager.</summary>
    public const int DefaultMaximumEntries = 256;

    private readonly object _gate = new();
    private readonly Dictionary<ProviderClientCacheKey, Entry> _entries = [];
    private readonly CancellationTokenSource _shutdown = new();
    private readonly int _maximumEntries;
    private bool _stopping;

    /// <summary>Initializes one agent-scoped provider client manager with a bounded key cardinality.</summary>
    /// <param name="maximumEntries">The positive maximum number of simultaneously owned cache entries.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maximumEntries"/> is outside 1..4096.</exception>
    public ProviderClientManager(int maximumEntries = DefaultMaximumEntries)
    {
        if (maximumEntries is < 1 or > 4096) throw new ArgumentOutOfRangeException(nameof(maximumEntries));
        _maximumEntries = maximumEntries;
    }

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
                if (_entries.Count == _maximumEntries)
                    throw new InvalidOperationException("The bounded provider client cache is full.");
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
        ValidateRequired(key.ProviderKey, 128, nameof(key.ProviderKey));
        ValidateRequired(key.AuthenticationIdentity, 256, nameof(key.AuthenticationIdentity));
        if (!Enum.IsDefined(key.Family)) throw new ArgumentException("The provider family is outside the closed registry.", nameof(key));
        if (key.AuthenticationGeneration < 0) throw new ArgumentOutOfRangeException(nameof(key));
        ValidateOptional(key.Endpoint, 2048, nameof(key.Endpoint));
        ValidateOptional(key.ProviderConfigFingerprint, 256, nameof(key.ProviderConfigFingerprint));
        ValidateOptional(key.ClientBoundModel, 512, nameof(key.ClientBoundModel));
    }

    private static void ValidateRequired(string value, int maximumLength, string name)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximumLength || value.Any(char.IsControl))
            throw new ArgumentException($"{name} must be nonblank, control-free, and at most {maximumLength} UTF-16 code units.", name);
    }

    private static void ValidateOptional(string? value, int maximumLength, string name)
    {
        if (value is not null && (value.Length == 0 || value.Length > maximumLength || value.Any(char.IsControl)))
            throw new ArgumentException($"{name} must be absent or control-free and at most {maximumLength} UTF-16 code units.", name);
    }

    private sealed class Entry
    {
        private int _disposed;
        public Entry(ProviderClientCacheKey key, Func<CancellationToken, ValueTask<TClient>> factory, CancellationToken cancellationToken)
        {
            Key = key;
            // Never execute provider-controlled construction inline while the manager lock is held.
            Construction = Task.Run(() => ConstructAsync(factory, cancellationToken), CancellationToken.None);
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
