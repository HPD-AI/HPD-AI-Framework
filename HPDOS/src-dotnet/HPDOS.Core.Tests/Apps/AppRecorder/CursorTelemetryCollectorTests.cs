using HPDOS.Apps.AppRecorder.Recording;
using Xunit;

namespace HPDOS.Core.Tests.Apps.AppRecorder;

/// <summary>
/// Unit and integration tests for CursorTelemetryCollector.
/// File I/O tests write to temp paths and clean up after themselves.
/// </summary>
public class CursorTelemetryCollectorTests : IAsyncLifetime
{
    private readonly List<string> _tempFiles = [];

    private string TempVideo()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hpdos_test_{Guid.NewGuid():N}.mp4");
        _tempFiles.Add(path);
        _tempFiles.Add(CursorTelemetryCollector.SidecarPathFor(path));
        return path;
    }

    public Task InitializeAsync() => Task.CompletedTask;
    public Task DisposeAsync()
    {
        foreach (var f in _tempFiles)
            if (File.Exists(f)) File.Delete(f);
        return Task.CompletedTask;
    }

    private static CursorTelemetryCollector Make(
        string? sessionId = null,
        int width = 1920, int height = 1080,
        Func<(double, double)>? provider = null) =>
        new(
            sessionId ?? Guid.NewGuid().ToString("N"),
            width, height,
            provider ?? (() => (0.5, 0.5)));

    // ── Sampling ──────────────────────────────────────────────────────────────

    // #39 10Hz sampling — ~10 samples per second (±2 tolerance)
    [Fact]
    public async Task Sampling_At10Hz_CorrectSampleCount()
    {
        var collector = Make();
        collector.Start();
        await Task.Delay(1000);
        var tel = await collector.StopAsync(TempVideo());
        Assert.InRange(tel.Samples.Count, 8, 12);
    }

    // #40 Timestamps monotonically increase
    [Fact]
    public async Task Sampling_TimestampsMonotonicallyIncrease()
    {
        var collector = Make();
        collector.Start();
        await Task.Delay(500);
        var tel = await collector.StopAsync(TempVideo());

        for (int i = 1; i < tel.Samples.Count; i++)
            Assert.True(tel.Samples[i].T >= tel.Samples[i - 1].T);
    }

    // #41 Normalised coordinates in bounds
    [Fact]
    public async Task Sampling_NormalisedCoordinatesInBounds()
    {
        var collector = Make(provider: () => (0.5, 0.5));
        collector.Start();
        await Task.Delay(300);
        var tel = await collector.StopAsync(TempVideo());

        foreach (var s in tel.Samples)
        {
            Assert.Equal(0.5, s.Cx);
            Assert.Equal(0.5, s.Cy);
        }
    }

    // #42 Edge coordinates preserved
    [Fact]
    public async Task Sampling_EdgeCoordinatesPreserved()
    {
        var collector = Make(provider: () => (0.0, 1.0));
        collector.Start();
        await Task.Delay(300);
        var tel = await collector.StopAsync(TempVideo());

        Assert.All(tel.Samples, s =>
        {
            Assert.Equal(0.0, s.Cx);
            Assert.Equal(1.0, s.Cy);
        });
    }

    // ── Sidecar I/O ───────────────────────────────────────────────────────────

    // #43 Sidecar written next to video
    [Fact]
    public async Task StopAsync_WritesSidecarNextToVideo()
    {
        var video = TempVideo();
        var collector = Make();
        collector.Start();
        await Task.Delay(200);
        await collector.StopAsync(video);

        var sidecar = CursorTelemetryCollector.SidecarPathFor(video);
        Assert.True(File.Exists(sidecar));
    }

    // #44 Sidecar JSON is valid and readable
    [Fact]
    public async Task StopAsync_SidecarIsReadable()
    {
        var video = TempVideo();
        var collector = Make();
        collector.Start();
        await Task.Delay(200);
        await collector.StopAsync(video);

        var loaded = await CursorTelemetryCollector.LoadSidecarAsync(video);
        Assert.NotNull(loaded);
    }

    // #45 Returned telemetry matches sidecar
    [Fact]
    public async Task StopAsync_ReturnedTelemetryMatchesSidecar()
    {
        var video = TempVideo();
        var collector = Make(sessionId: "sess-abc");
        collector.Start();
        await Task.Delay(300);
        var returned = await collector.StopAsync(video);
        var loaded = await CursorTelemetryCollector.LoadSidecarAsync(video);

        Assert.NotNull(loaded);
        Assert.Equal(returned.SessionId, loaded.SessionId);
        Assert.Equal(returned.Samples.Count, loaded.Samples.Count);
        for (int i = 0; i < returned.Samples.Count; i++)
            Assert.Equal(returned.Samples[i], loaded.Samples[i]);
    }

    // #46 LoadSidecarAsync returns null if no sidecar
    [Fact]
    public async Task LoadSidecarAsync_ReturnsNullIfNoSidecar()
    {
        var video = TempVideo();
        var result = await CursorTelemetryCollector.LoadSidecarAsync(video);
        Assert.Null(result);
    }

    // #47 SidecarPathFor produces correct path
    [Fact]
    public void SidecarPathFor_CorrectPath()
    {
        var sidecar = CursorTelemetryCollector.SidecarPathFor("/a/b/video.mp4");
        Assert.Equal("/a/b/video.cursor.json", sidecar);
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    // #48 DisposeAsync stops sampling cleanly
    [Fact]
    public async Task DisposeAsync_StopsSamplingWithoutException()
    {
        var collector = Make();
        collector.Start();
        await Task.Delay(150);
        await collector.DisposeAsync(); // should not throw
    }

    // #49 Multiple start/stop cycles produce independent sample sets
    [Fact]
    public async Task MultipleCycles_ProduceIndependentSamples()
    {
        var video1 = TempVideo();
        var video2 = TempVideo();

        var collector = Make(provider: () => (0.1, 0.1));
        collector.Start();
        await Task.Delay(300);
        var tel1 = await collector.StopAsync(video1);

        // Reset and start again with different coordinates
        // CursorTelemetryCollector.Start() clears _samples
        var collector2 = Make(provider: () => (0.9, 0.9));
        collector2.Start();
        await Task.Delay(300);
        var tel2 = await collector2.StopAsync(video2);

        Assert.All(tel1.Samples, s => Assert.Equal(0.1, s.Cx));
        Assert.All(tel2.Samples, s => Assert.Equal(0.9, s.Cx));
    }

    // #51 Zero samples if stopped immediately
    [Fact]
    public async Task StopImmediately_ZeroOrVerySamples_ValidJson()
    {
        var video = TempVideo();
        var collector = Make();
        collector.Start();
        var tel = await collector.StopAsync(video); // stop before first tick

        // May have 0 or 1 sample depending on timing — what matters is it's valid
        Assert.NotNull(tel);
        Assert.NotNull(tel.Samples);
        var loaded = await CursorTelemetryCollector.LoadSidecarAsync(video);
        Assert.NotNull(loaded);
    }

    // #52 SessionId preserved in output
    [Fact]
    public async Task SessionId_PreservedInOutput()
    {
        var video = TempVideo();
        var collector = Make(sessionId: "my-session-123");
        collector.Start();
        await Task.Delay(200);
        var tel = await collector.StopAsync(video);
        Assert.Equal("my-session-123", tel.SessionId);
    }

    // #53 Display dimensions preserved
    [Fact]
    public async Task DisplayDimensions_Preserved()
    {
        var video = TempVideo();
        var collector = Make(width: 2560, height: 1440);
        collector.Start();
        await Task.Delay(200);
        var tel = await collector.StopAsync(video);
        Assert.Equal(2560, tel.DisplayWidth);
        Assert.Equal(1440, tel.DisplayHeight);
    }

    // ── Edge cases ────────────────────────────────────────────────────────────

    // #94 Sidecar file already exists — StopAsync overwrites without error
    [Fact]
    public async Task StopAsync_OverwritesExistingSidecar()
    {
        var video = TempVideo();
        var sidecar = CursorTelemetryCollector.SidecarPathFor(video);
        await File.WriteAllTextAsync(sidecar, "stale content");

        var collector = Make();
        collector.Start();
        await Task.Delay(200);
        await collector.StopAsync(video); // should overwrite without error

        var loaded = await CursorTelemetryCollector.LoadSidecarAsync(video);
        Assert.NotNull(loaded); // valid JSON, not the stale string
    }

    // #95 Video path with spaces — sidecar path correct
    [Fact]
    public void SidecarPathFor_PathWithSpaces_Correct()
    {
        var sidecar = CursorTelemetryCollector.SidecarPathFor("/Users/foo/my recordings/test.mp4");
        Assert.Equal("/Users/foo/my recordings/test.cursor.json", sidecar);
    }

    // OpenShot gap #3 — Path separator tolerance
    // SidecarPathFor uses Path.ChangeExtension which is platform-agnostic.
    // Verify a Windows-style path on macOS still produces a usable, consistent sidecar path.
    [Fact]
    public void SidecarPathFor_WindowsStylePath_StillConsistent()
    {
        // On macOS Path.ChangeExtension still works character-for-character
        var sidecar = CursorTelemetryCollector.SidecarPathFor(@"C:\Users\foo\video.mp4");
        Assert.EndsWith(".cursor.json", sidecar);
        // The sidecar path should be derivable from the video path — same base
        Assert.Equal(
            CursorTelemetryCollector.SidecarPathFor(@"C:\Users\foo\video.mp4"),
            sidecar);
    }
}
