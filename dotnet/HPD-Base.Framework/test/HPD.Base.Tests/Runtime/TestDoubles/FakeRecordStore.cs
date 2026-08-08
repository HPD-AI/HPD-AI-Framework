using HPD.Base;

namespace HPD.Base.Tests;

internal class FakeRecordStore : IAtomicRecordStore
{
    protected readonly Dictionary<string, RecordEnvelope> Records = new(StringComparer.Ordinal);

    public FakeRecordStore(
        string storeId,
        RecordReadCapability? read = null,
        RecordMutationCapability? mutation = null,
        RevisionCapability? revision = null,
        bool includeAtomicBatchCapability = true,
        TimeSpan? minimumTimeout = null)
    {
        Capabilities = new StoreCapabilityDescriptor
        {
            StoreId = storeId,
            StoreKind = BaseStoreKinds.Custom,
            StoreVersion = "test",
            Read = read ?? new RecordReadCapability { List = true, Get = true, MaxPageSize = 1_000 },
            Mutation = mutation ?? new RecordMutationCapability
            {
                Create = true,
                Patch = true,
                Replace = true,
                Delete = true,
                IdAuthority = IdAuthority.Hybrid,
                TimestampAuthority = TimestampAuthority.Runtime,
                Consistency = ConsistencyModel.Strong
            },
            Query = QueryCapabilities(),
            Revision = revision,
            Batch = includeAtomicBatchCapability
                ? new StoreBatchCapability
            {
                Modes = [BaseRecordBatchExecutionMode.Atomic],
                MaxOperations = 1_000,
                MaxCanonicalPayloadBytes = 16_777_216,
                MinimumAcquisitionTimeout = minimumTimeout ?? TimeSpan.FromMilliseconds(10),
                MinimumTransactionTimeout = minimumTimeout ?? TimeSpan.FromMilliseconds(10),
                MinimumCommitCompletionTimeout = minimumTimeout ?? TimeSpan.FromMilliseconds(10),
                TimeoutGranularity = TimeSpan.FromMilliseconds(10),
                Ordered = true,
                PartialResults = true,
                CrossCollectionAtomic = true,
                ReadYourWrites = true,
                Durable = false,
                TransactionalJournal = false,
                Isolation = BaseTransactionIsolation.Serializable
            }
                : null,
            Upsert = new StoreUpsertCapability
            {
                Atomic = true,
                UpdateModes = [RecordUpsertUpdateMode.Patch, RecordUpsertUpdateMode.Replace],
                ExpectedRevision = revision?.Patch == true || revision?.Replace == true,
                ExistenceConditions = true
            }
        };
    }

    public StoreCapabilityDescriptor Capabilities { get; }
    public RecordQuery? LastListQuery { get; private set; }
    public int ListCalls { get; private set; }
    public int GetCalls { get; private set; }
    public int CreateCalls { get; private set; }
    public int PatchCalls { get; protected set; }
    public int ReplaceCalls { get; protected set; }
    public int DeleteCalls { get; private set; }
    public int SingleExecutionCalls { get; private set; }
    public int AtomicExecutionCalls { get; private set; }
    public IAtomicRecordSession? LastSession { get; private set; }
    public RecordMutationExecutionOutcome? ForcedOutcomeAfterProcessing { get; set; }
    public BaseError? ForcedOutcomeError { get; set; }
    public Func<BaseRecordMutationFact, BaseRecordMutationFact>? MutationFactTransform { get; set; }
    public List<OperationContext> MutationContexts { get; } = [];
    public RecordCreateRequest? LastCreateRequest { get; private set; }
    public RecordPatchRequest? LastPatchRequest { get; protected set; }
    public RecordReplaceRequest? LastReplaceRequest { get; protected set; }
    public Action? AfterCreateCommitted { get; set; }
    public BaseAtomicMutationProjectionRequest? LastProjectionRequest { get; private set; }

    public void AddRecord(RecordEnvelope record)
    {
        ArgumentNullException.ThrowIfNull(record);
        Records[record.Id.Value] = record.Metadata.Revision is null
            ? record with { Metadata = record.Metadata with { Revision = new RevisionToken("1") } }
            : record;
    }

    public ValueTask<OperationResult<RecordPage>> ListAsync(
        CollectionDefinition collection,
        RecordQuery query,
        OperationContext context,
        CancellationToken cancellationToken = default)
    {
        ListCalls++;
        LastListQuery = query;
        return ValueTask.FromResult(new OperationResult<RecordPage>
        {
            Status = OperationStatus.Ok,
            Value = new RecordPage
            {
                Items = Records.Values.ToArray(),
                Page = new PageInfo { Limit = Records.Count, HasMore = false }
            }
        });
    }

