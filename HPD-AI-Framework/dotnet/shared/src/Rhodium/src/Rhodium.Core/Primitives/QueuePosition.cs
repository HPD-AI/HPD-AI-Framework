namespace Rhodium.Primitives;

/// <summary>
/// Queue position tracking for realistic fill simulation (HFT).
/// </summary>
/// <param name="QtyAhead">Quantity ahead of this order in the queue.</param>
/// <param name="RelativePosition">Relative position in queue, where 0.0 is front and 1.0 is back.</param>
public readonly record struct QueuePosition(
    decimal QtyAhead,
    decimal RelativePosition
);
