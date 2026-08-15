using HPD.Agent.Audio.Graph;

namespace HPD.Agent.Audio.VoiceActivity;

internal interface IVoiceActivityDerivedResidenceCommitV1
{
    GraphMediaBindingV1 DestinationMedia { get; }
    bool IsCommitted { get; }
    bool TryCommit();
}

internal abstract record VoiceActivityDerivedResidencePreparationResultV1
{
    private VoiceActivityDerivedResidencePreparationResultV1() { }

    internal sealed record Prepared(VoiceActivityDerivedResidenceCommitV1 Commit) :
        VoiceActivityDerivedResidencePreparationResultV1;

    internal sealed record Rejected(GraphMediaDerivedCopyResultV1 Result) :
        VoiceActivityDerivedResidencePreparationResultV1;
}

internal sealed class VoiceActivityDerivedResidenceCommitV1 : IVoiceActivityDerivedResidenceCommitV1
{
    private GraphMediaResidenceLedgerV1 _residences;
    private GraphMediaOwnershipLedgerV1 _ownership;
    private GraphMediaResidenceLedgerV1.DerivedCopyPlan? _plan;
    private bool _committed;

    private VoiceActivityDerivedResidenceCommitV1(
        GraphMediaResidenceLedgerV1 residences,
        GraphMediaOwnershipLedgerV1 ownership,
        GraphMediaResidenceLedgerV1.DerivedCopyPlan? plan,
        GraphMediaBindingV1 destinationMedia,
        bool committed)
    {
        _residences = residences;
        _ownership = ownership;
        _plan = plan;
        DestinationMedia = destinationMedia;
        _committed = committed;
    }

    internal static VoiceActivityDerivedResidencePreparationResultV1 Prepare(
        GraphMediaDerivedResidenceRequestV1 request,
        GraphMediaResidenceLedgerV1 residences,
        GraphMediaOwnershipLedgerV1 ownership)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(residences);
        ArgumentNullException.ThrowIfNull(ownership);
        var planned = residences.PlanDerivedCopy(request, ownership);
        return planned.Result switch
        {
            GraphMediaDerivedCopyResultV1.Planned when planned.Plan is not null =>
                new VoiceActivityDerivedResidencePreparationResultV1.Prepared(
                    new(residences, ownership, planned.Plan, request.DestinationMedia, false)),
            GraphMediaDerivedCopyResultV1.IdempotentCommitted =>
                new VoiceActivityDerivedResidencePreparationResultV1.Prepared(
                    new(planned.ResidenceLedger, planned.OwnershipLedger, null, request.DestinationMedia, true)),
            _ => new VoiceActivityDerivedResidencePreparationResultV1.Rejected(planned.Result),
        };
    }

    public GraphMediaBindingV1 DestinationMedia { get; }
    public bool IsCommitted => _committed;
    internal GraphMediaResidenceLedgerV1 Residences => _residences;
    internal GraphMediaOwnershipLedgerV1 Ownership => _ownership;

    public bool TryCommit()
    {
        if (_committed) return true;
        if (_plan is null) return false;
        var committed = _residences.CommitDerivedCopy(_plan, _ownership);
        if (committed.Result != GraphMediaDerivedCopyResultV1.Committed) return false;
        _residences = committed.ResidenceLedger;
        _ownership = committed.OwnershipLedger;
        _plan = null;
        _committed = true;
        return true;
    }
}