    public ValueTask<OperationResult<RecordEnvelope>> GetAsync(
        CollectionDefinition collection,
        RecordId id,
        OperationContext context,
        CancellationToken cancellationToken = default)
    {
        GetCalls++;
        return ValueTask.FromResult(Get(Records, id));
    }

    public virtual ValueTask<RecordMutationExecutionResult> ExecuteSingleAsync(
        IAtomicMutationProcessor processor,
        RecordMutationExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        SingleExecutionCalls++;
        return ExecuteCoreAsync(processor, cancellationToken);
    }

    public virtual ValueTask<RecordMutationExecutionResult> ExecuteAtomicAsync(
        IAtomicMutationProcessor processor,
        RecordMutationExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        AtomicExecutionCalls++;
        return ExecuteCoreAsync(processor, cancellationToken);
    }

    private async ValueTask<RecordMutationExecutionResult> ExecuteCoreAsync(
        IAtomicMutationProcessor processor,
        CancellationToken cancellationToken)
    {
        var staged = Records.ToDictionary(
            static pair => pair.Key,
            static pair => pair.Value,
            StringComparer.Ordinal);
        var session = new Session(this, staged);
        LastSession = session;
        AtomicMutationProcessingResult processing;
        try
        {
            processing = await processor.ProcessAsync(session, cancellationToken);
        }
        finally
        {
            session.Close();
        }

        if (processing.Outcome == AtomicMutationProcessingOutcome.Failed)
        {
            return new RecordMutationExecutionResult(
                RecordMutationExecutionOutcome.RollbackConfirmed,
                processing,
                processing.Error);
        }

        if (ForcedOutcomeAfterProcessing is { } forced
            && forced != RecordMutationExecutionOutcome.Committed)
        {
            return new RecordMutationExecutionResult(
                forced,
                forced == RecordMutationExecutionOutcome.Indeterminate ? null : processing,
                ForcedOutcomeError);
        }

        Records.Clear();
        foreach (var pair in staged)
            Records.Add(pair.Key, pair.Value);
        if (processing.Mutations.Any(static mutation =>
                mutation.CommittedOperation == BaseCommittedRecordMutationKind.Create))
        {
            AfterCreateCommitted?.Invoke();
        }

        return new RecordMutationExecutionResult(
            RecordMutationExecutionOutcome.Committed,
            processing);
    }

    private static OperationResult<RecordEnvelope> Get(
        Dictionary<string, RecordEnvelope> records,
        RecordId id) =>
        records.TryGetValue(id.Value, out var record)
            ? new OperationResult<RecordEnvelope> { Status = OperationStatus.Ok, Value = record }
            : new OperationResult<RecordEnvelope>
            {
                Status = OperationStatus.NotFound,
                Error = new BaseError
                {
                    Code = "base.runtime.record.notFound",
                    Message = "Record was not found.",
                    Category = ErrorCategory.NotFound
                }
            };

    private static QueryCapability QueryCapabilities() => new()
    {
        Filter = new FilterCapability
        {
            Supported = true,
            BooleanComposition = true,
            Not = true,
            NullChecks = true,
            MissingFieldChecks = true
        },
        Sort = new SortCapability { Supported = true, NullOrdering = true },
        Pagination = new PaginationCapability
        {
            Page = true,
            Offset = true,
            Cursor = QueryCursorGuarantee.Seek,
            MaxLimit = 1_000
        },
        Count = new CountCapability
        {
            SupportedModes =
            [
                QueryCountMode.None,
                QueryCountMode.IfAvailable,
                QueryCountMode.Exact,
                QueryCountMode.Estimated,
                QueryCountMode.Limited
            ]
        },
        Select = new SelectCapability { PayloadFields = true },
        Include = new QueryIncludeCapability
        {
            Supported = true,
            IncludeFilters = true,
            IncludeSort = true,
            IncludeLimit = true
        }
    };

