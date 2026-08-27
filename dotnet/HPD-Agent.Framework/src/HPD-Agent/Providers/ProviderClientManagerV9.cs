namespace HPD.Agent.Providers;

/// <summary>Identifies credential state relevant to reusable provider-client construction.</summary>
public abstract record ProviderClientCredentialCacheIdentity
{
    private ProviderClientCredentialCacheIdentity() { }

    /// <summary>Identifies a client that acquires renewable credentials at request time.</summary>
    public sealed record RequestTime(
        string StableCredentialIdentity,
        string GrantIdentity) : ProviderClientCredentialCacheIdentity;

    /// <summary>Identifies a client that captures an exact credential generation.</summary>
    public sealed record ConstructionTime(
        string StableCredentialIdentity,
        ProviderCredentialGeneration CredentialGeneration) : ProviderClientCredentialCacheIdentity;
}

/// <summary>Identifies one reusable provider client without credential material.</summary>
public sealed record ProviderClientCacheKey
{
    /// <summary>Gets the canonical provider key.</summary>
    public required string ProviderKey { get; init; }
    /// <summary>Gets the canonical backend key.</summary>
    public required string BackendKey { get; init; }
    /// <summary>Gets the client family.</summary>
    public required ProviderClientFamily Family { get; init; }
    /// <summary>Gets the model when construction binds it.</summary>
    public string? ModelName { get; init; }
    /// <summary>Gets the binding-sensitive credential identity.</summary>
    public required ProviderClientCredentialCacheIdentity Credential { get; init; }
    /// <summary>Gets the authorization-scope identity.</summary>
    public required string AuthorizationScopeIdentity { get; init; }
    /// <summary>Gets the effective configuration fingerprint.</summary>
    public required string EffectiveConfigurationFingerprint { get; init; }
    /// <summary>Gets the provider manifest revision.</summary>
    public required string ProviderManifestRevision { get; init; }
}

/// <summary>Leases a reusable provider client from its owner.</summary>
public interface IProviderClientLease<out TClient> : IAsyncDisposable where TClient : class
{
    /// <summary>Gets the leased client.</summary>
    TClient Client { get; }
}

/// <summary>Coordinates construction, leasing, eviction, and owner-safe shutdown.</summary>
public sealed class ProviderClientManager<TClient> : IAsyncDisposable where TClient : class
{
    /// <summary>The default maximum number of cached constructions.</summary>
    public const int DefaultMaximumEntries = 256;

    private readonly object _gate = new();
    private readonly Dictionary<ProviderClientCacheKey, Entry> _entries = [];
    private readonly CancellationTokenSource _shutdown = new();
    private readonly int _maximumEntries;
    private bool _stopping;

    /// <summary>Initializes a bounded agent-scoped client manager.</summary>
    public ProviderClientManager(int maximumEntries = DefaultMaximumEntries)
    {
        if (maximumEntries is < 1 or > 4096)
            throw new ArgumentOutOfRangeException(nameof(maximumEntries));
        _maximumEntries = maximumEntries;
    }

