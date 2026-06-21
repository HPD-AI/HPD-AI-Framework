using HPD.Agent.TUI.Composition;
using HPD.Agent.TUI.Commands;
using HPD.Agent.TUI.Interactions;
using HPD.Agent.TUI.Models;
using HPD.Agent.TUI.Views;
using HPD.TUI.Components;
using HPD.TUI.Controllers;
using HPD.TUI.Core;
using HPD.TUI.Views;

namespace HPD.Agent.TUI;

public sealed class HpdAgentTuiBuilder
{
    private readonly Dictionary<string, HpdAgentTuiCommandDescriptor> _commands = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, HpdAgentTuiPageDescriptor> _pages = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IAgentTuiStatusItem> _statusItems = new(StringComparer.Ordinal);
    private readonly Dictionary<(TuiSlot Slot, string Key), IAgentTuiWidget> _widgets = [];
    private readonly Dictionary<string, IAgentTuiAutocompleteProvider> _autocompleteProviders = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HpdAgentTuiShortcutDescriptor> _shortcuts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IAgentTuiEventHandler> _eventHandlers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IAgentTuiInteractionHandler> _interactionHandlers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IAgentTuiTranscriptRendererAdapter> _transcriptRenderers = new(StringComparer.Ordinal);
    private readonly HashSet<KeyGesture> _shortcutGestures = [];
    private IAgentTuiShellComponent? _header;
    private IAgentTuiShellComponent? _footer;
    private IAgentTuiPromptFactory? _promptFactory;
    private IAgentTuiShellLayout? _shellLayout;
    private AgentTuiShellChrome _shellChrome = new();
    private Theme? _theme;
    private bool _includeSlashCommandAutocomplete;
    private AgentTuiRunConfigComposer? _runConfigComposer;

    public HpdAgentTuiBuilder AddAgentTuiDefaults()
        => AddDefaultShell()
            .AddDefaultPrompt()
            .AddDefaultTranscriptRenderers()
            .AddDefaultCommandSupport()
            .AddDefaultShellCommands();

    public HpdAgentTuiBuilder AddDefaultShell()
        => AddDefaultHeader()
            .AddDefaultFooter()
            .AddDefaultShellLayout();

    public HpdAgentTuiBuilder AddDefaultHeader()
    {
        TryAddHeader(new DefaultHeaderShellComponent());
        return this;
    }

    public HpdAgentTuiBuilder AddDefaultFooter()
    {
        TryAddFooter(new DefaultFooterShellComponent());
        return this;
    }

    public HpdAgentTuiBuilder AddDefaultPrompt()
    {
        TryAddPrompt(new DefaultAgentTuiPromptFactory());
        return this;
    }

    public HpdAgentTuiBuilder AddDefaultShellLayout()
    {
        TryAddShellLayout(new DefaultAgentTuiShellLayout());
        return this;
    }

    public HpdAgentTuiBuilder AddDefaultCommandSupport()
        => AddSlashCommandAutocomplete();

    public HpdAgentTuiBuilder AddSlashCommandAutocomplete()
    {
        _includeSlashCommandAutocomplete = true;
        return this;
    }

    public HpdAgentTuiBuilder AddDefaultShellCommands()
    {
        TryAddPage(new HpdAgentTuiPageDescriptor("hpd.help", _ =>
        {
            var commands = string.Join("\n", _commands.Values
                .Where(static command => !command.Hidden)
                .OrderBy(static command => command.SlashName, StringComparer.OrdinalIgnoreCase)
                .Select(static command =>
                {
                    var description = string.IsNullOrWhiteSpace(command.Description)
                        ? command.Title
                        : command.Description;
                    return $"- `/{command.SlashName}` {description}";
                }));

            return new Markdown($"**Commands**\n\n{commands}");
        })
        {
            Title = "Commands",
            Description = "Available shell commands.",
            Hidden = true
        });

        TryAddSlashCommand(new HpdAgentTuiCommandDescriptor("help", context =>
        {
            context.Navigation.GoToPage("hpd.help");
        })
        {
            Title = "/help",
            Description = "Show available shell commands."
        });
        TryAddSlashCommand(new HpdAgentTuiCommandDescriptor("clear", context => context.Shell.Transcript.Clear())
        {
            Title = "/clear",
            Description = "Clear the transcript."
        });
        return this;
    }

