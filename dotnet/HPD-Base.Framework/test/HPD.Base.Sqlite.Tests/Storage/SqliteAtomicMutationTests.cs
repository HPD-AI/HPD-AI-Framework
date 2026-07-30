using FluentAssertions;
using HPD.Base;
using HPD.Base.Records;
using HPD.Base.Results;
using HPD.Base.Runtime;
using HPD.Base.Schema;
using HPD.Base.Sqlite.Configuration;
using HPD.Base.Sqlite.Internal;
using HPD.Base.Stores;
using Microsoft.Data.Sqlite;
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
            new HPD.Base.Events.BaseMutationJournalReadRequest { Limit = 10 });
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
            new HPD.Base.Events.BaseMutationJournalReadRequest { Limit = 10 }))
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
            new HPD.Base.Events.BaseMutationJournalReadRequest { Limit = 10 }))
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
                CollectionIds = ["items"]
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
        });
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
            new HPD.Base.Events.BaseMutationJournalReadRequest { Limit = 10 }))
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
            CollectionIds = ["items", "first", "second"]
        });

    private static SqliteRecordStore FaultableStore(
        ISqliteTransactionController transactions) =>
        SqliteTestFactory.Create(
            new HPDBaseSqliteOptions
            {
                StoreId = $"atomic-{Guid.NewGuid():N}",
                CollectionIds = ["items"]
            },
            transactions: transactions);

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
        Operations = new CollectionOperationMatrix
        {
            List = true,
            Get = true,
            Create = true,
            Patch = true,
            Replace = true,
            Delete = true,
            Upsert = true
        }
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
}
