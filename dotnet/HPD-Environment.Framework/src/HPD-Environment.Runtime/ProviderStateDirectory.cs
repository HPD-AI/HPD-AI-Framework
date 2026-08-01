namespace HPD.Environment.Runtime;

public static class ProviderStateDirectory
{
    private const UnixFileMode PrivateDirectoryMode =
        UnixFileMode.UserRead |
        UnixFileMode.UserWrite |
        UnixFileMode.UserExecute;

    public static string EnsurePrivateRoot(
        string path,
        string diagnosticCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(diagnosticCode);
        string fullPath = Path.GetFullPath(path);
        if (File.Exists(fullPath))
            throw Invalid(
                diagnosticCode,
                "the provider state root is a file");
        if (Directory.Exists(fullPath))
            RejectLinkedDirectory(fullPath, diagnosticCode);
        else
            Directory.CreateDirectory(fullPath);
        RejectLinkedDirectory(fullPath, diagnosticCode);
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            File.SetUnixFileMode(fullPath, PrivateDirectoryMode);
            UnixFileMode observed = File.GetUnixFileMode(fullPath);
            if ((observed & ~PrivateDirectoryMode) != 0 ||
                (observed & PrivateDirectoryMode) != PrivateDirectoryMode)
                throw Invalid(
                    diagnosticCode,
                    "the provider state root is not private to its owner");
        }
        return fullPath;
    }

    private static void RejectLinkedDirectory(
        string path,
        string diagnosticCode)
    {
        var info = new DirectoryInfo(path);
        info.Refresh();
        if (!info.Exists ||
            info.LinkTarget is not null ||
            info.Attributes.HasFlag(FileAttributes.ReparsePoint))
            throw Invalid(
                diagnosticCode,
                "the provider state root is linked or unavailable");
    }

    private static InvalidOperationException Invalid(
        string diagnosticCode,
        string message) =>
        new($"{diagnosticCode}: {message}.");
}
