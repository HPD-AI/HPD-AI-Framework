using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using HPD.Agent;
using HPD.Agent.Serialization;

namespace HPD.Agent.ActionInvocation.Tests;

public sealed class SubAgentDurabilityTests
{
    [Fact]
    public async Task CompactionSeedPreservesTerminalCreationReplayReceipt()
    {
        var store = new InMemorySessionStore(CoreAgentEventComposition.Instance.Codec);
        var parent = new ThreadKey("session", "parent");
        await CreateThreadAsync(store, parent);
        var creations = new JournalSubAgentCreationStore(store);
        var key = new SubAgentCreationKey(parent, "call-1", CapabilityId.Create("test:role"));
        var reserved = await creations.TryReserveSubAgentCreationAsync(key, new SubAgentCreationRequest
        {
            RoleName = "reviewer",
            ChildAgentId = "reviewer-agent",
            Context = SubAgentCreationContext.Fresh,
            ExecutionPolicy = SubAgentTestPolicies.Default,
            InputFingerprint = "ABC"
        });
        var terminal = reserved.Record with
        {
            Phase = SubAgentCreationPhase.Terminal,
            Revision = reserved.Record.Revision + 1,
            TerminalStatus = SubAgentOperationStatus.Completed,
            TerminalOutput = "done"
        };
        await creations.WriteSubAgentCreationAsync(
            terminal, new SubAgentCreationWriteCondition(reserved.Record.Revision));

        var seeds = await CompositeThreadJournalRebaseSeedProvider.Create(store)
            .CreateSeedEventsAsync(parent);

        var seed = Assert.Single(seeds.OfType<SubAgentRegistrySeedEvent>());
        var replay = Assert.Single(seed.PendingCreations);
        Assert.Equal(SubAgentCreationPhase.Terminal, replay.Phase);
        Assert.Equal("done", replay.TerminalOutput);
    }

    [Fact]
    public void GeneratedRoleBinderCreatesTypedProjectionAndRejectsUnknownProperties()
    {
        using var valid = JsonDocument.Parse("""{"input":"inspect","context":"fresh"}""");
        var result = SubAgentGeneratedBranchBinder.Bind(valid.RootElement, allowContext: true);
        var bound = Assert.IsType<BoundSubAgentStartAction>(result.Value);
        Assert.Equal("inspect", bound.Input);
        Assert.Equal("fresh", bound.Context);

        using var invalid = JsonDocument.Parse("""{"input":"inspect","extra":true}""");
        Assert.ThrowsAny<Exception>(() => SubAgentGeneratedBranchBinder.Bind(invalid.RootElement, allowContext: false));
    }

    [Fact]
    public void AotContextsContainNewSubAgentAndForkResults()
    {
        Assert.NotNull(HPDJsonContext.Default.GetTypeInfo(typeof(SubAgentOperationResult)));
        Assert.NotNull(HPDJsonContext.Default.GetTypeInfo(typeof(ThreadForkResult)));
        Assert.NotNull(AgentEventJsonContext.Default.GetTypeInfo(typeof(ThreadForkOperationRecord)));
    }

    [Fact]
    public async Task PreparedSessionCreationIsConditionalAndIdempotent()
    {
        var store = new InMemorySessionStore(CoreAgentEventComposition.Instance.Codec);
        var source = new ThreadKey("source-session", "source-thread");
        var preparation = new SessionPreparationDescriptor("operation", source, "fingerprint");
        var session = new Session("isolated") { Preparation = preparation };

        Assert.Equal(SessionPreparationResult.Created, await store.TryPrepareSessionAsync(session));
        Assert.Equal(SessionPreparationResult.ExistingOwned,
            await store.TryPrepareSessionAsync(new Session("isolated") { Preparation = preparation }));
        Assert.Equal(SessionPreparationResult.Conflict,
            await store.TryPrepareSessionAsync(new Session("isolated")
            {
                Preparation = preparation with { OperationId = "other" }
            }));
        Assert.Null(await store.LoadSessionAsync("isolated"));
    }

