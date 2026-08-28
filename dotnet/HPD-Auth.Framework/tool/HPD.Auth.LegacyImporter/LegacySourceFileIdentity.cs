using System.Security.Cryptography;

namespace HPD.Auth.LegacyImporter;

/// <summary>Immutable identity of the stopped legacy source file.</summary>
internal sealed record LegacySourceFileIdentity(long Length, DateTimeOffset LastWriteTimeUtc, byte[] Digest)
{
    internal static async ValueTask<LegacySourceFileIdentity> CaptureAsync(string path, CancellationToken cancellationToken)
    {
        var info = new FileInfo(path);
        if (!info.Exists)
            throw new LegacyImportException(LegacyImportFailure.SourceUnavailable, "The legacy source database is unavailable.");

        await using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 131_072,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        byte[] digest = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        info.Refresh();
        return new LegacySourceFileIdentity(info.Length, info.LastWriteTimeUtc, digest);
    }

    internal bool SecurelyEquals(LegacySourceFileIdentity other) =>
        Length == other.Length
        && LastWriteTimeUtc == other.LastWriteTimeUtc
        && CryptographicOperations.FixedTimeEquals(Digest, other.Digest);
}
