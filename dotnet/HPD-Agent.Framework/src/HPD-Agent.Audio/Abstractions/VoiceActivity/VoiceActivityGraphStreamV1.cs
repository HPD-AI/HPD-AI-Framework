using HPD.Agent.Audio.Graph;
using HPD.Agent.Audio.ProviderContracts.VoiceActivity;
using HPD.Agent.Authority;
using HPD.Audio.Primitives;

namespace HPD.Agent.Audio.VoiceActivity;

internal sealed record VoiceActivityGraphStreamConfigurationV1(
    AudioFormat InputFormat,
    VoiceActivityInputFormatV1 OutputFormat,
    TimeSpan Window,
    int MaximumBatchSize);

internal abstract record VoiceActivityGraphStreamCompilationResultV1
{
    private VoiceActivityGraphStreamCompilationResultV1() { }
    internal sealed record Compiled(VoiceActivityGraphStreamConfigurationV1 Configuration) :
        VoiceActivityGraphStreamCompilationResultV1;
    internal sealed record Rejected(string SafeCode) : VoiceActivityGraphStreamCompilationResultV1;
}

internal static class VoiceActivityGraphStreamCompilerV1
{
    internal static VoiceActivityGraphStreamCompilationResultV1 Compile(
        VoiceActivityEffectiveSourcePlanV1 source,
        AudioFormat inputFormat)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (inputFormat.SampleFormat != AudioSampleFormat.Pcm16 ||
            inputFormat.SampleRate is < 8_000 or > 192_000 || inputFormat.ChannelCount is < 1 or > 8)
            return Reject("graph-input-format-invalid");
        var output = source.Capabilities.Formats
            .Where(static format => format.Encoding == VoiceActivitySampleEncodingV1.SignedPcm16 && format.Channels == 1)
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
        return new VoiceActivityGraphStreamCompilationResultV1.Compiled(
            new VoiceActivityGraphStreamConfigurationV1(
                inputFormat, output, window, source.Capabilities.Window.MaximumBatchSize));
    }

    private static VoiceActivityGraphStreamCompilationResultV1.Rejected Reject(string code) => new(code);
}

internal sealed class VoiceActivityGraphStreamV1
{
    private readonly VoiceActivitySourceProductV1 _product;
    private VoiceActivityPcm16WindowAssemblerV1 _assembler;
    private readonly IVoiceActivityDerivedResidenceCommitV1 _derivedResidence;
    private readonly VoiceActivityTransferredWorkRegistryV1? _transferredWork;
    private bool _closed;

    internal VoiceActivityGraphStreamV1(
        VoiceActivitySourceProductV1 product,
        VoiceActivityGraphStreamConfigurationV1 configuration,
        VoiceActivityTransferredWorkRegistryV1? transferredWork,
        IVoiceActivityDerivedResidenceCommitV1 derivedResidence)
    {
        _product = product ?? throw new ArgumentNullException(nameof(product));
        ArgumentNullException.ThrowIfNull(configuration);
        var capabilities = product switch
        {
            VoiceActivitySourceProductV1.BorrowedSynchronous borrowed => borrowed.Source.Capabilities,
            VoiceActivitySourceProductV1.Transferred transferred => transferred.Source.Capabilities,
            _ => throw new ArgumentException("Opaque sources do not consume graph PCM.", nameof(product)),
        };
        if (!capabilities.Formats.Contains(configuration.OutputFormat))
            throw new ArgumentException("The graph stream output is not supported by the source.", nameof(configuration));
        if (configuration.Window < capabilities.Window.MinimumWindow ||
            configuration.Window > capabilities.Window.MaximumWindow ||
            configuration.MaximumBatchSize < 1 ||
            configuration.MaximumBatchSize > capabilities.Window.MaximumBatchSize)
            throw new ArgumentException("The graph stream window exceeds the created source capability.", nameof(configuration));
        if ((product is VoiceActivitySourceProductV1.Transferred) != (transferredWork is not null))
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
        _assembler = new VoiceActivityPcm16WindowAssemblerV1(
            configuration.InputFormat, configuration.OutputFormat,
            configuration.Window, configuration.MaximumBatchSize);
    }

    internal VoiceActivityWindowAssemblyResultV1 AssembleBorrowed(
        scoped in AudioFrameView frame,
        GraphMediaRangeV1 range)
    {
        ThrowIfClosed();
        var candidate = _assembler.Fork();
        var result = candidate.Process(frame.Data, frame.Format, frame.SamplesPerChannel,
            frame.RecoveryKind, frame.Flags, range);
        return Adopt(candidate, result);
    }

    internal VoiceActivityWindowAssemblyResultV1 AssembleOwned(
        OwnedAudioFrame ownedFrame,
        GraphMediaRangeV1 range)
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

    internal VoiceActivitySourceOutcomeV1 Observe(
        VoiceActivityAssembledWindowV1 window,
        MonotonicStampV1 observedAt)
    {
        ThrowIfClosed();
        return VoiceActivityGraphAdapterV1.ObserveAssembled(_product, window, observedAt);
    }

    internal ValueTask<VoiceActivityTransferResultV1> TransferAsync(
        OperationId operationId,
        VoiceActivityAssembledWindowV1 window,
        MonotonicStampV1 observedAt,
        CancellationToken cancellationToken)
    {
        ThrowIfClosed();
        if (_transferredWork is null)
            return ValueTask.FromResult<VoiceActivityTransferResultV1>(new VoiceActivityTransferResultV1.Rejected(
                new VoiceActivitySourceOutcomeV1.InvalidInput(VoiceActivityInputInvalidReasonV1.FormatMismatch)));
        var ownedWindow = new VoiceActivityOwnedWindowV1(
            operationId, window.Bytes.Span, window.Format, window.Extent, observedAt);
        return _transferredWork.TransferAsync(ownedWindow, cancellationToken);
    }

    internal ValueTask<VoiceActivitySettlementResultV1> SettleAsync(
        OperationId operationId,
        CancellationToken cancellationToken)
    {
        ThrowIfClosed(settlement: true);
        return _transferredWork is null
            ? ValueTask.FromResult<VoiceActivitySettlementResultV1>(
                new VoiceActivitySettlementResultV1.NotFound(operationId))
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

    private VoiceActivityWindowAssemblyResultV1 Adopt(
        VoiceActivityPcm16WindowAssemblerV1 candidate,
        VoiceActivityWindowAssemblyResultV1 result)
    {
        if (result is VoiceActivityWindowAssemblyResultV1.Rejected) return result;
        if (!_derivedResidence.TryCommit())
            return new VoiceActivityWindowAssemblyResultV1.Rejected(VoiceActivityInputInvalidReasonV1.ExtentInvalid);
        _assembler = candidate;
        return result;
    }
}
