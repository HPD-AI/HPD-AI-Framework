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

    private sealed class CapturingRuntime : IHpdAgentTuiRuntime
    {
        private readonly AgentTuiRuntimeScope _scope;

        public CapturingRuntime(AgentTuiRuntimeScope scope)
        {
            _scope = scope;
        }

        public AgentInputEvent? LastInput { get; private set; }

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
        {
            LastInput = input;
            return Task.CompletedTask;
        }

        public Task InterruptAsync(
            AgentTuiRuntimeScope scope,
            string reason,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

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
            => Task.FromResult<AgentTuiThreadRun?>(null);
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
