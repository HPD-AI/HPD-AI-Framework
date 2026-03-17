using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using HPDOS.Apps.AppRecorder.Recording;

namespace HPDOS.Shell.Recording;

/// <summary>
/// macOS ScreenCaptureKit recording backend.
/// Calls the HpdRecorder Swift static library via P/Invoke (@_cdecl exports).
/// Requires macCatalyst 18.0+ (ScreenCaptureKit availability).
/// Wire up via AppRecorderApp.SetBackend(new NativeRecordingBackend()).
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("maccatalyst18.2")]
public sealed class NativeRecordingBackend : IRecordingBackend
{
    public bool SupportsSystemAudio => true;
    public bool SupportsCursorTelemetry => true;

    // ── P/Invoke declarations ──────────────────────────────────────────────────

    // hpd_list_sources calls onSource once per source, then onComplete.
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void SourceCallback(uint sourceId, IntPtr displayName, int kind, int width, int height);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void ListCompleteCallback(IntPtr error);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void StopCallback(IntPtr videoPath, long durationMs, int width, int height, int frameRate, IntPtr error);

    [DllImport("__Internal", EntryPoint = "hpd_list_sources")]
    private static extern void NativeListSources(SourceCallback onSource, ListCompleteCallback onComplete);

    [DllImport("__Internal", EntryPoint = "hpd_start_capture")]
    private static extern uint NativeStartCapture(
        uint sourceId,
        [MarshalAs(UnmanagedType.I1)] bool isWindow,
        int frameRate,
        [MarshalAs(UnmanagedType.LPStr)] string outputPath,
        [MarshalAs(UnmanagedType.I1)] bool captureMic,
        [MarshalAs(UnmanagedType.I1)] bool captureSystemAudio,
        float micGain,
        float systemAudioGain,
        StopCallback onStop);

    [DllImport("__Internal", EntryPoint = "hpd_stop_capture")]
    private static extern void NativeStopCapture(uint sessionId);

    [DllImport("__Internal", EntryPoint = "hpd_get_cursor")]
    private static extern void NativeGetCursor(int displayWidth, int displayHeight, out double cx, out double cy);

    // ── Cursor position ────────────────────────────────────────────────────────

    public (double Cx, double Cy) GetCursorPosition(int displayWidth, int displayHeight)
    {
        NativeGetCursor(displayWidth, displayHeight, out var cx, out var cy);
        return (cx, cy);
    }

    // ── Source enumeration ─────────────────────────────────────────────────────

    public Task<IReadOnlyList<RecordingSource>> ListSourcesAsync(CancellationToken ct = default)
    {
        var tcs = new TaskCompletionSource<IReadOnlyList<RecordingSource>>();
        var sources = new List<RecordingSource>();

        // Keep delegates alive until the callback fires.
        SourceCallback onSource = null!;
        ListCompleteCallback onComplete = null!;

        onSource = (sourceId, namePtr, kind, width, height) =>
        {
            var name = Marshal.PtrToStringUTF8(namePtr) ?? $"Source {sourceId}";
            var sourceKind = kind == 0 ? RecordingSourceKind.Screen : RecordingSourceKind.Window;
            sources.Add(new RecordingSource(
                Id: sourceId.ToString(),
                DisplayName: name,
                Kind: sourceKind,
                Width: width,
                Height: height
            ));
        };

        onComplete = (errorPtr) =>
        {
            // GC.KeepAlive ensures delegates survive until this point.
            GC.KeepAlive(onSource);
            GC.KeepAlive(onComplete);

            if (errorPtr != IntPtr.Zero)
            {
                var msg = Marshal.PtrToStringUTF8(errorPtr) ?? "Unknown error";
                tcs.TrySetException(new InvalidOperationException($"ListSources failed: {msg}"));
            }
            else
            {
                tcs.TrySetResult(sources.AsReadOnly());
            }
        };

        NativeListSources(onSource, onComplete);

        ct.Register(() => tcs.TrySetCanceled(ct));
        return tcs.Task;
    }

