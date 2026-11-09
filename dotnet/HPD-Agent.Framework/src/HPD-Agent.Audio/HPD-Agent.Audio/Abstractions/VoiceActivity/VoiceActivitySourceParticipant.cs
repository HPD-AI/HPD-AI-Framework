using HPD.Agent.Authority;
using HPD.Agent.Audio.ProviderContracts.VoiceActivity;
using HPD.Agent.Runtime;

namespace HPD.Agent.Audio.VoiceActivity;

internal delegate ValueTask<VoiceActivitySourceProduct> VoiceActivitySourceProductFactory(
    CancellationToken cancellationToken);

internal enum VoiceActivitySourceParticipantState : byte
{
    Created = 1,
    Prepared = 2,
    Started = 3,
    Drained = 4,
    Terminated = 5,
}

internal sealed class VoiceActivitySourceParticipant : IRuntimeParticipantV1
{
    private readonly VoiceActivitySourceProductFactory _factory;
    private readonly VoiceActivityGraphStreamConfiguration? _graphConfiguration;
    private readonly IVoiceActivityDerivedResidenceCommit? _derivedResidence;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private VoiceActivitySourceParticipantState _state = VoiceActivitySourceParticipantState.Created;
    private RuntimePreparedHandleV1? _handle;
    private VoiceActivitySourceProduct? _product;
    private VoiceActivityTransferredWorkRegistry? _transferredWork;
    private VoiceActivityGraphStream? _graphStream;
    private bool _disposed;

    internal VoiceActivitySourceParticipant(
        RuntimeParticipantDescriptorV1 descriptor,
        VoiceActivitySourceProductFactory factory,
        VoiceActivityGraphStreamConfiguration? graphConfiguration = null,
        IVoiceActivityDerivedResidenceCommit? derivedResidence = null)
    {
        Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _graphConfiguration = graphConfiguration;
        _derivedResidence = derivedResidence;
        if ((graphConfiguration is null) != (derivedResidence is null))
            throw new ArgumentException("Graph streams require one prepared derived residence.", nameof(derivedResidence));
    }

    public RuntimeParticipantDescriptorV1 Descriptor { get; }

    internal VoiceActivitySourceParticipantState State => _state;

    internal VoiceActivitySourceProduct StartedProduct =>
        _state == VoiceActivitySourceParticipantState.Started && _product is not null
            ? _product
            : throw new InvalidOperationException("The voice activity source is not started.");

    internal VoiceActivityTransferredWorkRegistry StartedTransferredWork =>
        _state == VoiceActivitySourceParticipantState.Started && _transferredWork is not null
            ? _transferredWork
            : throw new InvalidOperationException("The transferred voice activity source is not started.");

    internal VoiceActivityGraphStream StartedGraphStream =>
        _state == VoiceActivitySourceParticipantState.Started && _graphStream is not null
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
            if (_state == VoiceActivitySourceParticipantState.Prepared && _handle is not null)
                return _handle.Context == context
                    ? Prepared(_handle)
                    : PrepareResult(RuntimeParticipantDispositionV1.Refused, "participant-context-conflict");
            if (_state != VoiceActivitySourceParticipantState.Created)
                return PrepareResult(RuntimeParticipantDispositionV1.Refused, "participant-state-conflict");

            VoiceActivitySourceProduct product;
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
            _state = VoiceActivitySourceParticipantState.Prepared;
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
            if (_state == VoiceActivitySourceParticipantState.Started && Equals(_handle, handle))
                return Result(RuntimeParticipantDispositionV1.Succeeded, "participant-already-started");
            if (_state != VoiceActivitySourceParticipantState.Prepared || !Equals(_handle, handle) || _product is null)
                return Result(RuntimeParticipantDispositionV1.Refused, "participant-start-invalid");
            if (_product is VoiceActivitySourceProduct.Transferred transferred)
                _transferredWork = new VoiceActivityTransferredWorkRegistry(transferred.Source);
            if (_graphConfiguration is not null)
            {
                try { _graphStream = new VoiceActivityGraphStream(_product, _graphConfiguration, _transferredWork, _derivedResidence!); }
                catch
                {
                    _transferredWork = null;
                    return Result(RuntimeParticipantDispositionV1.Failed, "participant-graph-stream-invalid");
                }
            }
            _state = VoiceActivitySourceParticipantState.Started;
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
            if (_state == VoiceActivitySourceParticipantState.Drained)
                return Result(RuntimeParticipantDispositionV1.Succeeded, "participant-already-drained");
            if (_state != VoiceActivitySourceParticipantState.Started)
                return Result(RuntimeParticipantDispositionV1.Refused, "participant-drain-invalid");
            _graphStream?.Close();
            _transferredWork?.Close();
            _state = VoiceActivitySourceParticipantState.Drained;
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
            if (_state == VoiceActivitySourceParticipantState.Terminated)
                return Result(RuntimeParticipantDispositionV1.Succeeded, "participant-already-terminated");
            _graphStream?.Close();
            _transferredWork?.Close();
            try
            {
                await DisposeProductAsync().ConfigureAwait(false);
            }
            catch
            {
                _state = VoiceActivitySourceParticipantState.Terminated;
                return Result(RuntimeParticipantDispositionV1.Failed, "participant-terminate-failed");
            }
            _state = VoiceActivitySourceParticipantState.Terminated;
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
            finally { _state = VoiceActivitySourceParticipantState.Terminated; }
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
            VoiceActivitySourceProduct.BorrowedSynchronous borrowed => borrowed.Source,
            VoiceActivitySourceProduct.Transferred transferred => transferred.Source,
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
