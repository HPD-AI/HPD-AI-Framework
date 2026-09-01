using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;

namespace HPD.Base;

/// <summary>Loads frozen built-in reports that executable provider tests must reproduce byte-for-byte.</summary>
internal static class BaseSemanticActivationBuiltInCertification
{
    internal static BaseSemanticActivationCertificationReport LoadFrozenExecutedReport(
        BaseSemanticActivationCertificationSubject subject, BaseSemanticActivationCapability capability)
    {
        var cases = ImmutableArray.CreateBuilder<BaseSemanticActivationCertificationCaseResult>(
            BaseSemanticActivationCertificationContract.MandatoryCaseIds.Length);
        for (int ordinal = 0; ordinal < BaseSemanticActivationCertificationContract.MandatoryCaseIds.Length; ordinal++)
        {
            string id = BaseSemanticActivationCertificationContract.MandatoryCaseIds[ordinal];
            bool advertised = Advertised(id, capability);
            BaseSemanticActivationCertificationApplicability applicability = advertised
                ? BaseSemanticActivationCertificationApplicability.Executed : BaseSemanticActivationCertificationApplicability.NotAdvertised;
            (OperationStatus observedStatus, string? observedError) =
                BaseSemanticActivationCertificationContract.ExpectedObservedOutcome(id, applicability);
            (RecordMutationExecutionOutcome? atomicOutcome, BaseAtomicReceiptResolutionDisposition resolution,
                BaseMutationRequestDisposition? disposition, ImmutableArray<byte> receiptChecksum) = ReceiptOutcome(
                    subject, id, advertised, capability.RestoreRecoveryFloorsSupported);
            BaseSemanticActivationCertificationObservation observation = Observation(id, ordinal, advertised);
            ImmutableArray<byte> evidenceChecksum = BaseSemanticActivationCertificationContract.CaseEvidenceChecksum(
                id, ordinal, applicability, OperationStatus.Ok, null, observedStatus, observedError,
                atomicOutcome, resolution, disposition, receiptChecksum, observation);
            cases.Add(new BaseSemanticActivationCertificationCaseResult
            {
                Id = id, Ordinal = ordinal,
                Applicability = applicability,
                Status = OperationStatus.Ok, ErrorCode = null,
                ObservedStatus = observedStatus, ObservedErrorCode = observedError,
                AtomicOutcome = atomicOutcome, ReceiptResolution = resolution, RequestDisposition = disposition,
                ReceiptChecksum = receiptChecksum,
                ObservationSequence = observation.Sequence,
                EvidenceChecksum = evidenceChecksum,
            });
        }
        return BaseSemanticActivationCertificationContract.CreateReport(subject, cases.MoveToImmutable());
    }


    private static bool Advertised(string id, BaseSemanticActivationCapability capability) => id is
        "inspection" or "maintenance-authority" or "maintenance"
            ? capability.MaintenanceSupported
            : id.StartsWith("maintenance-", StringComparison.Ordinal) && id != "maintenance-authority"
                ? capability.MaintenanceSupported
            : id is "fault-NonCooperativeMaintenance" or "fault-InterruptMaintenancePublication"
                ? capability.MaintenanceSupported && capability.RestoreRecoveryFloorsSupported
            : id is "backup-restore" or "recovery-floor" or "fault-NonCooperativeRestore" or "fault-CorruptRecoveryEntry"
                or "fault-InterruptRestorePublication" or "fault-RetentionOvertake"
                ? capability.RestoreRecoveryFloorsSupported && !capability.BackupModes.IsEmpty && !capability.RestoreModes.IsEmpty
                : true;

    private static (RecordMutationExecutionOutcome?, BaseAtomicReceiptResolutionDisposition,
        BaseMutationRequestDisposition?, ImmutableArray<byte>) ReceiptOutcome(
        BaseSemanticActivationCertificationSubject subject, string id, bool advertised, bool durable)
    {
        if (!advertised || id is "inspection" or "maintenance-authority" or "maintenance" or "backup-restore" or "recovery-floor"
            || id.StartsWith("maintenance-", StringComparison.Ordinal) && id != "maintenance-authority"
            || id is "fault-NonCooperativeMaintenance" or "fault-NonCooperativeRestore"
            or "fault-CorruptRecoveryEntry" or "fault-InterruptMaintenancePublication"
            or "fault-InterruptRestorePublication" or "fault-RetentionOvertake")
            return (null, BaseAtomicReceiptResolutionDisposition.NotApplicable, null, []);
        ImmutableArray<byte> receipt = id is "atomic-missing-ensure" or "different-parent-race" or "existing-replay"
            or "terminal-retirement" or "receipt-resolution" or "fault-ResponseLossAfterCommit"
            ? BaseSemanticActivationCertificationContract.CanonicalExecutedEvidence(id, "receipt") : [];
        if (id == "existing-replay") return (RecordMutationExecutionOutcome.Committed,
            BaseAtomicReceiptResolutionDisposition.NotApplicable, BaseMutationRequestDisposition.Duplicate, receipt);
        if (id == "receipt-resolution") return (RecordMutationExecutionOutcome.Committed,
            BaseAtomicReceiptResolutionDisposition.Found, BaseMutationRequestDisposition.Duplicate, receipt);
        if (id is "atomic-missing-ensure" or "different-parent-race" or "terminal-retirement")
            return (RecordMutationExecutionOutcome.Committed, BaseAtomicReceiptResolutionDisposition.NotApplicable,
                BaseMutationRequestDisposition.Committed, receipt);
        if (id is "fault-ResponseLossAfterCommit" or "fault-IndeterminateCommit")
            return (RecordMutationExecutionOutcome.Indeterminate, BaseAtomicReceiptResolutionDisposition.NotApplicable,
                BaseMutationRequestDisposition.Committed, receipt);
        if (id is "noncooperative-release" or "fault-NonCooperativeCapture"
            or "fault-NonCooperativePrepare" or "fault-NonCooperativeApply")
            return (durable ? RecordMutationExecutionOutcome.CancelledRollbackConfirmed : RecordMutationExecutionOutcome.RollbackConfirmed,
                BaseAtomicReceiptResolutionDisposition.NotApplicable, BaseMutationRequestDisposition.Committed, []);
        return (RecordMutationExecutionOutcome.RollbackConfirmed,
            id == "fault-NonCooperativeReceipt" ? BaseAtomicReceiptResolutionDisposition.Unavailable : BaseAtomicReceiptResolutionDisposition.NotApplicable,
            BaseMutationRequestDisposition.Committed, []);
    }

