using HPD.Agent.Secrets;

namespace HPD.Agent.Providers;

public interface IProviderContributor
{
    void ConfigureProviders(
        IProviderContributionBuilder builder,
        HpdProviderContributionContext context);
}

public interface IProviderContributionBuilder
{
    void AddProvider(IProvider provider);

    void AddProviderFactory(
        string key,
        Func<IServiceProvider, IProvider> create);

    void AddProviderConfigSerializer(
        string providerKey,
        ProviderClientFamily family,
        ProviderConfigRegistration registration);

    void AddSecretAlias(
        string secretKey,
        params string[] environmentVariableNames);

    void AddModelCatalog(
        IProviderModelCatalog catalog);
}

public interface IProviderModelCatalog
{
    string ProviderKey { get; }

    ValueTask<IReadOnlyList<ProviderModelDescriptor>> GetModelsAsync(
        ProviderModelCatalogContext context,
        ProviderModelQuery query,
        CancellationToken cancellationToken = default);
}

public sealed class ProviderModelCatalogContext
{
    public ProviderModelCatalogContext(IServiceProvider services)
    {
        Services = services ?? throw new ArgumentNullException(nameof(services));
    }

    public IServiceProvider Services { get; }
}

public sealed record ProviderModelQuery(
    ProviderClientFamily Family = ProviderClientFamily.Chat,
    string? Search = null,
    bool Live = false,
    bool FreeOnly = false);

public sealed record ProviderModelDescriptor(
    string ProviderKey,
    string ModelId,
    ProviderClientFamily Family = ProviderClientFamily.Chat,
    string? DisplayName = null,
    bool IsRecommended = false,
    bool IsFree = false,
    bool SupportsTools = false);

public sealed class HpdProviderContributionContext
{
    public required HpdContributionOwner Owner { get; init; }

    public required IServiceProvider Services { get; init; }
}

public sealed record ProviderContribution<T>(
    string Key,
    T Value,
    HpdContributionOwner Owner);

public sealed record ProviderSecretAlias(
    string SecretKey,
    IReadOnlyList<string> EnvironmentVariableNames);

public sealed class ProviderContributionStore
{
    private readonly bool _applySecretAliases;
    private readonly object _gate = new();
    private readonly Dictionary<string, ProviderContribution<Func<IServiceProvider, IProvider>>> _factories =
        new(StringComparer.Ordinal);
    private readonly Dictionary<ProviderConfigKey, ProviderContribution<ProviderConfigRegistration>> _configSerializers = [];
    private readonly Dictionary<string, ProviderContribution<ProviderSecretAlias>> _secretAliases =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, ProviderContribution<IProviderModelCatalog>> _modelCatalogs =
        new(StringComparer.Ordinal);

    public ProviderContributionStore()
        : this(applySecretAliases: true)
    {
    }

    internal ProviderContributionStore(bool applySecretAliases)
    {
        _applySecretAliases = applySecretAliases;
    }

    public event EventHandler<ProviderContributionChangedEventArgs>? Changed;

    public IReadOnlyList<HpdContributionOwner> Owners
    {
        get
        {
            lock (_gate)
            {
                return _factories.Values.Select(static contribution => contribution.Owner)
                    .Concat(_configSerializers.Values.Select(static contribution => contribution.Owner))
                    .Concat(_secretAliases.Values.Select(static contribution => contribution.Owner))
                    .Concat(_modelCatalogs.Values.Select(static contribution => contribution.Owner))
                    .Distinct()
                    .OrderBy(static owner => owner.Scope, StringComparer.Ordinal)
                    .ThenBy(static owner => owner.Id, StringComparer.Ordinal)
                    .ToArray();
            }
        }
    }

    public void AddProvider(IProvider provider, HpdContributionOwner owner)
    {
        ArgumentNullException.ThrowIfNull(provider);
        AddProviderFactory(provider.ProviderKey, _ => provider, owner);
    }

    public void AddProviderFactory(
        string key,
        Func<IServiceProvider, IProvider> create,
        HpdContributionOwner owner)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(create);
        ArgumentNullException.ThrowIfNull(owner);

        lock (_gate)
        {
            _factories[key] = new ProviderContribution<Func<IServiceProvider, IProvider>>(key, create, owner);
        }

