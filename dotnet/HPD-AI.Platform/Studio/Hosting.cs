using System.Collections.Immutable;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.AI.Platform.Studio;

/// <summary>Contributes one explicitly registered framework Studio module.</summary>
public interface IStudioModuleContribution
{
    /// <summary>Gets the exact module identity this bootstrap contributor owns.</summary>
    string ModuleId { get; }
    /// <summary>Creates the immutable module from the finalized application services.</summary>
    StudioModuleRegistration Create(IServiceProvider services);
}

internal sealed class StudioContributionCatalog
{
    internal sealed record Entry(Type Type, Func<IServiceProvider, IStudioModuleContribution> Factory);
    private readonly List<Entry> _entries = [];
    private bool _sealed;

    internal void Add(Entry entry)
    {
        if (_sealed) throw new InvalidOperationException("The Studio contribution graph is already frozen.");
        if (_entries.Any(value => value.Type == entry.Type)) throw new InvalidOperationException("A Studio contribution was registered more than once.");
        _entries.Add(entry);
    }

    internal ImmutableArray<Entry> Freeze()
    {
        _sealed = true;
        Entry[] values = _entries.ToArray();
        if (!values.Select(static value => value.Type.FullName).SequenceEqual(
                values.Select(static value => value.Type.FullName).Order(StringComparer.Ordinal)))
            throw new InvalidOperationException("Studio contribution types must be registered in canonical ordinal order.");
        return values.ToImmutableArray();
    }
}

/// <summary>Materializes the exact application Studio graph once from explicit contributions.</summary>
public sealed class StudioApplicationGraphProvider
{
    private readonly IServiceProvider _services;
    private readonly ImmutableArray<StudioContributionCatalog.Entry> _contributions;
    private readonly object _gate = new();
    private StudioApplicationGraph? _graph;

    internal StudioApplicationGraphProvider(IServiceProvider services, StudioContributionCatalog catalog)
    { _services = services; _contributions = catalog.Freeze(); }

    /// <summary>Gets the single finalized graph, failing closed on substituted contributions.</summary>
    public StudioApplicationGraph GetRequiredGraph()
    {
        StudioApplicationGraph? current = Volatile.Read(ref _graph);
        if (current is not null) return current;
        lock (_gate)
        {
            current = _graph;
            if (current is not null) return current;
            StudioModuleRegistration[] modules = _contributions.Select(entry =>
            {
                IStudioModuleContribution contributor = entry.Factory(_services);
                StudioModuleRegistration module = contributor.Create(_services);
                if (!StringComparer.Ordinal.Equals(contributor.ModuleId, module.Identity.ModuleId))
                    throw new InvalidOperationException("A Studio bootstrap contributor substituted its module identity.");
                return module;
            }).ToArray();
            StudioModuleRegistration baseModule = modules.Single(static value => value.ModuleClass == StudioModuleClass.Base);
            current = StudioApplicationGraph.Create(baseModule.OwningApplicationId, 1, modules);
            Volatile.Write(ref _graph, current);
            return current;
        }
    }
}

/// <summary>Registers immutable Studio contributions on the shared platform builder.</summary>
public static class StudioHostingBuilderExtensions
{
    /// <summary>Adds one explicit, source-known Studio module contribution.</summary>
    public static HPDAIPlatformBuilder AddStudioModule<TContribution>(this HPDAIPlatformBuilder builder)
        where TContribution : class, IStudioModuleContribution, new()
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.StudioContributions.Add(new StudioContributionCatalog.Entry(
            typeof(TContribution), static _ => new TContribution()));
        if (typeof(IStudioModuleRuntimeContributionFactory).IsAssignableFrom(typeof(TContribution)))
            builder.Services.AddSingleton(typeof(IStudioModuleRuntimeContributionFactory), static _ => new TContribution());
        return builder;
    }
}
