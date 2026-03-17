using System.Reflection;
using HPDOS.Apps.AppRecorder.Export;
using Xunit;

namespace HPDOS.Core.Tests.Apps.AppRecorder.Phase1;

/// <summary>
/// Unit tests for FfmpegProber encoder selection logic.
/// Tests use reflection to construct FfmpegCapabilities with a known encoder set
/// (bypassing the actual ffmpeg subprocess) so these tests are pure unit tests.
/// </summary>
public class FfmpegProberTests
{
    private static FfmpegCapabilities Make(string binary, params string[] encoders)
    {
        var set = new HashSet<string>(encoders, StringComparer.OrdinalIgnoreCase);
        // FfmpegCapabilities has an internal constructor — use reflection.
        var ctor = typeof(FfmpegCapabilities)
            .GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
                null, [typeof(string), typeof(HashSet<string>)], null)!;
        return (FfmpegCapabilities)ctor.Invoke([binary, set]);
    }

    // #1 Capabilities before ProbeAsync throws
    [Fact]
    public void Capabilities_BeforeProbe_Throws()
    {
        // Reset cached state via reflection so the test is independent
        typeof(FfmpegProber)
            .GetField("_cached", BindingFlags.Static | BindingFlags.NonPublic)!
            .SetValue(null, null);

        Assert.Throws<InvalidOperationException>(() => _ = FfmpegProber.Capabilities);
    }

    // #2 macOS: prefer h264_videotoolbox over libx264
    [Fact]
    public void PickFirst_PrefersVideoToolbox()
    {
        var caps = Make("/usr/bin/ffmpeg", "h264_videotoolbox", "libx264");
        Assert.Equal("h264_videotoolbox", caps.VideoEncoder);
    }

    // #3 falls back to libx264 when no HW encoder
    [Fact]
    public void PickFirst_FallsBackToLibx264()
    {
        var caps = Make("/usr/bin/ffmpeg", "libx264");
        Assert.Equal("libx264", caps.VideoEncoder);
    }

    // #4 last resort: libvpx-vp9
    [Fact]
    public void PickFirst_LastResortLibvpx()
    {
        var caps = Make("/usr/bin/ffmpeg", "libvpx-vp9");
        Assert.Equal("libvpx-vp9", caps.VideoEncoder);
    }

    // #5 Windows: nvenc preferred over libx264
    [Fact]
    public void PickFirst_NvencOverLibx264()
    {
        var caps = Make("C:\\ffmpeg.exe", "h264_nvenc", "libx264");
        Assert.Equal("h264_nvenc", caps.VideoEncoder);
    }

    // #6 audio: prefer aac_at (AudioToolbox)
    [Fact]
    public void AudioEncoder_PrefersAacAt()
    {
        var caps = Make("/usr/bin/ffmpeg", "libx264", "aac_at", "aac");
        Assert.Equal("aac_at", caps.AudioEncoder);
    }

    // #7 audio: falls back to aac
    [Fact]
    public void AudioEncoder_FallsBackToAac()
    {
        var caps = Make("/usr/bin/ffmpeg", "libx264", "aac");
        Assert.Equal("aac", caps.AudioEncoder);
    }

    // #8 audio: last resort libopus
    [Fact]
    public void AudioEncoder_FallsBackToLibopus()
    {
        var caps = Make("/usr/bin/ffmpeg", "libx264", "libopus");
        Assert.Equal("libopus", caps.AudioEncoder);
    }

    // #9 FfmpegBinary stored
    [Fact]
    public void FfmpegBinary_IsStored()
    {
        var caps = Make("/opt/homebrew/bin/ffmpeg", "libx264");
        Assert.Equal("/opt/homebrew/bin/ffmpeg", caps.FfmpegBinary);
    }

    // #10 HasVideoToolbox true when present
    [Fact]
    public void HasVideoToolbox_TrueWhenPresent()
    {
        var caps = Make("/usr/bin/ffmpeg", "h264_videotoolbox");
        Assert.True(caps.HasVideoToolbox);
    }

    // HasVideoToolbox false when absent
    [Fact]
    public void HasVideoToolbox_FalseWhenAbsent()
    {
        var caps = Make("/usr/bin/ffmpeg", "libx264");
        Assert.False(caps.HasVideoToolbox);
    }

    // HasNvenc true when present
    [Fact]
    public void HasNvenc_TrueWhenPresent()
    {
        var caps = Make("/usr/bin/ffmpeg", "h264_nvenc", "libx264");
        Assert.True(caps.HasNvenc);
    }
}

/// <summary>
/// Integration tests — only run when ffmpeg is on PATH.
/// Skipped automatically when ffmpeg is unavailable.
/// </summary>
public class FfmpegProberIntegrationTests
{
    private static bool FfmpegAvailable()
    {
        try
        {
            using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ffmpeg", "-version")
            { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false });
            p?.WaitForExit(2000);
            return p?.ExitCode == 0;
        }
        catch { return false; }
    }

    [SkippableFact]
    public async Task ProbeAsync_ReturnsCapabilities_WhenFfmpegInstalled()
    {
        Skip.IfNot(FfmpegAvailable(), "ffmpeg not on PATH");

        // Reset cache
        typeof(FfmpegProber)
            .GetField("_cached", BindingFlags.Static | BindingFlags.NonPublic)!
            .SetValue(null, null);

        var caps = await FfmpegProber.ProbeAsync();
        Assert.NotNull(caps);
        Assert.NotEmpty(caps.FfmpegBinary);
    }

    [SkippableFact]
    public async Task ProbeAsync_IsCached_SecondCallReturnsSame()
    {
        Skip.IfNot(FfmpegAvailable(), "ffmpeg not on PATH");

        typeof(FfmpegProber)
            .GetField("_cached", BindingFlags.Static | BindingFlags.NonPublic)!
            .SetValue(null, null);

        var first  = await FfmpegProber.ProbeAsync();
        var second = await FfmpegProber.ProbeAsync();
        Assert.Same(first, second);
    }

    [SkippableFact]
    public async Task VideoEncoder_IsNotEmpty()
    {
        Skip.IfNot(FfmpegAvailable(), "ffmpeg not on PATH");

        typeof(FfmpegProber)
            .GetField("_cached", BindingFlags.Static | BindingFlags.NonPublic)!
            .SetValue(null, null);

        var caps = await FfmpegProber.ProbeAsync();
        Assert.NotEmpty(caps.VideoEncoder);
    }

    [SkippableFact]
    public async Task AudioEncoder_IsNotEmpty()
    {
        Skip.IfNot(FfmpegAvailable(), "ffmpeg not on PATH");

        typeof(FfmpegProber)
            .GetField("_cached", BindingFlags.Static | BindingFlags.NonPublic)!
            .SetValue(null, null);

        var caps = await FfmpegProber.ProbeAsync();
        Assert.NotEmpty(caps.AudioEncoder);
    }
}