    private sealed class Session(
        FakeRecordStore owner,
        Dictionary<string, RecordEnvelope> records) : IAtomicRecordSession
    {
        private bool _active = true;
        private long _purgeGeneration;

        public void Close() => _active = false;

        public ValueTask<OperationResult<RecordEnvelope>> GetAsync(
            CollectionDefinition collection,
            RecordId id,
            OperationContext context,
            CancellationToken cancellationToken = default)
        {
            EnsureActive();
            owner.GetCalls++;
            return ValueTask.FromResult(Get(records, id));
        }

        public ValueTask<OperationResult<RecordMutationSessionResult>> CreateAsync(
            CollectionDefinition collection,
            RecordCreateRequest request,
            RecordMutationSessionContext context,
            CancellationToken cancellationToken = default)
        {
            EnsureActive();
            owner.CreateCalls++;
            owner.LastCreateRequest = request;
            var id = request.RequestedId ?? new RecordId($"rec_{owner.CreateCalls}");
            if (records.ContainsKey(id.Value))
                return ValueTask.FromResult(Conflict());
            var record = Envelope(collection, id, request.Payload);
            records[id.Value] = record;
            return ValueTask.FromResult(SessionResult(
                OperationStatus.Created,
                context,
                collection,
                BaseCommittedRecordMutationKind.Create,
                null,
                record,
                null));
        }

        public ValueTask<OperationResult<RecordMutationSessionResult>> PatchAsync(
            CollectionDefinition collection,
            RecordId id,
            RecordPatchRequest request,
            RecordMutationSessionContext context,
            CancellationToken cancellationToken = default)
        {
            EnsureActive();
            owner.PatchCalls++;
            owner.LastPatchRequest = request;
            if (!records.TryGetValue(id.Value, out var before))
                return ValueTask.FromResult(NotFound());
            if (!RevisionMatches(before, request.ExpectedRevision))
                return ValueTask.FromResult(RevisionConflict());
            var payload = Merge(before.Payload, request.Patch);
            var after = Envelope(collection, id, payload);
            records[id.Value] = after;
            return ValueTask.FromResult(SessionResult(
                OperationStatus.Updated,
                context,
                collection,
                BaseCommittedRecordMutationKind.Patch,
                before,
                after,
                request.Patch.Fields?.Keys.ToArray()));
        }

        public ValueTask<OperationResult<RecordMutationSessionResult>> ReplaceAsync(
            CollectionDefinition collection,
            RecordId id,
            RecordReplaceRequest request,
            RecordMutationSessionContext context,
            CancellationToken cancellationToken = default)
        {
            EnsureActive();
            owner.ReplaceCalls++;
            owner.LastReplaceRequest = request;
            if (!records.TryGetValue(id.Value, out var before))
                return ValueTask.FromResult(NotFound());
            if (!RevisionMatches(before, request.ExpectedRevision))
                return ValueTask.FromResult(RevisionConflict());
            var after = Envelope(collection, id, request.Payload);
            records[id.Value] = after;
            return ValueTask.FromResult(SessionResult(
                OperationStatus.Updated,
                context,
                collection,
                BaseCommittedRecordMutationKind.Replace,
                before,
                after,
                null));
        }

        public ValueTask<OperationResult<RecordMutationSessionResult>> DeleteAsync(
            CollectionDefinition collection,
            RecordId id,
            RecordDeleteRequest request,
            RecordMutationSessionContext context,
            CancellationToken cancellationToken = default)
        {
            EnsureActive();
            owner.DeleteCalls++;
            if (!records.Remove(id.Value, out var before))
                return ValueTask.FromResult(NotFound());
            if (!RevisionMatches(before, request.ExpectedRevision))
            {
                records[id.Value] = before;
                return ValueTask.FromResult(RevisionConflict());
            }
            var delete = new DeleteResult
            {
                Id = id,
                Deleted = true,
                Previous = request.ReturnPrevious ? before : null
            };
            var result = SessionResult(
                OperationStatus.Deleted,
                context,
                collection,
                BaseCommittedRecordMutationKind.Delete,
                before,
                null,
                null);
            return ValueTask.FromResult(result with
            {
                Value = result.Value! with
                {
                    Delete = delete,
                    Mutation = result.Value!.Mutation with { Delete = delete }
                }
            });
        }

        public ValueTask<OperationResult<long>> AdvancePurgeGenerationAsync(
            CollectionDefinition collection,
            long? expectedGeneration,
            CancellationToken cancellationToken = default)
        {
            EnsureActive();
            if (expectedGeneration is { } expected && expected != _purgeGeneration)
                return ValueTask.FromResult(OperationResults.Conflict<long>(new BaseError
                {
                    Code = BaseCollectionErrorCodes.PurgeGenerationConflict,
                    Message = "The purge generation did not match.",
                    Category = ErrorCategory.Conflict
                }));
            return ValueTask.FromResult(OperationResults.Ok(++_purgeGeneration));
        }

        public ValueTask<OperationResult> ApplyMutationProjectionsAsync(
            BaseAtomicMutationProjectionRequest request,
            CancellationToken cancellationToken = default)
        {
            EnsureActive();
            cancellationToken.ThrowIfCancellationRequested();
            owner.LastProjectionRequest = request;
            return ValueTask.FromResult(OperationResults.NoContent());
        }

        private OperationResult<RecordMutationSessionResult> SessionResult(
            OperationStatus status,
            RecordMutationSessionContext context,
            CollectionDefinition collection,
            BaseCommittedRecordMutationKind committed,
            RecordEnvelope? before,
            RecordEnvelope? after,
            string[]? changedFields)
        {
            owner.MutationContexts.Add(context.Operation);
            var fact = new BaseRecordMutationFact
            {
                ItemId = context.ItemId,
                RequestedOperation = context.RequestedOperation,
                CommittedOperation = committed,
                UpsertOutcome = context.RequestedOperation == BaseRecordMutationKind.Upsert
                    ? committed == BaseCommittedRecordMutationKind.Create
                        ? RecordUpsertOutcome.Created
                        : RecordUpsertOutcome.Updated
                    : null,
                Collection = collection,
                Event = new EventReference
                {
                    EventId = context.EventId,
                    Type = committed switch
                    {
                        BaseCommittedRecordMutationKind.Create => BaseEventTypes.RecordCreated,
                        BaseCommittedRecordMutationKind.Patch => BaseEventTypes.RecordPatched,
                        BaseCommittedRecordMutationKind.Replace => BaseEventTypes.RecordUpdated,
                        BaseCommittedRecordMutationKind.Delete => BaseEventTypes.RecordDeleted,
                        _ => throw new InvalidOperationException("Unsupported committed mutation kind.")
                    },
                    Guarantee = EventDeliveryGuarantee.BestEffort
                },
                Before = before,
                After = after,
                ChangedFields = changedFields
            };
            fact = owner.MutationFactTransform?.Invoke(fact) ?? fact;
            return new OperationResult<RecordMutationSessionResult>
            {
                Status = status,
                Value = new RecordMutationSessionResult
                {
                    Record = after,
                    Mutation = fact
                }
            };
        }

        private static OperationResult<RecordMutationSessionResult> NotFound() => new()
        {
            Status = OperationStatus.NotFound,
            Error = new BaseError
            {
                Code = "base.runtime.record.notFound",
                Message = "Record was not found.",
                Category = ErrorCategory.NotFound
            }
        };

        private static OperationResult<RecordMutationSessionResult> Conflict() => new()
        {
            Status = OperationStatus.Conflict,
            Error = new BaseError
            {
                Code = "base.runtime.record.conflict",
                Message = "Record already exists.",
                Category = ErrorCategory.Conflict
            }
        };

        private static OperationResult<RecordMutationSessionResult> RevisionConflict() => new()
        {
            Status = OperationStatus.Conflict,
            Error = new BaseError
            {
                Code = BaseMutationErrorCodes.RevisionConflict,
                Message = "The expected revision did not match.",
                Category = ErrorCategory.Conflict
            }
        };

        private static bool RevisionMatches(
            RecordEnvelope record,
            RevisionToken? expected) =>
            expected is null || record.Metadata.Revision?.Value == expected.Value.Value;

        private static RecordEnvelope Envelope(
            CollectionDefinition collection,
            RecordId id,
            RecordPayload payload) => new()
        {
            CollectionId = collection.Id,
            Id = id,
            Payload = payload,
            Metadata = new RecordMetadata { Revision = new RevisionToken("1") }
        };

        private static RecordPayload Merge(RecordPayload existing, RecordPayload patch)
        {
            if (existing.Kind != RecordPayloadKind.FieldMap
                || patch.Kind != RecordPayloadKind.FieldMap)
            {
                return patch;
            }

            var fields = new Dictionary<string, System.Text.Json.JsonElement>(
                existing.Fields ?? [],
                StringComparer.Ordinal);
            foreach (var pair in patch.Fields ?? [])
                fields[pair.Key] = pair.Value;
            return new RecordPayload { Kind = RecordPayloadKind.FieldMap, Fields = fields };
        }

        private void EnsureActive()
        {
            if (!_active)
                throw new InvalidOperationException("The mutation session is no longer active.");
        }
    }
}
