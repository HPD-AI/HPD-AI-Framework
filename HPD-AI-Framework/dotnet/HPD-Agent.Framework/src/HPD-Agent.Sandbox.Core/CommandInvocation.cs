namespace HPD.Agent.Sandbox;

/// <summary>
/// Structured process invocation before platform process-isolation wrapping.
/// </summary>
public sealed record CommandInvocation(
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
