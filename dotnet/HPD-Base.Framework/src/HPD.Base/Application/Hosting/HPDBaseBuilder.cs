using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HPD.Base;
/// <summary>Installs one optional provider or hosting integration into HPD.BASE.</summary>
public interface IHPDBaseBuilderExtension
{
    /// <summary>Gets id.</summary>
    string Id { get; }

    /// <summary>Gets is Record Provider.</summary>
    bool IsRecordProvider { get; }

    /// <summary>Gets supports Required Indexes.</summary>
    bool SupportsRequiredIndexes { get; }

    /// <summary>Performs configure.</summary>
    void Configure(IServiceCollection services, IReadOnlyList<CollectionDefinition> collections);
    /// <summary>Performs initialize Async.</summary>
    ValueTask InitializeAsync(IServiceProvider services, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
}

/// <summary>Collects one deterministic HPD.BASE host configuration.</summary>
public sealed class HPDBaseBuilder
{
    /// <summary>Provides _services.</summary>
    private readonly IServiceCollection _services;
    /// <summary>Provides _collections.</summary>
    private readonly Dictionary<string, CollectionDefinition> _collections = new(StringComparer.Ordinal);
    /// <summary>Provides _reads.</summary>
    private readonly Dictionary<string, IBaseReadRegistration> _reads = new(StringComparer.Ordinal);
    /// <summary>Provides _dependency Templates.</summary>
    private readonly List<BaseDependencyTemplate> _dependencyTemplates = [];
    /// <summary>Provides _extensions.</summary>
    private readonly List<IHPDBaseBuilderExtension> _extensions = [];
    /// <summary>Provides _runtime.</summary>
    private Action<HPDBaseRuntimeOptions>? _runtime;
    /// <summary>Provides _files.</summary>
    private Action<HPDBaseFilesOptions>? _files;
    /// <summary>Provides _dependencies.</summary>
    private Action<BaseDependencyOptions>? _dependencies;
    /// <summary>Provides _realtime.</summary>
    private Action<BaseRealtimeOptions>? _realtime;
    /// <summary>Provides _live Queries.</summary>
    private Action<BaseLiveQueryOptions>? _liveQueries;
    /// <summary>Provides the optional InMemory store configuration.</summary>
    private Action<HPDBaseInMemoryStoreOptions>? _inMemoryStore;
    /// <summary>Provides _relational.</summary>
    private Action<HPDBaseRelationalOptions>? _relational;
    /// <summary>Provides _schema.</summary>
    private Action<HPDBaseSchemaOptions>? _schema;
    private Action<HPDBaseTokenProtectionOptions>? _tokenProtection;
    /// <summary>Provides _built.</summary>
    private bool _built;
    internal HPDBaseBuilder(IServiceCollection services) => _services = services;
    /// <summary>Performs configure Runtime.</summary>
    public HPDBaseBuilder ConfigureRuntime(Action<HPDBaseRuntimeOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        _runtime += configure;
        return this;
    }

    /// <summary>Configures bounded relational-read and include execution.</summary>
    public HPDBaseBuilder ConfigureRelational(Action<HPDBaseRelationalOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        _relational += configure;
        return this;
    }

    /// <summary>Configures bounded schema planning and application.</summary>
    public HPDBaseBuilder ConfigureSchema(Action<HPDBaseSchemaOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        _schema += configure;
        return this;
    }

    /// <summary>Configures the shared key ring for durable purpose-bound BASE tokens and artifacts.</summary>
    public HPDBaseBuilder ConfigureTokenProtection(Action<HPDBaseTokenProtectionOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        _tokenProtection += configure;
        return this;
    }

