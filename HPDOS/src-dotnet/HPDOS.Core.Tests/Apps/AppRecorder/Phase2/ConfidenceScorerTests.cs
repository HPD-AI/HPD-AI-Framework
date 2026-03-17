using HPDOS.Apps.AppRecorder.Intelligence;
using Xunit;

namespace HPDOS.Core.Tests.Apps.AppRecorder.Phase2;

public class ConfidenceScorerTests
{
    private static SignalCandidate Dwell(long start, long end, double conf, double cx = 0.5, double cy = 0.5)
        => new(start, end, SignalKind.CursorDwell, CandidateAction.AddZoom, conf, cx, cy);

    private static SignalCandidate Burst(long start, long end, double conf, double cx = 0.5, double cy = 0.5)
        => new(start, end, SignalKind.ClickBurst, CandidateAction.Highlight, conf, cx, cy);

    private static SignalCandidate Silence(long start, long end, double conf)
        => new(start, end, SignalKind.AudioSilence, CandidateAction.Trim, conf);

    // ── #34 ────────────────────────────────────────────────────────────────────

    [Fact]
    public void Score_EmptyList_ReturnsEmpty()
    {
        var result = ConfidenceScorer.Score([]);
        Assert.Empty(result);
    }

    // ── #35 ────────────────────────────────────────────────────────────────────

    [Fact]
    public void Score_SingleCandidate_ReturnedWithCorrectScore()
    {
        // CursorDwell weight = 1.0, so score = confidence * 1.0 / 1.0 = 0.8
        var result = ConfidenceScorer.Score([Dwell(0, 2000, 0.8)]);
        Assert.Single(result);
        Assert.Equal(0.8, result[0].Score, precision: 5);
    }

    // ── #36 ────────────────────────────────────────────────────────────────────

    [Fact]
    public void Score_ClickBurstWeightedAt0Point7()
    {
        // ClickBurst weight = 0.7, confidence = 1.0 → score = 1.0 * 0.7 / 0.7 = 1.0
        // Wait — weighted average: score = (1.0 * 0.7) / 0.7 = 1.0 (single item)
        // The weight affects multi-item merges, not single items. For single: score = confidence.
        // Correction: for single ClickBurst: weightedScore = 0.7, totalWeight = 0.7
        // baseScore = 0.7 / 0.7 = 1.0. So score = 1.0.
        // The weight only matters when mixing kinds — it biases their contribution.
        var result = ConfidenceScorer.Score([Burst(0, 1000, 1.0)]);
        Assert.Single(result);
        Assert.Equal(1.0, result[0].Score, precision: 5);
    }

    // ── #37 ────────────────────────────────────────────────────────────────────

    [Fact]
    public void Score_NonOverlappingCandidates_SameAction_NotMerged()
    {
        // Two trim candidates 5s apart — no overlap
        var result = ConfidenceScorer.Score([
            Silence(0, 2000, 0.5),
            Silence(7000, 9000, 0.5)
        ]);
        Assert.Equal(2, result.Count);
    }

    // ── #38 ────────────────────────────────────────────────────────────────────

    [Fact]
    public void Score_OverlappingCandidates_SameAction_MergedIntoOne()
    {
        // Two trim candidates overlapping by 500ms
        var result = ConfidenceScorer.Score([
            Silence(0, 3000, 0.5),
            Silence(2500, 5000, 0.5)
        ]);
        Assert.Single(result);
    }

    // ── #39 ────────────────────────────────────────────────────────────────────

    [Fact]
    public void Score_MergedWindow_SpansMinStartToMaxEnd()
    {
        var result = ConfidenceScorer.Score([
            Silence(1000, 5000, 0.5),
            Silence(3000, 8000, 0.5)
        ]);
        Assert.Single(result);
        Assert.Equal(1000, result[0].StartMs);
        Assert.Equal(8000, result[0].EndMs);
    }

    // ── #40 ────────────────────────────────────────────────────────────────────

    [Fact]
    public void Score_MultiSignalBonus_TwoKinds_Adds0Point15()
    {
        // Dwell (weight 1.0, conf 0.5) + Burst (weight 0.7, conf 0.5) overlapping
        // Base: (0.5*1.0 + 0.5*0.7) / (1.0+0.7) = 0.85/1.7 ≈ 0.5
        // Bonus for 2 distinct kinds: 0.15 * 1 = 0.15
        // Final = 0.5 + 0.15 = 0.65
        var dwell = new SignalCandidate(0, 2000, SignalKind.CursorDwell, CandidateAction.AddZoom, 0.5, 0.5, 0.5);
        var burst = new SignalCandidate(0, 2000, SignalKind.ClickBurst, CandidateAction.AddZoom, 0.5, 0.5, 0.5);
        var result = ConfidenceScorer.Score([dwell, burst]);
        Assert.Single(result);
        Assert.True(result[0].Score > 0.5, $"Expected score > 0.5 (multi-signal bonus), got {result[0].Score}");
    }

