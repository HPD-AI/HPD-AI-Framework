using HPD.Agent;
using HPD.Agent.Serialization;
using HPD.Agent.Tests.Infrastructure;
using Microsoft.Extensions.AI;

namespace HPD.Agent.ActionInvocation.Tests;

public sealed class QuestionToolTests
{
    private static readonly AgentQuestion[] Questions =
        [new("environment", "Which environment?", Options: [new("stage", "Staging"), new("prod", "Production")], RecommendedOptionId: "stage")];

    [Fact]
    public void RecommendationDoesNotMakeAnEmptyResponseValid()
    {
        Assert.Throws<ArgumentException>(() => QuestionValidation.ValidateResponse(Questions,
            new("q", "AskUser", QuestionOutcome.Answered, [new("environment", [])])));
        QuestionValidation.ValidateResponse(Questions, new("q", "AskUser", QuestionOutcome.Dismissed, []));
        QuestionValidation.ValidateResponse(Questions, new("q", "AskUser", QuestionOutcome.Discuss, []));
    }

    [Theory]
    [InlineData("unknown", "stage")]
    [InlineData("environment", "unknown")]
    public void UnknownQuestionAndOptionIdsAreRejected(string question, string option)
        => Assert.Throws<ArgumentException>(() => QuestionValidation.ValidateResponse(Questions,
            new("q", "AskUser", QuestionOutcome.Answered, [new(question, [option])])));

    [Fact]
    public void MultipleChoicesRequireMultipleModeAndDuplicateIdsAreRejected()
    {
        var response = new QuestionResponseEvent("q", "AskUser", QuestionOutcome.Answered,
            [new("environment", ["stage", "prod"], Notes: "Both")]);
        Assert.Throws<ArgumentException>(() => QuestionValidation.ValidateResponse(Questions, response));
        QuestionValidation.ValidateResponse([Questions[0] with { Multiple = true }], response);
        Assert.Throws<ArgumentException>(() => QuestionValidation.Validate([Questions[0], Questions[0]]));
        Assert.Throws<ArgumentException>(() => QuestionValidation.ValidateResponse(Questions,
            response with { Answers = [new("environment", ["stage", "stage"])] }));
    }

    [Fact]
    public async Task InvalidReplyDoesNotResolveWaiterAndAcceptedUnscopedReplyIsDurable()
    {
        var client = new FakeChatClient();
        client.EnqueueToolCall("AskUser", "ask", new Dictionary<string, object?>
        {
            ["questions"] = System.Text.Json.JsonDocument.Parse("""[{"id":"question","text":"What should I do?"}]""").RootElement.Clone()
        });
        client.EnqueueTextResponse("Answer received.");
        await using var agent = await new AgentBuilder(new AgentConfig { Name = "questions" })
            .WithEventComposition(CoreAgentEventComposition.Instance).WithChatClient(new IdentifiedClient(client)).BuildAsync();
        await agent.CreateSessionAsync("session");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var key = new ThreadKey("session", "main");
        var run = agent.RunAsync("Ask", sessionId: key.SessionId, threadId: key.ThreadId,
            runConfig: new() { AllowUserQuestions = true }, cancellationToken: timeout.Token);
        UserQuestionRequestEvent? question = null;
        while (question is null)
        {
            if (run.IsCompleted) await run;
            question = agent.GetPendingRequests(key).Select(p => p.Request).OfType<UserQuestionRequestEvent>().FirstOrDefault();
            if (question is null) await Task.Delay(10, timeout.Token);
        }
        var invalid = new QuestionResponseEvent(question.RequestId, question.SourceName, QuestionOutcome.Answered,
            [new("question", [])]);
        Assert.Equal(AgentRespondStatus.InvalidResponse, (await agent.AnswerRequestAsync(invalid)).Status);
        Assert.False(run.IsCompleted);
        var answer = invalid with { Answers = [new("question", [], "Proceed with staging")] };
        Assert.Equal(AgentRespondStatus.TargetMismatch,
            (await agent.AnswerRequestAsync(answer with { ThreadId = "wrong" })).Status);
        Assert.True((await agent.AnswerRequestAsync(answer)).Accepted);
        Assert.False((await agent.AnswerRequestAsync(answer)).Accepted);
        await run;
        var persisted = Assert.Single((await agent.Config!.SessionStore!.CollectThreadEventsAsync(key))!
            .OfType<QuestionResponseEvent>());
        Assert.Equal(question.ThreadExecutionId, persisted.ThreadExecutionId);
        Assert.Equal("Proceed with staging", persisted.Answers[0].CustomText);
    }

