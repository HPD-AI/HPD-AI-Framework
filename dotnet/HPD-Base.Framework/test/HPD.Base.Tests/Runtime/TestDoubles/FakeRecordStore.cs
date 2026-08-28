using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HPD.Base;

namespace HPD.Base.Tests;

internal class FakeRecordStore : IAtomicRecordStore
{
    public BaseActivationGuard? LastCapturedActivationGuard { get; private set; }
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
        TimeSpan? minimumTimeout = null,
        bool includeAtomicRequestCapability = false)
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
            },
            AtomicRequest = includeAtomicRequestCapability ? new AtomicRequestCapability
            {
                Supported = true, Durability = BaseAtomicRequestDurability.ProcessLocal,
                DuplicateResultReplay = true, FingerprintConflictDetection = true,
                IndeterminateResolution = true, MaxIdentityBytes = 4_096,
                MaxReceiptBytes = 1_048_576, MinReceiptLifetime = TimeSpan.FromSeconds(1),
                MaxReceiptLifetime = TimeSpan.FromDays(365),
            } : null,
            ModuleMutation = new BaseModuleMutationCapability
            {
                Supported = true,
                SerializableExecution = true,
                DurableReceipts = true,
                GenerationCells = true,
                AtomicRecordAndGenerationCommit = true,
                MaximumRemovedFieldsPerMutation = 256,
                MaximumLimits = BaseModuleMutationPlatform.MaximumLimits,
            },
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
    public Func<BaseCapturedAtomicExecution, BaseCapturedAtomicExecution>? AtomicCaptureTransform { get; set; }
    public Func<BaseProvisionalAtomicExecution, BaseProvisionalAtomicExecution>? AtomicProvisionalTransform { get; set; }
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
            LogicalSchemaChecksum = BaseSchemaAuthorityChecksum.Create(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(HPDBaseStoreInstallationContext.ComputeSchemaDigest(collections)))),
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
        public ValueTask<OperationResult<BaseCapturedActivationGuardEvidence>> ValidateActivationGuardAsync(
            BaseActivationGuard guard,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(OperationResults.Unsupported<BaseCapturedActivationGuardEvidence>(new BaseError
            {
                Code = "base.activation.capabilityUnavailable",
                Message = "Activation guards are not supported by this test store.",
                Category = ErrorCategory.Unsupported,
            }));
        private bool _active = true;
        private long _purgeGeneration;
        private BaseCapturedAtomicExecution? _captured;
        private BaseFinalizedAtomicExecutionPlan? _plan;
        private BasePreparedAtomicExecution? _prepared;

        public void Close() => _active = false;

        public ValueTask<OperationResult<BaseTransactionalActivationCommitEvidence>> FinalizeActivationAsync(
            BaseTransactionalActivationFinalization finalization,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(OperationResults.Unsupported<BaseTransactionalActivationCommitEvidence>(new BaseError
            {
                Code = "base.activation.capabilityUnavailable",
                Message = "The fake record provider does not support activation terminalization.",
                Category = ErrorCategory.Unsupported,
            }));

        public ValueTask<OperationResult<BaseCapturedAtomicExecution>> CaptureAtomicExecutionAsync(
            BaseAtomicExecutionRequest request,
            CancellationToken cancellationToken = default)
        {
            EnsureActive();
            cancellationToken.ThrowIfCancellationRequested();
            owner.LastCapturedActivationGuard = request.ActivationGuard;
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
            var moduleRecords = ImmutableArray.CreateBuilder<BaseCapturedModuleRecord>(request.Module?.Records.Length ?? 0);
            var moduleGenerations = ImmutableArray.CreateBuilder<BaseCapturedModuleGeneration>(
                request.Module?.Generations.Length ?? 0);
            long generationBytes = 0;
            if (request.Module is { } module)
            {
                foreach (BaseModuleRecordCaptureRequest capture in module.Records)
                {
                    overlay.TryGetValue(capture.RecordId.Value, out RecordEnvelope? current);
                    if (capture.Presence == BaseModuleCapturePresence.RequirePresent && current is null
                        || capture.Presence == BaseModuleCapturePresence.RequireMissing && current is not null)
                        return ValueTask.FromResult(OperationResults.StoreError<BaseCapturedAtomicExecution>(new BaseError
                        {
                            Code = "base.moduleMutation.captureEvidenceInvalid",
                            Message = "The requested module capture presence did not match.",
                            Category = ErrorCategory.Store,
                        }));
                    intervalBuilder.Add(Interval($"collection:{capture.Collection.Id}:record", capture.RecordId));
                    if (current is not null)
                    {
                        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(
                            current, HPDBaseJsonSerializerContext.Default.RecordEnvelope);
                        digest.AppendData(bytes);
                        selected = checked(selected + bytes.LongLength);
                    }
                    moduleRecords.Add(new BaseCapturedModuleRecord
                    {
                        Ordinal = capture.Ordinal,
                        CaptureId = capture.CaptureId,
                        CollectionId = capture.Collection.Id,
                        RecordId = capture.RecordId,
                        Exists = current is not null,
                        Current = current is null ? null : RecordCloneHelpers.CloneEnvelope(current),
                    });
                }
                foreach (BaseModuleGenerationCaptureRequest capture in module.Generations)
                {
                    byte[] key = Encoding.UTF8.GetBytes(string.Join('\0',
                        capture.Cell.Id, capture.Cell.Version.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        ((int)capture.Scope.Kind).ToString(System.Globalization.CultureInfo.InvariantCulture),
                        capture.Scope.Tenant ?? string.Empty, capture.Scope.Project ?? string.Empty,
                        Convert.ToHexStringLower(capture.KeyUtf8.AsSpan())));
                    intervalBuilder.Add(new BaseAtomicReadIntervalEvidence
                    {
                        LogicalAccessPathId = "module-generation",
                        CanonicalLowerBound = key.ToImmutableArray(), LowerInclusive = true,
                        CanonicalUpperBound = key.ToImmutableArray(), UpperInclusive = true,
                    });
                    digest.AppendData(key);
                    generationBytes = checked(generationBytes + key.LongLength + 1);
                    moduleGenerations.Add(new BaseCapturedModuleGeneration
                    {
                        Ordinal = capture.Ordinal, CaptureId = capture.CaptureId,
                        CellId = capture.Cell.Id, CellVersion = capture.Cell.Version,
                        CanonicalKeyDigest = Convert.ToHexStringLower(SHA256.HashData(key)),
                        Exists = false, Generation = null,
                    });
                }
            }
            ImmutableArray<BaseAtomicReadIntervalEvidence> intervals = intervalBuilder.ToImmutable();
            long evidence = BaseSubjectCanonicalRetainedWork.MeasureIntervals(intervals);
            _captured = new BaseCapturedAtomicExecution
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
                    LogicalSchemaChecksum = intent.Authority.LogicalSchemaChecksum,
                    Collections = intent.Authority.Collections,
                    Isolation = BaseAtomicSelectionIsolationClass.WriteOwningSerializable,
                    TransactionEvidenceToken = [1],
                },
                Items = items,
                ModuleRecords = moduleRecords.ToImmutable(), ModuleRelationTargets = [],
                Generations = moduleGenerations.ToImmutable(),
                ReadIntervals = intervals,
                Accounting = new BaseAtomicCaptureAccounting
                {
                    Records = checked(items.Length + items.Sum(static item => item.RelationTargets.Length)
                        + moduleRecords.Count),
                    RelationTargetReads = items.Sum(static item => item.RelationTargets.Length),
                    GenerationReads = moduleGenerations.Count,
                    SelectedBytes = selected,
                    RelationTargetBytes = 0, GenerationBytes = generationBytes,
                    ReadIntervals = intervals.Length,
                    EvidenceBytes = evidence,
                    TransientBytes = selected + evidence + generationBytes,
                    RetirementBarrierReads=0,RetirementAcknowledgementReads=0,RetirementProjections=0,RetirementPublications=0,RetirementEvidenceBytes=0,RetirementPublicationBytes=0,
                },
            };
            _captured = owner.AtomicCaptureTransform?.Invoke(_captured) ?? _captured;
            if ((request.Schema is null) != (request.Limits.Schema is null))
                return ValueTask.FromResult(OperationResults.StoreError<BaseCapturedAtomicExecution>(new BaseError { Code = BaseSchemaErrorCodes.ProviderEvidenceInvalid, Message = "Invalid schema capture request.", Category = ErrorCategory.Store }));
            if (request.Schema is not null)
            {
                CollectionDefinition[] collections = intent.Items.Select(static item => item.Collection)
                    .Concat(intent.Items.SelectMany(static item => item.RelationTargets).Select(static relation => relation.TargetCollection))
                    .DistinctBy(static collection => collection.Id).ToArray();
                _captured = _captured with { Schema = BaseAtomicSchemaContract.Capture(request.Schema, _captured.Authority, collections, items) };
            }
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

        public ValueTask<OperationResult<BasePreparedAtomicExecution>> PrepareAtomicExecutionAsync(
            BaseCapturedAtomicExecution captured,
            BaseFinalizedAtomicExecutionPlan plan,
            CancellationToken cancellationToken = default)
        {
            EnsureActive();
            cancellationToken.ThrowIfCancellationRequested();
            if (!ReferenceEquals(captured, _captured) || _prepared is not null)
                return ValueTask.FromResult(OperationResults.StoreError<BasePreparedAtomicExecution>(new BaseError { Code = BaseSubjectErrorCodes.ProviderContractInvalid, Message = "Invalid preparation.", Category = ErrorCategory.Store }));
            _plan = plan;
            BaseAtomicSchemaPreparedExtension? preparedSchema;
            try { preparedSchema = BaseAtomicSchemaContract.Prepare(this, captured.Schema, plan.Schema, plan.Items); }
            catch (InvalidOperationException exception) { return ValueTask.FromResult(OperationResults.StoreError<BasePreparedAtomicExecution>(new BaseError { Code = exception.Message, Message = "Invalid schema preparation.", Category = ErrorCategory.Store })); }
            _prepared = new BasePreparedAtomicExecution
            {
                Kind = plan.Kind,
                PlanDigest = plan.PlanDigest,
                Authority = captured.Authority,
                SubjectAuthorities = [],
                Dispositions = plan.Items.Select(static item => item.Kind switch
                {
                    BaseCommittedRecordMutationKind.Create => BaseCapturedMutationDisposition.Create,
                    BaseCommittedRecordMutationKind.Delete => BaseCapturedMutationDisposition.Delete,
                    _ => BaseCapturedMutationDisposition.Update,
                }).ToImmutableArray(),
                Generations = [],
                SubjectOverlay = [],
                SubjectValidations = [],
                Schema = preparedSchema,
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
                    RetirementBarrierReads=0,RetirementAcknowledgementReads=0,RetirementProjections=0,RetirementPublications=0,RetirementEvidenceBytes=0,RetirementPublicationBytes=0,
                },
            };
            return ValueTask.FromResult(OperationResults.Ok(_prepared));
        }

        public async ValueTask<OperationResult<BaseProvisionalAtomicExecution>> ApplyPreparedAtomicExecutionAsync(
            BasePreparedAtomicExecution prepared,
            CancellationToken cancellationToken = default)
        {
            EnsureActive();
            if (!ReferenceEquals(prepared, _prepared) || _plan is null)
                return OperationResults.StoreError<BaseProvisionalAtomicExecution>(new BaseError { Code = BaseSubjectErrorCodes.ProviderContractInvalid, Message = "Invalid apply.", Category = ErrorCategory.Store });
            BaseAtomicSchemaProvisionalExtension? provisionalSchema;
            try { provisionalSchema = BaseAtomicSchemaContract.Apply(this, prepared.Schema, _plan.Schema); }
            catch (InvalidOperationException exception) { return OperationResults.StoreError<BaseProvisionalAtomicExecution>(new BaseError { Code = exception.Message, Message = "Invalid schema application.", Category = ErrorCategory.Store }); }
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
                    BaseCommittedRecordMutationKind.Patch => await PatchAsync(item.Collection, item.RecordId, new RecordPatchRequest { Patch = PatchDelta(item), RemovedFieldIds = item.RemovedFieldIds, ExpectedRevision = item.Current?.Metadata.Revision }, context, cancellationToken),
                    BaseCommittedRecordMutationKind.Replace => await ReplaceAsync(item.Collection, item.RecordId, new RecordReplaceRequest { Payload = item.ProposedPayload!, ExpectedRevision = item.Current?.Metadata.Revision }, context, cancellationToken),
                    BaseCommittedRecordMutationKind.Delete => await DeleteAsync(item.Collection, item.RecordId, item.Delete!, context, cancellationToken),
                    _ => throw new InvalidOperationException(),
                };
                if (!result.IsSuccess() || result.Value is null)
                    return new OperationResult<BaseProvisionalAtomicExecution> { Status = result.Status, Error = result.Error };
                BaseRecordMutationFact mutation = result.Value.Mutation;
                BaseRecordMutationFact journaled = mutation with
                {
                    Event = mutation.Event with
                    {
                        PublishedAt = item.Operation.Now,
                        Stream = "base.mutations",
                        Guarantee = EventDeliveryGuarantee.Transactional,
                    },
                    JournalPosition = new BaseMutationJournalPosition(checked(item.Ordinal + 1L)),
                };
                facts.Add(BaseOwnedMutationFact.Freeze(journaled, 1));
            }
            BaseRecordMutationFact[] materialized = facts.Select(static fact => fact.MaterializeOwned()).ToArray();
            await ApplyMutationProjectionsAsync(BaseAtomicMutationProjectionFactory.Create(materialized), cancellationToken);
            long factBytes = facts.Sum(static fact => (long)fact.EncodedLength);
            long journalBytes = materialized.Sum(static fact => (long)JsonSerializer.SerializeToUtf8Bytes(
                fact, HPDBaseJsonSerializerContext.Default.BaseRecordMutationFact).LongLength);
            long writtenBytes = materialized.Sum(static fact => fact.After is null
                ? Encoding.UTF8.GetByteCount(fact.Before!.Id.Value) + sizeof(long)
                : (long)JsonSerializer.SerializeToUtf8Bytes(
                    fact.After, HPDBaseJsonSerializerContext.Default.RecordEnvelope).LongLength);
            var provisional = new BaseProvisionalAtomicExecution
            {
                Kind = _plan.Kind,
                PlanDigest = _plan.PlanDigest,
                Authority = prepared.Authority,
                Facts = facts.MoveToImmutable(),
                Generations = [],
                Schema = provisionalSchema,
                Accounting = new BaseProvisionalAtomicMutationAccounting
                {
                    WrittenBytes = writtenBytes,
                    GenerationBytes = prepared.Accounting.GenerationBytes,
                    FactBytes = factBytes,
                    JournalBytes = journalBytes,
                    RelationChecks = 0,
                    UniqueConstraintChecks = 0,
                    AuthorityReads = prepared.Accounting.AuthorityReads,
                    ReadIntervals = prepared.ReadIntervals.Length,
                    SelectedBytes = prepared.Accounting.SelectedBytes,
                    EvidenceBytes = prepared.Accounting.EvidenceBytes,
                    TransientBytes = checked(prepared.Accounting.TransientBytes
                        + writtenBytes + factBytes + journalBytes),
                    RetirementBarrierReads=0,RetirementAcknowledgementReads=0,RetirementProjections=0,RetirementPublications=0,RetirementEvidenceBytes=0,RetirementPublicationBytes=0,
                },
            };
            return OperationResults.Ok(owner.AtomicProvisionalTransform?.Invoke(provisional) ?? provisional);
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
            var id = request.RequestedId ?? RecordId.Create($"rec_{owner.CreateCalls}");
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
                context.ChangedFields));
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
                context.ChangedFields));
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
                context.ChangedFields));
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
                context.ChangedFields);
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

        public ValueTask<OperationResult<BaseSubjectAcknowledgementResult>> ApplySubjectRetirementAcknowledgementAsync(
            BaseSubjectRetirementProviderAcknowledgementRequest request,
            CancellationToken cancellationToken = default)
        {
            EnsureActive();
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(new OperationResult<BaseSubjectAcknowledgementResult>
            {
                Status = OperationStatus.CapabilityUnavailable,
                Error = new BaseError
                {
                    Code = BaseSubjectRetirementErrorCodes.ProviderContractInvalid,
                    Message = "Subject retirement is not supported by this test store.",
                    Category = ErrorCategory.Capability,
                },
            });
        }

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
