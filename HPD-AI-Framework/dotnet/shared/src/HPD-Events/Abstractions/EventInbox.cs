using System.Threading.Channels;

namespace HPD.Events;

/// <summary>
/// Caller-owned typed class-event inbox.
/// </summary>
public readonly struct EventInbox<TEvent> : IAsyncDisposable
    where TEvent : Event
{
    private readonly ChannelWriter<TEvent>? _writer;
    private readonly Action<ChannelWriter<TEvent>>? _dispose;

    internal EventInbox(
        ChannelReader<TEvent> reader,
        ChannelWriter<TEvent> writer,
        Action<ChannelWriter<TEvent>> dispose)
    {
        Reader = reader;
        _writer = writer;
        _dispose = dispose;
    }

    /// <summary>
    /// Reader owned by the caller.
    /// </summary>
    public ChannelReader<TEvent> Reader { get; }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        if (_writer is not null && _dispose is not null)
            _dispose(_writer);

        return ValueTask.CompletedTask;
    }
}
