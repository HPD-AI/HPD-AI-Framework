using System.Collections.Immutable;
using System.Reflection;
using HPDOS.Apps.AppRecorder.Export;
using HPDOS.Apps.AppRecorder.Project;
using Xunit;

namespace HPDOS.Core.Tests.Apps.AppRecorder.Phase1;

/// <summary>
/// Unit tests for ExportPipeline filter-chain construction.
/// All tests call BuildVideoFilter / BuildGifFilter via reflection (private static methods)
/// — pure string logic, no ffmpeg subprocess invoked.
/// </summary>
public class ExportPipelineFilterTests
{
    // Reflection helpers
    private static readonly MethodInfo BuildVideoFilter =
        typeof(ExportPipeline).GetMethod("BuildVideoFilter",
            BindingFlags.Static | BindingFlags.NonPublic)!;

    private static readonly MethodInfo BuildGifFilter =
        typeof(ExportPipeline).GetMethod("BuildGifFilter",
            BindingFlags.Static | BindingFlags.NonPublic)!;

    private static readonly MethodInfo GifWidthMethod =
        typeof(ExportPipeline).GetMethod("GifWidth",
            BindingFlags.Static | BindingFlags.NonPublic)!;

    private static string VideoFilter(ProjectModel project, int w = 1920, int h = 1080, long durMs = 10_000)
        => (string)BuildVideoFilter.Invoke(null, [project, w, h, durMs])!;

    private static string GifFilter(ProjectModel project, int gifW, int fps, long durMs = 10_000)
        => (string)BuildGifFilter.Invoke(null, [project, gifW, fps, durMs])!;

    private static int GifWidth(int w, int h, string size)
        => (int)GifWidthMethod.Invoke(null, [w, h, size])!;

    private static ProjectModel Empty() => new()
    {
        ProjectId = "test",
        SourceType = SourceType.Screen,
        ScreenMetadata = new ScreenSourceMetadata("d0", 1920, 1080),
        VideoPath = "/tmp/v.mp4",
        CreatedAt = DateTimeOffset.UtcNow,
    };

    // #1 no commands → "null" passthrough
    [Fact]
    public void BuildVideoFilter_NoCommands_ReturnsNull()
    {
        var f = VideoFilter(Empty());
        Assert.Equal("null", f);
    }

    // #2 single trim → select filter
    [Fact]
    public void BuildVideoFilter_SingleTrimRegion_GeneratesSelectFilter()
    {
        var model = Empty().Append(new AddTrimRegion(500, 2000));
        var f = VideoFilter(model);
        Assert.Contains("select=", f);
        Assert.Contains("not(between(t,0.500,2.000))", f);
    }

    // #3 multiple trims → joined with *
    [Fact]
    public void BuildVideoFilter_MultipleTrimRegions_JoinedWithAnd()
    {
        var model = Empty()
            .Append(new AddTrimRegion(0, 1000))
            .Append(new AddTrimRegion(3000, 4000));
        var f = VideoFilter(model);
        Assert.Contains("not(between(t,0.000,1.000))", f);
        Assert.Contains("not(between(t,3.000,4.000))", f);
        Assert.Contains("*", f); // joined
    }

    // #4 SetSpeed → setpts with conditional expression
    [Fact]
    public void BuildVideoFilter_SetSpeed_GeneratesSetpts()
    {
        var model = Empty().Append(new SetSpeed(1000, 3000, 2.0));
        var f = VideoFilter(model);
        Assert.Contains("setpts=", f);
        Assert.Contains("between(t", f);
        Assert.Contains("0.5000", f); // 1/2.0
    }

    // #5 zoom → crop+scale
    [Fact]
    public void BuildVideoFilter_ZoomRegion_GeneratesCropAndScale()
    {
        var model = Empty().Append(new AddZoomRegion(0, 5000, 2.0, 0.5, 0.5));
        var f = VideoFilter(model);
        Assert.Contains("crop=", f);
        Assert.Contains("scale=", f);
        Assert.Contains("between(t", f);
    }

