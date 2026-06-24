using FluentAssertions;
using HPD.Agent.TUI.Composition;
using HPD.Agent.TUI.Interactions;
using HPD.Agent.TUI.Models;
using HPD.Agent.TUI.Runtime;
using HPD.TUI.Core;

namespace HPD.Agent.TUI.Tests;

public sealed class RequestInteractionTests
{
    [Fact]
    public void AddInteractionHandler_RegistersRequestInteractionHandlers()
    {
        var registry = new HpdAgentTuiBuilder()
            .AddAgentTuiDefaults()
            .AddInteractionHandler<PermissionRequestEvent>("hpd.permission", new PermissionRequestInteractionHandler())
            .AddInteractionHandler<ContinuationRequestEvent>("hpd.continuation", new ContinuationRequestInteractionHandler())
            .AddInteractionHandler<ClarificationRequestEvent>("hpd.clarification", new ClarificationRequestInteractionHandler())
            .Build();

        registry.InteractionHandlers.Select(handler => handler.Key)
            .Should()
            .Contain(["hpd.permission", "hpd.continuation", "hpd.clarification"]);
        registry.TryFindInteractionHandler(
            new PermissionRequestEvent("permission-1", "permissions", "shell.exec", null, "call-1", null),
            out var handler).Should().BeTrue();
        handler.Key.Should().Be("hpd.permission");
    }

    [Fact]
    public void AddAgentTuiDefaults_DoesNotRegisterRequestInteractionHandlers()
    {
        var registry = new HpdAgentTuiBuilder()
            .AddAgentTuiDefaults()
            .Build();

        registry.InteractionHandlers.Should().BeEmpty();
    }

