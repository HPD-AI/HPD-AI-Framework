using HPD.Agent.Audio.Graph;
using HPD.Agent.Audio.ProviderContracts.VoiceActivity;
using HPD.Agent.Authority;
using HPD.Audio.Primitives;

namespace HPD.Agent.Audio.VoiceActivity;

internal sealed record VoiceActivityGraphStreamConfiguration(
    AudioFormat InputFormat,
    VoiceActivityInputFormat OutputFormat,
    TimeSpan Window,
    int MaximumBatchSize);

internal abstract record VoiceActivityGraphStreamCompilationResult
{
    private VoiceActivityGraphStreamCompilationResult() { }
    internal sealed record Compiled(VoiceActivityGraphStreamConfiguration Configuration) :
        VoiceActivityGraphStreamCompilationResult;
    internal sealed record Rejected(string SafeCode) : VoiceActivityGraphStreamCompilationResult;
}

internal static class VoiceActivityGraphStreamCompiler
{
    internal static VoiceActivityGraphStreamCompilationResult Compile(
        VoiceActivityEffectiveSourcePlan source,
        AudioFormat inputFormat)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (inputFormat.SampleFormat != AudioSampleFormat.Pcm16 ||
            inputFormat.SampleRate is < 8_000 or > 192_000 || inputFormat.ChannelCount is < 1 or > 8)
            return Reject("graph-input-format-invalid");
        var output = source.Capabilities.Formats
            .Where(static format => format.Encoding == VoiceActivitySampleEncoding.SignedPcm16 && format.Channels == 1)
            .OrderBy(format => format.SampleRate == inputFormat.SampleRate ? 0 : 1)
            .ThenBy(format => Math.Abs((long)format.SampleRate - inputFormat.SampleRate))
            .ThenBy(static format => format.SampleRate)
            .FirstOrDefault();
        if (output is null) return Reject("source-format-conversion-unsupported");
        var window = source.Capabilities.Window.MinimumWindow >= source.Capabilities.Window.Stride
            ? source.Capabilities.Window.MinimumWindow : source.Capabilities.Window.Stride;
        if (window > source.EffectiveMaximumWindow)
            return Reject("source-window-stride-unsupported");
        if ((long)output.SampleRate * window.Ticks % TimeSpan.TicksPerSecond != 0)
            return Reject("source-window-nonintegral");
        return new VoiceActivityGraphStreamCompilationResult.Compiled(
            new VoiceActivityGraphStreamConfiguration(
                inputFormat, output, window, source.Capabilities.Window.MaximumBatchSize));
    }

    private static VoiceActivityGraphStreamCompilationResult.Rejected Reject(string code) => new(code);
}

internal sealed class VoiceActivityGraphStream
{
    private readonly VoiceActivitySourceProduct _product;
    private VoiceActivityPcm16WindowAssembler _assembler;
    private readonly IVoiceActivityDerivedResidenceCommit _derivedResidence;
    private readonly VoiceActivityTransferredWorkRegistry? _transferredWork;
    private bool _closed;

    internal VoiceActivityGraphStream(
        VoiceActivitySourceProduct product,
        VoiceActivityGraphStreamConfiguration configuration,
        VoiceActivityTransferredWorkRegistry? transferredWork,
        IVoiceActivityDerivedResidenceCommit derivedResidence)
    {
        _product = product ?? throw new ArgumentNullException(nameof(product));
        ArgumentNullException.ThrowIfNull(configuration);
        var capabilities = product switch
        {
            VoiceActivitySourceProduct.BorrowedSynchronous borrowed => borrowed.Source.Capabilities,
            VoiceActivitySourceProduct.Transferred transferred => transferred.Source.Capabilities,
            _ => throw new ArgumentException("Opaque sources do not consume graph PCM.", nameof(product)),
        };
        if (!capabilities.Formats.Contains(configuration.OutputFormat))
            throw new ArgumentException("The graph stream output is not supported by the source.", nameof(configuration));
        if (configuration.Window < capabilities.Window.MinimumWindow ||
            configuration.Window > capabilities.Window.MaximumWindow ||
            configuration.MaximumBatchSize < 1 ||
            configuration.MaximumBatchSize > capabilities.Window.MaximumBatchSize)
            throw new ArgumentException("The graph stream window exceeds the created source capability.", nameof(configuration));
        if ((product is VoiceActivitySourceProduct.Transferred) != (transferredWork is not null))
            throw new ArgumentException("Transferred graph streams require their participant work registry.", nameof(transferredWork));
        _derivedResidence = derivedResidence ?? throw new ArgumentNullException(nameof(derivedResidence));
        var windowSamples = checked((long)configuration.OutputFormat.SampleRate * configuration.Window.Ticks /
            TimeSpan.TicksPerSecond);
        var requiredFrames = checked(windowSamples * (configuration.MaximumBatchSize + 1L) - 1L);
        var media = derivedResidence.DestinationMedia;
        if (media.SampleRateHz != (ulong)configuration.OutputFormat.SampleRate || media.ChannelCount != 1 ||
            media.BytesPerSample != sizeof(short) || media.FrameCount < requiredFrames ||
            media.ByteLength < checked(requiredFrames * sizeof(short)))
            throw new ArgumentException("The derived residence does not cover the bounded conversion buffer.", nameof(derivedResidence));
        _transferredWork = transferredWork;
        _assembler = new VoiceActivityPcm16WindowAssembler(
            configuration.InputFormat, configuration.OutputFormat,
            configuration.Window, configuration.MaximumBatchSize);
    }

