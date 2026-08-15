using HPD.Agent.Authority;

namespace HPD.Agent.Audio.ProviderContracts.VoiceActivity;

public readonly ref struct VoiceActivityBorrowedWindowV1
{
    public VoiceActivityBorrowedWindowV1(
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

    public ReadOnlySpan<byte> Bytes { get; }
    public VoiceActivityInputFormatV1 Format { get; }
    public VoiceActivityMediaExtentV1 Extent { get; }
    public MonotonicStampV1 ObservedAt { get; }
}

public sealed record VoiceActivityOwnedWindowV1
{
    private readonly byte[] _bytes;

    public VoiceActivityOwnedWindowV1(
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

    public OperationId OperationId { get; }
    public ReadOnlyMemory<byte> Bytes => _bytes.ToArray();
    public VoiceActivityInputFormatV1 Format { get; }
    public VoiceActivityMediaExtentV1 Extent { get; }
    public MonotonicStampV1 ObservedAt { get; }
}

public interface IBorrowedSynchronousVoiceActivitySourceV1
{
    VoiceActivitySourceCapabilitiesV1 Capabilities { get; }
    VoiceActivitySourceOutcomeV1 Observe(scoped in VoiceActivityBorrowedWindowV1 window);
}

public interface ITransferredVoiceActivitySourceV1
{
    VoiceActivitySourceCapabilitiesV1 Capabilities { get; }
    ValueTask<VoiceActivityTransferResultV1> TransferAsync(
        VoiceActivityOwnedWindowV1 window,
        CancellationToken cancellationToken);
    ValueTask<VoiceActivitySettlementResultV1> SettleAsync(
        OperationId operationId,
        CancellationToken cancellationToken);
}

public abstract record VoiceActivityTransferResultV1
{
    private VoiceActivityTransferResultV1() { }

    public sealed record Accepted : VoiceActivityTransferResultV1
    {
        public Accepted(OperationId operationId)
        {
            if (!operationId.IsValid) throw new ArgumentException("An operation identity is required.", nameof(operationId));
            OperationId = operationId;
        }

        public OperationId OperationId { get; }
    }

    public sealed record Rejected : VoiceActivityTransferResultV1
    {
        public Rejected(VoiceActivitySourceOutcomeV1 outcome)
        {
            ArgumentNullException.ThrowIfNull(outcome);
            if (outcome is VoiceActivitySourceOutcomeV1.Observed)
                throw new ArgumentException("A rejected transfer cannot carry an observation.", nameof(outcome));
            Outcome = outcome;
        }

        public VoiceActivitySourceOutcomeV1 Outcome { get; }
    }

    public sealed record OutcomeUnknown : VoiceActivityTransferResultV1
    {
        public OutcomeUnknown(OperationId operationId)
        {
            if (!operationId.IsValid) throw new ArgumentException("An operation identity is required.", nameof(operationId));
            OperationId = operationId;
        }

        public OperationId OperationId { get; }
    }
}

public abstract record VoiceActivitySettlementResultV1
{
    private VoiceActivitySettlementResultV1() { }

    public sealed record Pending : VoiceActivitySettlementResultV1
    {
        public Pending(OperationId operationId) => OperationId = Require(operationId);
        public OperationId OperationId { get; }
    }

    public sealed record Settled : VoiceActivitySettlementResultV1
    {
        public Settled(OperationId operationId, VoiceActivitySourceOutcomeV1 outcome)
        {
            if (!operationId.IsValid) throw new ArgumentException("An operation identity is required.", nameof(operationId));
            ArgumentNullException.ThrowIfNull(outcome);
            OperationId = operationId;
            Outcome = outcome;
        }

        public OperationId OperationId { get; }
        public VoiceActivitySourceOutcomeV1 Outcome { get; }
    }

    public sealed record OutcomeUnknown : VoiceActivitySettlementResultV1
    {
        public OutcomeUnknown(OperationId operationId) => OperationId = Require(operationId);
        public OperationId OperationId { get; }
    }

    public sealed record NotFound : VoiceActivitySettlementResultV1
    {
        public NotFound(OperationId operationId) => OperationId = Require(operationId);
        public OperationId OperationId { get; }
    }

    private static OperationId Require(OperationId operationId) => operationId.IsValid
        ? operationId
        : throw new ArgumentException("An operation identity is required.", nameof(operationId));
}
