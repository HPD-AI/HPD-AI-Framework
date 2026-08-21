
using System.Collections.Immutable;

namespace HPD.Base;

internal sealed class InMemoryStoreState
{
    /// <summary>Gets the collections.</summary>
    public Dictionary<string, InMemoryCollectionState> Collections { get; } = new(StringComparer.Ordinal);
    /// <summary>Gets or sets the next record ID.</summary>
    public long NextRecordId { get; set; }
    /// <summary>Gets or sets the next revision.</summary>
    public long NextRevision { get; set; }
    /// <summary>Gets or sets the global committed mutation position.</summary>
    public long GlobalMutationPosition { get; set; }
    /// <summary>Gets process-local atomic request receipts.</summary>
    public Dictionary<string, InMemoryMutationReceipt> Receipts { get; } = new(StringComparer.Ordinal);
    /// <summary>Gets BASE-owned immutable vector projection slots by canonical collection/index key.</summary>
    public Dictionary<string, InMemoryVectorProjectionState> VectorProjections { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, InMemoryTextProjectionState> TextProjections { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, InMemoryTextRebuildReceipt> TextRebuildReceipts { get; } = new(StringComparer.Ordinal);
    /// <summary>Gets current exported-subject contract authority by canonical contract key.</summary>
    public Dictionary<string, InMemorySubjectContractState> SubjectContracts { get; } = new(StringComparer.Ordinal);
    /// <summary>Gets current exported-subject lifetimes by canonical subject key.</summary>
    public Dictionary<string, InMemorySubjectLifetimeState> SubjectLifetimes { get; } = new(StringComparer.Ordinal);
    /// <summary>Gets latest terminal lifetime evidence by logical subject key.</summary>
    public Dictionary<string, InMemorySubjectTerminalState> SubjectTerminals { get; } = new(StringComparer.Ordinal);
    /// <summary>Gets canonical durable lifecycle facts.</summary>
    public List<InMemorySubjectLifecycleFactRow> SubjectLifecycleFacts { get; } = [];
    /// <summary>Gets consumer-indexed lifecycle memberships.</summary>
    public List<InMemorySubjectLifecycleMembershipRow> SubjectLifecycleMemberships { get; } = [];
    /// <summary>Gets exact protected-scope membership seeks without cross-scope enumeration.</summary>
    public Dictionary<string, List<int>> SubjectLifecycleMembershipIndex { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, InMemorySubjectLifecycleConsumerProjection> SubjectLifecycleConsumers { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, InMemorySubjectLifecycleCheckpointState> SubjectLifecycleCheckpoints { get; } = new(StringComparer.Ordinal);
    public long SubjectLifecycleDeliveryEpoch { get; set; } = 1;
    /// <summary>Gets current coordinated-retirement barriers by protected lifetime key.</summary>
    public Dictionary<string, InMemorySubjectRetirementBarrierState> SubjectRetirementBarriers { get; } = new(StringComparer.Ordinal);
    /// <summary>Gets immutable terminal coordinated-retirement evidence.</summary>
    public Dictionary<string, InMemorySubjectRetirementTerminalState> SubjectRetirementTerminals { get; } = new(StringComparer.Ordinal);
    /// <summary>Gets the next dedicated retirement-control position.</summary>
    public long SubjectRetirementPosition { get; set; }
    /// <summary>Gets sanitized retirement control publications in provider order.</summary>
    public List<BaseSubjectRetirementPublicationRow> SubjectRetirementPublications { get; } = [];
    /// <summary>Gets module-owned generation cells by canonical scoped key.</summary>
    public Dictionary<string, long> ModuleGenerations { get; } = new(StringComparer.Ordinal);
    /// <summary>Gets durable activation rows by deterministic activation identity.</summary>
    public Dictionary<string, InMemoryActivationRow> Activations { get; } = new(StringComparer.Ordinal);
    /// <summary>Gets durable executor incarnations by application/host/process key.</summary>
    public Dictionary<string, InMemoryExecutorRow> Executors { get; } = new(StringComparer.Ordinal);
    /// <summary>Gets current durable schedule authority by ID/version.</summary>
    public Dictionary<string, BaseScheduleAuthority> Schedules { get; } = new(StringComparer.Ordinal);
    /// <summary>Gets immutable occurrence facts by occurrence identity.</summary>
    public Dictionary<string, BaseScheduleOccurrenceFact> ScheduleOccurrences { get; } = new(StringComparer.Ordinal);
    /// <summary>Gets the next positive executor generation.</summary>
    public long NextExecutorGeneration { get; set; }
    /// <summary>Gets or sets the generation invalidating finite due observations.</summary>
    public long ActivationIndexGeneration { get; set; }
    /// <summary>Gets the shared record/control mutation journal by append position.</summary>
    public SortedDictionary<long, BaseMutationJournalEntry> MutationJournal { get; } = [];

    /// <summary>Executes the clone operation.</summary>
    public InMemoryStoreState Clone()
    {
        var clone = new InMemoryStoreState
        {
            NextRecordId = NextRecordId,
            NextRevision = NextRevision,
            GlobalMutationPosition = GlobalMutationPosition,
            SubjectLifecycleDeliveryEpoch = SubjectLifecycleDeliveryEpoch,
            SubjectRetirementPosition = SubjectRetirementPosition,
            ActivationIndexGeneration = ActivationIndexGeneration,
            NextExecutorGeneration = NextExecutorGeneration,
        };

        foreach (var (id, collection) in Collections)
            clone.Collections.Add(id, collection.Clone());
        foreach (var (id, receipt) in Receipts)
            clone.Receipts.Add(id, receipt.DeepClone());
        foreach (var (id, projection) in VectorProjections)
            clone.VectorProjections.Add(id, projection);
        foreach (var (id, projection) in TextProjections)
            clone.TextProjections.Add(id, projection.Clone());
        foreach (var (id, receipt) in TextRebuildReceipts)
            clone.TextRebuildReceipts.Add(id, receipt with { Fingerprint = [.. receipt.Fingerprint], Result = receipt.Result with { PublicationChecksum = ImmutableArray.Create(receipt.Result.PublicationChecksum.ToArray()) } });
        foreach (var (id, subject) in SubjectContracts)
            clone.SubjectContracts.Add(id, subject with { });
        foreach (var (id, lifetime) in SubjectLifetimes)
            clone.SubjectLifetimes.Add(id, lifetime with { });
        foreach (var (id, terminal) in SubjectTerminals)
            clone.SubjectTerminals.Add(id, terminal with { });
        clone.SubjectLifecycleFacts.AddRange(SubjectLifecycleFacts.Select(static value => value with
        {
            Scope = value.Scope with { IndexDigest = [.. value.Scope.IndexDigest], ProtectedCanonicalValue = [.. value.Scope.ProtectedCanonicalValue] },
            Fact = value.Fact with { },
        }));
        clone.SubjectLifecycleMemberships.AddRange(SubjectLifecycleMemberships.Select(static value => value with
        {
            Scope = value.Scope with { IndexDigest = [.. value.Scope.IndexDigest], ProtectedCanonicalValue = [.. value.Scope.ProtectedCanonicalValue] },
        }));
        foreach ((string key, List<int> indexes) in SubjectLifecycleMembershipIndex)
            clone.SubjectLifecycleMembershipIndex.Add(key, [.. indexes]);
        foreach (var (id, consumer) in SubjectLifecycleConsumers) clone.SubjectLifecycleConsumers.Add(id, consumer with { Cutoff = consumer.Cutoff is null ? null : consumer.Cutoff with { } });
        foreach (var (id, checkpoint) in SubjectLifecycleCheckpoints) clone.SubjectLifecycleCheckpoints.Add(id, checkpoint with
        {
            Scope = checkpoint.Scope with { IndexDigest = [.. checkpoint.Scope.IndexDigest], ProtectedCanonicalValue = [.. checkpoint.Scope.ProtectedCanonicalValue] },
            Through = checkpoint.Through is null ? null : checkpoint.Through with { },
        });
        foreach ((string key, InMemorySubjectRetirementBarrierState barrier) in SubjectRetirementBarriers)
            clone.SubjectRetirementBarriers.Add(key, barrier.DeepClone());
        foreach ((string key, InMemorySubjectRetirementTerminalState terminal) in SubjectRetirementTerminals)
            clone.SubjectRetirementTerminals.Add(key, terminal.DeepClone());
        clone.SubjectRetirementPublications.AddRange(SubjectRetirementPublications.Select(static row => row with
        {
            Scope = row.Scope is null ? null : row.Scope with { IndexDigest = [.. row.Scope.IndexDigest], ProtectedCanonicalValue = [.. row.Scope.ProtectedCanonicalValue] },
            Fact = row.Fact with { },
        }));
        foreach ((string key, long generation) in ModuleGenerations)
            clone.ModuleGenerations.Add(key, generation);
        foreach ((string key, InMemoryActivationRow activation) in Activations)
            clone.Activations.Add(key, activation.DeepClone());
        foreach ((string key, InMemoryExecutorRow executor) in Executors)
            clone.Executors.Add(key, executor.DeepClone());
        foreach ((string key, BaseScheduleAuthority schedule) in Schedules)
            clone.Schedules.Add(key, CloneSchedule(schedule));
        foreach ((string key, BaseScheduleOccurrenceFact occurrence) in ScheduleOccurrences)
            clone.ScheduleOccurrences.Add(key, CloneOccurrence(occurrence));
        foreach ((long position, BaseMutationJournalEntry entry) in MutationJournal)
            clone.MutationJournal.Add(position, CloneJournalEntry(entry));

        return clone;
    }

    internal void RebuildSubjectLifecycleMembershipIndex()
    {
        SubjectLifecycleMembershipIndex.Clear();
        for (int index = 0; index < SubjectLifecycleMemberships.Count; index++)
        {
            InMemorySubjectLifecycleMembershipRow membership = SubjectLifecycleMemberships[index];
            string key = $"{membership.ConsumerId}\n{membership.ConsumerVersion}\n{(int)membership.Scope.Kind}\n{Convert.ToHexString(membership.Scope.IndexDigest)}";
            if (!SubjectLifecycleMembershipIndex.TryGetValue(key, out List<int>? indexes))
                SubjectLifecycleMembershipIndex.Add(key, indexes = []);
            indexes.Add(index);
        }
    }

    private static BaseMutationJournalEntry CloneJournalEntry(BaseMutationJournalEntry entry) => new()
    {
        Kind = entry.Kind,
        Position = entry.Position,
        RecordMutation = entry.RecordMutation is null ? null : entry.RecordMutation with
        {
            Before = CloneSnapshot(entry.RecordMutation.Before),
            After = CloneSnapshot(entry.RecordMutation.After),
        },
        SubjectAuthorityPublication = entry.SubjectAuthorityPublication is null
            ? null
            : entry.SubjectAuthorityPublication with { },
    };

    private static BaseScheduleAuthority CloneSchedule(BaseScheduleAuthority value) => value with
    {
        Definition = BaseScheduleDefinitionBuilder.Create(value.Definition),
        Checksum = value.Checksum.ToArray().ToImmutableArray(),
    };

    private static BaseScheduleOccurrenceFact CloneOccurrence(BaseScheduleOccurrenceFact value) => value with
    {
        Checksum = value.Checksum.ToArray().ToImmutableArray(),
        Disposition = value.Disposition switch
        {
            BaseOccurrenceMaterialized materialized => materialized with { },
            BaseOccurrenceSkippedMisfire skipped => skipped with { },
            BaseOccurrenceSkippedOverlap skipped => skipped with { },
            BaseOccurrenceCancelled cancelled => cancelled with { },
            BaseOccurrenceSuppressedByReplacement replacement => replacement with { },
            BaseOccurrenceSuppressedByRestoreFloor floor => floor with { FloorChecksum = floor.FloorChecksum.ToArray().ToImmutableArray() },
            _ => throw new InvalidOperationException("base.activation.occurrenceInvalid"),
        },
    };

    private static RecordSnapshot? CloneSnapshot(RecordSnapshot? snapshot) => snapshot is null ? null : snapshot with
    {
        Payload = snapshot.Payload is null ? null : RecordCloneHelpers.ClonePayload(snapshot.Payload),
        Metadata = snapshot.Metadata is null ? null : RecordCloneHelpers.CloneMetadata(snapshot.Metadata),
    };
}

internal sealed record InMemoryExecutorRow(
    BaseExecutorIncarnationAuthority Authority,
    BaseExecutorHeartbeatObservation Heartbeat,
    bool Retired)
{
    internal InMemoryExecutorRow DeepClone() => new(
        Authority with
        {
            WorkerDefinitionSetChecksum = Authority.WorkerDefinitionSetChecksum.ToArray().ToImmutableArray(),
            Checksum = Authority.Checksum.ToArray().ToImmutableArray(),
        },
        Heartbeat with
        {
            ExecutorAuthorityChecksum = Heartbeat.ExecutorAuthorityChecksum.ToArray().ToImmutableArray(),
            Checksum = Heartbeat.Checksum.ToArray().ToImmutableArray(),
        },
        Retired);
}

internal sealed record InMemoryActivationRow(
    BaseActivationPayload Payload,
    BaseActivationState State,
    long Generation,
    long RequestedDueAt,
    long EffectiveDueAt,
    byte[] Fingerprint,
    byte[] ControlChecksum,
    int AttemptNumber = 0,
    long ClaimEpoch = 0,
    BaseActivationClaimAuthority? Claim = null,
    BaseActivationLeaseObservation? Lease = null,
    byte[]? CanonicalResult = null,
    BaseEffectExecutionAuthority? Effect = null)
{
    internal InMemoryActivationRow DeepClone() => new(
        Payload with
        {
            Definition = Payload.Definition with { Checksum = Payload.Definition.Checksum.ToArray().ToImmutableArray() },
            CanonicalInput = Payload.CanonicalInput.ToArray().ToImmutableArray(),
            InputChecksum = Payload.InputChecksum.ToArray().ToImmutableArray(),
            Scope = Payload.Scope with { },
            Checksum = Payload.Checksum.ToArray().ToImmutableArray(),
        },
        State,
        Generation,
        RequestedDueAt,
        EffectiveDueAt,
        [.. Fingerprint],
        [.. ControlChecksum],
        AttemptNumber,
        ClaimEpoch,
        Claim is null ? null : Claim with
        {
            FencingToken = Claim.FencingToken.ToArray().ToImmutableArray(),
            DefinitionChecksum = Claim.DefinitionChecksum.ToArray().ToImmutableArray(),
        },
        Lease is null ? null : Lease with { Checksum = Lease.Checksum.ToArray().ToImmutableArray() },
        CanonicalResult is null ? null : [.. CanonicalResult],
        Effect is null ? null : Effect with
        {
            Claim = Effect.Claim with
            {
                FencingToken = Effect.Claim.FencingToken.ToArray().ToImmutableArray(),
                DefinitionChecksum = Effect.Claim.DefinitionChecksum.ToArray().ToImmutableArray(),
            },
            Executor = Effect.Executor with
            {
                WorkerDefinitionSetChecksum = Effect.Executor.WorkerDefinitionSetChecksum.ToArray().ToImmutableArray(),
                Checksum = Effect.Executor.Checksum.ToArray().ToImmutableArray(),
            },
            Checksum = Effect.Checksum.ToArray().ToImmutableArray(),
        });
}

internal sealed record InMemorySubjectContractState(
    string ContractId,
    int ContractVersion,
    string ContractChecksum,
    BaseSubjectAuthorityEpoch AuthorityEpoch,
    long RestoreEpoch,
    long StateGeneration,
    BaseSubjectCurrentPublicationReceipt CurrentPublicationReceipt);

internal sealed record InMemorySubjectLifetimeState(
    string ContractId,
    int ContractVersion,
    BaseSubjectId SubjectId,
    BaseSubjectIncarnation Incarnation,
    long LifetimeGeneration,
    BaseSubjectLifecycleState LifecycleState,
    long SubjectSequence,
    BaseOwnedSubjectScopeEvidence Scope,
    string PrivateCollectionId,
    RecordId PrivateRecordId,
    long CreatedJournalPosition,
    long LastLifecyclePosition);

internal sealed record InMemorySubjectTerminalState(
    string ContractId,
    int ContractVersion,
    BaseSubjectId SubjectId,
    BaseOwnedSubjectScopeEvidence Scope,
    BaseSubjectAuthorityEpoch AuthorityEpoch,
    BaseSubjectIncarnation Incarnation,
    long LifetimeGeneration,
    long SubjectSequence,
    long RetiredPosition,
    long ContractStateGeneration,
    long RestoreEpoch,
    string ReceiptChecksum);
internal sealed record InMemorySubjectLifecycleFactRow(BaseProtectedSubjectScope Scope, BaseSubjectLifecycleOrderingBoundary Boundary, BaseSubjectLifecycleFact Fact);
internal sealed record InMemorySubjectLifecycleMembershipRow(string ConsumerId, int ConsumerVersion, string ConsumerChecksum, long ProjectionGeneration, BaseSubjectLifecycleState MatchedState, BaseProtectedSubjectScope Scope, int FactIndex);
internal sealed record InMemorySubjectLifecycleConsumerProjection(string ConsumerId, int ConsumerVersion, string ConsumerChecksum, string ContractId, int ContractVersion, long ProjectionGeneration, BaseSubjectLifecycleOrderingBoundary? Cutoff, long PublishedGraphGeneration, DateTimeOffset InstalledAtUtc, TimeSpan MaximumCheckpointLag);
internal sealed record InMemorySubjectLifecycleCheckpointState(string ConsumerId, int ConsumerVersion, string ConsumerChecksum, string ContractId, int ContractVersion, long ProjectionGeneration, BaseProtectedSubjectScope Scope, BaseSubjectLifecycleOrderingBoundary? Through, long Generation, DateTimeOffset AdvancedAtUtc, bool Overtaken);
internal sealed record InMemorySubjectRetirementAcknowledgement(string ConsumerId, int ConsumerVersion, string ConsumerChecksum, long ThroughSequence, BaseSubjectAcknowledgementDisposition Disposition, long Position);
internal sealed record InMemorySubjectRetirementBarrierState(BaseProtectedSubjectScope Scope, BaseSubjectRetirementBarrier Barrier, Dictionary<string, InMemorySubjectRetirementAcknowledgement> Acknowledgements)
{
    internal InMemorySubjectRetirementBarrierState DeepClone() => new(
        Scope with { IndexDigest = [.. Scope.IndexDigest], ProtectedCanonicalValue = [.. Scope.ProtectedCanonicalValue] },
        Barrier with { AuthorityEpoch = new BaseSubjectAuthorityEpoch(Barrier.AuthorityEpoch.ToArray()), Incarnation = new BaseSubjectIncarnation(Barrier.Incarnation.ToArray()) },
        Acknowledgements.ToDictionary(static pair => new string(pair.Key.AsSpan()), static pair => pair.Value with { }, StringComparer.Ordinal));
}
internal sealed record InMemorySubjectRetirementTerminalState(BaseSubjectRetirementTerminalReceipt Receipt)
{
    internal InMemorySubjectRetirementTerminalState DeepClone() => new(Receipt with
    {
        Scope=Receipt.Scope with{IndexDigest=[..Receipt.Scope.IndexDigest],ProtectedCanonicalValue=[..Receipt.Scope.ProtectedCanonicalValue]},
        AuthorityEpoch=new BaseSubjectAuthorityEpoch(Receipt.AuthorityEpoch.ToArray()),Incarnation=new BaseSubjectIncarnation(Receipt.Incarnation.ToArray()),
        Acknowledgements=[..Receipt.Acknowledgements.Select(static value=>value with{})],
    });
}

internal sealed class InMemoryVectorProjectionState
{
    internal long AppliedThrough { get; set; }
    internal long Generation { get; set; } = 1;
    internal long PurgeGeneration { get; set; }
    internal Dictionary<string, InMemoryVectorCarrier> Carriers { get; } = new(StringComparer.Ordinal);
    internal InMemoryVectorProjectionState Clone()
    {
        var clone = new InMemoryVectorProjectionState { AppliedThrough = AppliedThrough, Generation = Generation, PurgeGeneration = PurgeGeneration };
        foreach ((string id, InMemoryVectorCarrier carrier) in Carriers) clone.Carriers.Add(id, carrier.Copy());
        return clone;
    }
}

internal sealed record InMemoryVectorCarrier(RecordId RecordId, RevisionToken Revision, long Position, BaseVector Vector)
{
    internal InMemoryVectorCarrier Copy() => this with { Vector = BaseVector.Create(Vector.ToArray()) };
}

internal sealed class InMemoryTextProjectionState
{
    internal long AppliedThrough { get; set; }
    internal long Generation { get; set; } = 1;
    internal long PurgeGeneration { get; set; }
    internal Dictionary<string, InMemoryTextCarrier> Carriers { get; } = new(StringComparer.Ordinal);
    internal InMemoryTextProjectionState Clone() { var copy = new InMemoryTextProjectionState { AppliedThrough = AppliedThrough, Generation = Generation, PurgeGeneration = PurgeGeneration }; foreach ((string key, InMemoryTextCarrier value) in Carriers) copy.Carriers.Add(key, value with { }); return copy; }
}
internal sealed record InMemoryTextCarrier(RecordId RecordId, RevisionToken Revision, long Position);
internal sealed record InMemoryTextRebuildReceipt(byte[] Fingerprint, BaseTextRebuildResult Result);

internal sealed record InMemoryMutationReceipt(
    byte[] Fingerprint,
    byte[] StructuralDigest,
    BaseAtomicReceiptResult Result,
    DateTimeOffset ExpiresAt)
{
    public InMemoryMutationReceipt DeepClone() => new(
        [.. Fingerprint],
        [.. StructuralDigest],
        CloneReceipt(Result),
        ExpiresAt);

    private static BaseAtomicReceiptResult CloneReceipt(BaseAtomicReceiptResult result) => new()
    {
        Kind = result.Kind,
        Mutations = result.Mutations.Select(static fact => BaseOwnedMutationFact.Freeze(fact.MaterializeOwned(), fact.CodecVersion)).ToImmutableArray(),
        SelectionMutation = result.SelectionMutation is null ? null : result.SelectionMutation with { },
        ModuleMutation = result.ModuleMutation is null ? null : result.ModuleMutation with
        {
            OperationId = new string(result.ModuleMutation.OperationId.AsSpan()),
            Generations = result.ModuleMutation.Generations.Select(static generation => generation with
            {
                CaptureId = new string(generation.CaptureId.AsSpan()),
                CellId = new string(generation.CellId.AsSpan()),
            }).ToImmutableArray(),
            CanonicalResultBytes = result.ModuleMutation.CanonicalResultBytes.ToArray().ToImmutableArray(),
        },
        SubjectLifecycleCheckpoint = result.SubjectLifecycleCheckpoint is null
            ? null
            : BaseSubjectLifecycleReceiptOwnership.Clone(result.SubjectLifecycleCheckpoint),
        SubjectLifecycleMaintenance = result.SubjectLifecycleMaintenance is null
            ? null
            : result.SubjectLifecycleMaintenance with { RollingChecksum = new string(result.SubjectLifecycleMaintenance.RollingChecksum.AsSpan()) },
        SubjectRetirement = result.SubjectRetirement is null ? null : result.SubjectRetirement with
        {
            Acknowledgement = result.SubjectRetirement.Acknowledgement is null ? null : result.SubjectRetirement.Acknowledgement with
            {
                BarrierChecksum = result.SubjectRetirement.Acknowledgement.BarrierChecksum is null ? null : new string(result.SubjectRetirement.Acknowledgement.BarrierChecksum.AsSpan()),
            },
            Timeout = result.SubjectRetirement.Timeout is null ? null : result.SubjectRetirement.Timeout with { BarrierChecksum = new string(result.SubjectRetirement.Timeout.BarrierChecksum.AsSpan()) },
            Override = result.SubjectRetirement.Override is null ? null : result.SubjectRetirement.Override with { BarrierChecksum = new string(result.SubjectRetirement.Override.BarrierChecksum.AsSpan()) },
            Purge = result.SubjectRetirement.Purge is null ? null : result.SubjectRetirement.Purge with { TerminalReceiptChecksum = new string(result.SubjectRetirement.Purge.TerminalReceiptChecksum.AsSpan()) },
            ConsumerRemoval = result.SubjectRetirement.ConsumerRemoval is null ? null : result.SubjectRetirement.ConsumerRemoval with { AcceptedConsumerSetChecksum = new string(result.SubjectRetirement.ConsumerRemoval.AcceptedConsumerSetChecksum.AsSpan()) },
            Maintenance = result.SubjectRetirement.Maintenance is null ? null : result.SubjectRetirement.Maintenance with { RollingChecksum = new string(result.SubjectRetirement.Maintenance.RollingChecksum.AsSpan()) },
        },
        ActivationCreation = result.ActivationCreation is null ? null : new BaseActivationCreationReceiptResult
        {
            ActivationIds = result.ActivationCreation.ActivationIds
                .Select(static value => new string(value.AsSpan()))
                .ToImmutableArray(),
        },
    };
}

internal sealed class InMemoryCollectionState
{
    public long NextAppendPosition { get; set; }
    public long PurgeGeneration { get; set; }
    /// <summary>Gets the records by ID.</summary>
    public Dictionary<string, StoredRecord> RecordsById { get; } = new(StringComparer.Ordinal);
    /// <summary>Gets or sets the optional immutable ordinal successor index used by vector projection scans.</summary>
    public ImmutableSortedSet<string>? RecordIdsOrdinal { get; set; }

    /// <summary>Executes the clone operation.</summary>
    public InMemoryCollectionState Clone()
    {
        var clone = new InMemoryCollectionState
        {
            NextAppendPosition = NextAppendPosition,
            PurgeGeneration = PurgeGeneration,
            RecordIdsOrdinal = RecordIdsOrdinal,
        };
        foreach (var (id, record) in RecordsById)
        {
            clone.RecordsById.Add(id, record with
            {
                Payload = RecordCloneHelpers.ClonePayload(record.Payload),
                Metadata = RecordCloneHelpers.CloneMetadata(record.Metadata)
            });
        }

        return clone;
    }
}

internal sealed record StoredRecord(
    string CollectionId,
    RecordId Id,
    RecordPayload Payload,
    RecordMetadata Metadata,
    long AppendPosition,
    long LatestMutationPosition = 0);
