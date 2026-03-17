using HPDOS.Apps.AppRecorder.Recording;
using Xunit;

namespace HPDOS.Core.Tests.Apps.AppRecorder.Phase1;

public class RecordingModelsTests
{
    // #1 Default RecordingOptions values
    [Fact]
    public void RecordingOptions_Defaults_AreCorrect()
    {
        var opts = new RecordingOptions();
        Assert.Equal(60, opts.FrameRate);
        Assert.True(opts.CaptureMicrophone);
        Assert.False(opts.CaptureSystemAudio);
        Assert.Equal(1.0f, opts.MicrophoneGain);
        Assert.Equal(1.0f, opts.SystemAudioGain);
    }

    // #2 RecordingSource record equality (value-based)
    [Fact]
    public void RecordingSource_EqualsByValue()
    {
        var a = new RecordingSource("id", "Display 1", RecordingSourceKind.Screen, 1920, 1080);
        var b = new RecordingSource("id", "Display 1", RecordingSourceKind.Screen, 1920, 1080);
        Assert.Equal(a, b);
    }

    // #3 RecordingHandle stores all fields
    [Fact]
    public void RecordingHandle_StoresAllFields()
    {
        var source = new RecordingSource("id", "Display", RecordingSourceKind.Screen, 1920, 1080);
        var opts = new RecordingOptions(30, CaptureMicrophone: false);
        var ts = DateTimeOffset.UtcNow;
        var handle = new RecordingHandle("sess-1", source, opts, ts);

        Assert.Equal("sess-1", handle.SessionId);
        Assert.Equal(source, handle.Source);
        Assert.Equal(opts, handle.Options);
        Assert.Equal(ts, handle.StartedAt);
    }

    // #4 RecordingResult stores all fields
    [Fact]
    public void RecordingResult_StoresAllFields()
    {
        var ts = DateTimeOffset.UtcNow;
        var result = new RecordingResult(
            "sess-1", "/tmp/v.mp4", null,
            TimeSpan.FromSeconds(10), 1920, 1080, 60, ts);

        Assert.Equal("sess-1", result.SessionId);
        Assert.Equal("/tmp/v.mp4", result.VideoPath);
        Assert.Null(result.TelemetryPath);
        Assert.Equal(TimeSpan.FromSeconds(10), result.Duration);
        Assert.Equal(1920, result.Width);
        Assert.Equal(1080, result.Height);
        Assert.Equal(60, result.FrameRate);
        Assert.Equal(ts, result.RecordedAt);
    }

    // #5 Custom gain preserved in RecordingOptions
    [Fact]
    public void RecordingOptions_CustomGain_Preserved()
    {
        var opts = new RecordingOptions(MicrophoneGain: 0.5f, SystemAudioGain: 1.5f);
        Assert.Equal(0.5f, opts.MicrophoneGain);
        Assert.Equal(1.5f, opts.SystemAudioGain);
    }
}
