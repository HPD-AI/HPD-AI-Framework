using HPDOS.Apps.AppRecorder;
using HPDOS.Apps.AppRecorder.Project;
using HPDOS.Apps.AppRecorder.Recording;
using Xunit;

namespace HPDOS.Core.Tests.Apps.AppRecorder.Phase1;

/// <summary>
/// Unit tests for AppRecorderApp recording lifecycle.
/// Uses a CapturingRecordingBackend that records what was passed to it.
/// </summary>
public class AppRecorderAppRecordingTests : IAsyncLifetime
{
    private readonly List<string> _tempFiles = [];

    public Task InitializeAsync() => Task.CompletedTask;
    public Task DisposeAsync()
    {
        foreach (var f in _tempFiles) if (File.Exists(f)) File.Delete(f);
        return Task.CompletedTask;
    }

    private string TempVideo()
    {
        var p = Path.Combine(Path.GetTempPath(), $"hpdos_app_{Guid.NewGuid():N}.mp4");
        _tempFiles.Add(p);
        _tempFiles.Add(p.Replace(".mp4", ".cursor.json"));
        return p;
    }

    private static RecordingSource Screen(string id = "display:0") =>
        new(id, "Screen", RecordingSourceKind.Screen, 1920, 1080);

    private AppRecorderApp MakeApp(IRecordingBackend backend)
    {
        var app = new AppRecorderApp();
        app.SetBackend(backend);
        return app;
    }

    // #1 ListSourcesAsync delegates to backend
    [Fact]
    public async Task ListSourcesAsync_DelegatesToBackend()
    {
        var backend = new FakeRecordingBackend(); // has 3 sources
        var app = MakeApp(backend);
        var sources = await app.ListSourcesAsync();
        Assert.Equal(3, sources.Count);
    }

    // #2 StartRecordingAsync starts telemetry when backend supports it
    [Fact]
    public async Task StartRecordingAsync_StartsTelemetry_WhenSupported()
    {
        var backend = new TelemetryCapableBackend(TempVideo());
        var app = MakeApp(backend);
        var handle = await app.StartRecordingAsync(Screen(), new RecordingOptions());
        Assert.NotEmpty(handle.SessionId);
        // Cleanup
        await app.StopRecordingAsync(handle.SessionId);
    }

    // #3 No telemetry collector when backend doesn't support it
    [Fact]
    public async Task StartRecordingAsync_NoTelemetry_WhenNotSupported()
    {
        var backend = new FakeRecordingBackend(); // SupportsCursorTelemetry = false
        var app = MakeApp(backend);
        var handle = await app.StartRecordingAsync(Screen(), new RecordingOptions());
        // Stop — should complete with TelemetryPath = null
        var (projectId, result) = await app.StopRecordingAsync(handle.SessionId);
        Assert.Null(result.TelemetryPath);
    }

    // #4 StopRecordingAsync stops telemetry and creates sidecar
    [Fact]
    public async Task StopRecordingAsync_StopsTelemetry_CreatesSidecar()
    {
        var videoPath = TempVideo();
        var backend = new TelemetryCapableBackend(videoPath);
        var app = MakeApp(backend);

        var handle = await app.StartRecordingAsync(Screen(), new RecordingOptions());
        await Task.Delay(200); // let a few samples accumulate
        var (_, result) = await app.StopRecordingAsync(handle.SessionId);

        Assert.NotNull(result.TelemetryPath);
        Assert.True(File.Exists(result.TelemetryPath));
    }

    // #5 StopRecordingAsync registers project in store
    [Fact]
    public async Task StopRecordingAsync_CreatesProject()
    {
        var videoPath = TempVideo();
        var backend = new FixedPathBackend(videoPath);
        var app = MakeApp(backend);

        var handle = await app.StartRecordingAsync(Screen(), new RecordingOptions());
        var (projectId, _) = await app.StopRecordingAsync(handle.SessionId);

        var project = app.GetProject(projectId);
        Assert.NotNull(project);
    }

    // #6 Project has correct VideoPath from result
    [Fact]
    public async Task StopRecordingAsync_ProjectHasCorrectVideoPath()
    {
        var videoPath = TempVideo();
        var backend = new FixedPathBackend(videoPath);
        var app = MakeApp(backend);

        var handle = await app.StartRecordingAsync(Screen(), new RecordingOptions());
        var (projectId, _) = await app.StopRecordingAsync(handle.SessionId);

        var project = app.GetProject(projectId);
        Assert.Equal(videoPath, project.VideoPath);
    }

