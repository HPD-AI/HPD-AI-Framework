using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace HPD.Base;

internal sealed partial class InMemoryRecordStore
{
    /// <inheritdoc />
    public async ValueTask<BaseResult<BaseSemanticActivationMaintenanceAuthority>> InspectMaintenanceAuthorityAsync(
        BaseSemanticActivationMaintenanceAuthorityRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!ValidMaintenanceAuthorityRequest(request))
            return SemanticAdminFailure<BaseSemanticActivationMaintenanceAuthority>(OperationStatus.ValidationFailed,
                BaseSemanticActivationErrorCodes.Invalid, ErrorCategory.Validation);
        await _stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            InMemoryStoreState state = Volatile.Read(ref _publishedState);
            if (!SemanticDefinitionInstalled(request.Definition)
                || state.RemovedSemanticDefinitions.Contains(DefinitionKey(request.Definition))
                || state.SemanticActivationAuthority is not { } authority
                || authority.RestoreEpoch != request.RestoreEpoch
                || authority.SemanticAuthorityGeneration != request.SemanticAuthorityGeneration)
                return SemanticAdminFailure<BaseSemanticActivationMaintenanceAuthority>(OperationStatus.Conflict,
                    BaseSemanticActivationErrorCodes.GraphChanged, ErrorCategory.Conflict);
            using var definitionHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            definitionHash.AppendData("base.semanticActivation.definitionState.v1\0"u8);
            using var retiredHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            retiredHash.AppendData("base.semanticActivation.orderedRows.v1\0"u8);
            using var negativeHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            negativeHash.AppendData("base.semanticActivation.orderedRows.v1\0"u8);
            long live = 0, retired = 0, absent = 0, rows = 0, bytes = 0;
            Span<byte> framedLength = stackalloc byte[4];
            foreach (SemanticAdminRow row in SemanticAdminRows(state, request.Definition))
            {
                ValidateSemanticAdminRow(row, request.Definition, authority);
                rows = checked(rows + 1);
                bytes = checked(bytes + row.Binding.Length + row.Key.Length + 1L + sizeof(long) + row.Authority.Length);
                switch (row.State)
                {
                    case BaseSemanticActivationSlotState.Live: live = checked(live + 1); break;
                    case BaseSemanticActivationSlotState.Retired: retired = checked(retired + 1); break;
                    case BaseSemanticActivationSlotState.CompactedAbsent: absent = checked(absent + 1); break;
                    default: throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
                }
                definitionHash.AppendData(row.Binding); definitionHash.AppendData(row.Key);
                definitionHash.AppendData([(byte)row.State]); definitionHash.AppendData(AdminInt64(row.Generation));
                definitionHash.AppendData(row.Authority);
                if (row.State == BaseSemanticActivationSlotState.Retired)
                {
                    BinaryPrimitives.WriteInt32BigEndian(framedLength, row.Authority.Length);
                    retiredHash.AppendData(framedLength); retiredHash.AppendData(row.Authority);
                }
                if (row.State is BaseSemanticActivationSlotState.Retired or BaseSemanticActivationSlotState.CompactedAbsent)
                {
                    byte[] negative = SemanticAdminHash("base.semanticActivation.historicalNegativeRow.v1\0",
                        row.Binding, row.Key, AdminInt64((int)row.State), row.Authority);
                    BinaryPrimitives.WriteInt32BigEndian(framedLength, negative.Length);
                    negativeHash.AppendData(framedLength); negativeHash.AppendData(negative);
                }
                if (rows > request.MaximumRows || bytes > request.MaximumBytes)
                    return SemanticAdminFailure<BaseSemanticActivationMaintenanceAuthority>(OperationStatus.ValidationFailed,
                        BaseSemanticActivationErrorCodes.BudgetExceeded, ErrorCategory.Validation);
            }
            var result = new BaseSemanticActivationMaintenanceAuthority
            {
                SemanticAuthorityGeneration = request.SemanticAuthorityGeneration,
                LiveCount = live, RetiredCount = retired, AbsenceCount = absent,
                RetiredAuthorityChecksum = retiredHash.GetHashAndReset().ToImmutableArray(),
                DefinitionStateChecksum = definitionHash.GetHashAndReset().ToImmutableArray(),
                AbsenceAuthorityChecksum = negativeHash.GetHashAndReset().ToImmutableArray(),
                ExaminedRows = rows, CanonicalBytes = bytes, Checksum = [],
            };
            result = result with { Checksum = BaseSemanticActivationMaintenanceAuthorityContract.Checksum(request, result) };
            return BaseProviderResultContract.Ok(CloneMaintenanceAuthority(result));
        }
        catch (OverflowException)
        {
            return SemanticAdminFailure<BaseSemanticActivationMaintenanceAuthority>(OperationStatus.ValidationFailed,
                BaseSemanticActivationErrorCodes.BudgetExceeded, ErrorCategory.Validation);
        }
        catch (Exception exception) when (exception is InvalidDataException or JsonException)
        {
            return SemanticAdminFailure<BaseSemanticActivationMaintenanceAuthority>(OperationStatus.StoreError,
                BaseSemanticActivationErrorCodes.Corrupt, ErrorCategory.Store);
        }
        finally { _stateGate.Release(); }
    }

    /// <inheritdoc />
    public async ValueTask<BaseResult<BaseSemanticActivationProviderInspectionPage>> InspectAsync(
        BaseSemanticActivationProviderInspectionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!ValidInspectionRequest(request))
            return SemanticAdminFailure<BaseSemanticActivationProviderInspectionPage>(OperationStatus.ValidationFailed,
                BaseSemanticActivationErrorCodes.Invalid, ErrorCategory.Validation);
        await _stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            InMemoryStoreState state = Volatile.Read(ref _publishedState);
            if (!SemanticDefinitionInstalled(request.Definition)
                || state.RemovedSemanticDefinitions.Contains(DefinitionKey(request.Definition)))
                return SemanticAdminFailure<BaseSemanticActivationProviderInspectionPage>(OperationStatus.ValidationFailed,
                    BaseSemanticActivationErrorCodes.NotInstalled, ErrorCategory.Validation);
            if (state.SemanticActivationAuthority is not { } authority || authority.RestoreEpoch != request.RestoreEpoch)
                return SemanticAdminFailure<BaseSemanticActivationProviderInspectionPage>(OperationStatus.Conflict,
                    BaseSemanticActivationErrorCodes.GraphChanged, ErrorCategory.Conflict);
            long generation = authority.SemanticAuthorityGeneration;
            if (request.After is { } after && (!string.Equals(after.DefinitionId, request.Definition.Id, StringComparison.Ordinal)
                || after.ProviderIncarnation.Length != 32
                || !CryptographicOperations.FixedTimeEquals(after.ProviderIncarnation.AsSpan(), _semanticProviderIncarnation.AsSpan())
                || after.CapturedAuthorityGeneration != generation || after.ScopeBindingId.Length != 32
                || after.RuntimeBoundaryChecksum.Length != 32
                || !CryptographicOperations.FixedTimeEquals(after.RuntimeBoundaryChecksum.AsSpan(),
                    BaseSemanticActivationInspectionContract.BoundaryChecksum(request,
                        after.ScopeBindingId.AsSpan(), after.Key, generation).AsSpan())))
                return SemanticAdminFailure<BaseSemanticActivationProviderInspectionPage>(OperationStatus.ValidationFailed,
                    BaseSemanticActivationErrorCodes.Invalid, ErrorCategory.Validation);
            IEnumerable<SemanticAdminRow> ordered = SemanticAdminRows(state, request.Definition);
            if (request.State is { } stateFilter) ordered = ordered.Where(row => row.State == stateFilter);
            if (request.After is { } boundary)
            {
                byte[] boundaryKey = boundary.Key.ToArray();
                ordered = ordered.Where(row => CompareAdminBoundary(row.Binding, row.Key,
                    boundary.ScopeBindingId.AsSpan(), boundaryKey) > 0);
            }
            var items = ImmutableArray.CreateBuilder<BaseSemanticActivationProviderInspectionItem>();
            long bytes = 0;
            foreach (SemanticAdminRow row in ordered.Take(request.Take))
            {
                ValidateSemanticAdminRow(row, request.Definition, authority);
                bytes = checked(bytes + row.Binding.Length + row.Key.Length + row.Authority.Length + 52L);
                var itemBoundary = new BaseSemanticActivationProviderInspectionBoundary
                {
                    DefinitionId = new string(request.Definition.Id.AsSpan()),
                    ProviderIncarnation = _semanticProviderIncarnation.ToArray().ToImmutableArray(),
                    ScopeBindingId = row.Binding.ToImmutableArray(), Key = BaseSemanticActivationKeyDigest.Create(row.Key),
                    CapturedAuthorityGeneration = generation, RuntimeBoundaryChecksum = [],
                };
                itemBoundary = itemBoundary with { RuntimeBoundaryChecksum = BaseSemanticActivationInspectionContract.BoundaryChecksum(
                    request, row.Binding, itemBoundary.Key, generation) };
                items.Add(new BaseSemanticActivationProviderInspectionItem
                {
                    State = row.State, SlotGeneration = row.Generation, Boundary = itemBoundary,
                    RetirementPosition = row.RetirementPosition,
                    StateChecksum = row.StateChecksum.ToArray().ToImmutableArray(),
                    CanonicalStateAuthority = row.Authority.ToImmutableArray(),
                });
            }
            ImmutableArray<BaseSemanticActivationProviderInspectionItem> pageItems = items.ToImmutable();
            var accounting = new BaseSemanticActivationAccounting
            {
                Operations = 0, ScopeDirectoryReads = 0, SlotReads = pageItems.Length,
                ActivationReads = 0, ReadIntervals = 1, IndexOperations = 1,
                KeyBytes = checked(pageItems.Length * 32L), ScopeDirectoryBytes = 0,
                ActivationBytes = 0, EvidenceBytes = bytes, ReceiptBytes = 0,
                TransientBytes = bytes, ActivationCreation = EmptySemanticAdminActivationAccounting(),
            };
            if (accounting.SlotReads > request.Limits.MaximumSlotReads
                || accounting.ReadIntervals > request.Limits.MaximumReadIntervals
                || accounting.IndexOperations > request.Limits.MaximumIndexOperations
                || accounting.EvidenceBytes > request.Limits.MaximumEvidenceBytes
                || accounting.TransientBytes > request.Limits.MaximumTransientBytes)
                return SemanticAdminFailure<BaseSemanticActivationProviderInspectionPage>(OperationStatus.ValidationFailed,
                    BaseSemanticActivationErrorCodes.BudgetExceeded, ErrorCategory.Validation);
            ImmutableArray<BaseAtomicReadIntervalEvidence> intervals = [SemanticInspectionInterval(request, pageItems)];
            BaseSemanticActivationProviderInspectionBoundary? next = pageItems.Length == request.Take
                ? CloneInspectionBoundary(pageItems[^1].Boundary) : null;
            var page = new BaseSemanticActivationProviderInspectionPage
            {
                Items = pageItems, Next = next, CapturedAuthorityGeneration = generation,
                ReadIntervals = intervals, Accounting = accounting, Checksum = [],
            };
            page = page with { Checksum = BaseSemanticActivationInspectionContract.PageChecksum(request, page) };
            return BaseProviderResultContract.Ok(CloneInspectionPage(page));
        }
        catch (OverflowException)
        {
            return SemanticAdminFailure<BaseSemanticActivationProviderInspectionPage>(OperationStatus.ValidationFailed,
                BaseSemanticActivationErrorCodes.BudgetExceeded, ErrorCategory.Validation);
        }
        catch (Exception exception) when (exception is InvalidDataException or JsonException)
        {
            return SemanticAdminFailure<BaseSemanticActivationProviderInspectionPage>(OperationStatus.StoreError,
                BaseSemanticActivationErrorCodes.Corrupt, ErrorCategory.Store);
        }
        finally { _stateGate.Release(); }
    }

    /// <inheritdoc />
    public async ValueTask<BaseResult<BaseSemanticActivationMaintenanceResult>> ExecuteAsync(
        BaseSemanticActivationMaintenanceRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!ValidMaintenanceRequest(request))
            return SemanticAdminFailure<BaseSemanticActivationMaintenanceResult>(OperationStatus.ValidationFailed,
                BaseSemanticActivationErrorCodes.Invalid, ErrorCategory.Validation);
        if (SemanticStoreIsQuarantined)
            return SemanticAdminFailure<BaseSemanticActivationMaintenanceResult>(OperationStatus.StoreError,
                BaseSemanticActivationErrorCodes.Quarantined, ErrorCategory.Store);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(request.Limits.Deadline);
        AtomicExecutionLease? acquired = await AcquireAtomicExecutionAsync(deadline.Token).ConfigureAwait(false);
        if (acquired is null)
            return cancellationToken.IsCancellationRequested
                ? MaintenanceCancellationResult(request)
                : SemanticAdminFailure<BaseSemanticActivationMaintenanceResult>(OperationStatus.StoreError,
                    BaseSemanticActivationErrorCodes.MaintenanceTimeout, ErrorCategory.Store);
        await using AtomicExecutionLease execution = acquired;
        try
        {
            await _stateGate.WaitAsync(deadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return SemanticAdminFailure<BaseSemanticActivationMaintenanceResult>(OperationStatus.StoreError,
                BaseSemanticActivationErrorCodes.MaintenanceTimeout, ErrorCategory.Store);
        }
        catch (OperationCanceledException)
        {
            return MaintenanceCancellationResult(request);
        }
        try
        {
            InMemoryStoreState current = Volatile.Read(ref _publishedState);
            string identityKey = MaintenanceIdentityKey(request.Identity);
            byte[] fingerprint = BaseSemanticActivationMaintenanceContract.RequestFingerprint(request).ToArray();
            bool removedReceipt = current.RemovedSemanticMaintenanceReceipts.TryGetValue(
                identityKey, out InMemorySemanticMaintenanceEntry? existing);
            if (!removedReceipt)
                current.SemanticMaintenance.TryGetValue(identityKey, out existing);
            if (existing is not null)
            {
                if (!CryptographicOperations.FixedTimeEquals(existing.Fingerprint, fingerprint))
                    return SemanticAdminFailure<BaseSemanticActivationMaintenanceResult>(OperationStatus.Conflict,
                        BaseSemanticActivationErrorCodes.FingerprintConflict, ErrorCategory.Conflict);
                if (existing.Result.Disposition != BaseSemanticActivationMaintenanceDisposition.InProgress)
                    return BaseProviderResultContract.Ok(CloneMaintenanceResult(existing.Result with
                    {
                        Disposition = BaseSemanticActivationMaintenanceDisposition.Duplicate,
                        ReceiptDisposition = BaseMutationRequestDisposition.Duplicate,
                    }));
                if (!ValidCheckpointForRequest(existing.Result.Checkpoint, request, fingerprint))
                {
                    Interlocked.Exchange(ref _semanticMaintenanceQuarantined, 1);
                    return SemanticAdminFailure<BaseSemanticActivationMaintenanceResult>(OperationStatus.StoreError,
                        BaseSemanticActivationErrorCodes.Corrupt, ErrorCategory.Store);
                }
            }
            if (current.SemanticActivationAuthority is not { } authority
                || authority.SemanticAuthorityGeneration != request.ExpectedSemanticAuthorityGeneration)
                return SemanticAdminFailure<BaseSemanticActivationMaintenanceResult>(OperationStatus.Conflict,
                    BaseSemanticActivationErrorCodes.GraphChanged, ErrorCategory.Conflict);
            if (current.RemovedSemanticDefinitions.Contains(DefinitionKey(request.Definition)))
                return SemanticAdminFailure<BaseSemanticActivationMaintenanceResult>(OperationStatus.ValidationFailed,
                    BaseSemanticActivationErrorCodes.NotInstalled, ErrorCategory.Validation);
            if (existing is null)
            {
                if (current.SemanticMaintenance.Values.Any(value =>
                        value.Result.Disposition == BaseSemanticActivationMaintenanceDisposition.InProgress
                        && MaintenanceDefinitionsOverlap(value, request)))
                    return SemanticAdminFailure<BaseSemanticActivationMaintenanceResult>(OperationStatus.Conflict,
                        BaseSemanticActivationErrorCodes.GraphChanged, ErrorCategory.Conflict);
                int checkpoints = current.SemanticMaintenance.Values.Count(static value =>
                    value.Result.Disposition == BaseSemanticActivationMaintenanceDisposition.InProgress);
                int receipts = checked(current.SemanticMaintenance.Count - checkpoints
                    + current.RemovedSemanticMaintenanceReceipts.Count);
                if (checkpoints >= SemanticActivationCapability.MaximumMaintenanceCheckpoints
                    || receipts >= SemanticActivationCapability.MaximumMaintenanceReceipts)
                    return SemanticAdminFailure<BaseSemanticActivationMaintenanceResult>(OperationStatus.CapabilityUnavailable,
                        BaseSemanticActivationErrorCodes.CapacityUnavailable, ErrorCategory.Capability);
            }

            InMemorySemanticMaintenanceEntry entry = existing is null
                ? NewMaintenanceEntry(request, fingerprint)
                : existing.DeepClone();
            var plan = new InMemorySemanticMaintenancePlan
            {
                Entry = entry,
                ReadLowerBoundary = CloneRecoveryBoundary(entry.Result.Checkpoint?.After),
            };
            plan.ChargeLookup(current.SemanticMaintenance.ContainsKey(identityKey) ? 1 : 2);
            plan.ChargeLookup(2); // store authority and removed-definition authority
            if (existing is null)
            {
                plan.ChargeScan(current.SemanticMaintenance.Count); // overlap fence scan
                plan.ChargeScan(current.SemanticMaintenance.Count); // active checkpoint count
                plan.ChargeLookup(2); // active and permanently retained receipt counts
            }
            BaseResult<BaseSemanticActivationMaintenanceResult>? failed = request switch
            {
                BaseSemanticActivationCompactRequest compact => ProcessCompactionPage(
                    current, plan, compact, deadline.Token),
                BaseSemanticActivationMigrateRequest migrate => ProcessMigrationPage(
                    current, plan, migrate, deadline.Token),
                BaseSemanticActivationRemoveRequest remove => ProcessRemoval(
                    current, plan, remove, deadline.Token),
                _ => SemanticAdminFailure<BaseSemanticActivationMaintenanceResult>(OperationStatus.ValidationFailed,
                    BaseSemanticActivationErrorCodes.Invalid, ErrorCategory.Validation),
            };
            if (failed is not null) return failed;
            BaseResult<BaseSemanticActivationMaintenanceResult>? prospectiveFailure =
                FinalizeMaintenanceAccounting(current, plan, request, identityKey);
            if (prospectiveFailure is not null) return prospectiveFailure;

            BeforeSemanticMaintenanceStateClone?.Invoke();
            InMemoryStoreState working = current.CloneForSemanticMaintenance();
            ApplyMaintenancePlan(working, plan, request);
            if (request is BaseSemanticActivationRemoveRequest
                && entry.Result.Disposition == BaseSemanticActivationMaintenanceDisposition.Completed)
            {
                foreach ((string key, InMemorySemanticMaintenanceEntry receipt) in
                    working.SemanticMaintenance.Where(pair =>
                        DefinitionEqual(pair.Value.Definition, request.Definition)).ToArray())
                {
                    working.SemanticMaintenance.Remove(key);
                    working.RemovedSemanticMaintenanceReceipts[key] = receipt.DeepClone();
                }
                working.RemovedSemanticMaintenanceReceipts[identityKey] = entry.DeepClone();
            }
            else
            {
                working.SemanticMaintenance[identityKey] = entry;
            }
            InMemorySemanticMaintenanceEntry retainedEntry =
                working.SemanticMaintenance.GetValueOrDefault(identityKey)
                ?? working.RemovedSemanticMaintenanceReceipts[identityKey];
            VerifyMaintenanceAccounting(working, retainedEntry, plan);
            Volatile.Write(ref _publishedState, working);
            _generation = checked(_generation + 1);
            return BaseProviderResultContract.Ok(CloneMaintenanceResult(entry.Result), OperationStatus.Updated);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return MaintenanceCancellationResult(request);
        }
        catch (OperationCanceledException)
        {
            return SemanticAdminFailure<BaseSemanticActivationMaintenanceResult>(OperationStatus.StoreError,
                BaseSemanticActivationErrorCodes.MaintenanceTimeout, ErrorCategory.Store);
        }
        catch (OverflowException)
        {
            return SemanticAdminFailure<BaseSemanticActivationMaintenanceResult>(OperationStatus.ValidationFailed,
                BaseSemanticActivationErrorCodes.BudgetExceeded, ErrorCategory.Validation);
        }
        catch (InvalidDataException)
        {
            Interlocked.Exchange(ref _semanticMaintenanceQuarantined, 1);
            return SemanticAdminFailure<BaseSemanticActivationMaintenanceResult>(OperationStatus.StoreError,
                BaseSemanticActivationErrorCodes.Corrupt, ErrorCategory.Store);
        }
        finally { _stateGate.Release(); }
    }

    private BaseResult<BaseSemanticActivationMaintenanceResult> MaintenanceCancellationResult(
        BaseSemanticActivationMaintenanceRequest request)
    {
        InMemoryStoreState state = Volatile.Read(ref _publishedState);
        string identityKey = MaintenanceIdentityKey(request.Identity);
        byte[] fingerprint = BaseSemanticActivationMaintenanceContract.RequestFingerprint(request).ToArray();
        if ((state.SemanticMaintenance.TryGetValue(identityKey, out InMemorySemanticMaintenanceEntry? existing)
                || state.RemovedSemanticMaintenanceReceipts.TryGetValue(identityKey, out existing))
            && CryptographicOperations.FixedTimeEquals(existing.Fingerprint, fingerprint)
            && existing.Result.Disposition == BaseSemanticActivationMaintenanceDisposition.InProgress)
            return BaseProviderResultContract.Ok(CloneMaintenanceResult(existing.Result));

        byte[] authority = OrderedRowsChecksum([]);
        var rolledBack = new BaseSemanticActivationMaintenanceResult
        {
            ProviderIncarnation = _semanticProviderIncarnation.ToArray().ToImmutableArray(),
            Disposition = BaseSemanticActivationMaintenanceDisposition.ConfirmedRolledBack,
            PreviousAuthorityGeneration = request.ExpectedSemanticAuthorityGeneration,
            ResultingAuthorityGeneration = request.ExpectedSemanticAuthorityGeneration,
            ExaminedRows = 0, ChangedRows = 0, CanonicalBytes = 0,
            AuthorityChecksum = authority.ToImmutableArray(), ResultChecksum = [],
            Checkpoint = null, ReceiptDisposition = null, CommitObservationChecksum = [],
        };
        ImmutableArray<byte> checksum = BaseSemanticActivationMaintenanceContract.ResultChecksum(
            rolledBack, authority);
        return BaseProviderResultContract.Ok(rolledBack with
        {
            ResultChecksum = checksum,
            CommitObservationChecksum = BaseSemanticActivationMaintenanceContract.CommitObservationChecksum(
                checksum.AsSpan()),
        });
    }

    private BaseResult<BaseSemanticActivationMaintenanceResult>? FinalizeMaintenanceAccounting(
        InMemoryStoreState current,
        InMemorySemanticMaintenancePlan plan,
        BaseSemanticActivationMaintenanceRequest request,
        string identityKey)
    {
        InMemorySemanticMaintenanceEntry entry = plan.Entry;
        PrepareMaintenancePublication(plan, request);
        plan.ReadUpperBoundary = entry.Result.Checkpoint?.After is { } checkpointBoundary
            ? CloneRecoveryBoundary(checkpointBoundary)
            : entry.ProcessedAuthorities.Count == 0
                ? plan.ReadLowerBoundary
                : RecoveryBoundary(SemanticAdminRows(current, request.Definition, plan)
                    .ElementAt(entry.ProcessedAuthorities.Count - 1));
        InMemorySemanticMaintenanceAccounting prior = entry.Accounting;
        long rows = entry.Result.ExaminedRows;
        long canonicalBytes = entry.Result.CanonicalBytes;
        long admittedRows = checked(rows - prior.Rows);
        long admittedBytes = checked(canonicalBytes - prior.CanonicalBytes);
        if (admittedRows < 0 || admittedBytes < 0)
            throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
        ChargePlannedPublicationOperations(current, plan, request);
        int pages = checked(prior.Pages + 1);
        int readIntervals = checked(prior.ReadIntervals + plan.PageReadIntervals);
        int indexOperations = checked(prior.IndexOperations + plan.PageIndexOperations);
        long intervalBytes = MeasureMaintenanceReadInterval(
            plan.ReadLowerBoundary, plan.ReadUpperBoundary);
        long evidenceBytes = checked(prior.EvidenceBytes + admittedBytes + intervalBytes);
        long currentReceiptBytes = checked(
            16L + System.Text.Encoding.UTF8.GetByteCount(identityKey)
            + entry.Fingerprint.LongLength
            + InMemorySemanticMaintenanceRetainedWork.MeasureResult(entry.Result));
        long receiptBytes = checked(prior.ReceiptBytes + currentReceiptBytes);
        long publishedSemanticRootBytes = MeasurePublishedSemanticRoot(current);
        long proposedSemanticRootBytes = MeasureProspectiveSemanticRoot(
            current, plan, request, identityKey);
        long planBytes = InMemorySemanticMaintenanceRetainedWork.MeasurePlan(plan);
        long currentTransientBytes = checked(publishedSemanticRootBytes + proposedSemanticRootBytes
            + planBytes + evidenceBytes + currentReceiptBytes + plan.MaximumMaterializedScanBytes);
        long transientBytes = Math.Max(prior.TransientBytes, currentTransientBytes);
        plan.ExpectedPublishedRootBytes = proposedSemanticRootBytes;
        plan.CurrentPlanBytes = planBytes;
        plan.CurrentReceiptBytes = currentReceiptBytes;
        plan.CurrentTransientBytes = currentTransientBytes;
        var accounting = new InMemorySemanticMaintenanceAccounting
        {
            Rows = rows, CanonicalBytes = canonicalBytes, Pages = pages,
            ReadIntervals = readIntervals, IndexOperations = indexOperations,
            EvidenceBytes = evidenceBytes, ReceiptBytes = receiptBytes,
            TransientBytes = transientBytes,
        };
        LastSemanticMaintenanceAccounting = accounting with { };
        BaseSemanticActivationExecutionLimits installed = InstalledMaintenanceExecutionLimits(request.Definition);
        BaseSemanticActivationCapability capability = SemanticActivationCapability;
        if (pages > request.Limits.MaximumPages || rows > request.Limits.MaximumRows
            || canonicalBytes > request.Limits.MaximumBytes
            || readIntervals > Math.Min(installed.MaximumReadIntervals, capability.MaximumReadIntervals)
            || indexOperations > Math.Min(installed.MaximumIndexOperations, capability.MaximumIndexOperations)
            || evidenceBytes > Math.Min(installed.MaximumEvidenceBytes, capability.MaximumEvidenceBytes)
            || receiptBytes > Math.Min(installed.MaximumReceiptBytes, capability.MaximumReceiptBytes)
            || transientBytes > Math.Min(installed.MaximumTransientBytes, capability.MaximumTransientBytes))
        {
            return SemanticAdminFailure<BaseSemanticActivationMaintenanceResult>(
                OperationStatus.ValidationFailed,
                BaseSemanticActivationErrorCodes.BudgetExceeded,
                ErrorCategory.Validation);
        }
        entry.Accounting = accounting;
        return null;
    }

    private static void ChargePlannedPublicationOperations(
        InMemoryStoreState current,
        InMemorySemanticMaintenancePlan plan,
        BaseSemanticActivationMaintenanceRequest request)
    {
        InMemorySemanticMaintenanceEntry entry = plan.Entry;
        plan.ChargeWrite(current.SemanticActivationSlots.Count
            + current.SemanticMaintenance.Count
            + current.RemovedSemanticMaintenanceReceipts.Count
            + current.RemovedSemanticDefinitions.Count
            + current.SemanticMigrationAuthorities.Count
            + current.SemanticMigrationHistory.Count
            + current.RemovedSemanticDefinitionAuthorities.Count
            + current.RemovedSemanticDefinitionHistory.Count);
        bool completed = entry.Result.Disposition == BaseSemanticActivationMaintenanceDisposition.Completed;
        bool changesAuthority = completed
            && entry.Result.ResultingAuthorityGeneration != entry.Result.PreviousAuthorityGeneration;
        if (changesAuthority)
        {
            plan.ChargeWrite(entry.StagedSlots.Count);
            plan.ChargeWrite(current.SemanticActivationSlots.Count);
            plan.ChargeWrite(); // semantic store authority replacement
            if (plan.RemovesDefinition) plan.ChargeWrite(3);
            if (plan.Migration is not null) plan.ChargeWrite(2);
        }
        if (request is BaseSemanticActivationRemoveRequest && completed)
        {
            int moved = current.SemanticMaintenance.Count(pair =>
                DefinitionEqual(pair.Value.Definition, request.Definition));
            plan.ChargeWrite(checked(moved * 2 + 1));
        }
        else
        {
            plan.ChargeWrite();
        }
    }

    private static long MeasurePublishedSemanticRoot(InMemoryStoreState state) => checked(
        InMemorySemanticMaintenanceRetainedWork.MeasureSlotDictionary(state.SemanticActivationSlots)
        + InMemorySemanticMaintenanceRetainedWork.MeasureMaintenanceDictionary(state.SemanticMaintenance)
        + InMemorySemanticMaintenanceRetainedWork.MeasureMaintenanceDictionary(
            state.RemovedSemanticMaintenanceReceipts)
        + InMemorySemanticMaintenanceRetainedWork.MeasureStoreAuthority(state.SemanticActivationAuthority)
        + InMemorySemanticMaintenanceRetainedWork.MeasureRemovedDefinitions(state.RemovedSemanticDefinitions)
        + InMemorySemanticMaintenanceRetainedWork.MeasureMigrationAuthorityDictionary(
            state.SemanticMigrationAuthorities)
        + InMemorySemanticMaintenanceRetainedWork.MeasureHistoricalAuthorityDictionary(
            state.SemanticMigrationHistory)
        + InMemorySemanticMaintenanceRetainedWork.MeasureRemovedDefinitionAuthorityDictionary(
            state.RemovedSemanticDefinitionAuthorities)
        + InMemorySemanticMaintenanceRetainedWork.MeasureHistoricalAuthorityDictionary(
            state.RemovedSemanticDefinitionHistory));

    private long MeasureProspectiveSemanticRoot(
        InMemoryStoreState current,
        InMemorySemanticMaintenancePlan plan,
        BaseSemanticActivationMaintenanceRequest request,
        string identityKey)
    {
        InMemorySemanticMaintenanceEntry entry = plan.Entry;
        long slots = 8;
        bool completed = entry.Result.Disposition == BaseSemanticActivationMaintenanceDisposition.Completed;
        bool changesAuthority = completed
            && entry.Result.ResultingAuthorityGeneration != entry.Result.PreviousAuthorityGeneration;
        BaseSemanticActivationStoreAuthorityRequirement? next = null;
        if (changesAuthority)
        {
            BaseSemanticActivationStoreAuthorityRequirement authority = current.SemanticActivationAuthority
                ?? throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
            next = authority with
            {
                SemanticAuthorityGeneration = checked(authority.SemanticAuthorityGeneration + 1),
                DefinitionSetChecksum = (plan.ReplacementDefinitionSetChecksum
                    ?? authority.DefinitionSetChecksum).ToArray().ToImmutableArray(),
            };
        }
        foreach ((string key, InMemorySemanticActivationSlot source) in current.SemanticActivationSlots)
        {
            InMemorySemanticActivationSlot selected = completed
                ? plan.Entry.StagedSlots.GetValueOrDefault(key) ?? source
                : source;
            InMemorySemanticActivationSlot measured = next is null ? selected : RebindSemanticSlot(selected, next);
            long slotBytes = InMemorySemanticMaintenanceRetainedWork.MeasureSlotEntry(key, measured);
            slots = checked(slots + slotBytes);
        }

        long active = 8;
        long removed = 8;
        bool removal = request is BaseSemanticActivationRemoveRequest && completed;
        foreach ((string key, InMemorySemanticMaintenanceEntry value) in current.SemanticMaintenance)
        {
            if (string.Equals(key, identityKey, StringComparison.Ordinal)) continue;
            if (removal && DefinitionEqual(value.Definition, request.Definition))
                removed = checked(removed + InMemorySemanticMaintenanceRetainedWork.MeasureEntry(key, value));
            else
                active = checked(active + InMemorySemanticMaintenanceRetainedWork.MeasureEntry(key, value));
        }
        foreach ((string key, InMemorySemanticMaintenanceEntry value) in current.RemovedSemanticMaintenanceReceipts)
        {
            if (string.Equals(key, identityKey, StringComparison.Ordinal)) continue;
            removed = checked(removed + InMemorySemanticMaintenanceRetainedWork.MeasureEntry(key, value));
        }
        long plannedEntry = InMemorySemanticMaintenanceRetainedWork.MeasureEntry(
            identityKey, entry, includeStaging: !completed);
        if (removal) removed = checked(removed + plannedEntry);
        else active = checked(active + plannedEntry);
        long storeAuthority = InMemorySemanticMaintenanceRetainedWork.MeasureStoreAuthority(next
            ?? current.SemanticActivationAuthority);
        long removedDefinitions = InMemorySemanticMaintenanceRetainedWork.MeasureRemovedDefinitions(
            current.RemovedSemanticDefinitions);
        long migrationAuthorities = InMemorySemanticMaintenanceRetainedWork.MeasureMigrationAuthorityDictionary(
            current.SemanticMigrationAuthorities);
        long migrationHistory = InMemorySemanticMaintenanceRetainedWork.MeasureHistoricalAuthorityDictionary(
            current.SemanticMigrationHistory);
        long removalAuthorities = InMemorySemanticMaintenanceRetainedWork.MeasureRemovedDefinitionAuthorityDictionary(
            current.RemovedSemanticDefinitionAuthorities);
        long removalHistory = InMemorySemanticMaintenanceRetainedWork.MeasureHistoricalAuthorityDictionary(
            current.RemovedSemanticDefinitionHistory);
        if (plan.MigrationAuthority is { } migrationAuthority && plan.Migration is { } migration)
        {
            string key = DefinitionKey(migration.From);
            migrationAuthorities = checked(migrationAuthorities + 8
                + 4L + Encoding.UTF8.GetByteCount(key)
                + InMemorySemanticMaintenanceRetainedWork.MeasureMigrationAuthority(migrationAuthority));
            migrationHistory = checked(migrationHistory + 8
                + 4L + Encoding.UTF8.GetByteCount(key)
                + InMemorySemanticMaintenanceRetainedWork.MeasureHistoricalAuthoritySequence(plan.HistoricalAuthority));
        }
        if (plan.RemovalAuthority is { } removalAuthority)
        {
            string key = DefinitionKey(request.Definition);
            removedDefinitions = checked(removedDefinitions + 8 + 4L + Encoding.UTF8.GetByteCount(key));
            removalAuthorities = checked(removalAuthorities + 8
                + 4L + Encoding.UTF8.GetByteCount(key)
                + InMemorySemanticMaintenanceRetainedWork.MeasureRemovedDefinitionAuthority(removalAuthority));
            removalHistory = checked(removalHistory + 8
                + 4L + Encoding.UTF8.GetByteCount(key)
                + InMemorySemanticMaintenanceRetainedWork.MeasureHistoricalAuthoritySequence(plan.HistoricalAuthority));
        }
        return checked(slots + active + removed + storeAuthority + removedDefinitions
            + migrationAuthorities + migrationHistory + removalAuthorities + removalHistory);
    }

    private void ApplyMaintenancePlan(
        InMemoryStoreState working,
        InMemorySemanticMaintenancePlan plan,
        BaseSemanticActivationMaintenanceRequest request)
    {
        InMemorySemanticMaintenanceEntry entry = plan.Entry;
        if (entry.Result.Disposition != BaseSemanticActivationMaintenanceDisposition.Completed)
            return;
        bool changesAuthority = entry.Result.ResultingAuthorityGeneration
            != entry.Result.PreviousAuthorityGeneration;
        if (!changesAuthority)
        {
            if (entry.StagedSlots.Count != 0 || plan.RemovesDefinition || plan.Migration is not null)
                throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
            return;
        }
        foreach ((string key, InMemorySemanticActivationSlot slot) in entry.StagedSlots)
            working.SemanticActivationSlots[key] = slot.DeepClone();
        BaseSemanticActivationStoreAuthorityRequirement authority = working.SemanticActivationAuthority
            ?? throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
        var next = authority with
        {
            SemanticAuthorityGeneration = checked(authority.SemanticAuthorityGeneration + 1),
            DefinitionSetChecksum = (plan.ReplacementDefinitionSetChecksum
                ?? authority.DefinitionSetChecksum).ToArray().ToImmutableArray(),
        };
        foreach ((string key, InMemorySemanticActivationSlot slot) in working.SemanticActivationSlots.ToArray())
            working.SemanticActivationSlots[key] = RebindSemanticSlot(slot, next);
        working.SemanticActivationAuthority = next;
        if (plan.RemovesDefinition)
        {
            working.RemovedSemanticDefinitions.Add(DefinitionKey(request.Definition));
            string key = DefinitionKey(request.Definition);
            if (plan.RemovalAuthority is null
                || !working.RemovedSemanticDefinitionAuthorities.TryAdd(key, plan.RemovalAuthority.DeepClone())
                || !working.RemovedSemanticDefinitionHistory.TryAdd(key,
                    plan.HistoricalAuthority.Select(static value => value.DeepClone()).ToImmutableArray()))
                throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
        }
        if (plan.Migration is { } migration && plan.MigrationAuthority is { } migrationAuthority)
        {
            string source = DefinitionKey(migration.From);
            if (!working.SemanticMigrationAuthorities.TryAdd(source, migrationAuthority with
                {
                    MigrationId = new string(migrationAuthority.MigrationId.AsSpan()),
                    From = CloneDefinition(migrationAuthority.From), To = CloneDefinition(migrationAuthority.To),
                    OrderedNegativeAuthorityChecksum = migrationAuthority.OrderedNegativeAuthorityChecksum.ToArray().ToImmutableArray(),
                    ReceiptChecksum = migrationAuthority.ReceiptChecksum.ToArray().ToImmutableArray(),
                    Checksum = migrationAuthority.Checksum.ToArray().ToImmutableArray(),
                }) || !working.SemanticMigrationHistory.TryAdd(source,
                    plan.HistoricalAuthority.Select(static value => value.DeepClone()).ToImmutableArray()))
                throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
        }
        entry.StagedSlots.Clear();
    }

    private static void VerifyMaintenanceAccounting(
        InMemoryStoreState proposed,
        InMemorySemanticMaintenanceEntry entry,
        InMemorySemanticMaintenancePlan plan)
    {
        long actualPublishedRootBytes = MeasurePublishedSemanticRoot(proposed);
        if (actualPublishedRootBytes != plan.ExpectedPublishedRootBytes
            || plan.CurrentTransientBytes > entry.Accounting.TransientBytes
            || entry.Result.ExaminedRows != entry.Accounting.Rows
            || entry.Result.CanonicalBytes != entry.Accounting.CanonicalBytes)
            throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
    }

    private BaseSemanticActivationExecutionLimits InstalledMaintenanceExecutionLimits(
        BaseSemanticActivationDefinitionKey definition)
    {
        BaseSemanticActivationKeyDefinition? installed = _options.SemanticActivations.FirstOrDefault(value =>
            value.Id == definition.Id && value.Version == definition.Version
            && CryptographicOperations.FixedTimeEquals(
                value.Checksum.AsSpan(), definition.Checksum.AsSpan()));
        installed ??= _options.SemanticActivationRemovals.Select(static value => value.From)
            .FirstOrDefault(value => value.Id == definition.Id && value.Version == definition.Version
                && CryptographicOperations.FixedTimeEquals(
                    value.Checksum.AsSpan(), definition.Checksum.AsSpan()));
        return installed?.Limits.Execution
            ?? throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
    }

    private static BaseSemanticActivationRecoveryBoundary? CloneRecoveryBoundary(
        BaseSemanticActivationRecoveryBoundary? value) => value is null ? null : value with
    {
        DefinitionId = new string(value.DefinitionId.AsSpan()),
        ScopeBindingId = value.ScopeBindingId.ToArray().ToImmutableArray(),
        Key = BaseSemanticActivationKeyDigest.Create(value.Key.ToArray()),
    };

    private static BaseSemanticActivationRecoveryBoundary RecoveryBoundary(SemanticAdminRow value) => new()
    {
        DefinitionId = new string(value.Definition.Id.AsSpan()),
        ScopeBindingId = value.Binding.ToArray().ToImmutableArray(),
        Key = BaseSemanticActivationKeyDigest.Create(value.Key),
    };

    private static long MeasureMaintenanceReadInterval(
        BaseSemanticActivationRecoveryBoundary? lower,
        BaseSemanticActivationRecoveryBoundary? upper)
    {
        var counter = new BaseCanonicalRetainedWork();
        counter.AddSequence(1);
        counter.AddContainer();
        counter.AddString("base.semanticActivation.maintenance");
        counter.AddBoolean();
        if (lower is not null) counter.Add(InMemorySemanticMaintenanceRetainedWork.MeasureBoundary(lower));
        counter.AddBoolean();
        if (upper is not null) counter.Add(InMemorySemanticMaintenanceRetainedWork.MeasureBoundary(upper));
        return counter.Bytes;
    }

    internal async ValueTask CorruptSemanticMaintenanceCheckpointForCertificationAsync(
        BaseMutationRequestIdentity identity,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identity);
        await _stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            InMemoryStoreState working = Volatile.Read(ref _publishedState).Clone();
            if (!working.SemanticMaintenance.TryGetValue(
                    MaintenanceIdentityKey(identity), out InMemorySemanticMaintenanceEntry? entry)
                || entry.Result.Checkpoint is not { } checkpoint)
                throw new InvalidOperationException("base.semanticActivation.certificationInvalid");
            byte[] checksum = checkpoint.Checksum.ToArray();
            checksum[0] ^= 0x80;
            entry.Result = entry.Result with
            {
                Checkpoint = checkpoint with { Checksum = checksum.ToImmutableArray() },
            };
            Volatile.Write(ref _publishedState, working);
            _generation = checked(_generation + 1);
        }
        finally
        {
            _stateGate.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask<BaseResult<BaseSemanticActivationMaintenanceResult>> ResolveAsync(
        BaseSemanticActivationMaintenanceResolutionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Identity is null || !ValidSemanticAdminDefinition(request.Definition)
            || request.ProviderIncarnation.Length != 32
            || !CryptographicOperations.FixedTimeEquals(request.ProviderIncarnation.AsSpan(), _semanticProviderIncarnation.AsSpan())
            || string.IsNullOrWhiteSpace(request.MaintenanceId) || request.RequestFingerprint.Length != 32
            || request.Deadline <= TimeSpan.Zero
            || !string.Equals(request.MaintenanceId, Convert.ToHexStringLower(request.RequestFingerprint.AsSpan()), StringComparison.Ordinal))
            return SemanticAdminFailure<BaseSemanticActivationMaintenanceResult>(OperationStatus.ValidationFailed,
                BaseSemanticActivationErrorCodes.Invalid, ErrorCategory.Validation);
        await _stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            InMemoryStoreState state = Volatile.Read(ref _publishedState);
            string identityKey = MaintenanceIdentityKey(request.Identity);
            if (!state.SemanticMaintenance.TryGetValue(identityKey, out InMemorySemanticMaintenanceEntry? entry)
                && !state.RemovedSemanticMaintenanceReceipts.TryGetValue(identityKey, out entry)
                || !DefinitionEqual(entry.Definition, request.Definition)
                || !CryptographicOperations.FixedTimeEquals(entry.Fingerprint, request.RequestFingerprint.AsSpan()))
                return SemanticAdminFailure<BaseSemanticActivationMaintenanceResult>(OperationStatus.StoreError,
                    BaseSemanticActivationErrorCodes.MaintenanceIndeterminate, ErrorCategory.Store);
            BaseSemanticActivationMaintenanceResult result = entry.Result.Disposition == BaseSemanticActivationMaintenanceDisposition.InProgress
                ? entry.Result
                : entry.Result with { Disposition = BaseSemanticActivationMaintenanceDisposition.Duplicate,
                    ReceiptDisposition = BaseMutationRequestDisposition.Duplicate };
            return BaseProviderResultContract.Ok(CloneMaintenanceResult(result));
        }
        finally { _stateGate.Release(); }
    }

    private bool ValidCheckpointForRequest(BaseSemanticActivationMaintenanceCheckpoint? checkpoint,
        BaseSemanticActivationMaintenanceRequest request, ReadOnlySpan<byte> fingerprint)
    {
        if (checkpoint is null || checkpoint.ProviderIncarnation.Length != 32
            || !CryptographicOperations.FixedTimeEquals(
                checkpoint.ProviderIncarnation.AsSpan(), _semanticProviderIncarnation.AsSpan())
            || checkpoint.CapturedStoreGeneration <= 0
            || checkpoint.CapturedDefinitionGeneration != request.ExpectedSemanticAuthorityGeneration
            || checkpoint.ExpectedAuthorityGeneration != request.ExpectedSemanticAuthorityGeneration
            || checkpoint.FenceToken.Length != 32 || !DefinitionEqual(checkpoint.Definition, request.Definition)
            || !CryptographicOperations.FixedTimeEquals(checkpoint.RequestFingerprint.AsSpan(), fingerprint)
            || !CryptographicOperations.FixedTimeEquals(
                checkpoint.Checksum.AsSpan(), BaseSemanticActivationMaintenanceContract.CheckpointChecksum(checkpoint).AsSpan()))
            return false;
        byte[] expectedFence = SemanticAdminHash("base.semanticActivation.inMemoryFence.v1\0",
            fingerprint.ToArray(), request.Definition.Checksum.ToArray());
        return CryptographicOperations.FixedTimeEquals(checkpoint.FenceToken.AsSpan(), expectedFence);
    }

    private static bool MaintenanceDefinitionsOverlap(InMemorySemanticMaintenanceEntry existing,
        BaseSemanticActivationMaintenanceRequest request)
    {
        static IEnumerable<BaseSemanticActivationDefinitionKey> Definitions(
            BaseSemanticActivationMaintenanceRequest value)
        {
            yield return value.Definition;
            if (value is BaseSemanticActivationMigrateRequest migration) yield return migration.Migration.To;
        }
        IEnumerable<BaseSemanticActivationDefinitionKey> existingDefinitions = existing.Kind == "migrate"
            && existing.TargetDefinition is { } target
                ? [existing.Definition, target]
                : [existing.Definition];
        return existingDefinitions.Any(left => Definitions(request).Any(right =>
            string.Equals(left.Id, right.Id, StringComparison.Ordinal)));
    }

    private static bool SemanticMaintenanceFencesDefinition(
        InMemoryStoreState state,
        BaseSemanticActivationDefinitionIdentity definition) =>
        state.SemanticMaintenance.Values.Any(entry =>
            entry.Result.Disposition == BaseSemanticActivationMaintenanceDisposition.InProgress
            && (string.Equals(entry.Definition.Id, definition.Id, StringComparison.Ordinal)
                || entry.TargetDefinition is { } target
                && string.Equals(target.Id, definition.Id, StringComparison.Ordinal)));

    private bool SemanticMaintenanceFencesActivation(
        InMemoryStoreState state,
        BaseActivationDefinitionKey activation)
    {
        HashSet<string> semanticDefinitionIds = _options.SemanticActivations
            .Where(definition => definition.Activation.Id == activation.Id
                && definition.Activation.Version == activation.Version
                && CryptographicOperations.FixedTimeEquals(
                    definition.Activation.Checksum.AsSpan(), activation.Checksum.AsSpan()))
            .Select(static definition => definition.Id)
            .ToHashSet(StringComparer.Ordinal);
        return semanticDefinitionIds.Count != 0 && state.SemanticMaintenance.Values.Any(entry =>
            entry.Result.Disposition == BaseSemanticActivationMaintenanceDisposition.InProgress
            && (semanticDefinitionIds.Contains(entry.Definition.Id)
                || entry.TargetDefinition is { } target
                && semanticDefinitionIds.Contains(target.Id)));
    }

    private bool SemanticMaintenanceFencesAnyActivation(
        InMemoryStoreState state,
        IEnumerable<BaseActivationDefinitionKey> activations) =>
        activations.Any(activation => SemanticMaintenanceFencesActivation(state, activation));

    private bool SemanticMaintenanceFencesSubjectContract(
        InMemoryStoreState state,
        string contractId,
        int contractVersion)
    {
        HashSet<string> semanticDefinitionIds = _options.SemanticActivations
            .Where(definition => definition.Compaction is BaseSemanticActivationSubjectRetirementCompaction compaction
                && compaction.SubjectContract.ContractId == contractId
                && compaction.SubjectContract.ContractVersion == contractVersion)
            .Select(static definition => definition.Id)
            .ToHashSet(StringComparer.Ordinal);
        return semanticDefinitionIds.Count != 0 && state.SemanticMaintenance.Values.Any(entry =>
            entry.Result.Disposition == BaseSemanticActivationMaintenanceDisposition.InProgress
            && (semanticDefinitionIds.Contains(entry.Definition.Id)
                || entry.TargetDefinition is { } target
                && semanticDefinitionIds.Contains(target.Id)));
    }

    private bool ValidMaintenanceRequest(BaseSemanticActivationMaintenanceRequest request) =>
        request.Identity is not null && ValidSemanticAdminDefinition(request.Definition)
        && request.ProviderIncarnation.Length == 32
        && CryptographicOperations.FixedTimeEquals(request.ProviderIncarnation.AsSpan(), _semanticProviderIncarnation.AsSpan())
        && request.ExpectedSemanticAuthorityGeneration > 0
        && request.Limits.PageSize is >= 1 and <= 256 && request.Limits.MaximumPages > 0
        && request.Limits.MaximumRows > 0 && request.Limits.MaximumBytes > 0
        && request.Limits.Deadline > TimeSpan.Zero
        && request.Limits.PageSize <= SemanticActivationCapability.MaximumMaintenancePageSize
        && request.Limits.Deadline <= SemanticActivationCapability.Deadlines.MaintenanceTimeout
        && request.Limits.MaximumBytes <= SemanticActivationCapability.MaximumTransientBytes
        && request.Limits.MaximumPages <= request.Limits.MaximumRows
        && request switch
        {
            BaseSemanticActivationCompactRequest compact => compact.ExpectedRetiredCount >= 0
                && compact.ExpectedRetiredChecksum.Length == 32
                && SemanticDefinitionSupportsCompaction(compact.Definition),
            BaseSemanticActivationMigrateRequest migrate => migrate.Migration is not null
                && _options.SemanticActivationMigrations.Any(value => value.Id == migrate.Migration.Id
                    && value.Version == migrate.Migration.Version
                    && CryptographicOperations.FixedTimeEquals(value.Checksum.AsSpan(), migrate.Migration.Checksum.AsSpan())),
            BaseSemanticActivationRemoveRequest remove => remove.RemovalAuthority is not null
                && remove.ExpectedLiveCount >= 0 && remove.ExpectedRetiredCount >= 0 && remove.ExpectedAbsenceCount >= 0
                && remove.ExpectedDefinitionStateChecksum.Length == 32 && remove.ExpectedAbsenceAuthorityChecksum.Length == 32,
            _ => false,
        };

    private bool SemanticDefinitionSupportsCompaction(BaseSemanticActivationDefinitionKey definition) =>
        _options.SemanticActivations.Any(value => value.Id == definition.Id
            && value.Version == definition.Version
            && CryptographicOperations.FixedTimeEquals(
                value.Checksum.AsSpan(), definition.Checksum.AsSpan())
            && value.Compaction is BaseSemanticActivationSubjectRetirementCompaction);

    private static string MaintenanceIdentityKey(BaseMutationRequestIdentity identity) =>
        string.Concat(identity.Scope, "\0", identity.Operation, "\0", identity.IdempotencyKey);

    private static string MaintenanceKind(BaseSemanticActivationMaintenanceRequest request) => request switch
    {
        BaseSemanticActivationCompactRequest => "compact",
        BaseSemanticActivationMigrateRequest => "migrate",
        BaseSemanticActivationRemoveRequest => "remove",
        _ => "invalid",
    };

    private BaseSemanticActivationMaintenanceCheckpoint CreateMaintenanceCheckpoint(
        BaseSemanticActivationMaintenanceRequest request, byte[] fingerprint,
        BaseSemanticActivationRecoveryBoundary? after, int pages, long rows, long bytes, byte[] rolling)
    {
        byte[] fence = SemanticAdminHash("base.semanticActivation.inMemoryFence.v1\0",
            fingerprint, request.Definition.Checksum.ToArray());
        var checkpoint = new BaseSemanticActivationMaintenanceCheckpoint
        {
            MaintenanceId = Convert.ToHexStringLower(fingerprint),
            ProviderIncarnation = _semanticProviderIncarnation.ToArray().ToImmutableArray(),
            CapturedStoreGeneration = Math.Max(1, _generation),
            CapturedDefinitionGeneration = request.ExpectedSemanticAuthorityGeneration,
            FenceToken = fence.ToImmutableArray(), OperationKind = MaintenanceKind(request),
            Definition = CloneDefinition(request.Definition),
            ExpectedAuthorityGeneration = request.ExpectedSemanticAuthorityGeneration,
            After = after is null ? null : after with
            {
                DefinitionId = new string(after.DefinitionId.AsSpan()),
                ScopeBindingId = after.ScopeBindingId.ToArray().ToImmutableArray(),
                Key = BaseSemanticActivationKeyDigest.Create(after.Key.ToArray()),
            },
            CompletedPages = pages, CompletedRows = rows, CompletedBytes = bytes,
            RollingChecksum = rolling.ToArray().ToImmutableArray(),
            RequestFingerprint = fingerprint.ToArray().ToImmutableArray(), Checksum = [],
        };
        return checkpoint with { Checksum = BaseSemanticActivationMaintenanceContract.CheckpointChecksum(checkpoint) };
    }

    private BaseSemanticActivationMaintenanceResult InProgressResult(
        BaseSemanticActivationMaintenanceRequest request,
        BaseSemanticActivationMaintenanceCheckpoint checkpoint) => new()
    {
        ProviderIncarnation = _semanticProviderIncarnation.ToArray().ToImmutableArray(),
        Disposition = BaseSemanticActivationMaintenanceDisposition.InProgress,
        PreviousAuthorityGeneration = request.ExpectedSemanticAuthorityGeneration,
        ResultingAuthorityGeneration = request.ExpectedSemanticAuthorityGeneration,
        ExaminedRows = checkpoint.CompletedRows, ChangedRows = 0,
        CanonicalBytes = checkpoint.CompletedBytes,
        AuthorityChecksum = checkpoint.RollingChecksum.ToArray().ToImmutableArray(),
        ResultChecksum = [], Checkpoint = checkpoint,
        ReceiptDisposition = BaseMutationRequestDisposition.Committed, CommitObservationChecksum = [],
    };

    private static byte[] OrderedRowsChecksum(IEnumerable<byte[]> rows)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData("base.semanticActivation.orderedRows.v1\0"u8);
        Span<byte> length = stackalloc byte[4];
        foreach (byte[] row in rows)
        {
            BinaryPrimitives.WriteInt32BigEndian(length, row.Length);
            hash.AppendData(length); hash.AppendData(row);
        }
        return hash.GetHashAndReset();
    }

    private static ImmutableArray<byte> MaintenanceReceiptChecksum(
        BaseMutationRequestIdentity identity, byte[] fingerprint,
        BaseSemanticActivationMaintenanceResult result) => SemanticAdminHash(
            "base.semanticActivation.maintenanceReceipt.v1\0",
            Encoding.UTF8.GetBytes(identity.Scope), Encoding.UTF8.GetBytes(identity.Operation),
            Encoding.UTF8.GetBytes(identity.IdempotencyKey), fingerprint,
            AdminInt64((int)result.Disposition), AdminInt64(result.PreviousAuthorityGeneration),
            AdminInt64(result.ResultingAuthorityGeneration), AdminInt64(result.ExaminedRows),
            AdminInt64(result.ChangedRows), AdminInt64(result.CanonicalBytes),
            result.ResultChecksum.ToArray()).ToImmutableArray();

    private static byte[] DefinitionStateChecksum(IEnumerable<SemanticAdminRow> rows)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData("base.semanticActivation.definitionState.v1\0"u8);
        foreach (SemanticAdminRow row in rows)
        {
            hash.AppendData(row.Binding); hash.AppendData(row.Key); hash.AppendData([(byte)row.State]);
            hash.AppendData(AdminInt64(row.Generation)); hash.AppendData(row.Authority);
        }
        return hash.GetHashAndReset();
    }

    private static InMemorySemanticActivationSlot RebindSemanticSlot(
        InMemorySemanticActivationSlot slot, BaseSemanticActivationStoreAuthorityRequirement requirement)
    {
        BaseSemanticActivationStoreAuthority store = BaseSemanticActivationEvidenceContract.CreateStoreAuthority(requirement);
        if (slot.Live is { } live)
        {
            BaseSemanticActivationLiveAuthority next = live with { StoreAuthority = store, Checksum = [] };
            next = next with { Checksum = BaseSemanticActivationEvidenceContract.LiveChecksum(next) };
            return slot.DeepClone() with { Live = next, Retired = null, Absent = null };
        }
        if (slot.Retired is { } retired)
        {
            BaseSemanticActivationRetirementAuthority next = retired with { StoreAuthority = store, Checksum = [] };
            next = next with { Checksum = BaseSemanticActivationEvidenceContract.RetirementChecksum(next) };
            return slot.DeepClone() with { Live = null, Retired = next, Absent = null };
        }
        if (slot.Absent is { } absent)
        {
            BaseSemanticActivationAbsenceAuthority next = absent with { StoreAuthority = store, Checksum = [] };
            next = next with { Checksum = BaseSemanticActivationEvidenceContract.AbsenceChecksum(next) };
            return slot.DeepClone() with { Live = null, Retired = null, Absent = next };
        }
        throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
    }

    private static BaseSemanticActivationDefinitionKey CloneDefinition(BaseSemanticActivationDefinitionKey value) => value with
    {
        Id = new string(value.Id.AsSpan()), Checksum = value.Checksum.ToArray().ToImmutableArray(),
    };

    private BaseSemanticActivationDefinitionIdentity SemanticDefinitionIdentity(
        BaseSemanticActivationDefinitionKey value)
    {
        BaseSemanticActivationKeyDefinition installed = _options.SemanticActivations.Single(definition =>
            definition.Id == value.Id && definition.Version == value.Version
            && CryptographicOperations.FixedTimeEquals(definition.Checksum.AsSpan(), value.Checksum.AsSpan()));
        return new BaseSemanticActivationDefinitionIdentity
        {
            Id = new string(value.Id.AsSpan()), Version = value.Version,
            Checksum = value.Checksum.ToArray().ToImmutableArray(),
            OwnerGeneration = _options.SemanticActivationOwnerGeneration,
            OwningModuleId = new string(installed.OwningModuleId.AsSpan()),
            RetirementOperation = installed.RetirementOperation with
            {
                OperationId = new string(installed.RetirementOperation.OperationId.AsSpan()),
                OperationChecksum = new string(installed.RetirementOperation.OperationChecksum.AsSpan()),
            },
        };
    }

    private static bool DefinitionEqual(BaseSemanticActivationDefinitionKey left,
        BaseSemanticActivationDefinitionKey right) => left.Id == right.Id && left.Version == right.Version
        && CryptographicOperations.FixedTimeEquals(left.Checksum.AsSpan(), right.Checksum.AsSpan());

    private static string DefinitionKey(BaseSemanticActivationDefinitionKey value) =>
        string.Concat(value.Id, "\0", value.Version.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "\0", Convert.ToHexString(value.Checksum.AsSpan()));

    private static BaseSemanticActivationMaintenanceResult CloneMaintenanceResult(
        BaseSemanticActivationMaintenanceResult value) => value with
    {
        ProviderIncarnation = value.ProviderIncarnation.ToArray().ToImmutableArray(),
        AuthorityChecksum = value.AuthorityChecksum.ToArray().ToImmutableArray(),
        ResultChecksum = value.ResultChecksum.ToArray().ToImmutableArray(),
        CommitObservationChecksum = value.CommitObservationChecksum.ToArray().ToImmutableArray(),
        Checkpoint = value.Checkpoint is null ? null : value.Checkpoint with
        {
            ProviderIncarnation = value.Checkpoint.ProviderIncarnation.ToArray().ToImmutableArray(),
            FenceToken = value.Checkpoint.FenceToken.ToArray().ToImmutableArray(),
            Definition = CloneDefinition(value.Checkpoint.Definition),
            After = value.Checkpoint.After is null ? null : value.Checkpoint.After with
            {
                DefinitionId = new string(value.Checkpoint.After.DefinitionId.AsSpan()),
                ScopeBindingId = value.Checkpoint.After.ScopeBindingId.ToArray().ToImmutableArray(),
                Key = BaseSemanticActivationKeyDigest.Create(value.Checkpoint.After.Key.ToArray()),
            },
            RollingChecksum = value.Checkpoint.RollingChecksum.ToArray().ToImmutableArray(),
            RequestFingerprint = value.Checkpoint.RequestFingerprint.ToArray().ToImmutableArray(),
            Checksum = value.Checkpoint.Checksum.ToArray().ToImmutableArray(),
        },
    };

    private InMemorySemanticMaintenanceEntry NewMaintenanceEntry(
        BaseSemanticActivationMaintenanceRequest request, byte[] fingerprint)
    {
        BaseSemanticActivationMaintenanceCheckpoint checkpoint = CreateMaintenanceCheckpoint(
            request, fingerprint, null, 0, 0, 0, OrderedRowsChecksum([]));
        return new InMemorySemanticMaintenanceEntry
        {
            Fingerprint = fingerprint.ToArray(), Kind = MaintenanceKind(request),
            Definition = CloneDefinition(request.Definition),
            TargetDefinition = request is BaseSemanticActivationMigrateRequest migration
                ? CloneDefinition(migration.Migration.To) : null,
            Result = InProgressResult(request, checkpoint),
            StagedSlots = new Dictionary<string, InMemorySemanticActivationSlot>(StringComparer.Ordinal),
            ProcessedAuthorities = [],
            ProcessedCanonicalBytes = [],
            Accounting = new InMemorySemanticMaintenanceAccounting
            {
                Rows = 0, CanonicalBytes = 0, Pages = 0, ReadIntervals = 0,
                IndexOperations = 0, EvidenceBytes = 0, ReceiptBytes = 0,
                TransientBytes = 0,
            },
        };
    }

    private BaseResult<BaseSemanticActivationMaintenanceResult>? ProcessCompactionPage(
        InMemoryStoreState current, InMemorySemanticMaintenancePlan plan,
        BaseSemanticActivationCompactRequest request, CancellationToken cancellationToken)
    {
        InMemorySemanticMaintenanceEntry entry = plan.Entry;
        SemanticAdminRow[] sourceRows = SemanticAdminRows(current, request.Definition, plan);
        SemanticAdminRow[] candidates = FilterAdminRows(sourceRows,
            static row => row.State == BaseSemanticActivationSlotState.Retired, plan);
        plan.ChargeRetainedTraversal(candidates.LongLength, 2); // count and ordered checksum
        byte[] expected = OrderedRowsChecksum(candidates.Select(static row => row.Authority));
        long candidateCount = candidates.LongLength;
        if (candidateCount != request.ExpectedRetiredCount
            || !CryptographicOperations.FixedTimeEquals(expected, request.ExpectedRetiredChecksum.AsSpan()))
            return SemanticAdminFailure<BaseSemanticActivationMaintenanceResult>(OperationStatus.Conflict,
                BaseSemanticActivationErrorCodes.CompactionBlocked, ErrorCategory.Conflict);
        if (candidateCount == 0)
        {
            CompleteNoChangeMaintenance(entry, request);
            return null;
        }
        int start = entry.ProcessedAuthorities.Count;
        foreach (SemanticAdminRow row in candidates.Skip(start).Take(request.Limits.PageSize))
        {
            cancellationToken.ThrowIfCancellationRequested();
            plan.ChargeLookup(); // slot lookup
            BaseSemanticActivationRetirementAuthority retired = JsonSerializer.Deserialize(row.Authority,
                HPDBaseJsonSerializerContext.Default.BaseSemanticActivationRetirementAuthority)
                ?? throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
            if (!CryptographicOperations.FixedTimeEquals(retired.Checksum.AsSpan(),
                    BaseSemanticActivationEvidenceContract.RetirementChecksum(retired).AsSpan())
                || !CompactionAuthoritySatisfied(current, row, retired, plan))
                return SemanticAdminFailure<BaseSemanticActivationMaintenanceResult>(OperationStatus.Conflict,
                    BaseSemanticActivationErrorCodes.CompactionBlocked, ErrorCategory.Conflict);
            var absence = new BaseSemanticActivationAbsenceAuthority
            {
                Key = BaseSemanticActivationKeyDigest.Create(retired.KeyDigest.ToArray()),
                Definition = SemanticDefinitionIdentity(retired.Definition),
                ScopeBindingId = retired.ScopeBindingId.ToArray().ToImmutableArray(),
                SubjectLifetime = retired.SubjectLifetime, FinalSlotGeneration = retired.SlotGeneration,
                AbsenceFloorGeneration = retired.SlotGeneration, RetirementPosition = retired.RetirementPosition,
                StoreAuthority = retired.StoreAuthority, Checksum = [],
            };
            absence = absence with { Checksum = BaseSemanticActivationEvidenceContract.AbsenceChecksum(absence) };
            entry.StagedSlots[row.SlotKey] = current.SemanticActivationSlots[row.SlotKey].DeepClone() with
            { Retired = null, Absent = absence };
            plan.ChargeWrite();
            entry.ProcessedAuthorities.Add(row.Authority.ToArray());
            entry.ProcessedCanonicalBytes.Add(MeasureStagedRow(row,
                SemanticAdminRow.From(row.SlotKey, entry.StagedSlots[row.SlotKey])));
        }
        return FinishOrCheckpoint(current, plan, request, candidates, sourceRows,
            request.ExpectedRetiredCount, null, false);
    }

    private bool CompactionAuthoritySatisfied(InMemoryStoreState state, SemanticAdminRow row,
        BaseSemanticActivationRetirementAuthority retired, InMemorySemanticMaintenancePlan plan)
    {
        // This is a deterministic conservative trace. It charges every point lookup
        // and the complete range that may influence authority, even when an earlier
        // predicate could short-circuit the physical traversal.
        plan.ChargeLookup(3); // activation, prune evidence, installed definition
        plan.ChargeScan(state.SemanticActivationScopes.Count);
        plan.ChargeScan(state.SubjectTerminals.Count);
        plan.ChargeScan(state.SubjectLifecycleMemberships.Count);
        plan.ChargeLookup(checked(state.SubjectLifecycleMemberships.Count * 2)); // fact and possible checkpoint
        plan.ChargeScan(state.Receipts.Count);
        plan.ChargeLookup(); // retained receipt floor
        if (retired.SubjectLifetime is not { } lifetime
            || state.Activations.ContainsKey(retired.ActivationId)
            || !state.ActivationPruneFloors.TryGetValue(retired.ActivationId, out BaseActivationPruneEvidence? prune)
            || !BaseActivationPruneEvidenceContract.IsValid(prune))
            return false;
        BaseSemanticActivationKeyDefinition? installed = _options.SemanticActivations.SingleOrDefault(value =>
            value.Id == retired.Definition.Id && value.Version == retired.Definition.Version
            && CryptographicOperations.FixedTimeEquals(value.Checksum.AsSpan(), retired.Definition.Checksum.AsSpan()));
        if (installed is null
            || prune.Definition.Id != installed.Activation.Id
            || prune.Definition.Version != installed.Activation.Version
            || !CryptographicOperations.FixedTimeEquals(
                prune.Definition.Checksum.AsSpan(), installed.Activation.Checksum.AsSpan())
            || !BaseSemanticActivationEvidenceContract.PruneEvidenceDominatesRetirement(prune, retired)
            || prune.ApplicationId != _options.SemanticActivationApplicationId
            || prune.LogicalStoreId != _options.StoreId || prune.StoreInstanceId != _options.StoreId
            || prune.RestoreEpoch != 0)
            return false;
        byte[] publication = SHA256.HashData(Encoding.UTF8.GetBytes(
            $"base.activation.publicationAuthority.v1\0{prune.ApplicationId}\n{prune.LogicalStoreId}\n{prune.StoreInstanceId}\n{prune.RestoreEpoch}\n{prune.PruneAuthorityGeneration}"));
        if (!CryptographicOperations.FixedTimeEquals(
                publication, prune.PublicationAuthorityChecksum.AsSpan())
            || !row.Binding.AsSpan().SequenceEqual(lifetime.ScopeBindingId.AsSpan())
            || !row.Binding.AsSpan().SequenceEqual(retired.ScopeBindingId.AsSpan()))
            return false;
        if (!state.SemanticActivationScopes.Values.Any(binding =>
                binding.BindingId.AsSpan().SequenceEqual(row.Binding)
                && binding.Checksum.Length == 32))
            return false;

        InMemorySubjectTerminalState? terminal = state.SubjectTerminals.Values.SingleOrDefault(value =>
            value.ContractId == lifetime.ContractId && value.ContractVersion == lifetime.ContractVersion
            && value.SubjectId.Equals(lifetime.SubjectId)
            && value.AuthorityEpoch.Equals(lifetime.AuthorityEpoch)
            && value.Incarnation.Equals(lifetime.Incarnation)
            && value.LifetimeGeneration == lifetime.Incarnation.LifetimeGeneration
            && value.RetiredPosition >= retired.RetirementPosition);
        if (terminal is null || terminal.RestoreEpoch != 0
            || !string.Equals(terminal.ReceiptChecksum, BaseSubjectTerminalIntegrity.Compute(
                terminal.ContractId, terminal.ContractVersion, terminal.SubjectId, terminal.Scope,
                terminal.AuthorityEpoch, terminal.Incarnation, terminal.LifetimeGeneration,
                terminal.SubjectSequence, new BaseMutationJournalPosition(terminal.RetiredPosition),
                terminal.ContractStateGeneration, terminal.RestoreEpoch), StringComparison.Ordinal))
            return false;

        BaseProtectedSubjectScope protectedScope = _subjectScopes.Protect(
            terminal.Scope, _subjectScopeProtectionKey);
        foreach (InMemorySubjectLifecycleMembershipRow membership in state.SubjectLifecycleMemberships)
        {
            InMemorySubjectLifecycleFactRow fact = state.SubjectLifecycleFacts[membership.FactIndex];
            if (fact.Fact.ContractId != lifetime.ContractId
                || fact.Fact.ContractVersion != lifetime.ContractVersion
                || !fact.Boundary.SubjectId.Equals(lifetime.SubjectId)
                || !fact.Boundary.AuthorityEpoch.Equals(lifetime.AuthorityEpoch)
                || !fact.Boundary.Incarnation.Equals(lifetime.Incarnation)
                || !membership.Scope.IndexDigest.AsSpan().SequenceEqual(protectedScope.IndexDigest))
                continue;
            string checkpointKey = ProtectedScopeKey(
                membership.ConsumerId, membership.ConsumerVersion, membership.Scope);
            if (!state.SubjectLifecycleCheckpoints.TryGetValue(
                    checkpointKey, out InMemorySubjectLifecycleCheckpointState? checkpoint)
                || !checkpoint.Overtaken && (checkpoint.Through is null
                    || CompareBoundary(checkpoint.Through, fact.Boundary) < 0))
                return false;
        }

        bool retainedReceiptFloor = state.Receipts.Values.Any(receipt =>
        {
            BaseSemanticActivationReceiptEvidence? evidence = receipt.Result.ModuleMutation?.SemanticActivation;
            return receipt.ExpiresAt <= _timeProvider.GetUtcNow() && evidence is
                { Operation: BaseSemanticActivationOperationKind.Retire,
                  State: BaseSemanticActivationSlotState.Retired }
                && evidence.DefinitionId == retired.Definition.Id
                && evidence.DefinitionVersion == retired.Definition.Version
                && CryptographicOperations.FixedTimeEquals(
                    evidence.DefinitionChecksum.AsSpan(), retired.Definition.Checksum.AsSpan())
                && evidence.Key.Equals(retired.KeyDigest)
                && evidence.SlotGeneration == retired.SlotGeneration
                && CryptographicOperations.FixedTimeEquals(
                    evidence.SlotChecksum.AsSpan(), retired.Checksum.AsSpan())
                && evidence.Checksum.Length == 32
                && CryptographicOperations.FixedTimeEquals(
                    BaseSemanticActivationEvidenceContract.ReceiptChecksum(evidence).AsSpan(),
                    evidence.Checksum.AsSpan());
        });
        if (retainedReceiptFloor) return true;
        return state.ExpiredSemanticRetirementReceiptFloors.Contains(
            SemanticRetirementReceiptFloorKey(retired.Definition, retired.KeyDigest,
                retired.SlotGeneration, retired.Checksum));
    }

    private static string SemanticRetirementReceiptFloorKey(
        BaseSemanticActivationReceiptEvidence evidence) =>
        SemanticRetirementReceiptFloorKey(new BaseSemanticActivationDefinitionKey
        {
            Id = evidence.DefinitionId,
            Version = evidence.DefinitionVersion,
            Checksum = evidence.DefinitionChecksum,
        }, evidence.Key, evidence.SlotGeneration, evidence.SlotChecksum);

    private static string SemanticRetirementReceiptFloorKey(
        BaseSemanticActivationDefinitionKey definition,
        BaseSemanticActivationKeyDigest key,
        long slotGeneration,
        ImmutableArray<byte> slotChecksum) => string.Concat(
        definition.Id, "\0",
        definition.Version.ToString(System.Globalization.CultureInfo.InvariantCulture), "\0",
        Convert.ToHexString(definition.Checksum.AsSpan()), "\0",
        Convert.ToHexString(key.ToArray()), "\0",
        slotGeneration.ToString(System.Globalization.CultureInfo.InvariantCulture), "\0",
        Convert.ToHexString(slotChecksum.AsSpan()));

    private BaseResult<BaseSemanticActivationMaintenanceResult>? ProcessMigrationPage(
        InMemoryStoreState current, InMemorySemanticMaintenancePlan plan,
        BaseSemanticActivationMigrateRequest request, CancellationToken cancellationToken)
    {
        InMemorySemanticMaintenanceEntry entry = plan.Entry;
        BaseSemanticActivationMigrationDefinition migration = BaseSemanticActivationMigrationContract.Seal(request.Migration);
        if (!DefinitionEqual(request.Definition, migration.From))
            return SemanticAdminFailure<BaseSemanticActivationMaintenanceResult>(OperationStatus.Conflict,
                BaseSemanticActivationErrorCodes.MigrationBlocked, ErrorCategory.Conflict);
        if (!SemanticDefinitionsMigratable(migration))
            return SemanticAdminFailure<BaseSemanticActivationMaintenanceResult>(OperationStatus.Conflict,
                BaseSemanticActivationErrorCodes.MigrationBlocked, ErrorCategory.Conflict);
        SemanticAdminRow[] sourceRows = SemanticAdminRows(current, migration.From, plan);
        SemanticAdminRow[] candidates = FilterAdminRows(sourceRows,
            static row => row.State == BaseSemanticActivationSlotState.Live, plan);
        long candidateCount = candidates.LongLength;
        int start = entry.ProcessedAuthorities.Count;
        foreach (SemanticAdminRow row in candidates.Skip(start).Take(request.Limits.PageSize))
        {
            cancellationToken.ThrowIfCancellationRequested();
            plan.ChargeLookup();
            InMemorySemanticActivationSlot source = current.SemanticActivationSlots[row.SlotKey];
            InMemorySemanticActivationSlot replacement = source.DeepClone();
            if (source.Live is { } live)
            {
                BaseSemanticActivationLiveAuthority next = live with
                {
                    Definition = SemanticDefinitionIdentity(migration.To), Checksum = [],
                };
                next = next with { Checksum = BaseSemanticActivationEvidenceContract.LiveChecksum(next) };
                replacement = replacement with { Live = next };
            }
            else if (source.Retired is { } retired)
            {
                BaseSemanticActivationRetirementAuthority next = retired with
                    { Definition = CloneDefinition(migration.To), Checksum = [] };
                next = next with { Checksum = BaseSemanticActivationEvidenceContract.RetirementChecksum(next) };
                replacement = replacement with { Retired = next };
            }
            else if (source.Absent is { } absent)
            {
                BaseSemanticActivationAbsenceAuthority next = absent with
                {
                    Definition = SemanticDefinitionIdentity(migration.To), Checksum = [],
                };
                next = next with { Checksum = BaseSemanticActivationEvidenceContract.AbsenceChecksum(next) };
                replacement = replacement with { Absent = next };
            }
            entry.StagedSlots[row.SlotKey] = replacement;
            plan.ChargeWrite();
            entry.ProcessedAuthorities.Add(row.Authority.ToArray());
            entry.ProcessedCanonicalBytes.Add(MeasureStagedRow(row,
                SemanticAdminRow.From(row.SlotKey, replacement)));
        }
        return FinishOrCheckpoint(current, plan, request, candidates, sourceRows,
            candidateCount, migration, false);
    }

    private BaseResult<BaseSemanticActivationMaintenanceResult>? ProcessRemoval(
        InMemoryStoreState current, InMemorySemanticMaintenancePlan plan,
        BaseSemanticActivationRemoveRequest request, CancellationToken cancellationToken)
    {
        InMemorySemanticMaintenanceEntry entry = plan.Entry;
        cancellationToken.ThrowIfCancellationRequested();
        if (!DefinitionEqual(request.Definition, new BaseSemanticActivationDefinitionKey
            {
                Id = request.RemovalAuthority.From.Id,
                Version = request.RemovalAuthority.From.Version,
                Checksum = request.RemovalAuthority.From.Checksum,
            }))
            return SemanticAdminFailure<BaseSemanticActivationMaintenanceResult>(OperationStatus.ValidationFailed,
                BaseSemanticActivationErrorCodes.Invalid, ErrorCategory.Validation);
        SemanticAdminRow[] rows = SemanticAdminRows(current, request.Definition, plan);
        // Three counts, definition checksum, negative checksum, history copy, and
        // processed-authority copy each traverse the retained ordered page graph.
        plan.ChargeRetainedTraversal(rows.LongLength, 7);
        long live = rows.LongCount(static row => row.State == BaseSemanticActivationSlotState.Live);
        long retired = rows.LongCount(static row => row.State == BaseSemanticActivationSlotState.Retired);
        long absent = rows.LongCount(static row => row.State == BaseSemanticActivationSlotState.CompactedAbsent);
        byte[] definitionState = DefinitionStateChecksum(rows);
        byte[] negative = OrderedRowsChecksum(rows.Where(static row => row.State is
                BaseSemanticActivationSlotState.Retired or BaseSemanticActivationSlotState.CompactedAbsent)
            .Select(static row => SemanticAdminHash("base.semanticActivation.historicalNegativeRow.v1\0",
                row.Binding, row.Key, AdminInt64((int)row.State), row.Authority)));
        if (live != request.ExpectedLiveCount || retired != request.ExpectedRetiredCount
            || absent != request.ExpectedAbsenceCount || live != 0 || retired != 0
            || !CryptographicOperations.FixedTimeEquals(definitionState, request.ExpectedDefinitionStateChecksum.AsSpan())
            || !CryptographicOperations.FixedTimeEquals(negative, request.ExpectedAbsenceAuthorityChecksum.AsSpan())
            || !_options.SemanticActivationRemovals.Any(value => value.Id == request.RemovalAuthority.Id
                && value.Version == request.RemovalAuthority.Version
                && CryptographicOperations.FixedTimeEquals(value.Checksum.AsSpan(), request.RemovalAuthority.Checksum.AsSpan()))
            || !RemovalDependenciesSatisfied(current, entry, request, plan))
        {
            return SemanticAdminFailure<BaseSemanticActivationMaintenanceResult>(OperationStatus.Conflict,
                BaseSemanticActivationErrorCodes.RemovalBlocked, ErrorCategory.Conflict);
        }
        ImmutableArray<InMemorySemanticHistoricalAuthority> history = rows.Select(static row =>
            new InMemorySemanticHistoricalAuthority(row.Binding.ToArray(), row.Key.ToArray(),
                row.State, row.Authority.ToArray())).ToImmutableArray();
        foreach (SemanticAdminRow row in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            entry.ProcessedAuthorities.Add(row.Authority.ToArray());
            entry.ProcessedCanonicalBytes.Add(checked(
                row.Binding.LongLength + row.Key.LongLength + row.Authority.LongLength));
        }
        CompleteMaintenanceEntry(current, entry, request);
        plan.ReplacementDefinitionSetChecksum = request.RemovalAuthority.ResultingDefinitionSetChecksum;
        plan.RemovesDefinition = true;
        plan.HistoricalAuthority = history;
        return null;
    }

    private bool RemovalDependenciesSatisfied(InMemoryStoreState state,
        InMemorySemanticMaintenanceEntry current, BaseSemanticActivationRemoveRequest request,
        InMemorySemanticMaintenancePlan plan)
    {
        plan.ChargeScan(state.SemanticMaintenance.Count);
        plan.ChargeScan(state.SemanticActivationSlots.Count);
        if (state.SemanticMaintenance.Values.Any(value => !ReferenceEquals(value, current)
                && value.Result.Disposition is BaseSemanticActivationMaintenanceDisposition.InProgress
                    or BaseSemanticActivationMaintenanceDisposition.Indeterminate
                && (DefinitionEqual(value.Definition, request.Definition)
                    || value.TargetDefinition is not null
                    && DefinitionEqual(value.TargetDefinition, request.Definition)))
            || state.SemanticActivationSlots.Values.Any(slot => slot.Live is { } live
                && live.Definition.Id == request.Definition.Id
                || slot.Retired is { } retired
                && retired.Definition.Id == request.Definition.Id))
            return false;
        DateTimeOffset now = _timeProvider.GetUtcNow();
        plan.ChargeScan(state.Receipts.Count);
        if (state.Receipts.Values.Any(receipt => receipt.ExpiresAt > now
            && receipt.Result.ModuleMutation?.SemanticActivation is { } semantic
            && semantic.DefinitionId == request.Definition.Id
            && semantic.DefinitionVersion == request.Definition.Version
            && CryptographicOperations.FixedTimeEquals(
                semantic.DefinitionChecksum.AsSpan(), request.Definition.Checksum.AsSpan())))
            return false;
        plan.ChargeScan(state.SemanticActivationSlots.Count);
        return state.SemanticActivationSlots.Values
            .Where(slot => slot.Absent is { } absence
                && absence.Definition.Id == request.Definition.Id
                && absence.Definition.Version == request.Definition.Version
                && CryptographicOperations.FixedTimeEquals(
                    absence.Definition.Checksum.AsSpan(), request.Definition.Checksum.AsSpan()))
            .All(slot => slot.Absent is { } absence
                && absence.Checksum.Length == 32
                && CryptographicOperations.FixedTimeEquals(
                    absence.Checksum.AsSpan(),
                    BaseSemanticActivationEvidenceContract.AbsenceChecksum(absence).AsSpan()));
    }

    private void PrepareMaintenancePublication(InMemorySemanticMaintenancePlan plan,
        BaseSemanticActivationMaintenanceRequest request)
    {
        InMemorySemanticMaintenanceEntry entry = plan.Entry;
        if (entry.Result.Disposition != BaseSemanticActivationMaintenanceDisposition.Completed
            || entry.Result.ResultingAuthorityGeneration == entry.Result.PreviousAuthorityGeneration)
            return;
        if (request is BaseSemanticActivationRemoveRequest removal)
            plan.RemovalAuthority ??= CreateRemovalAuthority(entry, removal);
        if (plan.Migration is { } migration)
            plan.MigrationAuthority ??= CreateMigrationAuthority(entry, request, migration,
                plan.MigrationSourceLive, plan.MigrationSourceRetired,
                plan.MigrationSourceAbsent, plan.HistoricalAuthority);
    }

    private static InMemorySemanticRemovedDefinitionAuthority CreateRemovalAuthority(
        InMemorySemanticMaintenanceEntry entry, BaseSemanticActivationRemoveRequest request)
    {
        ImmutableArray<byte> receipt = MaintenanceReceiptChecksum(
            request.Identity, entry.Fingerprint, entry.Result);
        byte[] checksum = SemanticAdminHash("base.semanticActivation.removedDefinition.v1\0",
            Encoding.UTF8.GetBytes(request.Definition.Id), AdminInt64(request.Definition.Version),
            request.Definition.Checksum.ToArray(), request.RemovalAuthority.Checksum.ToArray(),
            AdminInt64(request.ExpectedAbsenceCount), request.ExpectedAbsenceAuthorityChecksum.ToArray(),
            AdminInt64(entry.Result.ResultingAuthorityGeneration), receipt.ToArray());
        return new InMemorySemanticRemovedDefinitionAuthority(
            CloneDefinition(request.Definition),
            BaseSemanticActivationRemovalAuthorityContract.Seal(request.RemovalAuthority),
            request.ExpectedAbsenceCount, request.ExpectedAbsenceAuthorityChecksum.ToArray(),
            entry.Result.ResultingAuthorityGeneration, receipt.ToArray(), checksum);
    }

    private BaseResult<BaseSemanticActivationMaintenanceResult>? FinishOrCheckpoint(
        InMemoryStoreState current, InMemorySemanticMaintenancePlan plan,
        BaseSemanticActivationMaintenanceRequest request, SemanticAdminRow[] operationRows,
        SemanticAdminRow[] sourceRows, long changedRows,
        BaseSemanticActivationMigrationDefinition? migration, bool removed)
    {
        InMemorySemanticMaintenanceEntry entry = plan.Entry;
        long totalRows = operationRows.LongLength;
        long bytes = entry.ProcessedCanonicalBytes.Sum();
        if (entry.ProcessedAuthorities.Count > request.Limits.MaximumRows || bytes > request.Limits.MaximumBytes)
            return SemanticAdminFailure<BaseSemanticActivationMaintenanceResult>(OperationStatus.ValidationFailed,
                BaseSemanticActivationErrorCodes.BudgetExceeded, ErrorCategory.Validation);
        int completedPages = entry.ProcessedAuthorities.Count == 0 ? 0
            : (entry.ProcessedAuthorities.Count + request.Limits.PageSize - 1) / request.Limits.PageSize;
        if (entry.ProcessedAuthorities.Count < totalRows)
        {
            if (completedPages >= request.Limits.MaximumPages)
                return SemanticAdminFailure<BaseSemanticActivationMaintenanceResult>(OperationStatus.ValidationFailed,
                    BaseSemanticActivationErrorCodes.BudgetExceeded, ErrorCategory.Validation);
            plan.ChargeLookup();
            SemanticAdminRow last = operationRows[entry.ProcessedAuthorities.Count - 1];
            var after = new BaseSemanticActivationRecoveryBoundary
            {
                DefinitionId = request.Definition.Id, ScopeBindingId = last.Binding.ToImmutableArray(),
                Key = BaseSemanticActivationKeyDigest.Create(last.Key),
            };
            BaseSemanticActivationMaintenanceCheckpoint checkpoint = CreateMaintenanceCheckpoint(request,
                entry.Fingerprint, after, completedPages, entry.ProcessedAuthorities.Count, bytes,
                OrderedRowsChecksum(entry.ProcessedAuthorities));
            entry.Result = InProgressResult(request, checkpoint);
            return null;
        }
        ImmutableArray<InMemorySemanticHistoricalAuthority> negativeHistory = [];
        long sourceLive = 0;
        if (migration is not null)
        {
            plan.ChargeRetainedTraversal(sourceRows.LongLength, 2); // negative history and live count
            negativeHistory = sourceRows
                .Where(static row => row.State is BaseSemanticActivationSlotState.Retired
                    or BaseSemanticActivationSlotState.CompactedAbsent)
                .Select(static row => new InMemorySemanticHistoricalAuthority(
                    row.Binding.ToArray(), row.Key.ToArray(), row.State, row.Authority.ToArray()))
                .ToImmutableArray();
            sourceLive = sourceRows.LongCount(static row => row.State == BaseSemanticActivationSlotState.Live);
            plan.ObserveSimultaneousMaterializedScans(MeasureAdminRows(sourceRows),
                MeasureHistoricalAuthorities(negativeHistory));
        }
        long sourceRetired = negativeHistory.LongCount(static row => row.State == BaseSemanticActivationSlotState.Retired);
        long sourceAbsent = negativeHistory.LongCount(static row => row.State == BaseSemanticActivationSlotState.CompactedAbsent);
        CompleteMaintenanceEntry(current, entry, request);
        entry.Result = entry.Result with { ChangedRows = changedRows };
        if (migration is not null)
        {
            plan.Migration = migration;
            plan.MigrationSourceLive = sourceLive;
            plan.MigrationSourceRetired = sourceRetired;
            plan.MigrationSourceAbsent = sourceAbsent;
            plan.HistoricalAuthority = negativeHistory;
        }
        return null;
    }

    private bool SemanticDefinitionsMigratable(BaseSemanticActivationMigrationDefinition migration)
    {
        BaseSemanticActivationKeyDefinition? from = _options.SemanticActivations.SingleOrDefault(value =>
            DefinitionEqual(new BaseSemanticActivationDefinitionKey
            {
                Id = value.Id, Version = value.Version, Checksum = value.Checksum,
            }, migration.From));
        BaseSemanticActivationKeyDefinition? to = _options.SemanticActivations.SingleOrDefault(value =>
            DefinitionEqual(new BaseSemanticActivationDefinitionKey
            {
                Id = value.Id, Version = value.Version, Checksum = value.Checksum,
            }, migration.To));
        return from is not null && to is not null
            && from.Id == to.Id && from.OwningApplicationId == to.OwningApplicationId
            && from.OwningModuleId == to.OwningModuleId && from.ScopeKind == to.ScopeKind
            && from.RequestTypeId == to.RequestTypeId
            && from.RequestSerializerChecksum.AsSpan().SequenceEqual(to.RequestSerializerChecksum.AsSpan())
            && from.KeyExpressionChecksum.AsSpan().SequenceEqual(to.KeyExpressionChecksum.AsSpan());
    }

    private BaseSemanticActivationDefinitionMigrationAuthority CreateMigrationAuthority(
        InMemorySemanticMaintenanceEntry entry, BaseSemanticActivationMaintenanceRequest request,
        BaseSemanticActivationMigrationDefinition migration, long live, long retired, long absent,
        ImmutableArray<InMemorySemanticHistoricalAuthority> negativeHistory)
    {
        byte[] negativeChecksum = OrderedRowsChecksum(negativeHistory.Select(static row =>
            SemanticAdminHash("base.semanticActivation.historicalNegativeRow.v1\0",
                row.ScopeBindingId, row.KeyDigest, AdminInt64((int)row.State), row.CanonicalAuthority)));
        ImmutableArray<byte> receipt = MaintenanceReceiptChecksum(request.Identity, entry.Fingerprint, entry.Result);
        var authority = new BaseSemanticActivationDefinitionMigrationAuthority
        {
            MigrationId = new string(migration.Id.AsSpan()), MigrationVersion = migration.Version,
            From = CloneDefinition(migration.From), To = CloneDefinition(migration.To),
            ExpectedLiveCount = live, ExpectedRetiredCount = retired, ExpectedAbsenceCount = absent,
            OrderedNegativeAuthorityChecksum = negativeChecksum.ToImmutableArray(),
            PublicationGeneration = entry.Result.ResultingAuthorityGeneration,
            ReceiptChecksum = receipt, Checksum = [],
        };
        authority = authority with
        {
            Checksum = BaseSemanticActivationMigrationAuthorityContract.Checksum(authority),
        };
        return authority;
    }

    private void CompleteMaintenanceEntry(InMemoryStoreState current,
        InMemorySemanticMaintenanceEntry entry,
        BaseSemanticActivationMaintenanceRequest request)
    {
        BaseSemanticActivationStoreAuthorityRequirement authority = current.SemanticActivationAuthority
            ?? throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
        long resultingGeneration = checked(authority.SemanticAuthorityGeneration + 1);
        byte[] authorityChecksum = OrderedRowsChecksum(entry.ProcessedAuthorities);
        long bytes = entry.ProcessedCanonicalBytes.Sum();
        var result = new BaseSemanticActivationMaintenanceResult
        {
            ProviderIncarnation = _semanticProviderIncarnation.ToArray().ToImmutableArray(),
            Disposition = BaseSemanticActivationMaintenanceDisposition.Completed,
            PreviousAuthorityGeneration = request.ExpectedSemanticAuthorityGeneration,
            ResultingAuthorityGeneration = resultingGeneration,
            ExaminedRows = entry.ProcessedAuthorities.Count, ChangedRows = entry.ProcessedAuthorities.Count,
            CanonicalBytes = bytes, AuthorityChecksum = authorityChecksum.ToImmutableArray(),
            ResultChecksum = [], Checkpoint = null,
            ReceiptDisposition = BaseMutationRequestDisposition.Committed, CommitObservationChecksum = [],
        };
        ImmutableArray<byte> resultChecksum = BaseSemanticActivationMaintenanceContract.ResultChecksum(result, authorityChecksum);
        entry.Result = result with { ResultChecksum = resultChecksum,
            CommitObservationChecksum = BaseSemanticActivationMaintenanceContract.CommitObservationChecksum(resultChecksum.AsSpan()) };
    }

    private void CompleteNoChangeMaintenance(InMemorySemanticMaintenanceEntry entry,
        BaseSemanticActivationMaintenanceRequest request)
    {
        byte[] authorityChecksum = OrderedRowsChecksum([]);
        var result = new BaseSemanticActivationMaintenanceResult
        {
            ProviderIncarnation = _semanticProviderIncarnation.ToArray().ToImmutableArray(),
            Disposition = BaseSemanticActivationMaintenanceDisposition.Completed,
            PreviousAuthorityGeneration = request.ExpectedSemanticAuthorityGeneration,
            ResultingAuthorityGeneration = request.ExpectedSemanticAuthorityGeneration,
            ExaminedRows = 0, ChangedRows = 0, CanonicalBytes = 0,
            AuthorityChecksum = authorityChecksum.ToImmutableArray(), ResultChecksum = [],
            Checkpoint = null, ReceiptDisposition = BaseMutationRequestDisposition.Committed,
            CommitObservationChecksum = [],
        };
        ImmutableArray<byte> resultChecksum = BaseSemanticActivationMaintenanceContract.ResultChecksum(
            result, authorityChecksum);
        entry.Result = result with
        {
            ResultChecksum = resultChecksum,
            CommitObservationChecksum = BaseSemanticActivationMaintenanceContract.CommitObservationChecksum(
                resultChecksum.AsSpan()),
        };
        entry.StagedSlots.Clear();
    }

    private static long MeasureStagedRow(SemanticAdminRow source, SemanticAdminRow replacement) =>
        checked(source.Binding.LongLength + source.Key.LongLength
            + source.Authority.LongLength + replacement.Authority.LongLength);

    private bool ValidMaintenanceAuthorityRequest(BaseSemanticActivationMaintenanceAuthorityRequest request) =>
        !string.IsNullOrWhiteSpace(request.ApplicationId) && !string.IsNullOrWhiteSpace(request.LogicalStoreId)
        && request.ApplicationId == _options.SemanticActivationApplicationId && request.LogicalStoreId == _options.StoreId
        && request.ProviderIncarnation.Length == 32
        && CryptographicOperations.FixedTimeEquals(request.ProviderIncarnation.AsSpan(), _semanticProviderIncarnation.AsSpan())
        && request.RestoreEpoch == 0 && request.SemanticAuthorityGeneration > 0
        && ValidSemanticAdminDefinition(request.Definition) && request.MaximumRows >= 0 && request.MaximumBytes >= 0
        && request.RuntimeRequestChecksum.Length == 32
        && CryptographicOperations.FixedTimeEquals(request.RuntimeRequestChecksum.AsSpan(),
            BaseSemanticActivationMaintenanceAuthorityContract.RequestChecksum(request).AsSpan());

    private bool ValidInspectionRequest(BaseSemanticActivationProviderInspectionRequest request) =>
        !string.IsNullOrWhiteSpace(request.ApplicationId) && !string.IsNullOrWhiteSpace(request.LogicalStoreId)
        && request.ApplicationId == _options.SemanticActivationApplicationId && request.LogicalStoreId == _options.StoreId
        && request.ProviderIncarnation.Length == 32
        && CryptographicOperations.FixedTimeEquals(request.ProviderIncarnation.AsSpan(), _semanticProviderIncarnation.AsSpan())
        && request.RestoreEpoch == 0 && ValidSemanticAdminDefinition(request.Definition)
        && request.Take is >= 1 and <= 256 && (request.State is null || Enum.IsDefined(request.State.Value))
        && request.RuntimeRequestAuthorityChecksum.Length == 32
        && CryptographicOperations.FixedTimeEquals(request.RuntimeRequestAuthorityChecksum.AsSpan(),
            BaseSemanticActivationInspectionContract.RequestChecksum(request).AsSpan());

    private static bool ValidSemanticAdminDefinition(BaseSemanticActivationDefinitionKey definition) =>
        !string.IsNullOrWhiteSpace(definition.Id) && definition.Version > 0 && definition.Checksum.Length == 32;

    private bool SemanticDefinitionInstalled(BaseSemanticActivationDefinitionKey definition) =>
        _options.SemanticActivations.Any(value => value.Id == definition.Id && value.Version == definition.Version
            && CryptographicOperations.FixedTimeEquals(value.Checksum.AsSpan(), definition.Checksum.AsSpan()))
        || _options.SemanticActivationRemovals.Any(value => value.From.Id == definition.Id
            && value.From.Version == definition.Version
            && CryptographicOperations.FixedTimeEquals(value.From.Checksum.AsSpan(), definition.Checksum.AsSpan()));

    private static SemanticAdminRow[] SemanticAdminRows(
        InMemoryStoreState state,
        BaseSemanticActivationDefinitionKey definition,
        InMemorySemanticMaintenancePlan? plan = null)
    {
        plan?.ChargeFullRange(state.SemanticActivationSlots.Count);
        var rows = new List<SemanticAdminRow>();
        foreach ((string key, InMemorySemanticActivationSlot slot) in state.SemanticActivationSlots)
        {
            BaseSemanticActivationDefinitionKey candidate = SlotDefinition(slot);
            if (!string.Equals(candidate.Id, definition.Id, StringComparison.Ordinal)
                || candidate.Version != definition.Version
                || !CryptographicOperations.FixedTimeEquals(
                    candidate.Checksum.AsSpan(), definition.Checksum.AsSpan()))
                continue;
            rows.Add(SemanticAdminRow.From(key, slot));
        }

        long retainedBytes = MeasureAdminRows(rows);
        plan?.ObserveMaterializedScan(retainedBytes);

        // The explicit stable insertion sort makes every comparison observable to
        // exact provider accounting instead of depending on an opaque LINQ sorter.
        for (int index = 1; index < rows.Count; index++)
        {
            SemanticAdminRow value = rows[index];
            int cursor = index - 1;
            while (cursor >= 0)
            {
                plan?.ChargeLookup();
                int compared = ByteArrayComparer.Instance.Compare(rows[cursor].Binding, value.Binding);
                if (compared == 0)
                    compared = ByteArrayComparer.Instance.Compare(rows[cursor].Key, value.Key);
                if (compared <= 0) break;
                rows[cursor + 1] = rows[cursor];
                cursor--;
            }
            rows[cursor + 1] = value;
        }
        return rows.ToArray();
    }

    private static SemanticAdminRow[] FilterAdminRows(
        SemanticAdminRow[] source,
        Func<SemanticAdminRow, bool> predicate,
        InMemorySemanticMaintenancePlan plan)
    {
        plan.ChargeRetainedTraversal(source.LongLength);
        SemanticAdminRow[] filtered = source.Where(predicate).ToArray();
        plan.ObserveSimultaneousMaterializedScans(
            MeasureAdminRows(source), MeasureAdminRowReferenceArray(filtered.LongLength));
        return filtered;
    }

    private static long MeasureAdminRows(IEnumerable<SemanticAdminRow> rows)
    {
        long retainedBytes = sizeof(int);
        foreach (SemanticAdminRow row in rows)
            retainedBytes = checked(retainedBytes + sizeof(int)
                + Encoding.UTF8.GetByteCount(row.SlotKey)
                + row.Binding.LongLength + row.Key.LongLength
                + row.StateChecksum.LongLength + row.Authority.LongLength);
        return retainedBytes;
    }

    private static long MeasureAdminRowReferenceArray(long count) =>
        checked(sizeof(int) + count * IntPtr.Size);

    private static long MeasureHistoricalAuthorities(
        ImmutableArray<InMemorySemanticHistoricalAuthority> values)
    {
        long bytes = sizeof(int);
        foreach (InMemorySemanticHistoricalAuthority value in values)
            bytes = checked(bytes + sizeof(int) + value.ScopeBindingId.LongLength
                + value.KeyDigest.LongLength + sizeof(int) + value.CanonicalAuthority.LongLength);
        return bytes;
    }

    private static BaseSemanticActivationDefinitionKey SlotDefinition(
        InMemorySemanticActivationSlot slot) => slot switch
    {
        { Live: { } live } => new BaseSemanticActivationDefinitionKey
            { Id = live.Definition.Id, Version = live.Definition.Version, Checksum = live.Definition.Checksum },
        { Retired: { } retired } => retired.Definition,
        { Absent: { } absent } => new BaseSemanticActivationDefinitionKey
            { Id = absent.Definition.Id, Version = absent.Definition.Version, Checksum = absent.Definition.Checksum },
        _ => throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt),
    };

    private static void ValidateSemanticAdminRow(SemanticAdminRow row, BaseSemanticActivationDefinitionKey definition,
        BaseSemanticActivationStoreAuthorityRequirement authority)
    {
        if (!string.Equals(row.Definition.Id, definition.Id, StringComparison.Ordinal)
            || row.Definition.Version != definition.Version
            || !CryptographicOperations.FixedTimeEquals(
                row.Definition.Checksum.AsSpan(), definition.Checksum.AsSpan())
            || row.Generation <= 0 || row.Binding.Length != 32 || row.Key.Length != 32
            || row.Authority.Length == 0 || row.StateChecksum.Length != 32
            || row.StoreAuthority.Requirement.SemanticAuthorityGeneration != authority.SemanticAuthorityGeneration
            || row.StoreAuthority.Requirement.RestoreEpoch != authority.RestoreEpoch
            || row.StoreAuthority.Requirement.SchemaGeneration != authority.SchemaGeneration
            || !CryptographicOperations.FixedTimeEquals(row.StoreAuthority.Requirement.DefinitionSetChecksum.AsSpan(), authority.DefinitionSetChecksum.AsSpan()))
            throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
    }

    private static int CompareAdminBoundary(byte[] leftBinding, byte[] leftKey,
        ReadOnlySpan<byte> rightBinding, ReadOnlySpan<byte> rightKey)
    {
        int compared = leftBinding.AsSpan().SequenceCompareTo(rightBinding);
        return compared != 0 ? compared : leftKey.AsSpan().SequenceCompareTo(rightKey);
    }

    private static BaseAtomicReadIntervalEvidence SemanticInspectionInterval(
        BaseSemanticActivationProviderInspectionRequest request,
        ImmutableArray<BaseSemanticActivationProviderInspectionItem> items)
    {
        byte[] lower = request.After is null ? Encoding.UTF8.GetBytes(request.Definition.Id)
            : request.After.RuntimeBoundaryChecksum.ToArray();
        byte[] upper = items.IsDefaultOrEmpty ? lower : items[^1].Boundary.RuntimeBoundaryChecksum.ToArray();
        return new BaseAtomicReadIntervalEvidence
        {
            LogicalAccessPathId = "base.semanticActivation.inspection",
            CanonicalLowerBound = lower.ToImmutableArray(), CanonicalUpperBound = upper.ToImmutableArray(),
            LowerInclusive = false, UpperInclusive = true,
        };
    }

    private static BaseActivationAccounting EmptySemanticAdminActivationAccounting() => new()
    {
        Candidates = 0, Comparisons = 0, ReadIntervals = 0, IndexOperations = 0,
        EvidenceBytes = 0, TransientBytes = 0,
    };

    private static BaseSemanticActivationMaintenanceAuthority CloneMaintenanceAuthority(
        BaseSemanticActivationMaintenanceAuthority value) => value with
    {
        RetiredAuthorityChecksum = value.RetiredAuthorityChecksum.ToArray().ToImmutableArray(),
        DefinitionStateChecksum = value.DefinitionStateChecksum.ToArray().ToImmutableArray(),
        AbsenceAuthorityChecksum = value.AbsenceAuthorityChecksum.ToArray().ToImmutableArray(),
        Checksum = value.Checksum.ToArray().ToImmutableArray(),
    };

    private static BaseSemanticActivationProviderInspectionBoundary CloneInspectionBoundary(
        BaseSemanticActivationProviderInspectionBoundary value) => value with
    {
        DefinitionId = new string(value.DefinitionId.AsSpan()),
        ProviderIncarnation = value.ProviderIncarnation.ToArray().ToImmutableArray(),
        ScopeBindingId = value.ScopeBindingId.ToArray().ToImmutableArray(),
        Key = BaseSemanticActivationKeyDigest.Create(value.Key.ToArray()),
        RuntimeBoundaryChecksum = value.RuntimeBoundaryChecksum.ToArray().ToImmutableArray(),
    };

    private static BaseSemanticActivationProviderInspectionPage CloneInspectionPage(
        BaseSemanticActivationProviderInspectionPage value) => value with
    {
        Items = value.Items.Select(item => item with
        {
            Boundary = CloneInspectionBoundary(item.Boundary),
            StateChecksum = item.StateChecksum.ToArray().ToImmutableArray(),
            CanonicalStateAuthority = item.CanonicalStateAuthority.ToArray().ToImmutableArray(),
        }).ToImmutableArray(),
        Next = value.Next is null ? null : CloneInspectionBoundary(value.Next),
        ReadIntervals = value.ReadIntervals.Select(interval => interval with
        {
            LogicalAccessPathId = new string(interval.LogicalAccessPathId.AsSpan()),
            CanonicalLowerBound = interval.CanonicalLowerBound.ToArray().ToImmutableArray(),
            CanonicalUpperBound = interval.CanonicalUpperBound.ToArray().ToImmutableArray(),
        }).ToImmutableArray(),
        Checksum = value.Checksum.ToArray().ToImmutableArray(),
    };

    private static byte[] SemanticAdminHash(string purpose, params byte[][] values)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes(purpose)); Span<byte> length = stackalloc byte[4];
        foreach (byte[] value in values)
        {
            BinaryPrimitives.WriteInt32BigEndian(length, value.Length); hash.AppendData(length); hash.AppendData(value);
        }
        return hash.GetHashAndReset();
    }

    private static byte[] AdminInt64(long value)
    {
        byte[] bytes = new byte[8]; BinaryPrimitives.WriteInt64BigEndian(bytes, value); return bytes;
    }

    private static BaseResult<T> SemanticAdminFailure<T>(OperationStatus status, string code,
        ErrorCategory category) => BaseProviderResultContract.Failure<T>(status, new BaseError
    {
        Code = code, Message = "The semantic activation administration request could not be completed.", Category = category,
    });

    private static BaseResult<T> SemanticMaintenanceUnavailable<T>() => SemanticAdminFailure<T>(
        OperationStatus.CapabilityUnavailable, BaseSemanticActivationErrorCodes.CapabilityUnavailable, ErrorCategory.Capability);

    private sealed record SemanticAdminRow(string SlotKey, BaseSemanticActivationDefinitionKey Definition,
        BaseSemanticActivationStoreAuthority StoreAuthority, byte[] Binding, byte[] Key,
        BaseSemanticActivationSlotState State, long Generation, long? RetirementPosition,
        byte[] StateChecksum, byte[] Authority)
    {
        internal static SemanticAdminRow From(string slotKey, InMemorySemanticActivationSlot slot)
        {
            if (slot.Live is { } live)
                return new(slotKey, new() { Id = live.Definition.Id, Version = live.Definition.Version, Checksum = live.Definition.Checksum },
                    live.StoreAuthority, slot.ScopeBinding.BindingId.ToArray(), live.KeyDigest.ToArray(),
                    BaseSemanticActivationSlotState.Live, live.SlotGeneration, null, live.Checksum.ToArray(),
                    JsonSerializer.SerializeToUtf8Bytes(live, HPDBaseJsonSerializerContext.Default.BaseSemanticActivationLiveAuthority));
            if (slot.Retired is { } retired)
                return new(slotKey, retired.Definition, retired.StoreAuthority, slot.ScopeBinding.BindingId.ToArray(), retired.KeyDigest.ToArray(),
                    BaseSemanticActivationSlotState.Retired, retired.SlotGeneration, retired.RetirementPosition,
                    retired.Checksum.ToArray(), JsonSerializer.SerializeToUtf8Bytes(retired,
                        HPDBaseJsonSerializerContext.Default.BaseSemanticActivationRetirementAuthority));
            if (slot.Absent is { } absent)
                return new(slotKey, new() { Id = absent.Definition.Id, Version = absent.Definition.Version, Checksum = absent.Definition.Checksum },
                    absent.StoreAuthority, slot.ScopeBinding.BindingId.ToArray(), absent.Key.ToArray(),
                    BaseSemanticActivationSlotState.CompactedAbsent, absent.FinalSlotGeneration, absent.RetirementPosition,
                    absent.Checksum.ToArray(), JsonSerializer.SerializeToUtf8Bytes(absent,
                        HPDBaseJsonSerializerContext.Default.BaseSemanticActivationAbsenceAuthority));
            throw new InvalidDataException(BaseSemanticActivationErrorCodes.Corrupt);
        }
    }

    private sealed class ByteArrayComparer : IComparer<byte[]>
    {
        internal static ByteArrayComparer Instance { get; } = new();
        public int Compare(byte[]? left, byte[]? right) => left is null ? right is null ? 0 : -1
            : right is null ? 1 : left.AsSpan().SequenceCompareTo(right);
    }
}
