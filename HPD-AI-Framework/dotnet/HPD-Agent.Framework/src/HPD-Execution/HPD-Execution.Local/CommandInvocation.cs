namespace HPD.Execution.Local;

/// <summary>
/// Structured process invocation before platform process-isolation wrapping.
/// </summary>
internal sealed record CommandInvocation(
    string FileName,
    IReadOnlyList<string> ArgumentList)
{
    public static CommandInvocation From(string fileName, IEnumerable<string> arguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(arguments);

        return new CommandInvocation(fileName, arguments.ToArray());
    }
}
