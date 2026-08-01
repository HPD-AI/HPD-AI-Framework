namespace HPD.Environment.Local;

using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using HPD.Environment.Contracts;

internal sealed record LocalVolumeIdentity(
    string ResourceId,
    string Scope,
    string LogicalId,
    string OwnerScopeId,
    string OwnerResourceId,
    string DeclarationId,
    string CompatibilityDomain,
    string FilesystemIdentity,
    long VolumeGeneration,
    long MaximumBytes);

internal sealed class LocalVolumeIdentityStore
{
    private static readonly byte[] Magic =
        "HPD-LOCAL-VOLUME"u8.ToArray();
    private const byte FormatVersion = 1;
    private const int MaximumFileBytes = 64 * 1024;
    private const int MaximumStringBytes = 4096;
    private readonly string _root;

    public LocalVolumeIdentityStore(string storageRoot)
    {
        _root = Path.Combine(storageRoot, "volume-state");
        Directory.CreateDirectory(_root);
    }

    public LocalVolumeIdentity Create(
        ResourceMetadata<DurableVolume> metadata,
        DurableVolumeSpec spec)
    {
        var identity = new LocalVolumeIdentity(
            metadata.Id.Value,
            metadata.Scope.Value,
            spec.LogicalId,
            spec.OwnerScopeId,
            spec.OwnerResourceId,
            spec.DeclarationId,
            spec.CompatibilityDomain,
            "local-volume:" + Guid.NewGuid().ToString("N"),
            metadata.Generation.Value,
            spec.MaximumBytes.Value);
        Write(identity);
        return identity;
    }

    public LocalVolumeIdentity ReadAndValidate(
        ResourceMetadata<DurableVolume> metadata,
        DurableVolumeSpec spec,
        ResourceGeneration expectedVolumeGeneration,
        string? expectedFilesystemIdentity)
    {
        LocalVolumeIdentity identity = Read(spec.LogicalId);
        bool matches =
            string.Equals(identity.ResourceId, metadata.Id.Value, StringComparison.Ordinal) &&
            string.Equals(identity.Scope, metadata.Scope.Value, StringComparison.Ordinal) &&
            string.Equals(identity.LogicalId, spec.LogicalId, StringComparison.Ordinal) &&
            string.Equals(identity.OwnerScopeId, spec.OwnerScopeId, StringComparison.Ordinal) &&
            string.Equals(identity.OwnerResourceId, spec.OwnerResourceId, StringComparison.Ordinal) &&
            string.Equals(identity.DeclarationId, spec.DeclarationId, StringComparison.Ordinal) &&
            string.Equals(identity.CompatibilityDomain, spec.CompatibilityDomain, StringComparison.Ordinal) &&
            identity.MaximumBytes == spec.MaximumBytes.Value &&
            identity.VolumeGeneration == expectedVolumeGeneration.Value &&
            (expectedFilesystemIdentity is null ||
             string.Equals(identity.FilesystemIdentity, expectedFilesystemIdentity, StringComparison.Ordinal));
        if (!matches)
            throw Invalid(
                "the physical Local volume identity does not match its authoritative ownership");
        return identity;
    }

    public LocalVolumeIdentity AdvanceGeneration(
        ResourceMetadata<DurableVolume> metadata,
        DurableVolumeSpec spec,
        ResourceGeneration currentGeneration,
        ResourceGeneration nextGeneration,
        string? expectedFilesystemIdentity)
    {
        LocalVolumeIdentity current = ReadAndValidate(
            metadata,
            spec,
            currentGeneration,
            expectedFilesystemIdentity);
        if (nextGeneration.Value != checked(currentGeneration.Value + 1))
            throw Invalid("volume generation advancement must be exactly monotonic");
        LocalVolumeIdentity next = current with
        {
            VolumeGeneration = nextGeneration.Value,
        };
        Write(next);
        return next;
    }

    public LocalVolumeIdentity ReadForPendingRestore(
        ResourceMetadata<DurableVolume> metadata,
        DurableVolumeSpec spec,
        ResourceGeneration previousGeneration,
        ResourceGeneration restoredGeneration,
        string? expectedFilesystemIdentity)
    {
        LocalVolumeIdentity identity = Read(spec.LogicalId);
        bool ownershipMatches =
            string.Equals(identity.ResourceId, metadata.Id.Value, StringComparison.Ordinal) &&
            string.Equals(identity.Scope, metadata.Scope.Value, StringComparison.Ordinal) &&
            string.Equals(identity.LogicalId, spec.LogicalId, StringComparison.Ordinal) &&
            string.Equals(identity.OwnerScopeId, spec.OwnerScopeId, StringComparison.Ordinal) &&
            string.Equals(identity.OwnerResourceId, spec.OwnerResourceId, StringComparison.Ordinal) &&
            string.Equals(identity.DeclarationId, spec.DeclarationId, StringComparison.Ordinal) &&
            string.Equals(identity.CompatibilityDomain, spec.CompatibilityDomain, StringComparison.Ordinal) &&
            identity.MaximumBytes == spec.MaximumBytes.Value &&
            identity.VolumeGeneration is var generation &&
            (generation == previousGeneration.Value ||
             generation == restoredGeneration.Value) &&
            (expectedFilesystemIdentity is null ||
             string.Equals(identity.FilesystemIdentity, expectedFilesystemIdentity, StringComparison.Ordinal));
        if (!ownershipMatches)
            throw Invalid(
                "the physical Local volume identity does not match pending restore ownership");
        return identity;
    }

