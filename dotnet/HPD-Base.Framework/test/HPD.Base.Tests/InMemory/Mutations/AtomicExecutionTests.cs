using HPD.Base.Tests.InMemory.TestDoubles;

namespace HPD.Base.Tests.InMemory.Mutations;

public sealed class AtomicExecutionTests
{
    private static readonly RecordMutationExecutionRequest ExecutionRequest = new()
    {
        AcquisitionTimeout = TimeSpan.FromSeconds(5),
        TransactionTimeout = TimeSpan.FromSeconds(5),
        CommitCompletionTimeout = TimeSpan.FromSeconds(5)
    };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SingleAndAtomicUseTheSameRestrictedSessionExecutor(bool atomic)
    {
        var store = new InMemoryRecordStore();
        var collection = InMemoryTestData.Collection();
        var processor = new CallbackProcessor(async (session, token) =>
        {
            var create = await session.CreateAsync(
                collection,
                Create("one", "value"),
                Context(BaseRecordMutationKind.Create, "one"),
                token);
            return Ready(create);
        });

        var execution = atomic
            ? await store.ExecuteAtomicAsync(processor, ExecutionRequest)
            : await store.ExecuteSingleAsync(processor, ExecutionRequest);

        execution.Outcome.Should().Be(RecordMutationExecutionOutcome.Committed);
        processor.InvocationCount.Should().Be(1);
        var read = await store.GetAsync(
            collection,
            new RecordId("one"),
            InMemoryTestData.Operation(BaseOperationKind.Get));
        read.Status.Should().Be(OperationStatus.Ok);
    }

    [Fact]
    public async Task AtomicSessionProvidesOrderedReadYourWritesAcrossCollections()
    {
        var store = new InMemoryRecordStore();
        var firstCollection = InMemoryTestData.Collection("first");
        var secondCollection = InMemoryTestData.Collection("second");
        var processor = new CallbackProcessor(async (session, token) =>
        {
            var created = await session.CreateAsync(
                firstCollection,
                Create("shared", "before"),
                Context(BaseRecordMutationKind.Create, "create"),
                token);
            created.Status.Should().Be(OperationStatus.Created);

            var observed = await session.GetAsync(
                firstCollection,
                new RecordId("shared"),
                InMemoryTestData.Operation(BaseOperationKind.Get, "first"),
                token);
            observed.Value!.Payload.Fields!["title"].GetString().Should().Be("before");

            var patched = await session.PatchAsync(
                firstCollection,
                new RecordId("shared"),
                new RecordPatchRequest { Patch = InMemoryTestData.Patch("title", "after") },
                Context(BaseRecordMutationKind.Patch, "patch"),
                token);
            var second = await session.CreateAsync(
                secondCollection,
                Create("other", "second"),
                Context(BaseRecordMutationKind.Create, "second"),
                token);
            return new AtomicMutationProcessingResult(
                AtomicMutationProcessingOutcome.ReadyToCommit,
                [created.Value!.Mutation, patched.Value!.Mutation, second.Value!.Mutation]);
        });

        var execution = await store.ExecuteAtomicAsync(processor, ExecutionRequest);

        execution.Outcome.Should().Be(RecordMutationExecutionOutcome.Committed);
        var first = await store.GetAsync(
            firstCollection,
            new RecordId("shared"),
            InMemoryTestData.Operation(BaseOperationKind.Get, "first"));
        var second = await store.GetAsync(
            secondCollection,
            new RecordId("other"),
            InMemoryTestData.Operation(BaseOperationKind.Get, "second"));
        first.Value!.Payload.Fields!["title"].GetString().Should().Be("after");
        second.Status.Should().Be(OperationStatus.Ok);
    }

