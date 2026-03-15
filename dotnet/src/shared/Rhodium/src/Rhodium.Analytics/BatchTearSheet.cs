namespace Rhodium.Analytics;

/// <summary>
/// Batch-native struct-of-arrays format for representing tear sheets
/// for tens of thousands of variants efficiently.
/// Aligns to virtual index ordering and uses IBatchMap metadata for aggregation.
/// This is an output format (facade-level), not part of the core primitive count.
/// </summary>
public readonly record struct BatchTearSheet(
    ReadOnlyMemory<double> TotalReturn,
    ReadOnlyMemory<double> Cagr,
    ReadOnlyMemory<double> Sharpe,
    ReadOnlyMemory<double> MaxDrawdown
);
