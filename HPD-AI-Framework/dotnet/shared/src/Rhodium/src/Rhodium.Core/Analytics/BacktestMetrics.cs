using System.Text;
using Rhodium.Primitives;

namespace Rhodium.Analytics;

/// <summary>
/// Validation metrics for assessing backtest realism.
/// Compare these metrics between backtest and live trading to identify discrepancies.
/// </summary>
public sealed record BacktestMetrics
{
    // ==================== QUEUE REALISM ====================

    /// <summary>
    /// Average relative queue position at time of fill (0.0 = front, 1.0 = tail).
    /// Expected range: 0.0–0.5 for realistic fills.
    /// Warning if always near 0.0 (too optimistic).
    /// </summary>
    public required decimal AvgQueuePositionAtFill { get; init; }

    /// <summary>
    /// Standard deviation of queue position at fill.
    /// Should show variance (not always same position).
    /// </summary>
    public required decimal StdDevQueuePosition { get; init; }

    /// <summary>
    /// Average time spent in queue before fill (nanoseconds).
    /// Too short = unrealistic (always front of queue).
    /// </summary>
    public required Duration AvgTimeInQueue { get; init; }

    // ==================== FILL COMPOSITION ====================

    /// <summary>
    /// Percentage of fills that were maker (passive).
    /// Expected range: 60–90% for market making strategies.
    /// </summary>
    public required decimal MakerFillRatePercent { get; init; }

    /// <summary>
    /// Percentage of fills that were taker (aggressive).
    /// Expected range: 10–40% for market making strategies.
    /// </summary>
    public required decimal TakerFillRatePercent { get; init; }

    /// <summary>
    /// Percentage of orders that resulted in partial fills.
    /// Only meaningful if FillBehavior = PartialFillOnTrade.
    /// </summary>
    public required decimal PartialFillRatePercent { get; init; }

    // ==================== LATENCY STATISTICS ====================

    /// <summary>Median entry latency (local → exchange).</summary>
    public required Duration P50EntryLatency { get; init; }

    /// <summary>95th percentile entry latency.</summary>
    public required Duration P95EntryLatency { get; init; }

    /// <summary>99th percentile entry latency (outliers).</summary>
    public required Duration P99EntryLatency { get; init; }

    /// <summary>Median response latency (exchange → local).</summary>
    public required Duration P50ResponseLatency { get; init; }

    /// <summary>95th percentile response latency.</summary>
    public required Duration P95ResponseLatency { get; init; }

    // ==================== FILL ANALYSIS ====================

    /// <summary>Total number of fills (partial + full).</summary>
    public required int TotalFills { get; init; }

    /// <summary>Number of partial fills.</summary>
    public required int PartialFills { get; init; }

    /// <summary>Number of full fills (order completely filled).</summary>
    public required int FullFills { get; init; }

    /// <summary>
    /// Average fill slippage as percentage of order price.
    /// Slippage = |fill_price - order_price| / order_price × 100%
    /// Expected: near 0% for limit orders at best bid/ask.
    /// </summary>
    public required decimal AvgFillSlippagePercent { get; init; }

    /// <summary>
    /// Average fill price deviation in ticks from order price.
    /// Positive = worse than expected, Negative = better than expected.
    /// </summary>
    public required double AvgFillPriceDeviationTicks { get; init; }

    // ==================== ORDER REALISM ====================

    /// <summary>
    /// Average order size as percentage of available depth at order price.
    /// Warning if > 10% (order may be too large for realistic fill).
    /// </summary>
    public required decimal AvgOrderSizeVsDepthPercent { get; init; }

    /// <summary>
    /// Maximum order size as percentage of depth (largest order).
    /// Warning if > 50% (definitely too large).
    /// </summary>
    public required decimal MaxOrderSizeVsDepthPercent { get; init; }

    // ==================== WARNINGS & ISSUES ====================

