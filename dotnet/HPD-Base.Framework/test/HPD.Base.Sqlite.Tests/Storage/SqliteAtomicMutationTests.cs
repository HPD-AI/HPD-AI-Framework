using FluentAssertions;
using HPD.Base;
using HPD.Base.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace HPD.Base.Sqlite.Tests.Storage;

public sealed class SqliteAtomicMutationTests
{
    [Fact]
    public async Task SingleAndAtomicExecutorsUseTransactionBoundSessions()
    {
        await using var store = Store();
        var first = Collection("first");
        var second = Collection("second");
        var mutations = new List<BaseRecordMutationFact>();
        var processor = new CallbackProcessor(async (session, cancellationToken) =>
        {
            var created = await session.CreateAsync(
                first,
                new RecordCreateRequest
                {
                    RequestedId = new RecordId("one"),
                    Payload = Payload("created")
                },
                MutationContext(BaseRecordMutationKind.Create, "evt-create", first.Id),
                cancellationToken);
            created.Status.Should().Be(OperationStatus.Created);
            mutations.Add(created.Value!.Mutation);

            var transactionRead = await session.GetAsync(
                first,
                new RecordId("one"),
                Operation(BaseOperationKind.Get, first.Id),
                cancellationToken);
            transactionRead.Status.Should().Be(OperationStatus.Ok);

            var patched = await session.PatchAsync(
                first,
                new RecordId("one"),
                new RecordPatchRequest
                {
                    Patch = Payload("patched"),
                    ExpectedRevision = transactionRead.Value!.Metadata.Revision
                },
                MutationContext(BaseRecordMutationKind.Patch, "evt-patch", first.Id),
                cancellationToken);
            patched.Status.Should().Be(OperationStatus.Updated);
            mutations.Add(patched.Value!.Mutation);

            var crossCollection = await session.CreateAsync(
                second,
                new RecordCreateRequest
                {
                    RequestedId = new RecordId("two"),
                    Payload = Payload("other")
                },
                MutationContext(BaseRecordMutationKind.Create, "evt-other", second.Id),
                cancellationToken);
            crossCollection.Status.Should().Be(OperationStatus.Created);
            mutations.Add(crossCollection.Value!.Mutation);

            return Ready(mutations);
        });

        var execution = await store.ExecuteAtomicAsync(processor, ExecutionRequest());

        execution.Outcome.Should().Be(RecordMutationExecutionOutcome.Committed);
        execution.Processing!.Mutations.Select(mutation => mutation.Event.EventId)
            .Should().Equal("evt-create", "evt-patch", "evt-other");
        (await store.GetAsync(first, new RecordId("one"), Operation(BaseOperationKind.Get, first.Id)))
            .Value!.Payload.Fields!["value"].GetString().Should().Be("patched");
        (await store.GetAsync(second, new RecordId("two"), Operation(BaseOperationKind.Get, second.Id)))
            .Status.Should().Be(OperationStatus.Ok);

        var journal = await store.ReadMutationJournalAsync(
            new HPD.Base.BaseMutationJournalReadRequest { Limit = 10 });
        journal.Entries.Select(entry => entry.EventId)
            .Should().Equal("evt-create", "evt-patch", "evt-other");
    }

