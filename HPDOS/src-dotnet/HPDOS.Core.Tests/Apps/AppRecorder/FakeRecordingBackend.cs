using HPDOS.Apps.AppRecorder.Recording;

namespace HPDOS.Core.Tests.Apps.AppRecorder;

/// <summary>
/// Test double for IRecordingBackend.
/// Creates temporary files to satisfy file-existence contract tests.
/// </summary>
internal sealed class FakeRecordingBackend : IRecordingBackend
{
    public bool SupportsSystemAudio => false;
    public bool SupportsCursorTelemetry => false;
    public (double Cx, double Cy) GetCursorPosition(int displayWidth, int displayHeight) => (0.5, 0.5);

    public Task<IReadOnlyList<RecordingSource>> ListSourcesAsync(CancellationToken ct = default)
    {
        IReadOnlyList<RecordingSource> sources =
        [
            new("display:0", "Primary Display", RecordingSourceKind.Screen, 1920, 1080),
            new("display:1", "Secondary Display", RecordingSourceKind.Screen, 2560, 1440),
            new("window:finder", "Finder", RecordingSourceKind.Window, 800, 600),
        ];
        return Task.FromResult(sources);
    }

    public Task<RecordingHandle> StartAsync(
        RecordingSource source,
        RecordingOptions options,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var handle = new RecordingHandle(
            Guid.NewGuid().ToString("N"),
            source,
            options,
            DateTimeOffset.UtcNow);
        return Task.FromResult(handle);
    }

    public async Task<RecordingResult> StopAsync(RecordingHandle handle, CancellationToken ct = default)
    {
        // Create a real (empty) temp file so file-existence contract tests pass.
        var path = Path.Combine(Path.GetTempPath(), $"fake_{handle.SessionId}.mp4");
        await File.WriteAllBytesAsync(path, [], ct);

        return new RecordingResult(
            handle.SessionId,
            VideoPath: path,
            TelemetryPath: null,
            Duration: TimeSpan.FromMilliseconds(100),
            Width: handle.Source.Width,
            Height: handle.Source.Height,
            FrameRate: handle.Options.FrameRate,
            RecordedAt: DateTimeOffset.UtcNow);
    }
}
