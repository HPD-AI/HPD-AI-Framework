using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace HPD.Base;

internal sealed partial class InMemoryRecordStore
{
    private const long MaximumYieldReceiptSlots = 1_000_000_000_000;
    private static readonly BaseActivationAccounting EmptyActivationAccounting = new()
    {
        Candidates = 0,
        Comparisons = 0,
        IndexOperations = 1,
        ReadIntervals = 1,
        EvidenceBytes = 0,
        TransientBytes = 0,
    };

    private bool ActivationRowCapacityAllows(InMemoryStoreState state)
    {
        int pending = 0, claimed = 0, terminal = 0;
        foreach (InMemoryActivationRow row in state.Activations.Values)
        {
            switch (row.State)
            {
                case BaseActivationState.Pending:
                case BaseActivationState.RetryPending:
                case BaseActivationState.YieldPending:
                    pending = checked(pending + 1);
                    break;
                case BaseActivationState.Claimed:
                case BaseActivationState.EffectStarted:
                    claimed = checked(claimed + 1);
                    break;
                default:
                    terminal = checked(terminal + 1);
                    break;
            }
        }
        return pending <= Descriptor.Capability.MaximumPendingRows
            && claimed <= Descriptor.Capability.MaximumClaimedRows
            && terminal <= Descriptor.Capability.MaximumTerminalRows;
    }

    private static bool TryReserveYieldReceiptSlots(InMemoryStoreState state, long maximumYields)
    {
        if (maximumYields == 0) return true;
        long slots = checked(maximumYields + 1);
        long reserved = checked(state.ActivationYieldReservedUnusedSlots + slots);
        if (checked(reserved + state.ActivationYieldRetainedUsedSlots) > MaximumYieldReceiptSlots) return false;
        state.ActivationYieldReservedUnusedSlots = reserved;
        state.ActivationYieldReservationGeneration = checked(state.ActivationYieldReservationGeneration + 1);
        return true;
    }

    private static void ApplyYieldReceiptReservationTransition(
        InMemoryStoreState state,
        InMemoryActivationRow row,
        BaseActivationState resultingState,
        BaseActivationYieldDisposition? yieldDisposition)
    {
        if (row.MaximumYields == 0) return;
        if (yieldDisposition is BaseActivationYieldDisposition.Yielded or BaseActivationYieldDisposition.LimitExceeded)
        {
            state.ActivationYieldReservedUnusedSlots = checked(state.ActivationYieldReservedUnusedSlots - 1);
            state.ActivationYieldRetainedUsedSlots = checked(state.ActivationYieldRetainedUsedSlots + 1);
            state.ActivationYieldReservationGeneration = checked(state.ActivationYieldReservationGeneration + 1);
            return;
        }
        if (resultingState is not (BaseActivationState.Succeeded or BaseActivationState.Exhausted or
            BaseActivationState.Cancelled or BaseActivationState.Disposed or BaseActivationState.Migrated)) return;
        if (row.State is BaseActivationState.Succeeded or BaseActivationState.Exhausted or
            BaseActivationState.Cancelled or BaseActivationState.Disposed or BaseActivationState.Migrated) return;
        long remaining = checked(row.MaximumYields + 1 - row.YieldCount);
        if (remaining == 0) return;
        state.ActivationYieldReservedUnusedSlots = checked(state.ActivationYieldReservedUnusedSlots - remaining);
        state.ActivationYieldReservationGeneration = checked(state.ActivationYieldReservationGeneration + 1);
    }

    /// <inheritdoc />
    public BaseActivationProviderDescriptor Descriptor { get; private set; } = null!;

