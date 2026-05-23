namespace HPD.ML.Backends.Pjrt;

/// <summary>
/// Basic runtime metadata from a created PJRT client.
/// </summary>
public sealed record PjrtClientInfo
{
    public required string PlatformName { get; init; }
    public required string PlatformVersion { get; init; }
    public required int DeviceCount { get; init; }
}