    [Fact]
    public async Task ConfirmedRollbackPublishesNeitherRecordsNorJournalRows()
    {
        await using var store = Store();
        var collection = Collection("items");
        var processor = new CallbackProcessor(async (session, cancellationToken) =>
        {
            var created = await session.CreateAsync(
                collection,
                new RecordCreateRequest
                {
                    RequestedId = new RecordId("provisional"),
                    Payload = Payload("provisional")
                },
                MutationContext(BaseRecordMutationKind.Create, "evt-provisional", collection.Id),
                cancellationToken);
            created.Status.Should().Be(OperationStatus.Created);
            return new AtomicMutationProcessingResult(
                AtomicMutationProcessingOutcome.Failed,
                [created.Value!.Mutation],
                Error("base.runtime.batch.itemInvalid"));
        });

        var execution = await store.ExecuteAtomicAsync(processor, ExecutionRequest());

        execution.Outcome.Should().Be(RecordMutationExecutionOutcome.RollbackConfirmed);
        (await store.GetAsync(
            collection,
            new RecordId("provisional"),
            Operation(BaseOperationKind.Get, collection.Id))).Status.Should().Be(OperationStatus.NotFound);
        (await store.ReadMutationJournalAsync(
            new HPD.Base.BaseMutationJournalReadRequest { Limit = 10 }))
            .Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task TransactionTimeoutRollsBackPriorWritesAndClosesRetainedSession()
    {
        await using var store = Store();
        var collection = Collection("items");
        IAtomicRecordSession? retained = null;
        var processor = new CallbackProcessor(async (session, cancellationToken) =>
        {
            retained = session;
            var created = await session.CreateAsync(
                collection,
                new RecordCreateRequest
                {
                    RequestedId = new RecordId("timed-out"),
                    Payload = Payload("timed-out")
                },
                MutationContext(BaseRecordMutationKind.Create, "evt-timeout", collection.Id),
                cancellationToken);
            created.Status.Should().Be(OperationStatus.Created);
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return Ready([created.Value!.Mutation]);
        });

        var execution = await store.ExecuteAtomicAsync(
            processor,
            ExecutionRequest(transactionTimeout: TimeSpan.FromSeconds(1)));

        execution.Outcome.Should().Be(
            RecordMutationExecutionOutcome.CancelledRollbackConfirmed,
            "the bounded processor lifetime should classify as cancellation; code was {0}",
            execution.Processing?.Error?.Message
                ?? execution.Error?.Message
                ?? execution.Processing?.Error?.Code
                ?? execution.Error?.Code
                ?? "<none>");
        execution.Processing!.Error!.Code.Should().Be(BaseMutationErrorCodes.TransactionTimeout);
        (await store.GetAsync(
            collection,
            new RecordId("timed-out"),
            Operation(BaseOperationKind.Get, collection.Id))).Status.Should().Be(OperationStatus.NotFound);
        (await store.ReadMutationJournalAsync(
            new HPD.Base.BaseMutationJournalReadRequest { Limit = 10 }))
            .Entries.Should().BeEmpty();

        var escapedCall = await retained!.GetAsync(
            collection,
            new RecordId("timed-out"),
            Operation(BaseOperationKind.Get, collection.Id));
        escapedCall.Status.Should().Be(OperationStatus.StoreError);
        escapedCall.Error!.Code.Should().Be("sqlite.mutation.sessionClosed");
    }

    [Fact]
    public async Task SingleExecutorUsesTheSameGuardedSessionPath()
    {
        await using var store = Store();
        var collection = Collection("items");
        IAtomicRecordSession? retained = null;
        var processor = new CallbackProcessor((session, _) =>
        {
            retained = session;
            return ValueTask.FromResult(Ready([]));
        });

        var execution = await store.ExecuteSingleAsync(processor, ExecutionRequest());

        execution.Outcome.Should().Be(RecordMutationExecutionOutcome.Committed);
        var escapedCall = await retained!.GetAsync(
            collection,
            new RecordId("missing"),
            Operation(BaseOperationKind.Get, collection.Id));
        escapedCall.Status.Should().Be(OperationStatus.StoreError);
        escapedCall.Error!.Code.Should().Be("sqlite.mutation.sessionClosed");
    }

    [Fact]
    public async Task MutationFactPreservesRuntimeComputedChangedFields()
    {
        await using var store = Store();
        var collection = Collection("items");
        var processor = new CallbackProcessor(async (session, cancellationToken) =>
        {
            var created = await session.CreateAsync(
                collection,
                new RecordCreateRequest
                {
                    RequestedId = new RecordId("changed-fields"),
                    Payload = Payload("value")
                },
                MutationContext(
                    BaseRecordMutationKind.Create,
                    "evt-changed-fields",
                    collection.Id) with
                {
                    ChangedFields = ["value", "removedByReplace"]
                },
                cancellationToken);
            return Ready([created.Value!.Mutation]);
        });

        var execution = await store.ExecuteSingleAsync(processor, ExecutionRequest());

        execution.Outcome.Should().Be(RecordMutationExecutionOutcome.Committed);
        execution.Processing!.Mutations.Single().ChangedFields
            .Should().Equal("value", "removedByReplace");
    }

    [Fact]
    public async Task JournalRetentionIsPrunedOncePerAtomicBoundary()
    {
        var time = new CountingTimeProvider();
        await using var store = SqliteTestFactory.Create(
            new HPDBaseSqliteOptions
            {
                StoreId = $"atomic-{Guid.NewGuid():N}",
                Collections = [Collection("items")]
            },
            time);
        var collection = Collection("items");
        var processor = new CallbackProcessor(async (session, cancellationToken) =>
        {
            var first = await session.CreateAsync(
                collection,
                new RecordCreateRequest
                {
                    RequestedId = new RecordId("first"),
                    Payload = Payload("first")
                },
                MutationContext(BaseRecordMutationKind.Create, "evt-first", collection.Id),
                cancellationToken);
            var second = await session.CreateAsync(
                collection,
                new RecordCreateRequest
                {
                    RequestedId = new RecordId("second"),
                    Payload = Payload("second")
                },
                MutationContext(BaseRecordMutationKind.Create, "evt-second", collection.Id),
                cancellationToken);
            return Ready([first.Value!.Mutation, second.Value!.Mutation]);
        });

        var execution = await store.ExecuteAtomicAsync(processor, ExecutionRequest());

        execution.Outcome.Should().Be(RecordMutationExecutionOutcome.Committed);
        time.UtcNowReads.Should().Be(1);
    }

    [Fact]
    public async Task UnsupportedExecutionBoundsRejectBeforeStorageOrProcessorAccess()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"hpd-base-sqlite-subsecond-{Guid.NewGuid():N}.db");
        await using var store = SqliteTestFactory.Create(new HPDBaseSqliteOptions
        {
            DataSource = path
        }, initializeSchema: false);
        var processor = new CallbackProcessor((_, _) =>
            throw new InvalidOperationException("The processor must not be invoked."));
        var requests = new[]
        {
            ExecutionRequest() with
            {
                AcquisitionTimeout = TimeSpan.FromMilliseconds(999)
            },
            ExecutionRequest() with
            {
                TransactionTimeout = TimeSpan.FromMilliseconds(999)
            },
            ExecutionRequest() with
            {
                CommitCompletionTimeout = TimeSpan.FromMilliseconds(999)
            },
            ExecutionRequest() with
            {
                TransactionTimeout = TimeSpan.FromMilliseconds(1_500)
            }
        };