    /// <summary>Configures the built-in process-local InMemory provider.</summary>
    /// <remarks>An explicit record provider cannot be combined with InMemory-provider configuration.</remarks>
    public HPDBaseBuilder ConfigureInMemoryStore(Action<HPDBaseInMemoryStoreOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        _inMemoryStore += configure;
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

    /// <summary>Performs add Collection.</summary>
    public HPDBaseBuilder AddCollection<T>(BaseCollection<T> collection)
    {
        ArgumentNullException.ThrowIfNull(collection);
        if (!_collections.TryAdd(collection.Id, collection.Definition))
            throw new InvalidOperationException($"Collection '{collection.Id}' is already registered.");
        return this;
    }

    /// <summary>Registers one generated typed relational read definition.</summary>
    public HPDBaseBuilder AddRead<TParameters, TRow>(BaseReadDefinition<TParameters, TRow> definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (!_reads.TryAdd(definition.Id, definition))
            throw new InvalidOperationException($"Read '{definition.Id}' is already registered.");
        return this;
    }

    /// <summary>Performs add Files.</summary>
    public HPDBaseBuilder AddFiles(Action<HPDBaseFilesOptions>? configure = null)
    {
        if (_files is not null)
            throw new InvalidOperationException("Files are already registered.");
        _files = configure ?? (_ =>
        {
        });
        return this;
    }

    /// <summary>Performs add Dependencies.</summary>
    public HPDBaseBuilder AddDependencies(Action<BaseDependencyOptions>? configure = null, Action<BaseDependencyCatalog>? define = null)
    {
        if (_dependencies is not null)
            throw new InvalidOperationException("Dependencies are already registered.");
        _dependencies = configure ?? (_ =>
        {
        });
        define?.Invoke(new BaseDependencyCatalog(_dependencyTemplates));
        return this;
    }

    /// <summary>Performs add Realtime.</summary>
    public HPDBaseBuilder AddRealtime(Action<BaseRealtimeOptions>? configure = null)
    {
        if (_realtime is not null)
            throw new InvalidOperationException("Realtime is already registered.");
        _realtime = configure ?? (_ =>
        {
        });
        return this;
    }

    /// <summary>Performs add Live Queries.</summary>
    public HPDBaseBuilder AddLiveQueries(Action<BaseLiveQueryOptions>? configure = null)
    {
        if (_liveQueries is not null)
            throw new InvalidOperationException("Live queries are already registered.");
        _liveQueries = configure ?? (_ =>
        {
        });
        return this;
    }

    /// <summary>Performs replace Policy Evaluator.</summary>
    public HPDBaseBuilder ReplacePolicyEvaluator<
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
    T>()
        where T : class, IPolicyEvaluator
    {
        _services.Replace(ServiceDescriptor.Singleton<IPolicyEvaluator, T>());
        return this;
    }

    /// <summary>Performs add Descriptor Contributor.</summary>
    public HPDBaseBuilder AddDescriptorContributor<
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
    T>()
        where T : class, IBaseDescriptorContributor
    {
        _services.TryAddEnumerable(ServiceDescriptor.Singleton<IBaseDescriptorContributor, T>());
        return this;
    }

    /// <summary>Performs build.</summary>
    internal void Build()
    {
        if (_built)
            throw new InvalidOperationException("The HPD.BASE builder was already applied.");
        _built = true;
        IHPDBaseBuilderExtension[] explicitProviders = _extensions.Where(static item => item.IsRecordProvider).ToArray();
        if (explicitProviders.Length > 1)
            throw new InvalidOperationException("Select at most one explicit HPD.BASE record provider.");
        if (explicitProviders.Length == 1 && _inMemoryStore is not null)
            throw new InvalidOperationException("ConfigureInMemoryStore cannot be combined with an explicit HPD.BASE record provider.");
        IHPDBaseBuilderExtension provider = explicitProviders.Length == 1 ? explicitProviders[0] : new InMemoryProviderInstaller(_inMemoryStore);
        CollectionDefinition[] collections = _collections.Values.ToArray();
        var relationalOptions = new HPDBaseRelationalOptions();
        _relational?.Invoke(relationalOptions);
        relationalOptions.Validate();
        var schemaOptions = new HPDBaseSchemaOptions();
        _schema?.Invoke(schemaOptions);
        schemaOptions.Validate();
        BaseApplicationGraphValidator.Validate(collections, _reads.Values, relationalOptions, schemaOptions);
        BaseLogicalSchema logicalSchema = BaseLogicalSchemaFactory.Create(schemaOptions, collections, _reads.Values);
        ValidateIndexCapabilities(collections, provider);
        _services.AddSingleton(new BaseReadRegistry(new Dictionary<string, IBaseReadRegistration>(_reads, StringComparer.Ordinal)));
        _services.AddSingleton(new BaseCollectionRegistry(collections.ToDictionary(static collection => collection.Id, StringComparer.Ordinal)));
        _services.AddSingleton(logicalSchema);
        _services.AddHPDBaseRuntime(_runtime).UseFailClosedPolicy();
        _services.AddSingleton(Microsoft.Extensions.Options.Options.Create(relationalOptions));
        _services.AddSingleton(Microsoft.Extensions.Options.Options.Create(schemaOptions));
        HPDBaseTokenProtectionOptions tokenOptions = CreateTokenOptions();
        _tokenProtection?.Invoke(tokenOptions);
        ValidateTokenOptions(tokenOptions);
        _services.AddSingleton(Microsoft.Extensions.Options.Options.Create(tokenOptions));
        _services.AddSingleton(new BaseTokenProtectionRegistration(_tokenProtection is not null));
        _services.TryAddSingleton<BaseOpaqueTokenProtector>();
        _services.AddSingleton<IBaseSchemaPlanProtector, DefaultBaseSchemaPlanProtector>();
        _services.AddSingleton<IBaseSchemaManager, DefaultBaseSchemaManager>();
        _services.AddSingleton<BaseSchemaCommandHost>();
        _services.TryAddSingleton<IBaseApplicationLifetime, DefaultBaseApplicationLifetime>();
        _services.AddSingleton<IBaseProviderBootstrap, DefaultBaseProviderBootstrap>();
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
                            ProviderRef = new FileProviderRef("inmemory")
                        };
                    }
                }
            });
            _services.AddHPDBaseFilesInMemoryProvider();
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

        IHPDBaseBuilderExtension[] installedExtensions = explicitProviders.Length == 0 ? [provider, .._extensions] : _extensions.ToArray();
        foreach (IHPDBaseBuilderExtension extension in installedExtensions)
            extension.Configure(_services, collections);
        _services.AddSingleton(new HPDBaseInstalledFeatures { Provider = provider.Id, CollectionIds = collections.Select(static item => item.Id).ToArray(), ReadIds = _reads.Keys.ToArray(), Files = _files is not null, Dependencies = _dependencies is not null, Realtime = _realtime is not null, LiveQueries = _liveQueries is not null, ExtensionIds = installedExtensions.Select(static item => item.Id).ToArray(), Extensions = installedExtensions, LogicalSchema = logicalSchema });
        _services.TryAddSingleton<IHPDBaseApplication, DefaultHPDBaseApplication>();
        _services.TryAddSingleton<IHPDBaseAdministration, UnavailableHPDBaseAdministration>();
        _services.TryAddEnumerable(ServiceDescriptor.Singleton<IBaseHealthContributor, BaseApplicationHealthContributor>());
    }

    private static HPDBaseTokenProtectionOptions CreateTokenOptions() => new()
    {
        ActiveKey = new BaseOpaqueTokenKey { Id = 0, Key = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32) },
    };

    private static void ValidateTokenOptions(HPDBaseTokenProtectionOptions options)
    {
        BaseOpaqueTokenKey[] keys = [options.ActiveKey, .. options.DecryptionKeys ?? []];
        if (keys.Any(static key => key?.Key is not { Length: 32 }) || keys.Select(static key => key.Id).Distinct().Count() != keys.Length)
            throw new ArgumentException("Token protection keys must have unique IDs and exactly 32 bytes.", nameof(options));
    }

    /// <summary>Performs validate Index Capabilities.</summary>
    private static void ValidateIndexCapabilities(CollectionDefinition[] collections, IHPDBaseBuilderExtension provider)
    {
        IndexDefinition? required = collections.SelectMany(static collection => collection.Indexes ?? []).FirstOrDefault(static index => index.Enforcement != EnforcementOwner.Advisory);
        if (required is not null && !provider.SupportsRequiredIndexes)
            throw new InvalidOperationException($"Required physical index '{required.CollectionId}/{required.Id}' cannot be installed by " + $"the selected provider '{provider.Id}'. Mark it Advisory or select a capable provider.");
    }
}

