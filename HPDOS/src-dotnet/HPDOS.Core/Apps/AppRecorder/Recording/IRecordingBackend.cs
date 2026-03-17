namespace HPDOS.Apps.AppRecorder.Recording;

/// <summary>
/// Abstracts the platform-specific screen capture implementation.
/// Today: NativeRecordingBackend (ScreenCaptureKit on macOS).
/// Future: BrowserRecordingBackend (getDisplayMedia), Windows.Graphics.Capture (Windows).
/// </summary>
public interface IRecordingBackend
{
    /// <summary>Enumerate available screens and capturable windows.</summary>
    Task<IReadOnlyList<RecordingSource>> ListSourcesAsync(CancellationToken ct = default);

    /// <summary>Begin capture. Returns a handle used to stop the recording.</summary>
    Task<RecordingHandle> StartAsync(RecordingSource source, RecordingOptions options, CancellationToken ct = default);

    /// <summary>Stop capture and finalise the raw video file. Returns the completed result.</summary>
    Task<RecordingResult> StopAsync(RecordingHandle handle, CancellationToken ct = default);

    /// <summary>True if this backend can capture system audio in addition to microphone audio.</summary>
    bool SupportsSystemAudio { get; }

    /// <summary>True if cursor telemetry can be collected on this backend.</summary>
    bool SupportsCursorTelemetry { get; }

    /// <summary>
    /// Return the current cursor position normalised to the given display bounds.
    /// Called at 10Hz by <see cref="CursorTelemetryCollector"/>.
    /// May be called from any thread. Must be non-blocking.
    /// Returns (0.5, 0.5) as a safe default when telemetry is not supported.
    /// </summary>
    (double Cx, double Cy) GetCursorPosition(int displayWidth, int displayHeight);
}
