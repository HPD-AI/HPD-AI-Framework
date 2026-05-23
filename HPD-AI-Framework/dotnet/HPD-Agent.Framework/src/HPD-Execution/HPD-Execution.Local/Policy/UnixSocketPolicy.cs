namespace HPD.Execution.Local.Policy;

internal sealed record UnixSocketPolicy
{
    public bool AllowAll { get; init; }
    public IReadOnlyList<string> AllowedPaths { get; init; } = [];
}
