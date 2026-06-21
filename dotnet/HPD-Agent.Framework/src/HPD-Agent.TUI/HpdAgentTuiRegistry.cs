using HPD.Agent.TUI.Composition;
using HPD.Agent.TUI.Commands;
using HPD.Agent.TUI.Interactions;
using HPD.TUI.Core;

namespace HPD.Agent.TUI;

public sealed class HpdAgentTuiRegistry
{
    private readonly IReadOnlyDictionary<string, HpdAgentTuiCommandDescriptor> _commands;
    private readonly HpdAgentTuiCommandDescriptor[] _commandList;
    private readonly IReadOnlyList<AgentTuiContribution<IAgentTuiStatusItem>> _statusItems;
    private readonly IReadOnlyList<AgentTuiContribution<IAgentTuiWidget>> _aboveEditorWidgets;
    private readonly IReadOnlyList<AgentTuiContribution<IAgentTuiWidget>> _belowEditorWidgets;
    private readonly IReadOnlyList<AgentTuiContribution<IAgentTuiAutocompleteProvider>> _autocompleteProviders;
    private readonly IReadOnlyDictionary<string, HpdAgentTuiShortcutDescriptor> _shortcuts;
    private readonly HpdAgentTuiShortcutDescriptor[] _shortcutList;
    private readonly IReadOnlyList<AgentTuiContribution<IAgentTuiEventHandler>> _eventHandlers;
    private readonly IReadOnlyList<AgentTuiContribution<IAgentTuiInteractionHandler>> _interactionHandlers;
    private readonly IReadOnlyDictionary<string, HpdAgentTuiPageDescriptor> _pages;
    private readonly HpdAgentTuiPageDescriptor[] _pageList;
    private readonly IAgentTuiPromptFactory? _promptFactory;
    private readonly IAgentTuiShellLayout? _shellLayout;

    internal HpdAgentTuiRegistry(
        IEnumerable<HpdAgentTuiCommandDescriptor> commands,
        IEnumerable<HpdAgentTuiPageDescriptor> pages,
        IReadOnlyDictionary<string, IAgentTuiStatusItem> statusItems,
        IReadOnlyDictionary<(TuiSlot Slot, string Key), IAgentTuiWidget> widgets,
        IReadOnlyDictionary<string, IAgentTuiAutocompleteProvider> autocompleteProviders,
        IEnumerable<HpdAgentTuiShortcutDescriptor> shortcuts,
        IReadOnlyDictionary<string, IAgentTuiEventHandler> eventHandlers,
        IReadOnlyDictionary<string, IAgentTuiInteractionHandler> interactionHandlers,
        IEnumerable<IAgentTuiTranscriptRendererAdapter> transcriptRenderers,
        IAgentTuiShellComponent? header,
        IAgentTuiShellComponent? footer,
        IAgentTuiPromptFactory? promptFactory,
        IAgentTuiShellLayout? shellLayout,
        AgentTuiShellChrome shellChrome,
        Theme? theme,
        bool includeSlashCommandAutocomplete,
        AgentTuiRunConfigComposer? runConfigComposer)
    {
        _commands = commands.ToDictionary(command => command.SlashName, StringComparer.OrdinalIgnoreCase);
        _commandList = _commands.Values.ToArray();
        _pages = pages.ToDictionary(page => page.Id, StringComparer.OrdinalIgnoreCase);
        _pageList = _pages.Values.ToArray();
        _statusItems = statusItems
            .Select(pair => new AgentTuiContribution<IAgentTuiStatusItem>(pair.Key, pair.Value))
            .ToArray();
        _aboveEditorWidgets = widgets
            .Where(pair => pair.Key.Slot == TuiSlot.AboveEditor)
            .Select(pair => new AgentTuiContribution<IAgentTuiWidget>(pair.Key.Key, pair.Value))
            .ToArray();
        _belowEditorWidgets = widgets
            .Where(pair => pair.Key.Slot == TuiSlot.BelowEditor)
            .Select(pair => new AgentTuiContribution<IAgentTuiWidget>(pair.Key.Key, pair.Value))
            .ToArray();
        var providerContributions = autocompleteProviders
            .Select(pair => new AgentTuiContribution<IAgentTuiAutocompleteProvider>(pair.Key, pair.Value))
            .ToList();

        if (includeSlashCommandAutocomplete)
        {
            providerContributions.Insert(0, new AgentTuiContribution<IAgentTuiAutocompleteProvider>(
                "hpd.slash-commands",
                new SlashCommandAgentAutocompleteProvider(this)));
        }

        _autocompleteProviders = providerContributions.ToArray();
        _shortcuts = shortcuts.ToDictionary(shortcut => shortcut.Key, StringComparer.Ordinal);
        _shortcutList = _shortcuts.Values.ToArray();
        _eventHandlers = eventHandlers
            .Select(pair => new AgentTuiContribution<IAgentTuiEventHandler>(pair.Key, pair.Value))
            .ToArray();
        _interactionHandlers = interactionHandlers
            .Select(pair => new AgentTuiContribution<IAgentTuiInteractionHandler>(pair.Key, pair.Value))
            .ToArray();
        TranscriptRenderers = new AgentTuiTranscriptRendererRegistry(transcriptRenderers);
        Header = header;
        Footer = footer;
        _promptFactory = promptFactory;
        _shellLayout = shellLayout;
        ShellChrome = (shellChrome ?? throw new ArgumentNullException(nameof(shellChrome))).Clone();
        Theme = theme;
        RunConfigComposer = runConfigComposer;
    }

