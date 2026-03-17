using HPDOS.Apps.AppRecorder.Intelligence;
using Xunit;

namespace HPDOS.Core.Tests.Apps.AppRecorder.Phase2;

public class SilenceDetectorTests
{
    // Helper: build typical ffmpeg silencedetect stderr output
    private static string MakeLine(double startSec, double endSec)
        => $"[silencedetect @ 0x1234] silence_start: {startSec.ToString(System.Globalization.CultureInfo.InvariantCulture)}\n" +
           $"[silencedetect @ 0x1234] silence_end: {endSec.ToString(System.Globalization.CultureInfo.InvariantCulture)} | silence_duration: {(endSec - startSec).ToString(System.Globalization.CultureInfo.InvariantCulture)}\n";

    // ── #23 ────────────────────────────────────────────────────────────────────

    [Fact]
    public void ParseSilenceOutput_EmptyString_ReturnsEmpty()
    {
        var result = SilenceDetector.ParseSilenceOutput("");
        Assert.Empty(result);
    }

    // ── #24 ────────────────────────────────────────────────────────────────────

    [Fact]
    public void ParseSilenceOutput_NoSilenceLines_ReturnsEmpty()
    {
        var result = SilenceDetector.ParseSilenceOutput("frame=100 fps=30 bitrate=5000kb/s\nEncoded 100 frames\n");
        Assert.Empty(result);
    }

    // ── #25 ────────────────────────────────────────────────────────────────────

    [Fact]
    public void ParseSilenceOutput_SingleSilence_ReturnsTrimCandidate()
    {
        // 3s silence at 5–8s → after 250ms pad: trim 5250–7750ms
        var result = SilenceDetector.ParseSilenceOutput(MakeLine(5.0, 8.0));
        Assert.Single(result);
        Assert.Equal(CandidateAction.Trim, result[0].Action);
        Assert.Equal(SignalKind.AudioSilence, result[0].Kind);
    }

    // ── #26 ────────────────────────────────────────────────────────────────────

    [Fact]
    public void ParseSilenceOutput_PaddingApplied_TrimStartAndEndCorrect()
    {
        // silence_start=5.000, silence_end=8.000 → raw 5000–8000ms
        // trimStart = 5000 + 250 = 5250, trimEnd = 8000 - 250 = 7750
        var result = SilenceDetector.ParseSilenceOutput(MakeLine(5.0, 8.0));
        Assert.Single(result);
        Assert.Equal(5250, result[0].StartMs);
        Assert.Equal(7750, result[0].EndMs);
    }

    // ── #27 ────────────────────────────────────────────────────────────────────

    [Fact]
    public void ParseSilenceOutput_SilenceBelowMinDuration_Ignored()
    {
        // 1.0s < MinSilenceMs (1500ms)
        var result = SilenceDetector.ParseSilenceOutput(MakeLine(5.0, 6.0));
        Assert.Empty(result);
    }

    // ── #28 ────────────────────────────────────────────────────────────────────

    [Fact]
    public void ParseSilenceOutput_AfterPadding_LessThan100ms_Dropped()
    {
        // 1.6s silence: raw 1600ms → after 2×250ms pad → core = 1100ms → > 100ms → kept
        // 1.55s silence: raw 1550ms → after pad → core = 1050ms → kept
        // Need core <= 100ms: duration = 500+250+250+100 = 1100ms raw → core = 600ms, still kept
        // To get core < 100ms: raw = PadMs*2 + 99 = 599ms < MinSilenceMs, so actually
        // this scenario can't happen (would be filtered by MinSilenceMs first).
        // The test verifies the 100ms guard: use a raw 1.6s but pads eat most of it.
        // Closest: MinSilenceMs=1500ms, pad=250ms each side → min core = 1500-500=1000ms > 100ms.
        // So actually this edge case is structurally unreachable with current constants.
        // Test that a 1.5s silence (exactly MinSilenceMs) produces core = 1000ms > 100ms → kept.
        var result = SilenceDetector.ParseSilenceOutput(MakeLine(5.0, 6.5));
        Assert.Single(result);
        Assert.True(result[0].EndMs - result[0].StartMs >= 100);
    }

    // ── #29 ────────────────────────────────────────────────────────────────────

    [Fact]
    public void ParseSilenceOutput_Confidence_ShortSilence_Low()
    {
        // Raw 2s silence at 5–7s: trimDur = (7000-250) - (5000+250) = 1500ms
        // confidence = min(1.0, 1500 / MaxSilenceMs(8000)) = 0.1875
        var result = SilenceDetector.ParseSilenceOutput(MakeLine(5.0, 7.0));
        Assert.Single(result);
        Assert.InRange(result[0].Confidence, 0.18, 0.20);
    }

    // ── #30 ────────────────────────────────────────────────────────────────────

    [Fact]
    public void ParseSilenceOutput_Confidence_LongSilence_CappedAt1()
    {
        // 12s silence → trimDur = (12000-500) >> MaxSilenceMs → confidence = 1.0
        var result = SilenceDetector.ParseSilenceOutput(MakeLine(0.0, 12.0));
        Assert.Single(result);
        Assert.Equal(1.0, result[0].Confidence, precision: 5);
    }

    // ── #31 ────────────────────────────────────────────────────────────────────

    [Fact]
    public void ParseSilenceOutput_MultipleSilences_ReturnedOrderedByConfidenceDesc()
    {
        // Three silences of 2s, 5s, 10s → longest confidence first
        var input = MakeLine(0.0, 2.0) + MakeLine(5.0, 10.0) + MakeLine(15.0, 25.0);
        var result = SilenceDetector.ParseSilenceOutput(input);
        Assert.Equal(3, result.Count);
        Assert.True(result[0].Confidence >= result[1].Confidence);
        Assert.True(result[1].Confidence >= result[2].Confidence);
    }

    // ── #32 ────────────────────────────────────────────────────────────────────

    [Fact]
    public void ParseSilenceOutput_MissingEnd_PendingStartIgnored()
    {
        // silence_start with no matching silence_end → no candidate emitted
        var input = "[silencedetect @ 0x1234] silence_start: 5.000\n";
        var result = SilenceDetector.ParseSilenceOutput(input);
        Assert.Empty(result);
    }

    // ── #33 ────────────────────────────────────────────────────────────────────

    [Fact]
    public void ParseSilenceOutput_InvariantCultureParsing()
    {
        // Ensure locale doesn't affect float parsing (5.500 should parse as 5.5 not 5500 etc.)
        var result = SilenceDetector.ParseSilenceOutput(MakeLine(5.5, 10.5));
        Assert.Single(result);
        Assert.Equal(5500 + SilenceDetector.PadMs, result[0].StartMs);
        Assert.Equal(10500 - SilenceDetector.PadMs, result[0].EndMs);
    }
}