    /// <inheritdoc />
    public async ValueTask<OperationResult<BaseActivationYieldReservationState>> ReadYieldReservationStateAsync(
        CancellationToken cancellationToken = default)
    {
        await _stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            InMemoryStoreState state = Volatile.Read(ref _publishedState);
            return OperationResults.Ok(BaseActivationYieldReservationContract.Create(
                state.ActivationYieldReservationGeneration,
                MaximumYieldReceiptSlots,
                state.ActivationYieldReservedUnusedSlots,
                state.ActivationYieldRetainedUsedSlots));
        }
        finally
        {
            _stateGate.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask<OperationResult<BaseActivationReceiptCompactionAuthority>> CaptureReceiptCompactionAuthorityAsync(
        BaseActivationReceiptCompactionAuthorityRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.ApplicationId != _options.SemanticActivationApplicationId
            || request.Definition.Version < 1 || request.Definition.Checksum.Length != 32
            || request.Scope.ProtectedIndexDigest.Length != 32
            || request.ReceiptRetention.ProtectedBackupCoverage != BaseActivationProtectedBackupCoverage.NotRequired
            || !ValidateLimits(request.Limits))
            return ActivationFailure<BaseActivationReceiptCompactionAuthority>(
                "base.activation.capabilityUnavailable", OperationStatus.CapabilityUnavailable, ErrorCategory.Capability);
        await _stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            InMemoryStoreState state = Volatile.Read(ref _publishedState);
            return OperationResults.Ok(new BaseActivationReceiptCompactionAuthority
            {
                Reservation = BaseActivationYieldReservationContract.Create(
                    state.ActivationYieldReservationGeneration, MaximumYieldReceiptSlots,
                    state.ActivationYieldReservedUnusedSlots, state.ActivationYieldRetainedUsedSlots),
                BackupFloor = new BaseActivationReceiptBackupFloor
                {
                    Kind = BaseActivationReceiptBackupFloorKind.NotApplicable,
                },
            });
        }
        finally { _stateGate.Release(); }
    }

    private static BaseActivationProviderDescriptor CreateActivationDescriptor(HPDBaseInMemoryStoreOptions options) =>
        BaseActivationCertificationReceiptContract.FromSuccessfulReport(
            "hpd.base.inMemory.activations", "1", CreateActivationCapability(options),
            ImmutableArray.CreateRange(Convert.FromHexString("d4319506e983adbbbe294e19340b198042a065be4675c28a46b963c98813e5ca")));

    internal static BaseActivationProviderCapability CreateActivationCapability(HPDBaseInMemoryStoreOptions options) => new()
        {
            AtomicCreationSupported = true,
            SelectionTargetSupported = true,
            ModuleTargetSupported = true,
            GuardedChildrenSupported = true,
            DurableYieldSupported = true,
            RestoreFencingSupported = true,
            DueInvalidation = BaseDueInvalidationClass.Native,
            ScheduleKinds = [BaseScheduleKind.Once, BaseScheduleKind.Interval, BaseScheduleKind.Cron, BaseScheduleKind.Calendar],
            ExecutionClasses = [BaseActivationExecutionClass.TransactionalOperation, BaseActivationExecutionClass.AtLeastOnceWorker, BaseActivationExecutionClass.AtMostOnceEffect],
            MaximumActivationsPerTransaction = 256,
            MaximumDueCandidates = options.ActivationMaximumDueCandidates,
            MaximumReadIntervals = options.ActivationMaximumReadIntervals,
            MaximumIndexOperations = 4096,
            MaximumInputBytes = 4L * 1024 * 1024,
            MaximumResultBytes = 4L * 1024 * 1024,
            MaximumEvidenceBytes = 16L * 1024 * 1024,
            MaximumTransientBytes = 16L * 1024 * 1024,
            MaximumReceiptBytes = 16L * 1024 * 1024,
            MaximumPendingRows = options.MaxPendingActivationRows,
            MaximumClaimedRows = options.MaxClaimedActivationRows,
            MaximumTerminalRows = options.MaxTerminalActivationRows,
            MaximumAttempts = 1024,
            MaximumYieldsPerActivation = 1_000_000,
            MaximumReservedYieldReceiptSlots = 1_000_000_000_000,
            MaximumRenewalsPerSlice = 4096,
            MaximumChildrenPerSlice = 4096,
            MaximumLineageDepth = 256,
            MaximumOccurrencePage = 256,
            MaximumPriorityAgingBoost = 32,
            PriorityAgingInterval = TimeSpan.FromMinutes(1),
            ObservationTokenLifetime = TimeSpan.FromMinutes(5),
            MaximumTimeZoneBytes = 64L * 1024 * 1024,
            MaximumHandlerDependencies = 4096,
            AcquisitionDeadline = TimeSpan.FromSeconds(5),
            TransactionDeadline = TimeSpan.FromSeconds(30),
            ObservationWaitDeadline = TimeSpan.FromMinutes(5),
            RenewalDeadline = TimeSpan.FromSeconds(5),
            CommitObservationDeadline = TimeSpan.FromSeconds(30),
            ReceiptResolutionDeadline = TimeSpan.FromSeconds(30),
            MaintenanceDeadline = TimeSpan.FromMinutes(5),
            ShutdownDrainDeadline = TimeSpan.FromSeconds(60),
            ProviderQuarantineSlots = 32,
            HandlerQuarantineSlots = 32,
            BackupModes = [],
            RestoreModes = [],
            CanonicalChecksum = ImmutableArray.CreateRange(SHA256.HashData("hpd.base.inMemory.activations.v2"u8)),
        };

    /// <inheritdoc />
    public async ValueTask<OperationResult<BaseActivationDependencyResult>> ReadDependenciesAsync(
        BaseActivationDependencyRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.ApplicationId) || request.MaximumDefinitions is < 1 or > 4096
            || request.DeadlineUtc.ToUnixTimeMilliseconds() < 0)
            return ActivationFailure<BaseActivationDependencyResult>(
                "base.activation.invalid", OperationStatus.ValidationFailed, ErrorCategory.Validation);
        await _stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            InMemoryStoreState state = Volatile.Read(ref _publishedState);
            var values = new Dictionary<string, (BaseActivationDefinitionKey Definition, bool Activation, bool Schedule)>(StringComparer.Ordinal);
            foreach (InMemoryActivationRow row in state.Activations.Values)
                Merge(row.Payload.Definition, activation: true, schedule: false);
            foreach (BaseScheduleAuthority schedule in state.Schedules.Values)
                Merge(schedule.Definition.Activation, activation: false, schedule: true);
            if (values.Count > request.MaximumDefinitions)
                return ActivationFailure<BaseActivationDependencyResult>(
                    "base.activation.capacityUnavailable", OperationStatus.CapabilityUnavailable, ErrorCategory.Capability);
            BaseActivationDefinitionDependency[] dependencies = values.Values
                .OrderBy(static item => item.Definition.Id, StringComparer.Ordinal)
                .ThenBy(static item => item.Definition.Version)
                .ThenBy(static item => Convert.ToHexString(item.Definition.Checksum.AsSpan()), StringComparer.Ordinal)
                .Select(static item => new BaseActivationDefinitionDependency
                {
                    Definition = item.Definition with { Checksum = item.Definition.Checksum.ToArray().ToImmutableArray() },
                    ReferencedByActivation = item.Activation,
                    ReferencedBySchedule = item.Schedule,
                }).ToArray();
            long evidenceBytes = dependencies.Sum(static item =>
                Encoding.UTF8.GetByteCount(item.Definition.Id) + item.Definition.Checksum.Length + 18L);
            return OperationResults.Ok(new BaseActivationDependencyResult
            {
                Dependencies = dependencies.ToImmutableArray(), CapturedGeneration = state.ActivationIndexGeneration,
                Accounting = EmptyActivationAccounting with
                {
                    Candidates = dependencies.Length, Comparisons = dependencies.Length,
                    IndexOperations = 2, ReadIntervals = 2, EvidenceBytes = evidenceBytes,
                    TransientBytes = evidenceBytes,
                },
            });

            void Merge(BaseActivationDefinitionKey definition, bool activation, bool schedule)
            {
                string key = $"{definition.Id}\n{definition.Version}\n{Convert.ToHexString(definition.Checksum.AsSpan())}";
                if (values.TryGetValue(key, out var current))
                    values[key] = (current.Definition, current.Activation || activation, current.Schedule || schedule);
                else
                    values.Add(key, (definition with { Checksum = definition.Checksum.ToArray().ToImmutableArray() }, activation, schedule));
            }
        }
        finally { _stateGate.Release(); }
    }

    /// <inheritdoc />
    public async ValueTask<OperationResult<BaseActivationMigrationCandidate>> ReadMigrationCandidateAsync(
        BaseActivationMigrationCandidateRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.ExpectedGeneration < 1 || request.SourceDefinition.Checksum.Length != 32
            || !ValidateLimits(request.Limits) || !AcceptActivationTime(request.AcceptedTime))
            return ActivationFailure<BaseActivationMigrationCandidate>("base.activation.invalid", OperationStatus.ValidationFailed, ErrorCategory.Validation);
        await _stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            InMemoryStoreState state = Volatile.Read(ref _publishedState);
            if (!state.Activations.TryGetValue(request.ActivationId, out InMemoryActivationRow? row)
                || row.Generation != request.ExpectedGeneration || !DefinitionMatches(row.Payload.Definition, request.SourceDefinition)
                || !ScopeMatches(row.Payload.Scope, request.Scope) || !MigrationSourceState(row.State) || row.MaximumYields > 0)
                return ActivationFailure<BaseActivationMigrationCandidate>("base.activation.migrationConflict", OperationStatus.Conflict, ErrorCategory.Conflict);
            long bytes = row.Payload.CanonicalInput.Length + row.Payload.InputChecksum.Length + row.ControlChecksum.Length;
            if (bytes > request.Limits.MaximumEvidenceBytes || bytes > request.Limits.MaximumTransientBytes)
                return ActivationFailure<BaseActivationMigrationCandidate>("base.activation.budgetExceeded", OperationStatus.ValidationFailed, ErrorCategory.Validation);
            return OperationResults.Ok(new BaseActivationMigrationCandidate
            {
                ActivationId = new string(row.Payload.ActivationId.AsSpan()),
                SourceDefinition = row.Payload.Definition with { Checksum = row.Payload.Definition.Checksum.ToArray().ToImmutableArray() },
                Generation = row.Generation, State = row.State,
                EffectiveDueAt = row.EffectiveDueAt, YieldCount = row.YieldCount,
                MaximumYields = row.MaximumYields, ExecutionSliceOrdinal = row.ExecutionSliceOrdinal,
                AttemptStartedAt = row.AttemptStartedAt, SliceStartedAt = row.SliceStartedAt,
                TerminalYieldDisposition = row.YieldTerminalDisposition,
                TerminalYieldFailureCode = row.YieldTerminalFailureCode,
                CanonicalInput = row.Payload.CanonicalInput.ToArray().ToImmutableArray(),
                InputChecksum = row.Payload.InputChecksum.ToArray().ToImmutableArray(),
                ControlChecksum = row.ControlChecksum.ToImmutableArray(),
                Accounting = EmptyActivationAccounting with { Candidates = 1, Comparisons = 4, EvidenceBytes = bytes, TransientBytes = bytes },
            });
        }
        finally { _stateGate.Release(); }
    }

    /// <inheritdoc />
    public async ValueTask<OperationResult<BaseActivationMigrationResult>> MigrateAsync(
        BaseActivationMigrationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.ExpectedSourceGeneration < 1 || request.ExpectedSourceInputChecksum.Length != 32
            || request.MigrationVersion < 1 || request.MigrationChecksum.Length != 32
            || request.Replacement.InputChecksum.Length != 32 || !ValidateLimits(request.Limits)
            || !AcceptActivationTime(request.AcceptedTime))
            return ActivationFailure<BaseActivationMigrationResult>("base.activation.invalid", OperationStatus.ValidationFailed, ErrorCategory.Validation);
        await _stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            InMemoryStoreState current = Volatile.Read(ref _publishedState);
            if (TryReadControlReceipt(current, request.Identity, "activation-migrated",
                HPDBaseJsonSerializerContext.Default.BaseActivationMigrationResult,
                static value => value with { Disposition = BaseMutationRequestDisposition.Duplicate }, out OperationResult<BaseActivationMigrationResult> replay))
                return replay;
            if (!current.Activations.TryGetValue(request.SourceActivationId, out InMemoryActivationRow? source)
                || source.Generation != request.ExpectedSourceGeneration || !MigrationSourceState(source.State)
                || source.MaximumYields > 0
                || !DefinitionMatches(source.Payload.Definition, request.SourceDefinition)
                || !ScopeMatches(source.Payload.Scope, request.Scope)
                || !CryptographicOperations.FixedTimeEquals(source.Payload.InputChecksum.AsSpan(), request.ExpectedSourceInputChecksum.AsSpan())
                || current.Activations.ContainsKey(request.ReplacementActivationId)
                || !ScopeMatches(request.Replacement.Scope, request.Scope)
                || !CryptographicOperations.FixedTimeEquals(SHA256.HashData(request.Replacement.CanonicalInput.AsSpan()), request.Replacement.InputChecksum.AsSpan()))
                return ActivationFailure<BaseActivationMigrationResult>("base.activation.migrationConflict", OperationStatus.Conflict, ErrorCategory.Conflict);

            long sourceGeneration = checked(source.Generation + 1);
            byte[] sourceChecksum = ControlChecksum(source.Payload.ActivationId, sourceGeneration,
                BaseActivationState.Migrated, source.EffectiveDueAt, source.YieldCount, source.MaximumYields,
                source.ExecutionSliceOrdinal, source.AttemptStartedAt, source.SliceStartedAt, null, null);
            byte[] replacementFingerprint = SHA256.HashData(Encoding.UTF8.GetBytes(
                $"base.activation.migration.create.v1\0{request.MigrationId}\n{request.MigrationVersion}\n{Convert.ToHexString(request.MigrationChecksum.AsSpan())}\n{request.SourceActivationId}\n{request.ReplacementActivationId}\n{Convert.ToHexString(request.Replacement.InputChecksum.AsSpan())}"));
            var replacementPayload = new BaseActivationPayload
            {
                ActivationId = request.ReplacementActivationId,
                Definition = request.Replacement.Definition with { Checksum = request.Replacement.Definition.Checksum.ToArray().ToImmutableArray() },
                ReceiptRetention = request.Replacement.ReceiptRetention with { },
                CanonicalInput = request.Replacement.CanonicalInput.ToArray().ToImmutableArray(),
                InputChecksum = request.Replacement.InputChecksum.ToArray().ToImmutableArray(),
                Scope = request.Replacement.Scope with { }, OccurrenceId = request.Replacement.OccurrenceId,
                RequestedDueAt = request.Replacement.RequestedDueAt,
                EffectiveDueAt = request.Replacement.EffectiveDueAt ?? request.Replacement.RequestedDueAt,
                Checksum = SHA256.HashData(request.Replacement.CanonicalInput.AsSpan()).ToImmutableArray(),
            };
            byte[] replacementChecksum = ControlChecksum(replacementPayload.ActivationId, 1,
                BaseActivationState.Pending, replacementPayload.EffectiveDueAt, 0,
                request.Replacement.MaximumYields, 0, null, null, null, null);
            var next = current.Clone();
            if (!TryReserveYieldReceiptSlots(next, request.Replacement.MaximumYields))
                return ActivationFailure<BaseActivationMigrationResult>("base.activation.capacityUnavailable", OperationStatus.CapabilityUnavailable, ErrorCategory.Capability);
            next.Activations[source.Payload.ActivationId] = source with
            {
                State = BaseActivationState.Migrated, Generation = sourceGeneration, Claim = null, Lease = null,
                Effect = null, ControlChecksum = sourceChecksum,
            };
            next.Activations.Add(replacementPayload.ActivationId, new InMemoryActivationRow(
                replacementPayload, BaseActivationState.Pending, 1, replacementPayload.RequestedDueAt,
                replacementPayload.EffectiveDueAt, replacementFingerprint, replacementChecksum,
                request.Replacement.OccurrenceId, request.Replacement.Priority,
                request.Replacement.OverlapKey.IsDefaultOrEmpty ? null : request.Replacement.OverlapKey.ToArray(),
                request.Replacement.OverlapPolicy,
                MaximumYields: request.Replacement.MaximumYields));
            IndexActivation(next, replacementPayload);
            next.ActivationIndexGeneration = checked(next.ActivationIndexGeneration + 2);
            var result = new BaseActivationMigrationResult
            {
                SourceActivationId = source.Payload.ActivationId, SourceGeneration = sourceGeneration,
                SourceDefinition = source.Payload.Definition with
                { Checksum = source.Payload.Definition.Checksum.ToArray().ToImmutableArray() },
                SourceControlChecksum = sourceChecksum.ToImmutableArray(),
                ReplacementActivationId = replacementPayload.ActivationId, ReplacementGeneration = 1,
                ReplacementDefinition = replacementPayload.Definition with
                { Checksum = replacementPayload.Definition.Checksum.ToArray().ToImmutableArray() },
                ReplacementControlChecksum = replacementChecksum.ToImmutableArray(),
                MigrationId = new string(request.MigrationId.AsSpan()),
                MigrationVersion = request.MigrationVersion,
                MigrationChecksum = request.MigrationChecksum.ToArray().ToImmutableArray(),
                Accounting = EmptyActivationAccounting with { Candidates = 1, Comparisons = 8, IndexOperations = 2 },
                Disposition = BaseMutationRequestDisposition.Committed,
            };
            byte[] terminalReceiptChecksum = WriteControlReceipt(next, request.Identity, "activation-migrated", result,
                HPDBaseJsonSerializerContext.Default.BaseActivationMigrationResult);
            next.Activations[source.Payload.ActivationId] = next.Activations[source.Payload.ActivationId] with
            { TerminalReceiptChecksum = terminalReceiptChecksum };
            if (!ActivationRowCapacityAllows(next))
                return ActivationFailure<BaseActivationMigrationResult>("base.activation.capacityUnavailable", OperationStatus.CapabilityUnavailable, ErrorCategory.Capability);
            Volatile.Write(ref _publishedState, next);
            return OperationResults.Ok(result);
        }
        finally { _stateGate.Release(); }
    }

    /// <inheritdoc />
    public async ValueTask<OperationResult<BaseActivationMaintenancePage>> AdvanceMaintenanceAsync(
        BaseActivationMaintenanceRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!Enum.IsDefined(request.Kind) || request.Take is < 1 or > 256 || !ValidateLimits(request.Limits)
            || !AcceptActivationTime(request.AcceptedTime))
            return ActivationFailure<BaseActivationMaintenancePage>("base.activation.invalid", OperationStatus.ValidationFailed, ErrorCategory.Validation);
        await _stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            InMemoryStoreState current = Volatile.Read(ref _publishedState); InMemoryStoreState next = current.Clone();
            if (TryReadControlReceipt(current, request.Identity, "activation-maintenance",
                HPDBaseJsonSerializerContext.Default.BaseActivationMaintenancePage,
                static value => value with { Disposition = BaseMutationRequestDisposition.Duplicate },
                out OperationResult<BaseActivationMaintenancePage>? replay)) return replay;
            InMemoryActivationRow[] candidates = current.Activations.Values
                .Where(row => row.Payload.Definition.Id == request.Definition.Id && row.Payload.Definition.Version == request.Definition.Version
                    && CryptographicOperations.FixedTimeEquals(row.Payload.Definition.Checksum.AsSpan(), request.Definition.Checksum.AsSpan())
                    && ScopeMatches(row.Payload.Scope, request.Scope)
                    && (request.AfterActivationId is null || string.CompareOrdinal(row.Payload.ActivationId, request.AfterActivationId) > 0))
                .Where(row => request.Kind switch
                {
                    BaseActivationMaintenanceKind.RecoverExpiredClaims => row.State == BaseActivationState.Claimed
                        && row.Lease is not null && row.Lease.LeaseExpiresAt <= request.AcceptedTime.CapturedUtc,
                    BaseActivationMaintenanceKind.RecoverExpiredEffects => row.State == BaseActivationState.EffectStarted
                        && row.Effect is not null && row.Effect.HeartbeatExpiresAt <= request.AcceptedTime.CapturedUtc
                        && !CurrentExecutorAllows(current, row.Effect.Executor, request.AcceptedTime.CapturedUtc),
                    _ => false,
                }).OrderBy(static row => row.Payload.ActivationId, StringComparer.Ordinal).Take(request.Take + 1).ToArray();
            bool completed = candidates.Length <= request.Take; InMemoryActivationRow[] page = candidates.Take(request.Take).ToArray();
            var items = ImmutableArray.CreateBuilder<BaseActivationMaintenanceItem>(page.Length);
            foreach (InMemoryActivationRow row in page)
            {
                BaseActivationState state = request.Kind == BaseActivationMaintenanceKind.RecoverExpiredClaims
                    ? BaseActivationState.RetryPending : BaseActivationState.OutcomeUnknown;
                long generation = checked(row.Generation + 1);
                long effectiveDueAt = request.Kind == BaseActivationMaintenanceKind.RecoverExpiredClaims
                    ? request.AcceptedTime.CapturedUtc : row.EffectiveDueAt;
                byte[] checksum = ControlChecksum(row.Payload.ActivationId, generation, state,
                    effectiveDueAt, row.YieldCount, row.MaximumYields, row.ExecutionSliceOrdinal,
                    row.AttemptStartedAt, row.SliceStartedAt, null, null);
                next.Activations[row.Payload.ActivationId] = row with
                {
                    State = state, Generation = generation, ControlChecksum = checksum,
                    EffectiveDueAt = effectiveDueAt,
                    Claim = null, Lease = null,
                };
                items.Add(new BaseActivationMaintenanceItem
                {
                    ActivationId = row.Payload.ActivationId, PreviousGeneration = row.Generation,
                    ResultingGeneration = generation, PreviousState = row.State, ResultingState = state,
                    ControlChecksum = checksum.ToImmutableArray(),
                });
            }
            if (page.Length != 0) next.ActivationIndexGeneration = checked(next.ActivationIndexGeneration + 1);
            var result = new BaseActivationMaintenancePage
            {
                Items = items.MoveToImmutable(), NextActivationId = completed || page.Length == 0 ? null : page[^1].Payload.ActivationId,
                Completed = completed, Accounting = EmptyActivationAccounting with
                { Candidates = candidates.Length, Comparisons = candidates.Length, IndexOperations = page.Length },
                Disposition = BaseMutationRequestDisposition.Committed,
            };
            WriteControlReceipt(next, request.Identity, "activation-maintenance", result,
                HPDBaseJsonSerializerContext.Default.BaseActivationMaintenancePage);
            if (!ActivationRowCapacityAllows(next))
                return ActivationFailure<BaseActivationMaintenancePage>("base.activation.capacityUnavailable", OperationStatus.CapabilityUnavailable, ErrorCategory.Capability);
            Volatile.Write(ref _publishedState, next);
            return OperationResults.Ok(result);
        }
        finally { _stateGate.Release(); }
    }

    /// <inheritdoc />
    public async ValueTask<OperationResult<BaseActivationPrunePage>> PruneAsync(
        BaseActivationPruneRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Take is < 1 or > 256 || !ValidateLimits(request.Limits) || request.Take > request.Limits.MaximumCandidates
            || !AcceptActivationTime(request.AcceptedTime))
            return ActivationFailure<BaseActivationPrunePage>("base.activation.invalid", OperationStatus.ValidationFailed, ErrorCategory.Validation);
        await _stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            InMemoryStoreState current = Volatile.Read(ref _publishedState); InMemoryStoreState next = current.Clone();
            if (TryReadControlReceipt(current, request.Identity, "activation-pruned",
                HPDBaseJsonSerializerContext.Default.BaseActivationPrunePage,
                static value => value with { Disposition = BaseMutationRequestDisposition.Duplicate },
                out OperationResult<BaseActivationPrunePage>? replay)) return replay;
            string authorityKey = DisposedActivationAuthorityKey(request.Scope, request.Definition);
            var pageBuilder = new List<string>(request.Take);
            bool hasBoundaryCandidate = false;
            if (current.DisposedActivationsByAuthority.TryGetValue(
                authorityKey, out SortedSet<string>? activationIds))
            {
                foreach (string id in activationIds)
                {
                    if (request.AfterActivationId is not null
                        && string.CompareOrdinal(id, request.AfterActivationId) <= 0)
                        continue;
                    if (pageBuilder.Count == request.Take)
                    {
                        hasBoundaryCandidate = true;
                        break;
                    }
                    pageBuilder.Add(id);
                }
            }
            string[] page = [.. pageBuilder];
            bool completed = !hasBoundaryCandidate;
            long resultingGeneration = page.Length == 0 ? next.ActivationIndexGeneration : checked(next.ActivationIndexGeneration + 1);
            var evidence = ImmutableArray.CreateBuilder<BaseActivationPruneEvidence>(page.Length);
            BaseActivationInstanceReceiptChainState priorChain = next.ActivationInstanceReceiptChain;
            BaseActivationYieldReservationState priorReservation = BaseActivationYieldReservationContract.Create(
                next.ActivationYieldReservationGeneration, MaximumYieldReceiptSlots,
                next.ActivationYieldReservedUnusedSlots, next.ActivationYieldRetainedUsedSlots);
            int deletedReceiptCount = 0;
            int deletedYieldReceiptCount = 0;
            string pruneReceiptKey = ActivationReceiptKey(request.Identity);
            foreach (string id in page)
            {
                InMemoryActivationRow row = next.Activations[id];
                KeyValuePair<string, InMemoryActivationInstanceReceiptRow>[] receipts = next.ActivationInstanceReceipts
                    .Where(pair => pair.Value.ActivationId == id)
                    .OrderBy(static pair => pair.Value.ReceiptSequence).ToArray();
                if (receipts.Any(receipt =>
                    receipt.Value.DuplicateResolveUntil > request.AcceptedTime.CapturedUtc
                    || receipt.Value.Retention.ProtectedBackupCoverage != BaseActivationProtectedBackupCoverage.NotRequired))
                    return ActivationFailure<BaseActivationPrunePage>(
                        "base.activation.removalBlocked", OperationStatus.Conflict, ErrorCategory.Conflict);
                if (row.Effect is not null || row.Claim is not null || row.TerminalReceiptChecksum is not { Length: 32 }
                    || !CryptographicOperations.FixedTimeEquals(row.ControlChecksum,
                        ControlChecksum(id, row.Generation, BaseActivationState.Disposed,
                            row.EffectiveDueAt, row.YieldCount, row.MaximumYields,
                            row.ExecutionSliceOrdinal, row.AttemptStartedAt, row.SliceStartedAt,
                            row.YieldTerminalDisposition, row.YieldTerminalFailureCode)))
                    return ActivationFailure<BaseActivationPrunePage>("base.activation.removalBlocked", OperationStatus.Conflict, ErrorCategory.Conflict);
                ImmutableArray<byte>? occurrence = row.OccurrenceId is not null && next.ScheduleOccurrences.TryGetValue(row.OccurrenceId, out BaseScheduleOccurrenceFact? fact)
                    ? fact.Checksum.ToArray().ToImmutableArray() : null;
                byte[] publication = SHA256.HashData(Encoding.UTF8.GetBytes(
                    $"base.activation.publicationAuthority.v1\0{request.ApplicationId}\n{_options.StoreId}\n{_options.StoreId}\n0\n{resultingGeneration}"));
                var item = new BaseActivationPruneEvidence
                {
                    ActivationId = id, Definition = row.Payload.Definition with { Checksum = row.Payload.Definition.Checksum.ToArray().ToImmutableArray() },
                    TerminalGeneration = row.Generation, TerminalControlChecksum = row.ControlChecksum.ToImmutableArray(),
                    TerminalReceiptChecksum = row.TerminalReceiptChecksum.ToImmutableArray(),
                    OccurrenceChecksum = occurrence, ResultChecksum = row.CanonicalResult is null ? null : SHA256.HashData(row.CanonicalResult).ToImmutableArray(),
                    PruneAuthorityGeneration = resultingGeneration, ApplicationId = request.ApplicationId, LogicalStoreId = _options.StoreId,
                    StoreInstanceId = _options.StoreId, RestoreEpoch = 0, PublicationAuthorityChecksum = publication.ToImmutableArray(), Checksum = [],
                };
                item = item with { Checksum = BaseActivationPruneEvidenceContract.Checksum(item) };
                next.ActivationPruneFloors.Add(id, item); evidence.Add(item);
                foreach (KeyValuePair<string, InMemoryActivationInstanceReceiptRow> receipt in receipts)
                {
                    BaseActivationCompactedReceiptFact compactedFact = BaseActivationCompactedReceiptFactContract.Create(
                        receipt.Value.ReceiptSequence, receipt.Key, receipt.Value.AuthorityChecksum,
                        receipt.Value.PriorOrderedChecksum, receipt.Value.OrderedChecksum, pruneReceiptKey);
                    next.ActivationInstanceReceiptCompactionFacts.Add(compactedFact.ReceiptSequence, compactedFact);
                    next.ActivationInstanceReceipts.Remove(receipt.Key);
                    deletedReceiptCount = checked(deletedReceiptCount + 1);
                    if (receipt.Value.Kind == "activation-yielded-v1")
                        deletedYieldReceiptCount = checked(deletedYieldReceiptCount + 1);
                }
                next.Activations.Remove(id);
                foreach (SortedSet<string> index in next.ActivationsByProtectedScope.Values) index.Remove(id);
                RemoveDisposedActivation(next, row.Payload);
            }
            if (deletedYieldReceiptCount > next.ActivationYieldRetainedUsedSlots)
                return ActivationFailure<BaseActivationPrunePage>(
                    "base.activation.receiptCorrupt", OperationStatus.StoreError, ErrorCategory.Store);
            if (deletedYieldReceiptCount > 0)
            {
                next.ActivationYieldRetainedUsedSlots -= deletedYieldReceiptCount;
                next.ActivationYieldReservationGeneration = checked(next.ActivationYieldReservationGeneration + 1);
            }
            if (deletedReceiptCount > 0)
                next.ActivationInstanceReceiptChain = BaseActivationInstanceReceiptChainContract.Create(
                    priorChain.CurrentSequence, priorChain.OrderedChecksum.AsSpan(), checked(priorChain.Generation + 1));
            next.ActivationIndexGeneration = resultingGeneration;
            long evidenceBytes = 0;
            foreach (BaseActivationPruneEvidence item in evidence) evidenceBytes = checked(evidenceBytes + BaseActivationPruneEvidenceContract.MeasureCanonicalBytes(item));
            long transientBytes = checked(evidenceBytes + page.Sum(static id => 4L + Encoding.UTF8.GetByteCount(id)));
            int readIntervals = hasBoundaryCandidate ? 2 : 1;
            int boundaryProbe = hasBoundaryCandidate ? 1 : 0;
            int indexOperations = checked(1 + boundaryProbe + page.Length * 2 + deletedReceiptCount * 2);
            if (page.Length > request.Limits.MaximumCandidates || evidenceBytes > request.Limits.MaximumEvidenceBytes
                || transientBytes > request.Limits.MaximumTransientBytes || indexOperations > request.Limits.MaximumIndexOperations
                || readIntervals > request.Limits.MaximumReadIntervals)
                return ActivationFailure<BaseActivationPrunePage>("base.activation.budgetExceeded", OperationStatus.ValidationFailed, ErrorCategory.Validation);
            BaseActivationYieldReservationState resultingReservation = BaseActivationYieldReservationContract.Create(
                next.ActivationYieldReservationGeneration, MaximumYieldReceiptSlots,
                next.ActivationYieldReservedUnusedSlots, next.ActivationYieldRetainedUsedSlots);
            var result = new BaseActivationPrunePage
            {
                Items = evidence.MoveToImmutable(), NextActivationId = completed || page.Length == 0 ? null : page[^1],
                DeletedReceiptCount = deletedReceiptCount,
                DeletedYieldReceiptCount = deletedYieldReceiptCount,
                PriorChain = priorChain,
                ResultingChain = next.ActivationInstanceReceiptChain,
                PriorReservation = priorReservation,
                ResultingReservation = resultingReservation,
                Completed = completed, Accounting = EmptyActivationAccounting with
                { Candidates = page.Length, Comparisons = page.Length + boundaryProbe, IndexOperations = indexOperations,
                    ReadIntervals = readIntervals, EvidenceBytes = evidenceBytes, TransientBytes = transientBytes },
                Disposition = BaseMutationRequestDisposition.Committed,
            };
            WriteControlReceipt(next, request.Identity, "activation-pruned", result,
                HPDBaseJsonSerializerContext.Default.BaseActivationPrunePage);
            Volatile.Write(ref _publishedState, next);
            return OperationResults.Ok(result);
        }
        finally { _stateGate.Release(); }
    }

    /// <inheritdoc />
    public async ValueTask<OperationResult<BaseActivationIndeterminateResolution>> ResolveIndeterminateAsync(
        BaseActivationIndeterminateRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        OperationResult<BaseActivationTransitionResult> result = await TransitionAsync(request.Reconciliation, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess() && result.Value is not null
            ? OperationResults.Ok(new BaseActivationIndeterminateResolution { Transition = result.Value })
            : new OperationResult<BaseActivationIndeterminateResolution> { Status = result.Status, Error = result.Error };
    }

    /// <inheritdoc />
    public ValueTask<OperationResult<BaseActivationQuarantinePage>> ReadQuarantineAsync(
        BaseActivationQuarantineRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request); cancellationToken.ThrowIfCancellationRequested();
        if (request.Take is < 1 or > 256 || request.AfterSequence < 0)
            return ValueTask.FromResult(ActivationFailure<BaseActivationQuarantinePage>(
                "base.activation.invalid", OperationStatus.ValidationFailed, ErrorCategory.Validation));
        return ValueTask.FromResult(OperationResults.Ok(new BaseActivationQuarantinePage { Items = [], NextSequence = null }));
    }

    /// <inheritdoc />
    public async ValueTask<OperationResult<BaseActivationDueObservation>> ObserveDueAsync(
        BaseActivationDueObservationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!AcceptActivationTime(request.AcceptedTime))
            return ActivationFailure<BaseActivationDueObservation>("base.activation.clockInvalid", OperationStatus.ValidationFailed, ErrorCategory.Validation);
        if (!ValidateLimits(request.Limits) || request.MaximumCandidates < 1 ||
            request.MaximumCandidates > Math.Min(256, request.Limits.MaximumCandidates))
            return ActivationFailure<BaseActivationDueObservation>("base.activation.budgetExceeded", OperationStatus.ValidationFailed, ErrorCategory.Validation);

        await _stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            InMemoryStoreState state = Volatile.Read(ref _publishedState);
            List<InMemoryActivationRow> eligible = EligibleRows(
                state, request.Definitions, request.Scope, request.AcceptedTime.CapturedUtc, request.After);
            int inspected = Math.Min(eligible.Count, request.MaximumCandidates);
            InMemoryActivationRow? first = eligible.FirstOrDefault();
            BaseActivationDueBoundary? boundary = first is null ? null : Boundary(first, request.AcceptedTime.CapturedUtc);
            byte[] token = DueToken(
                state.ActivationIndexGeneration,
                request.AcceptedTime.CapturedUtc,
                request.Scope.ProtectedIndexDigest.AsSpan(),
                request.Definitions,
                boundary);
            BaseAtomicReadIntervalEvidence interval = DueInterval(request.Scope, request.AcceptedTime.CapturedUtc, request.After, boundary);
            long evidenceBytes = checked(token.Length + IntervalBytes(interval));
            if (evidenceBytes > request.Limits.MaximumEvidenceBytes)
                return ActivationFailure<BaseActivationDueObservation>("base.activation.budgetExceeded", OperationStatus.ValidationFailed, ErrorCategory.Validation);

            return OperationResults.Ok(new BaseActivationDueObservation
            {
                Earliest = boundary,
                Token = new BaseDueObservationToken { Value = token.ToImmutableArray() },
                Intervals = [interval],
                Accounting = new BaseActivationAccounting
                {
                    Candidates = inspected,
                    Comparisons = inspected,
                    IndexOperations = 1,
                    ReadIntervals = 1,
                    EvidenceBytes = evidenceBytes,
                    TransientBytes = evidenceBytes,
                },
            });
        }
        finally
        {
            _stateGate.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask<BaseDueWaitResult> WaitForDueChangeAsync(
        BaseDueObservationToken token,
        DateTimeOffset deadline,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(token);
        (long observedGeneration, long acceptedAt) = DecodeDueAuthority(token.Value.AsSpan());
        if (observedGeneration < 0 || _timeProvider.GetUtcNow().ToUnixTimeMilliseconds() - acceptedAt > 300_000)
            return new BaseDueWaitResult { Outcome = BaseDueWaitOutcome.TokenInvalid };
        while (_timeProvider.GetUtcNow() < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Volatile.Read(ref _publishedState).ActivationIndexGeneration != observedGeneration)
                return new BaseDueWaitResult { Outcome = BaseDueWaitOutcome.Changed };
            TimeSpan remaining = deadline - _timeProvider.GetUtcNow();
            await Task.Delay(remaining < TimeSpan.FromMilliseconds(25) ? remaining : TimeSpan.FromMilliseconds(25), cancellationToken)
                .ConfigureAwait(false);
        }
        return new BaseDueWaitResult { Outcome = BaseDueWaitOutcome.Deadline };
    }

    /// <inheritdoc />
    public async ValueTask<OperationResult<BaseActivationClaimResult>> TryClaimNextAsync(
        BaseActivationClaimRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!AcceptActivationTime(request.AcceptedTime))
            return ActivationFailure<BaseActivationClaimResult>("base.activation.clockInvalid", OperationStatus.ValidationFailed, ErrorCategory.Validation);
        if (!ValidateLimits(request.Limits) || request.LeaseMilliseconds <= 0)
            return ActivationFailure<BaseActivationClaimResult>("base.activation.invalid", OperationStatus.ValidationFailed, ErrorCategory.Validation);

        await _stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            InMemoryStoreState current = Volatile.Read(ref _publishedState);
            if (TryReadInstanceReceipt(current, request.Identity, "activation-claimed", request.AcceptedTime.CapturedUtc,
                HPDBaseJsonSerializerContext.Default.BaseActivationClaimResult, static value => value,
                out OperationResult<BaseActivationClaimResult>? replay))
                return ResolveClaimReplay(current, replay, request.AcceptedTime.CapturedUtc);
            (long tokenGeneration, long tokenAcceptedAt) = DecodeDueAuthority(request.Observation.Value.AsSpan());
            if (tokenGeneration < 0 || request.AcceptedTime.CapturedUtc - tokenAcceptedAt > 300_000)
                return ActivationFailure<BaseActivationClaimResult>("base.activation.observationTokenInvalid", OperationStatus.ValidationFailed, ErrorCategory.Validation);
            if (tokenGeneration != current.ActivationIndexGeneration)
                return OperationResults.Ok<BaseActivationClaimResult>(new BaseActivationObservationChangedResult(
                    new BaseDueObservationToken { Value = CurrentWorkerToken(current, request).ToImmutableArray() }));

            List<InMemoryActivationRow> eligible = EligibleRows(
                current, request.Worker.Definitions, request.Worker.Scope, request.AcceptedTime.CapturedUtc, null);
            if (eligible.Count == 0)
                return OperationResults.Ok<BaseActivationClaimResult>(new BaseActivationClaimEmptyResult(
                    new BaseDueObservationToken { Value = CurrentWorkerToken(current, request).ToImmutableArray() }));

            InMemoryActivationRow row = eligible[0];
            var next = current.Clone();
            InMemoryActivationRow mutable = next.Activations[row.Payload.ActivationId];
            if (mutable.State == BaseActivationState.Claimed && mutable.Lease is not null &&
                mutable.Lease.LeaseExpiresAt <= request.AcceptedTime.CapturedUtc)
            {
                long recoveredGeneration = checked(mutable.Generation + 1);
                next.Activations[row.Payload.ActivationId] = mutable with
                {
                    State = BaseActivationState.RetryPending,
                    Generation = recoveredGeneration,
                    Claim = null,
                    Lease = null,
                    EffectiveDueAt = request.AcceptedTime.CapturedUtc,
                    ControlChecksum = ControlChecksum(row.Payload.ActivationId, recoveredGeneration,
                        BaseActivationState.RetryPending, request.AcceptedTime.CapturedUtc,
                        row.YieldCount, row.MaximumYields, row.ExecutionSliceOrdinal,
                        row.AttemptStartedAt, row.SliceStartedAt, null, null),
                };
                next.ActivationIndexGeneration = checked(next.ActivationIndexGeneration + 1);
                if (!ActivationRowCapacityAllows(next))
                    return ActivationFailure<BaseActivationClaimResult>("base.activation.capacityUnavailable", OperationStatus.CapabilityUnavailable, ErrorCategory.Capability);
                BaseActivationClaimResult recoveredResult = new BaseActivationRecoveredClaimResult(
                    row.Payload.ActivationId, recoveredGeneration);
                WriteInstanceReceipt(next, request.Identity, "activation-claimed", mutable, request.AcceptedTime.CapturedUtc, recoveredResult,
                    HPDBaseJsonSerializerContext.Default.BaseActivationClaimResult);
                Volatile.Write(ref _publishedState, next);
                return OperationResults.Ok(recoveredResult);
            }

            bool resumedYield = mutable.State == BaseActivationState.YieldPending;
            int attemptNumber = resumedYield ? mutable.AttemptNumber : checked(mutable.AttemptNumber + 1);
            long executionSliceOrdinal = checked(mutable.ExecutionSliceOrdinal + 1);
            long attemptStartedAt = resumedYield
                ? mutable.AttemptStartedAt ?? throw new InvalidOperationException("base.activation.providerContractInvalid")
                : request.AcceptedTime.CapturedUtc;
            long sliceStartedAt = request.AcceptedTime.CapturedUtc;
            long claimEpoch = checked(mutable.ClaimEpoch + 1);
            long generation = checked(mutable.Generation + 1);
            byte[] fence = BaseActivationClaimChecksumContract.Create(mutable.Payload.ActivationId,
                attemptNumber, claimEpoch, executionSliceOrdinal, attemptStartedAt, sliceStartedAt,
                mutable.YieldCount, mutable.MaximumYields, request.Worker.WorkerIdentity).ToArray();
            var claim = new BaseActivationClaimAuthority
            {
                ActivationId = mutable.Payload.ActivationId,
                AttemptNumber = attemptNumber,
                ActivationGeneration = generation,
                ExecutionSliceOrdinal = executionSliceOrdinal,
                AttemptStartedAt = attemptStartedAt,
                SliceStartedAt = sliceStartedAt,
                YieldCount = mutable.YieldCount,
                MaximumYields = mutable.MaximumYields,
                ClaimEpoch = claimEpoch,
                FencingToken = fence.ToImmutableArray(),
                WorkerIdentity = request.Worker.WorkerIdentity,
                CancellationGeneration = 0,
                StoreInstanceId = _options.StoreId,
                RestoreEpoch = 0,
                DefinitionChecksum = mutable.Payload.Definition.Checksum.ToArray().ToImmutableArray(),
            };
            long expiresAt = checked(request.AcceptedTime.CapturedUtc + request.LeaseMilliseconds);
            var lease = new BaseActivationLeaseObservation
            {
                LeaseRevision = 1,
                LeaseExpiresAt = expiresAt,
                Checksum = Hash($"base.activation.lease.v2\0{mutable.Payload.ActivationId}\n1\n{expiresAt}").ToImmutableArray(),
            };
            byte[] controlChecksum = ControlChecksum(mutable.Payload.ActivationId, generation,
                BaseActivationState.Claimed, mutable.EffectiveDueAt, mutable.YieldCount,
                mutable.MaximumYields, executionSliceOrdinal, attemptStartedAt, sliceStartedAt, null, null);
            next.Activations[mutable.Payload.ActivationId] = mutable with
            {
                State = BaseActivationState.Claimed,
                Generation = generation,
                AttemptNumber = attemptNumber,
                ExecutionSliceOrdinal = executionSliceOrdinal,
                AttemptStartedAt = attemptStartedAt,
                SliceStartedAt = sliceStartedAt,
                ClaimEpoch = claimEpoch,
                Claim = claim,
                Lease = lease,
                ControlChecksum = controlChecksum,
            };
            next.ActivationIndexGeneration = checked(next.ActivationIndexGeneration + 1);
            var attempt = new BaseActivationAttemptEvidence
            {
                AttemptId = $"{mutable.Payload.ActivationId}:{attemptNumber}",
                AttemptNumber = attemptNumber,
                StartedAt = request.AcceptedTime.CapturedUtc,
                Checksum = Hash($"base.activation.attempt.v2\0{mutable.Payload.ActivationId}\n{attemptNumber}").ToImmutableArray(),
            };
            BaseActivationClaimResult claimedResult = new BaseActivationClaimedResult(
                mutable.Payload.DeepClone(),
                claim,
                lease,
                attempt,
                [DueInterval(request.Worker.Scope, request.AcceptedTime.CapturedUtc, null, Boundary(mutable, request.AcceptedTime.CapturedUtc))],
                EmptyActivationAccounting with { Candidates = 1, Comparisons = 1 });
            WriteInstanceReceipt(next, request.Identity, "activation-claimed", mutable, request.AcceptedTime.CapturedUtc, claimedResult,
                HPDBaseJsonSerializerContext.Default.BaseActivationClaimResult);
            if (!ActivationRowCapacityAllows(next))
                return ActivationFailure<BaseActivationClaimResult>("base.activation.capacityUnavailable", OperationStatus.CapabilityUnavailable, ErrorCategory.Capability);
            Volatile.Write(ref _publishedState, next);
            return OperationResults.Ok(claimedResult);
        }
        finally
        {
            _stateGate.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask<OperationResult<BaseTransactionalActivationCandidate>> ReadTransactionalCandidateAsync(
        BaseTransactionalActivationCandidateRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!AcceptActivationTime(request.AcceptedTime) || !ValidateLimits(request.Limits))
            return ActivationFailure<BaseTransactionalActivationCandidate>(
                "base.activation.invalid", OperationStatus.ValidationFailed, ErrorCategory.Validation);

        await _stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            InMemoryStoreState state = Volatile.Read(ref _publishedState);
            (long observedGeneration, long observedAt) = DecodeDueAuthority(request.Observation.Value.AsSpan());
            if (observedGeneration != state.ActivationIndexGeneration
                || request.AcceptedTime.CapturedUtc - observedAt > 300_000)
                return ActivationFailure<BaseTransactionalActivationCandidate>(
                    "base.activation.claimUnavailable", OperationStatus.Conflict, ErrorCategory.Conflict);
            ImmutableArray<BaseActivationDefinitionKey> definitions = [request.Definition];
            InMemoryActivationRow? row = EligibleRows(
                state, definitions, request.Scope, request.AcceptedTime.CapturedUtc, null).FirstOrDefault();
            if (row is null)
                return ActivationFailure<BaseTransactionalActivationCandidate>(
                    "base.activation.notDue", OperationStatus.Conflict, ErrorCategory.Conflict);
            BaseActivationDueBoundary boundary = Boundary(row, request.AcceptedTime.CapturedUtc);
            BaseAtomicReadIntervalEvidence interval = DueInterval(request.Scope, request.AcceptedTime.CapturedUtc, null, boundary);
            long evidenceBytes = checked(row.Payload.CanonicalInput.Length + row.ControlChecksum.Length + IntervalBytes(interval));
            if (evidenceBytes > request.Limits.MaximumEvidenceBytes || row.Payload.CanonicalInput.Length > request.Limits.MaximumInputBytes)
                return ActivationFailure<BaseTransactionalActivationCandidate>(
                    "base.activation.budgetExceeded", OperationStatus.ValidationFailed, ErrorCategory.Validation);
            return OperationResults.Ok(new BaseTransactionalActivationCandidate
            {
                Payload = row.Payload.DeepClone(),
                ActivationGeneration = row.Generation,
                AcceptedAt = request.AcceptedTime.CapturedUtc,
                ControlChecksum = row.ControlChecksum.ToImmutableArray(),
                ReadIntervals = [interval],
                Accounting = new BaseActivationAccounting
                {
                    Candidates = 1,
                    Comparisons = 1,
                    IndexOperations = 1,
                    ReadIntervals = 1,
                    EvidenceBytes = evidenceBytes,
                    TransientBytes = evidenceBytes,
                },
                Limits = request.Limits with { },
            });
        }
        finally
        {
            _stateGate.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask<OperationResult<BaseActivationRenewResult>> RenewAsync(
        BaseActivationRenewRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!AcceptActivationTime(request.AcceptedTime))
            return ActivationFailure<BaseActivationRenewResult>("base.activation.clockInvalid", OperationStatus.ValidationFailed, ErrorCategory.Validation);
        await _stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            InMemoryStoreState current = Volatile.Read(ref _publishedState);
            if (TryReadInstanceReceipt(current, request.Identity, "activation-renewed", request.AcceptedTime.CapturedUtc,
                HPDBaseJsonSerializerContext.Default.BaseActivationRenewResult,
                static value => value with { Disposition = BaseMutationRequestDisposition.Duplicate }, out OperationResult<BaseActivationRenewResult>? replay))
                return replay;
            if (!current.Activations.TryGetValue(request.Claim.ActivationId, out InMemoryActivationRow? row) ||
                !ClaimMatches(row, request.Claim) || row.Lease?.LeaseRevision != request.ExpectedLeaseRevision ||
                row.Lease.LeaseExpiresAt <= request.AcceptedTime.CapturedUtc)
                return ActivationFailure<BaseActivationRenewResult>("base.activation.claimLost", OperationStatus.Conflict, ErrorCategory.Conflict);
            long revision = checked(request.ExpectedLeaseRevision + 1);
            long expiresAt = checked(request.AcceptedTime.CapturedUtc + request.ExtensionMilliseconds);
            var lease = new BaseActivationLeaseObservation
            {
                LeaseRevision = revision,
                LeaseExpiresAt = expiresAt,
                Checksum = Hash($"base.activation.lease.v2\0{row.Payload.ActivationId}\n{revision}\n{expiresAt}").ToImmutableArray(),
            };
            var next = current.Clone();
            next.Activations[row.Payload.ActivationId] = next.Activations[row.Payload.ActivationId] with { Lease = lease };
            var result = new BaseActivationRenewResult
            {
                Claim = request.Claim,
                Lease = lease,
                Accounting = EmptyActivationAccounting with
                { Candidates = 1, Comparisons = 1, ReadIntervals = 0, IndexOperations = 1 },
                Disposition = BaseMutationRequestDisposition.Committed,
            };
            WriteInstanceReceipt(next, request.Identity, "activation-renewed", row, request.AcceptedTime.CapturedUtc, result, HPDBaseJsonSerializerContext.Default.BaseActivationRenewResult);
            Volatile.Write(ref _publishedState, next);
            return OperationResults.Ok(result);
        }
        finally
        {
            _stateGate.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask<OperationResult<BaseActivationTransitionResult>> TransitionAsync(
        BaseActivationTransitionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!AcceptActivationTime(request.AcceptedTime))
            return ActivationFailure<BaseActivationTransitionResult>("base.activation.clockInvalid", OperationStatus.ValidationFailed, ErrorCategory.Validation);
        await _stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            InMemoryStoreState current = Volatile.Read(ref _publishedState);
            string receiptKind = ActivationTransitionReceiptKind(request);
            if (request is BaseActivationYieldRequest && TryReadInstanceReceipt(current,
                request.Identity, receiptKind, request.AcceptedTime.CapturedUtc, HPDBaseJsonSerializerContext.Default.BaseActivationYieldReceipt,
                static value => value, out OperationResult<BaseActivationYieldReceipt>? yieldReplay))
                return yieldReplay.IsSuccess() && yieldReplay.Value is { } storedYield
                    ? OperationResults.Ok(storedYield.ToTransitionResult(BaseMutationRequestDisposition.Duplicate))
                    : new OperationResult<BaseActivationTransitionResult>
                    { Status = yieldReplay.Status, Error = yieldReplay.Error };
            if (TryReadInstanceReceipt(current, request.Identity, receiptKind, request.AcceptedTime.CapturedUtc,
                HPDBaseJsonSerializerContext.Default.BaseActivationTransitionResult,
                static value => value with { Disposition = BaseMutationRequestDisposition.Duplicate }, out OperationResult<BaseActivationTransitionResult>? replay))
                return replay;
            if (!current.Activations.TryGetValue(request.ActivationId, out InMemoryActivationRow? row))
                return ActivationFailure<BaseActivationTransitionResult>("base.activation.notFound", OperationStatus.NotFound, ErrorCategory.NotFound);

            if (request is BaseActivationEffectHeartbeatRequest effectHeartbeat)
            {
                if (row.State != BaseActivationState.EffectStarted || row.Effect is null ||
                    !EffectMatches(row.Effect, effectHeartbeat.Effect) ||
                    row.Effect.HeartbeatRevision != effectHeartbeat.ExpectedHeartbeatRevision || effectHeartbeat.ExtensionMilliseconds <= 0 ||
                    !CurrentExecutorAllows(current, row.Effect.Executor, request.AcceptedTime.CapturedUtc))
                    return ActivationFailure<BaseActivationTransitionResult>("base.activation.effectLost", OperationStatus.Conflict, ErrorCategory.Conflict);
                BaseEffectExecutionAuthority replacement = Effect(row.Effect.Claim, row.Effect.Executor,
                    row.Effect.EffectStartGeneration, checked(row.Effect.HeartbeatRevision + 1),
                    checked(request.AcceptedTime.CapturedUtc + effectHeartbeat.ExtensionMilliseconds));
                var heartbeatState = current.Clone();
                heartbeatState.Activations[row.Payload.ActivationId] = heartbeatState.Activations[row.Payload.ActivationId] with { Effect = replacement };
                var heartbeatResult = new BaseActivationTransitionResult
                {
                    State = row.State, Generation = row.Generation, ControlChecksum = row.ControlChecksum.ToImmutableArray(),
                    Accounting = EmptyActivationAccounting with { IndexOperations = 1 }, Disposition = BaseMutationRequestDisposition.Committed,
                    Effect = replacement,
                };
                WriteInstanceReceipt(heartbeatState, request.Identity, receiptKind, row, request.AcceptedTime.CapturedUtc, heartbeatResult,
                    HPDBaseJsonSerializerContext.Default.BaseActivationTransitionResult);
                Volatile.Write(ref _publishedState, heartbeatState);
                return OperationResults.Ok(heartbeatResult);
            }

            BaseActivationState resultingState;
            byte[]? result = null;
            BaseEffectExecutionAuthority? resultingEffect = null;
            BaseActivationYieldRequest? yieldRequest = null;
            BaseActivationYieldDisposition? yieldDisposition = null;
            long resultingYieldCount = row.YieldCount;
            switch (request)
            {
                case BaseActivationCompleteRequest complete when ClaimMatches(row, complete.Claim):
                    if (!CryptographicOperations.FixedTimeEquals(
                        SHA256.HashData(complete.CanonicalResult.AsSpan()), complete.ResultChecksum.AsSpan()))
                        return ActivationFailure<BaseActivationTransitionResult>("base.activation.invalid", OperationStatus.ValidationFailed, ErrorCategory.Validation);
                    resultingState = BaseActivationState.Succeeded;
                    result = complete.CanonicalResult.ToArray();
                    break;
                case BaseActivationFailRequest failed when ClaimMatches(row, failed.Claim):
                    if ((failed.Disposition == BaseActivationFailureDisposition.Retry) != failed.RetryDueAt.HasValue ||
                        failed.RetryDueAt is < 0)
                        return ActivationFailure<BaseActivationTransitionResult>("base.activation.invalid", OperationStatus.ValidationFailed, ErrorCategory.Validation);
                    resultingState = failed.Disposition == BaseActivationFailureDisposition.Retry
                        ? BaseActivationState.RetryPending
                        : BaseActivationState.Exhausted;
                    break;
                case BaseActivationYieldRequest yielded when ClaimMatches(row, yielded.Claim):
                    long? requestedResumeAt = CanonicalYieldResumeAt(yielded.RequestedResumeAt);
                    long expectedEffectiveDueAt = requestedResumeAt.HasValue
                        ? Math.Max(requestedResumeAt.Value, request.AcceptedTime.CapturedUtc)
                        : request.AcceptedTime.CapturedUtc;
                    if (yielded.ProgressFingerprint.Length != 32 || yielded.ExpectedYieldCount != row.YieldCount
                        || yielded.MaximumYields != row.MaximumYields || yielded.MaximumYields <= 0
                        || expectedEffectiveDueAt < 0 || yielded.EffectiveDueAt != expectedEffectiveDueAt)
                        return ActivationFailure<BaseActivationTransitionResult>("base.activation.yieldInvalid", OperationStatus.ValidationFailed, ErrorCategory.Validation);
                    yieldRequest = yielded;
                    if (row.YieldCount == row.MaximumYields)
                    {
                        resultingState = BaseActivationState.Exhausted;
                        yieldDisposition = BaseActivationYieldDisposition.LimitExceeded;
                    }
                    else
                    {
                        resultingState = BaseActivationState.YieldPending;
                        yieldDisposition = BaseActivationYieldDisposition.Yielded;
                        resultingYieldCount = checked(row.YieldCount + 1);
                    }
                    break;
                case BaseActivationCancelRequest cancel when row.Generation == cancel.ExpectedGeneration:
                    resultingState = row.State == BaseActivationState.EffectStarted
                        ? BaseActivationState.EffectStarted
                        : BaseActivationState.Cancelled;
                    resultingEffect = row.State == BaseActivationState.EffectStarted ? row.Effect : null;
                    break;
                case BaseActivationBeginEffectRequest begin when ClaimMatches(row, begin.Claim) && begin.HeartbeatMilliseconds > 0 &&
                    CurrentExecutorAllows(current, begin.Executor, request.AcceptedTime.CapturedUtc) &&
                    HeartbeatsEqual(current.Executors[ExecutorKey(begin.Executor.ApplicationId, begin.Executor.HostId, begin.Executor.ProcessIncarnationId)].Heartbeat, begin.ExecutorHeartbeat):
                    resultingState = BaseActivationState.EffectStarted;
                    resultingEffect = Effect(begin.Claim, begin.Executor, checked(row.Generation + 1), 1,
                        checked(request.AcceptedTime.CapturedUtc + begin.HeartbeatMilliseconds));
                    break;
                case BaseActivationCompleteEffectRequest completeEffect when row.State == BaseActivationState.EffectStarted && row.Effect is not null &&
                    EffectMatches(row.Effect, completeEffect.Effect):
                    if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(completeEffect.CanonicalResult.AsSpan()), completeEffect.ResultChecksum.AsSpan()))
                        return ActivationFailure<BaseActivationTransitionResult>("base.activation.invalid", OperationStatus.ValidationFailed, ErrorCategory.Validation);
                    resultingState = BaseActivationState.Succeeded;
                    result = completeEffect.CanonicalResult.ToArray();
                    break;
                case BaseActivationRecoverEffectRequest recover when row.State == BaseActivationState.EffectStarted && row.Effect is not null &&
                    EffectMatches(row.Effect, recover.Effect) && row.Effect.HeartbeatExpiresAt <= request.AcceptedTime.CapturedUtc &&
                    !CurrentExecutorAllows(current, row.Effect.Executor, request.AcceptedTime.CapturedUtc):
                    resultingState = BaseActivationState.OutcomeUnknown;
                    resultingEffect = row.Effect;
                    break;
                case BaseActivationReconcileEffectRequest reconcile when row.State == BaseActivationState.OutcomeUnknown && row.Effect is not null &&
                    row.Generation == reconcile.ExpectedGeneration &&
                    row.Effect.EffectStartGeneration == reconcile.ExpectedEffectStartGeneration &&
                    reconcile.ExpectedEffectChecksum.Length == 32 &&
                    CryptographicOperations.FixedTimeEquals(row.Effect.Checksum.AsSpan(), reconcile.ExpectedEffectChecksum.AsSpan()):
                    if (reconcile.VerificationEvidence.IsDefaultOrEmpty || reconcile.VerificationChecksum.Length != 32 ||
                        !Enum.IsDefined(reconcile.Disposition) ||
                        !CryptographicOperations.FixedTimeEquals(
                            SHA256.HashData(reconcile.VerificationEvidence.AsSpan()), reconcile.VerificationChecksum.AsSpan()))
                        return ActivationFailure<BaseActivationTransitionResult>("base.activation.invalid", OperationStatus.ValidationFailed, ErrorCategory.Validation);
                    resultingState = reconcile.Disposition switch
                    {
                        BaseEffectReconciliationDisposition.Succeeded => BaseActivationState.Succeeded,
                        BaseEffectReconciliationDisposition.Exhausted => BaseActivationState.Exhausted,
                        BaseEffectReconciliationDisposition.Disposed => BaseActivationState.Disposed,
                        _ => BaseActivationState.OutcomeUnknown,
                    };
                    result = reconcile.Disposition == BaseEffectReconciliationDisposition.Succeeded
                        ? reconcile.VerificationEvidence.ToArray()
                        : null;
                    break;
                case BaseActivationOperatorRetryRequest retry when row.State == BaseActivationState.Exhausted &&
                    row.Generation == retry.ExpectedGeneration && retry.RetryDueAt >= request.AcceptedTime.CapturedUtc:
                    resultingState = BaseActivationState.RetryPending;
                    break;
                case BaseActivationDisposeRequest dispose when row.Generation == dispose.ExpectedGeneration &&
                    row.State is BaseActivationState.Succeeded or BaseActivationState.Exhausted or
                        BaseActivationState.Cancelled or BaseActivationState.Migrated:
                    resultingState = BaseActivationState.Disposed;
                    break;
                default:
                    return ActivationFailure<BaseActivationTransitionResult>("base.activation.claimLost", OperationStatus.Conflict, ErrorCategory.Conflict);
            }

            long generation = checked(row.Generation + 1);
            long resultingEffectiveDueAt = yieldRequest is not null
                ? yieldRequest.EffectiveDueAt
                : resultingState == BaseActivationState.RetryPending
                ? request switch
                {
                    BaseActivationFailRequest failed => failed.RetryDueAt!.Value,
                    BaseActivationOperatorRetryRequest retry => retry.RetryDueAt,
                    _ => row.EffectiveDueAt,
                }
                : row.EffectiveDueAt;
            BaseActivationYieldDisposition? terminalYieldDisposition = yieldDisposition == BaseActivationYieldDisposition.LimitExceeded
                ? BaseActivationYieldDisposition.LimitExceeded : null;
            string? terminalYieldFailureCode = terminalYieldDisposition.HasValue
                ? "base.activation.yieldLimitExceeded" : null;
            byte[] checksum = ControlChecksum(row.Payload.ActivationId, generation, resultingState,
                resultingEffectiveDueAt, resultingYieldCount, row.MaximumYields,
                row.ExecutionSliceOrdinal, row.AttemptStartedAt, row.SliceStartedAt,
                terminalYieldDisposition, terminalYieldFailureCode);
            var next = current.Clone();
            ApplyYieldReceiptReservationTransition(next, row, resultingState, yieldDisposition);
            next.Activations[row.Payload.ActivationId] = next.Activations[row.Payload.ActivationId] with
            {
                State = resultingState,
                Generation = generation,
                Claim = null,
                Lease = null,
                CanonicalResult = result,
                Effect = resultingState is BaseActivationState.EffectStarted or BaseActivationState.OutcomeUnknown
                    ? resultingEffect
                    : null,
                EffectiveDueAt = resultingEffectiveDueAt,
                YieldCount = resultingYieldCount,
                YieldTerminalDisposition = terminalYieldDisposition,
                YieldTerminalFailureCode = terminalYieldFailureCode,
                ControlChecksum = checksum,
            };
            if (resultingState == BaseActivationState.Disposed)
                IndexDisposedActivation(next, row.Payload);
            next.ActivationIndexGeneration = checked(next.ActivationIndexGeneration + 1);
            var transitionResult = new BaseActivationTransitionResult
            {
                State = resultingState,
                Generation = generation,
                ControlChecksum = checksum.ToImmutableArray(),
                Accounting = EmptyActivationAccounting with
                { Candidates = 1, Comparisons = 1, ReadIntervals = 0, IndexOperations = 1 },
                Disposition = BaseMutationRequestDisposition.Committed,
                Effect = resultingEffect,
                CanonicalResult = result?.ToImmutableArray() ?? ImmutableArray<byte>.Empty,
                YieldCount = resultingYieldCount,
                ExecutionSliceOrdinal = row.ExecutionSliceOrdinal,
                EffectiveDueAt = yieldRequest?.EffectiveDueAt,
                YieldDisposition = yieldDisposition,
                YieldTerminalFailureCode = yieldDisposition == BaseActivationYieldDisposition.LimitExceeded
                    ? "base.activation.yieldLimitExceeded" : null,
            };
            BaseActivationYieldReceipt? yieldReceipt = yieldRequest is null ? null : new()
            {
                Definition = row.Payload.Definition with
                { Checksum = row.Payload.Definition.Checksum.ToArray().ToImmutableArray() },
                ActivationId = row.Payload.ActivationId,
                PriorGeneration = row.Generation,
                ResultingGeneration = generation,
                AttemptNumber = row.AttemptNumber,
                ExecutionSliceOrdinal = row.ExecutionSliceOrdinal,
                AttemptStartedAt = row.AttemptStartedAt!.Value,
                SliceStartedAt = row.SliceStartedAt!.Value,
                PriorYieldCount = row.YieldCount,
                ResultingYieldCount = resultingYieldCount,
                EffectiveDueAt = resultingEffectiveDueAt,
                ProgressFingerprint = yieldRequest.ProgressFingerprint.ToArray().ToImmutableArray(),
                ResultingState = resultingState,
                Disposition = yieldDisposition!.Value,
                FailureCode = terminalYieldFailureCode,
                ControlChecksum = checksum.ToImmutableArray(),
                Accounting = transitionResult.Accounting with { },
            };
            byte[] instanceReceiptAuthority = yieldReceipt is null
                ? WriteInstanceReceipt(next, request.Identity, receiptKind, row, request.AcceptedTime.CapturedUtc, transitionResult,
                    HPDBaseJsonSerializerContext.Default.BaseActivationTransitionResult)
                : WriteInstanceReceipt(next, request.Identity, receiptKind, row, request.AcceptedTime.CapturedUtc, yieldReceipt,
                    HPDBaseJsonSerializerContext.Default.BaseActivationYieldReceipt);
            next.Activations[row.Payload.ActivationId] = next.Activations[row.Payload.ActivationId] with
            {
                TerminalReceiptChecksum = resultingState is BaseActivationState.Succeeded or BaseActivationState.Exhausted
                    or BaseActivationState.Cancelled or BaseActivationState.Migrated or BaseActivationState.Disposed
                    ? instanceReceiptAuthority : null,
            };
            if (!ActivationRowCapacityAllows(next))
                return ActivationFailure<BaseActivationTransitionResult>("base.activation.capacityUnavailable", OperationStatus.CapabilityUnavailable, ErrorCategory.Capability);
            Volatile.Write(ref _publishedState, next);
            return OperationResults.Ok(transitionResult);
        }
        finally
        {
            _stateGate.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask<OperationResult<BaseExecutorRegistrationResult>> RegisterExecutorAsync(
        BaseExecutorRegistrationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!AcceptActivationTime(request.AcceptedTime))
            return ActivationFailure<BaseExecutorRegistrationResult>("base.activation.clockInvalid", OperationStatus.ValidationFailed, ErrorCategory.Validation);
        if (string.IsNullOrWhiteSpace(request.ApplicationId) || string.IsNullOrWhiteSpace(request.HostId) ||
            string.IsNullOrWhiteSpace(request.ProcessIncarnationId) || request.WorkerDefinitionSetChecksum.Length != 32 ||
            request.RequestedHeartbeatMilliseconds <= 0 || request.AcceptedTime.ApplicationId != request.ApplicationId)
            return ActivationFailure<BaseExecutorRegistrationResult>("base.activation.invalid", OperationStatus.ValidationFailed, ErrorCategory.Validation);
        await _stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            InMemoryStoreState current = Volatile.Read(ref _publishedState);
            if (TryReadControlReceipt(current, request.Identity, "executor-registered",
                HPDBaseJsonSerializerContext.Default.BaseExecutorRegistrationResult,
                static value => value with { Disposition = BaseMutationRequestDisposition.Duplicate }, out OperationResult<BaseExecutorRegistrationResult>? replay))
                return replay;
            string key = ExecutorKey(request.ApplicationId, request.HostId, request.ProcessIncarnationId);
            if (current.Executors.TryGetValue(key, out InMemoryExecutorRow? existing) && !existing.Retired)
                return ActivationFailure<BaseExecutorRegistrationResult>("base.activation.executorConflict", OperationStatus.Conflict, ErrorCategory.Conflict);
            var next = current.Clone();
            long generation = checked(next.NextExecutorGeneration + 1);
            next.NextExecutorGeneration = generation;
            byte[] authorityChecksum = Hash($"base.activation.executor.v2\0{request.ApplicationId}\n{request.HostId}\n{request.ProcessIncarnationId}\n{generation}\n{_options.StoreId}\n0\n{Convert.ToHexString(request.WorkerDefinitionSetChecksum.AsSpan())}");
            var authority = new BaseExecutorIncarnationAuthority
            {
                ApplicationId = new string(request.ApplicationId.AsSpan()), HostId = new string(request.HostId.AsSpan()),
                ProcessIncarnationId = new string(request.ProcessIncarnationId.AsSpan()), ExecutorGeneration = generation,
                StoreInstanceId = _options.StoreId, RestoreEpoch = 0,
                WorkerDefinitionSetChecksum = request.WorkerDefinitionSetChecksum.ToArray().ToImmutableArray(),
                Checksum = authorityChecksum.ToImmutableArray(),
            };
            var heartbeat = Heartbeat(authority, 1, checked(request.AcceptedTime.CapturedUtc + request.RequestedHeartbeatMilliseconds));
            next.Executors[key] = new InMemoryExecutorRow(authority, heartbeat, false);
            var result = new BaseExecutorRegistrationResult
            {
                Executor = authority, Heartbeat = heartbeat, Accounting = EmptyActivationAccounting with { IndexOperations = 1 },
                Disposition = BaseMutationRequestDisposition.Committed,
            };
            WriteControlReceipt(next, request.Identity, "executor-registered", result, HPDBaseJsonSerializerContext.Default.BaseExecutorRegistrationResult);
            Volatile.Write(ref _publishedState, next);
            return OperationResults.Ok(result);
        }
        finally { _stateGate.Release(); }
    }

    /// <inheritdoc />
    public async ValueTask<OperationResult<BaseExecutorHeartbeatResult>> HeartbeatExecutorAsync(
        BaseExecutorHeartbeatRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!AcceptActivationTime(request.AcceptedTime))
            return ActivationFailure<BaseExecutorHeartbeatResult>("base.activation.clockInvalid", OperationStatus.ValidationFailed, ErrorCategory.Validation);
        if (request.ExpectedHeartbeatRevision <= 0 || request.ExtensionMilliseconds <= 0)
            return ActivationFailure<BaseExecutorHeartbeatResult>("base.activation.invalid", OperationStatus.ValidationFailed, ErrorCategory.Validation);
        await _stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            InMemoryStoreState current = Volatile.Read(ref _publishedState);
            if (TryReadControlReceipt(current, request.Identity, "executor-heartbeat",
                HPDBaseJsonSerializerContext.Default.BaseExecutorHeartbeatResult,
                static value => value with { Disposition = BaseMutationRequestDisposition.Duplicate }, out OperationResult<BaseExecutorHeartbeatResult>? replay))
                return replay;
            string key = ExecutorKey(request.Executor.ApplicationId, request.Executor.HostId, request.Executor.ProcessIncarnationId);
            if (!current.Executors.TryGetValue(key, out InMemoryExecutorRow? row) || row.Retired ||
                !ExecutorMatches(row.Authority, request.Executor) || row.Heartbeat.HeartbeatRevision != request.ExpectedHeartbeatRevision ||
                row.Heartbeat.HeartbeatExpiresAt < request.AcceptedTime.CapturedUtc)
                return ActivationFailure<BaseExecutorHeartbeatResult>("base.activation.executorLost", OperationStatus.Conflict, ErrorCategory.Conflict);
            var next = current.Clone();
            long revision = checked(row.Heartbeat.HeartbeatRevision + 1);
            BaseExecutorHeartbeatObservation heartbeat = Heartbeat(row.Authority, revision,
                checked(request.AcceptedTime.CapturedUtc + request.ExtensionMilliseconds));
            next.Executors[key] = next.Executors[key] with { Heartbeat = heartbeat };
            var result = new BaseExecutorHeartbeatResult
            {
                Executor = row.Authority, Heartbeat = heartbeat, Accounting = EmptyActivationAccounting with { IndexOperations = 1 },
                Disposition = BaseMutationRequestDisposition.Committed,
            };
            WriteControlReceipt(next, request.Identity, "executor-heartbeat", result, HPDBaseJsonSerializerContext.Default.BaseExecutorHeartbeatResult);
            Volatile.Write(ref _publishedState, next);
            return OperationResults.Ok(result);
        }
        finally { _stateGate.Release(); }
    }

    /// <inheritdoc />
    public async ValueTask<OperationResult<BaseExecutorRetirementResult>> RetireExecutorAsync(
        BaseExecutorRetirementRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!AcceptActivationTime(request.AcceptedTime))
            return ActivationFailure<BaseExecutorRetirementResult>("base.activation.clockInvalid", OperationStatus.ValidationFailed, ErrorCategory.Validation);
        await _stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            InMemoryStoreState current = Volatile.Read(ref _publishedState);
            if (TryReadControlReceipt(current, request.Identity, "executor-retired",
                HPDBaseJsonSerializerContext.Default.BaseExecutorRetirementResult,
                static value => value with { Disposition = BaseMutationRequestDisposition.Duplicate }, out OperationResult<BaseExecutorRetirementResult>? replay))
                return replay;
            string key = ExecutorKey(request.Executor.ApplicationId, request.Executor.HostId, request.Executor.ProcessIncarnationId);
            if (!current.Executors.TryGetValue(key, out InMemoryExecutorRow? row) || row.Retired ||
                !ExecutorMatches(row.Authority, request.Executor) || row.Heartbeat.HeartbeatRevision != request.ExpectedHeartbeatRevision)
                return ActivationFailure<BaseExecutorRetirementResult>("base.activation.executorLost", OperationStatus.Conflict, ErrorCategory.Conflict);
            var next = current.Clone();
            next.Executors[key] = next.Executors[key] with { Retired = true };
            byte[] checksum = Hash($"base.activation.executor.retired.v2\0{Convert.ToHexString(row.Authority.Checksum.AsSpan())}\n{row.Heartbeat.HeartbeatRevision}");
            var result = new BaseExecutorRetirementResult
            {
                Executor = row.Authority, HeartbeatRevision = row.Heartbeat.HeartbeatRevision,
                RetirementChecksum = checksum.ToImmutableArray(), Accounting = EmptyActivationAccounting with { IndexOperations = 1 },
                Disposition = BaseMutationRequestDisposition.Committed,
            };
            WriteControlReceipt(next, request.Identity, "executor-retired", result, HPDBaseJsonSerializerContext.Default.BaseExecutorRetirementResult);
            Volatile.Write(ref _publishedState, next);
            return OperationResults.Ok(result);
        }
        finally { _stateGate.Release(); }
    }

    /// <inheritdoc />
    public ValueTask<OperationResult<BaseScheduleAuthority>> ReadScheduleAsync(
        string scheduleId, int scheduleVersion, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        InMemoryStoreState state = Volatile.Read(ref _publishedState);
        return ValueTask.FromResult(state.Schedules.TryGetValue(ScheduleKey(scheduleId, scheduleVersion), out BaseScheduleAuthority? value)
            ? OperationResults.Ok(value with { Definition = BaseScheduleDefinitionBuilder.Create(value.Definition), Checksum = value.Checksum.ToArray().ToImmutableArray() })
            : ActivationFailure<BaseScheduleAuthority>("base.activation.scheduleNotFound", OperationStatus.NotFound, ErrorCategory.NotFound));
    }

    /// <inheritdoc />
    public async ValueTask<OperationResult<BaseScheduleMutationResult>> MutateScheduleAsync(
        BaseScheduleMutationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!AcceptActivationTime(request.AcceptedTime))
            return ActivationFailure<BaseScheduleMutationResult>("base.activation.clockInvalid", OperationStatus.ValidationFailed, ErrorCategory.Validation);
        BaseScheduleDefinition definition;
        try { definition = BaseScheduleDefinitionBuilder.Create(request.Definition); }
        catch { return ActivationFailure<BaseScheduleMutationResult>("base.activation.scheduleInvalid", OperationStatus.ValidationFailed, ErrorCategory.Validation); }
        await _stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            InMemoryStoreState current = Volatile.Read(ref _publishedState); var next = current.Clone();
            if (TryReadControlReceipt(current, request.Identity, "schedule-mutated",
                HPDBaseJsonSerializerContext.Default.BaseScheduleMutationResult,
                static value => value with { Disposition = BaseMutationRequestDisposition.Duplicate }, out OperationResult<BaseScheduleMutationResult>? replay))
                return replay;
            string key = ScheduleKey(definition.Id, definition.Version);
            current.Schedules.TryGetValue(key, out BaseScheduleAuthority? existing);
            if (request.Kind == BaseScheduleMutationKind.Create && existing is not null ||
                request.Kind != BaseScheduleMutationKind.Create && (existing is null || existing.DefinitionGeneration != request.ExpectedDefinitionGeneration))
                return ActivationFailure<BaseScheduleMutationResult>("base.activation.scheduleConflict", OperationStatus.Conflict, ErrorCategory.Conflict);
            if (request.Kind == BaseScheduleMutationKind.Remove)
            {
                next.Schedules.Remove(key);
                var removed = new BaseScheduleMutationResult { Authority = null, Accounting = EmptyActivationAccounting with { IndexOperations = 1 }, Disposition = BaseMutationRequestDisposition.Committed };
                WriteControlReceipt(next, request.Identity, "schedule-mutated", removed, HPDBaseJsonSerializerContext.Default.BaseScheduleMutationResult);
                Volatile.Write(ref _publishedState, next);
                return OperationResults.Ok(removed);
            }
            long generation = existing is null ? 1 : checked(existing.DefinitionGeneration + 1);
            long epoch = existing is null ? 1 : request.Kind == BaseScheduleMutationKind.Update ? checked(existing.ScheduleEpoch + 1) : existing.ScheduleEpoch;
            bool enabled = request.Kind switch { BaseScheduleMutationKind.Disable => false, BaseScheduleMutationKind.Enable => true, _ => existing?.Enabled ?? true };
            long? last = request.Kind == BaseScheduleMutationKind.Update ? null : existing?.LastConsideredNominal;
            long? following = request.Kind == BaseScheduleMutationKind.Update || existing is null
                ? request.InitialNextNominal
                : existing.NextNominal;
            BaseScheduleAuthority authority = ScheduleAuthority(definition, generation, enabled, epoch, last, following);
            next.Schedules[key] = authority;
            var result = new BaseScheduleMutationResult { Authority = authority, Accounting = EmptyActivationAccounting with { IndexOperations = 1 }, Disposition = BaseMutationRequestDisposition.Committed };
            WriteControlReceipt(next, request.Identity, "schedule-mutated", result, HPDBaseJsonSerializerContext.Default.BaseScheduleMutationResult);
            Volatile.Write(ref _publishedState, next);
            return OperationResults.Ok(result);
        }
        finally { _stateGate.Release(); }
    }

    /// <inheritdoc />
    public async ValueTask<OperationResult<BaseScheduleMaintenancePage>> AdvanceSchedulesAsync(
        BaseScheduleMaintenanceRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!AcceptActivationTime(request.AcceptedTime))
            return ActivationFailure<BaseScheduleMaintenancePage>("base.activation.clockInvalid", OperationStatus.ValidationFailed, ErrorCategory.Validation);
        if (request.Occurrences.Length is < 1 or > 256)
            return ActivationFailure<BaseScheduleMaintenancePage>("base.activation.budgetExceeded", OperationStatus.ValidationFailed, ErrorCategory.Validation);
        await _stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            InMemoryStoreState current = Volatile.Read(ref _publishedState); var next = current.Clone();
            if (TryReadControlReceipt(current, request.Identity, "occurrence-page",
                HPDBaseJsonSerializerContext.Default.BaseScheduleMaintenancePage,
                static value => value with { Disposition = BaseMutationRequestDisposition.Duplicate }, out OperationResult<BaseScheduleMaintenancePage>? replay))
                return replay;
            string key = ScheduleKey(request.ScheduleId, request.ScheduleVersion);
            if (!current.Schedules.TryGetValue(key, out BaseScheduleAuthority? authority) || !authority.Enabled ||
                !CryptographicOperations.FixedTimeEquals(authority.Checksum.AsSpan(), request.ExpectedAuthorityChecksum.AsSpan()))
                return ActivationFailure<BaseScheduleMaintenancePage>("base.activation.scheduleConflict", OperationStatus.Conflict, ErrorCategory.Conflict);
            long previous = authority.LastConsideredNominal ?? -1;
            var committedFacts = ImmutableArray.CreateBuilder<BaseScheduleOccurrenceFact>(request.Occurrences.Length);
            var cancellations = ImmutableArray.CreateBuilder<BaseScheduleCancellationAuthority>();
            foreach (BaseScheduleOccurrenceProposal proposal in request.Occurrences)
            {
                BaseScheduleOccurrenceProposal effectiveProposal;
                try { effectiveProposal = ResolveOverlap(next, proposal); }
                catch (InvalidOperationException)
                { return ActivationFailure<BaseScheduleMaintenancePage>("base.activation.budgetExceeded", OperationStatus.ValidationFailed, ErrorCategory.Validation); }
                BaseScheduleOccurrenceFact fact = effectiveProposal.Fact;
                if (fact.ScheduleId != authority.Definition.Id || fact.ScheduleEpoch != authority.ScheduleEpoch || fact.NominalAt <= previous ||
                    next.ScheduleOccurrences.ContainsKey(fact.OccurrenceId) || !OccurrenceShapeValid(effectiveProposal) ||
                    !CryptographicOperations.FixedTimeEquals(fact.Checksum.AsSpan(), OccurrenceChecksum(fact)))
                    return ActivationFailure<BaseScheduleMaintenancePage>("base.activation.occurrenceInvalid", OperationStatus.ValidationFailed, ErrorCategory.Validation);
                previous = fact.NominalAt;
                next.ScheduleOccurrences.Add(fact.OccurrenceId, fact);
                committedFacts.Add(fact);
                if (effectiveProposal.Activation is { } activation)
                {
                    string activationId = ((BaseOccurrenceMaterialized)fact.Disposition).ActivationId;
                    InMemoryActivationRow[] cancellationBlockers = activation.OverlapPolicy == BaseScheduleOverlapPolicy.CancelPrevious
                        ? ActiveOverlapRows(next, activation.OverlapKey).ToArray()
                        : [];
                    byte[] fingerprint = ScheduleActivationFingerprint(activation, fact.OccurrenceId);
                    if (next.Activations.TryGetValue(activationId, out InMemoryActivationRow? existing) && !CryptographicOperations.FixedTimeEquals(existing.Fingerprint, fingerprint))
                        return ActivationFailure<BaseScheduleMaintenancePage>("base.activation.fingerprintConflict", OperationStatus.Conflict, ErrorCategory.Conflict);
                    if (existing is null)
                    {
                        if (!TryReserveYieldReceiptSlots(next, activation.MaximumYields))
                            return ActivationFailure<BaseScheduleMaintenancePage>("base.activation.capacityUnavailable", OperationStatus.CapabilityUnavailable, ErrorCategory.Capability);
                        byte[] payloadChecksum = SHA256.HashData(activation.CanonicalInput.AsSpan());
                        var payload = new BaseActivationPayload { ActivationId = activationId, Definition = activation.Definition,
                            ReceiptRetention = activation.ReceiptRetention with { },
                            CanonicalInput = activation.CanonicalInput, InputChecksum = activation.InputChecksum, Scope = activation.Scope,
                            OccurrenceId = activation.OccurrenceId, RequestedDueAt = activation.RequestedDueAt,
                            EffectiveDueAt = activation.EffectiveDueAt ?? activation.RequestedDueAt,
                            Checksum = payloadChecksum.ToImmutableArray() };
                        next.Activations.Add(activationId, new InMemoryActivationRow(payload, BaseActivationState.Pending, 1,
                            activation.RequestedDueAt, activation.EffectiveDueAt ?? activation.RequestedDueAt, fingerprint,
                            ControlChecksum(activationId, 1, BaseActivationState.Pending,
                                activation.EffectiveDueAt ?? activation.RequestedDueAt, 0,
                                activation.MaximumYields, 0, null, null, null, null), activation.OccurrenceId,
                            activation.Priority, activation.OverlapKey.IsDefaultOrEmpty ? null : activation.OverlapKey.ToArray(),
                            activation.OverlapPolicy, cancellationBlockers.Length == 0,
                            MaximumYields: activation.MaximumYields));
                        IndexActivation(next, payload);
                        next.ActivationIndexGeneration = checked(next.ActivationIndexGeneration + 1);
                    }
                    if (cancellationBlockers.Length != 0)
                    {
                        InMemoryActivationRow high = cancellationBlockers[^1];
                        string maintenanceId = Convert.ToHexStringLower(Hash(
                            $"base.activation.schedule.cancelPrevious.v2\0{fact.OccurrenceId}\n{activationId}"));
                        next.ScheduleCancellations[maintenanceId] = new InMemoryScheduleCancellationRow(
                            maintenanceId, activationId, activation.OverlapKey.ToArray(),
                            new BaseScheduleCancellationBoundary
                            { EffectiveDueAt = high.EffectiveDueAt, ActivationId = high.Payload.ActivationId }, null, false);
                        cancellations.Add(new BaseScheduleCancellationAuthority
                        {
                            MaintenanceId = maintenanceId, ReplacementActivationId = activationId,
                            OverlapKey = activation.OverlapKey.ToArray().ToImmutableArray(),
                            HighWater = new BaseScheduleCancellationBoundary
                            { EffectiveDueAt = high.EffectiveDueAt, ActivationId = high.Payload.ActivationId },
                        });
                    }
                }
            }
            if (previous != request.ResultingLastConsideredNominal)
                return ActivationFailure<BaseScheduleMaintenancePage>("base.activation.occurrenceInvalid", OperationStatus.ValidationFailed, ErrorCategory.Validation);
            BaseScheduleAuthority replacement = ScheduleAuthority(authority.Definition, authority.DefinitionGeneration, authority.Enabled,
                authority.ScheduleEpoch, request.ResultingLastConsideredNominal, request.ResultingNextNominal);
            next.Schedules[key] = replacement;
            var result = new BaseScheduleMaintenancePage
            {
                Authority = replacement, Occurrences = committedFacts.MoveToImmutable(), Cancellations = cancellations.ToImmutable(),
                Accounting = EmptyActivationAccounting with { Candidates = request.Occurrences.Length, IndexOperations = request.Occurrences.Length * 2 },
                Disposition = BaseMutationRequestDisposition.Committed,
            };
            WriteControlReceipt(next, request.Identity, "occurrence-page", result, HPDBaseJsonSerializerContext.Default.BaseScheduleMaintenancePage);
            if (!ActivationRowCapacityAllows(next))
                return ActivationFailure<BaseScheduleMaintenancePage>("base.activation.capacityUnavailable", OperationStatus.CapabilityUnavailable, ErrorCategory.Capability);
            Volatile.Write(ref _publishedState, next);
            return OperationResults.Ok(result);
        }
        finally { _stateGate.Release(); }
    }

    private static BaseScheduleOccurrenceProposal ResolveOverlap(InMemoryStoreState state, BaseScheduleOccurrenceProposal proposal)
    {
        if (proposal.Activation is not { } activation || activation.OverlapKey.IsDefaultOrEmpty ||
            activation.OverlapPolicy is BaseScheduleOverlapPolicy.Allow or BaseScheduleOverlapPolicy.Queue)
            return proposal;
        InMemoryActivationRow[] blockers = ActiveOverlapRows(state, activation.OverlapKey).ToArray();
        if (activation.OverlapPolicy == BaseScheduleOverlapPolicy.SkipWhileActive && blockers.Length != 0)
        {
            BaseScheduleOccurrenceFact skipped = proposal.Fact with
            { Disposition = new BaseOccurrenceSkippedOverlap(blockers[0].Payload.ActivationId), Checksum = [] };
            skipped = skipped with { Checksum = OccurrenceChecksum(skipped).ToImmutableArray() };
            return new BaseScheduleOccurrenceProposal { Fact = skipped };
        }
        return proposal;
    }

    private static IOrderedEnumerable<InMemoryActivationRow> ActiveOverlapRows(
        InMemoryStoreState state, ImmutableArray<byte> overlapKey) => state.Activations.Values.Where(row =>
            row.OverlapKey is not null && CryptographicOperations.FixedTimeEquals(row.OverlapKey, overlapKey.AsSpan()) &&
            row.State is BaseActivationState.Pending or BaseActivationState.RetryPending or BaseActivationState.YieldPending or BaseActivationState.Claimed or BaseActivationState.EffectStarted)
        .OrderBy(static row => row.EffectiveDueAt).ThenBy(static row => row.Payload.ActivationId, StringComparer.Ordinal);

    /// <inheritdoc />
    public async ValueTask<OperationResult<BaseScheduleCancellationMaintenancePage>> AdvanceScheduleCancellationAsync(
        BaseScheduleCancellationMaintenanceRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!AcceptActivationTime(request.AcceptedTime) || request.OverlapKey.Length != 32 ||
            request.HighWater.ActivationId.Length == 0 || request.Limits.MaximumCandidates is < 1 or > 256)
            return ActivationFailure<BaseScheduleCancellationMaintenancePage>("base.activation.invalid", OperationStatus.ValidationFailed, ErrorCategory.Validation);
        await _stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            InMemoryStoreState current = Volatile.Read(ref _publishedState);
            if (TryReadControlReceipt(current, request.Identity, "cancellation-maintenance",
                HPDBaseJsonSerializerContext.Default.BaseScheduleCancellationMaintenancePage,
                static value => value with { Disposition = BaseMutationRequestDisposition.Duplicate }, out OperationResult<BaseScheduleCancellationMaintenancePage>? replay))
                return replay;
            if (!current.ScheduleCancellations.TryGetValue(request.MaintenanceId, out InMemoryScheduleCancellationRow? stored) ||
                stored.ReplacementActivationId != request.ReplacementActivationId || stored.Completed ||
                !CryptographicOperations.FixedTimeEquals(stored.OverlapKey, request.OverlapKey.AsSpan()) ||
                !BoundaryEquals(stored.HighWater, request.HighWater) || !BoundaryEquals(stored.After, request.After))
                return ActivationFailure<BaseScheduleCancellationMaintenancePage>("base.activation.maintenanceConflict", OperationStatus.Conflict, ErrorCategory.Conflict);
            var next = current.Clone();
            InMemoryActivationRow[] page = ActiveOverlapRows(next, request.OverlapKey)
                .Where(row => row.Payload.ActivationId != request.ReplacementActivationId &&
                    BoundaryAfter(row, request.After) && BoundaryAtOrBefore(row, request.HighWater))
                .Take(Math.Min(256, request.Limits.MaximumCandidates)).ToArray();
            foreach (InMemoryActivationRow blocker in page)
            {
                ApplyYieldReceiptReservationTransition(next, blocker, BaseActivationState.Cancelled, null);
                long generation = checked(blocker.Generation + 1);
                next.Activations[blocker.Payload.ActivationId] = blocker with
                {
                    State = BaseActivationState.Cancelled, Generation = generation, Claim = null, Lease = null, Eligible = false,
                    ControlChecksum = ControlChecksum(blocker.Payload.ActivationId, generation,
                        BaseActivationState.Cancelled, blocker.EffectiveDueAt, blocker.YieldCount,
                        blocker.MaximumYields, blocker.ExecutionSliceOrdinal, blocker.AttemptStartedAt,
                        blocker.SliceStartedAt, null, null),
                };
            }
            BaseScheduleCancellationBoundary? boundary = page.Length == 0 ? request.After : new()
            { EffectiveDueAt = page[^1].EffectiveDueAt, ActivationId = page[^1].Payload.ActivationId };
            bool completed = !ActiveOverlapRows(next, request.OverlapKey).Any(row =>
                row.Payload.ActivationId != request.ReplacementActivationId && BoundaryAfter(row, boundary) && BoundaryAtOrBefore(row, request.HighWater));
            if (completed)
            {
                if (!next.Activations.TryGetValue(request.ReplacementActivationId, out InMemoryActivationRow? replacement) || replacement.State != BaseActivationState.Pending)
                    return ActivationFailure<BaseScheduleCancellationMaintenancePage>("base.activation.maintenanceConflict", OperationStatus.Conflict, ErrorCategory.Conflict);
                next.Activations[request.ReplacementActivationId] = replacement with { Eligible = true };
                next.ScheduleCancellations[request.MaintenanceId] = stored with { After = boundary, Completed = true };
            }
            else next.ScheduleCancellations[request.MaintenanceId] = stored with { After = boundary };
            next.ActivationIndexGeneration = checked(next.ActivationIndexGeneration + 1);
            var result = new BaseScheduleCancellationMaintenancePage
            {
                MaintenanceId = request.MaintenanceId, CancelledCount = page.Length, Next = completed ? null : boundary,
                Completed = completed, Accounting = EmptyActivationAccounting with
                { Candidates = page.Length, Comparisons = page.Length, IndexOperations = page.Length + 1 },
                Disposition = BaseMutationRequestDisposition.Committed,
            };
            WriteControlReceipt(next, request.Identity, "cancellation-maintenance", result,
                HPDBaseJsonSerializerContext.Default.BaseScheduleCancellationMaintenancePage);
            if (!ActivationRowCapacityAllows(next))
                return ActivationFailure<BaseScheduleCancellationMaintenancePage>("base.activation.capacityUnavailable", OperationStatus.CapabilityUnavailable, ErrorCategory.Capability);
            Volatile.Write(ref _publishedState, next);
            return OperationResults.Ok(result);
        }
        finally { _stateGate.Release(); }
    }

    private static bool BoundaryEquals(BaseScheduleCancellationBoundary? left, BaseScheduleCancellationBoundary? right) =>
        left is null ? right is null : right is not null && left.EffectiveDueAt == right.EffectiveDueAt && left.ActivationId == right.ActivationId;
    private static bool BoundaryAfter(InMemoryActivationRow row, BaseScheduleCancellationBoundary? boundary) => boundary is null ||
        row.EffectiveDueAt > boundary.EffectiveDueAt || row.EffectiveDueAt == boundary.EffectiveDueAt &&
        string.CompareOrdinal(row.Payload.ActivationId, boundary.ActivationId) > 0;
    private static bool BoundaryAtOrBefore(InMemoryActivationRow row, BaseScheduleCancellationBoundary boundary) =>
        row.EffectiveDueAt < boundary.EffectiveDueAt || row.EffectiveDueAt == boundary.EffectiveDueAt &&
        string.CompareOrdinal(row.Payload.ActivationId, boundary.ActivationId) <= 0;

    /// <inheritdoc />
    public async ValueTask<OperationResult<BaseActivationAdministrationPage>> ReadAdministrationAsync(
        BaseActivationAdministrationQueryRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!AcceptActivationTime(request.AcceptedTime) || !ValidateLimits(request.Limits)
            || request.Take is < 1 or > 256 || !Enum.IsDefined(request.States)
            || request.Scope.ProtectedIndexDigest.Length != SHA256.HashSizeInBytes)
            return ActivationFailure<BaseActivationAdministrationPage>(
                "base.activation.invalid", OperationStatus.ValidationFailed, ErrorCategory.Validation);
        await _stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            InMemoryStoreState state = Volatile.Read(ref _publishedState);
            string scopeKey = Convert.ToHexString(request.Scope.ProtectedIndexDigest.AsSpan());
            IEnumerable<InMemoryActivationRow> rows = state.ActivationsByProtectedScope.TryGetValue(
                    scopeKey, out SortedSet<string>? activationIds)
                ? activationIds.Select(id => state.Activations[id])
                : [];
            rows = rows.Where(row => ScopeMatches(row.Payload.Scope, request.Scope))
                .Where(row => request.Definition is null ||
                    row.Payload.Definition.Id == request.Definition.Id &&
                    row.Payload.Definition.Version == request.Definition.Version &&
                    CryptographicOperations.FixedTimeEquals(
                        row.Payload.Definition.Checksum.AsSpan(), request.Definition.Checksum.AsSpan()))
                .Where(row => StateSelected(row.State, request.States))
                .OrderBy(static row => row.Payload.Definition.Id, StringComparer.Ordinal)
                .ThenBy(static row => row.Payload.Definition.Version)
                .ThenBy(static row => row.EffectiveDueAt)
                .ThenBy(static row => row.Payload.ActivationId, StringComparer.Ordinal)
                .Where(row => request.After is null || AdministrationAfter(row, request.After));
            InMemoryActivationRow[] selected = rows.Take(request.Take + 1).ToArray();
            bool hasMore = selected.Length > request.Take;
            if (hasMore) selected = selected[..request.Take];
            BaseActivationAdministrationItem[] items = selected.Select(static row => new BaseActivationAdministrationItem
            {
                ActivationId = row.Payload.ActivationId,
                Definition = row.Payload.Definition with { Checksum = row.Payload.Definition.Checksum.ToArray().ToImmutableArray() },
                State = row.State,
                Generation = row.Generation,
                EffectiveDueAt = row.EffectiveDueAt,
                OccurrenceId = row.OccurrenceId,
                AttemptNumber = row.AttemptNumber,
                ExecutionSliceOrdinal = row.ExecutionSliceOrdinal,
                AttemptStartedAt = row.AttemptStartedAt,
                SliceStartedAt = row.SliceStartedAt,
                YieldCount = row.YieldCount,
                MaximumYields = row.MaximumYields,
                TerminalYieldDisposition = row.YieldTerminalDisposition,
                TerminalYieldFailureCode = row.YieldTerminalFailureCode,
                ResultRetained = row.CanonicalResult is not null,
                EffectAuthorityRetained = row.Effect is not null,
                ControlChecksum = row.ControlChecksum.ToImmutableArray(),
            }).ToArray();
            BaseActivationAdministrationBoundary? next = hasMore && selected.Length != 0
                ? AdministrationBoundary(selected[^1]) : null;
            BaseAtomicReadIntervalEvidence interval = AdministrationInterval(request, next);
            long evidenceBytes = checked(IntervalBytes(interval) + items.Sum(static item =>
                Encoding.UTF8.GetByteCount(item.ActivationId) + Encoding.UTF8.GetByteCount(item.Definition.Id)
                + item.Definition.Checksum.Length + item.ControlChecksum.Length
                + (item.TerminalYieldFailureCode is null ? 0 : Encoding.UTF8.GetByteCount(item.TerminalYieldFailureCode)) + 80L));
            if (items.Length > request.Limits.MaximumCandidates
                || evidenceBytes > request.Limits.MaximumEvidenceBytes
                || evidenceBytes > request.Limits.MaximumTransientBytes)
                return ActivationFailure<BaseActivationAdministrationPage>(
                    "base.activation.budgetExceeded", OperationStatus.ValidationFailed, ErrorCategory.Validation);
            return OperationResults.Ok(new BaseActivationAdministrationPage
            {
                Items = items.ToImmutableArray(), Next = next,
                CapturedIndexGeneration = state.ActivationIndexGeneration,
                Intervals = [interval],
                Accounting = EmptyActivationAccounting with
                {
                    Candidates = items.Length,
                    Comparisons = items.Length,
                    ReadIntervals = 1,
                    EvidenceBytes = evidenceBytes,
                    TransientBytes = evidenceBytes,
                },
            });
        }
        finally { _stateGate.Release(); }
    }

    private static bool StateSelected(BaseActivationState state, BaseActivationStateSelector selector) => selector switch
    {
        BaseActivationStateSelector.All => true,
        BaseActivationStateSelector.Runnable => state is BaseActivationState.Pending or BaseActivationState.RetryPending or BaseActivationState.YieldPending,
        BaseActivationStateSelector.Active => state is BaseActivationState.Claimed or BaseActivationState.EffectStarted,
        BaseActivationStateSelector.Terminal => state is BaseActivationState.Succeeded or BaseActivationState.Exhausted
            or BaseActivationState.Cancelled or BaseActivationState.Disposed or BaseActivationState.Migrated,
        BaseActivationStateSelector.OutcomeUnknown => state == BaseActivationState.OutcomeUnknown,
        _ => false,
    };

    private static bool AdministrationAfter(InMemoryActivationRow row, BaseActivationAdministrationBoundary after) =>
        string.CompareOrdinal(row.Payload.Definition.Id, after.DefinitionId) is > 0
        || row.Payload.Definition.Id == after.DefinitionId &&
        (row.Payload.Definition.Version > after.DefinitionVersion
        || row.Payload.Definition.Version == after.DefinitionVersion &&
        (row.EffectiveDueAt > after.EffectiveDueAt
        || row.EffectiveDueAt == after.EffectiveDueAt &&
        string.CompareOrdinal(row.Payload.ActivationId, after.ActivationId) > 0));

    private static BaseActivationAdministrationBoundary AdministrationBoundary(InMemoryActivationRow row) => new()
    {
        DefinitionId = row.Payload.Definition.Id,
        DefinitionVersion = row.Payload.Definition.Version,
        EffectiveDueAt = row.EffectiveDueAt,
        ActivationId = row.Payload.ActivationId,
    };

    private static BaseAtomicReadIntervalEvidence AdministrationInterval(
        BaseActivationAdministrationQueryRequest request,
        BaseActivationAdministrationBoundary? next) => new()
    {
        LogicalAccessPathId = "base.activation.administration.byScopeDefinitionStateDue.v1",
        CanonicalLowerBound = Encoding.UTF8.GetBytes(
            $"{Convert.ToHexString(request.Scope.ProtectedIndexDigest.AsSpan())}\n{request.After?.DefinitionId ?? string.Empty}\n{request.After?.DefinitionVersion ?? 0}\n{request.After?.EffectiveDueAt ?? -1}\n{request.After?.ActivationId ?? string.Empty}").ToImmutableArray(),
        LowerInclusive = false,
        CanonicalUpperBound = Encoding.UTF8.GetBytes(
            $"{Convert.ToHexString(request.Scope.ProtectedIndexDigest.AsSpan())}\n{request.Definition?.Id ?? string.Empty}\n{request.Definition?.Version ?? 0}\n{(int)request.States}\n{next?.ActivationId ?? string.Empty}").ToImmutableArray(),
        UpperInclusive = true,
    };

    /// <inheritdoc />
    public async ValueTask<OperationResult<BaseActivationReceiptResolution>> ResolveReceiptAsync(
        BaseActivationReceiptResolutionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!AcceptActivationTime(request.AcceptedTime) || !ValidateLimits(request.Limits))
            return ActivationFailure<BaseActivationReceiptResolution>(
                "base.activation.invalid", OperationStatus.ValidationFailed, ErrorCategory.Validation);
        await _stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            InMemoryStoreState state = Volatile.Read(ref _publishedState);
            string receiptKey = ActivationReceiptKey(request.Identity);
            InMemoryActivationInstanceReceiptRow? instanceReceipt = state.ActivationInstanceReceipts.GetValueOrDefault(receiptKey);
            InMemoryActivationControlReceiptRow? controlReceipt = state.ActivationControlReceipts.GetValueOrDefault(receiptKey);
            if ((instanceReceipt is null) == (controlReceipt is null))
                return ActivationFailure<BaseActivationReceiptResolution>(
                    instanceReceipt is null ? "base.activation.receiptNotFound" : "base.activation.receiptCorrupt",
                    instanceReceipt is null ? OperationStatus.NotFound : OperationStatus.StoreError,
                    instanceReceipt is null ? ErrorCategory.NotFound : ErrorCategory.Store);
            string kind = instanceReceipt?.Kind ?? controlReceipt!.Kind;
            byte[] fingerprint = instanceReceipt?.Fingerprint ?? controlReceipt!.Fingerprint;
            byte[] storedResult = instanceReceipt?.Result ?? controlReceipt!.Result;
            if (instanceReceipt is not null && (request.AcceptedTime.CapturedUtc >= instanceReceipt.DuplicateResolveUntil
                || !InstanceReceiptValid(instanceReceipt, receiptKey)))
                return ActivationFailure<BaseActivationReceiptResolution>(
                    request.AcceptedTime.CapturedUtc >= instanceReceipt.DuplicateResolveUntil
                        ? "base.activation.receiptNotFound" : "base.activation.receiptCorrupt",
                    request.AcceptedTime.CapturedUtc >= instanceReceipt.DuplicateResolveUntil
                        ? OperationStatus.NotFound : OperationStatus.StoreError,
                    request.AcceptedTime.CapturedUtc >= instanceReceipt.DuplicateResolveUntil
                        ? ErrorCategory.NotFound : ErrorCategory.Store);
            if (controlReceipt is not null && !ControlReceiptValid(controlReceipt, receiptKey))
                return ActivationFailure<BaseActivationReceiptResolution>(
                    "base.activation.receiptCorrupt", OperationStatus.StoreError, ErrorCategory.Store);
            if (!CryptographicOperations.FixedTimeEquals(
                    fingerprint, request.Identity.Fingerprint.ToArray()))
                return ActivationFailure<BaseActivationReceiptResolution>(
                    "base.activation.fingerprintConflict", OperationStatus.Conflict, ErrorCategory.Conflict);
            byte[] bytes = storedResult.ToArray();
            if (kind == "activation-claimed")
            {
                BaseActivationClaimResult? stored = JsonSerializer.Deserialize(
                    bytes, HPDBaseJsonSerializerContext.Default.BaseActivationClaimResult);
                if (stored is null)
                    return ActivationFailure<BaseActivationReceiptResolution>(
                        "base.activation.receiptCorrupt", OperationStatus.StoreError, ErrorCategory.Store);
                OperationResult<BaseActivationClaimResult> resolved = ResolveClaimReplay(
                    state, OperationResults.Ok(stored), request.AcceptedTime.CapturedUtc);
                if (!resolved.IsSuccess() || resolved.Value is null)
                    return ActivationFailure<BaseActivationReceiptResolution>(
                        resolved.Error?.Code ?? "base.activation.receiptCorrupt", resolved.Status,
                        resolved.Error?.Category ?? ErrorCategory.Store);
                bytes = JsonSerializer.SerializeToUtf8Bytes(
                    resolved.Value, HPDBaseJsonSerializerContext.Default.BaseActivationClaimResult);
            }
            if (bytes.LongLength > request.Limits.MaximumResultBytes
                || bytes.LongLength > request.Limits.MaximumEvidenceBytes
                || bytes.LongLength > request.Limits.MaximumTransientBytes)
                return ActivationFailure<BaseActivationReceiptResolution>(
                    "base.activation.budgetExceeded", OperationStatus.ValidationFailed, ErrorCategory.Validation);
            return OperationResults.Ok(new BaseActivationReceiptResolution
            {
                OperationKind = new string(kind.AsSpan()),
                Fingerprint = fingerprint.ToArray().ToImmutableArray(),
                CanonicalResult = bytes.ToImmutableArray(),
                Accounting = EmptyActivationAccounting with
                {
                    Candidates = 1, Comparisons = 1,
                    EvidenceBytes = bytes.LongLength, TransientBytes = bytes.LongLength,
                },
            });
        }
        finally { _stateGate.Release(); }
    }

    /// <inheritdoc />
    public async ValueTask<OperationResult<BaseActivationReceiptCompactionResult>> CompactActivationReceiptsAsync(
        BaseActivationReceiptCompactionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.ApplicationId != _options.SemanticActivationApplicationId
            || request.AcceptedTime.ApplicationId != request.ApplicationId
            || request.Definition.Version < 1 || request.Definition.Checksum.Length != 32
            || request.Take is < 1 or > 256 || request.After is { ReceiptSequence: < 1 }
            || request.Take > request.Limits.MaximumCandidates
            || request.ExpectedReservation is null || !BaseActivationYieldReservationContract.IsValid(request.ExpectedReservation)
            || !Enum.IsDefined(request.BackupFloor.Kind)
            || request.BackupFloor.Kind == BaseActivationReceiptBackupFloorKind.NotApplicable
                && (request.BackupFloor.Checkpoint is not null
                    || request.ReceiptRetention.ProtectedBackupCoverage != BaseActivationProtectedBackupCoverage.NotRequired)
            || request.BackupFloor.Kind == BaseActivationReceiptBackupFloorKind.Checkpoint
                && request.BackupFloor.Checkpoint is null
            || !ValidateLimits(request.Limits) || !AcceptActivationTime(request.AcceptedTime))
            return ActivationFailure<BaseActivationReceiptCompactionResult>(
                "base.activation.invalid", OperationStatus.ValidationFailed, ErrorCategory.Validation);
        if (request.BackupFloor.Kind == BaseActivationReceiptBackupFloorKind.Checkpoint)
            return ActivationFailure<BaseActivationReceiptCompactionResult>(
                "base.activation.capabilityUnavailable", OperationStatus.CapabilityUnavailable, ErrorCategory.Capability);
        await _stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            InMemoryStoreState current = Volatile.Read(ref _publishedState);
            if (TryReadControlReceipt(current, request.Identity, "activation-receipts-compacted",
                HPDBaseJsonSerializerContext.Default.BaseActivationReceiptCompactionResult,
                static value => value with { Disposition = BaseMutationRequestDisposition.Duplicate },
                out OperationResult<BaseActivationReceiptCompactionResult> replay)) return replay;
            BaseActivationYieldReservationState priorReservation = BaseActivationYieldReservationContract.Create(
                current.ActivationYieldReservationGeneration, MaximumYieldReceiptSlots,
                current.ActivationYieldReservedUnusedSlots, current.ActivationYieldRetainedUsedSlots);
            if (!ReservationMatches(request.ExpectedReservation, priorReservation))
                return ActivationFailure<BaseActivationReceiptCompactionResult>(
                    "base.activation.maintenanceConflict", OperationStatus.Conflict, ErrorCategory.Conflict);
            BaseActivationInstanceReceiptChainState priorChain = current.ActivationInstanceReceiptChain;
            var candidates = current.ActivationInstanceReceipts
                .Where(pair => pair.Value.Kind == "activation-yielded-v1"
                    && DefinitionMatches(pair.Value.Definition, request.Definition)
                    && pair.Value.Retention == request.ReceiptRetention
                    && current.Activations.TryGetValue(pair.Value.ActivationId, out InMemoryActivationRow? row)
                    && ScopeMatches(row.Payload.Scope, request.Scope))
                .OrderBy(static pair => pair.Value.ActivationId, StringComparer.Ordinal)
                .ThenBy(static pair => pair.Value.ReceiptSequence)
                .Where(pair => request.After is null
                    || string.CompareOrdinal(pair.Value.ActivationId, request.After.ActivationId) > 0
                    || pair.Value.ActivationId == request.After.ActivationId
                        && pair.Value.ReceiptSequence > request.After.ReceiptSequence)
                .Take(request.Take).ToArray();
            KeyValuePair<string, InMemoryActivationInstanceReceiptRow>[] examined = candidates;
            bool hasMore = examined.Length == request.Take && current.ActivationInstanceReceipts
                .Where(pair => pair.Value.Kind == "activation-yielded-v1"
                    && DefinitionMatches(pair.Value.Definition, request.Definition)
                    && pair.Value.Retention == request.ReceiptRetention
                    && current.Activations.TryGetValue(pair.Value.ActivationId, out InMemoryActivationRow? row)
                    && ScopeMatches(row.Payload.Scope, request.Scope))
                .OrderBy(static pair => pair.Value.ActivationId, StringComparer.Ordinal)
                .ThenBy(static pair => pair.Value.ReceiptSequence)
                .Any(pair => string.CompareOrdinal(pair.Value.ActivationId, examined[^1].Value.ActivationId) > 0
                    || pair.Value.ActivationId == examined[^1].Value.ActivationId
                        && pair.Value.ReceiptSequence > examined[^1].Value.ReceiptSequence);
            var deleted = new List<KeyValuePair<string, InMemoryActivationInstanceReceiptRow>>();
            foreach (KeyValuePair<string, InMemoryActivationInstanceReceiptRow> candidate in examined)
            {
                InMemoryActivationInstanceReceiptRow receipt = candidate.Value;
                if (receipt.DuplicateResolveUntil > request.AcceptedTime.CapturedUtc
                    || !current.Activations.TryGetValue(receipt.ActivationId, out InMemoryActivationRow? row)) continue;
                BaseActivationYieldReceipt? yielded = JsonSerializer.Deserialize(
                    receipt.Result, HPDBaseJsonSerializerContext.Default.BaseActivationYieldReceipt);
                if (yielded is null || row.ExecutionSliceOrdinal <= yielded.ExecutionSliceOrdinal
                    || row.State == BaseActivationState.YieldPending
                        && row.Generation == yielded.ResultingGeneration
                        && row.YieldCount == yielded.ResultingYieldCount) continue;
                deleted.Add(candidate);
            }
            string compactionReceiptKey = ActivationReceiptKey(request.Identity);
            var next = current.Clone();
            foreach (KeyValuePair<string, InMemoryActivationInstanceReceiptRow> item in deleted)
            {
                InMemoryActivationInstanceReceiptRow receipt = item.Value;
                BaseActivationCompactedReceiptFact fact = BaseActivationCompactedReceiptFactContract.Create(
                    receipt.ReceiptSequence, item.Key, receipt.AuthorityChecksum,
                    receipt.PriorOrderedChecksum, receipt.OrderedChecksum, compactionReceiptKey);
                next.ActivationInstanceReceiptCompactionFacts.Add(fact.ReceiptSequence, fact);
                next.ActivationInstanceReceipts.Remove(item.Key);
            }
            BaseActivationInstanceReceiptChainState resultingChain = deleted.Count == 0 ? priorChain
                : BaseActivationInstanceReceiptChainContract.Create(
                    priorChain.CurrentSequence, priorChain.OrderedChecksum.AsSpan(), checked(priorChain.Generation + 1));
            next.ActivationInstanceReceiptChain = resultingChain;
            if (deleted.Count > next.ActivationYieldRetainedUsedSlots)
                return ActivationFailure<BaseActivationReceiptCompactionResult>(
                    "base.activation.receiptCorrupt", OperationStatus.StoreError, ErrorCategory.Store);
            if (deleted.Count > 0)
            {
                next.ActivationYieldRetainedUsedSlots -= deleted.Count;
                next.ActivationYieldReservationGeneration = checked(next.ActivationYieldReservationGeneration + 1);
            }
            BaseActivationYieldReservationState resultingReservation = BaseActivationYieldReservationContract.Create(
                next.ActivationYieldReservationGeneration, MaximumYieldReceiptSlots,
                next.ActivationYieldReservedUnusedSlots, next.ActivationYieldRetainedUsedSlots);
            BaseActivationReceiptCompactionCursor? cursor = examined.Length == 0 ? request.After
                : new BaseActivationReceiptCompactionCursor
                {
                    ActivationId = examined[^1].Value.ActivationId,
                    ReceiptSequence = examined[^1].Value.ReceiptSequence,
                };
            var result = new BaseActivationReceiptCompactionResult
            {
                ExaminedCount = examined.Length, DeletedCount = deleted.Count,
                DeletedYieldReceiptCount = deleted.Count, Next = hasMore ? cursor : null,
                PriorChain = priorChain, ResultingChain = resultingChain,
                PriorReservation = priorReservation, ResultingReservation = resultingReservation,
                DeletedAuthorityOrderedDigest = DeletedReceiptAuthorityDigest(deleted.Select(static item => item.Value.AuthorityChecksum)),
                Completed = !hasMore,
                Accounting = EmptyActivationAccounting with
                {
                    Candidates = candidates.Length, Comparisons = candidates.Length,
                    IndexOperations = deleted.Count * 2,
                    EvidenceBytes = deleted.Count * 32L, TransientBytes = candidates.Sum(static item => (long)item.Value.Result.Length),
                },
                Disposition = BaseMutationRequestDisposition.Committed,
            };
            WriteControlReceipt(next, request.Identity, "activation-receipts-compacted", result,
                HPDBaseJsonSerializerContext.Default.BaseActivationReceiptCompactionResult);
            Volatile.Write(ref _publishedState, next);
            return OperationResults.Ok(result);
        }
        finally { _stateGate.Release(); }
    }

    private static bool ReservationMatches(
        BaseActivationYieldReservationState expected,
        BaseActivationYieldReservationState actual) =>
        expected.FormatVersion == actual.FormatVersion && expected.Generation == actual.Generation
        && expected.MaximumSlots == actual.MaximumSlots && expected.ReservedUnusedSlots == actual.ReservedUnusedSlots
        && expected.RetainedUsedSlots == actual.RetainedUsedSlots
        && CryptographicOperations.FixedTimeEquals(expected.Checksum.AsSpan(), actual.Checksum.AsSpan());

    private static ImmutableArray<byte> DeletedReceiptAuthorityDigest(IEnumerable<byte[]> authorities)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData("base.activation.receiptCompaction.deleted.v1\0"u8);
        Span<byte> length = stackalloc byte[4];
        foreach (byte[] authority in authorities)
        {
            BinaryPrimitives.WriteInt32BigEndian(length, authority.Length);
            hash.AppendData(length); hash.AppendData(authority);
        }
        return hash.GetHashAndReset().ToImmutableArray();
    }

    private static bool TryReadControlReceipt<T>(InMemoryStoreState state, BaseMutationRequestIdentity identity,
        string kind, JsonTypeInfo<T> typeInfo, Func<T, T> duplicate, out OperationResult<T> result)
    {
        string key = ActivationReceiptKey(identity);
        if (state.ActivationInstanceReceipts.ContainsKey(key))
        {
            result = ActivationFailure<T>("base.activation.fingerprintConflict", OperationStatus.Conflict, ErrorCategory.Conflict);
            return true;
        }
        if (!state.ActivationControlReceipts.TryGetValue(key, out InMemoryActivationControlReceiptRow? receipt))
        { result = default!; return false; }
        if (receipt.Kind != kind || !CryptographicOperations.FixedTimeEquals(receipt.Fingerprint, identity.Fingerprint.ToArray()))
        {
            result = ActivationFailure<T>("base.activation.fingerprintConflict", OperationStatus.Conflict, ErrorCategory.Conflict);
            return true;
        }
        if (!ControlReceiptValid(receipt, key))
        {
            result = ActivationFailure<T>("base.activation.receiptCorrupt", OperationStatus.StoreError, ErrorCategory.Store);
            return true;
        }
        T value = JsonSerializer.Deserialize(receipt.Result, typeInfo) ?? throw new InvalidOperationException("base.activation.receiptCorrupt");
        result = OperationResults.Ok(duplicate(value)); return true;
    }

    private static bool TryReadInstanceReceipt<T>(InMemoryStoreState state, BaseMutationRequestIdentity identity,
        string kind, long acceptedAt, JsonTypeInfo<T> typeInfo, Func<T, T> duplicate, out OperationResult<T> result)
    {
        string key = ActivationReceiptKey(identity);
        if (state.ActivationControlReceipts.ContainsKey(key))
        {
            result = ActivationFailure<T>("base.activation.fingerprintConflict", OperationStatus.Conflict, ErrorCategory.Conflict);
            return true;
        }
        if (!state.ActivationInstanceReceipts.TryGetValue(key, out InMemoryActivationInstanceReceiptRow? receipt))
        { result = default!; return false; }
        if (receipt.Kind != kind || !CryptographicOperations.FixedTimeEquals(receipt.Fingerprint, identity.Fingerprint.ToArray()))
        {
            result = ActivationFailure<T>("base.activation.fingerprintConflict", OperationStatus.Conflict, ErrorCategory.Conflict);
            return true;
        }
        if (acceptedAt >= receipt.DuplicateResolveUntil)
        {
            result = ActivationFailure<T>("base.activation.receiptNotFound", OperationStatus.NotFound, ErrorCategory.NotFound);
            return true;
        }
        if (!InstanceReceiptValid(receipt, key))
        {
            result = ActivationFailure<T>("base.activation.receiptCorrupt", OperationStatus.StoreError, ErrorCategory.Store);
            return true;
        }
        T value = JsonSerializer.Deserialize(receipt.Result, typeInfo) ?? throw new InvalidOperationException("base.activation.receiptCorrupt");
        result = OperationResults.Ok(duplicate(value));
        return true;
    }

    private static byte[] WriteControlReceipt<T>(InMemoryStoreState state, BaseMutationRequestIdentity identity,
        string kind, T result, JsonTypeInfo<T> typeInfo)
    {
        string key = ActivationReceiptKey(identity);
        if (state.ActivationInstanceReceipts.ContainsKey(key)) throw new InvalidOperationException("base.activation.fingerprintConflict");
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(result, typeInfo);
        byte[] resultChecksum = SHA256.HashData(bytes);
        byte[] authorityChecksum = BaseActivationControlReceiptContract.AuthorityChecksum(
            key, kind, identity.Fingerprint.ToArray(), resultChecksum).ToArray();
        state.ActivationControlReceipts.Add(key, new InMemoryActivationControlReceiptRow(
            kind, identity.Fingerprint.ToArray(), bytes, resultChecksum, authorityChecksum));
        return authorityChecksum;
    }

    private static bool ControlReceiptValid(InMemoryActivationControlReceiptRow receipt, string receiptKey)
    {
        byte[] actualResultChecksum = SHA256.HashData(receipt.Result);
        if (!CryptographicOperations.FixedTimeEquals(actualResultChecksum, receipt.ResultChecksum))
            return false;
        ImmutableArray<byte> expectedAuthority = BaseActivationControlReceiptContract.AuthorityChecksum(
            receiptKey, receipt.Kind, receipt.Fingerprint, receipt.ResultChecksum);
        return CryptographicOperations.FixedTimeEquals(expectedAuthority.AsSpan(), receipt.AuthorityChecksum);
    }

    private static byte[] WriteInstanceReceipt<T>(InMemoryStoreState state, BaseMutationRequestIdentity identity,
        string kind, InMemoryActivationRow activation, long committedAt, T result, JsonTypeInfo<T> typeInfo)
    {
        string key = ActivationReceiptKey(identity);
        if (state.ActivationControlReceipts.ContainsKey(key)) throw new InvalidOperationException("base.activation.fingerprintConflict");
        BaseActivationInstanceReceiptChainState priorState = state.ActivationInstanceReceiptChain;
        if (!BaseActivationInstanceReceiptChainContract.IsValid(priorState))
            throw new InvalidOperationException("base.activation.receiptCorrupt");
        long sequence = checked(priorState.CurrentSequence + 1);
        long lifetimeMilliseconds = activation.Payload.ReceiptRetention.DuplicateResolutionLifetime.Ticks / TimeSpan.TicksPerMillisecond;
        long duplicateResolveUntil = checked(committedAt + lifetimeMilliseconds);
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(result, typeInfo);
        byte[] resultChecksum = SHA256.HashData(bytes);
        byte[] authorityChecksum = BaseActivationInstanceReceiptChainContract.ReceiptAuthorityChecksum(
            key, kind, activation.Payload.ActivationId, activation.Payload.Definition,
            activation.Payload.ReceiptRetention, identity.Fingerprint.ToArray(), resultChecksum,
            committedAt, duplicateResolveUntil, sequence, priorState.OrderedChecksum.AsSpan()).ToArray();
        byte[] orderedChecksum = BaseActivationInstanceReceiptChainContract.Append(
            sequence, priorState.OrderedChecksum.AsSpan(), authorityChecksum, key).ToArray();
        state.ActivationInstanceReceipts.Add(key, new InMemoryActivationInstanceReceiptRow(
            kind, activation.Payload.ActivationId,
            activation.Payload.Definition with { Checksum = activation.Payload.Definition.Checksum.ToArray().ToImmutableArray() },
            activation.Payload.ReceiptRetention with { }, identity.Fingerprint.ToArray(), bytes,
            resultChecksum, authorityChecksum, committedAt, duplicateResolveUntil, sequence,
            priorState.OrderedChecksum.ToArray(), orderedChecksum));
        state.ActivationInstanceReceiptChain = BaseActivationInstanceReceiptChainContract.Create(
            sequence, orderedChecksum, checked(priorState.Generation + 1));
        return authorityChecksum;
    }

    private static bool InstanceReceiptValid(InMemoryActivationInstanceReceiptRow value, string key)
    {
        if (value.ResultChecksum.Length != 32 || value.AuthorityChecksum.Length != 32
            || value.PriorOrderedChecksum.Length != 32 || value.OrderedChecksum.Length != 32
            || value.Definition.Checksum.Length != 32 || value.ReceiptSequence <= 0
            || value.CommittedAt < 0 || value.DuplicateResolveUntil <= value.CommittedAt
            || value.Retention.FormatVersion != 1
            || !Enum.IsDefined(value.Retention.ProtectedBackupCoverage)
            || value.Retention.DuplicateResolutionLifetime.Ticks % TimeSpan.TicksPerMillisecond != 0
            || value.DuplicateResolveUntil - value.CommittedAt
                != value.Retention.DuplicateResolutionLifetime.Ticks / TimeSpan.TicksPerMillisecond
            || !CryptographicOperations.FixedTimeEquals(SHA256.HashData(value.Result), value.ResultChecksum))
            return false;
        ImmutableArray<byte> authority = BaseActivationInstanceReceiptChainContract.ReceiptAuthorityChecksum(
            key, value.Kind, value.ActivationId, value.Definition, value.Retention,
            value.Fingerprint, value.ResultChecksum, value.CommittedAt, value.DuplicateResolveUntil,
            value.ReceiptSequence, value.PriorOrderedChecksum);
        if (!CryptographicOperations.FixedTimeEquals(authority.AsSpan(), value.AuthorityChecksum)) return false;
        ImmutableArray<byte> ordered = BaseActivationInstanceReceiptChainContract.Append(
            value.ReceiptSequence, value.PriorOrderedChecksum, value.AuthorityChecksum, key);
        return CryptographicOperations.FixedTimeEquals(ordered.AsSpan(), value.OrderedChecksum);
    }

    private static string ActivationReceiptKey(BaseMutationRequestIdentity identity) =>
        $"{identity.Scope}\n{identity.Operation}\n{identity.IdempotencyKey}";

    private static OperationResult<BaseActivationClaimResult> ResolveClaimReplay(
        InMemoryStoreState state, OperationResult<BaseActivationClaimResult> replay, long acceptedNow)
    {
        if (!replay.IsSuccess() || replay.Value is not BaseActivationClaimedResult claimed) return replay;
        if (!state.Activations.TryGetValue(claimed.Claim.ActivationId, out InMemoryActivationRow? row))
            return OperationResults.Ok<BaseActivationClaimResult>(new BaseActivationClaimSupersededResult(claimed.Claim.ActivationId));
        if (row.State == BaseActivationState.Cancelled)
            return OperationResults.Ok<BaseActivationClaimResult>(new BaseActivationClaimCancelledResult(row.Payload.ActivationId));
        if (row.State is BaseActivationState.Succeeded or BaseActivationState.Exhausted or BaseActivationState.OutcomeUnknown or BaseActivationState.Disposed or BaseActivationState.Migrated)
            return OperationResults.Ok<BaseActivationClaimResult>(new BaseActivationClaimTerminalResult(row.Payload.ActivationId, row.State));
        if (!ClaimMatches(row, claimed.Claim))
            return OperationResults.Ok<BaseActivationClaimResult>(new BaseActivationClaimSupersededResult(row.Payload.ActivationId));
        if (row.Lease is null || row.Lease.LeaseExpiresAt <= acceptedNow)
            return OperationResults.Ok<BaseActivationClaimResult>(new BaseActivationClaimExpiredResult(row.Payload.ActivationId));
        return OperationResults.Ok<BaseActivationClaimResult>(claimed with { Lease = row.Lease });
    }

    private static string ActivationTransitionReceiptKind(BaseActivationTransitionRequest request) => request switch
    {
        BaseActivationCompleteRequest => "activation-completed",
        BaseActivationFailRequest failed when failed.Disposition == BaseActivationFailureDisposition.Retry => "activation-retried",
        BaseActivationFailRequest => "activation-failed-terminal",
        BaseActivationYieldRequest => "activation-yielded-v1",
        BaseActivationCancelRequest => "activation-cancelled",
        BaseActivationBeginEffectRequest => "effect-started",
        BaseActivationEffectHeartbeatRequest => "effect-heartbeat",
        BaseActivationCompleteEffectRequest => "effect-completed",
        BaseActivationRecoverEffectRequest => "effect-outcome-unknown",
        BaseActivationReconcileEffectRequest => "effect-reconciled",
        BaseActivationOperatorRetryRequest => "activation-operator-retried",
        BaseActivationDisposeRequest => "activation-disposed",
        _ => throw new InvalidOperationException("base.activation.invalid"),
    };

    private static string ScheduleKey(string id, int version) => $"{id}\n{version}";

    private static BaseScheduleAuthority ScheduleAuthority(BaseScheduleDefinition definition, long generation, bool enabled,
        long epoch, long? last, long? next)
    {
        byte[] checksum = Hash($"base.activation.schedule.authority.v2\0{definition.Id}\n{definition.Version}\n{Convert.ToHexString(definition.Checksum.AsSpan())}\n{generation}\n{enabled}\n{epoch}\n{last?.ToString() ?? "none"}\n{next?.ToString() ?? "none"}");
        return new BaseScheduleAuthority { Definition = BaseScheduleDefinitionBuilder.Create(definition), DefinitionGeneration = generation,
            Enabled = enabled, ScheduleEpoch = epoch, LastConsideredNominal = last, NextNominal = next, Checksum = checksum.ToImmutableArray() };
    }

    private static bool OccurrenceShapeValid(BaseScheduleOccurrenceProposal proposal) => proposal.Fact.Disposition switch
    {
        BaseOccurrenceMaterialized materialized => proposal.Activation is not null && materialized.ActivationId.Length > 0,
        BaseOccurrenceSkippedMisfire => proposal.Activation is null,
        BaseOccurrenceSkippedOverlap skipped => proposal.Activation is null && skipped.BlockingActivationId.Length > 0,
        BaseOccurrenceCancelled cancelled => proposal.Activation is null && cancelled.CancellationReceiptId.Length > 0,
        BaseOccurrenceSuppressedByReplacement replacement => proposal.Activation is null && replacement.ReplacementGeneration > 0,
        BaseOccurrenceSuppressedByRestoreFloor floor => proposal.Activation is null && floor.FloorChecksum.Length == 32,
        _ => false,
    };

    private static byte[] ScheduleActivationFingerprint(BaseActivationCreateIntent activation, string occurrenceId) =>
        Hash($"base.activation.schedule.create.v3\0{occurrenceId}\n{activation.Definition.Id}\n{activation.Definition.Version}\n{activation.MaximumYields}\n{Convert.ToHexString(activation.InputChecksum.AsSpan())}\n{activation.RequestedDueAt}\n{activation.EffectiveDueAt ?? activation.RequestedDueAt}");

    private bool AcceptActivationTime(BaseAcceptedTimeReceipt receipt)
    {
        long native = _timeProvider.GetUtcNow().ToUnixTimeMilliseconds();
        if (!BaseActivationAcceptedTimeAuthority.Verify(receipt, native)) return false;
        while (true)
        {
            long observed = Volatile.Read(ref _acceptedActivationUtc);
            if (receipt.CapturedUtc < observed) return false;
            if (Interlocked.CompareExchange(ref _acceptedActivationUtc, receipt.CapturedUtc, observed) == observed) return true;
        }
    }

    internal static byte[] OccurrenceChecksum(BaseScheduleOccurrenceFact fact) => Hash(
        $"base.activation.schedule.occurrence.v2\0{fact.OccurrenceId}\n{fact.ScheduleId}\n{fact.ScheduleEpoch}\n{fact.NominalAt}\n{fact.EffectiveAt}\n{fact.OverlapOrdinal}\n{DispositionText(fact.Disposition)}");

    private static string DispositionText(BaseScheduleOccurrenceDisposition disposition) => disposition switch
    {
        BaseOccurrenceMaterialized value => $"materialized:{value.ActivationId}",
        BaseOccurrenceSkippedMisfire => "skipped-misfire",
        BaseOccurrenceSkippedOverlap value => $"skipped-overlap:{value.BlockingActivationId}",
        BaseOccurrenceCancelled value => $"cancelled:{value.CancellationReceiptId}",
        BaseOccurrenceSuppressedByReplacement value => $"replacement:{value.ReplacementGeneration}",
        BaseOccurrenceSuppressedByRestoreFloor value => $"restore:{Convert.ToHexString(value.FloorChecksum.AsSpan())}",
        _ => throw new InvalidOperationException("base.activation.occurrenceInvalid"),
    };

    private static string ExecutorKey(string applicationId, string hostId, string processId) => $"{applicationId}\n{hostId}\n{processId}";

    private static BaseExecutorHeartbeatObservation Heartbeat(BaseExecutorIncarnationAuthority authority, long revision, long expiresAt)
    {
        byte[] checksum = Hash($"base.activation.executor.heartbeat.v2\0{Convert.ToHexString(authority.Checksum.AsSpan())}\n{revision}\n{expiresAt}");
        return new BaseExecutorHeartbeatObservation
        {
            HeartbeatRevision = revision, HeartbeatExpiresAt = expiresAt,
            ExecutorAuthorityChecksum = authority.Checksum.ToArray().ToImmutableArray(), Checksum = checksum.ToImmutableArray(),
        };
    }

    private static bool ExecutorMatches(BaseExecutorIncarnationAuthority left, BaseExecutorIncarnationAuthority right) =>
        left.ApplicationId == right.ApplicationId && left.HostId == right.HostId &&
        left.ProcessIncarnationId == right.ProcessIncarnationId && left.ExecutorGeneration == right.ExecutorGeneration &&
        left.StoreInstanceId == right.StoreInstanceId && left.RestoreEpoch == right.RestoreEpoch &&
        CryptographicOperations.FixedTimeEquals(left.WorkerDefinitionSetChecksum.AsSpan(), right.WorkerDefinitionSetChecksum.AsSpan()) &&
        CryptographicOperations.FixedTimeEquals(left.Checksum.AsSpan(), right.Checksum.AsSpan());

    private static bool HeartbeatsEqual(BaseExecutorHeartbeatObservation left, BaseExecutorHeartbeatObservation right) =>
        left.HeartbeatRevision == right.HeartbeatRevision && left.HeartbeatExpiresAt == right.HeartbeatExpiresAt &&
        CryptographicOperations.FixedTimeEquals(left.ExecutorAuthorityChecksum.AsSpan(), right.ExecutorAuthorityChecksum.AsSpan()) &&
        CryptographicOperations.FixedTimeEquals(left.Checksum.AsSpan(), right.Checksum.AsSpan());

    private static bool CurrentExecutorAllows(InMemoryStoreState state, BaseExecutorIncarnationAuthority authority, long now) =>
        state.Executors.TryGetValue(ExecutorKey(authority.ApplicationId, authority.HostId, authority.ProcessIncarnationId), out InMemoryExecutorRow? row) &&
        !row.Retired && ExecutorMatches(row.Authority, authority) && row.Heartbeat.HeartbeatExpiresAt > now;

    private static BaseEffectExecutionAuthority Effect(
        BaseActivationClaimAuthority claim, BaseExecutorIncarnationAuthority executor, long generation, long revision, long expiresAt)
    {
        byte[] checksum = Hash($"base.activation.effect.v2\0{claim.ActivationId}\n{Convert.ToHexString(claim.FencingToken.AsSpan())}\n{Convert.ToHexString(executor.Checksum.AsSpan())}\n{generation}\n{revision}\n{expiresAt}");
        return new BaseEffectExecutionAuthority
        {
            Claim = claim, Executor = executor, EffectStartGeneration = generation, HeartbeatRevision = revision,
            HeartbeatExpiresAt = expiresAt, Checksum = checksum.ToImmutableArray(),
        };
    }

    private static bool EffectMatches(BaseEffectExecutionAuthority left, BaseEffectExecutionAuthority right) =>
        left.EffectStartGeneration == right.EffectStartGeneration && left.HeartbeatRevision == right.HeartbeatRevision &&
        left.HeartbeatExpiresAt == right.HeartbeatExpiresAt && ClaimAuthoritiesEqual(left.Claim, right.Claim) &&
        ExecutorMatches(left.Executor, right.Executor) && CryptographicOperations.FixedTimeEquals(left.Checksum.AsSpan(), right.Checksum.AsSpan());

    private static bool ClaimAuthoritiesEqual(BaseActivationClaimAuthority left, BaseActivationClaimAuthority right) =>
        left.ActivationId == right.ActivationId && left.AttemptNumber == right.AttemptNumber && left.ClaimEpoch == right.ClaimEpoch &&
        left.WorkerIdentity == right.WorkerIdentity && left.CancellationGeneration == right.CancellationGeneration &&
        left.StoreInstanceId == right.StoreInstanceId && left.RestoreEpoch == right.RestoreEpoch &&
        CryptographicOperations.FixedTimeEquals(left.FencingToken.AsSpan(), right.FencingToken.AsSpan()) &&
        CryptographicOperations.FixedTimeEquals(left.DefinitionChecksum.AsSpan(), right.DefinitionChecksum.AsSpan());

    private static List<InMemoryActivationRow> EligibleRows(
        InMemoryStoreState state,
        ImmutableArray<BaseActivationDefinitionKey> definitions,
        BaseOwnedScopeSeekAuthority scope,
        long acceptedNow,
        BaseActivationDueBoundary? after)
    {
        var keys = definitions.ToDictionary(static value => $"{value.Id}\n{value.Version}", StringComparer.Ordinal);
        return state.Activations.Values
            .Where(row => keys.TryGetValue($"{row.Payload.Definition.Id}\n{row.Payload.Definition.Version}", out BaseActivationDefinitionKey? key) &&
                CryptographicOperations.FixedTimeEquals(key.Checksum.AsSpan(), row.Payload.Definition.Checksum.AsSpan()))
            .Where(row => ScopeMatches(row.Payload.Scope, scope))
            .Where(static row => row.Eligible)
            .Where(row => row.OverlapPolicy != BaseScheduleOverlapPolicy.Queue || !HasEarlierActiveOverlap(state, row))
            .Where(row => (row.State is BaseActivationState.Pending or BaseActivationState.RetryPending or BaseActivationState.YieldPending) ||
                (row.State == BaseActivationState.Claimed && row.Lease is not null && row.Lease.LeaseExpiresAt <= acceptedNow))
            .Where(row => row.EffectiveDueAt <= acceptedNow)
            .OrderBy(row => Boundary(row, acceptedNow), Comparer<BaseActivationDueBoundary>.Create(Compare))
            .Where(row => after is null || Compare(Boundary(row, acceptedNow), after) > 0)
            .ToList();
    }

    private static bool HasEarlierActiveOverlap(InMemoryStoreState state, InMemoryActivationRow row)
    {
        if (row.OverlapKey is null) return false;
        return state.Activations.Values.Any(other => !ReferenceEquals(other, row) && other.OverlapKey is not null &&
            CryptographicOperations.FixedTimeEquals(other.OverlapKey, row.OverlapKey) &&
            (other.State is BaseActivationState.Pending or BaseActivationState.RetryPending or BaseActivationState.YieldPending or BaseActivationState.Claimed) &&
            (other.EffectiveDueAt < row.EffectiveDueAt || other.EffectiveDueAt == row.EffectiveDueAt &&
                string.Compare(other.Payload.ActivationId, row.Payload.ActivationId, StringComparison.Ordinal) < 0));
    }

    private static bool ScopeMatches(BaseOwnedSubjectScopeEvidence scope, BaseOwnedScopeSeekAuthority authority) =>
        scope.Kind == authority.Kind && CryptographicOperations.FixedTimeEquals(ScopeDigest(scope), authority.ProtectedIndexDigest.AsSpan());

    private static bool DefinitionMatches(BaseActivationDefinitionKey left, BaseActivationDefinitionKey right) =>
        left.Id == right.Id && left.Version == right.Version && left.Checksum.Length == 32 && right.Checksum.Length == 32
        && CryptographicOperations.FixedTimeEquals(left.Checksum.AsSpan(), right.Checksum.AsSpan());

    private static bool MigrationSourceState(BaseActivationState state) => state is
        BaseActivationState.Pending or BaseActivationState.RetryPending or BaseActivationState.YieldPending or BaseActivationState.Exhausted
        or BaseActivationState.Cancelled;

    private static byte[] ScopeDigest(BaseOwnedSubjectScopeEvidence scope) =>
        Hash($"base.activation.scope.v2\0{(int)scope.Kind}\n{scope.Value ?? string.Empty}");

    private static void IndexActivation(InMemoryStoreState state, BaseActivationPayload payload)
    {
        string key = Convert.ToHexString(ScopeDigest(payload.Scope));
        if (!state.ActivationsByProtectedScope.TryGetValue(key, out SortedSet<string>? values))
        {
            values = new SortedSet<string>(StringComparer.Ordinal);
            state.ActivationsByProtectedScope.Add(key, values);
        }
        values.Add(payload.ActivationId);
    }

    private static string DisposedActivationAuthorityKey(
        BaseOwnedScopeSeekAuthority scope,
        BaseActivationDefinitionKey definition) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"base.activation.disposed.index.v1\0{Convert.ToHexString(scope.ProtectedIndexDigest.AsSpan())}\n" +
            $"{definition.Id}\n{definition.Version}\n{Convert.ToHexString(definition.Checksum.AsSpan())}")));

    private static string DisposedActivationAuthorityKey(BaseActivationPayload payload) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"base.activation.disposed.index.v1\0{Convert.ToHexString(ScopeDigest(payload.Scope))}\n" +
            $"{payload.Definition.Id}\n{payload.Definition.Version}\n" +
            $"{Convert.ToHexString(payload.Definition.Checksum.AsSpan())}")));

    private static void IndexDisposedActivation(InMemoryStoreState state, BaseActivationPayload payload)
    {
        string key = DisposedActivationAuthorityKey(payload);
        if (!state.DisposedActivationsByAuthority.TryGetValue(key, out SortedSet<string>? values))
        {
            values = new SortedSet<string>(StringComparer.Ordinal);
            state.DisposedActivationsByAuthority.Add(key, values);
        }
        values.Add(payload.ActivationId);
    }

    private static void RemoveDisposedActivation(InMemoryStoreState state, BaseActivationPayload payload)
    {
        string key = DisposedActivationAuthorityKey(payload);
        if (!state.DisposedActivationsByAuthority.TryGetValue(key, out SortedSet<string>? values)) return;
        values.Remove(payload.ActivationId);
        if (values.Count == 0) state.DisposedActivationsByAuthority.Remove(key);
    }

    private static BaseActivationDueBoundary Boundary(InMemoryActivationRow row, long acceptedNow) => new()
    {
        EffectiveAgedPriority = Math.Min(32, row.Priority + checked((int)Math.Min(int.MaxValue,
            Math.Max(0, acceptedNow - row.EffectiveDueAt) / 60_000))),
        EffectiveDueAt = row.EffectiveDueAt,
        OccurrenceId = row.OccurrenceId,
        ActivationId = row.Payload.ActivationId,
    };

    private static int Compare(BaseActivationDueBoundary left, BaseActivationDueBoundary right)
    {
        int priority = right.EffectiveAgedPriority.CompareTo(left.EffectiveAgedPriority);
        if (priority != 0) return priority;
        int due = left.EffectiveDueAt.CompareTo(right.EffectiveDueAt);
        if (due != 0) return due;
        int occurrence = string.Compare(left.OccurrenceId, right.OccurrenceId, StringComparison.Ordinal);
        return occurrence != 0 ? occurrence : string.Compare(left.ActivationId, right.ActivationId, StringComparison.Ordinal);
    }

    private static byte[] CurrentWorkerToken(InMemoryStoreState state, BaseActivationClaimRequest request)
    {
        InMemoryActivationRow? first = EligibleRows(
            state, request.Worker.Definitions, request.Worker.Scope, request.AcceptedTime.CapturedUtc, null).FirstOrDefault();
        return DueToken(state.ActivationIndexGeneration, request.AcceptedTime.CapturedUtc,
            request.Worker.Scope.ProtectedIndexDigest.AsSpan(), request.Worker.Definitions,
            first is null ? null : Boundary(first, request.AcceptedTime.CapturedUtc));
    }

    private static byte[] DueToken(
        long generation,
        long acceptedNow,
        ReadOnlySpan<byte> scopeDigest,
        ImmutableArray<BaseActivationDefinitionKey> definitions,
        BaseActivationDueBoundary? earliest)
    {
        string definitionText = string.Join("\n", definitions.Select(static item =>
            $"{item.Id}:{item.Version}:{Convert.ToHexString(item.Checksum.AsSpan())}"));
        byte[] digest = Hash($"base.activation.due.token.v2\0{generation}\n{acceptedNow}\n{Convert.ToHexString(scopeDigest)}\n{definitionText}\n{earliest?.ActivationId ?? string.Empty}");
        byte[] token = new byte[48];
        BinaryPrimitives.WriteInt64BigEndian(token, generation);
        BinaryPrimitives.WriteInt64BigEndian(token.AsSpan(8), acceptedNow);
        digest.CopyTo(token, 16);
        return token;
    }

    private static (long Generation, long AcceptedAt) DecodeDueAuthority(ReadOnlySpan<byte> token) =>
        token.Length == 48
            ? (BinaryPrimitives.ReadInt64BigEndian(token), BinaryPrimitives.ReadInt64BigEndian(token[8..]))
            : (-1, -1);

    private static BaseAtomicReadIntervalEvidence DueInterval(
        BaseOwnedScopeSeekAuthority scope,
        long acceptedNow,
        BaseActivationDueBoundary? after,
        BaseActivationDueBoundary? result) => new()
    {
        LogicalAccessPathId = "base.activation.due.byScopeDefinitionPriorityTime.v1",
        CanonicalLowerBound = Encoding.UTF8.GetBytes(after?.ActivationId ?? string.Empty).ToImmutableArray(),
        LowerInclusive = false,
        CanonicalUpperBound = Encoding.UTF8.GetBytes($"{acceptedNow}\n{result?.ActivationId ?? string.Empty}\n{Convert.ToHexString(scope.ProtectedIndexDigest.AsSpan())}").ToImmutableArray(),
        UpperInclusive = true,
    };

    private static long IntervalBytes(BaseAtomicReadIntervalEvidence interval) =>
        checked(Encoding.UTF8.GetByteCount(interval.LogicalAccessPathId) + interval.CanonicalLowerBound.Length + interval.CanonicalUpperBound.Length + 2);

    private static bool ClaimMatches(InMemoryActivationRow row, BaseActivationClaimAuthority claim) =>
        row.State == BaseActivationState.Claimed && row.Claim is not null && row.Lease is not null &&
        row.Claim.ActivationId == claim.ActivationId &&
        row.Claim.AttemptNumber == claim.AttemptNumber &&
        row.Claim.ActivationGeneration == claim.ActivationGeneration &&
        row.Claim.ClaimEpoch == claim.ClaimEpoch &&
        row.Claim.CancellationGeneration == claim.CancellationGeneration &&
        CryptographicOperations.FixedTimeEquals(row.Claim.FencingToken.AsSpan(), claim.FencingToken.AsSpan());

    private static bool ValidateLimits(BaseActivationExecutionLimits limits) =>
        limits.MaximumCandidates is > 0 and <= 256 &&
        limits.MaximumInputBytes is > 0 and <= 4L * 1024 * 1024 &&
        limits.MaximumResultBytes is > 0 and <= 4L * 1024 * 1024 &&
        limits.MaximumEvidenceBytes is > 0 and <= 16L * 1024 * 1024 &&
        limits.MaximumTransientBytes is > 0 and <= 16L * 1024 * 1024 &&
        limits.MaximumReadIntervals > 0 && limits.MaximumIndexOperations > 0;

    private static byte[] ControlChecksum(
        string activationId, long generation, BaseActivationState state, long effectiveDueAt,
        long yieldCount, long maximumYields, long executionSliceOrdinal,
        long? attemptStartedAt, long? sliceStartedAt,
        BaseActivationYieldDisposition? terminalYieldDisposition, string? terminalYieldFailureCode) =>
        BaseActivationControlChecksumContract.Create(activationId, generation, state, effectiveDueAt,
            yieldCount, maximumYields, executionSliceOrdinal, attemptStartedAt, sliceStartedAt,
            terminalYieldDisposition, terminalYieldFailureCode).ToArray();

    private static long? CanonicalYieldResumeAt(DateTimeOffset? value)
    {
        if (value is null) return null;
        if (value.Value.Offset != TimeSpan.Zero || value.Value.Ticks % TimeSpan.TicksPerMillisecond != 0)
            return -1;
        try { return value.Value.ToUnixTimeMilliseconds(); }
        catch (ArgumentOutOfRangeException) { return -1; }
    }

    private static byte[] Hash(string value) => SHA256.HashData(Encoding.UTF8.GetBytes(value));

    private static OperationResult<T> ActivationFailure<T>(
        string code,
        OperationStatus status,
        ErrorCategory category) => new()
    {
        Status = status,
        Error = new BaseError
        {
            Code = code,
            Message = "The activation operation could not be completed.",
            Category = category,
        },
    };
}

internal static class BaseActivationPayloadCloneExtensions
{
    internal static BaseActivationPayload DeepClone(this BaseActivationPayload payload) => payload with
    {
        Definition = payload.Definition with { Checksum = payload.Definition.Checksum.ToArray().ToImmutableArray() },
        CanonicalInput = payload.CanonicalInput.ToArray().ToImmutableArray(),
        InputChecksum = payload.InputChecksum.ToArray().ToImmutableArray(),
        Scope = payload.Scope with { },
        OccurrenceId = payload.OccurrenceId is null ? null : new string(payload.OccurrenceId.AsSpan()),
        Checksum = payload.Checksum.ToArray().ToImmutableArray(),
    };
}
