namespace Rhodium.Primitives;

/// <summary>
/// How trailing stop offset is specified.
/// </summary>
public enum TrailingOffsetType : byte
{
    /// <summary>Absolute price offset (e.g., $2.00 from peak).</summary>
    Price = 1,

    /// <summary>Number of ticks (e.g., 10 ticks from peak).</summary>
    Ticks = 2,

    /// <summary>Percentage of price (e.g., 2% from peak).</summary>
    Percent = 3
}