    // ── Active recordings ──────────────────────────────────────────────────────

    // Tracks in-flight sessions: nativeSessionId → (handle, stopTcs, stopCallback)
    private readonly ConcurrentDictionary<uint, ActiveSession> _sessions = new();

    private sealed record ActiveSession(
        RecordingHandle Handle,
        TaskCompletionSource<RecordingResult> Tcs,
        StopCallback Callback  // kept alive so GC doesn't collect the delegate
    );

    // ── Start ─────────────────────────────────────────────────────────────────

    public async Task<RecordingHandle> StartAsync(RecordingSource source, RecordingOptions options, CancellationToken ct = default)
    {
        if (!uint.TryParse(source.Id, out var nativeSourceId))
            throw new ArgumentException($"Invalid source id: {source.Id}");

        var sessionId = Guid.NewGuid().ToString("N");
        var outputPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "hpdos", "recordings",
            $"{sessionId}.mp4"
        );
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        var handle = new RecordingHandle(
            SessionId: sessionId,
            Source: source,
            Options: options,
            StartedAt: DateTimeOffset.UtcNow
        );

        var tcs = new TaskCompletionSource<RecordingResult>();
        StopCallback stopCb = null!;

        stopCb = (videoPathPtr, durationMs, width, height, fps, errorPtr) =>
        {
            GC.KeepAlive(stopCb);

            if (_sessions.TryRemove(nativeSourceId, out _))
            {
                if (errorPtr != IntPtr.Zero)
                {
                    var msg = Marshal.PtrToStringUTF8(errorPtr) ?? "Capture error";
                    tcs.TrySetException(new InvalidOperationException($"Recording failed: {msg}"));
                }
                else
                {
                    var path = Marshal.PtrToStringUTF8(videoPathPtr) ?? outputPath;
                    tcs.TrySetResult(new RecordingResult(
                        SessionId: sessionId,
                        VideoPath: path,
                        TelemetryPath: null,  // CursorTelemetryCollector writes sidecar separately (Task 12)
                        Duration: TimeSpan.FromMilliseconds(durationMs),
                        Width: width,
                        Height: height,
                        FrameRate: fps,
                        RecordedAt: DateTimeOffset.UtcNow
                    ));
                }
            }
        };

        var nativeId = NativeStartCapture(
            nativeSourceId,
            source.Kind == RecordingSourceKind.Window,
            options.FrameRate,
            outputPath,
            options.CaptureMicrophone,
            options.CaptureSystemAudio,
            options.MicrophoneGain,
            options.SystemAudioGain,
            stopCb
        );

        if (nativeId == 0)
            throw new InvalidOperationException("Failed to start capture — NativeStartCapture returned 0.");

        // Store with the nativeId key so StopAsync can call NativeStopCapture.
        _sessions[nativeId] = new ActiveSession(handle with { SessionId = nativeId.ToString() }, tcs, stopCb);

        // Return the handle immediately — caller can call StopAsync later.
        return await Task.FromResult(handle);
    }

    // ── Stop ──────────────────────────────────────────────────────────────────

    public async Task<RecordingResult> StopAsync(RecordingHandle handle, CancellationToken ct = default)
    {
        // Find the native session id.  We stored the original sessionId in the handle;
        // but the ConcurrentDictionary is keyed by nativeId (uint).
        // Walk to find the matching session.
        var entry = _sessions.FirstOrDefault(kv => kv.Value.Handle.SessionId == handle.SessionId
            || kv.Value.Handle.StartedAt == handle.StartedAt);

        if (entry.Value is null)
            throw new KeyNotFoundException($"No active recording for session {handle.SessionId}.");

        NativeStopCapture(entry.Key);

        // Wait for the stop callback to deliver the result.
        using var reg = ct.Register(() => entry.Value.Tcs.TrySetCanceled(ct));
        return await entry.Value.Tcs.Task;
    }
}
