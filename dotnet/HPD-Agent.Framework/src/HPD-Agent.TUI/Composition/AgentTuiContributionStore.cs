using HPD.Agent.TUI.Commands;
using HPD.Agent.TUI.Interactions;
using HPD.TUI.Core;

namespace HPD.Agent.TUI.Composition;

public sealed class AgentTuiContributionStore
{
    internal readonly Dictionary<string, HpdAgentTuiCommandDescriptor> Commands = new(StringComparer.OrdinalIgnoreCase);
    internal readonly Dictionary<string, HpdAgentTuiPageDescriptor> Pages = new(StringComparer.OrdinalIgnoreCase);
    internal readonly Dictionary<string, IAgentTuiStatusItem> StatusItems = new(StringComparer.Ordinal);
    internal readonly Dictionary<(TuiSlot Slot, string Key), IAgentTuiWidget> Widgets = [];
    internal readonly Dictionary<string, IAgentTuiAutocompleteProvider> AutocompleteProviders = new(StringComparer.Ordinal);
    internal readonly Dictionary<string, HpdAgentTuiShortcutDescriptor> Shortcuts = new(StringComparer.Ordinal);
    internal readonly Dictionary<string, IAgentTuiEventHandler> EventHandlers = new(StringComparer.Ordinal);
    internal readonly Dictionary<string, IAgentTuiInteractionHandler> InteractionHandlers = new(StringComparer.Ordinal);
    internal readonly Dictionary<string, IAgentTuiTranscriptRendererAdapter> TranscriptRenderers = new(StringComparer.Ordinal);
    internal readonly Dictionary<string, IAgentTuiRunConfigContributor> RunConfigContributors = new(StringComparer.Ordinal);
    internal readonly HashSet<KeyGesture> ShortcutGestures = [];

    private readonly Dictionary<string, HpdContributionOwner> _commandOwners = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, HpdContributionOwner> _pageOwners = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, HpdContributionOwner> _statusItemOwners = new(StringComparer.Ordinal);
    private readonly Dictionary<(TuiSlot Slot, string Key), HpdContributionOwner> _widgetOwners = [];
    private readonly Dictionary<string, HpdContributionOwner> _autocompleteProviderOwners = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HpdContributionOwner> _shortcutOwners = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HpdContributionOwner> _eventHandlerOwners = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HpdContributionOwner> _interactionHandlerOwners = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HpdContributionOwner> _transcriptRendererOwners = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HpdContributionOwner> _runConfigContributorOwners = new(StringComparer.Ordinal);

    internal IAgentTuiShellComponent? Header;
    internal IAgentTuiShellComponent? Footer;
    internal IAgentTuiPromptFactory? PromptFactory;
    internal IAgentTuiShellLayout? ShellLayout;
    internal AgentTuiShellChrome ShellChrome = new();
    internal Theme? Theme;
    internal bool IncludeSlashCommandAutocomplete;

    internal HpdContributionOwner? HeaderOwner;
    internal HpdContributionOwner? FooterOwner;
    internal HpdContributionOwner? PromptFactoryOwner;
    internal HpdContributionOwner? ShellLayoutOwner;
    internal HpdContributionOwner? ShellChromeOwner;
    internal HpdContributionOwner? ThemeOwner;

    public event EventHandler<AgentTuiContributionChangedEventArgs>? Changed;

    public IReadOnlyList<HpdContributionOwner> Owners =>
        _commandOwners.Values
            .Concat(_pageOwners.Values)
            .Concat(_statusItemOwners.Values)
            .Concat(_widgetOwners.Values)
            .Concat(_autocompleteProviderOwners.Values)
            .Concat(_shortcutOwners.Values)
            .Concat(_eventHandlerOwners.Values)
            .Concat(_interactionHandlerOwners.Values)
            .Concat(_transcriptRendererOwners.Values)
            .Concat(_runConfigContributorOwners.Values)
            .Concat(GetSingleOwners())
            .Distinct()
            .OrderBy(static owner => owner.Scope, StringComparer.Ordinal)
            .ThenBy(static owner => owner.Id, StringComparer.Ordinal)
            .ToArray();

