namespace HPDOS.Apps.AppRecorder.Recording;

// ── Source ────────────────────────────────────────────────────────────────────

public enum RecordingSourceKind { Screen, Window }

public sealed record RecordingSource(
    string Id,
    string DisplayName,
    RecordingSourceKind Kind,
    int Width,
    int Height
);

// ── Options ───────────────────────────────────────────────────────────────────

public sealed record RecordingOptions(
    int FrameRate = 60,
    bool CaptureMicrophone = true,
    bool CaptureSystemAudio = false,
    float MicrophoneGain = 1.0f,
    float SystemAudioGain = 1.0f
);

// ── Handle (live recording) ───────────────────────────────────────────────────

/// <summary>Opaque token returned by StartAsync. Passed back to StopAsync.</summary>
public sealed record RecordingHandle(
    string SessionId,
    RecordingSource Source,
    RecordingOptions Options,
    DateTimeOffset StartedAt
);

// ── Result (completed recording) ─────────────────────────────────────────────

public sealed record RecordingResult(
    string SessionId,
    string VideoPath,
    string? TelemetryPath,      // null if backend does not support cursor telemetry
    TimeSpan Duration,
    int Width,
    int Height,
    int FrameRate,
    DateTimeOffset RecordedAt
);
