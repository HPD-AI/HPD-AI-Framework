using Rhodium.Primitives;

namespace Rhodium.Indicators;

/// <summary>
/// Candlestick pattern result.
/// </summary>
public enum PatternSignal : sbyte { Bearish = -1, None = 0, Bullish = 1 }

/// <summary>
/// Candlestick pattern recognition functions.
/// </summary>
public static class Patterns
{
    // ==================== SINGLE CANDLE ====================

    /// <summary>
    /// Doji - open and close are nearly equal.
    /// </summary>
    public static bool Doji(Bar bar, decimal threshold = 0.05m)
    {
        var bodySize = Math.Abs(bar.Close.Value - bar.Open.Value);
        var range = bar.High.Value - bar.Low.Value;
        return range > 0 && bodySize / range <= threshold;
    }

    /// <summary>
    /// Hammer - small body at top, long lower shadow.
    /// </summary>
    public static bool Hammer(Bar bar)
    {
        var bodySize = Math.Abs(bar.Close.Value - bar.Open.Value);
        var range = bar.High.Value - bar.Low.Value;
        var upperShadow = bar.High.Value - Math.Max(bar.Open.Value, bar.Close.Value);
        var lowerShadow = Math.Min(bar.Open.Value, bar.Close.Value) - bar.Low.Value;
        return range > 0 && lowerShadow >= bodySize * 2 && upperShadow <= bodySize * 0.5m;
    }

    /// <summary>
    /// Hanging Man - same shape as hammer but in uptrend.
    /// </summary>
    public static bool HangingMan(ReadOnlySpan<Bar> bars)
    {
        if (bars.Length < 2) return false;
        return Hammer(bars[^1]) && bars[^2].Close > bars[^2].Open;
    }

    /// <summary>
    /// Inverted Hammer - small body at bottom, long upper shadow.
    /// </summary>
    public static bool InvertedHammer(Bar bar)
    {
        var bodySize = Math.Abs(bar.Close.Value - bar.Open.Value);
        var range = bar.High.Value - bar.Low.Value;
        var upperShadow = bar.High.Value - Math.Max(bar.Open.Value, bar.Close.Value);
        var lowerShadow = Math.Min(bar.Open.Value, bar.Close.Value) - bar.Low.Value;
        return range > 0 && upperShadow >= bodySize * 2 && lowerShadow <= bodySize * 0.5m;
    }

    /// <summary>
    /// Shooting Star - inverted hammer in uptrend.
    /// </summary>
    public static bool ShootingStar(ReadOnlySpan<Bar> bars)
    {
        if (bars.Length < 2) return false;
        return InvertedHammer(bars[^1]) && bars[^2].Close > bars[^2].Open;
    }

    /// <summary>
    /// Marubozu - no shadows (or very small).
    /// </summary>
    public static bool Marubozu(Bar bar, decimal threshold = 0.01m)
    {
        var range = bar.High.Value - bar.Low.Value;
        if (range == 0) return false;
        var upperShadow = bar.High.Value - Math.Max(bar.Open.Value, bar.Close.Value);
        var lowerShadow = Math.Min(bar.Open.Value, bar.Close.Value) - bar.Low.Value;
        return upperShadow / range <= threshold && lowerShadow / range <= threshold;
    }

    /// <summary>
    /// Spinning Top - small body, shadows on both sides.
    /// </summary>
    public static bool SpinningTop(Bar bar)
    {
        var bodySize = Math.Abs(bar.Close.Value - bar.Open.Value);
        var range = bar.High.Value - bar.Low.Value;
        var upperShadow = bar.High.Value - Math.Max(bar.Open.Value, bar.Close.Value);
        var lowerShadow = Math.Min(bar.Open.Value, bar.Close.Value) - bar.Low.Value;
        return range > 0 && bodySize <= range * 0.3m && upperShadow > bodySize && lowerShadow > bodySize;
    }

    // ==================== TWO CANDLE ====================