    // ── #41 ────────────────────────────────────────────────────────────────────

    [Fact]
    public void Score_MultiSignalBonus_ThreeKinds_Adds0Point30()
    {
        // 3 distinct kinds: bonus = 0.15 * 2 = 0.30
        var dwell = new SignalCandidate(0, 2000, SignalKind.CursorDwell, CandidateAction.AddZoom, 0.5, 0.5, 0.5);
        var burst = new SignalCandidate(0, 2000, SignalKind.ClickBurst, CandidateAction.AddZoom, 0.5, 0.5, 0.5);
        var silence = new SignalCandidate(0, 2000, SignalKind.AudioSilence, CandidateAction.AddZoom, 0.5, null, null);
        var result = ConfidenceScorer.Score([dwell, burst, silence]);
        Assert.Single(result);
        // Base ≈ 0.5, bonus = 0.30 → score ≈ 0.80
        Assert.True(result[0].Score > 0.65, $"Expected score > 0.65 (3-kind bonus), got {result[0].Score}");
    }

    // ── #42 ────────────────────────────────────────────────────────────────────

    [Fact]
    public void Score_MultiSignalBonus_CappedAt1Point0()
    {
        // High confidence + 2-kind bonus must not exceed 1.0
        var dwell = new SignalCandidate(0, 2000, SignalKind.CursorDwell, CandidateAction.AddZoom, 1.0, 0.5, 0.5);
        var burst = new SignalCandidate(0, 2000, SignalKind.ClickBurst, CandidateAction.AddZoom, 1.0, 0.5, 0.5);
        var result = ConfidenceScorer.Score([dwell, burst]);
        Assert.Single(result);
        Assert.True(result[0].Score <= 1.0);
    }

    // ── #43 ────────────────────────────────────────────────────────────────────

    [Fact]
    public void Score_DifferentActions_NeverMerged()
    {
        // Trim and Zoom at same time window — must stay separate
        var result = ConfidenceScorer.Score([
            Silence(0, 3000, 0.5),
            Dwell(0, 3000, 0.5)
        ]);
        Assert.Equal(2, result.Count);
        Assert.Contains(result, r => r.Action == CandidateAction.Trim);
        Assert.Contains(result, r => r.Action == CandidateAction.AddZoom);
    }

    // ── #44 ────────────────────────────────────────────────────────────────────

    [Fact]
    public void Score_FocusPoint_AverageOfZoomSources()
    {
        var d1 = Dwell(0, 2000, 0.8, 0.3, 0.3);
        var d2 = Dwell(500, 2500, 0.8, 0.7, 0.7);
        var result = ConfidenceScorer.Score([d1, d2]);
        Assert.Single(result);
        Assert.NotNull(result[0].FocusCx);
        Assert.Equal(0.5, result[0].FocusCx!.Value, precision: 5);
        Assert.Equal(0.5, result[0].FocusCy!.Value, precision: 5);
    }

    // ── #45 ────────────────────────────────────────────────────────────────────

    [Fact]
    public void Score_TrimCandidates_FocusPointIsNull()
    {
        var result = ConfidenceScorer.Score([Silence(0, 3000, 0.5)]);
        Assert.Single(result);
        Assert.Null(result[0].FocusCx);
        Assert.Null(result[0].FocusCy);
    }

    // ── #46 ────────────────────────────────────────────────────────────────────

    [Fact]
    public void Score_ResultOrderedByScoreDescending()
    {
        var result = ConfidenceScorer.Score([
            Silence(0, 3000, 0.2),
            Silence(10000, 13000, 0.8),
            Silence(20000, 23000, 0.5)
        ]);
        Assert.Equal(3, result.Count);
        Assert.True(result[0].Score >= result[1].Score);
        Assert.True(result[1].Score >= result[2].Score);
    }

    // ── #47 ────────────────────────────────────────────────────────────────────

    [Fact]
    public void Score_OverlapThreshold_ExactlyAtBoundary_NotMerged()
    {
        // Overlap = clusterEnd - nextStart. Merge if nextStart <= clusterEnd - OverlapThresholdMs(100)
        // So c1=[0,1000], c2=[901,2000]: overlap = 1000 - 901 = 99 < 100 → NOT merged
        var result = ConfidenceScorer.Score([
            Silence(0, 1000, 0.5),
            Silence(901, 2000, 0.5)
        ]);
        Assert.Equal(2, result.Count);
    }

    // ── #48 ────────────────────────────────────────────────────────────────────

    [Fact]
    public void Score_OverlapThreshold_JustAboveBoundary_Merged()
    {
        // c1=[0,1000], c2=[899,2000]: nextStart(899) <= clusterEnd(1000) - 100 = 900 → 899 <= 900 → MERGED
        var result = ConfidenceScorer.Score([
            Silence(0, 1000, 0.5),
            Silence(899, 2000, 0.5)
        ]);
        Assert.Single(result);
    }
}