    public bool RemoveOwner(HpdContributionOwner owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var removed = false;
        if (_autocompleteProviderOwners.TryGetValue("hpd.slash-commands", out var slashAutocompleteOwner) &&
            slashAutocompleteOwner == owner)
        {
            IncludeSlashCommandAutocomplete = false;
        }

        removed |= RemoveOwned(Commands, _commandOwners, owner);
        removed |= RemoveOwned(Pages, _pageOwners, owner);
        removed |= RemoveOwned(StatusItems, _statusItemOwners, owner);
        removed |= RemoveOwned(Widgets, _widgetOwners, owner);
        removed |= RemoveOwned(AutocompleteProviders, _autocompleteProviderOwners, owner);
        removed |= RemoveOwned(EventHandlers, _eventHandlerOwners, owner);
        removed |= RemoveOwned(InteractionHandlers, _interactionHandlerOwners, owner);
        removed |= RemoveOwned(TranscriptRenderers, _transcriptRendererOwners, owner);
        removed |= RemoveOwned(RunConfigContributors, _runConfigContributorOwners, owner);
        removed |= RemoveOwnedShortcuts(owner);
        removed |= RemoveOwnedSingle(owner);

        if (removed)
        {
            OnChanged(AgentTuiContributionChangeKind.OwnerRemoved, owner);
        }

        return removed;
    }

    internal HpdContributionOwner GetCommandOwner(string key) => GetOwner(_commandOwners, key);

    internal HpdContributionOwner GetPageOwner(string key) => GetOwner(_pageOwners, key);

    internal HpdContributionOwner GetStatusItemOwner(string key) => GetOwner(_statusItemOwners, key);

    internal HpdContributionOwner GetWidgetOwner((TuiSlot Slot, string Key) key) => GetOwner(_widgetOwners, key);

    internal HpdContributionOwner GetAutocompleteProviderOwner(string key) => GetOwner(_autocompleteProviderOwners, key);

    internal HpdContributionOwner GetShortcutOwner(string key) => GetOwner(_shortcutOwners, key);

    internal HpdContributionOwner GetEventHandlerOwner(string key) => GetOwner(_eventHandlerOwners, key);

    internal HpdContributionOwner GetInteractionHandlerOwner(string key) => GetOwner(_interactionHandlerOwners, key);

    internal HpdContributionOwner GetTranscriptRendererOwner(string key) => GetOwner(_transcriptRendererOwners, key);

    internal HpdContributionOwner GetRunConfigContributorOwner(string key) => GetOwner(_runConfigContributorOwners, key);

    internal void MarkCommand(string key, HpdContributionOwner owner) =>
        Mark(_commandOwners, key, owner, AgentTuiContributionChangeKind.Command);

    internal void MarkPage(string key, HpdContributionOwner owner) =>
        Mark(_pageOwners, key, owner, AgentTuiContributionChangeKind.Page);

    internal void MarkStatusItem(string key, HpdContributionOwner owner) =>
        Mark(_statusItemOwners, key, owner, AgentTuiContributionChangeKind.StatusItem);

    internal void MarkWidget((TuiSlot Slot, string Key) key, HpdContributionOwner owner) =>
        Mark(_widgetOwners, key, owner, AgentTuiContributionChangeKind.Widget);

    internal void RemoveWidgetOwner((TuiSlot Slot, string Key) key, HpdContributionOwner owner)
    {
        _widgetOwners.Remove(key);
        OnChanged(AgentTuiContributionChangeKind.Widget, owner);
    }

    internal void MarkAutocompleteProvider(string key, HpdContributionOwner owner) =>
        Mark(_autocompleteProviderOwners, key, owner, AgentTuiContributionChangeKind.AutocompleteProvider);

    internal void MarkShortcut(string key, HpdContributionOwner owner) =>
        Mark(_shortcutOwners, key, owner, AgentTuiContributionChangeKind.Shortcut);

    internal void MarkEventHandler(string key, HpdContributionOwner owner) =>
        Mark(_eventHandlerOwners, key, owner, AgentTuiContributionChangeKind.EventHandler);

    internal void MarkInteractionHandler(string key, HpdContributionOwner owner) =>
        Mark(_interactionHandlerOwners, key, owner, AgentTuiContributionChangeKind.InteractionHandler);

    internal void MarkTranscriptRenderer(string key, HpdContributionOwner owner) =>
        Mark(_transcriptRendererOwners, key, owner, AgentTuiContributionChangeKind.TranscriptRenderer);

    internal void MarkRunConfigContributor(string key, HpdContributionOwner owner) =>
        Mark(_runConfigContributorOwners, key, owner, AgentTuiContributionChangeKind.RunConfigContributor);