    [Fact]
    public void AddInteractionHandler_FailsOnDuplicateKey()
    {
        var builder = new HpdAgentTuiBuilder()
            .AddInteractionHandler("sample.interaction", new NoopInteractionHandler());

        var act = () => builder.AddInteractionHandler("sample.interaction", new NoopInteractionHandler());

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ReplaceInteractionHandler_ReplacesExistingHandler()
    {
        var replacement = new NoopInteractionHandler();
        var registry = new HpdAgentTuiBuilder()
            .AddInteractionHandler("sample.interaction", new NoopInteractionHandler())
            .ReplaceInteractionHandler("sample.interaction", replacement)
            .Build();

        registry.InteractionHandlers.Single(handler => handler.Key == "sample.interaction")
            .Value.Should().BeSameAs(replacement);
    }

    [Fact]
    public void TypedInteractionHandler_OnlyMatchesRegisteredRequestType()
    {
        var registry = new HpdAgentTuiBuilder()
            .AddInteractionHandler<ClarificationRequestEvent>(
                "sample.clarification",
                new TypedClarificationHandler())
            .Build();

        registry.TryFindInteractionHandler(
                new ClarificationRequestEvent("clarify-1", "sample", "Question?"),
                out var handler)
            .Should().BeTrue();
        handler.Key.Should().Be("sample.clarification");

        registry.TryFindInteractionHandler(
                new PermissionRequestEvent("permission-1", "sample", "tool", null, "call-1", null),
                out _)
            .Should().BeFalse();
    }

    [Fact]
    public async Task PermissionHandler_ReturnsSelectedPermissionResponse()
    {
        var dialogs = new TestDialogService { SelectIndex = 1 };
        var handler = new PermissionRequestInteractionHandler();

        var response = await handler.HandleAsync(
            CreateContext(
                dialogs,
                new PermissionRequestEvent(
                    "permission-1",
                    "permissions",
                    "shell.exec",
                    "Run command",
                    "call-1",
                    new Dictionary<string, object?> { ["cmd"] = "dotnet test" })),
            CancellationToken.None);

        response.Should().BeOfType<PermissionResponseEvent>()
            .Which.Should().Match<PermissionResponseEvent>(evt =>
                evt.PermissionId == "permission-1" &&
                evt.Approved &&
                evt.Choice == PermissionChoice.AlwaysAllow);
    }

    [Fact]
    public async Task ContinuationHandler_ReturnsSelectedExtensionResponse()
    {
        var dialogs = new TestDialogService { SelectIndex = 0 };
        var handler = new ContinuationRequestInteractionHandler();

        var response = await handler.HandleAsync(
            CreateContext(
                dialogs,
                new ContinuationRequestEvent("continue-1", "iteration", 50, 50)),
            CancellationToken.None);

        response.Should().BeOfType<ContinuationResponseEvent>()
            .Which.Should().Match<ContinuationResponseEvent>(evt =>
                evt.ContinuationId == "continue-1" &&
                evt.Approved &&
                evt.ExtensionAmount == 10);
    }

    [Fact]
    public async Task ClarificationHandler_UsesOptionsWhenProvided()
    {
        var dialogs = new TestDialogService { SelectIndex = 1 };
        var handler = new ClarificationRequestInteractionHandler();

        var response = await handler.HandleAsync(
            CreateContext(
                dialogs,
                new ClarificationRequestEvent(
                    "clarify-1",
                    "planner",
                    "Which thread?",
                    Options: ["main", "feature"])),
            CancellationToken.None);

        response.Should().BeOfType<ClarificationResponseEvent>()
            .Which.Should().Match<ClarificationResponseEvent>(evt =>
                evt.RequestId == "clarify-1" &&
                evt.Answer == "feature");
    }

    [Fact]
    public async Task ClarificationHandler_UsesInputWhenNoOptionsProvided()
    {
        var dialogs = new TestDialogService { Input = "Use the safer path." };
        var handler = new ClarificationRequestInteractionHandler();

        var response = await handler.HandleAsync(
            CreateContext(
                dialogs,
                new ClarificationRequestEvent(
                    "clarify-1",
                    "planner",
                    "What should I do?")),
            CancellationToken.None);

        response.Should().BeOfType<ClarificationResponseEvent>()
            .Which.Answer.Should().Be("Use the safer path.");
    }

    private static AgentTuiInteractionContext CreateContext(
        IAgentTuiDialogService dialogs,
        AgentEvent request)
    {
        var scope = new AgentTuiRuntimeScope("agent", "session", "main");
        var shell = new ChatShellModel(scope);
        return new AgentTuiInteractionContext(
            scope,
            shell,
            shell.Navigation,
            new NoopRuntime(),
            dialogs,
            request);
    }

    private sealed class NoopInteractionHandler : IAgentTuiInteractionHandler
    {
        public bool CanHandle(AgentEvent request) => false;

        public Task<AgentEvent?> HandleAsync(
            AgentTuiInteractionContext context,
            CancellationToken cancellationToken = default)
            => Task.FromResult<AgentEvent?>(null);
    }

    private sealed class TypedClarificationHandler :
        AgentTuiInteractionHandler<ClarificationRequestEvent>
    {
        protected override Task<AgentEvent?> HandleAsync(
            AgentTuiInteractionContext<ClarificationRequestEvent> context,
            CancellationToken cancellationToken)
            => Task.FromResult<AgentEvent?>(null);
    }

    private sealed class TestDialogService : IAgentTuiDialogService
    {
        public int SelectIndex { get; init; }

        public string? Input { get; init; }

        public bool HasOpenDialog => false;

        public Task<TResult?> ShowAsync<TResult>(
            string key,
            Func<AgentTuiDialogContext<TResult>, IComponent> componentFactory,
            CancellationToken cancellationToken = default)
            => Task.FromResult<TResult?>(default);

        public bool Close(string key) => true;

        public bool CloseTop() => true;

        public Task<bool?> ConfirmAsync(
            string title,
            bool? defaultValue = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult<bool?>(defaultValue ?? true);

        public Task<T?> SelectAsync<T>(
            string title,
            IReadOnlyList<T> options,
            Func<T, string> titleSelector,
            CancellationToken cancellationToken = default)
            => Task.FromResult<T?>(options[SelectIndex]);

        public Task<string?> InputAsync(
            string title,
            string? defaultValue = null,
            bool allowEmpty = false,
            CancellationToken cancellationToken = default)
            => Task.FromResult<string?>(Input ?? defaultValue);

        public Task<string?> SecretInputAsync(
            string title,
            bool allowEmpty = false,
            CancellationToken cancellationToken = default)
            => Task.FromResult<string?>(Input);
    }

    private sealed class NoopRuntime : IHpdAgentTuiRuntime
    {
        public Task<AgentTuiScopeResolution> ResolveInitialScopeAsync(
            AgentTuiRuntimeScope? requested,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new AgentTuiScopeResolution(
                requested ?? new AgentTuiRuntimeScope("agent", "session", "main"),
                IsDurable: true));

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
            => Task.CompletedTask;

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
}
