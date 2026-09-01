using HPD.Payments.Contracts.PublicationObligation;
using HPD.Payments.Contracts.WorkRequirement;
using HPD.Payments.Primitives.Identity;
using HPD.Payments.Runtime.Publication;
using HPD.Payments.Runtime.Repair;

namespace HPD.Payments.Runtime.DurableWork;

/// <summary>Immutable owner projection joining work, publication, and governed-repair dispositions without merging their authorities.</summary>
public sealed record DurableExecutionSnapshot
{
    /// <summary>Gets the durable work identity.</summary>
    public SemanticId WorkId { get; }
    /// <summary>Gets the work disposition.</summary>
    public WorkDisposition WorkDisposition { get; }
    /// <summary>Gets the retained work attempt count.</summary>
    public int WorkAttempts { get; }
    /// <summary>Gets the latest claim epoch.</summary>
    public OwnerGeneration ClaimEpoch { get; }
    /// <summary>Gets whether work reconciliation is mandatory.</summary>
    public bool WorkRequiresReconciliation { get; }
    /// <summary>Gets the audience-specific publication disposition.</summary>
    public PublicationDisposition PublicationDisposition { get; }
    /// <summary>Gets the publication attempt count.</summary>
    public uint PublicationAttempts { get; }
    /// <summary>Gets whether publication reconciliation is mandatory.</summary>
    public bool PublicationRequiresReconciliation { get; }
    /// <summary>Gets the governed repair state.</summary>
    public GovernedRepairState RepairState { get; }
    /// <summary>Gets the retained terminal repair branch count.</summary>
    public int RepairReceiptCount { get; }

    /// <summary>Creates a validated projection from independently authoritative protocol states.</summary>
    public static DurableExecutionSnapshot Capture(WorkProtocolState work, PublicationProtocolState publication,
        GovernedRepairProtocol repair)
    {
        ArgumentNullException.ThrowIfNull(work); ArgumentNullException.ThrowIfNull(publication); ArgumentNullException.ThrowIfNull(repair);
        return new(work.Requirement.WorkId, work.Disposition, work.AttemptCount, work.ClaimEpoch,
            work.RequiresReconciliation, publication.Disposition, publication.Attempt, publication.AwaitingReconciliation,
            repair.State, repair.Receipts.Count);
    }

    /// <summary>Rehydrates an exact previously stored projection.</summary>
    public static DurableExecutionSnapshot Restore(SemanticId workId, WorkDisposition workDisposition, int workAttempts,
        OwnerGeneration claimEpoch, bool workRequiresReconciliation, PublicationDisposition publicationDisposition,
        uint publicationAttempts, bool publicationRequiresReconciliation, GovernedRepairState repairState, int repairReceiptCount) =>
        new(workId, workDisposition, workAttempts, claimEpoch, workRequiresReconciliation, publicationDisposition,
            publicationAttempts, publicationRequiresReconciliation, repairState, repairReceiptCount);

    private DurableExecutionSnapshot(SemanticId workId, WorkDisposition workDisposition, int workAttempts, OwnerGeneration claimEpoch,
        bool workRequiresReconciliation, PublicationDisposition publicationDisposition, uint publicationAttempts,
        bool publicationRequiresReconciliation, GovernedRepairState repairState, int repairReceiptCount)
    {
        if (!workId.IsValid || workDisposition is WorkDisposition.None || !Enum.IsDefined(workDisposition) || workAttempts < 0 ||
            publicationDisposition is PublicationDisposition.None || !Enum.IsDefined(publicationDisposition) ||
            repairState is GovernedRepairState.None || !Enum.IsDefined(repairState) || repairReceiptCount < 0)
            throw new ArgumentException("Durable execution projection is invalid.");
        WorkId = workId; WorkDisposition = workDisposition; WorkAttempts = workAttempts; ClaimEpoch = claimEpoch;
        WorkRequiresReconciliation = workRequiresReconciliation; PublicationDisposition = publicationDisposition;
        PublicationAttempts = publicationAttempts; PublicationRequiresReconciliation = publicationRequiresReconciliation;
        RepairState = repairState; RepairReceiptCount = repairReceiptCount;
    }
}