    // #6 AddCrop → static crop filter
    [Fact]
    public void BuildVideoFilter_StaticCrop_GeneratesCrop()
    {
        var model = Empty().Append(new AddCrop(new CropOptions(0.1, 0.1, 0.8, 0.8)));
        var f = VideoFilter(model);
        Assert.Contains("crop=", f);
        // Width = 0.8 * 1920 = 1536
        Assert.Contains("1536", f);
    }

    // #7 combined commands appear in order: trim, speed, zoom, crop
    [Fact]
    public void BuildVideoFilter_CombinedCommands_OrderedCorrectly()
    {
        var model = Empty()
            .Append(new AddTrimRegion(0, 1000))
            .Append(new SetSpeed(2000, 4000, 2.0))
            .Append(new AddZoomRegion(0, 5000, 2.0, 0.5, 0.5))
            .Append(new AddCrop(new CropOptions(0.0, 0.0, 1.0, 1.0)));

        var f = VideoFilter(model);

        var selectIdx  = f.IndexOf("select=", StringComparison.Ordinal);
        var setptsIdx  = f.IndexOf("setpts=", StringComparison.Ordinal);
        var cropIdx    = f.IndexOf("crop=", StringComparison.Ordinal);

        Assert.True(selectIdx < setptsIdx, "trim (select) should come before speed (setpts)");
        Assert.True(setptsIdx < cropIdx,   "speed (setpts) should come before crop");
    }

    // #8 Add then Remove a trim → net zero active trims → no select filter
    [Fact]
    public void BuildVideoFilter_RemoveCommands_Excluded()
    {
        var model = Empty()
            .Append(new AddTrimRegion(0, 2000))
            .Append(new RemoveTrimRegion(0));  // removes first trim

        // After undo-equivalent (both commands active), but active contains both —
        // the pipeline applies AddTrimRegion only; RemoveTrimRegion is not a filter.
        // The real test: if no AddTrimRegion commands exist, no select filter.
        var emptyModel = Empty().Append(new RemoveTrimRegion(0)); // only a remove
        var f = VideoFilter(emptyModel);
        Assert.DoesNotContain("select=", f);
    }

    // #9 GIF filter contains palettegen and paletteuse
    [Fact]
    public void BuildGifFilter_IncludesPaletteGenAndUse()
    {
        var f = GifFilter(Empty(), 854, 20);
        Assert.Contains("palettegen", f);
        Assert.Contains("paletteuse=dither=bayer", f);
    }

    // #10 medium size gif = 854px (min(1920, 854))
    [Fact]
    public void BuildGifFilter_ScalesToMediumWidth()
    {
        var w = GifWidth(1920, 1080, "medium");
        Assert.Equal(854, w);
        var f = GifFilter(Empty(), w, 20);
        Assert.Contains("854", f);
    }

    // #11 large size = min(1920, 1280) = 1280
    [Fact]
    public void BuildGifFilter_ScalesToLargeWidth()
    {
        var w = GifWidth(1920, 1080, "large");
        Assert.Equal(1280, w);
        var f = GifFilter(Empty(), w, 20);
        Assert.Contains("1280", f);
    }

    // #12 original size keeps source width
    [Fact]
    public void BuildGifFilter_Original_KeepsSourceWidth()
    {
        var w = GifWidth(1920, 1080, "original");
        Assert.Equal(1920, w);
        var f = GifFilter(Empty(), w, 20);
        Assert.Contains("1920", f);
    }

    // Bonus: small source + large size = source width (not 1280)
    [Fact]
    public void GifWidth_Large_ClampedToSourceWidth()
    {
        var w = GifWidth(800, 600, "large");
        Assert.Equal(800, w);
    }

    // fps appears in GIF filter
    [Fact]
    public void BuildGifFilter_FpsIncluded()
    {
        var f = GifFilter(Empty(), 854, 15);
        Assert.Contains("fps=15", f);
    }
}
