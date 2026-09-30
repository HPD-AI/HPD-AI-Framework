using HPD.Audio.Primitives;

namespace HPD.Agent.Audio.LiveKit;

internal enum LiveKitSessionStopDisposition : byte
{
    Stopped = 1,
    AlreadyStopped = 2,
    OutcomeUnknown = 3,
    Quarantined = 4
}

internal readonly record struct LiveKitSessionStopProjection(
    LiveKitSessionStopDisposition Disposition,
    string? OperationId,
    string? SafeCode);

internal interface ILiveKitSessionControlPort : ILiveKitAudioCapturePort
{
    ValueTask UnpublishAsync(CancellationToken cancellationToken);
    ValueTask DisconnectAsync(CancellationToken cancellationToken);
    ValueTask<bool> ReconcileStopAsync(string operationId, CancellationToken cancellationToken);
}

internal sealed class LiveKitRoomMediaOwner : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly List<LiveKitNativeHandleOwner> _releaseOrder;
    private readonly List<IDisposable> _registrations = [];
    private int _released;

    internal LiveKitRoomMediaOwner(
        LiveKitNativeHandleOwner room,
        LiveKitNativeHandleOwner participant,
        LiveKitNativeHandleOwner? stream,
        LiveKitNativeHandleOwner? source,
        LiveKitNativeHandleOwner? track,
        LiveKitNativeHandleOwner? publication)
    {
        _releaseOrder = [.. new[] { publication, track, source, stream, participant, room }.Where(static owner => owner is not null).Cast<LiveKitNativeHandleOwner>()];
    }

    internal bool IsReleased => Volatile.Read(ref _released) != 0;

    internal void AddDynamic(LiveKitNativeHandleOwner owner, IDisposable registration)
    {
        ArgumentNullException.ThrowIfNull(owner); ArgumentNullException.ThrowIfNull(registration);
        lock (_gate)
        {
            if (_released != 0) throw new ObjectDisposedException(nameof(LiveKitRoomMediaOwner));
            _releaseOrder.Insert(0, owner);
            _registrations.Add(registration);
        }
    }

    internal void AddRegistration(IDisposable registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        lock (_gate)
        {
            if (_released != 0) throw new ObjectDisposedException(nameof(LiveKitRoomMediaOwner));
            _registrations.Add(registration);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _released, 1) != 0) return;
        IDisposable[] registrations;
        LiveKitNativeHandleOwner[] owners;
        lock (_gate) { registrations = [.. _registrations]; owners = [.. _releaseOrder]; }
        for (var index = registrations.Length - 1; index >= 0; index--) registrations[index].Dispose();
        foreach (var owner in owners) await owner.DisposeAsync().ConfigureAwait(false);
    }
}

