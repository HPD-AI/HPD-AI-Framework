using HPD.Base.Events;
using HPD.Base.Query;
using HPD.Base.Records;
using HPD.Base.Results;
using HPD.Base.Runtime;
using HPD.Base.Schema;
using HPD.Base.Stores;

namespace HPD.Base.Realtime.Tests;

internal sealed class TestMutationJournalStore : ITransactionalMutationJournalStore
{
    private readonly IRecordStore _inner;
    private readonly List<BaseMutationJournalEntry> _entries;

    public TestMutationJournalStore(IRecordStore inner, IEnumerable<BaseMutationJournalEntry>? entries = null)
    {
        _inner = inner;
        _entries = entries?.OrderBy(entry => entry.Position.Value).ToList() ?? [];
    }

    public StoreCapabilityDescriptor Capabilities => _inner.Capabilities;

    public void Add(BaseMutationJournalEntry entry)
    {
        lock (_entries)
        {
            _entries.Add(entry);
            _entries.Sort((left, right) => left.Position.Value.CompareTo(right.Position.Value));
        }
    }

    public ValueTask<BaseMutationJournalBounds> GetMutationJournalBoundsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_entries)
        {
            return ValueTask.FromResult(new BaseMutationJournalBounds(
                new BaseMutationJournalPosition(_entries.Count == 0 ? 0 : _entries[0].Position.Value),
                new BaseMutationJournalPosition(_entries.Count == 0 ? 0 : _entries[^1].Position.Value)));
        }
    }

    public ValueTask<BaseMutationJournalPage> ReadMutationJournalAsync(
        BaseMutationJournalReadRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_entries)
        {
            var high = request.Through?.Value ?? (_entries.Count == 0 ? 0 : _entries[^1].Position.Value);
            var matching = _entries
                .Where(entry => entry.Position.Value > request.After.Value && entry.Position.Value <= high)
                .Take(request.Limit + 1)
                .ToArray();
            var hasMore = matching.Length > request.Limit;
            return ValueTask.FromResult(new BaseMutationJournalPage
            {
                Entries = matching.Take(request.Limit).ToArray(),
                Earliest = new BaseMutationJournalPosition(_entries.Count == 0 ? 0 : _entries[0].Position.Value),
                HighWatermark = new BaseMutationJournalPosition(high),
                HasMore = hasMore
            });
        }
    }

    public ValueTask<BaseMutationJournalEntry?> FindMutationJournalEntryAsync(
        string eventId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_entries)
        {
            return ValueTask.FromResult(_entries.FirstOrDefault(entry =>
                string.Equals(entry.EventId, eventId, StringComparison.Ordinal)));
        }
    }

    public ValueTask<OperationResult<RecordPage>> ListAsync(CollectionDefinition collection, RecordQuery query, OperationContext context, CancellationToken cancellationToken = default) =>
        _inner.ListAsync(collection, query, context, cancellationToken);

    public ValueTask<OperationResult<RecordEnvelope>> GetAsync(CollectionDefinition collection, RecordId id, OperationContext context, CancellationToken cancellationToken = default) =>
        _inner.GetAsync(collection, id, context, cancellationToken);

}
