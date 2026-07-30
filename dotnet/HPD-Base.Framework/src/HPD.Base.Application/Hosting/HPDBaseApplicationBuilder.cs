using HPD.Base.Application.Collections;
using HPD.Base.Application.Dependencies;
using HPD.Base.Dependencies;
using HPD.Base.Dependencies.Configuration;
using HPD.Base.Dependencies.DependencyInjection;
using HPD.Base.Files.Configuration;
using HPD.Base.Files.DependencyInjection;
using HPD.Base.Files.InMemory.Configuration;
using HPD.Base.Files.InMemory.DependencyInjection;
using HPD.Base.InMemory.Configuration;
using HPD.Base.InMemory.DependencyInjection;
using HPD.Base.LiveQuery.Configuration;
using HPD.Base.LiveQuery.DependencyInjection;
using HPD.Base.Realtime.Configuration;
using HPD.Base.Realtime.DependencyInjection;
using HPD.Base.Runtime.Configuration;
using HPD.Base.Runtime.DependencyInjection;
using HPD.Base.Schema;
using HPD.Base.Sqlite.Configuration;
using HPD.Base.Sqlite.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using HPD.Base.Application.Sessions;
using HPD.Base.Policy;
using HPD.Base.Runtime.Descriptors;
using System.Diagnostics.CodeAnalysis;

namespace HPD.Base.Application.Hosting;

/// <summary>
/// Collects one deterministic HPD.BASE host configuration without exposing
/// the underlying service collection.
/// </summary>
public sealed class HPDBaseApplicationBuilder
{
    private readonly IServiceCollection _services;
    private readonly Dictionary<string, CollectionDefinition> _collections =
        new(StringComparer.Ordinal);
    private readonly List<BaseDependencyTemplate> _dependencyTemplates = [];
    private Action<HPDBaseRuntimeOptions>? _runtime;
    private Action<HPDBaseInMemoryOptions>? _inMemory;
    private Action<HPDBaseSqliteOptions>? _sqlite;
    private Action<HPDBaseFilesOptions>? _files;
    private Action<HPDBaseFilesInMemoryOptions>? _fileStore;
    private Action<BaseDependencyOptions>? _dependencies;
    private Action<BaseRealtimeOptions>? _realtime;
    private Action<BaseLiveQueryOptions>? _liveQueries;
    private bool _aspNetCore;
    private bool _built;

    internal HPDBaseApplicationBuilder(IServiceCollection services) => _services = services;

    public HPDBaseApplicationBuilder ConfigureRuntime(Action<HPDBaseRuntimeOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        _runtime += configure;
        return this;
    }

    public HPDBaseApplicationBuilder UseFailClosedPolicy() => this;

    public HPDBaseApplicationBuilder UseInMemory(Action<HPDBaseInMemoryOptions>? configure = null)
    {
        EnsureNoStore();
        _inMemory = configure ?? (_ => { });
        return this;
    }

    public HPDBaseApplicationBuilder UseSqlite(Action<HPDBaseSqliteOptions>? configure = null)
    {
        EnsureNoStore();
        _sqlite = configure ?? (_ => { });
        return this;
    }

    public HPDBaseApplicationBuilder AddCollection<T>(BaseCollection<T> collection)
    {
        ArgumentNullException.ThrowIfNull(collection);
        if (!_collections.TryAdd(collection.Id, collection.Definition))
        {
            throw new InvalidOperationException(
                $"Collection '{collection.Id}' is already registered.");
        }

        return this;
    }

    public HPDBaseApplicationBuilder AddFiles(
        Action<HPDBaseFilesOptions>? configure = null,
        Action<HPDBaseFilesInMemoryOptions>? configureStore = null)
    {
        if (_files is not null)
        {
            throw new InvalidOperationException("Files are already registered.");
        }

        _files = configure ?? (_ => { });
        _fileStore = configureStore ?? (_ => { });
        return this;
    }

    public HPDBaseApplicationBuilder AddDependencies(
        Action<BaseDependencyOptions> configure,
        Action<BaseDependencyCatalog>? define = null)
    {
        ArgumentNullException.ThrowIfNull(configure);
        if (_dependencies is not null)
        {
            throw new InvalidOperationException("Dependencies are already registered.");
        }

        _dependencies = configure;
        define?.Invoke(new BaseDependencyCatalog(_dependencyTemplates));
        return this;
    }

    public HPDBaseApplicationBuilder AddRealtime(
        Action<BaseRealtimeOptions>? configure = null)
    {
        _realtime = configure ?? (_ => { });
        return this;
    }

    public HPDBaseApplicationBuilder AddLiveQueries(
        Action<BaseLiveQueryOptions>? configure = null)
    {
        _liveQueries = configure ?? (_ => { });
        return this;
    }

    public HPDBaseApplicationBuilder AddAspNetCore()
    {
        _aspNetCore = true;
        return this;
    }