    [Fact]
    public async Task FailedAtomicExecutionRollsBackRecordsAndCounters()
    {
        var store = new InMemoryRecordStore();
        var collection = InMemoryTestData.Collection();
        var failed = new CallbackProcessor(async (session, token) =>
        {
            var create = await session.CreateAsync(
                collection,
                new RecordCreateRequest
                {
                    Payload = InMemoryTestData.Payload(("title", "discard"))
                },
                Context(BaseRecordMutationKind.Create, "discard"),
                token);
            return new AtomicMutationProcessingResult(
                AtomicMutationProcessingOutcome.Failed,
                [create.Value!.Mutation],
                Error("test.rollback", "Force rollback."));
        });

        var failure = await store.ExecuteAtomicAsync(failed, ExecutionRequest);
        var committed = await InMemoryMutationTestDriver.CreateAsync(
            store,
            collection,
            new RecordCreateRequest
            {
                Payload = InMemoryTestData.Payload(("title", "kept"))
            },
            InMemoryTestData.Operation(BaseOperationKind.Create));

        failure.Outcome.Should().Be(RecordMutationExecutionOutcome.RollbackConfirmed);
        committed.Value!.Id.Value.Should().Be("mem:0000000000000001");
        committed.Value.Metadata.Revision!.Value.Value.Should().Be("mem:0000000000000001");
    }

    [Fact]
    public async Task ConcurrentCommitCausesConfirmedConflictWithoutRetryOrLostWrite()
    {
        var store = new InMemoryRecordStore();
        var collection = InMemoryTestData.Collection();
        var staged = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var losingProcessor = new CallbackProcessor(async (session, token) =>
        {
            var create = await session.CreateAsync(
                collection,
                Create("loser", "discard"),
                Context(BaseRecordMutationKind.Create, "loser"),
                token);
            staged.SetResult();
            await release.Task.WaitAsync(token);
            return Ready(create);
        });

        var losingExecution = store.ExecuteAtomicAsync(
            losingProcessor,
            ExecutionRequest).AsTask();
        await staged.Task;
        var winning = await InMemoryMutationTestDriver.CreateAsync(
            store,
            collection,
            Create("winner", "keep"),
            InMemoryTestData.Operation(BaseOperationKind.Create));
        release.SetResult();
        var conflict = await losingExecution;

        winning.Status.Should().Be(OperationStatus.Created);
        conflict.Outcome.Should().Be(RecordMutationExecutionOutcome.ConflictRollbackConfirmed);
        conflict.Error!.Code.Should().Be(BaseMutationErrorCodes.TransactionConflict);
        conflict.Error.Store!.Retryable.Should().BeFalse();
        losingProcessor.InvocationCount.Should().Be(1);
        (await store.GetAsync(
            collection,
            new RecordId("winner"),
            InMemoryTestData.Operation(BaseOperationKind.Get))).Status.Should().Be(OperationStatus.Ok);
        (await store.GetAsync(
            collection,
            new RecordId("loser"),
            InMemoryTestData.Operation(BaseOperationKind.Get))).Status.Should().Be(OperationStatus.NotFound);
    }

    [Fact]
    public async Task SessionFailsClosedAfterProviderInvocationCompletes()
    {
        var store = new InMemoryRecordStore();
        var collection = InMemoryTestData.Collection();
        IAtomicRecordSession? retained = null;
        var processor = new CallbackProcessor(async (session, token) =>
        {
            retained = session;
            var create = await session.CreateAsync(
                collection,
                Create("one", "value"),
                Context(BaseRecordMutationKind.Create, "one"),
                token);
            return Ready(create);
        });

        var execution = await store.ExecuteSingleAsync(processor, ExecutionRequest);
        var escapedUse = await retained!.GetAsync(
            collection,
            new RecordId("one"),
            InMemoryTestData.Operation(BaseOperationKind.Get));

        execution.Outcome.Should().Be(RecordMutationExecutionOutcome.Committed);
        escapedUse.Status.Should().Be(OperationStatus.StoreError);
        escapedUse.Error!.Store!.Retryable.Should().BeFalse();
    }

