using HPD.Agent.Audio.Graph;

namespace HPD.Agent.Audio.VoiceActivity;

internal interface IVoiceActivityDerivedResidenceCommit
{
    GraphMediaBinding DestinationMedia { get; }
    bool IsCommitted { get; }
    bool TryCommit();
}

internal abstract record VoiceActivityDerivedResidencePreparationResult
{
    private VoiceActivityDerivedResidencePreparationResult() { }

    internal sealed record Prepared(VoiceActivityDerivedResidenceCommit Commit) :
        VoiceActivityDerivedResidencePreparationResult;

    internal sealed record Rejected(GraphMediaDerivedCopyResult Result) :
        VoiceActivityDerivedResidencePreparationResult;
}

internal sealed class VoiceActivityDerivedResidenceCommit : IVoiceActivityDerivedResidenceCommit
{
    private GraphMediaResidenceLedger _residences;
    private GraphMediaOwnershipLedger _ownership;
    private GraphMediaResidenceLedger.DerivedCopyPlan? _plan;
    private bool _committed;

    private VoiceActivityDerivedResidenceCommit(
        GraphMediaResidenceLedger residences,
        GraphMediaOwnershipLedger ownership,
        GraphMediaResidenceLedger.DerivedCopyPlan? plan,
        GraphMediaBinding destinationMedia,
        bool committed)
    {
        _residences = residences;
        _ownership = ownership;
        _plan = plan;
        DestinationMedia = destinationMedia;
        _committed = committed;
    }

    internal static VoiceActivityDerivedResidencePreparationResult Prepare(
        GraphMediaDerivedResidenceRequest request,
        GraphMediaResidenceLedger residences,
        GraphMediaOwnershipLedger ownership)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(residences);
        ArgumentNullException.ThrowIfNull(ownership);
        var planned = residences.PlanDerivedCopy(request, ownership);
        return planned.Result switch
        {
            GraphMediaDerivedCopyResult.Planned when planned.Plan is not null =>
                new VoiceActivityDerivedResidencePreparationResult.Prepared(
                    new(residences, ownership, planned.Plan, request.DestinationMedia, false)),
            GraphMediaDerivedCopyResult.IdempotentCommitted =>
                new VoiceActivityDerivedResidencePreparationResult.Prepared(
                    new(planned.ResidenceLedger, planned.OwnershipLedger, null, request.DestinationMedia, true)),
            _ => new VoiceActivityDerivedResidencePreparationResult.Rejected(planned.Result),
        };
    }

    public GraphMediaBinding DestinationMedia { get; }
    public bool IsCommitted => _committed;
    internal GraphMediaResidenceLedger Residences => _residences;
    internal GraphMediaOwnershipLedger Ownership => _ownership;

    public bool TryCommit()
    {
        if (_committed) return true;
        if (_plan is null) return false;
        var committed = _residences.CommitDerivedCopy(_plan, _ownership);
        if (committed.Result != GraphMediaDerivedCopyResult.Committed) return false;
        _residences = committed.ResidenceLedger;
        _ownership = committed.OwnershipLedger;
        _plan = null;
        _committed = true;
        return true;
    }
}
