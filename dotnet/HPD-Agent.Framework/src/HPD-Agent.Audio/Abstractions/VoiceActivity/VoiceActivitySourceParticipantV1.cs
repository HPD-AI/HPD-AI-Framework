using HPD.Agent.Authority;
using HPD.Agent.Audio.ProviderContracts.VoiceActivity;
using HPD.Agent.Runtime;

namespace HPD.Agent.Audio.VoiceActivity;

internal delegate ValueTask<VoiceActivitySourceProductV1> VoiceActivitySourceProductFactoryV1(
    CancellationToken cancellationToken);

internal enum VoiceActivitySourceParticipantStateV1 : byte
{
    Created = 1,
    Prepared = 2,
    Started = 3,
    Drained = 4,
    Terminated = 5,
}

internal sealed class VoiceActivitySourceParticipantV1 : IRuntimeParticipantV1
{
    private readonly VoiceActivitySourceProductFactoryV1 _factory;
    private readonly VoiceActivityGraphStreamConfigurationV1? _graphConfiguration;
    private readonly IVoiceActivityDerivedResidenceCommitV1? _derivedResidence;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private VoiceActivitySourceParticipantStateV1 _state = VoiceActivitySourceParticipantStateV1.Created;
    private RuntimePreparedHandleV1? _handle;
    private VoiceActivitySourceProductV1? _product;
    private VoiceActivityTransferredWorkRegistryV1? _transferredWork;
    private VoiceActivityGraphStreamV1? _graphStream;
    private bool _disposed;

    internal VoiceActivitySourceParticipantV1(
        RuntimeParticipantDescriptorV1 descriptor,
        VoiceActivitySourceProductFactoryV1 factory,
        VoiceActivityGraphStreamConfigurationV1? graphConfiguration = null,
        IVoiceActivityDerivedResidenceCommitV1? derivedResidence = null)
    {
        Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _graphConfiguration = graphConfiguration;
        _derivedResidence = derivedResidence;
        if ((graphConfiguration is null) != (derivedResidence is null))
            throw new ArgumentException("Graph streams require one prepared derived residence.", nameof(derivedResidence));
    }

    public RuntimeParticipantDescriptorV1 Descriptor { get; }

    internal VoiceActivitySourceParticipantStateV1 State => _state;

    internal VoiceActivitySourceProductV1 StartedProduct =>
        _state == VoiceActivitySourceParticipantStateV1.Started && _product is not null
            ? _product
            : throw new InvalidOperationException("The voice activity source is not started.");

    internal VoiceActivityTransferredWorkRegistryV1 StartedTransferredWork =>
        _state == VoiceActivitySourceParticipantStateV1.Started && _transferredWork is not null
            ? _transferredWork
            : throw new InvalidOperationException("The transferred voice activity source is not started.");

    internal VoiceActivityGraphStreamV1 StartedGraphStream =>
        _state == VoiceActivitySourceParticipantStateV1.Started && _graphStream is not null
            ? _graphStream
            : throw new InvalidOperationException("The voice activity graph stream is not started.");

    public async ValueTask<RuntimeParticipantPrepareResultV1> PrepareAsync(
        RuntimeParticipantContextV1 context,
        CancellationToken cancellationToken)
    {
        if (!context.IsValid) throw new ArgumentException("A valid participant context is required.", nameof(context));
        if (cancellationToken.IsCancellationRequested)
            return PrepareResult(RuntimeParticipantDispositionV1.Cancelled, "participant-prepare-cancelled");
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_state == VoiceActivitySourceParticipantStateV1.Prepared && _handle is not null)
                return _handle.Context == context
                    ? Prepared(_handle)
                    : PrepareResult(RuntimeParticipantDispositionV1.Refused, "participant-context-conflict");
            if (_state != VoiceActivitySourceParticipantStateV1.Created)
                return PrepareResult(RuntimeParticipantDispositionV1.Refused, "participant-state-conflict");

            VoiceActivitySourceProductV1 product;
            try
            {
                product = await _factory(cancellationToken).ConfigureAwait(false)
                    ?? throw new InvalidOperationException("The source factory returned no product.");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return PrepareResult(RuntimeParticipantDispositionV1.Cancelled, "participant-prepare-cancelled");
            }
            catch
            {
                return PrepareResult(RuntimeParticipantDispositionV1.Failed, "participant-prepare-failed");
            }

            if (cancellationToken.IsCancellationRequested)
            {
                _product = product;
                try { await DisposeProductAsync().ConfigureAwait(false); }
                catch { return PrepareResult(RuntimeParticipantDispositionV1.Failed, "participant-terminate-failed"); }
                return PrepareResult(RuntimeParticipantDispositionV1.Cancelled, "participant-prepare-cancelled");
            }

