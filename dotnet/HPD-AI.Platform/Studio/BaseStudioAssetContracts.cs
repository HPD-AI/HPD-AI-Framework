using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;

namespace HPD.AI.Platform.Studio;

/// <summary>Owns the one static shell ABI admitted by all module asset manifests.</summary>
public sealed class BaseStudioShellContract
{
    private static readonly byte[] Canonical = Encoding.UTF8.GetBytes("hpd.studio.shell.svelte5.v1");
    private BaseStudioShellContract() => Checksum = BaseStudioSha256.Compute(Canonical);
    /// <summary>Gets the installed shell-contract checksum.</summary>
    public BaseStudioSha256 Checksum { get; }
    /// <summary>Gets the platform-owned current shell contract.</summary>
    public static BaseStudioShellContract Current { get; } = new();
}

/// <summary>Represents one deeply owned SHA-256 Studio authority.</summary>
public sealed class BaseStudioSha256 : IEquatable<BaseStudioSha256>
{
    private const int Length = 32;
    private readonly byte[] _bytes;

    private BaseStudioSha256(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != Length)
            throw new ArgumentException("A Studio SHA-256 authority must contain exactly 32 bytes.", nameof(bytes));
        _bytes = bytes.ToArray();
    }

    /// <summary>Creates a deeply owned authority from an already computed SHA-256 value.</summary>
    internal static BaseStudioSha256 FromBytes(ReadOnlySpan<byte> bytes) => new(bytes);

    /// <summary>Creates a deeply owned checksum value for correspondence with an independently installed graph authority.</summary>
    public static BaseStudioSha256 FromDigest(ReadOnlySpan<byte> bytes) => new(bytes);

    /// <summary>Computes SHA-256 over the supplied canonical bytes.</summary>
    internal static BaseStudioSha256 Compute(ReadOnlySpan<byte> canonicalBytes)
        => new(SHA256.HashData(canonicalBytes));

    /// <summary>Returns a defensive copy of the authority bytes.</summary>
    public byte[] ToArray() => _bytes.ToArray();

    /// <inheritdoc />
    public bool Equals(BaseStudioSha256? other)
        => other is not null && CryptographicOperations.FixedTimeEquals(_bytes, other._bytes);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is BaseStudioSha256 other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode()
        => BinaryPrimitives.ReadInt32BigEndian(_bytes.AsSpan(0, sizeof(int)));

    /// <summary>Compares two authorities without data-dependent early exit.</summary>
    public static bool FixedTimeEquals(BaseStudioSha256 left, BaseStudioSha256 right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        return CryptographicOperations.FixedTimeEquals(left._bytes, right._bytes);
    }
}

/// <summary>Specifies whether failure to activate a disclosed Studio module fails readiness.</summary>
public enum BaseStudioModuleNecessity : byte
{
    /// <summary>The module is required for the installed Studio graph.</summary>
    Required = 1,
    /// <summary>The module may be quarantined without failing the remaining Studio graph.</summary>
    Optional = 2,
}

/// <summary>Identifies one allowlisted Studio asset media type.</summary>
public enum BaseStudioAssetMediaType : byte
{
    /// <summary>An ECMAScript module.</summary>
    JavaScriptModule = 1,
    /// <summary>A CSS stylesheet.</summary>
    Css = 2,
    /// <summary>An SVG image.</summary>
    Svg = 3,
    /// <summary>A WOFF2 font.</summary>
    Woff2 = 4,
    /// <summary>A PNG image.</summary>
    Png = 5,
    /// <summary>A bounded JSON resource.</summary>
    Json = 6,
}

/// <summary>Describes one content-addressed asset in a Studio module contribution.</summary>
public sealed class BaseStudioAssetEntry
{
    private BaseStudioAssetEntry(string path, BaseStudioAssetMediaType mediaType, long length, BaseStudioSha256 digest)
    {
        Path = path;
        MediaType = mediaType;
        Length = length;
        Digest = BaseStudioSha256.FromBytes(digest.ToArray());
    }

    /// <summary>Gets the normalized relative asset path.</summary>
    public string Path { get; }

    /// <summary>Gets the declared media type.</summary>
    public BaseStudioAssetMediaType MediaType { get; }

    /// <summary>Gets the exact asset length.</summary>
    public long Length { get; }

    /// <summary>Gets the content digest.</summary>
    public BaseStudioSha256 Digest { get; }

    internal static BaseStudioAssetEntry Create(
        string path,
        BaseStudioAssetMediaType mediaType,
        long length,
        BaseStudioSha256 digest)
    {
        ArgumentNullException.ThrowIfNull(digest);
        ValidateAssetPath(path);
        if (!Enum.IsDefined(mediaType))
            throw new ArgumentOutOfRangeException(nameof(mediaType));
        if (length < 1 || length > BaseStudioAssetManifest.MaximumAssetBytes)
            throw new ArgumentOutOfRangeException(nameof(length));
        return new(path, mediaType, length, digest);
    }

