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
    private readonly Dictionary<string, IAgentTuiFooterItem> _footerItems = new(StringComparer.Ordinal);
    private readonly Dictionary<(TuiSlot Slot, string Key), IAgentTuiWidget> _widgets = [];
    private readonly Dictionary<string, IAgentTuiAutocompleteProvider> _autocompleteProviders = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HpdAgentTuiShortcutDescriptor> _shortcuts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, AgentTuiEventHandlerRegistration> _eventHandlers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, AgentTuiInteractionHandlerRegistration> _interactionHandlers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IAgentTuiTranscriptRendererAdapter> _transcriptRenderers = new(StringComparer.Ordinal);
    private readonly PermissionPresentationRendererRegistry _permissionPresentationRenderers = new();
    private readonly HashSet<KeyGesture> _shortcutGestures = [];
    private IAgentTuiShellComponent? _header;
    private IAgentTuiShellComponent? _promptStatus;
    private IAgentTuiShellComponent? _footer;
    private IAgentTuiPromptFactory? _promptFactory;
    private IAgentTuiShellLayout? _shellLayout;
    private AgentTuiShellChrome _shellChrome = new();
    private Theme? _theme;
    private bool _includeSlashCommandAutocomplete;
    private AgentTuiRunConfigComposer? _runConfigComposer;
    private IAgentTuiThreadStateReconciler? _threadStateReconciler;
    private TranscriptHistoryPresentation _transcriptHistoryPresentation;
    private bool _showReasoning = true;

    /// <summary>Controls whether reasoning events are projected into the transcript.</summary>
    public HpdAgentTuiBuilder ShowReasoning(bool show = true)
    {
        _showReasoning = show;
        return this;
    }

    public HpdAgentTuiBuilder UseTranscriptHistoryPresentation(
        TranscriptHistoryPresentation presentation)
    {
        if (!Enum.IsDefined(presentation))
            throw new ArgumentOutOfRangeException(nameof(presentation));

        _transcriptHistoryPresentation = presentation;
        return this;
    }

    public HpdAgentTuiBuilder AddAgentTuiDefaults()
        => AddDefaultShell()
            .AddDefaultPrompt()
            .AddDefaultTranscriptRenderers()
            .AddDefaultCommandSupport()
            .AddDefaultShellCommands()
            .AddDefaultPermissionInteraction();

    /// <summary>Adds the standard permission request handler and its typed renderer dispatch.</summary>
    public HpdAgentTuiBuilder AddDefaultPermissionInteraction()
    {
        TryAddInteractionHandler<PermissionRequestEvent>(
            "hpd.permission",
            new PermissionRequestInteractionHandler(_permissionPresentationRenderers));
        return this;
    }

    /// <summary>Adds one exact typed permission-presentation renderer.</summary>
    public HpdAgentTuiBuilder AddPermissionPresentationRenderer<TPresentation>(
        string presentationId,
        IPermissionPresentationRenderer<TPresentation> renderer)
    {
        _permissionPresentationRenderers.Add(presentationId, renderer);
        return this;
    }

    public HpdAgentTuiBuilder AddDefaultShell()
        => AddDefaultHeader()
            .AddDefaultPromptStatus()
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

    /// <summary>Adds the default prompt-status renderer.</summary>
    public HpdAgentTuiBuilder AddDefaultPromptStatus()
    {
        TryAddPromptStatus(new DefaultPromptStatusShellComponent());
        return this;
    }

    /// <summary>Adds the component rendered immediately above the prompt.</summary>
    public HpdAgentTuiBuilder AddPromptStatus(IAgentTuiShellComponent promptStatus)
    {
        ArgumentNullException.ThrowIfNull(promptStatus);
        if (_promptStatus is not null)
            throw new InvalidOperationException("A prompt-status contribution is already registered.");

        _promptStatus = promptStatus;
        return this;
    }

    /// <summary>Adds a delegate component rendered immediately above the prompt.</summary>
    public HpdAgentTuiBuilder AddPromptStatus(Func<AgentTuiShellContext, IComponent> render)
        => AddPromptStatus(new DelegateAgentTuiShellComponent(render));

    /// <summary>Adds the prompt-status component when none is registered.</summary>
    public HpdAgentTuiBuilder TryAddPromptStatus(IAgentTuiShellComponent promptStatus)
    {
        ArgumentNullException.ThrowIfNull(promptStatus);
        _promptStatus ??= promptStatus;
        return this;
    }

    /// <summary>Replaces the registered prompt-status component.</summary>
    public HpdAgentTuiBuilder ReplacePromptStatus(IAgentTuiShellComponent promptStatus)
    {
        ArgumentNullException.ThrowIfNull(promptStatus);
        if (_promptStatus is null)
            throw new InvalidOperationException("Cannot replace prompt status because none is registered.");

        _promptStatus = promptStatus;
        return this;
    }

    /// <summary>Replaces the registered prompt-status component with a delegate component.</summary>
    public HpdAgentTuiBuilder ReplacePromptStatus(Func<AgentTuiShellContext, IComponent> render)
        => ReplacePromptStatus(new DelegateAgentTuiShellComponent(render));

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
        TryAddPage(new HpdAgentTuiPageDescriptor("hpd.help", context =>
        {
            var commands = string.Join("\n", _commands.Values
                .Where(static command => !command.Hidden)
                .OrderBy(static command => command.Order)
                .ThenBy(static command => command.SlashName, StringComparer.OrdinalIgnoreCase)
                .Select(static command =>
                {
                    var description = string.IsNullOrWhiteSpace(command.Description)
                        ? command.Title
                        : command.Description;
                    return $"- `/{command.SlashName}` {description}";
                }));

            return HPD.TUI.Content.MarkdownBlock.Prepare(
                $"**Commands**\n\n{commands}", context.Width, context.Theme, context.ColorSystem);
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
            Description = "Show available shell commands.",
            Order = 600
        });
        TryAddSlashCommand(new HpdAgentTuiCommandDescriptor("clear", context => context.Shell.Transcript.ClearAll())
        {
            Title = "/clear",
            Description = "Clear the transcript.",
            Order = 700
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
        return SetRunConfigComposer(_ => new AgentTuiInputRunConfig(selection.ToRunConfig()));
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

    /// <summary>Adds an event projection handler with explicit runtime-tree visibility.</summary>
    public HpdAgentTuiBuilder AddEventHandler(
        string key,
        IAgentTuiEventHandler handler,
        AgentTuiEventScope scope = AgentTuiEventScope.CurrentThread)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(handler);
        AgentTuiEventScopeRouting.Validate(scope, nameof(scope));
        if (!_eventHandlers.TryAdd(key, new AgentTuiEventHandlerRegistration(handler, scope)))
        {
            throw new InvalidOperationException($"An event handler is already registered for '{key}'.");
        }

        return this;
    }

    public HpdAgentTuiBuilder AddEventHandler<TEvent>(
        string key,
        AgentTuiEventHandler<TEvent> handler,
        AgentTuiEventScope scope = AgentTuiEventScope.CurrentThread)
        where TEvent : AgentEvent
        => AddEventHandler(key, (IAgentTuiEventHandler)handler, scope);

    public HpdAgentTuiBuilder AddEventHandler<TEvent, THandler>(
        string key,
        AgentTuiEventScope scope = AgentTuiEventScope.CurrentThread)
        where TEvent : AgentEvent
        where THandler : AgentTuiEventHandler<TEvent>, new()
        => AddEventHandler<TEvent>(key, new THandler(), scope);

    /// <summary>Adds an event projection handler when its key is not already registered.</summary>
    public HpdAgentTuiBuilder TryAddEventHandler(
        string key,
        IAgentTuiEventHandler handler,
        AgentTuiEventScope scope = AgentTuiEventScope.CurrentThread)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(handler);
        AgentTuiEventScopeRouting.Validate(scope, nameof(scope));
        _eventHandlers.TryAdd(key, new AgentTuiEventHandlerRegistration(handler, scope));
        return this;
    }

    public HpdAgentTuiBuilder TryAddEventHandler<TEvent>(
        string key,
        AgentTuiEventHandler<TEvent> handler,
        AgentTuiEventScope scope = AgentTuiEventScope.CurrentThread)
        where TEvent : AgentEvent
        => TryAddEventHandler(key, (IAgentTuiEventHandler)handler, scope);

    public HpdAgentTuiBuilder TryAddEventHandler<TEvent, THandler>(
        string key,
        AgentTuiEventScope scope = AgentTuiEventScope.CurrentThread)
        where TEvent : AgentEvent
        where THandler : AgentTuiEventHandler<TEvent>, new()
        => TryAddEventHandler<TEvent>(key, new THandler(), scope);

    /// <summary>Replaces an event projection handler and its runtime-tree visibility.</summary>
    public HpdAgentTuiBuilder ReplaceEventHandler(
        string key,
        IAgentTuiEventHandler handler,
        AgentTuiEventScope scope = AgentTuiEventScope.CurrentThread)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(handler);
        AgentTuiEventScopeRouting.Validate(scope, nameof(scope));
        if (!_eventHandlers.ContainsKey(key))
        {
            throw new InvalidOperationException($"Cannot replace event handler '{key}' because none is registered.");
        }

        _eventHandlers[key] = new AgentTuiEventHandlerRegistration(handler, scope);
        return this;
    }

    public HpdAgentTuiBuilder ReplaceEventHandler<TEvent>(
        string key,
        AgentTuiEventHandler<TEvent> handler,
        AgentTuiEventScope scope = AgentTuiEventScope.CurrentThread)
        where TEvent : AgentEvent
        => ReplaceEventHandler(key, (IAgentTuiEventHandler)handler, scope);

    public HpdAgentTuiBuilder ReplaceEventHandler<TEvent, THandler>(
        string key,
        AgentTuiEventScope scope = AgentTuiEventScope.CurrentThread)
        where TEvent : AgentEvent
        where THandler : AgentTuiEventHandler<TEvent>, new()
        => ReplaceEventHandler<TEvent>(key, new THandler(), scope);

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

    /// <summary>Adds an application-owned footer item under a unique key.</summary>
    public HpdAgentTuiBuilder AddFooterItem(string key, IAgentTuiFooterItem item)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(item);
        if (!_footerItems.TryAdd(key, item))
        {
            throw new InvalidOperationException($"A footer item is already registered for '{key}'.");
        }

        return this;
    }

    /// <summary>Adds an application-owned footer item when its key is not registered.</summary>
    public HpdAgentTuiBuilder TryAddFooterItem(string key, IAgentTuiFooterItem item)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(item);
        _footerItems.TryAdd(key, item);
        return this;
    }

    /// <summary>Replaces an application-owned footer item registered under the supplied key.</summary>
    public HpdAgentTuiBuilder ReplaceFooterItem(string key, IAgentTuiFooterItem item)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(item);
        if (!_footerItems.ContainsKey(key))
        {
            throw new InvalidOperationException($"Cannot replace footer item '{key}' because none is registered.");
        }

        _footerItems[key] = item;
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

    /// <summary>Adds a bidirectional request handler with explicit runtime-tree visibility.</summary>
    public HpdAgentTuiBuilder AddInteractionHandler(
        string key,
        IAgentTuiInteractionHandler handler,
        AgentTuiEventScope scope = AgentTuiEventScope.CurrentThread)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(handler);
        AgentTuiEventScopeRouting.Validate(scope, nameof(scope));
        if (!_interactionHandlers.TryAdd(key, new AgentTuiInteractionHandlerRegistration(handler, scope)))
        {
            throw new InvalidOperationException($"An interaction handler is already registered for '{key}'.");
        }

        return this;
    }

    public HpdAgentTuiBuilder AddInteractionHandler<TRequest>(
        string key,
        AgentTuiInteractionHandler<TRequest> handler,
        AgentTuiEventScope scope = AgentTuiEventScope.CurrentThread)
        where TRequest : AgentEvent
        => AddInteractionHandler(key, (IAgentTuiInteractionHandler)handler, scope);

    public HpdAgentTuiBuilder AddInteractionHandler<TRequest, THandler>(
        string key,
        AgentTuiEventScope scope = AgentTuiEventScope.CurrentThread)
        where TRequest : AgentEvent
        where THandler : AgentTuiInteractionHandler<TRequest>, new()
        => AddInteractionHandler<TRequest>(key, new THandler(), scope);

    /// <summary>Adds a bidirectional request handler when its key is not already registered.</summary>
    public HpdAgentTuiBuilder TryAddInteractionHandler(
        string key,
        IAgentTuiInteractionHandler handler,
        AgentTuiEventScope scope = AgentTuiEventScope.CurrentThread)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(handler);
        AgentTuiEventScopeRouting.Validate(scope, nameof(scope));
        _interactionHandlers.TryAdd(key, new AgentTuiInteractionHandlerRegistration(handler, scope));
        return this;
    }

    public HpdAgentTuiBuilder TryAddInteractionHandler<TRequest>(
        string key,
        AgentTuiInteractionHandler<TRequest> handler,
        AgentTuiEventScope scope = AgentTuiEventScope.CurrentThread)
        where TRequest : AgentEvent
        => TryAddInteractionHandler(key, (IAgentTuiInteractionHandler)handler, scope);

    public HpdAgentTuiBuilder TryAddInteractionHandler<TRequest, THandler>(
        string key,
        AgentTuiEventScope scope = AgentTuiEventScope.CurrentThread)
        where TRequest : AgentEvent
        where THandler : AgentTuiInteractionHandler<TRequest>, new()
        => TryAddInteractionHandler<TRequest>(key, new THandler(), scope);

    /// <summary>Replaces a bidirectional request handler and its runtime-tree visibility.</summary>
    public HpdAgentTuiBuilder ReplaceInteractionHandler(
        string key,
        IAgentTuiInteractionHandler handler,
        AgentTuiEventScope scope = AgentTuiEventScope.CurrentThread)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(handler);
        AgentTuiEventScopeRouting.Validate(scope, nameof(scope));
        if (!_interactionHandlers.ContainsKey(key))
        {
            throw new InvalidOperationException($"Cannot replace interaction handler '{key}' because none is registered.");
        }

        _interactionHandlers[key] = new AgentTuiInteractionHandlerRegistration(handler, scope);
        return this;
    }

    public HpdAgentTuiBuilder ReplaceInteractionHandler<TRequest>(
        string key,
        AgentTuiInteractionHandler<TRequest> handler,
        AgentTuiEventScope scope = AgentTuiEventScope.CurrentThread)
        where TRequest : AgentEvent
        => ReplaceInteractionHandler(key, (IAgentTuiInteractionHandler)handler, scope);

    public HpdAgentTuiBuilder ReplaceInteractionHandler<TRequest, THandler>(
        string key,
        AgentTuiEventScope scope = AgentTuiEventScope.CurrentThread)
        where TRequest : AgentEvent
        where THandler : AgentTuiInteractionHandler<TRequest>, new()
        => ReplaceInteractionHandler<TRequest>(key, new THandler(), scope);

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

    /// <summary>
    /// Adds the component that reconciles current shell state from durable thread state.
    /// </summary>
    public HpdAgentTuiBuilder AddThreadStateReconciler(IAgentTuiThreadStateReconciler reconciler)
    {
        ArgumentNullException.ThrowIfNull(reconciler);
        if (_threadStateReconciler is not null)
            throw new InvalidOperationException("A thread-state reconciler is already registered.");

        _threadStateReconciler = reconciler;
        return this;
    }

    /// <summary>
    /// Adds the durable thread-state reconciler when none is already registered.
    /// </summary>
    public HpdAgentTuiBuilder TryAddThreadStateReconciler(IAgentTuiThreadStateReconciler reconciler)
    {
        ArgumentNullException.ThrowIfNull(reconciler);
        _threadStateReconciler ??= reconciler;
        return this;
    }

    public HpdAgentTuiRegistry Build()
    {
        _permissionPresentationRenderers.Freeze();
        return new(
            _commands.Values,
            _pages.Values,
            _footerItems,
            _widgets,
            _autocompleteProviders,
            _shortcuts.Values,
            _eventHandlers,
            _interactionHandlers,
            _transcriptRenderers.Values,
            _header,
            _promptStatus,
            _footer,
            _promptFactory,
            _shellLayout,
            _shellChrome,
            _theme,
            _includeSlashCommandAutocomplete,
            _runConfigComposer,
            _threadStateReconciler,
            _transcriptHistoryPresentation,
            _showReasoning);
    }

}
