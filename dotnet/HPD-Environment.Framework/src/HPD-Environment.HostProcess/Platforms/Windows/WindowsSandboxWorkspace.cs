namespace HPD.Environment.HostProcess.Platforms.Windows;

using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

/// <summary>Owns a fresh writable directory for a low-integrity Windows command.</summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsSandboxWorkspace : IDisposable
{
    private readonly DirectoryInfo _directory;

    private WindowsSandboxWorkspace(DirectoryInfo directory) => _directory = directory;

    public string Path => _directory.FullName;

    internal static string ResolveDirectory(string root, string? requestedPath)
    {
        string path = string.IsNullOrWhiteSpace(requestedPath) ? root : System.IO.Path.GetFullPath(requestedPath, root);
        if (!ContainsDirectory(root, path))
            throw new UnauthorizedAccessException("The working directory is outside the bound workspace.");
        for (var entry = new DirectoryInfo(path); entry is not null; entry = entry.Parent)
        {
            RejectReparsePoint(entry);
        }
        return path;
    }

    internal static bool ContainsDirectory(string root, string path)
    {
        string relative = System.IO.Path.GetRelativePath(root, path);
        return !System.IO.Path.IsPathRooted(relative) && relative != ".." &&
            !relative.StartsWith("..\\", StringComparison.Ordinal);
    }

    private static void RejectReparsePoint(FileSystemInfo entry)
    {
        if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new IOException($"Project inputs cannot include reparse points: {entry.FullName}");
    }

    public static WindowsSandboxWorkspace Create(SecurityIdentifier workspaceSid)
    {
        ArgumentNullException.ThrowIfNull(workspaceSid);
        var directory = Directory.CreateTempSubdirectory("hpd-windows-workspace-");
        try
        {
            var acl = directory.GetAccessControl();
            acl.AddAccessRule(new FileSystemAccessRule(
                workspaceSid, FileSystemRights.Modify,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None, AccessControlType.Allow));
            directory.SetAccessControl(acl);

            var start = new ProcessStartInfo(
                System.IO.Path.Combine(System.Environment.SystemDirectory, "icacls.exe"))
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            start.ArgumentList.Add(directory.FullName);
            start.ArgumentList.Add("/setintegritylevel");
            start.ArgumentList.Add("(OI)(CI)L");
            using var label = Process.Start(start)
                ?? throw new InvalidOperationException("Could not start the Windows workspace integrity label tool.");
            label.WaitForExit();
            if (label.ExitCode != 0)
                throw new InvalidOperationException($"Could not set the Windows workspace integrity label (exit code {label.ExitCode}).");

            return new WindowsSandboxWorkspace(directory);
        }
        catch
        {
            directory.Delete(recursive: true);
            throw;
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(Path))
            _directory.Delete(recursive: true);
    }
}