    /// <summary>
    /// Bullish Engulfing - bearish candle followed by larger bullish candle.
    /// </summary>
    public static bool BullishEngulfing(ReadOnlySpan<Bar> bars)
    {
        if (bars.Length < 2) return false;
        var prev = bars[^2];
        var curr = bars[^1];
        return prev.Close < prev.Open &&  // Previous is bearish
               curr.Close > curr.Open &&  // Current is bullish
               curr.Open < prev.Close &&  // Current opens below previous close
               curr.Close > prev.Open;    // Current closes above previous open
    }

    /// <summary>
    /// Bearish Engulfing - bullish candle followed by larger bearish candle.
    /// </summary>
    public static bool BearishEngulfing(ReadOnlySpan<Bar> bars)
    {
        if (bars.Length < 2) return false;
        var prev = bars[^2];
        var curr = bars[^1];
        return prev.Close > prev.Open &&  // Previous is bullish
               curr.Close < curr.Open &&  // Current is bearish
               curr.Open > prev.Close &&  // Current opens above previous close
               curr.Close < prev.Open;    // Current closes below previous open
    }

    /// <summary>
    /// Harami - large candle followed by smaller candle within its body.
    /// </summary>
    public static PatternSignal Harami(ReadOnlySpan<Bar> bars)
    {
        if (bars.Length < 2) return PatternSignal.None;
        var prev = bars[^2];
        var curr = bars[^1];
        var prevBody = Math.Abs(prev.Close.Value - prev.Open.Value);
        var currBody = Math.Abs(curr.Close.Value - curr.Open.Value);

        if (currBody >= prevBody) return PatternSignal.None;

        var prevHigh = Math.Max(prev.Open.Value, prev.Close.Value);
        var prevLow = Math.Min(prev.Open.Value, prev.Close.Value);
        var currHigh = Math.Max(curr.Open.Value, curr.Close.Value);
        var currLow = Math.Min(curr.Open.Value, curr.Close.Value);

        if (currHigh <= prevHigh && currLow >= prevLow)
            return prev.Close > prev.Open ? PatternSignal.Bearish : PatternSignal.Bullish;

        return PatternSignal.None;
    }

    /// <summary>
    /// Piercing Line - bearish candle followed by bullish candle closing above midpoint.
    /// </summary>
    public static bool PiercingLine(ReadOnlySpan<Bar> bars)
    {
        if (bars.Length < 2) return false;
        var prev = bars[^2];
        var curr = bars[^1];
        var midpoint = (prev.Open.Value + prev.Close.Value) / 2;
        return prev.Close < prev.Open &&           // Previous is bearish
               curr.Close > curr.Open &&           // Current is bullish
               curr.Open < prev.Close &&           // Opens below previous close
               curr.Close > midpoint &&            // Closes above midpoint
               curr.Close < prev.Open;             // But below previous open
    }

    /// <summary>
    /// Dark Cloud Cover - bullish candle followed by bearish candle closing below midpoint.
    /// </summary>
    public static bool DarkCloudCover(ReadOnlySpan<Bar> bars)
    {
        if (bars.Length < 2) return false;
        var prev = bars[^2];
        var curr = bars[^1];
        var midpoint = (prev.Open.Value + prev.Close.Value) / 2;
        return prev.Close > prev.Open &&           // Previous is bullish
               curr.Close < curr.Open &&           // Current is bearish
               curr.Open > prev.Close &&           // Opens above previous close
               curr.Close < midpoint &&            // Closes below midpoint
               curr.Close > prev.Open;             // But above previous open
    }

    /// <summary>
    /// Tweezer Tops - two candles with same high in uptrend.
    /// </summary>
    public static bool TweezerTops(ReadOnlySpan<Bar> bars, decimal tolerance = 0.001m)
    {
        if (bars.Length < 2) return false;
        var prev = bars[^2];
        var curr = bars[^1];
        var range = Math.Max(prev.High.Value, curr.High.Value) * tolerance;
        return Math.Abs(prev.High.Value - curr.High.Value) <= range &&
               prev.Close > prev.Open &&
               curr.Close < curr.Open;
    }

