using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HPD.Base;

namespace HPD.Base.Tests;

internal class FakeRecordStore : IAtomicRecordStore
{
    public virtual ValueTask<RecordMutationExecutionResult> ResolveAtomicReceiptAsync(
        IAtomicMutationProcessor processor,
        BaseMutationRequestIdentity identity,
        TimeSpan resolutionTimeout,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new RecordMutationExecutionResult(
            RecordMutationExecutionOutcome.RollbackConfirmed,
            processing: null,
            new BaseError
            {
                Code = BaseMutationRequestErrorCodes.ReceiptUnavailable,
                Message = "The stored mutation receipt cannot be resolved.",
                Category = ErrorCategory.Authorization,
            }));

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

    public ValueTask<OperationResult<BaseAtomicMutationAuthorityRequirement>> CaptureAtomicMutationAuthorityRequirementAsync(
        string applicationId,
        ImmutableArray<CollectionDefinition> collections,
        BaseAtomicMutationExecutionLimits limits,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(OperationResults.Ok(new BaseAtomicMutationAuthorityRequirement
        {
            ApplicationId = applicationId,
            StoreInstanceId = Capabilities.StoreId,
            RestoreEpoch = 0,
            SchemaGeneration = 1,
            Collections = collections.OrderBy(static value => value.Id, StringComparer.Ordinal)
                .Select(static value => new BaseCollectionGenerationRequirement
                {
                    CollectionId = value.Id,
                    CollectionGeneration = 1,
                }).ToImmutableArray(),
        }));
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
        private BaseCapturedAtomicMutationAuthority? _captured;
        private BaseAtomicMutationPlan? _plan;
        private BasePreparedAtomicMutation? _prepared;

        public void Close() => _active = false;

        public ValueTask<OperationResult<BaseCapturedAtomicMutationAuthority>> CaptureAtomicMutationAuthorityAsync(
            BaseAtomicMutationCaptureRequest request,
            CancellationToken cancellationToken = default)
        {
            EnsureActive();
            cancellationToken.ThrowIfCancellationRequested();
            BaseAtomicMutationIntent intent = request.Intent;
            owner.GetCalls = checked(owner.GetCalls + intent.Items.Count(static item =>
                item.RequestedKind is BaseRecordMutationKind.Patch or BaseRecordMutationKind.Replace or
                    BaseRecordMutationKind.Delete or BaseRecordMutationKind.Upsert));
            var overlay = records.ToDictionary(
                static pair => pair.Key,
                static pair => (RecordEnvelope?)RecordCloneHelpers.CloneEnvelope(pair.Value),
                StringComparer.Ordinal);
            var itemBuilder = ImmutableArray.CreateBuilder<BaseCapturedMutationItem>(intent.Items.Length);
            foreach (BaseAtomicMutationIntentItem item in intent.Items)
            {
                overlay.TryGetValue(item.RecordId.Value, out RecordEnvelope? current);
                itemBuilder.Add(new BaseCapturedMutationItem
                {
                    Ordinal = item.Ordinal, CollectionId = item.Collection.Id, RecordId = item.RecordId,
                    RuntimeAssignedRecordId = item.RuntimeAssignedRecordId,
                    Disposition = item.RequestedKind switch
                    {
                        BaseRecordMutationKind.Create when current is null => BaseCapturedMutationDisposition.Create,
                        BaseRecordMutationKind.Create => BaseCapturedMutationDisposition.Update,
                        BaseRecordMutationKind.Delete => BaseCapturedMutationDisposition.Delete,
                        BaseRecordMutationKind.Upsert when current is null => BaseCapturedMutationDisposition.Create,
                        _ => BaseCapturedMutationDisposition.Update,
                    },
                    Current = current is null ? null : RecordCloneHelpers.CloneEnvelope(current),
                    RelationTargets = item.RelationTargets.Select(relation => new BaseCapturedRelationTarget
                    {
                        SourceFieldId = relation.SourceFieldId, TargetCollectionId = relation.TargetCollection.Id,
                        TargetRecordId = relation.TargetRecordId,
                        Current = overlay.TryGetValue(relation.TargetRecordId.Value, out RecordEnvelope? target) && target is not null
                            ? RecordCloneHelpers.CloneEnvelope(target) : null,
                    }).ToImmutableArray(),
                });
                RecordPayload? next = item.RequestedKind switch
                {
                    BaseRecordMutationKind.Create => item.Create!.Payload,
                    BaseRecordMutationKind.Patch when current is not null => BasePolicyRuntimeSimulation.MergePatchPayload(current.Payload, item.Patch!.Patch),
                    BaseRecordMutationKind.Replace => item.Replace!.Payload,
                    BaseRecordMutationKind.Upsert when current is null => item.Upsert!.CreatePayload,
                    BaseRecordMutationKind.Upsert when item.Upsert!.UpdateMode == RecordUpsertUpdateMode.Patch => BasePolicyRuntimeSimulation.MergePatchPayload(current!.Payload, item.Upsert.UpdatePayload),
                    BaseRecordMutationKind.Upsert => item.Upsert!.UpdatePayload,
                    _ => null,
                };
                overlay[item.RecordId.Value] = item.RequestedKind == BaseRecordMutationKind.Delete ? null : new RecordEnvelope
                {
                    CollectionId = item.Collection.Id, Id = item.RecordId,
                    Payload = RecordCloneHelpers.ClonePayload(next!), Metadata = current?.Metadata ?? new RecordMetadata(),
                };
            }
            ImmutableArray<BaseCapturedMutationItem> items = itemBuilder.MoveToImmutable();
            var intervalBuilder = ImmutableArray.CreateBuilder<BaseAtomicReadIntervalEvidence>();
            using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            digest.AppendData(Encoding.UTF8.GetBytes(intent.IntentDigest));
            long selected = 0;
            foreach (BaseCapturedMutationItem item in items)
            {
                if (item.Current is not null)
                {
                    byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(item.Current, HPDBaseJsonSerializerContext.Default.RecordEnvelope);
                    digest.AppendData(bytes); selected += bytes.LongLength;
                }
                byte[] recordKey = Encoding.UTF8.GetBytes(item.RecordId.Value);
                digest.AppendData(recordKey);
                foreach (BaseCapturedRelationTarget relation in item.RelationTargets)
                {
                    if (relation.Current is not null)
                    {
                        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(relation.Current, HPDBaseJsonSerializerContext.Default.RecordEnvelope);
                        digest.AppendData(bytes); selected += bytes.LongLength;
                    }
                    intervalBuilder.Add(Interval($"collection:{relation.TargetCollectionId}:record", relation.TargetRecordId));
                }
                intervalBuilder.Add(Interval($"collection:{item.CollectionId}:record", item.RecordId));
            }
            ImmutableArray<BaseAtomicReadIntervalEvidence> intervals = intervalBuilder.ToImmutable();
            long evidence = BaseSubjectCanonicalRetainedWork.MeasureIntervals(intervals);
            _captured = new BaseCapturedAtomicMutationAuthority
            {
                Kind = request.Kind,
                IntentDigest = intent.IntentDigest,
                CaptureDigest = Convert.ToHexStringLower(digest.GetHashAndReset()),
                Authority = new BaseAtomicMutationAuthorityEvidence
                {
                    ApplicationId = intent.Authority.ApplicationId,
                    StoreInstanceId = owner.Capabilities.StoreId,
                    RestoreEpoch = intent.Authority.RestoreEpoch,
                    SchemaGeneration = 1,
                    Collections = intent.Authority.Collections,
                    Isolation = BaseAtomicSelectionIsolationClass.WriteOwningSerializable,
                    TransactionEvidenceToken = [1],
                },
                Items = items,
                ModuleRecords = [], ModuleRelationTargets = [], Generations = [],
                ReadIntervals = intervals,
                Accounting = new BaseAtomicCaptureAccounting
                {
                    Records = checked(items.Length + items.Sum(static item => item.RelationTargets.Length)),
                    RelationTargetReads = items.Sum(static item => item.RelationTargets.Length), GenerationReads = 0,
                    SelectedBytes = selected,
                    RelationTargetBytes = 0, GenerationBytes = 0,
                    ReadIntervals = intervals.Length,
                    EvidenceBytes = evidence,
                    TransientBytes = selected + evidence,
                },
            };
            return ValueTask.FromResult(OperationResults.Ok(_captured));

            static BaseAtomicReadIntervalEvidence Interval(string path, RecordId id)
            {
                ImmutableArray<byte> key = Encoding.UTF8.GetBytes(id.Value).ToImmutableArray();
                return new BaseAtomicReadIntervalEvidence
                {
                    LogicalAccessPathId = path, CanonicalLowerBound = key, LowerInclusive = true,
                    CanonicalUpperBound = key, UpperInclusive = true,
                };
            }
        }

        public ValueTask<OperationResult<BasePreparedAtomicMutation>> PrepareAtomicMutationAsync(
            BaseCapturedAtomicMutationAuthority captured,
            BaseAtomicMutationPlan plan,
            CancellationToken cancellationToken = default)
        {
            EnsureActive();
            cancellationToken.ThrowIfCancellationRequested();
            if (!ReferenceEquals(captured, _captured) || _prepared is not null)
                return ValueTask.FromResult(OperationResults.StoreError<BasePreparedAtomicMutation>(new BaseError { Code = BaseSubjectErrorCodes.ProviderContractInvalid, Message = "Invalid preparation.", Category = ErrorCategory.Store }));
            _plan = plan;
            _prepared = new BasePreparedAtomicMutation
            {
                Kind = plan.Kind,
                PlanDigest = plan.PlanDigest,
                Authority = captured.Authority,
                SubjectAuthorities = [],
                Dispositions = captured.Items.Select(static item => item.Disposition).ToImmutableArray(),
                Generations = [],
                SubjectOverlay = [],
                SubjectValidations = [],
                ReadIntervals = captured.ReadIntervals,
                Accounting = new BasePreparedAtomicMutationAccounting
                {
                    AuthorityReads = captured.Items.Length,
                    ReadIntervals = captured.ReadIntervals.Length,
                    GenerationReads = 0,
                    GenerationComparisons = 0,
                    GenerationIncrements = 0,
                    SelectedBytes = captured.Accounting.SelectedBytes,
                    GenerationBytes = 0,
                    EvidenceBytes = captured.Accounting.EvidenceBytes,
                    TransientBytes = captured.Accounting.TransientBytes,
                },
            };
            return ValueTask.FromResult(OperationResults.Ok(_prepared));
        }

        public async ValueTask<OperationResult<BaseProvisionalAppliedAtomicMutation>> ApplyPreparedAtomicMutationAsync(
            BasePreparedAtomicMutation prepared,
            CancellationToken cancellationToken = default)
        {
            EnsureActive();
            if (!ReferenceEquals(prepared, _prepared) || _plan is null)
                return OperationResults.StoreError<BaseProvisionalAppliedAtomicMutation>(new BaseError { Code = BaseSubjectErrorCodes.ProviderContractInvalid, Message = "Invalid apply.", Category = ErrorCategory.Store });
            _prepared = null;
            var facts = ImmutableArray.CreateBuilder<BaseOwnedMutationFact>(_plan.Items.Length);
            foreach (BaseAtomicMutationPlanItem item in _plan.Items)
            {
                RecordMutationSessionContext context = new()
                {
                    ItemId = item.ItemId,
                    RequestedOperation = item.RequestedKind,
                    EventId = item.EventId,
                    Operation = item.Operation,
                    ChangedFields = item.ChangedFields.ToArray(),
                };
                OperationResult<RecordMutationSessionResult> result = item.Kind switch
                {
                    BaseCommittedRecordMutationKind.Create => await CreateAsync(item.Collection, new RecordCreateRequest { RequestedId = item.RecordId, Payload = item.ProposedPayload! }, context, cancellationToken),
                    BaseCommittedRecordMutationKind.Patch => await PatchAsync(item.Collection, item.RecordId, new RecordPatchRequest { Patch = PatchDelta(item), ExpectedRevision = item.Current?.Metadata.Revision }, context, cancellationToken),
                    BaseCommittedRecordMutationKind.Replace => await ReplaceAsync(item.Collection, item.RecordId, new RecordReplaceRequest { Payload = item.ProposedPayload!, ExpectedRevision = item.Current?.Metadata.Revision }, context, cancellationToken),
                    BaseCommittedRecordMutationKind.Delete => await DeleteAsync(item.Collection, item.RecordId, item.Delete!, context, cancellationToken),
                    _ => throw new InvalidOperationException(),
                };
                if (!result.IsSuccess() || result.Value is null)
                    return new OperationResult<BaseProvisionalAppliedAtomicMutation> { Status = result.Status, Error = result.Error };
                facts.Add(BaseOwnedMutationFact.Freeze(result.Value.Mutation, 1));
            }
            BaseRecordMutationFact[] materialized = facts.Select(static fact => fact.MaterializeOwned()).ToArray();
            await ApplyMutationProjectionsAsync(BaseAtomicMutationProjectionFactory.Create(materialized), cancellationToken);
            long bytes = facts.Sum(static fact => (long)fact.EncodedLength);
            return OperationResults.Ok(new BaseProvisionalAppliedAtomicMutation
            {
                Kind = _plan.Kind,
                PlanDigest = _plan.PlanDigest,
                Authority = prepared.Authority,
                Facts = facts.MoveToImmutable(),
                Generations = [],
                Accounting = new BaseProvisionalAtomicMutationAccounting
                {
                    WrittenBytes = bytes,
                    GenerationBytes = 0,
                    FactBytes = bytes,
                    JournalBytes = bytes,
                    RelationChecks = 0,
                    UniqueConstraintChecks = 0,
                    AuthorityReads = 0,
                    ReadIntervals = 0,
                    SelectedBytes = 0,
                    EvidenceBytes = 0,
                    TransientBytes = bytes * 3,
                },
            });
        }

        private static RecordPayload PatchDelta(BaseAtomicMutationPlanItem item)
        {
            var fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (string name in item.ChangedFields)
                if (item.ProposedPayload?.Fields?.TryGetValue(name, out JsonElement value) == true) fields[name] = value.Clone();
            return new RecordPayload { Kind = RecordPayloadKind.FieldMap, Fields = fields };
        }

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

        public ValueTask<OperationResult<BaseSubjectLifecycleCheckpointResult>> AdvanceSubjectLifecycleCheckpointAsync(
            BaseSubjectLifecycleProviderCheckpointRequest request,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(OperationResults.Unsupported<BaseSubjectLifecycleCheckpointResult>(new BaseError
            {
                Code = BaseSubjectErrorCodes.ProviderContractInvalid,
                Message = "This test store does not provide subject lifecycle delivery.",
                Category = ErrorCategory.Unsupported,
            }));

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
