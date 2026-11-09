using HPD.Agent.Audio.Graph;
using HPD.Agent.Audio.ProviderContracts.VoiceActivity;
using HPD.Agent.Authority;
using HPD.Audio.Primitives;

namespace HPD.Agent.Audio.VoiceActivity;

internal static class VoiceActivityGraphAdapter
{
    internal static VoiceActivitySourceOutcome ObserveAssembled(
        VoiceActivitySourceProduct product,
        VoiceActivityAssembledWindow window,
        MonotonicStampV1 observedAt)
    {
        ArgumentNullException.ThrowIfNull(product);
        ArgumentNullException.ThrowIfNull(window);
        if (product is not VoiceActivitySourceProduct.BorrowedSynchronous borrowed ||
            !observedAt.IsValid || !borrowed.Source.Capabilities.Formats.Contains(window.Format))
            return Invalid(VoiceActivityInputInvalidReason.FormatMismatch);
        var borrowedWindow = new VoiceActivityBorrowedWindow(
            window.Bytes.Span, window.Format, window.Extent, observedAt);
        return borrowed.Source.Observe(in borrowedWindow)
            ?? new VoiceActivitySourceOutcome.Fault(
                VoiceActivitySourceFaultClass.ContractViolation,
                VoiceActivityStateValidity.Quarantined, VoiceActivityRetryability.Never);
    }

    internal static ValueTask<VoiceActivityTransferResult> TransferAssembledAsync(
        VoiceActivitySourceProduct product,
        OperationId operationId,
        VoiceActivityAssembledWindow window,
        MonotonicStampV1 observedAt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(product);
        ArgumentNullException.ThrowIfNull(window);
        if (product is not VoiceActivitySourceProduct.Transferred transferred ||
            !observedAt.IsValid || !transferred.Source.Capabilities.Formats.Contains(window.Format))
            return ValueTask.FromResult<VoiceActivityTransferResult>(
                Rejected(VoiceActivityInputInvalidReason.FormatMismatch));
        var ownedWindow = new VoiceActivityOwnedWindow(
            operationId, window.Bytes.Span, window.Format, window.Extent, observedAt);
        return transferred.Source.TransferAsync(ownedWindow, cancellationToken);
    }

    internal static VoiceActivitySourceOutcome ObserveBorrowed(
        VoiceActivitySourceProduct product,
        scoped in AudioFrameView frame,
        GraphMediaRange range,
        MonotonicStampV1 observedAt)
    {
        ArgumentNullException.ThrowIfNull(product);
        if (product is not VoiceActivitySourceProduct.BorrowedSynchronous borrowed)
            return Invalid(VoiceActivityInputInvalidReason.FormatMismatch);
        if (!TryMap(frame.Data, frame.Format, frame.SamplesPerChannel, frame.Duration,
                frame.RecoveryKind, frame.Flags, range, observedAt, borrowed.Source.Capabilities,
                out var format, out var extent, out var invalid))
            return Invalid(invalid);
        var window = new VoiceActivityBorrowedWindow(frame.Data, format!, extent, observedAt);
        return borrowed.Source.Observe(in window)
            ?? new VoiceActivitySourceOutcome.Fault(
                VoiceActivitySourceFaultClass.ContractViolation,
                VoiceActivityStateValidity.Quarantined, VoiceActivityRetryability.Never);
    }

