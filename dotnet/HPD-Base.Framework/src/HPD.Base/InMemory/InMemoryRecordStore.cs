using HPD.Events;
using Microsoft.Extensions.Options;
using System.Buffers;
using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace HPD.Base;

/// <summary>
/// Process-local, thread-safe, non-durable HPD.BASE record store implementation.
/// </summary>
internal sealed partial class InMemoryRecordStore : IAtomicRecordStore, IStreamingRecordStore, IRelationalReadStore, IConsistentRecordIncludeStore, IInMemoryProjectionAuthority, ITransactionalMutationJournalStore, IBaseSubjectAdministration, IBaseSubjectPublicationStore, IBaseSubjectValidationPlanReceiptStore, IBaseSubjectLifecycleStore
{
    /// <inheritdoc />
    public ValueTask<RecordMutationExecutionResult> AdvanceCheckpointAsync(
        IAtomicMutationProcessor processor,
        RecordMutationExecutionRequest execution,
        CancellationToken cancellationToken = default) =>
        ExecuteAtomicAsync(processor, execution, cancellationToken);

    /// <inheritdoc />
    public async ValueTask<OperationResult<BaseSubjectLifecycleProviderPage>> ReadAsync(
        BaseSubjectLifecycleProviderReadRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (_lifecycleMaintenance is not null) return LifecycleReadFailure(BaseSubjectErrorCodes.MaintenanceRequired, OperationStatus.CapabilityUnavailable, ErrorCategory.Capability);
        if (request.Take is < 1 or > 256 || request.MaximumResultBytes is < 1 or > 1_048_576 || request.DeadlineUtc <= _timeProvider.GetUtcNow())
            return LifecycleReadFailure(BaseSubjectErrorCodes.LifecycleContractInvalid, OperationStatus.ValidationFailed, ErrorCategory.Validation);
        await _stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            InMemoryStoreState state = Volatile.Read(ref _publishedState);
            BaseExportedSubjectDefinition? contract = _options.ExportedSubjects.SingleOrDefault(value => value.Id == request.ContractId && value.Version == request.ContractVersion);
            string consumerKey = $"{request.ConsumerId}\n{request.ConsumerVersion}";
            InMemorySubjectLifecycleConsumerProjection? projection = state.SubjectLifecycleConsumers.GetValueOrDefault(consumerKey);
            if (contract is null || BaseSubjectContractGraph.Checksum(contract) != request.ContractChecksum
                || projection is null || projection.ContractId != request.ContractId || projection.ContractVersion != request.ContractVersion
                || projection.ConsumerChecksum != request.ConsumerChecksum)
                return LifecycleReadFailure(BaseSubjectErrorCodes.LifecycleProviderContractInvalid);
            BaseProtectedSubjectScope protectedScope = _subjectScopes.Protect(request.Scope, _subjectScopeProtectionKey);
            string scopeKey = ProtectedScopeKey(request.ConsumerId, request.ConsumerVersion, protectedScope);
            InMemorySubjectLifecycleCheckpointState? durableCheckpoint = state.SubjectLifecycleCheckpoints.GetValueOrDefault(scopeKey);
            if (durableCheckpoint?.Overtaken == true)
                return LifecycleReadFailure(BaseSubjectErrorCodes.CursorOvertaken, OperationStatus.Conflict, ErrorCategory.Conflict);
            if (projection.ProjectionGeneration != request.ProjectionGeneration)
                return LifecycleReadFailure(BaseSubjectErrorCodes.LifecycleProviderContractInvalid);
            BaseSubjectLifecycleOrderingBoundary? durableThrough = durableCheckpoint?.Through;
            BaseSubjectLifecycleOrderingBoundary? effectiveAfter = request.After is null ? durableThrough
                : durableThrough is null || CompareBoundary(request.After, durableThrough) >= 0 ? request.After : durableThrough;
            IEnumerable<(InMemorySubjectLifecycleMembershipRow Membership, InMemorySubjectLifecycleFactRow Row)> retained =
                state.SubjectLifecycleMembershipIndex.GetValueOrDefault(scopeKey, [])
                    .Select(index => state.SubjectLifecycleMemberships[index])
                    .Where(membership => membership.ConsumerChecksum == request.ConsumerChecksum && membership.ProjectionGeneration == request.ProjectionGeneration)
                    .Select(membership => (Membership: membership, Row: state.SubjectLifecycleFacts[membership.FactIndex]))
                    .Where(pair => pair.Row.Fact.ContractId == request.ContractId && pair.Row.Fact.ContractVersion == request.ContractVersion
                        && ProtectedScopeEquals(pair.Membership.Scope, protectedScope) && ProtectedScopeEquals(pair.Row.Scope, protectedScope));
            BaseSubjectLifecycleOrderingBoundary? earliest = retained.OrderBy(static pair => pair.Row.Boundary, BaseLifecycleBoundaryComparer.Instance).Select(static pair => pair.Row.Boundary).FirstOrDefault();
            BaseSubjectLifecycleOrderingBoundary? high = retained.OrderByDescending(static pair => pair.Row.Boundary, BaseLifecycleBoundaryComparer.Instance).Select(static pair => pair.Row.Boundary).FirstOrDefault();
            if (effectiveAfter is not null && earliest is not null && CompareBoundary(effectiveAfter, earliest) < 0)
                return LifecycleReadFailure(BaseSubjectErrorCodes.CursorOvertaken, OperationStatus.Conflict, ErrorCategory.Conflict);
            IEnumerable<(InMemorySubjectLifecycleMembershipRow Membership, InMemorySubjectLifecycleFactRow Row)> query = retained;
            if (effectiveAfter is not null) query = query.Where(pair => CompareBoundary(pair.Row.Boundary, effectiveAfter) > 0);
            var facts = ImmutableArray.CreateBuilder<BaseSubjectLifecycleProviderFact>();
            long bytes = 8;
            int rowsSought = 0;
            foreach ((InMemorySubjectLifecycleMembershipRow membership, InMemorySubjectLifecycleFactRow row) in query
                .OrderBy(static pair => pair.Row.Boundary, BaseLifecycleBoundaryComparer.Instance).Take(request.Take))
            {
                rowsSought++;
                var providerFact = new BaseSubjectLifecycleProviderFact
                {
                    Boundary = row.Boundary with { }, Scope = protectedScope with { IndexDigest = (byte[])protectedScope.IndexDigest.Clone(), ProtectedCanonicalValue = (byte[])protectedScope.ProtectedCanonicalValue.Clone() },
                    Fact = row.Fact with { }, ConsumerId = membership.ConsumerId, ConsumerVersion = membership.ConsumerVersion,
                    ConsumerChecksum = membership.ConsumerChecksum, ProjectionGeneration = membership.ProjectionGeneration,
                    MatchedObservedState = membership.MatchedState,
                };
                long size = checked(8L + BaseSubjectCanonicalRetainedWork.MeasureLifecycleProviderFact(providerFact));
                if (checked(bytes + size) > request.MaximumResultBytes)
                    return facts.Count == 0
                        ? LifecycleReadFailure(BaseSubjectErrorCodes.LifecycleCapacityExceeded, OperationStatus.StoreError, ErrorCategory.Store)
                        : OperationResults.Ok(Page());
                bytes += size;
                facts.Add(providerFact);
            }
            return OperationResults.Ok(Page());

            BaseSubjectLifecycleProviderPage Page()
            {
                BaseSubjectLifecycleOrderingBoundary? through = facts.Count == 0 ? null : facts[^1].Boundary;
                ImmutableArray<BaseReadIntervalEvidence> intervals = BaseSubjectLifecycleReadIntervals.Create(request, protectedScope, through);
                return new BaseSubjectLifecycleProviderPage
                {
                    StoreInstanceId = _options.StoreId, RestoreEpoch = state.SubjectContracts[SubjectContractKey(request.ContractId, request.ContractVersion)].RestoreEpoch,
                    DeliveryEpoch = state.SubjectLifecycleDeliveryEpoch, CheckpointGeneration = durableCheckpoint?.Generation ?? 0,
                    Scope = protectedScope, Facts = facts.ToImmutable(), EarliestRetained = earliest, HighWater = high,
                    Through = through, ProjectionGeneration = projection.ProjectionGeneration, Intervals = intervals,
                    Accounting = new BaseSubjectLifecycleReadAccounting { RowsSought = rowsSought, RowsHydrated = facts.Count, ResultBytes = bytes, TransientBytes = checked(bytes + BaseSubjectCanonicalRetainedWork.MeasureLifecycleIntervals(intervals)) },
                };
            }
        }
        finally { _stateGate.Release(); }
    }

    private static bool ScopeEquals(BaseOwnedSubjectScopeEvidence left, BaseOwnedSubjectScopeEvidence right) => left.Kind == right.Kind && string.Equals(left.Value, right.Value, StringComparison.Ordinal);
    private static bool ProtectedScopeEquals(BaseProtectedSubjectScope left, BaseProtectedSubjectScope right) =>
        // ProtectedCanonicalValue is nonce-randomized authenticated ciphertext.
        // Equality and seek authority are the keyed deterministic index digest;
        // ciphertext remains retained only for authorized recovery/rotation.
        left.Kind == right.Kind && CryptographicOperations.FixedTimeEquals(left.IndexDigest, right.IndexDigest);
    private static string ProtectedScopeKey(string consumerId, int consumerVersion, BaseProtectedSubjectScope scope) =>
        $"{consumerId}\n{consumerVersion}\n{(int)scope.Kind}\n{Convert.ToHexString(scope.IndexDigest)}";
    private static int CompareBoundary(BaseSubjectLifecycleOrderingBoundary left, BaseSubjectLifecycleOrderingBoundary right) => BaseLifecycleBoundaryComparer.Instance.Compare(left, right);
    private static OperationResult<BaseSubjectLifecycleProviderPage> LifecycleReadFailure(string code, OperationStatus status = OperationStatus.StoreError, ErrorCategory category = ErrorCategory.Store) => new()
    { Status = status, Error = new BaseError { Code = code, Message = "The subject lifecycle provider operation failed.", Category = category } };

    /// <inheritdoc />
    public async ValueTask<OperationResult<BaseSubjectLifecycleProviderReconciliationPage>> ReconcileAsync(
        BaseSubjectLifecycleProviderReconciliationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!BaseSubjectLifecycleProviderCapabilities.BuiltIn.ReconciliationSupported)
            return LifecycleReconciliationFailure(BaseSubjectErrorCodes.LifecycleReconciliationUnavailable, OperationStatus.CapabilityUnavailable, ErrorCategory.Capability);
        if (request.Take is < 1 or > 256 || request.MaximumResultBytes is < 1 or > 1_048_576 || request.DeadlineUtc <= _timeProvider.GetUtcNow())
            return LifecycleReconciliationFailure(BaseSubjectErrorCodes.LifecycleContractInvalid, OperationStatus.ValidationFailed, ErrorCategory.Validation);
        await _stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            InMemoryStoreState state = Volatile.Read(ref _publishedState);
            BaseExportedSubjectDefinition? contract = _options.ExportedSubjects.SingleOrDefault(value => value.Id == request.ContractId && value.Version == request.ContractVersion);
            InMemorySubjectContractState? authority = state.SubjectContracts.GetValueOrDefault(SubjectContractKey(request.ContractId, request.ContractVersion));
            if (contract is null || authority is null || BaseSubjectContractGraph.Checksum(contract) != request.ContractChecksum)
                return LifecycleReconciliationFailure(BaseSubjectErrorCodes.LifecycleProviderContractInvalid);
            BaseProtectedSubjectScope protectedScope = _subjectScopes.Protect(request.Scope, _subjectScopeProtectionKey);
            IEnumerable<InMemorySubjectLifetimeState> query = state.SubjectLifetimes.Values.Where(value =>
                value.ContractId == request.ContractId && value.ContractVersion == request.ContractVersion && ScopeEquals(value.Scope, request.Scope));
            if (request.AfterSubjectId is not null)
                query = query.Where(value => string.CompareOrdinal(value.SubjectId.Value, request.AfterSubjectId.Value.Value) > 0);
            var subjects = ImmutableArray.CreateBuilder<BaseCurrentSubjectLifecycle>();
            long bytes = 0;
            foreach (InMemorySubjectLifetimeState lifetime in query.OrderBy(static value => value.SubjectId.Value, StringComparer.Ordinal).Take(request.Take))
            {
                long size = checked(96L + Encoding.UTF8.GetByteCount(lifetime.SubjectId.Value));
                if (checked(bytes + size) > request.MaximumResultBytes) break;
                bytes += size;
                subjects.Add(new BaseCurrentSubjectLifecycle
                {
                    SubjectId = lifetime.SubjectId,
                    AuthorityEpoch = authority.AuthorityEpoch,
                    Incarnation = lifetime.Incarnation,
                    State = lifetime.LifecycleState,
                    SubjectSequence = lifetime.SubjectSequence,
                });
            }
            BaseSubjectLifecycleOrderingBoundary? highWater = state.SubjectLifecycleFacts
                .Where(value => value.Fact.ContractId == request.ContractId && value.Fact.ContractVersion == request.ContractVersion && _subjectScopes.Matches(value.Scope, request.Scope))
                .OrderByDescending(static value => value.Boundary, BaseLifecycleBoundaryComparer.Instance)
                .Select(static value => value.Boundary).FirstOrDefault();
            return OperationResults.Ok(new BaseSubjectLifecycleProviderReconciliationPage
            {
                Scope = protectedScope,
                Subjects = subjects.ToImmutable(),
                NextSubjectId = subjects.Count == request.Take ? subjects[^1].SubjectId : null,
                CapturedHighWater = highWater,
                ProjectionGeneration = request.ProjectionGeneration,
                Intervals = [],
                Accounting = new BaseSubjectLifecycleReadAccounting { RowsSought = subjects.Count, RowsHydrated = subjects.Count, ResultBytes = bytes, TransientBytes = bytes },
            });
        }
        finally { _stateGate.Release(); }
    }

    /// <inheritdoc />
    public ValueTask<OperationResult<BaseSubjectLifecycleProviderInspection>> InspectAsync(
        BaseSubjectLifecycleProviderInspectionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (request.DeadlineUtc <= _timeProvider.GetUtcNow())
            return ValueTask.FromResult(LifecycleInspectionFailure(BaseSubjectErrorCodes.LifecycleContractInvalid, OperationStatus.ValidationFailed, ErrorCategory.Validation));
        if (request.ScopeAuthority.Mode == BaseSubjectScopeQueryMode.AllAuthorizedScopes
            && (request.ScopeAuthority.ExactScope is not null || request.IncludeTerminalReceipt || request.SubjectId is not null
                || !_options.SubjectLifecycleInspectionAuthorities.Any(value =>
                    value.ContractId == request.ContractId && value.ContractVersion == request.ContractVersion
                    && string.Equals(value.Digest, request.ScopeAuthority.InstalledAuthorityDigest, StringComparison.Ordinal))))
            return ValueTask.FromResult(LifecycleInspectionFailure(BaseSubjectErrorCodes.LifecycleUnauthorized, OperationStatus.PolicyDenied, ErrorCategory.Authorization));
        InMemoryStoreState state = Volatile.Read(ref _publishedState);
        BaseOwnedSubjectScopeEvidence? scope = request.ScopeAuthority.Mode == BaseSubjectScopeQueryMode.ExactScope ? request.ScopeAuthority.ExactScope : null;
        if (request.ScopeAuthority.Mode == BaseSubjectScopeQueryMode.ExactScope && scope is null)
            return ValueTask.FromResult(LifecycleInspectionFailure(BaseSubjectErrorCodes.LifecycleContractInvalid, OperationStatus.ValidationFailed, ErrorCategory.Validation));
        if (request.ScopeAuthority.Mode == BaseSubjectScopeQueryMode.ExactScope && !ExactInspectionAuthorityMatches(request))
            return ValueTask.FromResult(LifecycleInspectionFailure(BaseSubjectErrorCodes.LifecycleUnauthorized, OperationStatus.PolicyDenied, ErrorCategory.Authorization));
        var consumersBuilder = ImmutableArray.CreateBuilder<BaseSubjectLifecycleConsumerInspection>();
        foreach (InMemorySubjectLifecycleConsumerProjection projection in state.SubjectLifecycleConsumers.Values.Where(value =>
            value.ContractId == request.ContractId && value.ContractVersion == request.ContractVersion
            && (request.ConsumerId is null || value.ConsumerId == request.ConsumerId)))
        {
            BaseProtectedSubjectScope? protectedInspectionScope = scope is null ? null : _subjectScopes.Protect(scope, _subjectScopeProtectionKey);
            InMemorySubjectLifecycleCheckpointState? checkpoint = protectedInspectionScope is null ? null
                : state.SubjectLifecycleCheckpoints.GetValueOrDefault(ProtectedScopeKey(projection.ConsumerId, projection.ConsumerVersion, protectedInspectionScope));
            consumersBuilder.Add(new BaseSubjectLifecycleConsumerInspection
            {
                ConsumerId = projection.ConsumerId,
                ConsumerVersion = projection.ConsumerVersion,
                ProjectionGeneration = projection.ProjectionGeneration,
                InstallationCutoff = projection.Cutoff,
                PublishedGraphGeneration = projection.PublishedGraphGeneration,
                Through = checkpoint?.Through,
                CheckpointGeneration = checkpoint?.Generation ?? 0,
                Overtaken = checkpoint?.Overtaken ?? false,
            });
        }
        ImmutableArray<BaseSubjectLifecycleConsumerInspection> consumers = consumersBuilder.ToImmutable();
        IEnumerable<InMemorySubjectLifecycleFactRow> facts = state.SubjectLifecycleFacts.Where(value =>
            value.Fact.ContractId == request.ContractId && value.Fact.ContractVersion == request.ContractVersion && (scope is null || _subjectScopes.Matches(value.Scope, scope)));
        BaseSubjectTerminalLifetimeReceipt? terminalReceipt = null;
        if (request.IncludeTerminalReceipt && request.SubjectId is BaseSubjectId requestedSubjectId && scope is not null
            && state.SubjectTerminals.TryGetValue(SubjectKey(scope, request.ContractId, request.ContractVersion, requestedSubjectId), out InMemorySubjectTerminalState? terminal))
        {
            BaseProtectedSubjectScope protectedScope = _subjectScopes.Protect(scope, _subjectScopeProtectionKey);
            terminalReceipt = new BaseSubjectTerminalLifetimeReceipt
            {
                ContractId = terminal.ContractId, ContractVersion = terminal.ContractVersion, SubjectId = terminal.SubjectId,
                Scope = protectedScope, RetiredAuthorityEpoch = terminal.AuthorityEpoch, RetiredIncarnation = terminal.Incarnation,
                RetiredLifetimeGeneration = terminal.LifetimeGeneration, RetiredSubjectSequence = terminal.SubjectSequence,
                RetiredPosition = new(terminal.RetiredPosition), ContractStateGeneration = terminal.ContractStateGeneration,
                RestoreEpoch = terminal.RestoreEpoch, ReceiptChecksum = terminal.ReceiptChecksum,
            };
            if (!BaseSubjectTerminalIntegrity.Verify(terminalReceipt, scope))
                return ValueTask.FromResult(LifecycleInspectionFailure(BaseSubjectErrorCodes.LifecycleProviderContractInvalid, OperationStatus.CapabilityUnavailable, ErrorCategory.Capability));
        }
        return ValueTask.FromResult(OperationResults.Ok(new BaseSubjectLifecycleProviderInspection
        {
            StoreInstanceId = _options.StoreId,
            RestoreEpoch = 0,
            DeliveryEpoch = state.SubjectLifecycleDeliveryEpoch,
            EarliestRetained = facts.OrderBy(static value => value.Boundary, BaseLifecycleBoundaryComparer.Instance).Select(static value => value.Boundary).FirstOrDefault(),
            HighWater = facts.OrderByDescending(static value => value.Boundary, BaseLifecycleBoundaryComparer.Instance).Select(static value => value.Boundary).FirstOrDefault(),
            Consumers = consumers,
            TerminalReceipt = terminalReceipt,
            Accounting = new BaseSubjectLifecycleReadAccounting { RowsSought = consumers.Length, RowsHydrated = consumers.Length, ResultBytes = consumers.Length * 96L, TransientBytes = consumers.Length * 96L },
        }));
    }

    private bool ExactInspectionAuthorityMatches(BaseSubjectLifecycleProviderInspectionRequest request)
    {
        BaseExportedSubjectDefinition? contract = _options.ExportedSubjects.SingleOrDefault(value => value.Id == request.ContractId && value.Version == request.ContractVersion);
        if (contract is null) return false;
        string expected = BaseSubjectContractGraph.Checksum(contract);
        if (request.ConsumerId is not null)
        {
            BaseSubjectLifecycleConsumerDefinition? consumer = _options.SubjectLifecycleConsumers.SingleOrDefault(value => value.Id == request.ConsumerId && value.ContractId == request.ContractId && value.ContractVersion == request.ContractVersion);
            if (consumer is null) return false;
            expected = BaseSubjectLifecycleRegistry.Checksum(BaseSubjectLifecycleRegistry.Normalize(consumer), expected);
        }
        return string.Equals(expected, request.ScopeAuthority.InstalledAuthorityDigest, StringComparison.Ordinal);
    }

    /// <inheritdoc />
    public ValueTask<RecordMutationExecutionResult> ExecuteMaintenanceAsync(
        IBaseSubjectLifecycleMaintenanceProcessor processor,
        BaseSubjectLifecycleMaintenanceExecutionRequest request,
        CancellationToken cancellationToken = default) => processor.ExecuteAsync(new InMemoryLifecycleMaintenanceSession(this), request, cancellationToken);

    private sealed class InMemoryLifecycleMaintenanceSession(InMemoryRecordStore owner) : IBaseSubjectLifecycleMaintenanceSession
    {
        public async ValueTask<OperationResult<BaseSubjectLifecycleMaintenanceResult>> ExecuteAsync(BaseSubjectLifecycleMaintenanceExecutionRequest request, CancellationToken cancellationToken = default)
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(request.OperationTimeout);
            if (request.Kind is BaseSubjectLifecycleMaintenanceKind.Prune or BaseSubjectLifecycleMaintenanceKind.RemoveConsumer
                or BaseSubjectLifecycleMaintenanceKind.RebuildDeliveryProjection or BaseSubjectLifecycleMaintenanceKind.RotateScopeProtection)
            {
                try { return await owner.ExecuteBoundedLifecycleMaintenanceAsync(request, deadline.Token).ConfigureAwait(false); }
                catch (InvalidDataException exception)
                {
                    string code = exception.Message.StartsWith("base.subjectLifecycle.", StringComparison.Ordinal) ? exception.Message : BaseSubjectErrorCodes.LifecycleProviderContractInvalid;
                    bool conflict = code is BaseSubjectErrorCodes.ScopeProtectionRotationConflict or BaseSubjectErrorCodes.LifecycleRegistrationConflict;
                    return MaintenanceFailure(code, conflict ? OperationStatus.Conflict : OperationStatus.CapabilityUnavailable,
                        conflict ? ErrorCategory.Conflict : ErrorCategory.Capability);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                { return MaintenanceFailure(BaseSubjectErrorCodes.Timeout, OperationStatus.StoreError, ErrorCategory.Store); }
            }
            await owner._stateGate.WaitAsync(deadline.Token).ConfigureAwait(false);
            try
            {
                if (owner._lifecycleMaintenance is not null)
                    return MaintenanceFailure(BaseSubjectErrorCodes.MaintenanceRequired, OperationStatus.CapabilityUnavailable, ErrorCategory.Capability);
                InMemoryStoreState current = owner._publishedState;
                string receiptKey = ReceiptKey(request.Identity);
                if (current.Receipts.TryGetValue(receiptKey, out InMemoryMutationReceipt? stored)
                    && stored.ExpiresAt > owner._timeProvider.GetUtcNow())
                {
                    if (!CryptographicOperations.FixedTimeEquals(stored.Fingerprint, request.Identity.Fingerprint.ToArray())
                        || !CryptographicOperations.FixedTimeEquals(stored.StructuralDigest, request.PlanChecksum)
                        || stored.Result.Kind != BaseAtomicReceiptResultKind.SubjectLifecycleMaintenance
                        || stored.Result.SubjectLifecycleMaintenance is null)
                        return MaintenanceFailure(BaseMutationRequestErrorCodes.FingerprintConflict, OperationStatus.Conflict, ErrorCategory.Conflict);
                    return OperationResults.Ok(stored.Result.SubjectLifecycleMaintenance with
                    {
                        RollingChecksum = new string(stored.Result.SubjectLifecycleMaintenance.RollingChecksum.AsSpan()),
                        Duplicate = true,
                    });
                }
                InMemorySubjectContractState? contract = request.ContractId is null ? null : current.SubjectContracts.GetValueOrDefault(SubjectContractKey(request.ContractId, request.ContractVersion!.Value));
                long restoreEpoch = contract?.RestoreEpoch ?? current.SubjectContracts.Values.Select(static value => value.RestoreEpoch).DefaultIfEmpty(0).Max();
                // ExpectedStoreGeneration is installed schema/store authority, not the
                // volatile immutable-root revision used to detect concurrent writes.
                // InMemory has one hard-broken installed schema generation.
                if (request.ExpectedStoreGeneration != 1 || request.ExpectedSchemaGeneration != 1 || restoreEpoch != request.ExpectedRestoreEpoch
                    || current.SubjectLifecycleDeliveryEpoch != request.ExpectedDeliveryEpoch || request.ExpectedScopeProtectionGeneration != owner._subjectScopeProtectionGeneration
                    || !string.Equals(request.ExpectedScopeProtectionKeyId, owner._subjectScopeProtectionKeyId, StringComparison.Ordinal))
                    return MaintenanceFailure(BaseSubjectErrorCodes.ScopeProtectionRotationConflict, OperationStatus.Conflict, ErrorCategory.Conflict);
                if (request.ExpectedProjectionGeneration is long expectedProjection
                    && (!current.SubjectLifecycleConsumers.TryGetValue($"{request.ConsumerId}\n{request.ConsumerVersion}", out InMemorySubjectLifecycleConsumerProjection? installedProjection)
                        || installedProjection.ProjectionGeneration != expectedProjection))
                    return MaintenanceFailure(BaseSubjectErrorCodes.LifecycleRegistrationConflict, OperationStatus.Conflict, ErrorCategory.Conflict);
                InMemoryStoreState working = current.Clone(); var changedKeys = new List<string>(); long examined = 0;
                long? projectionGeneration = null;
                switch (request.Kind)
                {
                    case BaseSubjectLifecycleMaintenanceKind.MarkCheckpointOvertaken:
                        if (!MarkOvertaken(working, request, changedKeys, ref examined)) return MaintenanceFailure(BaseSubjectErrorCodes.LifecycleRegistrationConflict, OperationStatus.Conflict, ErrorCategory.Conflict);
                        projectionGeneration = working.SubjectLifecycleConsumers[$"{request.ConsumerId}\n{request.ConsumerVersion}"].ProjectionGeneration;
                        break;
                    default:
                        return MaintenanceFailure(BaseSubjectErrorCodes.LifecycleProviderContractInvalid, OperationStatus.CapabilityUnavailable, ErrorCategory.Capability);
                }
                byte[] canonical = Encoding.UTF8.GetBytes(string.Join('\n', changedKeys.Order(StringComparer.Ordinal)));
                var result = new BaseSubjectLifecycleMaintenanceResult
                {
                    Kind = request.Kind, ExaminedCount = examined, ChangedCount = changedKeys.Count, CanonicalBytes = canonical.LongLength,
                    RollingChecksum = Convert.ToHexStringLower(SHA256.HashData(canonical)), DeliveryEpoch = working.SubjectLifecycleDeliveryEpoch,
                    ProjectionGeneration = projectionGeneration, Duplicate = false,
                };
                working.Receipts[receiptKey] = new InMemoryMutationReceipt(
                    request.Identity.Fingerprint.ToArray(), request.PlanChecksum.ToArray(),
                    new BaseAtomicReceiptResult
                    {
                        Kind = BaseAtomicReceiptResultKind.SubjectLifecycleMaintenance,
                        Mutations = [],
                        SubjectLifecycleMaintenance = result with { RollingChecksum = new string(result.RollingChecksum.AsSpan()) },
                    },
                    owner._timeProvider.GetUtcNow().AddDays(30));
                long publishedStoreGeneration = checked(owner._generation + 1);
                owner._publishedState = working;
                owner._generation = publishedStoreGeneration;
                return OperationResults.Ok(result);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            { return MaintenanceFailure(BaseSubjectErrorCodes.Timeout, OperationStatus.StoreError, ErrorCategory.Store); }
            finally { owner._stateGate.Release(); }
        }

        private bool MarkOvertaken(InMemoryStoreState state, BaseSubjectLifecycleMaintenanceExecutionRequest request, List<string> changed, ref long examined)
        {
            string consumerKey = $"{request.ConsumerId}\n{request.ConsumerVersion}";
            if (!state.SubjectLifecycleConsumers.TryGetValue(consumerKey, out InMemorySubjectLifecycleConsumerProjection? projection)) return false;
            BaseProtectedSubjectScope protectedScope = owner._subjectScopes.Protect(request.Scope!, owner._subjectScopeProtectionKey);
            string key = ProtectedScopeKey(request.ConsumerId!, request.ConsumerVersion!.Value, protectedScope);
            state.SubjectLifecycleCheckpoints.TryGetValue(key, out InMemorySubjectLifecycleCheckpointState? checkpoint);
            if (checkpoint?.Overtaken == true) return false;
            BaseSubjectLifecycleOrderingBoundary? through = checkpoint?.Through ?? projection.Cutoff;
            if (request.RetainedFrom is null || through is not null && CompareBoundary(through, request.RetainedFrom) >= 0) return false;
            if (!state.SubjectLifecycleFacts.Any(row => row.Fact.ContractId == projection.ContractId
                && row.Fact.ContractVersion == projection.ContractVersion
                && ProtectedScopeEquals(row.Scope, protectedScope)
                && CompareBoundary(row.Boundary, request.RetainedFrom) == 0)) return false;
            DateTimeOffset authorityTime = checkpoint?.AdvancedAtUtc ?? projection.InstalledAtUtc;
            if (owner._timeProvider.GetUtcNow() < authorityTime.Add(projection.MaximumCheckpointLag)) return false;
            long generation = checkpoint is null ? 1 : checked(checkpoint.Generation + 1);
            state.SubjectLifecycleCheckpoints[key] = checkpoint is null
                ? new(request.ConsumerId!, request.ConsumerVersion.Value, projection.ConsumerChecksum, projection.ContractId, projection.ContractVersion,
                    projection.ProjectionGeneration, protectedScope, projection.Cutoff, generation, projection.InstalledAtUtc, true)
                : checkpoint with { Overtaken = true, Generation = generation };
            examined = 1;
            changed.Add($"checkpoint\0{key}\0{generation}\0{request.RetainedFrom.CommitPosition.Value}");
            return true;
        }

        private static OperationResult<BaseSubjectLifecycleMaintenanceResult> MaintenanceFailure(string code, OperationStatus status, ErrorCategory category) => new() { Status = status, Error = new BaseError { Code = code, Category = category, Message = "The subject lifecycle maintenance operation failed." } };
    }

    private static OperationResult<BaseSubjectLifecycleProviderReconciliationPage> LifecycleReconciliationFailure(string code, OperationStatus status = OperationStatus.StoreError, ErrorCategory category = ErrorCategory.Store) => new()
    { Status = status, Error = new BaseError { Code = code, Message = "The subject lifecycle reconciliation operation failed.", Category = category } };
    private static OperationResult<BaseSubjectLifecycleProviderInspection> LifecycleInspectionFailure(string code, OperationStatus status = OperationStatus.StoreError, ErrorCategory category = ErrorCategory.Store) => new()
    { Status = status, Error = new BaseError { Code = code, Message = "The subject lifecycle inspection operation failed.", Category = category } };

    private sealed class BaseLifecycleBoundaryComparer : IComparer<BaseSubjectLifecycleOrderingBoundary>
    {
        internal static BaseLifecycleBoundaryComparer Instance { get; } = new();
        public int Compare(BaseSubjectLifecycleOrderingBoundary? left, BaseSubjectLifecycleOrderingBoundary? right)
        {
            if (ReferenceEquals(left, right)) return 0; if (left is null) return -1; if (right is null) return 1;
            int comparison = left.CommitPosition.Value.CompareTo(right.CommitPosition.Value);
            if (comparison != 0) return comparison;
            comparison = string.CompareOrdinal(left.SubjectId.Value, right.SubjectId.Value); if (comparison != 0) return comparison;
            comparison = left.AuthorityEpoch.ToArray().AsSpan().SequenceCompareTo(right.AuthorityEpoch.ToArray()); if (comparison != 0) return comparison;
            comparison = left.Incarnation.ToArray().AsSpan().SequenceCompareTo(right.Incarnation.ToArray()); if (comparison != 0) return comparison;
            return left.SubjectSequence.CompareTo(right.SubjectSequence);
        }
    }
    /// <inheritdoc />
    public ValueTask<OperationResult<BaseSubjectValidationPlanReceipt[]>> ReadSubjectValidationPlanReceiptsAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        BaseSubjectValidationPlanReceipt[] receipts = _options.ExportedSubjects
            .OrderBy(static value => value.ValidationPlan.Id, StringComparer.Ordinal)
            .ThenBy(static value => value.ValidationPlan.Version)
            .Select(value => new BaseSubjectValidationPlanReceipt
            {
                PlanId = new string(value.ValidationPlan.Id.AsSpan()),
                PlanVersion = value.ValidationPlan.Version,
                PlanChecksum = BaseSubjectContractNormalizer.NormalizePlan(value.ValidationPlan).Checksum,
                StoreInstanceId = new string(_options.StoreId.AsSpan()),
                SchemaGeneration = 1,
                Access = value.ValidationPlan.Access,
                LoweringFormatVersion = 1,
            })
            .ToArray();
        return ValueTask.FromResult(OperationResults.Ok(receipts));
    }

    /// <inheritdoc />
    public async ValueTask<BaseMutationJournalBounds> GetMutationJournalBoundsAsync(
        CancellationToken cancellationToken = default)
    {
        await _stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            InMemoryStoreState state = Volatile.Read(ref _publishedState);
            long highWater = state.GlobalMutationPosition;
            long earliest = state.MutationJournal.Count == 0 ? checked(highWater + 1) : state.MutationJournal.First().Key;
            return new BaseMutationJournalBounds(
                new BaseMutationJournalPosition(earliest),
                new BaseMutationJournalPosition(highWater),
                RestoreEpoch: 0);
        }
        finally
        {
            _stateGate.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask<BaseMutationJournalPage> ReadMutationJournalAsync(
        BaseMutationJournalReadRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentOutOfRangeException.ThrowIfNegative(request.After.Value);
        if (request.Through is { Value: < 0 })
            throw new ArgumentOutOfRangeException(nameof(request), "Journal boundary cannot be negative.");
        if (request.Limit is < 1 or > 1_024)
            throw new ArgumentOutOfRangeException(nameof(request), "Journal read limit must be between 1 and 1024.");

        await _stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            InMemoryStoreState state = Volatile.Read(ref _publishedState);
            long highWater = Math.Min(request.Through?.Value ?? state.GlobalMutationPosition, state.GlobalMutationPosition);
            long earliest = state.MutationJournal.Count == 0 ? checked(state.GlobalMutationPosition + 1) : state.MutationJournal.First().Key;
            BaseMutationJournalEntry[] candidates = state.MutationJournal
                .Where(pair => pair.Key > request.After.Value && pair.Key <= highWater)
                .Take(checked(request.Limit + 1))
                .Select(static pair => CloneJournalEntry(pair.Value))
                .ToArray();
            bool hasMore = candidates.Length > request.Limit;
            return new BaseMutationJournalPage
            {
                Entries = hasMore ? candidates[..request.Limit] : candidates,
                HighWatermark = new BaseMutationJournalPosition(highWater),
                Earliest = new BaseMutationJournalPosition(earliest),
                HasMore = hasMore,
            };
        }
        finally
        {
            _stateGate.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask<BaseMutationJournalEntry?> FindMutationJournalEntryAsync(
        string eventId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventId);
        await _stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            BaseMutationJournalEntry? entry = Volatile.Read(ref _publishedState).MutationJournal.Values.FirstOrDefault(candidate =>
                candidate.Kind == BaseMutationJournalEntryKind.RecordMutation
                && string.Equals(candidate.RecordMutation?.EventId, eventId, StringComparison.Ordinal));
            return entry is null ? null : CloneJournalEntry(entry);
        }
        finally
        {
            _stateGate.Release();
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

    private static RecordSnapshot? CloneSnapshot(RecordSnapshot? snapshot) => snapshot is null ? null : snapshot with
    {
        Payload = snapshot.Payload is null ? null : RecordCloneHelpers.ClonePayload(snapshot.Payload),
        Metadata = snapshot.Metadata is null ? null : RecordCloneHelpers.CloneMetadata(snapshot.Metadata),
    };

    /// <inheritdoc />
    public async ValueTask<OperationResult<BaseSubjectEpochRotationResult>> RotateEpochAsync(
        BaseSubjectEpochRotationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.ContractId)
            || request.ContractVersion < 1
            || request.ExpectedStateGeneration < 1
            || !string.Equals(request.DestructiveIntent, "rotate-subject-authority-epoch", StringComparison.Ordinal))
        {
            return SubjectAdministrationFailure<BaseSubjectEpochRotationResult>(
                BaseSubjectErrorCodes.ContractInvalid,
                OperationStatus.ValidationFailed,
                ErrorCategory.Validation);
        }

        await _stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_lifecycleMaintenance is not null)
                return SubjectAdministrationFailure<BaseSubjectEpochRotationResult>(BaseSubjectErrorCodes.MaintenanceRequired, OperationStatus.CapabilityUnavailable, ErrorCategory.Capability);
            InMemoryStoreState current = Volatile.Read(ref _publishedState);
            string key = SubjectContractKey(request.ContractId, request.ContractVersion);
            if (!current.SubjectContracts.TryGetValue(key, out InMemorySubjectContractState? contract))
                return SubjectAdministrationFailure<BaseSubjectEpochRotationResult>(BaseSubjectErrorCodes.ContractInvalid, OperationStatus.ValidationFailed, ErrorCategory.Validation);
            if (contract.StateGeneration != request.ExpectedStateGeneration)
                return SubjectAdministrationFailure<BaseSubjectEpochRotationResult>(BaseSubjectErrorCodes.SchemaGenerationChanged, OperationStatus.Conflict, ErrorCategory.Conflict);
            if (contract.StateGeneration == long.MaxValue)
                return SubjectAdministrationFailure<BaseSubjectEpochRotationResult>(BaseSubjectErrorCodes.ContractInvalid, OperationStatus.ValidationFailed, ErrorCategory.Validation);

            InMemoryStoreState working = current.Clone();
            BaseSubjectAuthorityEpoch replacement = BaseSubjectAuthorityEpoch.Create();
            long examined = 0;
            long rewrittenCount = 0;
            foreach (CollectionDefinition collection in _options.Collections ?? [])
            {
                FieldDefinition[] fields = (collection.Fields ?? []).Where(field =>
                    field.SubjectReference is { } subjectReference
                    && string.Equals(subjectReference.ContractId, request.ContractId, StringComparison.Ordinal)
                    && subjectReference.ContractVersion == request.ContractVersion).ToArray();
                if (fields.Length == 0 || !working.Collections.TryGetValue(collection.Id, out InMemoryCollectionState? collectionState))
                    continue;

                foreach ((string recordId, StoredRecord record) in collectionState.RecordsById.ToArray())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    examined = checked(examined + 1);
                    Dictionary<string, JsonElement> values = PayloadFields(record.Payload);
                    bool changed = false;
                    foreach (FieldDefinition field in fields)
                    {
                        if (!values.TryGetValue(field.WireName, out JsonElement value)
                            || value.ValueKind == JsonValueKind.Null)
                            continue;
                        if (!BaseSubjectReferenceEncoding.TryRewriteAuthorityEpoch(
                                value, contract.AuthorityEpoch, replacement, out JsonElement rewritten))
                        {
                            return SubjectAdministrationFailure<BaseSubjectEpochRotationResult>(BaseSubjectErrorCodes.ProviderContractInvalid);
                        }
                        values[field.WireName] = rewritten;
                        changed = true;
                        rewrittenCount = checked(rewrittenCount + 1);
                    }
                    if (!changed) continue;
                    RevisionToken revision = NextRevision(working);
                    RecordMetadata metadata = record.Metadata with
                    {
                        Revision = revision,
                        ETag = ETag(revision),
                        UpdatedAt = _timeProvider.GetUtcNow(),
                    };
                    collectionState.RecordsById[recordId] = record with
                    {
                        Payload = new RecordPayload { Kind = RecordPayloadKind.FieldMap, Fields = values },
                        Metadata = metadata,
                    };
                    foreach ((string projectionKey, InMemoryVectorProjectionState projection) in working.VectorProjections)
                    {
                        if (projectionKey.StartsWith(collection.Id + "\n", StringComparison.Ordinal)
                            && projection.Carriers.TryGetValue(recordId, out InMemoryVectorCarrier? carrier))
                            projection.Carriers[recordId] = carrier with { Revision = revision };
                    }
                }
            }

            long previous = contract.StateGeneration;
            long published = checked(previous + 1);
            long position = checked(++working.GlobalMutationPosition);
            var journalPosition = new BaseMutationJournalPosition(position);
            string digest = BaseSubjectPublicationIntegrity.Compute(
                contract.ContractId, contract.ContractVersion, contract.ContractChecksum,
                previous, published, contract.RestoreEpoch,
                BaseSubjectAuthorityPublicationKind.EpochRotation, journalPosition, replacement);
            var receipt = new BaseSubjectCurrentPublicationReceipt
            {
                PreviousStateGeneration = previous,
                PublishedStateGeneration = published,
                RestoreEpoch = contract.RestoreEpoch,
                Kind = BaseSubjectAuthorityPublicationKind.EpochRotation,
                OriginalPublicationPosition = journalPosition,
                PublicationDigest = digest,
            };
            working.SubjectContracts[key] = contract with
            {
                AuthorityEpoch = replacement,
                StateGeneration = published,
                CurrentPublicationReceipt = receipt,
            };
            var publication = new BaseSubjectAuthorityPublicationFact
            {
                Position = journalPosition,
                ContractId = contract.ContractId,
                ContractVersion = contract.ContractVersion,
                PreviousStateGeneration = previous,
                PublishedStateGeneration = published,
                RestoreEpoch = contract.RestoreEpoch,
                Kind = BaseSubjectAuthorityPublicationKind.EpochRotation,
            };
            working.MutationJournal.Add(position, new BaseMutationJournalEntry
            {
                Kind = BaseMutationJournalEntryKind.SubjectAuthorityPublication,
                Position = journalPosition,
                SubjectAuthorityPublication = publication,
            });
            // The protected membership seek is derived exclusively from committed
            // membership rows. Rebuild it at the publication boundary so no
            // session-local mutation path can publish memberships without their
            // exact consumer/contract/scope index authority.
            working.RebuildSubjectLifecycleMembershipIndex();
            Volatile.Write(ref _publishedState, working);
            checked { _generation++; }
            return OperationResults.Ok(new BaseSubjectEpochRotationResult
            {
                ContractId = contract.ContractId,
                ContractVersion = contract.ContractVersion,
                PreviousStateGeneration = previous,
                PublishedStateGeneration = published,
                PublicationPosition = journalPosition,
                ExaminedRecords = examined,
                RewrittenReferences = rewrittenCount,
            });
        }
        finally
        {
            _stateGate.Release();
        }
    }

    /// <inheritdoc />
    public ValueTask<OperationResult<BaseSubjectCurrentPublicationState[]>> ReadCurrentSubjectPublicationsAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        InMemoryStoreState state = Volatile.Read(ref _publishedState);
        BaseSubjectCurrentPublicationState[] publications = state.SubjectContracts.Values
            .OrderBy(static contract => contract.ContractId, StringComparer.Ordinal)
            .ThenBy(static contract => contract.ContractVersion)
            .Select(static contract => new BaseSubjectCurrentPublicationState
            {
                ContractId = new string(contract.ContractId.AsSpan()),
                ContractVersion = contract.ContractVersion,
                ContractChecksum = new string(contract.ContractChecksum.AsSpan()),
                AuthorityEpoch = new BaseSubjectAuthorityEpoch(contract.AuthorityEpoch.ToArray()),
                Receipt = contract.CurrentPublicationReceipt with
                {
                    PublicationDigest = new string(contract.CurrentPublicationReceipt.PublicationDigest.AsSpan()),
                },
            }).ToArray();
        return ValueTask.FromResult(OperationResults.Ok(publications));
    }

    private static Dictionary<string, JsonElement> PayloadFields(RecordPayload payload) => payload.Kind switch
    {
        RecordPayloadKind.FieldMap => payload.Fields?.ToDictionary(
            static pair => pair.Key,
            static pair => pair.Value.Clone(),
            StringComparer.Ordinal) ?? [],
        RecordPayloadKind.Json when payload.Json.ValueKind == JsonValueKind.Object => payload.Json.EnumerateObject().ToDictionary(
            static property => property.Name,
            static property => property.Value.Clone(),
            StringComparer.Ordinal),
        _ => throw new InvalidOperationException(BaseSubjectErrorCodes.ProviderContractInvalid),
    };

    private static OperationResult<T> SubjectAdministrationFailure<T>(
        string code,
        OperationStatus status = OperationStatus.StoreError,
        ErrorCategory category = ErrorCategory.Store) => new()
    {
        Status = status,
        Error = new BaseError
        {
            Code = code,
            Message = "The subject administration operation failed.",
            Category = category,
        },
    };

    /// <inheritdoc />
    public ValueTask<OperationResult<BaseAtomicMutationAuthorityRequirement>> CaptureAtomicMutationAuthorityRequirementAsync(
        string applicationId,
        ImmutableArray<CollectionDefinition> collections,
        BaseAtomicMutationExecutionLimits limits,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(limits);
        if (_lifecycleMaintenance is not null)
            return ValueTask.FromResult(LifecycleMaintenanceRequired<BaseAtomicMutationAuthorityRequirement>());
        BaseCollectionGenerationRequirement[] generations = collections
            .OrderBy(static value => value.Id, StringComparer.Ordinal)
            .Select(value => new BaseCollectionGenerationRequirement
            {
                CollectionId = new string(value.Id.AsSpan()),
                CollectionGeneration = Volatile.Read(ref _generation),
            }).ToArray();
        if (generations.Select(static value => value.CollectionId).Distinct(StringComparer.Ordinal).Count() != generations.Length)
            throw new ArgumentException("Collection authority requests must be unique.", nameof(collections));
        return ValueTask.FromResult(OperationResults.Ok(new BaseAtomicMutationAuthorityRequirement
        {
            ApplicationId = new string(applicationId.AsSpan()),
            StoreInstanceId = new string(_options.StoreId.AsSpan()),
            RestoreEpoch = 0,
            SchemaGeneration = 1,
            Collections = [.. generations],
        }));
    }

    public async ValueTask<OperationResult<IInMemoryProjectionReadSession>> CaptureAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_lifecycleMaintenance is not null)
                return LifecycleMaintenanceRequired<IInMemoryProjectionReadSession>();
            InMemoryVectorRootLease lease = RetainVectorRoot();
            return OperationResults.Ok<IInMemoryProjectionReadSession>(new InMemoryProjectionReadSession(
                lease,
                _generation,
                VectorIdentityDigest,
                _options.Collections ?? []));
        }
        finally
        {
            _stateGate.Release();
        }
    }

    public ValueTask<OperationResult<IInMemoryProjectionReplacement>> BeginReplacementAsync(
        long expectedRootGeneration,
        long expectedProjectionGeneration,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (expectedRootGeneration < 0 || expectedProjectionGeneration < 1)
            return ValueTask.FromResult(OperationResults.ValidationFailed<IInMemoryProjectionReplacement>(new BaseError
            {
                Code = "base.vector.inMemory.projectionInvalid",
                Message = "The in-memory projection replacement is invalid.",
                Category = ErrorCategory.Validation,
            }));
        return ValueTask.FromResult(OperationResults.Ok<IInMemoryProjectionReplacement>(new InMemoryProjectionReplacement(this, expectedRootGeneration, expectedProjectionGeneration)));
    }

    internal async ValueTask<OperationResult<BaseInMemoryProjectionReplacementOutcome>> PublishProjectionReplacementAsync(
        long expectedRootGeneration,
        long expectedProjectionGeneration,
        BaseInMemoryProjectionIndexHandle handle,
        InMemoryVectorProjectionState replacement,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_lifecycleMaintenance is not null)
                return OperationResults.Ok(BaseInMemoryProjectionReplacementOutcome.ProjectionGenerationChanged);
            if (_generation != expectedRootGeneration)
                return OperationResults.Ok(BaseInMemoryProjectionReplacementOutcome.RootGenerationChanged);
            string key = handle.Collection.Id + "\n" + handle.Index.Id;
            InMemoryStoreState captured = Volatile.Read(ref _publishedState);
            long currentGeneration = captured.VectorProjections.GetValueOrDefault(key)?.Generation ?? 1;
            if (currentGeneration != expectedProjectionGeneration)
                return OperationResults.Ok(BaseInMemoryProjectionReplacementOutcome.ProjectionGenerationChanged);
            if (handle.Owner.RootGeneration != expectedRootGeneration ||
                !string.Equals(handle.Owner.SchemaDigest, HPDBaseStoreInstallationContext.ComputeSchemaDigest(_options.Collections ?? []), StringComparison.Ordinal) ||
                handle.Generation != expectedProjectionGeneration ||
                replacement.Generation != checked(expectedProjectionGeneration + 1) ||
                replacement.AppliedThrough != captured.GlobalMutationPosition ||
                replacement.PurgeGeneration != (captured.Collections.GetValueOrDefault(handle.Collection.Id)?.PurgeGeneration ?? 0))
                return OperationResults.Ok(BaseInMemoryProjectionReplacementOutcome.InvalidState);

            InMemoryStoreState working = captured.Clone();
            working.VectorProjections[key] = replacement.Clone();
            long carriers = 0, bytes = 0;
            foreach (InMemoryVectorProjectionState projection in working.VectorProjections.Values)
            foreach (InMemoryVectorCarrier carrier in projection.Carriers.Values)
            {
                carriers = checked(carriers + 1);
                bytes = checked(bytes + (long)carrier.Vector.Dimensions * sizeof(float));
            }
            if (carriers > _options.MaxVectorIndexedRecords || bytes > _options.MaxVectorBytes)
                return OperationResults.Ok(BaseInMemoryProjectionReplacementOutcome.CapacityExceeded);
            Volatile.Write(ref _publishedState, working);
            _generation++;
            return OperationResults.Ok(BaseInMemoryProjectionReplacementOutcome.Published);
        }
        catch (OverflowException)
        {
            return OperationResults.Ok(BaseInMemoryProjectionReplacementOutcome.InvalidState);
        }
        finally
        {
            _stateGate.Release();
        }
    }

    internal InMemoryStoreState CaptureVectorRoot() => Volatile.Read(ref _publishedState);

    private readonly HPDBaseInMemoryStoreOptions _options;
    private readonly BaseQueryCursorCodec _queryCursors;
    private readonly BaseSubjectScopeProtector _subjectScopes;
    private readonly BaseOpaqueTokenProtector _subjectScopeTokens;
    private string _subjectScopeProtectionKeyId;
    private byte _subjectScopeProtectionKey;
    private long _subjectScopeProtectionGeneration = 1;
    private readonly TimeProvider _timeProvider;
    private readonly IInMemoryAtomicMutationProjection? _vectorProjection;
    private readonly SemaphoreSlim _stateGate = new(1, 1);
    private readonly Lock _vectorLeaseGate = new();
    private readonly Dictionary<InMemoryStoreState, int> _retainedVectorRoots = new(ReferenceEqualityComparer.Instance);
    private InMemoryStoreState _publishedState = new();
    private long _generation;
    private readonly string? _vectorIdentityDigest;
    internal string VectorIdentityDigest => _vectorIdentityDigest ?? throw new InvalidOperationException("base.vector.providerUnavailable");

    internal InMemoryVectorRootLease RetainVectorRoot()
    {
        InMemoryStoreState root = CaptureVectorRoot();
        lock (_vectorLeaseGate) _retainedVectorRoots[root] = _retainedVectorRoots.GetValueOrDefault(root) + 1;
        return new InMemoryVectorRootLease(root, ReleaseVectorRoot);
    }

    private void ReleaseVectorRoot(InMemoryStoreState root)
    {
        lock (_vectorLeaseGate)
        {
            int count = _retainedVectorRoots.GetValueOrDefault(root);
            if (count <= 1) _retainedVectorRoots.Remove(root); else _retainedVectorRoots[root] = count - 1;
        }
    }

    internal ValueTask<OperationResult> InitializeVectorProjectionAsync(CancellationToken cancellationToken) =>
        _vectorProjection is null
            ? ValueTask.FromResult(OperationResults.NoContent())
            : _vectorProjection.InitializeAsync(new BaseInMemoryProjectionInitializationContext(_options), cancellationToken);

    /// <summary>
    /// Initializes a new store using configured options.
    /// </summary>
    /// <param name="options">The configured InMemory options.</param>
    public InMemoryRecordStore(
        IOptions<HPDBaseInMemoryStoreOptions> options,
        BaseOpaqueTokenProtector tokenProtector,
        TimeProvider timeProvider)
        : this(options.Value, tokenProtector, timeProvider)
    {
    }

    /// <summary>
    /// Initializes a new store using the supplied options, or defaults when omitted.
    /// </summary>
    /// <param name="options">The InMemory options.</param>
    public InMemoryRecordStore(HPDBaseInMemoryStoreOptions? options = null)
        : this(options, CreateProcessLocalTokenProtector(), TimeProvider.System)
    {
    }

    internal InMemoryRecordStore(
        HPDBaseInMemoryStoreOptions? options,
        BaseOpaqueTokenProtector tokenProtector,
        TimeProvider timeProvider)
    {
        _options = options ?? new HPDBaseInMemoryStoreOptions();
        _timeProvider = timeProvider;
        _queryCursors = new BaseQueryCursorCodec(tokenProtector, timeProvider);
        _subjectScopeTokens = tokenProtector;
        _subjectScopes = new BaseSubjectScopeProtector(tokenProtector);
        _subjectScopeProtectionKey = tokenProtector.ActiveKeyId;
        _subjectScopeProtectionKeyId = _subjectScopeProtectionKey.ToString(CultureInfo.InvariantCulture);
        if ((_options.Collections ?? []).Any(static collection => (collection.VectorIndexes ?? []).Length != 0))
        {
            _vectorProjection = new InMemoryVectorMutationProjection();
            _vectorIdentityDigest = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        }
        ValidateOptions(_options);
        foreach (BaseExportedSubjectDefinition subject in _options.ExportedSubjects)
        {
            string key = SubjectContractKey(subject.Id, subject.Version);
            BaseSubjectAuthorityEpoch epoch = BaseSubjectAuthorityEpoch.Create();
            long position = checked(++_publishedState.GlobalMutationPosition);
            var publicationPosition = new BaseMutationJournalPosition(position);
            string checksum = BaseSubjectContractGraph.Checksum(subject);
            string digest = BaseSubjectPublicationIntegrity.Compute(
                subject.Id, subject.Version, checksum, 0, 1, 0,
                BaseSubjectAuthorityPublicationKind.InitialInstallation, publicationPosition, epoch);
            var publication = new BaseSubjectAuthorityPublicationFact
            {
                Position = publicationPosition,
                ContractId = subject.Id,
                ContractVersion = subject.Version,
                PreviousStateGeneration = 0,
                PublishedStateGeneration = 1,
                RestoreEpoch = 0,
                Kind = BaseSubjectAuthorityPublicationKind.InitialInstallation,
            };
            var receipt = new BaseSubjectCurrentPublicationReceipt
            {
                PreviousStateGeneration = 0,
                PublishedStateGeneration = 1,
                RestoreEpoch = 0,
                Kind = BaseSubjectAuthorityPublicationKind.InitialInstallation,
                OriginalPublicationPosition = publicationPosition,
                PublicationDigest = digest,
            };
            if (!_publishedState.SubjectContracts.TryAdd(key, new InMemorySubjectContractState(
                subject.Id,
                subject.Version,
                checksum,
                epoch,
                RestoreEpoch: 0,
                StateGeneration: 1,
                receipt)))
                throw new InvalidOperationException(BaseSubjectErrorCodes.RegistrationConflict);
            _publishedState.MutationJournal.Add(position, new BaseMutationJournalEntry
            {
                Kind = BaseMutationJournalEntryKind.SubjectAuthorityPublication,
                Position = publicationPosition,
                SubjectAuthorityPublication = publication,
            });
        }
        BaseSubjectLifecycleOrderingBoundary? lifecycleCutoff = _publishedState.SubjectLifecycleFacts
            .OrderBy(static row => row.Boundary, BaseLifecycleBoundaryComparer.Instance)
            .Select(static row => row.Boundary).LastOrDefault();
        foreach (BaseSubjectLifecycleConsumerDefinition consumer in _options.SubjectLifecycleConsumers)
        {
            BaseSubjectLifecycleConsumerDefinition normalized = BaseSubjectLifecycleRegistry.Normalize(consumer);
            BaseExportedSubjectDefinition subject = _options.ExportedSubjects.Single(value => value.Id == normalized.ContractId && value.Version == normalized.ContractVersion);
            string checksum = BaseSubjectLifecycleRegistry.Checksum(normalized, BaseSubjectContractGraph.Checksum(subject));
            _publishedState.SubjectLifecycleConsumers.Add($"{normalized.Id}\n{normalized.Version}", new(
                normalized.Id, normalized.Version, checksum, normalized.ContractId, normalized.ContractVersion, 1, lifecycleCutoff, 1,
                _timeProvider.GetUtcNow(), normalized.Limits.MaximumCheckpointLag));
        }
        Capabilities = CreateCapabilities(_options);
        Includes = new RecordIncludeExecutionCapability
        {
            Supported = true,
            MaxDepth = 3,
            MaxIncludes = 8,
            MaxRecords = Math.Min(1_000, _options.MaxPageSize),
            SnapshotConsistency = true,
        };
    }

    private static string SubjectContractKey(string contractId, int version) => $"{contractId}\n{version}";
    private string SubjectKey(
        BaseOwnedSubjectScopeEvidence scope,
        string contractId,
        int version,
        BaseSubjectId subjectId) =>
        $"{(int)scope.Kind}\n{Convert.ToHexString(_subjectScopes.Protect(scope, _subjectScopeProtectionKey).IndexDigest)}\n{contractId}\n{version}\n{subjectId.Value}";

    private static BaseOpaqueTokenProtector CreateProcessLocalTokenProtector() =>
        new(Options.Create(new HPDBaseTokenProtectionOptions
        {
            ActiveKey = new BaseOpaqueTokenKey
            {
                Id = 0,
                Key = RandomNumberGenerator.GetBytes(32),
                IssueNotBefore = DateTimeOffset.UnixEpoch
            }
        }));

    /// <inheritdoc />
    public StoreCapabilityDescriptor Capabilities { get; }

    /// <inheritdoc />
    public ValueTask<OperationResult<RecordPage>> ListAsync(
        CollectionDefinition collection,
        RecordQuery query,
        OperationContext context,
        CancellationToken cancellationToken = default) =>
        HPDBaseInMemoryTelemetry.TraceAsync(
            HPDBaseTelemetrySpans.StoreList,
            BaseOperationKind.List,
            _options.StoreId,
            CollectionIdForTelemetry(collection),
            () => ListCoreAsync(collection, query, context, cancellationToken));

    private ValueTask<OperationResult<RecordPage>> ListCoreAsync(
        CollectionDefinition collection,
        RecordQuery query,
        OperationContext context,
        CancellationToken cancellationToken = default)
    {
        if (_lifecycleMaintenance is not null) return ValueTask.FromResult(LifecycleMaintenanceRequired<RecordPage>());
        ArgumentNullException.ThrowIfNull(collection);
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        if (InMemoryValidation.ValidateCollectionId<RecordPage>(collection.Id) is { } collectionError)
        {
            return ValueTask.FromResult(collectionError);
        }

        if (ValidateUnsupportedQuery<RecordPage>(query, allowCount: true) is { } queryError)
        {
            return ValueTask.FromResult(queryError);
        }

        var published = Volatile.Read(ref _publishedState);
        InMemoryCollectionState? collectionState = GetCollectionOrNull(published, collection.Id);
        var snapshot = collectionState?.RecordsById.Values
            .OrderBy(record => record.AppendPosition)
            .ThenBy(record => record.Id.Value, StringComparer.Ordinal)
            .ToArray() ?? [];

        var filtered = new List<StoredRecord>(snapshot.Length);
        foreach (var record in snapshot)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (query.Filter is null || MatchesFilter(record, query.Filter))
            {
                filtered.Add(record);
            }
        }

        var sortedResult = ApplySort<RecordPage>(filtered, query.Sort);
        if (sortedResult.Result is not null)
        {
            return ValueTask.FromResult(sortedResult.Result);
        }

        var sorted = sortedResult.Value!;
        var total = sorted.Count;
        var pageResult = ApplyPage<RecordPage>(sorted, query, collection, context, collectionState, out var pageInfo);
        if (pageResult.Result is not null)
        {
            return ValueTask.FromResult(pageResult.Result);
        }

        var page = pageResult.Value!;
        var items = ApplySelect(page, query.Select)
            .Select(RecordCloneHelpers.CloneEnvelope)
            .ToArray();

        var recordPage = new RecordPage
        {
            Items = items,
            Page = pageInfo,
            Count = query.Count == QueryCountMode.None
                ? null
                : new CountInfo { Mode = query.Count, Total = total, IsExact = true }
        };

        return ValueTask.FromResult(OperationResults.Ok(recordPage));
    }

    /// <inheritdoc />
    public ValueTask<OperationResult<RecordEnvelope>> GetAsync(
        CollectionDefinition collection,
        RecordId id,
        OperationContext context,
        CancellationToken cancellationToken = default) =>
        HPDBaseInMemoryTelemetry.TraceAsync(
            HPDBaseTelemetrySpans.StoreGet,
            BaseOperationKind.Get,
            _options.StoreId,
            CollectionIdForTelemetry(collection),
            () => GetCoreAsync(collection, id, context, cancellationToken));

    private ValueTask<OperationResult<RecordEnvelope>> GetCoreAsync(
        CollectionDefinition collection,
        RecordId id,
        OperationContext context,
        CancellationToken cancellationToken = default)
    {
        if (_lifecycleMaintenance is not null) return ValueTask.FromResult(LifecycleMaintenanceRequired<RecordEnvelope>());
        var published = Volatile.Read(ref _publishedState);
        return GetFromStateAsync(published, collection, id, context, cancellationToken);
    }

    private static ValueTask<OperationResult<RecordEnvelope>> GetFromStateAsync(
        InMemoryStoreState state,
        CollectionDefinition collection,
        RecordId id,
        OperationContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(collection);
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        if (InMemoryValidation.ValidateCollectionId<RecordEnvelope>(collection.Id) is { } collectionError)
        {
            return ValueTask.FromResult(collectionError);
        }

        if (InMemoryValidation.ValidateRecordId<RecordEnvelope>(id.Value) is { } idError)
        {
            return ValueTask.FromResult(idError);
        }

        var record = GetCollectionOrNull(state, collection.Id)?.RecordsById.GetValueOrDefault(id.Value);
        if (record is null)
        {
            return ValueTask.FromResult(InMemoryResultFactory.NotFound<RecordEnvelope>(id.Value));
        }

        var envelope = RecordCloneHelpers.CloneEnvelope(record);
        return ValueTask.FromResult(InMemoryResultFactory.WithRevision(OperationResults.Ok(envelope), record.Metadata));
    }

    /// <inheritdoc />
    public ValueTask<RecordMutationExecutionResult> ExecuteSingleAsync(
        IAtomicMutationProcessor processor,
        RecordMutationExecutionRequest request,
        CancellationToken cancellationToken = default) =>
        ExecuteMutationAsync(processor, request, cancellationToken);

    /// <inheritdoc />
    public ValueTask<RecordMutationExecutionResult> ExecuteAtomicAsync(
        IAtomicMutationProcessor processor,
        RecordMutationExecutionRequest request,
        CancellationToken cancellationToken = default) =>
        ExecuteMutationAsync(processor, request, cancellationToken);

    public async ValueTask<RecordMutationExecutionResult> ResolveAtomicReceiptAsync(
        IAtomicMutationProcessor processor,
        BaseMutationRequestIdentity identity,
        TimeSpan resolutionTimeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(processor);
        ArgumentNullException.ThrowIfNull(identity);
        if (resolutionTimeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(resolutionTimeout));
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lifetime.CancelAfter(resolutionTimeout);
        InMemoryMutationReceipt? receipt;
        await _stateGate.WaitAsync(lifetime.Token).ConfigureAwait(false);
        try
        {
            _publishedState.Receipts.TryGetValue(ReceiptKey(identity), out receipt);
            if (receipt is null || receipt.ExpiresAt <= _timeProvider.GetUtcNow()
                || !CryptographicOperations.FixedTimeEquals(identity.Fingerprint.ToArray(), receipt.Fingerprint))
                receipt = null;
        }
        finally { _stateGate.Release(); }
        if (receipt is null)
            return Rollback(BaseMutationRequestErrorCodes.ReceiptUnavailable, "The stored mutation receipt cannot be resolved.");
        AtomicMutationProcessingResult resolved = await processor.ResolveReceiptAsync(receipt.Result, lifetime.Token).ConfigureAwait(false);
        return resolved.Outcome == AtomicMutationProcessingOutcome.ReadyToCommit
            ? new RecordMutationExecutionResult(RecordMutationExecutionOutcome.Committed, resolved)
              { RequestDisposition = BaseMutationRequestDisposition.Duplicate }
            : new RecordMutationExecutionResult(RecordMutationExecutionOutcome.RollbackConfirmed, resolved, resolved.Error);
    }

    private async ValueTask<RecordMutationExecutionResult> ExecuteMutationAsync(
        IAtomicMutationProcessor processor,
        RecordMutationExecutionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(processor);
        ArgumentNullException.ThrowIfNull(request);
        ValidateExecutionRequest(request);

        InMemoryStoreState working;
        long capturedGeneration;
        using var acquisitionLifetime =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        acquisitionLifetime.CancelAfter(request.AcquisitionTimeout);
        try
        {
            await _stateGate.WaitAsync(acquisitionLifetime.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return cancellationToken.IsCancellationRequested
                ? CancelledRollback("The mutation was cancelled before its state snapshot was acquired.")
                : Rollback(
                    BaseMutationErrorCodes.TransactionTimeout,
                    "The mutation state snapshot could not be acquired in time.");
        }

        try
        {
            if (_lifecycleMaintenance is not null)
                return Rollback(BaseSubjectErrorCodes.MaintenanceRequired, "Subject lifecycle maintenance must complete before this operation.");
            capturedGeneration = _generation;
            working = Volatile.Read(ref _publishedState).Clone();
        }
        finally
        {
            _stateGate.Release();
        }

        if (acquisitionLifetime.IsCancellationRequested)
        {
            return cancellationToken.IsCancellationRequested
                ? CancelledRollback("The mutation was cancelled while its state snapshot was acquired.")
                : Rollback(
                    BaseMutationErrorCodes.TransactionTimeout,
                    "The mutation state snapshot could not be acquired in time.");
        }

        string? receiptKey = request.AtomicRequest is null ? null : ReceiptKey(request.AtomicRequest.Identity);
        if (receiptKey is not null && working.Receipts.TryGetValue(receiptKey, out InMemoryMutationReceipt? receipt))
        {
            if (receipt.ExpiresAt <= _timeProvider.GetUtcNow())
            {
                working.Receipts.Remove(receiptKey);
            }
            else
            {
                AtomicMutationProcessingResult resolved = await processor.ResolveReceiptAsync(
                    receipt.Result,
                    cancellationToken).ConfigureAwait(false);
                if (resolved.Outcome != AtomicMutationProcessingOutcome.ReadyToCommit)
                    return new RecordMutationExecutionResult(RecordMutationExecutionOutcome.RollbackConfirmed, resolved, resolved.Error);

                bool fingerprintsMatch = CryptographicOperations.FixedTimeEquals(
                    request.AtomicRequest!.Identity.Fingerprint.ToArray(), receipt.Fingerprint);
                bool structuresMatch = CryptographicOperations.FixedTimeEquals(
                    request.AtomicRequest.StructuralDigest, receipt.StructuralDigest);
                if (!fingerprintsMatch || !structuresMatch)
                    return Rollback(BaseMutationRequestErrorCodes.FingerprintConflict, "The mutation request identity conflicts with an existing receipt.");

                return new RecordMutationExecutionResult(RecordMutationExecutionOutcome.Committed, resolved)
                {
                    RequestDisposition = BaseMutationRequestDisposition.Duplicate,
                };
            }
        }

        using var processingLifetime =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        processingLifetime.CancelAfter(request.TransactionTimeout);
        var session = new AtomicSession(this, working);
        AtomicMutationProcessingResult processing;
        try
        {
            var processingTask =
                processor.ProcessAsync(session, processingLifetime.Token).AsTask();
            try
            {
                processing = await processingTask
                    .WaitAsync(processingLifetime.Token)
                    .ConfigureAwait(false);
            }
            catch
            {
                ObserveCompletion(processingTask);
                throw;
            }
        }
        catch (OperationCanceledException)
        {
            await session.CloseAsync().ConfigureAwait(false);
            return cancellationToken.IsCancellationRequested
                ? CancelledRollback("The mutation was cancelled before commit.")
                : Rollback(
                    BaseMutationErrorCodes.TransactionTimeout,
                    "The mutation transaction exceeded its bounded lifetime.");
        }
        catch
        {
            await session.CloseAsync().ConfigureAwait(false);
            return Rollback(
                InMemoryErrorCodes.MutationProcessorFailed,
                "The mutation processor failed.");
        }

        await session.CloseAsync().ConfigureAwait(false);
        if (processingLifetime.IsCancellationRequested)
        {
            return cancellationToken.IsCancellationRequested
                ? CancelledRollback("The mutation was cancelled before commit.", processing)
                : Rollback(
                    BaseMutationErrorCodes.TransactionTimeout,
                    "The mutation transaction exceeded its bounded lifetime.",
                    processing);
        }

        if (processing.Outcome != AtomicMutationProcessingOutcome.ReadyToCommit)
        {
            return new RecordMutationExecutionResult(
                RecordMutationExecutionOutcome.RollbackConfirmed,
                processing,
                processing.Error);
        }

        if (!session.ValidateCommitFinalization(processing))
            return Rollback(BaseSubjectErrorCodes.ProviderContractInvalid, "The mutation commit finalization was invalid.", processing);

        if (request.AtomicRequest is { } identified)
        {
            int receiptBytes = JsonSerializer.SerializeToUtf8Bytes(
                BaseAtomicReceiptWire.From(processing.Receipt),
                HPDBaseJsonSerializerContext.Default.BaseAtomicReceiptWire).Length;
            if (receiptBytes > identified.MaxReceiptBytes)
                return Rollback(BaseMutationRequestErrorCodes.ReceiptTooLarge, "The mutation receipt exceeds its configured bound.", processing);
            working.Receipts[receiptKey!] = new InMemoryMutationReceipt(
                identified.Identity.Fingerprint.ToArray(),
                [.. identified.StructuralDigest],
                processing.Receipt,
                identified.ExpiresAt);
        }

        using var commitLifetime = new CancellationTokenSource(request.CommitCompletionTimeout);
        try
        {
            await _stateGate.WaitAsync(commitLifetime.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return Rollback(
                BaseMutationErrorCodes.TransactionTimeout,
                "The mutation state could not be published in time.",
                processing);
        }

        try
        {
            if (_generation != capturedGeneration)
            {
                return new RecordMutationExecutionResult(
                    RecordMutationExecutionOutcome.ConflictRollbackConfirmed,
                    processing,
                    Error(
                        BaseMutationErrorCodes.TransactionConflict,
                        "The InMemory mutation snapshot was superseded by a concurrent commit."));
            }

            foreach (BaseRecordMutationFact mutation in processing.Mutations.Where(static mutation => mutation.JournalPosition.Value <= 0))
            {
                long position = checked(++working.GlobalMutationPosition);
                working.MutationJournal.Add(position, CreateJournalEntry(
                    mutation with
                    {
                        Event = mutation.Event with { Guarantee = EventDeliveryGuarantee.Transactional },
                        JournalPosition = new BaseMutationJournalPosition(position),
                    },
                    tenantId: null));
            }
            Volatile.Write(ref _publishedState, working);
            _generation++;
            return new RecordMutationExecutionResult(
                RecordMutationExecutionOutcome.Committed,
                processing);
        }
        finally
        {
            _stateGate.Release();
        }
    }

    private static void ValidateExecutionRequest(RecordMutationExecutionRequest request)
    {
        if (request.AcquisitionTimeout <= TimeSpan.Zero
            || request.TransactionTimeout <= TimeSpan.Zero
            || request.CommitCompletionTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Execution timeouts must be positive.");
        }
        if (request.AtomicRequest is { StructuralDigest.Length: not 32 } or { MaxReceiptBytes: < 4096 })
            throw new ArgumentOutOfRangeException(nameof(request), "The identified mutation request bounds are invalid.");
    }

    private static BaseMutationJournalEntry CreateJournalEntry(BaseRecordMutationFact mutation, string? tenantId)
    {
        DateTimeOffset occurredAt = mutation.Event.PublishedAt
            ?? mutation.After?.Metadata.UpdatedAt
            ?? mutation.Before?.Metadata.UpdatedAt
            ?? DateTimeOffset.UnixEpoch;
        return new BaseMutationJournalEntry
        {
            Kind = BaseMutationJournalEntryKind.RecordMutation,
            Position = mutation.JournalPosition,
            RecordMutation = new BaseRecordMutationJournalEntry
            {
                EventId = mutation.Event.EventId,
                Type = mutation.Event.Type,
                SchemaVersion = BaseEventSchemaVersions.V1,
                OccurredAt = occurredAt,
                TenantId = tenantId,
                Operation = mutation.CommittedOperation switch
                {
                    BaseCommittedRecordMutationKind.Create => BaseOperationKind.Create,
                    BaseCommittedRecordMutationKind.Patch => BaseOperationKind.Patch,
                    BaseCommittedRecordMutationKind.Replace => BaseOperationKind.Replace,
                    BaseCommittedRecordMutationKind.Delete => BaseOperationKind.Delete,
                    _ => throw new InvalidOperationException("Unsupported committed mutation kind."),
                },
                Visibility = mutation.Collection.Visibility?.Visibility ?? VisibilityLevel.Public,
                CollectionId = mutation.Collection.Id,
                RecordId = mutation.After?.Id ?? mutation.Before?.Id ?? throw new InvalidOperationException("A journal mutation requires a record identity."),
                Before = Snapshot(mutation.Before),
                After = Snapshot(mutation.After),
            },
        };
    }

    private static RecordSnapshot? Snapshot(RecordEnvelope? record) => record is null ? null : new RecordSnapshot
    {
        CollectionId = record.CollectionId,
        Id = record.Id,
        Payload = RecordCloneHelpers.ClonePayload(record.Payload),
        Metadata = RecordCloneHelpers.CloneMetadata(record.Metadata),
        Redacted = false,
    };

    private static string ReceiptKey(BaseMutationRequestIdentity identity) =>
        string.Concat(identity.Scope, "\u001f", identity.Operation, "\u001f", identity.IdempotencyKey);

    private static RecordMutationExecutionResult Rollback(
        string code,
        string message,
        AtomicMutationProcessingResult? processing = null) =>
        new(
            RecordMutationExecutionOutcome.RollbackConfirmed,
            processing ?? FailedProcessing(code, message),
            Error(code, message));

    private static RecordMutationExecutionResult CancelledRollback(
        string message,
        AtomicMutationProcessingResult? processing = null) =>
        new(
            RecordMutationExecutionOutcome.CancelledRollbackConfirmed,
            processing ?? FailedProcessing(BaseMutationErrorCodes.TransactionTimeout, message),
            Error(BaseMutationErrorCodes.TransactionTimeout, message));

    private static AtomicMutationProcessingResult FailedProcessing(string code, string message) =>
        new(
            AtomicMutationProcessingOutcome.Failed,
            [],
            Error(code, message));

    private static BaseError Error(string code, string message) => new()
    {
        Code = code,
        Message = message,
        Category = ErrorCategory.Store,
        Store = new StoreErrorInfo { Retryable = false }
    };

    private static OperationResult<T>? MutationModeFailure<T>(
        CollectionDefinition collection,
        BaseOperationKind operation)
    {
        bool allowed = operation switch
        {
            BaseOperationKind.Create => collection.MutationMode is
                BaseCollectionMutationMode.Mutable or
                BaseCollectionMutationMode.AppendOnly or
                BaseCollectionMutationMode.AppendOnlyWithAdministrativePurge,
            BaseOperationKind.Patch or BaseOperationKind.Replace =>
                collection.MutationMode == BaseCollectionMutationMode.Mutable,
            BaseOperationKind.Delete or BaseOperationKind.SubjectLifecycleFinalizeRetirement =>
                collection.MutationMode == BaseCollectionMutationMode.Mutable,
            BaseOperationKind.Purge =>
                collection.MutationMode == BaseCollectionMutationMode.AppendOnlyWithAdministrativePurge,
            _ => false
        };
        if (allowed) return null;
        string code = !Enum.IsDefined(collection.MutationMode)
            ? BaseCollectionErrorCodes.MutationModeInvalid
            : collection.MutationMode == BaseCollectionMutationMode.ReadOnly
                ? BaseCollectionErrorCodes.ReadOnlyMutationForbidden
                : operation is BaseOperationKind.Patch or BaseOperationKind.Replace
                    ? BaseCollectionErrorCodes.AppendOnlyUpdateForbidden
                    : operation is BaseOperationKind.Delete or BaseOperationKind.SubjectLifecycleFinalizeRetirement
                        ? BaseCollectionErrorCodes.AppendOnlyDeleteForbidden
                        : BaseCollectionErrorCodes.PurgeUnsupported;
        return InMemoryResultFactory.Unsupported<T>(code, "The collection mutation mode does not permit this operation.");
    }

    private static void ObserveCompletion(Task task) =>
        _ = task.ContinueWith(
            completed => _ = completed.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

    private ValueTask<OperationResult<RecordEnvelope>> CreateCoreAsync(
        InMemoryStoreState working,
        CollectionDefinition collection,
        RecordCreateRequest request,
        OperationContext context,
        CancellationToken cancellationToken = default,
        bool runtimeAssignedId = false)
    {
        ArgumentNullException.ThrowIfNull(collection);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (MutationModeFailure<RecordEnvelope>(collection, BaseOperationKind.Create) is { } modeError)
            return ValueTask.FromResult(modeError);

        if (InMemoryValidation.ValidateCollectionId<RecordEnvelope>(collection.Id) is { } collectionError)
        {
            return ValueTask.FromResult(collectionError);
        }

        var normalizedCreatePayload = NormalizeObjectPayload<RecordEnvelope>(request.Payload);
        if (normalizedCreatePayload.Value is not { } payload)
        {
            return ValueTask.FromResult(normalizedCreatePayload.Result!);
        }

        foreach (var field in payload.Fields ?? [])
        {
            if (InMemoryValidation.ValidateFieldName<RecordEnvelope>(field.Key) is { } fieldError)
            {
                return ValueTask.FromResult(fieldError);
            }
        }

        var id = request.RequestedId ?? new RecordId(NextRecordId(working));
        if (request.RequestedId is not null && !runtimeAssignedId && !_options.AllowClientRequestedIds)
        {
            return ValueTask.FromResult(InMemoryResultFactory.Unsupported<RecordEnvelope>(
                InMemoryErrorCodes.RequestedIdUnsupported,
                "Client-requested ids are disabled for this InMemory store.",
                id.Value));
        }

        if (InMemoryValidation.ValidateRecordId<RecordEnvelope>(id.Value) is { } idError)
            return ValueTask.FromResult(idError);

        var state = GetOrCreateCollection(working, collection.Id);
        if (state.RecordsById.ContainsKey(id.Value))
            return ValueTask.FromResult(InMemoryResultFactory.DuplicateId<RecordEnvelope>(id.Value));

        var now = Now(context);
        var revision = NextRevision(working);
        var metadata = new RecordMetadata
        {
            CreatedAt = now,
            UpdatedAt = now,
            Revision = revision,
            ETag = ETag(revision),
            StoreId = _options.StoreId
        };
        InMemoryCollectionState collectionState = GetOrCreateCollection(working, collection.Id);
        if (collectionState.NextAppendPosition == long.MaxValue)
            return ValueTask.FromResult(InMemoryResultFactory.StoreError<RecordEnvelope>("base.collection.appendPosition.exhausted", "The collection append position is exhausted."));
        var record = new StoredRecord(collection.Id, id, payload, metadata, ++collectionState.NextAppendPosition);
        state.RecordsById.Add(id.Value, record);
        if (state.RecordIdsOrdinal is not null)
            state.RecordIdsOrdinal = state.RecordIdsOrdinal.Add(id.Value);
        return ValueTask.FromResult(InMemoryResultFactory.WithRevision(
            OperationResults.Created(RecordCloneHelpers.CloneEnvelope(record)), metadata));
    }

    private ValueTask<OperationResult<DeleteResult>> DeleteCoreAsync(
        InMemoryStoreState working,
        CollectionDefinition collection,
        RecordId id,
        RecordDeleteRequest request,
        OperationContext context,
        CancellationToken cancellationToken = default,
        Action? relationCheck = null)
    {
        ArgumentNullException.ThrowIfNull(collection);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (MutationModeFailure<DeleteResult>(collection, context.Operation) is { } modeError)
            return ValueTask.FromResult(modeError);

        if (InMemoryValidation.ValidateCollectionId<DeleteResult>(collection.Id) is { } collectionError)
        {
            return ValueTask.FromResult(collectionError);
        }

        if (InMemoryValidation.ValidateRecordId<DeleteResult>(id.Value) is { } idError)
        {
            return ValueTask.FromResult(idError);
        }

        var state = GetCollectionOrNull(working, collection.Id);
        if (state is null || !state.RecordsById.TryGetValue(id.Value, out var current))
            return ValueTask.FromResult(InMemoryResultFactory.NotFound<DeleteResult>(id.Value));

        if (request.ExpectedRevision is { } expected && !RevisionEquals(current.Metadata.Revision, expected))
        {
            return ValueTask.FromResult(InMemoryResultFactory.RevisionConflict<DeleteResult>(expected, current.Metadata.Revision, id.Value));
        }

        if (HasRestrictedIncomingReference(working, collection.Id, id.Value, relationCheck))
        {
            return ValueTask.FromResult(OperationResults.Conflict<DeleteResult>(new BaseError
            {
                Code = "base.relation.deleteRestricted",
                Message = "The record cannot be deleted while it is referenced.",
                Category = ErrorCategory.Conflict
            }));
        }

        var previous = request.ReturnPrevious ? RecordCloneHelpers.CloneEnvelope(current) : null;
        state.RecordsById.Remove(id.Value);
        if (state.RecordIdsOrdinal is not null)
            state.RecordIdsOrdinal = state.RecordIdsOrdinal.Remove(id.Value);
        var result = OperationResults.Deleted(new DeleteResult { Id = id, Deleted = true, Previous = previous });
        return ValueTask.FromResult(InMemoryResultFactory.WithRevision(result, current.Metadata));
    }

    /// <inheritdoc />
    public ValueTask<OperationResult<AsyncStream<RecordEnvelope>>> OpenStreamAsync(
        CollectionDefinition collection,
        RecordQuery query,
        OperationContext context,
        CancellationToken cancellationToken = default) =>
        HPDBaseInMemoryTelemetry.TraceAsync(
            HPDBaseTelemetrySpans.StoreStreamOpen,
            BaseOperationKind.RealtimeSubscribe,
            _options.StoreId,
            CollectionIdForTelemetry(collection),
            () => OpenStreamCoreAsync(collection, query, context, cancellationToken));

    private ValueTask<OperationResult<AsyncStream<RecordEnvelope>>> OpenStreamCoreAsync(
        CollectionDefinition collection,
        RecordQuery query,
        OperationContext context,
        CancellationToken cancellationToken = default)
    {
        if (_lifecycleMaintenance is not null)
            return ValueTask.FromResult(LifecycleMaintenanceRequired<AsyncStream<RecordEnvelope>>());
        ArgumentNullException.ThrowIfNull(collection);
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();
        if (!_options.EnableStreamingCapability)
        {
            return ValueTask.FromResult(InMemoryResultFactory.Unsupported<AsyncStream<RecordEnvelope>>(
                InMemoryErrorCodes.UnsupportedQuery,
                "Streaming is disabled for this HPD.BASE InMemory store.",
                collection.Id));
        }

        if (ValidateUnsupportedQuery<RecordEnvelope>(query, allowCount: false) is { } queryError)
        {
            return ValueTask.FromResult(new OperationResult<AsyncStream<RecordEnvelope>>
            {
                Status = queryError.Status,
                Error = queryError.Error,
                Warnings = queryError.Warnings,
                Diagnostics = queryError.Diagnostics,
                Revision = queryError.Revision,
                Events = queryError.Events
            });
        }

        var stream = new AsyncStream<RecordEnvelope>
        {
            Items = StreamItemsAsync(collection, query, context, cancellationToken),
            Descriptor = new AsyncStreamDescriptor
            {
                StreamId = $"{_options.StoreId}:{collection.Id}",
                Backpressure = AsyncStreamBackpressureMode.Wait,
                DeliveryGuarantee = AsyncStreamDeliveryGuarantee.BestEffort
            }
        };

        return ValueTask.FromResult(OperationResults.Ok(stream));
    }

    private async IAsyncEnumerable<RecordEnvelope> StreamItemsAsync(
        CollectionDefinition collection,
        RecordQuery query,
        OperationContext context,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var queryWithoutCount = query with { Count = QueryCountMode.None };
        var list = await ListAsync(collection, queryWithoutCount, context, cancellationToken).ConfigureAwait(false);
        if (!list.IsSuccess() || list.Value is null)
        {
            yield break;
        }

        var yielded = 0;
        foreach (var item in list.Value.Items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_options.MaxStreamItems is { } maxItems && yielded >= maxItems)
            {
                yield break;
            }

            yielded++;
            yield return RecordCloneHelpers.CloneEnvelope(new StoredRecord(
                item.CollectionId,
                item.Id,
                item.Payload,
                item.Metadata,
                0));
        }
    }

    private ValueTask<OperationResult<RecordEnvelope>> PatchCoreAsync(
        InMemoryStoreState working,
        CollectionDefinition collection,
        RecordId id,
        RecordPatchRequest request,
        OperationContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(collection);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (MutationModeFailure<RecordEnvelope>(collection, BaseOperationKind.Patch) is { } modeError)
            return ValueTask.FromResult(modeError);

        if (InMemoryValidation.ValidateCollectionId<RecordEnvelope>(collection.Id) is { } collectionError)
        {
            return ValueTask.FromResult(collectionError);
        }

        if (InMemoryValidation.ValidateRecordId<RecordEnvelope>(id.Value) is { } idError)
        {
            return ValueTask.FromResult(idError);
        }

        if (request.Patch.Kind != RecordPayloadKind.FieldMap)
        {
            return ValueTask.FromResult(InMemoryResultFactory.Unsupported<RecordEnvelope>(
                InMemoryErrorCodes.PatchUnsupportedShape,
                "Portable InMemory patch requires a field-map payload.",
                id.Value));
        }

        if (request.Patch.Fields is null || request.Patch.Fields.Count == 0)
        {
            return ValueTask.FromResult(InMemoryResultFactory.Validation<RecordEnvelope>(
                InMemoryErrorCodes.EmptyPatch,
                "Patch must contain at least one top-level field.",
                id.Value));
        }

        foreach (var field in request.Patch.Fields)
        {
            if (InMemoryValidation.ValidateFieldName<RecordEnvelope>(field.Key) is { } fieldError)
            {
                return ValueTask.FromResult(fieldError);
            }
        }

        var state = GetCollectionOrNull(working, collection.Id);
        if (state is null || !state.RecordsById.TryGetValue(id.Value, out var current))
            return ValueTask.FromResult(InMemoryResultFactory.NotFound<RecordEnvelope>(id.Value));

        if (request.ExpectedRevision is { } expected && !RevisionEquals(current.Metadata.Revision, expected))
        {
            return ValueTask.FromResult(InMemoryResultFactory.RevisionConflict<RecordEnvelope>(expected, current.Metadata.Revision, id.Value));
        }

        var existingFields = ToFieldMap<RecordEnvelope>(current.Payload);
        if (existingFields.Value is not { } fields)
            return ValueTask.FromResult(existingFields.Result!);

        foreach (var field in request.Patch.Fields)
            fields[field.Key] = field.Value.Clone();

        var updatedPayload = new RecordPayload { Kind = RecordPayloadKind.FieldMap, Fields = fields };
        var updated = MutateRecord(working, current, updatedPayload, context);
        state.RecordsById[id.Value] = updated;
        return ValueTask.FromResult(InMemoryResultFactory.WithRevision(
            OperationResults.Updated(RecordCloneHelpers.CloneEnvelope(updated)), updated.Metadata));
    }

    private ValueTask<OperationResult<RecordEnvelope>> ReplaceCoreAsync(
        InMemoryStoreState working,
        CollectionDefinition collection,
        RecordId id,
        RecordReplaceRequest request,
        OperationContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(collection);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (MutationModeFailure<RecordEnvelope>(collection, BaseOperationKind.Replace) is { } modeError)
            return ValueTask.FromResult(modeError);

        if (InMemoryValidation.ValidateCollectionId<RecordEnvelope>(collection.Id) is { } collectionError)
        {
            return ValueTask.FromResult(collectionError);
        }

        if (InMemoryValidation.ValidateRecordId<RecordEnvelope>(id.Value) is { } idError)
        {
            return ValueTask.FromResult(idError);
        }

        var normalizedReplacePayload = NormalizeObjectPayload<RecordEnvelope>(request.Payload);
        if (normalizedReplacePayload.Value is not { } payload)
        {
            return ValueTask.FromResult(normalizedReplacePayload.Result!);
        }

        foreach (var field in payload.Fields ?? [])
        {
            if (InMemoryValidation.ValidateFieldName<RecordEnvelope>(field.Key) is { } fieldError)
            {
                return ValueTask.FromResult(fieldError);
            }
        }

        var state = GetCollectionOrNull(working, collection.Id);
        if (state is null || !state.RecordsById.TryGetValue(id.Value, out var current))
            return ValueTask.FromResult(InMemoryResultFactory.NotFound<RecordEnvelope>(id.Value));

        if (request.ExpectedRevision is { } expected && !RevisionEquals(current.Metadata.Revision, expected))
        {
            return ValueTask.FromResult(InMemoryResultFactory.RevisionConflict<RecordEnvelope>(expected, current.Metadata.Revision, id.Value));
        }

        var updated = MutateRecord(working, current, payload, context);
        state.RecordsById[id.Value] = updated;
        return ValueTask.FromResult(InMemoryResultFactory.WithRevision(
            OperationResults.Updated(RecordCloneHelpers.CloneEnvelope(updated)), updated.Metadata));
    }

    private StoredRecord MutateRecord(
        InMemoryStoreState working,
        StoredRecord current,
        RecordPayload payload,
        OperationContext context)
    {
        var revision = NextRevision(working);
        var metadata = current.Metadata with
        {
            UpdatedAt = Now(context),
            Revision = revision,
            ETag = ETag(revision)
        };
        return current with
        {
            Payload = RecordCloneHelpers.ClonePayload(payload),
            Metadata = metadata
        };
    }

    private InMemoryCollectionState GetOrCreateCollection(InMemoryStoreState state, string collectionId)
    {
        if (state.Collections.TryGetValue(collectionId, out var collection))
        {
            return collection;
        }

        bool vectorEnabled = (_options.Collections ?? []).Any(definition =>
            string.Equals(definition.Id, collectionId, StringComparison.Ordinal) &&
            (definition.VectorIndexes ?? []).Length != 0);
        collection = new InMemoryCollectionState
        {
            RecordIdsOrdinal = vectorEnabled
                ? System.Collections.Immutable.ImmutableSortedSet.Create<string>(StringComparer.Ordinal)
                : null,
        };
        state.Collections.Add(collectionId, collection);
        return collection;
    }

    private static InMemoryCollectionState? GetCollectionOrNull(InMemoryStoreState state, string collectionId) =>
        state.Collections.GetValueOrDefault(collectionId);

    private bool HasRestrictedIncomingReference(InMemoryStoreState state, string targetCollectionId, string targetRecordId, Action? checkedRelation = null)
    {
        foreach (CollectionDefinition source in _options.Collections ?? [])
        {
            InMemoryCollectionState? sourceState = GetCollectionOrNull(state, source.Id);
            if (sourceState is null) continue;
            foreach (FieldDefinition field in source.Fields ?? [])
            {
                if (field.Relation is not { OwningSide: BaseRelationOwningSide.Source, DeleteBehavior: BaseRelationDeleteBehavior.Restrict } relation ||
                    !string.Equals(relation.TargetCollectionId, targetCollectionId, StringComparison.Ordinal)) continue;
                foreach (StoredRecord record in sourceState.RecordsById.Values)
                {
                    checkedRelation?.Invoke();
                    if (RelationContains(record.Payload, field.WireName, targetRecordId)) return true;
                }
            }
        }
        return false;
    }

    private static bool RelationContains(RecordPayload payload, string fieldName, string targetRecordId)
    {
        if (payload.Fields?.TryGetValue(fieldName, out JsonElement value) != true) return false;
        return value.ValueKind == JsonValueKind.String
            ? string.Equals(value.GetString(), targetRecordId, StringComparison.Ordinal)
            : value.ValueKind == JsonValueKind.Array && value.EnumerateArray().Any(item => item.ValueKind == JsonValueKind.String && string.Equals(item.GetString(), targetRecordId, StringComparison.Ordinal));
    }

    private static string NextRecordId(InMemoryStoreState state) => $"mem:{++state.NextRecordId:x16}";

    private static RevisionToken NextRevision(InMemoryStoreState state) => new($"mem:{++state.NextRevision:x16}");

    private static string ETag(RevisionToken revision) => $"\"{revision.Value}\"";

    private static DateTimeOffset Now(OperationContext context) =>
        context.Now == default ? DateTimeOffset.UtcNow : context.Now;

    private static bool RevisionEquals(RevisionToken? left, RevisionToken right) =>
        left is { } current && string.Equals(current.Value, right.Value, StringComparison.Ordinal);

    private static PayloadNormalizeResult<T> NormalizeObjectPayload<T>(RecordPayload payload)
    {
        if (payload is null)
        {
            return PayloadNormalizeResult<T>.Failure(InMemoryResultFactory.Validation<T>(
                InMemoryErrorCodes.PayloadRequired,
                "A record payload is required."));
        }

        if (payload.Kind == RecordPayloadKind.FieldMap)
        {
            return PayloadNormalizeResult<T>.Success(new RecordPayload
            {
                Kind = RecordPayloadKind.FieldMap,
                Fields = RecordCloneHelpers.CloneFields(payload.Fields)
            });
        }

        if (payload.Json.ValueKind != System.Text.Json.JsonValueKind.Object)
        {
            return PayloadNormalizeResult<T>.Failure(InMemoryResultFactory.Validation<T>(
                InMemoryErrorCodes.ObjectPayloadRequired,
                "JSON record payloads must be objects."));
        }

        var fields = new Dictionary<string, System.Text.Json.JsonElement>(StringComparer.Ordinal);
        foreach (var property in payload.Json.EnumerateObject())
        {
            fields[property.Name] = property.Value.Clone();
        }

        return PayloadNormalizeResult<T>.Success(new RecordPayload
        {
            Kind = RecordPayloadKind.FieldMap,
            Fields = fields
        });
    }

    private static PayloadFieldsResult<T> ToFieldMap<T>(RecordPayload payload)
    {
        var normalized = NormalizeObjectPayload<T>(payload);
        return normalized.Value is null
            ? PayloadFieldsResult<T>.Failure(normalized.Result!)
            : PayloadFieldsResult<T>.Success(RecordCloneHelpers.CloneFields(normalized.Value.Fields));
    }

    private static QueryResult<List<StoredRecord>, T> ApplySort<T>(
        List<StoredRecord> records,
        QuerySort[]? sort)
    {
        if (sort is null || sort.Length == 0)
        {
            return QueryResult<List<StoredRecord>, T>.Success(records);
        }

        foreach (var sortField in sort)
        {
            foreach (var record in records)
            {
                if (TryReadField(record.Payload, sortField.Field, out var sortValue)
                    && sortValue.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                {
                    return QueryResult<List<StoredRecord>, T>.Failure(InMemoryResultFactory.Validation<T>(
                        InMemoryErrorCodes.InvalidQuery,
                        "Object and array values cannot be used as sort keys.",
                        sortField.Field));
                }
            }
        }

        records.Sort((left, right) =>
        {
            foreach (var sortField in sort)
            {
                var leftPresent = TryReadField(left.Payload, sortField.Field, out var leftValue);
                var rightPresent = TryReadField(right.Payload, sortField.Field, out var rightValue);
                var compare = CompareSortValues(leftPresent, leftValue, rightPresent, rightValue, sortField.Nulls);
                if (compare != 0)
                {
                    return sortField.Direction == QuerySortDirection.Desc ? -compare : compare;
                }
            }

            return string.Compare(left.Id.Value, right.Id.Value, StringComparison.Ordinal);
        });

        return QueryResult<List<StoredRecord>, T>.Success(records);
    }

    private QueryResult<StoredRecord[], T> ApplyPage<T>(
        List<StoredRecord> snapshot,
        RecordQuery query,
        CollectionDefinition collection,
        OperationContext context,
        InMemoryCollectionState? collectionState,
        out PageInfo pageInfo)
    {
        var page = query.Page;
        pageInfo = new PageInfo();
        var limit = page?.Limit ?? page?.PerPage ?? _options.DefaultPageSize;

        int offset = 0;
        BaseQueryCursorPayload? cursorPayload = null;
        QueryCursorGuarantee guarantee = collection.MutationMode is
            BaseCollectionMutationMode.AppendOnly or
            BaseCollectionMutationMode.AppendOnlyWithAdministrativePurge
                ? QueryCursorGuarantee.StableHistory
                : QueryCursorGuarantee.Seek;
        long appendHighWater = collectionState?.NextAppendPosition ?? 0;
        long purgeGeneration = collectionState?.PurgeGeneration ?? 0;
        switch (page?.Mode)
        {
            case QueryPaginationMode.Offset:
                offset = page.Offset ?? 0;
                break;
            case QueryPaginationMode.Cursor:
                if (page.CursorDirection != QueryCursorDirection.After)
                {
                    return CursorFailure<T>(BaseQueryErrorCodes.CursorDirectionUnsupported,
                        "The requested cursor direction is not supported.");
                }
                if (!string.IsNullOrWhiteSpace(page.Cursor))
                {
                    BaseQueryCursorReadResult decoded = _queryCursors.Unprotect(
                        page.Cursor, query, limit, _options.StoreId, collection.Id, context,
                        restoreEpoch: 0, schemaGeneration: 0, guarantee, purgeGeneration);
                    if (decoded.Status != BaseQueryCursorStatus.Valid)
                        return CursorFailure<T>(CursorErrorCode(decoded.Status), "The query cursor cannot be continued.");
                    cursorPayload = decoded.Payload;
                    appendHighWater = cursorPayload!.AppendHighWater;
                    snapshot = snapshot
                        .Where(record => guarantee != QueryCursorGuarantee.StableHistory || record.AppendPosition <= appendHighWater)
                        .Where(record => CompareToCursor(record, query.Sort, cursorPayload) > 0)
                        .ToList();
                }
                break;
            case QueryPaginationMode.Page:
            default:
                offset = ((page?.Page ?? 1) - 1) * limit;
                break;
        }

        var items = snapshot.Skip(offset).Take(limit).ToArray();
        var nextOffset = offset + items.Length;
        bool hasMore = nextOffset < snapshot.Count;
        string? nextCursor = null;
        if (hasMore && items.Length != 0)
        {
            try
            {
                nextCursor = _queryCursors.Protect(new BaseQueryCursorPayload
                {
                    Guarantee = guarantee,
                    Direction = QueryCursorDirection.After,
                    RestoreEpoch = 0,
                    SchemaGeneration = 0,
                    AppendHighWater = appendHighWater,
                    PurgeGeneration = purgeGeneration,
                    Keys = CursorKeys(items[^1], query.Sort),
                    RecordId = items[^1].Id.Value
                }, query, limit, _options.StoreId, collection.Id, context);
            }
            catch (BaseQueryCursorKeyTooLargeException)
            {
                return CursorFailure<T>(BaseQueryErrorCodes.CursorKeyTooLarge,
                    "The query ordering key exceeds the cursor bound.");
            }
        }
        pageInfo = new PageInfo
        {
            Page = page?.Mode is null or QueryPaginationMode.Page ? page?.Page ?? 1 : null,
            PerPage = page?.Mode is null or QueryPaginationMode.Page ? limit : null,
            Offset = page?.Mode == QueryPaginationMode.Offset ? offset : null,
            Limit = page?.Mode == QueryPaginationMode.Offset ? limit : null,
            Cursor = page?.Cursor,
            NextCursor = nextCursor,
            HasMore = hasMore
        };
        return QueryResult<StoredRecord[], T>.Success(items);
    }

    private static QueryResult<StoredRecord[], T> CursorFailure<T>(string code, string message) =>
        QueryResult<StoredRecord[], T>.Failure(InMemoryResultFactory.Validation<T>(code, message));

    private static string CursorErrorCode(BaseQueryCursorStatus status) => status switch
    {
        BaseQueryCursorStatus.ScopeMismatch => BaseQueryErrorCodes.CursorScopeMismatch,
        BaseQueryCursorStatus.QueryMismatch => BaseQueryErrorCodes.CursorQueryMismatch,
        BaseQueryCursorStatus.Expired => BaseQueryErrorCodes.CursorExpired,
        BaseQueryCursorStatus.VersionUnsupported => BaseQueryErrorCodes.CursorVersionUnsupported,
        BaseQueryCursorStatus.SchemaChanged => BaseQueryErrorCodes.CursorSchemaChanged,
        BaseQueryCursorStatus.RestoreInvalidated => BaseQueryErrorCodes.CursorRestoreInvalidated,
        BaseQueryCursorStatus.GuaranteeUnavailable => BaseQueryErrorCodes.CursorGuaranteeUnavailable,
        BaseQueryCursorStatus.DirectionUnsupported => BaseQueryErrorCodes.CursorDirectionUnsupported,
        BaseQueryCursorStatus.KeyTooLarge => BaseQueryErrorCodes.CursorKeyTooLarge,
        _ => BaseQueryErrorCodes.CursorInvalid
    };

    private static BaseQueryCursorKey[] CursorKeys(StoredRecord record, QuerySort[]? sort)
    {
        if (sort is null || sort.Length == 0)
            return [new BaseQueryCursorKey(true, record.AppendPosition.ToString(CultureInfo.InvariantCulture))];
        return sort.Select(item => TryReadField(record.Payload, item.Field, out JsonElement value)
            ? new BaseQueryCursorKey(true, value.GetRawText())
            : new BaseQueryCursorKey(false, "null")).ToArray();
    }

    private static int CompareToCursor(StoredRecord record, QuerySort[]? sort, BaseQueryCursorPayload cursor)
    {
        if (sort is null || sort.Length == 0)
        {
            long value = long.Parse(cursor.Keys[0].Json, CultureInfo.InvariantCulture);
            int append = record.AppendPosition.CompareTo(value);
            return append != 0 ? append : string.Compare(record.Id.Value, cursor.RecordId, StringComparison.Ordinal);
        }
        if (cursor.Keys.Length != sort.Length) return 0;
        for (int index = 0; index < sort.Length; index++)
        {
            bool present = TryReadField(record.Payload, sort[index].Field, out JsonElement current);
            using JsonDocument document = JsonDocument.Parse(cursor.Keys[index].Json);
            int compared = CompareSortValues(present, current, cursor.Keys[index].Present, document.RootElement, sort[index].Nulls);
            if (compared != 0)
                return sort[index].Direction == QuerySortDirection.Desc ? -compared : compared;
        }
        return string.Compare(record.Id.Value, cursor.RecordId, StringComparison.Ordinal);
    }

    private static void AppendFilterShape(StringBuilder builder, FilterExpression? filter)
    {
        if (filter is null)
        {
            builder.Append("filter=null;");
            return;
        }

        builder.Append("filter=(")
            .Append(filter.Kind).Append(',')
            .Append(filter.Field).Append(',')
            .Append(filter.Operator).Append(',');
        AppendQueryValueShape(builder, filter.Value);
        AppendQueryValueArrayShape(builder, filter.Values);
        foreach (var child in filter.Children ?? [])
        {
            AppendFilterShape(builder, child);
        }

        builder.Append(')');
    }

    private static void AppendSortShape(StringBuilder builder, QuerySort[]? sort)
    {
        builder.Append("sort=[");
        foreach (var item in sort ?? [])
        {
            builder.Append(item.Field).Append(',')
                .Append(item.Direction).Append(',')
                .Append(item.Nulls).Append(';');
        }

        builder.Append("];");
    }

    private static void AppendStringArrayShape(StringBuilder builder, string name, string[]? values)
    {
        builder.Append(name).Append("=[");
        foreach (var value in values ?? [])
        {
            builder.Append(value).Append(';');
        }

        builder.Append("];");
    }

    private static void AppendQueryValueArrayShape(StringBuilder builder, QueryValue[]? values)
    {
        builder.Append('[');
        foreach (var value in values ?? [])
        {
            AppendQueryValueShape(builder, value);
        }

        builder.Append(']');
    }

    private static void AppendQueryValueShape(StringBuilder builder, QueryValue? value)
    {
        if (value is null)
        {
            builder.Append("null;");
            return;
        }

        builder.Append(value.Kind).Append(':')
            .Append(value.String).Append(':')
            .Append(value.Boolean?.ToString(CultureInfo.InvariantCulture)).Append(':')
            .Append(value.Integer?.ToString(CultureInfo.InvariantCulture)).Append(':')
            .Append(value.Number?.ToString(CultureInfo.InvariantCulture)).Append(':')
            .Append(value.Decimal).Append(':')
            .Append(value.DateTime?.ToString("O", CultureInfo.InvariantCulture)).Append(':')
            .Append(value.Id).Append(':');
        AppendQueryValueArrayShape(builder, value.Array);
        builder.Append(';');
    }

    private static StoredRecord[] ApplySelect(StoredRecord[] records, string[]? select)
    {
        if (select is null || select.Length == 0)
        {
            return records;
        }

        return records.Select(record =>
        {
            var root = new SelectNode();
            foreach (var fieldPath in select)
            {
                if (TryReadField(record.Payload, fieldPath, out var selectedValue))
                {
                    AddSelectedValue(root, fieldPath, selectedValue);
                }
            }

            return record with
            {
                Payload = new RecordPayload
                {
                    Kind = RecordPayloadKind.FieldMap,
                    Fields = MaterializeSelectedFields(root)
                }
            };
        }).ToArray();
    }

    private static void AddSelectedValue(SelectNode root, string fieldPath, JsonElement value)
    {
        var parts = fieldPath.Split('.', StringSplitOptions.RemoveEmptyEntries);
        var current = root;
        foreach (var part in parts)
        {
            if (!current.Children.TryGetValue(part, out var child))
            {
                child = new SelectNode();
                current.Children.Add(part, child);
            }

            current = child;
        }

        current.Value = value.Clone();
    }

    private static Dictionary<string, JsonElement> MaterializeSelectedFields(SelectNode root)
    {
        var fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var child in root.Children)
        {
            fields[child.Key] = MaterializeSelectedValue(child.Value);
        }

        return fields;
    }

    private static JsonElement MaterializeSelectedValue(SelectNode node)
    {
        if (node.Children.Count == 0 && node.Value is { } value)
        {
            return value.Clone();
        }

        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            WriteSelectedObject(writer, node);
        }

        using var document = JsonDocument.Parse(buffer.WrittenMemory);
        return document.RootElement.Clone();
    }

    private static void WriteSelectedObject(Utf8JsonWriter writer, SelectNode node)
    {
        writer.WriteStartObject();
        foreach (var child in node.Children)
        {
            writer.WritePropertyName(child.Key);
            if (child.Value.Children.Count == 0 && child.Value.Value is { } value)
            {
                value.WriteTo(writer);
            }
            else
            {
                WriteSelectedObject(writer, child.Value);
            }
        }

        writer.WriteEndObject();
    }

    private OperationResult<T>? ValidateUnsupportedQuery<T>(RecordQuery query, bool allowCount)
    {
        if (query.Include is { Length: > 0 })
        {
            return InMemoryResultFactory.Unsupported<T>(InMemoryErrorCodes.UnsupportedQuery, "Includes are not supported by HPD.BASE InMemory.");
        }

        if (query.Extensions is { Length: > 0 })
        {
            return InMemoryResultFactory.Unsupported<T>(InMemoryErrorCodes.UnsupportedQuery, "Query extensions are not supported by HPD.BASE InMemory.");
        }

        if (!allowCount && query.Count != QueryCountMode.None)
        {
            return InMemoryResultFactory.Unsupported<T>(InMemoryErrorCodes.UnsupportedQuery, "Streaming does not support count modes.");
        }

        if (query.Count is QueryCountMode.Estimated or QueryCountMode.Limited)
        {
            return InMemoryResultFactory.Unsupported<T>(InMemoryErrorCodes.UnsupportedQuery, "Estimated and limited count modes are not supported by HPD.BASE InMemory.");
        }

        if ((query.Sort?.Length ?? 0) > _options.MaxSortFields)
        {
            return InMemoryResultFactory.Validation<T>(InMemoryErrorCodes.InvalidQuery, "Query contains too many sort fields.");
        }

        if ((query.Select?.Length ?? 0) > _options.MaxSelectFields)
        {
            return InMemoryResultFactory.Validation<T>(InMemoryErrorCodes.InvalidQuery, "Query contains too many selected fields.");
        }

        foreach (var selectedField in query.Select ?? [])
        {
            var segments = selectedField.Split('.', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0
                || selectedField.StartsWith(".", StringComparison.Ordinal)
                || selectedField.EndsWith(".", StringComparison.Ordinal)
                || selectedField.Contains("..", StringComparison.Ordinal)
                || segments.Any(segment => InMemoryValidation.ValidateFieldName<T>(segment) is not null))
            {
                return InMemoryResultFactory.Validation<T>(InMemoryErrorCodes.InvalidQuery, "Selected payload fields must be valid field paths.");
            }
        }

        if (query.Page?.Limit is < 0 || query.Page?.PerPage is < 0 || query.Page?.Offset is < 0)
        {
            return InMemoryResultFactory.Validation<T>(InMemoryErrorCodes.InvalidQuery, "Query pagination values must be non-negative.");
        }

        if (query.Page?.Page is <= 0)
        {
            return InMemoryResultFactory.Validation<T>(InMemoryErrorCodes.InvalidQuery, "Page mode is one-based.");
        }

        if ((query.Page?.Limit ?? query.Page?.PerPage) is { } requestedLimit && requestedLimit > _options.MaxPageSize)
        {
            return InMemoryResultFactory.Validation<T>(InMemoryErrorCodes.InvalidQuery, "Query page size exceeds the InMemory store limit.");
        }

        if (query.Filter is not null)
        {
            var nodeCount = 0;
            if (ValidateFilter<T>(query.Filter, depth: 1, ref nodeCount) is { } filterError)
            {
                return filterError;
            }
        }

        return null;
    }

    private OperationResult<T>? ValidateFilter<T>(FilterExpression filter, int depth, ref int nodeCount)
    {
        nodeCount++;
        if (depth > _options.MaxFilterDepth || nodeCount > _options.MaxFilterNodes)
        {
            return InMemoryResultFactory.Validation<T>(InMemoryErrorCodes.InvalidQuery, "Query filter exceeds InMemory limits.");
        }

        var error = filter.Kind switch
        {
            FilterNodeKind.Extension => InMemoryResultFactory.Unsupported<T>(InMemoryErrorCodes.UnsupportedQuery, "Filter extensions are not supported by HPD.BASE InMemory."),
            FilterNodeKind.Compare when filter.Operator is FilterOperator.Like or FilterOperator.NotLike => InMemoryResultFactory.Unsupported<T>(InMemoryErrorCodes.UnsupportedQuery, "Like and not-like filters are not supported by HPD.BASE InMemory."),
            FilterNodeKind.Compare when string.IsNullOrWhiteSpace(filter.Field) || filter.Value is null => InMemoryResultFactory.Validation<T>(InMemoryErrorCodes.InvalidQuery, "Compare filters require a field and value."),
            FilterNodeKind.In when string.IsNullOrWhiteSpace(filter.Field) || filter.Values is not { Length: > 0 } => InMemoryResultFactory.Validation<T>(InMemoryErrorCodes.InvalidQuery, "In filters require a field and values."),
            FilterNodeKind.In when filter.Values!.Length > _options.MaxInValues => InMemoryResultFactory.Validation<T>(InMemoryErrorCodes.InvalidQuery, "In filters contain too many values."),
            FilterNodeKind.Between when string.IsNullOrWhiteSpace(filter.Field) || filter.Values is not { Length: 2 } => InMemoryResultFactory.Validation<T>(InMemoryErrorCodes.InvalidQuery, "Between filters require a field and exactly two values."),
            FilterNodeKind.IsNull or FilterNodeKind.IsDefined when string.IsNullOrWhiteSpace(filter.Field) => InMemoryResultFactory.Validation<T>(InMemoryErrorCodes.InvalidQuery, "Field filter nodes require a field."),
            FilterNodeKind.Not when filter.Children is not { Length: 1 } => InMemoryResultFactory.Validation<T>(InMemoryErrorCodes.InvalidQuery, "Not filters require exactly one child."),
            FilterNodeKind.And or FilterNodeKind.Or when filter.Children is not { Length: > 0 } => InMemoryResultFactory.Validation<T>(InMemoryErrorCodes.InvalidQuery, "Boolean filters require children."),
            _ => null
        };

        if (error is not null)
        {
            return error;
        }

        foreach (var child in filter.Children ?? [])
        {
            if (ValidateFilter<T>(child, depth + 1, ref nodeCount) is { } childError)
            {
                return childError;
            }
        }

        return null;
    }

    private static bool MatchesFilter(StoredRecord record, FilterExpression filter) =>
        filter.Kind switch
        {
            FilterNodeKind.True => true,
            FilterNodeKind.False => false,
            FilterNodeKind.Not => filter.Children is [{ } child] && !MatchesFilter(record, child),
            FilterNodeKind.And => filter.Children is { Length: > 0 } children && children.All(child => MatchesFilter(record, child)),
            FilterNodeKind.Or => filter.Children is { Length: > 0 } children && children.Any(child => MatchesFilter(record, child)),
            FilterNodeKind.Compare => MatchesCompare(record, filter),
            FilterNodeKind.In => MatchesIn(record, filter),
            FilterNodeKind.Between => MatchesBetween(record, filter),
            FilterNodeKind.IsNull => TryReadField(record.Payload, filter.Field, out var value) && value.ValueKind == JsonValueKind.Null,
            FilterNodeKind.IsDefined => TryReadField(record.Payload, filter.Field, out _),
            _ => false
        };

    private static bool MatchesCompare(StoredRecord record, FilterExpression filter)
    {
        if (!TryReadField(record.Payload, filter.Field, out var fieldValue) || filter.Value is null)
        {
            return false;
        }

        return filter.Operator switch
        {
            FilterOperator.Equal => ValueEquals(fieldValue, filter.Value),
            FilterOperator.NotEqual => !ValueEquals(fieldValue, filter.Value),
            FilterOperator.LessThan => CompareValues(fieldValue, filter.Value) is < 0,
            FilterOperator.LessThanOrEqual => CompareValues(fieldValue, filter.Value) is <= 0,
            FilterOperator.GreaterThan => CompareValues(fieldValue, filter.Value) is > 0,
            FilterOperator.GreaterThanOrEqual => CompareValues(fieldValue, filter.Value) is >= 0,
            FilterOperator.Contains => ContainsValue(fieldValue, filter.Value),
            FilterOperator.NotContains => !ContainsValue(fieldValue, filter.Value),
            FilterOperator.StartsWith => fieldValue.ValueKind == JsonValueKind.String
                && filter.Value.String is { } prefix
                && (fieldValue.GetString() ?? string.Empty).StartsWith(prefix, StringComparison.Ordinal),
            FilterOperator.EndsWith => fieldValue.ValueKind == JsonValueKind.String
                && filter.Value.String is { } suffix
                && (fieldValue.GetString() ?? string.Empty).EndsWith(suffix, StringComparison.Ordinal),
            _ => false
        };
    }

    private static bool MatchesIn(StoredRecord record, FilterExpression filter)
    {
        if (!TryReadField(record.Payload, filter.Field, out var fieldValue) || filter.Values is null)
        {
            return false;
        }

        return fieldValue.ValueKind == JsonValueKind.Array
            ? fieldValue.EnumerateArray().Any(item => filter.Values.Any(queryValue => ValueEquals(item, queryValue)))
            : filter.Values.Any(queryValue => ValueEquals(fieldValue, queryValue));
    }

    private static bool MatchesBetween(StoredRecord record, FilterExpression filter)
    {
        if (!TryReadField(record.Payload, filter.Field, out var fieldValue)
            || filter.Values is not [{ } lower, { } upper])
        {
            return false;
        }

        return CompareValues(fieldValue, lower) is >= 0 && CompareValues(fieldValue, upper) is <= 0;
    }

    private static bool TryReadField(RecordPayload payload, string? fieldPath, out JsonElement value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(fieldPath))
        {
            return false;
        }

        var parts = fieldPath.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return false;
        }

        if (payload.Kind == RecordPayloadKind.FieldMap)
        {
            if (payload.Fields?.TryGetValue(parts[0], out value) != true)
            {
                return false;
            }
        }
        else
        {
            value = payload.Json;
            if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(parts[0], out value))
            {
                return false;
            }
        }

        for (var index = 1; index < parts.Length; index++)
        {
            if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(parts[index], out value))
            {
                return false;
            }
        }

        return true;
    }

    private static bool ValueEquals(JsonElement fieldValue, QueryValue queryValue)
    {
        if (queryValue.Kind == QueryValueKind.Null)
        {
            return fieldValue.ValueKind == JsonValueKind.Null;
        }

        if (TryDecimal(fieldValue, out var fieldDecimal) && TryDecimal(queryValue, out var queryDecimal))
        {
            return fieldDecimal == queryDecimal;
        }

        if (queryValue.Kind == QueryValueKind.Boolean && fieldValue.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            return fieldValue.GetBoolean() == queryValue.Boolean;
        }

        var fieldString = ScalarString(fieldValue);
        var queryString = ScalarString(queryValue);
        return fieldString is not null
            && queryString is not null
            && string.Equals(fieldString, queryString, StringComparison.Ordinal);
    }

    private static int? CompareValues(JsonElement fieldValue, QueryValue queryValue)
    {
        if (TryDecimal(fieldValue, out var fieldDecimal) && TryDecimal(queryValue, out var queryDecimal))
        {
            return fieldDecimal.CompareTo(queryDecimal);
        }

        if (queryValue.DateTime is { } queryDate
            && fieldValue.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(fieldValue.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var fieldDate))
        {
            return fieldDate.ToUniversalTime().CompareTo(queryDate.ToUniversalTime());
        }

        var fieldString = ScalarString(fieldValue);
        var queryString = ScalarString(queryValue);
        return fieldString is null || queryString is null
            ? null
            : string.Compare(fieldString, queryString, StringComparison.Ordinal);
    }

    private static bool ContainsValue(JsonElement fieldValue, QueryValue queryValue)
    {
        if (fieldValue.ValueKind == JsonValueKind.Array)
        {
            return fieldValue.EnumerateArray().Any(item => ValueEquals(item, queryValue));
        }

        return fieldValue.ValueKind == JsonValueKind.String
            && queryValue.String is { } text
            && (fieldValue.GetString() ?? string.Empty).Contains(text, StringComparison.Ordinal);
    }

    private static int CompareSortValues(
        bool leftPresent,
        JsonElement left,
        bool rightPresent,
        JsonElement right,
        QueryNullOrder nullOrder)
    {
        var leftNull = !leftPresent || left.ValueKind == JsonValueKind.Null;
        var rightNull = !rightPresent || right.ValueKind == JsonValueKind.Null;
        if (leftNull || rightNull)
        {
            if (leftNull && rightNull)
            {
                return 0;
            }

            return nullOrder == QueryNullOrder.First
                ? leftNull ? -1 : 1
                : leftNull ? 1 : -1;
        }

        if (TryDecimal(left, out var leftDecimal) && TryDecimal(right, out var rightDecimal))
        {
            return leftDecimal.CompareTo(rightDecimal);
        }

        return string.Compare(ScalarString(left), ScalarString(right), StringComparison.Ordinal);
    }

    private static bool TryDecimal(JsonElement element, out decimal value)
    {
        value = default;
        return element.ValueKind switch
        {
            JsonValueKind.Number => element.TryGetDecimal(out value),
            JsonValueKind.String => decimal.TryParse(element.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out value),
            _ => false
        };
    }

    private static bool TryDecimal(QueryValue queryValue, out decimal value)
    {
        value = default;
        return queryValue.Kind switch
        {
            QueryValueKind.Integer when queryValue.Integer is { } integer => TryAssign(integer, out value),
            QueryValueKind.Number when queryValue.Number is { } number && double.IsFinite(number) => TryAssign((decimal)number, out value),
            QueryValueKind.Decimal when queryValue.Decimal is { } text => decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out value),
            _ => false
        };
    }

    private static bool TryAssign(decimal input, out decimal value)
    {
        value = input;
        return true;
    }

    private static string? ScalarString(JsonElement element) =>
        element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.GetRawText(),
            JsonValueKind.True => bool.TrueString,
            JsonValueKind.False => bool.FalseString,
            _ => null
        };

    private static string? ScalarString(QueryValue queryValue) =>
        queryValue.Kind switch
        {
            QueryValueKind.String => queryValue.String,
            QueryValueKind.Id => queryValue.Id,
            QueryValueKind.Integer => queryValue.Integer?.ToString(CultureInfo.InvariantCulture),
            QueryValueKind.Number => queryValue.Number?.ToString(CultureInfo.InvariantCulture),
            QueryValueKind.Decimal => queryValue.Decimal,
            QueryValueKind.Boolean => queryValue.Boolean?.ToString(),
            QueryValueKind.DateTime => queryValue.DateTime?.ToString("O", CultureInfo.InvariantCulture),
            _ => null
        };

    private static void ValidateOptions(HPDBaseInMemoryStoreOptions options)
    {
        ValidateStableId(options.StoreId, nameof(options.StoreId));
        ValidateStableId(options.ModuleId, nameof(options.ModuleId));
        ValidateStableId(options.HealthRefId, nameof(options.HealthRefId));
        ValidateStableId(options.DiagnosticRefId, nameof(options.DiagnosticRefId));
        if (options.DefaultPageSize <= 0 || options.MaxPageSize <= 0 || options.DefaultPageSize > options.MaxPageSize)
        {
            throw new ArgumentOutOfRangeException(nameof(options.DefaultPageSize), "Default and maximum page sizes must be positive and ordered.");
        }

        if (options.CollectionIds.Any(id => !InMemoryValidation.IsValidIdText(id)))
        {
            throw new ArgumentException("Collection ids must be non-empty and contain no control characters.", nameof(options.CollectionIds));
        }

        if (options.CollectionIds.Length > 0 && options.Collections is { Length: > 0 } collections)
        {
            var configured = options.CollectionIds.Order(StringComparer.Ordinal).ToArray();
            var contributed = collections.Select(collection => collection.Id).Order(StringComparer.Ordinal).ToArray();
            if (!configured.SequenceEqual(contributed, StringComparer.Ordinal))
            {
                throw new ArgumentException("CollectionIds and Collections must contain the same collection ids when both are configured.", nameof(options.Collections));
            }
        }
    }

    private static void ValidateStableId(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (!string.Equals(value, value.Trim(), StringComparison.Ordinal) || value.Any(char.IsControl))
        {
            throw new ArgumentException("Identifier values must be trimmed and contain no control characters.", parameterName);
        }
    }

    private static StoreCapabilityDescriptor CreateCapabilities(HPDBaseInMemoryStoreOptions options) => new()
    {
        StoreId = options.StoreId,
        StoreKind = BaseStoreKinds.InMemory,
        StoreVersion = options.StoreVersion,
        Read = new RecordReadCapability
        {
            List = true,
            Get = true,
            MaxPageSize = options.MaxPageSize
        },
        Mutation = new RecordMutationCapability
        {
            Create = true,
            Patch = true,
            Replace = true,
            Delete = true,
            IdAuthority = options.AllowClientRequestedIds ? IdAuthority.Hybrid : IdAuthority.Store,
            TimestampAuthority = TimestampAuthority.Store,
            Consistency = ConsistencyModel.Strong,
            MutationModes = Enum.GetValues<BaseCollectionMutationMode>(),
            AdministrativePurge = true,
        },
        Query = new QueryCapability
        {
            Filter = new FilterCapability
            {
                Supported = true,
                Operators =
                [
                    FilterOperator.Equal,
                    FilterOperator.NotEqual,
                    FilterOperator.LessThan,
                    FilterOperator.LessThanOrEqual,
                    FilterOperator.GreaterThan,
                    FilterOperator.GreaterThanOrEqual,
                    FilterOperator.Contains,
                    FilterOperator.NotContains,
                    FilterOperator.StartsWith,
                    FilterOperator.EndsWith
                ],
                BooleanComposition = true,
                Not = true,
                NullChecks = true,
                MissingFieldChecks = true,
                NestedFieldPaths = true,
                ArrayMembership = true,
                MaxDepth = options.MaxFilterDepth,
                MaxNodes = options.MaxFilterNodes,
                MaxSerializedLength = options.MaxSerializedQueryLength,
                ExecutionMode = QueryExecutionMode.Native
            },
            Sort = new SortCapability
            {
                Supported = true,
                MaxFields = options.MaxSortFields,
                NestedFieldPaths = true,
                NullOrdering = true,
                StableTieBreaker = true
            },
            Pagination = new PaginationCapability
            {
                Page = true,
                Offset = true,
                Cursor = QueryCursorGuarantee.StableHistory,
                DefaultLimit = options.DefaultPageSize,
                MaxLimit = options.MaxPageSize,
                CursorRequiresStableSort = true
            },
            Count = new CountCapability
            {
                SupportedModes = [QueryCountMode.None, QueryCountMode.IfAvailable, QueryCountMode.Exact]
            },
            Select = new SelectCapability
            {
                PayloadFields = true,
                NestedFieldPaths = true
            },
            Include = new QueryIncludeCapability { Supported = true, MaxDepth = 3, BackRelations = true, IncludeFilters = true, IncludeSort = true, IncludeLimit = true, ExecutionMode = QueryExecutionMode.Native }
        },
        Revision = new RevisionCapability
        {
            Supported = true,
            Guarantee = RevisionGuarantee.Store,
            Patch = true,
            Replace = true,
            Delete = true
        },
        Batch = new StoreBatchCapability
        {
            Modes = [BaseRecordBatchExecutionMode.Atomic],
            MaxOperations = HPDBaseInMemoryDefaults.MaximumBatchOperations,
            MaxCanonicalPayloadBytes = HPDBaseInMemoryDefaults.MaximumBatchCanonicalPayloadBytes,
            MinimumAcquisitionTimeout = TimeSpan.FromMilliseconds(10),
            MinimumTransactionTimeout = TimeSpan.FromMilliseconds(10),
            MinimumCommitCompletionTimeout = TimeSpan.FromMilliseconds(10),
            TimeoutGranularity = TimeSpan.FromMilliseconds(10),
            Ordered = true,
            PartialResults = false,
            CrossCollectionAtomic = true,
            ReadYourWrites = true,
            Durable = false,
            TransactionalJournal = true,
            Isolation = BaseTransactionIsolation.Serializable,
            NestedTransactions = false,
            Savepoints = false
        },
        Upsert = options.AllowClientRequestedIds
            ? new StoreUpsertCapability
            {
                Atomic = true,
                UpdateModes =
                [
                    RecordUpsertUpdateMode.Patch,
                    RecordUpsertUpdateMode.Replace
                ],
                ExpectedRevision = true,
                ExistenceConditions = true
            }
            : null,
        AtomicRequest = new AtomicRequestCapability
        {
            Supported = true,
            Durability = BaseAtomicRequestDurability.ProcessLocal,
            DuplicateResultReplay = true,
            FingerprintConflictDetection = true,
            IndeterminateResolution = false,
            MaxIdentityBytes = 512,
            MaxReceiptBytes = 16_777_216,
            MinReceiptLifetime = TimeSpan.FromHours(1),
            MaxReceiptLifetime = TimeSpan.FromDays(90),
        },
        SelectionMutation = CreateSelectionCapability(
            options.MaxFilterNodes, options.MaxFilterDepth, options.MaxPageSize,
            BaseAtomicSelectionIsolationClass.OptimisticRangeValidatedSerializable),
        ModuleMutation = new BaseModuleMutationCapability
        {
            Supported = true, SerializableExecution = true, DurableReceipts = true,
            GenerationCells = true, AtomicRecordAndGenerationCommit = true,
            MaximumLimits = BaseModuleMutationPlatform.MaximumLimits,
        },
        Administration = new BaseAdministrationCapability
        {
            Backup = false, Validate = false, Restore = false, AdministrativePurge = true,
            VectorRebuild = false,
            OnlineBackup = false, WritersBlockedDuringBackup = false, ReadersBlockedDuringBackup = false,
            RestoreRequiresExclusiveMaintenance = false, Durable = false, MaxArtifactBytes = 0,
        },
        Streaming = new StreamingCapability
        {
            Supported = options.EnableStreamingCapability,
            MaxItems = options.MaxStreamItems,
            RequiresStableSort = true
        }
    };

    private static BaseSelectionMutationCapability CreateSelectionCapability(
        int maxNodes, int maxDepth, int maxRecords, BaseAtomicSelectionIsolationClass isolation) => new()
    {
        IsSupported = true,
        CertifiedMaxima = new BaseSelectionOperationLimits
        {
            MaximumQueryNodes = maxNodes, MaximumQueryDepth = maxDepth, MaximumLiteralValues = 1024,
            MaximumSelectedRecords = maxRecords, MaximumSelectedBytes = 16_777_216,
            MaximumProducedMutations = maxRecords, MaximumQueryExecutions = 1,
            MaximumReadIntervals = maxNodes, MaximumWrittenBytes = 16_777_216,
            MaximumFactBytes = 16_777_216, MaximumJournalBytes = 16_777_216,
            MaximumReceiptBytes = 16_777_216, MaximumRelationChecks = 4096,
            MaximumUniqueConstraintChecks = 4096, MaximumPreviousStateRequirements = 256,
            MaximumTransientBytes = 33_554_432, MaximumResultBytes = 1_048_576,
            AcquisitionTimeout = TimeSpan.FromMinutes(1), ExecutionTimeout = TimeSpan.FromMinutes(5),
            CallerCommitObservationTimeout = TimeSpan.FromMinutes(5),
        },
        Isolation = isolation, ReceiptEnvelopeFormatVersions = [2], CanonicalCodecVersions = [1],
        SupportedFilterOperators = Enum.GetValues<FilterOperator>().ToImmutableArray(),
        SupportedFilterNodeKinds = Enum.GetValues<FilterNodeKind>().ToImmutableArray(),
        SupportedIndexShapes = [BaseIndexAccessShape.CollectionGenerationScan],
        ConstraintAttribution = BaseConstraintAttributionClass.RecordIdentity,
        SupportsReceiptOnlyCommit = true, SuppliesReadIntervalEvidence = true,
        SupportsRelationParticipation = true, SupportsReadYourWrites = true,
        SupportsBoundedCancellation = true, SupportsBoundedCommitObservation = true,
    };

    private static string CollectionIdForTelemetry(CollectionDefinition? collection) => collection?.Id ?? string.Empty;

    private sealed class AtomicSession : IAtomicRecordSession
    {
        private int _relationChecks;
        private int _uniqueChecks;
        private long _selectionRetainedBytes;
        private BaseCapturedAtomicMutationAuthority? _capturedMutation;
        private BasePreparedAtomicMutation? _preparedMutation;
        private BaseAtomicMutationPlan? _preparedPlan;
        private BaseProvisionalAppliedAtomicMutation? _appliedProvisional;
        private Dictionary<int, BaseSubjectIncarnation>? _preparedLifecycleIncarnations;
        private Dictionary<int, string>? _capturedModuleGenerationKeys;
        private BaseModuleMutationCaptureExtension? _capturedModuleExtension;
        public ValueTask<OperationResult<BaseSelectionMutationCommitAccounting>> MeasureSelectionMutationAsync(
            BaseAtomicReceiptResult receipt, BaseSelectionMutationResult result, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            long written = 0, facts = 0, journal = 0;
            foreach (BaseOwnedMutationFact owned in receipt.Mutations)
            {
                BaseRecordMutationFact fact = owned.MaterializeOwned();
                facts = checked(facts + owned.EncodedLength);
                if (fact.After is { } after)
                {
                    InMemoryCollectionState state = _owner.GetOrCreateCollection(_working, fact.Collection.Id);
                    StoredRecord persisted = state.RecordsById[fact.After.Id.Value];
                    written = checked(written + JsonSerializer.SerializeToUtf8Bytes(
                        RecordCloneHelpers.CloneEnvelope(persisted), HPDBaseJsonSerializerContext.Default.RecordEnvelope).LongLength);
                }
                else
                {
                    written = checked(written + System.Text.Encoding.UTF8.GetByteCount(fact.Before!.Id.Value) + sizeof(long));
                }
                journal = checked(journal + owned.EncodedLength + System.Text.Encoding.UTF8.GetByteCount(fact.Event.EventId) + sizeof(long));
            }
            long receiptBytes = JsonSerializer.SerializeToUtf8Bytes(BaseAtomicReceiptWire.From(receipt), HPDBaseJsonSerializerContext.Default.BaseAtomicReceiptWire).LongLength;
            long resultBytes = JsonSerializer.SerializeToUtf8Bytes(result, HPDBaseJsonSerializerContext.Default.BaseSelectionMutationResult).LongLength;
            return ValueTask.FromResult(OperationResults.Ok(new BaseSelectionMutationCommitAccounting
            {
                WrittenBytes = written, FactBytes = facts, JournalBytes = journal, ReceiptBytes = receiptBytes,
                RelationChecks = _relationChecks, UniqueConstraintChecks = _uniqueChecks, ResultBytes = resultBytes,
                TransientBytes = checked(_selectionRetainedBytes + written + facts + journal + receiptBytes + resultBytes),
            }));
        }

        private const int Active = 0;
        private const int Closing = 1;
        private const int Closed = 2;

        private readonly InMemoryRecordStore _owner;
        private readonly InMemoryStoreState _working;
        private readonly SemaphoreSlim _operationGate = new(1, 1);
        private int _lifetimeState;

        /// <summary>Initializes a new instance.</summary>
        public AtomicSession(InMemoryRecordStore owner, InMemoryStoreState working)
        {
            _owner = owner;
            _working = working;
            _uniqueChecks = 0;
        }

        public ValueTask<OperationResult<BaseCapturedAtomicMutationAuthority>> CaptureAtomicMutationAuthorityAsync(
            BaseAtomicMutationCaptureRequest request,
            CancellationToken cancellationToken = default) => ExecuteAsync(cancellationToken, token =>
        {
            ArgumentNullException.ThrowIfNull(request);
            BaseAtomicMutationIntent intent = request.Intent;
            BaseAtomicMutationExecutionLimits limits = request.Limits;
            if (request.Kind == BaseAtomicMutationExecutionKind.SelectionMutation)
                return ValueTask.FromResult(CaptureSelectionAuthority(request, token));
            if (request.Kind == BaseAtomicMutationExecutionKind.ModuleMutation)
                return CaptureModuleAuthority(request, token);
            if (_capturedMutation is not null || request.Kind != BaseAtomicMutationExecutionKind.RecordMutations
                || request.Selection is not null || request.Module is not null
                || intent.Items.IsDefaultOrEmpty || intent.Items.Length > limits.MaximumItems ||
                !string.Equals(intent.Authority.StoreInstanceId, _owner._options.StoreId, StringComparison.Ordinal) ||
                intent.Authority.RestoreEpoch != 0 || intent.Authority.SchemaGeneration != 1
                || !AuthorityCollectionsMatch(intent.Authority.Collections, intent.Items, _owner._generation))
                return ValueTask.FromResult(SubjectFailure<BaseCapturedAtomicMutationAuthority>(BaseSubjectErrorCodes.ProviderContractInvalid));
            var items = ImmutableArray.CreateBuilder<BaseCapturedMutationItem>(intent.Items.Length);
            var intervals = ImmutableArray.CreateBuilder<BaseAtomicReadIntervalEvidence>(intent.Items.Length);
            long selectedBytes = 0;
            long retainedBytes;
            var transactionRecords = new Dictionary<string, RecordEnvelope?>(StringComparer.Ordinal);
            using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            digest.AppendData(System.Text.Encoding.UTF8.GetBytes(intent.IntentDigest));
            for (int index = 0; index < intent.Items.Length; index++)
            {
                token.ThrowIfCancellationRequested();
                BaseAtomicMutationIntentItem item = intent.Items[index];
                if (item.Ordinal != index) return ValueTask.FromResult(SubjectFailure<BaseCapturedAtomicMutationAuthority>(BaseSubjectErrorCodes.ProviderContractInvalid));
                string itemKey = CaptureRecordKey(item.Collection.Id, item.RecordId);
                if (!transactionRecords.TryGetValue(itemKey, out RecordEnvelope? current))
                {
                    current = SnapshotRecord(item.Collection, item.RecordId);
                    transactionRecords[itemKey] = current;
                }
                if (current is not null)
                {
                    byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(current, HPDBaseJsonSerializerContext.Default.RecordEnvelope);
                    selectedBytes = checked(selectedBytes + bytes.LongLength);
                    digest.AppendData(bytes);
                }
                byte[] key = System.Text.Encoding.UTF8.GetBytes(item.RecordId.Value);
                digest.AppendData(key);
                BaseCapturedMutationDisposition disposition = item.RequestedKind switch
                {
                    BaseRecordMutationKind.Create => current is null
                        ? BaseCapturedMutationDisposition.Create
                        : BaseCapturedMutationDisposition.Update,
                    BaseRecordMutationKind.Upsert => current is null ? BaseCapturedMutationDisposition.Create : BaseCapturedMutationDisposition.Update,
                    BaseRecordMutationKind.Delete => BaseCapturedMutationDisposition.Delete,
                    _ => BaseCapturedMutationDisposition.Update,
                };
                var relationTargets = ImmutableArray.CreateBuilder<BaseCapturedRelationTarget>(item.RelationTargets.Length);
                foreach (BaseAtomicRelationTargetIntent relation in item.RelationTargets)
                {
                    string relationRecordKey = CaptureRecordKey(relation.TargetCollection.Id, relation.TargetRecordId);
                    if (!transactionRecords.TryGetValue(relationRecordKey, out RecordEnvelope? target))
                    {
                        target = SnapshotRecord(relation.TargetCollection, relation.TargetRecordId);
                        transactionRecords[relationRecordKey] = target;
                    }
                    if (target is not null)
                    {
                        byte[] targetBytes = JsonSerializer.SerializeToUtf8Bytes(target, HPDBaseJsonSerializerContext.Default.RecordEnvelope);
                        selectedBytes = checked(selectedBytes + targetBytes.LongLength);
                        digest.AppendData(targetBytes);
                    }
                    byte[] relationKey = System.Text.Encoding.UTF8.GetBytes(relation.TargetRecordId.Value);
                    intervals.Add(new BaseAtomicReadIntervalEvidence
                    {
                        LogicalAccessPathId = $"collection:{relation.TargetCollection.Id}:record",
                        CanonicalLowerBound = relationKey.ToImmutableArray(), LowerInclusive = true,
                        CanonicalUpperBound = relationKey.ToImmutableArray(), UpperInclusive = true,
                    });
                    relationTargets.Add(new BaseCapturedRelationTarget
                    {
                        SourceFieldId = new string(relation.SourceFieldId.AsSpan()),
                        TargetCollectionId = new string(relation.TargetCollection.Id.AsSpan()),
                        TargetRecordId = relation.TargetRecordId,
                        Current = target,
                    });
                }
                items.Add(new BaseCapturedMutationItem
                {
                    Ordinal = index,
                    CollectionId = item.Collection.Id,
                    RecordId = item.RecordId,
                    RuntimeAssignedRecordId = item.RuntimeAssignedRecordId,
                    Disposition = disposition,
                    Current = current,
                    RelationTargets = relationTargets.MoveToImmutable(),
                });
                intervals.Add(new BaseAtomicReadIntervalEvidence
                {
                    LogicalAccessPathId = $"collection:{item.Collection.Id}:record",
                    CanonicalLowerBound = key.ToImmutableArray(), LowerInclusive = true,
                    CanonicalUpperBound = key.ToImmutableArray(), UpperInclusive = true,
                });
                transactionRecords[itemKey] = SimulateIntentRecord(item, current);
            }
            ImmutableArray<BaseCapturedMutationItem> ownedItems = items.ToImmutable();
            OperationResult<ImmutableArray<BaseCapturedSubjectLifecycleConsumerProjection>> lifecycleProjectionResult =
                CaptureLifecycleConsumerProjections(request.LifecycleConsumerProjections, digest, intervals);
            if (!lifecycleProjectionResult.IsSuccess() || lifecycleProjectionResult.Value.IsDefault)
                return ValueTask.FromResult(SubjectFailure<BaseCapturedAtomicMutationAuthority>(BaseSubjectErrorCodes.ProviderContractInvalid));
            ImmutableArray<BaseAtomicReadIntervalEvidence> ownedIntervals = intervals.ToImmutable();
            long evidenceBytes = BaseSubjectCanonicalRetainedWork.MeasureIntervals(ownedIntervals);
            retainedBytes = BaseSubjectCanonicalRetainedWork.MeasureCapture(intent, ownedItems, ownedIntervals, lifecycleProjectionResult.Value);
            long transient = retainedBytes;
            if (selectedBytes > limits.MaximumSelectedBytes || evidenceBytes > limits.MaximumEvidenceBytes ||
                transient > limits.MaximumTransientBytes || intervals.Count > limits.MaximumReadIntervals)
                return ValueTask.FromResult(SubjectFailure<BaseCapturedAtomicMutationAuthority>(BaseSubjectErrorCodes.BudgetExceeded));
            _capturedMutation = new BaseCapturedAtomicMutationAuthority
            {
                Kind = request.Kind,
                IntentDigest = new string(intent.IntentDigest.AsSpan()),
                CaptureDigest = Convert.ToHexStringLower(digest.GetHashAndReset()),
                Authority = new BaseAtomicMutationAuthorityEvidence
                {
                    ApplicationId = intent.Authority.ApplicationId, StoreInstanceId = _owner._options.StoreId,
                    RestoreEpoch = 0, SchemaGeneration = 1,
                    Collections = intent.Authority.Collections.Select(static value => value with { }).ToImmutableArray(),
                    Isolation = BaseAtomicSelectionIsolationClass.OptimisticRangeValidatedSerializable,
                    TransactionEvidenceToken = BitConverter.GetBytes(_working.GlobalMutationPosition).ToImmutableArray(),
                },
                Items = ownedItems, ModuleRecords = [], ModuleRelationTargets = [], Generations = [],
                LifecycleConsumerProjections = lifecycleProjectionResult.Value,
                ReadIntervals = ownedIntervals,
                Accounting = new BaseAtomicCaptureAccounting
                {
                    Records = checked(intent.Items.Length + intent.Items.Sum(static item => item.RelationTargets.Length)),
                    RelationTargetReads = intent.Items.Sum(static item => item.RelationTargets.Length), GenerationReads = 0,
                    SelectedBytes = selectedBytes, ReadIntervals = intervals.Count,
                    RelationTargetBytes = 0, GenerationBytes = 0,
                    EvidenceBytes = evidenceBytes, TransientBytes = transient,
                },
            };
            return ValueTask.FromResult(OperationResults.Ok(_capturedMutation));
        });

        private OperationResult<ImmutableArray<BaseCapturedSubjectLifecycleConsumerProjection>> CaptureLifecycleConsumerProjections(
            ImmutableArray<BaseSubjectLifecycleConsumerProjectionCaptureRequest> requests,
            IncrementalHash digest,
            ImmutableArray<BaseAtomicReadIntervalEvidence>.Builder intervals)
        {
            var captured = ImmutableArray.CreateBuilder<BaseCapturedSubjectLifecycleConsumerProjection>(requests.Length);
            for (int index = 0; index < requests.Length; index++)
            {
                BaseSubjectLifecycleConsumerProjectionCaptureRequest request = requests[index];
                if (index > 0 && CompareLifecycleProjectionRequest(requests[index - 1], request) >= 0
                    || !_working.SubjectLifecycleConsumers.TryGetValue($"{request.ConsumerId}\n{request.ConsumerVersion}", out InMemorySubjectLifecycleConsumerProjection? projection)
                    || projection.ConsumerChecksum != request.ConsumerChecksum || projection.ContractId != request.ContractId
                    || projection.ContractVersion != request.ContractVersion || projection.ProjectionGeneration < 1
                    || projection.PublishedGraphGeneration < 1)
                    return SubjectFailure<ImmutableArray<BaseCapturedSubjectLifecycleConsumerProjection>>(BaseSubjectErrorCodes.ProviderContractInvalid);
                var value = new BaseCapturedSubjectLifecycleConsumerProjection
                {
                    ConsumerId = request.ConsumerId, ConsumerVersion = request.ConsumerVersion,
                    ConsumerChecksum = request.ConsumerChecksum, ContractId = request.ContractId,
                    ContractVersion = request.ContractVersion, ProjectionGeneration = projection.ProjectionGeneration,
                    PublishedGraphGeneration = projection.PublishedGraphGeneration,
                };
                captured.Add(value);
                digest.AppendData(System.Text.Encoding.UTF8.GetBytes($"\0lifecycle-consumer\0{value.ConsumerId}\0{value.ConsumerVersion}\0{value.ConsumerChecksum}\0{value.ContractId}\0{value.ContractVersion}\0{value.ProjectionGeneration}\0{value.PublishedGraphGeneration}\0"));
                byte[] intervalKey = System.Text.Encoding.UTF8.GetBytes($"{value.ConsumerId}\0{value.ConsumerVersion}");
                intervals.Add(new BaseAtomicReadIntervalEvidence
                {
                    LogicalAccessPathId = "subject-lifecycle:consumer-projection",
                    CanonicalLowerBound = intervalKey.ToImmutableArray(), LowerInclusive = true,
                    CanonicalUpperBound = intervalKey.ToImmutableArray(), UpperInclusive = true,
                });
            }
            return OperationResults.Ok(captured.MoveToImmutable());
        }

        private static int CompareLifecycleProjectionRequest(
            BaseSubjectLifecycleConsumerProjectionCaptureRequest left,
            BaseSubjectLifecycleConsumerProjectionCaptureRequest right)
        {
            int byId = string.CompareOrdinal(left.ConsumerId, right.ConsumerId);
            return byId != 0 ? byId : left.ConsumerVersion.CompareTo(right.ConsumerVersion);
        }

        private ValueTask<OperationResult<BaseCapturedAtomicMutationAuthority>> CaptureModuleAuthority(
            BaseAtomicMutationCaptureRequest request,
            CancellationToken cancellationToken)
        {
            BaseAtomicMutationIntent intent = request.Intent;
            BaseModuleMutationCaptureExtension? module = request.Module;
            BaseAtomicMutationExecutionLimits limits = request.Limits;
            if (_capturedMutation is not null || module is null || request.Selection is not null || !intent.Items.IsDefaultOrEmpty
                || !string.Equals(intent.Authority.StoreInstanceId, _owner._options.StoreId, StringComparison.Ordinal)
                || intent.Authority.RestoreEpoch != 0 || intent.Authority.SchemaGeneration != 1
                || module.Records.Length > limits.MaximumRecordCaptures
                || module.RelationTargets.Length > limits.MaximumRelationTargetCaptures
                || module.Generations.Length > limits.MaximumGenerationReads
                || !AuthorityCollectionsMatch(intent.Authority.Collections, module, _owner._generation))
                return ValueTask.FromResult(SubjectFailure<BaseCapturedAtomicMutationAuthority>(BaseSubjectErrorCodes.ProviderContractInvalid));
            _capturedModuleExtension = module;

            var records = ImmutableArray.CreateBuilder<BaseCapturedModuleRecord>(module.Records.Length);
            var relations = ImmutableArray.CreateBuilder<BaseCapturedModuleRelationTarget>(module.RelationTargets.Length);
            var generations = ImmutableArray.CreateBuilder<BaseCapturedModuleGeneration>(module.Generations.Length);
            var generationKeys = new Dictionary<int, string>();
            var intervals = ImmutableArray.CreateBuilder<BaseAtomicReadIntervalEvidence>(
                checked(module.Records.Length + module.RelationTargets.Length + module.Generations.Length));
            long selectedBytes = 0;
            long relationBytes = 0;
            long generationBytes = 0;
            using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            digest.AppendData(System.Text.Encoding.UTF8.GetBytes(intent.IntentDigest));

            for (int index = 0; index < module.Records.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                BaseModuleRecordCaptureRequest capture = module.Records[index];
                if (capture.Ordinal != index) return ValueTask.FromResult(SubjectFailure<BaseCapturedAtomicMutationAuthority>(BaseSubjectErrorCodes.ProviderContractInvalid));
                RecordEnvelope? current = SnapshotRecord(capture.Collection, capture.RecordId);
                if ((capture.Presence == BaseModuleCapturePresence.RequirePresent && current is null)
                    || (capture.Presence == BaseModuleCapturePresence.RequireMissing && current is not null))
                    return ValueTask.FromResult(SubjectFailure<BaseCapturedAtomicMutationAuthority>(BaseSubjectErrorCodes.ProviderContractInvalid));
                byte[] key = System.Text.Encoding.UTF8.GetBytes(capture.RecordId.Value);
                intervals.Add(ExactInterval($"collection:{capture.Collection.Id}:record", key));
                if (current is not null)
                {
                    byte[] encoded = JsonSerializer.SerializeToUtf8Bytes(current, HPDBaseJsonSerializerContext.Default.RecordEnvelope);
                    selectedBytes = checked(selectedBytes + encoded.LongLength);
                    digest.AppendData(encoded);
                }
                records.Add(new BaseCapturedModuleRecord
                {
                    Ordinal = index, CaptureId = new string(capture.CaptureId.AsSpan()), CollectionId = new string(capture.Collection.Id.AsSpan()),
                    RecordId = capture.RecordId, Exists = current is not null, Current = current,
                });
            }

            for (int index = 0; index < module.RelationTargets.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                BaseModuleRelationTargetCaptureRequest capture = module.RelationTargets[index];
                if (capture.Ordinal != index) return ValueTask.FromResult(SubjectFailure<BaseCapturedAtomicMutationAuthority>(BaseSubjectErrorCodes.ProviderContractInvalid));
                RecordEnvelope? current = SnapshotRecord(capture.TargetCollection, capture.TargetRecordId);
                byte[] key = System.Text.Encoding.UTF8.GetBytes(capture.TargetRecordId.Value);
                intervals.Add(ExactInterval($"collection:{capture.TargetCollection.Id}:record", key));
                if (current is not null)
                {
                    byte[] encoded = JsonSerializer.SerializeToUtf8Bytes(current, HPDBaseJsonSerializerContext.Default.RecordEnvelope);
                    relationBytes = checked(relationBytes + encoded.LongLength);
                    digest.AppendData(encoded);
                }
                relations.Add(new BaseCapturedModuleRelationTarget
                {
                    Ordinal = index, SourceStatementId = new string(capture.SourceStatementId.AsSpan()),
                    SourceFieldId = new string(capture.SourceFieldId.AsSpan()), TargetCollectionId = new string(capture.TargetCollection.Id.AsSpan()),
                    TargetRecordId = capture.TargetRecordId, Current = current,
                });
            }

            for (int index = 0; index < module.Generations.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                BaseModuleGenerationCaptureRequest capture = module.Generations[index];
                if (capture.Ordinal != index || !ValidGenerationScope(capture))
                    return ValueTask.FromResult(SubjectFailure<BaseCapturedAtomicMutationAuthority>(BaseSubjectErrorCodes.ProviderContractInvalid));
                string storageKey = ModuleGenerationStorageKey(capture);
                generationKeys.Add(index, storageKey);
                bool exists = _working.ModuleGenerations.TryGetValue(storageKey, out long value);
                if ((capture.Absence == BaseModuleGenerationAbsenceBehavior.RequireExisting && !exists)
                    || (capture.Absence == BaseModuleGenerationAbsenceBehavior.RequireMissing && exists))
                    return ValueTask.FromResult(SubjectFailure<BaseCapturedAtomicMutationAuthority>(BaseSubjectErrorCodes.ProviderContractInvalid));
                string keyDigest = Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(storageKey)));
                byte[] key = System.Text.Encoding.UTF8.GetBytes(storageKey);
                intervals.Add(ExactInterval("module-generation", key));
                generationBytes = checked(generationBytes + key.LongLength + 1 + (exists ? 8 : 0));
                digest.AppendData(key);
                if (exists) digest.AppendData(BitConverter.GetBytes(value));
                generations.Add(new BaseCapturedModuleGeneration
                {
                    Ordinal = index, CaptureId = new string(capture.CaptureId.AsSpan()), CellId = new string(capture.Cell.Id.AsSpan()),
                    CellVersion = capture.Cell.Version, CanonicalKeyDigest = keyDigest, Exists = exists,
                    Generation = exists ? BaseModuleGeneration.Create(value) : null,
                });
            }

            long evidenceBytes = BaseSubjectCanonicalRetainedWork.MeasureIntervals(intervals);
            long transient = checked(selectedBytes + relationBytes + generationBytes + evidenceBytes);
            if (selectedBytes > limits.MaximumSelectedBytes || generationBytes > limits.MaximumGenerationBytes
                || evidenceBytes > limits.MaximumEvidenceBytes || transient > limits.MaximumTransientBytes
                || intervals.Count > limits.MaximumReadIntervals)
                return ValueTask.FromResult(SubjectFailure<BaseCapturedAtomicMutationAuthority>(BaseSubjectErrorCodes.BudgetExceeded));
            int readIntervalCount = intervals.Count;
            _capturedMutation = new BaseCapturedAtomicMutationAuthority
            {
                Kind = request.Kind, IntentDigest = new string(intent.IntentDigest.AsSpan()),
                CaptureDigest = Convert.ToHexStringLower(digest.GetHashAndReset()),
                Authority = new BaseAtomicMutationAuthorityEvidence
                {
                    ApplicationId = intent.Authority.ApplicationId, StoreInstanceId = _owner._options.StoreId,
                    RestoreEpoch = 0, SchemaGeneration = 1,
                    Collections = intent.Authority.Collections.Select(static value => value with { }).ToImmutableArray(),
                    Isolation = BaseAtomicSelectionIsolationClass.OptimisticRangeValidatedSerializable,
                    TransactionEvidenceToken = BitConverter.GetBytes(_working.GlobalMutationPosition).ToImmutableArray(),
                },
                Items = [], ModuleRecords = records.MoveToImmutable(), ModuleRelationTargets = relations.MoveToImmutable(),
                Generations = generations.MoveToImmutable(), ReadIntervals = intervals.MoveToImmutable(),
                Accounting = new BaseAtomicCaptureAccounting
                {
                    Records = module.Records.Length, RelationTargetReads = module.RelationTargets.Length,
                    GenerationReads = module.Generations.Length, ReadIntervals = readIntervalCount,
                    SelectedBytes = selectedBytes, RelationTargetBytes = relationBytes, GenerationBytes = generationBytes,
                    EvidenceBytes = evidenceBytes, TransientBytes = transient,
                },
            };
            _capturedModuleGenerationKeys = generationKeys;
            return ValueTask.FromResult(OperationResults.Ok(_capturedMutation));
        }

        public ValueTask<OperationResult<BasePreparedAtomicMutation>> PrepareAtomicMutationAsync(
            BaseCapturedAtomicMutationAuthority captured,
            BaseAtomicMutationPlan plan,
            CancellationToken cancellationToken = default) => ExecuteAsync(cancellationToken, token =>
        {
            token.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(captured); ArgumentNullException.ThrowIfNull(plan);
            if (!ReferenceEquals(captured, _capturedMutation) || _preparedMutation is not null ||
                plan.Kind != captured.Kind ||
                !string.Equals(plan.IntentDigest, captured.IntentDigest, StringComparison.Ordinal) ||
                !string.Equals(plan.CaptureDigest, captured.CaptureDigest, StringComparison.Ordinal) ||
                !LifecycleProjectionBindingsValid(plan, captured) ||
                (plan.Kind == BaseAtomicMutationExecutionKind.ModuleMutation
                    ? plan.Module is null || captured.Items.Length != 0
                    : plan.Items.Length != captured.Items.Length))
                return ValueTask.FromResult(SubjectFailure<BasePreparedAtomicMutation>(BaseSubjectErrorCodes.ProviderContractInvalid));
            var preparedGenerations = ImmutableArray.CreateBuilder<BasePreparedModuleGenerationEvidence>(captured.Generations.Length);
            if (plan.Kind == BaseAtomicMutationExecutionKind.ModuleMutation)
            {
                if (captured.Accounting.GenerationReads > plan.Limits.MaximumGenerationReads
                    || captured.Accounting.GenerationBytes > plan.Limits.MaximumGenerationBytes
                    || plan.Module!.Comparisons.Length > plan.Limits.MaximumGenerationComparisons
                    || plan.Module.Increments.Length > plan.Limits.MaximumGenerationIncrements)
                    return ValueTask.FromResult(SubjectFailure<BasePreparedAtomicMutation>(BaseSubjectErrorCodes.BudgetExceeded, OperationStatus.ValidationFailed, ErrorCategory.Validation));
                if (_capturedModuleGenerationKeys is null
                    || !ModuleBindingsValid(plan, captured)
                    || plan.Module!.Comparisons.Select(static value => value.CaptureOrdinal).Distinct().Count() != plan.Module.Comparisons.Length
                    || plan.Module.Increments.Select(static value => value.CaptureOrdinal).Distinct().Count() != plan.Module.Increments.Length)
                    return ValueTask.FromResult(SubjectFailure<BasePreparedAtomicMutation>(BaseSubjectErrorCodes.ProviderContractInvalid));
                for (int ordinal = 0; ordinal < captured.Generations.Length; ordinal++)
                {
                    BaseCapturedModuleGeneration capture = captured.Generations[ordinal];
                    BaseModuleGenerationComparison? comparison = plan.Module.Comparisons.SingleOrDefault(value => value.CaptureOrdinal == ordinal);
                    BaseModuleGenerationIncrement? increment = plan.Module.Increments.SingleOrDefault(value => value.CaptureOrdinal == ordinal);
                    bool comparisonSatisfied = comparison is null || comparison.Kind switch
                    {
                        BaseModuleGenerationComparisonKind.MustExist => capture.Exists,
                        BaseModuleGenerationComparisonKind.MustBeMissing => !capture.Exists,
                        BaseModuleGenerationComparisonKind.MustEqual => capture.Exists && comparison.Expected is not null && capture.Generation!.Equals(comparison.Expected),
                        _ => false,
                    };
                    if (!comparisonSatisfied || (increment is not null && !capture.Exists && !increment.CreateIfAbsent))
                        return ValueTask.FromResult(SubjectFailure<BasePreparedAtomicMutation>("base.moduleMutation.generationConflict", OperationStatus.Conflict, ErrorCategory.Conflict));
                    BaseModuleGeneration? resulting = increment is null
                        ? capture.Generation
                        : capture.Generation is null ? BaseModuleGeneration.Create(1) : capture.Generation.Increment();
                    preparedGenerations.Add(new BasePreparedModuleGenerationEvidence
                    {
                        CaptureOrdinal = ordinal,
                        CanonicalKeyDigest = new string(capture.CanonicalKeyDigest.AsSpan()),
                        Previous = capture.Generation,
                        Resulting = resulting,
                        Disposition = increment is null
                            ? capture.Exists ? BaseModuleGenerationPreparationDisposition.Preserved : BaseModuleGenerationPreparationDisposition.RemainedAbsent
                            : capture.Exists ? BaseModuleGenerationPreparationDisposition.Incremented : BaseModuleGenerationPreparationDisposition.Created,
                    });
                }
            }
            var lifetimes = new Dictionary<string, InMemorySubjectLifetimeState?>(StringComparer.Ordinal);
            var overlays = new Dictionary<string, BasePreparedSubjectOverlayEvidence>(StringComparer.Ordinal);
            var lifecycleIncarnations = new Dictionary<int, BaseSubjectIncarnation>();
            var subjectAuthorities = new Dictionary<string, BaseSubjectTransactionAuthorityEvidence>(StringComparer.Ordinal);
            var intervals = captured.ReadIntervals.ToBuilder();
            int authorityReads = captured.Accounting.Records;
            long retainedBytes = checked(captured.Accounting.TransientBytes + CanonicalPlanRetainedBytes(plan));
            foreach (BaseAtomicMutationPlanItem item in plan.Items)
            {
                if (item.SubjectLifecycle is not { } lifecycle) continue;
                string contractKey = SubjectContractKey(lifecycle.ContractId, lifecycle.ContractVersion);
                if (!_working.SubjectContracts.TryGetValue(contractKey, out InMemorySubjectContractState? contract)
                    || !string.Equals(contract.ContractChecksum, lifecycle.ContractChecksum, StringComparison.Ordinal))
                    return ValueTask.FromResult(SubjectFailure<BasePreparedAtomicMutation>(BaseSubjectErrorCodes.SchemaGenerationChanged, OperationStatus.Conflict, ErrorCategory.Conflict));
                authorityReads = checked(authorityReads + 1);
                subjectAuthorities[contractKey] = SubjectAuthority(contract);
                BaseExportedSubjectDefinition? definition = _owner._options.ExportedSubjects.FirstOrDefault(subject =>
                    string.Equals(subject.Id, lifecycle.ContractId, StringComparison.Ordinal) && subject.Version == lifecycle.ContractVersion);
                if (definition is null)
                    return ValueTask.FromResult(SubjectFailure<BasePreparedAtomicMutation>(BaseSubjectErrorCodes.ProviderContractInvalid));
                BaseOwnedSubjectScopeEvidence ownedScope = ScopeFor(item, lifecycle.ContractId, lifecycle.ContractVersion);
                string subjectKey = _owner.SubjectKey(ownedScope, lifecycle.ContractId, lifecycle.ContractVersion, lifecycle.SubjectId);
                intervals.Add(ExactInterval($"subject:{lifecycle.ContractId}:contract", System.Text.Encoding.UTF8.GetBytes(contractKey)));
                intervals.Add(ExactInterval($"subject:{lifecycle.ContractId}:lifetime", System.Text.Encoding.UTF8.GetBytes(subjectKey)));
                intervals.Add(ExactInterval($"subject:{lifecycle.ContractId}:record", System.Text.Encoding.UTF8.GetBytes(lifecycle.SubjectId.Value)));
                if (!lifetimes.TryGetValue(subjectKey, out InMemorySubjectLifetimeState? existingLifetime))
                {
                    _working.SubjectLifetimes.TryGetValue(subjectKey, out existingLifetime);
                    if (existingLifetime is null && lifecycle.Kind != BaseSubjectLifecycleMutationKind.Create)
                    {
                        BaseOwnedSubjectScopeEvidence originalScope = ScopeForCurrent(item, lifecycle.ContractId, lifecycle.ContractVersion);
                        string originalKey = _owner.SubjectKey(originalScope, lifecycle.ContractId, lifecycle.ContractVersion, lifecycle.SubjectId);
                        _working.SubjectLifetimes.TryGetValue(originalKey, out existingLifetime);
                        existingLifetime ??= _working.SubjectLifetimes.Values.SingleOrDefault(value =>
                            value.ContractId == lifecycle.ContractId && value.ContractVersion == lifecycle.ContractVersion &&
                            value.SubjectId.Equals(lifecycle.SubjectId) && value.PrivateCollectionId == item.Collection.Id &&
                            value.PrivateRecordId.Equals(item.RecordId));
                    }
                    lifetimes[subjectKey] = existingLifetime;
                    authorityReads = checked(authorityReads + 1);
                }
                switch (lifecycle.Kind)
                {
                    case BaseSubjectLifecycleMutationKind.Create:
                        if (existingLifetime is not null)
                            return ValueTask.FromResult(SubjectFailure<BasePreparedAtomicMutation>(BaseSubjectErrorCodes.TransactionConflict, OperationStatus.Conflict, ErrorCategory.Conflict));
                        long generation;
                        try
                        {
                            generation = _working.SubjectTerminals.TryGetValue(subjectKey, out InMemorySubjectTerminalState? terminal)
                                ? checked(terminal.LifetimeGeneration + 1) : 1;
                        }
                        catch (OverflowException)
                        {
                            return ValueTask.FromResult(SubjectFailure<BasePreparedAtomicMutation>(BaseSubjectErrorCodes.LifetimeGenerationExhausted, OperationStatus.Conflict, ErrorCategory.Conflict));
                        }
                        BaseSubjectIncarnation createdIncarnation = BaseSubjectIncarnation.Create(generation);
                        lifetimes[subjectKey] = new InMemorySubjectLifetimeState(
                            lifecycle.ContractId, lifecycle.ContractVersion, lifecycle.SubjectId,
                            createdIncarnation, generation, lifecycle.ResultingState, 1, ownedScope,
                            item.Collection.Id, item.RecordId,
                            checked(_working.GlobalMutationPosition + item.Ordinal + 1), checked(_working.GlobalMutationPosition + item.Ordinal + 1));
                        lifecycleIncarnations[item.Ordinal] = createdIncarnation;
                        break;
                    case BaseSubjectLifecycleMutationKind.Preserve:
                        if (existingLifetime is null)
                            return ValueTask.FromResult(SubjectFailure<BasePreparedAtomicMutation>(BaseSubjectErrorCodes.ProviderContractInvalid));
                        long subjectSequence;
                        try { subjectSequence = lifecycle.PublishFact ? checked(existingLifetime.SubjectSequence + 1) : existingLifetime.SubjectSequence; }
                        catch (OverflowException)
                        {
                            return ValueTask.FromResult(SubjectFailure<BasePreparedAtomicMutation>(BaseSubjectErrorCodes.SequenceExhausted, OperationStatus.Conflict, ErrorCategory.Conflict));
                        }
                        lifecycleIncarnations[item.Ordinal] = existingLifetime.Incarnation;
                        lifetimes[subjectKey] = existingLifetime with
                        {
                            LifecycleState = lifecycle.ResultingState,
                            SubjectSequence = subjectSequence,
                            LastLifecyclePosition = lifecycle.PublishFact ? checked(_working.GlobalMutationPosition + item.Ordinal + 1) : existingLifetime.LastLifecyclePosition,
                            Scope = ownedScope,
                        };
                        break;
                    case BaseSubjectLifecycleMutationKind.Retire:
                        if (existingLifetime is null)
                            return ValueTask.FromResult(SubjectFailure<BasePreparedAtomicMutation>(BaseSubjectErrorCodes.ProviderContractInvalid));
                        lifetimes[subjectKey] = null;
                        break;
                    default:
                        return ValueTask.FromResult(SubjectFailure<BasePreparedAtomicMutation>(BaseSubjectErrorCodes.ProviderContractInvalid));
                }
                lifetimes.TryGetValue(subjectKey, out InMemorySubjectLifetimeState? finalLifetime);
                RecordEnvelope? finalPrivateRecord = lifecycle.Kind == BaseSubjectLifecycleMutationKind.Retire
                    ? null
                    : new RecordEnvelope
                    {
                        CollectionId = item.Collection.Id, Id = item.RecordId,
                        Payload = RecordCloneHelpers.ClonePayload(item.ProposedPayload!),
                        Metadata = item.Current?.Metadata ?? new RecordMetadata(),
                    };
                bool? finalActive = null;
                string? finalScope = null;
                if (finalPrivateRecord is not null && definition.ValidationPlan.Active.Kind == BaseSubjectActiveBindingKind.RequiredBooleanField
                    && TryField(finalPrivateRecord, item.Collection.Id, definition.ValidationPlan.Active.FieldId!, out JsonElement activeElement)
                    && activeElement.ValueKind is JsonValueKind.True or JsonValueKind.False)
                    finalActive = activeElement.GetBoolean();
                if (finalPrivateRecord is not null && definition.ValidationPlan.Scope.Kind != BaseSubjectScopeBindingKind.Global
                    && TryField(finalPrivateRecord, item.Collection.Id, definition.ValidationPlan.Scope.FieldId!, out JsonElement scopeElement)
                    && scopeElement.ValueKind == JsonValueKind.String)
                {
                    finalScope = scopeElement.GetString();
                    try { _ = BaseSubjectId.Create(finalScope!, BaseSubjectIdKind.OrdinalString, 256); }
                    catch { return ValueTask.FromResult(SubjectFailure<BasePreparedAtomicMutation>(BaseSubjectErrorCodes.ProviderContractInvalid)); }
                }
                if (finalPrivateRecord is not null
                    && (definition.ValidationPlan.Active.Kind == BaseSubjectActiveBindingKind.RequiredBooleanField && finalActive is null
                        || definition.ValidationPlan.Scope.Kind != BaseSubjectScopeBindingKind.Global && finalScope is null))
                    return ValueTask.FromResult(SubjectFailure<BasePreparedAtomicMutation>(BaseSubjectErrorCodes.ProviderContractInvalid));
                overlays[subjectKey] = new BasePreparedSubjectOverlayEvidence
                {
                    ContractId = lifecycle.ContractId,
                    ContractVersion = lifecycle.ContractVersion,
                    SubjectId = lifecycle.SubjectId,
                    Exists = finalLifetime is not null && finalPrivateRecord is not null,
                    Incarnation = finalLifetime?.Incarnation,
                    Active = finalActive,
                    Scope = lifecycle.Kind == BaseSubjectLifecycleMutationKind.Retire ? ownedScope.Value : finalScope,
                    ProtectedScope = _owner._subjectScopes.Protect(ownedScope, _owner._subjectScopeProtectionKey),
                    LifecycleState = finalLifetime?.LifecycleState,
                    SubjectSequence = finalLifetime?.SubjectSequence,
                };
                if (lifecycle.Kind == BaseSubjectLifecycleMutationKind.Preserve)
                {
                    BaseOwnedSubjectScopeEvidence originalScope = ScopeForCurrent(item, lifecycle.ContractId, lifecycle.ContractVersion);
                    string originalKey = _owner.SubjectKey(originalScope, lifecycle.ContractId, lifecycle.ContractVersion, lifecycle.SubjectId);
                    if (!string.Equals(originalKey, subjectKey, StringComparison.Ordinal) && plan.SubjectValidations.Any(validation =>
                        validation.ValidationPlanId == definition.ValidationPlan.Id && validation.ValidationPlanVersion == definition.ValidationPlan.Version &&
                        validation.Reference.SubjectId.Equals(lifecycle.SubjectId) && validation.Scope.Kind == originalScope.Kind &&
                        string.Equals(validation.Scope.Value, originalScope.Value, StringComparison.Ordinal)))
                    {
                        lifetimes[originalKey] = null;
                        intervals.Add(ExactInterval($"subject:{lifecycle.ContractId}:lifetime", System.Text.Encoding.UTF8.GetBytes(originalKey)));
                        overlays[originalKey] = new BasePreparedSubjectOverlayEvidence
                        {
                            ContractId = lifecycle.ContractId, ContractVersion = lifecycle.ContractVersion,
                            SubjectId = lifecycle.SubjectId, Exists = false, Incarnation = null, Active = null,
                            Scope = originalScope.Value, ProtectedScope = _owner._subjectScopes.Protect(originalScope, _owner._subjectScopeProtectionKey),
                            LifecycleState = null, SubjectSequence = null,
                        };
                    }
                }
            }

            var validationEvidence = ImmutableArray.CreateBuilder<BasePreparedSubjectValidationEvidence>(plan.SubjectValidations.Length);
            for (int ordinal = 0; ordinal < plan.SubjectValidations.Length; ordinal++)
            {
                BaseSubjectReferenceValidationPlanItem validation = plan.SubjectValidations[ordinal];
                BaseExportedSubjectDefinition? definition = _owner._options.ExportedSubjects.FirstOrDefault(subject =>
                    string.Equals(subject.ValidationPlan.Id, validation.ValidationPlanId, StringComparison.Ordinal)
                    && subject.ValidationPlan.Version == validation.ValidationPlanVersion);
                bool valid = definition is not null;
                string subjectKey = definition is null ? string.Empty : _owner.SubjectKey(validation.Scope, definition.Id, definition.Version, validation.Reference.SubjectId);
                InMemorySubjectLifetimeState? lifetime = null;
                InMemorySubjectContractState? contract = null;
                RecordEnvelope? privateRecord = null;
                if (valid)
                {
                    bool authorityPresent = _working.SubjectContracts.TryGetValue(
                        SubjectContractKey(definition!.Id, definition.Version), out contract);
                    authorityReads = checked(authorityReads + 1);
                    bool lifetimeKnown = lifetimes.TryGetValue(subjectKey, out lifetime);
                    if (!lifetimeKnown)
                    {
                        _working.SubjectLifetimes.TryGetValue(subjectKey, out lifetime);
                        lifetimes[subjectKey] = lifetime;
                        authorityReads = checked(authorityReads + 1);
                    }
                    bool lifetimePresent = lifetime is not null;
                    if (contract is not null)
                        subjectAuthorities[SubjectContractKey(definition.Id, definition.Version)] = SubjectAuthority(contract);
                    if (lifetime is not null)
                        privateRecord = ResolveFinalRecord(
                            plan.Items,
                            definition.ValidationPlan.PrivateCollectionId,
                            lifetime.PrivateRecordId);
                    authorityReads = checked(authorityReads + 1);
                    valid = authorityPresent
                        && lifetimePresent
                        && privateRecord is not null
                        && contract!.AuthorityEpoch.Equals(validation.Reference.AuthorityEpoch)
                        && lifetime!.Incarnation.Equals(validation.Reference.Incarnation);
                }
                bool? active = null;
                string? scope = null;
                if (privateRecord is not null && definition!.ValidationPlan.Active.Kind == BaseSubjectActiveBindingKind.RequiredBooleanField)
                {
                    if (!TryField(privateRecord!, definition.ValidationPlan.PrivateCollectionId, definition.ValidationPlan.Active.FieldId!, out JsonElement activeValue)
                        || activeValue.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                        return ValueTask.FromResult(SubjectFailure<BasePreparedAtomicMutation>(BaseSubjectErrorCodes.ProviderContractInvalid));
                    active = activeValue.GetBoolean();
                }
                if (privateRecord is not null && definition!.ValidationPlan.Scope.Kind != BaseSubjectScopeBindingKind.Global)
                {
                    if (!TryField(privateRecord!, definition.ValidationPlan.PrivateCollectionId, definition.ValidationPlan.Scope.FieldId!, out JsonElement scopeValue)
                        || scopeValue.ValueKind != JsonValueKind.String)
                        return ValueTask.FromResult(SubjectFailure<BasePreparedAtomicMutation>(BaseSubjectErrorCodes.ProviderContractInvalid));
                    scope = scopeValue.GetString();
                    try { _ = BaseSubjectId.Create(scope!, BaseSubjectIdKind.OrdinalString, 256); }
                    catch { return ValueTask.FromResult(SubjectFailure<BasePreparedAtomicMutation>(BaseSubjectErrorCodes.ProviderContractInvalid)); }
                }
                if (valid)
                    valid = (validation.Requirement != BaseSubjectReferenceRequirement.Active
                            || active == definition!.ValidationPlan.Active.ActiveValue)
                        && (definition!.Scope == BaseSubjectScopeKind.Global
                            || string.Equals(scope, validation.Scope.Value, StringComparison.Ordinal));
                validationEvidence.Add(new BasePreparedSubjectValidationEvidence
                {
                    Ordinal = ordinal,
                    MutationOrdinal = validation.MutationOrdinal,
                    SourceFieldId = validation.SourceFieldId,
                    State = valid ? BaseSubjectValidationState.Valid : BaseSubjectValidationState.Invalid,
                });
                if (definition is not null)
                {
                    byte[] contractBytes = System.Text.Encoding.UTF8.GetBytes(SubjectContractKey(definition.Id, definition.Version));
                    byte[] subjectBytes = System.Text.Encoding.UTF8.GetBytes(subjectKey);
                    byte[] recordBytes = System.Text.Encoding.UTF8.GetBytes(validation.Reference.SubjectId.Value);
                    intervals.Add(ExactInterval($"subject:{definition.Id}:contract", contractBytes));
                    intervals.Add(ExactInterval($"subject:{definition.Id}:lifetime", subjectBytes));
                    intervals.Add(ExactInterval($"subject:{definition.Id}:record", recordBytes));
                    overlays[subjectKey] = new BasePreparedSubjectOverlayEvidence
                    {
                        ContractId = definition.Id,
                        ContractVersion = definition.Version,
                        SubjectId = validation.Reference.SubjectId,
                        Exists = lifetime is not null && privateRecord is not null,
                        Incarnation = lifetime?.Incarnation,
                        Active = active,
                        Scope = definition.Scope == BaseSubjectScopeKind.Global ? null : validation.Scope.Value,
                        ProtectedScope = _owner._subjectScopes.Protect(validation.Scope, _owner._subjectScopeProtectionKey),
                        LifecycleState = lifetime?.LifecycleState,
                        SubjectSequence = lifetime?.SubjectSequence,
                    };
                }
            }
            long evidenceBytes = BaseSubjectCanonicalRetainedWork.MeasureIntervals(intervals.ToImmutable());
            BasePreparedSubjectOverlayEvidence[] ownedOverlays = overlays.Values.ToArray();
            BaseSubjectTransactionAuthorityEvidence[] ownedAuthorities = subjectAuthorities.Values.ToArray();
            BasePreparedSubjectValidationEvidence[] ownedValidations = validationEvidence.ToArray();
            long addedIntervalBytes = checked(
                BaseSubjectCanonicalRetainedWork.MeasureIntervals(intervals.ToImmutable())
                - BaseSubjectCanonicalRetainedWork.MeasureIntervals(captured.ReadIntervals));
            retainedBytes = checked(retainedBytes + addedIntervalBytes
                + BaseSubjectCanonicalRetainedWork.MeasurePreparedEvidence(ownedOverlays, ownedAuthorities, ownedValidations)
                + BaseSubjectCanonicalRetainedWork.MeasureStringDictionary(lifetimes,
                    value => value is null ? 1L : checked(1L + CanonicalLifetimeRetainedBytes(value)))
                + BaseSubjectCanonicalRetainedWork.MeasureStringDictionary(overlays)
                + BaseSubjectCanonicalRetainedWork.MeasureStringDictionary(subjectAuthorities)
                + BaseSubjectCanonicalRetainedWork.MeasureIntegerDictionary(lifecycleIncarnations, static _ => 24L));
            long transient = retainedBytes;
            int intervalCount = intervals.Count;
            if (authorityReads > plan.Limits.MaximumAuthorityReads || intervals.Count > plan.Limits.MaximumReadIntervals
                || evidenceBytes > plan.Limits.MaximumEvidenceBytes || transient > plan.Limits.MaximumTransientBytes)
                return ValueTask.FromResult(SubjectFailure<BasePreparedAtomicMutation>(BaseSubjectErrorCodes.BudgetExceeded, OperationStatus.ValidationFailed, ErrorCategory.Validation));
            _preparedPlan = plan;
            _preparedLifecycleIncarnations = lifecycleIncarnations;
            _preparedMutation = new BasePreparedAtomicMutation
            {
                Kind = plan.Kind,
                PlanDigest = new string(plan.PlanDigest.AsSpan()), Authority = captured.Authority with { },
                SubjectAuthorities = subjectAuthorities.Values.OrderBy(static value => value.ContractId, StringComparer.Ordinal)
                    .ThenBy(static value => value.ContractVersion).ToImmutableArray(),
                Dispositions = plan.Kind == BaseAtomicMutationExecutionKind.ModuleMutation
                    ? plan.Items.Select(static item => item.Kind switch
                    {
                        BaseCommittedRecordMutationKind.Create => BaseCapturedMutationDisposition.Create,
                        BaseCommittedRecordMutationKind.Delete => BaseCapturedMutationDisposition.Delete,
                        _ => BaseCapturedMutationDisposition.Update,
                    }).ToImmutableArray()
                    : captured.Items.Select(static item => item.Disposition).ToImmutableArray(),
                Generations = preparedGenerations.MoveToImmutable(),
                SubjectOverlay = overlays.Values.OrderBy(static value => value.ContractId, StringComparer.Ordinal).ThenBy(static value => value.ContractVersion).ThenBy(static value => value.SubjectId.Value, StringComparer.Ordinal).ToImmutableArray(),
                SubjectValidations = validationEvidence.MoveToImmutable(),
                ReadIntervals = intervals.ToImmutable(),
                Accounting = new BasePreparedAtomicMutationAccounting
                {
                    AuthorityReads = authorityReads,
                    GenerationReads = captured.Generations.Length,
                    GenerationComparisons = plan.Module?.Comparisons.Length ?? 0,
                    GenerationIncrements = plan.Module?.Increments.Length ?? 0,
                    ReadIntervals = intervalCount,
                    SelectedBytes = captured.Accounting.SelectedBytes, GenerationBytes = captured.Accounting.GenerationBytes, EvidenceBytes = evidenceBytes,
                    TransientBytes = transient,
                },
            };
            return ValueTask.FromResult(OperationResults.Ok(_preparedMutation));
        });

        private static bool LifecycleProjectionBindingsValid(BaseAtomicMutationPlan plan, BaseCapturedAtomicMutationAuthority captured)
        {
            var expected = captured.LifecycleConsumerProjections.ToDictionary(
                static value => (value.ConsumerId, value.ConsumerVersion));
            foreach (BaseSubjectLifecycleMembershipPlanItem membership in plan.Items
                .SelectMany(static item => item.SubjectLifecycle?.Memberships ?? []))
            {
                if (!expected.TryGetValue((membership.ConsumerId, membership.ConsumerVersion), out BaseCapturedSubjectLifecycleConsumerProjection? projection)
                    || projection.ConsumerChecksum != membership.ConsumerChecksum
                    || projection.ProjectionGeneration != membership.ProjectionGeneration)
                    return false;
            }
            return true;
        }

        private static long CanonicalLifetimeRetainedBytes(InMemorySubjectLifetimeState value)
        {
            var counter = new BaseSubjectCanonicalRetainedWork();
            counter.AddContainer(); counter.AddString(value.ContractId); counter.AddInteger();
            counter.AddString(value.SubjectId.Value); counter.AddFixed16();
            counter.AddString(value.PrivateCollectionId); counter.AddString(value.PrivateRecordId.Value); counter.AddInteger();
            return counter.Bytes;
        }

        private static long CanonicalOverlayRetainedBytes(BasePreparedSubjectOverlayEvidence value) =>
            BaseSubjectCanonicalRetainedWork.MeasureOverlay(value);

        private static long CanonicalAuthorityRetainedBytes(BaseSubjectTransactionAuthorityEvidence value) =>
            BaseSubjectCanonicalRetainedWork.MeasureAuthority(value);

        private static long CanonicalPlanRetainedBytes(BaseAtomicMutationPlan plan) =>
            BaseSubjectCanonicalRetainedWork.MeasurePlan(plan);

        private BaseSubjectTransactionAuthorityEvidence SubjectAuthority(InMemorySubjectContractState contract) => new()
        {
            ContractId = new string(contract.ContractId.AsSpan()),
            ContractVersion = contract.ContractVersion,
            ContractChecksum = new string(contract.ContractChecksum.AsSpan()),
            StoreInstanceId = new string(_owner._options.StoreId.AsSpan()),
            RestoreEpoch = contract.RestoreEpoch,
            SchemaGeneration = 1,
            StateGeneration = contract.StateGeneration,
            AuthorityEpoch = new BaseSubjectAuthorityEpoch(contract.AuthorityEpoch.ToArray()),
        };

        private static BaseAtomicReadIntervalEvidence ExactInterval(string path, byte[] key) => new()
        {
            LogicalAccessPathId = path,
            CanonicalLowerBound = key.ToImmutableArray(), LowerInclusive = true,
            CanonicalUpperBound = key.ToImmutableArray(), UpperInclusive = true,
        };

        private static string CaptureRecordKey(string collectionId, RecordId recordId) =>
            collectionId + "\n" + recordId.Value;

        private static bool AuthorityCollectionsMatch(
            ImmutableArray<BaseCollectionGenerationRequirement> requirements,
            ImmutableArray<BaseAtomicMutationIntentItem> items,
            long generation)
        {
            string[] expected = items
                .Select(static item => item.Collection.Id)
                .Concat(items.SelectMany(static item => item.RelationTargets.Select(static target => target.TargetCollection.Id)))
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray();
            return requirements.Length == expected.Length
                && requirements.Select(static value => value.CollectionId).SequenceEqual(expected, StringComparer.Ordinal)
                && requirements.All(value => value.CollectionGeneration == generation);
        }

        private static bool AuthorityCollectionsMatch(
            ImmutableArray<BaseCollectionGenerationRequirement> requirements,
            BaseModuleMutationCaptureExtension module,
            long generation)
        {
            string[] expected = module.Records.Select(static value => value.Collection.Id)
                .Concat(module.RelationTargets.Select(static value => value.TargetCollection.Id))
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            return requirements.Length == expected.Length
                && requirements.Select(static value => value.CollectionId).SequenceEqual(expected, StringComparer.Ordinal)
                && requirements.All(value => value.CollectionGeneration == generation);
        }

        private static bool ValidGenerationScope(BaseModuleGenerationCaptureRequest capture)
        {
            bool keyed = capture.Cell.Scope is BaseModuleGenerationScope.TenantAndKey or BaseModuleGenerationScope.ProjectAndKey;
            if (capture.Cell.Scope != capture.Scope.Kind || capture.KeyUtf8.IsDefault
                || (keyed ? capture.KeyUtf8.IsDefaultOrEmpty || capture.KeyUtf8.Length > capture.Cell.MaximumKeyUtf8Bytes : !capture.KeyUtf8.IsEmpty))
                return false;
            return capture.Scope.Kind switch
            {
                BaseModuleGenerationScope.Application => capture.Scope.Tenant is null && capture.Scope.Project is null,
                BaseModuleGenerationScope.Tenant or BaseModuleGenerationScope.TenantAndKey =>
                    !string.IsNullOrEmpty(capture.Scope.Tenant) && capture.Scope.Project is null,
                BaseModuleGenerationScope.Project or BaseModuleGenerationScope.ProjectAndKey =>
                    capture.Scope.Tenant is null && !string.IsNullOrEmpty(capture.Scope.Project),
                _ => false,
            };
        }

        private static bool ModuleBindingsValid(BaseAtomicMutationPlan plan, BaseCapturedAtomicMutationAuthority captured)
        {
            if (plan.Module is null || plan.Module.ItemBindings.Length != plan.Items.Length) return false;
            if (plan.Module.RelationTargets.Select(static value => value.CaptureOrdinal).Distinct().Count()
                != plan.Module.RelationTargets.Length) return false;
            foreach (BaseAuthorizedModuleRelationTarget authorized in plan.Module.RelationTargets)
            {
                if (authorized.CaptureOrdinal < 0 || authorized.CaptureOrdinal >= captured.ModuleRelationTargets.Length) return false;
                BaseCapturedModuleRelationTarget actual = captured.ModuleRelationTargets[authorized.CaptureOrdinal];
                if (actual.Ordinal != authorized.CaptureOrdinal
                    || !string.Equals(actual.SourceStatementId, authorized.SourceStatementId, StringComparison.Ordinal)
                    || !string.Equals(actual.SourceFieldId, authorized.SourceFieldId, StringComparison.Ordinal)
                    || !string.Equals(actual.TargetCollectionId, authorized.TargetCollectionId, StringComparison.Ordinal)
                    || actual.TargetRecordId != authorized.TargetRecordId) return false;
            }
            var overlay = new Dictionary<(string CollectionId, RecordId RecordId), RecordEnvelope?>();
            for (int ordinal = 0; ordinal < plan.Items.Length; ordinal++)
            {
                BaseModuleMutationItemCaptureBinding binding = plan.Module.ItemBindings[ordinal];
                if (binding.MutationOrdinal != ordinal || binding.RecordCaptureOrdinal < 0
                    || binding.RecordCaptureOrdinal >= captured.ModuleRecords.Length) return false;
                BaseAtomicMutationPlanItem item = plan.Items[ordinal];
                BaseCapturedModuleRecord capture = captured.ModuleRecords[binding.RecordCaptureOrdinal];
                if (!string.Equals(item.Collection.Id, capture.CollectionId, StringComparison.Ordinal) || item.RecordId != capture.RecordId) return false;
                var key = (item.Collection.Id, item.RecordId);
                RecordEnvelope? expected = overlay.TryGetValue(key, out RecordEnvelope? prior) ? prior : capture.Current;
                if (!SameEnvelope(item.Current, expected)) return false;
                overlay[key] = item.Kind == BaseCommittedRecordMutationKind.Delete ? null : new RecordEnvelope
                {
                    CollectionId = item.Collection.Id, Id = item.RecordId,
                    Payload = RecordCloneHelpers.ClonePayload(item.ProposedPayload!),
                    Metadata = item.Current?.Metadata ?? new RecordMetadata(),
                };
            }
            return true;
        }

        private static bool SameEnvelope(RecordEnvelope? left, RecordEnvelope? right) => left is null && right is null
            || left is not null && right is not null
            && JsonSerializer.SerializeToUtf8Bytes(left, HPDBaseJsonSerializerContext.Default.RecordEnvelope).AsSpan()
                .SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(right, HPDBaseJsonSerializerContext.Default.RecordEnvelope));

        private static string ModuleGenerationStorageKey(BaseModuleGenerationCaptureRequest capture) => string.Join('\n',
            capture.Cell.Id,
            capture.Cell.Version.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ((int)capture.Scope.Kind).ToString(System.Globalization.CultureInfo.InvariantCulture),
            capture.Scope.Tenant ?? string.Empty,
            capture.Scope.Project ?? string.Empty,
            Convert.ToHexStringLower(capture.KeyUtf8.AsSpan()));

        private static RecordEnvelope? SimulateIntentRecord(
            BaseAtomicMutationIntentItem item,
            RecordEnvelope? current)
        {
            if (item.RequestedKind == BaseRecordMutationKind.Delete) return null;
            RecordPayload? payload = item.RequestedKind switch
            {
                BaseRecordMutationKind.Create => item.Create?.Payload,
                BaseRecordMutationKind.Replace => item.Replace?.Payload,
                BaseRecordMutationKind.Patch => MergeCapturePatch(current?.Payload, item.Patch?.Patch),
                BaseRecordMutationKind.Upsert when current is null => item.Upsert?.CreatePayload,
                BaseRecordMutationKind.Upsert when item.Upsert?.UpdateMode == RecordUpsertUpdateMode.Replace => item.Upsert.UpdatePayload,
                BaseRecordMutationKind.Upsert => MergeCapturePatch(current?.Payload, item.Upsert?.UpdatePayload),
                _ => null,
            };
            if (payload is null) return current;
            return new RecordEnvelope
            {
                CollectionId = item.Collection.Id,
                Id = item.RecordId,
                Payload = RecordCloneHelpers.ClonePayload(payload),
                Metadata = current?.Metadata is { } metadata ? RecordCloneHelpers.CloneMetadata(metadata) : new RecordMetadata(),
            };
        }

        private static RecordPayload? MergeCapturePatch(RecordPayload? current, RecordPayload? patch)
        {
            if (current?.Kind != RecordPayloadKind.FieldMap || current.Fields is null
                || patch?.Kind != RecordPayloadKind.FieldMap || patch.Fields is null)
                return patch;
            var fields = current.Fields.ToDictionary(
                static value => value.Key,
                static value => value.Value.Clone(),
                StringComparer.Ordinal);
            foreach ((string key, JsonElement value) in patch.Fields)
                fields[key] = value.Clone();
            return new RecordPayload { Kind = RecordPayloadKind.FieldMap, Fields = fields };
        }

        private RecordEnvelope? ResolveFinalRecord(ImmutableArray<BaseAtomicMutationPlanItem> items, string collectionId, RecordId id)
        {
            BaseAtomicMutationPlanItem? last = items.LastOrDefault(item => string.Equals(item.Collection.Id, collectionId, StringComparison.Ordinal) && item.RecordId == id);
            if (last is null)
            {
                CollectionDefinition collection = (_owner._options.Collections ?? []).Single(item => string.Equals(item.Id, collectionId, StringComparison.Ordinal));
                return SnapshotRecord(collection, id);
            }
            if (last.Kind == BaseCommittedRecordMutationKind.Delete) return null;
            return new RecordEnvelope
            {
                CollectionId = collectionId, Id = id, Payload = RecordCloneHelpers.ClonePayload(last.ProposedPayload!),
                Metadata = last.Current?.Metadata ?? new RecordMetadata(),
            };
        }

        private bool TryField(RecordEnvelope record, string collectionId, string fieldId, out JsonElement value)
        {
            value = default;
            CollectionDefinition? collection = (_owner._options.Collections ?? []).FirstOrDefault(item => string.Equals(item.Id, collectionId, StringComparison.Ordinal));
            string? wireName = collection?.Fields?.FirstOrDefault(field => string.Equals(field.Id, fieldId, StringComparison.Ordinal))?.WireName;
            return wireName is not null && record.Payload.Fields?.TryGetValue(wireName, out value) == true;
        }

        public ValueTask<OperationResult<BaseProvisionalAppliedAtomicMutation>> ApplyPreparedAtomicMutationAsync(
            BasePreparedAtomicMutation prepared,
            CancellationToken cancellationToken = default) => ExecuteAsync(cancellationToken, async token =>
        {
            token.ThrowIfCancellationRequested();
            if (!ReferenceEquals(prepared, _preparedMutation) || _preparedPlan is null)
                return SubjectFailure<BaseProvisionalAppliedAtomicMutation>(BaseSubjectErrorCodes.ProviderContractInvalid);

            BaseAtomicMutationPlan plan = _preparedPlan;
            _preparedMutation = null;
            Dictionary<int, BaseSubjectIncarnation> lifecycleIncarnations = _preparedLifecycleIncarnations
                ?? new Dictionary<int, BaseSubjectIncarnation>();
            _preparedLifecycleIncarnations = null;
            var facts = ImmutableArray.CreateBuilder<BaseOwnedMutationFact>(plan.Items.Length);
            long writtenBytes = 0;
            long factBytes = 0;
            foreach (BaseAtomicMutationPlanItem item in plan.Items)
            {
                token.ThrowIfCancellationRequested();
                RecordMutationSessionContext context = new()
                {
                    ItemId = item.ItemId,
                    RequestedOperation = item.RequestedKind,
                    EventId = item.EventId,
                    Operation = item.Operation,
                    ChangedFields = item.ChangedFields.ToArray(),
                };
                OperationResult<RecordMutationSessionResult> mutation;
                switch (item.Kind)
                {
                    case BaseCommittedRecordMutationKind.Create:
                    {
                        OperationResult<RecordEnvelope> result = await _owner.CreateCoreAsync(
                            _working, item.Collection,
                            new RecordCreateRequest { RequestedId = item.RecordId, Payload = item.ProposedPayload! },
                            item.Operation, token, item.RuntimeAssignedRecordId).ConfigureAwait(false);
                        mutation = ProjectMutation(result, item.Collection, context, item.Kind, null, result.Value, null, item.ChangedFields.ToArray());
                        break;
                    }
                    case BaseCommittedRecordMutationKind.Patch:
                    {
                        RecordEnvelope? before = SnapshotRecord(item.Collection, item.RecordId);
                        OperationResult<RecordEnvelope> result = await _owner.PatchCoreAsync(
                            _working, item.Collection, item.RecordId,
                            new RecordPatchRequest { Patch = PatchDelta(item), ExpectedRevision = before?.Metadata.Revision },
                            item.Operation, token).ConfigureAwait(false);
                        mutation = ProjectMutation(result, item.Collection, context, item.Kind, before, result.Value, null, item.ChangedFields.ToArray());
                        break;
                    }
                    case BaseCommittedRecordMutationKind.Replace:
                    {
                        RecordEnvelope? before = SnapshotRecord(item.Collection, item.RecordId);
                        OperationResult<RecordEnvelope> result = await _owner.ReplaceCoreAsync(
                            _working, item.Collection, item.RecordId,
                            new RecordReplaceRequest { Payload = item.ProposedPayload!, ExpectedRevision = before?.Metadata.Revision },
                            item.Operation, token).ConfigureAwait(false);
                        mutation = ProjectMutation(result, item.Collection, context, item.Kind, before, result.Value, null, item.ChangedFields.ToArray());
                        break;
                    }
                    case BaseCommittedRecordMutationKind.Delete:
                    {
                        RecordEnvelope? before = SnapshotRecord(item.Collection, item.RecordId);
                        OperationResult<DeleteResult> result = await _owner.DeleteCoreAsync(
                            _working, item.Collection, item.RecordId,
                            item.Delete! with { ExpectedRevision = before?.Metadata.Revision },
                            item.Operation, token, () => _relationChecks = checked(_relationChecks + 1)).ConfigureAwait(false);
                        mutation = ProjectMutation(result, item.Collection, context, item.Kind, before, null, result.Value, null);
                        break;
                    }
                    default:
                        return SubjectFailure<BaseProvisionalAppliedAtomicMutation>(BaseSubjectErrorCodes.ProviderContractInvalid);
                }
                if (!mutation.IsSuccess() || mutation.Value is null)
                    return new OperationResult<BaseProvisionalAppliedAtomicMutation> { Status = mutation.Status, Error = mutation.Error };
                long journalPosition = checked(++_working.GlobalMutationPosition);
                BaseSubjectLifecycleCommitEvidence? committedLifecycle = null;
                if (item.SubjectLifecycle is { } plannedLifecycle)
                {
                    BaseOwnedSubjectScopeEvidence plannedScope = ScopeFor(item, plannedLifecycle.ContractId, plannedLifecycle.ContractVersion);
                    string lifetimeKey = _owner.SubjectKey(plannedScope, plannedLifecycle.ContractId, plannedLifecycle.ContractVersion, plannedLifecycle.SubjectId);
                    _working.SubjectLifetimes.TryGetValue(lifetimeKey, out InMemorySubjectLifetimeState? previousLifetime);
                    if (previousLifetime is null && plannedLifecycle.Kind != BaseSubjectLifecycleMutationKind.Create)
                    {
                        BaseOwnedSubjectScopeEvidence originalScope = ScopeForCurrent(item, plannedLifecycle.ContractId, plannedLifecycle.ContractVersion);
                        _working.SubjectLifetimes.TryGetValue(_owner.SubjectKey(originalScope, plannedLifecycle.ContractId, plannedLifecycle.ContractVersion, plannedLifecycle.SubjectId), out previousLifetime);
                        previousLifetime ??= _working.SubjectLifetimes.Values.SingleOrDefault(value =>
                            value.ContractId == plannedLifecycle.ContractId && value.ContractVersion == plannedLifecycle.ContractVersion &&
                            value.SubjectId.Equals(plannedLifecycle.SubjectId) && value.PrivateCollectionId == item.Collection.Id &&
                            value.PrivateRecordId.Equals(item.RecordId));
                    }
                    InMemorySubjectContractState contract = _working.SubjectContracts[SubjectContractKey(plannedLifecycle.ContractId, plannedLifecycle.ContractVersion)];
                    BaseSubjectIncarnation incarnation = plannedLifecycle.Kind == BaseSubjectLifecycleMutationKind.Create
                        ? lifecycleIncarnations.GetValueOrDefault(item.Ordinal)
                        : previousLifetime?.Incarnation ?? default;
                    long sequence = plannedLifecycle.Kind == BaseSubjectLifecycleMutationKind.Create
                        ? 1
                        : plannedLifecycle.PublishFact
                            ? checked((previousLifetime?.SubjectSequence ?? 0) + 1)
                            : previousLifetime?.SubjectSequence ?? 0;
                    if (incarnation.Equals(default(BaseSubjectIncarnation)) || sequence <= 0)
                        return SubjectFailure<BaseProvisionalAppliedAtomicMutation>(BaseSubjectErrorCodes.ProviderContractInvalid);
                    committedLifecycle = new BaseSubjectLifecycleCommitEvidence
                    {
                        ContractId = plannedLifecycle.ContractId,
                        ContractVersion = plannedLifecycle.ContractVersion,
                        SubjectId = plannedLifecycle.SubjectId.Value,
                        Kind = plannedLifecycle.Kind,
                        AuthorityEpoch = contract.AuthorityEpoch,
                        Incarnation = incarnation,
                        SubjectSequence = sequence,
                        ContractStateGeneration = contract.StateGeneration,
                        DeliveryEpoch = _working.SubjectLifecycleDeliveryEpoch,
                        Scope = ScopeFor(item, plannedLifecycle.ContractId, plannedLifecycle.ContractVersion),
                        PreviousState = plannedLifecycle.PreviousState,
                        ResultingState = plannedLifecycle.ResultingState,
                        CommitPosition = new BaseMutationJournalPosition(journalPosition),
                    };
                }
                BaseRecordMutationFact journaledFact = mutation.Value.Mutation with
                {
                    Event = mutation.Value.Mutation.Event with
                    {
                        PublishedAt = Now(item.Operation),
                        Stream = "base.mutations",
                        Guarantee = EventDeliveryGuarantee.Transactional,
                    },
                    JournalPosition = new BaseMutationJournalPosition(journalPosition),
                    SubjectLifecycle = committedLifecycle,
                };
                _working.MutationJournal.Add(journalPosition, CreateJournalEntry(journaledFact, item.Operation.TenantId));
                BaseOwnedMutationFact owned = BaseOwnedMutationFact.Freeze(journaledFact, 1);
                facts.Add(owned);
                factBytes = checked(factBytes + owned.EncodedLength);
                writtenBytes = checked(writtenBytes + (mutation.Value.Record is null
                    ? System.Text.Encoding.UTF8.GetByteCount(item.RecordId.Value) + sizeof(long)
                    : JsonSerializer.SerializeToUtf8Bytes(mutation.Value.Record, HPDBaseJsonSerializerContext.Default.RecordEnvelope).LongLength));
            }

            foreach (BaseAtomicMutationPlanItem item in plan.Items)
            {
                if (item.SubjectLifecycle is not { } lifecycle) continue;
                BaseOwnedSubjectScopeEvidence itemScope = ScopeFor(item, lifecycle.ContractId, lifecycle.ContractVersion);
                string key = _owner.SubjectKey(itemScope, lifecycle.ContractId, lifecycle.ContractVersion, lifecycle.SubjectId);
                _working.SubjectLifetimes.TryGetValue(key, out InMemorySubjectLifetimeState? previousLifetime);
                string previousKey = key;
                if (previousLifetime is null && lifecycle.Kind != BaseSubjectLifecycleMutationKind.Create)
                {
                    BaseOwnedSubjectScopeEvidence originalScope = ScopeForCurrent(item, lifecycle.ContractId, lifecycle.ContractVersion);
                    previousKey = _owner.SubjectKey(originalScope, lifecycle.ContractId, lifecycle.ContractVersion, lifecycle.SubjectId);
                    _working.SubjectLifetimes.TryGetValue(previousKey, out previousLifetime);
                    if (previousLifetime is null)
                    {
                        KeyValuePair<string, InMemorySubjectLifetimeState> located = _working.SubjectLifetimes.SingleOrDefault(pair =>
                            pair.Value.ContractId == lifecycle.ContractId && pair.Value.ContractVersion == lifecycle.ContractVersion &&
                            pair.Value.SubjectId.Equals(lifecycle.SubjectId) && pair.Value.PrivateCollectionId == item.Collection.Id &&
                            pair.Value.PrivateRecordId.Equals(item.RecordId));
                        if (located.Value is not null) { previousKey = located.Key; previousLifetime = located.Value; }
                    }
                }
                BaseSubjectIncarnation committedIncarnation;
                long committedGeneration;
                long committedSequence;
                BaseOwnedSubjectScopeEvidence committedScope;
                switch (lifecycle.Kind)
                {
                    case BaseSubjectLifecycleMutationKind.Create:
                        if (!lifecycleIncarnations.TryGetValue(item.Ordinal, out BaseSubjectIncarnation incarnation)
                            || !_working.SubjectLifetimes.TryAdd(key,
                            new InMemorySubjectLifetimeState(
                                lifecycle.ContractId, lifecycle.ContractVersion, lifecycle.SubjectId, incarnation,
                                incarnation.LifetimeGeneration, lifecycle.ResultingState, 1,
                                ScopeFor(item, lifecycle.ContractId, lifecycle.ContractVersion),
                                item.Collection.Id, item.RecordId, facts[item.Ordinal].MaterializeOwned().JournalPosition.Value,
                                facts[item.Ordinal].MaterializeOwned().JournalPosition.Value)))
                            return SubjectFailure<BaseProvisionalAppliedAtomicMutation>(BaseSubjectErrorCodes.ProviderContractInvalid);
                        committedIncarnation = incarnation; committedGeneration = incarnation.LifetimeGeneration; committedSequence = 1;
                        committedScope = ScopeFor(item, lifecycle.ContractId, lifecycle.ContractVersion);
                        break;
                    case BaseSubjectLifecycleMutationKind.Preserve:
                        if (previousLifetime is null)
                            return SubjectFailure<BaseProvisionalAppliedAtomicMutation>(BaseSubjectErrorCodes.ProviderContractInvalid);
                        committedSequence = lifecycle.PublishFact ? checked(previousLifetime.SubjectSequence + 1) : previousLifetime.SubjectSequence;
                        if (!string.Equals(previousKey, key, StringComparison.Ordinal)) _working.SubjectLifetimes.Remove(previousKey);
                        _working.SubjectLifetimes[key] = previousLifetime with
                        {
                            LifecycleState = lifecycle.ResultingState,
                            SubjectSequence = committedSequence,
                            Scope = itemScope,
                            LastLifecyclePosition = lifecycle.PublishFact ? facts[item.Ordinal].MaterializeOwned().JournalPosition.Value : previousLifetime.LastLifecyclePosition,
                        };
                        committedIncarnation = previousLifetime.Incarnation; committedGeneration = previousLifetime.LifetimeGeneration; committedScope = previousLifetime.Scope;
                        break;
                    case BaseSubjectLifecycleMutationKind.Retire:
                        if (previousLifetime is null || !_working.SubjectLifetimes.Remove(previousKey))
                            return SubjectFailure<BaseProvisionalAppliedAtomicMutation>(BaseSubjectErrorCodes.ProviderContractInvalid);
                        committedIncarnation = previousLifetime.Incarnation; committedGeneration = previousLifetime.LifetimeGeneration;
                        committedSequence = checked(previousLifetime.SubjectSequence + 1); committedScope = previousLifetime.Scope;
                        InMemorySubjectContractState terminalContract = _working.SubjectContracts[SubjectContractKey(lifecycle.ContractId, lifecycle.ContractVersion)];
                        long retiredPosition = facts[item.Ordinal].MaterializeOwned().JournalPosition.Value;
                        string terminalChecksum = BaseSubjectTerminalIntegrity.Compute(
                            lifecycle.ContractId, lifecycle.ContractVersion, lifecycle.SubjectId, committedScope,
                            terminalContract.AuthorityEpoch, committedIncarnation, committedGeneration, committedSequence,
                            new BaseMutationJournalPosition(retiredPosition), terminalContract.StateGeneration, terminalContract.RestoreEpoch);
                        _working.SubjectTerminals[key] = new InMemorySubjectTerminalState(
                            lifecycle.ContractId, lifecycle.ContractVersion, lifecycle.SubjectId, committedScope,
                            terminalContract.AuthorityEpoch,
                            committedIncarnation, committedGeneration, committedSequence,
                            retiredPosition, terminalContract.StateGeneration, terminalContract.RestoreEpoch, terminalChecksum);
                        break;
                    default:
                        return SubjectFailure<BaseProvisionalAppliedAtomicMutation>(BaseSubjectErrorCodes.ProviderContractInvalid);
                }
                if (lifecycle.PublishFact)
                {
                    if (_working.SubjectLifecycleFacts.Count >= BaseSubjectLifecycleProviderCapabilities.BuiltIn.MaximumRetainedFacts)
                        return SubjectFailure<BaseProvisionalAppliedAtomicMutation>(BaseSubjectErrorCodes.LifecycleCapacityExceeded);
                    InMemorySubjectContractState contract = _working.SubjectContracts[SubjectContractKey(lifecycle.ContractId, lifecycle.ContractVersion)];
                    var position = facts[item.Ordinal].MaterializeOwned().JournalPosition;
                    var boundary = new BaseSubjectLifecycleOrderingBoundary
                    {
                        CommitPosition = position, SubjectId = lifecycle.SubjectId, AuthorityEpoch = contract.AuthorityEpoch,
                        Incarnation = committedIncarnation, SubjectSequence = committedSequence,
                    };
                    var lifecycleFact = new BaseSubjectLifecycleFact
                    {
                        CommitPosition = position, ContractId = lifecycle.ContractId, ContractVersion = lifecycle.ContractVersion,
                        SubjectId = lifecycle.SubjectId, AuthorityEpoch = contract.AuthorityEpoch, Incarnation = committedIncarnation,
                        SubjectSequence = committedSequence, ContractStateGeneration = contract.StateGeneration,
                        DeliveryEpoch = _working.SubjectLifecycleDeliveryEpoch,
                        Kind = lifecycle.Kind == BaseSubjectLifecycleMutationKind.Create ? BaseSubjectLifecycleFactKind.Created
                            : lifecycle.Kind == BaseSubjectLifecycleMutationKind.Retire ? BaseSubjectLifecycleFactKind.Retired
                            : BaseSubjectLifecycleFactKind.Transitioned,
                        Created = lifecycle.Kind == BaseSubjectLifecycleMutationKind.Create ? new() { CurrentState = BaseSubjectLifecycleState.Active } : null,
                        Transitioned = lifecycle.Kind == BaseSubjectLifecycleMutationKind.Preserve ? new() { PreviousState = lifecycle.PreviousState!.Value, CurrentState = lifecycle.ResultingState } : null,
                        Retired = lifecycle.Kind == BaseSubjectLifecycleMutationKind.Retire ? new() { PreviousState = BaseSubjectLifecycleState.Tombstoned } : null,
                    };
                    int factIndex = _working.SubjectLifecycleFacts.Count;
                    BaseProtectedSubjectScope protectedCommittedScope = _owner._subjectScopes.Protect(committedScope, _owner._subjectScopeProtectionKey);
                    _working.SubjectLifecycleFacts.Add(new InMemorySubjectLifecycleFactRow(protectedCommittedScope, boundary, lifecycleFact));
                    foreach (BaseSubjectLifecycleMembershipPlanItem membership in lifecycle.Memberships)
                    {
                        int membershipIndex = _working.SubjectLifecycleMemberships.Count;
                        _working.SubjectLifecycleMemberships.Add(new InMemorySubjectLifecycleMembershipRow(
                            membership.ConsumerId, membership.ConsumerVersion, membership.ConsumerChecksum,
                            membership.ProjectionGeneration, membership.MatchedObservedState, protectedCommittedScope, factIndex));
                        string membershipKey = ProtectedScopeKey(membership.ConsumerId, membership.ConsumerVersion, protectedCommittedScope);
                        if (!_working.SubjectLifecycleMembershipIndex.TryGetValue(membershipKey, out List<int>? indexes))
                            _working.SubjectLifecycleMembershipIndex.Add(membershipKey, indexes = []);
                        indexes.Add(membershipIndex);
                    }
                }
            }

            if (plan.Kind == BaseAtomicMutationExecutionKind.ModuleMutation)
            {
                if (_capturedModuleGenerationKeys is null || prepared.Generations.Length != _capturedModuleGenerationKeys.Count)
                    return SubjectFailure<BaseProvisionalAppliedAtomicMutation>(BaseSubjectErrorCodes.ProviderContractInvalid);
                foreach (BasePreparedModuleGenerationEvidence generation in prepared.Generations)
                {
                    if (!_capturedModuleGenerationKeys.TryGetValue(generation.CaptureOrdinal, out string? key))
                        return SubjectFailure<BaseProvisionalAppliedAtomicMutation>(BaseSubjectErrorCodes.ProviderContractInvalid);
                    if (generation.Resulting is null)
                        _working.ModuleGenerations.Remove(key);
                    else
                        _working.ModuleGenerations[key] = generation.Resulting.Value;
                }
                _capturedModuleGenerationKeys = null;
            }

            BaseRecordMutationFact[] materialized = facts.Select(static fact => fact.MaterializeOwned()).ToArray();
            if (_owner._vectorProjection is { } projection)
            {
                OperationResult projected;
                try
                {
                    projected = await projection.ApplyAsync(
                        new BaseInMemoryProjectionMutationContext(
                            _working,
                            _owner._options,
                            BaseAtomicMutationProjectionFactory.Create(materialized),
                            checked(_working.GlobalMutationPosition - materialized.Length)),
                        token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                catch { return SubjectFailure<BaseProvisionalAppliedAtomicMutation>(BaseSubjectErrorCodes.ProviderContractInvalid); }
                if (!projected.IsSuccess())
                    return new OperationResult<BaseProvisionalAppliedAtomicMutation> { Status = projected.Status, Error = projected.Error };
            }
            long journalBytes = materialized.Sum(static fact =>
                (long)JsonSerializer.SerializeToUtf8Bytes(fact, HPDBaseJsonSerializerContext.Default.BaseRecordMutationFact).LongLength);
            long transient = checked(prepared.Accounting.TransientBytes + writtenBytes + factBytes + journalBytes);
            ImmutableArray<BaseModuleCommittedGeneration> generations = CommittedGenerations(prepared);
            var applied = new BaseProvisionalAppliedAtomicMutation
            {
                Kind = plan.Kind,
                PlanDigest = new string(plan.PlanDigest.AsSpan()),
                Authority = prepared.Authority with { },
                Facts = facts.MoveToImmutable(),
                Generations = generations,
                Accounting = new BaseProvisionalAtomicMutationAccounting
                {
                    WrittenBytes = writtenBytes,
                    GenerationBytes = prepared.Accounting.GenerationBytes,
                    FactBytes = factBytes,
                    JournalBytes = journalBytes,
                    RelationChecks = _relationChecks,
                    UniqueConstraintChecks = _uniqueChecks,
                    AuthorityReads = prepared.Accounting.AuthorityReads,
                    ReadIntervals = prepared.ReadIntervals.Length,
                    SelectedBytes = prepared.Accounting.SelectedBytes,
                    EvidenceBytes = prepared.Accounting.EvidenceBytes,
                    TransientBytes = transient,
                },
            };
            _appliedProvisional = applied;
            return OperationResults.Ok(applied);
        });

        internal bool ValidateCommitFinalization(AtomicMutationProcessingResult processing)
        {
            if (processing.Receipt.Kind != BaseAtomicReceiptResultKind.ModuleMutation)
                return processing.Finalization is null;
            BaseAtomicMutationCommitFinalization? finalization = processing.Finalization;
            BaseProvisionalAppliedAtomicMutation? applied = _appliedProvisional;
            BaseModuleMutationReceiptResult? module = processing.Receipt.ModuleMutation;
            if (finalization is null || applied is null || module is null
                || !ReferenceEquals(finalization.Receipt, processing.Receipt)
                || !string.Equals(finalization.PlanDigest, applied.PlanDigest, StringComparison.Ordinal)
                || !finalization.CanonicalResultBytes.AsSpan().SequenceEqual(module.CanonicalResultBytes.AsSpan())
                || !applied.Facts.Select(static value => value.CopyCanonicalBytes()).SequenceEqual(
                    processing.Receipt.Mutations.Select(static value => value.CopyCanonicalBytes()), ByteArrayComparer.Instance))
                return false;
            long receiptBytes;
            try
            {
                receiptBytes = JsonSerializer.SerializeToUtf8Bytes(
                    BaseAtomicReceiptWire.From(processing.Receipt),
                    HPDBaseJsonSerializerContext.Default.BaseAtomicReceiptWire).LongLength;
            }
            catch { return false; }
            BaseAtomicCommitAccounting actual = finalization.Accounting;
            BaseProvisionalAtomicMutationAccounting prior = applied.Accounting;
            long resultBytes = finalization.CanonicalResultBytes.Length;
            return actual.WrittenBytes == prior.WrittenBytes
                && actual.GenerationBytes == prior.GenerationBytes
                && actual.FactBytes == prior.FactBytes
                && actual.JournalBytes == prior.JournalBytes
                && actual.ReceiptBytes == receiptBytes
                && actual.ResultBytes == resultBytes
                && actual.RelationChecks == prior.RelationChecks
                && actual.UniqueConstraintChecks == prior.UniqueConstraintChecks
                && actual.AuthorityReads == prior.AuthorityReads
                && actual.ReadIntervals == prior.ReadIntervals
                && actual.SelectedBytes == prior.SelectedBytes
                && actual.EvidenceBytes == prior.EvidenceBytes
                && actual.TransientBytes == checked(prior.TransientBytes + receiptBytes + resultBytes);
        }

        private sealed class ByteArrayComparer : IEqualityComparer<byte[]>
        {
            internal static ByteArrayComparer Instance { get; } = new();
            public bool Equals(byte[]? left, byte[]? right) => left is not null && right is not null && left.AsSpan().SequenceEqual(right);
            public int GetHashCode(byte[] value) => 0;
        }

        private ImmutableArray<BaseModuleCommittedGeneration> CommittedGenerations(BasePreparedAtomicMutation prepared)
        {
            if (prepared.Kind != BaseAtomicMutationExecutionKind.ModuleMutation) return [];
            BaseModuleMutationCaptureExtension module = _capturedModuleExtension
                ?? throw new InvalidOperationException(BaseSubjectErrorCodes.ProviderContractInvalid);
            return prepared.Generations
                .Where(static generation => generation.Disposition is BaseModuleGenerationPreparationDisposition.Created
                    or BaseModuleGenerationPreparationDisposition.Incremented)
                .Select(generation =>
                {
                    BaseModuleGenerationCaptureRequest capture = module.Generations.Single(item => item.Ordinal == generation.CaptureOrdinal);
                    return new BaseModuleCommittedGeneration
                    {
                        CaptureId = capture.CaptureId,
                        CellId = capture.Cell.Id,
                        CellVersion = capture.Cell.Version,
                        Previous = generation.Previous,
                        Resulting = generation.Resulting!,
                    };
                }).ToImmutableArray();
        }

        private static RecordPayload PatchDelta(BaseAtomicMutationPlanItem item)
        {
            Dictionary<string, JsonElement> proposed = item.ProposedPayload?.Fields
                ?? throw new InvalidOperationException(BaseSubjectErrorCodes.ProviderContractInvalid);
            var fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (string name in item.ChangedFields)
                if (proposed.TryGetValue(name, out JsonElement value)) fields[name] = value.Clone();
            return new RecordPayload { Kind = RecordPayloadKind.FieldMap, Fields = fields };
        }

        private BaseOwnedSubjectScopeEvidence ScopeFor(BaseAtomicMutationPlanItem item, string contractId, int contractVersion)
        {
            BaseExportedSubjectDefinition definition = _owner._options.ExportedSubjects.Single(subject =>
                subject.Id == contractId && subject.Version == contractVersion);
            string? value = definition.Scope switch
            {
                BaseSubjectScopeKind.Global => null,
                BaseSubjectScopeKind.Tenant or BaseSubjectScopeKind.Project => ReadScopeValue(item, definition),
                _ => null,
            };
            return new BaseOwnedSubjectScopeEvidence { Kind = definition.Scope, Value = value };
        }

        private BaseOwnedSubjectScopeEvidence ScopeForCurrent(BaseAtomicMutationPlanItem item, string contractId, int contractVersion)
        {
            BaseExportedSubjectDefinition definition = _owner._options.ExportedSubjects.Single(subject =>
                subject.Id == contractId && subject.Version == contractVersion);
            string? value = null;
            if (definition.Scope != BaseSubjectScopeKind.Global)
            {
                string fieldId = definition.ValidationPlan.Scope.FieldId
                    ?? throw new InvalidOperationException(BaseSubjectErrorCodes.ProviderContractInvalid);
                string wireName = (item.Collection.Fields ?? []).Single(field => field.Id == fieldId).WireName;
                if (item.Current?.Payload.Fields is { } fields && fields.TryGetValue(wireName, out JsonElement element) && element.ValueKind == JsonValueKind.String)
                    value = element.GetString();
            }
            return new BaseOwnedSubjectScopeEvidence { Kind = definition.Scope, Value = value };
        }

        private static string? ReadScopeValue(BaseAtomicMutationPlanItem item, BaseExportedSubjectDefinition definition)
        {
            string fieldId = definition.ValidationPlan.Scope.FieldId
                ?? throw new InvalidOperationException(BaseSubjectErrorCodes.ProviderContractInvalid);
            string wireName = (item.Collection.Fields ?? []).Single(field => field.Id == fieldId).WireName;
            RecordPayload? payload = item.Kind == BaseCommittedRecordMutationKind.Delete ? item.Current?.Payload : item.ProposedPayload;
            return payload?.Fields is { } fields && fields.TryGetValue(wireName, out JsonElement value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }

        private static OperationResult<T> SubjectFailure<T>(string code, OperationStatus status = OperationStatus.StoreError, ErrorCategory category = ErrorCategory.Store) => new()
        {
            Status = status,
            Error = new BaseError { Code = code, Message = "The subject mutation provider operation failed.", Category = category },
        };

        private OperationResult<BaseCapturedAtomicMutationAuthority> CaptureSelectionAuthority(
            BaseAtomicMutationCaptureRequest capture,
            CancellationToken cancellationToken)
        {
            BaseAtomicSelectionRequest? request = capture.Selection?.Selection;
            BaseAtomicMutationIntent intent = capture.Intent;
            BaseAtomicMutationExecutionLimits limits = capture.Limits;
            ArgumentNullException.ThrowIfNull(request);
            BaseCollectionGenerationRequirement? collectionAuthority = intent.Authority.Collections.SingleOrDefault(
                value => string.Equals(value.CollectionId, request.Collection.Id, StringComparison.Ordinal));
            if (_capturedMutation is not null || capture.Selection is null || capture.Module is not null
                || !intent.Items.IsDefaultOrEmpty || collectionAuthority is null
                || !string.Equals(intent.Authority.StoreInstanceId, _owner._options.StoreId, StringComparison.Ordinal)
                || intent.Authority.RestoreEpoch != 0
                || intent.Authority.SchemaGeneration != 1
                || collectionAuthority.CollectionGeneration != _owner._generation)
            {
                return SelectionFailure<BaseCapturedAtomicMutationAuthority>(
                    OperationStatus.ValidationFailed,
                    "base.provider.selection.authorityInvalid",
                    ErrorCategory.Validation);
            }

            if (limits.MaximumSelectedRecords < 1
                || limits.MaximumSelectedBytes < 1
                || limits.MaximumReadIntervals < 1
                || limits.MaximumTransientBytes < 1
                || request.CanonicalRecordCodecVersion < 1)
            {
                return SelectionFailure<BaseCapturedAtomicMutationAuthority>(
                    OperationStatus.ValidationFailed,
                    "base.provider.selection.limitExceeded",
                    ErrorCategory.Validation);
            }

            cancellationToken.ThrowIfCancellationRequested();
            InMemoryCollectionState? collection = GetCollectionOrNull(_working, request.Collection.Id);
            IEnumerable<StoredRecord> source = collection is null
                ? Enumerable.Empty<StoredRecord>()
                : collection.RecordsById.Values;
            var records = source
                .Where(record => BaseRecordFilterMatcher.Matches(
                    RecordCloneHelpers.CloneEnvelope(record), request.Query.Filter))
                .ToList();
            QueryResult<List<StoredRecord>, BaseCapturedAtomicMutationAuthority> sorted =
                ApplySort<BaseCapturedAtomicMutationAuthority>(records, request.Query.Sort);
            if (sorted.Result is { } sortFailure)
            {
                return SelectionFailure<BaseCapturedAtomicMutationAuthority>(
                    OperationStatus.Unsupported,
                    "base.provider.selection.queryUnsupported",
                    ErrorCategory.Unsupported);
            }

            int requested = request.Query.Page?.Limit ?? limits.MaximumSelectedRecords;
            if (requested < 1 || requested > limits.MaximumSelectedRecords)
            {
                return SelectionFailure<BaseCapturedAtomicMutationAuthority>(
                    OperationStatus.ValidationFailed,
                    "base.provider.selection.limitExceeded",
                    ErrorCategory.Validation);
            }

            var owned = ImmutableArray.CreateBuilder<BaseOwnedSelectedRecord>(Math.Min(requested, records.Count));
            long selectedBytes = 0;
            foreach (StoredRecord record in records.Take(requested))
            {
                cancellationToken.ThrowIfCancellationRequested();
                BaseOwnedSelectedRecord frozen = BaseOwnedSelectedRecord.Freeze(
                    RecordCloneHelpers.CloneEnvelope(record), owned.Count, request.CanonicalRecordCodecVersion);
                selectedBytes = checked(selectedBytes + frozen.CanonicalBytes);
                if (selectedBytes > limits.MaximumSelectedBytes
                    || selectedBytes > limits.MaximumTransientBytes)
                {
                    return SelectionFailure<BaseCapturedAtomicMutationAuthority>(
                        OperationStatus.ValidationFailed,
                        "base.provider.selection.limitExceeded",
                        ErrorCategory.Validation);
                }
                owned.Add(frozen);
            }

            byte[] boundary = owned.Count == 0 ? [] : BaseSelectionOrderTuple.Encode(owned[^1].MaterializeOwned(), request.Query.Sort!);
            _selectionRetainedBytes = checked(selectedBytes + boundary.LongLength);
            if (_selectionRetainedBytes > limits.MaximumTransientBytes)
                return SelectionFailure<BaseCapturedAtomicMutationAuthority>(OperationStatus.ValidationFailed,
                    "base.provider.selection.limitExceeded", ErrorCategory.Validation);
            var interval = new BaseAtomicReadIntervalEvidence
            {
                LogicalAccessPathId = $"collection:{request.Collection.Id}",
                CanonicalLowerBound = ImmutableArray<byte>.Empty,
                LowerInclusive = true,
                CanonicalUpperBound = boundary.ToImmutableArray(),
                UpperInclusive = true,
            };
            int selectedCount = owned.Count;
            ImmutableArray<BaseOwnedSelectedRecord> selectedRecords = owned.MoveToImmutable();
            ImmutableArray<byte> transactionEvidence = BitConverter.GetBytes(_working.GlobalMutationPosition).ToImmutableArray();
            string selectionCaptureDigest = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(
                selectedRecords.SelectMany(static record => record.CopyCanonicalBytes()).Concat(boundary).ToArray()));
            _capturedMutation = new BaseCapturedAtomicMutationAuthority
            {
                Kind = BaseAtomicMutationExecutionKind.SelectionMutation,
                IntentDigest = new string(intent.IntentDigest.AsSpan()),
                CaptureDigest = selectionCaptureDigest,
                Authority = new BaseAtomicMutationAuthorityEvidence
                {
                    ApplicationId = intent.Authority.ApplicationId,
                    StoreInstanceId = intent.Authority.StoreInstanceId,
                    RestoreEpoch = 0,
                    SchemaGeneration = 1,
                    Collections = [new BaseCollectionGenerationRequirement
                    {
                        CollectionId = request.Collection.Id,
                        CollectionGeneration = _owner._generation,
                    }],
                    Isolation = BaseAtomicSelectionIsolationClass.OptimisticRangeValidatedSerializable,
                    TransactionEvidenceToken = transactionEvidence,
                },
                Selection = new BaseCapturedSelectionEvidence
                {
                    Records = selectedRecords,
                    CanonicalOrderBoundary = boundary.ToImmutableArray(),
                    Accounting = new BaseAtomicSelectionAccounting
                    {
                        SelectedRecords = selectedCount, SelectedBytes = selectedBytes,
                        ReadIntervals = 1, EvidenceBytes = boundary.LongLength,
                    },
                },
                Items = selectedRecords.Select((record, index) => new BaseCapturedMutationItem
                {
                    Ordinal = index,
                    CollectionId = request.Collection.Id,
                    RecordId = new RecordId(record.RecordId),
                    Disposition = BaseCapturedMutationDisposition.Update,
                    Current = record.MaterializeOwned(),
                    RelationTargets = [],
                }).ToImmutableArray(),
                ModuleRecords = [], ModuleRelationTargets = [], Generations = [],
                ReadIntervals = [interval],
                Accounting = new BaseAtomicCaptureAccounting
                {
                    Records = selectedCount,
                    RelationTargetReads = 0, GenerationReads = 0,
                    SelectedBytes = selectedBytes,
                    RelationTargetBytes = 0, GenerationBytes = 0,
                    ReadIntervals = 1,
                    EvidenceBytes = boundary.LongLength,
                    TransientBytes = _selectionRetainedBytes,
                },
            };
            return OperationResults.Ok(_capturedMutation);
        }

        private static OperationResult<T> SelectionFailure<T>(
            OperationStatus status,
            string code,
            ErrorCategory category) => new()
        {
            Status = status,
            Error = new BaseError { Code = code, Message = "The provider selection failed.", Category = category },
        };

        /// <summary>Executes the get async operation.</summary>
        public ValueTask<OperationResult<RecordEnvelope>> GetAsync(
            CollectionDefinition collection,
            RecordId id,
            OperationContext context,
            CancellationToken cancellationToken = default) =>
            ExecuteAsync(
                cancellationToken,
                token => HPDBaseInMemoryTelemetry.TraceAsync(
                    HPDBaseTelemetrySpans.StoreGet,
                    BaseOperationKind.Get,
                    _owner._options.StoreId,
                    CollectionIdForTelemetry(collection),
                    () => GetFromStateAsync(
                        _working,
                        collection,
                        id,
                        context,
                        token)));

        /// <summary>Executes the create async operation.</summary>
        public ValueTask<OperationResult<RecordMutationSessionResult>> CreateAsync(
            CollectionDefinition collection,
            RecordCreateRequest request,
            RecordMutationSessionContext context,
            CancellationToken cancellationToken = default) =>
            ExecuteAsync(
                cancellationToken,
                async token =>
                {
                    ArgumentNullException.ThrowIfNull(context);
                    var result = await HPDBaseInMemoryTelemetry.TraceAsync(
                        HPDBaseTelemetrySpans.StoreCreate,
                        BaseOperationKind.Create,
                        _owner._options.StoreId,
                        CollectionIdForTelemetry(collection),
                        () => _owner.CreateCoreAsync(
                            _working,
                            collection,
                            request,
                            context.Operation,
                            token)).ConfigureAwait(false);
                    return ProjectMutation(
                        result,
                        collection,
                        context,
                        BaseCommittedRecordMutationKind.Create,
                        before: null,
                        after: result.Value,
                        delete: null,
                        changedFields: PayloadFieldNames(request.Payload));
                });

        /// <summary>Executes the patch async operation.</summary>
        public ValueTask<OperationResult<RecordMutationSessionResult>> PatchAsync(
            CollectionDefinition collection,
            RecordId id,
            RecordPatchRequest request,
            RecordMutationSessionContext context,
            CancellationToken cancellationToken = default) =>
            ExecuteAsync(
                cancellationToken,
                async token =>
                {
                    ArgumentNullException.ThrowIfNull(context);
                    var before = SnapshotRecord(collection, id);
                    var result = await HPDBaseInMemoryTelemetry.TraceAsync(
                        HPDBaseTelemetrySpans.StorePatch,
                        BaseOperationKind.Patch,
                        _owner._options.StoreId,
                        CollectionIdForTelemetry(collection),
                        () => _owner.PatchCoreAsync(
                            _working,
                            collection,
                            id,
                            request,
                            context.Operation,
                            token)).ConfigureAwait(false);
                    return ProjectMutation(
                        result,
                        collection,
                        context,
                        BaseCommittedRecordMutationKind.Patch,
                        before,
                        result.Value,
                        delete: null,
                        changedFields: PayloadFieldNames(request.Patch));
                });

        /// <summary>Executes the replace async operation.</summary>
        public ValueTask<OperationResult<RecordMutationSessionResult>> ReplaceAsync(
            CollectionDefinition collection,
            RecordId id,
            RecordReplaceRequest request,
            RecordMutationSessionContext context,
            CancellationToken cancellationToken = default) =>
            ExecuteAsync(
                cancellationToken,
                async token =>
                {
                    ArgumentNullException.ThrowIfNull(context);
                    var before = SnapshotRecord(collection, id);
                    var result = await HPDBaseInMemoryTelemetry.TraceAsync(
                        HPDBaseTelemetrySpans.StoreReplace,
                        BaseOperationKind.Replace,
                        _owner._options.StoreId,
                        CollectionIdForTelemetry(collection),
                        () => _owner.ReplaceCoreAsync(
                            _working,
                            collection,
                            id,
                            request,
                            context.Operation,
                            token)).ConfigureAwait(false);
                    return ProjectMutation(
                        result,
                        collection,
                        context,
                        BaseCommittedRecordMutationKind.Replace,
                        before,
                        result.Value,
                        delete: null,
                        changedFields: PayloadFieldNames(request.Payload));
                });

        /// <summary>Executes the delete async operation.</summary>
        public ValueTask<OperationResult<RecordMutationSessionResult>> DeleteAsync(
            CollectionDefinition collection,
            RecordId id,
            RecordDeleteRequest request,
            RecordMutationSessionContext context,
            CancellationToken cancellationToken = default) =>
            ExecuteAsync(
                cancellationToken,
                async token =>
                {
                    ArgumentNullException.ThrowIfNull(context);
                    var before = SnapshotRecord(collection, id);
                    var result = await HPDBaseInMemoryTelemetry.TraceAsync(
                        HPDBaseTelemetrySpans.StoreDelete,
                        BaseOperationKind.Delete,
                        _owner._options.StoreId,
                        CollectionIdForTelemetry(collection),
                        () => _owner.DeleteCoreAsync(
                            _working,
                            collection,
                            id,
                            request,
                            context.Operation,
                            token,
                            () => _relationChecks = checked(_relationChecks + 1))).ConfigureAwait(false);
                    return ProjectMutation(
                        result,
                        collection,
                        context,
                        BaseCommittedRecordMutationKind.Delete,
                        before,
                        after: null,
                        delete: result.Value,
                        changedFields: null);
                });

        public ValueTask<OperationResult<long>> AdvancePurgeGenerationAsync(
            CollectionDefinition collection,
            long? expectedGeneration,
            CancellationToken cancellationToken = default) =>
            ExecuteAsync(cancellationToken, _ =>
            {
                ArgumentNullException.ThrowIfNull(collection);
                if (collection.MutationMode != BaseCollectionMutationMode.AppendOnlyWithAdministrativePurge)
                    return ValueTask.FromResult(InMemoryResultFactory.Unsupported<long>(
                        BaseCollectionErrorCodes.PurgeUnsupported,
                        "The collection does not support administrative purge."));
                InMemoryCollectionState state = _owner.GetOrCreateCollection(_working, collection.Id);
                if (expectedGeneration is { } expected && expected != state.PurgeGeneration)
                    return ValueTask.FromResult(OperationResults.Conflict<long>(new BaseError
                    {
                        Code = BaseCollectionErrorCodes.PurgeGenerationConflict,
                        Message = "The purge generation did not match.",
                        Category = ErrorCategory.Conflict
                    }));
                if (state.PurgeGeneration == long.MaxValue)
                    return ValueTask.FromResult(InMemoryResultFactory.StoreError<long>(
                        BaseCollectionErrorCodes.PurgeFailed,
                        "The purge generation is exhausted."));
                return ValueTask.FromResult(OperationResults.Ok(++state.PurgeGeneration));
            });

        public ValueTask<OperationResult<BaseSubjectLifecycleCheckpointResult>> AdvanceSubjectLifecycleCheckpointAsync(
            BaseSubjectLifecycleProviderCheckpointRequest request,
            CancellationToken cancellationToken = default) =>
            ExecuteAsync(cancellationToken, _ =>
            {
                ArgumentNullException.ThrowIfNull(request);
                if (request.DeadlineUtc <= _owner._timeProvider.GetUtcNow()
                    || request.ExpectedCheckpointGeneration < 0
                    || request.ProjectionGeneration < 1)
                    return ValueTask.FromResult(OperationResults.ValidationFailed<BaseSubjectLifecycleCheckpointResult>(new BaseError
                    {
                        Code = BaseSubjectErrorCodes.ContractInvalid,
                        Message = "The subject lifecycle checkpoint operation is invalid.",
                        Category = ErrorCategory.Validation,
                    }));

                string consumerKey = $"{request.ConsumerId}\n{request.ConsumerVersion}";
                if (!_working.SubjectLifecycleConsumers.TryGetValue(consumerKey, out InMemorySubjectLifecycleConsumerProjection? projection)
                    || projection.ConsumerChecksum != request.ConsumerChecksum
                    || projection.ProjectionGeneration != request.ProjectionGeneration
                    || projection.ContractId != request.ContractId
                    || projection.ContractVersion != request.ContractVersion)
                    return ValueTask.FromResult(OperationResults.StoreError<BaseSubjectLifecycleCheckpointResult>(new BaseError
                    {
                        Code = BaseSubjectErrorCodes.ProviderContractInvalid,
                        Message = "The subject lifecycle checkpoint authority is invalid.",
                        Category = ErrorCategory.Store,
                    }));

                BaseProtectedSubjectScope protectedScope = _owner._subjectScopes.Protect(request.Scope, _owner._subjectScopeProtectionKey);
                string checkpointKey = ProtectedScopeKey(request.ConsumerId, request.ConsumerVersion, protectedScope);
                _working.SubjectLifecycleCheckpoints.TryGetValue(checkpointKey, out InMemorySubjectLifecycleCheckpointState? prior);
                long actualGeneration = prior?.Generation ?? 0;
                if (actualGeneration != request.ExpectedCheckpointGeneration || prior?.Overtaken == true
                    || prior?.Through is not null && request.Through is not null && CompareBoundary(request.Through, prior.Through) < 0)
                    return ValueTask.FromResult(OperationResults.Conflict<BaseSubjectLifecycleCheckpointResult>(new BaseError
                    {
                        Code = BaseSubjectErrorCodes.CursorOvertaken,
                        Message = "The subject lifecycle checkpoint is no longer current.",
                        Category = ErrorCategory.Conflict,
                    }));

                if (request.Through is not null && !_working.SubjectLifecycleMemberships.Any(membership =>
                    membership.ConsumerId == request.ConsumerId && membership.ConsumerVersion == request.ConsumerVersion
                    && membership.ConsumerChecksum == request.ConsumerChecksum && membership.ProjectionGeneration == request.ProjectionGeneration
                    && ProtectedScopeEquals(membership.Scope, protectedScope)
                    && ProtectedScopeEquals(_working.SubjectLifecycleFacts[membership.FactIndex].Scope, protectedScope)
                    && CompareBoundary(_working.SubjectLifecycleFacts[membership.FactIndex].Boundary, request.Through) == 0))
                    return ValueTask.FromResult(OperationResults.ValidationFailed<BaseSubjectLifecycleCheckpointResult>(new BaseError
                    {
                        Code = BaseSubjectErrorCodes.CursorInvalid,
                        Message = "The subject lifecycle checkpoint is invalid.",
                        Category = ErrorCategory.Validation,
                    }));

                long generation = checked(actualGeneration + 1);
                DateTimeOffset now = _owner._timeProvider.GetUtcNow();
                var result = new BaseSubjectLifecycleCheckpointResult
                {
                    Through = request.Through is null ? prior?.Through : request.Through with { },
                    CheckpointGeneration = generation,
                    ProjectionGeneration = request.ProjectionGeneration,
                    AdvancedAtUtc = now,
                    Duplicate = false,
                };
                _working.SubjectLifecycleCheckpoints[checkpointKey] = new(request.ConsumerId, request.ConsumerVersion,
                    request.ConsumerChecksum, request.ContractId, request.ContractVersion, request.ProjectionGeneration,
                    protectedScope, result.Through, generation, now, false);
                return ValueTask.FromResult(OperationResults.Ok(result));
            });

        /// <inheritdoc />
        public async ValueTask<OperationResult> ApplyMutationProjectionsAsync(
            BaseAtomicMutationProjectionRequest request,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            cancellationToken.ThrowIfCancellationRequested();
            IInMemoryAtomicMutationProjection? projection = _owner._vectorProjection;
            if (projection is null) return OperationResults.NoContent();
            try
            {
                return await projection.ApplyAsync(
                    new BaseInMemoryProjectionMutationContext(_working, _owner._options, request),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                return new OperationResult
                {
                    Status = OperationStatus.StoreError,
                    Error = new BaseError
                    {
                        Code = "base.runtime.projectionFailed",
                        Message = "A transactional projection failed.",
                        Category = ErrorCategory.Store,
                    },
                };
            }
        }

        /// <summary>Executes the close async operation.</summary>
        public async ValueTask CloseAsync()
        {
            if (Interlocked.CompareExchange(
                    ref _lifetimeState,
                    Closing,
                    Active) != Active)
            {
                return;
            }

            await _operationGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            Volatile.Write(ref _lifetimeState, Closed);
            _operationGate.Release();
        }

        private async ValueTask<OperationResult<T>> ExecuteAsync<T>(
            CancellationToken cancellationToken,
            Func<CancellationToken, ValueTask<OperationResult<T>>> action)
        {
            if (Volatile.Read(ref _lifetimeState) != Active)
                return SessionClosed<T>();

            try
            {
                await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return SessionOperationCancelled<T>();
            }

            try
            {
                if (Volatile.Read(ref _lifetimeState) != Active)
                    return SessionClosed<T>();

                return await action(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return SessionOperationCancelled<T>();
            }
            finally
            {
                _operationGate.Release();
            }
        }

        private RecordEnvelope? SnapshotRecord(CollectionDefinition collection, RecordId id)
        {
            var record = GetCollectionOrNull(_working, collection.Id)?
                .RecordsById.GetValueOrDefault(id.Value);
            return record is null ? null : RecordCloneHelpers.CloneEnvelope(record);
        }

        private static OperationResult<RecordMutationSessionResult> ProjectMutation<T>(
            OperationResult<T> result,
            CollectionDefinition collection,
            RecordMutationSessionContext context,
            BaseCommittedRecordMutationKind committedOperation,
            RecordEnvelope? before,
            RecordEnvelope? after,
            DeleteResult? delete,
            string[]? changedFields)
        {
            RecordMutationSessionResult? value = null;
            if (result.Value is not null)
            {
                RecordUpsertOutcome? upsertOutcome =
                    context.RequestedOperation == BaseRecordMutationKind.Upsert
                        ? committedOperation == BaseCommittedRecordMutationKind.Create
                            ? RecordUpsertOutcome.Created
                            : RecordUpsertOutcome.Updated
                        : null;
                var mutation = new BaseRecordMutationFact
                {
                    ItemId = context.ItemId,
                    RequestedOperation = context.RequestedOperation,
                    CommittedOperation = committedOperation,
                    UpsertOutcome = upsertOutcome,
                    Collection = collection,
                    Event = EventReference(
                        context.EventId,
                        committedOperation),
                    Before = before,
                    After = after,
                    Delete = delete,
                    ChangedFields = context.ChangedFields
                };
                value = new RecordMutationSessionResult
                {
                    Mutation = mutation,
                    Record = after,
                    Delete = delete
                };
            }

            return new OperationResult<RecordMutationSessionResult>
            {
                Status = result.Status,
                Value = value,
                Error = result.Error,
                Warnings = result.Warnings,
                Diagnostics = result.Diagnostics,
                Revision = result.Revision
            };
        }

        private static EventReference EventReference(
            string eventId,
            BaseCommittedRecordMutationKind operation) => new()
        {
            EventId = eventId,
            Type = operation switch
            {
                BaseCommittedRecordMutationKind.Create => BaseEventTypes.RecordCreated,
                BaseCommittedRecordMutationKind.Patch => BaseEventTypes.RecordPatched,
                BaseCommittedRecordMutationKind.Replace => BaseEventTypes.RecordUpdated,
                BaseCommittedRecordMutationKind.Delete => BaseEventTypes.RecordDeleted,
                _ => throw new InvalidOperationException("Unsupported committed mutation kind.")
            },
            Guarantee = EventDeliveryGuarantee.BestEffort
        };

        private static string[]? PayloadFieldNames(RecordPayload? payload)
        {
            if (payload is null)
                return null;
            if (payload.Kind == RecordPayloadKind.FieldMap)
                return payload.Fields?.Keys.Order(StringComparer.Ordinal).ToArray();
            if (payload.Json.ValueKind != JsonValueKind.Object)
                return null;
            return payload.Json
                .EnumerateObject()
                .Select(static property => property.Name)
                .Order(StringComparer.Ordinal)
                .ToArray();
        }

        private static OperationResult<T> SessionClosed<T>() =>
            InMemoryResultFactory.StoreError<T>(
                InMemoryErrorCodes.SessionClosed,
                "The InMemory mutation session is no longer active.");

        private static OperationResult<T> SessionOperationCancelled<T>() =>
            InMemoryResultFactory.StoreError<T>(
                InMemoryErrorCodes.SessionOperationCancelled,
                "The InMemory mutation session operation was cancelled.");
    }

    private readonly record struct PayloadNormalizeResult<T>(RecordPayload? Value, OperationResult<T>? Result)
    {
        /// <summary>Executes the success operation.</summary>
        public static PayloadNormalizeResult<T> Success(RecordPayload payload) => new(payload, null);
        /// <summary>Executes the failure operation.</summary>
        public static PayloadNormalizeResult<T> Failure(OperationResult<T> result) => new(null, result);
    }

    private readonly record struct PayloadFieldsResult<T>(Dictionary<string, System.Text.Json.JsonElement>? Value, OperationResult<T>? Result)
    {
        /// <summary>Executes the success operation.</summary>
        public static PayloadFieldsResult<T> Success(Dictionary<string, System.Text.Json.JsonElement> fields) => new(fields, null);
        /// <summary>Executes the failure operation.</summary>
        public static PayloadFieldsResult<T> Failure(OperationResult<T> result) => new(null, result);
    }

    private readonly record struct QueryResult<TValue, TResult>(TValue? Value, OperationResult<TResult>? Result)
        where TValue : class
    {
        /// <summary>Executes the success operation.</summary>
        public static QueryResult<TValue, TResult> Success(TValue value) => new(value, null);
        /// <summary>Executes the failure operation.</summary>
        public static QueryResult<TValue, TResult> Failure(OperationResult<TResult> result) => new(null, result);
    }

    private sealed class SelectNode
    {
        /// <summary>Gets or sets the value.</summary>
        public JsonElement? Value { get; set; }
        /// <summary>Gets the children.</summary>
        public Dictionary<string, SelectNode> Children { get; } = new(StringComparer.Ordinal);
    }
}

internal sealed class InMemoryVectorRootLease(InMemoryStoreState root, Action<InMemoryStoreState> release) : IDisposable
{
    private int _disposed;
    internal InMemoryStoreState Root { get; } = root;
    public void Dispose() { if (Interlocked.Exchange(ref _disposed, 1) == 0) release(Root); }
}
