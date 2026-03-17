using HPDOS.Apps.AppRecorder.Export;
using Xunit;

namespace HPDOS.Core.Tests.Apps.AppRecorder.Phase1;

public class BitrateCalculatorTests
{
    // #1 medium uses 0.055 bpp
    [Fact]
    public void VideoKbps_Medium_UsesLowBpp()
    {
        var result = BitrateCalculator.VideoKbps(1920, 1080, 60, "medium");
        var formula = (int)(1920 * 1080 * 60 * 0.055 / 1000);
        Assert.Equal(Math.Max(384, formula), result);
    }

    // #2 good uses 0.08 bpp, floor 5000
    [Fact]
    public void VideoKbps_Good_UsesDefaultBpp()
    {
        var result = BitrateCalculator.VideoKbps(1920, 1080, 60, "good");
        Assert.True(result >= 5_000);
    }

    // #3 source uses 0.12 bpp, floor 15000
    [Fact]
    public void VideoKbps_Source_UsesHighBpp()
    {
        var result = BitrateCalculator.VideoKbps(1920, 1080, 60, "source");
        Assert.True(result >= 15_000);
    }

    // #4 unknown string falls back to good (floor 5000)
    [Fact]
    public void VideoKbps_UnknownQuality_FallsBackToGood()
    {
        var unknown = BitrateCalculator.VideoKbps(1920, 1080, 60, "ultra");
        var good    = BitrateCalculator.VideoKbps(1920, 1080, 60, "good");
        Assert.Equal(good, unknown);
    }

    // #5 tiny resolution floors to 384 for medium
    [Fact]
    public void VideoKbps_TinyResolution_FloorEnforced_Medium()
    {
        var result = BitrateCalculator.VideoKbps(64, 64, 1, "medium");
        Assert.Equal(384, result);
    }

    // #6 tiny resolution floors to 15000 for source
    [Fact]
    public void VideoKbps_TinyResolution_FloorEnforced_Source()
    {
        var result = BitrateCalculator.VideoKbps(64, 64, 1, "source");
        Assert.Equal(15_000, result);
    }

    // #7 formula: w×h×fps×bpp/1000 vs floor
    [Fact]
    public void VideoKbps_FormulaCorrect()
    {
        // 1280×720×30×0.08/1000 = 2211.84 → (int)2211 → max(5000, 2211) = 5000
        var result = BitrateCalculator.VideoKbps(1280, 720, 30, "good");
        Assert.Equal(5_000, result);
    }

    // #8 audio medium = 96
    [Fact]
    public void AudioKbps_Medium() => Assert.Equal(96, BitrateCalculator.AudioKbps("medium"));

    // #9 audio good = 128
    [Fact]
    public void AudioKbps_Good() => Assert.Equal(128, BitrateCalculator.AudioKbps("good"));

    // #10 audio source = 192
    [Fact]
    public void AudioKbps_Source() => Assert.Equal(192, BitrateCalculator.AudioKbps("source"));

    // #11 audio unknown falls back to 128
    [Fact]
    public void AudioKbps_Unknown_FallsBackToGood() => Assert.Equal(128, BitrateCalculator.AudioKbps("4k"));
}