    private static BaseSemanticActivationCertificationObservation Observation(string id, int ordinal, bool advertised)
    {
        bool live = advertised && id is ("atomic-missing-ensure" or "different-parent-race" or "existing-replay"
            or "receipt-resolution" or "fault-ResponseLossAfterCommit" or "fault-IndeterminateCommit"
            or "fault-NonCooperativeReceipt" or "fault-SubstituteSlotGeneration"
            or "fault-SubstituteActivation" or "fault-SubstituteDueAuthority");
        bool retired = advertised && id is ("terminal-retirement" or "recovery-floor" or "fault-CorruptRetirement"
            or "fault-CorruptRecoveryEntry" or "fault-RetentionOvertake");
        bool absent = advertised && id == "fault-CorruptAbsence";
        bool compactedPair = advertised && id is "maintenance-compact-multipage"
            or "maintenance-progress-invisible" or "maintenance-resume";
        bool released = advertised && (id == "noncooperative-release" || id.Contains("NonCooperative", StringComparison.Ordinal));
        bool replay = advertised && id is ("existing-replay" or "receipt-resolution");
        bool before = replay || id is "fault-ResponseLossAfterCommit" or "fault-IndeterminateCommit" or "fault-NonCooperativeReceipt";
        ImmutableArray<byte> authority = before
            ? BaseSemanticActivationCertificationContract.CanonicalExecutedEvidence(id, "authority") : [];
        ImmutableArray<byte> after = live || retired || absent
            ? BaseSemanticActivationCertificationContract.CanonicalExecutedEvidence(id, "authority") : [];
        long receipts = !advertised ? 0 : id switch
        {
            "maintenance-compact-multipage" or "maintenance-progress-invisible"
                or "maintenance-resume" => 13,
            "maintenance-migrate" => 3,
            "maintenance" => 1,
            _ when id.StartsWith("maintenance-", StringComparison.Ordinal) && id != "maintenance-authority" => 1,
            "different-parent-race" or "terminal-retirement" or "recovery-floor" or "fault-CorruptRetirement"
                or "fault-CorruptAbsence" or "fault-CorruptRecoveryEntry" or "fault-RetentionOvertake" => 2,
            "atomic-missing-ensure" or "existing-replay" or "receipt-resolution" or "fault-ResponseLossAfterCommit"
                or "fault-IndeterminateCommit" or "fault-NonCooperativeReceipt" or "fault-SubstituteSlotGeneration"
                or "fault-SubstituteActivation" or "fault-SubstituteDueAuthority" => 1,
            _ => 0,
        };
        return new()
        {
            Sequence = advertised ? checked(ordinal + 1L) : 1,
            Evidence = BaseSemanticActivationCertificationContract.CanonicalExecutedEvidence(id, "observation"),
            LiveSlots = advertised && id == "maintenance-migrate" ? 2 : live ? 1 : 0,
            RetiredSlots = retired ? 1 : 0, AbsenceMarkers = compactedPair ? 2 : absent ? 1 : 0,
            Activations = advertised && id == "maintenance-migrate" ? 2 : live || retired || absent ? 1 : 0,
            Receipts = receipts,
            ActiveWork = 0, QuarantinedWork = 0, ReleasedWork = released ? 1 : 0,
            RejectedLateCompletions = released ? 1 : 0, ExactLimitAccepted = id == "accounting-limits",
            MaxPlusOneRejected = id == "accounting-limits", RecoveryFloorVerified = advertised && id == "recovery-floor",
            ReceiptResolved = id is "fault-ResponseLossAfterCommit" or "receipt-resolution", AuthorityBeforeChecksum = authority,
            AuthorityAfterChecksum = after,
        };
    }

}
