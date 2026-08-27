namespace HPD.Agent.Providers;

/// <summary>Immutable registry of provider authentication strategies.</summary>
public sealed class ProviderAuthenticationStrategyRegistry : IProviderAuthenticationStrategyRegistry
{
    private readonly IReadOnlyDictionary<Key, IProviderAuthenticationStrategy> _strategies;

    /// <summary>Creates a registry and rejects duplicate provider/backend/kind identities.</summary>
    public ProviderAuthenticationStrategyRegistry(IEnumerable<IProviderAuthenticationStrategy> strategies)
    {
        ArgumentNullException.ThrowIfNull(strategies);
        var entries = new Dictionary<Key, IProviderAuthenticationStrategy>();
        foreach (var strategy in strategies)
        {
            ArgumentNullException.ThrowIfNull(strategy);
            var descriptor = strategy.Descriptor;
            Validate(descriptor.ProviderKey, nameof(descriptor.ProviderKey));
            Validate(descriptor.BackendKey, nameof(descriptor.BackendKey));
            Validate(descriptor.StrategyId.Value, nameof(descriptor.StrategyId));
            var key = new Key(descriptor.ProviderKey, descriptor.BackendKey, descriptor.Kind);
            if (!entries.TryAdd(key, strategy))
                throw new ArgumentException(
                    $"Duplicate authentication strategy for '{key.ProviderKey}/{key.BackendKey}/{key.Kind}'.",
                    nameof(strategies));
        }
        _strategies = entries;
    }

    /// <inheritdoc />
    public IProviderAuthenticationStrategy? Find(
        string providerKey,
        string backendKey,
        ProviderAuthenticationKind kind) =>
        _strategies.TryGetValue(new Key(providerKey, backendKey, kind), out var strategy) ? strategy : null;

    private static void Validate(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Any(char.IsControl))
            throw new ArgumentException("Registry identities must be nonblank and control-free.", name);
    }

    private readonly record struct Key(
        string ProviderKey,
        string BackendKey,
        ProviderAuthenticationKind Kind);
}

/// <summary>Immutable registry of protected provider authorization stores.</summary>
public sealed class ProviderAuthorizationStoreRegistry : IProviderAuthorizationStoreRegistry
{
    private readonly IReadOnlyDictionary<string, ProviderAuthorizationStoreRegistration> _stores;
    private readonly ProviderAuthorizationStoreRegistration? _default;

    /// <summary>Creates a registry and requires at most one explicit default store.</summary>
    public ProviderAuthorizationStoreRegistry(IEnumerable<ProviderAuthorizationStoreRegistration> stores)
    {
        ArgumentNullException.ThrowIfNull(stores);
        var entries = new Dictionary<string, ProviderAuthorizationStoreRegistration>(StringComparer.Ordinal);
        foreach (var registration in stores)
        {
            ArgumentNullException.ThrowIfNull(registration);
            if (string.IsNullOrWhiteSpace(registration.Identity) || registration.Identity.Any(char.IsControl))
                throw new ArgumentException("Store identities must be nonblank and control-free.", nameof(stores));
            if (!entries.TryAdd(registration.Identity, registration))
                throw new ArgumentException($"Duplicate authorization store '{registration.Identity}'.", nameof(stores));
        }
        _stores = entries;
        var defaults = entries.Values.Where(static value => value.IsDefault).ToArray();
        if (defaults.Length > 1)
            throw new ArgumentException("Only one authorization store may be the explicit default.", nameof(stores));
        _default = defaults.SingleOrDefault();
    }

    /// <inheritdoc />
    public ProviderAuthorizationStoreRegistration Resolve(string? storeKey)
    {
        if (string.IsNullOrWhiteSpace(storeKey))
            return _default ?? throw new InvalidOperationException(
                "OAuth omitted StoreKey, but no explicit default authorization store is registered.");
        return _stores.TryGetValue(storeKey, out var registration)
            ? registration
            : throw new KeyNotFoundException($"Authorization store '{storeKey}' is not registered.");
    }
}

