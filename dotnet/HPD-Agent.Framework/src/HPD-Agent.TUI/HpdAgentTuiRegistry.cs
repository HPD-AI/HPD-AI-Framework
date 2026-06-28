using HPD.Agent.TUI.Composition;
using HPD.Agent.TUI.Commands;
using HPD.Agent.TUI.Interactions;
using HPD.TUI.Core;

namespace HPD.Agent.TUI;

public sealed class HpdAgentTuiRegistry
{
    private readonly IReadOnlyDictionary<string, HpdAgentTuiCommandDescriptor> _commands;
    private readonly HpdAgentTuiCommandDescriptor[] _commandList;
    private readonly IReadOnlyList<AgentTuiContribution<HpdAgentTuiCommandDescriptor>> _commandContributions;
    private readonly IReadOnlyList<AgentTuiContribution<IAgentTuiStatusItem>> _statusItems;
    private readonly IReadOnlyList<AgentTuiContribution<IAgentTuiWidget>> _aboveEditorWidgets;
    private readonly IReadOnlyList<AgentTuiContribution<IAgentTuiWidget>> _belowEditorWidgets;
    private readonly IReadOnlyList<AgentTuiContribution<IAgentTuiAutocompleteProvider>> _autocompleteProviders;
    private readonly IReadOnlyDictionary<string, HpdAgentTuiShortcutDescriptor> _shortcuts;
    private readonly HpdAgentTuiShortcutDescriptor[] _shortcutList;
    private readonly IReadOnlyList<AgentTuiContribution<IAgentTuiEventHandler>> _eventHandlers;
    private readonly IReadOnlyList<AgentTuiContribution<IAgentTuiInteractionHandler>> _interactionHandlers;
    private readonly IReadOnlyList<AgentTuiContribution<IAgentTuiRunConfigContributor>> _runConfigContributors;
    private readonly IReadOnlyDictionary<string, HpdAgentTuiPageDescriptor> _pages;
    private readonly HpdAgentTuiPageDescriptor[] _pageList;
    private readonly IReadOnlyList<AgentTuiContribution<HpdAgentTuiPageDescriptor>> _pageContributions;
    private readonly IAgentTuiPromptFactory? _promptFactory;
    private readonly IAgentTuiShellLayout? _shellLayout;

