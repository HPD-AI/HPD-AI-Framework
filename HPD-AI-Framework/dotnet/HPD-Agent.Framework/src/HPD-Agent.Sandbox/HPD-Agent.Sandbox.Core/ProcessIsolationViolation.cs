namespace HPD.Agent.Sandbox;

public sealed record ProcessIsolationViolation
{
    public required ProcessIsolationViolationType Type { get; init; }
    public required string Message { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
    public string? Path { get; init; }
}

public enum ProcessIsolationViolationType
{
    FilesystemRead,
    FilesystemWrite,
    NetworkAccess
}
