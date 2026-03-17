using System.Text.Json;
using System.Text.Json.Serialization;

namespace HPDOS.Apps.AppRecorder.Recording;

// ── Data model ────────────────────────────────────────────────────────────────

/// <param name="T">Milliseconds since recording start.</param>
/// <param name="Cx">Normalised horizontal position (0.0 = left, 1.0 = right).</param>
/// <param name="Cy">Normalised vertical position (0.0 = top, 1.0 = bottom).</param>
public sealed record CursorSample(long T, double Cx, double Cy);

public sealed record CursorTelemetry(
    string SessionId,
    int DisplayWidth,
    int DisplayHeight,
    IReadOnlyList<CursorSample> Samples
);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(CursorTelemetry))]
[JsonSerializable(typeof(CursorSample))]
[JsonSerializable(typeof(List<CursorSample>))]
internal partial class CursorTelemetryJsonContext : JsonSerializerContext { }

// ── Collector ─────────────────────────────────────────────────────────────────

/// <summary>
/// Samples the cursor at 10Hz during recording.
/// Call StartAsync when recording begins, StopAsync when recording ends.
/// Writes a .cursor.json sidecar alongside the video file.
/// The serialized telemetry is also returned so the project file can embed it.
/// </summary>
public sealed class CursorTelemetryCollector : IAsyncDisposable
{
    private const int SampleIntervalMs = 100; // 10Hz

    private readonly string _sessionId;
    private readonly int _displayWidth;
    private readonly int _displayHeight;
    private readonly Func<(double cx, double cy)> _cursorProvider;

    private readonly List<CursorSample> _samples = [];
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private DateTimeOffset _startedAt;

    /// <param name="cursorProvider">
    /// Platform-specific delegate that returns the current cursor position
    /// as normalised (cx, cy) coordinates. Provided by NativeRecordingBackend.
    /// </param>
    public CursorTelemetryCollector(
        string sessionId,
        int displayWidth,
        int displayHeight,
        Func<(double cx, double cy)> cursorProvider)
    {
        _sessionId = sessionId;
        _displayWidth = displayWidth;
        _displayHeight = displayHeight;
        _cursorProvider = cursorProvider;
    }

    public void Start()
    {
        _startedAt = DateTimeOffset.UtcNow;
        _samples.Clear();
        _cts = new CancellationTokenSource();
        _loop = RunAsync(_cts.Token);
    }

    private async Task RunAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(SampleIntervalMs));
        var origin = DateTimeOffset.UtcNow;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await timer.WaitForNextTickAsync(ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            try
            {
                var (cx, cy) = _cursorProvider();
                var t = (long)(DateTimeOffset.UtcNow - origin).TotalMilliseconds;
                _samples.Add(new CursorSample(t, cx, cy));
            }
            catch (Exception)
            {
                // Provider failure is non-fatal — skip this sample and continue.
            }
        }
    }

    /// <summary>
    /// Stop sampling, write sidecar file, return the telemetry for embedding.
    /// </summary>
    /// <param name="videoPath">Path to the raw video file — sidecar is written alongside it.</param>
    public async Task<CursorTelemetry> StopAsync(string videoPath)
    {
        if (_cts is not null)
        {
            await _cts.CancelAsync();
            if (_loop is not null) await _loop.ConfigureAwait(false);
        }

        var telemetry = new CursorTelemetry(_sessionId, _displayWidth, _displayHeight, [.. _samples]);

        var sidecarPath = Path.ChangeExtension(videoPath, ".cursor.json");
        await WriteSidecarAsync(sidecarPath, telemetry);

        return telemetry;
    }

    private static async Task WriteSidecarAsync(string path, CursorTelemetry telemetry)
    {
        await using var fs = File.Create(path);
        await JsonSerializer.SerializeAsync(fs, telemetry, CursorTelemetryJsonContext.Default.CursorTelemetry);
    }

    public static string SidecarPathFor(string videoPath) =>
        Path.ChangeExtension(videoPath, ".cursor.json");

    public static async Task<CursorTelemetry?> LoadSidecarAsync(string videoPath)
    {
        var path = SidecarPathFor(videoPath);
        if (!File.Exists(path)) return null;

        await using var fs = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync(fs, CursorTelemetryJsonContext.Default.CursorTelemetry);
    }

    public async ValueTask DisposeAsync()
    {
        if (_cts is not null)
        {
            await _cts.CancelAsync();
            _cts.Dispose();
        }
    }
}
