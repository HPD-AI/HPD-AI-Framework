using HPD.Agent.Authority;

namespace HPD.Agent.Audio.VoiceActivity;

internal readonly ref struct VoiceActivityBorrowedWindowV1
{
    internal VoiceActivityBorrowedWindowV1(
        ReadOnlySpan<byte> bytes,
        VoiceActivityInputFormatV1 format,
        VoiceActivityMediaExtentV1 extent,
        MonotonicStampV1 observedAt)
    {
        if (bytes.IsEmpty) throw new ArgumentException("A borrowed window must be nonempty.", nameof(bytes));
        ArgumentNullException.ThrowIfNull(format);
        if (format.Encoding == VoiceActivitySampleEncodingV1.ProviderOpaque)
            throw new ArgumentException("Borrowed graph media must have decoded geometry.", nameof(format));
        if (!observedAt.IsValid) throw new ArgumentException("An observation time is required.", nameof(observedAt));
        Bytes = bytes;
        Format = format;
        Extent = extent;
        ObservedAt = observedAt;
    }

    internal ReadOnlySpan<byte> Bytes { get; }
    internal VoiceActivityInputFormatV1 Format { get; }
    internal VoiceActivityMediaExtentV1 Extent { get; }
    internal MonotonicStampV1 ObservedAt { get; }
}

internal sealed record VoiceActivityOwnedWindowV1
{
    private readonly byte[] _bytes;

    internal VoiceActivityOwnedWindowV1(
        OperationId operationId,
        ReadOnlySpan<byte> bytes,
        VoiceActivityInputFormatV1 format,
        VoiceActivityMediaExtentV1 extent,
        MonotonicStampV1 observedAt)
    {
        if (!operationId.IsValid) throw new ArgumentException("An operation identity is required.", nameof(operationId));
        if (bytes.IsEmpty) throw new ArgumentException("An owned window must be nonempty.", nameof(bytes));
        ArgumentNullException.ThrowIfNull(format);
        if (!observedAt.IsValid) throw new ArgumentException("An observation time is required.", nameof(observedAt));
        OperationId = operationId;
        _bytes = bytes.ToArray();
        Format = format;
        Extent = extent;
        ObservedAt = observedAt;
    }

    internal OperationId OperationId { get; }
    internal ReadOnlyMemory<byte> Bytes => _bytes.ToArray();
    internal VoiceActivityInputFormatV1 Format { get; }
    internal VoiceActivityMediaExtentV1 Extent { get; }
    internal MonotonicStampV1 ObservedAt { get; }
}

internal interface IBorrowedSynchronousVoiceActivitySourceV1
{
    VoiceActivitySourceCapabilitiesV1 Capabilities { get; }
    VoiceActivitySourceOutcomeV1 Observe(scoped in VoiceActivityBorrowedWindowV1 window);
}

internal interface ITransferredVoiceActivitySourceV1
{
    VoiceActivitySourceCapabilitiesV1 Capabilities { get; }
    ValueTask<VoiceActivityTransferResultV1> TransferAsync(
        VoiceActivityOwnedWindowV1 window,
        CancellationToken cancellationToken);
    ValueTask<VoiceActivitySettlementResultV1> SettleAsync(
        OperationId operationId,
        CancellationToken cancellationToken);
}

internal abstract record VoiceActivityTransferResultV1
{
    private VoiceActivityTransferResultV1() { }

    internal sealed record Accepted : VoiceActivityTransferResultV1
    {
        internal Accepted(OperationId operationId)
        {
            if (!operationId.IsValid) throw new ArgumentException("An operation identity is required.", nameof(operationId));
            OperationId = operationId;
        }

        internal OperationId OperationId { get; }
    }

    internal sealed record Rejected : VoiceActivityTransferResultV1
    {
        internal Rejected(VoiceActivitySourceOutcomeV1 outcome)
        {
            ArgumentNullException.ThrowIfNull(outcome);
            if (outcome is VoiceActivitySourceOutcomeV1.Observed)
                throw new ArgumentException("A rejected transfer cannot carry an observation.", nameof(outcome));
            Outcome = outcome;
        }

        internal VoiceActivitySourceOutcomeV1 Outcome { get; }
    }

    internal sealed record OutcomeUnknown : VoiceActivityTransferResultV1
    {
        internal OutcomeUnknown(OperationId operationId)
        {
            if (!operationId.IsValid) throw new ArgumentException("An operation identity is required.", nameof(operationId));
            OperationId = operationId;
        }

        internal OperationId OperationId { get; }
    }
}

internal abstract record VoiceActivitySettlementResultV1
{
    private VoiceActivitySettlementResultV1() { }

    internal sealed record Pending : VoiceActivitySettlementResultV1
    {
        internal Pending(OperationId operationId) => OperationId = Require(operationId);
        internal OperationId OperationId { get; }
    }

    internal sealed record Settled : VoiceActivitySettlementResultV1
    {
        internal Settled(OperationId operationId, VoiceActivitySourceOutcomeV1 outcome)
        {
            if (!operationId.IsValid) throw new ArgumentException("An operation identity is required.", nameof(operationId));
            ArgumentNullException.ThrowIfNull(outcome);
            OperationId = operationId;
            Outcome = outcome;
        }

        internal OperationId OperationId { get; }
        internal VoiceActivitySourceOutcomeV1 Outcome { get; }
    }

    internal sealed record OutcomeUnknown : VoiceActivitySettlementResultV1
    {
        internal OutcomeUnknown(OperationId operationId) => OperationId = Require(operationId);
        internal OperationId OperationId { get; }
    }

    internal sealed record NotFound : VoiceActivitySettlementResultV1
    {
        internal NotFound(OperationId operationId) => OperationId = Require(operationId);
        internal OperationId OperationId { get; }
    }

    private static OperationId Require(OperationId operationId) => operationId.IsValid
        ? operationId
        : throw new ArgumentException("An operation identity is required.", nameof(operationId));
}
