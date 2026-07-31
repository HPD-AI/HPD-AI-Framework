using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HPD.Base;

/// <summary>Installs one optional provider or hosting integration into HPD.BASE.</summary>
public interface IHPDBaseBuilderExtension
{
    string Id { get; }
    bool IsRecordProvider { get; }
    bool SupportsRequiredIndexes { get; }
    void Configure(IServiceCollection services, IReadOnlyList<CollectionDefinition> collections);
    void Initialize(IServiceProvider services) { }
}

/// <summary>Collects one deterministic HPD.BASE host configuration.</summary>
public sealed class HPDBaseBuilder
{
    private readonly IServiceCollection _services;
    private readonly Dictionary<string, CollectionDefinition> _collections = new(StringComparer.Ordinal);
    private readonly List<BaseDependencyTemplate> _dependencyTemplates = [];
    private readonly List<IHPDBaseBuilderExtension> _extensions = [];
    private Action<HPDBaseRuntimeOptions>? _runtime;
    private Action<HPDBaseFilesOptions>? _files;
    private Action<BaseDependencyOptions>? _dependencies;
    private Action<BaseRealtimeOptions>? _realtime;
    private Action<BaseLiveQueryOptions>? _liveQueries;
    private Action<HPDBaseVolatileStoreOptions>? _volatileStore;
    private bool _built;

    internal HPDBaseBuilder(IServiceCollection services) => _services = services;

    public HPDBaseBuilder ConfigureRuntime(Action<HPDBaseRuntimeOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        _runtime += configure;
        return this;
    }

    /// <summary>Configures the built-in process-local volatile provider.</summary>
    /// <remarks>An explicit record provider cannot be combined with volatile-provider configuration.</remarks>
    public HPDBaseBuilder ConfigureVolatileStore(Action<HPDBaseVolatileStoreOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        _volatileStore += configure;
        return this;
    }

    /// <summary>Installs an advanced provider or hosting extension.</summary>
    public HPDBaseBuilder Use(IHPDBaseBuilderExtension extension)
    {
        ArgumentNullException.ThrowIfNull(extension);
        if (_extensions.Any(item => string.Equals(item.Id, extension.Id, StringComparison.Ordinal)))
            throw new InvalidOperationException($"HPD.BASE extension '{extension.Id}' is already installed.");
        _extensions.Add(extension);
        return this;
    }

    public HPDBaseBuilder AddCollection<T>(BaseCollection<T> collection)
    {
        ArgumentNullException.ThrowIfNull(collection);
        if (!_collections.TryAdd(collection.Id, collection.Definition))
            throw new InvalidOperationException($"Collection '{collection.Id}' is already registered.");
        return this;
    }

    public HPDBaseBuilder AddFiles(Action<HPDBaseFilesOptions>? configure = null)
    {
        if (_files is not null)
            throw new InvalidOperationException("Files are already registered.");
        _files = configure ?? (_ => { });
        return this;
    }

    public HPDBaseBuilder AddDependencies(
        Action<BaseDependencyOptions>? configure = null,
        Action<BaseDependencyCatalog>? define = null)
    {
        if (_dependencies is not null)
            throw new InvalidOperationException("Dependencies are already registered.");
        _dependencies = configure ?? (_ => { });
        define?.Invoke(new BaseDependencyCatalog(_dependencyTemplates));
        return this;
    }

    public HPDBaseBuilder AddRealtime(Action<BaseRealtimeOptions>? configure = null)
    {
        if (_realtime is not null)
            throw new InvalidOperationException("Realtime is already registered.");
        _realtime = configure ?? (_ => { });
        return this;
    }

    public HPDBaseBuilder AddLiveQueries(Action<BaseLiveQueryOptions>? configure = null)
    {
        if (_liveQueries is not null)
            throw new InvalidOperationException("Live queries are already registered.");
        _liveQueries = configure ?? (_ => { });
        return this;
    }

    public HPDBaseBuilder ReplacePolicyEvaluator<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>()
        where T : class, IPolicyEvaluator
    {
        _services.Replace(ServiceDescriptor.Singleton<IPolicyEvaluator, T>());
        return this;
    }

    public HPDBaseBuilder AddDescriptorContributor<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>()
        where T : class, IBaseDescriptorContributor
    {
        _services.TryAddEnumerable(ServiceDescriptor.Singleton<IBaseDescriptorContributor, T>());
        return this;
    }

