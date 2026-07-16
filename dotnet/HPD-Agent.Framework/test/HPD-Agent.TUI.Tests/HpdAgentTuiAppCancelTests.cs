using System.Reflection;
using System.Text;
using FluentAssertions;
using HPD.Agent;
using HPD.Agent.TUI.Application;
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
    public async Task Escape_WithActiveRun_RequestsInterruptAndMarksActivityCancelling()
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
        await runtime.Interrupted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        runtime.InterruptReason.Should().Be("Cancelled from TUI.");

        var state = GetPrivateField<AgentTuiSessionState>(app, "_state");
        var entries = state.Shell.Transcript.Snapshot().Entries;
        entries.Select(static entry => entry.Cell).OfType<RunStatusCell>().Should().BeEmpty();
        state.Shell.Activities.Activities.Should().ContainSingle(activity =>
            activity.Label == "run run-1234 cancelling" &&
            activity.State == ActivityState.Running &&
            activity.Severity == ActivitySeverity.Warning);
        state.Shell.FooterText.Should().Be("state: cancelling | run: run-1234");
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

    private sealed class CancelRuntime : IHpdAgentTuiRuntime
    {
        private readonly AgentTuiRuntimeScope _scope;

        public CancelRuntime(AgentTuiRuntimeScope scope)
        {
            _scope = scope;
        }

        public AgentTuiThreadRun? ActiveRun { get; init; }

        public string? InterruptReason { get; private set; }

        public TaskCompletionSource ActiveRunRequested { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Interrupted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<AgentTuiScopeResolution> ResolveInitialScopeAsync(
            AgentTuiRuntimeScope? requested,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new AgentTuiScopeResolution(requested ?? _scope, IsDurable: true));

        public Task<AgentTuiRuntimeScope> EnsureDurableScopeAsync(
            AgentTuiRuntimeScope scope,
            CancellationToken cancellationToken = default)
            => Task.FromResult(scope);

        public async IAsyncEnumerable<AgentTuiEventBatch> ObserveAsync(
            AgentTuiRuntimeScope scope,
            long afterSequenceNumber,
            long initialObservedHead,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        public Task<AgentTuiSubmitResult> SubmitInputAsync(
            AgentTuiRuntimeScope scope,
            AgentInputEvent input,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new AgentTuiSubmitResult(
                ActiveRun ?? new AgentTuiThreadRun("run", scope.AgentId, scope.SessionId, scope.ThreadId, "active", DateTimeOffset.UtcNow)));

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
            ActiveRunRequested.SetResult();
            return Task.FromResult(new AgentTuiThreadState(0, ActiveRun, []));
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
