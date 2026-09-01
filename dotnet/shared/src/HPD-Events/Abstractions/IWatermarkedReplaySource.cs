#pragma warning restore CS1591

namespace HPD.Events;

/// <summary>Produces replay data and exclusive timestamp watermarks without consumer-filter pushdown.</summary>
/// <typeparam name="TEvent">The replayed event type.</typeparam>
public interface IWatermarkedReplaySource<TEvent> where TEvent : Event
{
    /// <summary>Reads the raw ordered source protocol used to validate and finalize frames.</summary>
    IAsyncEnumerable<ReplaySourceMessage<TEvent>> ReadMessagesAsync(CancellationToken ct = default);
}

/// <summary>Identifies the active payload in a replay source message.</summary>
public enum ReplaySourceMessageKind : byte
{
    /// <summary>The message carries one event.</summary>
    Event,
    /// <summary>The message carries an exclusive timestamp watermark.</summary>
    ExclusiveWatermark
}

/// <summary>Represents either one source event or an exclusive timestamp watermark.</summary>
/// <typeparam name="TEvent">The replayed event type.</typeparam>
public readonly record struct ReplaySourceMessage<TEvent> where TEvent : Event
{
    private ReplaySourceMessage(ReplaySourceMessageKind kind, TEvent? eventValue, long watermark)
    {
        Kind = kind;
        Event = eventValue;
        ExclusiveWatermarkTimestampNs = watermark;
    }

    /// <summary>Gets the active message kind.</summary>
    public ReplaySourceMessageKind Kind { get; }
    /// <summary>Gets the event for an event message.</summary>
    public TEvent? Event { get; }
    /// <summary>Gets the timestamp for an exclusive-watermark message.</summary>
    public long ExclusiveWatermarkTimestampNs { get; }

    /// <summary>Creates an event message.</summary>
    public static ReplaySourceMessage<TEvent> FromEvent(TEvent eventValue)
    {
        ArgumentNullException.ThrowIfNull(eventValue);
        return new(ReplaySourceMessageKind.Event, eventValue, default);
    }

    /// <summary>Creates an exclusive-watermark message.</summary>
    public static ReplaySourceMessage<TEvent> FromExclusiveWatermark(long timestampNs) =>
        new(ReplaySourceMessageKind.ExclusiveWatermark, null, timestampNs);
}
