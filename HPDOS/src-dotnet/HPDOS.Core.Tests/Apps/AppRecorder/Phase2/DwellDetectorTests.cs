using HPDOS.Apps.AppRecorder.Intelligence;
using HPDOS.Apps.AppRecorder.Recording;
using Xunit;

namespace HPDOS.Core.Tests.Apps.AppRecorder.Phase2;

public class DwellDetectorTests
{
    // Helper: build a CursorTelemetry from a list of (t, cx, cy) tuples
    private static CursorTelemetry Tel(params (long t, double cx, double cy)[] pts)
        => new CursorTelemetry("test", 1920, 1080, pts.Select(p => new CursorSample(p.t, p.cx, p.cy)).ToList());

    // Helper: evenly-spaced samples at a fixed point for a given duration
    private static CursorTelemetry Dwell(double cx, double cy, long startMs, long durationMs, int hz = 10)
    {
        var samples = new List<CursorSample>();
        var intervalMs = 1000 / hz;
        for (long t = startMs; t <= startMs + durationMs; t += intervalMs)
            samples.Add(new CursorSample(t, cx, cy));
        return new CursorTelemetry("test", 1920, 1080, samples);
    }

    // ── #1 ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void EmptyTelemetry_ReturnsEmpty()
    {
        var result = DwellDetector.Detect(new CursorTelemetry("test", 1920, 1080, []));
        Assert.Empty(result);
    }

    // ── #2 ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void SingleSample_ReturnsEmpty()
    {
        var result = DwellDetector.Detect(Tel((0, 0.5, 0.5)));
        Assert.Empty(result);
    }

    // ── #3 ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void ShortDwell_BelowMinDwellMs_NotEmitted()
    {
        // 700ms < MinDwellMs (800ms)
        var result = DwellDetector.Detect(Dwell(0.5, 0.5, 0, 700));
        Assert.Empty(result);
    }

    // ── #4 ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void LongDwell_AboveMinDwellMs_EmitsZoomCandidate()
    {
        var result = DwellDetector.Detect(Dwell(0.5, 0.5, 0, 1000));
        Assert.Single(result);
        Assert.Equal(CandidateAction.AddZoom, result[0].Action);
        Assert.Equal(SignalKind.CursorDwell, result[0].Kind);
    }

    // ── #5 ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void DwellConfidence_800ms_Is0Point4()
    {
        // confidence = min(1.0, durationMs / 2000.0) = 800/2000 = 0.4
        var result = DwellDetector.Detect(Dwell(0.5, 0.5, 0, 800));
        Assert.Single(result);
        Assert.Equal(0.4, result[0].Confidence, precision: 3);
    }

    // ── #6 ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void DwellConfidence_2000ms_Is1Point0()
    {
        var result = DwellDetector.Detect(Dwell(0.5, 0.5, 0, 2000));
        Assert.Single(result);
        Assert.Equal(1.0, result[0].Confidence, precision: 5);
    }

    // ── #7 ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void DwellConfidence_5000ms_CappedAt1Point0()
    {
        var result = DwellDetector.Detect(Dwell(0.5, 0.5, 0, 5000));
        Assert.Single(result);
        Assert.Equal(1.0, result[0].Confidence, precision: 5);
    }

    // ── #8 ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void FocusPoint_IsAverageOfSamplesInWindow()
    {
        // Tight cluster within DwellRadiusNorm (0.08): all samples within 0.04 of centroid
        var result = DwellDetector.Detect(Tel(
            (0,   0.48, 0.48),
            (100, 0.50, 0.50),
            (200, 0.52, 0.52),
            (300, 0.50, 0.50),
            (400, 0.49, 0.51),
            (500, 0.51, 0.49),
            (600, 0.50, 0.50),
            (700, 0.50, 0.50),
            (800, 0.50, 0.50)
        ));
        Assert.NotEmpty(result);
        Assert.InRange(result[0].FocusCx!.Value, 0.45, 0.55);
        Assert.InRange(result[0].FocusCy!.Value, 0.45, 0.55);
    }

    // ── #9 ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void WideSpreadSamples_ExceedRadius_NotMergedIntoDwell()
    {
        // Samples widely spread — no window stays within DwellRadiusNorm (0.08)
        var result = DwellDetector.Detect(Tel(
            (0, 0.1, 0.1),
            (100, 0.9, 0.9),
            (200, 0.1, 0.9),
            (300, 0.9, 0.1),
            (400, 0.1, 0.1),
            (500, 0.9, 0.9),
            (600, 0.1, 0.9),
            (700, 0.9, 0.1),
            (800, 0.1, 0.1)
        ));
        Assert.Empty(result);
    }

    // ── #10 ────────────────────────────────────────────────────────────────────

    [Fact]
    public void AdjacentDwells_CloseInTimeAndSpace_MergedIntoOne()
    {
        // Two 900ms dwells at same location, 200ms apart (< 500ms merge threshold)
        var s1 = Dwell(0.5, 0.5, 0, 900);
        var s2 = Dwell(0.5, 0.5, 1100, 900);
        var combined = new CursorTelemetry("test", 1920, 1080, s1.Samples.Concat(s2.Samples).ToList());

        var result = DwellDetector.Detect(combined);
        Assert.Single(result);
    }

    // ── #11 ────────────────────────────────────────────────────────────────────

    [Fact]
    public void AdjacentDwells_FarApart_NotMerged()
    {
        // Two dwells at different locations > DwellRadiusNorm*1.5 apart — cannot be merged
        var s1 = Dwell(0.1, 0.5, 0, 900);
        var s2 = Dwell(0.9, 0.5, 2000, 900);
        var combined = new CursorTelemetry("test", 1920, 1080, s1.Samples.Concat(s2.Samples).ToList());

        var result = DwellDetector.Detect(combined);
        Assert.Equal(2, result.Count);
    }

    // ── #12 ────────────────────────────────────────────────────────────────────

    [Fact]
    public void MultipleDwells_ReturnedOrderedByConfidenceDescending()
    {
        // Three dwells at different durations: 800ms, 1200ms, 2000ms
        // Each at different locations so they don't merge
        var s1 = Dwell(0.1, 0.5, 0, 800);
        var s2 = Dwell(0.9, 0.5, 3000, 1200);
        var s3 = Dwell(0.5, 0.1, 6000, 2000);
        var combined = new CursorTelemetry("test", 1920, 1080, s1.Samples.Concat(s2.Samples).Concat(s3.Samples).ToList());

        var result = DwellDetector.Detect(combined);

        Assert.Equal(3, result.Count);
        Assert.True(result[0].Confidence >= result[1].Confidence);
        Assert.True(result[1].Confidence >= result[2].Confidence);
    }
}
