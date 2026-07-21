using System.Diagnostics;
using HPD.Agent.TUI.Application;
using HPD.Agent.TUI.Commands;
using HPD.Agent.TUI.Composition;
using HPD.Agent.TUI.Interactions;
using HPD.Agent.TUI.Models;
using HPD.Agent.TUI.Observability;
using HPD.Agent.TUI.Runtime;
using HPD.Agent.TUI.Views;
using HPD.Events;
using HPD.TUI.Controllers;
using HPD.TUI.Core;
using HPD.TUI.Models;
using HPD.TUI.Rendering;
using HPD.TUI.Terminal;
using HPD.TUI.Views;
using Microsoft.Extensions.AI;

namespace HPD.Agent.TUI;

public sealed class HpdAgentTuiApp : IAsyncDisposable
{
    private static readonly TimeSpan CancelConfirmationWindow = TimeSpan.FromSeconds(2);
    private readonly IHpdAgentTuiRuntime _runtime;
    private readonly AgentTuiRuntimeScope? _requestedScope;
    private readonly HpdAgentTuiRegistry _registry;
    private readonly ManagedTerminalTuiApplication _application;
    private PromptView? _prompt;
    private AgentTuiSessionState? _state;
    private AgentTuiRuntimeScope? _scope;
    private AgentTuiDialogService? _dialogs;
    private CancellationTokenSource? _observeCancellation;
    private Task? _observeTask;
    private CancellationToken _runCancellationToken;
    private readonly HashSet<string> _handledInteractionIds = new(StringComparer.Ordinal);
    private readonly HashSet<string> _sessionTitleUpdates = new(StringComparer.Ordinal);
    private readonly HashSet<string> _completedThreadExecutionIds = new(StringComparer.Ordinal);
    private readonly Queue<string> _pendingPrompts = new();
    private string? _activeThreadExecutionId;
    private string? _cancelConfirmationExecutionId;
    private DateTimeOffset _cancelConfirmationExpiresAt;
    private bool _inputSubmissionPending;
    private long _submissionSequence;
    private long _awaitingRuntimeSubmissionId;
    private bool _scopeIsDurable;
    private ThreadJournalCursor _appliedCursor;
    private ThreadJournalCursor _initialObservedCursor;
    private IReadOnlyList<AgentEvent> _pendingRecoveryRequests = [];
    private AgentTuiThreadState? _hydratedThreadState;

    private HpdAgentTuiApp(
        IHpdAgentTuiRuntime runtime,
        AgentTuiRuntimeScope? requestedScope,
        HpdAgentTuiRegistry registry,
        ITerminal terminal)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _requestedScope = requestedScope;
        _registry = registry;
        _application = new ManagedTerminalTuiApplication(terminal);
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

    public async Task RunAsync(
        TuiRunOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _runCancellationToken = linked.Token;
        var initialScope = await _runtime.ResolveInitialScopeAsync(_requestedScope, linked.Token)
            .ConfigureAwait(false);
        RebuildShell(initialScope.Scope, "Connected to agent runtime.");
        if (initialScope.IsDurable)
        {
            await NotifyDurableScopeEnsuredAsync(initialScope.Scope, linked.Token).ConfigureAwait(false);
            if (await HydrateThreadAsync(initialScope.Scope, linked.Token).ConfigureAwait(false))
            {
                _scopeIsDurable = true;
                StartObserver(initialScope.Scope, linked.Token);
            }
        }

        await _application.RunAsync(options, linked.Token).ConfigureAwait(false);
        await linked.CancelAsync().ConfigureAwait(false);
        await StopObserverAsync().ConfigureAwait(false);
    }

    public Task ExecuteCommandAsync(
        string commandLine,
        CancellationToken cancellationToken = default)
        => SubmitCommandAsync(commandLine, cancellationToken);

    public Func<AgentTuiRuntimeScope, CancellationToken, ValueTask>? DurableScopeEnsuredAsync { get; set; }

