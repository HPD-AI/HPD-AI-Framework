using System.Threading.Channels;
using HPD.Audio.Primitives;

namespace HPD.Agent.Audio.LiveKit;

internal interface ILiveKitAudioCapturePort
{
    ValueTask CaptureAsync(AudioFrame frame, CancellationToken cancellationToken);
    ValueTask ClearAsync(CancellationToken cancellationToken);
}

internal sealed class LiveKitInboundAudioSource : IAudioSource
{
    private readonly Channel<AudioFrame> _frames;
    private int _state;

    internal LiveKitInboundAudioSource(AudioFormat format, int capacity)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        Format = format;
        _frames = Channel.CreateBounded<AudioFrame>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });
    }

    public AudioFormat Format { get; }
    public bool CanChangeFormat => false;
    public AudioSourceState State => (AudioSourceState)Volatile.Read(ref _state);

    internal void AdmitCopiedPcm(ReadOnlySpan<byte> pcm, int samplesPerChannel, long sequence)
    {
        if (State != AudioSourceState.Open) throw new AudioSourceException(AudioStreamErrorKind.AlreadyCompleted, "Inbound LiveKit audio is closed.");
        var bytes = pcm.ToArray();
        var frame = new AudioFrame { Data = bytes, Format = Format, SamplesPerChannel = samplesPerChannel, SequenceNumber = sequence, ObservedAt = DateTimeOffset.UtcNow };
        if (!_frames.Writer.TryWrite(frame))
        {
            Fail();
            throw new AudioSourceException(AudioStreamErrorKind.BackpressureOverflow, "Inbound LiveKit PCM capacity was exceeded.");
        }
    }

    public bool TryRead(out AudioFrame frame) => _frames.Reader.TryRead(out frame);

    public async ValueTask<AudioReadResult> ReadAsync(CancellationToken cancellationToken = default)
    {
        while (await _frames.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
            if (_frames.Reader.TryRead(out var frame)) return new() { HasFrame = true, Frame = frame };
        return new() { HasFrame = false };
    }

    internal void Complete()
    {
        if (Interlocked.CompareExchange(ref _state, (int)AudioSourceState.Completed, (int)AudioSourceState.Open) == (int)AudioSourceState.Open)
            _frames.Writer.TryComplete();
    }

    internal void Fail()
    {
        Interlocked.Exchange(ref _state, (int)AudioSourceState.Failed);
        _frames.Writer.TryComplete(new AudioSourceException(AudioStreamErrorKind.BackpressureOverflow, "Inbound LiveKit PCM overflowed."));
    }

    public ValueTask DisposeAsync()
    {
        Interlocked.Exchange(ref _state, (int)AudioSourceState.Disposed);
        _frames.Writer.TryComplete();
        return ValueTask.CompletedTask;
    }
}

internal sealed class LiveKitOutboundAudioSink : IAudioSink
{
    private const int FrameDurationMilliseconds = 20;
    private const int Pcm16BytesPerSample = sizeof(short);
    private readonly ILiveKitAudioCapturePort _capture;
    private readonly SemaphoreSlim _serial = new(1, 1);
    private readonly object _writeFenceGate = new();
    private CancellationTokenSource _writeFence = new();
    private int _state;

    internal LiveKitOutboundAudioSink(AudioFormat format, ILiveKitAudioCapturePort capture)
    {
        PreferredFormat = format;
        _capture = capture;
    }

    public AudioFormat? PreferredFormat { get; }
    public AudioSinkState State => (AudioSinkState)Volatile.Read(ref _state);

    public bool TryWrite(in AudioFrame frame) => false;