    [Fact]
    public void QuestionAndResultEventsUseGeneratedDurableMetadata()
    {
        if (System.Environment.GetEnvironmentVariable("HPD_REQUIRE_GENERATED_JSON") == "1")
            Assert.False(System.Text.Json.JsonSerializer.IsReflectionEnabledByDefault);
        AgentEvent[] events = [new UserQuestionRequestEvent("q", "AskUser", Questions),
            new ParentQuestionRequestEvent("q", "Parent", new("session", "main"), Questions),
            new QuestionResponseEvent("q", "AskUser", QuestionOutcome.Answered, [new("environment", ["stage"])]),
            new SubAgentQuestionRaisedEvent("q", new("session", "child"), "run", Questions),
            new SubAgentExecutionControllerEvent("run", new("session", "main")) { OperationId = "operation" },
            new SubAgentResultSubmittedEvent("run", "complete", new("session", "main"), "Report")];
        var codec = CoreAgentEventComposition.Instance.Codec;
        foreach (var evt in events)
        {
            codec.RequireDurable(evt);
            var json = codec.Serialize(evt);
            Assert.Equal(json, codec.Serialize(codec.DeserializeEvent(json)));
        }
    }

    [Fact]
    public async Task FunctionTimeoutCancelsQuestionWaiterAndPersistsTerminal()
    {
        var client = new FakeChatClient();
        client.EnqueueToolCall("AskUser", "ask", new Dictionary<string, object?>
        {
            ["questions"] = System.Text.Json.JsonDocument.Parse("""[{"id":"q","text":"Continue?"}]""").RootElement.Clone()
        });
        client.EnqueueTextResponse("No answer was received.");
        await using var agent = await new AgentBuilder(new AgentConfig { Name = "deadline" })
            .WithEventComposition(CoreAgentEventComposition.Instance).WithChatClient(new IdentifiedClient(client))
            .WithMiddleware(new HPD.Agent.Middleware.Function.FunctionTimeoutMiddleware(TimeSpan.FromMilliseconds(200))).BuildAsync();
        await agent.CreateSessionAsync("deadline");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await agent.RunAsync("Ask", sessionId: "deadline", runConfig: new() { AllowUserQuestions = true }, cancellationToken: deadline.Token);
        var key = new ThreadKey("deadline", "main");
        Assert.Empty(agent.GetPendingRequests(key));
        var events = (await agent.Config!.SessionStore!.CollectThreadEventsAsync(key))!;
        Assert.Single(events.OfType<UserQuestionRequestEvent>());
        Assert.Single(events.OfType<AgentRequestTerminatedEvent>());
        Assert.Empty(events.OfType<QuestionResponseEvent>());
    }

    private sealed class IdentifiedClient(FakeChatClient inner) : DelegatingChatClient(inner)
    {
        public override object? GetService(Type type, object? key = null)
            => type == typeof(HPD.Agent.Providers.ProviderClientExecutionIdentity)
                ? HPD.Agent.Providers.ProviderClientExecutionIdentity.CreateSafe("test", "test",
                    HPD.Agent.Providers.ProviderClientFamily.Chat, "fake", "test/chat", "test/final")
                : base.GetService(type, key);
    }
}