    public HpdAgentTuiBuilder AddDefaultTranscriptRenderers()
        => TryAddTranscriptRenderer(
                AgentTuiTranscriptRendererKeys.UserMessage,
                new UserMessageCellRenderer())
            .TryAddTranscriptRenderer(
                AgentTuiTranscriptRendererKeys.AssistantMessage,
                new AssistantMessageCellRenderer())
            .TryAddTranscriptRenderer(
                AgentTuiTranscriptRendererKeys.ReasoningMessage,
                new ReasoningMessageCellRenderer())
            .TryAddTranscriptRenderer(
                AgentTuiTranscriptRendererKeys.Notice,
                new NoticeCellRenderer())
            .TryAddTranscriptRenderer(
                AgentTuiTranscriptRendererKeys.RunStatus,
                new RunStatusCellRenderer())
            .TryAddTranscriptRenderer(
                AgentTuiTranscriptRendererKeys.ToolCall,
                new ToolCallCellRenderer())
            .TryAddTranscriptRenderer(
                AgentTuiTranscriptRendererKeys.CustomComponent,
                new CustomComponentCellRenderer());

    public HpdAgentTuiBuilder AddTranscriptRenderer<TCell>(
        string key,
        IAgentTuiTranscriptRenderer<TCell> renderer)
        where TCell : TranscriptCell
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(renderer);
        if (_transcriptRenderers.ContainsKey(key))
        {
            throw new InvalidOperationException($"A transcript renderer is already registered for '{key}'.");
        }

        if (TryFindTranscriptRendererKeyForCellType(typeof(TCell), out var existingKey))
        {
            throw new InvalidOperationException(
                $"A transcript renderer is already registered for cell type '{typeof(TCell).Name}' with key '{existingKey}'.");
        }

