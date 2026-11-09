using HPD.Agent.Authority;

namespace HPD.Agent.Audio.ProviderContracts.VoiceActivity;

public readonly ref struct VoiceActivityBorrowedWindow
{
    public VoiceActivityBorrowedWindow(
        ReadOnlySpan<byte> bytes,
        VoiceActivityInputFormat format,
        VoiceActivityMediaExtent extent,
        MonotonicStampV1 observedAt)
    {
        if (bytes.IsEmpty) throw new ArgumentException("A borrowed window must be nonempty.", nameof(bytes));
        ArgumentNullException.ThrowIfNull(format);
        if (format.Encoding == VoiceActivitySampleEncoding.ProviderOpaque)
            throw new ArgumentException("Borrowed graph media must have decoded geometry.", nameof(format));
        if (!observedAt.IsValid) throw new ArgumentException("An observation time is required.", nameof(observedAt));
        Bytes = bytes;
        Format = format;
        Extent = extent;
        ObservedAt = observedAt;
    }

    public ReadOnlySpan<byte> Bytes { get; }
    public VoiceActivityInputFormat Format { get; }
    public VoiceActivityMediaExtent Extent { get; }
    public MonotonicStampV1 ObservedAt { get; }
}

public sealed record VoiceActivityOwnedWindow
{
    private readonly byte[] _bytes;

    public VoiceActivityOwnedWindow(
        OperationId operationId,
        ReadOnlySpan<byte> bytes,
        VoiceActivityInputFormat format,
        VoiceActivityMediaExtent extent,
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
    public VoiceActivityInputFormat Format { get; }
    public VoiceActivityMediaExtent Extent { get; }
    public MonotonicStampV1 ObservedAt { get; }
}

public interface IBorrowedSynchronousVoiceActivitySource
{
    VoiceActivitySourceCapabilities Capabilities { get; }
    VoiceActivitySourceOutcome Observe(scoped in VoiceActivityBorrowedWindow window);
}

public interface ITransferredVoiceActivitySource
{
    VoiceActivitySourceCapabilities Capabilities { get; }
    ValueTask<VoiceActivityTransferResult> TransferAsync(
        VoiceActivityOwnedWindow window,
        CancellationToken cancellationToken);
    ValueTask<VoiceActivitySettlementResult> SettleAsync(
        OperationId operationId,
        CancellationToken cancellationToken);
}

public abstract record VoiceActivityTransferResult
{
    private VoiceActivityTransferResult() { }

    public sealed record Accepted : VoiceActivityTransferResult
    {
        public Accepted(OperationId operationId)
        {
            if (!operationId.IsValid) throw new ArgumentException("An operation identity is required.", nameof(operationId));
            OperationId = operationId;
        }

        public OperationId OperationId { get; }
    }

    public sealed record Rejected : VoiceActivityTransferResult
    {
        public Rejected(VoiceActivitySourceOutcome outcome)
        {
            ArgumentNullException.ThrowIfNull(outcome);
            if (outcome is VoiceActivitySourceOutcome.Observed)
                throw new ArgumentException("A rejected transfer cannot carry an observation.", nameof(outcome));
            Outcome = outcome;
        }

        public VoiceActivitySourceOutcome Outcome { get; }
    }

    public sealed record OutcomeUnknown : VoiceActivityTransferResult
    {
        public OutcomeUnknown(OperationId operationId)
        {
            if (!operationId.IsValid) throw new ArgumentException("An operation identity is required.", nameof(operationId));
            OperationId = operationId;
        }

        public OperationId OperationId { get; }
    }
}

public abstract record VoiceActivitySettlementResult
{
    private VoiceActivitySettlementResult() { }

    public sealed record Pending : VoiceActivitySettlementResult
    {
        public Pending(OperationId operationId) => OperationId = Require(operationId);
        public OperationId OperationId { get; }
    }

    public sealed record Settled : VoiceActivitySettlementResult
    {
        public Settled(OperationId operationId, VoiceActivitySourceOutcome outcome)
        {
            if (!operationId.IsValid) throw new ArgumentException("An operation identity is required.", nameof(operationId));
            ArgumentNullException.ThrowIfNull(outcome);
            OperationId = operationId;
            Outcome = outcome;
        }

        public OperationId OperationId { get; }
        public VoiceActivitySourceOutcome Outcome { get; }
    }

    public sealed record OutcomeUnknown : VoiceActivitySettlementResult
    {
        public OutcomeUnknown(OperationId operationId) => OperationId = Require(operationId);
        public OperationId OperationId { get; }
    }

    public sealed record NotFound : VoiceActivitySettlementResult
    {
        public NotFound(OperationId operationId) => OperationId = Require(operationId);
        public OperationId OperationId { get; }
    }

    private static OperationId Require(OperationId operationId) => operationId.IsValid
        ? operationId
        : throw new ArgumentException("An operation identity is required.", nameof(operationId));
}
