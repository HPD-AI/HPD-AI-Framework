using Rhodium.Events;

namespace Rhodium.Simulation;

/// <summary>
/// Materialized replay history for deterministic in-memory simulations.
/// </summary>
public sealed class SharedHistory
{
    private readonly FinanceEvent[] _events;

    private SharedHistory(FinanceEvent[] events)
    {
        _events = events;
    }

    /// <summary>Load shared history from a synchronous event sequence.</summary>
    public static SharedHistory Load(IEnumerable<FinanceEvent> events)
        => new(events.ToArray());

    /// <summary>Load shared history from an asynchronous event sequence.</summary>
    public static async Task<SharedHistory> LoadAsync(
        IAsyncEnumerable<FinanceEvent> events,
        CancellationToken ct = default)
    {
        var buffer = new List<FinanceEvent>();
        await foreach (var evt in events.WithCancellation(ct).ConfigureAwait(false))
            buffer.Add(evt);

        return new SharedHistory(buffer.ToArray());
    }

    /// <summary>Number of events in the materialized history.</summary>
    public int Count => _events.Length;

    /// <summary>Get an event by zero-based index.</summary>
    public FinanceEvent this[int index] => _events[index];

    /// <summary>Span view over the materialized event array.</summary>
    public ReadOnlySpan<FinanceEvent> Span => _events;

    /// <summary>Memory view over the materialized event array.</summary>
    public ReadOnlyMemory<FinanceEvent> Memory => _events;
}