    /// <summary>
    /// Tweezer Bottoms - two candles with same low in downtrend.
    /// </summary>
    public static bool TweezerBottoms(ReadOnlySpan<Bar> bars, decimal tolerance = 0.001m)
    {
        if (bars.Length < 2) return false;
        var prev = bars[^2];
        var curr = bars[^1];
        var range = Math.Max(prev.Low.Value, curr.Low.Value) * tolerance;
        return Math.Abs(prev.Low.Value - curr.Low.Value) <= range &&
               prev.Close < prev.Open &&
               curr.Close > curr.Open;
    }

    // ==================== THREE CANDLE ====================

    /// <summary>
    /// Morning Star - bearish, small body (gap down), bullish.
    /// </summary>
    public static bool MorningStar(ReadOnlySpan<Bar> bars)
    {
        if (bars.Length < 3) return false;
        var first = bars[^3];
        var second = bars[^2];
        var third = bars[^1];
        var secondBody = Math.Abs(second.Close.Value - second.Open.Value);
        var firstBody = Math.Abs(first.Close.Value - first.Open.Value);

        return first.Close < first.Open &&                    // First is bearish
               secondBody < firstBody * 0.3m &&               // Second is small
               second.High < first.Close &&                   // Second gaps down
               third.Close > third.Open &&                    // Third is bullish
               third.Close > (first.Open.Value + first.Close.Value) / 2;  // Third closes above first's midpoint
    }

    /// <summary>
    /// Evening Star - bullish, small body (gap up), bearish.
    /// </summary>
    public static bool EveningStar(ReadOnlySpan<Bar> bars)
    {
        if (bars.Length < 3) return false;
        var first = bars[^3];
        var second = bars[^2];
        var third = bars[^1];
        var secondBody = Math.Abs(second.Close.Value - second.Open.Value);
        var firstBody = Math.Abs(first.Close.Value - first.Open.Value);

        return first.Close > first.Open &&                    // First is bullish
               secondBody < firstBody * 0.3m &&               // Second is small
               second.Low > first.Close &&                    // Second gaps up
               third.Close < third.Open &&                    // Third is bearish
               third.Close < (first.Open.Value + first.Close.Value) / 2;  // Third closes below first's midpoint
    }

    /// <summary>
    /// Three White Soldiers - three consecutive bullish candles with higher closes.
    /// </summary>
    public static bool ThreeWhiteSoldiers(ReadOnlySpan<Bar> bars)
    {
        if (bars.Length < 3) return false;
        for (int i = bars.Length - 3; i < bars.Length; i++)
        {
            if (bars[i].Close <= bars[i].Open) return false;  // Must be bullish
            if (i > bars.Length - 3 && bars[i].Close <= bars[i - 1].Close) return false;  // Higher close
        }
        return true;
    }

    /// <summary>
    /// Three Black Crows - three consecutive bearish candles with lower closes.
    /// </summary>
    public static bool ThreeBlackCrows(ReadOnlySpan<Bar> bars)
    {
        if (bars.Length < 3) return false;
        for (int i = bars.Length - 3; i < bars.Length; i++)
        {
            if (bars[i].Close >= bars[i].Open) return false;  // Must be bearish
            if (i > bars.Length - 3 && bars[i].Close >= bars[i - 1].Close) return false;  // Lower close
        }
        return true;
    }

    /// <summary>
    /// Three Inside Up - bullish harami followed by bullish confirmation.
    /// </summary>
    public static bool ThreeInsideUp(ReadOnlySpan<Bar> bars)
    {
        if (bars.Length < 3) return false;
        return Harami(bars[^3..^1]) == PatternSignal.Bullish &&
               bars[^1].Close > bars[^1].Open &&
               bars[^1].Close > bars[^2].Close;
    }