internal sealed class LiveKitTransportSessionRuntime : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly ILiveKitSessionControlPort _control;
    private readonly LiveKitRoomMediaOwner _owners;
    private readonly LiveKitSessionGenerationFence _fence;
    private readonly long _generation;
    private LiveKitSessionStopProjection? _terminal;
    private string? _unknownOperation;
    private bool _stopInFlight;
    private bool _disposed;
    private ulong _selectedInboundStream;

    internal LiveKitTransportSessionRuntime(
        string audioSessionId,
        AudioFormat format,
        ILiveKitSessionControlPort control,
        LiveKitRoomMediaOwner owners,
        LiveKitSessionGenerationFence fence,
        int inboundCapacity = 64)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(audioSessionId);
        AudioSessionId = audioSessionId;
        _control = control;
        _owners = owners;
        _fence = fence;
        Inbound = new(format, inboundCapacity);
        Outbound = new(format, control);
        _generation = fence.Replace(Quarantine);
    }

    internal string AudioSessionId { get; }
    internal LiveKitInboundAudioSource Inbound { get; }
    internal LiveKitOutboundAudioSink Outbound { get; }

    internal void SelectInboundStream(ulong streamHandle)
    {
        if (streamHandle == 0) throw new ArgumentOutOfRangeException(nameof(streamHandle));
        EnsureCurrent();
        lock (_gate) _selectedInboundStream = streamHandle;
    }

    internal void AdmitInbound(ulong streamHandle, ReadOnlySpan<byte> pcm, int channels, int sampleRate, int samplesPerChannel, long sequence)
    {
        EnsureCurrent();
        lock (_gate) if (_selectedInboundStream != streamHandle) return;
        if (channels != Inbound.Format.ChannelCount || sampleRate != Inbound.Format.SampleRate)
        {
            Quarantine("transport-inbound-format-mismatch");
            throw new AudioSourceException(AudioStreamErrorKind.FormatMismatch, "Inbound LiveKit PCM format changed after admission.");
        }
        try { Inbound.AdmitCopiedPcm(pcm, samplesPerChannel, sequence); }
        catch (AudioSourceException) { Quarantine("transport-inbound-overflow"); throw; }
    }

    internal void CompleteInbound(ulong streamHandle)
    {
        EnsureCurrent();
        lock (_gate) if (_selectedInboundStream == streamHandle) _selectedInboundStream = 0;
    }

    internal async ValueTask<LiveKitSessionStopProjection> StopAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_terminal is { } terminal) return terminal.Disposition == LiveKitSessionStopDisposition.Stopped
                ? new(LiveKitSessionStopDisposition.AlreadyStopped, null, null)
                : terminal;
            if (_unknownOperation is not null) return new(LiveKitSessionStopDisposition.OutcomeUnknown, _unknownOperation, null);
            if (_stopInFlight) throw new InvalidOperationException("LiveKit stop is already in flight.");
            _stopInFlight = true;
        }

        var operationId = $"livekit-stop:{AudioSessionId}:{_generation}";
        try
        {
            EnsureCurrent();
            await _control.ClearAsync(cancellationToken).ConfigureAwait(false);
            await _control.UnpublishAsync(cancellationToken).ConfigureAwait(false);
            await _control.DisconnectAsync(cancellationToken).ConfigureAwait(false);
            await CompleteStopAsync().ConfigureAwait(false);
            return SetTerminal(new(LiveKitSessionStopDisposition.Stopped, null, null));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            lock (_gate) { _stopInFlight = false; _unknownOperation = operationId; }
            return new(LiveKitSessionStopDisposition.OutcomeUnknown, operationId, null);
        }
        catch
        {
            lock (_gate) { _stopInFlight = false; _unknownOperation = operationId; }
            return new(LiveKitSessionStopDisposition.OutcomeUnknown, operationId, null);
        }
    }

    internal async ValueTask<LiveKitSessionStopProjection> ReconcileStopAsync(
        string operationId,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_unknownOperation != operationId) throw new InvalidOperationException("Operation is not the retained unknown stop.");
        }
        if (!await _control.ReconcileStopAsync(operationId, cancellationToken).ConfigureAwait(false))
            return new(LiveKitSessionStopDisposition.OutcomeUnknown, operationId, null);
        await CompleteStopAsync().ConfigureAwait(false);
        return SetTerminal(new(LiveKitSessionStopDisposition.Stopped, null, null));
    }

    internal void Quarantine(string safeCode)
    {
        lock (_gate)
        {
            if (_terminal is not null) return;
            _terminal = new(LiveKitSessionStopDisposition.Quarantined, null, safeCode);
            _stopInFlight = false;
            _unknownOperation = null;
        }
        Inbound.Fail();
    }

    private async ValueTask CompleteStopAsync()
    {
        Inbound.Complete();
        await Outbound.CompleteAsync(CancellationToken.None).ConfigureAwait(false);
        await _owners.DisposeAsync().ConfigureAwait(false);
        _fence.Release(_generation);
    }

    private LiveKitSessionStopProjection SetTerminal(LiveKitSessionStopProjection terminal)
    {
        lock (_gate) { _terminal = terminal; _unknownOperation = null; _stopInFlight = false; return terminal; }
    }

    private void EnsureCurrent()
    {
        if (!_fence.IsCurrent(_generation))
        {
            Quarantine("stale-session-generation");
            throw new InvalidOperationException("LiveKit session generation is stale.");
        }
        lock (_gate) if (_terminal is { Disposition: LiveKitSessionStopDisposition.Quarantined, SafeCode: var code })
            throw new InvalidDataException($"LiveKit session is quarantined: {code}.");
    }

    public async ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_disposed) return;
            if (_terminal is not { Disposition: LiveKitSessionStopDisposition.Stopped or LiveKitSessionStopDisposition.Quarantined })
                throw new InvalidOperationException("LiveKit session disposal is legal only after a terminal stop or quarantine.");
            _disposed = true;
        }
        if (_terminal is { Disposition: LiveKitSessionStopDisposition.Quarantined } && !_owners.IsReleased)
            await _owners.DisposeAsync().ConfigureAwait(false);
        await Inbound.DisposeAsync().ConfigureAwait(false);
        await Outbound.DisposeAsync().ConfigureAwait(false);
    }
}