    internal static void ValidateAssetPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (path.Length > 256 || !path.All(static value => char.IsAsciiLetterOrDigit(value) || value is '.' or '_' or '-' or '/') ||
            path.StartsWith("/", StringComparison.Ordinal) || path.EndsWith("/", StringComparison.Ordinal) ||
            path.Contains('\\') || path.Contains("//", StringComparison.Ordinal) ||
            path.Split('/').Any(static segment => segment is "" or "." or "..") ||
            path.EndsWith(".map", StringComparison.Ordinal))
            throw new ArgumentException("A Studio asset path is invalid.", nameof(path));
    }
}

/// <summary>Provides deeply owned build-time bytes for one verified asset contribution.</summary>
public sealed class BaseStudioAssetSource
{
    private readonly byte[] _content;
    private BaseStudioAssetSource(string path, BaseStudioAssetMediaType mediaType, byte[] content)
    {
        Path = path;
        MediaType = mediaType;
        _content = content;
    }

    /// <summary>Gets the normalized asset path.</summary>
    public string Path { get; }
    /// <summary>Gets the declared asset media type.</summary>
    public BaseStudioAssetMediaType MediaType { get; }

    /// <summary>Creates a verified build input by deeply owning the actual asset bytes.</summary>
    public static BaseStudioAssetSource Create(string path, BaseStudioAssetMediaType mediaType, ReadOnlySpan<byte> content)
    {
        BaseStudioAssetEntry.ValidateAssetPath(path);
        if (!Enum.IsDefined(mediaType) || content.Length is < 1 or > (int)BaseStudioAssetManifest.MaximumAssetBytes)
            throw new ArgumentOutOfRangeException(nameof(content));
        ValidateExtension(path, mediaType);
        return new(path, mediaType, content.ToArray());
    }

    internal BaseStudioAssetEntry CreateEntry()
        => BaseStudioAssetEntry.Create(Path, MediaType, _content.LongLength, BaseStudioSha256.Compute(_content));

    internal byte[] GetContent() => _content.ToArray();

    private static void ValidateExtension(string path, BaseStudioAssetMediaType mediaType)
    {
        string extension = System.IO.Path.GetExtension(path);
        string expected = mediaType switch
        {
            BaseStudioAssetMediaType.JavaScriptModule => ".js",
            BaseStudioAssetMediaType.Css => ".css",
            BaseStudioAssetMediaType.Svg => ".svg",
            BaseStudioAssetMediaType.Woff2 => ".woff2",
            BaseStudioAssetMediaType.Png => ".png",
            BaseStudioAssetMediaType.Json => ".json",
            _ => throw new ArgumentOutOfRangeException(nameof(mediaType)),
        };
        if (!StringComparer.Ordinal.Equals(extension, expected))
            throw new ArgumentException("A Studio asset extension does not match its media type.", nameof(path));
    }
}

/// <summary>Represents the frozen executable asset contribution for one Studio module.</summary>
public sealed class BaseStudioAssetManifest
{
    private readonly ImmutableDictionary<string, byte[]> _content;
    /// <summary>Gets the maximum number of bytes in one asset.</summary>
    public const long MaximumAssetBytes = 8L * 1024 * 1024;
    /// <summary>Gets the maximum number of bytes in one module asset graph.</summary>
    public const long MaximumModuleBytes = 32L * 1024 * 1024;
    /// <summary>Gets the maximum asset count in one module asset graph.</summary>
    public const int MaximumAssets = 256;

    private BaseStudioAssetManifest(
        string entryModulePath,
        string entryExportName,
        BaseStudioModuleNecessity necessity,
        BaseStudioSha256 shellContractChecksum,
        ImmutableArray<BaseStudioAssetEntry> assets,
        ImmutableDictionary<string, byte[]> content,
        BaseStudioSha256 assetGraphChecksum)
    {
        EntryModulePath = entryModulePath;
        EntryExportName = entryExportName;
        Necessity = necessity;
        ShellContractChecksum = BaseStudioSha256.FromBytes(shellContractChecksum.ToArray());
        Assets = assets;
        _content = content;
        AssetGraphChecksum = assetGraphChecksum;
    }

    /// <summary>Gets the module entry asset path.</summary>
    public string EntryModulePath { get; }
    /// <summary>Gets the fixed entry export name.</summary>
    public string EntryExportName { get; }
    /// <summary>Gets the module activation necessity.</summary>
    public BaseStudioModuleNecessity Necessity { get; }
    /// <summary>Gets the required static shell ABI checksum.</summary>
    public BaseStudioSha256 ShellContractChecksum { get; }
    /// <summary>Gets the immutable assets in ordinal path order.</summary>
    public ImmutableArray<BaseStudioAssetEntry> Assets { get; }
    /// <summary>Gets the Runtime-computed asset graph checksum.</summary>
    public BaseStudioSha256 AssetGraphChecksum { get; }

