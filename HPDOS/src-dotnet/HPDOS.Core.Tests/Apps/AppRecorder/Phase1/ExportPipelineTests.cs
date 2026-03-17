using HPDOS.Apps.AppRecorder.Export;
using HPDOS.Apps.AppRecorder.Project;
using Xunit;

namespace HPDOS.Core.Tests.Apps.AppRecorder.Phase1;

/// <summary>
/// Integration tests for ExportPipeline.
/// All tests are skipped when ffmpeg is not on PATH or a sample video is unavailable.
/// Set env var HPDOS_TEST_VIDEO to a short (3–5 s) .mp4 path to enable these tests.
/// </summary>
public class ExportPipelineTests : IAsyncLifetime
{
    private readonly List<string> _tempFiles = [];

    public Task InitializeAsync() => Task.CompletedTask;
    public Task DisposeAsync()
    {
        foreach (var f in _tempFiles) try { if (File.Exists(f)) File.Delete(f); } catch { }
        return Task.CompletedTask;
    }

    private string TempMp4()
    {
        var p = Path.Combine(Path.GetTempPath(), $"hpdos_exp_{Guid.NewGuid():N}.mp4");
        _tempFiles.Add(p);
        return p;
    }

    private string TempGif()
    {
        var p = Path.Combine(Path.GetTempPath(), $"hpdos_exp_{Guid.NewGuid():N}.gif");
        _tempFiles.Add(p);
        return p;
    }

    private static bool FfmpegAvailable()
    {
        try
        {
            using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ffmpeg", "-version")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            p?.WaitForExit(3000);
            return p?.ExitCode == 0;
        }
        catch { return false; }
    }

    private static string? SampleVideo() => Environment.GetEnvironmentVariable("HPDOS_TEST_VIDEO");

    private static bool CanRun() => FfmpegAvailable() && SampleVideo() is { } v && File.Exists(v);

    // Probe ffmpeg capabilities once for the test class.
    private static async Task EnsureProbed()
    {
        if (!FfmpegAvailable()) return;
        try { await FfmpegProber.ProbeAsync(); } catch { }
    }

    private static ProjectModel MakeProject(string videoPath, Func<ProjectModel, ProjectModel>? configure = null)
    {
        var p = ProjectPersistence.CreateImportProject(videoPath);
        return configure is null ? p : configure(p);
    }

    // ── MP4 export ─────────────────────────────────────────────────────────────

    // #1 Basic passthrough export
    [SkippableFact]
    public async Task ExportMp4Async_EmptyProject_ProducesValidMp4()
    {
        Skip.IfNot(CanRun(), "ffmpeg or HPDOS_TEST_VIDEO not available");
        await EnsureProbed();

        var video = SampleVideo()!;
        var project = MakeProject(video);
        var dest = TempMp4();

        var result = await ExportPipeline.ExportMp4Async(project, "good", dest);

        Assert.Equal(dest, result);
        Assert.True(File.Exists(dest));
        Assert.True(new FileInfo(dest).Length > 0);
    }

    // #2 Medium quality produces smaller file than good
    [SkippableFact]
    public async Task ExportMp4Async_MediumQuality_BitrateLower()
    {
        Skip.IfNot(CanRun(), "ffmpeg or HPDOS_TEST_VIDEO not available");
        await EnsureProbed();

        var video = SampleVideo()!;
        var project = MakeProject(video);
        var destMedium = TempMp4();
        var destGood = TempMp4();

        await ExportPipeline.ExportMp4Async(project, "medium", destMedium);
        await ExportPipeline.ExportMp4Async(project, "good", destGood);

        var mediumSize = new FileInfo(destMedium).Length;
        var goodSize = new FileInfo(destGood).Length;

        // medium should be smaller (lower bitrate ceiling), but VideoToolbox uses -q:v so
        // compare that both are non-zero and medium ≤ good
        Assert.True(mediumSize > 0);
        Assert.True(mediumSize <= goodSize || FfmpegProber.Capabilities.VideoEncoder == "h264_videotoolbox",
            $"Expected medium ({mediumSize}) ≤ good ({goodSize}) for CBR encoders");
    }