    /// <summary>
    /// List of warnings generated during backtest.
    /// Examples:
    /// - "Order at 10:32:15.123 was 25% of depth (too large)"
    /// - "Queue position always less than 0.05 (unrealistically optimistic)"
    /// - "95% of fills were maker (too passive, verify queue model)"
    /// </summary>
    public required List<string> Warnings { get; init; }

    /// <summary>
    /// Generate summary report for validation.
    /// </summary>
    public string ToSummaryReport()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== Backtest Validation Metrics ===");
        sb.AppendLine();

        sb.AppendLine("Queue Realism:");
        sb.AppendLine($"  Avg Queue Position at Fill: {AvgQueuePositionAtFill:F3} (0.0=front, 1.0=tail)");
        sb.AppendLine($"  Std Dev Queue Position:     {StdDevQueuePosition:F3}");
        sb.AppendLine($"  Avg Time in Queue:          {AvgTimeInQueue}");
        sb.AppendLine();

        sb.AppendLine("Fill Composition:");
        sb.AppendLine($"  Maker Fill Rate:            {MakerFillRatePercent:F1}%");
        sb.AppendLine($"  Taker Fill Rate:            {TakerFillRatePercent:F1}%");
        sb.AppendLine($"  Partial Fill Rate:          {PartialFillRatePercent:F1}%");
        sb.AppendLine();

        sb.AppendLine("Latency (P50/P95/P99):");
        sb.AppendLine($"  Entry Latency:              {P50EntryLatency} / {P95EntryLatency} / {P99EntryLatency}");
        sb.AppendLine($"  Response Latency:           {P50ResponseLatency} / {P95ResponseLatency} / -");
        sb.AppendLine();

        sb.AppendLine("Fill Analysis:");
        sb.AppendLine($"  Total Fills:                {TotalFills}");
        sb.AppendLine($"  Partial Fills:              {PartialFills}");
        sb.AppendLine($"  Full Fills:                 {FullFills}");
        sb.AppendLine($"  Avg Slippage:               {AvgFillSlippagePercent:F3}%");
        sb.AppendLine($"  Avg Price Deviation:        {AvgFillPriceDeviationTicks:F2} ticks");
        sb.AppendLine();

        sb.AppendLine("Order Size Realism:");
        sb.AppendLine($"  Avg Order vs Depth:         {AvgOrderSizeVsDepthPercent:F1}%");
        sb.AppendLine($"  Max Order vs Depth:         {MaxOrderSizeVsDepthPercent:F1}%");

        if (Warnings.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"Warnings ({Warnings.Count}):");
            foreach (var warning in Warnings.Take(10))
            {
                sb.AppendLine($"  - {warning}");
            }
            if (Warnings.Count > 10)
                sb.AppendLine($"  ... and {Warnings.Count - 10} more");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Check for common issues and return validation status.
    /// </summary>
    public ValidationStatus GetValidationStatus()
    {
        var issues = new List<string>();

        // Check queue realism
        if (AvgQueuePositionAtFill < 0.05m)
            issues.Add("Queue position unrealistically low (always near front)");

        if (StdDevQueuePosition < 0.1m)
            issues.Add("Queue position has low variance (not realistic)");

        // Check fill composition
        if (MakerFillRatePercent > 95m)
            issues.Add("Maker fill rate too high (verify queue model)");

        if (TakerFillRatePercent > 50m)
            issues.Add("Taker fill rate very high (strategy may be too aggressive)");

        // Check order size
        if (AvgOrderSizeVsDepthPercent > 10m)
            issues.Add("Average order size > 10% of depth (may affect realism)");

        if (MaxOrderSizeVsDepthPercent > 50m)
            issues.Add("Maximum order size > 50% of depth (definitely unrealistic)");

        return issues.Count == 0
            ? new ValidationStatus.Pass()
            : new ValidationStatus.Warning(issues);
    }
}

/// <summary>
/// Validation status result.
/// </summary>
public abstract record ValidationStatus
{
    public sealed record Pass : ValidationStatus;
    public sealed record Warning(List<string> Issues) : ValidationStatus;
}
