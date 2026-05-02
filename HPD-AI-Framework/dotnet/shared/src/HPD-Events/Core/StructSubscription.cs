using System.Threading.Channels;

namespace HPD.Events;

/// <summary>
/// A disposable subscription to a local struct-event stream.
/// </summary>
public readonly struct StructSubscription<TEvent> : IAsyncDisposable
    where TEvent : struct, IStructEvent
{
    private readonly ChannelWriter<TEvent>? _writer;
    private readonly Action<ChannelWriter<TEvent>>? _dispose;

    internal StructSubscription(
        ChannelReader<TEvent> reader,
        ChannelWriter<TEvent> writer,
        Action<ChannelWriter<TEvent>> dispose)
    {
        Reader = reader;
        _writer = writer;
        _dispose = dispose;
    }

    /// <summary>Typed reader for this subscription.</summary>
    public ChannelReader<TEvent> Reader { get; }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        if (_writer is not null && _dispose is not null)
            _dispose(_writer);

        return ValueTask.CompletedTask;
    }
}
