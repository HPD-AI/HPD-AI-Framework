
namespace HPD.Base;

/// <summary>
/// Preserves the aggregate and per-item outcome of one application batch.
/// </summary>
public sealed class BaseBatchResult
{
    private readonly object _owner;
    private readonly BaseRecordBatchResult _result;

    internal BaseBatchResult(
        object owner,
        BaseRecordBatchResult result)
    {
        _owner = owner;
        _result = result;
    }

    /// <summary>Gets the aggregate commit outcome.</summary>
    public BaseRecordBatchOutcome Outcome => _result.Outcome;

    /// <summary>Gets the bounded number of post-commit warnings.</summary>
    public int PostCommitWarningCount => _result.PostCommitWarningCount;

    /// <summary>Gets the safe aggregate failure, when present.</summary>
    public BaseError? Error => _result.Error;

    /// <summary>
    /// Proves that every requested mutation committed.
    /// </summary>
    public BaseCommittedBatch RequireCommitted()
    {
        if (_result.Outcome != BaseRecordBatchOutcome.Committed)
        {
            throw new BaseBatchOutcomeException(_result.Outcome, _result.Error);
        }

        return new BaseCommittedBatch(_owner, _result);
    }
}

/// <summary>
/// Exposes typed item results only after aggregate commitment is proven.
/// </summary>
public sealed class BaseCommittedBatch
{
    private readonly object _owner;
    private readonly BaseRecordBatchResult _result;

    internal BaseCommittedBatch(
        object owner,
        BaseRecordBatchResult result)
    {
        _owner = owner;
        _result = result;
    }

    /// <summary>Gets the committed typed record for an item handle.</summary>
    public BaseRecord<T> Record<T>(BaseBatchItem<T> item)
    {
        ArgumentNullException.ThrowIfNull(item);
        ValidateOwner(item.Owner);
        BaseRecordBatchItemResult result = Find(item.ItemId);

        RecordEnvelope envelope = item.Kind == BaseRecordMutationKind.Upsert
            ? result.Upsert?.Record
                ?? throw Malformed(item.ItemId)
            : result.Record
                ?? throw Malformed(item.ItemId);
        return BaseRecordCodec.Decode(item.Collection, envelope);
    }

    /// <summary>Gets the committed delete result for an item handle.</summary>
    public DeleteResult Deleted(BaseDeleteBatchItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        ValidateOwner(item.Owner);
        return Find(item.ItemId).Delete
            ?? throw Malformed(item.ItemId);
    }

    private BaseRecordBatchItemResult Find(string itemId)
    {
        BaseRecordBatchItemResult? result = _result.Items.SingleOrDefault(
            candidate => StringComparer.Ordinal.Equals(candidate.ItemId, itemId));
        if (result is null ||
            result.Disposition != BaseRecordBatchItemDisposition.Committed)
        {
            throw Malformed(itemId);
        }

        return result;
    }

    private void ValidateOwner(object owner)
    {
        if (!ReferenceEquals(_owner, owner))
        {
            throw new ArgumentException(
                "The batch item belongs to a different batch.",
                nameof(owner));
        }
    }

    private static InvalidOperationException Malformed(string itemId) =>
        new($"Committed batch item '{itemId}' has no compatible result.");
}

/// <summary>
/// Reports that committed-only access was requested for another batch outcome.
/// </summary>
public sealed class BaseBatchOutcomeException : Exception
{
    internal BaseBatchOutcomeException(
        BaseRecordBatchOutcome outcome,
        BaseError? error)
        : base(error?.Message ?? $"Batch outcome was '{outcome}'.")
    {
        Outcome = outcome;
        Error = error;
    }

    /// <summary>Gets the aggregate batch outcome.</summary>
    public BaseRecordBatchOutcome Outcome { get; }

    /// <summary>Gets the safe aggregate error, when present.</summary>
    public BaseError? Error { get; }
}
