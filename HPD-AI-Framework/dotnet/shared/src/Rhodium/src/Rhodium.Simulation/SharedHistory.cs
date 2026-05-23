using Rhodium.Events;

namespace Rhodium.Simulation;

public sealed class SharedHistory
{
    private readonly FinanceEvent[] _events;

    private SharedHistory(FinanceEvent[] events)
    {
        _events = events;
    }

    public static SharedHistory Load(IEnumerable<FinanceEvent> events)
        => new(events.ToArray());

    public static async Task<SharedHistory> LoadAsync(
        IAsyncEnumerable<FinanceEvent> events,
        CancellationToken ct = default)
    {
        var buffer = new List<FinanceEvent>();
        await foreach (var evt in events.WithCancellation(ct).ConfigureAwait(false))
            buffer.Add(evt);

        return new SharedHistory(buffer.ToArray());
    }

    public int Count => _events.Length;

    public ReadOnlySpan<FinanceEvent> Span => _events;

    public ReadOnlyMemory<FinanceEvent> Memory => _events;
}
