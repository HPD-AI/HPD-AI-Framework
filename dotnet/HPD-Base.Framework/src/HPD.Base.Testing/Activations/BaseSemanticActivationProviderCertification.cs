using System.Collections.Immutable;
using System.Security.Cryptography;

namespace HPD.Base.Testing;

/// <summary>Identifies every closed L53 provider-certification fault.</summary>
public enum BaseSemanticActivationCertificationFault
{
    /// <summary>Loses the response after a committed transition.</summary>
    ResponseLossAfterCommit = 1,
    /// <summary>Makes commit observation indeterminate.</summary>
    IndeterminateCommit = 2,
    /// <summary>Retains capture beyond its cooperative deadline.</summary>
    NonCooperativeCapture = 3,
    /// <summary>Retains preparation beyond its cooperative deadline.</summary>
    NonCooperativePrepare = 4,
    /// <summary>Retains provisional apply beyond its cooperative deadline.</summary>
    NonCooperativeApply = 5,
    /// <summary>Retains receipt resolution beyond its cooperative deadline.</summary>
    NonCooperativeReceipt = 6,
    /// <summary>Retains maintenance beyond its cooperative deadline.</summary>
    NonCooperativeMaintenance = 7,
    /// <summary>Retains restore beyond its cooperative deadline.</summary>
    NonCooperativeRestore = 8,
    /// <summary>Substitutes the semantic key digest.</summary>
    SubstituteKey = 9,
    /// <summary>Substitutes protected scope-binding evidence.</summary>
    SubstituteScopeBinding = 10,
    /// <summary>Substitutes provider seek evidence.</summary>
    SubstituteSeekDigest = 11,
    /// <summary>Substitutes the captured slot generation.</summary>
    SubstituteSlotGeneration = 12,
    /// <summary>Substitutes the mapped activation authority.</summary>
    SubstituteActivation = 13,
    /// <summary>Substitutes accepted due-time authority.</summary>
    SubstituteDueAuthority = 14,
    /// <summary>Corrupts read-interval evidence.</summary>
    CorruptInterval = 15,
    /// <summary>Corrupts provider accounting evidence.</summary>
    CorruptAccounting = 16,
    /// <summary>Corrupts a terminal retirement tombstone.</summary>
    CorruptRetirement = 17,
    /// <summary>Corrupts a permanent absence marker.</summary>
    CorruptAbsence = 18,
    /// <summary>Corrupts a durable recovery entry.</summary>
    CorruptRecoveryEntry = 19,
    /// <summary>Interrupts maintenance publication.</summary>
    InterruptMaintenancePublication = 20,
    /// <summary>Interrupts restore authority publication.</summary>
    InterruptRestorePublication = 21,
    /// <summary>Advances retention beyond required recovery authority.</summary>
    RetentionOvertake = 22,
}

/// <summary>Requests one exact fault occurrence.</summary>
public sealed record BaseSemanticActivationCertificationFaultRequest
{
    /// <summary>Gets the exact closed fault.</summary>
    public required BaseSemanticActivationCertificationFault Fault { get; init; }
    /// <summary>Gets the positive occurrence to fault.</summary>
    public required int Occurrence { get; init; }
}

/// <summary>Identifies one genuine provider operation executed by the host.</summary>
public enum BaseSemanticActivationCertificationOperation
{
    /// <summary>Creates a missing semantic activation.</summary>
    Ensure = 1,
    /// <summary>Races ensures from distinct parent activations.</summary>
    EnsureDifferentParent = 2,
    /// <summary>Replays an existing semantic activation.</summary>
    ExistingReplay = 3,
    /// <summary>Retires a terminal semantic activation.</summary>
    Retire = 4,
    /// <summary>Resolves an identified outer receipt.</summary>
    ResolveReceipt = 5,
    /// <summary>Exercises hostile capture evidence.</summary>
    HostileCapture = 6,
    /// <summary>Exercises hostile prepared evidence.</summary>
    HostilePrepare = 7,
    /// <summary>Exercises hostile provisional evidence.</summary>
    HostileApply = 8,
    /// <summary>Exercises exact and max-plus-one accounting limits.</summary>
    AccountingLimits = 9,
    /// <summary>Inspects bounded private provider state.</summary>
    Inspect = 10,
    /// <summary>Executes semantic maintenance.</summary>
    Maintain = 11,
    /// <summary>Backs up and restores semantic authority.</summary>
    BackupRestore = 12,
    /// <summary>Exercises recovery-floor enforcement.</summary>
    RecoveryFloor = 13,
    /// <summary>Releases retained late work and observes recovery.</summary>
    NonCooperativeRelease = 14,
    /// <summary>Captures exact bounded maintenance-command authority.</summary>
    MaintenanceAuthority = 15,
}

/// <summary>Contains one fixture-authored closed input whose execution remains host-owned.</summary>
public sealed record BaseSemanticActivationCertificationOperationInput
{
    /// <summary>Gets the processor for an atomic semantic operation.</summary>
    public IAtomicMutationProcessor? AtomicProcessor { get; init; }
    /// <summary>Gets the identified atomic request.</summary>
    public RecordMutationExecutionRequest? AtomicRequest { get; init; }
    /// <summary>Gets the second processor for an independently parented race.</summary>
    public IAtomicMutationProcessor? SecondaryAtomicProcessor { get; init; }
    /// <summary>Gets the second identified request for an independently parented race.</summary>
    public RecordMutationExecutionRequest? SecondaryAtomicRequest { get; init; }
    /// <summary>Gets a fresh recapture processor for the first identified race request.</summary>
    public IAtomicMutationProcessor? AtomicRetryProcessor { get; init; }
    /// <summary>Gets the first retry request, byte-identical in durable identity.</summary>
    public RecordMutationExecutionRequest? AtomicRetryRequest { get; init; }
    /// <summary>Gets a fresh recapture processor for the second identified race request.</summary>
    public IAtomicMutationProcessor? SecondaryAtomicRetryProcessor { get; init; }
    /// <summary>Gets the second retry request, byte-identical in durable identity.</summary>
    public RecordMutationExecutionRequest? SecondaryAtomicRetryRequest { get; init; }
    /// <summary>Gets the identity resolved by a receipt operation.</summary>
    public BaseMutationRequestIdentity? ReceiptIdentity { get; init; }
    /// <summary>Gets a provider-private inspection request.</summary>
    public BaseSemanticActivationProviderInspectionRequest? Inspection { get; init; }
    /// <summary>Gets a provider-private maintenance-authority request.</summary>
    public BaseSemanticActivationMaintenanceAuthorityRequest? MaintenanceAuthority { get; init; }
    /// <summary>Gets a closed maintenance request.</summary>
    public BaseSemanticActivationMaintenanceRequest? Maintenance { get; init; }
    /// <summary>Gets an authenticated backup request.</summary>
    public BaseBackupRequest? Backup { get; init; }
    /// <summary>Gets the matching restore request.</summary>
    public BaseRestoreRequest? Restore { get; init; }
}

