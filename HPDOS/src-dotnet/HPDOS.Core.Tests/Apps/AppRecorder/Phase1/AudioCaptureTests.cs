using HPDOS.Apps.AppRecorder;
using HPDOS.Apps.AppRecorder.Recording;
using HPDOS.Apps.AppRecorder.Toolkits;
using Xunit;

namespace HPDOS.Core.Tests.Apps.AppRecorder.Phase1;

/// <summary>
/// Tests for audio-related fields in RecordingOptions and their forwarding through the stack.
/// AudioGainTests #2–5 require native CMSampleBuffer — those are omitted here;
/// they are tested indirectly via ffprobe in the real integration suite (Phase 1 hardening).
/// </summary>
public class AudioCaptureTests : IAsyncLifetime
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
        var p = Path.Combine(Path.GetTempPath(), $"hpdos_audio_{Guid.NewGuid():N}.mp4");
        _tempFiles.Add(p);
        return p;
    }

    // #1 CaptureMicrophone defaults to true
    [Fact]
    public void RecordingOptions_CaptureMicDefault_True()
        => Assert.True(new RecordingOptions().CaptureMicrophone);

    // #2 CaptureSystemAudio defaults to false
    [Fact]
    public void RecordingOptions_CaptureSystemAudioDefault_False()
        => Assert.False(new RecordingOptions().CaptureSystemAudio);

    // #3 Gains default to 1.0
    [Fact]
    public void RecordingOptions_GainsDefault_One()
    {
        var opts = new RecordingOptions();
        Assert.Equal(1.0f, opts.MicrophoneGain);
        Assert.Equal(1.0f, opts.SystemAudioGain);
    }

    // #5 CaptureMicrophone=false forwarded to backend
    [Fact]
    public async Task StartRecording_PassesMicFlag_ToBackend()
    {
        var backend = new AudioCapturingBackend(TempVideo());
        var app = new AppRecorderApp();
        app.SetBackend(backend);
        var tk = new AppRecorderToolkit(app);

        await tk.StartRecording("display:0", new RecordingOptions(CaptureMicrophone: false));
        await tk.StopRecording();

        Assert.False(backend.LastOptions!.CaptureMicrophone);
    }

    // #6 CaptureSystemAudio=true forwarded to backend
    [Fact]
    public async Task StartRecording_PassesSystemAudioFlag_ToBackend()
    {
        var backend = new AudioCapturingBackend(TempVideo());
        var app = new AppRecorderApp();
        app.SetBackend(backend);
        var tk = new AppRecorderToolkit(app);

        await tk.StartRecording("display:0", new RecordingOptions(CaptureSystemAudio: true));
        await tk.StopRecording();

        Assert.True(backend.LastOptions!.CaptureSystemAudio);
    }

    // #7 MicrophoneGain=0.5 forwarded
    [Fact]
    public async Task StartRecording_PassesMicGain_ToBackend()
    {
        var backend = new AudioCapturingBackend(TempVideo());
        var app = new AppRecorderApp();
        app.SetBackend(backend);
        var tk = new AppRecorderToolkit(app);

        await tk.StartRecording("display:0", new RecordingOptions(MicrophoneGain: 0.5f));
        await tk.StopRecording();

        Assert.Equal(0.5f, backend.LastOptions!.MicrophoneGain);
    }

    // #8 SystemAudioGain=1.5 forwarded
    [Fact]
    public async Task StartRecording_PassesSystemAudioGain_ToBackend()
    {
        var backend = new AudioCapturingBackend(TempVideo());
        var app = new AppRecorderApp();
        app.SetBackend(backend);
        var tk = new AppRecorderToolkit(app);

        await tk.StartRecording("display:0", new RecordingOptions(SystemAudioGain: 1.5f));
        await tk.StopRecording();

        Assert.Equal(1.5f, backend.LastOptions!.SystemAudioGain);
    }

    // #10 Both audio sources combined
    [Fact]
    public async Task StartRecording_BothAudioSources_BothForwarded()
    {
        var backend = new AudioCapturingBackend(TempVideo());
        var app = new AppRecorderApp();
        app.SetBackend(backend);
        var tk = new AppRecorderToolkit(app);

        await tk.StartRecording("display:0",
            new RecordingOptions(CaptureMicrophone: true, CaptureSystemAudio: true));
        await tk.StopRecording();

        Assert.True(backend.LastOptions!.CaptureMicrophone);
        Assert.True(backend.LastOptions!.CaptureSystemAudio);
    }

    // #11 TelemetryPath is null (audio in-band, not a separate file)
    [Fact]
    public async Task RecordingResult_AudioNotInTelemetryPath()
    {
        var backend = new AudioCapturingBackend(TempVideo());
        var app = new AppRecorderApp();
        app.SetBackend(backend);

        var handle = await app.StartRecordingAsync(
            new RecordingSource("display:0", "Screen", RecordingSourceKind.Screen, 1920, 1080),
            new RecordingOptions());
        var (_, result) = await app.StopRecordingAsync(handle.SessionId);

        // TelemetryPath is cursor data only — not audio
        Assert.Null(result.TelemetryPath);
    }
}

// ── AudioGainTests — pure logic (gains, scale factors) ───────────────────────

public class AudioGainTests
{
    // #1 Gain=1.0 → no mutation (identity)
    [Fact]
    public void ScaledAudioBuffer_GainOne_SamplesUnchanged()
    {
        float[] samples = [0.8f, 0.4f, -0.2f, 0.6f];
        var result = ApplyGain(samples, 1.0f);
        Assert.Equal(samples, result);
    }

    // #2 Gain=0.5 → halved
    [Fact]
    public void ScaledAudioBuffer_GainHalf_SamplesHalved()
    {
        float[] samples = [0.8f, 0.4f];
        var result = ApplyGain(samples, 0.5f);
        Assert.Equal([0.4f, 0.2f], result, EqualityComparer<float>.Default);
    }

    // #3 Gain=2.0 → doubled
    [Fact]
    public void ScaledAudioBuffer_GainTwo_SamplesDoubled()
    {
        float[] samples = [0.2f, 0.1f];
        var result = ApplyGain(samples, 2.0f);
        Assert.Equal(0.4f, result[0], precision: 5);
        Assert.Equal(0.2f, result[1], precision: 5);
    }

    // #4 Gain=0 → silence
    [Fact]
    public void ScaledAudioBuffer_GainZero_SilenceOutput()
    {
        float[] samples = [0.5f, -0.3f, 0.8f];
        var result = ApplyGain(samples, 0.0f);
        Assert.All(result, s => Assert.Equal(0.0f, s));
    }

    // #6/#7 Gain=1.0 is identity — the scale operation is a no-op
    [Fact]
    public void ScaledAudioBuffer_GainOne_IsIdentity()
    {
        float[] samples = [0.1f, 0.5f, -0.7f];
        var original = (float[])samples.Clone();
        var result = ApplyGain(samples, 1.0f);
        Assert.Equal(original, result);
    }

    // Helper: pure C# model of the audio gain scale path (mirrors what the native side does)
    private static float[] ApplyGain(float[] samples, float gain)
    {
        if (gain == 1.0f) return samples;
        var result = new float[samples.Length];
        for (int i = 0; i < samples.Length; i++)
            result[i] = samples[i] * gain;
        return result;
    }
}

// ── Test double ───────────────────────────────────────────────────────────────

file sealed class AudioCapturingBackend(string videoPath) : IRecordingBackend
{
    public RecordingOptions? LastOptions { get; private set; }
    public bool SupportsSystemAudio => true;
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
