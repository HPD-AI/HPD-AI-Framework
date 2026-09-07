using System.Runtime.CompilerServices;
using System.Text.Json;
using HPD.Agent;
using HPD.Agent.Goals;
using HPD.Agent.Providers;
using HPD.Agent.Serialization;
using Microsoft.Extensions.AI;

var fixture = new GoalData { GoalId = "fixture", Objective = "Verify lifecycle metadata", Status = GoalStatus.Active, Revision = 1 };
AgentEvent[] lifecycleFixtures =
[
    new GoalStartedEvent(fixture, "metadata_smoke"),
    new GoalUpdatedEvent(fixture, "metadata_smoke"),
    new GoalPausedEvent(fixture, "metadata_smoke"),
    new GoalResumedEvent(fixture, "metadata_smoke"),
    new GoalEditedEvent(fixture, "metadata_smoke"),
    new GoalClearedEvent(fixture, "metadata_smoke"),
    new GoalContinuationScheduledEvent(fixture, "metadata_smoke"),
    new GoalContinuationStartedEvent(fixture, "metadata_smoke"),
    new GoalContinuationSkippedEvent(fixture, "metadata_smoke"),
    new GoalProgressAccountedEvent(fixture, "metadata_smoke"),
    new GoalCompletionProposedEvent(fixture, "metadata_smoke"),
    new GoalCompletionRejectedEvent(fixture, "metadata_smoke"),
    new GoalCompletedEvent(fixture, "metadata_smoke"),
    new GoalBlockerReportedEvent(fixture, "metadata_smoke"),
    new GoalBlockerRejectedEvent(fixture, "metadata_smoke"),
    new GoalAwaitingInputEvent(fixture, "metadata_smoke"),
    new GoalBlockedEvent(fixture, "metadata_smoke"),
    new GoalUsageLimitedEvent(fixture, "metadata_smoke"),
    new GoalFaultedEvent(fixture, "metadata_smoke")
];
var eventCodec = CoreAgentEventComposition.Instance.Codec;
foreach (var evt in lifecycleFixtures)
{
    _ = eventCodec.RequireDurable(evt);
    var json = eventCodec.Serialize(evt);
    if (eventCodec.Serialize(eventCodec.DeserializeEvent(json)) != json) throw new Exception("Goal lifecycle metadata did not round-trip.");
}

var client = new SmokeClient();
var config = HpdAgentConfigSerializer.Deserialize("{\"name\":\"goals-aot-smoke\",\"goals\":{\"enabled\":true}}", HPD.Serialization.HpdConfigFormat.Json)!;
await using var agent = await new AgentBuilder(config)
    .WithEventComposition(CoreAgentEventComposition.Instance)
    .WithChatClient(client)
    .BuildAsync();
await agent.CreateSessionAsync("smoke");
var completion = new TaskCompletionSource<GoalCompletedEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
using var subscription = agent.Subscribe<GoalCompletedEvent>(evt =>
{
    completion.TrySetResult(evt);
    return ValueTask.CompletedTask;
});
await agent.StartAsync();
var codec = new AgentInputCodec(ProviderComposition.Create([]));
var input = new CreateGoalInputEvent { Objective = "Verify native Goal continuation", SessionId = "smoke", ThreadId = "main" };
await agent.RunAsync(codec.Deserialize(codec.Serialize(input)));
var completed = await completion.Task.WaitAsync(TimeSpan.FromSeconds(20));
await agent.StopAsync();
if (completed.Goal.Status != GoalStatus.Completed || completed.Goal.Accounting.ExecutionCount != 2 || client.Calls != 4 || completed.Goal.Accounting.TokensUsed != 20 || completed.AcceptedProposal is null)
    throw new InvalidOperationException("Goal continuation smoke result is invalid.");
await agent.CreateSessionAsync("readonly");
await agent.RunAsync(new CreateGoalInputEvent { Objective = "Verify read-only composition", SessionId = "readonly", ThreadId = "main",
    RunConfig = new() { Goals = new() { ToolAccess = GoalToolAccess.ReadOnly } } });
await agent.CreateSessionAsync("hidden");
await agent.RunAsync(new CreateGoalInputEvent { Objective = "Verify hidden composition", SessionId = "hidden", ThreadId = "main",
    RunConfig = new() { Goals = new() { ToolAccess = GoalToolAccess.Hidden } } });
