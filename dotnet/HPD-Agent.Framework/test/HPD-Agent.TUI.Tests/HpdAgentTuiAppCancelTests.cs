using System.Reflection;
using System.Text;
using FluentAssertions;
using HPD.Agent;
using HPD.Agent.TUI.Application;
using HPD.Agent.TUI.Composition;
using HPD.Agent.TUI.Models;
using HPD.Agent.TUI.Runtime;
using HPD.TUI.Core;
using HPD.TUI.Models;
using HPD.TUI.Terminal;
using HPD.TUI.Views;

namespace HPD.Agent.TUI.Tests;

public sealed class HpdAgentTuiAppCancelTests
{
    [Fact]
    public async Task FirstInput_PromotesAndHydratesTransientScopeBeforeObservationAndSubmission()
    {
        var scope = new AgentTuiRuntimeScope("agent-a", "pending-session", "main");
        var runtime = new CancelRuntime(scope) { InitialIsDurable = false };
        await using var app = HpdAgentTuiApp.Create(
            runtime,
            scope,
            static builder => builder.AddAgentTuiDefaults(),
            new TestTerminal(80, 24));
        InvokePrivate(app, "RebuildShell", scope, "Connected.");
        var input = new UserMessagesInputEvent
        {
            Messages = [new Microsoft.Extensions.AI.ChatMessage(Microsoft.Extensions.AI.ChatRole.User, "hello")],
            AgentId = scope.AgentId,
            SessionId = scope.SessionId,
            ThreadId = scope.ThreadId
        };

        await InvokePrivate<Task>(app, "SubmitInputAsync", scope, input, null!);
        var observed = await runtime.ObserverStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        observed.Should().Be(ThreadJournalCursor.Start(1));
        runtime.Calls.Should().ContainInOrder("ensure", "state", "observe", "submit");
        runtime.Calls.IndexOf("state").Should().BeLessThan(runtime.Calls.IndexOf("observe"));
        runtime.Calls.IndexOf("observe").Should().BeLessThan(runtime.Calls.IndexOf("submit"));
    }

    [Fact]
    public async Task Hydration_InvokesThreadStateReconcilerWithAuthoritativeSnapshot()
    {
        var scope = new AgentTuiRuntimeScope("agent-a", "session-a", "main");
        var activeRun = new AgentTuiThreadRun(
            "run-authoritative",
            scope.AgentId,
            scope.SessionId,
            scope.ThreadId,
            "active",
            DateTimeOffset.UtcNow);
        var runtime = new CancelRuntime(scope) { ActiveRun = activeRun };
        var reconciler = new RecordingThreadStateReconciler();
        await using var app = HpdAgentTuiApp.Create(
            runtime,
            scope,
            builder => builder
                .AddAgentTuiDefaults()
                .AddThreadStateReconciler(reconciler),
            new TestTerminal(80, 24));
        InvokePrivate(app, "RebuildShell", scope, "Connected.");

        var hydrated = await InvokePrivate<Task<bool>>(
            app,
            "HydrateThreadAsync",
            scope,
            CancellationToken.None);

        hydrated.Should().BeTrue();
        reconciler.Snapshots.Should().ContainSingle().Which.ActiveRun.Should().BeSameAs(activeRun);
        GetPrivateField<AgentTuiSessionState>(app, "_state").Shell.FooterText
            .Should().Be("snapshot: active");
    }

    [Fact]
    public async Task HistoricalBatch_ReappliesHydratedSnapshotAfterEventHandlers()
    {
        var scope = new AgentTuiRuntimeScope("agent-a", "session-a", "main");
        var runtime = new CancelRuntime(scope);
        var reconciler = new RecordingThreadStateReconciler();
        await using var app = HpdAgentTuiApp.Create(
            runtime,
            scope,
            builder => builder
                .AddAgentTuiDefaults()
                .AddEventHandler("test.stale-footer", new StaleHistoricalFooterHandler())
                .AddThreadStateReconciler(reconciler),
            new TestTerminal(80, 24));
        InvokePrivate(app, "RebuildShell", scope, "Connected.");
        (await InvokePrivate<Task<bool>>(
            app,
            "HydrateThreadAsync",
            scope,
            CancellationToken.None)).Should().BeTrue();

        await InvokePrivate<Task>(
            app,
            "OnAgentEventBatchAsync",
            new AgentTuiEventBatch(
                [new ThreadRunStartedEvent("historical-run", scope.AgentId, DateTimeOffset.UtcNow)],
                AgentTuiEventDeliveryMode.Historical,
                ThreadJournalCursor.Start(1),
                new ThreadJournalCursor(1, 1),
                new ThreadJournalCursor(1, 1)),
            CancellationToken.None);

        reconciler.Snapshots.Should().HaveCount(2);
        GetPrivateField<AgentTuiSessionState>(app, "_state").Shell.FooterText
            .Should().Be("snapshot: idle");
    }

