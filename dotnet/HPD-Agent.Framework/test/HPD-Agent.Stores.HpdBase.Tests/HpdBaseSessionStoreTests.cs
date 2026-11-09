// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: FSL-1.1-ALv2

using HPD.Agent;
using HPD.Agent.Serialization;
using HPD.Agent.Stores.HpdBase;
using HPD.Base;
using HPD.Base.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;

namespace HPD.Agent.Stores.HpdBase.Tests;

public sealed class HpdBaseSessionStoreTests
{
    [Fact]
    public async Task SessionStore_RoundTripsMetadataAndPreparedSessionCollision()
    {
        string database = TemporaryDatabase();
        try
        {
            await using ServiceProvider provider = await CreateProviderAsync(database);
            ISessionStore store = provider.GetRequiredService<ISessionStore>();
            var session = new Session("session-a");
            session.AddMetadata("project", "alpha");
            session.MiddlewareState["mode"] = "test";

            (await store.TryCreateSessionAsync(session)).Should().BeTrue();
            (await store.TryCreateSessionAsync(new Session("session-a"))).Should().BeFalse();
            (await store.ListSessionIdsAsync()).Should().Equal("session-a");

            Session loaded = (await store.LoadSessionAsync("session-a"))!;
            loaded.Metadata["project"].Should().BeOfType<JsonElement>()
                .Which.GetString().Should().Be("alpha");
            loaded.MiddlewareState["mode"].Should().Be("test");

            var prepared = new Session("prepared")
            {
                Preparation = new SessionPreparationDescriptor(
                    "operation",
                    new ThreadKey("source-session", "source-thread"),
                    "request-fingerprint"),
            };
            (await store.TryPrepareSessionAsync(prepared))
                .Should().Be(SessionPreparationResult.Created);
            (await store.TryPrepareSessionAsync(prepared))
                .Should().Be(SessionPreparationResult.ExistingOwned);
            (await store.TryPrepareSessionAsync(new Session("prepared")))
                .Should().Be(SessionPreparationResult.Conflict);

            await store.DeleteSessionAsync("session-a");
            (await store.LoadSessionAsync("session-a")).Should().BeNull();
        }
        finally
        {
            ClearDatabase(database);
        }
    }

    [Fact]
    public async Task SessionStore_AppendsReadsReplacesAndSurvivesRestart()
    {
        string database = TemporaryDatabase();
        ThreadKey thread = new("session-durable", "main");
        try
        {
            await using (ServiceProvider first = await CreateProviderAsync(database))
            {
                ISessionStore store = first.GetRequiredService<ISessionStore>();
                await store.SaveSessionAsync(new Session("session-durable"));

                ThreadEventAppendResult firstAppend = await store.AppendThreadEventsAsync(
                    thread,
                    [Created(thread)]);
                firstAppend.CurrentCursor.Should().Be(new ThreadJournalCursor(1, 1));
                (await store.GetThreadEventHeadAsync(thread))
                    .Should().BeEquivalentTo(new ThreadEventHead(1, 1, firstAppend.CommittedEvents[0].Timestamp));

                await Assert.ThrowsAsync<ThreadAppendConflictException>(async () =>
                    await store.AppendThreadEventsAsync(
                        thread,
                        [Started(thread, "message-1")],
                        new ThreadAppendCondition(ThreadJournalCursor.Start(1))));

                ThreadEventAppendResult secondAppend = await store.AppendThreadEventsAsync(
                    thread,
                    [Started(thread, "message-1")],
                    new ThreadAppendCondition(firstAppend.CurrentCursor));
                secondAppend.CurrentCursor.Should().Be(new ThreadJournalCursor(1, 2));
                (await store.GetThreadAsync(thread))!.MessageCount.Should().Be(1);
            }

            await using ServiceProvider restarted = await CreateProviderAsync(database);
            ISessionStore restartedStore = restarted.GetRequiredService<ISessionStore>();
            var events = await ReadAllAsync(
                restartedStore.ReadThreadEventsAsync(
                    thread,
                    new ThreadEventReadRequest(ThreadJournalCursor.Start(1))));
            events.Select(static value => value.ThreadSequenceNumber)
                .Should().Equal(1, 2);

            ThreadEventHead head = (await restartedStore.GetThreadEventHeadAsync(thread))!;
            ThreadJournalReplaceResult replacement =
                await restartedStore.ReplaceThreadEventsAsync(
                    thread,
                    [Created(thread), Started(thread, "replacement-message")],
                    head.Cursor);
            replacement.PreviousCursor.Should().Be(new ThreadJournalCursor(1, 2));
            replacement.CurrentCursor.Should().Be(new ThreadJournalCursor(2, 2));

            events = await ReadAllAsync(
                restartedStore.ReadThreadEventsAsync(
                    thread,
                    new ThreadEventReadRequest(ThreadJournalCursor.Start(2))));
            events.Should().HaveCount(2);
            events[1].Should().BeOfType<TextMessageStartEvent>()
                .Which.MessageId.Should().Be("replacement-message");
        }
        finally
        {
            ClearDatabase(database);
        }
    }