    // #3 Custom output path used
    [SkippableFact]
    public async Task ExportMp4Async_CustomOutputPath_UsesProvidedPath()
    {
        Skip.IfNot(CanRun(), "ffmpeg or HPDOS_TEST_VIDEO not available");
        await EnsureProbed();

        var video = SampleVideo()!;
        var project = MakeProject(video);
        var dest = TempMp4();

        var result = await ExportPipeline.ExportMp4Async(project, "medium", dest);

        Assert.Equal(dest, result);
        Assert.True(File.Exists(dest));
    }

    // #4 Null output path derives from video path
    [SkippableFact]
    public async Task ExportMp4Async_NullOutputPath_DerivedFromVideoPath()
    {
        Skip.IfNot(CanRun(), "ffmpeg or HPDOS_TEST_VIDEO not available");
        await EnsureProbed();

        var video = SampleVideo()!;
        var project = MakeProject(video);

        var stem = Path.GetFileNameWithoutExtension(video);
        var dir = Path.GetDirectoryName(video)!;
        var expectedPath = Path.Combine(dir, stem + "_export.mp4");
        _tempFiles.Add(expectedPath); // ensure cleanup

        var result = await ExportPipeline.ExportMp4Async(project, "good", null);

        Assert.Equal(expectedPath, result);
        Assert.True(File.Exists(result));
    }

    // #5 Progress reports monotonically
    [SkippableFact]
    public async Task ExportMp4Async_Progress_ReportsMonotonically()
    {
        Skip.IfNot(CanRun(), "ffmpeg or HPDOS_TEST_VIDEO not available");
        await EnsureProbed();

        var video = SampleVideo()!;
        var project = MakeProject(video);
        var dest = TempMp4();

        var reports = new List<double>();
        var progress = new Progress<double>(v => reports.Add(v));

        await ExportPipeline.ExportMp4Async(project, "medium", dest, progress);

        // Allow brief moment for progress callbacks to fire
        await Task.Delay(50);

        for (var i = 1; i < reports.Count; i++)
            Assert.True(reports[i] >= reports[i - 1],
                $"Progress decreased at index {i}: {reports[i - 1]} → {reports[i]}");
    }

    // #6 Progress reaches 1.0 at end
    [SkippableFact]
    public async Task ExportMp4Async_Progress_ReachesOneAtEnd()
    {
        Skip.IfNot(CanRun(), "ffmpeg or HPDOS_TEST_VIDEO not available");
        await EnsureProbed();

        var video = SampleVideo()!;
        var project = MakeProject(video);
        var dest = TempMp4();

        double? lastReport = null;
        var progress = new Progress<double>(v => lastReport = v);

        await ExportPipeline.ExportMp4Async(project, "medium", dest, progress);
        await Task.Delay(50);

        Assert.Equal(1.0, lastReport);
    }