await agent.CreateSessionAsync("conversational");
GoalData? conversationalGoal = null;
using var conversationalSubscription = agent.Subscribe<GoalProgressAccountedEvent>(evt =>
{
    if (evt.SessionId == "conversational") conversationalGoal = evt.Goal;
    return ValueTask.CompletedTask;
});
await agent.RunAsync(new UserMessagesInputEvent { SessionId = "conversational", ThreadId = "main",
    Messages = [new(ChatRole.User, "Create a persistent Goal to verify conversational creation.")] });
if (conversationalGoal?.Accounting.ExecutionCount != 1) throw new Exception("Conversational creation was not accounted.");
Console.WriteLine("PASS: generated Goal binding, input codec, durable events, queued continuation, and terminal accounting.");

sealed class SmokeClient : IChatClient
{
    public int Calls { get; private set; }
    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        CancellationToken cancellationToken = default) => throw new NotSupportedException("Smoke uses streaming.");

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
        ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Task.Yield();
        var call = ++Calls;
        var function = options?.Tools?.OfType<HPDAIFunctionFactory.HPDAIFunction>().SingleOrDefault(tool => tool.Name == "goal");
        if (call == 1)
        {
            if (function?.OperationContract?.Actions.Count != 8) throw new Exception("Missing generated actions.");
            foreach (var action in new[]
            {
                "{\"action\":\"create\",\"objective\":\"Outcome\"}", "{\"action\":\"get\"}",
                "{\"action\":\"pause\"}", "{\"action\":\"resume\"}", "{\"action\":\"clear\"}",
                "{\"action\":\"edit\",\"objective\":\"Updated outcome\"}",
                "{\"action\":\"reportBlocker\",\"category\":\"Environment\",\"description\":\"Unavailable\",\"requiredChange\":\"Restore\"}",
                "{\"action\":\"proposeCompletion\",\"summary\":\"Verified\",\"evidence\":[{\"kind\":\"test\",\"description\":\"Passed\"}]}"
            })
            {
                using var args = JsonDocument.Parse("{\"operation\":" + action + "}");
                _ = function.ArgumentBinder!(args.RootElement);
            }
        }
        if (call == 5)
        {
            if (function?.OperationContract?.Actions.Count != 1 || !function.OperationContract.Actions.ContainsKey("get"))
                throw new Exception("Read-only schema was not restricted.");
            using var forged = JsonDocument.Parse("{\"operation\":{\"action\":\"clear\"}}");
            var rejected = false;
            try { _ = function.ArgumentBinder!(forged.RootElement); }
            catch (InvalidOperationException) { rejected = true; }
            if (!rejected) throw new Exception("Read-only binder accepted a mutation.");
        }
        if (call == 6 && function is not null) throw new Exception("Hidden Goal tool remained visible.");
        yield return new ChatResponseUpdate { Contents = [new UsageContent(new UsageDetails { InputTokenCount = 3, OutputTokenCount = 2, TotalTokenCount = 5 })] };
        if (call is 1 or 3 or 7)
        {
            var json = call == 1
                ? "{\"action\":\"reportBlocker\",\"category\":\"Environment\",\"description\":\"Check pending\",\"requiredChange\":\"Inspect alternative\"}"
                : call == 7 ? "{\"action\":\"create\",\"objective\":\"Verify conversational creation\"}"
                : "{\"action\":\"proposeCompletion\",\"summary\":\"Verified\",\"evidence\":[{\"kind\":\"test\",\"description\":\"Native acceptance passed\"}]}";
            using var document = JsonDocument.Parse(json);
            yield return new ChatResponseUpdate
            {
                Contents = [new FunctionCallContent($"call-{call}", "goal", new Dictionary<string, object?>
                {
                    ["operation"] = document.RootElement.Clone()
                })], FinishReason = ChatFinishReason.ToolCalls
            };
        }
        else if (call is 2 or 4 or 5 or 6 or 8)
            yield return new ChatResponseUpdate { Contents = [new TextContent("Work verified.")], FinishReason = ChatFinishReason.Stop };
        else throw new InvalidOperationException("Unexpected extra model request.");
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
        => serviceType == typeof(ProviderStreamingUsageSemanticsDeclaration)
            ? new ProviderStreamingUsageSemanticsDeclaration(ProviderClientFamily.Chat, UsageUpdateSemantics.FinalOnly, "smoke", "goals-aot-final-only")
            : serviceType == typeof(ProviderClientExecutionIdentity) ? new ProviderClientExecutionIdentity
        {
            ProviderKey = "smoke", BackendKey = "smoke", Family = ProviderClientFamily.Chat,
            ModelName = "smoke", OperationAdapterKey = "smoke/chat", UsageSemanticsKey = "smoke/final",
            SafeConfigurationFingerprint = "smoke"
        } : null;

    public void Dispose() { }
}