    [Fact]
    public async Task SessionStore_ObservesFutureEventsAndThreadDeletion()
    {
        string database = TemporaryDatabase();
        ThreadKey thread = new("session-observe", "main");
        try
        {
            await using ServiceProvider provider = await CreateProviderAsync(database);
            ISessionStore store = provider.GetRequiredService<ISessionStore>();
            await store.SaveSessionAsync(new Session("session-observe"));
            ThreadEventAppendResult initial = await store.AppendThreadEventsAsync(
                thread,
                [Created(thread)]);

            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var observed = new List<ThreadEventBatch>();
            Task reader = Task.Run(async () =>
            {
                await foreach (ThreadEventBatch batch in store.ObserveThreadEventsAsync(
                                   thread,
                                   initial.CurrentCursor,
                                   new ThreadObservationOptions(8),
                                   cancellation.Token))
                {
                    observed.Add(batch);
                    if (observed.Sum(static value => value.Events.Count) >= 1)
                        return;
                }
            });

            await Task.Delay(100);
            await store.AppendThreadEventsAsync(thread, [Started(thread, "future")]);
            await reader.WaitAsync(TimeSpan.FromSeconds(5));
            observed.Should().ContainSingle();
            observed[0].Events.Should().ContainSingle()
                .Which.Should().BeOfType<TextMessageStartEvent>();

            await store.DeleteThreadAsync(thread.SessionId, thread.ThreadId);
            (await store.GetThreadAsync(thread)).Should().BeNull();
        }
        finally
        {
            ClearDatabase(database);
        }
    }

    [Fact]
    public async Task SessionStore_StagesFinalizesAndRecoversStreamingDeltas()
    {
        string database = TemporaryDatabase();
        ThreadKey thread = new("session-deltas", "main");
        try
        {
            await using ServiceProvider provider = await CreateProviderAsync(database);
            IThreadDeltaStore store =
                (IThreadDeltaStore)provider.GetRequiredService<ISessionStore>();
            await provider.GetRequiredService<ISessionStore>()
                .AppendThreadEventsAsync(thread, [Created(thread)]);

            await store.StageThreadDeltaAsync(thread, Delta(thread, "message-1", "hello "));
            await store.StageThreadDeltaAsync(thread, Delta(thread, "message-1", "world"));
            ThreadEventAppendResult finalized =
                await store.FinalizeThreadDeltasAsync(
                    thread,
                    End(thread, "message-1"));
            finalized.CommittedEvents.Should().HaveCount(2);
            finalized.CommittedEvents[0].Should().BeOfType<TextDeltaEvent>()
                .Which.Text.Should().Be("hello world");
            finalized.CommittedEvents[1].Should().BeOfType<TextMessageEndEvent>();

            await store.StageThreadDeltaAsync(thread, Delta(thread, "message-2", "recover "));
            await store.StageThreadDeltaAsync(thread, Delta(thread, "message-2", "me"));
            await store.RecoverThreadDeltasAsync(thread);
            ThreadEventHead afterRecovery =
                (await ((ISessionStore)store).GetThreadEventHeadAsync(thread))!;
            afterRecovery.ThreadSequenceNumber.Should().Be(5);

            await store.RecoverThreadDeltasAsync(thread);
            (await ((ISessionStore)store).GetThreadEventHeadAsync(thread))!
                .ThreadSequenceNumber.Should().Be(5);
        }
        finally
        {
            ClearDatabase(database);
        }
    }

    private static ThreadCreatedEvent Created(ThreadKey thread) => new(
        "test-agent",
        "Main",
        "Session store test",
        ["test"],
        new Dictionary<string, object> { ["owner"] = "tests" },
        DateTime.UtcNow)
    {
        EventId = "created-" + thread.ThreadId,
        SessionId = thread.SessionId,
        ThreadId = thread.ThreadId,
        Timestamp = DateTimeOffset.UtcNow,
    };

