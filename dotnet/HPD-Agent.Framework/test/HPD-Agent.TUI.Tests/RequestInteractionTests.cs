using FluentAssertions;
using HPD.Agent.TUI.Composition;
using HPD.Agent.TUI.Interactions;
using HPD.Agent.TUI.Models;
using HPD.Agent.TUI.Runtime;
using HPD.Agent.Permissions;
using HPD.TUI.Core;
using HPD.TUI.Forms;

namespace HPD.Agent.TUI.Tests;

public sealed class RequestInteractionTests
{
    [Fact]
    public void AddInteractionHandler_RegistersRequestInteractionHandlers()
    {
        var registry = new HpdAgentTuiBuilder()
            .AddAgentTuiDefaults()
            .AddInteractionHandler<ContinuationRequestEvent>("hpd.continuation", new ContinuationRequestInteractionHandler())
            .AddInteractionHandler<UserQuestionRequestEvent>("hpd.questions", new UserQuestionInteractionHandler())
            .Build();

        registry.InteractionHandlers.Select(handler => handler.Key)
            .Should()
            .Contain(["hpd.permission", "hpd.continuation", "hpd.questions"]);
        registry.TryFindInteractionHandler(
            new PermissionRequestEvent("permission-1", "permissions", "shell.exec", null, "call-1", null),
            new AgentTuiRuntimeScope("agent", "session", "main"),
            out var handler).Should().BeTrue();
        handler.Key.Should().Be("hpd.permission");
    }

    [Fact]
    public void AddAgentTuiDefaults_RegistersPermissionButQuestionsRequireExplicitRegistration()
    {
        var registry = new HpdAgentTuiBuilder()
            .AddAgentTuiDefaults()
            .Build();

        registry.InteractionHandlers.Select(h => h.Key).Should().Contain("hpd.permission");
        registry.InteractionHandlers.Should().NotContain(h => h.Value is UserQuestionInteractionHandler);
    }

    [Fact]
    public async Task QuestionHistoryUsesOriginalLabelsAndPreservesNonAnswerOutcome()
    {
        var scope = new AgentTuiRuntimeScope("agent", "session", "main");
        var shell = new ChatShellModel(scope);
        var registry = new HpdAgentTuiBuilder().AddQuestionInteraction(AgentTuiEventScope.CurrentThreadAndDescendants).Build();
        var context = new AgentTuiEventContext(scope, shell, shell.Navigation, registry, new AgentTuiStateBag());
        var handler = new QuestionTranscriptHandler();
        await handler.HandleAsync(new UserQuestionRequestEvent("q", "AskUser",
            [new("environment", "Which environment?", Options: [new("stage", "Staging")])])
            { SessionId = "session", ThreadId = "child", ThreadExecutionId = "run" }, context, default);
        await handler.HandleAsync(new QuestionResponseEvent("q", "AskUser", QuestionOutcome.Answered,
            [new("environment", ["stage"], Notes: "Use a fresh database")])
            { SessionId = "session", ThreadId = "child", ThreadExecutionId = "run" }, context, default);
        await handler.HandleAsync(new QuestionResponseEvent("dismiss", "AskUser", QuestionOutcome.Dismissed, [])
            { SessionId = "session", ThreadId = "child", ThreadExecutionId = "run" }, context, default);
        shell.Transcript.Snapshot().Entries.Count.Should().Be(3);
        shell.Transcript.Snapshot().Entries.Select(e => ((NoticeCell)e.Cell).Title).Should()
            .Contain(["Question for you · child", "Questions answered · child", "Questions dismissed · child"]);
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
            .AddInteractionHandler<UserQuestionRequestEvent>(
                "sample.questions",
                new TypedQuestionHandler())
            .Build();

        registry.TryFindInteractionHandler(
                new UserQuestionRequestEvent("question-1", "sample", [new("question", "Question?")]),
                new AgentTuiRuntimeScope("agent", "session", "main"),
                out var handler)
            .Should().BeTrue();
        handler.Key.Should().Be("sample.questions");

        registry.TryFindInteractionHandler(
                new PermissionRequestEvent("permission-1", "sample", "tool", null, "call-1", null),
                new AgentTuiRuntimeScope("agent", "session", "main"),
                out _)
            .Should().BeFalse();
    }

