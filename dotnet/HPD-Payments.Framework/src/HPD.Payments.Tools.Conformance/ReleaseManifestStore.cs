using System.Text;

namespace HPD.Payments.Tools.Conformance;

/// <summary>Retains immutable release manifests without treating them as release approval.</summary>
internal static class ReleaseManifestStore
{
    private static long _attemptSequence;

    internal static ReleaseManifestStoreResult Write(string rootDirectory, ReleaseManifest manifest)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        ArgumentNullException.ThrowIfNull(manifest);
        var root = PrepareRoot(rootDirectory);
        var digest = manifest.ContentAddress();
        var directory = PrepareContentDirectory(root, digest);
        var destination = Path.Combine(directory, $"{digest}.manifest");
        var bytes = Encoding.UTF8.GetBytes(manifest.ToCanonicalText());
        if (File.Exists(destination)) return VerifyReplay(destination, digest, bytes);

        var attempt = Interlocked.Increment(ref _attemptSequence);
        var temporary = Path.Combine(directory,
            $".{digest}.{Environment.ProcessId}.{Environment.CurrentManagedThreadId}.{attempt}.tmp");
        var lockPath = destination + ".lock";
        try
        {
            using var gate = Acquire(lockPath);
            if (File.Exists(destination)) return VerifyReplay(destination, digest, bytes);
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                bufferSize: 16_384, FileOptions.WriteThrough))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, destination, overwrite: false);
            return new(destination, digest, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    internal static ReleaseManifest Load(string rootDirectory, string contentAddress)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        if (contentAddress.Length != 64 || contentAddress.Any(static c => !(c is >= '0' and <= '9' or >= 'a' and <= 'f')))
            throw new InvalidDataException("Release manifest content address is malformed.");
        var digest = contentAddress;
        var root = PrepareRoot(rootDirectory);
        var directory = PrepareContentDirectory(root, digest);
        var path = Path.Combine(directory, $"{digest}.manifest");
        if (!File.Exists(path) || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new FileNotFoundException("Release manifest is absent or linked.", path);
        var canonical = File.ReadAllText(path, new UTF8Encoding(false, true));
        var manifest = ReleaseManifest.Parse(canonical);
        if (!StringComparer.Ordinal.Equals(manifest.ContentAddress(), digest))
            throw new InvalidDataException("Release manifest content does not match its address.");
        return manifest;
    }

    private static string PrepareRoot(string rootDirectory)
    {
        var root = Path.GetFullPath(rootDirectory);
        Directory.CreateDirectory(root);
        if (new DirectoryInfo(root).LinkTarget is not null)
            throw new IOException("Release manifest root may not be a symbolic link.");
        return root;
    }

    private static string PrepareContentDirectory(string root, string digest)
    {
        var directory = Path.Combine(root, digest[..2]);
        Directory.CreateDirectory(directory);
        if (new DirectoryInfo(directory).LinkTarget is not null)
            throw new IOException("Release manifest content directory may not be a symbolic link.");
        return directory;
    }

    private static ReleaseManifestStoreResult VerifyReplay(string destination, string digest, byte[] bytes)
    {
        if ((File.GetAttributes(destination) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Release manifest may not be a symbolic link.");
        if (!File.ReadAllBytes(destination).AsSpan().SequenceEqual(bytes))
            throw new IOException("Content-address collision or modified release manifest.");
        return new(destination, digest, false);
    }

    private static FileStream Acquire(string lockPath)
    {
        var spin = new SpinWait();
        for (var attempt = 0; attempt < 100_000; attempt++)
        {
            try
            {
                return new(lockPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 1,
                    FileOptions.DeleteOnClose | FileOptions.WriteThrough);
            }
            catch (IOException) { spin.SpinOnce(); }
        }
        throw new IOException("Timed out acquiring release-manifest lock.");
    }
}

internal sealed record ReleaseManifestStoreResult(string Path, string ContentAddress, bool Created);
