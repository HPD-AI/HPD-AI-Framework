namespace HPD.Environment.HostProcess;

/// <summary>
/// Structured process invocation before platform process-isolation wrapping.
/// </summary>
public sealed record CommandInvocation(
    string FileName,
    IReadOnlyList<string> ArgumentList)
{
    public string? WorkingDirectory { get; init; }

    public static CommandInvocation From(
        string fileName,
        IEnumerable<string> arguments,
        string? workingDirectory = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(arguments);

        return new CommandInvocation(fileName, arguments.ToArray())
        {
            WorkingDirectory = workingDirectory
        };
    }
}