    /// <summary>Acquires a lease over one cached provider construction.</summary>
    public async ValueTask<IProviderClientLease<TClient>> AcquireAsync(
        ProviderClientCacheKey key,
        Func<CancellationToken, ValueTask<ProviderClientConstruction<TClient>>> factory,
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
                entry = new Entry(factory, _shutdown.Token);
                _entries.Add(key, entry);
            }
        }

        ProviderClientConstruction<TClient> construction;
        try
        {
            construction = await entry.Construction.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            lock (_gate)
                if ((entry.Construction.IsFaulted || entry.Construction.IsCanceled) &&
                    _entries.TryGetValue(key, out var current) && ReferenceEquals(current, entry))
                    _entries.Remove(key);
            throw;
        }

        lock (_gate)
        {
            if (_stopping || entry.Draining)
                throw new ObjectDisposedException(nameof(ProviderClientManager<TClient>));
            entry.LeaseCount++;
            return new Lease(this, entry, construction.Client);
        }
    }

    /// <summary>Evicts an entry and disposes its owner after active leases drain.</summary>
    public async ValueTask<bool> EvictAsync(ProviderClientCacheKey key)
    {
        Entry? entry;
        lock (_gate)
        {
            if (!_entries.Remove(key, out entry)) return false;
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
            if (_stopping) return;
            _stopping = true;
            entries = _entries.Values.ToArray();
            _entries.Clear();
            foreach (var entry in entries) entry.Draining = true;
            _shutdown.Cancel();
        }
        await Task.WhenAll(entries.Select(entry => DisposeWhenDrainedAsync(entry).AsTask())).ConfigureAwait(false);
        _shutdown.Dispose();
    }

    private async ValueTask DisposeWhenDrainedAsync(Entry entry)
    {
        Task wait;
        lock (_gate) wait = entry.LeaseCount == 0 ? Task.CompletedTask : entry.Drained.Task;
        await wait.ConfigureAwait(false);
        await entry.DisposeOwnerAsync().ConfigureAwait(false);
    }

    private async ValueTask ReleaseAsync(Entry entry)
    {
        var dispose = false;
        lock (_gate)
        {
            if (entry.LeaseCount > 0) entry.LeaseCount--;
            if (entry.LeaseCount == 0 && entry.Draining)
            {
                entry.Drained.TrySetResult();
                dispose = true;
            }
        }
        if (dispose) await entry.DisposeOwnerAsync().ConfigureAwait(false);
    }

    private static void ValidateKey(ProviderClientCacheKey key)
    {
        Require(key.ProviderKey, nameof(key.ProviderKey));
        Require(key.BackendKey, nameof(key.BackendKey));
        Require(key.AuthorizationScopeIdentity, nameof(key.AuthorizationScopeIdentity));
        Require(key.EffectiveConfigurationFingerprint, nameof(key.EffectiveConfigurationFingerprint));
        Require(key.ProviderManifestRevision, nameof(key.ProviderManifestRevision));
        ArgumentNullException.ThrowIfNull(key.Credential);
        if (!Enum.IsDefined(key.Family)) throw new ArgumentOutOfRangeException(nameof(key));
        static void Require(string value, string name)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Any(char.IsControl))
                throw new ArgumentException($"{name} must be nonblank and control-free.", name);
        }
    }

    private sealed class Entry
    {
        private int _disposed;
        internal Entry(
            Func<CancellationToken, ValueTask<ProviderClientConstruction<TClient>>> factory,
            CancellationToken cancellationToken) =>
            Construction = Task.Run(() => ConstructAsync(factory, cancellationToken), CancellationToken.None);
        internal Task<ProviderClientConstruction<TClient>> Construction { get; }
        internal int LeaseCount { get; set; }
        internal bool Draining { get; set; }
        internal TaskCompletionSource Drained { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal async ValueTask DisposeOwnerAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            ProviderClientConstruction<TClient> construction;
            try { construction = await Construction.ConfigureAwait(false); }
            catch { return; }
            await construction.Owner.DisposeAsync().ConfigureAwait(false);
        }
        private static async Task<ProviderClientConstruction<TClient>> ConstructAsync(
            Func<CancellationToken, ValueTask<ProviderClientConstruction<TClient>>> factory,
            CancellationToken cancellationToken)
        {
            var construction = await factory(cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("A provider factory returned null construction.");
            ArgumentNullException.ThrowIfNull(construction.Client);
            ArgumentNullException.ThrowIfNull(construction.Owner);
            return construction;
        }
    }

    private sealed class Lease : IProviderClientLease<TClient>
    {
        private ProviderClientManager<TClient>? _owner;
        private Entry? _entry;
        internal Lease(ProviderClientManager<TClient> owner, Entry entry, TClient client)
        { _owner = owner; _entry = entry; Client = client; }
        public TClient Client { get; }
        public ValueTask DisposeAsync()
        {
            var owner = Interlocked.Exchange(ref _owner, null);
            var entry = Interlocked.Exchange(ref _entry, null);
            return owner is null || entry is null ? ValueTask.CompletedTask : owner.ReleaseAsync(entry);
        }
    }
}