    /// <summary>Creates, validates, orders, deeply owns, and checksums one asset manifest.</summary>
    public static BaseStudioAssetManifest Create(
        string entryModulePath,
        BaseStudioModuleNecessity necessity,
        BaseStudioShellContract shellContract,
        IEnumerable<BaseStudioAssetSource> assets)
    {
        ArgumentNullException.ThrowIfNull(assets);
        ArgumentNullException.ThrowIfNull(shellContract);
        BaseStudioAssetEntry.ValidateAssetPath(entryModulePath);
        if (!Enum.IsDefined(necessity))
            throw new ArgumentOutOfRangeException(nameof(necessity));

        BaseStudioAssetSource[] sources = assets.Take(MaximumAssets + 1).ToArray();
        if (sources.Any(static source => source is null))
            throw new ArgumentException("A Studio asset manifest contains a null asset.", nameof(assets));
        BaseStudioAssetEntry[] materialized = sources.Select(static source => source.CreateEntry()).ToArray();
        if (materialized.Length is < 1 or > MaximumAssets)
            throw new ArgumentException("A Studio asset manifest has an invalid asset count.", nameof(assets));
        if (materialized.Select(static asset => asset.Path).Distinct(StringComparer.Ordinal).Count() != materialized.Length)
            throw new ArgumentException("A Studio asset manifest contains duplicate paths.", nameof(assets));
        if (!materialized.Select(static asset => asset.Path).SequenceEqual(
                materialized.Select(static asset => asset.Path).Order(StringComparer.Ordinal)))
            throw new ArgumentException("Studio assets must already be in canonical ordinal path order.", nameof(assets));
        if (!materialized.Any(asset => StringComparer.Ordinal.Equals(asset.Path, entryModulePath) &&
                asset.MediaType == BaseStudioAssetMediaType.JavaScriptModule))
            throw new ArgumentException("The Studio entry module is absent or has the wrong media type.", nameof(entryModulePath));

        long total = 0;
        foreach (BaseStudioAssetEntry asset in materialized)
            total = checked(total + asset.Length);
        if (total > MaximumModuleBytes)
            throw new ArgumentException("A Studio module asset graph is too large.", nameof(assets));

        ImmutableArray<BaseStudioAssetEntry> owned = materialized
            .Select(static asset => BaseStudioAssetEntry.Create(asset.Path, asset.MediaType, asset.Length, asset.Digest))
            .ToImmutableArray();
        ImmutableDictionary<string, byte[]> content = sources.ToImmutableDictionary(
            static source => source.Path, static source => source.GetContent(), StringComparer.Ordinal);
        BaseStudioSha256 shellContractChecksum = BaseStudioSha256.FromBytes(shellContract.Checksum.ToArray());
        BaseStudioSha256 graphChecksum = BaseStudioAssetCanonicalEncoding.Compute(
            entryModulePath,
            "activateStudioModule",
            necessity,
            shellContractChecksum,
            owned);
        return new(entryModulePath, "activateStudioModule", necessity, shellContractChecksum, owned, content, graphChecksum);
    }

    internal byte[] GetRequiredContent(string path)
        => _content.TryGetValue(path, out byte[]? value) ? value.ToArray()
            : throw new InvalidOperationException("The Studio asset is absent from its frozen content graph.");
}

internal static class BaseStudioAssetCanonicalEncoding
{
    private static readonly byte[] Purpose = Encoding.ASCII.GetBytes("base.studio.asset-manifest.v1");

    internal static BaseStudioSha256 Compute(
        string entryModulePath,
        string entryExportName,
        BaseStudioModuleNecessity necessity,
        BaseStudioSha256 shellContractChecksum,
        ImmutableArray<BaseStudioAssetEntry> assets)
    {
        using var stream = new MemoryStream();
        stream.Write(Purpose);
        stream.WriteByte(0);
        stream.WriteByte(1);
        WriteString(stream, entryModulePath);
        WriteString(stream, entryExportName);
        stream.WriteByte((byte)necessity);
        stream.Write(shellContractChecksum.ToArray());
        WriteUInt32(stream, checked((uint)assets.Length));
        foreach (BaseStudioAssetEntry asset in assets)
        {
            WriteString(stream, asset.Path);
            stream.WriteByte((byte)asset.MediaType);
            WriteInt64(stream, asset.Length);
            stream.Write(asset.Digest.ToArray());
        }
        return BaseStudioSha256.Compute(stream.GetBuffer().AsSpan(0, checked((int)stream.Length)));
    }

    private static void WriteString(Stream stream, string value)
    {
        if (!value.IsNormalized(NormalizationForm.FormC))
            throw new ArgumentException("Studio canonical strings must already be NFC.", nameof(value));
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        WriteUInt32(stream, checked((uint)bytes.Length));
        stream.Write(bytes);
    }

    private static void WriteUInt32(Stream stream, uint value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        stream.Write(bytes);
    }

    private static void WriteInt64(Stream stream, long value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(long)];
        BinaryPrimitives.WriteInt64BigEndian(bytes, value);
        stream.Write(bytes);
    }
}