    internal HpdAgentTuiRegistry(AgentTuiContributionStore store)
    {
        ArgumentNullException.ThrowIfNull(store);

        _commands = store.Commands.Values.ToDictionary(command => command.SlashName, StringComparer.OrdinalIgnoreCase);
        _commandList = _commands.Values
            .OrderBy(static command => command.Order)
            .ThenBy(static command => command.SlashName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        _commandContributions = _commandList
            .Select(command => new AgentTuiContribution<HpdAgentTuiCommandDescriptor>(
                command.SlashName,
                command,
                store.GetCommandOwner(command.SlashName),
                command.Order))
            .ToArray();
        _pages = store.Pages.Values.ToDictionary(page => page.Id, StringComparer.OrdinalIgnoreCase);
        _pageList = _pages.Values.ToArray();
        _pageContributions = _pageList
            .Select(page => new AgentTuiContribution<HpdAgentTuiPageDescriptor>(
                page.Id,
                page,
                store.GetPageOwner(page.Id)))
            .ToArray();
        _statusItems = store.StatusItems
            .Select(pair => new AgentTuiContribution<IAgentTuiStatusItem>(
                pair.Key,
                pair.Value,
                store.GetStatusItemOwner(pair.Key)))
            .ToArray();
        _aboveEditorWidgets = store.Widgets
            .Where(pair => pair.Key.Slot == TuiSlot.AboveEditor)
            .Select(pair => new AgentTuiContribution<IAgentTuiWidget>(
                pair.Key.Key,
                pair.Value,
                store.GetWidgetOwner(pair.Key)))
            .ToArray();
        _belowEditorWidgets = store.Widgets
            .Where(pair => pair.Key.Slot == TuiSlot.BelowEditor)
            .Select(pair => new AgentTuiContribution<IAgentTuiWidget>(
                pair.Key.Key,
                pair.Value,
                store.GetWidgetOwner(pair.Key)))
            .ToArray();
        var providerContributions = store.AutocompleteProviders
            .Select(pair => new AgentTuiContribution<IAgentTuiAutocompleteProvider>(
                pair.Key,
                pair.Value,
                store.GetAutocompleteProviderOwner(pair.Key)))
            .ToList();

        if (store.IncludeSlashCommandAutocomplete)
        {
            providerContributions.Insert(0, new AgentTuiContribution<IAgentTuiAutocompleteProvider>(
                "hpd.slash-commands",
                new SlashCommandAgentAutocompleteProvider(this),
                HpdContributionOwner.Framework));
        }

        _autocompleteProviders = providerContributions.ToArray();
        _shortcuts = store.Shortcuts.Values.ToDictionary(shortcut => shortcut.Key, StringComparer.Ordinal);
        _shortcutList = _shortcuts.Values.ToArray();
        _eventHandlers = store.EventHandlers
            .Select(pair => new AgentTuiContribution<IAgentTuiEventHandler>(
                pair.Key,
                pair.Value,
                store.GetEventHandlerOwner(pair.Key)))
            .ToArray();
        _interactionHandlers = store.InteractionHandlers
            .Select(pair => new AgentTuiContribution<IAgentTuiInteractionHandler>(
                pair.Key,
                pair.Value,
                store.GetInteractionHandlerOwner(pair.Key)))
            .ToArray();
        _runConfigContributors = store.RunConfigContributors
            .Select(pair => new AgentTuiContribution<IAgentTuiRunConfigContributor>(
                pair.Key,
                pair.Value,
                store.GetRunConfigContributorOwner(pair.Key)))
            .ToArray();
        TranscriptRenderers = new AgentTuiTranscriptRendererRegistry(store.TranscriptRenderers.Values);
        Header = store.Header;
        Footer = store.Footer;
        _promptFactory = store.PromptFactory;
        _shellLayout = store.ShellLayout;
        ShellChrome = store.ShellChrome.Clone();
        Theme = store.Theme;
    }

    public IReadOnlyList<HpdAgentTuiCommandDescriptor> Commands => _commandList;

    public IReadOnlyList<AgentTuiContribution<HpdAgentTuiCommandDescriptor>> CommandContributions => _commandContributions;

    public IReadOnlyList<HpdAgentTuiPageDescriptor> Pages => _pageList;

    public IReadOnlyList<AgentTuiContribution<HpdAgentTuiPageDescriptor>> PageContributions => _pageContributions;

    public string? DefaultPageId => _pages.Count > 0 ? _pages.Values.First().Id : null;

    public IAgentTuiShellComponent? Header { get; }

    public IAgentTuiShellComponent? Footer { get; }

    public IAgentTuiPromptFactory PromptFactory => _promptFactory ?? throw new InvalidOperationException(
        "No prompt factory was registered. Add one with AddDefaultPrompt() or AddPrompt(...).");

    public IAgentTuiShellLayout ShellLayout => _shellLayout ?? throw new InvalidOperationException(
        "No shell layout was registered. Add one with AddDefaultShellLayout() or AddShellLayout(...).");

    public AgentTuiShellChrome ShellChrome { get; }

    public Theme? Theme { get; }

    public AgentTuiTranscriptRendererRegistry TranscriptRenderers { get; }

    public IReadOnlyList<AgentTuiContribution<IAgentTuiStatusItem>> StatusItems => _statusItems;

    public IReadOnlyList<AgentTuiContribution<IAgentTuiWidget>> AboveEditorWidgets => _aboveEditorWidgets;

    public IReadOnlyList<AgentTuiContribution<IAgentTuiWidget>> BelowEditorWidgets => _belowEditorWidgets;

    public IReadOnlyList<AgentTuiContribution<IAgentTuiAutocompleteProvider>> AutocompleteProviders => _autocompleteProviders;

    public IReadOnlyList<HpdAgentTuiShortcutDescriptor> Shortcuts => _shortcutList;

    public IReadOnlyList<AgentTuiContribution<IAgentTuiEventHandler>> EventHandlers => _eventHandlers;

    public IReadOnlyList<AgentTuiContribution<IAgentTuiInteractionHandler>> InteractionHandlers => _interactionHandlers;

    public IReadOnlyList<AgentTuiContribution<IAgentTuiRunConfigContributor>> RunConfigContributors => _runConfigContributors;

    public bool TryFindSlashCommand(
        ReadOnlySpan<char> commandLine,
        out HpdAgentTuiCommandDescriptor command,
        out string arguments)
    {
        if (TryFindSlashCommandContribution(commandLine, out var contribution, out arguments))
        {
            command = contribution.Value;
            return true;
        }

        command = null!;
        return false;
    }

    public bool TryFindSlashCommandContribution(
        ReadOnlySpan<char> commandLine,
        out AgentTuiContribution<HpdAgentTuiCommandDescriptor> contribution,
        out string arguments)
    {
        var trimmed = commandLine.Trim();
        if (trimmed.IsEmpty)
        {
            contribution = null!;
            arguments = "";
            return false;
        }

        if (trimmed[0] == '/')
        {
            trimmed = trimmed[1..];
        }

        if (trimmed.Trim().IsEmpty && _commands.TryGetValue("help", out var helpCommand))
        {
            contribution = FindCommandContribution(helpCommand.SlashName);
            arguments = "";
            return true;
        }

        var split = trimmed.IndexOf(' ');
        var name = split < 0 ? trimmed : trimmed[..split];
        arguments = split < 0 ? "" : trimmed[(split + 1)..].Trim().ToString();

        if (_commands.TryGetValue(name.ToString(), out var command))
        {
            contribution = FindCommandContribution(command.SlashName);
            return true;
        }

        contribution = null!;
        return false;
    }

    private AgentTuiContribution<HpdAgentTuiCommandDescriptor> FindCommandContribution(string slashName)
        => _commandContributions.First(contribution =>
            string.Equals(contribution.Key, slashName, StringComparison.OrdinalIgnoreCase));

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
