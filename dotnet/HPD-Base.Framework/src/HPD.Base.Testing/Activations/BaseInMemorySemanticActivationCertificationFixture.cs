using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace HPD.Base.Testing;

/// <summary>Creates isolated production InMemory domains for L53 certification.</summary>
public sealed class BaseInMemorySemanticActivationCertificationFixtureFactory
    : IBaseSemanticActivationCertificationFixtureFactory
{
    /// <inheritdoc />
    public BaseSemanticActivationCertificationSubject Subject { get; }

    /// <summary>Initializes the factory from the exact built-in provider capabilities.</summary>
    public BaseInMemorySemanticActivationCertificationFixtureFactory()
    {
        IBaseSemanticActivationCertificationStore store = CreateStore();
        Subject = BaseSemanticActivationCertificationContract.CreateSubject(
            "hpd.base.inMemory.semanticActivations", "1", "inmemory", HPDBaseStoreProviderFactory.ProtocolVersion,
            store.SemanticProvider.SemanticActivationCapability, store.ModuleMutationCapability, store.ActivationProvider.Descriptor.Capability);
    }

    /// <inheritdoc />
    public ValueTask<IBaseSemanticActivationCertificationFixture> CreateAsync(
        string caseId, int ordinal, DateTimeOffset deadlineUtc, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(caseId) || ordinal < 0 || deadlineUtc <= DateTimeOffset.UtcNow)
            throw new ArgumentException("base.semanticActivation.certificationInvalid");
        return ValueTask.FromResult<IBaseSemanticActivationCertificationFixture>(
            new Fixture(Subject, CreateStore(), caseId, ordinal, deadlineUtc));
    }

    private sealed class InMemoryCertificationStore(InMemoryRecordStore store)
        : IBaseSemanticActivationCertificationStore
    {
        public string LogicalStoreId => "semantic-certification";
        public TimeSpan NonCooperativeTransactionTimeout => TimeSpan.FromMilliseconds(100);
        public bool RecoveryFloorVerified => false;
        public ValueTask InstallFaultAsync(BaseSemanticActivationCertificationFaultRequest request, CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask<bool> ReleaseLateWorkAsync(BaseSemanticActivationCertificationFault fault, int occurrence, CancellationToken cancellationToken) => ValueTask.FromResult(false);
        public IAtomicRecordStore AtomicStore => store;
        public IBaseActivationProvider ActivationProvider => store;
        public IBaseSemanticActivationCapabilityProvider SemanticProvider => store;
        public BaseModuleMutationCapability ModuleMutationCapability => store.Capabilities.ModuleMutation!;
        public IBaseSemanticActivationAdministration? SemanticAdministration => null;
        public ValueTask<(long Live, long Retired, long Absent, long Activations, long Receipts)> ObserveAsync(CancellationToken cancellationToken) => store.ObserveSemanticActivationCertificationStateAsync(cancellationToken);
        public ValueTask<ImmutableArray<byte>> ReadAuthorityAsync(CancellationToken cancellationToken) => store.ReadSemanticActivationCertificationAuthorityAsync(cancellationToken);
        public (int Active, int Quarantined, int Released, int RejectedLateCompletions) ObserveLateWork() => store.ObserveAtomicLateWorkCertificationState();
        public ValueTask CorruptAsync(bool compactedAbsence, BaseSemanticActivationDefinitionIdentity definition, CancellationToken cancellationToken) => store.CorruptSemanticActivationCertificationStateAsync(compactedAbsence, definition, cancellationToken);
        public ValueTask<OperationResult<BaseBackupManifest>> CreateBackupAsync(Stream destination, BaseBackupRequest request, CancellationToken cancellationToken) =>
            ValueTask.FromResult(OperationResults.Unsupported<BaseBackupManifest>(new BaseError { Code = BaseSemanticActivationErrorCodes.ProviderContractInvalid, Message = "InMemory semantic backup is not advertised.", Category = ErrorCategory.Unsupported }));
        public ValueTask<OperationResult<BaseRestoreResult>> RestoreAsync(Stream source, BaseRestoreRequest request, CancellationToken cancellationToken) =>
            ValueTask.FromResult(OperationResults.Unsupported<BaseRestoreResult>(new BaseError { Code = BaseSemanticActivationErrorCodes.ProviderContractInvalid, Message = "InMemory semantic restore is not advertised.", Category = ErrorCategory.Unsupported }));
        public ValueTask<BaseSemanticActivationCertificationOperationInput?> CreateAdministrationInputAsync(
            BaseSemanticActivationCertificationOperation operation, string caseId, int ordinal,
            DateTimeOffset deadlineUtc, CancellationToken cancellationToken) => ValueTask.FromResult<BaseSemanticActivationCertificationOperationInput?>(null);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private static IBaseSemanticActivationCertificationStore CreateStore() => new InMemoryCertificationStore(new InMemoryRecordStore(new HPDBaseInMemoryStoreOptions
        {
            StoreId = "semantic-certification", SemanticActivationApplicationId = "certification-application",
            SemanticActivationOwnerGeneration = 1,
            SemanticActivationDefinitionSetChecksum = BaseSemanticActivationCertificationProcessor.InstalledDefinitionSetChecksum.ToArray(),
        }, new BaseOpaqueTokenProtector(Options.Create(new HPDBaseTokenProtectionOptions
        {
            ActiveKey = new BaseOpaqueTokenKey
            {
                Id = 1, Key = Enumerable.Repeat((byte)0x53, 32).ToArray(),
                IssueNotBefore = DateTimeOffset.UnixEpoch,
            },
        })), TimeProvider.System));

    internal sealed class Fixture(
        BaseSemanticActivationCertificationSubject subject,
        IBaseSemanticActivationCertificationStore store,
        string caseId,
        int ordinal,
        DateTimeOffset deadlineUtc) : IBaseSemanticActivationCertificationFixture
    {
        private readonly BaseAtomicMutationExecutionLimits limits =
            DefaultBaseModuleMutationRuntime.ResolveExecutionLimits(BaseModuleMutationPlatform.MaximumLimits);
        private BaseAtomicMutationAuthorityRequirement? authority;
        private ImmutableArray<byte> replayAuthority = [];
        private readonly CertificationAtomicStore atomicStore = new(store.AtomicStore, new FaultController());

        private FaultController Faults => atomicStore.Faults;

        public BaseSemanticActivationCertificationSubject Subject { get; } =
            BaseSemanticActivationCertificationContract.CloneSubject(subject);
        public IAtomicRecordStore AtomicStore => atomicStore;
        public IBaseActivationProvider ActivationProvider => store.ActivationProvider;
        public IBaseSemanticActivationCapabilityProvider SemanticProvider => store.SemanticProvider;
        public BaseModuleMutationCapability ModuleMutationCapability => store.ModuleMutationCapability;
        public IBaseSemanticActivationAdministration? SemanticAdministration => store.SemanticAdministration;

        public ValueTask<OperationResult<BaseBackupManifest>> CreateBackupAsync(
            Stream destination, BaseBackupRequest request, CancellationToken cancellationToken) =>
            store.CreateBackupAsync(destination, request, cancellationToken);

        public ValueTask<OperationResult<BaseRestoreResult>> RestoreAsync(
            Stream source, BaseRestoreRequest request, CancellationToken cancellationToken) =>
            store.RestoreAsync(source, request, cancellationToken);

        public async ValueTask<BaseSemanticActivationCertificationOperationInput> CreateInputAsync(
            BaseSemanticActivationCertificationOperation operation, CancellationToken cancellationToken)
        {
            Faults.Operation = operation;
            if (operation == BaseSemanticActivationCertificationOperation.RecoveryFloor
                || operation == BaseSemanticActivationCertificationOperation.BackupRestore
                    && Faults.Fault is BaseSemanticActivationCertificationFault.CorruptRecoveryEntry
                        or BaseSemanticActivationCertificationFault.RetentionOvertake)
                await SeedRetiredStateAsync($"{ordinal}:{caseId}:floor", cancellationToken).ConfigureAwait(false);
            if (operation is BaseSemanticActivationCertificationOperation.Inspect
                or BaseSemanticActivationCertificationOperation.MaintenanceAuthority
                or BaseSemanticActivationCertificationOperation.Maintain
                or BaseSemanticActivationCertificationOperation.BackupRestore
                or BaseSemanticActivationCertificationOperation.RecoveryFloor)
            {
                BaseSemanticActivationCertificationOperationInput? administrative = await store
                    .CreateAdministrationInputAsync(operation, caseId, ordinal, deadlineUtc, cancellationToken)
                    .ConfigureAwait(false);
                if (administrative is not null) return administrative;
            }
            BaseAtomicMutationAuthorityRequirement captured = await AuthorityAsync(cancellationToken).ConfigureAwait(false);
            string prefix = $"{ordinal}:{caseId}";
            if (operation == BaseSemanticActivationCertificationOperation.EnsureDifferentParent)
            {
                RecordMutationExecutionRequest leftRequest = Request(prefix + ":left", deadlineUtc);
                RecordMutationExecutionRequest rightRequest = Request(prefix + ":right", deadlineUtc);
                return new()
                {
                    AtomicProcessor = new BaseSemanticActivationCertificationProcessor(captured, limits, store.LogicalStoreId, "parent-left"),
                    AtomicRequest = leftRequest,
                    SecondaryAtomicProcessor = new BaseSemanticActivationCertificationProcessor(captured, limits, store.LogicalStoreId, "parent-right"),
                    SecondaryAtomicRequest = rightRequest,
                    AtomicRetryProcessor = new BaseSemanticActivationCertificationProcessor(captured, limits, store.LogicalStoreId, "parent-left"),
                    AtomicRetryRequest = CloneRequest(leftRequest),
                    SecondaryAtomicRetryProcessor = new BaseSemanticActivationCertificationProcessor(captured, limits, store.LogicalStoreId, "parent-right"),
                    SecondaryAtomicRetryRequest = CloneRequest(rightRequest),
                };
            }
            string requestId = prefix + ":request";
            RecordMutationExecutionRequest request = Request(requestId, deadlineUtc);
            if (Faults.Fault is BaseSemanticActivationCertificationFault.SubstituteSlotGeneration
                or BaseSemanticActivationCertificationFault.SubstituteActivation
                or BaseSemanticActivationCertificationFault.SubstituteDueAuthority)
            {
                RecordMutationExecutionResult seeded = await store.AtomicStore.ExecuteAtomicAsync(
                    new BaseSemanticActivationCertificationProcessor(captured, limits, store.LogicalStoreId, "hostile-live-seed"),
                    Request(prefix + ":hostile-live-seed", deadlineUtc), cancellationToken).ConfigureAwait(false);
                if (seeded.Outcome != RecordMutationExecutionOutcome.Committed)
                    throw new InvalidOperationException("base.semanticActivation.certificationInvalid");
            }
            if (Faults.Fault is >= BaseSemanticActivationCertificationFault.NonCooperativeCapture
                and <= BaseSemanticActivationCertificationFault.NonCooperativeApply)
                request = request with { TransactionTimeout = store.NonCooperativeTransactionTimeout };
            IAtomicMutationProcessor Processor(string parent, bool retire = false, BaseSemanticActivationExecutionLimits? semanticLimits = null) =>
                new FaultingProcessor(new BaseSemanticActivationCertificationProcessor(
                    captured, limits, store.LogicalStoreId, parent, retire, semanticLimits,
                    acceptedTime: retire ? 12 : 1), Faults);
            if (operation == BaseSemanticActivationCertificationOperation.AccountingLimits)
            {
                (RecordMutationExecutionResult measuredResult, BaseSemanticActivationCertificationProcessor measuredProcessor) =
                    await ExecuteIsolatedAsync(prefix + ":measure", null, cancellationToken).ConfigureAwait(false);
                BaseSemanticActivationAccounting measured = measuredProcessor.PreparedAccounting
                    ?? throw new InvalidOperationException("base.semanticActivation.certificationInvalid");
                BaseSemanticActivationExecutionLimits exactLimits = LimitsFrom(measured);
                (RecordMutationExecutionResult exact, _) = await ExecuteIsolatedAsync(
                    prefix + ":exact", exactLimits, cancellationToken).ConfigureAwait(false);
                Faults.ExactLimitAccepted = measuredResult.Outcome == RecordMutationExecutionOutcome.Committed
                    && exact.Outcome == RecordMutationExecutionOutcome.Committed;
                BaseSemanticActivationExecutionLimits[] below = BelowExact(exactLimits, measured);
                Faults.MaxPlusOneRejected = below.Length != 0;
                for (int index = 0; index < below.Length && Faults.MaxPlusOneRejected; index++)
                {
                    (RecordMutationExecutionResult rejected, _) = await ExecuteIsolatedAsync(
                        $"{prefix}:below:{index}", below[index], cancellationToken).ConfigureAwait(false);
                    Faults.MaxPlusOneRejected = rejected.Outcome == RecordMutationExecutionOutcome.RollbackConfirmed
                        && rejected.Error?.Code == BaseSemanticActivationErrorCodes.BudgetExceeded;
                }
                BaseSemanticActivationExecutionLimits tooSmall = below[0];
                return new()
                {
                    AtomicProcessor = Processor("accounting-max-plus-one", semanticLimits: tooSmall),
                    AtomicRequest = request, ReceiptIdentity = request.AtomicRequest!.Identity,
                };
            }
            if (operation is BaseSemanticActivationCertificationOperation.ExistingReplay
                or BaseSemanticActivationCertificationOperation.ResolveReceipt)
            {
                var seed = new BaseSemanticActivationCertificationProcessor(captured, limits, store.LogicalStoreId, "replay-parent");
                RecordMutationExecutionResult committed = await store.AtomicStore.ExecuteAtomicAsync(seed, request, cancellationToken).ConfigureAwait(false);
                if (committed.Outcome != RecordMutationExecutionOutcome.Committed || committed.ReceiptAuthority is null)
                    throw new InvalidOperationException($"base.semanticActivation.certificationInvalid:{committed.Outcome}:{committed.Error?.Code}");
                replayAuthority = committed.Processing!.Receipt.ModuleMutation!.SemanticActivation!.SlotChecksum;
                return new()
                {
                    AtomicProcessor = Processor("replay-parent"),
                    AtomicRequest = CloneRequest(request), ReceiptIdentity = request.AtomicRequest!.Identity,
                };
            }
            if (operation == BaseSemanticActivationCertificationOperation.Retire)
            {
                var seed = new BaseSemanticActivationCertificationProcessor(captured, limits, store.LogicalStoreId, "retirement-seed");
                RecordMutationExecutionResult committed = await store.AtomicStore.ExecuteAtomicAsync(
                    seed, Request(prefix + ":seed", deadlineUtc), cancellationToken).ConfigureAwait(false);
                if (committed.Outcome != RecordMutationExecutionOutcome.Committed || seed.Provisional?.ActivationId is null)
                    throw new InvalidOperationException("base.semanticActivation.certificationInvalid");
                await CompleteActivationAsync(store, seed.Provisional.ActivationId, prefix, cancellationToken).ConfigureAwait(false);
                captured = (await store.AtomicStore.CaptureAtomicMutationAuthorityRequirementAsync(
                    "certification-application", [], limits, cancellationToken).ConfigureAwait(false)).Value
                    ?? throw new InvalidOperationException("base.semanticActivation.certificationInvalid");
                if (Faults.Fault is BaseSemanticActivationCertificationFault.CorruptRetirement
                    or BaseSemanticActivationCertificationFault.CorruptAbsence)
                {
                    var retirementProcessor = new BaseSemanticActivationCertificationProcessor(
                        captured, limits, store.LogicalStoreId, "retirement", retire: true, acceptedTime: 12);
                    RecordMutationExecutionResult retired = await store.AtomicStore.ExecuteAtomicAsync(
                        retirementProcessor,
                        Request(prefix + ":retire-seed", deadlineUtc), cancellationToken).ConfigureAwait(false);
                    if (retired.Outcome != RecordMutationExecutionOutcome.Committed)
                        throw new InvalidOperationException(
                            $"base.semanticActivation.certificationInvalid:{retired.Outcome}:{retired.Error?.Code}:{retired.Error?.Message}:" +
                            $"captured={retirementProcessor.Captured is not null}:prepared={retirementProcessor.PreparedAccounting is not null}:" +
                            $"provisional={retirementProcessor.Provisional is not null}");
                    await store.CorruptAsync(
                        Faults.Fault == BaseSemanticActivationCertificationFault.CorruptAbsence,
                        BaseSemanticActivationCertificationProcessor.Definition(),
                        cancellationToken).ConfigureAwait(false);
                }
                return new()
                {
                    AtomicProcessor = Processor("retirement", retire: true),
                    AtomicRequest = request, ReceiptIdentity = request.AtomicRequest!.Identity,
                };
            }
            return new()
            {
                AtomicProcessor = Processor("certification-parent"),
                AtomicRequest = request, ReceiptIdentity = request.AtomicRequest!.Identity,
            };
        }

        private async ValueTask SeedRetiredStateAsync(string prefix, CancellationToken cancellationToken)
        {
            BaseAtomicMutationAuthorityRequirement captured = (await store.AtomicStore
                .CaptureAtomicMutationAuthorityRequirementAsync("certification-application", [], limits, cancellationToken)
                .ConfigureAwait(false)).Value ?? throw new InvalidOperationException("base.semanticActivation.certificationInvalid");
            var seed = new BaseSemanticActivationCertificationProcessor(
                captured, limits, store.LogicalStoreId, prefix + ":ensure");
            RecordMutationExecutionResult ensured = await store.AtomicStore.ExecuteAtomicAsync(
                seed, Request(prefix + ":ensure", deadlineUtc), cancellationToken).ConfigureAwait(false);
            if (ensured.Outcome != RecordMutationExecutionOutcome.Committed || seed.Provisional?.ActivationId is null)
                throw new InvalidOperationException("base.semanticActivation.certificationInvalid");
            await CompleteActivationAsync(store, seed.Provisional.ActivationId, prefix, cancellationToken).ConfigureAwait(false);
            captured = (await store.AtomicStore.CaptureAtomicMutationAuthorityRequirementAsync(
                "certification-application", [], limits, cancellationToken).ConfigureAwait(false)).Value
                ?? throw new InvalidOperationException("base.semanticActivation.certificationInvalid");
            var retire = new BaseSemanticActivationCertificationProcessor(
                captured, limits, store.LogicalStoreId, prefix + ":retire", retire: true, acceptedTime: 12);
            RecordMutationExecutionResult retired = await store.AtomicStore.ExecuteAtomicAsync(
                retire, Request(prefix + ":retire", deadlineUtc), cancellationToken).ConfigureAwait(false);
            if (retired.Outcome != RecordMutationExecutionOutcome.Committed)
                throw new InvalidOperationException("base.semanticActivation.certificationInvalid");
        }

        public async ValueTask InstallFaultAsync(
            BaseSemanticActivationCertificationFaultRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (request.Occurrence != 1 || Faults.Fault is not null) throw new ArgumentException("base.semanticActivation.certificationInvalid");
            Faults.Fault = request.Fault;
            await store.InstallFaultAsync(request, cancellationToken).ConfigureAwait(false);
        }

        public async ValueTask<BaseSemanticActivationCertificationObservation> ObserveAsync(CancellationToken cancellationToken)
        {
            (long live, long retired, long absent, long activations, long receipts) =
                await store.ObserveAsync(cancellationToken).ConfigureAwait(false);
            (int activeWork, int quarantinedWork, int releasedWork, int rejectedLateCompletions) =
                store.ObserveLateWork();
            ImmutableArray<byte> currentAuthority = live + retired + absent == 1
                ? await store.ReadAuthorityAsync(cancellationToken).ConfigureAwait(false) : [];
            return new()
            {
                Sequence = checked(ordinal + 1L), Evidence = Digest("observation:" + caseId),
                LiveSlots = live, RetiredSlots = retired, AbsenceMarkers = absent, Activations = activations,
                Receipts = receipts, ActiveWork = activeWork, QuarantinedWork = quarantinedWork,
                ReleasedWork = releasedWork, RejectedLateCompletions = rejectedLateCompletions,
                ExactLimitAccepted = Faults.ExactLimitAccepted, MaxPlusOneRejected = Faults.MaxPlusOneRejected,
                RecoveryFloorVerified = store.RecoveryFloorVerified, ReceiptResolved = Faults.ReceiptResolved,
                AuthorityBeforeChecksum = replayAuthority.IsDefaultOrEmpty
                    ? Faults.SemanticAuthorityBefore : replayAuthority,
                AuthorityAfterChecksum = currentAuthority,
            };
        }

        public async ValueTask<bool> ReleaseLateWorkAsync(
            BaseSemanticActivationCertificationFault requestedFault, int occurrence, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            BaseSemanticActivationOperationalStatus quarantined = store.SemanticProvider.SemanticActivationOperationalStatus;
            if (quarantined.Ready || !quarantined.Quarantined || quarantined.RetainedOperations != 1
                || quarantined.ActiveOperations != 1
                || quarantined.RetainedOperations > quarantined.MaximumRetainedOperations)
                return false;
            DateTimeOffset deadlineUtc = DateTimeOffset.UtcNow.AddSeconds(1);
            RecordMutationExecutionResult fenced = await store.AtomicStore.ExecuteAtomicAsync(
                new BaseSemanticActivationCertificationProcessor(await AuthorityAsync(cancellationToken).ConfigureAwait(false),
                    limits, store.LogicalStoreId, "quarantine-admission"),
                Request(caseId + ":quarantine-admission", deadlineUtc),
                cancellationToken).ConfigureAwait(false);
            if (fenced.Outcome != RecordMutationExecutionOutcome.RollbackConfirmed
                || fenced.Error?.Code != BaseSemanticActivationErrorCodes.Quarantined)
                return false;
            bool released = Faults.Release(requestedFault, occurrence);
            released |= await store.ReleaseLateWorkAsync(requestedFault, occurrence, cancellationToken).ConfigureAwait(false);
            if (!released) return false;
            for (int attempt = 0; attempt < 100; attempt++)
            {
                if (store.ObserveLateWork().Released > 0)
                {
                    BaseSemanticActivationOperationalStatus recovered = store.SemanticProvider.SemanticActivationOperationalStatus;
                    if (!recovered.Ready || recovered.Quarantined || recovered.RetainedOperations != 0)
                        return false;
                    RecordMutationExecutionResult admitted = await store.AtomicStore.ResolveAtomicReceiptAsync(
                        new BaseSemanticActivationCertificationProcessor(await AuthorityAsync(cancellationToken).ConfigureAwait(false),
                            limits, store.LogicalStoreId, "recovered-admission"),
                        Identity(caseId + ":missing-receipt"),
                        TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
                    return admitted.Error?.Code == BaseMutationRequestErrorCodes.ReceiptUnavailable;
                }
                await Task.Delay(TimeSpan.FromMilliseconds(10), cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
            }
            return false;
        }

        public ValueTask DisposeAsync() => store.DisposeAsync();

        private async ValueTask<BaseAtomicMutationAuthorityRequirement> AuthorityAsync(CancellationToken cancellationToken) =>
            authority ??= (await store.AtomicStore.CaptureAtomicMutationAuthorityRequirementAsync(
                "certification-application", [], limits, cancellationToken).ConfigureAwait(false)).Value
                ?? throw new InvalidOperationException("base.semanticActivation.certificationInvalid");

        private async ValueTask<(RecordMutationExecutionResult Result, BaseSemanticActivationCertificationProcessor Processor)>
            ExecuteIsolatedAsync(string id, BaseSemanticActivationExecutionLimits? semanticLimits, CancellationToken cancellationToken)
        {
            await using IBaseSemanticActivationCertificationStore isolated = CreateStore();
            BaseAtomicMutationAuthorityRequirement isolatedAuthority = (await isolated.AtomicStore
                .CaptureAtomicMutationAuthorityRequirementAsync("certification-application", [], limits, cancellationToken)
                .ConfigureAwait(false)).Value ?? throw new InvalidOperationException("base.semanticActivation.certificationInvalid");
            var processor = new BaseSemanticActivationCertificationProcessor(
                isolatedAuthority, limits, isolated.LogicalStoreId, id, semanticLimits: semanticLimits);
            return (await isolated.AtomicStore.ExecuteAtomicAsync(processor, Request(id, deadlineUtc), cancellationToken)
                .ConfigureAwait(false), processor);
        }

        private static BaseSemanticActivationExecutionLimits LimitsFrom(BaseSemanticActivationAccounting value) => new()
        {
            MaximumOperations = value.Operations, MaximumScopeDirectoryReads = value.ScopeDirectoryReads,
            MaximumSlotReads = value.SlotReads, MaximumActivationReads = value.ActivationReads,
            MaximumReadIntervals = value.ReadIntervals, MaximumIndexOperations = value.IndexOperations,
            MaximumActivationBytes = value.ActivationBytes, MaximumScopeDirectoryBytes = value.ScopeDirectoryBytes,
            MaximumEvidenceBytes = value.EvidenceBytes, MaximumReceiptBytes = value.ReceiptBytes,
            MaximumTransientBytes = value.TransientBytes,
        };

        private static BaseSemanticActivationExecutionLimits[] BelowExact(
            BaseSemanticActivationExecutionLimits exact, BaseSemanticActivationAccounting measured)
        {
            var values = new List<BaseSemanticActivationExecutionLimits>();
            if (measured.Operations > 1) values.Add(exact with { MaximumOperations = measured.Operations - 1 });
            if (measured.ScopeDirectoryReads > 1) values.Add(exact with { MaximumScopeDirectoryReads = measured.ScopeDirectoryReads - 1 });
            if (measured.SlotReads > 1) values.Add(exact with { MaximumSlotReads = measured.SlotReads - 1 });
            if (measured.ActivationReads > 1) values.Add(exact with { MaximumActivationReads = measured.ActivationReads - 1 });
            if (measured.ReadIntervals > 1) values.Add(exact with { MaximumReadIntervals = measured.ReadIntervals - 1 });
            if (measured.IndexOperations > 1) values.Add(exact with { MaximumIndexOperations = measured.IndexOperations - 1 });
            if (measured.ActivationBytes > 1) values.Add(exact with { MaximumActivationBytes = measured.ActivationBytes - 1 });
            if (measured.ScopeDirectoryBytes > 1) values.Add(exact with { MaximumScopeDirectoryBytes = measured.ScopeDirectoryBytes - 1 });
            if (measured.EvidenceBytes > 1) values.Add(exact with { MaximumEvidenceBytes = measured.EvidenceBytes - 1 });
            if (measured.ReceiptBytes > 1) values.Add(exact with { MaximumReceiptBytes = measured.ReceiptBytes - 1 });
            if (measured.TransientBytes > 1) values.Add(exact with { MaximumTransientBytes = measured.TransientBytes - 1 });
            return [.. values];
        }

        private static RecordMutationExecutionRequest Request(string id, DateTimeOffset deadline)
        {
            byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes("base.semanticActivation.certificationRequest.v2\0" + id));
            return new RecordMutationExecutionRequest
            {
                AcquisitionTimeout = TimeSpan.FromSeconds(5), TransactionTimeout = TimeSpan.FromSeconds(5),
                CommitCompletionTimeout = TimeSpan.FromSeconds(5), AtomicRequest = new BaseAtomicMutationExecutionRequest
                {
                    Identity = BaseMutationRequestIdentity.Create("semantic-certification", "semantic.ensure", id,
                        BaseMutationRequestFingerprint.Create(digest)),
                    StructuralDigest = digest, ExpiresAt = deadline.AddMinutes(5), MaxReceiptBytes = 1_048_576,
                },
            };
        }

        private static RecordMutationExecutionRequest CloneRequest(RecordMutationExecutionRequest value) => value with
        {
            AtomicRequest = value.AtomicRequest! with
            {
                Identity = BaseMutationRequestIdentity.Create(value.AtomicRequest.Identity.Scope,
                    value.AtomicRequest.Identity.Operation, value.AtomicRequest.Identity.IdempotencyKey,
                    BaseMutationRequestFingerprint.Create(value.AtomicRequest.Identity.Fingerprint.ToArray())),
                StructuralDigest = value.AtomicRequest.StructuralDigest.ToArray(),
            },
        };

        private ImmutableArray<byte> Digest(string value) => SHA256.HashData(Encoding.UTF8.GetBytes(
            $"base.semanticActivation.inMemoryCertification.v2\0{ordinal}\n{caseId}\n{value}")).ToImmutableArray();

        private static async ValueTask CompleteActivationAsync(
            IBaseSemanticActivationCertificationStore store, string activationId, string prefix, CancellationToken cancellationToken)
        {
            BaseActivationExecutionLimits execution = ActivationExecutionLimits();
            BaseActivationDefinitionKey definition = BaseSemanticActivationCertificationProcessor
                .InstalledDefinition(DefaultBaseModuleMutationRuntime.ResolveExecutionLimits(BaseModuleMutationPlatform.MaximumLimits)).Activation;
            BaseOwnedScopeSeekAuthority scope = new()
            {
                Kind = BaseSubjectScopeKind.Global,
                ProtectedIndexDigest = SHA256.HashData(Encoding.UTF8.GetBytes(
                    $"base.activation.scope.v2\0{(int)BaseSubjectScopeKind.Global}\n")).ToImmutableArray(),
            };
            BaseActivationDueObservation observation = (await store.ActivationProvider.ObserveDueAsync(new BaseActivationDueObservationRequest
            {
                ApplicationId = "certification-application", WorkerModuleId = "certification", Definitions = [definition],
                Scope = scope, AcceptedTime = AcceptedTime(10), MaximumCandidates = 8, Limits = execution,
            }, cancellationToken).ConfigureAwait(false)).Value
                ?? throw new InvalidOperationException("base.semanticActivation.certificationInvalid");
            BaseActivationWorkerAuthority worker = new()
            {
                ApplicationId = "certification-application", ModuleId = "certification",
                WorkerIdentity = "semantic-certification-worker", Definitions = [definition], Scope = scope,
                Checksum = new byte[32].ToImmutableArray(),
            };
            BaseActivationClaimedResult claimed = (BaseActivationClaimedResult)(await store.ActivationProvider.TryClaimNextAsync(
                new BaseActivationClaimRequest
                {
                    Observation = observation.Token, Worker = worker, AcceptedTime = AcceptedTime(10),
                    LeaseMilliseconds = 1_000, Identity = Identity(prefix + ":claim"), Limits = execution,
                }, cancellationToken).ConfigureAwait(false)).Value!;
            if (!string.Equals(claimed.Claim.ActivationId, activationId, StringComparison.Ordinal))
                throw new InvalidOperationException("base.semanticActivation.certificationInvalid");
            byte[] result = "certification-complete"u8.ToArray();
            OperationResult<BaseActivationTransitionResult> completed = await store.ActivationProvider.TransitionAsync(
                new BaseActivationCompleteRequest
                {
                    ActivationId = activationId, Claim = claimed.Claim, CanonicalResult = result.ToImmutableArray(),
                    ResultChecksum = SHA256.HashData(result).ToImmutableArray(), AcceptedTime = AcceptedTime(11),
                    Identity = Identity(prefix + ":complete"), Limits = execution,
                }, cancellationToken).ConfigureAwait(false);
            if (!completed.IsSuccess()) throw new InvalidOperationException("base.semanticActivation.certificationInvalid");
        }

        private static BaseActivationExecutionLimits ActivationExecutionLimits() => new()
        {
            MaximumCandidates = 8, MaximumInputBytes = 4096, MaximumResultBytes = 4096,
            MaximumEvidenceBytes = 4096, MaximumTransientBytes = 16384, MaximumReadIntervals = 8,
            MaximumIndexOperations = 16, AcquisitionTimeout = TimeSpan.FromSeconds(5),
            TransactionTimeout = TimeSpan.FromSeconds(5), CommitObservationTimeout = TimeSpan.FromSeconds(5),
            ReceiptResolutionTimeout = TimeSpan.FromSeconds(5),
        };

        private static BaseMutationRequestIdentity Identity(string key)
        {
            byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(key));
            return BaseMutationRequestIdentity.Create("semantic-certification", "activation.transition", key,
                BaseMutationRequestFingerprint.Create(digest));
        }

        private static BaseAcceptedTimeReceipt AcceptedTime(long milliseconds)
        {
            const string application = "certification-application"; const long generation = 1, skew = 30_000;
            long sequence = checked(milliseconds + 1); using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            Append(hash, "base.activation.acceptedTime.v2\0"); Append(hash, application); Append(hash, generation);
            Append(hash, milliseconds); Append(hash, milliseconds); Append(hash, sequence); Append(hash, skew);
            return new BaseAcceptedTimeReceipt(application, generation, milliseconds, milliseconds, sequence, skew, hash.GetHashAndReset());
        }

        private static void Append(IncrementalHash hash, string value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value); Span<byte> length = stackalloc byte[4];
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(length, checked((uint)bytes.Length));
            hash.AppendData(length); hash.AppendData(bytes);
        }

        private static void Append(IncrementalHash hash, long value)
        {
            Span<byte> bytes = stackalloc byte[8]; System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(bytes, value);
            hash.AppendData(bytes);
        }
    }

    private sealed class FaultController
    {
        internal BaseSemanticActivationCertificationFault? Fault { get; set; }
        internal BaseSemanticActivationCertificationOperation Operation { get; set; }
        private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool ExactLimitAccepted { get; set; }
        internal bool MaxPlusOneRejected { get; set; }
        internal bool ReceiptResolved { get; set; }
        internal ImmutableArray<byte> ResolvedReceiptChecksum { get; set; } = [];
        internal ImmutableArray<byte> SemanticAuthorityBefore { get; set; } = [];

        internal bool Release(BaseSemanticActivationCertificationFault fault, int occurrence)
        {
            if (occurrence != 1 || Fault != fault || !IsNonCooperative(fault)) return false;
            return release.TrySetResult();
        }

        internal Task WaitForReleaseAsync() => release.Task;
        internal static bool IsNonCooperative(BaseSemanticActivationCertificationFault fault) =>
            fault is >= BaseSemanticActivationCertificationFault.NonCooperativeCapture
                and <= BaseSemanticActivationCertificationFault.NonCooperativeRestore;
    }

    private sealed class CertificationAtomicStore(IAtomicRecordStore inner, FaultController controller)
        : BaseTestAtomicRecordStore(inner, new BaseTestFaults())
    {
        internal FaultController Faults => controller;

        public override async ValueTask<RecordMutationExecutionResult> ExecuteAtomicAsync(
            IAtomicMutationProcessor processor, RecordMutationExecutionRequest request,
            CancellationToken cancellationToken = default)
        {
            BaseSemanticActivationCertificationFault? fault = controller.Fault;
            if (fault is BaseSemanticActivationCertificationFault.ResponseLossAfterCommit
                or BaseSemanticActivationCertificationFault.IndeterminateCommit)
            {
                RecordMutationExecutionResult committed = await Inner.ExecuteAtomicAsync(processor, request, cancellationToken).ConfigureAwait(false);
                if (committed.Outcome != RecordMutationExecutionOutcome.Committed) return committed;
                controller.SemanticAuthorityBefore = committed.Processing?.Receipt.ModuleMutation?
                    .SemanticActivation?.SlotChecksum ?? [];
                return new RecordMutationExecutionResult(RecordMutationExecutionOutcome.Indeterminate, null,
                    Error(BaseSemanticActivationErrorCodes.CommitIndeterminate));
            }
            RecordMutationExecutionResult result = await Inner.ExecuteAtomicAsync(processor, request, cancellationToken).ConfigureAwait(false);
            if (result.Error?.Code == BaseSubjectErrorCodes.ProviderContractInvalid)
                return new RecordMutationExecutionResult(result.Outcome, result.Processing,
                    Error(BaseSemanticActivationErrorCodes.ProviderContractInvalid));
            if (fault is { } injected && FaultController.IsNonCooperative(injected)
                && result.Error?.Code == BaseMutationErrorCodes.TransactionTimeout)
                return new RecordMutationExecutionResult(result.Outcome, result.Processing,
                    Error(BaseSemanticActivationErrorCodes.TransactionTimeout));
            return result;
        }

        public override async ValueTask<RecordMutationExecutionResult> ResolveAtomicReceiptAsync(
            IAtomicMutationProcessor processor, BaseMutationRequestIdentity identity,
            TimeSpan resolutionTimeout, CancellationToken cancellationToken = default)
        {
            RecordMutationExecutionResult result = await Inner.ResolveAtomicReceiptAsync(
                processor, identity,
                controller.Fault == BaseSemanticActivationCertificationFault.NonCooperativeReceipt
                    ? TimeSpan.FromMilliseconds(100) : resolutionTimeout,
                cancellationToken).ConfigureAwait(false);
            controller.ReceiptResolved = result.ReceiptResolution == BaseAtomicReceiptResolutionDisposition.Found;
            if (controller.ReceiptResolved && result.ReceiptAuthority?.ReceiptChecksum.Length == 32)
                controller.ResolvedReceiptChecksum = result.ReceiptAuthority.ReceiptChecksum;
            return result;
        }

        private static RecordMutationExecutionResult Rollback(string code)
        {
            BaseError error = Error(code);
            return new RecordMutationExecutionResult(RecordMutationExecutionOutcome.RollbackConfirmed,
                new AtomicMutationProcessingResult(AtomicMutationProcessingOutcome.Failed, [], error), error);
        }

        private static BaseError Error(string code) => new()
        {
            Code = code, Message = "The semantic certification fault was injected.", Category = ErrorCategory.Store,
        };
    }


    private sealed class FaultingProcessor(IAtomicMutationProcessor inner, FaultController controller)
        : IAtomicSemanticActivationProcessor
    {
        public bool ContainsSemanticActivation => inner is IAtomicSemanticActivationProcessor { ContainsSemanticActivation: true };
        public async ValueTask<AtomicMutationProcessingResult> ResolveReceiptAsync(
            BaseRecordMutationFact[] committedMutations, CancellationToken cancellationToken = default)
        {
            if (controller.Fault == BaseSemanticActivationCertificationFault.NonCooperativeReceipt)
                await controller.WaitForReleaseAsync().ConfigureAwait(false);
            return await inner.ResolveReceiptAsync(committedMutations, cancellationToken).ConfigureAwait(false);
        }

        public async ValueTask<AtomicMutationProcessingResult> ResolveReceiptAsync(
            BaseAtomicReceiptResult committedResult, CancellationToken cancellationToken = default)
        {
            if (controller.Fault == BaseSemanticActivationCertificationFault.NonCooperativeReceipt)
                await controller.WaitForReleaseAsync().ConfigureAwait(false);
            return await inner.ResolveReceiptAsync(committedResult, cancellationToken).ConfigureAwait(false);
        }

        public ValueTask<AtomicMutationProcessingResult> ProcessAsync(
            IAtomicRecordSession session, CancellationToken cancellationToken = default) =>
            inner.ProcessAsync(new FaultingSession(session, controller), cancellationToken);
    }

    private sealed class FaultingSession(IAtomicRecordSession inner, FaultController controller)
        : IAtomicRecordSession
    {
        private async ValueTask WaitAsync(BaseSemanticActivationCertificationFault phase)
        {
            if (controller.Fault == phase) await controller.WaitForReleaseAsync().ConfigureAwait(false);
        }

        public async ValueTask<OperationResult<BaseCapturedAtomicExecution>> CaptureAtomicExecutionAsync(BaseAtomicExecutionRequest request, CancellationToken cancellationToken = default)
        {
            await WaitAsync(BaseSemanticActivationCertificationFault.NonCooperativeCapture).ConfigureAwait(false);
            OperationResult<BaseCapturedAtomicExecution> result = await inner.CaptureAtomicExecutionAsync(request, cancellationToken).ConfigureAwait(false);
            if (result.Value is { } value && controller.Operation == BaseSemanticActivationCertificationOperation.HostileCapture)
                return OperationResults.Ok(value with { CaptureDigest = value.CaptureDigest + ":hostile" });
            if (result.Value is { SemanticActivation: { } semantic } captured && controller.Fault is { } fault
                && fault is >= BaseSemanticActivationCertificationFault.SubstituteKey
                    and <= BaseSemanticActivationCertificationFault.CorruptAccounting)
            {
                BaseCapturedSemanticActivationEvidence hostile = Mutate(semantic, fault);
                hostile = hostile with { Checksum = BaseSemanticActivationEvidenceContract.CapturedChecksum(request.SemanticActivation!, hostile) };
                return OperationResults.Ok(captured with
                {
                    SemanticActivation = hostile,
                    CaptureDigest = Convert.ToHexStringLower(hostile.Checksum.AsSpan()),
                });
            }
            return result;
        }

        private static BaseCapturedSemanticActivationEvidence Mutate(
            BaseCapturedSemanticActivationEvidence value, BaseSemanticActivationCertificationFault fault)
        {
            byte[] substituted = SHA256.HashData(Encoding.UTF8.GetBytes("base.semanticActivation.hostileSubstitution.v1"));
            if (fault == BaseSemanticActivationCertificationFault.SubstituteKey && value.Missing is { } missing)
                return value with { Missing = missing with { Key = BaseSemanticActivationKeyDigest.Create(substituted) } };
            if (fault is BaseSemanticActivationCertificationFault.SubstituteScopeBinding
                or BaseSemanticActivationCertificationFault.SubstituteSeekDigest)
            {
                BaseSemanticActivationScopeBinding binding = value.ScopeDirectory.ResultingBinding;
                binding = fault == BaseSemanticActivationCertificationFault.SubstituteScopeBinding
                    ? binding with { BindingId = substituted.ToImmutableArray() }
                    : binding with { SeekDigest = substituted.ToImmutableArray() };
                binding = binding with { Checksum = BaseSemanticActivationEvidenceContract.ScopeBindingChecksum(binding) };
                return value with { ScopeDirectory = value.ScopeDirectory with
                {
                    ResultingBinding = binding,
                    Checksum = BaseSemanticActivationEvidenceContract.ScopeDirectoryChecksum(binding),
                } };
            }
            if (value.Live is { } live && fault is (BaseSemanticActivationCertificationFault.SubstituteSlotGeneration
                or BaseSemanticActivationCertificationFault.SubstituteActivation
                or BaseSemanticActivationCertificationFault.SubstituteDueAuthority))
            {
                live = fault switch
                {
                    BaseSemanticActivationCertificationFault.SubstituteSlotGeneration => live with { SlotGeneration = checked(live.SlotGeneration + 1) },
                    BaseSemanticActivationCertificationFault.SubstituteActivation => live with { ActivationId = live.ActivationId + ":hostile" },
                    _ => live with { Due = live.Due with { CanonicalUnixMilliseconds = checked(live.Due.CanonicalUnixMilliseconds + 1) } },
                };
                live = live with { Checksum = BaseSemanticActivationEvidenceContract.LiveChecksum(live) };
                return value with { Live = live };
            }
            if (fault == BaseSemanticActivationCertificationFault.CorruptInterval)
            {
                ImmutableArray<BaseAtomicReadIntervalEvidence> intervals = value.ReadIntervals.SetItem(0,
                    value.ReadIntervals[0] with { CanonicalLowerBound = substituted.ToImmutableArray() });
                return value with { ReadIntervals = intervals };
            }
            if (fault == BaseSemanticActivationCertificationFault.CorruptAccounting)
                return value with { Accounting = value.Accounting with { EvidenceBytes = checked(value.Accounting.EvidenceBytes + 1) } };
            throw new InvalidOperationException("base.semanticActivation.certificationInvalid");
        }

        public async ValueTask<OperationResult<BasePreparedAtomicExecution>> PrepareAtomicExecutionAsync(BaseCapturedAtomicExecution captured, BaseFinalizedAtomicExecutionPlan plan, CancellationToken cancellationToken = default)
        {
            await WaitAsync(BaseSemanticActivationCertificationFault.NonCooperativePrepare).ConfigureAwait(false);
            OperationResult<BasePreparedAtomicExecution> result = await inner.PrepareAtomicExecutionAsync(captured, plan, cancellationToken).ConfigureAwait(false);
            return result.Value is { } value && controller.Operation == BaseSemanticActivationCertificationOperation.HostilePrepare
                ? OperationResults.Ok(value with { PlanDigest = value.PlanDigest + ":hostile" }) : result;
        }

        public async ValueTask<OperationResult<BaseProvisionalAtomicExecution>> ApplyPreparedAtomicExecutionAsync(BasePreparedAtomicExecution prepared, CancellationToken cancellationToken = default)
        {
            await WaitAsync(BaseSemanticActivationCertificationFault.NonCooperativeApply).ConfigureAwait(false);
            OperationResult<BaseProvisionalAtomicExecution> result = await inner.ApplyPreparedAtomicExecutionAsync(prepared, cancellationToken).ConfigureAwait(false);
            return result.Value is { } value && controller.Operation == BaseSemanticActivationCertificationOperation.HostileApply
                ? OperationResults.Ok(value with { PlanDigest = value.PlanDigest + ":hostile" }) : result;
        }

        public ValueTask<OperationResult<BaseCapturedActivationGuardEvidence>> ValidateActivationGuardAsync(BaseActivationGuard guard, CancellationToken cancellationToken = default) => inner.ValidateActivationGuardAsync(guard, cancellationToken);
        public ValueTask<OperationResult<BaseTransactionalActivationCommitEvidence>> FinalizeActivationAsync(BaseTransactionalActivationFinalization finalization, CancellationToken cancellationToken = default) => inner.FinalizeActivationAsync(finalization, cancellationToken);
        public ValueTask<OperationResult<BaseSelectionMutationCommitAccounting>> MeasureSelectionMutationAsync(BaseAtomicReceiptResult receipt, BaseSelectionMutationResult result, CancellationToken cancellationToken = default) => inner.MeasureSelectionMutationAsync(receipt, result, cancellationToken);
        public ValueTask<OperationResult<RecordEnvelope>> GetAsync(CollectionDefinition collection, RecordId id, OperationContext context, CancellationToken cancellationToken = default) => inner.GetAsync(collection, id, context, cancellationToken);
        public ValueTask<OperationResult<RecordMutationSessionResult>> CreateAsync(CollectionDefinition collection, RecordCreateRequest request, RecordMutationSessionContext context, CancellationToken cancellationToken = default) => inner.CreateAsync(collection, request, context, cancellationToken);
        public ValueTask<OperationResult<RecordMutationSessionResult>> PatchAsync(CollectionDefinition collection, RecordId id, RecordPatchRequest request, RecordMutationSessionContext context, CancellationToken cancellationToken = default) => inner.PatchAsync(collection, id, request, context, cancellationToken);
        public ValueTask<OperationResult<RecordMutationSessionResult>> ReplaceAsync(CollectionDefinition collection, RecordId id, RecordReplaceRequest request, RecordMutationSessionContext context, CancellationToken cancellationToken = default) => inner.ReplaceAsync(collection, id, request, context, cancellationToken);
        public ValueTask<OperationResult<RecordMutationSessionResult>> DeleteAsync(CollectionDefinition collection, RecordId id, RecordDeleteRequest request, RecordMutationSessionContext context, CancellationToken cancellationToken = default) => inner.DeleteAsync(collection, id, request, context, cancellationToken);
        public ValueTask<OperationResult<long>> AdvancePurgeGenerationAsync(CollectionDefinition collection, long? expectedGeneration, CancellationToken cancellationToken = default) => inner.AdvancePurgeGenerationAsync(collection, expectedGeneration, cancellationToken);
        public ValueTask<OperationResult<BaseSubjectLifecycleCheckpointResult>> AdvanceSubjectLifecycleCheckpointAsync(BaseSubjectLifecycleProviderCheckpointRequest request, CancellationToken cancellationToken = default) => inner.AdvanceSubjectLifecycleCheckpointAsync(request, cancellationToken);
        public ValueTask<OperationResult<BaseSubjectAcknowledgementResult>> ApplySubjectRetirementAcknowledgementAsync(BaseSubjectRetirementProviderAcknowledgementRequest request, CancellationToken cancellationToken = default) => inner.ApplySubjectRetirementAcknowledgementAsync(request, cancellationToken);
        public ValueTask<OperationResult<BaseSubjectRetirementTimeoutResult>> ApplySubjectRetirementTimeoutAsync(BaseSubjectRetirementProviderTimeoutRequest request, CancellationToken cancellationToken = default) => inner.ApplySubjectRetirementTimeoutAsync(request, cancellationToken);
        public ValueTask<OperationResult<BaseSubjectRetirementOverrideResult>> ApplySubjectRetirementOverrideAsync(BaseSubjectRetirementProviderOverrideRequest request, CancellationToken cancellationToken = default) => inner.ApplySubjectRetirementOverrideAsync(request, cancellationToken);
        public ValueTask<OperationResult<BaseSubjectRetirementPurgeApplied>> ApplySubjectRetirementPurgeAsync(BaseSubjectRetirementProviderPurgeRequest request, CancellationToken cancellationToken = default) => inner.ApplySubjectRetirementPurgeAsync(request, cancellationToken);
        public ValueTask<OperationResult> ApplyMutationProjectionsAsync(BaseAtomicMutationProjectionRequest request, CancellationToken cancellationToken = default) => inner.ApplyMutationProjectionsAsync(request, cancellationToken);
    }
}
