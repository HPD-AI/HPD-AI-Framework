namespace HPD.ML.Abstractions;

/// <summary>Ordering guarantee on a DataHandle.</summary>
public enum OrderingPolicy
{
    Unordered = 0,
    Ordered,

    /// <summary>Original insertion order. Required by scan transforms.</summary>
    StrictlyOrdered
}