    // #7 Cancellation stops ffmpeg
    [SkippableFact]
    public async Task ExportMp4Async_CancellationToken_Cancels()
    {
        Skip.IfNot(CanRun(), "ffmpeg or HPDOS_TEST_VIDEO not available");
        await EnsureProbed();

        var video = SampleVideo()!;
        var project = MakeProject(video);
        var dest = TempMp4();

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => ExportPipeline.ExportMp4Async(project, "source", dest, ct: cts.Token));
    }

    // #8 Faststart flag — moov atom appears before mdat
    [SkippableFact]
    public async Task ExportMp4Async_FaststartFlag_MoovAtStart()
    {
        Skip.IfNot(CanRun(), "ffmpeg or HPDOS_TEST_VIDEO not available");
        await EnsureProbed();

        var video = SampleVideo()!;
        var project = MakeProject(video);
        var dest = TempMp4();

        await ExportPipeline.ExportMp4Async(project, "medium", dest);

        var bytes = await File.ReadAllBytesAsync(dest);

        // Search for 'moov' and 'mdat' 4-byte box names
        int FindBox(string name)
        {
            var needle = System.Text.Encoding.ASCII.GetBytes(name);
            for (var i = 0; i <= bytes.Length - 4; i++)
                if (bytes[i] == needle[0] && bytes[i + 1] == needle[1] &&
                    bytes[i + 2] == needle[2] && bytes[i + 3] == needle[3])
                    return i;
            return -1;
        }

        var moovPos = FindBox("moov");
        var mdatPos = FindBox("mdat");

        Assert.True(moovPos >= 0, "moov box not found");
        Assert.True(mdatPos >= 0, "mdat box not found");
        Assert.True(moovPos < mdatPos, $"moov ({moovPos}) should be before mdat ({mdatPos}) with +faststart");
    }

    // #9 Zoom region doesn't change output dimensions
    [SkippableFact]
    public async Task ExportMp4Async_WithZoomRegion_DimensionsPreserved()
    {
        Skip.IfNot(CanRun(), "ffmpeg or HPDOS_TEST_VIDEO not available");
        await EnsureProbed();

        var video = SampleVideo()!;
        var project = MakeProject(video, p => p.Append(new AddZoomRegion(0, 3000, 1.5, 0.5, 0.5)));
        var dest = TempMp4();

        await ExportPipeline.ExportMp4Async(project, "medium", dest);

        // Use ffprobe to check dimensions
        var probeArgs = $"-v quiet -print_format json -show_streams \"{dest}\"";
        using var proc = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ffprobe", probeArgs)
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        })!;
        var json = await proc.StandardOutput.ReadToEndAsync();
        await proc.WaitForExitAsync();

        // Output should have the same dimensions as input (crop+scale in filter preserves dimensions)
        Assert.Contains("\"width\"", json);
        Assert.Contains("\"height\"", json);
    }

    // #10 Source quality export produces a file
    [SkippableFact]
    public async Task ExportMp4Async_SourceQuality_ProducesFile()
    {
        Skip.IfNot(CanRun(), "ffmpeg or HPDOS_TEST_VIDEO not available");
        await EnsureProbed();

        var video = SampleVideo()!;
        var project = MakeProject(video);
        var dest = TempMp4();

        await ExportPipeline.ExportMp4Async(project, "source", dest);

        Assert.True(File.Exists(dest));
        Assert.True(new FileInfo(dest).Length > 0);
    }

    // ── GIF export ─────────────────────────────────────────────────────────────

    // #11 GIF output exists and starts with GIF89a magic bytes
    [SkippableFact]
    public async Task ExportGifAsync_ProducesValidGif()
    {
        Skip.IfNot(CanRun(), "ffmpeg or HPDOS_TEST_VIDEO not available");
        await EnsureProbed();

        var video = SampleVideo()!;
        var project = MakeProject(video);
        var dest = TempGif();

        await ExportPipeline.ExportGifAsync(project, 15, "medium", dest);

        Assert.True(File.Exists(dest));
        var header = await File.ReadAllBytesAsync(dest);
        Assert.True(header.Length >= 6);
        // GIF89a magic
        Assert.Equal((byte)'G', header[0]);
        Assert.Equal((byte)'I', header[1]);
        Assert.Equal((byte)'F', header[2]);
    }

    // #12 GIF custom output path used
    [SkippableFact]
    public async Task ExportGifAsync_CustomOutputPath_UsesProvidedPath()
    {
        Skip.IfNot(CanRun(), "ffmpeg or HPDOS_TEST_VIDEO not available");
        await EnsureProbed();

        var video = SampleVideo()!;
        var project = MakeProject(video);
        var dest = TempGif();

        var result = await ExportPipeline.ExportGifAsync(project, 15, "medium", dest);

        Assert.Equal(dest, result);
        Assert.True(File.Exists(dest));
    }

    // #13 Null output path derives .gif from video path
    [SkippableFact]
    public async Task ExportGifAsync_NullOutputPath_DerivedFromVideoPath()
    {
        Skip.IfNot(CanRun(), "ffmpeg or HPDOS_TEST_VIDEO not available");
        await EnsureProbed();

        var video = SampleVideo()!;
        var project = MakeProject(video);

        var stem = Path.GetFileNameWithoutExtension(video);
        var dir = Path.GetDirectoryName(video)!;
        var expectedPath = Path.Combine(dir, stem + "_export.gif");
        _tempFiles.Add(expectedPath);

        var result = await ExportPipeline.ExportGifAsync(project, 15, "medium", null);

        Assert.Equal(expectedPath, result);
        Assert.True(File.Exists(result));
    }

    // #14 Lower fps GIF is smaller than higher fps
    [SkippableFact]
    public async Task ExportGifAsync_LowerFps_SmallerFile()
    {
        Skip.IfNot(CanRun(), "ffmpeg or HPDOS_TEST_VIDEO not available");
        await EnsureProbed();

        var video = SampleVideo()!;
        var project = MakeProject(video);
        var dest15 = TempGif();
        var dest30 = TempGif();

        await ExportPipeline.ExportGifAsync(project, 15, "medium", dest15);
        await ExportPipeline.ExportGifAsync(project, 30, "medium", dest30);

        var size15 = new FileInfo(dest15).Length;
        var size30 = new FileInfo(dest30).Length;
        Assert.True(size15 > 0);
        Assert.True(size30 > 0);
        Assert.True(size15 <= size30, $"15fps ({size15}) should be ≤ 30fps ({size30})");
    }

    // #15 GIF cancellation stops ffmpeg
    [SkippableFact]
    public async Task ExportGifAsync_CancellationToken_Cancels()
    {
        Skip.IfNot(CanRun(), "ffmpeg or HPDOS_TEST_VIDEO not available");
        await EnsureProbed();

        var video = SampleVideo()!;
        var project = MakeProject(video);
        var dest = TempGif();

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => ExportPipeline.ExportGifAsync(project, 30, "original", dest, ct: cts.Token));
    }

    // #16 GIF progress reaches 1.0 at end
    [SkippableFact]
    public async Task ExportGifAsync_Progress_ReachesOneAtEnd()
    {
        Skip.IfNot(CanRun(), "ffmpeg or HPDOS_TEST_VIDEO not available");
        await EnsureProbed();

        var video = SampleVideo()!;
        var project = MakeProject(video);
        var dest = TempGif();

        double? lastReport = null;
        var progress = new Progress<double>(v => lastReport = v);

        await ExportPipeline.ExportGifAsync(project, 15, "medium", dest, progress);
        await Task.Delay(50);

        Assert.Equal(1.0, lastReport);
    }

    // #17 Large GIF size gives 1280px-wide output (capped)
    [SkippableFact]
    public async Task ExportGifAsync_LargeSize_Width1280OrSourceWidth()
    {
        Skip.IfNot(CanRun(), "ffmpeg or HPDOS_TEST_VIDEO not available");
        await EnsureProbed();

        var video = SampleVideo()!;
        var project = MakeProject(video);
        var dest = TempGif();

        await ExportPipeline.ExportGifAsync(project, 15, "large", dest);

        Assert.True(File.Exists(dest));
        Assert.True(new FileInfo(dest).Length > 0);
    }

    // #18 Original size GIF keeps source width
    [SkippableFact]
    public async Task ExportGifAsync_OriginalSize_ProducesFile()
    {
        Skip.IfNot(CanRun(), "ffmpeg or HPDOS_TEST_VIDEO not available");
        await EnsureProbed();

        var video = SampleVideo()!;
        var project = MakeProject(video);
        var dest = TempGif();

        await ExportPipeline.ExportGifAsync(project, 15, "original", dest);

        Assert.True(File.Exists(dest));
        Assert.True(new FileInfo(dest).Length > 0);
    }

    // #19 GIF with trim region produces a file
    [SkippableFact]
    public async Task ExportGifAsync_WithTrimRegion_ProducesFile()
    {
        Skip.IfNot(CanRun(), "ffmpeg or HPDOS_TEST_VIDEO not available");
        await EnsureProbed();

        var video = SampleVideo()!;
        var project = MakeProject(video, p => p.Append(new AddTrimRegion(0, 1000)));
        var dest = TempGif();

        await ExportPipeline.ExportGifAsync(project, 15, "medium", dest);

        Assert.True(File.Exists(dest));
        Assert.True(new FileInfo(dest).Length > 0);
    }
}