    [Fact]
    public async Task ForkOperationRoundTripsAuthoritativeFingerprintAndOutcomes()
    {
        var store = new InMemorySessionStore(CoreAgentEventComposition.Instance.Codec);
        var source = new ThreadKey("session", "source");
        await CreateThreadAsync(store, source);
        var operationStore = new JournalThreadForkOperationStore(store, source);
        var operation = new ThreadForkOperationRecord
        {
            OperationId = "fork-1",
            Source = source,
            Target = new ThreadKey("session", "target"),
            SourceBoundary = new ThreadJournalCursor(1, 1),
            RequestFingerprint = "ABC",
            SubAgentPolicy = SubAgentForkPolicy.Detach,
            Status = ThreadForkOperationStatus.Prepared,
            Revision = 1,
            PreparedChildren = [],
            ChildOutcomes = []
        };
        await operationStore.WriteThreadForkOperationAsync(operation, new ThreadForkOperationWriteCondition(0));

        var replay = await operationStore.GetThreadForkOperationAsync("fork-1");
        Assert.Equal("ABC", replay!.RequestFingerprint);
        Assert.Equal(operation.Target, replay.Target);
    }

    [Fact]
    public async Task ForkOperationRejectsTerminalAndOutOfOrderTransitions()
    {
        var store = new InMemorySessionStore(CoreAgentEventComposition.Instance.Codec);
        var source = new ThreadKey("session", "source");
        await CreateThreadAsync(store, source);
        var operationStore = new JournalThreadForkOperationStore(store, source);
        var prepared = CreateForkOperation(source, "fork-transitions");
        await operationStore.WriteThreadForkOperationAsync(prepared, new ThreadForkOperationWriteCondition(0));

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await operationStore.WriteThreadForkOperationAsync(
                prepared with { Status = ThreadForkOperationStatus.Committed, Revision = 2 },
                new ThreadForkOperationWriteCondition(1)));
        var aborted = prepared with { Status = ThreadForkOperationStatus.Aborted, Revision = 2 };
        await operationStore.WriteThreadForkOperationAsync(aborted, new ThreadForkOperationWriteCondition(1));
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await operationStore.WriteThreadForkOperationAsync(
                aborted with { Status = ThreadForkOperationStatus.Committed, Revision = 3 },
                new ThreadForkOperationWriteCondition(2)));
    }

    [Fact]
    public async Task CreationOrdinalContinuesFromDurableRegistryAfterReceiptRetention()
    {
        var store = new InMemorySessionStore(CoreAgentEventComposition.Instance.Codec);
        var parent = new ThreadKey("session", "parent");
        await CreateThreadAsync(store, parent);
        await new SubAgentChildRegistry(store).RegisterAsync(parent, new SubAgentChildReference
        {
            LocalId = new SubAgentLocalId("reviewer-129"),
            RoleName = "reviewer",
            CapabilityId = CapabilityId.Create("test:old-role"),
            ChildAgentId = "reviewer-agent",
            ChildThread = new ThreadKey("session", "old-child"),
            CreationContext = SubAgentCreationContext.Fresh,
            CreationInvocationId = "old-invocation",
            ParentToolCallId = "old-call",
            ExecutionPolicy = SubAgentTestPolicies.Default,
            CreatedAt = DateTimeOffset.UtcNow
        });

        var reservation = await new JournalSubAgentCreationStore(store).TryReserveSubAgentCreationAsync(
            new SubAgentCreationKey(parent, "new-call", CapabilityId.Create("test:new-role")),
            new SubAgentCreationRequest
            {
                RoleName = "reviewer",
                ChildAgentId = "reviewer-agent",
                Context = SubAgentCreationContext.Fresh,
                ExecutionPolicy = SubAgentTestPolicies.Default,
                InputFingerprint = "ABC"
            });

        Assert.Equal("reviewer-130", reservation.Record.LocalId.Value);
    }

    [Fact]
    public async Task CompactionSeedPreservesContinuationAdmissionAndTerminalReceipt()
    {
        var store = new InMemorySessionStore(CoreAgentEventComposition.Instance.Codec);
        var child = new ThreadKey("session", "child");
        await CreateThreadAsync(store, child);
        var head = await store.GetThreadEventHeadAsync(child);
        await store.AppendThreadEventsAsync(
            child,
            [
                new ThreadExecutionStartedEvent("continue-abc", "child-agent", DateTimeOffset.UtcNow),
                new SubAgentExecutionControllerEvent("continue-abc", new("session", "parent")),
                new SubAgentResultSubmittedEvent("continue-abc", "complete", new("session", "parent"), "scoped output"),
                new ThreadExecutionFinishedEvent(
                    "continue-abc", "child-agent", ThreadExecutionOutcome.Succeeded, DateTimeOffset.UtcNow)
            ],
            new ThreadAppendCondition(head!.Cursor));

        var seed = await new AgentCommunicationRebaseSeedProvider(store).CreateSeedEventsAsync(child);

        Assert.Collection(
            seed,
            value => Assert.Equal("continue-abc", Assert.IsType<ThreadExecutionStartedEvent>(value).ThreadExecutionId),
            value => Assert.IsType<SubAgentExecutionControllerEvent>(value),
            value => Assert.Equal("scoped output", Assert.IsType<SubAgentResultSubmittedEvent>(value).Report),
            value => Assert.Equal("continue-abc", Assert.IsType<ThreadExecutionFinishedEvent>(value).ThreadExecutionId));
    }

    [Fact]
    public async Task CommunicationRebasePreservesActiveQuestionOwnershipAndExecutionOrder()
    {
        var store = new InMemorySessionStore(CoreAgentEventComposition.Instance.Codec);
        var child = new ThreadKey("session", "child");
        var parent = new ThreadKey("session", "parent");
        await CreateThreadAsync(store, child);
        await store.AppendThreadEventsAsync(child,
        [
            new ThreadExecutionStartedEvent("z-earlier", "child-agent", DateTimeOffset.UtcNow),
            new SubAgentExecutionControllerEvent("z-earlier", parent),
            new SubAgentResultSubmittedEvent("z-earlier", "done", parent, "Previous report"),
            new ThreadExecutionFinishedEvent("z-earlier", "child-agent", ThreadExecutionOutcome.Succeeded, DateTimeOffset.UtcNow),
            new ThreadExecutionStartedEvent("a-current", "child-agent", DateTimeOffset.UtcNow),
            new SubAgentExecutionControllerEvent("a-current", parent) { OperationId = "operation" },
            new ParentQuestionRequestEvent("pending", "Parent", parent, [new("q", "Which target?")]) { ThreadExecutionId = "a-current" }
        ]);
        var seed = await CompositeThreadJournalRebaseSeedProvider.Create(store).CreateSeedEventsAsync(child);
        var current = SubAgentActivityReader.Project(seed, "a-current");
        Assert.Equal("a-current", current.ExecutionId);
        Assert.Equal("waiting for parent", current.Status);
        Assert.Equal(1, current.ParentQuestionCount);
        Assert.Null(current.Report);
        Assert.Equal("operation", seed.OfType<SubAgentExecutionControllerEvent>().Last().OperationId);
        Assert.Equal("pending", Assert.IsType<ParentQuestionRequestEvent>(Assert.Single(AgentRequestProjector.ProjectPending(seed, "a-current"))).RequestId);
    }

    [Fact]
    public async Task SharedControlGrantIsChildKeyedAndRequiresCommittedForkAuthority()
    {
        var store = new InMemorySessionStore(CoreAgentEventComposition.Instance.Codec);
        var source = new ThreadKey("session", "source");
        var child = new ThreadKey("session", "child");
        var controller = new ThreadKey("session", "fork");
        await CreateThreadAsync(store, source);
        await CreateThreadAsync(store, child);
        var operationStore = new JournalThreadForkOperationStore(store, source);
        var operation = CreateForkOperation(source, "fork-share") with
        {
            Target = controller,
            SubAgentPolicy = SubAgentForkPolicy.Share,
            ChildOutcomes = [new SubAgentForkChildOutcome(
                "reviewer-1",
                SubAgentForkPolicy.Share,
                child,
                child,
                SubAgentChildAvailability.Available,
                OwningParent: source,
                Controller: controller)]
        };
        await operationStore.WriteThreadForkOperationAsync(operation, new ThreadForkOperationWriteCondition(0));
        foreach (var status in new[]
                 {
                     ThreadForkOperationStatus.ChildrenPreparing,
                     ThreadForkOperationStatus.ParentPreparing,
                     ThreadForkOperationStatus.ReadyToCommit,
                     ThreadForkOperationStatus.Committed
                 })
        {
            var next = operation with { Status = status, Revision = operation.Revision + 1 };
            await operationStore.WriteThreadForkOperationAsync(
                next, new ThreadForkOperationWriteCondition(operation.Revision));
            operation = next;
        }

        await SubAgentControllerAuthority.GrantAsync(
            store, child, controller, new SubAgentLocalId("reviewer-1"), "fork-share", source);

        Assert.True(await SubAgentControllerAuthority.IsGrantedAsync(
            store, child, controller, new SubAgentLocalId("reviewer-1")));
        var childEvents = new List<AgentEvent>();
        await foreach (var batch in store.ReadThreadEventsAsync(
                           child,
                           new ThreadEventReadRequest(ThreadJournalCursor.Start(1))))
            childEvents.AddRange(batch.Events);
        Assert.DoesNotContain(childEvents, static evt => evt is SubAgentChildControllerAuthorityEvent);
        var authorityRoute = AuthorityRoute(child);
        Assert.NotNull(await store.GetThreadEventHeadAsync(authorityRoute));
        Assert.Null(await store.GetThreadAsync(authorityRoute));
        var visibleThreads = new List<ThreadDescriptor>();
        await foreach (var descriptor in store.ListThreadsAsync(
                           child.SessionId,
                           new ThreadListRequest { IncludeHidden = true }))
            visibleThreads.Add(descriptor);
        Assert.DoesNotContain(visibleThreads, descriptor => descriptor.Key == authorityRoute);
        Assert.False(await SubAgentControllerAuthority.IsGrantedAsync(
            store, child, new ThreadKey("session", "other"), new SubAgentLocalId("reviewer-1")));
        await SubAgentControllerAuthority.RevokeAsync(
            store, child, controller, new SubAgentLocalId("reviewer-1"), "fork-share", source);
        Assert.False(await SubAgentControllerAuthority.IsGrantedAsync(
            store, child, controller, new SubAgentLocalId("reviewer-1")));
    }

    [Fact]
    public async Task SharedControlAuthorityRouteCollisionFailsClosed()
    {
        var store = new InMemorySessionStore(CoreAgentEventComposition.Instance.Codec);
        var child = new ThreadKey("session", "child");
        await CreateThreadAsync(store, child);
        var authority = AuthorityRoute(child);
        await CreateThreadAsync(store, authority);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await SubAgentControllerAuthority.GrantAsync(
                store,
                child,
                new ThreadKey("session", "controller"),
                new SubAgentLocalId("reviewer-1"),
                "fork-share",
                new ThreadKey("session", "source")));

        Assert.Equal("subagent_controller_authority_route_collision", exception.Message);
    }

    [Fact]
    public async Task ForkPoliciesDetachOrCopyTheSameParentLocalChildIdentity()
    {
        var store = new InMemorySessionStore(CoreAgentEventComposition.Instance.Codec);
        await using var agent = new Agent(
            new AgentConfig
            {
                Name = "test-agent",
                SessionStore = store,
                EventComposition = CoreAgentEventComposition.Instance
            },
            baseClient: null,
            mergedOptions: null);
        var session = new Session("subagent-fork-session");
        await store.SaveSessionAsync(session);
        var source = session.CreateThread("test-agent", "main");
        await store.SaveInitialThreadAsync(session.Id, source);
        source.Session = session;
        var sourceKey = new ThreadKey(session.Id, source.Id);
        var childKey = new ThreadKey(session.Id, "main/subagent/reviewer-1");
        await store.AppendThreadEventsAsync(
            childKey,
            [new ThreadCreatedEvent(
                "reviewer-agent", null, null, null, null, DateTime.UtcNow,
                ThreadKind.SubAgent, ThreadVisibility.Hidden,
                sourceKey.SessionId, sourceKey.ThreadId, "reviewer",
                InvocationId: "create-reviewer", ParentToolCallId: "call-reviewer", ContextPolicy: "Fresh")
            {
                SessionId = childKey.SessionId,
                ThreadId = childKey.ThreadId
            }],
            new ThreadAppendCondition(ThreadJournalCursor.Start(1)));
        var original = new SubAgentChildReference
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
        await new SubAgentChildRegistry(store).RegisterAsync(sourceKey, original);
        var forkPoint = new Microsoft.Extensions.AI.ChatMessage(Microsoft.Extensions.AI.ChatRole.User, "fork here")
        {
            MessageId = "fork-point"
        };
        source.AddMessage(forkPoint);
        await store.AppendThreadEventsAsync(
            sourceKey,
            [new ContentAddedEvent(
                forkPoint.MessageId!,
                "user",
                new Microsoft.Extensions.AI.TextContent("fork here"))
            {
                SessionId = sourceKey.SessionId,
                ThreadId = sourceKey.ThreadId
            }]);

        var detached = await agent.ForkThreadAsync(source, "detached", fromMessageId: forkPoint.MessageId,
            new ThreadForkOptions
            {
                OperationId = "detach-operation",
                SubAgents = new SubAgentForkOptions { Policy = SubAgentForkPolicy.Detach }
            });
        var detachedChild = (await new SubAgentChildRegistry(store)
            .ProjectAsync(new ThreadKey(detached.SessionId, detached.Id))).Entries[original.LocalId];
        Assert.Equal(SubAgentChildAvailability.Detached, detachedChild.Availability);
        Assert.IsType<SubAgentChildTombstone>(detachedChild);

        var copied = await agent.ForkThreadAsync(source, "copied", fromMessageId: forkPoint.MessageId,
            new ThreadForkOptions
            {
                OperationId = "copy-operation",
                SubAgents = new SubAgentForkOptions
                {
                    Policy = SubAgentForkPolicy.ForkDirectChildren,
                    DescendantPolicy = SubAgentForkPolicy.Detach
                }
            });
        var copiedChild = (await new SubAgentChildRegistry(store)
            .ProjectAsync(new ThreadKey(copied.SessionId, copied.Id))).AvailableChildren[original.LocalId];
        Assert.NotEqual(childKey, copiedChild.ChildThread);
        var copiedDescriptor = await store.GetThreadAsync(copiedChild.ChildThread);
        Assert.NotNull(copiedDescriptor);
        Assert.Equal(ThreadKind.SubAgent, copiedDescriptor.Kind);
        Assert.Equal(ThreadVisibility.Hidden, copiedDescriptor.Visibility);
        Assert.Equal("reviewer-agent", copiedDescriptor.DefaultAgent.AgentId);
        Assert.Equal(copied.Id, copiedDescriptor.RuntimeChild!.ParentThreadId);
        Assert.Equal("reviewer", copiedDescriptor.RuntimeChild.SubAgentName);
        Assert.NotNull(await store.GetThreadAsync(childKey));
    }

    [Fact]
    public async Task SharedControlRevocationSurvivesAuthorityJournalRebase()
    {
        var store = new InMemorySessionStore(CoreAgentEventComposition.Instance.Codec);
        var source = new ThreadKey("session", "source");
        var child = new ThreadKey("session", "child");
        var controller = new ThreadKey("session", "fork");
        var localId = new SubAgentLocalId("reviewer-1");
        await CreateThreadAsync(store, source);
        await CreateThreadAsync(store, child);
        await WriteCommittedShareOperationAsync(
            store, source, child, controller, localId, "fork-share");

        await SubAgentControllerAuthority.GrantAsync(
            store, child, controller, localId, "fork-share", source);
        await SubAgentControllerAuthority.RevokeAsync(
            store, child, controller, localId, "fork-share", source);

        var authorityRoute = AuthorityRoute(child);
        var head = Assert.IsType<ThreadEventHead>(await store.GetThreadEventHeadAsync(authorityRoute));
        ThreadCreatedEvent? created = null;
        await foreach (var batch in store.ReadThreadEventsAsync(
                           authorityRoute,
                           new ThreadEventReadRequest(ThreadJournalCursor.Start(head.Generation), head.ThreadSequenceNumber)))
            created ??= batch.Events.OfType<ThreadCreatedEvent>().FirstOrDefault();
        Assert.NotNull(created);
        var seeds = await new SubAgentControllerAuthorityRebaseSeedProvider(store)
            .CreateSeedEventsAsync(authorityRoute);
        var revoked = Assert.Single(seeds.OfType<SubAgentChildControllerAuthorityEvent>());
        Assert.True(revoked.Revoked);

        await store.ReplaceThreadEventsAsync(
            authorityRoute,
            [created! with { ThreadSequenceNumber = 0 }, .. seeds],
            head.Cursor);

        Assert.False(await SubAgentControllerAuthority.IsGrantedAsync(
            store, child, controller, localId));
        Assert.Null(await store.GetThreadAsync(authorityRoute));
        var listed = new List<ThreadDescriptor>();
        await foreach (var descriptor in store.ListThreadsAsync(
                           child.SessionId,
                           new ThreadListRequest { IncludeHidden = true }))
            listed.Add(descriptor);
        Assert.DoesNotContain(listed, descriptor => descriptor.Key == authorityRoute);
    }

    [Fact]
    public async Task SharedControlAuthoritySurvivesFileStoreRestartAndRemainsInternal()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"hpd-subagent-authority-{Guid.NewGuid():N}");
        var source = new ThreadKey("session", "source");
        var child = new ThreadKey("session", "child");
        var controller = new ThreadKey("session", "fork");
        var localId = new SubAgentLocalId("reviewer-1");
        var authorityRoute = AuthorityRoute(child);
        try
        {
            var first = new FileSessionStore(directory, CoreAgentEventComposition.Instance.Codec);
            await CreateThreadAsync(first, source);
            await CreateThreadAsync(first, child);
            await WriteCommittedShareOperationAsync(
                first, source, child, controller, localId, "fork-share");
            await SubAgentControllerAuthority.GrantAsync(
                first, child, controller, localId, "fork-share", source);

            var reopened = new FileSessionStore(directory, CoreAgentEventComposition.Instance.Codec);
            var reopenedOperation = await new JournalThreadForkOperationStore(reopened, source)
                .GetThreadForkOperationAsync("fork-share");
            Assert.NotNull(reopenedOperation);
            Assert.Equal(ThreadForkOperationStatus.Committed, reopenedOperation.Status);
            Assert.Contains(reopenedOperation.ChildOutcomes, outcome =>
                outcome.LocalId == localId.Value && outcome.Target == child && outcome.Controller == controller);
            var authorityHead = await reopened.GetThreadEventHeadAsync(authorityRoute);
            Assert.NotNull(authorityHead);
            var authorityEvents = new List<AgentEvent>();
            await foreach (var batch in reopened.ReadThreadEventsAsync(
                               authorityRoute,
                               new ThreadEventReadRequest(
                                   ThreadJournalCursor.Start(authorityHead.Generation),
                                   authorityHead.ThreadSequenceNumber)))
                authorityEvents.AddRange(batch.Events);
            Assert.Contains(authorityEvents, evt => evt is SubAgentChildControllerAuthorityEvent authority &&
                !authority.Revoked && authority.Controller == controller && authority.LocalId == localId);
            Assert.True(await SubAgentControllerAuthority.IsGrantedAsync(
                reopened, child, controller, localId));
            Assert.Null(await reopened.GetThreadAsync(authorityRoute));
            var listed = new List<ThreadDescriptor>();
            await foreach (var descriptor in reopened.ListThreadsAsync(
                               child.SessionId,
                               new ThreadListRequest { IncludeHidden = true }))
                listed.Add(descriptor);
            Assert.DoesNotContain(listed, descriptor => descriptor.Key == authorityRoute);

            await SubAgentControllerAuthority.RevokeAsync(
                reopened, child, controller, localId, "fork-share", source);
            var reopenedAfterRevocation = new FileSessionStore(
                directory, CoreAgentEventComposition.Instance.Codec);
            Assert.False(await SubAgentControllerAuthority.IsGrantedAsync(
                reopenedAfterRevocation, child, controller, localId));
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
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

    private static ThreadForkOperationRecord CreateForkOperation(ThreadKey source, string operationId) => new()
    {
        OperationId = operationId,
        Source = source,
        Target = new ThreadKey(source.SessionId, $"{source.ThreadId}-target"),
        SourceBoundary = new ThreadJournalCursor(1, 1),
        RequestFingerprint = "ABC",
        SubAgentPolicy = SubAgentForkPolicy.Detach,
        Status = ThreadForkOperationStatus.Prepared,
        Revision = 1,
        PreparedChildren = [],
        ChildOutcomes = []
    };

    private static async Task WriteCommittedShareOperationAsync(
        ISessionStore store,
        ThreadKey source,
        ThreadKey child,
        ThreadKey controller,
        SubAgentLocalId localId,
        string operationId)
    {
        var operationStore = new JournalThreadForkOperationStore(store, source);
        var operation = CreateForkOperation(source, operationId) with
        {
            Target = controller,
            SubAgentPolicy = SubAgentForkPolicy.Share,
            ChildOutcomes = [new SubAgentForkChildOutcome(
                localId.Value,
                SubAgentForkPolicy.Share,
                child,
                child,
                SubAgentChildAvailability.Available,
                OwningParent: source,
                Controller: controller)]
        };
        await operationStore.WriteThreadForkOperationAsync(
            operation, new ThreadForkOperationWriteCondition(0));
        foreach (var status in new[]
                 {
                     ThreadForkOperationStatus.ChildrenPreparing,
                     ThreadForkOperationStatus.ParentPreparing,
                     ThreadForkOperationStatus.ReadyToCommit,
                     ThreadForkOperationStatus.Committed
                 })
        {
            var next = operation with { Status = status, Revision = operation.Revision + 1 };
            await operationStore.WriteThreadForkOperationAsync(
                next, new ThreadForkOperationWriteCondition(operation.Revision));
            operation = next;
        }
    }

    private static ThreadKey AuthorityRoute(ThreadKey child)
    {
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{child.SessionId}\u001f{child.ThreadId}"))).ToLowerInvariant();
        return new ThreadKey(child.SessionId, $"__hpd/subagent-controller-authority/{digest}");
    }
}
