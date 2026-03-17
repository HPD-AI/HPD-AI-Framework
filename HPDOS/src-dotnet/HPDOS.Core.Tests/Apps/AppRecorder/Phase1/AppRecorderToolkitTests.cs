using HPDOS.Apps.AppRecorder;
using HPDOS.Apps.AppRecorder.Recording;
using HPDOS.Apps.AppRecorder.Toolkits;
using Xunit;

namespace HPDOS.Core.Tests.Apps.AppRecorder.Phase1;

/// <summary>
/// Unit tests for AppRecorderToolkit (the agent-facing recording tool wrapper).
/// </summary>
public class AppRecorderToolkitTests : IAsyncLifetime
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
        var p = Path.Combine(Path.GetTempPath(), $"hpdos_tk_{Guid.NewGuid():N}.mp4");
        _tempFiles.Add(p);
        return p;
    }

    private (AppRecorderApp app, AppRecorderToolkit toolkit) Make(IRecordingBackend backend)
    {
        var app = new AppRecorderApp();
        app.SetBackend(backend);
        return (app, new AppRecorderToolkit(app));
    }

    // #1 ListSources delegates to app/backend
    [Fact]
    public async Task ListSources_ReturnsSources()
    {
        var (_, tk) = Make(new FakeRecordingBackend());
        var sources = await tk.ListSources();
        Assert.Equal(3, sources.Count);
    }

    // #2 StartRecording with valid sourceId returns session id
    [Fact]
    public async Task StartRecording_ValidSourceId_ReturnsSessionId()
    {
        var (app, tk) = Make(new FixedPathToolkitBackend(TempVideo()));
        var sessionId = await tk.StartRecording("display:0");
        Assert.NotEmpty(sessionId);
        // Cleanup
        await tk.StopRecording();
    }

    // #3 StartRecording with invalid sourceId throws ArgumentException
    [Fact]
    public async Task StartRecording_InvalidSourceId_Throws()
    {
        var (_, tk) = Make(new FakeRecordingBackend());
        await Assert.ThrowsAsync<ArgumentException>(() => tk.StartRecording("nonexistent"));
    }

    // #4 StopRecording without prior start throws InvalidOperationException
    [Fact]
    public async Task StopRecording_WithoutStart_Throws()
    {
        var (_, tk) = Make(new FakeRecordingBackend());
        await Assert.ThrowsAsync<InvalidOperationException>(() => tk.StopRecording());
    }

    // #5 Full start → stop lifecycle returns StopRecordingResult with ProjectId
    [Fact]
    public async Task StopRecording_AfterStart_ReturnsResult()
    {
        var (_, tk) = Make(new FixedPathToolkitBackend(TempVideo()));
        await tk.StartRecording("display:0");
        var result = await tk.StopRecording();
        Assert.NotEmpty(result.ProjectId);
    }

    // #6 TelemetryWritten = false when backend doesn't support telemetry
    [Fact]
    public async Task StopRecording_TelemetryWritten_FalseWhenBackendDoesNotSupport()
    {
        var (_, tk) = Make(new FixedPathToolkitBackend(TempVideo()));
        await tk.StartRecording("display:0");
        var result = await tk.StopRecording();
        Assert.False(result.TelemetryWritten);
    }

    // #7 Default options use 60fps
    [Fact]
    public async Task StartRecording_DefaultOptions_Uses60fps()
    {
        var backend = new CapturingBackend(TempVideo());
        var (_, tk) = Make(backend);
        await tk.StartRecording("display:0");
        await tk.StopRecording();
        Assert.Equal(60, backend.LastOptions!.FrameRate);
    }

    // #8 Custom options forwarded to backend
    [Fact]
    public async Task StartRecording_CustomOptions_Forwarded()
    {
        var backend = new CapturingBackend(TempVideo());
        var (_, tk) = Make(backend);
        await tk.StartRecording("display:0", new RecordingOptions(30, CaptureMicrophone: false));
        await tk.StopRecording();
        Assert.Equal(30, backend.LastOptions!.FrameRate);
        Assert.False(backend.LastOptions.CaptureMicrophone);
    }

    // #9 Second stop throws (session cleared after first stop)
    [Fact]
    public async Task StopRecording_ClearsActiveSession_SecondStopThrows()
    {
        var (_, tk) = Make(new FixedPathToolkitBackend(TempVideo()));
        await tk.StartRecording("display:0");
        await tk.StopRecording();
        await Assert.ThrowsAsync<InvalidOperationException>(() => tk.StopRecording());
    }
}

// ── Test doubles ──────────────────────────────────────────────────────────────

file sealed class FixedPathToolkitBackend(string videoPath) : IRecordingBackend
{
    public bool SupportsSystemAudio => false;
    public bool SupportsCursorTelemetry => false;
    public (double Cx, double Cy) GetCursorPosition(int w, int h) => (0.5, 0.5);

    public Task<IReadOnlyList<RecordingSource>> ListSourcesAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<RecordingSource>>([
            new("display:0", "Primary Display", RecordingSourceKind.Screen, 1920, 1080),
        ]);

    public Task<RecordingHandle> StartAsync(RecordingSource source, RecordingOptions options, CancellationToken ct = default) =>
        Task.FromResult(new RecordingHandle(Guid.NewGuid().ToString("N"), source, options, DateTimeOffset.UtcNow));

    public async Task<RecordingResult> StopAsync(RecordingHandle handle, CancellationToken ct = default)
    {
        await File.WriteAllBytesAsync(videoPath, [], ct);
        return new RecordingResult(handle.SessionId, videoPath, null,
            TimeSpan.FromSeconds(1), 1920, 1080, 60, DateTimeOffset.UtcNow);
    }
}

file sealed class CapturingBackend(string videoPath) : IRecordingBackend
{
    public RecordingOptions? LastOptions { get; private set; }

    public bool SupportsSystemAudio => false;
    public bool SupportsCursorTelemetry => false;
    public (double Cx, double Cy) GetCursorPosition(int w, int h) => (0.5, 0.5);

    public Task<IReadOnlyList<RecordingSource>> ListSourcesAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<RecordingSource>>([
            new("display:0", "Primary Display", RecordingSourceKind.Screen, 1920, 1080),
        ]);

    public Task<RecordingHandle> StartAsync(RecordingSource source, RecordingOptions options, CancellationToken ct = default)
    {
        LastOptions = options;
        return Task.FromResult(new RecordingHandle(Guid.NewGuid().ToString("N"), source, options, DateTimeOffset.UtcNow));
    }

    public async Task<RecordingResult> StopAsync(RecordingHandle handle, CancellationToken ct = default)
    {
        await File.WriteAllBytesAsync(videoPath, [], ct);
        return new RecordingResult(handle.SessionId, videoPath, null,
            TimeSpan.FromSeconds(1), 1920, 1080, 60, DateTimeOffset.UtcNow);
    }
}