        _transcriptRenderers[key] = new AgentTuiTranscriptRendererAdapter<TCell>(key, renderer);
        return this;
    }

    public HpdAgentTuiBuilder AddTranscriptRenderer<TCell>(
        string key,
        Func<AgentTuiTranscriptRenderContext<TCell>, IComponent> create)
        where TCell : TranscriptCell
        => AddTranscriptRenderer(key, new DelegateAgentTuiTranscriptRenderer<TCell>(create));

    public HpdAgentTuiBuilder TryAddTranscriptRenderer<TCell>(
        string key,
        IAgentTuiTranscriptRenderer<TCell> renderer)
        where TCell : TranscriptCell
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(renderer);
        if (_transcriptRenderers.ContainsKey(key) ||
            TryFindTranscriptRendererKeyForCellType(typeof(TCell), out _))
        {
            return this;
        }

        _transcriptRenderers[key] = new AgentTuiTranscriptRendererAdapter<TCell>(key, renderer);
        return this;
    }

    public HpdAgentTuiBuilder TryAddTranscriptRenderer<TCell>(
        string key,
        Func<AgentTuiTranscriptRenderContext<TCell>, IComponent> create)
        where TCell : TranscriptCell
        => TryAddTranscriptRenderer(key, new DelegateAgentTuiTranscriptRenderer<TCell>(create));

    public HpdAgentTuiBuilder ReplaceTranscriptRenderer<TCell>(
        string key,
        IAgentTuiTranscriptRenderer<TCell> renderer)
        where TCell : TranscriptCell
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(renderer);
        if (!_transcriptRenderers.TryGetValue(key, out var existing))
        {
            throw new InvalidOperationException($"Cannot replace transcript renderer '{key}' because none is registered.");
        }

        if (existing.CellType != typeof(TCell))
        {
            throw new InvalidOperationException(
                $"Cannot replace transcript renderer '{key}' for '{existing.CellType.Name}' with renderer for '{typeof(TCell).Name}'.");
        }

        _transcriptRenderers[key] = new AgentTuiTranscriptRendererAdapter<TCell>(key, renderer);
        return this;
    }

    public HpdAgentTuiBuilder ReplaceTranscriptRenderer<TCell>(
        string key,
        Func<AgentTuiTranscriptRenderContext<TCell>, IComponent> create)
        where TCell : TranscriptCell
        => ReplaceTranscriptRenderer(key, new DelegateAgentTuiTranscriptRenderer<TCell>(create));

    public HpdAgentTuiBuilder DecorateTranscriptRenderer<TCell>(
        string key,
        Func<IAgentTuiTranscriptRenderer<TCell>, IAgentTuiTranscriptRenderer<TCell>> decorate)
        where TCell : TranscriptCell
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(decorate);
        if (!_transcriptRenderers.TryGetValue(key, out var existing))
        {
            throw new InvalidOperationException($"Cannot decorate transcript renderer '{key}' because none is registered.");
        }

        if (existing is not AgentTuiTranscriptRendererAdapter<TCell> typed)
        {
            throw new InvalidOperationException(
                $"Cannot decorate transcript renderer '{key}' for '{existing.CellType.Name}' as '{typeof(TCell).Name}'.");
        }

        var decorated = decorate(typed.Renderer)
            ?? throw new InvalidOperationException("Transcript renderer decorator returned null.");
        _transcriptRenderers[key] = new AgentTuiTranscriptRendererAdapter<TCell>(key, decorated);
        return this;
    }

    public HpdAgentTuiBuilder AddPage(HpdAgentTuiPageDescriptor page)
    {
        ArgumentNullException.ThrowIfNull(page);
        if (!_pages.TryAdd(page.Id, page))
        {
            throw new InvalidOperationException($"A page is already registered for '{page.Id}'.");
        }

        return this;
    }

    public HpdAgentTuiBuilder AddPage(
        string id,
        Func<AgentTuiPageContext, IComponent> render)
        => AddPage(new HpdAgentTuiPageDescriptor(id, render));

    public HpdAgentTuiBuilder TryAddPage(HpdAgentTuiPageDescriptor page)
    {
        ArgumentNullException.ThrowIfNull(page);
        _pages.TryAdd(page.Id, page);
        return this;
    }

    public HpdAgentTuiBuilder TryAddPage(
        string id,
        Func<AgentTuiPageContext, IComponent> render)
        => TryAddPage(new HpdAgentTuiPageDescriptor(id, render));

    public HpdAgentTuiBuilder ReplacePage(HpdAgentTuiPageDescriptor page)
    {
        ArgumentNullException.ThrowIfNull(page);
        if (!_pages.ContainsKey(page.Id))
        {
            throw new InvalidOperationException($"Cannot replace page '{page.Id}' because none is registered.");
        }

        _pages[page.Id] = page;
        return this;
    }

    public HpdAgentTuiBuilder ReplacePage(
        string id,
        Func<AgentTuiPageContext, IComponent> render)
        => ReplacePage(new HpdAgentTuiPageDescriptor(id, render));

    public HpdAgentTuiBuilder AddHeader(IAgentTuiShellComponent header)
    {
        ArgumentNullException.ThrowIfNull(header);
        if (_header is not null)
        {
            throw new InvalidOperationException("A header contribution is already registered.");
        }

        _header = header;
        return this;
    }

    public HpdAgentTuiBuilder AddHeader(Func<AgentTuiShellContext, IComponent> render)
        => AddHeader(new DelegateAgentTuiShellComponent(render));

    public HpdAgentTuiBuilder TryAddHeader(IAgentTuiShellComponent header)
    {
        ArgumentNullException.ThrowIfNull(header);
        _header ??= header;
        return this;
    }

    public HpdAgentTuiBuilder ReplaceHeader(IAgentTuiShellComponent header)
    {
        ArgumentNullException.ThrowIfNull(header);
        if (_header is null)
        {
            throw new InvalidOperationException("Cannot replace header because none is registered.");
        }

        _header = header;
        return this;
    }

    public HpdAgentTuiBuilder ReplaceHeader(Func<AgentTuiShellContext, IComponent> render)
        => ReplaceHeader(new DelegateAgentTuiShellComponent(render));

    public HpdAgentTuiBuilder DecorateHeader(
        Func<IAgentTuiShellComponent, IAgentTuiShellComponent> decorate)
    {
        ArgumentNullException.ThrowIfNull(decorate);
        if (_header is null)
        {
            throw new InvalidOperationException("Cannot decorate header because none is registered.");
        }

        _header = decorate(_header) ?? throw new InvalidOperationException("Header decorator returned null.");
        return this;
    }

    public HpdAgentTuiBuilder AddFooter(IAgentTuiShellComponent footer)
    {
        ArgumentNullException.ThrowIfNull(footer);
        if (_footer is not null)
        {
            throw new InvalidOperationException("A footer contribution is already registered.");
        }

        _footer = footer;
        return this;
    }

    public HpdAgentTuiBuilder AddFooter(Func<AgentTuiShellContext, IComponent> render)
        => AddFooter(new DelegateAgentTuiShellComponent(render));

    public HpdAgentTuiBuilder TryAddFooter(IAgentTuiShellComponent footer)
    {
        ArgumentNullException.ThrowIfNull(footer);
        _footer ??= footer;
        return this;
    }

    public HpdAgentTuiBuilder ReplaceFooter(IAgentTuiShellComponent footer)
    {
        ArgumentNullException.ThrowIfNull(footer);
        if (_footer is null)
        {
            throw new InvalidOperationException("Cannot replace footer because none is registered.");
        }

        _footer = footer;
        return this;
    }

    public HpdAgentTuiBuilder ReplaceFooter(Func<AgentTuiShellContext, IComponent> render)
        => ReplaceFooter(new DelegateAgentTuiShellComponent(render));

    public HpdAgentTuiBuilder DecorateFooter(
        Func<IAgentTuiShellComponent, IAgentTuiShellComponent> decorate)
    {
        ArgumentNullException.ThrowIfNull(decorate);
        if (_footer is null)
        {
            throw new InvalidOperationException("Cannot decorate footer because none is registered.");
        }

        _footer = decorate(_footer) ?? throw new InvalidOperationException("Footer decorator returned null.");
        return this;
    }

    public HpdAgentTuiBuilder UseTheme(Theme theme)
    {
        _theme = theme ?? throw new ArgumentNullException(nameof(theme));
        return this;
    }

    public HpdAgentTuiBuilder ConfigureShellChrome(Action<AgentTuiShellChrome> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(_shellChrome);
        return this;
    }

    public HpdAgentTuiBuilder SetRunConfigComposer(AgentTuiRunConfigComposer composer)
    {
        _runConfigComposer = composer ?? throw new ArgumentNullException(nameof(composer));
        return this;
    }

    public HpdAgentTuiBuilder ClearRunConfigComposer()
    {
        _runConfigComposer = null;
        return this;
    }

    public HpdAgentTuiBuilder UseModelSelectionRunConfig(
        AgentTuiModelSelectionState selection)
    {
        ArgumentNullException.ThrowIfNull(selection);
        return SetRunConfigComposer(_ => selection.ToRunConfig());
    }

    public HpdAgentTuiBuilder AddModelSelectionCommand(
        IAgentTuiModelCatalog catalog,
        AgentTuiModelSelectionState selection,
        string commandName = "model",
        Action<AgentTuiModelSelectionOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(selection);
        var options = new AgentTuiModelSelectionOptions();
        configure?.Invoke(options);
        return AddSlashCommand(ModelSelectionCommand.Create(catalog, selection, commandName, options));
    }

    public HpdAgentTuiBuilder AddModelSelection(
        IAgentTuiModelCatalog catalog,
        AgentTuiModelSelectionState? selection = null,
        string commandName = "model",
        Action<AgentTuiModelSelectionOptions>? configure = null)
    {
        selection ??= new AgentTuiModelSelectionState();
        return AddModelSelectionCommand(catalog, selection, commandName, configure)
            .UseModelSelectionRunConfig(selection);
    }

    public HpdAgentTuiBuilder AddShellLayout(IAgentTuiShellLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        if (_shellLayout is not null)
        {
            throw new InvalidOperationException("A shell layout is already registered.");
        }

        _shellLayout = layout;
        return this;
    }

    public HpdAgentTuiBuilder AddShellLayout<TLayout>()
        where TLayout : IAgentTuiShellLayout, new()
        => AddShellLayout(new TLayout());

    public HpdAgentTuiBuilder TryAddShellLayout(IAgentTuiShellLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        _shellLayout ??= layout;
        return this;
    }

    public HpdAgentTuiBuilder TryAddShellLayout<TLayout>()
        where TLayout : IAgentTuiShellLayout, new()
        => TryAddShellLayout(new TLayout());

    public HpdAgentTuiBuilder ReplaceShellLayout(IAgentTuiShellLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        if (_shellLayout is null)
        {
            throw new InvalidOperationException("Cannot replace shell layout because none is registered.");
        }

        _shellLayout = layout;
        return this;
    }

    public HpdAgentTuiBuilder ReplaceShellLayout<TLayout>()
        where TLayout : IAgentTuiShellLayout, new()
        => ReplaceShellLayout(new TLayout());

    public HpdAgentTuiBuilder AddPrompt(IAgentTuiPromptFactory promptFactory)
    {
        ArgumentNullException.ThrowIfNull(promptFactory);
        if (_promptFactory is not null)
        {
            throw new InvalidOperationException("A prompt contribution is already registered.");
        }

        _promptFactory = promptFactory;
        return this;
    }

    public HpdAgentTuiBuilder AddPrompt(
        Func<AgentTuiPromptContext, Action<ReadOnlyMemory<char>>, AutocompleteController, PromptView> create)
        => AddPrompt(new DelegateAgentTuiPromptFactory(create));

    public HpdAgentTuiBuilder TryAddPrompt(IAgentTuiPromptFactory promptFactory)
    {
        ArgumentNullException.ThrowIfNull(promptFactory);
        _promptFactory ??= promptFactory;
        return this;
    }

    public HpdAgentTuiBuilder ReplacePrompt(IAgentTuiPromptFactory promptFactory)
    {
        ArgumentNullException.ThrowIfNull(promptFactory);
        if (_promptFactory is null)
        {
            throw new InvalidOperationException("Cannot replace prompt because none is registered.");
        }

        _promptFactory = promptFactory;
        return this;
    }

    public HpdAgentTuiBuilder ReplacePrompt(
        Func<AgentTuiPromptContext, Action<ReadOnlyMemory<char>>, AutocompleteController, PromptView> create)
        => ReplacePrompt(new DelegateAgentTuiPromptFactory(create));

    public HpdAgentTuiBuilder AddEventHandler(string key, IAgentTuiEventHandler handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(handler);
        if (!_eventHandlers.TryAdd(key, handler))
        {
            throw new InvalidOperationException($"An event handler is already registered for '{key}'.");
        }

        return this;
    }

    public HpdAgentTuiBuilder AddEventHandler<TEvent>(
        string key,
        AgentTuiEventHandler<TEvent> handler)
        where TEvent : AgentEvent
        => AddEventHandler(key, (IAgentTuiEventHandler)handler);

    public HpdAgentTuiBuilder AddEventHandler<TEvent, THandler>(string key)
        where TEvent : AgentEvent
        where THandler : AgentTuiEventHandler<TEvent>, new()
        => AddEventHandler<TEvent>(key, new THandler());

    public HpdAgentTuiBuilder TryAddEventHandler(string key, IAgentTuiEventHandler handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(handler);
        _eventHandlers.TryAdd(key, handler);
        return this;
    }

    public HpdAgentTuiBuilder TryAddEventHandler<TEvent>(
        string key,
        AgentTuiEventHandler<TEvent> handler)
        where TEvent : AgentEvent
        => TryAddEventHandler(key, (IAgentTuiEventHandler)handler);

    public HpdAgentTuiBuilder TryAddEventHandler<TEvent, THandler>(string key)
        where TEvent : AgentEvent
        where THandler : AgentTuiEventHandler<TEvent>, new()
        => TryAddEventHandler<TEvent>(key, new THandler());

    public HpdAgentTuiBuilder ReplaceEventHandler(string key, IAgentTuiEventHandler handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(handler);
        if (!_eventHandlers.ContainsKey(key))
        {
            throw new InvalidOperationException($"Cannot replace event handler '{key}' because none is registered.");
        }

        _eventHandlers[key] = handler;
        return this;
    }

    public HpdAgentTuiBuilder ReplaceEventHandler<TEvent>(
        string key,
        AgentTuiEventHandler<TEvent> handler)
        where TEvent : AgentEvent
        => ReplaceEventHandler(key, (IAgentTuiEventHandler)handler);

    public HpdAgentTuiBuilder ReplaceEventHandler<TEvent, THandler>(string key)
        where TEvent : AgentEvent
        where THandler : AgentTuiEventHandler<TEvent>, new()
        => ReplaceEventHandler<TEvent>(key, new THandler());

    public HpdAgentTuiBuilder AddSlashCommand(
        HpdAgentTuiCommandDescriptor command)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!_commands.TryAdd(command.SlashName, command))
        {
            throw new InvalidOperationException(
                $"A slash command is already registered for /{command.SlashName}.");
        }

        return this;
    }

    public HpdAgentTuiBuilder TryAddSlashCommand(
        HpdAgentTuiCommandDescriptor command)
    {
        ArgumentNullException.ThrowIfNull(command);
        _commands.TryAdd(command.SlashName, command);
        return this;
    }

    public HpdAgentTuiBuilder ReplaceSlashCommand(
        HpdAgentTuiCommandDescriptor command)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!_commands.ContainsKey(command.SlashName))
        {
            throw new InvalidOperationException(
                $"Cannot replace slash command /{command.SlashName} because none is registered.");
        }

        _commands[command.SlashName] = command;
        return this;
    }

    public HpdAgentTuiBuilder AddStatusItem(string key, IAgentTuiStatusItem item)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(item);
        if (!_statusItems.TryAdd(key, item))
        {
            throw new InvalidOperationException($"A status item is already registered for '{key}'.");
        }

        return this;
    }

    public HpdAgentTuiBuilder TryAddStatusItem(string key, IAgentTuiStatusItem item)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(item);
        _statusItems.TryAdd(key, item);
        return this;
    }

    public HpdAgentTuiBuilder ReplaceStatusItem(string key, IAgentTuiStatusItem item)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(item);
        if (!_statusItems.ContainsKey(key))
        {
            throw new InvalidOperationException($"Cannot replace status item '{key}' because none is registered.");
        }

        _statusItems[key] = item;
        return this;
    }

    public HpdAgentTuiBuilder AddWidget(TuiSlot slot, string key, IAgentTuiWidget widget)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(widget);
        if (!_widgets.TryAdd((slot, key), widget))
        {
            throw new InvalidOperationException($"A widget is already registered for {slot} slot key '{key}'.");
        }

        return this;
    }

    public HpdAgentTuiBuilder TryAddWidget(TuiSlot slot, string key, IAgentTuiWidget widget)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(widget);
        _widgets.TryAdd((slot, key), widget);
        return this;
    }

    public HpdAgentTuiBuilder ReplaceWidget(TuiSlot slot, string key, IAgentTuiWidget widget)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(widget);
        if (!_widgets.ContainsKey((slot, key)))
        {
            throw new InvalidOperationException($"Cannot replace widget for {slot} slot key '{key}' because none is registered.");
        }

        _widgets[(slot, key)] = widget;
        return this;
    }

    public HpdAgentTuiBuilder RemoveWidget(TuiSlot slot, string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        _widgets.Remove((slot, key));
        return this;
    }

    public HpdAgentTuiBuilder AddAutocompleteProvider(string key, IAgentTuiAutocompleteProvider provider)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(provider);
        if (!_autocompleteProviders.TryAdd(key, provider))
        {
            throw new InvalidOperationException($"An autocomplete provider is already registered for '{key}'.");
        }

        return this;
    }

    public HpdAgentTuiBuilder TryAddAutocompleteProvider(string key, IAgentTuiAutocompleteProvider provider)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(provider);
        _autocompleteProviders.TryAdd(key, provider);
        return this;
    }

    public HpdAgentTuiBuilder ReplaceAutocompleteProvider(string key, IAgentTuiAutocompleteProvider provider)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(provider);
        if (!_autocompleteProviders.ContainsKey(key))
        {
            throw new InvalidOperationException($"Cannot replace autocomplete provider '{key}' because none is registered.");
        }

        _autocompleteProviders[key] = provider;
        return this;
    }

    public HpdAgentTuiBuilder AddShortcut(HpdAgentTuiShortcutDescriptor shortcut)
    {
        ArgumentNullException.ThrowIfNull(shortcut);
        if (_shortcuts.ContainsKey(shortcut.Key))
        {
            throw new InvalidOperationException($"A shortcut is already registered for '{shortcut.Key}'.");
        }

        if (!_shortcutGestures.Add(shortcut.Gesture))
        {
            throw new InvalidOperationException($"A shortcut is already registered for gesture {shortcut.Gesture}.");
        }

        _shortcuts[shortcut.Key] = shortcut;
        return this;
    }

    public HpdAgentTuiBuilder TryAddShortcut(HpdAgentTuiShortcutDescriptor shortcut)
    {
        ArgumentNullException.ThrowIfNull(shortcut);
        if (_shortcuts.ContainsKey(shortcut.Key) || _shortcutGestures.Contains(shortcut.Gesture))
        {
            return this;
        }

        _shortcuts[shortcut.Key] = shortcut;
        _shortcutGestures.Add(shortcut.Gesture);
        return this;
    }

    public HpdAgentTuiBuilder ReplaceShortcut(HpdAgentTuiShortcutDescriptor shortcut)
    {
        ArgumentNullException.ThrowIfNull(shortcut);
        if (!_shortcuts.TryGetValue(shortcut.Key, out var existing))
        {
            throw new InvalidOperationException($"Cannot replace shortcut '{shortcut.Key}' because none is registered.");
        }

        if (!existing.Gesture.Equals(shortcut.Gesture) && _shortcutGestures.Contains(shortcut.Gesture))
        {
            throw new InvalidOperationException($"A shortcut is already registered for gesture {shortcut.Gesture}.");
        }

        _shortcutGestures.Remove(existing.Gesture);
        _shortcutGestures.Add(shortcut.Gesture);
        _shortcuts[shortcut.Key] = shortcut;
        return this;
    }

    public HpdAgentTuiBuilder AddInteractionHandler(string key, IAgentTuiInteractionHandler handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(handler);
        if (!_interactionHandlers.TryAdd(key, handler))
        {
            throw new InvalidOperationException($"An interaction handler is already registered for '{key}'.");
        }

        return this;
    }

    public HpdAgentTuiBuilder AddInteractionHandler<TRequest>(
        string key,
        AgentTuiInteractionHandler<TRequest> handler)
        where TRequest : AgentEvent
        => AddInteractionHandler(key, (IAgentTuiInteractionHandler)handler);

    public HpdAgentTuiBuilder AddInteractionHandler<TRequest, THandler>(string key)
        where TRequest : AgentEvent
        where THandler : AgentTuiInteractionHandler<TRequest>, new()
        => AddInteractionHandler<TRequest>(key, new THandler());

    public HpdAgentTuiBuilder TryAddInteractionHandler(string key, IAgentTuiInteractionHandler handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(handler);
        _interactionHandlers.TryAdd(key, handler);
        return this;
    }

    public HpdAgentTuiBuilder TryAddInteractionHandler<TRequest>(
        string key,
        AgentTuiInteractionHandler<TRequest> handler)
        where TRequest : AgentEvent
        => TryAddInteractionHandler(key, (IAgentTuiInteractionHandler)handler);

    public HpdAgentTuiBuilder TryAddInteractionHandler<TRequest, THandler>(string key)
        where TRequest : AgentEvent
        where THandler : AgentTuiInteractionHandler<TRequest>, new()
        => TryAddInteractionHandler<TRequest>(key, new THandler());

    public HpdAgentTuiBuilder ReplaceInteractionHandler(string key, IAgentTuiInteractionHandler handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(handler);
        if (!_interactionHandlers.ContainsKey(key))
        {
            throw new InvalidOperationException($"Cannot replace interaction handler '{key}' because none is registered.");
        }

        _interactionHandlers[key] = handler;
        return this;
    }

    public HpdAgentTuiBuilder ReplaceInteractionHandler<TRequest>(
        string key,
        AgentTuiInteractionHandler<TRequest> handler)
        where TRequest : AgentEvent
        => ReplaceInteractionHandler(key, (IAgentTuiInteractionHandler)handler);

    public HpdAgentTuiBuilder ReplaceInteractionHandler<TRequest, THandler>(string key)
        where TRequest : AgentEvent
        where THandler : AgentTuiInteractionHandler<TRequest>, new()
        => ReplaceInteractionHandler<TRequest>(key, new THandler());

    private bool TryFindTranscriptRendererKeyForCellType(Type cellType, out string key)
    {
        foreach (var pair in _transcriptRenderers)
        {
            if (pair.Value.CellType == cellType)
            {
                key = pair.Key;
                return true;
            }
        }

        key = "";
        return false;
    }

    public HpdAgentTuiRegistry Build()
        => new(
            _commands.Values,
            _pages.Values,
            _statusItems,
            _widgets,
            _autocompleteProviders,
            _shortcuts.Values,
            _eventHandlers,
            _interactionHandlers,
            _transcriptRenderers.Values,
            _header,
            _footer,
            _promptFactory,
            _shellLayout,
            _shellChrome,
            _theme,
            _includeSlashCommandAutocomplete,
            _runConfigComposer);
}