/// <summary>Discloses only purpose-bound certification authority for one semantic processor.</summary>
public interface IBaseSemanticActivationCertificationProcessor : IAtomicSemanticActivationProcessor
{
    /// <summary>Gets the exact current parent-activation authority checksum.</summary>
    ImmutableArray<byte> ParentActivationAuthorityChecksum { get; }
    /// <summary>Gets the parent-independent semantic intent checksum.</summary>
    ImmutableArray<byte> SemanticIntentChecksum { get; }
}

/// <summary>Supplies one isolated production-provider domain to a single host-owned case.</summary>
public interface IBaseSemanticActivationCertificationFixture : IAsyncDisposable
{
    /// <summary>Gets the exact selected immutable profile.</summary>
    BaseSemanticActivationCertificationSubject Subject { get; }
    /// <summary>Gets the production atomic store under certification.</summary>
    IAtomicRecordStore AtomicStore { get; }
    /// <summary>Gets the production L51 provider composed by L53.</summary>
    IBaseActivationProvider ActivationProvider { get; }
    /// <summary>Gets the semantic capability owner under certification.</summary>
    IBaseSemanticActivationCapabilityProvider SemanticProvider { get; }
    /// <summary>Gets the actual composed L50 capability exercised by the fixture.</summary>
    BaseModuleMutationCapability ModuleMutationCapability { get; }
    /// <summary>Gets semantic administration when advertised.</summary>
    IBaseSemanticActivationAdministration? SemanticAdministration { get; }
    /// <summary>Creates an authenticated backup through the production administration owner.</summary>
    ValueTask<OperationResult<BaseBackupManifest>> CreateBackupAsync(
        Stream destination, BaseBackupRequest request, CancellationToken cancellationToken);
    /// <summary>Restores an authenticated artifact through the production administration owner.</summary>
    ValueTask<OperationResult<BaseRestoreResult>> RestoreAsync(
        Stream source, BaseRestoreRequest request, CancellationToken cancellationToken);
    /// <summary>Creates one closed input for the named host-owned operation.</summary>
    ValueTask<BaseSemanticActivationCertificationOperationInput> CreateInputAsync(
        BaseSemanticActivationCertificationOperation operation, CancellationToken cancellationToken);
    /// <summary>Installs exactly one fault occurrence.</summary>
    ValueTask InstallFaultAsync(BaseSemanticActivationCertificationFaultRequest request, CancellationToken cancellationToken);
    /// <summary>Observes positive sequenced evidence after execution.</summary>
    ValueTask<BaseSemanticActivationCertificationObservation> ObserveAsync(CancellationToken cancellationToken);
    /// <summary>Releases one explicitly retained late operation.</summary>
    ValueTask<bool> ReleaseLateWorkAsync(BaseSemanticActivationCertificationFault fault, int occurrence, CancellationToken cancellationToken);
}

/// <summary>Creates a fresh store, graph, and restore domain for every certification case.</summary>
public interface IBaseSemanticActivationCertificationFixtureFactory
{
    /// <summary>Gets the profile common to every isolated fixture.</summary>
    BaseSemanticActivationCertificationSubject Subject { get; }
    /// <summary>Creates one isolated production-provider authority.</summary>
    ValueTask<IBaseSemanticActivationCertificationFixture> CreateAsync(string caseId, int ordinal,
        DateTimeOffset deadlineUtc, CancellationToken cancellationToken);
}

/// <summary>Adapts one isolated production store to provider-neutral executable certification.</summary>
public interface IBaseSemanticActivationCertificationStore : IAsyncDisposable
{
    /// <summary>Gets the configured logical store identity used by provider authority checks.</summary>
    string LogicalStoreId { get; }
    /// <summary>Gets the provider-valid timeout used to prove non-cooperative transaction handling.</summary>
    TimeSpan NonCooperativeTransactionTimeout { get; }
    /// <summary>Installs one provider-specific administration fault when applicable.</summary>
    ValueTask InstallFaultAsync(BaseSemanticActivationCertificationFaultRequest request, CancellationToken cancellationToken);
    /// <summary>Releases provider-specific retained work when applicable.</summary>
    ValueTask<bool> ReleaseLateWorkAsync(BaseSemanticActivationCertificationFault fault, int occurrence, CancellationToken cancellationToken);
    /// <summary>Gets whether the fixture proved durable recovery-floor dominance.</summary>
    bool RecoveryFloorVerified { get; }
    /// <summary>Gets the production atomic store.</summary>
    IAtomicRecordStore AtomicStore { get; }
    /// <summary>Gets the production activation provider.</summary>
    IBaseActivationProvider ActivationProvider { get; }
    /// <summary>Gets the production semantic capability owner.</summary>
    IBaseSemanticActivationCapabilityProvider SemanticProvider { get; }
    /// <summary>Gets the composed L50 capability.</summary>
    BaseModuleMutationCapability ModuleMutationCapability { get; }
    /// <summary>Gets semantic administration when advertised.</summary>
    IBaseSemanticActivationAdministration? SemanticAdministration { get; }
    /// <summary>Observes bounded retained provider state.</summary>
    ValueTask<(long Live, long Retired, long Absent, long Activations, long Receipts)> ObserveAsync(CancellationToken cancellationToken);
    /// <summary>Reads the sole retained semantic authority when present.</summary>
    ValueTask<ImmutableArray<byte>> ReadAuthorityAsync(CancellationToken cancellationToken);
    /// <summary>Observes bounded late-work accounting.</summary>
    (int Active, int Quarantined, int Released, int RejectedLateCompletions) ObserveLateWork();
    /// <summary>Applies one exact certification-only corruption to real provider state.</summary>
    ValueTask CorruptAsync(bool compactedAbsence, BaseSemanticActivationDefinitionIdentity definition, CancellationToken cancellationToken);
    /// <summary>Creates one production backup artifact.</summary>
    ValueTask<OperationResult<BaseBackupManifest>> CreateBackupAsync(Stream destination, BaseBackupRequest request, CancellationToken cancellationToken);
    /// <summary>Restores one production backup artifact.</summary>
    ValueTask<OperationResult<BaseRestoreResult>> RestoreAsync(Stream source, BaseRestoreRequest request, CancellationToken cancellationToken);
    /// <summary>Creates exact provider-specific administration input for an advertised case.</summary>
    ValueTask<BaseSemanticActivationCertificationOperationInput?> CreateAdministrationInputAsync(
        BaseSemanticActivationCertificationOperation operation, string caseId, int ordinal,
        DateTimeOffset deadlineUtc, CancellationToken cancellationToken);
}