    internal static async ValueTask<VoiceActivityTransferResult> TransferOwnedAsync(
        VoiceActivitySourceProduct product,
        OperationId operationId,
        OwnedAudioFrame ownedFrame,
        GraphMediaRange range,
        MonotonicStampV1 observedAt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(product);
        VoiceActivityOwnedWindow? window = null;
        VoiceActivityInputInvalidReason invalid = VoiceActivityInputInvalidReason.ExtentInvalid;
        try
        {
            if (product is not VoiceActivitySourceProduct.Transferred transferred)
                return Rejected(VoiceActivityInputInvalidReason.FormatMismatch);
            var frame = ownedFrame.Frame;
            if (!TryMap(frame.Data.Span, frame.Format, frame.SamplesPerChannel, frame.Duration,
                    frame.RecoveryKind, frame.Flags, range, observedAt, transferred.Source.Capabilities,
                    out var format, out var extent, out invalid))
                return Rejected(invalid);
            window = new VoiceActivityOwnedWindow(operationId, frame.Data.Span, format!, extent, observedAt);
        }
        finally
        {
            ownedFrame.Dispose();
        }

        return await ((VoiceActivitySourceProduct.Transferred)product).Source
            .TransferAsync(window, cancellationToken).ConfigureAwait(false);
    }

    internal static ValueTask<VoiceActivitySettlementResult> SettleAsync(
        VoiceActivitySourceProduct product,
        OperationId operationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(product);
        if (product is not VoiceActivitySourceProduct.Transferred transferred)
            return ValueTask.FromResult<VoiceActivitySettlementResult>(
                new VoiceActivitySettlementResult.NotFound(operationId));
        return transferred.Source.SettleAsync(operationId, cancellationToken);
    }

    private static bool TryMap(
        ReadOnlySpan<byte> bytes,
        AudioFormat audioFormat,
        int samplesPerChannel,
        TimeSpan duration,
        AudioRecoveryKind recovery,
        AudioFrameFlags flags,
        GraphMediaRange range,
        MonotonicStampV1 observedAt,
        VoiceActivitySourceCapabilities capabilities,
        out VoiceActivityInputFormat? format,
        out VoiceActivityMediaExtent extent,
        out VoiceActivityInputInvalidReason invalid)
    {
        format = null;
        extent = default;
        invalid = VoiceActivityInputInvalidReason.FormatMismatch;
        if (audioFormat.SampleFormat != AudioSampleFormat.Pcm16 ||
            audioFormat.SampleRate is < 8_000 or > 192_000 ||
            audioFormat.ChannelCount is < 1 or > 8 || samplesPerChannel <= 0 ||
            bytes.Length != (long)samplesPerChannel * audioFormat.ChannelCount * sizeof(short))
            return false;
        format = new VoiceActivityInputFormat(
            VoiceActivitySampleEncoding.SignedPcm16, audioFormat.SampleRate, audioFormat.ChannelCount);
        if (!capabilities.Formats.Contains(format)) return false;
        if (duration < capabilities.Window.MinimumWindow || duration > capabilities.Window.MaximumWindow)
        {
            invalid = VoiceActivityInputInvalidReason.ExtentInvalid;
            return false;
        }
        if (!range.IsValid || range.Domain != GraphTrafficDomain.Media ||
            range.Direction != GraphDirection.IngressForward || range.EncodedBytes != (ulong)bytes.Length ||
            range.Start.Value > long.MaxValue || range.EndExclusive.Value > long.MaxValue || !observedAt.IsValid)
        {
            invalid = VoiceActivityInputInvalidReason.ExtentInvalid;
            return false;
        }
        if ((flags & AudioFrameFlags.Discontinuity) != 0)
        {
            invalid = VoiceActivityInputInvalidReason.DiscontinuousWindow;
            return false;
        }
        extent = new VoiceActivityMediaExtent(range.GraphGeneration,
            checked((long)range.Start.Value), checked((long)range.EndExclusive.Value),
            recovery == AudioRecoveryKind.None && (flags & AudioFrameFlags.ClockAdjusted) == 0);
        return true;
    }

    private static VoiceActivitySourceOutcome.InvalidInput Invalid(VoiceActivityInputInvalidReason reason) => new(reason);
    private static VoiceActivityTransferResult.Rejected Rejected(VoiceActivityInputInvalidReason reason) =>
        new(new VoiceActivitySourceOutcome.InvalidInput(reason));
}
