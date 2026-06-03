namespace HPD.Agent.Audio.Turns;

public interface ITurnController
{
    TurnSnapshot Snapshot { get; }

    ValueTask<TurnDecision> ObserveAsync(
        TurnEvidence evidence,
        CancellationToken cancellationToken = default);
}
