using HPD.Agent;
using HPD.Agent.Serialization;
using Microsoft.Extensions.AI;

namespace HPD.Agent.ActionInvocation.Tests;

public sealed class SubAgentForkCrashRecoveryTests
{
    [Fact]
    public async Task RestartFromChildrenPreparingCommitsOneExactPreparedChild()
    {
        var firstStore = new InMemorySessionStore(CoreAgentEventComposition.Instance.Codec);
        var firstSession = new Session("fork-crash-recovery");
        await firstStore.SaveSessionAsync(firstSession);
        var firstSource = firstSession.CreateThread("test-agent", "main");
        await firstStore.SaveInitialThreadAsync(firstSession.Id, firstSource);
        firstSource.Session = firstSession;
        var sourceKey = new ThreadKey(firstSession.Id, firstSource.Id);
        var sourceChild = new ThreadKey(firstSession.Id, "main/subagent/reviewer-1");
        await AppendSubAgentAsync(firstStore, sourceChild, sourceKey);
        var childReference = new SubAgentChildReference
        {
            LocalId = new SubAgentLocalId("reviewer-1"),
            RoleName = "reviewer",
            CapabilityId = CapabilityId.Create("test:reviewer"),
            ChildAgentId = "reviewer-agent",
            Availability = SubAgentChildAvailability.Available,
            ChildThread = sourceChild,
            CreationContext = SubAgentCreationContext.Fresh,
            CreationInvocationId = "create-reviewer",
            ParentToolCallId = "call-reviewer",
            CreatedAt = DateTimeOffset.UtcNow
        };
        await new SubAgentChildRegistry(firstStore).RegisterAsync(sourceKey, childReference);
        const string forkPointId = "fork-point";
        var forkPoint = new ChatMessage(ChatRole.User, "fork") { MessageId = forkPointId };
        firstSource.AddMessage(forkPoint);
        await firstStore.AppendThreadEventsAsync(
            sourceKey,
            [new ContentAddedEvent(forkPointId, "user", new TextContent("fork"))
            {
                SessionId = sourceKey.SessionId,
                ThreadId = sourceKey.ThreadId
            }]);
        await using (var firstAgent = CreateAgent(firstStore))
        {
            _ = await firstAgent.ForkThreadAsync(
                firstSource,
                "copy",
                fromMessageId: forkPointId,
                new ThreadForkOptions
                {
                    OperationId = "crash-recovery",
                    SubAgents = new SubAgentForkOptions
                    {
                        Policy = SubAgentForkPolicy.ForkDirectChildren,
                        DescendantPolicy = SubAgentForkPolicy.Detach
                    }
                });
        }

        var sourceEvents = await ReadAllAsync(firstStore, sourceKey);
        var preparingEvent = sourceEvents
            .OfType<ThreadForkOperationChangedEvent>()
            .Where(evt => evt.Operation.OperationId == "crash-recovery" &&
                evt.Operation.Status == ThreadForkOperationStatus.ChildrenPreparing &&
                evt.Operation.ChildOutcomes.Count == 1)
            .Last();
        var admitted = Assert.Single(preparingEvent.Operation.ChildOutcomes);
        var preparedChild = Assert.IsType<ThreadKey>(admitted.Target);
        var sourceCrashImage = sourceEvents
            .Where(evt => evt.ThreadSequenceNumber <= preparingEvent.ThreadSequenceNumber)
            .Select(ResetPosition)
            .ToArray();
        var childCrashImage = (await ReadAllAsync(firstStore, preparedChild))
            .Select(ResetPosition)
            .ToArray();
        var sourceChildImage = (await ReadAllAsync(firstStore, sourceChild))
            .Select(ResetPosition)
            .ToArray();

        var restartStore = new InMemorySessionStore(CoreAgentEventComposition.Instance.Codec);
        var restartSession = new Session(firstSession.Id);
        await restartStore.SaveSessionAsync(restartSession);
        await restartStore.AppendThreadEventsAsync(
            sourceKey, sourceCrashImage, new ThreadAppendCondition(ThreadJournalCursor.Start(1)));
        await restartStore.AppendThreadEventsAsync(
            sourceChild, sourceChildImage, new ThreadAppendCondition(ThreadJournalCursor.Start(1)));
        await restartStore.AppendThreadEventsAsync(
            preparedChild, childCrashImage, new ThreadAppendCondition(ThreadJournalCursor.Start(1)));
        var restartedChildEvents = await ReadAllAsync(restartStore, preparedChild);
        var restartedCreated = Assert.Single(restartedChildEvents.OfType<ThreadCreatedEvent>());
        Assert.Equal(preparingEvent.Operation.OperationId, restartedCreated.Preparation?.OperationId);
        Assert.Equal(admitted.SourceBoundary, restartedCreated.Preparation?.SourceBoundary);
        Assert.Equal(admitted.TargetSeedFingerprint, restartedCreated.Preparation?.TargetSeedFingerprint);
        var restartSource = Assert.IsType<Thread>(await restartStore.ProjectThreadAsync(
            sourceKey.SessionId, sourceKey.ThreadId, ThreadProjectionPurpose.ForkConstruction));
        restartSource.Session = restartSession;

        await using var restartAgent = CreateAgent(restartStore);
        var recovered = await restartAgent.ForkThreadAsync(
            restartSource,
            "copy",
            fromMessageId: forkPointId,
            new ThreadForkOptions
            {
                OperationId = "crash-recovery",
                SubAgents = new SubAgentForkOptions
                {
                    Policy = SubAgentForkPolicy.ForkDirectChildren,
                    DescendantPolicy = SubAgentForkPolicy.Detach
                }
            });

        var recoveredKey = new ThreadKey(recovered.SessionId, recovered.Id);
        var projection = await new SubAgentChildRegistry(restartStore).ProjectAsync(recoveredKey);
        var recoveredChild = Assert.Single(projection.Children).Value;
        Assert.Equal(preparedChild, recoveredChild.ChildThread);
        Assert.NotNull(await restartStore.GetThreadAsync(preparedChild));
        var committed = Assert.IsType<ThreadForkOperationRecord>(
            await new JournalThreadForkOperationStore(restartStore, sourceKey)
                .GetThreadForkOperationAsync("crash-recovery"));
        Assert.Equal(ThreadForkOperationStatus.Committed, committed.Status);
        Assert.Equal(preparedChild, Assert.Single(committed.ChildOutcomes).Target);
        var allThreads = new List<ThreadDescriptor>();
        await foreach (var descriptor in restartStore.ListThreadsAsync(
                           sourceKey.SessionId,
                           new ThreadListRequest { IncludeHidden = true }))
            allThreads.Add(descriptor);
        Assert.Equal(2, allThreads.Count(descriptor => descriptor.Kind == ThreadKind.SubAgent));
        Assert.Single(allThreads, descriptor => descriptor.Key == preparedChild);
    }

