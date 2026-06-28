using FluentAssertions;
using HPD.Agent;
using HPD.Agent.TUI.Composition;
using HPD.Agent.TUI.Runtime;
using HPD.TUI.Core;
using HPD.TUI.Terminal;
using System.Reflection;
using System.Text;

namespace HPD.Agent.TUI.Tests;

public sealed class RunConfigContributorTests
{
    [Fact]
    public async Task SubmittedPrompt_CarriesComposedRunConfig()
    {
        var scope = new AgentTuiRuntimeScope("agent-a", "session-a", "main");
        var runtime = new CapturingRuntime(scope);
        await using var app = HpdAgentTuiApp.Create(
            runtime,
            scope,
            TuiTestBuilder.CreateProvider(builder => builder
                .AddAgentTuiDefaults()
                .AddRunConfigContributor("test.model", (context, runConfig) =>
                {
                    context.Scope.Should().BeSameAs(scope);
                    context.PromptText.Should().Be("hello");

                    runConfig.SetProviderModel("openrouter", "deepseek/deepseek-chat");
                })),
            new TestTerminal(80, 24));

        InvokePrivate(app, "RebuildShell", scope, "Connected.", null!);
        InvokePrivate(app, "SubmitPrompt", "hello".AsMemory());

        runtime.LastInput.Should().BeOfType<UserMessagesInputEvent>()
            .Which.RunConfig.Should().NotBeNull();
        runtime.LastInput!.RunConfig!.ProviderKey.Should().Be("openrouter");
        runtime.LastInput.RunConfig.ModelId.Should().Be("deepseek/deepseek-chat");
    }

    [Fact]
    public async Task SubmittedPrompt_MergesMultipleRunConfigContributors()
    {
        var scope = new AgentTuiRuntimeScope("agent-a", "session-a", "main");
        var runtime = new CapturingRuntime(scope);
        await using var app = HpdAgentTuiApp.Create(
            runtime,
            scope,
            TuiTestBuilder.CreateProvider(builder => builder
                .AddAgentTuiDefaults()
                .AddRunConfigContributor("test.model", (_, runConfig) =>
                {
                    runConfig.SetProviderModel("openrouter", "deepseek/deepseek-chat");
                    runConfig.AddAdditionalSystemInstructions("test.model", "Prefer short answers.");
                })
                .AddRunConfigContributor("test.workspace", (_, runConfig) =>
                {
                    runConfig.AddContextOverride("workspace", "/repo");
                    runConfig.AddPermissionOverride("ExecuteCommand", requiresPermission: true);
                    runConfig.AddAdditionalSystemInstructions("test.workspace", "Use the workspace context.");
                })),
            new TestTerminal(80, 24));

        InvokePrivate(app, "RebuildShell", scope, "Connected.", null!);
        InvokePrivate(app, "SubmitPrompt", "hello".AsMemory());

        var input = runtime.LastInput.Should().BeOfType<UserMessagesInputEvent>().Subject;
        input.RunConfig.Should().NotBeNull();
        var runConfig = input.RunConfig!;
        runConfig.ProviderKey.Should().Be("openrouter");
        runConfig.ModelId.Should().Be("deepseek/deepseek-chat");
        runConfig.ContextOverrides.Should().Contain("workspace", "/repo");
        runConfig.PermissionOverrides.Should().Contain("ExecuteCommand", true);
        runConfig.AdditionalSystemInstructions.Should().Be(
            "Prefer short answers.\n\nUse the workspace context.");
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