    // #7 StopRecordingAsync throws for unknown session
    [Fact]
    public async Task StopRecordingAsync_UnknownSession_Throws()
    {
        var app = MakeApp(new FakeRecordingBackend());
        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => app.StopRecordingAsync("no-such-session"));
    }

    // #8 RegisterProject stores project
    [Fact]
    public void RegisterProject_StoresProject()
    {
        var app = MakeApp(new FakeRecordingBackend());
        var project = new ProjectModel
        {
            ProjectId = "reg-1",
            SourceType = SourceType.Import,
            ImportMetadata = new ImportSourceMetadata("/tmp/v.mp4"),
            VideoPath = "/tmp/v.mp4",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        app.RegisterProject(project);
        Assert.Same(project, app.GetProject("reg-1"));
    }

    // #9 Backend property accessible after SetBackend
    [Fact]
    public void SetBackend_BackendAccessible()
    {
        var app = new AppRecorderApp();
        var backend = new FakeRecordingBackend();
        app.SetBackend(backend);
        Assert.Same(backend, app.Backend);
    }

    // #10 Backend throws when not set
    [Fact]
    public void Backend_Unset_Throws()
    {
        var app = new AppRecorderApp();
        Assert.Throws<InvalidOperationException>(() => _ = app.Backend);
    }

    // #11 Two concurrent start calls → both sessions tracked
    [Fact]
    public async Task StartRecording_Twice_BothTracked()
    {
        var v1 = TempVideo();
        var v2 = TempVideo();
        var app = MakeApp(new MultiVideoBackend([v1, v2]));

        var h1 = await app.StartRecordingAsync(Screen("d0"), new RecordingOptions());
        var h2 = await app.StartRecordingAsync(Screen("d1"), new RecordingOptions());

        Assert.NotEqual(h1.SessionId, h2.SessionId);

        await app.StopRecordingAsync(h1.SessionId);
        await app.StopRecordingAsync(h2.SessionId);
    }
}

// ── Test doubles ──────────────────────────────────────────────────────────────

/// <summary>Backend that supports cursor telemetry. Writes a real temp file on stop.</summary>
file sealed class TelemetryCapableBackend(string videoPath) : IRecordingBackend
{
    public bool SupportsSystemAudio => false;
    public bool SupportsCursorTelemetry => true;
    public (double Cx, double Cy) GetCursorPosition(int w, int h) => (0.5, 0.5);

    public Task<IReadOnlyList<RecordingSource>> ListSourcesAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<RecordingSource>>([new("d0", "Screen", RecordingSourceKind.Screen, 1920, 1080)]);

    public Task<RecordingHandle> StartAsync(RecordingSource source, RecordingOptions options, CancellationToken ct = default) =>
        Task.FromResult(new RecordingHandle(Guid.NewGuid().ToString("N"), source, options, DateTimeOffset.UtcNow));

    public async Task<RecordingResult> StopAsync(RecordingHandle handle, CancellationToken ct = default)
    {
        await File.WriteAllBytesAsync(videoPath, [], ct);
        return new RecordingResult(handle.SessionId, videoPath, null,
            TimeSpan.FromSeconds(1), 1920, 1080, 60, DateTimeOffset.UtcNow);
    }
}

/// <summary>Backend that always returns a fixed video path on stop.</summary>
file sealed class FixedPathBackend(string videoPath) : IRecordingBackend
{
    public bool SupportsSystemAudio => false;
    public bool SupportsCursorTelemetry => false;
    public (double Cx, double Cy) GetCursorPosition(int w, int h) => (0.5, 0.5);

    public Task<IReadOnlyList<RecordingSource>> ListSourcesAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<RecordingSource>>([new("d0", "Screen", RecordingSourceKind.Screen, 1920, 1080)]);

    public Task<RecordingHandle> StartAsync(RecordingSource source, RecordingOptions options, CancellationToken ct = default) =>
        Task.FromResult(new RecordingHandle(Guid.NewGuid().ToString("N"), source, options, DateTimeOffset.UtcNow));

    public async Task<RecordingResult> StopAsync(RecordingHandle handle, CancellationToken ct = default)
    {
        await File.WriteAllBytesAsync(videoPath, [], ct);
        return new RecordingResult(handle.SessionId, videoPath, null,
            TimeSpan.FromSeconds(1), 1920, 1080, 60, DateTimeOffset.UtcNow);
    }
}

/// <summary>Backend that cycles through a list of video paths for multiple sessions.</summary>
file sealed class MultiVideoBackend(string[] paths) : IRecordingBackend
{
    private int _idx;
    public bool SupportsSystemAudio => false;
    public bool SupportsCursorTelemetry => false;
    public (double Cx, double Cy) GetCursorPosition(int w, int h) => (0.5, 0.5);

    public Task<IReadOnlyList<RecordingSource>> ListSourcesAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<RecordingSource>>([
            new("d0", "Screen 0", RecordingSourceKind.Screen, 1920, 1080),
            new("d1", "Screen 1", RecordingSourceKind.Screen, 1920, 1080),
        ]);

    public Task<RecordingHandle> StartAsync(RecordingSource source, RecordingOptions options, CancellationToken ct = default) =>
        Task.FromResult(new RecordingHandle(Guid.NewGuid().ToString("N"), source, options, DateTimeOffset.UtcNow));

    public async Task<RecordingResult> StopAsync(RecordingHandle handle, CancellationToken ct = default)
    {
        var path = paths[Interlocked.Increment(ref _idx) - 1];
        await File.WriteAllBytesAsync(path, [], ct);
        return new RecordingResult(handle.SessionId, path, null,
            TimeSpan.FromSeconds(1), 1920, 1080, 60, DateTimeOffset.UtcNow);
    }
}
