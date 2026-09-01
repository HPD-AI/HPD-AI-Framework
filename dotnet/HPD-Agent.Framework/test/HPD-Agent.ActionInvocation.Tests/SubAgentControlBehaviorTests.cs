using System.Text.Json;
using HPD.Agent;
using HPD.Agent.Middleware;
using HPD.Agent.Serialization;
using Microsoft.Extensions.AI;

namespace HPD.Agent.ActionInvocation.Tests;

public sealed class SubAgentControlBehaviorTests
{
    [Fact]
    public async Task ListAndWaitReportTerminalIdleAndDetachedChildren()
    {
        var (store, parent, context) = await CreateParentAsync();
        var finished = await RegisterChildAsync(store, parent, "worker-1", "child-1");
        await RegisterChildAsync(store, parent, "worker-2", "child-2");
        await new SubAgentChildRegistry(store).RegisterAsync(parent, new SubAgentChildReference
        {
            LocalId = new SubAgentLocalId("worker-3"),
            RoleName = "worker",
            CapabilityId = CapabilityId.Create("test:worker-3"),
            ChildAgentId = "worker-agent",
            ChildThread = new ThreadKey(parent.SessionId, "detached-child"),
            CreationContext = SubAgentCreationContext.Fresh,
            CreationInvocationId = "create-3",
            ParentToolCallId = "call-3",
            ExecutionPolicy = SubAgentRunConfig.Inherit().CompilePolicy(),
            CreatedAt = DateTimeOffset.UtcNow
        });
        await store.AppendThreadEventsAsync(parent,
            [new SubAgentChildDetachedEvent(new SubAgentLocalId("worker-3"), "detached")]);
        await store.AppendThreadEventsAsync(
            finished,
            [
                new ThreadExecutionStartedEvent("execution-1", "worker-agent", DateTimeOffset.UtcNow),
                new ThreadExecutionFinishedEvent(
                    "execution-1", "worker-agent", ThreadExecutionOutcome.Succeeded, DateTimeOffset.UtcNow)
            ]);

        using var listJson = JsonDocument.Parse("{}");
        var listed = Assert.IsType<SubAgentListResult>(await SubAgentRuntime.ControlAsync(
            "list", listJson.RootElement, context, CancellationToken.None));
        Assert.Equal(3, listed.Children.Count);
        Assert.Contains(listed.Children, child => child.Child == "worker-3" &&
            child.Availability == SubAgentChildAvailability.Detached);

        using var waitJson = JsonDocument.Parse("""{"mode":"all","timeoutSeconds":1}""");
        var waited = Assert.IsType<SubAgentWaitResult>(await SubAgentRuntime.ControlAsync(
            "wait", waitJson.RootElement, context, CancellationToken.None));
        Assert.False(waited.TimedOut);
        Assert.Contains(waited.Children, child => child.Child == "worker-1" && child.Status == ThreadExecutionStatus.Succeeded);
        Assert.Contains(waited.Children, child => child.Child == "worker-2" && child.Status == "idle");
        Assert.Contains(waited.Children, child => child.Child == "worker-3" && child.Status == "unavailable");
    }

    [Fact]
    public async Task WaitTimesOutForTheExactLatestActiveExecution()
    {
        var (store, parent, context) = await CreateParentAsync();
        var child = await RegisterChildAsync(store, parent, "worker-1", "child-1");
        await store.AppendThreadEventsAsync(
            child,
            [new ThreadExecutionStartedEvent("execution-active", "worker-agent", DateTimeOffset.UtcNow)]);

        using var waitJson = JsonDocument.Parse(
            """{"children":["worker-1"],"mode":"any","timeoutSeconds":0}""");
        var waited = Assert.IsType<SubAgentWaitResult>(await SubAgentRuntime.ControlAsync(
            "wait", waitJson.RootElement, context, CancellationToken.None));

        Assert.True(waited.TimedOut);
        Assert.Empty(waited.Children);
    }

    private static async Task<(InMemorySessionStore Store, ThreadKey Parent, FunctionExecutionContext Context)>
        CreateParentAsync()
    {
        var store = new InMemorySessionStore(CoreAgentEventComposition.Instance.Codec);
        var session = new Session("session");
        await store.SaveSessionAsync(session);
        var thread = session.CreateThread("parent-agent", "parent");
        await store.SaveInitialThreadAsync(session.Id, thread);
        thread.Session = session;
        session.Store = store;
        var function = AIFunctionFactory.Create(
            (string input) => input,
            new AIFunctionFactoryOptions { Name = "SubAgents" });
        var state = AgentLoopState.InitialSafe([], "run", "conversation", "parent-agent");
        var agentContext = new AgentContext(
            "parent-agent",
            "conversation",
            state,
            new HPD.Events.Core.EventCoordinator(),
            session,
            thread,
            CancellationToken.None);
        var before = agentContext.AsBeforeFunction(
            function,
            "tool-call",
            new Dictionary<string, object?>(),
            new AgentRunConfig(),
            toolharnessName: null,
            skillName: null);
        return (store, new ThreadKey(session.Id, thread.Id), new FunctionExecutionContext(
            before,
            new FunctionRequest
            {
                Function = function,
                CallId = "tool-call",
                Arguments = new Dictionary<string, object?>(),
                State = state,
                ResultMetadata = new ToolResultMetadata(),
                EventCoordinator = agentContext.EventCoordinator
            }));
    }

    private static async Task<ThreadKey> RegisterChildAsync(
        InMemorySessionStore store,
        ThreadKey parent,
        string localId,
        string threadId)
    {
        var route = new ThreadKey(parent.SessionId, threadId);
        await store.AppendThreadEventsAsync(
            route,
            [new ThreadCreatedEvent(
                "worker-agent", null, null, null, null, DateTime.UtcNow,
                ThreadKind.SubAgent, ThreadVisibility.Hidden,
                parent.SessionId, parent.ThreadId, "worker",
                ParentToolCallId: $"call-{localId}")
            {
                SessionId = route.SessionId,
                ThreadId = route.ThreadId
            }],
            new ThreadAppendCondition(ThreadJournalCursor.Start(1)));
        await new SubAgentChildRegistry(store).RegisterAsync(parent, new SubAgentChildReference
        {
            LocalId = new SubAgentLocalId(localId),
            RoleName = "worker",
            CapabilityId = CapabilityId.Create($"test:{localId}"),
            ChildAgentId = "worker-agent",
            ChildThread = route,
            CreationContext = SubAgentCreationContext.Fresh,
            CreationInvocationId = $"create-{localId}",
            ParentToolCallId = $"call-{localId}",
            ExecutionPolicy = SubAgentRunConfig.Inherit().CompilePolicy(),
            CreatedAt = DateTimeOffset.UtcNow
        });
        return route;
    }
}
