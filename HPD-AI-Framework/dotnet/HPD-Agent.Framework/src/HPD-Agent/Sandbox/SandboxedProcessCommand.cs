namespace HPD.Agent.Sandbox;

/// <summary>
/// Structured command request for sandboxed process execution.
/// </summary>
public sealed record SandboxedProcessCommand
{
    public required string FileName { get; init; }

    public IReadOnlyList<string> Arguments { get; init; } = Array.Empty<string>();

    public string? WorkingDirectory { get; init; }

    public IReadOnlyDictionary<string, string?> Environment { get; init; } =
        new Dictionary<string, string?>();

    public static SandboxedProcessCommand Exec(
        string fileName,
        IReadOnlyList<string>? arguments = null,
        string? workingDirectory = null,
        IReadOnlyDictionary<string, string?>? environment = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        return new SandboxedProcessCommand
        {
            FileName = fileName,
            Arguments = arguments ?? Array.Empty<string>(),
            WorkingDirectory = workingDirectory,
            Environment = environment ?? new Dictionary<string, string?>()
        };
    }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(FileName))
            throw new InvalidOperationException("Exec command mode requires FileName.");
    }
}