    [Fact]
    public async Task DoubleEscape_WithActiveRun_RequestsInterruptAndMarksActivityCancelling()
    {
        var scope = new AgentTuiRuntimeScope("agent-a", "session-a", "main");
        var runtime = new CancelRuntime(scope)
        {
            ActiveRun = new AgentTuiThreadRun(
                "run-123456789",
                scope.AgentId,
                scope.SessionId,
                scope.ThreadId,
                "active",
                DateTimeOffset.UtcNow)
        };
        await using var app = HpdAgentTuiApp.Create(
            runtime,
            scope,
            static builder => builder.AddAgentTuiDefaults(),
            new TestTerminal(80, 24));

        InvokePrivate(app, "RebuildShell", scope, "Connected.");

        var handled = InvokePrivate<bool>(
            app,
            "TryExecuteShortcut",
            new KeyEvent(KeyCode.Escape));

        handled.Should().BeTrue();
        await runtime.ActiveRunRequested.Task.WaitAsync(TimeSpan.FromSeconds(2));
        runtime.Interrupted.Task.IsCompleted.Should().BeFalse();

        var state = GetPrivateField<AgentTuiSessionState>(app, "_state");
        state.Shell.FooterText.Should().Be("state: running | press Esc again to cancel run run-1234");

        InvokePrivate<bool>(
            app,
            "TryExecuteShortcut",
            new KeyEvent(KeyCode.Escape)).Should().BeTrue();

        await runtime.Interrupted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        runtime.InterruptReason.Should().Be("Cancelled from TUI.");

        var entries = state.Shell.Transcript.Snapshot().Entries;
        entries.Select(static entry => entry.Cell).OfType<RunStatusCell>().Should().BeEmpty();
        state.Shell.Activities.Activities.Should().ContainSingle(activity =>
            activity.Label == "run run-1234 cancelling" &&
            activity.State == ActivityState.Running &&
            activity.Severity == ActivitySeverity.Warning);
        state.Shell.FooterText.Should().Be("state: cancelling | run: run-1234");
    }

    [Fact]
    public async Task Escape_AfterConfirmationExpires_RearmsWithoutInterrupting()
    {
        var scope = new AgentTuiRuntimeScope("agent-a", "session-a", "main");
        var runtime = new CancelRuntime(scope)
        {
            ActiveRun = new AgentTuiThreadRun(
                "run-123456789",
                scope.AgentId,
                scope.SessionId,
                scope.ThreadId,
                "active",
                DateTimeOffset.UtcNow)
        };
        await using var app = HpdAgentTuiApp.Create(
            runtime,
            scope,
            static builder => builder.AddAgentTuiDefaults(),
            new TestTerminal(80, 24));
        InvokePrivate(app, "RebuildShell", scope, "Connected.");

        InvokePrivate<bool>(app, "TryExecuteShortcut", new KeyEvent(KeyCode.Escape)).Should().BeTrue();
        await runtime.ActiveRunRequested.Task.WaitAsync(TimeSpan.FromSeconds(2));
        SetPrivateField(app, "_cancelConfirmationExpiresAt", DateTimeOffset.UtcNow.AddSeconds(-1));

        InvokePrivate<bool>(app, "TryExecuteShortcut", new KeyEvent(KeyCode.Escape)).Should().BeTrue();
        await Task.Delay(50);

        runtime.Interrupted.Task.IsCompleted.Should().BeFalse();
        GetPrivateField<AgentTuiSessionState>(app, "_state").Shell.FooterText
            .Should().Be("state: running | press Esc again to cancel run run-1234");
    }

    [Fact]
    public async Task Escape_WithoutActiveRun_DoesNotRequestInterrupt()
    {
        var scope = new AgentTuiRuntimeScope("agent-a", "session-a", "main");
        var runtime = new CancelRuntime(scope);
        await using var app = HpdAgentTuiApp.Create(
            runtime,
            scope,
            static builder => builder.AddAgentTuiDefaults(),
            new TestTerminal(80, 24));

        InvokePrivate(app, "RebuildShell", scope, "Connected.");

        var handled = InvokePrivate<bool>(
            app,
            "TryExecuteShortcut",
            new KeyEvent(KeyCode.Escape));

        handled.Should().BeTrue();
        await runtime.ActiveRunRequested.Task.WaitAsync(TimeSpan.FromSeconds(2));
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
            scope,
            static builder => builder.AddAgentTuiDefaults(),
            new TestTerminal(80, 24));

        InvokePrivate(app, "RebuildShell", scope, "Connected.");
        var state = GetPrivateField<AgentTuiSessionState>(app, "_state");
        state.Shell.Navigation.GoToPage("hpd.help");

        var handled = InvokePrivate<bool>(
            app,
            "TryExecuteShortcut",
            new KeyEvent(KeyCode.Escape));