/// <summary>Immutable registry of named SDK-native external identities.</summary>
public sealed class ProviderExternalIdentityRegistry : IProviderExternalIdentityRegistry
{
    private readonly IReadOnlyDictionary<string, IProviderExternalIdentityRegistration> _registrations;

    /// <summary>Creates a registry and rejects duplicate or malformed names.</summary>
    public ProviderExternalIdentityRegistry(IEnumerable<IProviderExternalIdentityRegistration> registrations)
    {
        ArgumentNullException.ThrowIfNull(registrations);
        var entries = new Dictionary<string, IProviderExternalIdentityRegistration>(StringComparer.Ordinal);
        foreach (var registration in registrations)
        {
            ArgumentNullException.ThrowIfNull(registration);
            if (string.IsNullOrWhiteSpace(registration.Name) || registration.Name.Any(char.IsControl))
                throw new ArgumentException("External identity names must be nonblank and control-free.", nameof(registrations));
            if (!entries.TryAdd(registration.Name, registration))
                throw new ArgumentException($"Duplicate external identity '{registration.Name}'.", nameof(registrations));
        }
        _registrations = entries;
    }

    /// <inheritdoc />
    public IProviderExternalIdentityRegistration? Find(string name) =>
        _registrations.TryGetValue(name, out var registration) ? registration : null;
}

/// <summary>Creates owned leases for a named SDK-native credential.</summary>
/// <typeparam name="TCredential">The SDK credential type.</typeparam>
public sealed class ProviderExternalIdentityRegistration<TCredential> : IProviderExternalIdentityRegistration
    where TCredential : class
{
    private readonly Func<CancellationToken, ValueTask<(TCredential Credential, IAsyncDisposable? Owner)>> _acquire;

    /// <summary>Creates a registration backed by an asynchronous credential factory.</summary>
    public ProviderExternalIdentityRegistration(
        string name,
        Func<CancellationToken, ValueTask<(TCredential Credential, IAsyncDisposable? Owner)>> acquire)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Any(char.IsControl))
            throw new ArgumentException("Registration name must be nonblank and control-free.", nameof(name));
        Name = name;
        _acquire = acquire ?? throw new ArgumentNullException(nameof(acquire));
    }

    /// <summary>Creates a registration that owns each credential returned by the factory.</summary>
    /// <param name="name">The opaque registration name.</param>
    /// <param name="factory">The synchronous SDK credential factory.</param>
    public ProviderExternalIdentityRegistration(string name, Func<TCredential> factory)
        : this(name, _ =>
        {
            ArgumentNullException.ThrowIfNull(factory);
            var credential = factory() ?? throw new InvalidOperationException("External identity factory returned null.");
            return ValueTask.FromResult((credential, OwnerFor(credential)));
        })
    {
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public Type CredentialType => typeof(TCredential);

    /// <inheritdoc />
    public async ValueTask<IProviderExternalIdentityLease> AcquireAsync(CancellationToken cancellationToken = default)
    {
        var acquired = await _acquire(cancellationToken).ConfigureAwait(false);
        return new Lease(acquired.Credential ?? throw new InvalidOperationException("External identity factory returned null."), acquired.Owner);
    }

    private sealed class Lease(TCredential credential, IAsyncDisposable? owner) : IProviderExternalIdentityLease
    {
        private IAsyncDisposable? _owner = owner;
        private TCredential? _credential = credential;

        public object Credential => _credential ?? throw new ObjectDisposedException(nameof(Lease));
        public Type CredentialType => typeof(TCredential);

        public async ValueTask DisposeAsync()
        {
            _credential = null;
            var current = Interlocked.Exchange(ref _owner, null);
            if (current is not null)
                await current.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static IAsyncDisposable? OwnerFor(TCredential credential) => credential switch
    {
        IAsyncDisposable asyncDisposable => asyncDisposable,
        IDisposable disposable => new DisposableOwner(disposable),
        _ => null
    };

    private sealed class DisposableOwner(IDisposable disposable) : IAsyncDisposable
    {
        private IDisposable? _disposable = disposable;

        public ValueTask DisposeAsync()
        {
            Interlocked.Exchange(ref _disposable, null)?.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
