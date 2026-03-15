using Rhodium.Indicators;
using Rhodium.Primitives;

namespace Rhodium.Indicators.Tests;

public class PatternsTests
{
    private static Bar CreateBar(decimal open, decimal high, decimal low, decimal close) =>
        new(
            new Price(open),
            new Price(high),
            new Price(low),
            new Price(close),
            Qty.Zero,
            Instant.Now,
            Duration.FromMinutes(5)
        );

    // ==================== SINGLE CANDLE TESTS ====================

    [Fact]
    public void Doji_DetectsDojiPattern()
    {
        var doji = CreateBar(100m, 100.5m, 99.5m, 100m); // Open and close nearly equal
        Assert.True(Patterns.Doji(doji));

        var notDoji = CreateBar(100m, 105m, 99m, 104m); // Large body
        Assert.False(Patterns.Doji(notDoji));
    }

    [Fact]
    public void Hammer_DetectsHammerPattern()
    {
        var hammer = CreateBar(100m, 100.3m, 95m, 100.2m); // Long lower shadow (5), small body (0.2), small upper shadow (0.1)
        Assert.True(Patterns.Hammer(hammer));

        var notHammer = CreateBar(100m, 105m, 99m, 104m); // No long shadow
        Assert.False(Patterns.Hammer(notHammer));
    }

    [Fact]
    public void InvertedHammer_DetectsInvertedHammerPattern()
    {
        var invHammer = CreateBar(100m, 105m, 99.95m, 99.96m); // Long upper shadow (~5), small body (0.01), tiny lower shadow (0.01)
        Assert.True(Patterns.InvertedHammer(invHammer));

        var notInvHammer = CreateBar(100m, 101m, 95m, 100.5m); // Long lower shadow
        Assert.False(Patterns.InvertedHammer(notInvHammer));
    }

    [Fact]
    public void Marubozu_DetectsMarubozuPattern()
    {
        var marubozu = CreateBar(100m, 105m, 100m, 105m); // No shadows
        Assert.True(Patterns.Marubozu(marubozu));

        var notMarubozu = CreateBar(100m, 106m, 98m, 105m); // Has shadows
        Assert.False(Patterns.Marubozu(notMarubozu));
    }

    [Fact]
    public void SpinningTop_DetectsSpinningTopPattern()
    {
        var spinningTop = CreateBar(100m, 102m, 98m, 100.5m); // Small body, shadows on both sides
        Assert.True(Patterns.SpinningTop(spinningTop));

        var notSpinningTop = CreateBar(100m, 105m, 100m, 105m); // Large body, no shadows
        Assert.False(Patterns.SpinningTop(notSpinningTop));
    }

    // ==================== TWO CANDLE TESTS ====================

    [Fact]
    public void BullishEngulfing_DetectsBullishEngulfingPattern()
    {
        var prev = CreateBar(105m, 106m, 100m, 100m); // Bearish
        var curr = CreateBar(99m, 107m, 99m, 107m);   // Bullish engulfing

        var bars = new[] { prev, curr };
        Assert.True(Patterns.BullishEngulfing(bars));
    }

    [Fact]
    public void BearishEngulfing_DetectsBearishEngulfingPattern()
    {
        var prev = CreateBar(100m, 106m, 100m, 106m); // Bullish
        var curr = CreateBar(107m, 107m, 98m, 98m);   // Bearish engulfing

        var bars = new[] { prev, curr };
        Assert.True(Patterns.BearishEngulfing(bars));
    }

    [Fact]
    public void Harami_DetectsHaramiPattern()
    {
        var prev = CreateBar(100m, 110m, 100m, 110m); // Large bullish
        var curr = CreateBar(104m, 106m, 104m, 106m); // Small bullish inside

        var bars = new[] { prev, curr };
        var result = Patterns.Harami(bars);

        Assert.Equal(PatternSignal.Bearish, result); // Bullish followed by small = bearish harami
    }

    [Fact]
    public void PiercingLine_DetectsPiercingLinePattern()
    {
        var prev = CreateBar(110m, 110m, 100m, 100m); // Bearish
        var curr = CreateBar(98m, 108m, 98m, 108m);   // Bullish closing above midpoint (105)

        var bars = new[] { prev, curr };
        Assert.True(Patterns.PiercingLine(bars));
    }

    [Fact]
    public void DarkCloudCover_DetectsDarkCloudCoverPattern()
    {
        var prev = CreateBar(100m, 110m, 100m, 110m); // Bullish
        var curr = CreateBar(112m, 112m, 102m, 102m); // Bearish closing below midpoint (105)

        var bars = new[] { prev, curr };
        Assert.True(Patterns.DarkCloudCover(bars));
    }

    [Fact]
    public void TweezerTops_DetectsTweezerTopsPattern()
    {
        var prev = CreateBar(100m, 110m, 100m, 108m); // Bullish
        var curr = CreateBar(108m, 110m, 102m, 102m); // Bearish with same high

        var bars = new[] { prev, curr };
        Assert.True(Patterns.TweezerTops(bars));
    }