    internal void Build()
    {
        if (_built)
            throw new InvalidOperationException("The HPD.BASE builder was already applied.");
        _built = true;

        IHPDBaseBuilderExtension[] explicitProviders = _extensions.Where(static item => item.IsRecordProvider).ToArray();
        if (explicitProviders.Length > 1)
            throw new InvalidOperationException("Select at most one explicit HPD.BASE record provider.");
        if (explicitProviders.Length == 1 && _volatileStore is not null)
            throw new InvalidOperationException(
                "ConfigureVolatileStore cannot be combined with an explicit HPD.BASE record provider.");

        IHPDBaseBuilderExtension provider = explicitProviders.Length == 1
            ? explicitProviders[0]
            : new VolatileProviderInstaller(_volatileStore);

        CollectionDefinition[] collections = _collections.Values.ToArray();
        ValidateIndexCapabilities(collections, provider);

        _services.AddHPDBaseRuntime(_runtime).UseFailClosedPolicy();
        if (_files is not null)
        {
            _services.AddHPDBaseFiles(options =>
            {
                _files(options);
                for (var index = 0; index < options.Buckets.Count; index++)
                {
                    if (options.Buckets[index].ProviderRef is null)
                    {
                        options.Buckets[index] = options.Buckets[index] with
                        {
                            ProviderRef = new FileProviderRef("volatile")
                        };
                    }
                }
            });
            _services.AddHPDBaseFilesVolatileProvider();
        }
        if (_dependencies is not null)
            _services.AddHPDBaseDependencies(_dependencies, _dependencyTemplates.ToArray());
        if (_realtime is not null)
            _services.AddHPDBaseRealtime(_realtime);
        if (_liveQueries is not null)
        {
            if (_dependencies is null)
                throw new InvalidOperationException("Live queries require AddDependencies.");
            _services.AddHPDBaseLiveQuery(_liveQueries);
        }

        IHPDBaseBuilderExtension[] installedExtensions = explicitProviders.Length == 0
            ? [provider, .. _extensions]
            : _extensions.ToArray();
        foreach (IHPDBaseBuilderExtension extension in installedExtensions)
            extension.Configure(_services, collections);

        _services.AddSingleton(new HPDBaseInstalledFeatures
        {
            Provider = provider.Id,
            CollectionIds = collections.Select(static item => item.Id).ToArray(),
            Files = _files is not null,
            Dependencies = _dependencies is not null,
            Realtime = _realtime is not null,
            LiveQueries = _liveQueries is not null,
            ExtensionIds = installedExtensions.Select(static item => item.Id).ToArray(),
            Extensions = installedExtensions
        });
        _services.TryAddEnumerable(ServiceDescriptor.Singleton<IBaseApplicationInitializer, BaseRecordStoreInitializer>());
    }

    private static void ValidateIndexCapabilities(
        CollectionDefinition[] collections,
        IHPDBaseBuilderExtension provider)
    {
        IndexDefinition? required = collections.SelectMany(static collection => collection.Indexes ?? [])
            .FirstOrDefault(static index => index.Enforcement != EnforcementOwner.Advisory);
        if (required is not null && !provider.SupportsRequiredIndexes)
            throw new InvalidOperationException(
                $"Required physical index '{required.CollectionId}/{required.Id}' cannot be installed by " +
                $"the selected provider '{provider.Id}'. Mark it Advisory or select a capable provider.");
    }
}

public sealed record HPDBaseInstalledFeatures
{
    public required string Provider { get; init; }
    public required string[] CollectionIds { get; init; }
    public required string[] ExtensionIds { get; init; }
    public bool Files { get; init; }
    public bool Dependencies { get; init; }
    public bool Realtime { get; init; }
    public bool LiveQueries { get; init; }
    internal IHPDBaseBuilderExtension[] Extensions { get; init; } = [];
}

/// <summary>Defines validated application dependency-template handles.</summary>
public sealed class BaseDependencyCatalog
{
    private readonly List<BaseDependencyTemplate> _templates;
    internal BaseDependencyCatalog(List<BaseDependencyTemplate> templates) => _templates = templates;

    public BaseDependencyTemplateHandle Define(
        string id,
        BaseDependencyKind kind,
        BaseDependencyVisibility visibility,
        string? description = null,
        params ReadOnlySpan<string> parameters)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (_templates.Any(template => string.Equals(template.Id, id, StringComparison.Ordinal)))
            throw new InvalidOperationException($"Dependency template '{id}' is already registered.");
        var template = new BaseDependencyTemplate
        {
            Id = id,
            Kind = kind,
            Visibility = visibility,
            Description = description,
            ParameterNames = parameters.ToArray()
        };
        _templates.Add(template);
        return new BaseDependencyTemplateHandle(template);
    }
}
