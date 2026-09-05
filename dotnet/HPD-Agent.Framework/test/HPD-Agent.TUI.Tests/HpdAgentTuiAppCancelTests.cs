using System.Reflection;
using System.Text;
using System.Threading.Channels;
using FluentAssertions;
using HPD.Agent;
using HPD.Agent.TUI.Application;
using HPD.Agent.TUI.Composition;
using HPD.Agent.TUI.Interactions;
using HPD.Agent.TUI.Models;
using HPD.Agent.TUI.Runtime;
using HPD.TUI.Core;
using HPD.TUI.Models;
using HPD.TUI.Markdown;
using HPD.TUI.Terminal;
using HPD.TUI.Views;

namespace HPD.Agent.TUI.Tests;

public sealed class HpdAgentTuiAppCancelTests
{
    [Fact]
    public async Task BlockedMarkdownPreparation_DoesNotBlockControlEscape()
    {
        var scope = new AgentTuiRuntimeScope("agent-a", "session-a", "main");
        var runtime = new CancelRuntime(scope);
        using var parser = new BlockingMarkdownParser();
        var terminal = new BlockingInputTerminal(80, 24);
        await using var app = HpdAgentTuiApp.Create(
            runtime,
            new DirectAgentTuiExecutionTarget(scope),
            static builder => builder.AddAgentTuiDefaults(),
            terminal,
            parser);
        using var runCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var run = app.RunAsync(cancellationToken: runCancellation.Token);
        await WaitUntilAsync(() => GetPrivateFieldValue<AgentTuiSessionState?>(app, "_state") is not null);

        var batch = new AgentTuiEventBatch(
            [new TextMessageStartEvent("blocked", "assistant") { SessionId = scope.SessionId, ThreadId = scope.ThreadId },
             new TextDeltaEvent("## heading\n", "blocked") { SessionId = scope.SessionId, ThreadId = scope.ThreadId }],
            AgentTuiEventDeliveryMode.Live,
            new ThreadJournalCursor(1, 1),
            new ThreadJournalCursor(1, 2),
            new ThreadJournalCursor(1, 2));
        var projection = Task.Run(() => InvokePrivate<Task>(
            app, "OnAgentEventBatchAsync", batch, CancellationToken.None));
        await parser.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));

        terminal.Enqueue(TerminalInputEvent.FromKey(new KeyEvent(KeyCode.Character, new Rune('x'))));
        await WaitUntilAsync(() => GetPrivateFieldValue<PromptView?>(app, "_prompt")?.Model.Text.ToString() == "x");
        terminal.Enqueue(TerminalInputEvent.FromKey(new KeyEvent(KeyCode.Escape, Modifiers: KeyModifiers.Ctrl)));
        await run.WaitAsync(TimeSpan.FromSeconds(2));

        parser.Release.Set();
        await projection.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task ToolStart_CommitsPrecedingMarkdownBeforeToolEntry()
    {
        var scope = new AgentTuiRuntimeScope("agent-a", "session-a", "main");
        var observer = new MarkdownBoundaryObserver();
        await using var app = HpdAgentTuiApp.Create(
            new CancelRuntime(scope),
            new DirectAgentTuiExecutionTarget(scope),
            builder => builder.AddAgentTuiDefaults().AddEventHandler<ToolCallStartEvent>("boundary-observer", observer),
            new TestTerminal(80, 24));
        InvokePrivate(app, "RebuildShell", new DirectAgentTuiExecutionTarget(scope), "Connected.");
        var batch = new AgentTuiEventBatch(
            [new TextMessageStartEvent("commentary", "assistant"),
             new TextDeltaEvent("before tool", "commentary"),
             new ToolCallStartEvent("call-1", "read", "commentary")],
            AgentTuiEventDeliveryMode.Live,
            new ThreadJournalCursor(1, 1), new ThreadJournalCursor(1, 3), new ThreadJournalCursor(1, 3));

        await InvokePrivate<Task>(app, "OnAgentEventBatchAsync", batch, CancellationToken.None);

        observer.SawPrecedingMarkdown.Should().BeTrue();
    }

    [Fact]
    public async Task ActivePage_ReceivesInputBeforeFocusedPrompt()
    {
        var scope = new AgentTuiRuntimeScope("agent-a", "session-a", "main");
        var runtime = new CancelRuntime(scope);
        var received = new List<KeyEvent>();
        await using var app = HpdAgentTuiApp.Create(
            runtime,
            new DirectAgentTuiExecutionTarget(scope),
            builder => builder
                .AddAgentTuiDefaults()
                .TryAddPage(new HpdAgentTuiPageDescriptor(
                    "test.page",
                    static _ => new HPD.TUI.Components.Text("Page"))
                {
                    HandleInput = (_, key) =>
                    {
                        received.Add(key);
                        return key.Key == KeyCode.Enter;
                    },
                }),
            new TestTerminal(80, 24));
        InvokePrivate(app, "RebuildShell", new DirectAgentTuiExecutionTarget(scope), "Connected.");
        var state = GetPrivateField<AgentTuiSessionState>(app, "_state");
        state.Shell.Navigation.GoToPage("test.page");
        var application = GetPrivateField<HPD.TUI.Rendering.ManagedTerminalTuiApplication>(
            app,
            "_application");

        application.HandleInput(
            TuiInputEvent.FromKey(new KeyEvent(KeyCode.Enter))).Should().BeTrue();

        received.Should().ContainSingle()
            .Which.Key.Should().Be(KeyCode.Enter);
        GetPrivateField<PromptView>(app, "_prompt").Model.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task FirstInput_PromotesAndHydratesTransientScopeBeforeObservationAndSubmission()
    {
        var scope = new AgentTuiRuntimeScope("agent-a", "pending-session", "main");
        var runtime = new CancelRuntime(scope) { InitialIsDurable = false };
        await using var app = HpdAgentTuiApp.Create(
            runtime,
            new DirectAgentTuiExecutionTarget(scope),
            static builder => builder.AddAgentTuiDefaults(),
            new TestTerminal(80, 24));
        InvokePrivate(app, "RebuildShell", new DirectAgentTuiExecutionTarget(scope), "Connected.");
        var input = new UserMessagesInputEvent
        {
            Messages = [new Microsoft.Extensions.AI.ChatMessage(Microsoft.Extensions.AI.ChatRole.User, "hello")],
            AgentId = scope.AgentId,
            SessionId = scope.SessionId,
            ThreadId = scope.ThreadId
        };

        await InvokePrivate<Task>(app, "SubmitInputAsync", new DirectAgentTuiExecutionTarget(scope), input, null!);
        var observed = await runtime.ObserverStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        observed.Should().Be(ThreadJournalCursor.Start(1));
        runtime.Calls.Should().ContainInOrder("ensure", "state", "observe", "submit");
        runtime.Calls.IndexOf("state").Should().BeLessThan(runtime.Calls.IndexOf("observe"));
        runtime.Calls.IndexOf("observe").Should().BeLessThan(runtime.Calls.IndexOf("submit"));
    }

    [Fact]
    public async Task SwitchTarget_LeavesTransientScopeUndurableUntilFirstInput()
    {
        var initialScope = new AgentTuiRuntimeScope("agent-a", "session-a", "main");
        var draftScope = new AgentTuiRuntimeScope("agent-a", "pending-session", "main");
        var runtime = new CancelRuntime(initialScope) { InitialIsDurable = false };
        await using var app = HpdAgentTuiApp.Create(
            runtime,
            new DirectAgentTuiExecutionTarget(initialScope),
            static builder => builder.AddAgentTuiDefaults(),
            new TestTerminal(80, 24));
        InvokePrivate(app, "RebuildShell", new DirectAgentTuiExecutionTarget(initialScope), "Connected.");

        await app.SwitchTargetAsync(
            new DirectAgentTuiExecutionTarget(draftScope),
            CancellationToken.None);

        app.CurrentScope.Should().Be(draftScope);
        runtime.Calls.Should().Equal("resolve");

        var input = new UserMessagesInputEvent
        {
            Messages = [new Microsoft.Extensions.AI.ChatMessage(Microsoft.Extensions.AI.ChatRole.User, "hello")],
            AgentId = draftScope.AgentId,
            SessionId = draftScope.SessionId,
            ThreadId = draftScope.ThreadId
        };
        await InvokePrivate<Task>(
            app,
            "SubmitInputAsync",
            new DirectAgentTuiExecutionTarget(draftScope),
            input,
            null!);

        runtime.Calls.Should().ContainInOrder("resolve", "ensure", "state", "observe", "submit");
    }

    [Fact]
    public async Task Hydration_InvokesThreadStateReconcilerWithAuthoritativeSnapshot()
    {
        var scope = new AgentTuiRuntimeScope("agent-a", "session-a", "main");
        var activeExecution = new AgentTuiThreadExecution(
            "run-authoritative",
            scope.AgentId,
            scope.SessionId,
            scope.ThreadId,
            "active",
            DateTimeOffset.UtcNow);
        var runtime = new CancelRuntime(scope) { ActiveExecution = activeExecution };
        var reconciler = new RecordingThreadStateReconciler();
        await using var app = HpdAgentTuiApp.Create(
            runtime,
            new DirectAgentTuiExecutionTarget(scope),
            builder => builder
                .AddAgentTuiDefaults()
                .AddThreadStateReconciler(reconciler),
            new TestTerminal(80, 24));
        InvokePrivate(app, "RebuildShell", new DirectAgentTuiExecutionTarget(scope), "Connected.");

        var hydrated = await InvokePrivate<Task<bool>>(
            app,
            "HydrateThreadAsync",
            scope,
            CancellationToken.None);

        hydrated.Should().BeTrue();
        reconciler.Snapshots.Should().ContainSingle().Which.ActiveExecution.Should().BeSameAs(activeExecution);
        GetPrivateField<AgentTuiSessionState>(app, "_state").Shell.PromptStatusText
            .Should().Be("snapshot: active");
    }

    [Fact]
    public async Task HistoricalBatch_ReappliesHydratedSnapshotAfterEventHandlers()
    {
        var scope = new AgentTuiRuntimeScope("agent-a", "session-a", "main");
        var activeExecution = new AgentTuiThreadExecution(
            "authoritative-run",
            scope.AgentId,
            scope.SessionId,
            scope.ThreadId,
            "active",
            DateTimeOffset.UtcNow);
        var runtime = new CancelRuntime(scope) { ActiveExecution = activeExecution };
        var reconciler = new RecordingThreadStateReconciler();
        await using var app = HpdAgentTuiApp.Create(
            runtime,
            new DirectAgentTuiExecutionTarget(scope),
            builder => builder
                .AddAgentTuiDefaults()
                .AddEventHandler("test.stale-footer", new StaleHistoricalFooterHandler())
                .AddThreadStateReconciler(reconciler),
            new TestTerminal(80, 24));
        InvokePrivate(app, "RebuildShell", new DirectAgentTuiExecutionTarget(scope), "Connected.");
        (await InvokePrivate<Task<bool>>(
            app,
            "HydrateThreadAsync",
            scope,
            CancellationToken.None)).Should().BeTrue();

        await InvokePrivate<Task>(
            app,
            "OnAgentEventBatchAsync",
            new AgentTuiEventBatch(
                [
                    new ThreadExecutionStartedEvent("historical-run", scope.AgentId, DateTimeOffset.UtcNow),
                    new ThreadExecutionFinishedEvent("historical-run", scope.AgentId, ThreadExecutionOutcome.Succeeded, DateTimeOffset.UtcNow)
                ],
                AgentTuiEventDeliveryMode.Historical,
                ThreadJournalCursor.Start(1),
                new ThreadJournalCursor(1, 1),
                new ThreadJournalCursor(1, 1)),
            CancellationToken.None);

        reconciler.Snapshots.Should().HaveCount(2);
        GetPrivateField<AgentTuiSessionState>(app, "_state").Shell.PromptStatusText
            .Should().Be("snapshot: active");
        GetPrivateField<string>(app, "_activeThreadExecutionId").Should().Be("authoritative-run");
    }

    [Fact]
    public async Task SubmitReturn_DoesNotResurrectRunCompletedWhileSubmissionWasInFlight()
    {
        var scope = new AgentTuiRuntimeScope("agent-a", "session-a", "main");
        var runtime = new CancelRuntime(scope) { DelaySubmission = true };
        await using var app = HpdAgentTuiApp.Create(
            runtime,
            new DirectAgentTuiExecutionTarget(scope),
            static builder => builder.AddAgentTuiDefaults(),
            new TestTerminal(80, 24));
        InvokePrivate(app, "RebuildShell", new DirectAgentTuiExecutionTarget(scope), "Connected.");
        SetPrivateField(app, "_scopeIsDurable", true);
        var input = new UserMessagesInputEvent
        {
            Messages = [new Microsoft.Extensions.AI.ChatMessage(Microsoft.Extensions.AI.ChatRole.User, "hello")],
            AgentId = scope.AgentId,
            SessionId = scope.SessionId,
            ThreadId = scope.ThreadId
        };

        var submission = InvokePrivate<Task>(app, "SubmitInputAsync", new DirectAgentTuiExecutionTarget(scope), input, null!);
        await runtime.SubmissionStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await InvokePrivate<Task>(
            app,
            "OnAgentEventBatchAsync",
            new AgentTuiEventBatch(
                [
                    new ThreadExecutionStartedEvent("fast-run", scope.AgentId, DateTimeOffset.UtcNow),
                    new ThreadExecutionFinishedEvent("fast-run", scope.AgentId, ThreadExecutionOutcome.Succeeded, DateTimeOffset.UtcNow)
                ],
                AgentTuiEventDeliveryMode.Live,
                new ThreadJournalCursor(1, 1),
                new ThreadJournalCursor(1, 2),
                new ThreadJournalCursor(1, 2)),
            CancellationToken.None);
        runtime.CompleteSubmission("fast-run");
        await submission;

        GetPrivateFieldValue<string?>(app, "_activeThreadExecutionId").Should().BeNull();
        GetPrivateFieldValue<bool>(app, "_inputSubmissionPending").Should().BeFalse();
    }

    [Fact]
    public async Task CatchUpLifecycleEvents_UpdateLiveControlState()
    {
        var scope = new AgentTuiRuntimeScope("agent-a", "session-a", "main");
        var runtime = new CancelRuntime(scope);
        await using var app = HpdAgentTuiApp.Create(
            runtime,
            new DirectAgentTuiExecutionTarget(scope),
            static builder => builder.AddAgentTuiDefaults(),
            new TestTerminal(80, 24));
        InvokePrivate(app, "RebuildShell", new DirectAgentTuiExecutionTarget(scope), "Connected.");

        await InvokePrivate<Task>(
            app,
            "OnAgentEventBatchAsync",
            new AgentTuiEventBatch(
                [new ThreadExecutionStartedEvent("catch-up-run", scope.AgentId, DateTimeOffset.UtcNow)],
                AgentTuiEventDeliveryMode.CatchUp,
                new ThreadJournalCursor(1, 1),
                new ThreadJournalCursor(1, 1),
                new ThreadJournalCursor(1, 1)),
            CancellationToken.None);
        GetPrivateField<string>(app, "_activeThreadExecutionId").Should().Be("catch-up-run");

        await InvokePrivate<Task>(
            app,
            "OnAgentEventBatchAsync",
            new AgentTuiEventBatch(
                [new ThreadExecutionFinishedEvent("catch-up-run", scope.AgentId, ThreadExecutionOutcome.Succeeded, DateTimeOffset.UtcNow)],
                AgentTuiEventDeliveryMode.CatchUp,
                new ThreadJournalCursor(1, 2),
                new ThreadJournalCursor(1, 2),
                new ThreadJournalCursor(1, 2)),
            CancellationToken.None);
        GetPrivateFieldValue<string?>(app, "_activeThreadExecutionId").Should().BeNull();
    }

    [Fact]
    public async Task RequestDialog_DoesNotBlockEventProjection()
    {
        var scope = new AgentTuiRuntimeScope("agent-a", "session-a", "main");
        var runtime = new CancelRuntime(scope);
        var handler = new BlockingInteractionHandler();
        await using var app = HpdAgentTuiApp.Create(
            runtime,
            new DirectAgentTuiExecutionTarget(scope),
            builder => builder
                .AddInteractionHandler<PermissionRequestEvent>("blocking", handler)
                .AddAgentTuiDefaults(),
            new TestTerminal(80, 24));
        InvokePrivate(app, "RebuildShell", new DirectAgentTuiExecutionTarget(scope), "Connected.");
        InvokePrivate(app, "StartObserver", new DirectAgentTuiExecutionTarget(scope), CancellationToken.None);
        var request = new PermissionRequestEvent(
            "permission-1", "test", "function", null, "call-1", null)
        {
            SessionId = scope.SessionId,
            ThreadId = scope.ThreadId,
            ThreadExecutionId = "run-1"
        };

        await InvokePrivate<Task>(
            app,
            "OnAgentEventBatchAsync",
            new AgentTuiEventBatch(
                [request],
                AgentTuiEventDeliveryMode.Live,
                new ThreadJournalCursor(1, 1),
                new ThreadJournalCursor(1, 1),
                new ThreadJournalCursor(1, 1)),
            CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(2));
        await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        await InvokePrivate<Task>(
            app,
            "OnAgentEventBatchAsync",
            new AgentTuiEventBatch(
                [new AgentRequestTerminatedEvent(
                    request.RequestId,
                    request.SourceName,
                    AgentRequestTerminalKind.Cancelled,
                    "cancelled",
                    DateTimeOffset.UtcNow)
                {
                    SessionId = scope.SessionId,
                    ThreadId = scope.ThreadId,
                    ThreadExecutionId = "run-1"
                }],
                AgentTuiEventDeliveryMode.Live,
                new ThreadJournalCursor(1, 2),
                new ThreadJournalCursor(1, 2),
                new ThreadJournalCursor(1, 2)),
            CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(2));

        GetPrivateFieldValue<ThreadJournalCursor>(app, "_appliedCursor")
            .Should().Be(new ThreadJournalCursor(1, 2));
        await handler.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task DoubleEscape_WithActiveExecution_RequestsInterruptAndMarksActivityCancelling()
    {
        var scope = new AgentTuiRuntimeScope("agent-a", "session-a", "main");
        var runtime = new CancelRuntime(scope)
        {
            ActiveExecution = new AgentTuiThreadExecution(
                "run-123456789",
                scope.AgentId,
                scope.SessionId,
                scope.ThreadId,
                "active",
                DateTimeOffset.UtcNow)
        };
        await using var app = HpdAgentTuiApp.Create(
            runtime,
            new DirectAgentTuiExecutionTarget(scope),
            static builder => builder.AddAgentTuiDefaults(),
            new TestTerminal(80, 24));

        InvokePrivate(app, "RebuildShell", new DirectAgentTuiExecutionTarget(scope), "Connected.");

        var handled = InvokePrivate<bool>(
            app,
            "TryExecuteShortcut",
            new KeyEvent(KeyCode.Escape));

        handled.Should().BeTrue();
        await runtime.ActiveExecutionRequested.Task.WaitAsync(TimeSpan.FromSeconds(2));
        runtime.Interrupted.Task.IsCompleted.Should().BeFalse();

        var state = GetPrivateField<AgentTuiSessionState>(app, "_state");
        state.Shell.PromptStatusText.Should().Be("state: running | press Esc again to cancel execution run-1234");

        InvokePrivate<bool>(
            app,
            "TryExecuteShortcut",
            new KeyEvent(KeyCode.Escape)).Should().BeTrue();

        await runtime.Interrupted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        runtime.CancelledExecutionId.Should().Be("run-123456789");

        var entries = state.Shell.Transcript.Snapshot().Entries;
        entries.Select(static entry => entry.Cell).OfType<RunStatusCell>().Should().BeEmpty();
        state.Shell.Activities.Activities.Should().ContainSingle(activity =>
            activity.Label == "execution run-1234 cancelling" &&
            activity.State == ActivityState.Running &&
            activity.Severity == ActivitySeverity.Warning);
        state.Shell.PromptStatusText.Should().Be("state: cancelling | execution: run-1234");
    }

    [Fact]
    public async Task Escape_AfterConfirmationExpires_RearmsWithoutInterrupting()
    {
        var scope = new AgentTuiRuntimeScope("agent-a", "session-a", "main");
        var runtime = new CancelRuntime(scope)
        {
            ActiveExecution = new AgentTuiThreadExecution(
                "run-123456789",
                scope.AgentId,
                scope.SessionId,
                scope.ThreadId,
                "active",
                DateTimeOffset.UtcNow)
        };
        await using var app = HpdAgentTuiApp.Create(
            runtime,
            new DirectAgentTuiExecutionTarget(scope),
            static builder => builder.AddAgentTuiDefaults(),
            new TestTerminal(80, 24));
        InvokePrivate(app, "RebuildShell", new DirectAgentTuiExecutionTarget(scope), "Connected.");

        InvokePrivate<bool>(app, "TryExecuteShortcut", new KeyEvent(KeyCode.Escape)).Should().BeTrue();
        await runtime.ActiveExecutionRequested.Task.WaitAsync(TimeSpan.FromSeconds(2));
        SetPrivateField(app, "_cancelConfirmationExpiresAt", DateTimeOffset.UtcNow.AddSeconds(-1));

        InvokePrivate<bool>(app, "TryExecuteShortcut", new KeyEvent(KeyCode.Escape)).Should().BeTrue();
        await Task.Delay(50);

        runtime.Interrupted.Task.IsCompleted.Should().BeFalse();
        GetPrivateField<AgentTuiSessionState>(app, "_state").Shell.PromptStatusText
            .Should().Be("state: running | press Esc again to cancel execution run-1234");
    }

    [Fact]
    public async Task Escape_WithoutActiveExecution_DoesNotRequestInterrupt()
    {
        var scope = new AgentTuiRuntimeScope("agent-a", "session-a", "main");
        var runtime = new CancelRuntime(scope);
        await using var app = HpdAgentTuiApp.Create(
            runtime,
            new DirectAgentTuiExecutionTarget(scope),
            static builder => builder.AddAgentTuiDefaults(),
            new TestTerminal(80, 24));

        InvokePrivate(app, "RebuildShell", new DirectAgentTuiExecutionTarget(scope), "Connected.");

        var handled = InvokePrivate<bool>(
            app,
            "TryExecuteShortcut",
            new KeyEvent(KeyCode.Escape));

        handled.Should().BeTrue();
        await runtime.ActiveExecutionRequested.Task.WaitAsync(TimeSpan.FromSeconds(2));
        runtime.Interrupted.Task.IsCompleted.Should().BeFalse();

        var state = GetPrivateField<AgentTuiSessionState>(app, "_state");
        var entries = state.Shell.Transcript.Snapshot().Entries;
        entries.Select(static entry => entry.Cell).OfType<RunStatusCell>().Should().BeEmpty();
    }

    [Fact]
    public async Task Escape_WithActivePage_GoesBackWithoutRequestingInterrupt()
    {
        var scope = new AgentTuiRuntimeScope("agent-a", "session-a", "main");
        var runtime = new CancelRuntime(scope);
        await using var app = HpdAgentTuiApp.Create(
            runtime,
            new DirectAgentTuiExecutionTarget(scope),
            static builder => builder.AddAgentTuiDefaults(),
            new TestTerminal(80, 24));

        InvokePrivate(app, "RebuildShell", new DirectAgentTuiExecutionTarget(scope), "Connected.");
        var state = GetPrivateField<AgentTuiSessionState>(app, "_state");
        state.Shell.Navigation.GoToPage("hpd.help");

        var handled = InvokePrivate<bool>(
            app,
            "TryExecuteShortcut",
            new KeyEvent(KeyCode.Escape));

        handled.Should().BeTrue();
        state.Shell.Navigation.IsTranscriptActive.Should().BeTrue();
        runtime.ActiveExecutionRequested.Task.IsCompleted.Should().BeFalse();
        runtime.Interrupted.Task.IsCompleted.Should().BeFalse();
    }

    [Fact]
    public async Task Escape_WithOpenDialog_IsLeftForDialogInputHandling()
    {
        var scope = new AgentTuiRuntimeScope("agent-a", "session-a", "main");
        var runtime = new CancelRuntime(scope);
        await using var app = HpdAgentTuiApp.Create(
            runtime,
            new DirectAgentTuiExecutionTarget(scope),
            static builder => builder.AddAgentTuiDefaults(),
            new TestTerminal(80, 24));

        InvokePrivate(app, "RebuildShell", new DirectAgentTuiExecutionTarget(scope), "Connected.");
        var state = GetPrivateField<AgentTuiSessionState>(app, "_state");
        var dialogs = GetPrivateField<AgentTuiDialogService>(app, "_dialogs");
        var pending = dialogs.InputAsync("Session title (optional)", allowEmpty: true);

        var handled = InvokePrivate<bool>(
            app,
            "TryExecuteShortcut",
            new KeyEvent(KeyCode.Escape));

        handled.Should().BeFalse();
        state.Shell.Navigation.Back().Should().BeTrue();
        var result = await pending.WaitAsync(TimeSpan.FromSeconds(2));
        result.IsDismissed.Should().BeTrue();
        dialogs.HasOpenDialog.Should().BeFalse();
        runtime.ActiveExecutionRequested.Task.IsCompleted.Should().BeFalse();
        runtime.Interrupted.Task.IsCompleted.Should().BeFalse();
    }

    [Fact]
    public async Task Escape_WithAutocompleteVisible_IsLeftForPromptInputHandling()
    {
        var scope = new AgentTuiRuntimeScope("agent-a", "session-a", "main");
        var runtime = new CancelRuntime(scope)
        {
            ActiveExecution = new AgentTuiThreadExecution(
                "run-123456789",
                scope.AgentId,
                scope.SessionId,
                scope.ThreadId,
                "active",
                DateTimeOffset.UtcNow)
        };
        await using var app = HpdAgentTuiApp.Create(
            runtime,
            new DirectAgentTuiExecutionTarget(scope),
            static builder => builder.AddAgentTuiDefaults(),
            new TestTerminal(80, 24));

        InvokePrivate(app, "RebuildShell", new DirectAgentTuiExecutionTarget(scope), "Connected.");
        var prompt = GetPrivateField<PromptView>(app, "_prompt");
        prompt.Controller.SetDraft("/");
        prompt.Controller.Autocomplete.Should().NotBeNull();
        prompt.Controller.Autocomplete!.SuggestionCount.Should().BeGreaterThan(0);

        var handled = InvokePrivate<bool>(
            app,
            "TryExecuteShortcut",
            new KeyEvent(KeyCode.Escape));

        handled.Should().BeFalse();
        runtime.ActiveExecutionRequested.Task.IsCompleted.Should().BeFalse();
        runtime.Interrupted.Task.IsCompleted.Should().BeFalse();

        prompt.Controller.HandleInput(new KeyEvent(KeyCode.Escape)).Should().BeTrue();
        prompt.Controller.Autocomplete.SuggestionCount.Should().Be(0);
        prompt.Model.Value.Should().Be("/");
    }

    private static void InvokePrivate(
        HpdAgentTuiApp app,
        string methodName,
        params object[] args)
        => InvokePrivate<object?>(app, methodName, args);

    private static T InvokePrivate<T>(
        HpdAgentTuiApp app,
        string methodName,
        params object[] args)
    {
        var method = typeof(HpdAgentTuiApp).GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.NonPublic);
        method.Should().NotBeNull();
        return (T)method!.Invoke(app, args)!;
    }

    private static T GetPrivateField<T>(HpdAgentTuiApp app, string fieldName)
        where T : class
    {
        var field = typeof(HpdAgentTuiApp).GetField(
            fieldName,
            BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull();
        return field!.GetValue(app).Should().BeOfType<T>().Subject;
    }

    private static T GetPrivateFieldValue<T>(HpdAgentTuiApp app, string fieldName)
    {
        var field = typeof(HpdAgentTuiApp).GetField(
            fieldName,
            BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull();
        return (T)field!.GetValue(app)!;
    }

    private static void SetPrivateField<T>(HpdAgentTuiApp app, string fieldName, T value)
    {
        var field = typeof(HpdAgentTuiApp).GetField(
            fieldName,
            BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull();
        field!.SetValue(app, value);
    }

    private sealed class CancelRuntime : IHpdAgentTuiRuntime
    {
        private readonly AgentTuiRuntimeScope _scope;

        public CancelRuntime(AgentTuiRuntimeScope scope)
        {
            _scope = scope;
        }

        public AgentTuiThreadExecution? ActiveExecution { get; init; }

        public bool InitialIsDurable { get; init; } = true;

        public bool DelaySubmission { get; init; }

        public TaskCompletionSource SubmissionStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private TaskCompletionSource<AgentTuiSubmitResult> DelayedSubmission { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public List<string> Calls { get; } = [];

        public TaskCompletionSource<ThreadJournalCursor> ObserverStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string? CancelledExecutionId { get; private set; }

        public TaskCompletionSource ActiveExecutionRequested { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Interrupted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<AgentTuiTargetResolution> ResolveInitialTargetAsync(
            AgentTuiExecutionTarget? requested,
            CancellationToken cancellationToken = default)
        {
            Calls.Add("resolve");
            return Task.FromResult(new AgentTuiTargetResolution(
                requested ?? new DirectAgentTuiExecutionTarget(_scope), InitialIsDurable));
        }

        public Task<AgentTuiExecutionTarget> EnsureDurableTargetAsync(
            AgentTuiExecutionTarget target,
            CancellationToken cancellationToken = default)
        {
            Calls.Add("ensure");
            return Task.FromResult(target);
        }

        public async IAsyncEnumerable<AgentTuiEventBatch> ObserveAsync(
            AgentTuiExecutionTarget target,
            ThreadJournalCursor after,
            ThreadJournalCursor initialObservedCursor,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Calls.Add("observe");
            ObserverStarted.TrySetResult(after);
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            yield break;
        }

        public Task<AgentTuiSubmitResult> SubmitInputAsync(
            AgentTuiExecutionTarget target,
            AgentInputEvent input,
            CancellationToken cancellationToken = default)
        {
            var scope = target.Scope;
            Calls.Add("submit");
            SubmissionStarted.TrySetResult();
            if (DelaySubmission)
            {
                return DelayedSubmission.Task;
            }
            return Task.FromResult(new AgentTuiSubmitResult(
                AgentInputDisposition.Queued,
                (ActiveExecution?.ThreadExecutionId ?? "run"),
                ActiveExecution ?? new AgentTuiThreadExecution("run", scope.AgentId, scope.SessionId, scope.ThreadId, "active", DateTimeOffset.UtcNow)));
        }

        public Task<AgentTuiSubmitResult> CancelExecutionAsync(
            AgentTuiRuntimeScope scope, string threadExecutionId, CancellationToken cancellationToken = default)
        {
            Calls.Add("cancel");
            CancelledExecutionId = threadExecutionId;
            Interrupted.TrySetResult();
            return Task.FromResult(new AgentTuiSubmitResult(
                AgentInputDisposition.Accepted,
                threadExecutionId,
                ActiveExecution));
        }

        public void CompleteSubmission(string threadExecutionId)
            => DelayedSubmission.SetResult(new AgentTuiSubmitResult(
                AgentInputDisposition.Queued,
                threadExecutionId,
                new AgentTuiThreadExecution(
                    threadExecutionId,
                    _scope.AgentId,
                    _scope.SessionId,
                    _scope.ThreadId,
                    "active",
                    DateTimeOffset.UtcNow)));

        public Task<AgentRespondResult> AnswerRequestAsync(
            AgentTuiRuntimeScope scope,
            AgentEvent response,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new AgentRespondResult(AgentRespondStatus.Accepted, ((IAgentResponseEvent)response).RequestId));

        public Task<AgentTuiThreadState> GetThreadStateAsync(
            AgentTuiRuntimeScope scope,
            CancellationToken cancellationToken = default)
        {
            Calls.Add("state");
            ActiveExecutionRequested.TrySetResult();
            return Task.FromResult(new AgentTuiThreadState(ThreadJournalCursor.Start(1), ActiveExecution, []));
        }
    }

    private sealed class BlockingInteractionHandler : AgentTuiInteractionHandler<PermissionRequestEvent>
    {
        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Cancelled { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<AgentTuiInteractionResult> HandleAsync(
            AgentTuiInteractionContext<PermissionRequestEvent> context,
            CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return AgentTuiInteractionResult.NoOp;
            }
            catch (OperationCanceledException)
            {
                Cancelled.TrySetResult();
                throw;
            }
        }
    }

    private sealed class RecordingThreadStateReconciler : IAgentTuiThreadStateReconciler
    {
        public List<AgentTuiThreadState> Snapshots { get; } = [];

        public ValueTask ReconcileAsync(
            AgentTuiThreadState threadState,
            AgentTuiEventContext context,
            CancellationToken cancellationToken)
        {
            Snapshots.Add(threadState);
            context.Shell.PromptStatusText = threadState.ActiveExecution is null
                ? "snapshot: idle"
                : "snapshot: active";
            return ValueTask.CompletedTask;
        }
    }

    private sealed class StaleHistoricalFooterHandler : AgentTuiEventHandler<ThreadExecutionStartedEvent>
    {
        public override ValueTask HandleAsync(
            ThreadExecutionStartedEvent evt,
            AgentTuiEventContext context,
            CancellationToken cancellationToken)
        {
            context.Shell.PromptStatusText = "event: running";
            return ValueTask.CompletedTask;
        }
    }

    private sealed class MarkdownBoundaryObserver : AgentTuiEventHandler<ToolCallStartEvent>
    {
        internal bool SawPrecedingMarkdown { get; private set; }
        public override ValueTask HandleAsync(ToolCallStartEvent evt, AgentTuiEventContext context,
            CancellationToken cancellationToken)
        {
            SawPrecedingMarkdown = context.Shell.Transcript.Snapshot().Entries
                .Any(static entry => entry.EntryKey == "assistant:commentary");
            return ValueTask.CompletedTask;
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while (!condition()) await Task.Delay(10, timeout.Token);
    }

    private sealed class BlockingMarkdownParser : IMarkdownDocumentParser, IDisposable
    {
        private readonly MarkdownDocumentParser _inner = new();
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal ManualResetEventSlim Release { get; } = new(false);

        public MarkdownDocumentSnapshot Parse(string source, MarkdownParseOptions options)
        {
            if (source.Length > 0)
            {
                Entered.TrySetResult();
                if (!Release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Markdown parser was not released.");
            }
            return _inner.Parse(source, options);
        }

        public void Dispose() { Release.Set(); Release.Dispose(); }
    }

    private sealed class BlockingInputTerminal : ITerminal, ITerminalInput, IManagedTerminalCapabilitySource
    {
        private readonly Channel<TerminalInputEvent> _input = Channel.CreateUnbounded<TerminalInputEvent>();
        private readonly TerminalSize _size;
        internal BlockingInputTerminal(int width, int height) => _size = new(width, height);
        public ManagedTerminalCapabilityProfile ManagedTerminalCapabilities => ManagedTerminalCapabilityProfile.Verified;
        public ITerminalInput Input => this;
        public TerminalSize GetSize() => _size;
        internal void Enqueue(TerminalInputEvent input) => _input.Writer.TryWrite(input);
        public ValueTask<TerminalInputEvent> ReadAsync(CancellationToken cancellationToken = default)
            => _input.Reader.ReadAsync(cancellationToken);
        public void Write(ReadOnlySpan<char> text) { }
        public void Flush() { }
        public void HideCursor() { }
        public void ShowCursor() { }
        public void Dispose() => _input.Writer.TryComplete();
        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    }

    private sealed class TestTerminal : ITerminal, ITerminalInput, IManagedTerminalCapabilitySource
    {
        public ManagedTerminalCapabilityProfile ManagedTerminalCapabilities
            => ManagedTerminalCapabilityProfile.Verified;
        private readonly StringBuilder _output = new();
        private TerminalSize _size;

        public TestTerminal(int width, int height)
        {
            _size = new TerminalSize(width, height);
        }

        public TerminalSize GetSize() => _size;

        public void Write(ReadOnlySpan<char> text)
        {
            _output.Append(text);
        }

        public void Flush()
        {
        }

        public ITerminalInput Input => this;

        public ValueTask<TerminalInputEvent> ReadAsync(CancellationToken cancellationToken = default)
            => ValueTask.FromResult(TerminalInputEvent.Stop);

        public void HideCursor()
        {
        }

        public void ShowCursor()
        {
        }

        public void Dispose()
        {
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