    [Fact]
    public void TweezerBottoms_DetectsTweezerBottomsPattern()
    {
        var prev = CreateBar(110m, 110m, 100m, 102m); // Bearish
        var curr = CreateBar(102m, 108m, 100m, 108m); // Bullish with same low

        var bars = new[] { prev, curr };
        Assert.True(Patterns.TweezerBottoms(bars));
    }

    // ==================== THREE CANDLE TESTS ====================

    [Fact]
    public void MorningStar_DetectsMorningStarPattern()
    {
        var first = CreateBar(110m, 110m, 100m, 100m);   // Bearish
        var second = CreateBar(98m, 99m, 97m, 98m);      // Small body, gap down
        var third = CreateBar(99m, 110m, 99m, 108m);     // Bullish

        var bars = new[] { first, second, third };
        Assert.True(Patterns.MorningStar(bars));
    }

    [Fact]
    public void EveningStar_DetectsEveningStarPattern()
    {
        var first = CreateBar(100m, 110m, 100m, 110m);   // Bullish
        var second = CreateBar(112m, 113m, 111m, 112m);  // Small body, gap up
        var third = CreateBar(111m, 111m, 100m, 102m);   // Bearish

        var bars = new[] { first, second, third };
        Assert.True(Patterns.EveningStar(bars));
    }

    [Fact]
    public void ThreeWhiteSoldiers_DetectsThreeWhiteSoldiersPattern()
    {
        var first = CreateBar(100m, 105m, 100m, 105m);   // Bullish
        var second = CreateBar(105m, 110m, 105m, 110m);  // Bullish, higher close
        var third = CreateBar(110m, 115m, 110m, 115m);   // Bullish, higher close

        var bars = new[] { first, second, third };
        Assert.True(Patterns.ThreeWhiteSoldiers(bars));
    }

    [Fact]
    public void ThreeBlackCrows_DetectsThreeBlackCrowsPattern()
    {
        var first = CreateBar(115m, 115m, 110m, 110m);   // Bearish
        var second = CreateBar(110m, 110m, 105m, 105m);  // Bearish, lower close
        var third = CreateBar(105m, 105m, 100m, 100m);   // Bearish, lower close

        var bars = new[] { first, second, third };
        Assert.True(Patterns.ThreeBlackCrows(bars));
    }

    [Fact]
    public void ThreeInsideUp_DetectsThreeInsideUpPattern()
    {
        var first = CreateBar(110m, 110m, 100m, 100m);   // Bearish (large)
        var second = CreateBar(102m, 106m, 102m, 106m);  // Bullish (small, inside first)
        var third = CreateBar(106m, 112m, 106m, 112m);   // Bullish confirmation

        var bars = new[] { first, second, third };
        Assert.True(Patterns.ThreeInsideUp(bars));
    }

    [Fact]
    public void ThreeInsideDown_DetectsThreeInsideDownPattern()
    {
        var first = CreateBar(100m, 110m, 100m, 110m);   // Bullish (large)
        var second = CreateBar(104m, 106m, 104m, 106m);  // Bearish (small, inside first)
        var third = CreateBar(106m, 106m, 96m, 96m);     // Bearish confirmation

        var bars = new[] { first, second, third };
        Assert.True(Patterns.ThreeInsideDown(bars));
    }

    [Fact]
    public void AbandonedBaby_DetectsBullishAbandonedBabyPattern()
    {
        var first = CreateBar(110m, 110m, 100m, 100m);   // Bearish
        var second = CreateBar(97m, 98m, 96m, 97m);      // Star with gap below first
        var third = CreateBar(100m, 110m, 100m, 108m);   // Bullish with gap above second

        var bars = new[] { first, second, third };
        Assert.Equal(PatternSignal.Bullish, Patterns.AbandonedBaby(bars));
    }

    [Fact]
    public void AbandonedBaby_DetectsBearishAbandonedBabyPattern()
    {
        var first = CreateBar(100m, 110m, 100m, 110m);   // Bullish
        var second = CreateBar(113m, 114m, 112m, 113m);  // Star with gap above first
        var third = CreateBar(111m, 111m, 100m, 102m);   // Bearish with gap below second

        var bars = new[] { first, second, third };
        Assert.Equal(PatternSignal.Bearish, Patterns.AbandonedBaby(bars));
    }

    // ==================== SCANNER TESTS ====================

    [Fact]
    public void ScanAll_FindsMultiplePatterns()
    {
        var doji = CreateBar(100m, 100.5m, 99.5m, 100m);
        var hammer = CreateBar(100m, 100.3m, 95m, 100.2m);

        var bars = new[] { doji, hammer };
        var patterns = Patterns.ScanAll(bars).ToList();

        Assert.Contains(patterns, p => p.Name == "Hammer");
        Assert.True(patterns.Count > 0);
    }

    [Fact]
    public void ScanAll_HandlesEmptyInput()
    {
        var patterns = Patterns.ScanAll(Array.Empty<Bar>()).ToList();
        Assert.Empty(patterns);
    }
}
