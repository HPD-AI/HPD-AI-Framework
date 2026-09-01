using System.Text.Json.Serialization;
using HPD.Payments.Contracts.PublicationObligation;
using HPD.Payments.Contracts.WorkRequirement;
using HPD.Payments.Primitives.Identity;
using HPD.Payments.Runtime.DurableWork;
using HPD.Payments.Runtime.Repair;

namespace HPD.Payments.Runtime.Base;

/// <summary>Provides the closed AOT-safe codec for durable execution projections.</summary>
public static class PaymentsDurableExecutionFactCodecs
{
    /// <summary>Gets the exact durable execution snapshot codec.</summary>
    public static PaymentsFactJsonCodec<DurableExecutionSnapshot> Snapshot { get; } = PaymentsFactJsonCodec.Create(
        "hpd.payments.durable-execution-snapshot.v1", PaymentsDurableExecutionJsonContext.Default.DurableExecutionSnapshotPayload,
        DurableExecutionSnapshotPayload.From, static payload => payload.ToValue());
}

internal sealed record DurableExecutionSnapshotPayload(PaymentsIdentityPayload WorkId, int WorkDisposition, int WorkAttempts,
    ulong ClaimEpoch, bool WorkRequiresReconciliation, int PublicationDisposition, uint PublicationAttempts,
    bool PublicationRequiresReconciliation, int RepairState, int RepairReceiptCount)
{
    internal static DurableExecutionSnapshotPayload From(DurableExecutionSnapshot value) => new(PaymentsIdentityPayload.From(value.WorkId),
        (int)value.WorkDisposition, value.WorkAttempts, value.ClaimEpoch.Value, value.WorkRequiresReconciliation,
        (int)value.PublicationDisposition, value.PublicationAttempts, value.PublicationRequiresReconciliation,
        (int)value.RepairState, value.RepairReceiptCount);
    internal DurableExecutionSnapshot ToValue() => DurableExecutionSnapshot.Restore(WorkId.ToValue(), (WorkDisposition)WorkDisposition,
        WorkAttempts, ClaimEpoch == 0 ? default : OwnerGeneration.Create(ClaimEpoch), WorkRequiresReconciliation,
        (PublicationDisposition)PublicationDisposition, PublicationAttempts, PublicationRequiresReconciliation,
        (GovernedRepairState)RepairState, RepairReceiptCount);
}

[JsonSerializable(typeof(DurableExecutionSnapshotPayload))]
internal sealed partial class PaymentsDurableExecutionJsonContext : JsonSerializerContext;