    public async ValueTask WriteAsync(AudioFrame frame, CancellationToken cancellationToken = default)
    {
        EnsureOpen(frame);
        CancellationToken fenceToken;
        lock (_writeFenceGate) fenceToken = _writeFence.Token;
        using var writeCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, fenceToken);
        var entered = false;
        try
        {
            await _serial.WaitAsync(writeCancellation.Token).ConfigureAwait(false);
            entered = true;
            EnsureOpen(frame);
            var samplesPerFrame = checked(frame.Format.SampleRate * FrameDurationMilliseconds / 1_000);
            var bytesPerSampleFrame = checked(frame.Format.ChannelCount * Pcm16BytesPerSample);
            var expectedBytes = checked(frame.SamplesPerChannel * bytesPerSampleFrame);
            if (frame.Data.Length != expectedBytes)
                throw new AudioSinkException(AudioStreamErrorKind.ProtocolError,
                    "Outbound LiveKit PCM byte length does not match its sample count.");

            var sampleOffset = 0;
            var frameIndex = 0L;
            while (sampleOffset < frame.SamplesPerChannel)
            {
                var sampleCount = Math.Min(samplesPerFrame, frame.SamplesPerChannel - sampleOffset);
                var byteOffset = checked(sampleOffset * bytesPerSampleFrame);
                var byteLength = checked(sampleCount * bytesPerSampleFrame);
                var transportFrame = new AudioFrame
                {
                    Data = frame.Data.Slice(byteOffset, byteLength),
                    Format = frame.Format,
                    SamplesPerChannel = sampleCount,
                    SequenceNumber = frame.SequenceNumber is { } sequence
                        ? checked(sequence + frameIndex)
                        : null,
                    CaptureTime = frame.CaptureTime is { } captureTime
                        ? captureTime + TimeSpan.FromSeconds((double)sampleOffset / frame.Format.SampleRate)
                        : null,
                    ObservedAt = frame.ObservedAt,
                    RecoveryKind = frame.RecoveryKind,
                    Flags = frame.Flags
                };
                await _capture.CaptureAsync(transportFrame, writeCancellation.Token).ConfigureAwait(false);
                sampleOffset += sampleCount;
                frameIndex++;
            }
        }
        catch (Exception) when (fenceToken.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            // A session interruption owns this cancellation. A tracked native capture
            // may surface cancellation as outcome-unknown rather than OCE. The clear
            // reconciles native playout, so neither shape poisons the retained sink.
        }
        catch (Exception) when (cancellationToken.IsCancellationRequested)
        {
            // A producer cancellation is the normal barge-in fence for one response. A
            // tracked native capture may report outcome-unknown rather than OCE when the
            // cancellation races admission. The following clear reconciles that local
            // queue; the session-owned LiveKit source must remain usable by the next turn.
            throw;
        }
        catch { Interlocked.Exchange(ref _state, (int)AudioSinkState.Failed); throw; }
        finally { if (entered) _serial.Release(); }
    }

    public async ValueTask FlushAsync(CancellationToken cancellationToken = default)
    {
        if (State != AudioSinkState.Open) throw new AudioSinkException(AudioStreamErrorKind.AlreadyCompleted, "Outbound LiveKit audio is closed.");
        CancellationTokenSource interrupted;
        lock (_writeFenceGate)
        {
            interrupted = _writeFence;
            _writeFence = new CancellationTokenSource();
            interrupted.Cancel();
        }
        await _serial.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { await _capture.ClearAsync(cancellationToken).ConfigureAwait(false); }
        finally
        {
            _serial.Release();
            interrupted.Dispose();
        }
    }

    public async ValueTask CompleteAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.CompareExchange(ref _state, (int)AudioSinkState.Completing, (int)AudioSinkState.Open) != (int)AudioSinkState.Open) return;
        await _serial.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { Interlocked.Exchange(ref _state, (int)AudioSinkState.Completed); }
        finally { _serial.Release(); }
    }

    private void EnsureOpen(AudioFrame frame)
    {
        if (State != AudioSinkState.Open) throw new AudioSinkException(AudioStreamErrorKind.AlreadyCompleted, "Outbound LiveKit audio is closed.");
        var expected = PreferredFormat!.Value;
        if (frame.Format.SampleRate != expected.SampleRate || frame.Format.ChannelCount != expected.ChannelCount || frame.Format.SampleFormat != expected.SampleFormat)
            throw new AudioSinkException(AudioStreamErrorKind.FormatMismatch, "Outbound LiveKit PCM format does not match the admitted source.");
    }

    public ValueTask DisposeAsync()
    {
        Interlocked.Exchange(ref _state, (int)AudioSinkState.Disposed);
        lock (_writeFenceGate)
        {
            _writeFence.Cancel();
            _writeFence.Dispose();
        }
        _serial.Dispose();
        return ValueTask.CompletedTask;
    }
}
