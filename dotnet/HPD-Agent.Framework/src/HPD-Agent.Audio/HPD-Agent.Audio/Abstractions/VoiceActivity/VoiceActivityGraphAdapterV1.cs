using HPD.Agent.Audio.Graph;
using HPD.Agent.Audio.ProviderContracts.VoiceActivity;
using HPD.Agent.Authority;
using HPD.Audio.Primitives;

namespace HPD.Agent.Audio.VoiceActivity;

internal static class VoiceActivityGraphAdapterV1
{
    internal static VoiceActivitySourceOutcomeV1 ObserveAssembled(
        VoiceActivitySourceProductV1 product,
        VoiceActivityAssembledWindowV1 window,
        MonotonicStampV1 observedAt)
    {
        ArgumentNullException.ThrowIfNull(product);
        ArgumentNullException.ThrowIfNull(window);
        if (product is not VoiceActivitySourceProductV1.BorrowedSynchronous borrowed ||
            !observedAt.IsValid || !borrowed.Source.Capabilities.Formats.Contains(window.Format))
            return Invalid(VoiceActivityInputInvalidReasonV1.FormatMismatch);
        var borrowedWindow = new VoiceActivityBorrowedWindowV1(
            window.Bytes.Span, window.Format, window.Extent, observedAt);
        return borrowed.Source.Observe(in borrowedWindow)
            ?? new VoiceActivitySourceOutcomeV1.Fault(
                VoiceActivitySourceFaultClassV1.ContractViolation,
                VoiceActivityStateValidityV1.Quarantined, VoiceActivityRetryabilityV1.Never);
    }

    internal static ValueTask<VoiceActivityTransferResultV1> TransferAssembledAsync(
        VoiceActivitySourceProductV1 product,
        OperationId operationId,
        VoiceActivityAssembledWindowV1 window,
        MonotonicStampV1 observedAt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(product);
        ArgumentNullException.ThrowIfNull(window);
        if (product is not VoiceActivitySourceProductV1.Transferred transferred ||
            !observedAt.IsValid || !transferred.Source.Capabilities.Formats.Contains(window.Format))
            return ValueTask.FromResult<VoiceActivityTransferResultV1>(
                Rejected(VoiceActivityInputInvalidReasonV1.FormatMismatch));
        var ownedWindow = new VoiceActivityOwnedWindowV1(
            operationId, window.Bytes.Span, window.Format, window.Extent, observedAt);
        return transferred.Source.TransferAsync(ownedWindow, cancellationToken);
    }

    internal static VoiceActivitySourceOutcomeV1 ObserveBorrowed(
        VoiceActivitySourceProductV1 product,
        scoped in AudioFrameView frame,
        GraphMediaRangeV1 range,
        MonotonicStampV1 observedAt)
    {
        ArgumentNullException.ThrowIfNull(product);
        if (product is not VoiceActivitySourceProductV1.BorrowedSynchronous borrowed)
            return Invalid(VoiceActivityInputInvalidReasonV1.FormatMismatch);
        if (!TryMap(frame.Data, frame.Format, frame.SamplesPerChannel, frame.Duration,
                frame.RecoveryKind, frame.Flags, range, observedAt, borrowed.Source.Capabilities,
                out var format, out var extent, out var invalid))
            return Invalid(invalid);
        var window = new VoiceActivityBorrowedWindowV1(frame.Data, format!, extent, observedAt);
        return borrowed.Source.Observe(in window)
            ?? new VoiceActivitySourceOutcomeV1.Fault(
                VoiceActivitySourceFaultClassV1.ContractViolation,
                VoiceActivityStateValidityV1.Quarantined, VoiceActivityRetryabilityV1.Never);
    }

