namespace HPD.Environment.HostProcess.Platforms.Windows;

using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Security.Cryptography;
using System.Text;

[SupportedOSPlatform("windows")]
internal sealed class WindowsWorkspaceGrant : IDisposable
{
    private readonly DirectoryInfo _root;
    private readonly FileSystemAccessRule _rule;
    private readonly FileStream _journal;
    private readonly string _journalPath;
    private int _disposed;

    public WindowsWorkspaceGrant(string path, SecurityIdentifier sid, bool writable)
    {
        _root = new DirectoryInfo(WindowsSandboxWorkspace.ResolveDirectory(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)), null));
        _rule = new FileSystemAccessRule(sid,
            writable ? FileSystemRights.Modify : FileSystemRights.ReadAndExecute,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None, AccessControlType.Allow);
        string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(_root.FullName.ToUpperInvariant())));
        string directory = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
            "HPD", "WindowsWorkspaceGrants", key);
        Directory.CreateDirectory(directory);
        // Serialize ACL updates from HPD bindings of this root. Each active grant holds
        // its journal open, so recovery only acts on entries from departed processes.
        using var gate = OpenGate(directory);
        foreach (string file in Directory.EnumerateFiles(directory, "S-1-15-2-*.grant"))
        {
            FileStream stale;
            try { stale = File.Open(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) { continue; }
            using (stale)
            {
                var staleSid = new SecurityIdentifier(Path.GetFileNameWithoutExtension(file));
                Remove(staleSid);
            }
            File.Delete(file);
        }

        _journalPath = Path.Combine(directory, sid.Value + ".grant");
        _journal = File.Open(_journalPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
        try
        {
            var acl = _root.GetAccessControl(AccessControlSections.Access);
            acl.AddAccessRule(_rule);
            _root.SetAccessControl(acl);
        }
        catch
        {
            _journal.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        try
        {
            using var gate = OpenGate(Path.GetDirectoryName(_journalPath)!);
            if (_root.Exists)
            {
                WindowsSandboxWorkspace.ResolveDirectory(_root.FullName, null);
                Remove((SecurityIdentifier)_rule.IdentityReference);
            }
            _journal.Dispose();
            File.Delete(_journalPath);
        }
        finally
        {
            _journal.Dispose();
        }
    }

    private void Remove(SecurityIdentifier sid)
    {
        var acl = _root.GetAccessControl(AccessControlSections.Access);
        acl.PurgeAccessRules(sid);
        _root.SetAccessControl(acl);
    }

    private static FileStream OpenGate(string directory)
    {
        string path = Path.Combine(directory, "acl.lock");
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        while (true)
        {
            try
            {
                return File.Open(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException error) when ((error.HResult & 0xffff) == 32 &&
                elapsed.Elapsed < TimeSpan.FromSeconds(5))
            {
                Thread.Sleep(25);
            }
        }
    }
}