    public HPDBaseApplicationBuilder ReplacePolicyEvaluator<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>()
        where T : class, IPolicyEvaluator
    {
        _services.Replace(ServiceDescriptor.Singleton<IPolicyEvaluator, T>());
        return this;
    }

    public HPDBaseApplicationBuilder AddDescriptorContributor<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>()
        where T : class, IBaseDescriptorContributor
    {
        _services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IBaseDescriptorContributor, T>());
        return this;
    }

    internal void Build()
    {
        if (_built)
        {
            throw new InvalidOperationException("The HPD.BASE builder was already applied.");
        }

        _built = true;
        if ((_inMemory is null) == (_sqlite is null))
        {
            throw new InvalidOperationException(
                "Select exactly one record provider with UseInMemory or UseSqlite.");
        }

        CollectionDefinition[] collections = _collections.Values.ToArray();
        ValidateIndexCapabilities(collections);
        var runtime = _services.AddHPDBaseRuntime(_runtime);
        runtime.UseFailClosedPolicy();

        if (_inMemory is not null)
        {
            _services.AddHPDBaseInMemoryStore(options =>
            {
                _inMemory(options);
                options.CollectionIds = collections.Select(static item => item.Id).ToArray();
                options.Collections = collections;
            });
        }
        else
        {
            _services.AddHPDBaseSqliteStore(options =>
            {
                _sqlite!(options);
                options.CollectionIds = collections.Select(static item => item.Id).ToArray();
                options.Collections = collections;
            });
        }

        if (_files is not null)
        {
            _services.AddHPDBaseFiles(_files);
            _services.AddHPDBaseFilesInMemoryProvider(_fileStore);
        }

        if (_dependencies is not null)
        {
            _services.AddHPDBaseDependencies(
                _dependencies,
                _dependencyTemplates.ToArray());
        }

        if (_realtime is not null)
        {
            _services.AddHPDBaseRealtime(_realtime);
        }

        if (_liveQueries is not null)
        {
            if (_dependencies is null)
            {
                throw new InvalidOperationException(
                    "Live queries require AddDependencies.");
            }

            _services.AddHPDBaseLiveQuery(_liveQueries);
        }

        if (_aspNetCore)
        {
            HPD.Base.AspNetCore.DependencyInjection
                .HPDBaseAspNetCoreServiceCollectionExtensions
                .AddHPDBaseAspNetCore(_services);
        }

        _services.AddSingleton(new HPDBaseInstalledFeatures
        {
            Provider = _inMemory is not null ? "inMemory" : "sqlite",
            CollectionIds = collections.Select(static item => item.Id).ToArray(),
            Files = _files is not null,
            Dependencies = _dependencies is not null,
            Realtime = _realtime is not null,
            LiveQueries = _liveQueries is not null,
            AspNetCore = _aspNetCore,
        });
        _services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IBaseApplicationInitializer, BaseRecordStoreInitializer>());
    }

    private void EnsureNoStore()
    {
        if (_inMemory is not null || _sqlite is not null)
        {
            throw new InvalidOperationException("Only one record provider may be selected.");
        }
    }

    private void ValidateIndexCapabilities(CollectionDefinition[] collections)
    {
        IndexDefinition? required = collections
            .SelectMany(static collection => collection.Indexes ?? [])
            .FirstOrDefault(static index => index.Enforcement != EnforcementOwner.Advisory);
        if (required is null)
        {
            return;
        }

        string provider = _inMemory is not null ? "InMemory" : "SQLite";
        throw new InvalidOperationException(
            $"Required physical index '{required.CollectionId}/{required.Id}' cannot be " +
            $"installed by the selected {provider} provider. Mark it Advisory or select a " +
            "provider that advertises physical application-index enforcement.");
    }
}

public sealed record HPDBaseInstalledFeatures
{
    public required string Provider { get; init; }
    public required string[] CollectionIds { get; init; }
    public bool Files { get; init; }
    public bool Dependencies { get; init; }
    public bool Realtime { get; init; }
    public bool LiveQueries { get; init; }
    public bool AspNetCore { get; init; }
}

/// <summary>Defines validated application dependency-template handles.</summary>
public sealed class BaseDependencyCatalog
{
    private readonly List<BaseDependencyTemplate> _templates;

    internal BaseDependencyCatalog(List<BaseDependencyTemplate> templates) =>
        _templates = templates;

    public BaseDependencyTemplateHandle Define(
        string id,
        BaseDependencyKind kind,
        BaseDependencyVisibility visibility,
        string? description = null,
        params ReadOnlySpan<string> parameters)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (_templates.Any(template => string.Equals(template.Id, id, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(
                $"Dependency template '{id}' is already registered.");
        }

        var template = new BaseDependencyTemplate
        {
            Id = id,
            Kind = kind,
            Visibility = visibility,
            Description = description,
            ParameterNames = parameters.ToArray(),
        };
        _templates.Add(template);
        return new BaseDependencyTemplateHandle(template);
    }
}