    /// <summary>
    /// Three Inside Down - bearish harami followed by bearish confirmation.
    /// </summary>
    public static bool ThreeInsideDown(ReadOnlySpan<Bar> bars)
    {
        if (bars.Length < 3) return false;
        return Harami(bars[^3..^1]) == PatternSignal.Bearish &&
               bars[^1].Close < bars[^1].Open &&
               bars[^1].Close < bars[^2].Close;
    }

    /// <summary>
    /// Abandoned Baby - star pattern with gap on both sides.
    /// </summary>
    public static PatternSignal AbandonedBaby(ReadOnlySpan<Bar> bars)
    {
        if (bars.Length < 3) return PatternSignal.None;
        var first = bars[^3];
        var second = bars[^2];
        var third = bars[^1];

        // Bullish abandoned baby
        if (first.Close < first.Open &&
            second.High < first.Low &&
            third.Low > second.High &&
            third.Close > third.Open)
            return PatternSignal.Bullish;

        // Bearish abandoned baby
        if (first.Close > first.Open &&
            second.Low > first.High &&
            third.High < second.Low &&
            third.Close < third.Open)
            return PatternSignal.Bearish;

        return PatternSignal.None;
    }

    // ==================== PATTERN SCANNER ====================

    /// <summary>
    /// Scan for all patterns in the given bars.
    /// Note: Takes Bar[] instead of ReadOnlySpan due to yield return limitation.
    /// </summary>
    public static IEnumerable<(string Name, PatternSignal Signal)> ScanAll(Bar[] bars)
    {
        if (bars.Length == 0) yield break;

        // Single candle
        if (Doji(bars[^1])) yield return ("Doji", PatternSignal.None);
        if (Hammer(bars[^1])) yield return ("Hammer", PatternSignal.Bullish);
        if (InvertedHammer(bars[^1])) yield return ("InvertedHammer", PatternSignal.Bullish);
        if (Marubozu(bars[^1])) yield return ("Marubozu", bars[^1].Close > bars[^1].Open ? PatternSignal.Bullish : PatternSignal.Bearish);
        if (SpinningTop(bars[^1])) yield return ("SpinningTop", PatternSignal.None);

        if (bars.Length < 2) yield break;

        // Two candle
        if (BullishEngulfing(bars)) yield return ("BullishEngulfing", PatternSignal.Bullish);
        if (BearishEngulfing(bars)) yield return ("BearishEngulfing", PatternSignal.Bearish);
        var harami = Harami(bars);
        if (harami != PatternSignal.None) yield return ("Harami", harami);
        if (PiercingLine(bars)) yield return ("PiercingLine", PatternSignal.Bullish);
        if (DarkCloudCover(bars)) yield return ("DarkCloudCover", PatternSignal.Bearish);
        if (TweezerTops(bars)) yield return ("TweezerTops", PatternSignal.Bearish);
        if (TweezerBottoms(bars)) yield return ("TweezerBottoms", PatternSignal.Bullish);

        if (bars.Length < 3) yield break;

        // Three candle
        if (MorningStar(bars)) yield return ("MorningStar", PatternSignal.Bullish);
        if (EveningStar(bars)) yield return ("EveningStar", PatternSignal.Bearish);
        if (ThreeWhiteSoldiers(bars)) yield return ("ThreeWhiteSoldiers", PatternSignal.Bullish);
        if (ThreeBlackCrows(bars)) yield return ("ThreeBlackCrows", PatternSignal.Bearish);
        if (ThreeInsideUp(bars)) yield return ("ThreeInsideUp", PatternSignal.Bullish);
        if (ThreeInsideDown(bars)) yield return ("ThreeInsideDown", PatternSignal.Bearish);
        var baby = AbandonedBaby(bars);
        if (baby != PatternSignal.None) yield return ("AbandonedBaby", baby);
    }
}
