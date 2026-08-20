using System.Threading.Channels;
using HPD.Agent.Authority;

namespace HPD.Agent.Audio.Runtime.Transports;

internal sealed record LocalAudioDeviceDescriptorV1
{
    internal LocalAudioDeviceDescriptorV1(
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

internal sealed class LocalAudioDeviceFrameV1
{
    private readonly byte[] _pcm;

    internal LocalAudioDeviceFrameV1(ulong sequence, ReadOnlySpan<byte> pcm)
    {
        Sequence = sequence;
        _pcm = pcm.ToArray();
    }

    internal ulong Sequence { get; }
    internal ReadOnlyMemory<byte> Pcm => _pcm;
}

internal abstract record LocalAudioDeviceWriteResultV1
{
    private LocalAudioDeviceWriteResultV1() { }
    internal sealed record Accepted(ulong Sequence) : LocalAudioDeviceWriteResultV1;
    internal sealed record Refused(BoundedAscii SafeCode) : LocalAudioDeviceWriteResultV1;
}

internal abstract record LocalAudioDeviceReadResultV1
{
    private LocalAudioDeviceReadResultV1() { }
    internal sealed record Frame(LocalAudioDeviceFrameV1 Value) : LocalAudioDeviceReadResultV1;
    internal sealed record End : LocalAudioDeviceReadResultV1;
    internal sealed record Refused(BoundedAscii SafeCode) : LocalAudioDeviceReadResultV1;
}

/// <summary>
/// A bounded virtual full-duplex local audio device. Playout bytes loop back to
/// capture through the same production S11 lifecycle boundary. It advertises
/// virtual-device evidence only and never claims physical microphone/speaker I/O.
/// </summary>
internal sealed class LocalLoopbackAudioDeviceLeafV1 : ITransportLifecycleEffectPortV1, IAsyncDisposable
{
    private readonly Channel<LocalAudioDeviceFrameV1> _frames;
    private readonly object _gate = new();
    private bool _bound;
    private bool _active;
    private bool _stopped;
    private ulong _nextSequence;

    internal LocalLoopbackAudioDeviceLeafV1(LocalAudioDeviceDescriptorV1 descriptor)
    {
        Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
        _frames = Channel.CreateBounded<LocalAudioDeviceFrameV1>(new BoundedChannelOptions(descriptor.FrameCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });
    }

    internal LocalAudioDeviceDescriptorV1 Descriptor { get; }

    public ValueTask<TransportAdapterEffectResultV1> ApplyAsync(
        TransportLifecycleCommandV1 command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            switch (command)
            {
                case TransportLifecycleCommandV1.Bind when !_bound && !_stopped:
                    _bound = true;
                    break;
                case TransportLifecycleCommandV1.Start when _bound && !_active && !_stopped:
                    _active = true;
                    break;
                case TransportLifecycleCommandV1.Stop when _active && !_stopped:
                    _active = false;
                    _stopped = true;
                    _frames.Writer.TryComplete();
                    break;
                default:
                    return ValueTask.FromResult<TransportAdapterEffectResultV1>(
                        new TransportAdapterEffectResultV1.Refused(new BoundedAscii("local-device-lifecycle-refused")));
            }
        }

        return ValueTask.FromResult<TransportAdapterEffectResultV1>(new TransportAdapterEffectResultV1.Completed());
    }

    internal ValueTask<LocalAudioDeviceWriteResultV1> WritePlayoutAsync(
        ReadOnlyMemory<byte> pcm,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (pcm.IsEmpty || pcm.Length > Descriptor.MaximumFrameBytes)
            return ValueTask.FromResult<LocalAudioDeviceWriteResultV1>(
                new LocalAudioDeviceWriteResultV1.Refused(new BoundedAscii("local-device-frame-invalid")));

        lock (_gate)
        {
            if (!_active || _stopped)
                return ValueTask.FromResult<LocalAudioDeviceWriteResultV1>(
                    new LocalAudioDeviceWriteResultV1.Refused(new BoundedAscii("local-device-not-active")));

            var sequence = checked(++_nextSequence);
            var frame = new LocalAudioDeviceFrameV1(sequence, pcm.Span);
            if (!_frames.Writer.TryWrite(frame))
                return ValueTask.FromResult<LocalAudioDeviceWriteResultV1>(
                    new LocalAudioDeviceWriteResultV1.Refused(new BoundedAscii("local-device-capacity-refused")));

            return ValueTask.FromResult<LocalAudioDeviceWriteResultV1>(
                new LocalAudioDeviceWriteResultV1.Accepted(sequence));
        }
    }

    internal async ValueTask<LocalAudioDeviceReadResultV1> ReadCaptureAsync(
        CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (!_active && !_stopped)
                return new LocalAudioDeviceReadResultV1.Refused(new BoundedAscii("local-device-not-active"));
        }

        try
        {
            var frame = await _frames.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            return new LocalAudioDeviceReadResultV1.Frame(frame);
        }
        catch (ChannelClosedException)
        {
            return new LocalAudioDeviceReadResultV1.End();
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
