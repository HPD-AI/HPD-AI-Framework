using System.Reflection;
using System.Text;
using FluentAssertions;
using HPD.Agent;
using HPD.Agent.TUI.Application;
using HPD.Agent.TUI.Models;
using HPD.Agent.TUI.Runtime;
using HPD.TUI.Core;
using HPD.TUI.Terminal;

namespace HPD.Agent.TUI.Tests;

public sealed class HpdAgentTuiAppCancelTests
{
    [Fact]
    public async Task Escape_WithActiveRun_RequestsInterruptAndMarksTranscriptCancelling()
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
        var entries = new List<TranscriptEntry>();
        state.Shell.Transcript.CopyTo(entries);
        var runCell = entries
            .Select(static entry => entry.Cell)
            .OfType<RunStatusCell>()
            .Single();
        runCell.RuntimeRunId.Should().Be("run-123456789");
        runCell.State.Should().Be(TranscriptRunState.Cancelling);
        state.Shell.FooterText.Should().Be("state: cancelling");
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
        var entries = new List<TranscriptEntry>();
        state.Shell.Transcript.CopyTo(entries);
        entries.Select(static entry => entry.Cell).OfType<RunStatusCell>().Should().BeEmpty();
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

        public Task<AgentTuiRuntimeScope> EnsureScopeAsync(
            AgentTuiRuntimeScope? requested,
            CancellationToken cancellationToken = default)
            => Task.FromResult(requested ?? _scope);

        public async IAsyncEnumerable<AgentEvent> ObserveAsync(
            AgentTuiRuntimeScope scope,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        public Task SubmitInputAsync(
            AgentTuiRuntimeScope scope,
            AgentInputEvent input,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task InterruptAsync(
            AgentTuiRuntimeScope scope,
            string reason,
            CancellationToken cancellationToken = default)
        {
            InterruptReason = reason;
            Interrupted.SetResult();
            return Task.CompletedTask;
        }

        public Task RespondAsync(
            AgentTuiRuntimeScope scope,
            AgentEvent response,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<IReadOnlyList<AgentEvent>> GetThreadEventsAsync(
            AgentTuiRuntimeScope scope,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<AgentEvent>>([]);

        public Task<AgentTuiThreadRun?> GetActiveRunAsync(
            AgentTuiRuntimeScope scope,
            CancellationToken cancellationToken = default)
        {
            ActiveRunRequested.SetResult();
            return Task.FromResult(ActiveRun);
        }
    }

    private sealed class TestTerminal : ITerminal
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

        public bool TryReadKey(out KeyEvent key)
        {
            key = default;
            return false;
        }

        public void HideCursor()
        {
        }

        public void ShowCursor()
        {
        }

        public void Dispose()
        {
        }
    }
}
