namespace Rhodium.Data;

/// <summary>
/// Transforms high-frequency market data into lower-frequency aggregates.
/// Timeframes become composition, not configuration.
/// </summary>
/// <typeparam name="TIn">Input data type (e.g., Trade)</typeparam>
/// <typeparam name="TOut">Output data type (e.g., Bar)</typeparam>
public interface IAggregator<TIn, TOut> where TOut : struct
{
    /// <summary>
    /// Process an input item. Returns true when an aggregate is complete.
    /// When true, aggregate contains the completed bar. When false, aggregate is default.
    /// </summary>
    bool TryAggregate(TIn input, out TOut aggregate);

    /// <summary>
    /// Force emit any partial aggregate (e.g., at end of session).
    /// Returns null if no partial aggregate exists.
    /// </summary>
    TOut? Flush();

    /// <summary>
    /// Reset aggregator state.
    /// </summary>
    void Reset();
}
