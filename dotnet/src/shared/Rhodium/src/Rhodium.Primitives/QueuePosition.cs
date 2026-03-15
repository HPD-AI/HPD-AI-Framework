namespace Rhodium.Primitives;

/// <summary>
/// Queue position tracking for realistic fill simulation (HFT).
/// </summary>
public readonly record struct QueuePosition(
    /// <summary>Quantity ahead of this order in the queue.</summary>
    decimal QtyAhead,

    /// <summary>Relative position in queue (0.0 = front, 1.0 = back).</summary>
    decimal RelativePosition
);