/// <summary>Represents hPDBase Installed Features.</summary>
public sealed record HPDBaseInstalledFeatures
{
    /// <summary>Gets or sets provider.</summary>
    public required string Provider { get; init; }
    /// <summary>Gets or sets collection Ids.</summary>
    public required string[] CollectionIds { get; init; }
    /// <summary>Gets or sets read Ids.</summary>
    public required string[] ReadIds { get; init; }
    /// <summary>Gets or sets extension Ids.</summary>
    public required string[] ExtensionIds { get; init; }
    /// <summary>Gets or sets files.</summary>
    public bool Files { get; init; }
    /// <summary>Gets or sets dependencies.</summary>
    public bool Dependencies { get; init; }
    /// <summary>Gets or sets realtime.</summary>
    public bool Realtime { get; init; }
    /// <summary>Gets or sets live Queries.</summary>
    public bool LiveQueries { get; init; }
    internal IHPDBaseBuilderExtension[] Extensions { get; init; } = [];
    internal BaseLogicalSchema LogicalSchema { get; init; } = null!;
}

/// <summary>Defines validated application dependency-template handles.</summary>
public sealed class BaseDependencyCatalog
{
    /// <summary>Provides _templates.</summary>
    private readonly List<BaseDependencyTemplate> _templates;
    internal BaseDependencyCatalog(List<BaseDependencyTemplate> templates) => _templates = templates;
    /// <summary>Performs define.</summary>
    public BaseDependencyTemplateHandle Define(string id, BaseDependencyKind kind, BaseDependencyVisibility visibility, string? description = null, params ReadOnlySpan<string> parameters)
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