    private static Agent CreateAgent(ISessionStore store) => new(
        new AgentConfig
        {
            Name = "test-agent",
            SessionStore = store,
            EventComposition = CoreAgentEventComposition.Instance
        },
        baseClient: null,
        mergedOptions: null);

    private static async Task AppendSubAgentAsync(
        ISessionStore store,
        ThreadKey child,
        ThreadKey parent) =>
        _ = await store.AppendThreadEventsAsync(
            child,
            [new ThreadCreatedEvent(
                "reviewer-agent", null, null, null, null, DateTime.UtcNow,
                ThreadKind.SubAgent, ThreadVisibility.Hidden,
                parent.SessionId, parent.ThreadId, "reviewer",
                InvocationId: "create-reviewer",
                ParentToolCallId: "call-reviewer",
                ContextPolicy: "Fresh")
            {
                SessionId = child.SessionId,
                ThreadId = child.ThreadId
            }],
            new ThreadAppendCondition(ThreadJournalCursor.Start(1)));

    private static async Task<IReadOnlyList<AgentEvent>> ReadAllAsync(
        ISessionStore store,
        ThreadKey key)
    {
        var head = Assert.IsType<ThreadEventHead>(await store.GetThreadEventHeadAsync(key));
        var events = new List<AgentEvent>();
        await foreach (var batch in store.ReadThreadEventsAsync(
                           key,
                           new ThreadEventReadRequest(
                               ThreadJournalCursor.Start(head.Generation),
                               head.ThreadSequenceNumber)))
            events.AddRange(batch.Events);
        return events;
    }

    private static AgentEvent ResetPosition(AgentEvent evt) => evt with
    {
        ThreadSequenceNumber = 0
    };
}
