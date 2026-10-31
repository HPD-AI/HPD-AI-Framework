namespace HPD.Agent.Security;

/// <summary>Canonicalizes existing symlink ancestors, including for a not-yet-created output.</summary>
public static class AgentFilesystemPath
{
    public static string Canonicalize(string path)
    {
        var full = Path.GetFullPath(path);
        var root = Path.GetPathRoot(full)!;
        var current = root;
        foreach (var component in full[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, component);
            FileSystemInfo info = Directory.Exists(current) ? new DirectoryInfo(current) : new FileInfo(current);
            if (info.LinkTarget is not null)
                current = info.ResolveLinkTarget(returnFinalTarget: true)?.FullName
                    ?? throw new IOException("Filesystem link could not be resolved.");
        }
        return Path.GetFullPath(current);
    }

    public static bool Contains(string root, string path)
    {
        root = Canonicalize(root).TrimEnd(Path.DirectorySeparatorChar);
        path = Canonicalize(path);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return path.Equals(root, comparison) || path.StartsWith(root + Path.DirectorySeparatorChar, comparison);
    }
}