        OnChanged(ProviderContributionChangeKind.Provider, owner);
    }

    internal void AddProviderFactory(
        ProviderContribution<Func<IServiceProvider, IProvider>> contribution)
    {
        ArgumentNullException.ThrowIfNull(contribution);
        lock (_gate)
        {
            _factories[contribution.Key] = contribution;
        }

        OnChanged(ProviderContributionChangeKind.Provider, contribution.Owner);
    }

    public void AddProviderConfigSerializer(
        string providerKey,
        ProviderClientFamily family,
        ProviderConfigRegistration registration,
        HpdContributionOwner owner)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerKey);
        ArgumentNullException.ThrowIfNull(registration);
        ArgumentNullException.ThrowIfNull(owner);

        lock (_gate)
        {
            _configSerializers[new ProviderConfigKey(providerKey, family)] =
                new ProviderContribution<ProviderConfigRegistration>(providerKey, registration, owner);
        }

        OnChanged(ProviderContributionChangeKind.ConfigSerializer, owner);
    }

    internal void AddProviderConfigSerializer(
        (ProviderClientFamily Family, ProviderContribution<ProviderConfigRegistration> Contribution) registration)
    {
        ArgumentNullException.ThrowIfNull(registration.Contribution);
        lock (_gate)
        {
            _configSerializers[new ProviderConfigKey(registration.Contribution.Key, registration.Family)] =
                registration.Contribution;
        }

        OnChanged(ProviderContributionChangeKind.ConfigSerializer, registration.Contribution.Owner);
    }

    public void AddSecretAlias(
        string secretKey,
        IReadOnlyList<string> environmentVariableNames,
        HpdContributionOwner owner)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secretKey);
        ArgumentNullException.ThrowIfNull(environmentVariableNames);
        ArgumentNullException.ThrowIfNull(owner);
        if (environmentVariableNames.Count == 0)
        {
            throw new ArgumentException("At least one environment variable name must be provided.", nameof(environmentVariableNames));
        }

        if (environmentVariableNames.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("Environment variable names cannot be null or whitespace.", nameof(environmentVariableNames));
        }

        var alias = new ProviderSecretAlias(secretKey, environmentVariableNames.ToArray());
        lock (_gate)
        {
            _secretAliases[secretKey] = new ProviderContribution<ProviderSecretAlias>(secretKey, alias, owner);
        }

        if (_applySecretAliases)
        {
            SecretAliasRegistry.Apply(secretKey, owner, environmentVariableNames.ToArray());
        }

        OnChanged(ProviderContributionChangeKind.SecretAlias, owner);
    }

    internal void AddSecretAlias(ProviderContribution<ProviderSecretAlias> contribution)
    {
        ArgumentNullException.ThrowIfNull(contribution);
        lock (_gate)
        {
            _secretAliases[contribution.Key] = contribution;
        }

        if (_applySecretAliases)
        {
            SecretAliasRegistry.Apply(
                contribution.Value.SecretKey,
                contribution.Owner,
                contribution.Value.EnvironmentVariableNames.ToArray());
        }

        OnChanged(ProviderContributionChangeKind.SecretAlias, contribution.Owner);
    }

    public void AddModelCatalog(
        IProviderModelCatalog catalog,
        HpdContributionOwner owner)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentException.ThrowIfNullOrWhiteSpace(catalog.ProviderKey);

        lock (_gate)
        {
            _modelCatalogs[catalog.ProviderKey] =
                new ProviderContribution<IProviderModelCatalog>(catalog.ProviderKey, catalog, owner);
        }

        OnChanged(ProviderContributionChangeKind.ModelCatalog, owner);
    }

    internal void AddModelCatalog(ProviderContribution<IProviderModelCatalog> contribution)
    {
        ArgumentNullException.ThrowIfNull(contribution);
        lock (_gate)
        {
            _modelCatalogs[contribution.Key] = contribution;
        }

        OnChanged(ProviderContributionChangeKind.ModelCatalog, contribution.Owner);
    }

    public bool RemoveOwner(HpdContributionOwner owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var removed = false;
        lock (_gate)
        {
            foreach (var key in _factories
                         .Where(pair => pair.Value.Owner == owner)
                         .Select(pair => pair.Key)
                         .ToArray())
            {
                removed |= _factories.Remove(key);
            }

            foreach (var key in _configSerializers
                         .Where(pair => pair.Value.Owner == owner)
                         .Select(pair => pair.Key)
                         .ToArray())
            {
                removed |= _configSerializers.Remove(key);
            }

            foreach (var key in _secretAliases
                         .Where(pair => pair.Value.Owner == owner)
                         .Select(pair => pair.Key)
                         .ToArray())
            {
                removed |= _secretAliases.Remove(key);
            }

            foreach (var key in _modelCatalogs
                         .Where(pair => pair.Value.Owner == owner)
                         .Select(pair => pair.Key)
                         .ToArray())
            {
                removed |= _modelCatalogs.Remove(key);
            }
        }

        if (removed)
        {
            SecretAliasRegistry.RemoveOwner(owner);
            OnChanged(ProviderContributionChangeKind.OwnerRemoved, owner);
        }

        return removed;
    }

    public IReadOnlyList<ProviderContribution<Func<IServiceProvider, IProvider>>> ProviderFactories
    {
        get
        {
            lock (_gate)
            {
                return _factories.Values
                    .OrderBy(static contribution => contribution.Key, StringComparer.Ordinal)
                    .ToArray();
            }
        }
    }

    public ProviderConfigRegistration? GetProviderConfigSerializer(
        string providerKey,
        ProviderClientFamily family)
    {
        lock (_gate)
        {
            return _configSerializers.TryGetValue(new ProviderConfigKey(providerKey, family), out var registration)
                ? registration.Value
                : null;
        }
    }

    public IReadOnlyDictionary<(string ProviderKey, ProviderClientFamily Family), ProviderContribution<ProviderConfigRegistration>> GetProviderConfigSerializers()
    {
        lock (_gate)
        {
            return _configSerializers.ToDictionary(
                pair => (pair.Key.ProviderKey, pair.Key.Family),
                pair => pair.Value);
        }
    }

    public IReadOnlyList<ProviderContribution<ProviderSecretAlias>> SecretAliases
    {
        get
        {
            lock (_gate)
            {
                return _secretAliases.Values
                    .OrderBy(static contribution => contribution.Key, StringComparer.Ordinal)
                    .ToArray();
            }
        }
    }

    public IReadOnlyList<ProviderContribution<IProviderModelCatalog>> ModelCatalogs
    {
        get
        {
            lock (_gate)
            {
                return _modelCatalogs.Values
                    .OrderBy(static contribution => contribution.Key, StringComparer.Ordinal)
                    .ToArray();
            }
        }
    }

    public IProviderModelCatalog? GetModelCatalog(string providerKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerKey);
        lock (_gate)
        {
            return _modelCatalogs.TryGetValue(providerKey, out var catalog)
                ? catalog.Value
                : null;
        }
    }

    public ProviderRegistry BuildRegistry(IServiceProvider? services = null)
    {
        var registry = new ProviderRegistry();
        ApplyTo(registry, services);
        return registry;
    }

    public void ApplyTo(IProviderRegistry registry, IServiceProvider? services = null)
    {
        ArgumentNullException.ThrowIfNull(registry);
        var providerServices = services ?? EmptyServiceProvider.Instance;
        foreach (var contribution in ProviderFactories)
        {
            registry.Register(contribution.Value(providerServices));
        }
    }

    private void OnChanged(ProviderContributionChangeKind kind, HpdContributionOwner owner) =>
        Changed?.Invoke(this, new ProviderContributionChangedEventArgs(kind, owner));
}