/// <summary>Runs genuine named semantic operations against isolated production provider instances.</summary>
public static class BaseSemanticActivationProviderCertification
{
    private static readonly ImmutableArray<(string Id, BaseSemanticActivationCertificationOperation Operation, bool Maintenance)> Functional =
    [
        ("atomic-missing-ensure", BaseSemanticActivationCertificationOperation.Ensure, false),
        ("different-parent-race", BaseSemanticActivationCertificationOperation.EnsureDifferentParent, false),
        ("existing-replay", BaseSemanticActivationCertificationOperation.ExistingReplay, false),
        ("terminal-retirement", BaseSemanticActivationCertificationOperation.Retire, false),
        ("receipt-resolution", BaseSemanticActivationCertificationOperation.ResolveReceipt, false),
        ("hostile-capture", BaseSemanticActivationCertificationOperation.HostileCapture, false),
        ("hostile-prepare", BaseSemanticActivationCertificationOperation.HostilePrepare, false),
        ("hostile-apply", BaseSemanticActivationCertificationOperation.HostileApply, false),
        ("accounting-limits", BaseSemanticActivationCertificationOperation.AccountingLimits, false),
        ("inspection", BaseSemanticActivationCertificationOperation.Inspect, true),
        ("maintenance-authority", BaseSemanticActivationCertificationOperation.MaintenanceAuthority, true),
        ("maintenance", BaseSemanticActivationCertificationOperation.Maintain, true),
        ("backup-restore", BaseSemanticActivationCertificationOperation.BackupRestore, true),
        ("recovery-floor", BaseSemanticActivationCertificationOperation.RecoveryFloor, true),
        ("noncooperative-release", BaseSemanticActivationCertificationOperation.NonCooperativeRelease, false),
    ];

    /// <summary>Executes every contract case against an isolated production-provider fixture.</summary>
    public static async ValueTask<BaseSemanticActivationCertificationReport> RunAsync(
        IBaseSemanticActivationCertificationFixtureFactory factory, TimeSpan caseTimeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(factory);
        if (caseTimeout <= TimeSpan.Zero || caseTimeout > TimeSpan.FromMinutes(10)
            || !BaseSemanticActivationCertificationContract.ValidateSubject(factory.Subject))
            throw new ArgumentException("base.semanticActivation.certificationInvalid");
        var results = ImmutableArray.CreateBuilder<BaseSemanticActivationCertificationCaseResult>(); int ordinal = 0;
        foreach ((string id, BaseSemanticActivationCertificationOperation operation, bool maintenance) in Functional)
            results.Add(await ExecuteAsync(factory, id, ordinal++, operation, maintenance, null, caseTimeout, cancellationToken).ConfigureAwait(false));
        foreach (BaseSemanticActivationCertificationFault fault in Enum.GetValues<BaseSemanticActivationCertificationFault>())
            results.Add(await ExecuteAsync(factory, $"fault-{fault}", ordinal++, FaultOperation(fault),
                fault is BaseSemanticActivationCertificationFault.NonCooperativeMaintenance or BaseSemanticActivationCertificationFault.NonCooperativeRestore
                    or BaseSemanticActivationCertificationFault.InterruptMaintenancePublication or BaseSemanticActivationCertificationFault.InterruptRestorePublication
                    or BaseSemanticActivationCertificationFault.CorruptRecoveryEntry or BaseSemanticActivationCertificationFault.RetentionOvertake,
                fault, caseTimeout, cancellationToken).ConfigureAwait(false));
        ImmutableArray<BaseSemanticActivationCertificationCaseResult> cases = results.ToImmutable();
        if (!cases.Select(static item => item.Id).SequenceEqual(BaseSemanticActivationCertificationContract.MandatoryCaseIds, StringComparer.Ordinal))
            throw new InvalidOperationException("base.semanticActivation.certificationInvalid");
        return BaseSemanticActivationCertificationContract.CreateReport(factory.Subject, cases);
    }

