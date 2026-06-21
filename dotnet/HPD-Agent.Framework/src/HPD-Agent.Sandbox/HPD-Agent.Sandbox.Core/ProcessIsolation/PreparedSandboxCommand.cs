namespace HPD.Agent.Sandbox.ProcessIsolation;

public sealed record PreparedSandboxCommand
{
    public PreparedSandboxCommand(
        string fileName,
        IEnumerable<string>? arguments = null,
        IReadOnlyDictionary<string, string>? environment = null)
    {
        FileName = string.IsNullOrWhiteSpace(fileName)
            ? throw new ArgumentException("Prepared command file name is required.", nameof(fileName))
            : fileName;
        ArgumentList = arguments?.ToArray() ?? [];
        Environment = environment ?? new Dictionary<string, string>(0, StringComparer.Ordinal);
    }

    public string FileName { get; init; }

    public IReadOnlyList<string> ArgumentList { get; init; }

    public IReadOnlyDictionary<string, string> Environment { get; init; }
}
