using System.Collections.Immutable;
using System.Security.Cryptography;

namespace HPD.Base;

internal sealed partial class InMemoryRecordStore
{
    /// <inheritdoc />
    public async ValueTask<OperationResult<BaseStudioControlInspectionPage>> ReadStudioControlFactsAsync(
        BaseStudioControlInspectionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!BaseStudioControlInspectionContract.IsValid(request)) throw new ArgumentException("Studio control inspection bounds are invalid.", nameof(request));
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); deadline.CancelAfter(request.Limits.Deadline);
        await _stateGate.WaitAsync(deadline.Token).ConfigureAwait(false);
        try
        {
            IEnumerable<BaseStudioControlFact> source = Facts(_publishedState, request.Kind);
            if (request.SubjectKind is not null) source = source.Where(value => value is BaseStudioActivationReceiptFact receipt &&
                StringComparer.Ordinal.Equals(receipt.SubjectKind, request.SubjectKind) && StringComparer.Ordinal.Equals(receipt.SubjectIdentity, request.SubjectIdentity));
            if (request.Identity is not null) source = source.Where(value => StringComparer.Ordinal.Equals(value.Identity, request.Identity));
            else if (request.AfterIdentity is not null) source = source.Where(value => StringComparer.Ordinal.Compare(value.Identity, request.AfterIdentity) > 0);
            BaseStudioControlFact[] selected = source.OrderBy(static value => value.Identity, StringComparer.Ordinal)
                .Take(checked(request.Take + 1)).ToArray();
            long rows = selected.LongLength; bool more = selected.Length > request.Take;
            BaseStudioControlFact[] pageItems = more ? selected[..request.Take] : selected;
            long bytes = pageItems.Sum(BaseStudioControlInspectionContract.Measure); long transient = bytes;
            if (rows > request.Limits.MaximumRowsRead || bytes > request.Limits.MaximumEvidenceBytes || transient > request.Limits.MaximumTransientBytes)
                return OperationResults.ValidationFailed<BaseStudioControlInspectionPage>(Error("base.studio.controlInspection.limitExceeded", ErrorCategory.Validation));
            string? next = more && pageItems.Length > 0 ? pageItems[^1].Identity : null;
            var page = new BaseStudioControlInspectionPage { Items = [.. pageItems], NextIdentity = next, RowsRead = rows,
                EvidenceBytes = bytes, TransientBytes = transient,
                PageChecksum = BaseStudioControlInspectionContract.PageChecksum(pageItems, next, rows, bytes, transient) };
            return BaseStudioControlInspectionContract.IsValidResult(request, page) ? OperationResults.Ok(page) :
                OperationResults.StoreError<BaseStudioControlInspectionPage>(Error("base.studio.controlInspection.corrupt", ErrorCategory.Store));
        }
        finally { _stateGate.Release(); }
    }

    private static IEnumerable<BaseStudioControlFact> Facts(InMemoryStoreState state, BaseStudioControlFactKind kind) => kind switch
    {
        BaseStudioControlFactKind.AtomicReceipt => state.Receipts.Select(static pair => Atomic(pair.Key, pair.Value)),
        // L53 receipts deliberately retain only replay authority. Do not fabricate the
        // sequence, commit time, or subject fields required by the older Studio shape.
        BaseStudioControlFactKind.ActivationReceipt => [],
        BaseStudioControlFactKind.Activation => state.Activations.Select(static pair => Activation(pair.Key, pair.Value)),
        BaseStudioControlFactKind.Schedule => state.Schedules.Values.Select(static value => Schedule(value)),
        BaseStudioControlFactKind.Occurrence => state.ScheduleOccurrences.Values.Select(static value => Occurrence(value)),
        BaseStudioControlFactKind.Executor => state.Executors.Values.Select(static value => Executor(value)),
        BaseStudioControlFactKind.Effect => state.Activations.Where(static pair => pair.Value.Effect is not null).Select(static pair => Effect(pair.Key, pair.Value.Effect!)),
        BaseStudioControlFactKind.Quarantine => [],
        BaseStudioControlFactKind.SubjectContract => state.SubjectContracts.Values.Select(static value => SubjectContract(value)),
        BaseStudioControlFactKind.Subject => state.SubjectLifetimes.Values.Select(static value => Subject(value)),
        BaseStudioControlFactKind.LifecycleConsumer => state.SubjectLifecycleConsumers.Values.Select(value => LifecycleConsumer(value, state.SubjectLifecycleDeliveryEpoch)),
        BaseStudioControlFactKind.LifecycleCheckpoint => state.SubjectLifecycleCheckpoints.Values.Select(static value => LifecycleCheckpoint(value)),
        BaseStudioControlFactKind.RetirementBarrier => state.SubjectRetirementBarriers.Values.Select(static value => RetirementBarrier(value)),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static BaseStudioAtomicReceiptFact Atomic(string identity, InMemoryMutationReceipt row)
    {
        string[] parts = identity.Split('\u001f');
        if (parts.Length != 3) throw new InvalidOperationException("base.studio.atomicReceiptIdentityInvalid");
        var value = new BaseStudioAtomicReceiptFact { Identity = BaseStudioControlInspectionContract.AtomicIdentity(parts[0], parts[1], parts[2]), ResultKind = row.Result.Kind,
            ExpiresAtUtc = row.ExpiresAt.ToUniversalTime(), RequestFingerprint = [.. row.Fingerprint], StructuralDigest = [.. row.StructuralDigest], FactChecksum = [] };
        return value with { FactChecksum = BaseStudioControlInspectionContract.FactChecksum(value) };
    }

    private static BaseStudioActivationFact Activation(string identity, InMemoryActivationRow row)
    {
        var value = new BaseStudioActivationFact { Identity = new(identity.AsSpan()), DefinitionId = new(row.Payload.Definition.Id.AsSpan()),
            DefinitionVersion = row.Payload.Definition.Version, State = row.State, Generation = row.Generation, AttemptNumber = row.AttemptNumber,
            ClaimEpoch = row.ClaimEpoch, EffectiveDueAt = row.EffectiveDueAt, OccurrenceId = row.OccurrenceId is null ? null : new(row.OccurrenceId.AsSpan()),
            HasEffect = row.Effect is not null, FactChecksum = [] };
        return value with { FactChecksum = BaseStudioControlInspectionContract.FactChecksum(value) };
    }

    private static BaseStudioScheduleFact Schedule(BaseScheduleAuthority row)
    {
        string identity = BaseStudioControlInspectionContract.ScheduleIdentity(row.Definition.Id, row.Definition.Version);
        var value = new BaseStudioScheduleFact { Identity = identity, Version = row.Definition.Version,
            DefinitionGeneration = row.DefinitionGeneration, Enabled = row.Enabled, ScheduleEpoch = row.ScheduleEpoch,
            NextNominal = row.NextNominal, FactChecksum = [] };
        return value with { FactChecksum = BaseStudioControlInspectionContract.FactChecksum(value) };
    }

    private static BaseStudioOccurrenceFact Occurrence(BaseScheduleOccurrenceFact row)
    {
        (string disposition, string? activation) = row.Disposition switch
        { BaseOccurrenceMaterialized x => ("materialized", x.ActivationId), BaseOccurrenceSkippedMisfire => ("skippedMisfire", null),
          BaseOccurrenceSkippedOverlap => ("skippedOverlap", null), BaseOccurrenceCancelled => ("cancelled", null),
          BaseOccurrenceSuppressedByReplacement => ("suppressedByReplacement", null), BaseOccurrenceSuppressedByRestoreFloor => ("suppressedByRestoreFloor", null),
          _ => throw new InvalidOperationException("base.studio.occurrenceDispositionInvalid") };
        var value = new BaseStudioOccurrenceFact { Identity = new(row.OccurrenceId.AsSpan()), ScheduleId = new(row.ScheduleId.AsSpan()),
            ScheduleEpoch = row.ScheduleEpoch, NominalAt = row.NominalAt, EffectiveAt = row.EffectiveAt, Disposition = disposition,
            ActivationId = activation is null ? null : new(activation.AsSpan()), FactChecksum = [] };
        return value with { FactChecksum = BaseStudioControlInspectionContract.FactChecksum(value) };
    }

    private static BaseStudioExecutorFact Executor(InMemoryExecutorRow row)
    {
        BaseExecutorIncarnationAuthority authority = row.Authority; BaseExecutorHeartbeatObservation heartbeat = row.Heartbeat;
        string identity = BaseStudioControlInspectionContract.ExecutorIdentity(authority.ApplicationId, authority.HostId, authority.ProcessIncarnationId);
        var value = new BaseStudioExecutorFact { Identity = identity, HostId = new(authority.HostId.AsSpan()),
            ProcessIncarnationId = new(authority.ProcessIncarnationId.AsSpan()), ExecutorGeneration = authority.ExecutorGeneration,
            HeartbeatRevision = heartbeat.HeartbeatRevision, HeartbeatExpiresAt = heartbeat.HeartbeatExpiresAt,
            Retired = row.Retired, FactChecksum = [] };
        return value with { FactChecksum = BaseStudioControlInspectionContract.FactChecksum(value) };
    }

    private static BaseStudioEffectFact Effect(string activationId, BaseEffectExecutionAuthority row)
    {
        var value = new BaseStudioEffectFact { Identity = new(activationId.AsSpan()), ActivationId = new(activationId.AsSpan()),
            AttemptNumber = row.Claim.AttemptNumber, EffectStartGeneration = row.EffectStartGeneration, ExecutorGeneration = row.Executor.ExecutorGeneration,
            HeartbeatRevision = row.HeartbeatRevision, HeartbeatExpiresAt = row.HeartbeatExpiresAt, FactChecksum = [] };
        return value with { FactChecksum = BaseStudioControlInspectionContract.FactChecksum(value) };
    }

    private static BaseStudioSubjectContractFact SubjectContract(InMemorySubjectContractState row)
    {
        var value = new BaseStudioSubjectContractFact { Identity = BaseStudioControlInspectionContract.SubjectContractIdentity(row.ContractId, row.ContractVersion),
            ContractId = row.ContractId, ContractVersion = row.ContractVersion, ContractChecksum = row.ContractChecksum,
            AuthorityEpoch = [.. row.AuthorityEpoch.ToArray()], RestoreEpoch = row.RestoreEpoch, StateGeneration = row.StateGeneration,
            PublicationKind = row.CurrentPublicationReceipt.Kind, PublicationPosition = row.CurrentPublicationReceipt.OriginalPublicationPosition.Value, FactChecksum = [] };
        return value with { FactChecksum = BaseStudioControlInspectionContract.FactChecksum(value) };
    }

    private static BaseStudioSubjectFact Subject(InMemorySubjectLifetimeState row)
    {
        var value = new BaseStudioSubjectFact { Identity = BaseStudioControlInspectionContract.SubjectIdentity(row.ContractId, row.ContractVersion, row.SubjectId.Value),
            ContractId = row.ContractId, ContractVersion = row.ContractVersion, SubjectId = row.SubjectId.Value,
            Incarnation = [.. row.Incarnation.ToArray()], CreatedJournalPosition = row.CreatedJournalPosition, FactChecksum = [] };
        return value with { FactChecksum = BaseStudioControlInspectionContract.FactChecksum(value) };
    }

    private static BaseStudioLifecycleConsumerFact LifecycleConsumer(InMemorySubjectLifecycleConsumerProjection row, long deliveryEpoch)
    {
        var value = new BaseStudioLifecycleConsumerFact { Identity = BaseStudioControlInspectionContract.LifecycleConsumerIdentity(row.ConsumerId, row.ConsumerVersion),
            ConsumerId = row.ConsumerId, ConsumerVersion = row.ConsumerVersion, ConsumerChecksum = row.ConsumerChecksum,
            ContractId = row.ContractId, ContractVersion = row.ContractVersion, ProjectionGeneration = row.ProjectionGeneration,
            PublishedGraphGeneration = row.PublishedGraphGeneration, DeliveryEpoch = deliveryEpoch, FactChecksum = [] };
        return value with { FactChecksum = BaseStudioControlInspectionContract.FactChecksum(value) };
    }

    private static BaseStudioLifecycleCheckpointFact LifecycleCheckpoint(InMemorySubjectLifecycleCheckpointState row)
    {
        string scope = Convert.ToHexString(row.Scope.IndexDigest.AsSpan()).ToLowerInvariant();
        var value = new BaseStudioLifecycleCheckpointFact { Identity = BaseStudioControlInspectionContract.LifecycleCheckpointIdentity(row.ConsumerId, row.ConsumerVersion, scope),
            ConsumerId = row.ConsumerId, ConsumerVersion = row.ConsumerVersion, ContractId = row.ContractId, ContractVersion = row.ContractVersion,
            ProtectedScopeIdentity = scope, ProjectionGeneration = row.ProjectionGeneration, CheckpointGeneration = row.Generation,
            ThroughBoundary = Boundary(row.Through), Overtaken = row.Overtaken, FactChecksum = [] };
        return value with { FactChecksum = BaseStudioControlInspectionContract.FactChecksum(value) };
    }

    private static BaseStudioRetirementBarrierFact RetirementBarrier(InMemorySubjectRetirementBarrierState row)
    {
        BaseSubjectRetirementBarrier barrier = row.Barrier; string epoch = barrier.AuthorityEpoch.ToBase64Url(); string incarnation = barrier.Incarnation.ToBase64Url();
        var value = new BaseStudioRetirementBarrierFact { Identity = BaseStudioControlInspectionContract.RetirementBarrierIdentity(barrier.ContractId, barrier.ContractVersion, barrier.SubjectId.Value, epoch, incarnation),
            ContractId = barrier.ContractId, ContractVersion = barrier.ContractVersion, ProtectedSubjectIdentity = barrier.SubjectId.Value,
            AuthorityEpoch = epoch, Incarnation = incarnation, TombstoneSequence = barrier.TombstoneSequence,
            RequiredConsumerSetChecksum = barrier.RequiredConsumerSetChecksum, DeadlineUtc = barrier.DeadlineUtc, State = barrier.State,
            Generation = barrier.Generation, BarrierChecksum = barrier.BarrierChecksum, FactChecksum = [] };
        return value with { FactChecksum = BaseStudioControlInspectionContract.FactChecksum(value) };
    }

    private static string Boundary(BaseSubjectLifecycleOrderingBoundary? value) => value is null ? "none" :
        $"{value.CommitPosition.Value}:{value.SubjectId.Value}:{value.AuthorityEpoch.ToBase64Url()}:{value.Incarnation.ToBase64Url()}:{value.SubjectSequence}";
}