    private static async ValueTask<BaseSemanticActivationCertificationCaseResult> ExecuteAsync(
        IBaseSemanticActivationCertificationFixtureFactory factory, string id, int ordinal,
        BaseSemanticActivationCertificationOperation operation, bool maintenance,
        BaseSemanticActivationCertificationFault? fault, TimeSpan timeout, CancellationToken cancellationToken)
    {
        await using IBaseSemanticActivationCertificationFixture fixture = await factory.CreateAsync(
            id, ordinal, DateTimeOffset.UtcNow.Add(timeout), cancellationToken).ConfigureAwait(false);
        ValidateFixture(factory.Subject, fixture);
        if (maintenance && !fixture.SemanticProvider.SemanticActivationCapability.MaintenanceSupported)
            return Case(id, ordinal, BaseSemanticActivationCertificationApplicability.NotAdvertised,
                OperationStatus.Ok, null, OperationStatus.Unsupported, "base.semanticActivation.certification.notAdvertised",
                null, BaseAtomicReceiptResolutionDisposition.NotApplicable, null, [], 1, NotAdvertisedObservation());
        BaseSemanticActivationCertificationFault? installedFault = fault
            ?? (operation == BaseSemanticActivationCertificationOperation.NonCooperativeRelease
                ? BaseSemanticActivationCertificationFault.NonCooperativeCapture : null);
        if (installedFault is { } injected) await fixture.InstallFaultAsync(new() { Fault = injected, Occurrence = 1 }, cancellationToken).ConfigureAwait(false);
        BaseSemanticActivationCertificationOperationInput input = await fixture.CreateInputAsync(operation, cancellationToken).ConfigureAwait(false);
        CertificationInvocation invocation;
        try { invocation = await InvokeAsync(fixture, operation, input, timeout, cancellationToken).ConfigureAwait(false); }
        catch (Exception exception) when (exception is not OperationCanceledException)
        { invocation = new(OperationStatus.StoreError, "base.semanticActivation.certification.failed", null, BaseAtomicReceiptResolutionDisposition.NotApplicable); }
        bool hostReceiptResolved = true;
        if (fault == BaseSemanticActivationCertificationFault.ResponseLossAfterCommit)
        {
            hostReceiptResolved = false;
            if (input.AtomicProcessor is not null && input.ReceiptIdentity is not null)
            {
                using var resolutionDeadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                resolutionDeadline.CancelAfter(timeout);
                RecordMutationExecutionResult resolution = await fixture.AtomicStore.ResolveAtomicReceiptAsync(
                    input.AtomicProcessor, input.ReceiptIdentity, timeout, resolutionDeadline.Token).ConfigureAwait(false);
                hostReceiptResolved = resolution.Outcome == RecordMutationExecutionOutcome.Committed
                    && resolution.ReceiptResolution == BaseAtomicReceiptResolutionDisposition.Found
                    && resolution.ReceiptAuthority?.ReceiptChecksum.Length == 32;
                if (hostReceiptResolved)
                    invocation = invocation with { ReceiptChecksum = resolution.ReceiptAuthority!.ReceiptChecksum };
            }
        }
        bool releaseRequired = installedFault is >= BaseSemanticActivationCertificationFault.NonCooperativeCapture
            and <= BaseSemanticActivationCertificationFault.NonCooperativeRestore;
        bool released = !releaseRequired || await fixture.ReleaseLateWorkAsync(installedFault!.Value, 1, cancellationToken).ConfigureAwait(false);
        BaseSemanticActivationCertificationObservation observed = await fixture.ObserveAsync(cancellationToken).ConfigureAwait(false);
        bool expected = ExpectedOutcome(operation, fault, invocation)
            && ValidateObservation(operation, fault, observed, releaseRequired, released, hostReceiptResolved,
                invocation.ReceiptChecksum);
        return Case(id, ordinal, BaseSemanticActivationCertificationApplicability.Executed,
            expected ? OperationStatus.Ok : OperationStatus.StoreError,
            expected ? null : "base.semanticActivation.certification.caseFailed",
            invocation.Status, invocation.Error, invocation.AtomicOutcome, invocation.ReceiptResolution,
            invocation.RequestDisposition, invocation.ReceiptChecksum, observed.Sequence, observed);
    }

