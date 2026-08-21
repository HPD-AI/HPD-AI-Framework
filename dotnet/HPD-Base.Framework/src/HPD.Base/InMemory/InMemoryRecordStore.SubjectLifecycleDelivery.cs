using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;

namespace HPD.Base;

internal sealed partial class InMemoryRecordStore
{
    public ValueTask<RecordMutationExecutionResult> ExecuteAsync(IAtomicMutationProcessor processor, RecordMutationExecutionRequest request, CancellationToken cancellationToken = default) =>
        ExecuteAtomicAsync(processor, request, cancellationToken);

    public async ValueTask<OperationResult<BaseSubjectRetirementPublicationPage>> ReadPublicationsAsync(BaseSubjectRetirementPublicationReadRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);if(request.Take is<1 or>256)return RetirementReadFailure<BaseSubjectRetirementPublicationPage>(OperationStatus.ValidationFailed,BaseSubjectRetirementErrorCodes.ContractInvalid,ErrorCategory.Validation);
        await _stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);try{InMemoryStoreState state=Volatile.Read(ref _publishedState);long high=state.SubjectRetirementPosition;long after=request.After?.Value??0;ImmutableArray<BaseSubjectRetirementPublicationRow> rows=[..state.SubjectRetirementPublications.Where(value=>value.Fact.Position.Value>after&&value.Fact.Position.Value<=high).OrderBy(value=>value.Fact.Position.Value).Take(request.Take).Select(CloneRetirementPublication)];return OperationResults.Ok(new BaseSubjectRetirementPublicationPage{Rows=rows,HighWater=high==0?default:new(high)});}finally{_stateGate.Release();}
    }

    private static BaseSubjectRetirementPublicationRow CloneRetirementPublication(BaseSubjectRetirementPublicationRow row)=>new(){Scope=row.Scope is null?null:CloneRetirementScope(row.Scope),Fact=row.Fact with{Barrier=row.Fact.Barrier is null?null:row.Fact.Barrier with{},AdvisoryAcknowledgement=row.Fact.AdvisoryAcknowledgement is null?null:row.Fact.AdvisoryAcknowledgement with{},Purged=row.Fact.Purged is null?null:row.Fact.Purged with{},ConsumerSet=row.Fact.ConsumerSet is null?null:row.Fact.ConsumerSet with{},Restore=row.Fact.Restore is null?null:row.Fact.Restore with{}}};

    public async ValueTask<OperationResult<BaseSubjectRetirementBarrierPage>> ReadBarriersAsync(BaseSubjectRetirementBarrierReadRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.DeadlineUtc <= _timeProvider.GetUtcNow() || request.Take is < 1 or > 256 || request.MaximumResultBytes is < 1 or > 1_048_576)
            return RetirementReadFailure<BaseSubjectRetirementBarrierPage>(OperationStatus.ValidationFailed, BaseSubjectRetirementErrorCodes.ContractInvalid, ErrorCategory.Validation);
        await _stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            BaseExportedSubjectDefinition? installedContract=(_options.ExportedSubjects??[]).SingleOrDefault(value=>value.Id==request.ContractId&&value.Version==request.ContractVersion);
            bool exactAuthority=request.ScopeAuthority.Mode==BaseSubjectScopeQueryMode.ExactScope&&installedContract is not null&&string.Equals(request.ScopeAuthority.InstalledAuthorityDigest,BaseSubjectContractGraph.Checksum(installedContract),StringComparison.Ordinal);bool allAuthority=request.ScopeAuthority.Mode==BaseSubjectScopeQueryMode.AllAuthorizedScopes&&request.ScopeAuthority.ExactScope is null&&_options.SubjectLifecycleInspectionAuthorities.Any(value=>value.ContractId==request.ContractId&&value.ContractVersion==request.ContractVersion&&value.Digest==request.ScopeAuthority.InstalledAuthorityDigest);
            if(installedContract is null||!exactAuthority&&!allAuthority)
                return RetirementReadFailure<BaseSubjectRetirementBarrierPage>(OperationStatus.CapabilityUnavailable,BaseSubjectRetirementErrorCodes.ProviderContractInvalid,ErrorCategory.Capability);
            BaseProtectedSubjectScope? exact = request.ScopeAuthority.Mode == BaseSubjectScopeQueryMode.ExactScope && request.ScopeAuthority.ExactScope is { } scope
                ? _subjectScopes.Protect(scope, _subjectScopeProtectionKey) : null;
            if (request.ScopeAuthority.Mode == BaseSubjectScopeQueryMode.ExactScope && exact is null)
                return RetirementReadFailure<BaseSubjectRetirementBarrierPage>(OperationStatus.ValidationFailed, BaseSubjectRetirementErrorCodes.ContractInvalid, ErrorCategory.Validation);
            IEnumerable<InMemorySubjectRetirementBarrierState> source = Volatile.Read(ref _publishedState).SubjectRetirementBarriers.Values
                .Where(value => value.Barrier.ContractId == request.ContractId && value.Barrier.ContractVersion == request.ContractVersion)
                .Where(value => exact is null || ProtectedScopeEquals(value.Scope, exact))
                .Where(value => request.State is null || value.Barrier.State == request.State)
                .OrderBy(static value => (int)value.Scope.Kind).ThenBy(static value => Convert.ToHexString(value.Scope.IndexDigest), StringComparer.Ordinal)
                .ThenBy(static value => value.Barrier.SubjectId.Value, StringComparer.Ordinal)
                .ThenBy(static value => value.Barrier.AuthorityEpoch.ToBase64Url(), StringComparer.Ordinal)
                .ThenBy(static value => value.Barrier.Incarnation.ToBase64Url(), StringComparer.Ordinal);
            if (request.After is { } after) source = source.Where(value => CompareRetirementKey(Key(value), after) > 0);
            InMemorySubjectRetirementBarrierState[] rows = source.Take(request.Take + 1).ToArray();
            bool more = rows.Length > request.Take; if (more) rows = rows[..request.Take];
            long resultBytes = rows.Sum(static row => RetirementBarrierBytes(row.Barrier));
            if (resultBytes > request.MaximumResultBytes)
                return RetirementReadFailure<BaseSubjectRetirementBarrierPage>(OperationStatus.ValidationFailed, BaseSubjectErrorCodes.BudgetExceeded, ErrorCategory.Validation);
            ImmutableArray<BaseSubjectRetirementBarrierRow> barriers = [.. rows.Select(static row => new BaseSubjectRetirementBarrierRow { Scope = CloneRetirementScope(row.Scope), Barrier = row.Barrier with { }, AcknowledgementChecksumInputs=[..row.Acknowledgements.Values.Select(static value=>BaseSubjectRetirementRegistry.AcknowledgementChecksumInput(value.ConsumerId,value.ConsumerVersion,value.ConsumerChecksum,value.ThroughSequence,value.Disposition,value.Position)).Order(StringComparer.Ordinal)] })];
            BaseSubjectRetirementBarrierKey? next = more && rows.Length != 0 ? Key(rows[^1]) : null;
            ImmutableArray<BaseReadIntervalEvidence> intervals = BaseSubjectRetirementReadIntervals.Create(request.ContractId, request.ContractVersion, request.State, exact, request.After, rows.Length == 0 ? request.After : Key(rows[^1]));
            BaseReadIntervalEvidence interval = intervals[0]; byte[] lower = interval.LowerInclusive; byte[] upper = interval.UpperInclusive;
            int acknowledgementRows=barriers.Sum(static row=>row.AcknowledgementChecksumInputs.Length);long acknowledgementBytes=barriers.Sum(static row=>row.AcknowledgementChecksumInputs.Sum(static value=>(long)Encoding.UTF8.GetByteCount(value)));long evidenceBytes = checked(lower.LongLength + upper.LongLength+acknowledgementBytes);
            return OperationResults.Ok(new BaseSubjectRetirementBarrierPage
            {
                Barriers = barriers, Next = next, CapturedBarrierGeneration = Volatile.Read(ref _publishedState).SubjectRetirementPosition,
                Intervals = intervals, Accounting = new BaseSubjectRetirementReadAccounting { BarrierRows = rows.Length, AcknowledgementRows = acknowledgementRows, ResultBytes = resultBytes, EvidenceBytes = evidenceBytes, TransientBytes = checked(resultBytes + evidenceBytes) },
            });
        }
        finally { _stateGate.Release(); }
    }

    async ValueTask<OperationResult<BaseSubjectRetirementInspection>> IBaseSubjectRetirementStore.InspectAsync(BaseSubjectRetirementInspectionRequest request, CancellationToken cancellationToken)
    {
        _options.SubjectRetirementInspectionStarted?.Invoke();
        ArgumentNullException.ThrowIfNull(request);
        if (request.DeadlineUtc <= _timeProvider.GetUtcNow() || request.MaximumResultBytes is < 1 or > 1_048_576 || request.ScopeAuthority.Mode != BaseSubjectScopeQueryMode.ExactScope || request.ScopeAuthority.ExactScope is null)
            return RetirementReadFailure<BaseSubjectRetirementInspection>(OperationStatus.ValidationFailed, BaseSubjectRetirementErrorCodes.ContractInvalid, ErrorCategory.Validation);
        await _stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            BaseExportedSubjectDefinition? installedContract=(_options.ExportedSubjects??[]).SingleOrDefault(value=>value.Id==request.ContractId&&value.Version==request.ContractVersion);
            if(installedContract is null||!string.Equals(request.ScopeAuthority.InstalledAuthorityDigest,BaseSubjectContractGraph.Checksum(installedContract),StringComparison.Ordinal))
                return RetirementReadFailure<BaseSubjectRetirementInspection>(OperationStatus.CapabilityUnavailable,BaseSubjectRetirementErrorCodes.ProviderContractInvalid,ErrorCategory.Capability);
            BaseProtectedSubjectScope scope = _subjectScopes.Protect(request.ScopeAuthority.ExactScope, _subjectScopeProtectionKey);
            string key = RetirementKey(scope, request.ContractId, request.ContractVersion, request.SubjectId, request.AuthorityEpoch, request.Incarnation);
            InMemoryStoreState state = Volatile.Read(ref _publishedState);
            state.SubjectRetirementBarriers.TryGetValue(key, out InMemorySubjectRetirementBarrierState? current);
            state.SubjectRetirementTerminals.TryGetValue(key, out InMemorySubjectRetirementTerminalState? terminal);
            BaseSubjectRetirementTerminalSummary? summary = request.IncludeTerminalSummary && terminal is not null ? new BaseSubjectRetirementTerminalSummary
            {
                ContractId = terminal.Receipt.ContractId, ContractVersion = terminal.Receipt.ContractVersion, SubjectId = terminal.Receipt.SubjectId,
                AuthorityEpoch = terminal.Receipt.AuthorityEpoch, Incarnation = terminal.Receipt.Incarnation, TombstoneSequence = terminal.Receipt.TombstoneSequence,
                RetiredPosition = terminal.Receipt.RetiredPosition, PurgedAtUtc = terminal.Receipt.PurgedAtUtc,
                TerminalReceiptChecksum = terminal.Receipt.ReceiptChecksum,
            } : null;
            if (current is not null && summary is not null)
                return RetirementReadFailure<BaseSubjectRetirementInspection>(OperationStatus.StoreError, BaseSubjectRetirementErrorCodes.ProviderContractInvalid, ErrorCategory.Store);
            long resultBytes = current is null ? summary is null ? 0 : 256 : RetirementBarrierBytes(current.Barrier);ImmutableArray<string> acknowledgementInputs=current is null?[]:[..current.Acknowledgements.Values.Select(static value=>BaseSubjectRetirementRegistry.AcknowledgementChecksumInput(value.ConsumerId,value.ConsumerVersion,value.ConsumerChecksum,value.ThroughSequence,value.Disposition,value.Position)).Order(StringComparer.Ordinal)];long evidenceBytes=acknowledgementInputs.Sum(static value=>(long)Encoding.UTF8.GetByteCount(value));
            return OperationResults.Ok(new BaseSubjectRetirementInspection
            {
                Scope = CloneRetirementScope(scope), CurrentBarrier = current is null ? null : current.Barrier with { }, TerminalSummary = summary,
                AcknowledgementChecksumInputs=acknowledgementInputs,
                Accounting = new BaseSubjectRetirementReadAccounting { BarrierRows = current is null ? 0 : 1, AcknowledgementRows = acknowledgementInputs.Length, ResultBytes = resultBytes, EvidenceBytes = evidenceBytes, TransientBytes = checked(resultBytes+evidenceBytes) },
            });
        }
        finally { _stateGate.Release(); }
    }

    private static OperationResult<T> RetirementReadFailure<T>(OperationStatus status, string code, ErrorCategory category) => new()
    { Status = status, Error = new BaseError { Code = code, Message = "The subject retirement barrier is unavailable.", Category = category } };

    private static BaseSubjectRetirementBarrierKey Key(InMemorySubjectRetirementBarrierState value) => new()
    { ScopeKind = value.Scope.Kind, ScopeIndexDigest = value.Scope.IndexDigest.ToArray(), ContractId = value.Barrier.ContractId, ContractVersion = value.Barrier.ContractVersion, SubjectId = value.Barrier.SubjectId, AuthorityEpoch = value.Barrier.AuthorityEpoch, Incarnation = value.Barrier.Incarnation };
    private static int CompareRetirementKey(BaseSubjectRetirementBarrierKey left, BaseSubjectRetirementBarrierKey right) =>
        RetirementKeyBytes(left).AsSpan().SequenceCompareTo(RetirementKeyBytes(right));
    private static byte[] RetirementKeyBytes(BaseSubjectRetirementBarrierKey key) => Encoding.UTF8.GetBytes($"{(int)key.ScopeKind:D2}\0{Convert.ToHexString(key.ScopeIndexDigest)}\0{key.ContractId}\0{key.ContractVersion:D10}\0{key.SubjectId.Value}\0{key.AuthorityEpoch.ToBase64Url()}\0{key.Incarnation.ToBase64Url()}");
    private static long RetirementBarrierBytes(BaseSubjectRetirementBarrier barrier) => Encoding.UTF8.GetByteCount($"{barrier.ContractId}\0{barrier.ContractVersion}\0{barrier.SubjectId.Value}\0{barrier.AuthorityEpoch.ToBase64Url()}\0{barrier.Incarnation.ToBase64Url()}\0{barrier.TombstoneSequence}\0{barrier.RequiredConsumerSetChecksum}\0{barrier.CreatedAtUtc.UtcTicks}\0{barrier.DeadlineUtc.UtcTicks}\0{(int)barrier.State}\0{barrier.Generation}\0{barrier.BarrierChecksum}");
    private static BaseProtectedSubjectScope CloneRetirementScope(BaseProtectedSubjectScope scope) => new() { Kind = scope.Kind, IndexDigest = scope.IndexDigest.ToArray(), ProtectedCanonicalValue = scope.ProtectedCanonicalValue.ToArray() };

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

    private static string RetirementKey(BaseProtectedSubjectScope scope, string contractId, int contractVersion, BaseSubjectId subjectId, BaseSubjectAuthorityEpoch epoch, BaseSubjectIncarnation incarnation) =>
        $"{(int)scope.Kind}\n{Convert.ToHexString(scope.IndexDigest)}\n{contractId}\n{contractVersion}\n{subjectId.Value}\n{Convert.ToHexString(epoch.ToArray())}\n{Convert.ToHexString(incarnation.ToArray())}";
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
            ScopeProtectionGeneration = _subjectScopeProtectionGeneration,
            ScopeProtectionKeyId = _subjectScopeProtectionKeyId,
            RetirementControlGeneration = state.SubjectRetirementPosition,
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
        IBaseSubjectAuthorityMaintenanceProcessor processor,
        BaseSubjectAuthorityMaintenanceExecutionRequest request,
        CancellationToken cancellationToken = default) => processor.ExecuteAsync(new InMemorySubjectAuthorityMaintenanceSession(this, request), request, cancellationToken);
    internal async ValueTask<OperationResult<BaseTextRebuildResult>> RebuildTextAsync(CollectionDefinition collection, BaseTextIndexDefinition index, BaseTextRebuildRequest request, CancellationToken cancellationToken)
    {
        string receiptKey = request.Identity.Scope + "\n" + request.Identity.Operation + "\n" + request.Identity.IdempotencyKey;
        byte[] fingerprint = TextRebuildFingerprint(request);
        for (int attempt = 0; attempt < 3; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            InMemoryStoreState captured = CaptureVectorRoot();
            if (captured.TextRebuildReceipts.TryGetValue(receiptKey, out InMemoryTextRebuildReceipt? existing))
                return CryptographicOperations.FixedTimeEquals(existing.Fingerprint, fingerprint)
                    ? OperationResults.Ok(existing.Result with { PublicationChecksum = ImmutableArray.Create(existing.Result.PublicationChecksum.ToArray()) })
                    : OperationResults.Conflict<BaseTextRebuildResult>(new BaseError { Code = BaseMutationRequestErrorCodes.FingerprintConflict, Message = "The text rebuild identity conflicts with stored evidence.", Category = ErrorCategory.Conflict });
            string slot = collection.Id + "\n" + index.Id;
            InMemoryTextProjectionState previous = captured.TextProjections.GetValueOrDefault(slot) ?? new InMemoryTextProjectionState { AppliedThrough = captured.GlobalMutationPosition, PurgeGeneration = captured.Collections.GetValueOrDefault(collection.Id)?.PurgeGeneration ?? 0 };
            if (previous.Generation != request.ExpectedGeneration) return OperationResults.Conflict<BaseTextRebuildResult>(new BaseError { Code = BaseTextErrorCodes.RebuildRequired, Message = "The text rebuild conflicts with current index state.", Category = ErrorCategory.Conflict });
            InMemoryStoreState working = captured.Clone(); var staged = new InMemoryTextProjectionState { Generation = checked(previous.Generation + 1), AppliedThrough = captured.GlobalMutationPosition, PurgeGeneration = previous.PurgeGeneration };
            IEnumerable<StoredRecord> records = working.Collections.GetValueOrDefault(collection.Id)?.RecordsById.Values ?? Enumerable.Empty<StoredRecord>();
            foreach (StoredRecord record in records.OrderBy(static value => value.Id.Value, StringComparer.Ordinal)) { cancellationToken.ThrowIfCancellationRequested(); BaseTextSemanticEvaluator.ValidateIndexedPayload(record.Payload, index); staged.Carriers.Add(record.Id.Value, new InMemoryTextCarrier(record.Id, record.Metadata.Revision!.Value, record.LatestMutationPosition)); }
            working.TextProjections[slot] = staged;
            byte[] checksum = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(string.Join('\n', staged.Carriers.Keys.Order(StringComparer.Ordinal))));
            var result = new BaseTextRebuildResult { PreviousGeneration = previous.Generation, PublishedGeneration = staged.Generation, VisibleThrough = new(staged.AppliedThrough), RecordCount = staged.Carriers.Count, PublicationChecksum = ImmutableArray.Create(checksum) };
            working.TextRebuildReceipts[receiptKey] = new(fingerprint, result);
            await _stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (!ReferenceEquals(_publishedState, captured)) continue;
                Volatile.Write(ref _publishedState, working); _generation++; return OperationResults.Ok(result);
            }
            finally { _stateGate.Release(); }
        }
        return OperationResults.Conflict<BaseTextRebuildResult>(new BaseError { Code = BaseTextErrorCodes.InMemoryGenerationChanged, Message = "The text authority changed during rebuild.", Category = ErrorCategory.Conflict });
    }
    private static byte[] TextRebuildFingerprint(BaseTextRebuildRequest request) { using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8); writer.Write("base.text.rebuild.v1"); writer.Write(request.Identity.Scope); writer.Write(request.Identity.Operation); writer.Write(request.Identity.IdempotencyKey); writer.Write(request.CollectionId); writer.Write(request.TextIndexId); writer.Write(request.ExpectedGeneration); writer.Write(request.Identity.Fingerprint.ToArray()); return SHA256.HashData(stream.ToArray()); }
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
}