        foreach (var request in requests)
        {
            var invoke = async () => await store.ExecuteSingleAsync(processor, request);
            await invoke.Should().ThrowAsync<ArgumentOutOfRangeException>()
                .WithMessage("*whole-second granularity*");
        }

        File.Exists(path).Should().BeFalse();
    }

    [Fact]
    public async Task CommitFailureWithConfirmedRollbackReturnsNoPersistedFacts()
    {
        var transactions = new FaultingTransactionController { FailCommit = true };
        await using var store = FaultableStore(transactions);
        var collection = Collection("items");
        var processor = CreateProcessor(
            collection,
            "commit-failure",
            "evt-commit-failure");

        var execution = await store.ExecuteAtomicAsync(processor, ExecutionRequest());

        execution.Outcome.Should().Be(RecordMutationExecutionOutcome.RollbackConfirmed);
        execution.Error!.Code.Should().Be("sqlite.database.unavailable");
        execution.Processing!.Mutations.Should().ContainSingle();
        transactions.CommitCalls.Should().Be(1);
        transactions.RollbackCalls.Should().Be(1);
        (await store.GetAsync(
            collection,
            new RecordId("commit-failure"),
            Operation(BaseOperationKind.Get, collection.Id))).Status.Should().Be(OperationStatus.NotFound);
        (await store.ReadMutationJournalAsync(
            new HPD.Base.BaseMutationJournalReadRequest { Limit = 10 }))
            .Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task RollbackFailureReturnsIndeterminateWithoutProvisionalFacts()
    {
        var transactions = new FaultingTransactionController { FailRollback = true };
        await using var store = FaultableStore(transactions);
        var processor = new CallbackProcessor((_, _) => ValueTask.FromResult(
            new AtomicMutationProcessingResult(
                AtomicMutationProcessingOutcome.Failed,
                [],
                Error("base.runtime.batch.itemInvalid"))));

        var execution = await store.ExecuteAtomicAsync(processor, ExecutionRequest());

        execution.Outcome.Should().Be(RecordMutationExecutionOutcome.Indeterminate);
        execution.Processing.Should().BeNull();
        execution.Error!.Code.Should().Be(BaseMutationErrorCodes.BatchIndeterminate);
        execution.Error.Store!.Retryable.Should().BeFalse();
        transactions.RollbackCalls.Should().Be(1);
    }

    [Fact]
    public async Task CommitAndRollbackFailureReturnsIndeterminateWithoutProvisionalFacts()
    {
        var transactions = new FaultingTransactionController
        {
            FailCommit = true,
            FailRollback = true
        };
        await using var store = FaultableStore(transactions);
        var collection = Collection("items");

        var execution = await store.ExecuteAtomicAsync(
            CreateProcessor(collection, "unknown", "evt-unknown"),
            ExecutionRequest());

        execution.Outcome.Should().Be(RecordMutationExecutionOutcome.Indeterminate);
        execution.Processing.Should().BeNull();
        execution.Error!.Code.Should().Be(BaseMutationErrorCodes.BatchIndeterminate);
        execution.Error.Store!.Retryable.Should().BeFalse();
        transactions.CommitCalls.Should().Be(1);
        transactions.RollbackCalls.Should().Be(1);
    }

    [Fact]
    public async Task NonCooperativeCommitCannotExceedCompletionBound()
    {
        var transactions = new FaultingTransactionController { BlockCommit = true };
        await using var store = FaultableStore(transactions);
        var collection = Collection("items");
        var started = System.Diagnostics.Stopwatch.StartNew();

        var execution = await store.ExecuteAtomicAsync(
            CreateProcessor(collection, "commit-timeout", "evt-commit-timeout"),
            ExecutionRequest() with { CommitCompletionTimeout = TimeSpan.FromSeconds(1) });

        started.Stop();
        execution.Outcome.Should().Be(RecordMutationExecutionOutcome.Indeterminate);
        execution.Processing.Should().BeNull();
        execution.Error!.Code.Should().Be(BaseMutationErrorCodes.BatchIndeterminate);
        started.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(3));

        transactions.ReleaseCommit();
        await transactions.CommitExited.WaitAsync(TimeSpan.FromSeconds(3));
    }

    [Fact]
    public async Task NonCooperativeRollbackCannotExceedCompletionBound()
    {
        var transactions = new FaultingTransactionController { BlockRollback = true };
        await using var store = FaultableStore(transactions);
        var processor = new CallbackProcessor((_, _) => ValueTask.FromResult(
            new AtomicMutationProcessingResult(
                AtomicMutationProcessingOutcome.Failed,
                [],
                Error("base.runtime.batch.itemInvalid"))));
        var started = System.Diagnostics.Stopwatch.StartNew();

        var execution = await store.ExecuteAtomicAsync(
            processor,
            ExecutionRequest() with { CommitCompletionTimeout = TimeSpan.FromSeconds(1) });

        started.Stop();
        execution.Outcome.Should().Be(RecordMutationExecutionOutcome.Indeterminate);
        execution.Processing.Should().BeNull();
        execution.Error!.Code.Should().Be(BaseMutationErrorCodes.BatchIndeterminate);
        started.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(3));

        transactions.ReleaseRollback();
        await transactions.RollbackExited.WaitAsync(TimeSpan.FromSeconds(3));
    }

    [Fact]
    public async Task RepeatedCleanupFailuresRemainCappedAndVisibleWithoutChangingCommits()
    {
        var disposer = new FaultingResourceDisposer();
        var options = new HPDBaseSqliteOptions
        {
            StoreId = $"atomic-{Guid.NewGuid():N}",
            Collections = [Collection("items")],
            MaxTrackedMutationExecutions = 2,
            QuarantinedMutationDrainTimeout = TimeSpan.FromMilliseconds(100)
        };
        var store = SqliteTestFactory.Create(
            options,
            transactionResourceDisposer: disposer);
        var collection = Collection("items");

        var first = await store.ExecuteAtomicAsync(
            CreateProcessor(collection, "cleanup-failure-1", "evt-cleanup-failure-1"),
            ExecutionRequest());
        var second = await store.ExecuteAtomicAsync(
            CreateProcessor(collection, "cleanup-failure-2", "evt-cleanup-failure-2"),
            ExecutionRequest());

        first.Outcome.Should().Be(RecordMutationExecutionOutcome.Committed);
        second.Outcome.Should().Be(RecordMutationExecutionOutcome.Committed);
        first.Processing!.Mutations.Should().ContainSingle();
        second.Processing!.Mutations.Should().ContainSingle();
        disposer.Calls.Should().Be(2);
        store.QuarantinedMutationCount.Should().Be(2);

        var health = await new SqliteHealthContributor(Options.Create(options), store)
            .GetHealthAsync();
        health.Single().Status.Should().Be(HealthStatus.Degraded);

        var rejected = await store.ExecuteAtomicAsync(
            CreateProcessor(collection, "cleanup-rejected", "evt-cleanup-rejected"),
            ExecutionRequest() with { AcquisitionTimeout = TimeSpan.FromSeconds(1) });
        rejected.Outcome.Should().Be(
            RecordMutationExecutionOutcome.CancelledRollbackConfirmed);
        disposer.Calls.Should().Be(2);

        await store.DisposeAsync();
        await disposer.ReleaseAllAsync();
    }

    [Fact]
    public async Task UnhealthyDatabaseWinsOverQuarantineDegradation()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"hpd-base-health-quarantine-{Guid.NewGuid():N}.db");
        var disposer = new FaultingResourceDisposer();
        var options = new HPDBaseSqliteOptions
        {
            StoreId = $"atomic-{Guid.NewGuid():N}",
            DataSource = path,
            Collections = [Collection("items")],
            QuarantinedMutationDrainTimeout = TimeSpan.FromMilliseconds(100)
        };
        var store = SqliteTestFactory.Create(
            options,
            transactionResourceDisposer: disposer);
        try
        {
            var collection = Collection("items");
            var execution = await store.ExecuteAtomicAsync(
                CreateProcessor(collection, "unhealthy-quarantine", "evt-unhealthy-quarantine"),
                ExecutionRequest());
            execution.Outcome.Should().Be(RecordMutationExecutionOutcome.Committed);
            store.QuarantinedMutationCount.Should().Be(1);

            await using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = $"DROP TABLE {PhysicalTable("items")};";
                await command.ExecuteNonQueryAsync();
            }

            var health = await new SqliteHealthContributor(Options.Create(options), store)
                .GetHealthAsync();
            health.Single().Status.Should().Be(HealthStatus.Unhealthy);
            health.Single().Metrics.Should().Contain(metric =>
                metric.Name == "quarantinedMutations" && metric.NumberValue == 1);
        }
        finally
        {
            await store.DisposeAsync();
            await disposer.ReleaseAllAsync();
            SqliteConnection.ClearAllPools();
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public async Task NonCooperativeSessionOperationIsQuarantinedWithoutRollbackRace()
    {
        var sessions = new BlockingSessionOperationController();
        var transactions = new FaultingTransactionController();
        await using var store = SqliteTestFactory.Create(
            new HPDBaseSqliteOptions
            {
                StoreId = $"atomic-{Guid.NewGuid():N}",
                Collections = [Collection("items")]
            },
            transactions: transactions,
            sessionOperations: sessions);
        var collection = Collection("items");
        var processor = new CallbackProcessor(async (session, cancellationToken) =>
        {
            _ = await session.GetAsync(
                collection,
                new RecordId("blocked"),
                Operation(BaseOperationKind.Get, collection.Id),
                cancellationToken);
            return Ready([]);
        });
        var started = System.Diagnostics.Stopwatch.StartNew();

        var execution = await store.ExecuteAtomicAsync(
            processor,
            ExecutionRequest(transactionTimeout: TimeSpan.FromSeconds(1)) with
            {
                CommitCompletionTimeout = TimeSpan.FromSeconds(1)
            });

        started.Stop();
        execution.Outcome.Should().Be(RecordMutationExecutionOutcome.Indeterminate);
        started.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(3));
        transactions.RollbackCalls.Should().Be(0);
        store.QuarantinedMutationCount.Should().Be(1);

        sessions.Release();
        await sessions.Exited.WaitAsync(TimeSpan.FromSeconds(3));
        await WaitForAsync(() => store.QuarantinedMutationCount == 0);
    }

    [Fact]
    public async Task PermanentQuarantineIsCappedReportedAndDoesNotBlockStoreDisposal()
    {
        var transactions = new FaultingTransactionController { BlockCommit = true };
        var options = new HPDBaseSqliteOptions
        {
            StoreId = $"atomic-{Guid.NewGuid():N}",
            Collections = [Collection("items")],
            MaxTrackedMutationExecutions = 1,
            QuarantinedMutationDrainTimeout = TimeSpan.FromMilliseconds(100)
        };
        var store = SqliteTestFactory.Create(options, transactions: transactions);
        var collection = Collection("items");

        var first = await store.ExecuteAtomicAsync(
            CreateProcessor(collection, "permanent", "evt-permanent"),
            ExecutionRequest() with { CommitCompletionTimeout = TimeSpan.FromSeconds(1) });
        first.Outcome.Should().Be(RecordMutationExecutionOutcome.Indeterminate);
        store.QuarantinedMutationCount.Should().Be(1);

        var health = await new SqliteHealthContributor(Options.Create(options), store)
            .GetHealthAsync();
        health.Single().Status.Should().Be(HealthStatus.Degraded);
        health.Single().Metrics.Should().Contain(metric =>
            metric.Name == "quarantinedMutations" && metric.NumberValue == 1);

        var second = await store.ExecuteAtomicAsync(
            CreateProcessor(collection, "rejected", "evt-rejected"),
            ExecutionRequest() with { AcquisitionTimeout = TimeSpan.FromSeconds(1) });
        second.Outcome.Should().Be(
            RecordMutationExecutionOutcome.CancelledRollbackConfirmed);

        var disposalStarted = System.Diagnostics.Stopwatch.StartNew();
        await store.DisposeAsync();
        disposalStarted.Stop();
        disposalStarted.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(1));

        transactions.ReleaseCommit();
        await transactions.CommitExited.WaitAsync(TimeSpan.FromSeconds(3));
    }

    [Fact]
    public async Task IdentifiedRetryRemainsIndeterminateUntilLateCommitResolvesToReceipt()
    {
        var transactions = new FaultingTransactionController { BlockCommit = true };
        var options = new HPDBaseSqliteOptions
        {
            StoreId = $"atomic-{Guid.NewGuid():N}",
            Collections = [Collection("items")],
            MaxTrackedMutationExecutions = 1,
        };
        await using SqliteRecordStore store = SqliteTestFactory.Create(options, transactions: transactions);
        CollectionDefinition collection = Collection("items");
        BaseMutationRequestIdentity identity = BaseMutationRequestIdentity.Create(
            "scope", "operation", "request",
            BaseMutationRequestFingerprint.Create(System.Security.Cryptography.SHA256.HashData("request"u8)));
        RecordMutationExecutionRequest request = ExecutionRequest() with
        {
            CommitCompletionTimeout = TimeSpan.FromSeconds(1),
            AtomicRequest = new BaseAtomicMutationExecutionRequest
            {
                Identity = identity,
                StructuralDigest = System.Security.Cryptography.SHA256.HashData("structure"u8),
                ExpiresAt = DateTimeOffset.UtcNow.AddDays(1),
                MaxReceiptBytes = 4_096,
            },
        };
        CallbackProcessor processor = CreateProcessor(collection, "identified", "evt-identified");

        RecordMutationExecutionResult first = await store.ExecuteAtomicAsync(processor, request);
        var retryTimer = System.Diagnostics.Stopwatch.StartNew();
        RecordMutationExecutionResult unresolvedRetry = await store.ExecuteAtomicAsync(processor, request);
        retryTimer.Stop();

        first.Outcome.Should().Be(RecordMutationExecutionOutcome.Indeterminate);
        unresolvedRetry.Outcome.Should().Be(RecordMutationExecutionOutcome.Indeterminate);
        retryTimer.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(1));

        transactions.ReleaseCommit();
        await transactions.CommitExited.WaitAsync(TimeSpan.FromSeconds(3));
        await WaitForAsync(() => store.QuarantinedMutationCount == 0);
        RecordMutationExecutionResult resolvedRetry = await store.ExecuteAtomicAsync(processor, request);

        resolvedRetry.Outcome.Should().Be(RecordMutationExecutionOutcome.Committed);
        resolvedRetry.RequestDisposition.Should().Be(BaseMutationRequestDisposition.Duplicate);
    }

    [Fact]
    public async Task ExpiredReceiptIsNewWhetherRetainedOrPhysicallyPruned()
    {
        string path = Path.Combine(Path.GetTempPath(), $"hpd-base-receipt-expiry-{Guid.NewGuid():N}.db");
        var clock = new MutableTimeProvider(new DateTimeOffset(2026, 8, 3, 0, 0, 0, TimeSpan.Zero));
        var options = new HPDBaseSqliteOptions
        {
            StoreId = "receipt-expiry",
            DataSource = path,
            Collections = [Collection("items")],
        };
        try
        {
            await using SqliteRecordStore store = SqliteTestFactory.Create(options, timeProvider: clock);
            CollectionDefinition collection = Collection("items");
            BaseMutationRequestIdentity retainedIdentity = Identity("retained");
            BaseMutationRequestIdentity prunedIdentity = Identity("pruned");

            (await store.ExecuteAtomicAsync(
                CreateProcessor(collection, "retained-before", "evt-retained-before"),
                IdentifiedRequest(retainedIdentity, clock.GetUtcNow().AddHours(1))))
                .RequestDisposition.Should().Be(BaseMutationRequestDisposition.Committed);
            (await store.ExecuteAtomicAsync(
                CreateProcessor(collection, "pruned-before", "evt-pruned-before"),
                IdentifiedRequest(prunedIdentity, clock.GetUtcNow().AddHours(1))))
                .RequestDisposition.Should().Be(BaseMutationRequestDisposition.Committed);

            clock.Advance(TimeSpan.FromHours(2));
            RecordMutationExecutionResult retained = await store.ExecuteAtomicAsync(
                CreateProcessor(collection, "retained-after", "evt-retained-after"),
                IdentifiedRequest(retainedIdentity, clock.GetUtcNow().AddHours(1)));

            using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
            {
                connection.Open();
                using var delete = connection.CreateCommand();
                delete.CommandText = "DELETE FROM hpd_base_operation_receipts WHERE scope='scope' AND operation='operation' AND idempotency_key='pruned';";
                delete.ExecuteNonQuery().Should().Be(1);
            }
            RecordMutationExecutionResult pruned = await store.ExecuteAtomicAsync(
                CreateProcessor(collection, "pruned-after", "evt-pruned-after"),
                IdentifiedRequest(prunedIdentity, clock.GetUtcNow().AddHours(1)));

            retained.RequestDisposition.Should().Be(BaseMutationRequestDisposition.Committed);
            pruned.RequestDisposition.Should().Be(BaseMutationRequestDisposition.Committed);
            retained.Outcome.Should().Be(RecordMutationExecutionOutcome.Committed);
            pruned.Outcome.Should().Be(RecordMutationExecutionOutcome.Committed);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (string candidate in new[] { path, path + "-wal", path + "-shm" })
                if (File.Exists(candidate)) File.Delete(candidate);
        }

        static BaseMutationRequestIdentity Identity(string key) => BaseMutationRequestIdentity.Create(
            "scope", "operation", key,
            BaseMutationRequestFingerprint.Create(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(key))));

        static RecordMutationExecutionRequest IdentifiedRequest(
            BaseMutationRequestIdentity identity,
            DateTimeOffset expiresAt) => ExecutionRequest() with
            {
                AtomicRequest = new BaseAtomicMutationExecutionRequest
                {
                    Identity = identity,
                    StructuralDigest = System.Security.Cryptography.SHA256.HashData("same-structure"u8),
                    ExpiresAt = expiresAt,
                    MaxReceiptBytes = 4_096,
                },
            };
    }

    [Fact]
    public async Task ConcurrentExactIdentityCommitsOneReceiptAndReplaysEveryOtherCaller()
    {
        await using SqliteRecordStore store = Store();
        CollectionDefinition collection = Collection("items");
        int processCalls = 0;
        var processor = new CallbackProcessor(async (session, cancellationToken) =>
        {
            Interlocked.Increment(ref processCalls);
            OperationResult<RecordMutationSessionResult> created = await session.CreateAsync(
                collection,
                new RecordCreateRequest
                {
                    RequestedId = new RecordId("concurrent-receipt"),
                    Payload = Payload("concurrent"),
                },
                MutationContext(BaseRecordMutationKind.Create, "evt-concurrent-receipt", collection.Id),
                cancellationToken);
            return Ready([created.Value!.Mutation]);
        });
        BaseMutationRequestIdentity identity = BaseMutationRequestIdentity.Create(
            "scope", "operation", "concurrent-key",
            BaseMutationRequestFingerprint.Create(System.Security.Cryptography.SHA256.HashData("concurrent-key"u8)));
        RecordMutationExecutionRequest request = ExecutionRequest() with
        {
            AtomicRequest = new BaseAtomicMutationExecutionRequest
            {
                Identity = identity,
                StructuralDigest = System.Security.Cryptography.SHA256.HashData("concurrent-structure"u8),
                ExpiresAt = DateTimeOffset.UtcNow.AddDays(1),
                MaxReceiptBytes = 4_096,
            },
        };

        RecordMutationExecutionResult[] results = await Task.WhenAll(
            Enumerable.Range(0, 12).Select(_ => store.ExecuteAtomicAsync(processor, request).AsTask()));

        processCalls.Should().Be(1);
        results.Should().ContainSingle(result => result.RequestDisposition == BaseMutationRequestDisposition.Committed);
        results.Count(result => result.RequestDisposition == BaseMutationRequestDisposition.Duplicate).Should().Be(11);
        results.Should().OnlyContain(result => result.Outcome == RecordMutationExecutionOutcome.Committed);
    }

    [Fact]
    public async Task DescriptorAdvertisesOnlyProvenL30Guarantees()
    {
        await using var store = Store();

        store.Capabilities.Read.MaxPageSize.Should().Be(1_000);
        store.Capabilities.Mutation.Replace.Should().BeTrue();
        store.Capabilities.Revision!.Replace.Should().BeTrue();
        store.Capabilities.Batch.Should().BeEquivalentTo(new StoreBatchCapability
        {
            Modes = [BaseRecordBatchExecutionMode.Atomic],
            MaxOperations = 100,
            MaxCanonicalPayloadBytes = 1_048_576,
            MinimumAcquisitionTimeout = TimeSpan.FromSeconds(1),
            MinimumTransactionTimeout = TimeSpan.FromSeconds(1),
            MinimumCommitCompletionTimeout = TimeSpan.FromSeconds(1),
            TimeoutGranularity = TimeSpan.FromSeconds(1),
            Ordered = true,
            PartialResults = false,
            CrossCollectionAtomic = true,
            ReadYourWrites = true,
            Durable = true,
            TransactionalJournal = true,
            Isolation = BaseTransactionIsolation.Serializable,
            NestedTransactions = false,
            Savepoints = false
        });
        store.Capabilities.Upsert!.Atomic.Should().BeTrue();
        store.Capabilities.Upsert.UpdateModes.Should()
            .Equal(RecordUpsertUpdateMode.Patch, RecordUpsertUpdateMode.Replace);
    }

    [Fact]
    public async Task DescriptorDoesNotAdvertisePortableUpsertWithoutRequestedIds()
    {
        await using var store = SqliteTestFactory.Create(new HPDBaseSqliteOptions
        {
            StoreId = $"atomic-{Guid.NewGuid():N}",
            AllowClientRequestedIds = false
        });

        store.Capabilities.Upsert.Should().BeNull();
    }

    private static SqliteRecordStore Store() =>
        SqliteTestFactory.Create(new HPDBaseSqliteOptions
        {
            StoreId = $"atomic-{Guid.NewGuid():N}",
            Collections = [Collection("items"), Collection("first"), Collection("second")]
        });

    private static SqliteRecordStore FaultableStore(
        ISqliteTransactionController transactions) =>
        SqliteTestFactory.Create(
            new HPDBaseSqliteOptions
            {
                StoreId = $"atomic-{Guid.NewGuid():N}",
                Collections = [Collection("items")]
            },
            transactions: transactions);

    private static string PhysicalTable(string collectionId) => "b_c_" + Convert.ToHexStringLower(
        System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(collectionId)))[..32];

    private static CallbackProcessor CreateProcessor(
        CollectionDefinition collection,
        string id,
        string eventId) =>
        new(async (session, cancellationToken) =>
        {
            var created = await session.CreateAsync(
                collection,
                new RecordCreateRequest
                {
                    RequestedId = new RecordId(id),
                    Payload = Payload(id)
                },
                MutationContext(BaseRecordMutationKind.Create, eventId, collection.Id),
                cancellationToken);
            return Ready([created.Value!.Mutation]);
        });

    private static RecordMutationExecutionRequest ExecutionRequest(
        TimeSpan? transactionTimeout = null) => new()
    {
        AcquisitionTimeout = TimeSpan.FromSeconds(2),
        TransactionTimeout = transactionTimeout ?? TimeSpan.FromSeconds(2),
        CommitCompletionTimeout = TimeSpan.FromSeconds(2)
    };

    private static CollectionDefinition Collection(string id) => new()
    {
        Id = id,
        Name = id,
        Kind = BaseCollectionKinds.Document,
        SchemaMode = SchemaMode.Loose,
        UnknownFields = UnknownFieldPolicy.Preserve,
        MutationMode = BaseCollectionMutationMode.Mutable
    };

    private static OperationContext Operation(BaseOperationKind kind, string collectionId) => new()
    {
        Operation = kind,
        CollectionId = collectionId,
        Now = DateTimeOffset.Parse("2026-07-30T12:00:00Z")
    };

    private static RecordMutationSessionContext MutationContext(
        BaseRecordMutationKind kind,
        string eventId,
        string collectionId) => new()
    {
        RequestedOperation = kind,
        EventId = eventId,
        Operation = Operation(kind switch
        {
            BaseRecordMutationKind.Create => BaseOperationKind.Create,
            BaseRecordMutationKind.Patch => BaseOperationKind.Patch,
            BaseRecordMutationKind.Replace => BaseOperationKind.Replace,
            BaseRecordMutationKind.Delete => BaseOperationKind.Delete,
            BaseRecordMutationKind.Upsert => BaseOperationKind.Upsert,
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        }, collectionId)
    };

    private static RecordPayload Payload(string value) => new()
    {
        Kind = RecordPayloadKind.FieldMap,
        Fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["value"] = JsonSerializer.SerializeToElement(value)
        }
    };

    private static AtomicMutationProcessingResult Ready(
        IEnumerable<BaseRecordMutationFact> mutations) =>
        new(AtomicMutationProcessingOutcome.ReadyToCommit, mutations.ToArray());

    private static BaseError Error(string code) => new()
    {
        Code = code,
        Message = "The processor rejected the mutation.",
        Category = ErrorCategory.Validation
    };

    private sealed class CallbackProcessor(
        Func<IAtomicRecordSession, CancellationToken, ValueTask<AtomicMutationProcessingResult>> callback)
        : IAtomicMutationProcessor
    {
        public ValueTask<AtomicMutationProcessingResult> ProcessAsync(
            IAtomicRecordSession session,
            CancellationToken cancellationToken = default) =>
            callback(session, cancellationToken);

        public ValueTask<AtomicMutationProcessingResult> ResolveReceiptAsync(
            BaseRecordMutationFact[] committedMutations,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(Ready(committedMutations));
        }
    }

    private sealed class CountingTimeProvider : TimeProvider
    {
        private int _utcNowReads;

        public int UtcNowReads => Volatile.Read(ref _utcNowReads);

        public override DateTimeOffset GetUtcNow()
        {
            Interlocked.Increment(ref _utcNowReads);
            return DateTimeOffset.Parse("2026-07-30T12:00:00Z");
        }
    }

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan duration) => _now += duration;
    }

    private sealed class FaultingTransactionController : ISqliteTransactionController
    {
        private readonly TaskCompletionSource _commitRelease =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _commitExited =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _rollbackRelease =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _rollbackExited =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool FailCommit { get; init; }

        public bool FailRollback { get; init; }

        public bool BlockCommit { get; init; }

        public bool BlockRollback { get; init; }

        public int CommitCalls { get; private set; }

        public int RollbackCalls { get; private set; }

        public Task CommitExited => _commitExited.Task;

        public Task RollbackExited => _rollbackExited.Task;

        public void ReleaseCommit() => _commitRelease.TrySetResult();

        public void ReleaseRollback() => _rollbackRelease.TrySetResult();

        public SqliteTransaction BeginImmediate(SqliteConnection connection) =>
            connection.BeginTransaction(deferred: false);

        public async ValueTask CommitAsync(
            SqliteTransaction transaction,
            CancellationToken cancellationToken)
        {
            CommitCalls++;
            cancellationToken.ThrowIfCancellationRequested();
            if (FailCommit)
                throw new InvalidOperationException("Injected commit failure.");

            if (BlockCommit)
                await _commitRelease.Task.ConfigureAwait(false);

            try
            {
                await transaction.CommitAsync(
                    BlockCommit ? CancellationToken.None : cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _commitExited.TrySetResult();
            }
        }

        public async ValueTask RollbackAsync(
            SqliteTransaction transaction,
            CancellationToken cancellationToken)
        {
            RollbackCalls++;
            cancellationToken.ThrowIfCancellationRequested();
            if (FailRollback)
                throw new InvalidOperationException("Injected rollback failure.");

            if (BlockRollback)
                await _rollbackRelease.Task.ConfigureAwait(false);

            try
            {
                await transaction.RollbackAsync(
                    BlockRollback ? CancellationToken.None : cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _rollbackExited.TrySetResult();
            }
        }
    }

    private sealed class BlockingSessionOperationController
        : ISqliteSessionOperationController
    {
        private readonly TaskCompletionSource _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _exited =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Exited => _exited.Task;

        public void Release() => _release.TrySetResult();

        public async ValueTask BeforeExecuteAsync(CancellationToken cancellationToken)
        {
            _ = cancellationToken;
            await _release.Task.ConfigureAwait(false);
            _exited.TrySetResult();
        }
    }

    private sealed class FaultingResourceDisposer : ISqliteTransactionResourceDisposer
    {
        private readonly List<(SqliteTransaction Transaction, SqliteConnection Connection)>
            _resources = [];

        public int Calls { get; private set; }

        public ValueTask DisposeAsync(
            SqliteTransaction transaction,
            SqliteConnection connection)
        {
            _resources.Add((transaction, connection));
            Calls++;
            throw new InvalidOperationException("Injected cleanup failure.");
        }

        public async ValueTask ReleaseAllAsync()
        {
            foreach (var (transaction, connection) in _resources)
            {
                try
                {
                    await transaction.DisposeAsync();
                }
                catch
                {
                    // Test cleanup still closes the owning connection.
                }

                await connection.DisposeAsync();
            }

            _resources.Clear();
        }
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
                throw new TimeoutException("The bounded condition was not reached.");

            await Task.Delay(10);
        }
    }
}