    private static TextMessageStartEvent Started(ThreadKey thread, string messageId) => new(
        messageId,
        "assistant")
    {
        EventId = "started-" + messageId,
        SessionId = thread.SessionId,
        ThreadId = thread.ThreadId,
        Timestamp = DateTimeOffset.UtcNow,
    };

    private static TextDeltaEvent Delta(
        ThreadKey thread,
        string messageId,
        string text) => new(text, messageId)
    {
        EventId = "delta-" + messageId + "-" + text.GetHashCode(),
        SessionId = thread.SessionId,
        ThreadId = thread.ThreadId,
        Timestamp = DateTimeOffset.UtcNow,
    };

    private static TextMessageEndEvent End(
        ThreadKey thread,
        string messageId) => new(messageId)
    {
        EventId = "end-" + messageId,
        SessionId = thread.SessionId,
        ThreadId = thread.ThreadId,
        Timestamp = DateTimeOffset.UtcNow,
    };

    private static async Task<List<AgentEvent>> ReadAllAsync(
        IAsyncEnumerable<ThreadEventBatch> batches)
    {
        var events = new List<AgentEvent>();
        await foreach (ThreadEventBatch batch in batches)
            events.AddRange(batch.Events);
        return events;
    }

    private static AgentEventCodec CreateCodec()
    {
        if (!AgentEventCompositionHost.TryComposeRegisteredModules(
                out AgentEventComposition composition))
        {
            throw new InvalidOperationException(
                "The HPD Agent generated event modules were not registered.");
        }
        return composition.Codec;
    }

    private static async Task<ServiceProvider> CreateProviderAsync(string database)
    {
        var services = new ServiceCollection().AddLogging();
        services.AddHPDBase(builder => builder
            .AddHpdAgentStores()
            .ConfigureSchema(options =>
                options.PlanProtectionKey = Enumerable.Repeat((byte)0x73, 32).ToArray())
            .AddPolicyAuthority(
                new BasePolicyAuthorityDefinition
                {
                    Id = "hpd.agent.session.stores.tests.policy",
                    Version = 1,
                    OwningModuleId = "hpd.agent.stores",
                    EvaluatorContractId = "hpd.agent.session.stores.tests.policy-evaluator",
                    EvaluatorContractVersion = 1,
                    CompositionOrder = 0,
                },
                new AllowPolicyEvaluator())
            .UseStore(SqliteStore.Configure(options =>
            {
                options.StoreId = "sqlite";
                options.DataSource = database;
            })));
        services.AddHpdBaseAgentStore();
        services.AddHpdBaseSessionStore(CreateCodec());

        ServiceProvider provider = services.BuildServiceProvider();
        IBaseSchemaManager schemas = provider.GetRequiredService<IBaseSchemaManager>();
        OperationResult<BaseSchemaPlan> planned = await schemas.PlanAsync(
            new BaseSchemaPlanRequest { StoreId = "sqlite" });
        planned.IsSuccess().Should().BeTrue(planned.Error?.Code + ":" + planned.Error?.Message);
        OperationResult<BaseSchemaApplyResult> applied = await schemas.ApplyAsync(
            new BaseSchemaApplyRequest
            {
                ProtectedArtifact = planned.Value!.ProtectedArtifact,
            });
        applied.IsSuccess().Should().BeTrue(applied.Error?.Code + ":" + applied.Error?.Message);
        OperationResult<BaseApplicationReadiness> initialized =
            await provider.GetRequiredService<IHPDBaseApplication>().InitializeAsync();
        initialized.IsSuccess().Should().BeTrue(
            initialized.Error?.Code + ":" + initialized.Error?.Message);
        return provider;
    }

    private static string TemporaryDatabase() =>
        Path.Combine(
            Path.GetTempPath(),
            "hpd-agent-base-session-store-" + Guid.NewGuid().ToString("N") + ".db");

    private static void ClearDatabase(string database)
    {
        SqliteConnection.ClearAllPools();
        foreach (string candidate in new[]
                 {
                     database,
                     database + "-wal",
                     database + "-shm",
                 })
        {
            if (File.Exists(candidate))
                File.Delete(candidate);
        }
    }

    private sealed class AllowPolicyEvaluator : IPolicyEvaluator
    {
        public ValueTask<PolicyDecision> EvaluateAsync(
            PolicyEvaluationRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(new PolicyDecision
            {
                Effect = PolicyEffect.Allow,
                Outcome = PolicyOutcome.Allowed,
            });
        }
    }
}
