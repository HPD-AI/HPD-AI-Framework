using HPD.Agent.Goals;
using HPD.Agent.Serialization;
using HPD.Agent.Tests.Infrastructure;

namespace HPD.Agent.Tests;

public class GoalRuntimeTests
{
    private class IdentifiedClient(FakeChatClient inner) : Microsoft.Extensions.AI.DelegatingChatClient(inner)
    {
        public override object? GetService(Type type, object? key = null)
            => type == typeof(HPD.Agent.Providers.ProviderClientExecutionIdentity)
                ? HPD.Agent.Providers.ProviderClientExecutionIdentity.CreateSafe("test", "test",
                    HPD.Agent.Providers.ProviderClientFamily.Chat, "fake", "test/chat", "test/final")
                : base.GetService(type, key);
    }

    private sealed class BlockingClient() : IdentifiedClient(new FakeChatClient())
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async IAsyncEnumerable<Microsoft.Extensions.AI.ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<Microsoft.Extensions.AI.ChatMessage> messages, Microsoft.Extensions.AI.ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
            yield break;
        }
    }

    [Fact]
    public async Task CallerCancellationPausesAndClosesAttribution()
    {
        var client = new BlockingClient();
        await using var agent = await new AgentBuilder(new AgentConfig { Name = "goal-test" })
            .WithEventComposition(CoreAgentEventComposition.Instance).WithChatClient(client).WithGoals().BuildAsync();
        await agent.CreateSessionAsync("s1");
        using var cancellation = new CancellationTokenSource();
        var running = agent.RunAsync(new CreateGoalInputEvent { Objective = "Verify migration", SessionId = "s1", ThreadId = "main" }, cancellation.Token);
        await client.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running.WaitAsync(TimeSpan.FromSeconds(10)));
        var state = await GoalPersistence.ReadAsync(agent.Config!.SessionStore!, new("s1", "main"), default);
        Assert.Equal(GoalStatus.Paused, state.Goal.Current!.Status);
        Assert.Null(state.Goal.Current.Continuation);
        Assert.Null(state.Goal.PendingExecution);
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RuntimeShutdownPreservesActiveWhileControllerCancellationPauses(bool shutdown)
    {
        var client = new BlockingClient();
        await using var agent = await new AgentBuilder(new AgentConfig { Name = "goal-test" })
            .WithEventComposition(CoreAgentEventComposition.Instance).WithChatClient(client).WithGoals().BuildAsync();
        await agent.CreateSessionAsync("s1");
        await agent.StartAsync();
        var running = agent.RunAsync(new CreateGoalInputEvent { Objective = "Verify cancellation", SessionId = "s1", ThreadId = "main" });
        await client.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var controller = ThreadExecutionControllerRegistry.For(agent.Config!.SessionStore!);
        var key = new ThreadKey("s1", "main");
        if (shutdown)
        {
            using var drainLimit = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
            await agent.StopAsync(drainLimit.Token).WaitAsync(TimeSpan.FromSeconds(10));
        }
        else
        {
            var active = await controller.FindActiveAsync(key);
            var mismatch = await controller.CancelAsync(key, "stale-execution", "Must not cancel");
            Assert.False(mismatch.Accepted);
            Assert.False(running.IsCompleted);
            await controller.CancelAsync(key, active.ThreadExecutionId!, "User requested pause");
        }
        if (shutdown) await running.WaitAsync(TimeSpan.FromSeconds(10));
        else await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running.WaitAsync(TimeSpan.FromSeconds(10)));
        var state = await GoalPersistence.ReadAsync(agent.Config.SessionStore!, key, default);
        Assert.Equal(shutdown ? GoalStatus.Active : GoalStatus.Paused, state.Goal.Current!.Status);
        Assert.Null(state.Goal.PendingExecution);
        Assert.Null(state.Goal.Current.Continuation);
        var errors = new List<MessageTurnErrorEvent>();
        await foreach (var batch in agent.Config.SessionStore!.ReadThreadEventsAsync(key,
            new(ThreadJournalCursor.Start(state.Cursor.Generation), state.Cursor.SequenceNumber)))
            errors.AddRange(batch.Events.OfType<MessageTurnErrorEvent>());
        var cancellation = Assert.Single(errors).Cancellation!;
        Assert.Equal(shutdown ? AgentInputCancellationCause.RuntimeShutdown : AgentInputCancellationCause.Explicit, cancellation.Cause);
        if (!shutdown) Assert.Equal("User requested pause", cancellation.Reason);
    }

    [Fact]
    public async Task StartedRuntimeContinuesThroughQueuedInputAndCompletesAfterClosedAccounting()
    {
        var client = new FakeChatClient();
        client.EnqueueToolCall("goal", "blocker", new() { ["operation"] = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(
            "{\"action\":\"reportBlocker\",\"category\":\"Environment\",\"description\":\"Check pending\",\"requiredChange\":\"Inspect alternative\"}") });
        client.EnqueueTextResponse("Checking alternatives.");
        client.EnqueueToolCall("goal", "complete", new() { ["operation"] = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(
            "{\"action\":\"proposeCompletion\",\"summary\":\"Verified outcome\",\"evidence\":[{\"kind\":\"test\",\"description\":\"Acceptance tests passed\"}]}") });
        client.EnqueueTextResponse("Verified.");
        await using var agent = await new AgentBuilder(new AgentConfig { Name = "goal-test" })
            .WithEventComposition(CoreAgentEventComposition.Instance).WithChatClient(new IdentifiedClient(client)).WithGoals().BuildAsync();
        await agent.CreateSessionAsync("s1");
        var completed = new TaskCompletionSource<GoalCompletedEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        var diagnostics = new System.Collections.Concurrent.ConcurrentQueue<string>();
        using var diagnosticSubscription = agent.Subscribe<AgentEvent>(evt =>
        {
            if (evt is ToolCallResultEvent or MessageTurnErrorEvent || evt.GetType().Namespace == typeof(GoalData).Namespace)
                diagnostics.Enqueue(CoreAgentEventComposition.Instance.Codec.Serialize(evt));
            return ValueTask.CompletedTask;
        });
        using var subscription = agent.Subscribe<GoalCompletedEvent>(evt =>
        {
            completed.TrySetResult(evt);
            return ValueTask.CompletedTask;
        });
        await agent.StartAsync();
        await agent.RunAsync(new CreateGoalInputEvent { Objective = "Verify migration", SessionId = "s1", ThreadId = "main" });
        GoalCompletedEvent outcome;
        try { outcome = await completed.Task.WaitAsync(TimeSpan.FromSeconds(15)); }
        catch (TimeoutException)
        {
            throw new Xunit.Sdk.XunitException($"Model requests: {client.CapturedRequests.Count}. " + string.Join("\n", diagnostics));
        }
        Assert.Equal(GoalStatus.Completed, outcome.Goal.Status);
        Assert.Equal("Verified outcome", outcome.AcceptedProposal!.Summary);
        Assert.Equal(2, outcome.Goal.Accounting.ExecutionCount);
        Assert.Equal(4, client.CapturedRequests.Count);
        await agent.StopAsync();
    }

    [Fact]
    public async Task DirectCreationCommitsBeforeModelAndLeavesActiveWithoutReservation()
    {
        var client = new FakeChatClient();
        client.EnqueueTextResponse("Initial work done; more remains.");
        await using var agent = await new AgentBuilder(new AgentConfig() { Name = "goal-test" })
            .WithEventComposition(CoreAgentEventComposition.Instance).WithChatClient(new IdentifiedClient(client)).WithGoals().BuildAsync();
        await agent.CreateSessionAsync("s1");
        await agent.RunAsync(new CreateGoalInputEvent { Objective = "Verify migration", SessionId = "s1", ThreadId = "main" });
        Assert.False(agent.IsRunning);
        var state = await GoalPersistence.ReadAsync(agent.Config!.SessionStore!, new("s1", "main"), default);
        Assert.Equal(GoalStatus.Active, state.Goal.Current!.Status);
        Assert.Equal(1, state.Goal.Current.Accounting.ExecutionCount);
        Assert.Null(state.Goal.Current.Continuation);
        Assert.Null(state.Goal.PendingExecution);
        Assert.Contains(client.CapturedRequests[0], m => m.Text.Contains("PERSISTENT GOAL CONTEXT"));
        Assert.Single(client.CapturedRequests[0], m => m.Role == Microsoft.Extensions.AI.ChatRole.User && m.Text == "Verify migration");
        var durable = await agent.Config.SessionStore!.ProjectThreadAsync("s1", "main", ThreadProjectionPurpose.ModelContext);
        Assert.DoesNotContain(durable!.Messages, m => m.Text.Contains("PERSISTENT GOAL CONTEXT"));
        Assert.Single(durable.Messages, m => m.Role == Microsoft.Extensions.AI.ChatRole.User && m.Text == "Verify migration");
    }

    [Fact]
    public async Task RestartUsesCurrentProviderConfigurationForReconciledGoal()
    {
        var store = new InMemorySessionStore(CoreAgentEventComposition.Instance.Codec);
        var firstClient = new FakeChatClient();
        firstClient.EnqueueTextResponse("More verification remains.");
        await using (var first = await new AgentBuilder(new AgentConfig { Name = "goal-test", SessionStore = store })
            .WithEventComposition(CoreAgentEventComposition.Instance).WithChatClient(new IdentifiedClient(firstClient)).WithGoals().BuildAsync())
        {
            await first.CreateSessionAsync("s1");
            await first.RunAsync(new CreateGoalInputEvent { Objective = "Verify restart", SessionId = "s1", ThreadId = "main" });
        }
        var secondClient = new FakeChatClient();
        secondClient.EnqueueToolCall("goal", "complete", new() { ["operation"] = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(
            "{\"action\":\"proposeCompletion\",\"summary\":\"Verified after restart\",\"evidence\":[{\"kind\":\"test\",\"description\":\"Restart passed\"}]}") });
        secondClient.EnqueueTextResponse("Verified.");
        var unusedDefault = new FakeChatClient();
        await using var second = await new AgentBuilder(new AgentConfig { Name = "goal-test", SessionStore = store })
            .WithEventComposition(CoreAgentEventComposition.Instance).WithChatClient(new IdentifiedClient(unusedDefault)).WithGoals().BuildAsync();
        await second.RestoreThreadAsync("s1", "main");
        Assert.Empty(secondClient.CapturedRequests);
        var completion = new TaskCompletionSource<GoalCompletedEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscription = second.Subscribe<GoalCompletedEvent>(evt => { completion.TrySetResult(evt); return ValueTask.CompletedTask; });
        await second.StartAsync(new AgentRunConfig
        {
            Clients = new AgentClientsConfig { Chat = new ChatClientConfig
            {
                Override = HPD.Agent.Providers.ClientOverride<Microsoft.Extensions.AI.IChatClient>.Borrow(new IdentifiedClient(secondClient), "test", "local")
            } }
        });
        var completed = await completion.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(2, completed.Goal.Accounting.ExecutionCount);
        Assert.Equal("Verified after restart", completed.AcceptedProposal!.Summary);
        Assert.Empty(unusedDefault.CapturedRequests);
        Assert.Equal(2, secondClient.CapturedRequests.Count);
        await second.StopAsync();
    }

    [Fact]
    public async Task ConversationalCreateCommitsAndAccountsTheCreatingTurn()
    {
        var client = new FakeChatClient();
        client.EnqueueToolCall("goal", "create", new() { ["operation"] = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(
            "{\"action\":\"create\",\"objective\":\"Verify conversational creation\"}") });
        client.EnqueueTextResponse("Goal created.");
        await using var agent = await new AgentBuilder(new AgentConfig { Name = "goal-test" })
            .WithEventComposition(CoreAgentEventComposition.Instance).WithChatClient(new IdentifiedClient(client)).WithGoals().BuildAsync();
        await agent.CreateSessionAsync("s1");
        await agent.RunAsync(new UserMessagesInputEvent { SessionId = "s1", ThreadId = "main",
            Messages = [new(Microsoft.Extensions.AI.ChatRole.User, "Create a persistent goal to verify conversational creation.")] });
        var state = (await GoalPersistence.ReadAsync(agent.Config!.SessionStore!, new("s1", "main"), default)).Goal;
        Assert.Equal("Verify conversational creation", state.Current!.Objective);
        Assert.Equal(1, state.Current.Accounting.ExecutionCount);
        Assert.Null(state.PendingExecution);
    }

    [Fact]
    public async Task SecondCreationIsRejectedBeforeAnotherModelRequest()
    {
        var client = new FakeChatClient();
        client.EnqueueTextResponse("Initial work.");
        await using var agent = await new AgentBuilder(new AgentConfig() { Name = "goal-test" })
            .WithEventComposition(CoreAgentEventComposition.Instance).WithChatClient(new IdentifiedClient(client)).WithGoals().BuildAsync();
        await agent.CreateSessionAsync("s1");
        var input = new CreateGoalInputEvent { Objective = "Verify migration", SessionId = "s1", ThreadId = "main" };
        await agent.RunAsync(input);
        await Assert.ThrowsAnyAsync<Exception>(() => agent.RunAsync(input with { Objective = "Replace it silently" }));
        Assert.Single(client.CapturedRequests);
    }
}