            _product = product;
            _handle = new RuntimePreparedHandleV1(Descriptor.Id, context);
            _state = VoiceActivitySourceParticipantStateV1.Prepared;
            return Prepared(_handle);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<RuntimeParticipantResultV1> StartAsync(
        RuntimePreparedHandleV1 handle,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handle);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_state == VoiceActivitySourceParticipantStateV1.Started && Equals(_handle, handle))
                return Result(RuntimeParticipantDispositionV1.Succeeded, "participant-already-started");
            if (_state != VoiceActivitySourceParticipantStateV1.Prepared || !Equals(_handle, handle) || _product is null)
                return Result(RuntimeParticipantDispositionV1.Refused, "participant-start-invalid");
            if (_product is VoiceActivitySourceProductV1.Transferred transferred)
                _transferredWork = new VoiceActivityTransferredWorkRegistryV1(transferred.Source);
            if (_graphConfiguration is not null)
            {
                try { _graphStream = new VoiceActivityGraphStreamV1(_product, _graphConfiguration, _transferredWork, _derivedResidence!); }
                catch
                {
                    _transferredWork = null;
                    return Result(RuntimeParticipantDispositionV1.Failed, "participant-graph-stream-invalid");
                }
            }
            _state = VoiceActivitySourceParticipantStateV1.Started;
            return Result(RuntimeParticipantDispositionV1.Succeeded, "participant-started");
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<RuntimeParticipantResultV1> DrainAsync(
        RuntimeDrainIntentV1 intent,
        CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(intent)) throw new ArgumentOutOfRangeException(nameof(intent));
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_state == VoiceActivitySourceParticipantStateV1.Drained)
                return Result(RuntimeParticipantDispositionV1.Succeeded, "participant-already-drained");
            if (_state != VoiceActivitySourceParticipantStateV1.Started)
                return Result(RuntimeParticipantDispositionV1.Refused, "participant-drain-invalid");
            _graphStream?.Close();
            _transferredWork?.Close();
            _state = VoiceActivitySourceParticipantStateV1.Drained;
            return Result(RuntimeParticipantDispositionV1.Succeeded, "participant-drained");
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<RuntimeParticipantResultV1> TerminateAsync(
        RuntimeTerminationCauseV1 cause,
        CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(cause)) throw new ArgumentOutOfRangeException(nameof(cause));
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_state == VoiceActivitySourceParticipantStateV1.Terminated)
                return Result(RuntimeParticipantDispositionV1.Succeeded, "participant-already-terminated");
            _graphStream?.Close();
            _transferredWork?.Close();
            try
            {
                await DisposeProductAsync().ConfigureAwait(false);
            }
            catch
            {
                _state = VoiceActivitySourceParticipantStateV1.Terminated;
                return Result(RuntimeParticipantDispositionV1.Failed, "participant-terminate-failed");
            }
            _state = VoiceActivitySourceParticipantStateV1.Terminated;
            return Result(RuntimeParticipantDispositionV1.Succeeded, "participant-terminated");
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed) return;
            _disposed = true;
            try { await DisposeProductAsync().ConfigureAwait(false); }
            finally { _state = VoiceActivitySourceParticipantStateV1.Terminated; }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async ValueTask DisposeProductAsync()
    {
        object? source = _product switch
        {
            VoiceActivitySourceProductV1.BorrowedSynchronous borrowed => borrowed.Source,
            VoiceActivitySourceProductV1.Transferred transferred => transferred.Source,
            _ => null,
        };
        _graphStream?.Close();
        _graphStream = null;
        _transferredWork = null;
        _product = null;
        if (source is IAsyncDisposable asyncDisposable)
            await asyncDisposable.DisposeAsync().ConfigureAwait(false);
        else if (source is IDisposable disposable)
            disposable.Dispose();
    }

    private static RuntimeParticipantPrepareResultV1 Prepared(RuntimePreparedHandleV1 handle) =>
        new(RuntimeParticipantDispositionV1.Succeeded, new BoundedAscii("participant-prepared"), handle);

    private static RuntimeParticipantPrepareResultV1 PrepareResult(
        RuntimeParticipantDispositionV1 disposition,
        string code) => new(disposition, new BoundedAscii(code), null);

    private static RuntimeParticipantResultV1 Result(RuntimeParticipantDispositionV1 disposition, string code) =>
        new(disposition, new BoundedAscii(code));
}
