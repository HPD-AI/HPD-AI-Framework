using HPD.Agent.Audio.ProviderContracts.VoiceActivity;
using HPD.Agent.Authority;

namespace HPD.Agent.Audio.VoiceActivity;

internal sealed class VoiceActivityTransferredWorkRegistryV1
{
    private readonly ITransferredVoiceActivitySourceV1 _source;
    private readonly HashSet<OperationId> _pending = [];
    private readonly object _gate = new();
    private bool _closed;

    internal VoiceActivityTransferredWorkRegistryV1(ITransferredVoiceActivitySourceV1 source)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
    }

    internal int PendingCount
    {
        get { lock (_gate) return _pending.Count; }
    }

    internal async ValueTask<VoiceActivityTransferResultV1> TransferAsync(
        VoiceActivityOwnedWindowV1 window,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(window);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (_closed) return ClosedRejected();
            if (_pending.Contains(window.OperationId))
                return new VoiceActivityTransferResultV1.OutcomeUnknown(window.OperationId);
            if (_pending.Count >= _source.Capabilities.MaximumPendingOperations)
                return CapacityRejected();
            _pending.Add(window.OperationId);
        }

        VoiceActivityTransferResultV1 result;
        try
        {
            result = await _source.TransferAsync(window, cancellationToken).ConfigureAwait(false)
                ?? new VoiceActivityTransferResultV1.OutcomeUnknown(window.OperationId);
        }
        catch
        {
            return new VoiceActivityTransferResultV1.OutcomeUnknown(window.OperationId);
        }
        if (result is VoiceActivityTransferResultV1.Accepted accepted && accepted.OperationId != window.OperationId ||
            result is VoiceActivityTransferResultV1.OutcomeUnknown unknown && unknown.OperationId != window.OperationId)
            return new VoiceActivityTransferResultV1.OutcomeUnknown(window.OperationId);
        if (result is VoiceActivityTransferResultV1.Rejected) Release(window.OperationId);
        return result;
    }

    internal async ValueTask<VoiceActivitySettlementResultV1> SettleAsync(
        OperationId operationId,
        CancellationToken cancellationToken)
    {
        if (!operationId.IsValid) throw new ArgumentException("An operation identity is required.", nameof(operationId));
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
            if (!_pending.Contains(operationId))
                return new VoiceActivitySettlementResultV1.NotFound(operationId);

        VoiceActivitySettlementResultV1 result;
        try
        {
            result = await _source.SettleAsync(operationId, cancellationToken).ConfigureAwait(false)
                ?? new VoiceActivitySettlementResultV1.OutcomeUnknown(operationId);
        }
        catch
        {
            return new VoiceActivitySettlementResultV1.OutcomeUnknown(operationId);
        }
        if (!Matches(result, operationId))
            return new VoiceActivitySettlementResultV1.OutcomeUnknown(operationId);
        if (result is VoiceActivitySettlementResultV1.Settled or VoiceActivitySettlementResultV1.NotFound)
            Release(operationId);
        return result;
    }

    internal void Close()
    {
        lock (_gate) _closed = true;
    }

    private void Release(OperationId operationId)
    {
        lock (_gate) _pending.Remove(operationId);
    }

    private static VoiceActivityTransferResultV1.Rejected CapacityRejected() => new(
        new VoiceActivitySourceOutcomeV1.Unavailable(
            VoiceActivitySourceUnavailableReasonV1.CapacityUnavailable,
            VoiceActivityRetryabilityV1.SameGeneration));

    private static VoiceActivityTransferResultV1.Rejected ClosedRejected() => new(
        new VoiceActivitySourceOutcomeV1.NoObservation(VoiceActivityNoObservationReasonV1.SourceRevoked));

    private static bool Matches(VoiceActivitySettlementResultV1 result, OperationId operationId) => result switch
    {
        VoiceActivitySettlementResultV1.Pending pending => pending.OperationId == operationId,
        VoiceActivitySettlementResultV1.Settled settled => settled.OperationId == operationId,
        VoiceActivitySettlementResultV1.OutcomeUnknown unknown => unknown.OperationId == operationId,
        VoiceActivitySettlementResultV1.NotFound notFound => notFound.OperationId == operationId,
        _ => false,
    };
}
