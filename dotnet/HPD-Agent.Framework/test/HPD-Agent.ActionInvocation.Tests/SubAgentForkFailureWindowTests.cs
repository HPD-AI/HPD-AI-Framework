using HPD.Agent;
using HPD.Agent.Serialization;
using Microsoft.Extensions.AI;

namespace HPD.Agent.ActionInvocation.Tests;

public sealed class SubAgentForkFailureWindowTests
{
    [Fact]
    public async Task PreparingChildIsAbsentFromOrdinaryGetAndList()
    {
        var store = new InMemorySessionStore(CoreAgentEventComposition.Instance.Codec);
        var source = new ThreadKey("session", "source");
        var targetParent = new ThreadKey("session", "target-parent");
        var stagedChild = new ThreadKey("session", "staged-child");
        await CreateThreadAsync(store, source);
        var operationStore = new JournalThreadForkOperationStore(store, source);
        var prepared = new ThreadForkOperationRecord
        {
            OperationId = "preparing-child",
            Source = source,
            Target = targetParent,
            SourceBoundary = new ThreadJournalCursor(1, 1),
            RequestFingerprint = "fingerprint",
            SubAgentPolicy = SubAgentForkPolicy.ForkDirectChildren,
            Status = ThreadForkOperationStatus.Prepared,
            Revision = 1,
            PreparedChildren = [],
            ChildOutcomes = []
        };
        await operationStore.WriteThreadForkOperationAsync(
            prepared, new ThreadForkOperationWriteCondition(0));
        var operation = prepared with
        {
            Status = ThreadForkOperationStatus.ChildrenPreparing,
            Revision = 2,
            PreparedChildren = [stagedChild],
            ChildOutcomes = [new SubAgentForkChildOutcome(
                "reviewer-1",
                SubAgentForkPolicy.ForkDirectChildren,
                new ThreadKey("session", "source-child"),
                stagedChild,
                SubAgentChildAvailability.Available,
                "child-seed",
                new ThreadJournalCursor(1, 1),
                source,
                targetParent)]
        };
        await operationStore.WriteThreadForkOperationAsync(
            operation, new ThreadForkOperationWriteCondition(1));
        await store.AppendThreadEventsAsync(
            stagedChild,
            [new ThreadCreatedEvent(
                "reviewer-agent", null, null, null, null, DateTime.UtcNow,
                ThreadKind.SubAgent, ThreadVisibility.Hidden,
                targetParent.SessionId, targetParent.ThreadId, "reviewer",
                InvocationId: "create-reviewer",
                ParentToolCallId: "call-reviewer",
                ContextPolicy: "Fresh")
            {
                Preparation = new ThreadPreparationDescriptor(
                    operation.OperationId,
                    operation.Source,
                    operation.RequestFingerprint,
                    "child-seed",
                    operation.SourceBoundary),
                SessionId = stagedChild.SessionId,
                ThreadId = stagedChild.ThreadId
            }],
            new ThreadAppendCondition(ThreadJournalCursor.Start(1)));

        Assert.NotNull(await store.GetThreadEventHeadAsync(stagedChild));
        Assert.Null(await store.GetThreadAsync(stagedChild));
        var listed = new List<ThreadDescriptor>();
        await foreach (var descriptor in store.ListThreadsAsync(
                           stagedChild.SessionId,
                           new ThreadListRequest { IncludeHidden = true }))
            listed.Add(descriptor);
        Assert.DoesNotContain(listed, descriptor => descriptor.Key == stagedChild);
    }

    [Fact]
    public async Task SourceMutationAfterForkDoesNotChangeCopiedChild()
    {
        var fixture = await CreateForkFixtureAsync(activeChild: false);
        await using var agent = fixture.Agent;
        var fork = await agent.ForkThreadAsync(
            fixture.Source,
            "copy",
            fromMessageId: fixture.ForkPointId,
            new ThreadForkOptions
            {
                OperationId = "source-isolation",
                SubAgents = new SubAgentForkOptions
                {
                    Policy = SubAgentForkPolicy.ForkDirectChildren,
                    DescendantPolicy = SubAgentForkPolicy.Detach
                }
            });
        var copied = (await new SubAgentChildRegistry(fixture.Store)
            .ProjectAsync(new ThreadKey(fork.SessionId, fork.Id))).AvailableChildren[fixture.Child.LocalId];
        var copiedKey = copied.ChildThread;
        var copiedHeadBefore = Assert.IsType<ThreadEventHead>(
            await fixture.Store.GetThreadEventHeadAsync(copiedKey));

        await fixture.Store.AppendThreadEventsAsync(
            fixture.Child.ChildThread,
            [new ContentAddedEvent("late", "user", new TextContent("late source mutation"))
            {
                SessionId = fixture.Child.ChildThread.SessionId,
                ThreadId = fixture.Child.ChildThread.ThreadId
            }]);

        var copiedHeadAfter = Assert.IsType<ThreadEventHead>(
            await fixture.Store.GetThreadEventHeadAsync(copiedKey));
        Assert.Equal(copiedHeadBefore.Cursor, copiedHeadAfter.Cursor);
        var copiedEvents = new List<AgentEvent>();
        await foreach (var batch in fixture.Store.ReadThreadEventsAsync(
                           copiedKey,
                           new ThreadEventReadRequest(ThreadJournalCursor.Start(copiedHeadAfter.Generation))))
            copiedEvents.AddRange(batch.Events);
        Assert.DoesNotContain(copiedEvents, evt => evt is ContentAddedEvent { MessageId: "late" });
    }

