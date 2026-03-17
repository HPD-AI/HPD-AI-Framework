using HPDOS.Apps.AppRecorder.Intelligence;
using HPDOS.Apps.AppRecorder.Recording;
using Xunit;

namespace HPDOS.Core.Tests.Apps.AppRecorder.Phase2;

public class ClickBurstDetectorTests
{
    private static CursorTelemetry Tel(params (long t, double cx, double cy)[] pts)
        => new CursorTelemetry("test", 1920, 1080, pts.Select(p => new CursorSample(p.t, p.cx, p.cy)).ToList());

    // ── #13 ────────────────────────────────────────────────────────────────────

    [Fact]
    public void EmptyTelemetry_ReturnsEmpty()
    {
        var result = ClickBurstDetector.Detect(new CursorTelemetry("test", 1920, 1080, []));
        Assert.Empty(result);
    }

    // ── #14 ────────────────────────────────────────────────────────────────────

    [Fact]
    public void LessThan3Samples_ReturnsEmpty()
    {
        var result = ClickBurstDetector.Detect(Tel((0, 0.1, 0.1), (100, 0.9, 0.9)));
        Assert.Empty(result);
    }

    // ── #15 ────────────────────────────────────────────────────────────────────

    [Fact]
    public void SlowCursor_BelowThreshold_NoCandidate()
    {
        // velocity = distance / dt. 0.05 norm / 0.1s = 0.5 norm/sec < threshold (1.5)
        var result = ClickBurstDetector.Detect(Tel(
            (0, 0.5, 0.5),
            (100, 0.505, 0.5),
            (200, 0.510, 0.5),
            (300, 0.515, 0.5),
            (400, 0.520, 0.5)
        ));
        Assert.Empty(result);
    }

    // ── #16 ────────────────────────────────────────────────────────────────────

    [Fact]
    public void FastCursor_AboveThreshold_EmitsHighlightCandidate()
    {
        // velocity = 0.3 / 0.1s = 3.0 norm/sec > threshold (1.5)
        var result = ClickBurstDetector.Detect(Tel(
            (0, 0.1, 0.5),
            (100, 0.4, 0.5),   // delta = 0.3 in 0.1s = 3.0/s
            (200, 0.4, 0.5),
            (300, 0.4, 0.5)
        ));
        Assert.NotEmpty(result);
        Assert.Equal(CandidateAction.Highlight, result[0].Action);
        Assert.Equal(SignalKind.ClickBurst, result[0].Kind);
    }

    // ── #17 ────────────────────────────────────────────────────────────────────

    [Fact]
    public void BurstWindowPadding_Applied()
    {
        // Burst at t=1000ms → windowStart = max(0, 1000 - BurstWindowMs/2) = 800
        // windowEnd = 1000 + BurstWindowMs/2 + PadMs = 1000 + 200 + 300 = 1500
        var result = ClickBurstDetector.Detect(Tel(
            (900, 0.1, 0.5),
            (1000, 0.4, 0.5),  // fast move here
            (1100, 0.4, 0.5),
            (1200, 0.4, 0.5)
        ));
        Assert.NotEmpty(result);
        Assert.True(result[0].StartMs <= 900);
        Assert.True(result[0].EndMs >= 1300);
    }

    // ── #18 ────────────────────────────────────────────────────────────────────

    [Fact]
    public void HighVelocity_ConfidenceFormula_CappedAt1()
    {
        // confidence = min(1.0, velocity / (VelocityThreshold * 2)) = min(1.0, 3.0 / 3.0) = 1.0
        var result = ClickBurstDetector.Detect(Tel(
            (0, 0.1, 0.5),
            (100, 0.4, 0.5),   // velocity = 3.0/s
            (200, 0.4, 0.5),
            (300, 0.4, 0.5)
        ));
        Assert.NotEmpty(result);
        Assert.Equal(1.0, result[0].Confidence, precision: 3);
    }

    // ── #19 ────────────────────────────────────────────────────────────────────

    [Fact]
    public void LowVelocity_JustAboveThreshold_LowConfidence()
    {
        // velocity = 0.2 / 0.1s = 2.0 norm/sec → confidence = 2.0 / (1.5*2) = 0.667
        var result = ClickBurstDetector.Detect(Tel(
            (0, 0.1, 0.5),
            (100, 0.3, 0.5),   // delta = 0.2 in 0.1s = 2.0/s (above threshold)
            (200, 0.3, 0.5),
            (300, 0.3, 0.5)
        ));
        Assert.NotEmpty(result);
        Assert.InRange(result[0].Confidence, 0.5, 0.8);
    }

    // ── #20 ────────────────────────────────────────────────────────────────────

    [Fact]
    public void AdjacentBursts_Overlapping_DeduplicatedIntoOne()
    {
        // Two fast moves very close together — windows overlap → deduplicated
        var result = ClickBurstDetector.Detect(Tel(
            (0, 0.1, 0.5),
            (100, 0.4, 0.5),   // burst 1
            (200, 0.4, 0.5),
            (250, 0.7, 0.5),   // burst 2 (within burst 1 window)
            (350, 0.7, 0.5),
            (450, 0.7, 0.5)
        ));
        Assert.Single(result);
    }

    // ── #21 ────────────────────────────────────────────────────────────────────

    [Fact]
    public void FocusPoint_IsCentroidOfWindowSamples()
    {
        var result = ClickBurstDetector.Detect(Tel(
            (0, 0.1, 0.5),
            (100, 0.4, 0.5),   // burst
            (200, 0.6, 0.5),   // in window
            (300, 0.6, 0.5)
        ));
        Assert.NotEmpty(result);
        Assert.NotNull(result[0].FocusCx);
        Assert.NotNull(result[0].FocusCy);
        // Focus should be average of window samples
        Assert.InRange(result[0].FocusCx!.Value, 0.0, 1.0);
        Assert.InRange(result[0].FocusCy!.Value, 0.0, 1.0);
    }

    // ── #22 ────────────────────────────────────────────────────────────────────

    [Fact]
    public void BurstSkipsForwardPastWindow_NoDoubleCount()
    {
        // One fast burst at t=100, then slow stable samples — only 1 candidate
        var samples = new List<(long t, double cx, double cy)>
        {
            (0,   0.1, 0.5),
            (100, 0.4, 0.5),  // big jump: velocity = 3.0/s → burst fires
            (200, 0.4, 0.5),
            (300, 0.4, 0.5),
            (400, 0.4, 0.5),
            (500, 0.4, 0.5),
            (600, 0.4, 0.5),
            (700, 0.4, 0.5),
            (800, 0.4, 0.5),
            (900, 0.4, 0.5),
            (1000, 0.4, 0.5),
        };
        var result = ClickBurstDetector.Detect(Tel(samples.ToArray()));
        Assert.Single(result);
    }
}
