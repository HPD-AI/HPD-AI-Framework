using Rhodium.Events;

namespace Rhodium.Connectivity;

/// <summary>
/// Normalizes raw exchange payloads into Rhodium events.
/// Each exchange has its own normalizer implementation.
/// </summary>
public interface INormalizer
{
    /// <summary>
    /// Exchange this normalizer handles.
    /// </summary>
    ExchangeId Exchange { get; }

    /// <summary>
    /// Normalize raw payload into events.
    /// Caller owns the buffer - memory safe (zero allocation on hot path).
    /// </summary>
    /// <param name="rawPayload">Raw bytes from exchange (JSON, MessagePack, etc.)</param>
    /// <param name="outputBuffer">Caller-supplied buffer to write events into</param>
    /// <returns>Number of events written</returns>
    int Normalize(ReadOnlySpan<byte> rawPayload, Span<FinanceEvent> outputBuffer);

    /// <summary>
    /// Normalize a single message (convenience method, may allocate).
    /// </summary>
    /// <param name="rawPayload">Raw bytes from exchange</param>
    /// <returns>Parsed events</returns>
    IReadOnlyList<FinanceEvent> Normalize(ReadOnlySpan<byte> rawPayload);
}
