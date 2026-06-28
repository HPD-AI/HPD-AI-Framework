using HPD.Agent.TUI.Composition;

namespace HPD.Agent.TUI;

public interface IHpdAgentTuiRegistryProvider
{
    HpdAgentTuiRegistry Current { get; }

    event EventHandler<HpdAgentTuiRegistryChangedEventArgs>? Changed;
}

public sealed class HpdAgentTuiRegistryProvider : IHpdAgentTuiRegistryProvider, IDisposable
{
    private readonly AgentTuiContributionStore _store;

    public HpdAgentTuiRegistryProvider(AgentTuiContributionStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        Current = new HpdAgentTuiRegistry(_store);
        _store.Changed += OnStoreChanged;
    }

    public HpdAgentTuiRegistry Current { get; private set; }

    public event EventHandler<HpdAgentTuiRegistryChangedEventArgs>? Changed;

    public void Dispose() => _store.Changed -= OnStoreChanged;

    private void OnStoreChanged(object? sender, AgentTuiContributionChangedEventArgs e)
    {
        Current = new HpdAgentTuiRegistry(_store);
        Changed?.Invoke(this, new HpdAgentTuiRegistryChangedEventArgs(
            Current,
            e.Kind,
            [e.Owner],
            RequiresShellRebuild(e.Kind),
            e.Kind == AgentTuiContributionChangeKind.TranscriptRenderer));
    }

    private static bool RequiresShellRebuild(AgentTuiContributionChangeKind kind)
        => kind is AgentTuiContributionChangeKind.StatusItem
            or AgentTuiContributionChangeKind.Widget
            or AgentTuiContributionChangeKind.Header
            or AgentTuiContributionChangeKind.Footer
            or AgentTuiContributionChangeKind.PromptFactory
            or AgentTuiContributionChangeKind.ShellLayout
            or AgentTuiContributionChangeKind.ShellChrome
            or AgentTuiContributionChangeKind.Theme
            or AgentTuiContributionChangeKind.TranscriptRenderer
            or AgentTuiContributionChangeKind.OwnerRemoved;
}

public sealed class HpdAgentTuiRegistryChangedEventArgs : EventArgs
{
    public HpdAgentTuiRegistryChangedEventArgs(
        HpdAgentTuiRegistry registry,
        AgentTuiContributionChangeKind kind,
        IReadOnlyList<HpdContributionOwner> owners,
        bool requiresShellRebuild,
        bool requiresTranscriptRendererCacheInvalidation)
    {
        Registry = registry ?? throw new ArgumentNullException(nameof(registry));
        Kind = kind;
        Owners = owners ?? throw new ArgumentNullException(nameof(owners));
        RequiresShellRebuild = requiresShellRebuild;
        RequiresTranscriptRendererCacheInvalidation = requiresTranscriptRendererCacheInvalidation;
    }

    public HpdAgentTuiRegistry Registry { get; }

    public AgentTuiContributionChangeKind Kind { get; }

    public IReadOnlyList<HpdContributionOwner> Owners { get; }

    public bool RequiresShellRebuild { get; }

    public bool RequiresTranscriptRendererCacheInvalidation { get; }
}
