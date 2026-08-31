using System.Text.Json;
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
            Availability = SubAgentChildAvailability.Available,
            ChildThread = new ThreadKey("session", "old-child"),
            CreationContext = SubAgentCreationContext.Fresh,
            CreationInvocationId = "old-invocation",
            ParentToolCallId = "old-call",
            CreatedAt = DateTimeOffset.UtcNow
        });

        var reservation = await new JournalSubAgentCreationStore(store).TryReserveSubAgentCreationAsync(
            new SubAgentCreationKey(parent, "new-call", CapabilityId.Create("test:new-role")),
            new SubAgentCreationRequest
            {
                RoleName = "reviewer",
                ChildAgentId = "reviewer-agent",
                Context = SubAgentCreationContext.Fresh,
                InputFingerprint = "ABC"
            });

        Assert.Equal("reviewer-130", reservation.Record.LocalId.Value);
    }

    private static async Task CreateThreadAsync(InMemorySessionStore store, ThreadKey key) =>
        _ = await store.AppendThreadEventsAsync(
            key,
            [new ThreadCreatedEvent("agent", null, null, null, null, DateTime.UtcNow)
            {
                SessionId = key.SessionId,
                ThreadId = key.ThreadId
            }],
            new ThreadAppendCondition(ThreadJournalCursor.Start(1)));
}
