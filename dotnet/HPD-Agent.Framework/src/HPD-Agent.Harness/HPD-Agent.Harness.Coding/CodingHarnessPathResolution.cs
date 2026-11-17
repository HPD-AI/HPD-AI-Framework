using HPD.Agent;
using HPD.Agent.Middleware;
using HPD.Agent.ToolHarness.Coding;

/// <summary>
/// Resolves CodingToolHarness paths against the host-selected workspace before
/// falling back to the process working directory.
/// </summary>
internal static class CodingHarnessPathResolution
{
    public static bool UsesWorkspace(FunctionExecutionContext? context)
        => context?.RunConfig.Context?.Properties?.ContainsKey(
            CodingWorkspace.ContextKey) == true;

    public static string BaseDirectory(FunctionExecutionContext? context)
        => UsesWorkspace(context)
            ? CodingWorkspace.From(context!.RunConfig).DefaultRootPath
            : Directory.GetCurrentDirectory();

    public static string ResolvePath(
        string path,
        FunctionExecutionContext? context)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var trimmed = path.Trim();
        return UsesWorkspace(context)
            ? CodingWorkspace.From(context!.RunConfig)
                .CanonicalizeExplicitPath(trimmed)
            : Path.GetFullPath(trimmed, Directory.GetCurrentDirectory());
    }
}
