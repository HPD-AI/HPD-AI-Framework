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

        var result = await handler.HandleAsync(
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

        result.Kind.Should().Be(AgentTuiInteractionResultKind.AnswerRequest);
        result.Response.Should().BeOfType<PermissionResponseEvent>()
            .Which.Should().Match<PermissionResponseEvent>(evt =>
                evt.PermissionId == "permission-1" &&
                evt.Approved &&
                evt.Choice == PermissionChoice.AlwaysAllow);
    }

    [Fact]
    public async Task PermissionHandler_FeedbackChoiceReturnsDeniedResponseWithInstruction()
    {
        var dialogs = new TestDialogService
        {
            SelectIndex = 3,
            Input = "Use the read-only status tool instead."
        };
        var handler = new PermissionRequestInteractionHandler();

        var result = await handler.HandleAsync(
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

        result.Kind.Should().Be(AgentTuiInteractionResultKind.AnswerRequest);
        result.Response.Should().BeOfType<PermissionResponseEvent>()
            .Which.Should().Match<PermissionResponseEvent>(evt =>
                evt.PermissionId == "permission-1" &&
                !evt.Approved &&
                evt.Choice == PermissionChoice.Ask &&
                evt.Reason == "Use the read-only status tool instead." &&
                evt.DeniedBehavior == PermissionDeniedBehavior.ReturnToModel);
    }

    [Fact]
    public async Task PermissionHandler_FeedbackChoiceWithoutInputStillReturnsToModel()
    {
        var dialogs = new TestDialogService
        {
            SelectIndex = 3,
            Input = null
        };
        var handler = new PermissionRequestInteractionHandler();

        var result = await handler.HandleAsync(
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

        result.Kind.Should().Be(AgentTuiInteractionResultKind.AnswerRequest);
        result.Response.Should().BeOfType<PermissionResponseEvent>()
            .Which.Should().Match<PermissionResponseEvent>(evt =>
                evt.PermissionId == "permission-1" &&
                !evt.Approved &&
                evt.Choice == PermissionChoice.Ask &&
                evt.Reason == "Permission dialog was canceled." &&
                evt.DeniedBehavior == PermissionDeniedBehavior.ReturnToModel);
    }

    [Fact]
    public async Task ContinuationHandler_ReturnsSelectedExtensionResponse()
    {
        var dialogs = new TestDialogService { SelectIndex = 0 };
        var handler = new ContinuationRequestInteractionHandler();

        var result = await handler.HandleAsync(
            CreateContext(
                dialogs,
                new ContinuationRequestEvent("continue-1", "iteration", 50, 50)),
            CancellationToken.None);

        result.Kind.Should().Be(AgentTuiInteractionResultKind.AnswerRequest);
        result.Response.Should().BeOfType<ContinuationResponseEvent>()
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

        var result = await handler.HandleAsync(
            CreateContext(
                dialogs,
                new ClarificationRequestEvent(
                    "clarify-1",
                    "planner",
                    "Which thread?",
                    Options: ["main", "feature"])),
            CancellationToken.None);

        result.Kind.Should().Be(AgentTuiInteractionResultKind.AnswerRequest);
        result.Response.Should().BeOfType<ClarificationResponseEvent>()
            .Which.Should().Match<ClarificationResponseEvent>(evt =>
                evt.RequestId == "clarify-1" &&
                evt.Answer == "feature");
    }

    [Fact]
    public async Task ClarificationHandler_UsesInputWhenNoOptionsProvided()
    {
        var dialogs = new TestDialogService { Input = "Use the safer path." };
        var handler = new ClarificationRequestInteractionHandler();

        var result = await handler.HandleAsync(
            CreateContext(
                dialogs,
                new ClarificationRequestEvent(
                    "clarify-1",
                    "planner",
                    "What should I do?")),
            CancellationToken.None);

        result.Kind.Should().Be(AgentTuiInteractionResultKind.AnswerRequest);
        result.Response.Should().BeOfType<ClarificationResponseEvent>()
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

        public Task<AgentTuiInteractionResult> HandleAsync(
            AgentTuiInteractionContext context,
            CancellationToken cancellationToken = default)
            => Task.FromResult(AgentTuiInteractionResult.NoOp);
    }

    private sealed class TypedClarificationHandler :
        AgentTuiInteractionHandler<ClarificationRequestEvent>
    {
        protected override Task<AgentTuiInteractionResult> HandleAsync(
            AgentTuiInteractionContext<ClarificationRequestEvent> context,
            CancellationToken cancellationToken)
            => Task.FromResult(AgentTuiInteractionResult.NoOp);
    }

    private sealed class TestDialogService : IAgentTuiDialogService
    {
        public int SelectIndex { get; init; }

        public string? Input { get; init; }

        public bool HasOpenDialog => false;

        public Task<AgentTuiDialogResult<TResult>> ShowAsync<TResult>(
            string key,
            Func<AgentTuiDialogContext<TResult>, IComponent> componentFactory,
            CancellationToken cancellationToken = default)
            => Task.FromResult(AgentTuiDialogResult<TResult>.Dismissed());

        public bool Close(string key) => true;

        public bool CloseTop() => true;

        public Task<AgentTuiDialogResult<bool>> ConfirmAsync(
            string title,
            bool? defaultValue = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(AgentTuiDialogResult<bool>.Submitted(defaultValue ?? true));

        public Task<AgentTuiDialogResult<T>> SelectAsync<T>(
            string title,
            IReadOnlyList<T> options,
            Func<T, string> titleSelector,
            CancellationToken cancellationToken = default)
            => Task.FromResult(AgentTuiDialogResult<T>.Submitted(options[SelectIndex]));

        public Task<AgentTuiDialogResult<string>> InputAsync(
            string title,
            string? defaultValue = null,
            bool allowEmpty = false,
            CancellationToken cancellationToken = default)
        {
            var value = Input ?? defaultValue;
            return Task.FromResult(value is null
                ? AgentTuiDialogResult<string>.Dismissed()
                : AgentTuiDialogResult<string>.Submitted(value));
        }

        public Task<AgentTuiDialogResult<string>> SecretInputAsync(
            string title,
            bool allowEmpty = false,
            CancellationToken cancellationToken = default)
            => Task.FromResult(Input is null
                ? AgentTuiDialogResult<string>.Dismissed()
                : AgentTuiDialogResult<string>.Submitted(Input));
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
            => Task.FromResult(Submitted(scope));

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

        private static AgentTuiSubmitResult Submitted(AgentTuiRuntimeScope scope) => new(
            new AgentTuiThreadRun("run", scope.AgentId, scope.SessionId, scope.ThreadId, "active", DateTimeOffset.UtcNow));
    }
}
