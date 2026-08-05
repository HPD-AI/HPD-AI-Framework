namespace HPD.Environment.Runtime;

using System.Buffers.Binary;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using HPD.Environment.Contracts;

public sealed record PortableVolumeBackupManifest
{
    public const string CurrentSchema =
        "hpd.environment.volume-backup/v1";

    public required string BackupId { get; init; }
    public required string OwnerTypeId { get; init; }
    public required string OwnerScopeId { get; init; }
    public required string OwnerVersion { get; init; }
    public required string CompatibilityDomain { get; init; }
    public required string LogicalVolumeId { get; init; }
    public required ulong VolumeGeneration { get; init; }
    public required string ProviderId { get; init; }
    public required VolumeBackupConsistency Consistency { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required long LogicalBytes { get; init; }
    public required int EntryCount { get; init; }
    public required string ContentSha256 { get; init; }
    public required string EncryptionKeyId { get; init; }
}

public readonly record struct PortableVolumeContentEvidence(
    string Sha256,
    long LogicalBytes,
    int EntryCount);

public static class PortableVolumeBackupArchive
{
    private static readonly byte[] Magic =
        "HPD-VOLUME-BACKUP"u8.ToArray();
    private const byte FormatVersion = 1;
    private const int MaximumChunkBytes = 1024 * 1024;
    private const int MaximumStringBytes = 4096;
    private const int MaximumRelativePathBytes = 1024;
    private const int MaximumEntries = 1_000_000;

    public static PortableVolumeBackupManifest Capture(
        string sourceRoot,
        string artifactPath,
        PortableVolumeBackupManifest identity,
        StorageBackupKeyMaterial key,
        long maximumLogicalBytes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(key);
        if (maximumLogicalBytes <= 0)
            throw Invalid("maximum logical bytes must be positive");
        string source = Path.GetFullPath(sourceRoot);
        string artifact = Path.GetFullPath(artifactPath);
        IReadOnlyList<ArchiveEntry> entries =
            EnumerateEntries(source, maximumLogicalBytes, cancellationToken);
        (string digest, long logicalBytes) =
            DigestEntries(source, entries, maximumLogicalBytes, cancellationToken);
        DateTimeOffset capturedAt =
            identity.CreatedAt == default
                ? DateTimeOffset.UtcNow
                : identity.CreatedAt;
        capturedAt = DateTimeOffset.FromUnixTimeMilliseconds(
            capturedAt.ToUnixTimeMilliseconds());
        PortableVolumeBackupManifest manifest = identity with
        {
            CreatedAt = capturedAt,
            LogicalBytes = logicalBytes,
            EntryCount = entries.Count,
            ContentSha256 = digest,
            EncryptionKeyId = key.KeyId,
        };

        string? parent = Path.GetDirectoryName(artifact);
        if (string.IsNullOrWhiteSpace(parent))
            throw Invalid("artifact parent is missing");
        Directory.CreateDirectory(parent);
        string staging = artifact + ".staging-" + Guid.NewGuid().ToString("N");
        try
        {
            using (FileStream output = new(
                       staging,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       64 * 1024,
                       FileOptions.WriteThrough))
            using (var encrypted = new ChunkedAeadWriteStream(
                       output,
                       key.Key.Span,
                       key.KeyId))
            using (var writer = new BinaryWriter(
                       encrypted,
                       Encoding.UTF8,
                       leaveOpen: true))
            {
                WriteManifest(writer, manifest);
                foreach (ArchiveEntry entry in entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    writer.Write((byte)entry.Kind);
                    WriteString(writer, entry.RelativePath, MaximumRelativePathBytes);
                    writer.Write(entry.Length);
                    if (entry.Kind == ArchiveEntryKind.Directory)
                        continue;
                    using FileStream input = OpenSourceFile(entry.FullPath);
                    CopyExact(
                        input,
                        encrypted,
                        entry.Length,
                        cancellationToken);
                    if (input.ReadByte() != -1)
                        throw Integrity(
                            "source file changed while backup was captured");
                }
                writer.Flush();
                encrypted.Complete();
                output.Flush(flushToDisk: true);
            }
            if (File.Exists(artifact))
                throw Invalid("immutable backup identity already exists");
            File.Move(staging, artifact);
            FlushDirectory(parent);
            _ = Validate(
                artifact,
                key,
                maximumLogicalBytes,
                cancellationToken);
            return manifest;
        }
        catch
        {
            TryDeleteFile(staging);
            throw;
        }
    }

    public static PortableVolumeContentEvidence MeasureContent(
        string sourceRoot,
        long maximumLogicalBytes,
        CancellationToken cancellationToken = default)
    {
        string source = Path.GetFullPath(sourceRoot);
        IReadOnlyList<ArchiveEntry> entries =
            EnumerateEntries(
                source,
                maximumLogicalBytes,
                cancellationToken);
        (string digest, long bytes) = DigestEntries(
            source,
            entries,
            maximumLogicalBytes,
            cancellationToken);
        return new(
            digest,
            bytes,
            entries.Count);
    }

