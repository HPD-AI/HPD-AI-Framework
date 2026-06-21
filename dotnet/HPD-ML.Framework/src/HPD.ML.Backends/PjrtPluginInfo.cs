namespace HPD.ML.Backends.Pjrt;

/// <summary>
/// Basic metadata read from a PJRT plugin without creating public tensor objects.
/// </summary>
public sealed record PjrtPluginInfo
{
    public required string LibraryPath { get; init; }
    public required PjrtApiVersion ApiVersion { get; init; }
    public required nuint ApiStructSize { get; init; }
}

