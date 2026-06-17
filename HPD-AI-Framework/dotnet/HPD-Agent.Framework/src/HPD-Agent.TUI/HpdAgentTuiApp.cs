using HPD.Agent.TUI.Application;
using HPD.Agent.TUI.Commands;
using HPD.Agent.TUI.Composition;
using HPD.Agent.TUI.Interactions;
using HPD.Agent.TUI.Models;
using HPD.Agent.TUI.Runtime;
using HPD.Agent.TUI.Views;
using HPD.Events;
using HPD.TUI.Controllers;
using HPD.TUI.Core;
using HPD.TUI.Rendering;
using HPD.TUI.Terminal;
using HPD.TUI.Views;

namespace HPD.Agent.TUI;

public sealed class HpdAgentTuiApp : IAsyncDisposable
{
    private readonly IHpdAgentTuiRuntime _runtime;
    private readonly AgentTuiRuntimeScope? _requestedScope;
    private readonly HpdAgentTuiRegistry _registry;
    private readonly NormalTerminalTuiApplication _application;
    private PromptView? _prompt;
    private AgentTuiSessionState? _state;
    private AgentTuiRuntimeScope? _scope;
    private AgentTuiDialogService? _dialogs;
    private CancellationTokenSource? _observeCancellation;
    private Task? _observeTask;
    private CancellationToken _runCancellationToken;
    private readonly HashSet<string> _handledInteractionIds = new(StringComparer.Ordinal);
    private readonly HashSet<string> _sessionTitleUpdates = new(StringComparer.Ordinal);

