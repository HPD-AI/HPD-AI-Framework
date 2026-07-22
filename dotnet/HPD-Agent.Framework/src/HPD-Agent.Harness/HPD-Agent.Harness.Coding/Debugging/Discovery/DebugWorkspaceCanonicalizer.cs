namespace HPD.Agent.ToolHarness.Coding.Debugging;

public interface IDebugWorkspaceCanonicalizer
{
    string Canonicalize(string workspaceRoot, string targetPlatform);
}

public sealed class LexicalDebugWorkspaceCanonicalizer : IDebugWorkspaceCanonicalizer
{
    public string Canonicalize(string workspaceRoot, string targetPlatform)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPlatform);
        if (workspaceRoot.Contains('\0'))
            throw new ArgumentException("Workspace root cannot contain a NUL character.", nameof(workspaceRoot));

        var windows = targetPlatform.StartsWith("windows", StringComparison.OrdinalIgnoreCase);
        var normalized = workspaceRoot.Replace('\\', '/');
        var prefix = normalized.StartsWith('/') ? "/" : string.Empty;
        var segments = new List<string>();
        foreach (var segment in normalized.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".")
                continue;
            if (segment == "..")
            {
                if (segments.Count == 0)
                    throw new ArgumentException("Workspace root escapes its lexical root.", nameof(workspaceRoot));
                segments.RemoveAt(segments.Count - 1);
                continue;
            }
            segments.Add(windows ? segment.ToUpperInvariant() : segment);
        }
        return prefix + string.Join('/', segments);
    }
}