    public IReadOnlyList<HpdAgentTuiCommandDescriptor> Commands => _commandList;

    public IReadOnlyList<HpdAgentTuiPageDescriptor> Pages => _pageList;

    public string? DefaultPageId => _pages.Count > 0 ? _pages.Values.First().Id : null;

    public IAgentTuiShellComponent? Header { get; }

    public IAgentTuiShellComponent? Footer { get; }

    public IAgentTuiPromptFactory PromptFactory => _promptFactory ?? throw new InvalidOperationException(
        "No prompt factory was registered. Add one with AddDefaultPrompt() or AddPrompt(...).");

    public IAgentTuiShellLayout ShellLayout => _shellLayout ?? throw new InvalidOperationException(
        "No shell layout was registered. Add one with AddDefaultShellLayout() or AddShellLayout(...).");

    public AgentTuiShellChrome ShellChrome { get; }

    public Theme? Theme { get; }

    public AgentTuiRunConfigComposer? RunConfigComposer { get; }

    public AgentTuiTranscriptRendererRegistry TranscriptRenderers { get; }

    public IReadOnlyList<AgentTuiContribution<IAgentTuiStatusItem>> StatusItems => _statusItems;

    public IReadOnlyList<AgentTuiContribution<IAgentTuiWidget>> AboveEditorWidgets => _aboveEditorWidgets;

    public IReadOnlyList<AgentTuiContribution<IAgentTuiWidget>> BelowEditorWidgets => _belowEditorWidgets;

    public IReadOnlyList<AgentTuiContribution<IAgentTuiAutocompleteProvider>> AutocompleteProviders => _autocompleteProviders;

    public IReadOnlyList<HpdAgentTuiShortcutDescriptor> Shortcuts => _shortcutList;

    public IReadOnlyList<AgentTuiContribution<IAgentTuiEventHandler>> EventHandlers => _eventHandlers;

    public IReadOnlyList<AgentTuiContribution<IAgentTuiInteractionHandler>> InteractionHandlers => _interactionHandlers;

    public bool TryFindSlashCommand(
        ReadOnlySpan<char> commandLine,
        out HpdAgentTuiCommandDescriptor command,
        out string arguments)
    {
        var trimmed = commandLine.Trim();
        if (trimmed.IsEmpty)
        {
            command = null!;
            arguments = "";
            return false;
        }

        if (trimmed[0] == '/')
        {
            trimmed = trimmed[1..];
        }

        if (trimmed.Trim().IsEmpty && _commands.TryGetValue("help", out command!))
        {
            arguments = "";
            return true;
        }

        var split = trimmed.IndexOf(' ');
        var name = split < 0 ? trimmed : trimmed[..split];
        arguments = split < 0 ? "" : trimmed[(split + 1)..].Trim().ToString();

        if (_commands.TryGetValue(name.ToString(), out command!))
        {
            return true;
        }

        command = null!;
        return false;
    }

    public bool TryFindShortcut(in KeyEvent key, out HpdAgentTuiShortcutDescriptor shortcut)
    {
        foreach (var candidate in _shortcuts.Values)
        {
            if (candidate.Gesture.Matches(in key))
            {
                shortcut = candidate;
                return true;
            }
        }

        shortcut = null!;
        return false;
    }

    public bool TryFindPage(string pageId, out HpdAgentTuiPageDescriptor page)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pageId);
        return _pages.TryGetValue(pageId, out page!);
    }

    public IEnumerable<AgentTuiContribution<IAgentTuiEventHandler>> FindEventHandlers(AgentEvent evt)
    {
        ArgumentNullException.ThrowIfNull(evt);

        foreach (var candidate in _eventHandlers)
        {
            if (candidate.Value.CanHandle(evt))
            {
                yield return candidate;
            }
        }
    }

    public bool TryFindInteractionHandler(
        AgentEvent request,
        out AgentTuiContribution<IAgentTuiInteractionHandler> handler)
    {
        ArgumentNullException.ThrowIfNull(request);

        foreach (var candidate in _interactionHandlers)
        {
            if (candidate.Value.CanHandle(request))
            {
                handler = candidate;
                return true;
            }
        }

        handler = null!;
        return false;
    }
}
