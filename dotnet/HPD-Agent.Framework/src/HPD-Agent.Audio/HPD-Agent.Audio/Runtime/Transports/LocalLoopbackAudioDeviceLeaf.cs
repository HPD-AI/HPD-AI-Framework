using System.Threading.Channels;
using HPD.Agent.Authority;

namespace HPD.Agent.Audio.Runtime.Transports;

internal sealed record LocalAudioDeviceDescriptor
{
    internal LocalAudioDeviceDescriptor(
        BoundedAscii deviceId,
        uint sampleRate,
        ushort channels,
        uint maximumFrameBytes,
        ushort frameCapacity)
    {
        if (sampleRate is < 8_000 or > 384_000)
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        if (channels is 0 or > 32)
            throw new ArgumentOutOfRangeException(nameof(channels));
        if (maximumFrameBytes is 0 or > 1_048_576)
            throw new ArgumentOutOfRangeException(nameof(maximumFrameBytes));
        if (frameCapacity is 0 or > 4_096)
            throw new ArgumentOutOfRangeException(nameof(frameCapacity));

        DeviceId = deviceId;
        SampleRate = sampleRate;
        Channels = channels;
        MaximumFrameBytes = maximumFrameBytes;
        FrameCapacity = frameCapacity;
    }

    internal BoundedAscii DeviceId { get; }
    internal uint SampleRate { get; }
    internal ushort Channels { get; }
    internal uint MaximumFrameBytes { get; }
    internal ushort FrameCapacity { get; }
    internal bool IsVirtual => true;
}

internal sealed class LocalAudioDeviceFrame
{
    private readonly byte[] _pcm;

    internal LocalAudioDeviceFrame(ulong sequence, ReadOnlySpan<byte> pcm)
    {
        Sequence = sequence;
        _pcm = pcm.ToArray();
    }

    internal ulong Sequence { get; }
    internal ReadOnlyMemory<byte> Pcm => _pcm;
}

internal abstract record LocalAudioDeviceWriteResult
{
    private LocalAudioDeviceWriteResult() { }
    internal sealed record Accepted(ulong Sequence) : LocalAudioDeviceWriteResult;
    internal sealed record Refused(BoundedAscii SafeCode) : LocalAudioDeviceWriteResult;
}

internal abstract record LocalAudioDeviceReadResult
{
    private LocalAudioDeviceReadResult() { }
    internal sealed record Frame(LocalAudioDeviceFrame Value) : LocalAudioDeviceReadResult;
    internal sealed record End : LocalAudioDeviceReadResult;
    internal sealed record Refused(BoundedAscii SafeCode) : LocalAudioDeviceReadResult;
}

/// <summary>
/// A bounded virtual full-duplex local audio device. Playout bytes loop back to
/// capture through the same production S11 lifecycle boundary. It advertises
/// virtual-device evidence only and never claims physical microphone/speaker I/O.
/// </summary>
internal sealed class LocalLoopbackAudioDeviceLeaf : ITransportLifecycleEffectPort, IAsyncDisposable
{
    private readonly Channel<LocalAudioDeviceFrame> _frames;
    private readonly object _gate = new();
    private bool _bound;
    private bool _active;
    private bool _stopped;
    private ulong _nextSequence;

    internal LocalLoopbackAudioDeviceLeaf(LocalAudioDeviceDescriptor descriptor)
    {
        Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
        _frames = Channel.CreateBounded<LocalAudioDeviceFrame>(new BoundedChannelOptions(descriptor.FrameCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });
    }

    internal LocalAudioDeviceDescriptor Descriptor { get; }

    public ValueTask<TransportAdapterEffectResult> ApplyAsync(
        TransportLifecycleCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            switch (command)
            {
                case TransportLifecycleCommand.Bind when !_bound && !_stopped:
                    _bound = true;
                    break;
                case TransportLifecycleCommand.Start when _bound && !_active && !_stopped:
                    _active = true;
                    break;
                case TransportLifecycleCommand.Stop when _active && !_stopped:
                    _active = false;
                    _stopped = true;
                    _frames.Writer.TryComplete();
                    break;
                default:
                    return ValueTask.FromResult<TransportAdapterEffectResult>(
                        new TransportAdapterEffectResult.Refused(new BoundedAscii("local-device-lifecycle-refused")));
            }
        }

        return ValueTask.FromResult<TransportAdapterEffectResult>(new TransportAdapterEffectResult.Completed());
    }

    internal ValueTask<LocalAudioDeviceWriteResult> WritePlayoutAsync(
        ReadOnlyMemory<byte> pcm,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (pcm.IsEmpty || pcm.Length > Descriptor.MaximumFrameBytes)
            return ValueTask.FromResult<LocalAudioDeviceWriteResult>(
                new LocalAudioDeviceWriteResult.Refused(new BoundedAscii("local-device-frame-invalid")));

        lock (_gate)
        {
            if (!_active || _stopped)
                return ValueTask.FromResult<LocalAudioDeviceWriteResult>(
                    new LocalAudioDeviceWriteResult.Refused(new BoundedAscii("local-device-not-active")));

            var sequence = checked(++_nextSequence);
            var frame = new LocalAudioDeviceFrame(sequence, pcm.Span);
            if (!_frames.Writer.TryWrite(frame))
                return ValueTask.FromResult<LocalAudioDeviceWriteResult>(
                    new LocalAudioDeviceWriteResult.Refused(new BoundedAscii("local-device-capacity-refused")));

            return ValueTask.FromResult<LocalAudioDeviceWriteResult>(
                new LocalAudioDeviceWriteResult.Accepted(sequence));
        }
    }

    internal async ValueTask<LocalAudioDeviceReadResult> ReadCaptureAsync(
        CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (!_active && !_stopped)
                return new LocalAudioDeviceReadResult.Refused(new BoundedAscii("local-device-not-active"));
        }

        try
        {
            var frame = await _frames.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            return new LocalAudioDeviceReadResult.Frame(frame);
        }
        catch (ChannelClosedException)
        {
            return new LocalAudioDeviceReadResult.End();
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            _active = false;
            _stopped = true;
            _frames.Writer.TryComplete();
        }
        return ValueTask.CompletedTask;
    }
}