    [Fact]
    public void InteractionHandlers_ApplyTheirRegisteredEventScope()
    {
        var registry = new HpdAgentTuiBuilder()
            .AddInteractionHandler<PermissionRequestEvent>(
                "permission",
                new PermissionRequestInteractionHandler(),
                AgentTuiEventScope.CurrentThreadAndDescendants)
            .AddInteractionHandler<UserQuestionRequestEvent>(
                "questions",
                new UserQuestionInteractionHandler())
            .Build();
        var scope = new AgentTuiRuntimeScope("agent", "session", "main");
        var childPermission = new PermissionRequestEvent(
            "permission-child",
            "permissions",
            "shell.exec",
            null,
            "call-child",
            null)
        {
            SessionId = "session",
            ThreadId = "subagent/explore/invocation-1"
        };
        var childQuestion = new UserQuestionRequestEvent(
            "questions-child",
            "questions",
            [new("question", "Question?")])
        {
            SessionId = "session",
            ThreadId = "subagent/explore/invocation-1"
        };

        registry.TryFindInteractionHandler(childPermission, scope, out var permission)
            .Should().BeTrue();
        permission.Key.Should().Be("permission");
        registry.TryFindInteractionHandler(childQuestion, scope, out _)
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
                    CreatePermissionEvaluation())),
            CancellationToken.None);

        result.Kind.Should().Be(AgentTuiInteractionResultKind.AnswerRequest);
        result.Response.Should().BeOfType<PermissionResponseEvent>()
            .Which.Should().Match<PermissionResponseEvent>(evt =>
                evt.PermissionId == "permission-1" &&
                evt.ChoiceId == "always_allow" &&
                evt.Feedback == null);
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
                    CreatePermissionEvaluation())),
            CancellationToken.None);

        result.Kind.Should().Be(AgentTuiInteractionResultKind.AnswerRequest);
        result.Response.Should().BeOfType<PermissionResponseEvent>()
            .Which.Should().Match<PermissionResponseEvent>(evt =>
                evt.PermissionId == "permission-1" &&
                evt.ChoiceId == "feedback" &&
                evt.Feedback == "Use the read-only status tool instead.");
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
                    CreatePermissionEvaluation())),
            CancellationToken.None);

        result.Kind.Should().Be(AgentTuiInteractionResultKind.AnswerRequest);
        result.Response.Should().BeOfType<PermissionResponseEvent>()
            .Which.Should().Match<PermissionResponseEvent>(evt =>
                evt.PermissionId == "permission-1" &&
                evt.ChoiceId == "feedback" &&
                evt.Feedback == "Permission dialog was canceled.");
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
    public async Task QuestionHandler_ReturnsStructuredAnswerWithSourceScope()
    {
        var dialogs = new TestDialogService { FormResponse = new QuestionResponseEvent("q", "AskUser",
            QuestionOutcome.Answered, [new("environment", ["staging"], null, "Please verify")]) };
        var handler = new UserQuestionInteractionHandler();
        var request = new UserQuestionRequestEvent("q", "AskUser",
            [new("environment", "Which environment?", Options: [new("staging", "Staging"), new("prod", "Production")])])
            { SessionId = "child-session", ThreadId = "child-thread", ThreadExecutionId = "child-run" };
        var result = await handler.HandleAsync(CreateContext(dialogs, request), CancellationToken.None);
        var response = Assert.IsType<QuestionResponseEvent>(result.Response);
        Assert.Equal("staging", Assert.Single(response.Answers[0].SelectedOptionIds));
        Assert.Equal("Please verify", response.Answers[0].Notes);
        Assert.Equal("child-session", response.SessionId);
        Assert.Equal("child-run", response.ThreadExecutionId);
    }

    [Fact]
    public async Task QuestionHandler_DismissalSettlesTheRequest()
    {
        var result = await new UserQuestionInteractionHandler().HandleAsync(CreateContext(new TestDialogService(),
            new UserQuestionRequestEvent("q", "AskUser", [new("question", "What should I do?")])), default);
        Assert.Equal(AgentTuiInteractionResultKind.AnswerRequest, result.Kind);
        var response = Assert.IsType<QuestionResponseEvent>(result.Response);
        Assert.Equal(QuestionOutcome.Dismissed, response.Outcome);
        Assert.Empty(response.Answers);
    }

    [Fact]
    public async Task QuestionPagesRetainChoicesAndTextWhenGoingBack()
    {
        var visit = 0;
        var dialogs = new TestDialogService { FormAction = model =>
        {
            if (visit == 0) model.Fields.OfType<ChoiceFormField<string>>().Single().Select("stage");
            if (visit == 1)
            {
                var text = model.Fields.OfType<TextFormField>().First();
                text.BeginEdit(); text.HandleInput(new KeyEvent(KeyCode.Paste, Text: "Fresh database")); text.AcceptEdit();
                model.Fields.OfType<ChoiceFormField<bool>>().Single(f => f.Key == "navigation").Select(true);
            }
            if (visit == 2) model.Fields.OfType<ChoiceFormField<string>>().Single().Value.Should().Be("stage");
            if (visit == 3) model.Fields.OfType<TextFormField>().First().Value.Should().Be("Fresh database");
            visit++;
        } };
        var result = await new UserQuestionInteractionHandler().HandleAsync(CreateContext(dialogs,
            new UserQuestionRequestEvent("q", "AskUser", [new("environment", "Where?", Options: [new("stage", "Staging")]),
                new("setup", "Which setup?")])), default);
        visit.Should().Be(4);
        var response = (QuestionResponseEvent)result.Response!;
        response.Answers[0].SelectedOptionIds.Should().Equal("stage");
        response.Answers[1].CustomText.Should().Be("Fresh database");
    }

    [Fact]
    public async Task MinimizeLeavesQuestionUnansweredAndPreservesDraft()
    {
        var request = new UserQuestionRequestEvent("defer", "AskUser", [new("q", "Choose", Options: [new("a", "A")])]);
        var handler = new UserQuestionInteractionHandler();
        var first = new TestDialogService { FormAction = model =>
        {
            model.Fields.OfType<ChoiceFormField<string>>().Single().Select("a");
            model.Fields.OfType<ChoiceFormField<bool>>().Single(f => f.Key == "defer").Select(true);
        } };
        var minimized = await handler.HandleAsync(CreateContext(first, request), default);
        minimized.Kind.Should().Be(AgentTuiInteractionResultKind.Defer);
        minimized.Response.Should().BeNull();
        var second = new TestDialogService { FormAction = model => model.Fields.OfType<ChoiceFormField<string>>().Single().Value.Should().Be("a") };
        var answered = await handler.HandleAsync(CreateContext(second, request), default);
        ((QuestionResponseEvent)answered.Response!).Answers[0].SelectedOptionIds.Should().Equal("a");
    }

    [Fact]
    public async Task QuestionDraftSurvivesPresentationCancellation()
    {
        var request = new UserQuestionRequestEvent("draft", "AskUser", [new("q", "Choose", Options: [new("a", "A")])]);
        var handler = new UserQuestionInteractionHandler();
        var first = new TestDialogService { FormAction = model =>
        {
            model.Fields.OfType<ChoiceFormField<string>>().Single().Select("a");
            throw new OperationCanceledException();
        } };
        await Assert.ThrowsAsync<OperationCanceledException>(() => handler.HandleAsync(CreateContext(first, request), default));
        var second = new TestDialogService { FormAction = model => model.Fields.OfType<ChoiceFormField<string>>().Single().Value.Should().Be("a") };
        var result = await handler.HandleAsync(CreateContext(second, request), default);
        ((QuestionResponseEvent)result.Response!).Answers[0].SelectedOptionIds.Should().Equal("a");
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

    private sealed class TypedQuestionHandler :
        AgentTuiInteractionHandler<UserQuestionRequestEvent>
    {
        protected override Task<AgentTuiInteractionResult> HandleAsync(
            AgentTuiInteractionContext<UserQuestionRequestEvent> context,
            CancellationToken cancellationToken)
            => Task.FromResult(AgentTuiInteractionResult.NoOp);
    }

    private sealed class TestDialogService : IAgentTuiDialogService
    {
        public Action<FormModel>? FormAction { get; init; }
        public object? FormResponse { get; init; }
        public Task<AgentTuiDialogResult<TResult>> FormAsync<TResult>(string title,
            HPD.TUI.Forms.FormDefinition<TResult> form, CancellationToken cancellationToken = default)
        {
            if (FormAction is not null)
            {
                FormAction(form.Model);
                return Task.FromResult(AgentTuiDialogResult<TResult>.Submitted(form.BuildResult()));
            }
            return Task.FromResult(FormResponse is TResult response ? AgentTuiDialogResult<TResult>.Submitted(response)
                : AgentTuiDialogResult<TResult>.Dismissed());
        }
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

    private static PermissionEvaluationEnvelope CreatePermissionEvaluation() => new()
    {
        PolicyId = "test",
        PolicyRevision = "1",
        Key = new PermissionKey("shell.exec", "Run command", "test", "test", "1"),
        Title = "Run command",
        Risk = PermissionRisk.Medium,
        Choices = new PermissionChoiceSet
        {
            Items =
            [
                new PermissionChoiceDescriptor { Id = "allow_once", Label = "Allow once", Decision = PermissionDecisionKind.Allow },
                new PermissionChoiceDescriptor { Id = "always_allow", Label = "Always allow", Decision = PermissionDecisionKind.Allow },
                new PermissionChoiceDescriptor { Id = "deny_once", Label = "Deny", Decision = PermissionDecisionKind.Deny },
                new PermissionChoiceDescriptor { Id = "feedback", Label = "Tell agent", Decision = PermissionDecisionKind.Feedback,
                    DeniedBehavior = PermissionDeniedBehavior.ReturnToModel }
            ]
        }
    };

    private sealed class NoopRuntime : IHpdAgentTuiRuntime
    {
        public Task<AgentTuiTargetResolution> ResolveInitialTargetAsync(
            AgentTuiExecutionTarget? requested,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new AgentTuiTargetResolution(
                requested ?? new DirectAgentTuiExecutionTarget(new AgentTuiRuntimeScope("agent", "session", "main")),
                IsDurable: true));

        public Task<AgentTuiExecutionTarget> EnsureDurableTargetAsync(
            AgentTuiExecutionTarget target,
            CancellationToken cancellationToken = default)
            => Task.FromResult(target);

        public async IAsyncEnumerable<AgentTuiEventBatch> ObserveAsync(
            AgentTuiExecutionTarget target,
            ThreadJournalCursor after,
            ThreadJournalCursor initialObservedCursor,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        public Task<AgentTuiSubmitResult> SubmitInputAsync(
            AgentTuiExecutionTarget target,
            AgentInputEvent input,
            CancellationToken cancellationToken = default)
            => Task.FromResult(Submitted(target.Scope));

        public Task<AgentRespondResult> AnswerRequestAsync(
            AgentTuiRuntimeScope scope,
            AgentEvent response,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new AgentRespondResult(AgentRespondStatus.Accepted, ((IAgentResponseEvent)response).RequestId));

        public Task<AgentTuiSubmitResult> CancelExecutionAsync(
            AgentTuiRuntimeScope scope, string threadExecutionId, CancellationToken cancellationToken = default)
            => Task.FromResult(new AgentTuiSubmitResult(AgentInputDisposition.Accepted, threadExecutionId, null));

        public Task<AgentTuiThreadState> GetThreadStateAsync(
            AgentTuiRuntimeScope scope,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new AgentTuiThreadState(ThreadJournalCursor.Start(1), null, []));

        private static AgentTuiSubmitResult Submitted(AgentTuiRuntimeScope scope) => new(
            AgentInputDisposition.Queued,
            "run",
            new AgentTuiThreadExecution("run", scope.AgentId, scope.SessionId, scope.ThreadId, "active", DateTimeOffset.UtcNow));
    }
}
