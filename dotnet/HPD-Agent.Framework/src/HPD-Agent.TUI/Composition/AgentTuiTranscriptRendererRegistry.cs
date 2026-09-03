using HPD.Agent.TUI.Models;
using HPD.TUI.Components;
using HPD.TUI.Core;

namespace HPD.Agent.TUI.Composition;

public sealed class AgentTuiTranscriptRendererRegistry
{
    private readonly IReadOnlyDictionary<string, IAgentTuiTranscriptRendererAdapter> _byKey;
    private readonly IReadOnlyDictionary<Type, IAgentTuiTranscriptRendererAdapter> _byType;
    private readonly FallbackTranscriptCellRenderer _fallback = new();

    internal AgentTuiTranscriptRendererRegistry(
        IEnumerable<IAgentTuiTranscriptRendererAdapter> renderers)
    {
        ArgumentNullException.ThrowIfNull(renderers);
        var byKey = new Dictionary<string, IAgentTuiTranscriptRendererAdapter>(StringComparer.Ordinal);
        var byType = new Dictionary<Type, IAgentTuiTranscriptRendererAdapter>();

        foreach (var renderer in renderers)
        {
            if (!byKey.TryAdd(renderer.Key, renderer))
            {
                throw new InvalidOperationException($"A transcript renderer is already registered for '{renderer.Key}'.");
            }

            if (!byType.TryAdd(renderer.CellType, renderer))
            {
                throw new InvalidOperationException(
                    $"A transcript renderer is already registered for cell type '{renderer.CellType.Name}'.");
            }
        }

        _byKey = byKey;
        _byType = byType;
    }

    public IReadOnlyCollection<string> Keys => _byKey.Keys.ToArray();

    public bool ContainsKey(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return _byKey.ContainsKey(key);
    }

    public bool TryFindRenderer<TCell>(
        string key,
        out IAgentTuiTranscriptRenderer<TCell> renderer)
        where TCell : TranscriptCell
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (_byKey.TryGetValue(key, out var adapter) &&
            adapter is AgentTuiTranscriptRendererAdapter<TCell> typed)
        {
            renderer = typed.Renderer;
            return true;
        }

        renderer = null!;
        return false;
    }

    public IComponent Create(TranscriptEntry entry, int width, Theme theme, ColorSystem colorSystem)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (_byType.TryGetValue(entry.Cell.GetType(), out var renderer))
        {
            return renderer.Create(entry, AgentTuiTranscriptRenderServices.Default, width, theme, colorSystem);
        }

        return _fallback.Create(new AgentTuiTranscriptRenderContext<TranscriptCell>(
            entry,
            entry.Cell,
            AgentTuiTranscriptRenderServices.Default,
            width,
            theme,
            colorSystem));
    }

    private sealed class FallbackTranscriptCellRenderer : IAgentTuiTranscriptRenderer<TranscriptCell>
    {
        public IComponent Create(AgentTuiTranscriptRenderContext<TranscriptCell> context)
            => new Text(context.Cell.GetType().Name, AgentTuiTranscriptRenderServices.Muted);
    }
}