    internal void MarkSingle(AgentTuiContributionChangeKind kind, HpdContributionOwner owner)
    {
        switch (kind)
        {
            case AgentTuiContributionChangeKind.Header:
                HeaderOwner = owner;
                break;
            case AgentTuiContributionChangeKind.Footer:
                FooterOwner = owner;
                break;
            case AgentTuiContributionChangeKind.PromptFactory:
                PromptFactoryOwner = owner;
                break;
            case AgentTuiContributionChangeKind.ShellLayout:
                ShellLayoutOwner = owner;
                break;
            case AgentTuiContributionChangeKind.ShellChrome:
                ShellChromeOwner = owner;
                break;
            case AgentTuiContributionChangeKind.Theme:
                ThemeOwner = owner;
                break;
        }

        OnChanged(kind, owner);
    }

    internal void MarkShellChrome(HpdContributionOwner owner) =>
        MarkSingle(AgentTuiContributionChangeKind.ShellChrome, owner);

    private static HpdContributionOwner GetOwner<TKey>(
        IReadOnlyDictionary<TKey, HpdContributionOwner> owners,
        TKey key)
        where TKey : notnull
        => owners.TryGetValue(key, out var owner)
            ? owner
            : HpdContributionOwner.App;

    private void Mark<TKey>(
        IDictionary<TKey, HpdContributionOwner> owners,
        TKey key,
        HpdContributionOwner owner,
        AgentTuiContributionChangeKind kind)
        where TKey : notnull
    {
        owners[key] = owner;
        OnChanged(kind, owner);
    }

    private void OnChanged(AgentTuiContributionChangeKind kind, HpdContributionOwner owner) =>
        Changed?.Invoke(this, new AgentTuiContributionChangedEventArgs(kind, owner));

    private IEnumerable<HpdContributionOwner> GetSingleOwners()
    {
        if (HeaderOwner is not null)
        {
            yield return HeaderOwner;
        }

        if (FooterOwner is not null)
        {
            yield return FooterOwner;
        }

        if (PromptFactoryOwner is not null)
        {
            yield return PromptFactoryOwner;
        }

        if (ShellLayoutOwner is not null)
        {
            yield return ShellLayoutOwner;
        }

        if (ShellChromeOwner is not null)
        {
            yield return ShellChromeOwner;
        }

        if (ThemeOwner is not null)
        {
            yield return ThemeOwner;
        }
    }

    internal void ApplyFrom(AgentTuiContributionStore candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        Apply(Commands, _commandOwners, candidate.Commands, candidate._commandOwners, AgentTuiContributionChangeKind.Command);
        Apply(Pages, _pageOwners, candidate.Pages, candidate._pageOwners, AgentTuiContributionChangeKind.Page);
        Apply(StatusItems, _statusItemOwners, candidate.StatusItems, candidate._statusItemOwners, AgentTuiContributionChangeKind.StatusItem);
        Apply(Widgets, _widgetOwners, candidate.Widgets, candidate._widgetOwners, AgentTuiContributionChangeKind.Widget);
        Apply(AutocompleteProviders, _autocompleteProviderOwners, candidate.AutocompleteProviders, candidate._autocompleteProviderOwners, AgentTuiContributionChangeKind.AutocompleteProvider);
        Apply(Shortcuts, _shortcutOwners, candidate.Shortcuts, candidate._shortcutOwners, AgentTuiContributionChangeKind.Shortcut);
        foreach (var shortcut in candidate.Shortcuts.Values)
        {
            ShortcutGestures.Add(shortcut.Gesture);
        }

        Apply(EventHandlers, _eventHandlerOwners, candidate.EventHandlers, candidate._eventHandlerOwners, AgentTuiContributionChangeKind.EventHandler);
        Apply(InteractionHandlers, _interactionHandlerOwners, candidate.InteractionHandlers, candidate._interactionHandlerOwners, AgentTuiContributionChangeKind.InteractionHandler);
        Apply(TranscriptRenderers, _transcriptRendererOwners, candidate.TranscriptRenderers, candidate._transcriptRendererOwners, AgentTuiContributionChangeKind.TranscriptRenderer);
        Apply(RunConfigContributors, _runConfigContributorOwners, candidate.RunConfigContributors, candidate._runConfigContributorOwners, AgentTuiContributionChangeKind.RunConfigContributor);

        ApplySingle(candidate.HeaderOwner, AgentTuiContributionChangeKind.Header, () => Header = candidate.Header);
        ApplySingle(candidate.FooterOwner, AgentTuiContributionChangeKind.Footer, () => Footer = candidate.Footer);
        ApplySingle(candidate.PromptFactoryOwner, AgentTuiContributionChangeKind.PromptFactory, () => PromptFactory = candidate.PromptFactory);
        ApplySingle(candidate.ShellLayoutOwner, AgentTuiContributionChangeKind.ShellLayout, () => ShellLayout = candidate.ShellLayout);
        ApplySingle(candidate.ShellChromeOwner, AgentTuiContributionChangeKind.ShellChrome, () => ShellChrome = candidate.ShellChrome);
        ApplySingle(candidate.ThemeOwner, AgentTuiContributionChangeKind.Theme, () => Theme = candidate.Theme);

        if (candidate.IncludeSlashCommandAutocomplete)
        {
            IncludeSlashCommandAutocomplete = true;
        }
    }