    [Fact]
    public void CapabilitiesAdvertiseOnlyProvenL30GuaranteesAndBounds()
    {
        var capabilities = new InMemoryRecordStore().Capabilities;

        capabilities.Read.List.Should().BeTrue();
        capabilities.Read.Get.Should().BeTrue();
        capabilities.Mutation.Create.Should().BeTrue();
        capabilities.Revision!.Patch.Should().BeTrue();
        capabilities.Revision.Replace.Should().BeTrue();
        capabilities.Revision.Delete.Should().BeTrue();
        capabilities.Batch!.Modes.Should().Equal(BaseRecordBatchExecutionMode.Atomic);
        capabilities.Batch.MaxOperations.Should().Be(100);
        capabilities.Batch.MaxCanonicalPayloadBytes.Should().Be(1_048_576);
        capabilities.Batch.Ordered.Should().BeTrue();
        capabilities.Batch.CrossCollectionAtomic.Should().BeTrue();
        capabilities.Batch.ReadYourWrites.Should().BeTrue();
        capabilities.Batch.Durable.Should().BeFalse();
        capabilities.Batch.TransactionalJournal.Should().BeTrue();
        capabilities.Batch.Isolation.Should().Be(BaseTransactionIsolation.Serializable);
        capabilities.Batch.NestedTransactions.Should().BeFalse();
        capabilities.Batch.Savepoints.Should().BeFalse();
        capabilities.Upsert!.Atomic.Should().BeTrue();
        capabilities.Upsert.UpdateModes.Should().Equal(
            RecordUpsertUpdateMode.Patch,
            RecordUpsertUpdateMode.Replace);
        capabilities.Upsert.ExpectedRevision.Should().BeTrue();
        capabilities.Upsert.ExistenceConditions.Should().BeTrue();
    }

    [Fact]
    public void AtomicUpsertIsNotAdvertisedWhenRequestedIdsAreDisabled()
    {
        var capabilities = new InMemoryRecordStore(
            new HPDBaseInMemoryStoreOptions { AllowClientRequestedIds = false }).Capabilities;

        capabilities.Upsert.Should().BeNull();
    }

    private static RecordCreateRequest Create(string id, string title) => new()
    {
        RequestedId = new RecordId(id),
        Payload = InMemoryTestData.Payload(("title", title))
    };

    private static RecordMutationSessionContext Context(
        BaseRecordMutationKind mutation,
        string eventId) => new()
    {
        RequestedOperation = mutation,
        EventId = eventId,
        Operation = InMemoryTestData.Operation(
            mutation switch
            {
                BaseRecordMutationKind.Create => BaseOperationKind.Create,
                BaseRecordMutationKind.Patch => BaseOperationKind.Patch,
                BaseRecordMutationKind.Replace => BaseOperationKind.Replace,
                BaseRecordMutationKind.Delete => BaseOperationKind.Delete,
                _ => BaseOperationKind.Upsert
            })
    };

    private static AtomicMutationProcessingResult Ready(
        OperationResult<RecordMutationSessionResult> result)
    {
        result.Value.Should().NotBeNull();
        return new AtomicMutationProcessingResult(
            AtomicMutationProcessingOutcome.ReadyToCommit,
            [result.Value!.Mutation]);
    }

    private static BaseError Error(string code, string message) => new()
    {
        Code = code,
        Message = message,
        Category = ErrorCategory.Unexpected
    };

    private sealed class CallbackProcessor(
        Func<IAtomicRecordSession, CancellationToken, ValueTask<AtomicMutationProcessingResult>> callback)
        : IAtomicMutationProcessor
    {
        public int InvocationCount { get; private set; }

        public ValueTask<AtomicMutationProcessingResult> ProcessAsync(
            IAtomicRecordSession session,
            CancellationToken cancellationToken = default)
        {
            InvocationCount++;
            return callback(session, cancellationToken);
        }
    }
}
