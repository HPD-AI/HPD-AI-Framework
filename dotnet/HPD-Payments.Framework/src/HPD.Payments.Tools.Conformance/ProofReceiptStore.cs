using System.Text;

namespace HPD.Payments.Tools.Conformance;

/// <summary>Writes canonical receipts into a content-addressed append-only filesystem tree.</summary>
internal static class ProofReceiptStore
{
    private static long _attemptSequence;
    /// <summary>Writes a receipt durably or verifies an exact existing replay without overwriting it.</summary>
    internal static ProofStoreResult Write(string rootDirectory, ProofReceipt receipt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory); ArgumentNullException.ThrowIfNull(receipt);
        var root = Path.GetFullPath(rootDirectory);
        Directory.CreateDirectory(root);
        if (new DirectoryInfo(root).LinkTarget is not null) throw new IOException("Proof root may not be a symbolic link.");
        var digest = receipt.ContentAddress();
        var directory = Path.Combine(root, digest[..2]);
        Directory.CreateDirectory(directory);
        if (new DirectoryInfo(directory).LinkTarget is not null)
            throw new IOException("Proof content directory may not be a symbolic link.");
        var destination = Path.Combine(directory, $"{digest}.receipt");
        var bytes = Encoding.UTF8.GetBytes(receipt.ToCanonicalText());
        if (File.Exists(destination))
        {
            var existing = File.ReadAllBytes(destination);
            if (!existing.AsSpan().SequenceEqual(bytes)) throw new IOException("Content-address collision or modified receipt.");
            return new(destination, digest, false);
        }

        var attempt = Interlocked.Increment(ref _attemptSequence);
        var temporary = Path.Combine(directory,
            $".{digest}.{Environment.ProcessId}.{Environment.CurrentManagedThreadId}.{attempt}.tmp");
        var lockPath = destination + ".lock";
        try
        {
            using var gate = Acquire(lockPath);
            if (File.Exists(destination))
            {
                var existing = File.ReadAllBytes(destination);
                if (!existing.AsSpan().SequenceEqual(bytes)) throw new IOException("Content-address collision or modified receipt.");
                return new(destination, digest, false);
            }
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                bufferSize: 16_384, FileOptions.WriteThrough))
            {
                stream.Write(bytes); stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, destination, overwrite: false);
            return new(destination, digest, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
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
        throw new IOException("Timed out acquiring append-only proof-store lock.");
    }
}

/// <summary>Reports the exact content-addressed receipt path and whether this invocation created it.</summary>
internal sealed record ProofStoreResult(string Path, string ContentAddress, bool Created);
