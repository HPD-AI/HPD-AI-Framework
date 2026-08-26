namespace HPD.Base.StoreConformance.Mutations;

public abstract class RecordStoreMutationModeConformanceTests<TFixture>
    where TFixture : IRecordStoreConformanceFixture, new()
{
    private readonly TFixture _fixture = new();

    [Fact]
    public async Task DirectSessionRejectsAppendOnlyReplaceAndDelete()
    {
        await _fixture.ResetAsync();
        IAtomicRecordStore store = (IAtomicRecordStore)await _fixture.CreateStoreAsync();
        CollectionDefinition collection = _fixture.Collection with { MutationMode = BaseCollectionMutationMode.AppendOnly };
        var errors = new List<string>();
        var processor = new DelegateProcessor(async session =>
        {
            RecordMutationSessionContext context = Context(BaseRecordMutationKind.Replace);
            OperationResult<RecordMutationSessionResult> replace = await session.ReplaceAsync(
                collection,
                RecordId.Create("record-1"),
                new RecordReplaceRequest { Payload = Payload() },
                context);
            OperationResult<RecordMutationSessionResult> delete = await session.DeleteAsync(
                collection,
                RecordId.Create("record-1"),
                new RecordDeleteRequest(),
                Context(BaseRecordMutationKind.Delete));
            errors.Add(replace.Error!.Code);
            errors.Add(delete.Error!.Code);
            return Failed(replace.Error!);
        });

        RecordMutationExecutionResult execution = await store.ExecuteAtomicAsync(processor, Request());

        Assert.Equal(RecordMutationExecutionOutcome.RollbackConfirmed, execution.Outcome);
        Assert.Equal(
            [
            BaseCollectionErrorCodes.AppendOnlyUpdateForbidden,
            BaseCollectionErrorCodes.AppendOnlyDeleteForbidden,
            ], errors);
        await DisposeAsync(store);
    }

    [Fact]
    public async Task DirectSessionRejectsEveryReadOnlyMutation()
    {
        await _fixture.ResetAsync();
        IAtomicRecordStore store = (IAtomicRecordStore)await _fixture.CreateStoreAsync();
        CollectionDefinition collection = _fixture.Collection with { MutationMode = BaseCollectionMutationMode.ReadOnly };
        BaseError? observed = null;
        var processor = new DelegateProcessor(async session =>
        {
            OperationResult<RecordMutationSessionResult> created = await session.CreateAsync(
                collection,
                new RecordCreateRequest { RequestedId = RecordId.Create("record-1"), Payload = Payload() },
                Context(BaseRecordMutationKind.Create));
            observed = created.Error;
            return Failed(created.Error!);
        });

        RecordMutationExecutionResult execution = await store.ExecuteAtomicAsync(processor, Request());

        Assert.Equal(RecordMutationExecutionOutcome.RollbackConfirmed, execution.Outcome);
        Assert.Equal(BaseCollectionErrorCodes.ReadOnlyMutationForbidden, observed!.Code);
        await DisposeAsync(store);
    }

    [Fact]
    public async Task DirectSessionAdvancesGenerationOnlyForPurgeEnabledHistory()
    {
        await _fixture.ResetAsync();
        IAtomicRecordStore store = (IAtomicRecordStore)await _fixture.CreateStoreAsync();
        CollectionDefinition appendOnly = _fixture.Collection with { MutationMode = BaseCollectionMutationMode.AppendOnly };
        CollectionDefinition purgeEnabled = _fixture.Collection with { MutationMode = BaseCollectionMutationMode.AppendOnlyWithAdministrativePurge };
        OperationResult<long>? rejected = null;
        OperationResult<long>? advanced = null;
        var processor = new DelegateProcessor(async session =>
        {
            rejected = await session.AdvancePurgeGenerationAsync(appendOnly, null);
            advanced = await session.AdvancePurgeGenerationAsync(purgeEnabled, 0);
            return new AtomicMutationProcessingResult(AtomicMutationProcessingOutcome.ReadyToCommit, []);
        });

        RecordMutationExecutionResult execution = await store.ExecuteAtomicAsync(processor, Request());

        Assert.Equal(RecordMutationExecutionOutcome.Committed, execution.Outcome);
        Assert.Equal(BaseCollectionErrorCodes.PurgeUnsupported, rejected!.Error!.Code);
        Assert.Equal(1, advanced!.Value);
        await DisposeAsync(store);
    }

    private static RecordMutationExecutionRequest Request() => new()
    {
        AcquisitionTimeout = TimeSpan.FromSeconds(5),
        TransactionTimeout = TimeSpan.FromSeconds(5),
        CommitCompletionTimeout = TimeSpan.FromSeconds(5),
    };

    private static RecordMutationSessionContext Context(BaseRecordMutationKind kind) => new()
    {
        RequestedOperation = kind,
        EventId = Guid.NewGuid().ToString("N"),
        Operation = new OperationContext
        {
            Operation = kind switch
            {
                BaseRecordMutationKind.Create => BaseOperationKind.Create,
                BaseRecordMutationKind.Replace => BaseOperationKind.Replace,
                BaseRecordMutationKind.Delete => BaseOperationKind.Delete,
                _ => BaseOperationKind.Batch,
            },
            CollectionId = "conformance-items",
            Now = DateTimeOffset.UnixEpoch,
        },
    };

    private static RecordPayload Payload() => new()
    {
        Kind = RecordPayloadKind.FieldMap,
        Fields = new Dictionary<string, JsonElement>
        {
            ["title"] = JsonSerializer.SerializeToElement("value"),
        },
    };

    private static AtomicMutationProcessingResult Failed(BaseError error) =>
        new(AtomicMutationProcessingOutcome.Failed, [], error);

    private static async ValueTask DisposeAsync(IAtomicRecordStore store)
    {
        if (store is IAsyncDisposable asyncDisposable)
            await asyncDisposable.DisposeAsync();
        else if (store is IDisposable disposable)
            disposable.Dispose();
    }

    private sealed class DelegateProcessor(
        Func<IAtomicRecordSession, ValueTask<AtomicMutationProcessingResult>> execute)
        : IAtomicMutationProcessor
    {
        public ValueTask<AtomicMutationProcessingResult> ProcessAsync(
            IAtomicRecordSession session,
            CancellationToken cancellationToken = default) => execute(session);
    }
}