    [Fact]
    public async Task FirstRunningChildFailsBeforeTargetBecomesVisible()
    {
        var fixture = await CreateForkFixtureAsync(activeChild: true);
        await using var agent = fixture.Agent;

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await agent.ForkThreadAsync(
                fixture.Source,
                "copy-active",
                fromMessageId: fixture.ForkPointId,
                new ThreadForkOptions
                {
                    OperationId = "active-child",
                    SubAgents = new SubAgentForkOptions
                    {
                        Policy = SubAgentForkPolicy.ForkDirectChildren,
                        DescendantPolicy = SubAgentForkPolicy.Detach
                    }
                }));

        Assert.Contains("boundary", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Null(await fixture.Store.GetThreadAsync(
            new ThreadKey(fixture.SourceKey.SessionId, "copy-active")));
        var operation = Assert.IsType<ThreadForkOperationRecord>(
            await new JournalThreadForkOperationStore(fixture.Store, fixture.SourceKey)
                .GetThreadForkOperationAsync("active-child"));
        Assert.Equal(ThreadForkOperationStatus.Aborted, operation.Status);
    }

    private static async Task<ForkFixture> CreateForkFixtureAsync(bool activeChild)
    {
        var store = new InMemorySessionStore(CoreAgentEventComposition.Instance.Codec);
        var agent = new Agent(new AgentConfig
        {
            Name = "test-agent",
            SessionStore = store,
            EventComposition = CoreAgentEventComposition.Instance
        }, baseClient: null, mergedOptions: null);
        var session = new Session($"failure-window-{activeChild}");
        await store.SaveSessionAsync(session);
        var source = session.CreateThread("test-agent", "main");
        await store.SaveInitialThreadAsync(session.Id, source);
        source.Session = session;
        var sourceKey = new ThreadKey(session.Id, source.Id);
        var childKey = new ThreadKey(session.Id, "main/subagent/reviewer-1");
        var childEvents = new List<AgentEvent>
        {
            new ThreadCreatedEvent(
                "reviewer-agent", null, null, null, null, DateTime.UtcNow,
                ThreadKind.SubAgent, ThreadVisibility.Hidden,
                sourceKey.SessionId, sourceKey.ThreadId, "reviewer",
                InvocationId: "create-reviewer", ParentToolCallId: "call-reviewer", ContextPolicy: "Fresh")
            {
                SessionId = childKey.SessionId,
                ThreadId = childKey.ThreadId
            }
        };
        if (activeChild)
            childEvents.Add(new ThreadExecutionStartedEvent(
                "running-execution", "reviewer-agent", DateTimeOffset.UtcNow));
        await store.AppendThreadEventsAsync(
            childKey, childEvents, new ThreadAppendCondition(ThreadJournalCursor.Start(1)));
        var child = new SubAgentChildReference
        {
            LocalId = new SubAgentLocalId("reviewer-1"),
            RoleName = "reviewer",
            CapabilityId = CapabilityId.Create("test:reviewer"),
            ChildAgentId = "reviewer-agent",
            ChildThread = childKey,
            CreationContext = SubAgentCreationContext.Fresh,
            CreationInvocationId = "create-reviewer",
            ParentToolCallId = "call-reviewer",
            ExecutionPolicy = SubAgentTestPolicies.Default,
            CreatedAt = DateTimeOffset.UtcNow
        };
        await new SubAgentChildRegistry(store).RegisterAsync(sourceKey, child);
        const string forkPointId = "fork-point";
        var forkPoint = new ChatMessage(ChatRole.User, "fork") { MessageId = forkPointId };
        source.AddMessage(forkPoint);
        await store.AppendThreadEventsAsync(
            sourceKey,
            [new ContentAddedEvent(forkPointId, "user", new TextContent("fork"))
            {
                SessionId = sourceKey.SessionId,
                ThreadId = sourceKey.ThreadId
            }]);
        return new ForkFixture(agent, store, source, sourceKey, child, forkPointId);
    }

    private static async Task CreateThreadAsync(ISessionStore store, ThreadKey key) =>
        _ = await store.AppendThreadEventsAsync(
            key,
            [new ThreadCreatedEvent("agent", null, null, null, null, DateTime.UtcNow)
            {
                SessionId = key.SessionId,
                ThreadId = key.ThreadId
            }],
            new ThreadAppendCondition(ThreadJournalCursor.Start(1)));

    private sealed record ForkFixture(
        Agent Agent,
        InMemorySessionStore Store,
        Thread Source,
        ThreadKey SourceKey,
        SubAgentChildReference Child,
        string ForkPointId);
}