    private HpdAgentTuiApp(
        IHpdAgentTuiRuntime runtime,
        AgentTuiRuntimeScope? requestedScope,
        HpdAgentTuiRegistry registry,
        ITerminal terminal)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _requestedScope = requestedScope;
        _registry = registry;
        _application = new NormalTerminalTuiApplication(terminal);
        _application.ShortcutHandler = TryExecuteShortcut;
        if (_registry.Theme is { } theme)
        {
            _application.Theme = theme;
        }
    }

    public static HpdAgentTuiApp Create(
        IHpdAgentTuiRuntime runtime,
        AgentTuiRuntimeScope? scope = null,
        Action<HpdAgentTuiBuilder>? configure = null,
        ITerminal? terminal = null)
    {
        ArgumentNullException.ThrowIfNull(runtime);

        var builder = new HpdAgentTuiBuilder();
        configure?.Invoke(builder);
        var registry = builder.Build();
        _ = registry.PromptFactory;
        _ = registry.ShellLayout;
        return new HpdAgentTuiApp(runtime, scope, registry, terminal ?? new ProcessTerminal());
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _runCancellationToken = linked.Token;
        var scope = await _runtime.EnsureScopeAsync(_requestedScope, linked.Token)
            .ConfigureAwait(false);
        RebuildShell(scope, "Connected to agent runtime.");
        await HydrateBranchAsync(scope, linked.Token).ConfigureAwait(false);
        StartObserver(scope, linked.Token);

        await _application.RunAsync(GetRenderHeight, linked.Token).ConfigureAwait(false);
        await linked.CancelAsync().ConfigureAwait(false);
        await StopObserverAsync().ConfigureAwait(false);
    }

    private void RebuildShell(
        AgentTuiRuntimeScope scope,
        string notice)
    {
        _scope = scope;
        _state = new AgentTuiSessionState(scope, _registry);
        _state.Shell.Runtime = _runtime;
        _state.Shell.SwitchScopeAsync = SwitchScopeAsync;
        var autocomplete = new AutocompleteController()
            .Register(new AgentTuiAutocompleteProviderAdapter(_registry, () => _state));
        _prompt = _registry.PromptFactory.Create(
            new AgentTuiPromptContext(scope, _state.Shell),
            SubmitPrompt,
            autocomplete);
        _state.Shell.Transcript.Append(new TranscriptEntry(
            Id: $"scope-notice-{Guid.NewGuid():N}",
            EntryKey: null,
            Cell: new NoticeCell(notice),
            Metadata: new TranscriptEntryMetadata()));

        var shell = _registry.ShellLayout.Create(new AgentTuiShellLayoutContext(
            _state.Shell,
            _prompt,
            _registry,
            _registry.ShellChrome,
            _state.State));
        var dialogHost = new DialogHost(shell, _application.Focus);
        _dialogs = new AgentTuiDialogService(
            dialogHost,
            _registry.ShellChrome.Dialog,
            _state.Shell.AboveEditor);
        _application.SetRoot(dialogHost);
        _application.SetFocus(_prompt);
        _prompt.IsFocused = true;
    }

    private void StartObserver(
        AgentTuiRuntimeScope scope,
        CancellationToken cancellationToken)
    {
        _observeCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _observeTask = ObserveAsync(scope, _observeCancellation.Token);
    }

    private async ValueTask StopObserverAsync()
    {
        if (_observeCancellation is null)
        {
            return;
        }

        await _observeCancellation.CancelAsync().ConfigureAwait(false);
        if (_observeTask is not null)
        {
            await _observeTask.ConfigureAwait(false);
        }

        _observeCancellation.Dispose();
        _observeCancellation = null;
        _observeTask = null;
    }

    private static int GetRenderHeight(TerminalSize size) => Math.Max(size.Height, 16_384);

    private void SubmitPrompt(ReadOnlyMemory<char> value)
    {
        if (_scope is null || _state is null || _dialogs is null)
        {
            return;
        }

        var text = value.ToString();
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        if (text.TrimStart().StartsWith('/'))
        {
            _ = SubmitCommandAsync(text, CancellationToken.None);
            return;
        }

        AgentRunConfig? runConfig;
        try
        {
            runConfig = _registry.RunConfigComposer?.Invoke(new AgentTuiRunConfigContext(
                _scope,
                _state.Shell,
                text));
        }
        catch (Exception ex)
        {
            _state.Shell.Transcript.Append(new TranscriptEntry(
                Id: $"run-config-error-{Guid.NewGuid():N}",
                EntryKey: null,
                Cell: new NoticeCell(
                    "Run config composer failed",
                    new HPD.TUI.Components.Text(ex.Message),
                    TranscriptSeverity.Error),
                Metadata: new TranscriptEntryMetadata(
                    AgentId: _scope.AgentId,
                    AgentName: "tui",
                    AgentChain: ["tui"])));
            return;
        }

        _state.AppendUserInput(text);
        _state.Shell.FooterText = "state: submitting";
        _ = SetSessionTitleFromFirstMessageAsync(_scope, text, CancellationToken.None);
        _ = SubmitInputAsync(
            _scope,
            new UserTextInputEvent(text)
            {
                AgentId = _scope.AgentId,
                SessionId = _scope.SessionId,
                BranchId = _scope.BranchId,
                RunConfig = runConfig
            });
    }

    private async Task SubmitInputAsync(
        AgentTuiRuntimeScope scope,
        AgentInputEvent input)
    {
        try
        {
            await _runtime.SubmitInputAsync(scope, input, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            if (_state is null || _scope != scope)
            {
                return;
            }

            _state.Shell.Transcript.Append(new TranscriptEntry(
                Id: $"submit-error-{Guid.NewGuid():N}",
                EntryKey: null,
                Cell: new NoticeCell(
                    "Input submission failed",
                    new HPD.TUI.Components.Text(ex.Message),
                    TranscriptSeverity.Error),
                Metadata: new TranscriptEntryMetadata(
                    AgentId: scope.AgentId,
                    AgentName: "tui",
                    AgentChain: ["tui"])));
            _state.Shell.FooterText = $"state: failed | {ex.Message}";
        }
    }

    private bool TryExecuteShortcut(KeyEvent key)
    {
        if (_scope is null || _state is null)
        {
            return false;
        }

        if (key.Key == KeyCode.Escape && key.Modifiers == KeyModifiers.None && TryGoBack())
        {
            return true;
        }

        if (!_registry.TryFindShortcut(in key, out var shortcut))
        {
            return false;
        }

        shortcut.Execute(new AgentTuiShortcutContext(_scope, _state.Shell, _state.Shell.Navigation, shortcut));
        return true;
    }

    private bool TryGoBack()
    {
        if (_state is null)
        {
            return false;
        }

        if (_dialogs?.CloseTop() == true)
        {
            return true;
        }

        return _state.Shell.Navigation.Back();
    }

    private async Task SubmitCommandAsync(
        string text,
        CancellationToken cancellationToken)
    {
        if (_scope is null || _state is null || _dialogs is null)
        {
            return;
        }

        if (!_registry.TryFindSlashCommand(text.AsSpan(), out var command, out var arguments))
        {
            _state.Shell.Transcript.Append(new TranscriptEntry(
                Id: $"unknown-command-{Guid.NewGuid():N}",
                EntryKey: null,
                Cell: new NoticeCell(
                    $"Unknown command: {text.Trim()}",
                    Severity: TranscriptSeverity.Warning),
                Metadata: new TranscriptEntryMetadata(
                    AgentId: _scope.AgentId,
                    AgentName: "tui",
                    AgentChain: ["tui"])));
            return;
        }

        try
        {
            await command.ExecuteAsync(new AgentTuiCommandContext(
                    _scope,
                    _state.Shell,
                    _state.Shell.Navigation,
                    _runtime,
                    _dialogs,
                    SwitchScopeAsync,
                    command,
                    arguments))
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _state.Shell.Transcript.Append(new TranscriptEntry(
                Id: $"command-error-{Guid.NewGuid():N}",
                EntryKey: null,
                Cell: new NoticeCell(
                    $"Command '{command.SlashName}' failed",
                    new HPD.TUI.Components.Text(ex.Message),
                    TranscriptSeverity.Error),
                Metadata: new TranscriptEntryMetadata(
                    AgentId: _scope.AgentId,
                    AgentName: "tui",
                    AgentChain: ["tui"])));
        }
    }

    private async ValueTask SwitchScopeAsync(
        AgentTuiRuntimeScope scope,
        CancellationToken cancellationToken)
    {
        var ensured = await _runtime.EnsureScopeAsync(scope, cancellationToken)
            .ConfigureAwait(false);
        await StopObserverAsync().ConfigureAwait(false);
        _handledInteractionIds.Clear();
        RebuildShell(
            ensured,
            $"Switched to agent `{ensured.AgentId}`, session `{ensured.SessionId}`, branch `{ensured.BranchId}`.");
        await HydrateBranchAsync(ensured, cancellationToken).ConfigureAwait(false);
        StartObserver(ensured, _runCancellationToken);
    }

    private async Task HydrateBranchAsync(
        AgentTuiRuntimeScope scope,
        CancellationToken cancellationToken)
    {
        if (_state is null)
        {
            return;
        }

        IReadOnlyList<AgentEvent> events;
        try
        {
            events = await _runtime.GetBranchEventsAsync(scope, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _state.Shell.Transcript.Append(new TranscriptEntry(
                Id: $"branch-hydration-error-{Guid.NewGuid():N}",
                EntryKey: null,
                Cell: new NoticeCell(
                    "Could not load branch history",
                    new HPD.TUI.Components.Text(ex.Message),
                    TranscriptSeverity.Warning),
                Metadata: new TranscriptEntryMetadata(
                    AgentId: scope.AgentId,
                    AgentName: "tui",
                    AgentChain: ["tui"])));
            return;
        }

        foreach (var evt in events)
        {
            await _state.ApplyEventAsync(evt, cancellationToken).ConfigureAwait(false);
        }

        _state.Shell.Transcript.ScrollToBottom();
    }

    private async Task SetSessionTitleFromFirstMessageAsync(
        AgentTuiRuntimeScope scope,
        string text,
        CancellationToken cancellationToken)
    {
        if (_runtime is not IAgentTuiSessionBranchRuntime sessions ||
            !_sessionTitleUpdates.Add(scope.SessionId))
        {
            return;
        }

        try
        {
            var session = await sessions.GetSessionAsync(scope.SessionId, cancellationToken)
                .ConfigureAwait(false);
            if (session is null || !string.IsNullOrWhiteSpace(session.Title))
            {
                return;
            }

            var title = CreateTitleFromMessage(text);
            if (string.IsNullOrWhiteSpace(title))
            {
                return;
            }

            await sessions.RenameSessionAsync(scope.SessionId, title, cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            // Session titles are convenience metadata; prompt submission should not fail because of them.
        }
    }

    private static string CreateTitleFromMessage(string text)
    {
        var normalized = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (normalized.Length <= 60)
        {
            return normalized;
        }

        return normalized[..57] + "...";
    }

    private async Task ObserveAsync(
        AgentTuiRuntimeScope scope,
        CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var evt in _runtime.ObserveAsync(scope, cancellationToken)
                .WithCancellation(cancellationToken)
                .ConfigureAwait(false))
            {
                await OnAgentEventAsync(evt, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task OnAgentEventAsync(
        AgentEvent evt,
        CancellationToken cancellationToken)
    {
        if (_scope is null || _state is null)
        {
            return;
        }

        await _state.ApplyEventAsync(evt, cancellationToken).ConfigureAwait(false);

        if (_dialogs is null)
        {
            return;
        }

        if (evt is not IRequestEvent request ||
            string.IsNullOrWhiteSpace(request.RequestId) ||
            !_registry.TryFindInteractionHandler(evt, out var handler))
        {
            return;
        }

        if (!_handledInteractionIds.Add(request.RequestId))
        {
            return;
        }

        try
        {
            var response = await handler.Value.HandleAsync(
                new AgentTuiInteractionContext(
                    _scope,
                    _state.Shell,
                    _state.Shell.Navigation,
                    _runtime,
                    _dialogs,
                    evt),
                cancellationToken).ConfigureAwait(false);

            if (response is not null)
            {
                await _runtime.RespondAsync(_scope, response, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _state.Shell.Transcript.Append(new TranscriptEntry(
                Id: $"interaction-error-{Guid.NewGuid():N}",
                EntryKey: null,
                Cell: new NoticeCell(
                    $"Interaction handler '{handler.Key}' failed",
                    new HPD.TUI.Components.Text(ex.Message),
                    TranscriptSeverity.Error),
                Metadata: new TranscriptEntryMetadata(
                    AgentId: _scope.AgentId,
                    AgentName: "tui",
                    AgentChain: ["tui"])));
        }
    }

    public ValueTask DisposeAsync()
    {
        _application.Dispose();
        return ValueTask.CompletedTask;
    }
}