    public AgentTuiRuntimeScope? CurrentScope => _scope;

    public ValueTask ShowNoticeAsync(
        string title,
        string? detail = null,
        TranscriptSeverity severity = TranscriptSeverity.Info,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_state is null)
        {
            return ValueTask.CompletedTask;
        }

        _state.Shell.Transcript.AddFinal(new TranscriptEntry(
            Id: $"notice-{Guid.NewGuid():N}",
            EntryKey: null,
            Cell: string.IsNullOrWhiteSpace(detail)
                ? new NoticeCell(title, Severity: severity)
                : new NoticeCell(title, new HPD.TUI.Components.Text(detail), severity),
            Metadata: new TranscriptEntryMetadata(
                AgentId: _scope?.AgentId,
                AgentName: "tui",
                SessionId: _scope?.SessionId,
                ThreadId: _scope?.ThreadId)));
        RequestRender();
        return ValueTask.CompletedTask;
    }

    private void RebuildShell(
        AgentTuiRuntimeScope scope,
        string notice)
    {
        _appliedCursor = default;
        _initialObservedCursor = default;
        _pendingRecoveryRequests = [];
        _hydratedThreadState = null;
        _completedThreadExecutionIds.Clear();
        _pendingPrompts.Clear();
        _inputSubmissionPending = false;
        _awaitingRuntimeSubmissionId = 0;
        _activeThreadExecutionId = null;
        _scopeIsDurable = false;
        _scope = scope;
        _state = new AgentTuiSessionState(scope, _registry);
        AgentTuiPerformanceDiagnostics.ConfigureFromEnvironment(_state.State);
        _state.Shell.Runtime = _runtime;
        _state.Shell.SwitchScopeAsync = SwitchScopeAsync;
        _state.Shell.SetPromptDraftAsync = SetPromptDraftAsync;
        var autocomplete = new AutocompleteController()
            .Register(new AgentTuiAutocompleteProviderAdapter(_registry, () => _state));
        _prompt = _registry.PromptFactory.Create(
            new AgentTuiPromptContext(scope, _state.Shell),
            SubmitPrompt,
            autocomplete);
        _state.Shell.Transcript.AddFinal(new TranscriptEntry(
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
            _state.Shell.AboveEditor,
            _state.Shell.Navigation,
            RequestRender);
        _application.SetRoot(dialogHost);
        _application.SetFocus(_prompt);
        _prompt.IsFocused = true;
    }

    private void StartObserver(
        AgentTuiRuntimeScope scope,
        CancellationToken cancellationToken)
    {
        _observeCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _observeTask = ObserveAsync(scope, _appliedCursor, _observeCancellation.Token);
    }

    private async ValueTask StartObserverIfNeededAsync(
        AgentTuiRuntimeScope scope,
        CancellationToken cancellationToken)
    {
        if (_observeTask is { IsCompleted: false })
        {
            return;
        }

        if (_observeCancellation is not null)
        {
            await _observeCancellation.CancelAsync().ConfigureAwait(false);
            _observeCancellation.Dispose();
            _observeCancellation = null;
            _observeTask = null;
        }

        StartObserver(scope, cancellationToken);
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

        if (_activeThreadExecutionId is not null)
        {
            _pendingPrompts.Enqueue(text);
            _state.Shell.FooterText = PendingPromptFooter(_pendingPrompts.Count);
            RequestRender();
            return;
        }

        if (_inputSubmissionPending)
        {
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
        catch (AgentTuiRunConfigRejectedException ex)
        {
            _state.Shell.Transcript.AddFinal(new TranscriptEntry(
                Id: $"run-config-rejected-{Guid.NewGuid():N}",
                EntryKey: null,
                Cell: new NoticeCell(
                    ex.Title,
                    string.IsNullOrWhiteSpace(ex.Detail)
                        ? null
                        : new HPD.TUI.Components.Text(ex.Detail),
                    ex.Severity),
                Metadata: new TranscriptEntryMetadata(
                    AgentId: _scope.AgentId,
                    AgentName: "tui",
                    AgentChain: ["tui"])));
            RequestRender();
            return;
        }
        catch (Exception ex)
        {
            _state.Shell.Transcript.AddFinal(new TranscriptEntry(
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
            RequestRender();
            return;
        }

        _inputSubmissionPending = true;
        _state.Shell.FooterText = "state: submitting";
        RequestRender();
        _ = SubmitInputAsync(
            _scope,
            new UserMessagesInputEvent { Messages = [new ChatMessage(ChatRole.User, text)],
                AgentId = _scope.AgentId,
                SessionId = _scope.SessionId,
                ThreadId = _scope.ThreadId,
                RunConfig = runConfig
            },
            text);
    }

    private async Task SubmitInputAsync(
        AgentTuiRuntimeScope scope,
        AgentInputEvent input,
        string? sessionTitleText = null)
    {
        _ = await SubmitInputCoreAsync(scope, input, sessionTitleText, restoreRejectedDraft: true)
            .ConfigureAwait(false);
    }

    private async Task<AgentTuiSubmitResult?> SubmitInputCoreAsync(
        AgentTuiRuntimeScope scope,
        AgentInputEvent input,
        string? sessionTitleText,
        bool restoreRejectedDraft)
    {
        var rejectedDraft = restoreRejectedDraft &&
            input is SteeringInputEvent { Messages.Count: 1 } steering
            ? steering.Messages[0].Text
            : null;
        try
        {
            var ensured = await _runtime.EnsureDurableScopeAsync(scope, CancellationToken.None)
                .ConfigureAwait(false);
            await NotifyDurableScopeEnsuredAsync(ensured, CancellationToken.None).ConfigureAwait(false);
            if (!_scopeIsDurable)
            {
                if (!await HydrateThreadAsync(ensured, CancellationToken.None).ConfigureAwait(false))
                {
                    throw new InvalidOperationException(
                        $"Thread '{ensured.SessionId}/{ensured.ThreadId}' could not be promoted to durable state.");
                }
                _scopeIsDurable = true;
            }
            await StartObserverIfNeededAsync(ensured, _runCancellationToken)
                .ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(sessionTitleText))
            {
                await SetSessionTitleFromFirstMessageAsync(ensured, sessionTitleText, CancellationToken.None)
                    .ConfigureAwait(false);
            }

            var submissionId = ++_submissionSequence;
            _awaitingRuntimeSubmissionId = submissionId;
            AgentTuiSubmitResult submitted;
            try
            {
                submitted = await _runtime.SubmitInputAsync(ensured, input, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            finally
            {
                if (_awaitingRuntimeSubmissionId == submissionId)
                    _awaitingRuntimeSubmissionId = 0;
            }
            if (_state is null || _scope != scope)
                return null;

            _inputSubmissionPending = false;
            if (submitted.Disposition == AgentInputDisposition.Queued &&
                submitted.ActiveExecution is { } queuedExecution &&
                !_completedThreadExecutionIds.Remove(queuedExecution.ThreadExecutionId))
            {
                _activeThreadExecutionId = queuedExecution.ThreadExecutionId;
            }
            else if (submitted.Disposition != AgentInputDisposition.Accepted &&
                     submitted.Disposition != AgentInputDisposition.Completed)
            {
                if (!string.IsNullOrEmpty(rejectedDraft) &&
                    _prompt is not null &&
                    _prompt.Model.Text.Length == 0)
                {
                    _prompt.Model.SetText(rejectedDraft);
                }
                _state.Shell.Transcript.AddFinal(new TranscriptEntry(
                    Id: $"input-rejected-{Guid.NewGuid():N}",
                    EntryKey: null,
                    Cell: new NoticeCell(
                        "Input not accepted",
                        new HPD.TUI.Components.Text(submitted.Disposition.ToString()),
                        TranscriptSeverity.Warning),
                    Metadata: new TranscriptEntryMetadata(
                        AgentId: scope.AgentId,
                        AgentName: "tui",
                        AgentChain: ["tui"],
                        SessionId: scope.SessionId,
                        ThreadId: scope.ThreadId)));
            }

            return submitted;
        }
        catch (Exception ex)
        {
            if (_state is null || _scope != scope)
            {
                return null;
            }

            _state.Shell.Transcript.AddFinal(new TranscriptEntry(
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
            RequestRender();
            return null;
        }
        finally
        {
            _inputSubmissionPending = false;
        }
    }

    private bool TryExecuteShortcut(KeyEvent key)
    {
        if (_scope is null || _state is null)
        {
            return false;
        }

        if (key.Key == KeyCode.Escape && key.Modifiers == KeyModifiers.None)
        {
            if (_dialogs?.HasOpenDialog == true)
            {
                return false;
            }

            if (TryGoBack())
            {
                return true;
            }

            if (_prompt?.Controller.Autocomplete is { SuggestionCount: > 0 })
            {
                return false;
            }

            if (_activeThreadExecutionId is not null && _pendingPrompts.Count > 0)
            {
                _ = PromotePendingPromptToSteeringAsync(_scope, _state);
                return true;
            }

            _ = ConfirmCancelActiveExecutionAsync(_scope, _state);
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

        return _state.Shell.Navigation.Back();
    }

    private async Task ConfirmCancelActiveExecutionAsync(
        AgentTuiRuntimeScope scope,
        AgentTuiSessionState state)
    {
        try
        {
            var threadState = await _runtime.GetThreadStateAsync(scope, CancellationToken.None)
                .ConfigureAwait(false);
            ReconcileRuntimeState(threadState);
            var activeExecution = threadState.ActiveExecution;
            if (activeExecution is null ||
                !string.Equals(activeExecution.Status, "active", StringComparison.OrdinalIgnoreCase))
            {
                ClearCancelConfirmation();
                state.Shell.FooterText = "state: ready";
                RequestRender();
                return;
            }

            var shortExecutionId = ShortId(activeExecution.ThreadExecutionId);
            var now = DateTimeOffset.UtcNow;
            if (!string.Equals(_cancelConfirmationExecutionId, activeExecution.ThreadExecutionId, StringComparison.Ordinal) ||
                now > _cancelConfirmationExpiresAt)
            {
                _cancelConfirmationExecutionId = activeExecution.ThreadExecutionId;
                _cancelConfirmationExpiresAt = now + CancelConfirmationWindow;
                state.Shell.FooterText = $"state: running | press Esc again to cancel execution {shortExecutionId}";
                RequestRender();
                return;
            }

            ClearCancelConfirmation();
            state.Shell.FooterText = $"state: cancelling | execution: {shortExecutionId}";
            UpsertRunActivity(
                state,
                activeExecution.ThreadExecutionId,
                $"execution {shortExecutionId} cancelling",
                ActivityState.Running,
                ActivitySeverity.Warning);
            RequestRender();
            var result = await _runtime.SubmitInputAsync(
                    scope,
                    new InterruptionRequestEvent(null, "Cancelled from TUI.", InterruptionSource.User)
                    {
                        AgentId = scope.AgentId,
                        SessionId = scope.SessionId,
                        ThreadId = scope.ThreadId,
                        ThreadExecutionId = activeExecution.ThreadExecutionId
                    },
                    CancellationToken.None)
                .ConfigureAwait(false);
            if (result.Disposition is AgentInputDisposition.NoActiveExecution or AgentInputDisposition.ExecutionFinishing)
            {
                _activeThreadExecutionId = null;
                state.Shell.FooterText = "state: ready";
                RequestRender();
            }
        }
        catch (Exception ex)
        {
            if (_state != state || _scope != scope)
            {
                return;
            }

            state.Shell.Transcript.AddFinal(new TranscriptEntry(
                Id: $"cancel-error-{Guid.NewGuid():N}",
                EntryKey: null,
                Cell: new NoticeCell(
                    "Cancel request failed",
                    new HPD.TUI.Components.Text(ex.Message),
                    TranscriptSeverity.Error),
                Metadata: new TranscriptEntryMetadata(
                    AgentId: scope.AgentId,
                    AgentName: "tui",
                    AgentChain: ["tui"])));
            RequestRender();
        }
    }

    private void ClearCancelConfirmation()
    {
        _cancelConfirmationExecutionId = null;
        _cancelConfirmationExpiresAt = default;
    }

    private static void UpsertRunActivity(
        AgentTuiSessionState state,
        string threadExecutionId,
        string label,
        ActivityState activityState,
        ActivitySeverity severity)
    {
        var prefix = $"execution {ShortId(threadExecutionId)}";
        foreach (var activity in state.Shell.Activities.Activities)
        {
            if (activity.Label.StartsWith(prefix, StringComparison.Ordinal))
            {
                activity.Label = label;
                activity.State = activityState;
                activity.Severity = severity;
                return;
            }
        }

        state.Shell.Activities.Add(new ActivityModel(label)
        {
            State = activityState,
            Severity = severity
        });
    }

    private static string ShortId(string value) => value[..Math.Min(8, value.Length)];

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
            _state.Shell.Transcript.AddFinal(new TranscriptEntry(
                Id: $"unknown-command-{Guid.NewGuid():N}",
                EntryKey: null,
                Cell: new NoticeCell(
                    $"Unknown command: {text.Trim()}",
                    Severity: TranscriptSeverity.Warning),
                Metadata: new TranscriptEntryMetadata(
                    AgentId: _scope.AgentId,
                    AgentName: "tui",
                    AgentChain: ["tui"])));
            RequestRender();
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
            RequestRender();
        }
        catch (Exception ex)
        {
            _state.Shell.Transcript.AddFinal(new TranscriptEntry(
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
            RequestRender();
        }
    }

    public async ValueTask SwitchScopeAsync(
        AgentTuiRuntimeScope scope,
        CancellationToken cancellationToken)
    {
        var ensured = await _runtime.EnsureDurableScopeAsync(scope, cancellationToken)
            .ConfigureAwait(false);
        await NotifyDurableScopeEnsuredAsync(ensured, cancellationToken).ConfigureAwait(false);
        await StopObserverAsync().ConfigureAwait(false);
        _handledInteractionIds.Clear();
        RebuildShell(
            ensured,
            $"Switched to agent `{ensured.AgentId}`, session `{ensured.SessionId}`, thread `{ensured.ThreadId}`.");
        if (await HydrateThreadAsync(ensured, cancellationToken).ConfigureAwait(false))
        {
            _scopeIsDurable = true;
            StartObserver(ensured, _runCancellationToken);
        }
    }

    private ValueTask NotifyDurableScopeEnsuredAsync(
        AgentTuiRuntimeScope scope,
        CancellationToken cancellationToken)
        => DurableScopeEnsuredAsync is { } callback
            ? callback(scope, cancellationToken)
            : ValueTask.CompletedTask;

    private ValueTask SetPromptDraftAsync(
        string value,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _prompt?.Controller.SetDraft(value);
        RequestRender();
        return ValueTask.CompletedTask;
    }

    private async Task<bool> HydrateThreadAsync(
        AgentTuiRuntimeScope scope,
        CancellationToken cancellationToken)
    {
        if (_state is null)
        {
            return false;
        }

        AgentTuiThreadState threadState;
        try
        {
            threadState = await _runtime.GetThreadStateAsync(scope, cancellationToken)
                .ConfigureAwait(false);
            if (threadState.ObservedCursor.Generation <= 0)
            {
                throw new InvalidDataException(
                    "The durable thread state did not include a valid journal generation.");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _state.Shell.Transcript.AddFinal(new TranscriptEntry(
                Id: $"thread-hydration-error-{Guid.NewGuid():N}",
                EntryKey: null,
                Cell: new NoticeCell(
                    "Could not load thread history",
                    new HPD.TUI.Components.Text(ex.Message),
                    TranscriptSeverity.Warning),
                Metadata: new TranscriptEntryMetadata(
                    AgentId: scope.AgentId,
                    AgentName: "tui",
                    AgentChain: ["tui"])));
            RequestRender();
            return false;
        }

        ReconcileRuntimeState(threadState);
        _hydratedThreadState = threadState;
        await ReconcileThreadPresentationAsync(threadState, cancellationToken).ConfigureAwait(false);
        _initialObservedCursor = threadState.ObservedCursor;
        if (_appliedCursor.Generation == 0)
            _appliedCursor = ThreadJournalCursor.Start(threadState.ObservedCursor.Generation);
        _pendingRecoveryRequests = threadState.PendingRequests;
        RequestRender();
        return true;
    }

    private async Task SetSessionTitleFromFirstMessageAsync(
        AgentTuiRuntimeScope scope,
        string text,
        CancellationToken cancellationToken)
    {
        if (_runtime is not IAgentTuiSessionThreadRuntime sessions ||
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
        ThreadJournalCursor after,
        CancellationToken cancellationToken)
    {
        try
        {
            foreach (var pendingRequest in _pendingRecoveryRequests)
            {
                await HandleInteractionAsync(pendingRequest, cancellationToken).ConfigureAwait(false);
            }
            _pendingRecoveryRequests = [];

            await foreach (var batch in _runtime.ObserveAsync(
                    scope,
                    after,
                    _initialObservedCursor,
                    cancellationToken)
                .WithCancellation(cancellationToken)
                .ConfigureAwait(false))
            {
                await OnAgentEventBatchAsync(batch, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (ThreadJournalReplacedException rebased)
        {
            _handledInteractionIds.Clear();
            RebuildShell(
                scope,
                $"Thread history was compacted into journal generation {rebased.CurrentCursor.Generation}; rehydrating.");
            if (await HydrateThreadAsync(scope, cancellationToken).ConfigureAwait(false))
            {
                _scopeIsDurable = true;
                StartObserver(scope, _runCancellationToken);
            }
        }
        catch (Exception ex)
        {
            if (_state is null)
                return;

            _state.Shell.Transcript.AddFinal(new TranscriptEntry(
                Id: $"event-projection-failed-{Guid.NewGuid():N}",
                EntryKey: null,
                Cell: new NoticeCell(
                    "Event projection stopped",
                    new HPD.TUI.Components.Text(
                        $"Position {_appliedCursor.Generation}:{_appliedCursor.SequenceNumber + 1} was not applied: {ex.Message}"),
                    TranscriptSeverity.Error),
                Metadata: new TranscriptEntryMetadata(
                    AgentId: scope.AgentId,
                    AgentName: "tui",
                    AgentChain: ["tui"])));
            _state.Shell.FooterText = "state: projection failed";
            RequestRender();
        }
    }

    private async Task OnAgentEventBatchAsync(
        AgentTuiEventBatch batch,
        CancellationToken cancellationToken)
    {
        var events = batch.Events;
        if (_state is null || events.Count == 0)
        {
            return;
        }

        var hasPerformanceSink = AgentTuiPerformanceDiagnostics.TryGetSink(_state.State, out var performanceSink);
        var startedAt = hasPerformanceSink ? Stopwatch.GetTimestamp() : 0;
        using (_state.Shell.Transcript.BeginUpdate())
        {
            foreach (var evt in events)
            {
                await OnAgentEventAsync(evt, batch.DeliveryMode, cancellationToken).ConfigureAwait(false);
            }
        }

        _appliedCursor = batch.LastCursor;
        if (batch.DeliveryMode == AgentTuiEventDeliveryMode.Historical &&
            _hydratedThreadState is { } hydratedThreadState)
        {
            ReconcileRuntimeState(hydratedThreadState);
            await ReconcileThreadPresentationAsync(hydratedThreadState, cancellationToken)
                .ConfigureAwait(false);
        }
        performanceSink?.Publish(new AgentTuiEventBatchApplied(
            _scope?.AgentId,
            batch.DeliveryMode,
            events.Count,
            batch.FirstCursor,
            batch.LastCursor,
            Stopwatch.GetElapsedTime(startedAt))
        {
            SessionId = _scope?.SessionId,
            ThreadId = _scope?.ThreadId,
            Metadata = _scope is null
                ? null
                : new AgentMetadata
                {
                    AgentId = _scope.AgentId,
                    AgentName = _scope.AgentId
                }
        });
        RequestRender();
    }

    private async Task OnAgentEventAsync(
        AgentEvent evt,
        AgentTuiEventDeliveryMode deliveryMode,
        CancellationToken cancellationToken)
    {
        if (_scope is null || _state is null)
        {
            return;
        }

        await _state.ApplyEventAsync(evt, cancellationToken, deliveryMode).ConfigureAwait(false);
        if (deliveryMode != AgentTuiEventDeliveryMode.Historical &&
            AgentTuiEventScope.CurrentThread.Includes(evt, _scope))
        {
            TrackThreadExecution(evt);
            if (evt is ThreadExecutionFinishedEvent)
                SubmitNextPendingPrompt();
        }

        await HandleInteractionAsync(evt, cancellationToken).ConfigureAwait(false);
    }

    private async Task HandleInteractionAsync(
        AgentEvent evt,
        CancellationToken cancellationToken)
    {
        if (_scope is null || _state is null)
        {
            return;
        }

        if (_dialogs is not null &&
            evt is IRequestEvent request &&
            !string.IsNullOrWhiteSpace(request.RequestId) &&
            _registry.TryFindInteractionHandler(evt, _scope, out var handler) &&
            _handledInteractionIds.Add(request.RequestId))
        {
            try
            {
                var result = await handler.Value.HandleAsync(
                    new AgentTuiInteractionContext(
                        _scope,
                        _state.Shell,
                        _state.Shell.Navigation,
                        _runtime,
                        _dialogs,
                        evt),
                    cancellationToken).ConfigureAwait(false);

                switch (result.Kind)
                {
                    case AgentTuiInteractionResultKind.AnswerRequest when result.Response is not null:
                        await _runtime.AnswerRequestAsync(_scope, result.Response, cancellationToken)
                            .ConfigureAwait(false);
                        break;

                    case AgentTuiInteractionResultKind.InterruptTurn:
                        if (_activeThreadExecutionId is { } interactionExecutionId)
                        {
                            await _runtime.SubmitInputAsync(
                                    _scope,
                                    new InterruptionRequestEvent(
                                        null,
                                        result.Reason ?? "Interrupted by TUI interaction.",
                                        InterruptionSource.User)
                                    {
                                        AgentId = _scope.AgentId,
                                        SessionId = _scope.SessionId,
                                        ThreadId = _scope.ThreadId,
                                        ThreadExecutionId = interactionExecutionId
                                    },
                                    cancellationToken)
                                .ConfigureAwait(false);
                        }
                        break;

                    case AgentTuiInteractionResultKind.Error:
                        throw new InvalidOperationException(result.Reason ?? "Interaction failed.");
                }
            }
            catch
            {
                _handledInteractionIds.Remove(request.RequestId);
                throw;
            }
        }
    }

    private void TrackThreadExecution(AgentEvent evt)
    {
        switch (evt)
        {
            case ThreadExecutionStartedEvent started:
                _completedThreadExecutionIds.Remove(started.ThreadExecutionId);
                _inputSubmissionPending = false;
                _activeThreadExecutionId = started.ThreadExecutionId;
                break;
            case ThreadExecutionFinishedEvent completed:
                if (_awaitingRuntimeSubmissionId != 0)
                    _completedThreadExecutionIds.Add(completed.ThreadExecutionId);
                if (string.Equals(_activeThreadExecutionId, completed.ThreadExecutionId, StringComparison.Ordinal))
                {
                    _activeThreadExecutionId = null;
                    _inputSubmissionPending = false;
                }
                break;
        }
    }

    private void ReconcileRuntimeState(AgentTuiThreadState threadState)
    {
        _activeThreadExecutionId = threadState.ActiveExecution is { Status: var status } activeExecution &&
            string.Equals(status, "active", StringComparison.OrdinalIgnoreCase)
                ? activeExecution.ThreadExecutionId
                : null;

        _inputSubmissionPending = false;
    }

    private async Task PromotePendingPromptToSteeringAsync(
        AgentTuiRuntimeScope scope,
        AgentTuiSessionState state)
    {
        if (_activeThreadExecutionId is not { } activeExecutionId ||
            _pendingPrompts.Count == 0 ||
            _inputSubmissionPending)
        {
            return;
        }

        var text = _pendingPrompts.Peek();
        _inputSubmissionPending = true;
        state.Shell.FooterText = "state: steering";
        RequestRender();
        var submitted = await SubmitInputCoreAsync(
            scope,
            new SteeringInputEvent
            {
                AgentId = scope.AgentId,
                SessionId = scope.SessionId,
                ThreadId = scope.ThreadId,
                ThreadExecutionId = activeExecutionId,
                ClientInputId = Guid.NewGuid().ToString("N"),
                Messages = [new ChatMessage(ChatRole.User, text)]
            },
            sessionTitleText: null,
            restoreRejectedDraft: false).ConfigureAwait(false);

        if (submitted?.Disposition == AgentInputDisposition.Accepted &&
            _pendingPrompts.Count > 0 &&
            string.Equals(_pendingPrompts.Peek(), text, StringComparison.Ordinal))
        {
            _pendingPrompts.Dequeue();
        }

        if (_state == state && _activeThreadExecutionId is null)
            SubmitNextPendingPrompt();

        if (_state == state && _pendingPrompts.Count > 0)
        {
            state.Shell.FooterText = PendingPromptFooter(_pendingPrompts.Count);
            RequestRender();
        }
    }

    private void SubmitNextPendingPrompt()
    {
        if (_activeThreadExecutionId is not null ||
            _pendingPrompts.Count == 0 ||
            _inputSubmissionPending)
        {
            return;
        }

        var text = _pendingPrompts.Dequeue();
        SubmitPrompt(text.AsMemory());
    }

    private static string PendingPromptFooter(int count)
        => count == 1
            ? "state: running | follow-up queued | press Esc to steer now"
            : $"state: running | {count} follow-ups queued | press Esc to steer next now";

    private ValueTask ReconcileThreadPresentationAsync(
        AgentTuiThreadState threadState,
        CancellationToken cancellationToken)
    {
        if (_scope is null || _state is null || _registry.ThreadStateReconciler is null)
            return ValueTask.CompletedTask;

        return _registry.ThreadStateReconciler.ReconcileAsync(
            threadState,
            new AgentTuiEventContext(
                _scope,
                _state.Shell,
                _state.Shell.Navigation,
                _registry,
                _state.State,
                AgentTuiEventDeliveryMode.Historical),
            cancellationToken);
    }

    private void RequestRender()
    {
        _application.RequestRender();
    }

    public ValueTask DisposeAsync()
    {
        _application.Dispose();
        return ValueTask.CompletedTask;
    }
}