    internal static async ValueTask<VoiceActivityTransferResultV1> TransferOwnedAsync(
        VoiceActivitySourceProductV1 product,
        OperationId operationId,
        OwnedAudioFrame ownedFrame,
        GraphMediaRangeV1 range,
        MonotonicStampV1 observedAt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(product);
        VoiceActivityOwnedWindowV1? window = null;
        VoiceActivityInputInvalidReasonV1 invalid = VoiceActivityInputInvalidReasonV1.ExtentInvalid;
        try
        {
            if (product is not VoiceActivitySourceProductV1.Transferred transferred)
                return Rejected(VoiceActivityInputInvalidReasonV1.FormatMismatch);
            var frame = ownedFrame.Frame;
            if (!TryMap(frame.Data.Span, frame.Format, frame.SamplesPerChannel, frame.Duration,
                    frame.RecoveryKind, frame.Flags, range, observedAt, transferred.Source.Capabilities,
                    out var format, out var extent, out invalid))
                return Rejected(invalid);
            window = new VoiceActivityOwnedWindowV1(operationId, frame.Data.Span, format!, extent, observedAt);
        }
        finally
        {
            ownedFrame.Dispose();
        }

        return await ((VoiceActivitySourceProductV1.Transferred)product).Source
            .TransferAsync(window, cancellationToken).ConfigureAwait(false);
    }

    internal static ValueTask<VoiceActivitySettlementResultV1> SettleAsync(
        VoiceActivitySourceProductV1 product,
        OperationId operationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(product);
        if (product is not VoiceActivitySourceProductV1.Transferred transferred)
            return ValueTask.FromResult<VoiceActivitySettlementResultV1>(
                new VoiceActivitySettlementResultV1.NotFound(operationId));
        return transferred.Source.SettleAsync(operationId, cancellationToken);
    }

    private static bool TryMap(
        ReadOnlySpan<byte> bytes,
        AudioFormat audioFormat,
        int samplesPerChannel,
        TimeSpan duration,
        AudioRecoveryKind recovery,
        AudioFrameFlags flags,
        GraphMediaRangeV1 range,
        MonotonicStampV1 observedAt,
        VoiceActivitySourceCapabilitiesV1 capabilities,
        out VoiceActivityInputFormatV1? format,
        out VoiceActivityMediaExtentV1 extent,
        out VoiceActivityInputInvalidReasonV1 invalid)
    {
        format = null;
        extent = default;
        invalid = VoiceActivityInputInvalidReasonV1.FormatMismatch;
        if (audioFormat.SampleFormat != AudioSampleFormat.Pcm16 ||
            audioFormat.SampleRate is < 8_000 or > 192_000 ||
            audioFormat.ChannelCount is < 1 or > 8 || samplesPerChannel <= 0 ||
            bytes.Length != (long)samplesPerChannel * audioFormat.ChannelCount * sizeof(short))
            return false;
        format = new VoiceActivityInputFormatV1(
            VoiceActivitySampleEncodingV1.SignedPcm16, audioFormat.SampleRate, audioFormat.ChannelCount);
        if (!capabilities.Formats.Contains(format)) return false;
        if (duration < capabilities.Window.MinimumWindow || duration > capabilities.Window.MaximumWindow)
        {
            invalid = VoiceActivityInputInvalidReasonV1.ExtentInvalid;
            return false;
        }
        if (!range.IsValid || range.Domain != GraphTrafficDomainV1.Media ||
            range.Direction != GraphDirectionV1.IngressForward || range.EncodedBytes != (ulong)bytes.Length ||
            range.Start.Value > long.MaxValue || range.EndExclusive.Value > long.MaxValue || !observedAt.IsValid)
        {
            invalid = VoiceActivityInputInvalidReasonV1.ExtentInvalid;
            return false;
        }
        if ((flags & AudioFrameFlags.Discontinuity) != 0)
        {
            invalid = VoiceActivityInputInvalidReasonV1.DiscontinuousWindow;
            return false;
        }
        extent = new VoiceActivityMediaExtentV1(range.GraphGeneration,
            checked((long)range.Start.Value), checked((long)range.EndExclusive.Value),
            recovery == AudioRecoveryKind.None && (flags & AudioFrameFlags.ClockAdjusted) == 0);
        return true;
    }

    private static VoiceActivitySourceOutcomeV1.InvalidInput Invalid(VoiceActivityInputInvalidReasonV1 reason) => new(reason);
    private static VoiceActivityTransferResultV1.Rejected Rejected(VoiceActivityInputInvalidReasonV1 reason) =>
        new(new VoiceActivitySourceOutcomeV1.InvalidInput(reason));
}