        handled.Should().BeTrue();
        state.Shell.Navigation.IsTranscriptActive.Should().BeTrue();
        runtime.ActiveRunRequested.Task.IsCompleted.Should().BeFalse();
        runtime.Interrupted.Task.IsCompleted.Should().BeFalse();
    }

    [Fact]
    public async Task Escape_WithOpenDialog_IsLeftForDialogInputHandling()
    {
        var scope = new AgentTuiRuntimeScope("agent-a", "session-a", "main");
        var runtime = new CancelRuntime(scope);
        await using var app = HpdAgentTuiApp.Create(
            runtime,
            scope,
            static builder => builder.AddAgentTuiDefaults(),
            new TestTerminal(80, 24));

        InvokePrivate(app, "RebuildShell", scope, "Connected.");
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
        runtime.ActiveRunRequested.Task.IsCompleted.Should().BeFalse();
        runtime.Interrupted.Task.IsCompleted.Should().BeFalse();
    }

    [Fact]
    public async Task Escape_WithAutocompleteVisible_IsLeftForPromptInputHandling()
    {
        var scope = new AgentTuiRuntimeScope("agent-a", "session-a", "main");
        var runtime = new CancelRuntime(scope)
        {
            ActiveRun = new AgentTuiThreadRun(
                "run-123456789",
                scope.AgentId,
                scope.SessionId,
                scope.ThreadId,
                "active",
                DateTimeOffset.UtcNow)
        };
        await using var app = HpdAgentTuiApp.Create(
            runtime,
            scope,
            static builder => builder.AddAgentTuiDefaults(),
            new TestTerminal(80, 24));

        InvokePrivate(app, "RebuildShell", scope, "Connected.");
        var prompt = GetPrivateField<PromptView>(app, "_prompt");
        prompt.Controller.SetDraft("/");
        prompt.Controller.Autocomplete.Should().NotBeNull();
        prompt.Controller.Autocomplete!.SuggestionCount.Should().BeGreaterThan(0);

        var handled = InvokePrivate<bool>(
            app,
            "TryExecuteShortcut",
            new KeyEvent(KeyCode.Escape));

        handled.Should().BeFalse();
        runtime.ActiveRunRequested.Task.IsCompleted.Should().BeFalse();
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

        public AgentTuiThreadRun? ActiveRun { get; init; }

        public bool InitialIsDurable { get; init; } = true;

        public List<string> Calls { get; } = [];

        public TaskCompletionSource<ThreadJournalCursor> ObserverStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string? InterruptReason { get; private set; }

        public TaskCompletionSource ActiveRunRequested { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Interrupted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<AgentTuiScopeResolution> ResolveInitialScopeAsync(
            AgentTuiRuntimeScope? requested,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new AgentTuiScopeResolution(requested ?? _scope, InitialIsDurable));

        public Task<AgentTuiRuntimeScope> EnsureDurableScopeAsync(
            AgentTuiRuntimeScope scope,
            CancellationToken cancellationToken = default)
        {
            Calls.Add("ensure");
            return Task.FromResult(scope);
        }

        public async IAsyncEnumerable<AgentTuiEventBatch> ObserveAsync(
            AgentTuiRuntimeScope scope,
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
            AgentTuiRuntimeScope scope,
            AgentInputEvent input,
            CancellationToken cancellationToken = default)
        {
            Calls.Add("submit");
            return Task.FromResult(new AgentTuiSubmitResult(
                ActiveRun ?? new AgentTuiThreadRun("run", scope.AgentId, scope.SessionId, scope.ThreadId, "active", DateTimeOffset.UtcNow)));
        }

        public Task<AgentTuiInterruptResult> InterruptAsync(
            AgentTuiRuntimeScope scope,
            string? expectedRuntimeRunId,
            string reason,
            CancellationToken cancellationToken = default)
        {
            InterruptReason = reason;
            Interrupted.SetResult();
            return Task.FromResult(new AgentTuiInterruptResult(AgentTuiInterruptStatus.Accepted, ActiveRun));
        }

        public Task AnswerRequestAsync(
            AgentTuiRuntimeScope scope,
            AgentEvent response,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<AgentTuiThreadState> GetThreadStateAsync(
            AgentTuiRuntimeScope scope,
            CancellationToken cancellationToken = default)
        {
            Calls.Add("state");
            ActiveRunRequested.TrySetResult();
            return Task.FromResult(new AgentTuiThreadState(ThreadJournalCursor.Start(1), ActiveRun, []));
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
            context.Shell.FooterText = threadState.ActiveRun is null
                ? "snapshot: idle"
                : "snapshot: active";
            return ValueTask.CompletedTask;
        }
    }

    private sealed class StaleHistoricalFooterHandler : AgentTuiEventHandler<ThreadRunStartedEvent>
    {
        public override ValueTask HandleAsync(
            ThreadRunStartedEvent evt,
            AgentTuiEventContext context,
            CancellationToken cancellationToken)
        {
            context.Shell.FooterText = "event: running";
            return ValueTask.CompletedTask;
        }
    }

    private sealed class TestTerminal : ITerminal, ITerminalInput
    {
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