internal sealed class ProviderContributionBuilder : IProviderContributionBuilder
{
    private readonly ProviderContributionStore _store;
    private readonly HpdContributionOwner _owner;

    public ProviderContributionBuilder(
        ProviderContributionStore store,
        HpdContributionOwner owner)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
    }

    public void AddProvider(IProvider provider) =>
        _store.AddProvider(provider, _owner);

    public void AddProviderFactory(
        string key,
        Func<IServiceProvider, IProvider> create) =>
        _store.AddProviderFactory(key, create, _owner);

    public void AddProviderConfigSerializer(
        string providerKey,
        ProviderClientFamily family,
        ProviderConfigRegistration registration) =>
        _store.AddProviderConfigSerializer(providerKey, family, registration, _owner);

    public void AddSecretAlias(
        string secretKey,
        params string[] environmentVariableNames) =>
        _store.AddSecretAlias(secretKey, environmentVariableNames, _owner);

    public void AddModelCatalog(IProviderModelCatalog catalog) =>
        _store.AddModelCatalog(catalog, _owner);
}

public sealed class ProviderContributionChangedEventArgs : EventArgs
{
    public ProviderContributionChangedEventArgs(
        ProviderContributionChangeKind kind,
        HpdContributionOwner owner)
    {
        Kind = kind;
        Owner = owner ?? throw new ArgumentNullException(nameof(owner));
    }

    public ProviderContributionChangeKind Kind { get; }

    public HpdContributionOwner Owner { get; }
}

public enum ProviderContributionChangeKind
{
    Provider,
    ConfigSerializer,
    SecretAlias,
    ModelCatalog,
    OwnerRemoved
}