    private static async ValueTask<CertificationInvocation> InvokeAsync(
        IBaseSemanticActivationCertificationFixture fixture, BaseSemanticActivationCertificationOperation operation,
        BaseSemanticActivationCertificationOperationInput input, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); deadline.CancelAfter(timeout);
        if (operation == BaseSemanticActivationCertificationOperation.EnsureDifferentParent)
        {
            if (input.AtomicProcessor is null || input.AtomicRequest is null
                || input.SecondaryAtomicProcessor is null || input.SecondaryAtomicRequest is null
                || input.AtomicRetryProcessor is null || input.AtomicRetryRequest is null
                || input.SecondaryAtomicRetryProcessor is null || input.SecondaryAtomicRetryRequest is null
                || !SameDurableRequest(input.AtomicRequest, input.AtomicRetryRequest)
                || !SameDurableRequest(input.SecondaryAtomicRequest, input.SecondaryAtomicRetryRequest)
                || SameDurableRequest(input.AtomicRequest, input.SecondaryAtomicRequest)
                || input.AtomicProcessor is not IBaseSemanticActivationCertificationProcessor primaryAuthority
                || input.SecondaryAtomicProcessor is not IBaseSemanticActivationCertificationProcessor secondaryAuthority
                || input.AtomicRetryProcessor is not IBaseSemanticActivationCertificationProcessor primaryRetryAuthority
                || input.SecondaryAtomicRetryProcessor is not IBaseSemanticActivationCertificationProcessor secondaryRetryAuthority
                || !RaceAuthorityValid(primaryAuthority, primaryRetryAuthority, secondaryAuthority, secondaryRetryAuthority)) return InvalidInput();
            RecordMutationExecutionResult[] raced = await Task.WhenAll(
                fixture.AtomicStore.ExecuteAtomicAsync(input.AtomicProcessor, input.AtomicRequest, deadline.Token).AsTask(),
                fixture.AtomicStore.ExecuteAtomicAsync(input.SecondaryAtomicProcessor, input.SecondaryAtomicRequest, deadline.Token).AsTask()).ConfigureAwait(false);
            bool acceptable = raced.All(static item => item.Outcome is RecordMutationExecutionOutcome.Committed
                or RecordMutationExecutionOutcome.ConflictRollbackConfirmed)
                && raced.Any(static item => item.Outcome == RecordMutationExecutionOutcome.Committed);
            var final = new RecordMutationExecutionResult?[2];
            for (int index = 0; index < raced.Length; index++)
                if (raced[index].Outcome == RecordMutationExecutionOutcome.Committed) final[index] = raced[index];
            for (int index = 0; acceptable && index < raced.Length; index++)
            {
                if (raced[index].Outcome != RecordMutationExecutionOutcome.ConflictRollbackConfirmed) continue;
                RecordMutationExecutionResult retry = index == 0
                    ? await fixture.AtomicStore.ExecuteAtomicAsync(input.AtomicRetryProcessor, input.AtomicRetryRequest, deadline.Token).ConfigureAwait(false)
                    : await fixture.AtomicStore.ExecuteAtomicAsync(input.SecondaryAtomicRetryProcessor, input.SecondaryAtomicRetryRequest, deadline.Token).ConfigureAwait(false);
                acceptable = retry.Outcome == RecordMutationExecutionOutcome.Committed;
                final[index] = retry;
            }
            acceptable = acceptable && final.All(static item => item is
                { Outcome: RecordMutationExecutionOutcome.Committed, RequestDisposition: BaseMutationRequestDisposition.Committed,
                    ReceiptAuthority.ReceiptChecksum.Length: 32 });
            BaseSemanticActivationEnsureDisposition[] semanticDispositions = acceptable
                ? final.Select(static item => item!.Processing?.Receipt.ModuleMutation?.SemanticActivation?.EnsureDisposition)
                    .Where(static item => item is not null).Select(static item => item!.Value).Order().ToArray()
                : [];
            acceptable = acceptable && semanticDispositions.SequenceEqual(
                [BaseSemanticActivationEnsureDisposition.Created, BaseSemanticActivationEnsureDisposition.Existing]);
            acceptable = acceptable && !CryptographicOperations.FixedTimeEquals(
                final[0]!.ReceiptAuthority!.ReceiptChecksum.AsSpan(),
                final[1]!.ReceiptAuthority!.ReceiptChecksum.AsSpan());
            ImmutableArray<byte> combinedReceipts = acceptable ? CombinedReceiptChecksum(
                final[0]!.ReceiptAuthority!.ReceiptChecksum, final[1]!.ReceiptAuthority!.ReceiptChecksum) : [];
            return acceptable
                ? new(OperationStatus.Ok, null, RecordMutationExecutionOutcome.Committed,
                    BaseAtomicReceiptResolutionDisposition.NotApplicable, BaseMutationRequestDisposition.Committed,
                    combinedReceipts)
                : new(OperationStatus.StoreError, "base.semanticActivation.certification.raceInvalid", null, BaseAtomicReceiptResolutionDisposition.NotApplicable);
        }
        if (operation is BaseSemanticActivationCertificationOperation.Ensure
            or BaseSemanticActivationCertificationOperation.ExistingReplay
            or BaseSemanticActivationCertificationOperation.Retire
            or BaseSemanticActivationCertificationOperation.HostileCapture
            or BaseSemanticActivationCertificationOperation.HostilePrepare
            or BaseSemanticActivationCertificationOperation.HostileApply
            or BaseSemanticActivationCertificationOperation.AccountingLimits
            or BaseSemanticActivationCertificationOperation.NonCooperativeRelease)
        {
            if (input.AtomicProcessor is null || input.AtomicRequest is null) return InvalidInput();
            RecordMutationExecutionResult value = await fixture.AtomicStore.ExecuteAtomicAsync(input.AtomicProcessor, input.AtomicRequest, deadline.Token).ConfigureAwait(false);
            return new(MapStatus(value), value.Error?.Code, value.Outcome, value.ReceiptResolution,
                value.RequestDisposition, value.ReceiptAuthority?.ReceiptChecksum ?? []);
        }
        if (operation == BaseSemanticActivationCertificationOperation.ResolveReceipt)
        {
            if (input.AtomicProcessor is null || input.ReceiptIdentity is null) return InvalidInput();
            RecordMutationExecutionResult value = await fixture.AtomicStore.ResolveAtomicReceiptAsync(input.AtomicProcessor, input.ReceiptIdentity, timeout, deadline.Token).ConfigureAwait(false);
            return new(MapStatus(value), value.Error?.Code, value.Outcome, value.ReceiptResolution,
                value.RequestDisposition, value.ReceiptAuthority?.ReceiptChecksum ?? []);
        }
        if (operation == BaseSemanticActivationCertificationOperation.Inspect && fixture.SemanticAdministration is { } semantic && input.Inspection is not null)
        { BaseResult<BaseSemanticActivationProviderInspectionPage> value = await semantic.InspectAsync(input.Inspection, deadline.Token).ConfigureAwait(false); return new(value.Status, value is BaseFailure<BaseSemanticActivationProviderInspectionPage> f ? f.Error.Code : null, null, BaseAtomicReceiptResolutionDisposition.NotApplicable); }
        if (operation == BaseSemanticActivationCertificationOperation.MaintenanceAuthority && fixture.SemanticAdministration is { } authorityProvider && input.MaintenanceAuthority is not null)
        {
            BaseResult<BaseSemanticActivationMaintenanceAuthority> value = await authorityProvider.InspectMaintenanceAuthorityAsync(input.MaintenanceAuthority, deadline.Token).ConfigureAwait(false);
            bool valid = value is BaseSuccess<BaseSemanticActivationMaintenanceAuthority> success
                && success.Value.ExaminedRows == success.Value.LiveCount + success.Value.RetiredCount + success.Value.AbsenceCount
                && success.Value.Checksum.Length == 32
                && CryptographicOperations.FixedTimeEquals(success.Value.Checksum.AsSpan(),
                    BaseSemanticActivationMaintenanceAuthorityContract.Checksum(input.MaintenanceAuthority, success.Value).AsSpan());
            return valid ? new(OperationStatus.Ok, null, null, BaseAtomicReceiptResolutionDisposition.NotApplicable)
                : new(value.Status, value is BaseFailure<BaseSemanticActivationMaintenanceAuthority> failure
                    ? failure.Error.Code : BaseSemanticActivationErrorCodes.ProviderContractInvalid, null,
                    BaseAtomicReceiptResolutionDisposition.NotApplicable);
        }
        if (operation == BaseSemanticActivationCertificationOperation.Maintain && fixture.SemanticAdministration is { } admin && input.Maintenance is not null)
        { BaseResult<BaseSemanticActivationMaintenanceResult> value = await admin.ExecuteAsync(input.Maintenance, deadline.Token).ConfigureAwait(false); return new(value.Status, value is BaseFailure<BaseSemanticActivationMaintenanceResult> f ? f.Error.Code : null, null, BaseAtomicReceiptResolutionDisposition.NotApplicable); }
        if (operation is BaseSemanticActivationCertificationOperation.BackupRestore or BaseSemanticActivationCertificationOperation.RecoveryFloor
            && input.Backup is not null && input.Restore is not null)
        { using var artifact = new MemoryStream(); OperationResult<BaseBackupManifest> backup = await fixture.CreateBackupAsync(artifact, input.Backup, deadline.Token).ConfigureAwait(false); if (!backup.IsSuccess()) return new(backup.Status, backup.Error?.Code, null, BaseAtomicReceiptResolutionDisposition.NotApplicable); artifact.Position = 0; OperationResult<BaseRestoreResult> restore = await fixture.RestoreAsync(artifact, input.Restore, deadline.Token).ConfigureAwait(false); return new(restore.Status, restore.Error?.Code, null, BaseAtomicReceiptResolutionDisposition.NotApplicable); }
        return new(OperationStatus.Unsupported, "base.semanticActivation.certification.operationUnavailable", null, BaseAtomicReceiptResolutionDisposition.NotApplicable);
    }

    private static OperationStatus MapStatus(RecordMutationExecutionResult value) => value.Outcome switch
    {
        RecordMutationExecutionOutcome.Committed => OperationStatus.Ok,
        RecordMutationExecutionOutcome.ConflictRollbackConfirmed => OperationStatus.Conflict,
        RecordMutationExecutionOutcome.CancelledRollbackConfirmed => OperationStatus.StoreError,
        RecordMutationExecutionOutcome.RollbackConfirmed when value.Error?.Code == BaseSemanticActivationErrorCodes.BudgetExceeded => OperationStatus.ValidationFailed,
        RecordMutationExecutionOutcome.RollbackConfirmed when value.Error?.Code == BaseSemanticActivationErrorCodes.ProviderContractInvalid => OperationStatus.CapabilityUnavailable,
        _ => OperationStatus.StoreError,
    };

    private static CertificationInvocation InvalidInput() => new(OperationStatus.ValidationFailed,
        "base.semanticActivation.certification.inputInvalid", null, BaseAtomicReceiptResolutionDisposition.NotApplicable);

    private static bool SameDurableRequest(RecordMutationExecutionRequest left, RecordMutationExecutionRequest right)
    {
        BaseAtomicMutationExecutionRequest? a = left.AtomicRequest; BaseAtomicMutationExecutionRequest? b = right.AtomicRequest;
        return a is not null && b is not null && a.Identity.Scope == b.Identity.Scope && a.Identity.Operation == b.Identity.Operation
            && a.Identity.IdempotencyKey == b.Identity.IdempotencyKey
            && CryptographicOperations.FixedTimeEquals(a.Identity.Fingerprint.ToArray(), b.Identity.Fingerprint.ToArray())
            && CryptographicOperations.FixedTimeEquals(a.StructuralDigest, b.StructuralDigest);
    }

    private static bool RaceAuthorityValid(IBaseSemanticActivationCertificationProcessor primary,
        IBaseSemanticActivationCertificationProcessor primaryRetry,
        IBaseSemanticActivationCertificationProcessor secondary,
        IBaseSemanticActivationCertificationProcessor secondaryRetry) =>
        primary.ParentActivationAuthorityChecksum.Length == 32 && secondary.ParentActivationAuthorityChecksum.Length == 32
        && primary.SemanticIntentChecksum.Length == 32 && secondary.SemanticIntentChecksum.Length == 32
        && !CryptographicOperations.FixedTimeEquals(primary.ParentActivationAuthorityChecksum.AsSpan(), secondary.ParentActivationAuthorityChecksum.AsSpan())
        && CryptographicOperations.FixedTimeEquals(primary.ParentActivationAuthorityChecksum.AsSpan(), primaryRetry.ParentActivationAuthorityChecksum.AsSpan())
        && CryptographicOperations.FixedTimeEquals(secondary.ParentActivationAuthorityChecksum.AsSpan(), secondaryRetry.ParentActivationAuthorityChecksum.AsSpan())
        && CryptographicOperations.FixedTimeEquals(primary.SemanticIntentChecksum.AsSpan(), secondary.SemanticIntentChecksum.AsSpan())
        && CryptographicOperations.FixedTimeEquals(primary.SemanticIntentChecksum.AsSpan(), primaryRetry.SemanticIntentChecksum.AsSpan())
        && CryptographicOperations.FixedTimeEquals(secondary.SemanticIntentChecksum.AsSpan(), secondaryRetry.SemanticIntentChecksum.AsSpan());

    private static ImmutableArray<byte> CombinedReceiptChecksum(ImmutableArray<byte> left, ImmutableArray<byte> right)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData("base.semanticActivation.certificationRaceReceipts.v2\0"u8);
        foreach (ImmutableArray<byte> value in new[] { left, right }.OrderBy(static item => Convert.ToHexString(item.AsSpan()), StringComparer.Ordinal))
            hash.AppendData(value.AsSpan());
        return hash.GetHashAndReset().ToImmutableArray();
    }

    private static bool ExpectedOutcome(BaseSemanticActivationCertificationOperation operation,
        BaseSemanticActivationCertificationFault? fault, CertificationInvocation value)
    {
        if (fault is null)
        {
            if (operation == BaseSemanticActivationCertificationOperation.ExistingReplay)
                return value.Status == OperationStatus.Ok && value.Error is null
                    && value.AtomicOutcome == RecordMutationExecutionOutcome.Committed
                    && value.ReceiptResolution == BaseAtomicReceiptResolutionDisposition.NotApplicable
                    && value.RequestDisposition == BaseMutationRequestDisposition.Duplicate && value.ReceiptChecksum.Length == 32;
            if (operation == BaseSemanticActivationCertificationOperation.ResolveReceipt)
                return value.Status == OperationStatus.Ok && value.Error is null
                    && value.AtomicOutcome == RecordMutationExecutionOutcome.Committed
                    && value.ReceiptResolution == BaseAtomicReceiptResolutionDisposition.Found
                    && value.RequestDisposition == BaseMutationRequestDisposition.Duplicate && value.ReceiptChecksum.Length == 32;
            if (operation is BaseSemanticActivationCertificationOperation.HostileCapture
                or BaseSemanticActivationCertificationOperation.HostilePrepare
                or BaseSemanticActivationCertificationOperation.HostileApply)
                return value.Status == OperationStatus.CapabilityUnavailable
                    && value.Error == BaseSemanticActivationErrorCodes.ProviderContractInvalid
                    && value.AtomicOutcome == RecordMutationExecutionOutcome.RollbackConfirmed;
            if (operation == BaseSemanticActivationCertificationOperation.AccountingLimits)
                return value.Status == OperationStatus.ValidationFailed
                    && value.Error == BaseSemanticActivationErrorCodes.BudgetExceeded
                    && value.AtomicOutcome == RecordMutationExecutionOutcome.RollbackConfirmed;
            if (operation == BaseSemanticActivationCertificationOperation.NonCooperativeRelease)
                return value.Status == OperationStatus.StoreError
                    && value.Error == BaseSemanticActivationErrorCodes.TransactionTimeout;
            if (operation == BaseSemanticActivationCertificationOperation.Maintain)
                return value.Status is OperationStatus.Ok or OperationStatus.Updated && value.Error is null;
            return value.Status == OperationStatus.Ok && value.Error is null;
        }
        (OperationStatus status, string error) = fault.Value switch
        {
            BaseSemanticActivationCertificationFault.ResponseLossAfterCommit or BaseSemanticActivationCertificationFault.IndeterminateCommit =>
                (OperationStatus.StoreError, BaseSemanticActivationErrorCodes.CommitIndeterminate),
            BaseSemanticActivationCertificationFault.NonCooperativeCapture or BaseSemanticActivationCertificationFault.NonCooperativePrepare
                or BaseSemanticActivationCertificationFault.NonCooperativeApply =>
                (OperationStatus.StoreError, BaseSemanticActivationErrorCodes.TransactionTimeout),
            BaseSemanticActivationCertificationFault.NonCooperativeReceipt =>
                (OperationStatus.StoreError, BaseSemanticActivationErrorCodes.ReceiptResolutionTimeout),
            BaseSemanticActivationCertificationFault.NonCooperativeMaintenance or BaseSemanticActivationCertificationFault.NonCooperativeRestore =>
                (OperationStatus.StoreError, BaseSemanticActivationErrorCodes.MaintenanceTimeout),
            BaseSemanticActivationCertificationFault.InterruptMaintenancePublication or BaseSemanticActivationCertificationFault.InterruptRestorePublication =>
                (OperationStatus.StoreError, BaseSemanticActivationErrorCodes.MaintenanceIndeterminate),
            BaseSemanticActivationCertificationFault.CorruptRetirement or BaseSemanticActivationCertificationFault.CorruptAbsence =>
                (OperationStatus.StoreError, BaseSemanticActivationErrorCodes.Corrupt),
            BaseSemanticActivationCertificationFault.CorruptRecoveryEntry or BaseSemanticActivationCertificationFault.RetentionOvertake =>
                (OperationStatus.StoreError, BaseSemanticActivationErrorCodes.RecoveryProofInvalid),
            _ => (OperationStatus.CapabilityUnavailable, BaseSemanticActivationErrorCodes.ProviderContractInvalid),
        };
        return value.Status == status && string.Equals(value.Error, error, StringComparison.Ordinal)
            && (fault is not (BaseSemanticActivationCertificationFault.ResponseLossAfterCommit
                or BaseSemanticActivationCertificationFault.IndeterminateCommit)
                || value.AtomicOutcome == RecordMutationExecutionOutcome.Indeterminate);
    }

    private static bool ValidateObservation(BaseSemanticActivationCertificationOperation operation,
        BaseSemanticActivationCertificationFault? fault, BaseSemanticActivationCertificationObservation value,
        bool releaseRequired, bool released, bool hostReceiptResolved, ImmutableArray<byte> receiptChecksum)
    {
        if (value.Sequence <= 0 || value.Evidence.IsDefaultOrEmpty || value.LiveSlots < 0 || value.RetiredSlots < 0
            || value.AbsenceMarkers < 0 || value.Activations < 0 || value.Receipts < 0 || value.ActiveWork < 0
            || value.QuarantinedWork < 0 || value.ReleasedWork < 0 || value.RejectedLateCompletions < 0) return false;
        if (releaseRequired && (!released || value.ActiveWork != 0 || value.QuarantinedWork != 0
            || value.ReleasedWork < 1 || value.RejectedLateCompletions < 1)) return false;
        if (operation == BaseSemanticActivationCertificationOperation.AccountingLimits
            && (!value.ExactLimitAccepted || !value.MaxPlusOneRejected)) return false;
        if (fault is null && operation is (BaseSemanticActivationCertificationOperation.Ensure
            or BaseSemanticActivationCertificationOperation.EnsureDifferentParent
            or BaseSemanticActivationCertificationOperation.ExistingReplay)
            && (value.LiveSlots != 1 || value.Activations != 1)) return false;
        if (fault is null && operation == BaseSemanticActivationCertificationOperation.EnsureDifferentParent
            && value.Receipts != 2) return false;
        if (fault is null && operation is (BaseSemanticActivationCertificationOperation.ExistingReplay
            or BaseSemanticActivationCertificationOperation.ResolveReceipt)
            && (value.Receipts != 1 || value.AuthorityBeforeChecksum.Length != 32
                || value.AuthorityAfterChecksum.Length != 32
                || !CryptographicOperations.FixedTimeEquals(value.AuthorityBeforeChecksum.AsSpan(), value.AuthorityAfterChecksum.AsSpan()))) return false;
        if (fault is null && operation == BaseSemanticActivationCertificationOperation.Retire
            && (value.LiveSlots != 0 || value.RetiredSlots != 1 || value.Activations != 1)) return false;
        if (operation == BaseSemanticActivationCertificationOperation.RecoveryFloor && !value.RecoveryFloorVerified) return false;
        if (fault == BaseSemanticActivationCertificationFault.ResponseLossAfterCommit
            && (!hostReceiptResolved || !value.ReceiptResolved || value.Receipts != 1 || value.LiveSlots != 1
                || value.Activations != 1 || receiptChecksum.Length != 32 || value.AuthorityBeforeChecksum.Length != 32
                || value.AuthorityAfterChecksum.Length != 32
                || !CryptographicOperations.FixedTimeEquals(value.AuthorityBeforeChecksum.AsSpan(), value.AuthorityAfterChecksum.AsSpan()))) return false;
        if (releaseRequired && fault == BaseSemanticActivationCertificationFault.NonCooperativeReceipt
            && (value.LiveSlots != 1 || value.RetiredSlots != 0 || value.AbsenceMarkers != 0
                || value.Activations != 1 || value.Receipts != 1)) return false;
        if (releaseRequired && fault != BaseSemanticActivationCertificationFault.NonCooperativeReceipt
            && (value.LiveSlots != 0 || value.RetiredSlots != 0 || value.AbsenceMarkers != 0
                || value.Activations != 0 || value.Receipts != 0)) return false;
        if (fault == BaseSemanticActivationCertificationFault.IndeterminateCommit && value.ReceiptResolved) return false;
        return true;
    }

    private readonly record struct CertificationInvocation(
        OperationStatus Status, string? Error, RecordMutationExecutionOutcome? AtomicOutcome,
        BaseAtomicReceiptResolutionDisposition ReceiptResolution,
        BaseMutationRequestDisposition? RequestDisposition = null,
        ImmutableArray<byte> ReceiptChecksum = default);

    private static BaseSemanticActivationCertificationObservation NotAdvertisedObservation() => new()
    {
        Sequence = 1, Evidence = "not-advertised"u8.ToArray().ToImmutableArray(), LiveSlots = 0, RetiredSlots = 0,
        AbsenceMarkers = 0, Activations = 0, Receipts = 0, ActiveWork = 0, QuarantinedWork = 0,
        ReleasedWork = 0, RejectedLateCompletions = 0, ExactLimitAccepted = false, MaxPlusOneRejected = false,
        RecoveryFloorVerified = false, ReceiptResolved = false,
        AuthorityBeforeChecksum = [], AuthorityAfterChecksum = [],
    };


    private static void ValidateFixture(BaseSemanticActivationCertificationSubject subject, IBaseSemanticActivationCertificationFixture fixture)
    {
        if (!BaseSemanticActivationCertificationContract.ValidateSubject(fixture.Subject)
            || !SubjectsEqual(subject, fixture.Subject)
            || !BaseActivationCertificationReceiptContract.Validate(fixture.ActivationProvider.Descriptor)
            || !CryptographicOperations.FixedTimeEquals(subject.ActivationCapabilityChecksum.AsSpan(),
                BaseActivationCertificationReceiptContract.CapabilityChecksum(fixture.ActivationProvider.Descriptor.Capability).AsSpan())
            || !CryptographicOperations.FixedTimeEquals(subject.ModuleMutationCapabilityChecksum.AsSpan(),
                BaseSemanticActivationCertificationContract.ModuleMutationCapabilityChecksum(fixture.ModuleMutationCapability).AsSpan())
            || !BaseSemanticActivationCapabilityContract.IsValid(fixture.SemanticProvider.SemanticActivationCapability)
            || !CryptographicOperations.FixedTimeEquals(subject.SemanticCapabilityChecksum.AsSpan(), fixture.SemanticProvider.SemanticActivationCapability.Checksum.AsSpan()))
            throw new InvalidOperationException("base.semanticActivation.certificationInvalid");
    }

    private static bool SubjectsEqual(BaseSemanticActivationCertificationSubject left, BaseSemanticActivationCertificationSubject right) =>
        string.Equals(left.ProviderId, right.ProviderId, StringComparison.Ordinal)
        && string.Equals(left.ProviderVersion, right.ProviderVersion, StringComparison.Ordinal)
        && string.Equals(left.StoreProviderKind, right.StoreProviderKind, StringComparison.Ordinal)
        && left.StoreProviderProtocolVersion == right.StoreProviderProtocolVersion
        && left.NativeDependencyReceipts.SequenceEqual(right.NativeDependencyReceipts, StringComparer.Ordinal)
        && CryptographicOperations.FixedTimeEquals(left.SemanticCapabilityChecksum.AsSpan(), right.SemanticCapabilityChecksum.AsSpan())
        && CryptographicOperations.FixedTimeEquals(left.ModuleMutationCapabilityChecksum.AsSpan(), right.ModuleMutationCapabilityChecksum.AsSpan())
        && CryptographicOperations.FixedTimeEquals(left.ActivationCapabilityChecksum.AsSpan(), right.ActivationCapabilityChecksum.AsSpan());

    private static BaseSemanticActivationCertificationOperation FaultOperation(BaseSemanticActivationCertificationFault fault) => fault switch
    {
        BaseSemanticActivationCertificationFault.NonCooperativeMaintenance or BaseSemanticActivationCertificationFault.InterruptMaintenancePublication => BaseSemanticActivationCertificationOperation.Maintain,
        BaseSemanticActivationCertificationFault.NonCooperativeRestore or BaseSemanticActivationCertificationFault.InterruptRestorePublication
            or BaseSemanticActivationCertificationFault.CorruptRecoveryEntry or BaseSemanticActivationCertificationFault.RetentionOvertake =>
            BaseSemanticActivationCertificationOperation.BackupRestore,
        BaseSemanticActivationCertificationFault.NonCooperativeReceipt => BaseSemanticActivationCertificationOperation.ResolveReceipt,
        BaseSemanticActivationCertificationFault.CorruptRetirement or BaseSemanticActivationCertificationFault.CorruptAbsence => BaseSemanticActivationCertificationOperation.Retire,
        _ => BaseSemanticActivationCertificationOperation.Ensure,
    };

    private static BaseSemanticActivationCertificationCaseResult Case(string id, int ordinal,
        BaseSemanticActivationCertificationApplicability applicability, OperationStatus status, string? error,
        OperationStatus observedStatus, string? observedError, RecordMutationExecutionOutcome? atomicOutcome,
        BaseAtomicReceiptResolutionDisposition receiptResolution, BaseMutationRequestDisposition? requestDisposition,
        ImmutableArray<byte> receiptChecksum, long sequence, BaseSemanticActivationCertificationObservation observation)
    {
        ImmutableArray<byte> canonicalReceipt = receiptChecksum.IsDefaultOrEmpty ? []
            : BaseSemanticActivationCertificationContract.CanonicalExecutedEvidence(id, "receipt");
        BaseSemanticActivationCertificationObservation canonicalObservation = observation with
        {
            Evidence = BaseSemanticActivationCertificationContract.CanonicalExecutedEvidence(id, "observation"),
            AuthorityBeforeChecksum = observation.AuthorityBeforeChecksum.IsDefaultOrEmpty ? []
                : BaseSemanticActivationCertificationContract.CanonicalExecutedEvidence(id, "authority"),
            AuthorityAfterChecksum = observation.AuthorityAfterChecksum.IsDefaultOrEmpty ? []
                : BaseSemanticActivationCertificationContract.CanonicalExecutedEvidence(id, "authority"),
        };
        return new() { Id = id, Ordinal = ordinal, Applicability = applicability, Status = status, ErrorCode = error,
            ObservedStatus = observedStatus, ObservedErrorCode = observedError, AtomicOutcome = atomicOutcome,
            ReceiptResolution = receiptResolution, RequestDisposition = requestDisposition,
            ReceiptChecksum = canonicalReceipt, ObservationSequence = sequence,
            EvidenceChecksum = BaseSemanticActivationCertificationContract.CaseEvidenceChecksum(id, ordinal, applicability,
                status, error, observedStatus, observedError, atomicOutcome, receiptResolution, requestDisposition,
                canonicalReceipt, canonicalObservation) };
    }

}