    internal VoiceActivityWindowAssemblyResult AssembleBorrowed(
        scoped in AudioFrameView frame,
        GraphMediaRange range)
    {
        ThrowIfClosed();
        var candidate = _assembler.Fork();
        var result = candidate.Process(frame.Data, frame.Format, frame.SamplesPerChannel,
            frame.RecoveryKind, frame.Flags, range);
        return Adopt(candidate, result);
    }

    internal VoiceActivityWindowAssemblyResult AssembleOwned(
        OwnedAudioFrame ownedFrame,
        GraphMediaRange range)
    {
        ThrowIfClosed();
        try
        {
            var frame = ownedFrame.Frame;
            var candidate = _assembler.Fork();
            var result = candidate.Process(frame.Data.Span, frame.Format, frame.SamplesPerChannel,
                frame.RecoveryKind, frame.Flags, range);
            return Adopt(candidate, result);
        }
        finally
        {
            ownedFrame.Dispose();
        }
    }

    internal VoiceActivitySourceOutcome Observe(
        VoiceActivityAssembledWindow window,
        MonotonicStampV1 observedAt)
    {
        ThrowIfClosed();
        return VoiceActivityGraphAdapter.ObserveAssembled(_product, window, observedAt);
    }

    internal ValueTask<VoiceActivityTransferResult> TransferAsync(
        OperationId operationId,
        VoiceActivityAssembledWindow window,
        MonotonicStampV1 observedAt,
        CancellationToken cancellationToken)
    {
        ThrowIfClosed();
        if (_transferredWork is null)
            return ValueTask.FromResult<VoiceActivityTransferResult>(new VoiceActivityTransferResult.Rejected(
                new VoiceActivitySourceOutcome.InvalidInput(VoiceActivityInputInvalidReason.FormatMismatch)));
        var ownedWindow = new VoiceActivityOwnedWindow(
            operationId, window.Bytes.Span, window.Format, window.Extent, observedAt);
        return _transferredWork.TransferAsync(ownedWindow, cancellationToken);
    }

    internal ValueTask<VoiceActivitySettlementResult> SettleAsync(
        OperationId operationId,
        CancellationToken cancellationToken)
    {
        ThrowIfClosed(settlement: true);
        return _transferredWork is null
            ? ValueTask.FromResult<VoiceActivitySettlementResult>(
                new VoiceActivitySettlementResult.NotFound(operationId))
            : _transferredWork.SettleAsync(operationId, cancellationToken);
    }

    internal void Close()
    {
        if (_closed) return;
        _closed = true;
        _assembler.Reset();
        _transferredWork?.Close();
    }

    private void ThrowIfClosed(bool settlement = false)
    {
        if (_closed && !settlement) throw new InvalidOperationException("The voice activity graph stream is closed.");
    }

    private VoiceActivityWindowAssemblyResult Adopt(
        VoiceActivityPcm16WindowAssembler candidate,
        VoiceActivityWindowAssemblyResult result)
    {
        if (result is VoiceActivityWindowAssemblyResult.Rejected) return result;
        if (!_derivedResidence.TryCommit())
            return new VoiceActivityWindowAssemblyResult.Rejected(VoiceActivityInputInvalidReason.ExtentInvalid);
        _assembler = candidate;
        return result;
    }
}