    private void ApplySingle(
        HpdContributionOwner? owner,
        AgentTuiContributionChangeKind kind,
        Action apply)
    {
        if (owner is null)
        {
            return;
        }

        apply();
        MarkSingle(kind, owner);
    }

    private void Apply<TKey, TValue>(
        IDictionary<TKey, TValue> values,
        IDictionary<TKey, HpdContributionOwner> owners,
        IReadOnlyDictionary<TKey, TValue> candidateValues,
        IReadOnlyDictionary<TKey, HpdContributionOwner> candidateOwners,
        AgentTuiContributionChangeKind kind)
        where TKey : notnull
    {
        foreach (var candidate in candidateValues)
        {
            values[candidate.Key] = candidate.Value;
            var owner = GetOwner(candidateOwners, candidate.Key);
            owners[candidate.Key] = owner;
            OnChanged(kind, owner);
        }
    }

    private static bool RemoveOwned<TKey, TValue>(
        IDictionary<TKey, TValue> values,
        IDictionary<TKey, HpdContributionOwner> owners,
        HpdContributionOwner owner)
        where TKey : notnull
    {
        var removed = false;
        foreach (var key in owners
                     .Where(pair => pair.Value == owner)
                     .Select(pair => pair.Key)
                     .ToArray())
        {
            removed |= values.Remove(key);
            removed |= owners.Remove(key);
        }

        return removed;
    }

    private bool RemoveOwnedShortcuts(HpdContributionOwner owner)
    {
        var removed = false;
        foreach (var key in _shortcutOwners
                     .Where(pair => pair.Value == owner)
                     .Select(pair => pair.Key)
                     .ToArray())
        {
            if (Shortcuts.Remove(key, out var shortcut))
            {
                ShortcutGestures.Remove(shortcut.Gesture);
                removed = true;
            }

            removed |= _shortcutOwners.Remove(key);
        }

        return removed;
    }

    private bool RemoveOwnedSingle(HpdContributionOwner owner)
    {
        var removed = false;
        if (HeaderOwner == owner)
        {
            Header = null;
            HeaderOwner = null;
            removed = true;
        }

        if (FooterOwner == owner)
        {
            Footer = null;
            FooterOwner = null;
            removed = true;
        }

        if (PromptFactoryOwner == owner)
        {
            PromptFactory = null;
            PromptFactoryOwner = null;
            removed = true;
        }

        if (ShellLayoutOwner == owner)
        {
            ShellLayout = null;
            ShellLayoutOwner = null;
            removed = true;
        }

        if (ShellChromeOwner == owner)
        {
            ShellChrome = new AgentTuiShellChrome();
            ShellChromeOwner = null;
            removed = true;
        }

        if (ThemeOwner == owner)
        {
            Theme = null;
            ThemeOwner = null;
            removed = true;
        }

        return removed;
    }
}

public sealed class AgentTuiContributionChangedEventArgs : EventArgs
{
    public AgentTuiContributionChangedEventArgs(
        AgentTuiContributionChangeKind kind,
        HpdContributionOwner owner)
    {
        Kind = kind;
        Owner = owner;
    }

    public AgentTuiContributionChangeKind Kind { get; }

    public HpdContributionOwner Owner { get; }
}

public enum AgentTuiContributionChangeKind
{
    Unknown,
    Command,
    Page,
    StatusItem,
    Widget,
    AutocompleteProvider,
    Shortcut,
    EventHandler,
    InteractionHandler,
    TranscriptRenderer,
    Header,
    Footer,
    PromptFactory,
    ShellLayout,
    ShellChrome,
    Theme,
    RunConfigContributor,
    OwnerRemoved
}