    public void Delete(string logicalId)
    {
        string path = PathFor(logicalId);
        if (File.Exists(path))
            File.Delete(path);
    }

    public bool Exists(string logicalId) =>
        File.Exists(PathFor(logicalId));

    public bool Any() =>
        Directory.EnumerateFiles(
            _root,
            "*.identity",
            SearchOption.TopDirectoryOnly).Any();

    private LocalVolumeIdentity Read(string logicalId)
    {
        string path = PathFor(logicalId);
        if (!File.Exists(path))
            throw Invalid("the physical Local volume identity record is missing");
        var info = new FileInfo(path);
        if (info.LinkTarget is not null ||
            info.Length <= Magic.Length + 1 ||
            info.Length > MaximumFileBytes)
            throw Invalid("the physical Local volume identity record is malformed");
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.None,
            bufferSize: 4096,
            FileOptions.SequentialScan);
        byte[] magic = ReadExactly(stream, Magic.Length);
        if (!CryptographicOperations.FixedTimeEquals(magic, Magic) ||
            stream.ReadByte() != FormatVersion)
            throw Invalid("the physical Local volume identity schema is unsupported");
        var value = new LocalVolumeIdentity(
            ReadString(stream),
            ReadString(stream),
            ReadString(stream),
            ReadString(stream),
            ReadString(stream),
            ReadString(stream),
            ReadString(stream),
            ReadString(stream),
            ReadInt64(stream),
            ReadInt64(stream));
        if (stream.ReadByte() != -1 ||
            value.VolumeGeneration <= 0 ||
            value.MaximumBytes <= 0 ||
            !string.Equals(value.LogicalId, logicalId, StringComparison.Ordinal))
            throw Invalid("the physical Local volume identity record has invalid or trailing content");
        return value;
    }

    private void Write(LocalVolumeIdentity identity)
    {
        string path = PathFor(identity.LogicalId);
        string temporary = Path.Combine(
            _root,
            $".{identity.LogicalId}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(
                temporary,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.WriteThrough))
            {
                stream.Write(Magic);
                stream.WriteByte(FormatVersion);
                WriteString(stream, identity.ResourceId);
                WriteString(stream, identity.Scope);
                WriteString(stream, identity.LogicalId);
                WriteString(stream, identity.OwnerScopeId);
                WriteString(stream, identity.OwnerResourceId);
                WriteString(stream, identity.DeclarationId);
                WriteString(stream, identity.CompatibilityDomain);
                WriteString(stream, identity.FilesystemIdentity);
                WriteInt64(stream, identity.VolumeGeneration);
                WriteInt64(stream, identity.MaximumBytes);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    private string PathFor(string logicalId)
    {
        Validate(logicalId);
        return Path.Combine(_root, logicalId + ".identity");
    }

    private static void WriteString(Stream stream, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        if (bytes.Length is 0 or > MaximumStringBytes)
            throw Invalid("a physical Local volume identity field exceeds its bound");
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(length, bytes.Length);
        stream.Write(length);
        stream.Write(bytes);
    }

    private static string ReadString(Stream stream)
    {
        int length = checked((int)ReadInt32(stream));
        if (length is <= 0 or > MaximumStringBytes)
            throw Invalid("a physical Local volume identity field exceeds its bound");
        try
        {
            return new UTF8Encoding(false, true).GetString(
                ReadExactly(stream, length));
        }
        catch (DecoderFallbackException exception)
        {
            throw Invalid("a physical Local volume identity field is not valid UTF-8", exception);
        }
    }

    private static void WriteInt64(Stream stream, long value)
    {
        Span<byte> bytes = stackalloc byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(bytes, value);
        stream.Write(bytes);
    }

    private static long ReadInt64(Stream stream) =>
        BinaryPrimitives.ReadInt64LittleEndian(ReadExactly(stream, 8));

    private static long ReadInt32(Stream stream) =>
        BinaryPrimitives.ReadInt32LittleEndian(ReadExactly(stream, 4));

    private static byte[] ReadExactly(Stream stream, int length)
    {
        byte[] bytes = new byte[length];
        try
        {
            stream.ReadExactly(bytes);
        }
        catch (EndOfStreamException exception)
        {
            throw Invalid("the physical Local volume identity record is truncated", exception);
        }
        return bytes;
    }

    private static void Validate(string value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.Length > 128 ||
            value is "." or ".." ||
            value.Any(character =>
                !(char.IsAsciiLetterOrDigit(character) ||
                  character is '-' or '_' or '.')))
            throw Invalid("logical volume identity is not one bounded safe component");
    }

    private static InvalidOperationException Invalid(
        string detail,
        Exception? inner = null) =>
        new(
            "Environment.Storage.IntegrityCheckRequired: " + detail + ".",
            inner);
}
