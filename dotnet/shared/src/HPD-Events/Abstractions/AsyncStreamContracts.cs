namespace HPD.Events;

/// <summary>
/// Describes an opened asynchronous stream without constraining the item domain.
/// </summary>
/// <typeparam name="TItem">Item type yielded by the stream.</typeparam>
public sealed record AsyncStream<TItem>
{
    /// <summary>Items yielded after the stream has opened successfully.</summary>
    public required IAsyncEnumerable<TItem> Items { get; init; }

    /// <summary>Stable stream metadata for diagnostics, replay, and transport projections.</summary>
    public AsyncStreamDescriptor Descriptor { get; init; } = AsyncStreamDescriptor.Empty;
}

/// <summary>
/// Metadata for an asynchronous stream.
/// </summary>
public sealed record AsyncStreamDescriptor
{
    /// <summary>Empty descriptor used when a source has no extra metadata to expose.</summary>
    public static AsyncStreamDescriptor Empty { get; } = new();

    /// <summary>Logical stream identifier, when one is available.</summary>
    public string? StreamId { get; init; }

    /// <summary>Opaque cursor or continuation token for the stream position.</summary>
    public string? Cursor { get; init; }

    /// <summary>Opaque checkpoint token suitable for replay or resume, when supported.</summary>
    public string? Checkpoint { get; init; }

    /// <summary>Whether the source can replay items from a prior point.</summary>
    public bool Replayable { get; init; }

    /// <summary>Whether the source can resume from a cursor or checkpoint.</summary>
    public bool Resumable { get; init; }

    /// <summary>Backpressure behavior expected after the stream has opened.</summary>
    public AsyncStreamBackpressureMode Backpressure { get; init; } = AsyncStreamBackpressureMode.Unspecified;

    /// <summary>Delivery guarantee expected after the stream has opened.</summary>
    public AsyncStreamDeliveryGuarantee DeliveryGuarantee { get; init; } = AsyncStreamDeliveryGuarantee.Unspecified;
}

/// <summary>
/// Result returned when opening an asynchronous stream.
/// </summary>
/// <typeparam name="TStream">Opened stream value type.</typeparam>
public sealed record AsyncStreamOpenResult<TStream>
{
    /// <summary>Open status.</summary>
    public required AsyncStreamOpenStatus Status { get; init; }

    /// <summary>Opened stream value when <see cref="Status"/> is <see cref="AsyncStreamOpenStatus.Opened"/>.</summary>
    public TStream? Value { get; init; }

    /// <summary>Error details for expected open failures.</summary>
    public AsyncStreamError? Error { get; init; }

    /// <summary>True when the stream opened successfully.</summary>
    public bool Succeeded => Status == AsyncStreamOpenStatus.Opened;

    /// <summary>Create a successful open result.</summary>
    public static AsyncStreamOpenResult<TStream> Opened(TStream value) => new()
    {
        Status = AsyncStreamOpenStatus.Opened,
        Value = value
    };

    /// <summary>Create a failed open result.</summary>
    public static AsyncStreamOpenResult<TStream> Failed(
        AsyncStreamOpenStatus status,
        AsyncStreamError error)
    {
        if (status == AsyncStreamOpenStatus.Opened)
            throw new ArgumentException("Opened status cannot be used for failed stream results.", nameof(status));

        ArgumentNullException.ThrowIfNull(error);
        return new AsyncStreamOpenResult<TStream>
        {
            Status = status,
            Error = error
        };
    }
}

/// <summary>
/// Expected error returned while opening an asynchronous stream.
/// </summary>
public sealed record AsyncStreamError
{
    /// <summary>Stable machine-readable error code.</summary>
    public required string Code { get; init; }

    /// <summary>Safe human-readable error message.</summary>
    public required string Message { get; init; }

    /// <summary>Optional target path, field, cursor, stream id, or capability id.</summary>
    public string? Target { get; init; }

    /// <summary>Error category for callers that do not understand source-specific codes.</summary>
    public AsyncStreamErrorCategory Category { get; init; } = AsyncStreamErrorCategory.None;
}

/// <summary>
/// Opens asynchronous streams from a request object.
/// </summary>
/// <typeparam name="TRequest">Request type used to validate and open the stream.</typeparam>
/// <typeparam name="TItem">Item type yielded by the opened stream.</typeparam>
public interface IAsyncStreamSource<in TRequest, TItem>
{
    /// <summary>
    /// Validate and open a stream. Expected open failures should be represented in the result.
    /// </summary>
    ValueTask<AsyncStreamOpenResult<AsyncStream<TItem>>> OpenAsync(
        TRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Status for opening an asynchronous stream.
/// </summary>
public enum AsyncStreamOpenStatus
{
    Opened,
    ValidationFailed,
    Unsupported,
    CapabilityUnavailable,
    NotFound,
    Unauthorized,
    Failed,
    Cancelled
}

/// <summary>
/// Broad category for an asynchronous stream open error.
/// </summary>
public enum AsyncStreamErrorCategory
{
    None,
    Validation,
    Authentication,
    Authorization,
    NotFound,
    Unsupported,
    Capability,
    Dependency,
    Cancellation,
    Unexpected
}

/// <summary>
/// Backpressure behavior for an opened asynchronous stream.
/// </summary>
public enum AsyncStreamBackpressureMode
{
    Unspecified,
    Wait,
    DropOldest,
    DropNewest,
    DropWrite,
    LatestOnly
}

/// <summary>
/// Delivery guarantee for an opened asynchronous stream.
/// </summary>
public enum AsyncStreamDeliveryGuarantee
{
    Unspecified,
    BestEffort,
    AtMostOnce,
    AtLeastOnce,
    ExactlyOnce,
    Replayable
}