    public static async ValueTask<PortableVolumeBackupManifest>
        CaptureEncodedPayloadAsync(
            string artifactPath,
            PortableVolumeBackupManifest manifest,
            StorageBackupKeyMaterial key,
            long encodedPayloadBytes,
            IAsyncEnumerable<ReadOnlyMemory<byte>> payload,
            long maximumLogicalBytes,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(payload);
        ValidateManifestBounds(manifest, maximumLogicalBytes);
        if (encodedPayloadBytes < 0 ||
            encodedPayloadBytes > MaximumEncodedPayloadBytes(
                manifest.LogicalBytes,
                manifest.EntryCount))
            throw Invalid("encoded backup payload exceeds accepted bounds");
        manifest = manifest with
        {
            EncryptionKeyId = key.KeyId,
            CreatedAt = DateTimeOffset.FromUnixTimeMilliseconds(
                manifest.CreatedAt.ToUnixTimeMilliseconds()),
        };
        string artifact = Path.GetFullPath(artifactPath);
        string? parent = Path.GetDirectoryName(artifact);
        if (string.IsNullOrWhiteSpace(parent))
            throw Invalid("artifact parent is missing");
        Directory.CreateDirectory(parent);
        string staging = artifact + ".staging-" +
            Guid.NewGuid().ToString("N");
        try
        {
            long received = 0;
            using (FileStream output = new(
                       staging,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       64 * 1024,
                       FileOptions.WriteThrough))
            using (var encrypted = new ChunkedAeadWriteStream(
                       output,
                       key.Key.Span,
                       key.KeyId))
            using (var writer = new BinaryWriter(
                       encrypted,
                       Encoding.UTF8,
                       leaveOpen: true))
            {
                WriteManifest(writer, manifest);
                writer.Flush();
                await foreach (ReadOnlyMemory<byte> chunk in
                               payload.WithCancellation(
                                   cancellationToken)
                                   .ConfigureAwait(false))
                {
                    if (chunk.IsEmpty)
                        throw Invalid(
                            "encoded backup payload contains an empty nonterminal chunk");
                    received = checked(received + chunk.Length);
                    if (received > encodedPayloadBytes)
                        throw Invalid(
                            "encoded backup payload exceeds its declared length");
                    encrypted.Write(chunk.Span);
                }
                if (received != encodedPayloadBytes)
                    throw Integrity(
                        "encoded backup payload length does not match");
                encrypted.Complete();
                output.Flush(flushToDisk: true);
            }
            if (File.Exists(artifact))
                throw Invalid("immutable backup identity already exists");
            File.Move(staging, artifact);
            FlushDirectory(parent);
            PortableVolumeBackupManifest validated = Validate(
                artifact,
                key,
                maximumLogicalBytes,
                cancellationToken);
            if (validated != manifest)
                throw Integrity(
                    "encoded backup manifest changed during capture");
            return validated;
        }
        catch
        {
            TryDeleteFile(staging);
            throw;
        }
    }

