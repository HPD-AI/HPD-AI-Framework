using HPD.Agent.Audio.ProviderContracts.VoiceActivity;
using HPD.Agent.Authority;

namespace HPD.Agent.Audio.VoiceActivity;

internal sealed class VoiceActivityTransferredWorkRegistry
{
    private readonly ITransferredVoiceActivitySource _source;
    private readonly HashSet<OperationId> _pending = [];
    private readonly object _gate = new();
    private bool _closed;

    internal VoiceActivityTransferredWorkRegistry(ITransferredVoiceActivitySource source)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
    }

    internal int PendingCount
    {
        get { lock (_gate) return _pending.Count; }
    }

    internal async ValueTask<VoiceActivityTransferResult> TransferAsync(
        VoiceActivityOwnedWindow window,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(window);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (_closed) return ClosedRejected();
            if (_pending.Contains(window.OperationId))
                return new VoiceActivityTransferResult.OutcomeUnknown(window.OperationId);
            if (_pending.Count >= _source.Capabilities.MaximumPendingOperations)
                return CapacityRejected();
            _pending.Add(window.OperationId);
        }

        VoiceActivityTransferResult result;
        try
        {
            result = await _source.TransferAsync(window, cancellationToken).ConfigureAwait(false)
                ?? new VoiceActivityTransferResult.OutcomeUnknown(window.OperationId);
        }
        catch
        {
            return new VoiceActivityTransferResult.OutcomeUnknown(window.OperationId);
        }
        if (result is VoiceActivityTransferResult.Accepted accepted && accepted.OperationId != window.OperationId ||
            result is VoiceActivityTransferResult.OutcomeUnknown unknown && unknown.OperationId != window.OperationId)
            return new VoiceActivityTransferResult.OutcomeUnknown(window.OperationId);
        if (result is VoiceActivityTransferResult.Rejected) Release(window.OperationId);
        return result;
    }

    internal async ValueTask<VoiceActivitySettlementResult> SettleAsync(
        OperationId operationId,
        CancellationToken cancellationToken)
    {
        if (!operationId.IsValid) throw new ArgumentException("An operation identity is required.", nameof(operationId));
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
            if (!_pending.Contains(operationId))
                return new VoiceActivitySettlementResult.NotFound(operationId);

        VoiceActivitySettlementResult result;
        try
        {
            result = await _source.SettleAsync(operationId, cancellationToken).ConfigureAwait(false)
                ?? new VoiceActivitySettlementResult.OutcomeUnknown(operationId);
        }
        catch
        {
            return new VoiceActivitySettlementResult.OutcomeUnknown(operationId);
        }
        if (!Matches(result, operationId))
            return new VoiceActivitySettlementResult.OutcomeUnknown(operationId);
        if (result is VoiceActivitySettlementResult.Settled or VoiceActivitySettlementResult.NotFound)
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

    private static VoiceActivityTransferResult.Rejected CapacityRejected() => new(
        new VoiceActivitySourceOutcome.Unavailable(
            VoiceActivitySourceUnavailableReason.CapacityUnavailable,
            VoiceActivityRetryability.SameGeneration));

    private static VoiceActivityTransferResult.Rejected ClosedRejected() => new(
        new VoiceActivitySourceOutcome.NoObservation(VoiceActivityNoObservationReason.SourceRevoked));

    private static bool Matches(VoiceActivitySettlementResult result, OperationId operationId) => result switch
    {
        VoiceActivitySettlementResult.Pending pending => pending.OperationId == operationId,
        VoiceActivitySettlementResult.Settled settled => settled.OperationId == operationId,
        VoiceActivitySettlementResult.OutcomeUnknown unknown => unknown.OperationId == operationId,
        VoiceActivitySettlementResult.NotFound notFound => notFound.OperationId == operationId,
        _ => false,
    };
}
