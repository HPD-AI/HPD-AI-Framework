using HPD.Agent;
using HPD.Agent.Serialization;

namespace HPD.Agent.ActionInvocation.Tests;

public sealed class ParentResultTests
{
    private static readonly ThreadKey Child = new("session", "child");
    private static readonly ThreadKey Controller = new("session", "continuing-controller");

    [Fact]
    public async Task SubmissionUsesExecutionControllerAndIsIdempotent()
    {
        var store = await CreateAsync();
        var first = await SubAgentResults.SubmitAsync(store, Child, "run", "call", "verified report", default);
        var retry = await SubAgentResults.SubmitAsync(store, Child, "run", "call", "verified report", default);
        Assert.Equal(Controller, first.Controller);
        Assert.Equal(first.Report, retry.Report);
        Assert.Single((await store.CollectThreadEventsAsync(Child))!.OfType<SubAgentResultSubmittedEvent>());
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            SubAgentResults.SubmitAsync(store, Child, "run", "other-call", "different result", default));
    }

    [Fact]
    public async Task ProseDoesNotCompleteAnAssignmentAndReportOnlyCompletesAfterTermination()
    {
        var store = await CreateAsync();
        await store.AppendThreadEventsAsync(Child, ThreadMessageEventConverter.ToThreadEvents(Child.SessionId,
            Child.ThreadId, new(Microsoft.Extensions.AI.ChatRole.Assistant, "I am done.")));
        Assert.Null(await SubAgentResults.ReadReportAsync(store, Child, "run", default));
        Assert.Equal("running", SubAgentActivityReader.Project((await store.CollectThreadEventsAsync(Child))!, "run").Status);
        await SubAgentResults.SubmitAsync(store, Child, "run", "call", "actual report", default);
        Assert.Equal("finishing", SubAgentActivityReader.Project((await store.CollectThreadEventsAsync(Child))!, "run").Status);
        await store.AppendThreadEventsAsync(Child,
            [new ThreadExecutionFinishedEvent("run", "worker", ThreadExecutionOutcome.Succeeded, DateTimeOffset.UtcNow)]);
        Assert.Equal("completed", (await SubAgentActivityReader.ReadAsync(store, Child)).Status);
    }

    [Fact]
    public async Task SuccessfulExitWithoutSubmissionIsStoppedWithoutResult()
    {
        var store = await CreateAsync();
        await store.AppendThreadEventsAsync(Child,
            [new ThreadExecutionFinishedEvent("run", "worker", ThreadExecutionOutcome.Succeeded, DateTimeOffset.UtcNow)]);
        Assert.Equal("stopped without result", (await SubAgentActivityReader.ReadAsync(store, Child)).Status);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            SubAgentResults.SubmitAsync(store, Child, "run", "late", "too late", default));
    }

    [Fact]
    public async Task FailureWinsOverReportAndNewExecutionDoesNotInheritCompletion()
    {
        var store = await CreateAsync();
        await SubAgentResults.SubmitAsync(store, Child, "run", "call", "report", default);
        await store.AppendThreadEventsAsync(Child,
            [new ThreadExecutionFinishedEvent("run", "worker", ThreadExecutionOutcome.Failed, DateTimeOffset.UtcNow, new ThreadExecutionError("failed", "failed"))]);
        Assert.Equal("failed", (await SubAgentActivityReader.ReadAsync(store, Child)).Status);
        await store.AppendThreadEventsAsync(Child,
            [new ThreadExecutionStartedEvent("next", "worker", DateTimeOffset.UtcNow)]);
        var state = SubAgentActivityReader.Project((await store.CollectThreadEventsAsync(Child))!, "next");
        Assert.Equal("running", state.Status);
        Assert.Null(state.Report);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            SubAgentResults.SubmitAsync(store, Child, "next", "call", "no controller", default));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CompleteTerminatesTheModelLoopAndPersistsItsToolResult(bool mixedBatch)
    {
        var client = new HPD.Agent.Tests.Infrastructure.FakeChatClient();
        var sideEffects = 0;
        if (mixedBatch) client.EnqueueToolCalls(
            ("Parent", "rejected-complete", new() { ["request"] = new Dictionary<string, object?> { ["action"] = "complete", ["report"] = "premature result" } }),
            ("Touch", "rejected-touch", new()));
        client.EnqueueToolCall("Parent", "complete-call", new Dictionary<string, object?>
        {
            ["request"] = new Dictionary<string, object?> { ["action"] = "complete", ["report"] = "verified result" }
        });
        await using var agent = await new AgentBuilder(new AgentConfig { Name = "worker" })
            .WithEventComposition(CoreAgentEventComposition.Instance)
            .WithChatClient(new IdentifiedClient(client))
            .WithNativeFunction(Microsoft.Extensions.AI.AIFunctionFactory.Create(() => ++sideEffects,
                new Microsoft.Extensions.AI.AIFunctionFactoryOptions { Name = "Touch" })).BuildAsync();
        await agent.CreateSessionAsync("session");
        var store = agent.Config.SessionStore!;
        var key = new ThreadKey("session", "main");
        await store.AppendThreadEventsAsync(key,
            [new ThreadExecutionStartedEvent("execution", agent.AgentId, DateTimeOffset.UtcNow),
             new SubAgentExecutionControllerEvent("execution", Controller) { ThreadExecutionId = "execution" }]);
        var input = new UserMessagesInputEvent
        {
            AgentId = agent.AgentId, SessionId = "session", ThreadId = "main", ThreadExecutionId = "execution",
            Messages = [new(Microsoft.Extensions.AI.ChatRole.User, "Finish your work")]
        };
        var reservation = new CoordinatorWorkReservation(agent.AgentId, "session", "main", "execution");
        reservation.BindPromotion(static _ => ValueTask.CompletedTask, static (_, _, _) => ValueTask.CompletedTask);
        await agent.RunAsync(agent.AuthorizeCoordinatorAssignedWork(input, reservation));
        Assert.Equal(0, sideEffects);
        Assert.Equal("verified result", await SubAgentResults.ReadReportAsync(store, key, "execution", default));
        var history = await store.ProjectThreadAsync("session", "main", ThreadProjectionPurpose.ThreadHistory);
        if (mixedBatch)
            Assert.Contains(history!.Messages.SelectMany(m => m.Contents), content =>
                content is Microsoft.Extensions.AI.FunctionResultContent result && result.CallId == "rejected-touch");
        Assert.Contains(history!.Messages.SelectMany(m => m.Contents), content =>
            content is Microsoft.Extensions.AI.FunctionResultContent result && result.CallId == "complete-call");
    }

    private sealed class IdentifiedClient(HPD.Agent.Tests.Infrastructure.FakeChatClient inner)
        : Microsoft.Extensions.AI.DelegatingChatClient(inner)
    {
        public override object? GetService(Type type, object? key = null)
            => type == typeof(HPD.Agent.Providers.ProviderClientExecutionIdentity)
                ? HPD.Agent.Providers.ProviderClientExecutionIdentity.CreateSafe("test", "test",
                    HPD.Agent.Providers.ProviderClientFamily.Chat, "fake", "test/chat", "test/final")
                : base.GetService(type, key);
    }

    private static async Task<InMemorySessionStore> CreateAsync()
    {
        var store = new InMemorySessionStore(CoreAgentEventComposition.Instance.Codec);
        await store.SaveSessionAsync(new Session(Child.SessionId));
        await store.AppendThreadEventsAsync(Child,
        [
            new ThreadCreatedEvent("worker", null, null, null, null, DateTime.UtcNow, ThreadKind.SubAgent,
                ThreadVisibility.Hidden, "session", "original-creator", "worker"),
            new ThreadExecutionStartedEvent("run", "worker", DateTimeOffset.UtcNow),
            new SubAgentExecutionControllerEvent("run", Controller) { ThreadExecutionId = "run" }
        ]);
        return store;
    }
}
