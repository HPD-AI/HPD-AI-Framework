using System.Diagnostics;
using System.Collections.Concurrent;
using System.Threading.Channels;
using HPD.Agent.TUI.Application;
using HPD.Agent.TUI.Commands;
using HPD.Agent.TUI.Composition;
using HPD.Agent.TUI.Interactions;
using HPD.Agent.TUI.Markdown;
using HPD.Agent.TUI.Models;
using HPD.Agent.TUI.Observability;
using HPD.Agent.TUI.Runtime;
using HPD.Agent.TUI.Views;
using HPD.Events;
using HPD.TUI.Controllers;
using HPD.TUI.Core;
using HPD.TUI.Models;
using HPD.TUI.Markdown;
using HPD.TUI.Rendering;
using HPD.TUI.Terminal;
using HPD.TUI.Views;
using Microsoft.Extensions.AI;

namespace HPD.Agent.TUI;

public sealed class HpdAgentTuiApp : IAsyncDisposable
{
    private static readonly IMarkdownLayoutEngine MarkdownLayoutEngine = new HPD.TUI.Markdown.MarkdownLayoutEngine();
    private sealed record QueuedCommand(
        string CommandLine,
        CancellationToken CancellationToken)
    {
        public TaskCompletionSource Completion { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private static readonly TimeSpan CancelConfirmationWindow = TimeSpan.FromSeconds(2);
    private readonly IHpdAgentTuiRuntime _runtime;
    private readonly AgentTuiRuntimeScope? _requestedScope;
    private readonly HpdAgentTuiRegistry _registry;
    private readonly ManagedTerminalTuiApplication _application;
    private readonly MarkdownStreamCoordinator _markdownStreams;
    private readonly HashSet<MarkdownStreamIdentity> _activeMarkdownStreams = [];
    private readonly object _commandGate = new();
    private readonly Queue<QueuedCommand> _queuedCommands = [];
    private bool _commandsReady;
    private PromptView? _prompt;
    private AgentTuiSessionState? _state;
    private AgentTuiRuntimeScope? _scope;
    private AgentTuiDialogService? _dialogs;
    private CancellationTokenSource? _observeCancellation;
    private Task? _observeTask;
    private Task? _interactionTask;
    private Channel<AgentEvent>? _interactionQueue;
    private CancellationToken _runCancellationToken;
    private readonly HashSet<string> _handledInteractionIds = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _activeInteractionCancellations = new(StringComparer.Ordinal);
    private readonly HashSet<string> _sessionTitleUpdates = new(StringComparer.Ordinal);
    private readonly HashSet<string> _completedThreadExecutionIds = new(StringComparer.Ordinal);
    private readonly Dictionary<AgentTuiRuntimeScope, PendingPromptQueue> _pendingPromptsByScope = [];
    private PendingPrompt? _queuedPromptBeingSubmitted;
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
    private IAgentTuiFramePreparable? _framePreparable;

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
        _markdownStreams = new MarkdownStreamCoordinator(
            new AgentTuiDispatcher(_application),
            PublishMarkdownUpdate);
        _application.ShortcutHandler = TryExecuteShortcut;
        _application.FramePreparing = PrepareMarkdownFrame;
        _application.Stopping = DiscardMarkdownState;
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

        var applicationTask = _application.RunAsync(options, linked.Token);
        StartQueuedCommands();
        await applicationTask.ConfigureAwait(false);
        await linked.CancelAsync().ConfigureAwait(false);
        await StopObserverAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Executes a slash command, or queues it until the initial TUI shell is ready.
    /// </summary>
    /// <param name="commandLine">The complete slash-command line.</param>
    /// <param name="cancellationToken">A token that cancels the queued or executing command.</param>
    /// <returns>A task that completes after the command finishes.</returns>
    public Task ExecuteCommandAsync(
        string commandLine,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandLine);
        lock (_commandGate)
        {
            if (!_commandsReady)
            {
                var queued = new QueuedCommand(commandLine, cancellationToken);
                _queuedCommands.Enqueue(queued);
                return queued.Completion.Task;
            }
        }

        return SubmitCommandAsync(commandLine, cancellationToken);
    }

    private void StartQueuedCommands()
    {
        QueuedCommand[] queued;
        lock (_commandGate)
        {
            _commandsReady = true;
            queued = _queuedCommands.ToArray();
            _queuedCommands.Clear();
        }

        foreach (var command in queued)
        {
            _ = ExecuteQueuedCommandAsync(command);
        }
    }

    private async Task ExecuteQueuedCommandAsync(QueuedCommand command)
    {
        try
        {
            await SubmitCommandAsync(command.CommandLine, command.CancellationToken).ConfigureAwait(false);
            command.Completion.TrySetResult();
        }
        catch (OperationCanceledException) when (command.CancellationToken.IsCancellationRequested)
        {
            command.Completion.TrySetCanceled(command.CancellationToken);
        }
        catch (Exception ex)
        {
            command.Completion.TrySetException(ex);
        }
    }

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
        foreach (var cancellation in _activeInteractionCancellations.Values)
            cancellation.Cancel();
        _activeInteractionCancellations.Clear();
        _appliedCursor = default;
        _initialObservedCursor = default;
        _pendingRecoveryRequests = [];
        _hydratedThreadState = null;
        _completedThreadExecutionIds.Clear();
        _inputSubmissionPending = false;
        _awaitingRuntimeSubmissionId = 0;
        _activeThreadExecutionId = null;
        _scopeIsDurable = false;
        _scope = scope;
        _state = new AgentTuiSessionState(scope, _registry, RequestRender);
        AgentTuiPerformanceDiagnostics.ConfigureFromEnvironment(_state.State);
        _state.Shell.Runtime = _runtime;
        _state.Shell.SwitchScopeAsync = SwitchScopeAsync;
        _state.Shell.SetPromptDraftAsync = SetPromptDraftAsync;
        _state.Shell.AboveEditor.Add(new PendingPromptPreview(PendingPrompts(scope)));
        var autocomplete = new AutocompleteController()
            .Register(new AgentTuiAutocompleteProviderAdapter(_registry, () => _state));
        _prompt = _registry.PromptFactory.Create(
            new AgentTuiPromptContext(scope, _state.Shell),
            SubmitPrompt,
            autocomplete);
        _state.Shell.FocusPromptAction = () => _application.SetFocus(_prompt);
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
        _framePreparable = shell as IAgentTuiFramePreparable;
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
        _interactionQueue = Channel.CreateUnbounded<AgentEvent>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });
        foreach (var pendingRequest in _pendingRecoveryRequests)
            _interactionQueue.Writer.TryWrite(pendingRequest);
        _pendingRecoveryRequests = [];
        _interactionTask = ProcessInteractionsAsync(_interactionQueue.Reader, _observeCancellation.Token);
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
            await StopObserverAsync().ConfigureAwait(false);

        StartObserver(scope, cancellationToken);
    }

    private async ValueTask StopObserverAsync()
    {
        if (_observeCancellation is null)
        {
            _markdownStreams.DiscardAllAfterProducerStopped();
            _activeMarkdownStreams.Clear();
            return;
        }

        await _observeCancellation.CancelAsync().ConfigureAwait(false);
        if (_observeTask is not null)
        {
            await _observeTask.ConfigureAwait(false);
        }
        if (_interactionTask is not null)
        {
            await _interactionTask.ConfigureAwait(false);
        }

        _observeCancellation.Dispose();
        _observeCancellation = null;
        _observeTask = null;
        _interactionTask = null;
        _interactionQueue = null;
        if (_application.CheckAccess())
            DiscardMarkdownState();
        else if (_application.IsRunning)
            await _application.InvokeAsync(DiscardMarkdownState).ConfigureAwait(false);
        else
        {
            _markdownStreams.DiscardAllAfterProducerStopped();
            _activeMarkdownStreams.Clear();
        }
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
            var pendingPrompts = PendingPrompts(_scope);
            pendingPrompts.Enqueue(text);
            _state.Shell.PromptStatusText = PendingPromptFooter(pendingPrompts.Count);
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
        _state.Shell.PromptStatusText = "state: submitting";
        RequestRender();
        _ = SubmitInputWithQueuedPromptAsync(
            _scope,
            new UserMessagesInputEvent { Messages = [new ChatMessage(ChatRole.User, text)],
                AgentId = _scope.AgentId,
                SessionId = _scope.SessionId,
                ThreadId = _scope.ThreadId,
                ClientInputId = _queuedPromptBeingSubmitted?.ClientInputId ?? Guid.NewGuid().ToString("N"),
                RunConfig = runConfig
            },
            text,
            _queuedPromptBeingSubmitted);
    }

    private async Task SubmitInputAsync(
        AgentTuiRuntimeScope scope,
        AgentInputEvent input,
        string? sessionTitleText = null)
        => await SubmitInputWithQueuedPromptAsync(scope, input, sessionTitleText, queuedPrompt: null)
            .ConfigureAwait(false);

    private async Task SubmitInputWithQueuedPromptAsync(
        AgentTuiRuntimeScope scope,
        AgentInputEvent input,
        string? sessionTitleText = null,
        PendingPrompt? queuedPrompt = null)
    {
        _ = await SubmitInputCoreWithQueuedPromptAsync(scope, input, sessionTitleText, restoreRejectedDraft: true, queuedPrompt)
            .ConfigureAwait(false);
    }

    private async Task<AgentTuiSubmitResult?> SubmitInputCoreAsync(
        AgentTuiRuntimeScope scope,
        AgentInputEvent input,
        string? sessionTitleText,
        bool restoreRejectedDraft)
        => await SubmitInputCoreWithQueuedPromptAsync(
            scope,
            input,
            sessionTitleText,
            restoreRejectedDraft,
            queuedPrompt: null).ConfigureAwait(false);

    private async Task<AgentTuiSubmitResult?> SubmitInputCoreWithQueuedPromptAsync(
        AgentTuiRuntimeScope scope,
        AgentInputEvent input,
        string? sessionTitleText,
        bool restoreRejectedDraft,
        PendingPrompt? queuedPrompt)
    {
        var rejectedDraft = restoreRejectedDraft
            ? input switch
            {
                UserMessagesInputEvent { Messages.Count: 1 } messages => messages.Messages[0].Text,
                _ => null
            }
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
            if (submitted.Disposition is AgentInputDisposition.Accepted or AgentInputDisposition.Completed or AgentInputDisposition.Queued)
            {
                if (queuedPrompt is not null)
                {
                    PendingPrompts(scope).Remove(queuedPrompt.ClientInputId);
                    RequestRender();
                }
            }
            if (submitted.Disposition == AgentInputDisposition.Queued &&
                submitted.ActiveExecution is { } queuedExecution &&
                !_completedThreadExecutionIds.Remove(queuedExecution.ThreadExecutionId))
            {
                _activeThreadExecutionId = queuedExecution.ThreadExecutionId;
            }
            else if (submitted.Disposition != AgentInputDisposition.Accepted &&
                     submitted.Disposition != AgentInputDisposition.Completed)
            {
                if (queuedPrompt is not null && input is UserMessagesInputEvent)
                {
                    PendingPrompts(scope).Remove(queuedPrompt.ClientInputId);
                    RequestRender();
                }
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

            if (queuedPrompt is not null && input is UserMessagesInputEvent)
            {
                PendingPrompts(scope).Remove(queuedPrompt.ClientInputId);
                if (_prompt is not null && _prompt.Model.Text.Length == 0)
                    _prompt.Model.SetText(queuedPrompt.Text);
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
            _state.Shell.PromptStatusText = $"state: failed | {ex.Message}";
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

        if (key.Key == KeyCode.Tab &&
            key.Modifiers is KeyModifiers.None or KeyModifiers.Shift &&
            TryMoveWidgetFocus())
        {
            return true;
        }

        if (key.Key == KeyCode.UpArrow && key.Modifiers == KeyModifiers.Alt)
        {
            var pendingPrompts = PendingPrompts(_scope);
            if (pendingPrompts.Count == 0) return false;
            if (_prompt is null || _prompt.Model.Text.Length != 0)
            {
                _state.Shell.PromptStatusText = "state: running | clear the current draft to edit a queued follow-up";
                RequestRender();
                return true;
            }

            if (pendingPrompts.PopNewest() is { } pending)
                _prompt.Model.SetText(pending.Text);
            _state.Shell.PromptStatusText = pendingPrompts.Count == 0
                ? "state: running"
                : PendingPromptFooter(pendingPrompts.Count);
            RequestRender();
            return true;
        }

        if (key.Key == KeyCode.Escape &&
            key.Modifiers == KeyModifiers.None &&
            _prompt is not null &&
            !ReferenceEquals(_application.Focused, _prompt))
        {
            return false;
        }

        if (TryHandleActivePageInput(in key))
        {
            return true;
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

            if (_activeThreadExecutionId is not null && PendingPrompts(_scope).Count > 0)
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

    private bool TryMoveWidgetFocus()
    {
        if (_state is null || _prompt is null)
        {
            return false;
        }

        if (!ReferenceEquals(_application.Focused, _prompt))
        {
            _application.SetFocus(_prompt);
            return true;
        }

        if (_prompt.Controller.Autocomplete is { SuggestionCount: > 0 })
        {
            return false;
        }

        var widget = _state.Shell.AboveEditor.Snapshot().OfType<IFocusable>().FirstOrDefault();
        if (widget is null)
        {
            return false;
        }

        _application.SetFocus(widget);
        return true;
    }

    private bool TryHandleActivePageInput(in KeyEvent key)
    {
        if (_scope is null ||
            _state is null ||
            _dialogs?.HasOpenDialog == true ||
            string.IsNullOrWhiteSpace(_state.Shell.Navigation.ActivePageId) ||
            !_registry.TryFindPage(_state.Shell.Navigation.ActivePageId, out var page) ||
            page.HandleInput is null)
        {
            return false;
        }

        return page.HandleInput(
            new AgentTuiPageContext(
                _scope,
                _state.Shell,
                _state.Shell.Navigation,
                _registry,
                page,
                _registry.ShellChrome.DefaultTranscriptHeight,
                _state.State),
            key);
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
                state.Shell.PromptStatusText = "state: ready";
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
                state.Shell.PromptStatusText = $"state: running | press Esc again to cancel execution {shortExecutionId}";
                RequestRender();
                return;
            }

            ClearCancelConfirmation();
            state.Shell.PromptStatusText = $"state: cancelling | execution: {shortExecutionId}";
            UpsertRunActivity(
                state,
                activeExecution.ThreadExecutionId,
                $"execution {shortExecutionId} cancelling",
                ActivityState.Running,
                ActivitySeverity.Warning);
            RequestRender();
            var result = await _runtime.CancelExecutionAsync(
                    scope,
                    activeExecution.ThreadExecutionId,
                    CancellationToken.None)
                .ConfigureAwait(false);
            if (result.Disposition is AgentInputDisposition.NoActiveExecution or AgentInputDisposition.ExecutionFinishing)
            {
                _activeThreadExecutionId = null;
                state.Shell.PromptStatusText = "state: ready";
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
            await foreach (var batch in _runtime.ObserveAsync(
                    scope,
                    after,
                    _initialObservedCursor,
                    cancellationToken)
                .WithCancellation(cancellationToken)
                .ConfigureAwait(false))
            {
                await _application.InvokeAsync(
                        () => new ValueTask(OnAgentEventBatchAsync(batch, cancellationToken)),
                        cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (ThreadJournalReplacedException rebased)
        {
            await _application.InvokeAsync(() =>
            {
                DiscardMarkdownState();
                _handledInteractionIds.Clear();
                RebuildShell(scope,
                    $"Thread history was compacted into journal generation {rebased.CurrentCursor.Generation}; rehydrating.");
            }, cancellationToken).ConfigureAwait(false);
            if (await HydrateThreadAsync(scope, cancellationToken).ConfigureAwait(false))
            {
                _scopeIsDurable = true;
                foreach (var pendingRequest in _pendingRecoveryRequests)
                    _interactionQueue?.Writer.TryWrite(pendingRequest);
                _pendingRecoveryRequests = [];
                await ObserveAsync(scope, _appliedCursor, cancellationToken).ConfigureAwait(false);
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
            _state.Shell.PromptStatusText = "state: projection failed";
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

        ProjectMarkdownEvent(evt);

        await _state.ApplyEventAsync(evt, cancellationToken, deliveryMode).ConfigureAwait(false);
        if (deliveryMode != AgentTuiEventDeliveryMode.Historical &&
            AgentTuiEventScope.CurrentThread.Includes(evt, _scope))
        {
            TrackThreadExecution(evt);
            if (evt is ThreadExecutionFinishedEvent)
                SubmitNextPendingPrompt();
        }

        if (deliveryMode != AgentTuiEventDeliveryMode.Historical && evt is IAgentRequestEvent)
            _interactionQueue?.Writer.TryWrite(evt);
        if (evt is IAgentResponseEvent response)
            CancelActiveInteraction(response.RequestId);
        else if (evt is AgentRequestTerminatedEvent terminal)
            CancelActiveInteraction(terminal.RequestId);
    }

    private void ProjectMarkdownEvent(AgentEvent evt)
    {
        switch (evt)
        {
            case TextMessageStartEvent start when !string.Equals(start.Role, "user", StringComparison.OrdinalIgnoreCase):
                _activeMarkdownStreams.Add(new(MarkdownStreamKind.Assistant, start.MessageId));
                _markdownStreams.Start(
                    new(MarkdownStreamKind.Assistant, start.MessageId),
                    new(
                        start.Role,
                        start.Source,
                        start.Visibility,
                        start.AuthorName ?? start.Metadata?.AgentName,
                        start.Persistence,
                        start.CreatedAt,
                        start.ClientInputId,
                        start.Metadata?.AgentId,
                        start.Metadata?.AgentName,
                        start.Metadata?.ParentAgentId,
                        start.Metadata?.AgentChain,
                        start.Metadata?.Depth ?? 0,
                        start.SessionId,
                        start.ThreadId),
                    start.AdditionalProperties is null
                        ? null
                        : new Dictionary<string, object?>(start.AdditionalProperties, StringComparer.Ordinal));
                break;
            case TextDeltaEvent delta:
                _markdownStreams.Append(new(MarkdownStreamKind.Assistant, delta.MessageId), delta.Text);
                break;
            case TextMessageEndEvent end:
                _activeMarkdownStreams.Remove(new(MarkdownStreamKind.Assistant, end.MessageId));
                _markdownStreams.Complete(new(MarkdownStreamKind.Assistant, end.MessageId));
                break;
            case ReasoningMessageStartEvent start:
                _activeMarkdownStreams.Add(new(MarkdownStreamKind.Reasoning, start.MessageId));
                _markdownStreams.Start(
                    new(MarkdownStreamKind.Reasoning, start.MessageId),
                    new(
                        Role: start.Role,
                        Source: AgentMessageSource.Internal,
                        AuthorName: start.Metadata?.AgentName,
                        AgentId: start.Metadata?.AgentId,
                        AgentName: start.Metadata?.AgentName,
                        ParentAgentId: start.Metadata?.ParentAgentId,
                        AgentChain: start.Metadata?.AgentChain,
                        AgentDepth: start.Metadata?.Depth ?? 0,
                        SessionId: start.SessionId,
                        ThreadId: start.ThreadId));
                break;
            case ReasoningDeltaEvent delta:
                _markdownStreams.Append(new(MarkdownStreamKind.Reasoning, delta.MessageId), delta.Text);
                break;
            case ReasoningMessageEndEvent end:
                _activeMarkdownStreams.Remove(new(MarkdownStreamKind.Reasoning, end.MessageId));
                _markdownStreams.Complete(new(MarkdownStreamKind.Reasoning, end.MessageId));
                break;
            case ThreadExecutionFinishedEvent finished when _activeMarkdownStreams.Count > 0:
                _activeMarkdownStreams.Clear();
                _markdownStreams.FinalizeAll(finished.Outcome switch
                {
                    ThreadExecutionOutcome.Cancelled => MarkdownMessageState.Cancelled,
                    ThreadExecutionOutcome.Failed => MarkdownMessageState.Failed,
                    _ => MarkdownMessageState.Completed
                });
                break;
        }
    }

    private void PublishMarkdownUpdate(MarkdownStreamUpdate update, MarkdownMessageProjection projection)
    {
        if (_state is null || update.Document.Presentation.Visibility == AgentMessageVisibility.Hidden) return;
        var document = update.Document;
        var reasoning = document.Identity.Kind == MarkdownStreamKind.Reasoning;
        var layout = PrepareMarkdown(document, projection, _application.Size.Width, _application.Theme, reasoning);
        if (AgentTuiPerformanceDiagnostics.TryGetSink(_state.State, out var performanceSink))
            performanceSink.Publish(new MarkdownProjectionMeasured(
                _scope?.AgentId,
                document.MessageId,
                document.Identity.Kind,
                document.State,
                update.Invalidation,
                layout.DegradationReason,
                update.Diagnostics,
                projection.Diagnostics)
            {
                SessionId = _scope?.SessionId,
                ThreadId = _scope?.ThreadId,
                Metadata = _scope is null ? null : new AgentMetadata
                {
                    AgentId = _scope.AgentId,
                    AgentName = _scope.AgentId
                }
            });
        var entryKey = $"{(reasoning ? "reasoning" : "assistant")}:{document.MessageId}";
        var entry = new TranscriptEntry(
            Id: $"{(reasoning ? "reasoning" : "assistant")}-{document.MessageId}",
            EntryKey: entryKey,
            Cell: reasoning
                ? new ReasoningMessageCell(document, projection)
                : new AssistantMessageCell(document.Presentation.AuthorName, document, projection),
            Metadata: new TranscriptEntryMetadata(
                AgentId: document.Presentation.AgentId ?? _scope?.AgentId,
                AgentName: document.Presentation.AgentName ?? document.Presentation.AuthorName,
                ParentAgentId: document.Presentation.ParentAgentId,
                AgentChain: document.Presentation.AgentChain,
                AgentDepth: document.Presentation.AgentDepth,
                SessionId: document.Presentation.SessionId ?? _scope?.SessionId,
                ThreadId: document.Presentation.ThreadId ?? _scope?.ThreadId,
                MessageId: document.MessageId,
                MessageRole: document.Presentation.Role,
                AdditionalProperties: document.AdditionalProperties));
        if (document.State == MarkdownMessageState.Streaming)
            _state.Shell.Transcript.UpsertLive(entry);
        else
            _state.Shell.Transcript.FinalizeLive(entryKey, entry);
    }

    private void PrepareMarkdownFrame(TerminalSize size, Theme theme)
    {
        if (_state is null) return;
        _framePreparable?.PrepareFrame(size, theme, ColorSystem.TrueColor);
        foreach (var entry in _state.Shell.Transcript.Snapshot().Entries)
        {
            if (entry.Cell is AssistantMessageCell assistant)
                PrepareMarkdown(assistant.Document, assistant.Projection, size.Width, theme, reasoning: false);
            else if (entry.Cell is ReasoningMessageCell reasoning)
                PrepareMarkdown(reasoning.Document, reasoning.Projection, size.Width, theme, reasoning: true);
        }
    }

    private static MarkdownLayout PrepareMarkdown(
        MarkdownMessageDocument document,
        MarkdownMessageProjection projection,
        int outerWidth,
        Theme theme,
        bool reasoning)
    {
        var depthIndent = Math.Max(0, document.Presentation.AgentDepth) * 2;
        var effectiveTheme = reasoning
            ? AgentTuiTranscriptRenderServices.Default.CreateMutedTheme(theme)
            : theme;
        var width = Math.Max(1, outerWidth - depthIndent - (reasoning ? 2 : 0));
        return projection.Prepare(
            document,
            new(width, MarkdownTheme.FromTheme(effectiveTheme), ColorSystem.TrueColor),
            MarkdownLayoutEngine);
    }

    private async Task ProcessInteractionsAsync(
        ChannelReader<AgentEvent> reader,
        CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var evt in reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
                await HandleInteractionAsync(evt, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
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
            evt is IAgentRequestEvent request &&
            !string.IsNullOrWhiteSpace(request.RequestId) &&
            _registry.TryFindInteractionHandler(evt, _scope, out var handler) &&
            _handledInteractionIds.Add(request.RequestId))
        {
            using var interactionCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _activeInteractionCancellations[request.RequestId] = interactionCancellation;
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
                    interactionCancellation.Token).ConfigureAwait(false);

                switch (result.Kind)
                {
                    case AgentTuiInteractionResultKind.AnswerRequest when result.Response is not null:
                        var answer = await _runtime.AnswerRequestAsync(
                                _scope, result.Response, interactionCancellation.Token)
                            .ConfigureAwait(false);
                        if (!answer.Accepted)
                        {
                            await ShowNoticeAsync(
                                "Request was not accepted",
                                answer.Message ?? answer.Status.ToString(),
                                TranscriptSeverity.Warning,
                                interactionCancellation.Token).ConfigureAwait(false);
                        }
                        await ReconcileAfterInteractionAsync(interactionCancellation.Token).ConfigureAwait(false);
                        break;

                    case AgentTuiInteractionResultKind.InterruptTurn:
                        if (_activeThreadExecutionId is { } interactionExecutionId)
                        {
                            await _runtime.CancelExecutionAsync(
                                    _scope,
                                    interactionExecutionId,
                                    interactionCancellation.Token)
                                .ConfigureAwait(false);
                        }
                        break;

                    case AgentTuiInteractionResultKind.Error:
                        throw new InvalidOperationException(result.Reason ?? "Interaction failed.");
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                _handledInteractionIds.Remove(request.RequestId);
            }
            catch (OperationCanceledException)
            {
                // Durable response/terminal projection invalidated this dialog.
            }
            catch (Exception ex)
            {
                _handledInteractionIds.Remove(request.RequestId);
                await ShowNoticeAsync(
                    "Interaction failed",
                    ex.Message,
                    TranscriptSeverity.Warning,
                    CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                _activeInteractionCancellations.TryRemove(request.RequestId, out _);
            }
        }
    }

    private void CancelActiveInteraction(string requestId)
    {
        if (_activeInteractionCancellations.TryGetValue(requestId, out var cancellation))
            cancellation.Cancel();
    }

    private async Task ReconcileAfterInteractionAsync(CancellationToken cancellationToken)
    {
        if (_scope is null)
            return;

        var state = await _runtime.GetThreadStateAsync(_scope, cancellationToken)
            .ConfigureAwait(false);
        ReconcileRuntimeState(state);
        await ReconcileThreadPresentationAsync(state, cancellationToken).ConfigureAwait(false);
        RequestRender();
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
            PendingPrompts(scope).Count == 0 ||
            _inputSubmissionPending)
        {
            return;
        }

        var pendingPrompts = PendingPrompts(scope);
        var pending = pendingPrompts.PeekOldest()!;
        var text = pending.Text;
        _inputSubmissionPending = true;
        state.Shell.PromptStatusText = "state: steering";
        RequestRender();
        var submitted = await SubmitInputCoreAsync(
            scope,
            new UserMessagesInputEvent
            {
                Delivery = AgentInputDelivery.Steer,
                AgentId = scope.AgentId,
                SessionId = scope.SessionId,
                ThreadId = scope.ThreadId,
                ThreadExecutionId = activeExecutionId,
                ClientInputId = pending.ClientInputId,
                Messages = [new ChatMessage(ChatRole.User, text)]
            },
            sessionTitleText: null,
            restoreRejectedDraft: false).ConfigureAwait(false);

        if (submitted?.Disposition == AgentInputDisposition.Accepted &&
            pendingPrompts.PeekOldest()?.ClientInputId == pending.ClientInputId)
        {
            pendingPrompts.Remove(pending.ClientInputId);
        }

        if (_state == state && _activeThreadExecutionId is null)
            SubmitNextPendingPrompt();

        if (_state == state && pendingPrompts.Count > 0)
        {
            state.Shell.PromptStatusText = PendingPromptFooter(pendingPrompts.Count);
            RequestRender();
        }
    }

    private void SubmitNextPendingPrompt()
    {
        if (_activeThreadExecutionId is not null ||
            PendingPrompts(_scope!).Count == 0 ||
            _inputSubmissionPending)
        {
            return;
        }

        var pending = PendingPrompts(_scope!).PeekOldest()!;
        _queuedPromptBeingSubmitted = pending;
        try
        {
            SubmitPrompt(pending.Text.AsMemory());
        }
        finally
        {
            _queuedPromptBeingSubmitted = null;
        }
    }

    private PendingPromptQueue PendingPrompts(AgentTuiRuntimeScope scope)
    {
        if (!_pendingPromptsByScope.TryGetValue(scope, out var queue))
            _pendingPromptsByScope[scope] = queue = new PendingPromptQueue();
        return queue;
    }

    private static string PendingPromptFooter(int count)
        => count == 1
            ? "state: running | follow-up queued | Alt+↑ edit | Esc steer now"
            : $"state: running | {count} follow-ups queued | Alt+↑ edit latest | Esc steer next";

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

    private void DiscardMarkdownState()
    {
        _markdownStreams.DiscardAll();
        _activeMarkdownStreams.Clear();
    }

    public async ValueTask DisposeAsync()
    {
        await StopObserverAsync().ConfigureAwait(false);
        _application.Dispose();
    }
}
