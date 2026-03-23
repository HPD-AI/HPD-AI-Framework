namespace HPD.Sandbox.Local;

/// <summary>
/// Represents a sandbox violation event.
/// </summary>
public sealed record SandboxViolation
{
    public required ViolationType Type { get; init; }
    public required string Message { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
    public string? Path { get; init; }
}

/// <summary>
/// Types of sandbox violations.
/// </summary>
public enum ViolationType
{
    FilesystemRead,
    FilesystemWrite,
    NetworkAccess
}
