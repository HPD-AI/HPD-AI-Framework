using FluentAssertions;
using HPD.Agent;
using HPD.Agent.TUI.Runtime;
using HPD.TUI.Core;
using HPD.TUI.Terminal;
using System.Reflection;
using System.Text;

namespace HPD.Agent.TUI.Tests;

public sealed class RunConfigComposerTests
{
    [Fact]
    public async Task SubmittedPrompt_CarriesComposedRunConfig()
    {
        var scope = new AgentTuiRuntimeScope("agent-a", "session-a", "main");
        var runtime = new CapturingRuntime(scope);
        await using var app = HpdAgentTuiApp.Create(
            runtime,
            scope,
            builder => builder
                .AddAgentTuiDefaults()
                .SetRunConfigComposer(context =>
                {
                    context.Scope.Should().BeSameAs(scope);
                    context.Prompt.Should().Be("hello");

                    return new AgentRunConfig
                    {
                        ProviderKey = "openrouter",
                        ModelId = "deepseek/deepseek-chat"
                    };
                }),
            new TestTerminal(80, 24));

        InvokePrivate(app, "RebuildShell", scope, "Connected.");
        InvokePrivate(app, "SubmitPrompt", "hello".AsMemory());

        runtime.LastInput.Should().BeOfType<UserMessagesInputEvent>()
            .Which.RunConfig.Should().NotBeNull();
        runtime.LastInput!.RunConfig!.ProviderKey.Should().Be("openrouter");
        runtime.LastInput.RunConfig.ModelId.Should().Be("deepseek/deepseek-chat");
    }

    [Fact]
    public async Task SubmittedPrompt_DoesNotAppendSenderOnlyUserMessage()
    {
        var scope = new AgentTuiRuntimeScope("agent-a", "session-a", "main");
        var runtime = new CapturingRuntime(scope);
        await using var app = HpdAgentTuiApp.Create(
            runtime,
            scope,
            static builder => builder.AddAgentTuiDefaults(),
            new TestTerminal(80, 24));

        InvokePrivate(app, "RebuildShell", scope, "Connected.");
        InvokePrivate(app, "SubmitPrompt", "hello".AsMemory());

        runtime.SubmitCount.Should().Be(1);
        var state = GetPrivateField<HPD.Agent.TUI.Application.AgentTuiSessionState>(app, "_state");
        state.Shell.Transcript.Snapshot().Entries
            .Select(static entry => entry.Cell)
            .OfType<HPD.Agent.TUI.Models.UserMessageCell>()
            .Should()
            .BeEmpty("the committed event stream is the only transcript authority");
    }

    [Fact]
    public async Task SubmittedPrompt_WhenRunIsActive_ShowsBusyNoticeWithoutSubmitting()
    {
        var scope = new AgentTuiRuntimeScope("agent-a", "session-a", "main");
        var runtime = new CapturingRuntime(scope);
        await using var app = HpdAgentTuiApp.Create(
            runtime,
            scope,
            static builder => builder.AddAgentTuiDefaults(),
            new TestTerminal(80, 24));

        InvokePrivate(app, "RebuildShell", scope, "Connected.");
        await InvokePrivateAsync(
            app,
            "OnAgentEventAsync",
            new ThreadRunStartedEvent("run-1", scope.AgentId, DateTimeOffset.UtcNow)
            {
                SessionId = scope.SessionId,
                ThreadId = scope.ThreadId
            },
            CancellationToken.None);
        InvokePrivate(app, "SubmitPrompt", "hello".AsMemory());

        runtime.SubmitCount.Should().Be(0);
        var state = GetPrivateField<HPD.Agent.TUI.Application.AgentTuiSessionState>(app, "_state");
        state.Shell.Transcript.Snapshot().Entries
            .Select(static entry => entry.Cell)
            .OfType<HPD.Agent.TUI.Models.NoticeCell>()
            .Should()
            .ContainSingle(cell => cell.Title == "Thread is busy");
    }

    private static void InvokePrivate(
        HpdAgentTuiApp app,
        string methodName,
        params object[] args)
    {
        var method = typeof(HpdAgentTuiApp).GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.NonPublic);
        method.Should().NotBeNull();
        method!.Invoke(app, args);
    }

    private static async Task InvokePrivateAsync(
        HpdAgentTuiApp app,
        string methodName,
        params object[] args)
    {
        var method = typeof(HpdAgentTuiApp).GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.NonPublic);
        method.Should().NotBeNull();
        await ((Task)method!.Invoke(app, args)!).ConfigureAwait(false);
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

    private sealed class CapturingRuntime : IHpdAgentTuiRuntime
    {
        private readonly AgentTuiRuntimeScope _scope;

        public CapturingRuntime(AgentTuiRuntimeScope scope)
        {
            _scope = scope;
        }

        public AgentInputEvent? LastInput { get; private set; }
        public int SubmitCount { get; private set; }

        public Task<AgentTuiScopeResolution> ResolveInitialScopeAsync(
            AgentTuiRuntimeScope? requested,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new AgentTuiScopeResolution(requested ?? _scope, IsDurable: true));

        public Task<AgentTuiRuntimeScope> EnsureDurableScopeAsync(
            AgentTuiRuntimeScope scope,
            CancellationToken cancellationToken = default)
            => Task.FromResult(scope);

        public async IAsyncEnumerable<AgentEvent> ObserveAsync(
            AgentTuiRuntimeScope scope,
            long afterSequenceNumber,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        public Task<AgentTuiSubmitResult> SubmitInputAsync(
            AgentTuiRuntimeScope scope,
            AgentInputEvent input,
            CancellationToken cancellationToken = default)
        {
            SubmitCount++;
            LastInput = input;
            return Task.FromResult(new AgentTuiSubmitResult(
                new AgentTuiThreadRun("run", scope.AgentId, scope.SessionId, scope.ThreadId, "active", DateTimeOffset.UtcNow)));
        }

        public Task<AgentTuiInterruptResult> InterruptAsync(
            AgentTuiRuntimeScope scope,
            string? expectedRuntimeRunId,
            string reason,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new AgentTuiInterruptResult(AgentTuiInterruptStatus.Accepted));

        public Task AnswerRequestAsync(
            AgentTuiRuntimeScope scope,
            AgentEvent response,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<AgentTuiThreadState> GetThreadStateAsync(
            AgentTuiRuntimeScope scope,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new AgentTuiThreadState(0, null, []));
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
