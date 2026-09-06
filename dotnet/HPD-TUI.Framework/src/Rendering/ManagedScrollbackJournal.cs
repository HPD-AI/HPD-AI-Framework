namespace HPD.TUI.Rendering;

internal sealed class ManagedScrollbackJournal(
    Func<ScrollbackBatch, ScrollbackCommitOptions, CancellationToken, ValueTask<ScrollbackCommitResult>> publish)
    : IScrollbackJournal
{
    private long _epoch = long.MinValue;
    private long _watermark;
    private bool _hasWatermark;
    private bool _commitActive;

    public void StartEpoch(long epoch)
    {
        if (_commitActive) throw new InvalidOperationException("A presentation epoch cannot change during a scrollback commit.");
        if (epoch <= _epoch) throw new ArgumentOutOfRangeException(nameof(epoch), "Presentation epochs must increase monotonically.");
        _epoch = epoch;
        _watermark = 0;
        _hasWatermark = false;
    }

    public async ValueTask<ScrollbackCommitResult> CommitAsync(
        ScrollbackBatchLease lease,
        ScrollbackCommitOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lease);
        if (_commitActive) throw new InvalidOperationException("Only one scrollback commit may be active.");
        var batch = lease.Batch;
        if (_epoch != long.MinValue && batch.PresentationEpoch != _epoch)
            throw new InvalidOperationException("The scrollback batch belongs to a stale presentation epoch.");

        if (_epoch == batch.PresentationEpoch && _hasWatermark)
        {
            var end = checked(batch.FirstSequence + batch.Rows.Count);
            if (batch.Rows.Count > 0 && end <= _watermark) return new(ScrollbackCommitStatus.Written, _watermark);
            if (batch.FirstSequence != _watermark)
                throw new InvalidOperationException("Scrollback batches must form one contiguous sequence.");
        }

        _commitActive = true;
        try
        {
            var result = await publish(batch, options, cancellationToken).ConfigureAwait(false);
            if (result.Status == ScrollbackCommitStatus.Written)
            {
                _epoch = batch.PresentationEpoch;
                _watermark = checked(batch.FirstSequence + batch.Rows.Count);
                _hasWatermark = true;
                return result with { CommittedThroughSequence = _watermark };
            }
            return result with { CommittedThroughSequence = _hasWatermark ? _watermark : batch.FirstSequence };
        }
        finally { _commitActive = false; }
    }
}
