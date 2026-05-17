namespace HPD.Sandbox.Local.Platforms;

internal sealed record SandboxDependencyCheck
{
    public IReadOnlyList<SandboxDependencyIssue> Issues { get; init; } = [];

    public IReadOnlyList<string> Errors { get; init; } = [];

    public IReadOnlyList<string> Warnings { get; init; } = [];

    public bool IsAvailable =>
        Errors.Count == 0 &&
        !Issues.Any(issue => issue.Severity == SandboxDependencySeverity.Error);

    public static SandboxDependencyCheck Available { get; } = new();

    public static SandboxDependencyCheck FromIssues(IEnumerable<SandboxDependencyIssue> issues)
    {
        var issueList = issues.ToArray();
        return new SandboxDependencyCheck
        {
            Issues = issueList,
            Errors = issueList
                .Where(issue => issue.Severity == SandboxDependencySeverity.Error)
                .Select(issue => issue.Message)
                .ToArray(),
            Warnings = issueList
                .Where(issue => issue.Severity == SandboxDependencySeverity.Warning)
                .Select(issue => issue.Message)
                .ToArray(),
        };
    }
}

internal sealed record SandboxDependencyIssue
{
    public required SandboxDependencySeverity Severity { get; init; }

    public required string Code { get; init; }

    public required string Component { get; init; }

    public required string Message { get; init; }

    public static SandboxDependencyIssue Error(string code, string component, string message) => new()
    {
        Severity = SandboxDependencySeverity.Error,
        Code = code,
        Component = component,
        Message = message,
    };

    public static SandboxDependencyIssue Warning(string code, string component, string message) => new()
    {
        Severity = SandboxDependencySeverity.Warning,
        Code = code,
        Component = component,
        Message = message,
    };
}

internal enum SandboxDependencySeverity
{
    Error,
    Warning,
}
