using Rhodium.Primitives;

namespace Rhodium.Analytics;

/// <summary>
/// Performance metrics calculated from completed trades.
/// </summary>
public readonly record struct TearSheet(
    // Returns
    decimal TotalReturn,
    decimal Cagr,
    decimal AnnualizedReturn,

    // Risk-adjusted
    decimal SharpeRatio,
    decimal SortinoRatio,
    decimal CalmarRatio,

    // Drawdown
    decimal MaxDrawdown,
    Duration MaxDrawdownDuration,

    // Win/Loss
    decimal WinRate,
    decimal ProfitFactor,
    decimal PayoffRatio,
    decimal ExpectancyPerTrade,

    // Counts
    int TotalTrades,
    int WinningTrades,
    int LosingTrades,
    int BreakevenTrades,

    // P&L
    Money TotalPnL,
    Money GrossPnL,
    Money TotalCommissions,
    Money AvgWin,
    Money AvgLoss,
    Money LargestWin,
    Money LargestLoss,

    // Time
    Duration AvgHoldingPeriod,
    Duration AvgWinHoldingPeriod,
    Duration AvgLossHoldingPeriod,
    DateRange Period
)
{
    public static TearSheet Calculate(
        IReadOnlyList<RoundTrip> trades,
        Money initialCapital,
        decimal annualRiskFreeRate = 0m)
    {
        if (trades.Count == 0)
            return Empty(initialCapital);

        var currency = initialCapital.Currency;

        // Categorize trades
        var wins = trades.Where(t => t.IsWin).ToArray();
        var losses = trades.Where(t => t.IsLoss).ToArray();
        var breakevens = trades.Where(t => t.IsBreakeven).ToArray();

        // P&L calculations
        var grossPnL = trades.Sum(t => t.GrossPnL.Amount);
        var totalCommissions = trades.Sum(t => t.Commission.Amount);
        var netPnL = grossPnL - totalCommissions;
        var totalReturn = netPnL / initialCapital.Amount;

        // Build equity curve for drawdown calculation
        var (maxDrawdown, maxDrawdownDuration) = CalculateDrawdown(trades, initialCapital);

        // Calculate returns series for Sharpe/Sortino
        var returns = trades.Select(t => t.ReturnPct).ToArray();
        var avgReturn = returns.Length > 0 ? returns.Average() : 0m;
        var stdDev = StdDev(returns);
        var downsideDev = DownsideDeviation(returns);

        // Annualization
        var tradingDays = 252m;
        var dailyRiskFree = annualRiskFreeRate / tradingDays;

        // Period calculation
        var period = trades.Count > 0
            ? new DateRange(trades.Min(t => t.EntryTime), trades.Max(t => t.ExitTime))
            : new DateRange(Instant.Now, Instant.Now);

        var totalDays = period.Duration.TotalDays;
        // Guard against overflow when TotalDays is very large
        var years = totalDays > double.MaxValue / 1000 ? 0m : (decimal)totalDays / 365.25m;
        var cagr = years > 0.01m && totalReturn > -1  // Require at least ~4 days
            ? (decimal)Math.Pow((double)(1 + totalReturn), (double)(1 / years)) - 1
            : 0m;

        // Win/loss metrics
        var winRate = trades.Count > 0 ? (decimal)wins.Length / trades.Count : 0m;
        var totalWins = wins.Sum(t => t.NetPnL.Amount);
        var totalLosses = Math.Abs(losses.Sum(t => t.NetPnL.Amount));
        var profitFactor = totalLosses > 0 ? totalWins / totalLosses : totalWins > 0 ? decimal.MaxValue : 0m;

        var avgWin = wins.Length > 0 ? wins.Average(t => t.NetPnL.Amount) : 0m;
        var avgLoss = losses.Length > 0 ? losses.Average(t => t.NetPnL.Amount) : 0m;
        var payoffRatio = avgLoss != 0 ? Math.Abs(avgWin / avgLoss) : avgWin > 0 ? decimal.MaxValue : 0m;

        // Expectancy
        var expectancy = trades.Count > 0 ? netPnL / trades.Count : 0m;

        return new TearSheet(
            TotalReturn: totalReturn,
            Cagr: cagr,
            AnnualizedReturn: avgReturn * tradingDays,

            SharpeRatio: stdDev > 0
                ? (avgReturn - dailyRiskFree) / stdDev * (decimal)Math.Sqrt((double)tradingDays)
                : 0m,
            SortinoRatio: downsideDev > 0
                ? (avgReturn - dailyRiskFree) / downsideDev * (decimal)Math.Sqrt((double)tradingDays)
                : 0m,
            CalmarRatio: maxDrawdown > 0 ? cagr / maxDrawdown : 0m,

            MaxDrawdown: maxDrawdown,
            MaxDrawdownDuration: maxDrawdownDuration,

            WinRate: winRate,
            ProfitFactor: profitFactor,
            PayoffRatio: payoffRatio,
            ExpectancyPerTrade: expectancy,

            TotalTrades: trades.Count,
            WinningTrades: wins.Length,
            LosingTrades: losses.Length,
            BreakevenTrades: breakevens.Length,

            TotalPnL: new Money(netPnL, currency),
            GrossPnL: new Money(grossPnL, currency),
            TotalCommissions: new Money(totalCommissions, currency),
            AvgWin: new Money(avgWin, currency),
            AvgLoss: new Money(avgLoss, currency),
            LargestWin: wins.Length > 0
                ? new Money(wins.Max(t => t.NetPnL.Amount), currency)
                : Money.Zero(currency),
            LargestLoss: losses.Length > 0
                ? new Money(losses.Min(t => t.NetPnL.Amount), currency)
                : Money.Zero(currency),

            AvgHoldingPeriod: trades.Count > 0
                ? Duration.FromNanos((long)trades.Average(t => (double)t.HoldingPeriod.Nanos))
                : Duration.Zero,
            AvgWinHoldingPeriod: wins.Length > 0
                ? Duration.FromNanos((long)wins.Average(t => (double)t.HoldingPeriod.Nanos))
                : Duration.Zero,
            AvgLossHoldingPeriod: losses.Length > 0
                ? Duration.FromNanos((long)losses.Average(t => (double)t.HoldingPeriod.Nanos))
                : Duration.Zero,
            Period: period
        );
    }

    private static (decimal MaxDrawdown, Duration MaxDrawdownDuration) CalculateDrawdown(
        IReadOnlyList<RoundTrip> trades,
        Money initial)
    {
        if (trades.Count == 0)
            return (0m, Duration.Zero);

        var equity = initial.Amount;
        var peak = equity;
        var maxDd = 0m;
        var peakTime = trades[0].EntryTime;
        var maxDdDuration = Duration.Zero;
        var currentDdStart = trades[0].EntryTime;

        foreach (var trade in trades.OrderBy(t => t.ExitTime))
        {
            equity += trade.NetPnL.Amount;

            if (equity > peak)
            {
                peak = equity;
                peakTime = trade.ExitTime;
                currentDdStart = trade.ExitTime;
            }
            else
            {
                var dd = (peak - equity) / peak;
                if (dd > maxDd)
                {
                    maxDd = dd;
                    maxDdDuration = trade.ExitTime - currentDdStart;
                }
            }
        }

        return (maxDd, maxDdDuration);
    }

    private static decimal StdDev(decimal[] values)
    {
        if (values.Length < 2) return 0m;
        var avg = values.Average();
        var sumSq = values.Sum(v => (v - avg) * (v - avg));
        return (decimal)Math.Sqrt((double)(sumSq / (values.Length - 1)));
    }

    private static decimal DownsideDeviation(decimal[] values)
    {
        var negatives = values.Where(v => v < 0).ToArray();
        if (negatives.Length < 2) return 0m;
        var sumSq = negatives.Sum(v => v * v);
        return (decimal)Math.Sqrt((double)(sumSq / negatives.Length));
    }

    private static TearSheet Empty(Money initialCapital) => new(
        TotalReturn: 0m, Cagr: 0m, AnnualizedReturn: 0m,
        SharpeRatio: 0m, SortinoRatio: 0m, CalmarRatio: 0m,
        MaxDrawdown: 0m, MaxDrawdownDuration: Duration.Zero,
        WinRate: 0m, ProfitFactor: 0m, PayoffRatio: 0m, ExpectancyPerTrade: 0m,
        TotalTrades: 0, WinningTrades: 0, LosingTrades: 0, BreakevenTrades: 0,
        TotalPnL: Money.Zero(initialCapital.Currency),
        GrossPnL: Money.Zero(initialCapital.Currency),
        TotalCommissions: Money.Zero(initialCapital.Currency),
        AvgWin: Money.Zero(initialCapital.Currency),
        AvgLoss: Money.Zero(initialCapital.Currency),
        LargestWin: Money.Zero(initialCapital.Currency),
        LargestLoss: Money.Zero(initialCapital.Currency),
        AvgHoldingPeriod: Duration.Zero,
        AvgWinHoldingPeriod: Duration.Zero,
        AvgLossHoldingPeriod: Duration.Zero,
        Period: new DateRange(Instant.Now, Instant.Now)
    );

    public override string ToString() => $"""
        === Performance Summary ===
        Period: {Period.Start:yyyy-MM-dd} to {Period.End:yyyy-MM-dd}

        Returns:
          Total Return: {TotalReturn:P2}
          CAGR: {Cagr:P2}

        Risk-Adjusted:
          Sharpe Ratio: {SharpeRatio:F2}
          Sortino Ratio: {SortinoRatio:F2}
          Calmar Ratio: {CalmarRatio:F2}
          Max Drawdown: {MaxDrawdown:P2}

        Win/Loss:
          Win Rate: {WinRate:P2}
          Profit Factor: {ProfitFactor:F2}
          Avg Win: {AvgWin}
          Avg Loss: {AvgLoss}

        Trades:
          Total: {TotalTrades}
          Winners: {WinningTrades}
          Losers: {LosingTrades}

        P&L:
          Net P&L: {TotalPnL}
          Gross P&L: {GrossPnL}
          Commissions: {TotalCommissions}
        """;
}
