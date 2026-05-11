using HPD.TUI.Content;
using HPD.TUI.Controllers;
using HPD.TUI.Core;
using HPD.TUI.Models;

namespace HPD.TUI.Extensions;

public sealed class TuiExtensionRegistry
{
    private readonly List<IAutocompleteProvider> _autocompleteProviders = [];
    private readonly List<IActivitySource> _activitySources = [];
    private readonly List<IToolResultMapper> _toolResultMappers = [];
    private readonly Dictionary<Type, object> _contentRenderers = [];
    private readonly Dictionary<Type, object> _selectionProviders = [];
    private readonly Dictionary<Type, List<object>> _viewStrategies = [];

    public CommandModel Commands { get; } = new();

    public IReadOnlyList<IAutocompleteProvider> AutocompleteProviders => _autocompleteProviders;

    public IReadOnlyList<IActivitySource> ActivitySources => _activitySources;

    public IReadOnlyList<IToolResultMapper> ToolResultMappers => _toolResultMappers;

    public TuiExtensionRegistry RegisterCommand(CommandDescriptor command)
    {
        Commands.Register(command);
        return this;
    }

    public TuiExtensionRegistry RegisterAutocompleteProvider(IAutocompleteProvider provider)
    {
        _autocompleteProviders.Add(provider ?? throw new ArgumentNullException(nameof(provider)));
        return this;
    }

    public TuiExtensionRegistry RegisterContentRenderer<TBlock>(IContentRenderer<TBlock> renderer)
        where TBlock : IContentBlock
    {
        _contentRenderers[typeof(TBlock)] = new ContentRendererAdapter<TBlock>(renderer ?? throw new ArgumentNullException(nameof(renderer)));
        return this;
    }

    public TuiExtensionRegistry RegisterSelectionProvider<T>(ISelectionProvider<T> provider)
    {
        _selectionProviders[typeof(T)] = provider ?? throw new ArgumentNullException(nameof(provider));
        return this;
    }

    public TuiExtensionRegistry RegisterViewStrategy<TModel>(IViewStrategy<TModel> strategy)
    {
        ArgumentNullException.ThrowIfNull(strategy);

        var type = typeof(TModel);
        if (!_viewStrategies.TryGetValue(type, out var strategies))
        {
            strategies = [];
            _viewStrategies[type] = strategies;
        }

        strategies.Add(strategy);
        return this;
    }

    public TuiExtensionRegistry RegisterActivitySource(IActivitySource source)
    {
        _activitySources.Add(source ?? throw new ArgumentNullException(nameof(source)));
        return this;
    }

    public TuiExtensionRegistry RegisterToolResultMapper(IToolResultMapper mapper)
    {
        _toolResultMappers.Add(mapper ?? throw new ArgumentNullException(nameof(mapper)));
        return this;
    }

    public bool TryRenderContent(IContentBlock block, out IComponent component)
    {
        ArgumentNullException.ThrowIfNull(block);

        var type = block.GetType();
        if (_contentRenderers.TryGetValue(type, out var renderer) &&
            renderer is IContentRendererAdapter adapter)
        {
            component = adapter.Render(block);
            return true;
        }

        component = block;
        return false;
    }

    public bool TryGetSelection<T>(out SelectionModel<T> model)
    {
        if (_selectionProviders.TryGetValue(typeof(T), out var provider) &&
            provider is ISelectionProvider<T> typed)
        {
            model = typed.GetSelection();
            return true;
        }

        model = null!;
        return false;
    }

    public bool TryCreateView<TModel>(TModel model, in ViewStrategyContext context, out IComponent component)
    {
        if (_viewStrategies.TryGetValue(typeof(TModel), out var strategies))
        {
            foreach (var strategy in strategies)
            {
                if (strategy is IViewStrategy<TModel> typed && typed.CanRender(model, in context))
                {
                    component = typed.CreateView(model, in context);
                    return true;
                }
            }
        }

        component = null!;
        return false;
    }

    public bool TryMapToolResult(string contentType, ReadOnlyMemory<char> payload, out IContentBlock block)
    {
        foreach (var mapper in _toolResultMappers)
        {
            if (mapper.TryMap(contentType, payload, out block))
            {
                return true;
            }
        }

        block = null!;
        return false;
    }

    private interface IContentRendererAdapter
    {
        IComponent Render(IContentBlock block);
    }

    private sealed class ContentRendererAdapter<TBlock> : IContentRendererAdapter
        where TBlock : IContentBlock
    {
        private readonly IContentRenderer<TBlock> _renderer;

        public ContentRendererAdapter(IContentRenderer<TBlock> renderer)
        {
            _renderer = renderer;
        }

        public IComponent Render(IContentBlock block)
        {
            return block is TBlock typed ? _renderer.Render(typed) : block;
        }
    }
}

public interface IContentRenderer<in TBlock>
    where TBlock : IContentBlock
{
    IComponent Render(TBlock block);
}

public interface ISelectionProvider<T>
{
    SelectionModel<T> GetSelection();
}

public interface IViewStrategy<in TModel>
{
    bool CanRender(TModel model, in ViewStrategyContext context);

    IComponent CreateView(TModel model, in ViewStrategyContext context);
}

public readonly record struct ViewStrategyContext(int Width, int Height, string? Mode = null);

public interface IActivitySource
{
    IEnumerable<ActivityModel> GetActivities();
}

public interface IToolResultMapper
{
    bool TryMap(string contentType, ReadOnlyMemory<char> payload, out IContentBlock block);
}
