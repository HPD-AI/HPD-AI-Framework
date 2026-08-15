using System.Text;

namespace HPD.Payments.Tools.Conformance;

internal static class ReleaseApprovalStore
{
    private static long _attemptSequence;

    internal static ReleaseApprovalStoreResult Write(string rootDirectory, ReleaseApproval approval)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory); ArgumentNullException.ThrowIfNull(approval);
        var root = PrepareRoot(rootDirectory);
        var digest = approval.ContentAddress();
        var directory = PrepareContentDirectory(root, digest);
        var destination = Path.Combine(directory, $"{digest}.approval");
        var bytes = Encoding.UTF8.GetBytes(approval.ToCanonicalText());
        if (File.Exists(destination)) return VerifyReplay(destination, digest, bytes);
        var temporary = Path.Combine(directory,
            $".{digest}.{Environment.ProcessId}.{Environment.CurrentManagedThreadId}.{Interlocked.Increment(ref _attemptSequence)}.tmp");
        var lockPath = destination + ".lock";
        try
        {
            using var gate = Acquire(lockPath);
            if (File.Exists(destination)) return VerifyReplay(destination, digest, bytes);
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                16_384, FileOptions.WriteThrough))
            {
                stream.Write(bytes); stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, destination, overwrite: false);
            return new(destination, digest, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    internal static ReleaseApproval Load(string rootDirectory, string contentAddress)
    {
        if (contentAddress.Length != 64 || contentAddress.Any(static c => !(c is >= '0' and <= '9' or >= 'a' and <= 'f')))
            throw new InvalidDataException("Release approval address is malformed.");
        var root = PrepareRoot(rootDirectory);
        var path = Path.Combine(PrepareContentDirectory(root, contentAddress), $"{contentAddress}.approval");
        if (!File.Exists(path) || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new FileNotFoundException("Release approval is absent or linked.", path);
        var approval = ReleaseApproval.Parse(File.ReadAllText(path, new UTF8Encoding(false, true)));
        if (!StringComparer.Ordinal.Equals(approval.ContentAddress(), contentAddress))
            throw new InvalidDataException("Release approval content does not match its address.");
        return approval;
    }

    private static string PrepareRoot(string rootDirectory)
    {
        var root = Path.GetFullPath(rootDirectory); Directory.CreateDirectory(root);
        if (new DirectoryInfo(root).LinkTarget is not null) throw new IOException("Release approval root may not be linked.");
        return root;
    }

    private static string PrepareContentDirectory(string root, string digest)
    {
        var directory = Path.Combine(root, digest[..2]); Directory.CreateDirectory(directory);
        if (new DirectoryInfo(directory).LinkTarget is not null) throw new IOException("Release approval content directory may not be linked.");
        return directory;
    }

    private static ReleaseApprovalStoreResult VerifyReplay(string path, string digest, byte[] bytes)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0 ||
            !File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes))
            throw new IOException("Content-address collision, link, or modified release approval.");
        return new(path, digest, false);
    }

    private static FileStream Acquire(string path)
    {
        var spin = new SpinWait();
        for (var attempt = 0; attempt < 100_000; attempt++)
        {
            try { return new(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 1,
                FileOptions.DeleteOnClose | FileOptions.WriteThrough); }
            catch (IOException) { spin.SpinOnce(); }
        }
        throw new IOException("Timed out acquiring release-approval lock.");
    }
}

internal sealed record ReleaseApprovalStoreResult(string Path, string ContentAddress, bool Created);
