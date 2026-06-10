#nullable enable

using HPD.Audio.Primitives;

namespace HPD.Audio.Connections;

/// <summary>
/// Describes the lifecycle state of a sessionful audio connection.
/// </summary>
public enum AudioConnectionState
{
    Created = 0,
    Starting = 1,
    Open = 2,
    Closing = 3,
    Closed = 4,
    Failed = 5,
    Disposed = 6
}

/// <summary>
/// Describes why an audio connection closed.
/// </summary>
public enum AudioCloseReason
{
    Normal = 0,
    RemoteClosed = 1,
    TransportFailure = 2,
    Canceled = 3,
    ProtocolError = 4,
    ApplicationShutdown = 5
}

/// <summary>
/// Identifies an audio connection event kind without allocating subclasses.
/// </summary>
public enum AudioConnectionEventKind
{
    Opened = 0,
    Closing = 1,
    Closed = 2,
    Failed = 3
}

/// <summary>
/// Represents an observable audio connection lifecycle event.
/// </summary>
public readonly struct AudioConnectionEvent
{
    /// <summary>Gets the event kind.</summary>
    public required AudioConnectionEventKind Kind { get; init; }

    /// <summary>Gets the connection identifier.</summary>
    public required string ConnectionId { get; init; }

    /// <summary>Gets the event timestamp.</summary>
    public required DateTimeOffset At { get; init; }

    /// <summary>Gets the close reason for closing and closed events.</summary>
    public AudioCloseReason? Reason { get; init; }

    /// <summary>Gets the failure for failed events.</summary>
    public Exception? Error { get; init; }
}

/// <summary>
/// Coordinates lifecycle for a sessionful audio transport.
/// </summary>
public interface IAudioConnection : IAsyncDisposable
{
    /// <summary>Gets the stable connection identifier.</summary>
    string Id { get; }

    /// <summary>Gets the inbound audio source when the connection has input.</summary>
    IAudioSource? Input { get; }

    /// <summary>Gets the outbound audio sink when the connection has output.</summary>
    IAudioSink? Output { get; }

    /// <summary>Gets the current connection state.</summary>
    AudioConnectionState State { get; }

    /// <summary>Reads the next lifecycle event or completion result.</summary>
    ValueTask<AudioConnectionEvent?> ReadEventAsync(CancellationToken cancellationToken = default);

    /// <summary>Starts the connection.</summary>
    ValueTask StartAsync(CancellationToken cancellationToken = default);

    /// <summary>Closes the connection and coordinates owned input and output where possible.</summary>
    ValueTask CloseAsync(AudioCloseReason reason = AudioCloseReason.Normal, CancellationToken cancellationToken = default);
}
