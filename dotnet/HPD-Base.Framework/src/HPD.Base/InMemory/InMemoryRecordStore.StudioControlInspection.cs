using System.Collections.Immutable;
using System.Security.Cryptography;

namespace HPD.Base;

internal sealed partial class InMemoryRecordStore
{
    /// <inheritdoc />
    public async ValueTask<OperationResult<StudioControlInspectionPage>> ReadStudioControlFactsAsync(
        StudioControlInspectionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!StudioControlInspectionContract.IsValid(request)) throw new ArgumentException("Studio control inspection bounds are invalid.", nameof(request));
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); deadline.CancelAfter(request.Limits.Deadline);
        await _stateGate.WaitAsync(deadline.Token).ConfigureAwait(false);
        try
        {
            IEnumerable<StudioControlFact> source = Facts(_publishedState, request.Kind);
            if (request.SubjectKind is not null) source = source.Where(value => value is StudioActivationReceiptFact receipt &&
                StringComparer.Ordinal.Equals(request.SubjectKind, "activation") && StringComparer.Ordinal.Equals(receipt.ActivationId, request.SubjectIdentity));
            if (request.Identity is not null) source = source.Where(value => StringComparer.Ordinal.Equals(value.Identity, request.Identity));
            else if (request.AfterIdentity is not null) source = source.Where(value => StringComparer.Ordinal.Compare(value.Identity, request.AfterIdentity) > 0);
            StudioControlFact[] selected = source.OrderBy(static value => value.Identity, StringComparer.Ordinal)
                .Take(checked(request.Take + 1)).ToArray();
            long rows = selected.LongLength; bool more = selected.Length > request.Take;
            StudioControlFact[] pageItems = more ? selected[..request.Take] : selected;
            long bytes = pageItems.Sum(StudioControlInspectionContract.Measure); long transient = bytes;
            if (rows > request.Limits.MaximumRowsRead || bytes > request.Limits.MaximumEvidenceBytes || transient > request.Limits.MaximumTransientBytes)
                return OperationResults.ValidationFailed<StudioControlInspectionPage>(Error("base.studio.controlInspection.limitExceeded", ErrorCategory.Validation));
            string? next = more && pageItems.Length > 0 ? pageItems[^1].Identity : null;
            var page = new StudioControlInspectionPage { Items = [.. pageItems], NextIdentity = next, RowsRead = rows,
                EvidenceBytes = bytes, TransientBytes = transient,
                PageChecksum = StudioControlInspectionContract.PageChecksum(pageItems, next, rows, bytes, transient) };
            return StudioControlInspectionContract.IsValidResult(request, page) ? OperationResults.Ok(page) :
                OperationResults.StoreError<StudioControlInspectionPage>(Error("base.studio.controlInspection.corrupt", ErrorCategory.Store));
        }
        finally { _stateGate.Release(); }
    }

    private static IEnumerable<StudioControlFact> Facts(InMemoryStoreState state, StudioControlFactKind kind) => kind switch
    {
        StudioControlFactKind.AtomicReceipt => state.Receipts.Select(static pair => Atomic(pair.Key, pair.Value)),
        // L53 receipts deliberately retain only replay authority. Do not fabricate the
        // sequence, commit time, or subject fields required by the older Studio shape.
        StudioControlFactKind.ActivationReceipt => [],
        StudioControlFactKind.Activation => state.Activations.Select(static pair => Activation(pair.Key, pair.Value)),
        StudioControlFactKind.Schedule => state.Schedules.Values.Select(static value => Schedule(value)),
        StudioControlFactKind.Occurrence => state.ScheduleOccurrences.Values.Select(static value => Occurrence(value)),
        StudioControlFactKind.Executor => state.Executors.Values.Select(static value => Executor(value)),
        StudioControlFactKind.Effect => state.Activations.Where(static pair => pair.Value.Effect is not null).Select(static pair => Effect(pair.Key, pair.Value.Effect!)),
        StudioControlFactKind.Quarantine => [],
        StudioControlFactKind.SubjectContract => state.SubjectContracts.Values.Select(static value => SubjectContract(value)),
        StudioControlFactKind.Subject => state.SubjectLifetimes.Values.Select(static value => Subject(value)),
        StudioControlFactKind.LifecycleConsumer => state.SubjectLifecycleConsumers.Values.Select(value => LifecycleConsumer(value, state.SubjectLifecycleDeliveryEpoch)),
        StudioControlFactKind.LifecycleCheckpoint => state.SubjectLifecycleCheckpoints.Values.Select(static value => LifecycleCheckpoint(value)),
        StudioControlFactKind.RetirementBarrier => state.SubjectRetirementBarriers.Values.Select(static value => RetirementBarrier(value)),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static StudioAtomicReceiptFact Atomic(string identity, InMemoryMutationReceipt row)
    {
        string[] parts = identity.Split('\u001f');
        if (parts.Length != 3) throw new InvalidOperationException("base.studio.atomicReceiptIdentityInvalid");
        var value = new StudioAtomicReceiptFact { Identity = StudioControlInspectionContract.AtomicIdentity(parts[0], parts[1], parts[2]), ResultKind = row.Result.Kind,
            ExpiresAtUtc = row.ExpiresAt.ToUniversalTime(), RequestFingerprint = [.. row.Fingerprint], StructuralDigest = [.. row.StructuralDigest], FactChecksum = [] };
        return value with { FactChecksum = StudioControlInspectionContract.FactChecksum(value) };
    }

    private static StudioActivationFact Activation(string identity, InMemoryActivationRow row)
    {
        var value = new StudioActivationFact { Identity = new(identity.AsSpan()), DefinitionId = new(row.Payload.Definition.Id.AsSpan()),
            DefinitionVersion = row.Payload.Definition.Version, State = row.State, Generation = row.Generation, AttemptNumber = row.AttemptNumber,
            ClaimEpoch = row.ClaimEpoch, EffectiveDueAt = row.EffectiveDueAt, OccurrenceId = row.OccurrenceId is null ? null : new(row.OccurrenceId.AsSpan()),
            HasEffect = row.Effect is not null, FactChecksum = [] };
        return value with { FactChecksum = StudioControlInspectionContract.FactChecksum(value) };
    }

    private static StudioScheduleFact Schedule(BaseScheduleAuthority row)
    {
        string identity = StudioControlInspectionContract.ScheduleIdentity(row.Definition.Id, row.Definition.Version);
        var value = new StudioScheduleFact { Identity = identity, Version = row.Definition.Version,
            DefinitionGeneration = row.DefinitionGeneration, Enabled = row.Enabled, ScheduleEpoch = row.ScheduleEpoch,
            NextNominal = row.NextNominal, FactChecksum = [] };
        return value with { FactChecksum = StudioControlInspectionContract.FactChecksum(value) };
    }

    private static StudioOccurrenceFact Occurrence(BaseScheduleOccurrenceFact row)
    {
        (string disposition, string? activation) = row.Disposition switch
        { BaseOccurrenceMaterialized x => ("materialized", x.ActivationId), BaseOccurrenceSkippedMisfire => ("skippedMisfire", null),
          BaseOccurrenceSkippedOverlap => ("skippedOverlap", null), BaseOccurrenceCancelled => ("cancelled", null),
          BaseOccurrenceSuppressedByReplacement => ("suppressedByReplacement", null), BaseOccurrenceSuppressedByRestoreFloor => ("suppressedByRestoreFloor", null),
          _ => throw new InvalidOperationException("base.studio.occurrenceDispositionInvalid") };
        var value = new StudioOccurrenceFact { Identity = new(row.OccurrenceId.AsSpan()), ScheduleId = new(row.ScheduleId.AsSpan()),
            ScheduleEpoch = row.ScheduleEpoch, NominalAt = row.NominalAt, EffectiveAt = row.EffectiveAt, Disposition = disposition,
            ActivationId = activation is null ? null : new(activation.AsSpan()), FactChecksum = [] };
        return value with { FactChecksum = StudioControlInspectionContract.FactChecksum(value) };
    }

    private static StudioExecutorFact Executor(InMemoryExecutorRow row)
    {
        BaseExecutorIncarnationAuthority authority = row.Authority; BaseExecutorHeartbeatObservation heartbeat = row.Heartbeat;
        string identity = StudioControlInspectionContract.ExecutorIdentity(authority.ApplicationId, authority.HostId, authority.ProcessIncarnationId);
        var value = new StudioExecutorFact { Identity = identity, HostId = new(authority.HostId.AsSpan()),
            ProcessIncarnationId = new(authority.ProcessIncarnationId.AsSpan()), ExecutorGeneration = authority.ExecutorGeneration,
            HeartbeatRevision = heartbeat.HeartbeatRevision, HeartbeatExpiresAt = heartbeat.HeartbeatExpiresAt,
            Retired = row.Retired, FactChecksum = [] };
        return value with { FactChecksum = StudioControlInspectionContract.FactChecksum(value) };
    }

    private static StudioEffectFact Effect(string activationId, BaseEffectExecutionAuthority row)
    {
        var value = new StudioEffectFact { Identity = new(activationId.AsSpan()), ActivationId = new(activationId.AsSpan()),
            AttemptNumber = row.Claim.AttemptNumber, EffectStartGeneration = row.EffectStartGeneration, ExecutorGeneration = row.Executor.ExecutorGeneration,
            HeartbeatRevision = row.HeartbeatRevision, HeartbeatExpiresAt = row.HeartbeatExpiresAt, FactChecksum = [] };
        return value with { FactChecksum = StudioControlInspectionContract.FactChecksum(value) };
    }

    private static StudioSubjectContractFact SubjectContract(InMemorySubjectContractState row)
    {
        var value = new StudioSubjectContractFact { Identity = StudioControlInspectionContract.SubjectContractIdentity(row.ContractId, row.ContractVersion),
            ContractId = row.ContractId, ContractVersion = row.ContractVersion, ContractChecksum = row.ContractChecksum,
            AuthorityEpoch = [.. row.AuthorityEpoch.ToArray()], RestoreEpoch = row.RestoreEpoch, StateGeneration = row.StateGeneration,
            PublicationKind = row.CurrentPublicationReceipt.Kind, PublicationPosition = row.CurrentPublicationReceipt.OriginalPublicationPosition.Value, FactChecksum = [] };
        return value with { FactChecksum = StudioControlInspectionContract.FactChecksum(value) };
    }

    private static StudioSubjectFact Subject(InMemorySubjectLifetimeState row)
    {
        var value = new StudioSubjectFact { Identity = StudioControlInspectionContract.SubjectIdentity(row.ContractId, row.ContractVersion, row.SubjectId.Value),
            ContractId = row.ContractId, ContractVersion = row.ContractVersion, SubjectId = row.SubjectId.Value,
            Incarnation = [.. row.Incarnation.ToArray()], CreatedJournalPosition = row.CreatedJournalPosition, FactChecksum = [] };
        return value with { FactChecksum = StudioControlInspectionContract.FactChecksum(value) };
    }

    private static StudioLifecycleConsumerFact LifecycleConsumer(InMemorySubjectLifecycleConsumerProjection row, long deliveryEpoch)
    {
        var value = new StudioLifecycleConsumerFact { Identity = StudioControlInspectionContract.LifecycleConsumerIdentity(row.ConsumerId, row.ConsumerVersion),
            ConsumerId = row.ConsumerId, ConsumerVersion = row.ConsumerVersion, ConsumerChecksum = row.ConsumerChecksum,
            ContractId = row.ContractId, ContractVersion = row.ContractVersion, ProjectionGeneration = row.ProjectionGeneration,
            PublishedGraphGeneration = row.PublishedGraphGeneration, DeliveryEpoch = deliveryEpoch, FactChecksum = [] };
        return value with { FactChecksum = StudioControlInspectionContract.FactChecksum(value) };
    }

    private static StudioLifecycleCheckpointFact LifecycleCheckpoint(InMemorySubjectLifecycleCheckpointState row)
    {
        string scope = Convert.ToHexString(row.Scope.IndexDigest.AsSpan()).ToLowerInvariant();
        var value = new StudioLifecycleCheckpointFact { Identity = StudioControlInspectionContract.LifecycleCheckpointIdentity(row.ConsumerId, row.ConsumerVersion, scope),
            ConsumerId = row.ConsumerId, ConsumerVersion = row.ConsumerVersion, ContractId = row.ContractId, ContractVersion = row.ContractVersion,
            ProtectedScopeIdentity = scope, ProjectionGeneration = row.ProjectionGeneration, CheckpointGeneration = row.Generation,
            ThroughBoundary = Boundary(row.Through), Overtaken = row.Overtaken, FactChecksum = [] };
        return value with { FactChecksum = StudioControlInspectionContract.FactChecksum(value) };
    }

    private static StudioRetirementBarrierFact RetirementBarrier(InMemorySubjectRetirementBarrierState row)
    {
        BaseSubjectRetirementBarrier barrier = row.Barrier; string epoch = barrier.AuthorityEpoch.ToBase64Url(); string incarnation = barrier.Incarnation.ToBase64Url();
        var value = new StudioRetirementBarrierFact { Identity = StudioControlInspectionContract.RetirementBarrierIdentity(barrier.ContractId, barrier.ContractVersion, barrier.SubjectId.Value, epoch, incarnation),
            ContractId = barrier.ContractId, ContractVersion = barrier.ContractVersion, ProtectedSubjectIdentity = barrier.SubjectId.Value,
            AuthorityEpoch = epoch, Incarnation = incarnation, TombstoneSequence = barrier.TombstoneSequence,
            RequiredConsumerSetChecksum = barrier.RequiredConsumerSetChecksum, DeadlineUtc = barrier.DeadlineUtc, State = barrier.State,
            Generation = barrier.Generation, BarrierChecksum = barrier.BarrierChecksum, FactChecksum = [] };
        return value with { FactChecksum = StudioControlInspectionContract.FactChecksum(value) };
    }

    private static string Boundary(BaseSubjectLifecycleOrderingBoundary? value) => value is null ? "none" :
        $"{value.CommitPosition.Value}:{value.SubjectId.Value}:{value.AuthorityEpoch.ToBase64Url()}:{value.Incarnation.ToBase64Url()}:{value.SubjectSequence}";
}
