namespace HPD.Execution.Local.Platforms;

internal sealed record ProcessIsolationDependencyCheck
{
    public IReadOnlyList<ProcessIsolationDependencyIssue> Issues { get; init; } = [];

    public IReadOnlyList<string> Errors { get; init; } = [];

    public IReadOnlyList<string> Warnings { get; init; } = [];

    public bool IsAvailable =>
        Errors.Count == 0 &&
        !Issues.Any(issue => issue.Severity == ProcessIsolationDependencySeverity.Error);

    public static ProcessIsolationDependencyCheck Available { get; } = new();

    public static ProcessIsolationDependencyCheck FromIssues(IEnumerable<ProcessIsolationDependencyIssue> issues)
    {
        var issueList = issues.ToArray();
        return new ProcessIsolationDependencyCheck
        {
            Issues = issueList,
            Errors = issueList
                .Where(issue => issue.Severity == ProcessIsolationDependencySeverity.Error)
                .Select(issue => issue.Message)
                .ToArray(),
            Warnings = issueList
                .Where(issue => issue.Severity == ProcessIsolationDependencySeverity.Warning)
                .Select(issue => issue.Message)
                .ToArray(),
        };
    }
}

internal sealed record ProcessIsolationDependencyIssue
{
    public required ProcessIsolationDependencySeverity Severity { get; init; }

    public required string Code { get; init; }

    public required string Component { get; init; }

    public required string Message { get; init; }

    public static ProcessIsolationDependencyIssue Error(string code, string component, string message) => new()
    {
        Severity = ProcessIsolationDependencySeverity.Error,
        Code = code,
        Component = component,
        Message = message,
    };

    public static ProcessIsolationDependencyIssue Warning(string code, string component, string message) => new()
    {
        Severity = ProcessIsolationDependencySeverity.Warning,
        Code = code,
        Component = component,
        Message = message,
    };
}

internal enum ProcessIsolationDependencySeverity
{
    Error,
    Warning,
}
