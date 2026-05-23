namespace Rhodium.Control;

public readonly struct StateTransitionResult
{
    public static StateTransitionResult None => default;

    public bool RequiresAdjustment { get; init; }
    public PositionTransition PositionTransition { get; init; }
}