    public static async ValueTask<PortableVolumeBackupManifest>
        StreamValidatedPayloadAsync(
            string artifactPath,
            StorageBackupKeyMaterial key,
            long maximumLogicalBytes,
            Func<ReadOnlyMemory<byte>, CancellationToken, ValueTask>
                writeChunk,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(writeChunk);
        using FileStream input = new(
            Path.GetFullPath(artifactPath),
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            FileOptions.SequentialScan);
        using var decrypted = new ChunkedAeadReadStream(
            input,
            key.Key.Span,
            key.KeyId);
        using var reader = new BinaryReader(
            decrypted,
            Encoding.UTF8,
            leaveOpen: true);
        PortableVolumeBackupManifest manifest =
            ReadManifest(reader);
        ValidateManifestBounds(manifest, maximumLogicalBytes);
        using IncrementalHash digest =
            IncrementalHash.CreateHash(
                HashAlgorithmName.SHA256);
        long restoredBytes = 0;
        string? previousPath = null;
        byte[] buffer = new byte[64 * 1024];
        for (int index = 0;
             index < manifest.EntryCount;
             index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArchiveEntryKind kind =
                (ArchiveEntryKind)reader.ReadByte();
            if (kind is not ArchiveEntryKind.Directory and
                not ArchiveEntryKind.File)
                throw Invalid("backup entry kind is invalid");
            string relative =
                ReadString(reader, MaximumRelativePathBytes);
            ValidateRelativePath(relative);
            if (previousPath is not null &&
                StringComparer.Ordinal.Compare(
                    previousPath,
                    relative) >= 0)
                throw Invalid(
                    "backup entries are not canonical and unique");
            previousPath = relative;
            long length = reader.ReadInt64();
            if (length < 0 ||
                (kind == ArchiveEntryKind.Directory &&
                 length != 0))
                throw Invalid("backup entry length is invalid");
            AppendDigestHeader(
                digest,
                kind,
                relative,
                length);
            using var header = new MemoryStream();
            header.WriteByte((byte)kind);
            byte[] pathBytes = Encoding.UTF8.GetBytes(relative);
            byte[] pathLength = new byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(
                pathLength,
                pathBytes.Length);
            header.Write(pathLength);
            header.Write(pathBytes);
            byte[] fileLength = new byte[8];
            BinaryPrimitives.WriteInt64LittleEndian(
                fileLength,
                length);
            header.Write(fileLength);
            await writeChunk(
                    header.ToArray(),
                    cancellationToken)
                .ConfigureAwait(false);
            long remaining = length;
            restoredBytes = checked(restoredBytes + length);
            if (restoredBytes > maximumLogicalBytes ||
                restoredBytes > manifest.LogicalBytes)
                throw Invalid(
                    "backup content exceeds accepted bounds");
            while (remaining != 0)
            {
                int requested = (int)Math.Min(
                    buffer.Length,
                    remaining);
                int read = decrypted.Read(
                    buffer,
                    0,
                    requested);
                if (read == 0)
                    throw Integrity(
                        "backup content ended unexpectedly");
                digest.AppendData(buffer.AsSpan(0, read));
                await writeChunk(
                        buffer.AsMemory(0, read),
                        cancellationToken)
                    .ConfigureAwait(false);
                remaining -= read;
            }
        }
        decrypted.VerifyComplete();
        if (restoredBytes != manifest.LogicalBytes)
            throw Integrity(
                "backup logical byte count does not match");
        string actualDigest = Convert.ToHexString(
                digest.GetHashAndReset())
            .ToLowerInvariant();
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(actualDigest),
                Encoding.ASCII.GetBytes(
                    manifest.ContentSha256)))
            throw Integrity(
                "backup content digest does not match");
        return manifest;
    }

    public static PortableVolumeBackupManifest Validate(
        string artifactPath,
        StorageBackupKeyMaterial key,
        long maximumLogicalBytes,
        CancellationToken cancellationToken = default)
    {
        return Read(
            artifactPath,
            destinationRoot: null,
            key,
            maximumLogicalBytes,
            cancellationToken);
    }

    public static PortableVolumeBackupManifest RestoreToStaging(
        string artifactPath,
        string stagingRoot,
        StorageBackupKeyMaterial key,
        long maximumLogicalBytes,
        CancellationToken cancellationToken = default)
    {
        string destination = Path.GetFullPath(stagingRoot);
        if (Directory.Exists(destination) ||
            File.Exists(destination))
            throw Invalid("restore staging destination already exists");
        Directory.CreateDirectory(destination);
        try
        {
            PortableVolumeBackupManifest manifest = Read(
                artifactPath,
                destination,
                key,
                maximumLogicalBytes,
                cancellationToken);
            FlushDirectory(destination);
            return manifest;
        }
        catch
        {
            if (Directory.Exists(destination))
                Directory.Delete(destination, recursive: true);
            throw;
        }
    }

    private static PortableVolumeBackupManifest Read(
        string artifactPath,
        string? destinationRoot,
        StorageBackupKeyMaterial key,
        long maximumLogicalBytes,
        CancellationToken cancellationToken)
    {
        if (maximumLogicalBytes <= 0)
            throw Invalid("maximum logical bytes must be positive");
        string artifact = Path.GetFullPath(artifactPath);
        using FileStream input = new(
            artifact,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            FileOptions.SequentialScan);
        using var decrypted = new ChunkedAeadReadStream(
            input,
            key.Key.Span,
            key.KeyId);
        using var reader = new BinaryReader(
            decrypted,
            Encoding.UTF8,
            leaveOpen: true);
        PortableVolumeBackupManifest manifest =
            ReadManifest(reader);
        ValidateManifestBounds(manifest, maximumLogicalBytes);
        using IncrementalHash digest =
            IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long restoredBytes = 0;
        string? previousPath = null;
        for (int index = 0; index < manifest.EntryCount; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArchiveEntryKind kind = (ArchiveEntryKind)reader.ReadByte();
            if (kind is not ArchiveEntryKind.Directory and
                not ArchiveEntryKind.File)
                throw Invalid("backup entry kind is invalid");
            string relative =
                ReadString(reader, MaximumRelativePathBytes);
            ValidateRelativePath(relative);
            if (previousPath is not null &&
                StringComparer.Ordinal.Compare(previousPath, relative) >= 0)
                throw Invalid("backup entries are not canonical and unique");
            previousPath = relative;
            long length = reader.ReadInt64();
            if (length < 0 ||
                (kind == ArchiveEntryKind.Directory && length != 0))
                throw Invalid("backup entry length is invalid");
            AppendDigestHeader(digest, kind, relative, length);
            string? destination = destinationRoot is null
                ? null
                : ResolveDestination(destinationRoot, relative);
            if (kind == ArchiveEntryKind.Directory)
            {
                if (destination is not null)
                    Directory.CreateDirectory(destination);
                continue;
            }
            restoredBytes = checked(restoredBytes + length);
            if (restoredBytes > maximumLogicalBytes ||
                restoredBytes > manifest.LogicalBytes)
                throw Invalid("backup content exceeds accepted bounds");
            Stream sink = destination is null
                ? Stream.Null
                : CreateDestinationFile(destination);
            using (sink)
                CopyExact(
                    decrypted,
                    sink,
                    length,
                    cancellationToken,
                    digest);
        }
        decrypted.VerifyComplete();
        if (restoredBytes != manifest.LogicalBytes)
            throw Integrity("backup logical byte count does not match");
        string actualDigest =
            Convert.ToHexString(digest.GetHashAndReset())
                .ToLowerInvariant();
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(actualDigest),
                Encoding.ASCII.GetBytes(manifest.ContentSha256)))
            throw Integrity("backup content digest does not match");
        return manifest;
    }

    private static IReadOnlyList<ArchiveEntry> EnumerateEntries(
        string root,
        long maximumBytes,
        CancellationToken cancellationToken)
    {
        DirectoryInfo rootInfo = new(root);
        RejectLinkedEntry(rootInfo);
        var entries = new List<ArchiveEntry>();
        long bytes = 0;
        Walk(rootInfo);
        entries.Sort(static (left, right) =>
            StringComparer.Ordinal.Compare(
                left.RelativePath,
                right.RelativePath));
        return entries;

        void Walk(DirectoryInfo directory)
        {
            foreach (FileSystemInfo item in directory
                         .EnumerateFileSystemInfos()
                         .OrderBy(
                             static value => value.Name,
                             StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                RejectLinkedEntry(item);
                string relative = NormalizeRelative(
                    Path.GetRelativePath(root, item.FullName));
                ValidateRelativePath(relative);
                if (entries.Count == MaximumEntries)
                    throw Invalid("backup contains too many entries");
                if (item is DirectoryInfo child)
                {
                    entries.Add(new(
                        ArchiveEntryKind.Directory,
                        relative,
                        child.FullName,
                        0));
                    Walk(child);
                }
                else if (item is FileInfo file)
                {
                    bytes = checked(bytes + file.Length);
                    if (bytes > maximumBytes)
                        throw Invalid("backup source exceeds accepted bounds");
                    entries.Add(new(
                        ArchiveEntryKind.File,
                        relative,
                        file.FullName,
                        file.Length));
                }
                else
                {
                    throw Integrity(
                        "backup source contains an unsupported entry");
                }
            }
        }
    }

    private static (string Digest, long Bytes) DigestEntries(
        string root,
        IReadOnlyList<ArchiveEntry> entries,
        long maximumBytes,
        CancellationToken cancellationToken)
    {
        _ = root;
        using IncrementalHash hash =
            IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long bytes = 0;
        foreach (ArchiveEntry entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AppendDigestHeader(
                hash,
                entry.Kind,
                entry.RelativePath,
                entry.Length);
            if (entry.Kind == ArchiveEntryKind.Directory)
                continue;
            using FileStream input = OpenSourceFile(entry.FullPath);
            bytes = checked(bytes + entry.Length);
            if (bytes > maximumBytes)
                throw Invalid("backup source exceeds accepted bounds");
            CopyExact(
                input,
                Stream.Null,
                entry.Length,
                cancellationToken,
                hash);
            if (input.ReadByte() != -1)
                throw Integrity(
                    "source file changed while backup was measured");
        }
        return (
            Convert.ToHexString(hash.GetHashAndReset())
                .ToLowerInvariant(),
            bytes);
    }

    private static void AppendDigestHeader(
        IncrementalHash digest,
        ArchiveEntryKind kind,
        string relative,
        long length)
    {
        byte[] path = Encoding.UTF8.GetBytes(relative);
        Span<byte> header = stackalloc byte[13];
        header[0] = (byte)kind;
        BinaryPrimitives.WriteInt32LittleEndian(
            header[1..5],
            path.Length);
        BinaryPrimitives.WriteInt64LittleEndian(
            header[5..13],
            length);
        digest.AppendData(header);
        digest.AppendData(path);
    }

    private static void WriteManifest(
        BinaryWriter writer,
        PortableVolumeBackupManifest value)
    {
        WriteString(writer, PortableVolumeBackupManifest.CurrentSchema);
        WriteString(writer, value.BackupId);
        WriteString(writer, value.OwnerTypeId);
        WriteString(writer, value.OwnerScopeId);
        WriteString(writer, value.OwnerVersion);
        WriteString(writer, value.CompatibilityDomain);
        WriteString(writer, value.LogicalVolumeId);
        writer.Write(value.VolumeGeneration);
        WriteString(writer, value.ProviderId);
        writer.Write((int)value.Consistency);
        writer.Write(value.CreatedAt.ToUnixTimeMilliseconds());
        writer.Write(value.LogicalBytes);
        writer.Write(value.EntryCount);
        WriteString(writer, value.ContentSha256);
        WriteString(writer, value.EncryptionKeyId);
    }

    private static PortableVolumeBackupManifest ReadManifest(
        BinaryReader reader)
    {
        string schema = ReadString(reader);
        if (!string.Equals(
                schema,
                PortableVolumeBackupManifest.CurrentSchema,
                StringComparison.Ordinal))
            throw Invalid("backup manifest schema is unsupported");
        string backupId = ReadString(reader);
        string ownerTypeId = ReadString(reader);
        string ownerScopeId = ReadString(reader);
        string ownerVersion = ReadString(reader);
        string compatibility = ReadString(reader);
        string logicalId = ReadString(reader);
        ulong volumeGeneration = reader.ReadUInt64();
        string providerId = ReadString(reader);
        int consistencyValue = reader.ReadInt32();
        if (!Enum.IsDefined(
                typeof(VolumeBackupConsistency),
                consistencyValue))
            throw Invalid("backup consistency is invalid");
        long createdMilliseconds = reader.ReadInt64();
        DateTimeOffset created;
        try
        {
            created = DateTimeOffset.FromUnixTimeMilliseconds(
                createdMilliseconds);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            throw Invalid("backup creation time is invalid", exception);
        }
        long logicalBytes = reader.ReadInt64();
        int entryCount = reader.ReadInt32();
        string digest = ReadString(reader);
        string keyId = ReadString(reader);
        if (digest.Length != 64 ||
            digest.Any(static character =>
                !Uri.IsHexDigit(character)))
            throw Invalid("backup content digest is malformed");
        return new()
        {
            BackupId = backupId,
            OwnerTypeId = ownerTypeId,
            OwnerScopeId = ownerScopeId,
            OwnerVersion = ownerVersion,
            CompatibilityDomain = compatibility,
            LogicalVolumeId = logicalId,
            VolumeGeneration = volumeGeneration,
            ProviderId = providerId,
            Consistency =
                (VolumeBackupConsistency)consistencyValue,
            CreatedAt = created,
            LogicalBytes = logicalBytes,
            EntryCount = entryCount,
            ContentSha256 = digest.ToLowerInvariant(),
            EncryptionKeyId = keyId,
        };
    }

    private static FileStream OpenSourceFile(string path)
    {
        FileInfo before = new(path);
        RejectLinkedEntry(before);
        FileStream stream = new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            FileOptions.SequentialScan);
        FileInfo after = new(path);
        try
        {
            RejectLinkedEntry(after);
            if (before.Length != after.Length ||
                before.LastWriteTimeUtc != after.LastWriteTimeUtc)
                throw Integrity(
                    "backup source changed while it was opened");
            return stream;
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    private static FileStream CreateDestinationFile(string path)
    {
        string? parent = Path.GetDirectoryName(path);
        if (string.IsNullOrWhiteSpace(parent))
            throw Invalid("backup entry parent is missing");
        Directory.CreateDirectory(parent);
        return new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            64 * 1024,
            FileOptions.WriteThrough);
    }

    private static string ResolveDestination(
        string root,
        string relative)
    {
        string candidate = Path.GetFullPath(
            Path.Combine(
                root,
                relative.Replace('/', Path.DirectorySeparatorChar)));
        string prefix = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;
        if (!candidate.StartsWith(
                prefix,
                StringComparison.Ordinal))
            throw Invalid("backup entry escapes the staging root");
        return candidate;
    }

    private static void CopyExact(
        Stream source,
        Stream destination,
        long length,
        CancellationToken cancellationToken,
        IncrementalHash? digest = null)
    {
        byte[] buffer = new byte[64 * 1024];
        long remaining = length;
        while (remaining != 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int requested = (int)Math.Min(buffer.Length, remaining);
            int read = source.Read(buffer, 0, requested);
            if (read == 0)
                throw Integrity("backup content ended unexpectedly");
            digest?.AppendData(buffer.AsSpan(0, read));
            destination.Write(buffer, 0, read);
            remaining -= read;
        }
    }

    private static void WriteString(
        BinaryWriter writer,
        string value,
        int maximumBytes = MaximumStringBytes)
    {
        if (string.IsNullOrEmpty(value))
            throw Invalid("backup manifest strings must be nonempty");
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        if (bytes.Length > maximumBytes)
            throw Invalid("backup manifest string exceeds its bound");
        writer.Write(bytes.Length);
        writer.Write(bytes);
    }

    private static string ReadString(
        BinaryReader reader,
        int maximumBytes = MaximumStringBytes)
    {
        int length = reader.ReadInt32();
        if (length <= 0 || length > maximumBytes)
            throw Invalid("backup manifest string length is invalid");
        byte[] bytes = reader.ReadBytes(length);
        if (bytes.Length != length)
            throw Integrity("backup manifest ended unexpectedly");
        try
        {
            return new UTF8Encoding(
                encoderShouldEmitUTF8Identifier: false,
                throwOnInvalidBytes: true).GetString(bytes);
        }
        catch (DecoderFallbackException exception)
        {
            throw Invalid("backup manifest contains invalid UTF-8", exception);
        }
    }

    private static void ValidateRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) ||
            path.StartsWith('/') ||
            path.EndsWith('/') ||
            path.Contains('\\') ||
            path.Contains('\0'))
            throw Invalid("backup entry path is invalid");
        string[] components = path.Split('/');
        if (components.Any(static component =>
                string.IsNullOrEmpty(component) ||
                component is "." or ".."))
            throw Invalid("backup entry path contains an unsafe component");
        if (Encoding.UTF8.GetByteCount(path) >
            MaximumRelativePathBytes)
            throw Invalid("backup entry path exceeds its bound");
    }

    private static string NormalizeRelative(string value) =>
        value.Replace(Path.DirectorySeparatorChar, '/');

    private static void RejectLinkedEntry(FileSystemInfo entry)
    {
        entry.Refresh();
        if (!entry.Exists ||
            entry.LinkTarget is not null ||
            entry.Attributes.HasFlag(FileAttributes.ReparsePoint))
            throw Integrity(
                "backup source contains a linked or unavailable entry");
    }

    private static void FlushDirectory(string path)
    {
        if (!OperatingSystem.IsLinux() &&
            !OperatingSystem.IsMacOS())
            return;
        int descriptor = OpenDirectoryForSync(path, 0);
        if (descriptor < 0)
            throw new IOException(
                "Environment.Storage.BackupInvalid: backup parent directory could not be opened for synchronization.",
                new Win32Exception(
                    Marshal.GetLastPInvokeError()));
        try
        {
            if (SyncFileDescriptor(descriptor) != 0)
                throw new IOException(
                    "Environment.Storage.BackupInvalid: backup parent directory synchronization failed.",
                    new Win32Exception(
                        Marshal.GetLastPInvokeError()));
        }
        finally
        {
            _ = CloseFileDescriptor(descriptor);
        }
    }

    [DllImport(
        "libc",
        EntryPoint = "open",
        SetLastError = true,
        CharSet = CharSet.Ansi)]
    private static extern int OpenDirectoryForSync(
        string path,
        int flags);

    [DllImport(
        "libc",
        EntryPoint = "fsync",
        SetLastError = true)]
    private static extern int SyncFileDescriptor(
        int descriptor);

    [DllImport(
        "libc",
        EntryPoint = "close",
        SetLastError = true)]
    private static extern int CloseFileDescriptor(
        int descriptor);

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // The caller retains the original failure. Startup cleanup treats
            // a remaining non-authoritative staging file as bounded garbage.
        }
    }

    private static InvalidOperationException Invalid(
        string message,
        Exception? inner = null) =>
        new(
            "Environment.Storage.BackupInvalid: " + message,
            inner);

    private static InvalidOperationException Integrity(
        string message) =>
        new(
            "Environment.Storage.IntegrityCheckRequired: " +
            message);

    private enum ArchiveEntryKind : byte
    {
        Directory = 1,
        File = 2,
    }

    private sealed record ArchiveEntry(
        ArchiveEntryKind Kind,
        string RelativePath,
        string FullPath,
        long Length);

    private sealed class ChunkedAeadWriteStream : Stream
    {
        private readonly Stream _output;
        private readonly AesGcm _aes;
        private readonly byte[] _headerHash;
        private readonly byte[] _noncePrefix;
        private readonly byte[] _buffer =
            new byte[MaximumChunkBytes];
        private int _buffered;
        private uint _counter;
        private long _plaintextBytes;
        private bool _completed;

        public ChunkedAeadWriteStream(
            Stream output,
            ReadOnlySpan<byte> masterKey,
            string keyId)
        {
            _output = output;
            byte[] salt = RandomNumberGenerator.GetBytes(16);
            _noncePrefix = RandomNumberGenerator.GetBytes(8);
            byte[] encryptionKey = new byte[32];
            HKDF.DeriveKey(
                HashAlgorithmName.SHA256,
                masterKey,
                encryptionKey,
                salt,
                "hpd.environment.volume-backup/v1"u8);
            _aes = new AesGcm(encryptionKey, 16);
            CryptographicOperations.ZeroMemory(encryptionKey);
            using var header = new MemoryStream();
            header.Write(Magic);
            header.WriteByte(FormatVersion);
            header.Write(salt);
            header.Write(_noncePrefix);
            byte[] keyBytes = Encoding.UTF8.GetBytes(keyId);
            if (keyBytes.Length is 0 or > MaximumStringBytes)
                throw Invalid("backup encryption key identity is invalid");
            Span<byte> length = stackalloc byte[2];
            BinaryPrimitives.WriteUInt16LittleEndian(
                length,
                checked((ushort)keyBytes.Length));
            header.Write(length);
            header.Write(keyBytes);
            byte[] headerBytes = header.ToArray();
            _headerHash = SHA256.HashData(headerBytes);
            _output.Write(headerBytes);
        }

        public override void Write(
            byte[] buffer,
            int offset,
            int count) =>
            Write(buffer.AsSpan(offset, count));

        public override void Write(ReadOnlySpan<byte> source)
        {
            if (_completed)
                throw new InvalidOperationException(
                    "Encrypted backup stream is complete.");
            while (!source.IsEmpty)
            {
                int copy = Math.Min(
                    _buffer.Length - _buffered,
                    source.Length);
                source[..copy].CopyTo(
                    _buffer.AsSpan(_buffered));
                _buffered += copy;
                source = source[copy..];
                if (_buffered == _buffer.Length)
                    FlushChunk(final: false);
            }
        }

        public void Complete()
        {
            if (_completed)
                return;
            if (_buffered != 0)
                FlushChunk(final: false);
            FlushChunk(final: true);
            _completed = true;
        }

        private void FlushChunk(bool final)
        {
            if (_counter == uint.MaxValue)
                throw Invalid("backup contains too many encrypted chunks");
            int length = final ? 0 : _buffered;
            byte[] ciphertext = new byte[length];
            byte[] tag = new byte[16];
            Span<byte> nonce = stackalloc byte[12];
            _noncePrefix.CopyTo(nonce);
            BinaryPrimitives.WriteUInt32BigEndian(
                nonce[8..],
                _counter);
            byte[] aad = CreateAad(
                _headerHash,
                _counter,
                length,
                final,
                _plaintextBytes);
            _aes.Encrypt(
                nonce,
                _buffer.AsSpan(0, length),
                ciphertext,
                tag,
                aad);
            Span<byte> frameLength = stackalloc byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(
                frameLength,
                length);
            _output.Write(frameLength);
            _output.Write(ciphertext);
            _output.Write(tag);
            _plaintextBytes = checked(
                _plaintextBytes + length);
            _counter++;
            _buffered = 0;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (!_completed)
                    Complete();
                _aes.Dispose();
                CryptographicOperations.ZeroMemory(_buffer);
            }
            base.Dispose(disposing);
        }

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length =>
            throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }
        public override void Flush() => _output.Flush();
        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();
        public override void SetLength(long value) =>
            throw new NotSupportedException();
    }

    private sealed class ChunkedAeadReadStream : Stream
    {
        private readonly Stream _input;
        private readonly AesGcm _aes;
        private readonly byte[] _headerHash;
        private readonly byte[] _noncePrefix;
        private byte[] _plaintext = [];
        private int _offset;
        private uint _counter;
        private long _plaintextBytes;
        private bool _final;

        public ChunkedAeadReadStream(
            Stream input,
            ReadOnlySpan<byte> masterKey,
            string expectedKeyId)
        {
            _input = input;
            byte[] fixedHeader =
                PortableVolumeBackupArchive.ReadExactly(
                    input,
                    Magic.Length + 1 + 16 + 8 + 2);
            if (!fixedHeader.AsSpan(0, Magic.Length)
                    .SequenceEqual(Magic) ||
                fixedHeader[Magic.Length] != FormatVersion)
                throw Invalid("backup envelope is unsupported");
            ReadOnlySpan<byte> salt =
                fixedHeader.AsSpan(Magic.Length + 1, 16);
            _noncePrefix =
                fixedHeader.AsSpan(
                    Magic.Length + 1 + 16,
                    8).ToArray();
            ushort keyLength =
                BinaryPrimitives.ReadUInt16LittleEndian(
                    fixedHeader.AsSpan(
                        Magic.Length + 1 + 16 + 8,
                        2));
            if (keyLength is 0 or > MaximumStringBytes)
                throw Invalid(
                    "backup envelope key identity is invalid");
            byte[] keyBytes =
                PortableVolumeBackupArchive.ReadExactly(
                    input,
                    keyLength);
            string keyId;
            try
            {
                keyId = new UTF8Encoding(
                    false,
                    true).GetString(keyBytes);
            }
            catch (DecoderFallbackException exception)
            {
                throw Invalid(
                    "backup envelope key identity is invalid",
                    exception);
            }
            if (!string.Equals(
                    keyId,
                    expectedKeyId,
                    StringComparison.Ordinal))
                throw Invalid(
                    "backup encryption credential does not match the envelope");
            byte[] completeHeader =
                [.. fixedHeader, .. keyBytes];
            _headerHash = SHA256.HashData(completeHeader);
            byte[] encryptionKey = new byte[32];
            HKDF.DeriveKey(
                HashAlgorithmName.SHA256,
                masterKey,
                encryptionKey,
                salt,
                "hpd.environment.volume-backup/v1"u8);
            _aes = new AesGcm(encryptionKey, 16);
            CryptographicOperations.ZeroMemory(encryptionKey);
        }

        public override int Read(
            byte[] buffer,
            int offset,
            int count) =>
            Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> destination)
        {
            if (destination.IsEmpty)
                return 0;
            if (_offset == _plaintext.Length)
            {
                if (_final)
                    return 0;
                ReadFrame();
                if (_final)
                    return 0;
            }
            int copy = Math.Min(
                destination.Length,
                _plaintext.Length - _offset);
            _plaintext.AsSpan(_offset, copy)
                .CopyTo(destination);
            _offset += copy;
            return copy;
        }

        public void VerifyComplete()
        {
            Span<byte> scratch = stackalloc byte[1];
            if (Read(scratch) != 0)
                throw Invalid(
                    "backup contains trailing plaintext");
            if (!_final || _input.ReadByte() != -1)
                throw Invalid(
                    "backup envelope is incomplete or has trailing bytes");
        }

        private void ReadFrame()
        {
            byte[] lengthBytes =
                PortableVolumeBackupArchive.ReadExactly(
                    _input,
                    4);
            int length =
                BinaryPrimitives.ReadInt32LittleEndian(
                    lengthBytes);
            if (length < 0 || length > MaximumChunkBytes)
                throw Invalid(
                    "backup encrypted chunk length is invalid");
            byte[] ciphertext =
                PortableVolumeBackupArchive.ReadExactly(
                    _input,
                    length);
            byte[] tag =
                PortableVolumeBackupArchive.ReadExactly(
                    _input,
                    16);
            bool final = length == 0;
            byte[] plaintext = new byte[length];
            Span<byte> nonce = stackalloc byte[12];
            _noncePrefix.CopyTo(nonce);
            BinaryPrimitives.WriteUInt32BigEndian(
                nonce[8..],
                _counter);
            byte[] aad = CreateAad(
                _headerHash,
                _counter,
                length,
                final,
                _plaintextBytes);
            try
            {
                _aes.Decrypt(
                    nonce,
                    ciphertext,
                    tag,
                    plaintext,
                    aad);
            }
            catch (AuthenticationTagMismatchException exception)
            {
                throw Invalid(
                    "backup authentication failed",
                    exception);
            }
            _plaintextBytes = checked(
                _plaintextBytes + length);
            _counter++;
            _plaintext = plaintext;
            _offset = 0;
            _final = final;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _aes.Dispose();
                CryptographicOperations.ZeroMemory(_plaintext);
            }
            base.Dispose(disposing);
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length =>
            throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();
        public override void SetLength(long value) =>
            throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }

    private static byte[] CreateAad(
        byte[] headerHash,
        uint counter,
        int length,
        bool final,
        long precedingBytes)
    {
        byte[] aad = new byte[
            headerHash.Length + 4 + 4 + 1 + 8];
        headerHash.CopyTo(aad, 0);
        BinaryPrimitives.WriteUInt32BigEndian(
            aad.AsSpan(headerHash.Length, 4),
            counter);
        BinaryPrimitives.WriteInt32BigEndian(
            aad.AsSpan(headerHash.Length + 4, 4),
            length);
        aad[headerHash.Length + 8] =
            final ? (byte)1 : (byte)0;
        BinaryPrimitives.WriteInt64BigEndian(
            aad.AsSpan(headerHash.Length + 9, 8),
            precedingBytes);
        return aad;
    }

    private static byte[] ReadExactly(
        Stream stream,
        int length)
    {
        byte[] value = new byte[length];
        stream.ReadExactly(value, 0, value.Length);
        return value;
    }

    private static void ValidateManifestBounds(
        PortableVolumeBackupManifest manifest,
        long maximumLogicalBytes)
    {
        if (maximumLogicalBytes <= 0 ||
            manifest.LogicalBytes < 0 ||
            manifest.LogicalBytes > maximumLogicalBytes ||
            manifest.EntryCount < 0 ||
            manifest.EntryCount > MaximumEntries ||
            manifest.ContentSha256.Length != 64 ||
            manifest.ContentSha256.Any(static character =>
                !Uri.IsHexDigit(character)))
            throw Invalid(
                "backup manifest exceeds accepted bounds");
    }

    private static long MaximumEncodedPayloadBytes(
        long logicalBytes,
        int entryCount) =>
        checked(
            logicalBytes +
            (long)entryCount *
            (1 + 4 + MaximumRelativePathBytes + 8));
}
