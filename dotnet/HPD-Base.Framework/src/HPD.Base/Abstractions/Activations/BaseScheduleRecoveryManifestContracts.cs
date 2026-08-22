using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using Org.BouncyCastle.Math.EC.Rfc8032;

namespace HPD.Base;

/// <summary>Contains one non-prunable durable schedule floor carried across disaster-domain restore.</summary>
public sealed record BaseScheduleRecoveryFloor
{
    /// <summary>Gets the protected SHA-256 schedule-key digest.</summary>
    public required ImmutableArray<byte> ProtectedScheduleKeyDigest { get; init; }
    /// <summary>Gets the positive schedule epoch.</summary>
    public required long ScheduleEpoch { get; init; }
    /// <summary>Gets the last considered nominal Unix-millisecond instant, or null before the first occurrence.</summary>
    public long? LastConsideredNominal { get; init; }
    /// <summary>Gets the nonnegative number of immutable occurrence dispositions covered by the floor.</summary>
    public required long OccurrenceCount { get; init; }
    /// <summary>Gets the SHA-256 checksum of the ordered occurrence authority.</summary>
    public required ImmutableArray<byte> OccurrenceChecksum { get; init; }
    /// <summary>Gets the SHA-256 checksum of the latest activation lineage authority.</summary>
    public required ImmutableArray<byte> LatestActivationLineageChecksum { get; init; }
}

/// <summary>Contains one immutable public verification key for schedule-recovery manifests.</summary>
public sealed record BaseScheduleRecoveryVerificationKey
{
    /// <summary>Gets the stable signing-key identity.</summary>
    public required string Id { get; init; }
    /// <summary>Gets the positive signing-key version.</summary>
    public required int Version { get; init; }
    /// <summary>Gets the 32-byte Ed25519 public key.</summary>
    public required ImmutableArray<byte> PublicKey { get; init; }
    /// <summary>Gets the first accepted issuance instant as Unix milliseconds.</summary>
    public required long ActiveFrom { get; init; }
    /// <summary>Gets the exclusive retirement instant, or null while retained for verification.</summary>
    public long? RetireAfter { get; init; }
    /// <summary>Gets the canonical key-registration checksum.</summary>
    public required ImmutableArray<byte> Checksum { get; init; }
}

/// <summary>Contains one authenticated external schedule floor for disaster-domain restore.</summary>
public sealed record BaseScheduleRecoveryManifest
{
    /// <summary>Gets the exact application identity.</summary>
    public required string ApplicationId { get; init; }
    /// <summary>Gets the logical store identity.</summary>
    public required string LogicalStoreId { get; init; }
    /// <summary>Gets the protected backup artifact identity.</summary>
    public required string BackupArtifactId { get; init; }
    /// <summary>Gets the SHA-256 backup artifact checksum.</summary>
    public required ImmutableArray<byte> BackupArtifactChecksum { get; init; }
    /// <summary>Gets the source store-instance identity.</summary>
    public required string SourceStoreInstanceId { get; init; }
    /// <summary>Gets the nonnegative source restore epoch.</summary>
    public required long SourceRestoreEpoch { get; init; }
    /// <summary>Gets schedule floors sorted by protected key digest.</summary>
    public required ImmutableArray<BaseScheduleRecoveryFloor> Floors { get; init; }
    /// <summary>Gets the issuance instant as Unix milliseconds.</summary>
    public required long IssuedAt { get; init; }
    /// <summary>Gets the exclusive expiration instant as Unix milliseconds.</summary>
    public required long ExpiresAt { get; init; }
    /// <summary>Gets the 32-byte one-use manifest nonce.</summary>
    public required ImmutableArray<byte> Nonce { get; init; }
    /// <summary>Gets the signing-key identity.</summary>
    public required string SigningKeyId { get; init; }
    /// <summary>Gets the signing-key version.</summary>
    public required int SigningKeyVersion { get; init; }
    /// <summary>Gets the SHA-256 checksum of the unsigned canonical authority.</summary>
    public required ImmutableArray<byte> ManifestChecksum { get; init; }
    /// <summary>Gets the 64-byte Ed25519 signature over the SHA-512 signed-authority digest.</summary>
    public required ImmutableArray<byte> Signature { get; init; }
}

/// <summary>Contains expected external authority used to validate one recovery manifest.</summary>
public sealed record BaseScheduleRecoveryManifestValidation
{
    /// <summary>Gets the expected application identity.</summary>
    public required string ApplicationId { get; init; }
    /// <summary>Gets the expected logical store identity.</summary>
    public required string LogicalStoreId { get; init; }
    /// <summary>Gets the expected backup artifact identity.</summary>
    public required string BackupArtifactId { get; init; }
    /// <summary>Gets the expected backup artifact checksum.</summary>
    public required ImmutableArray<byte> BackupArtifactChecksum { get; init; }
    /// <summary>Gets the accepted current instant as Unix milliseconds.</summary>
    public required long AcceptedNow { get; init; }
    /// <summary>Gets the exact expected protected schedule-key digests.</summary>
    public required ImmutableArray<ImmutableArray<byte>> ExpectedScheduleKeyDigests { get; init; }
}

/// <summary>Creates and verifies the canonical Ed25519 schedule-recovery authority.</summary>
public static class BaseScheduleRecoveryManifestContract
{
    private static ReadOnlySpan<byte> AuthorityMarker => "base.activation.scheduleRecoveryManifest.v1\0"u8;
    private static ReadOnlySpan<byte> KeyMarker => "base.activation.scheduleRecoveryKey.v1\0"u8;

    /// <summary>Creates one immutable verification-key registration from a public key.</summary>
    public static BaseScheduleRecoveryVerificationKey CreateVerificationKey(
        string id, int version, ReadOnlySpan<byte> publicKey, long activeFrom, long? retireAfter = null)
    {
        if (!ValidText(id) || version <= 0 || publicKey.Length != Ed25519.PublicKeySize || activeFrom < 0
            || retireAfter is not null && retireAfter <= activeFrom)
            throw Invalid();
        using var stream = new MemoryStream(); stream.Write(KeyMarker); String(stream, id); I64(stream, version);
        Bytes(stream, publicKey); I64(stream, activeFrom); OptionalI64(stream, retireAfter);
        return new BaseScheduleRecoveryVerificationKey
        {
            Id = Copy(id), Version = version, PublicKey = publicKey.ToArray().ToImmutableArray(),
            ActiveFrom = activeFrom, RetireAfter = retireAfter,
            Checksum = SHA256.HashData(stream.ToArray()).ToImmutableArray(),
        };
    }

    /// <summary>Derives a verification registration from a 32-byte private seed owned by backup-control infrastructure.</summary>
    public static BaseScheduleRecoveryVerificationKey CreateVerificationKeyFromPrivateSeed(
        string id, int version, ReadOnlySpan<byte> privateSeed, long activeFrom, long? retireAfter = null)
    {
        if (privateSeed.Length != Ed25519.SecretKeySize) throw Invalid();
        byte[] publicKey = new byte[Ed25519.PublicKeySize];
        byte[] seed = privateSeed.ToArray();
        try { Ed25519.GeneratePublicKey(seed, 0, publicKey, 0); }
        finally { CryptographicOperations.ZeroMemory(seed); }
        return CreateVerificationKey(id, version, publicKey, activeFrom, retireAfter);
    }

    /// <summary>Creates and signs one canonical recovery manifest using a 32-byte Ed25519 private seed.</summary>
    public static BaseScheduleRecoveryManifest Sign(
        BaseScheduleRecoveryManifest unsigned,
        BaseScheduleRecoveryVerificationKey key,
        ReadOnlySpan<byte> privateSeed)
    {
        ArgumentNullException.ThrowIfNull(unsigned); ArgumentNullException.ThrowIfNull(key);
        if (privateSeed.Length != Ed25519.SecretKeySize || !KeyValid(key)
            || unsigned.SigningKeyId != key.Id || unsigned.SigningKeyVersion != key.Version
            || unsigned.Signature.Length != 0 || unsigned.ManifestChecksum.Length != 0)
            throw Invalid();
        byte[] generatedPublic = new byte[Ed25519.PublicKeySize];
        Ed25519.GeneratePublicKey(privateSeed.ToArray(), 0, generatedPublic, 0);
        if (!CryptographicOperations.FixedTimeEquals(generatedPublic, key.PublicKey.AsSpan())) throw Invalid();
        byte[] authority = AuthorityBytes(unsigned);
        byte[] checksum = SHA256.HashData(authority);
        byte[] signed = SignedBytes(authority, checksum);
        byte[] digest = SHA512.HashData(signed);
        byte[] signature = new byte[Ed25519.SignatureSize];
        byte[] seed = privateSeed.ToArray();
        Ed25519.Sign(seed, 0, generatedPublic, 0, digest, 0, digest.Length, signature, 0);
        CryptographicOperations.ZeroMemory(seed);
        return Clone(unsigned) with
        {
            ManifestChecksum = checksum.ToImmutableArray(), Signature = signature.ToImmutableArray(),
        };
    }

    /// <summary>Validates canonical shape, external binding, key authority, checksum, and signature.</summary>
    public static bool Validate(
        BaseScheduleRecoveryManifest manifest,
        BaseScheduleRecoveryManifestValidation expected,
        IEnumerable<BaseScheduleRecoveryVerificationKey> keys)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(manifest); ArgumentNullException.ThrowIfNull(expected);
            BaseScheduleRecoveryVerificationKey[] matching = keys.Where(key =>
                key.Id == manifest.SigningKeyId && key.Version == manifest.SigningKeyVersion).ToArray();
            if (matching.Length != 1 || !KeyValid(matching[0]) || !ManifestShapeValid(manifest)
                || manifest.ApplicationId != expected.ApplicationId || manifest.LogicalStoreId != expected.LogicalStoreId
                || manifest.BackupArtifactId != expected.BackupArtifactId
                || !Fixed(manifest.BackupArtifactChecksum, expected.BackupArtifactChecksum)
                || expected.AcceptedNow < manifest.IssuedAt || expected.AcceptedNow >= manifest.ExpiresAt
                || manifest.IssuedAt < matching[0].ActiveFrom
                || matching[0].RetireAfter is long retired && manifest.IssuedAt >= retired
                || !ExactCoverage(manifest.Floors, expected.ExpectedScheduleKeyDigests)) return false;
            byte[] authority = AuthorityBytes(manifest); byte[] checksum = SHA256.HashData(authority);
            if (!CryptographicOperations.FixedTimeEquals(checksum, manifest.ManifestChecksum.AsSpan())) return false;
            byte[] digest = SHA512.HashData(SignedBytes(authority, checksum));
            return Ed25519.Verify(manifest.Signature.ToArray(), 0, matching[0].PublicKey.ToArray(), 0,
                digest, 0, digest.Length);
        }
        catch { return false; }
    }

    /// <summary>Returns the exact signed canonical bytes used for backup and hostile-input tests.</summary>
    public static ImmutableArray<byte> CanonicalBytes(BaseScheduleRecoveryManifest manifest)
    {
        if (!ManifestShapeValid(manifest)) throw Invalid();
        byte[] authority = AuthorityBytes(manifest);
        if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(authority), manifest.ManifestChecksum.AsSpan())) throw Invalid();
        using var stream = new MemoryStream(); stream.Write(SignedBytes(authority, manifest.ManifestChecksum.AsSpan()));
        Bytes(stream, manifest.Signature.AsSpan()); return stream.ToArray().ToImmutableArray();
    }

    private static byte[] AuthorityBytes(BaseScheduleRecoveryManifest value)
    {
        if (!ManifestAuthorityShapeValid(value)) throw Invalid();
        using var stream = new MemoryStream(); stream.Write(AuthorityMarker);
        String(stream, value.ApplicationId); String(stream, value.LogicalStoreId); String(stream, value.BackupArtifactId);
        Bytes(stream, value.BackupArtifactChecksum.AsSpan()); String(stream, value.SourceStoreInstanceId);
        I64(stream, value.SourceRestoreEpoch); U32(stream, value.Floors.Length);
        foreach (BaseScheduleRecoveryFloor floor in value.Floors)
        {
            Bytes(stream, floor.ProtectedScheduleKeyDigest.AsSpan()); I64(stream, floor.ScheduleEpoch);
            OptionalI64(stream, floor.LastConsideredNominal); I64(stream, floor.OccurrenceCount);
            Bytes(stream, floor.OccurrenceChecksum.AsSpan()); Bytes(stream, floor.LatestActivationLineageChecksum.AsSpan());
        }
        I64(stream, value.IssuedAt); I64(stream, value.ExpiresAt); Bytes(stream, value.Nonce.AsSpan());
        String(stream, value.SigningKeyId); I64(stream, value.SigningKeyVersion); return stream.ToArray();
    }

    private static byte[] SignedBytes(byte[] authority, ReadOnlySpan<byte> checksum)
    { using var stream = new MemoryStream(); stream.Write(authority); Bytes(stream, checksum); return stream.ToArray(); }
    private static bool ManifestShapeValid(BaseScheduleRecoveryManifest value) => ManifestAuthorityShapeValid(value)
        && value.ManifestChecksum.Length == 32 && value.Signature.Length == Ed25519.SignatureSize;
    private static bool ManifestAuthorityShapeValid(BaseScheduleRecoveryManifest value)
    {
        if (!ValidText(value.ApplicationId) || !ValidText(value.LogicalStoreId) || !ValidText(value.BackupArtifactId)
            || !ValidText(value.SourceStoreInstanceId) || value.BackupArtifactChecksum.Length != 32
            || value.SourceRestoreEpoch < 0 || value.IssuedAt < 0 || value.ExpiresAt <= value.IssuedAt
            || value.Nonce.Length != 32 || !ValidText(value.SigningKeyId) || value.SigningKeyVersion <= 0
            || value.Floors.IsDefault) return false;
        ReadOnlySpan<byte> previous = default;
        foreach (BaseScheduleRecoveryFloor floor in value.Floors)
        {
            if (floor.ProtectedScheduleKeyDigest.Length != 32 || floor.ScheduleEpoch <= 0
                || floor.LastConsideredNominal is < 0 || floor.OccurrenceCount < 0
                || floor.OccurrenceChecksum.Length != 32 || floor.LatestActivationLineageChecksum.Length != 32
                || !previous.IsEmpty && previous.SequenceCompareTo(floor.ProtectedScheduleKeyDigest.AsSpan()) >= 0) return false;
            previous = floor.ProtectedScheduleKeyDigest.AsSpan();
        }
        return true;
    }
    private static bool KeyValid(BaseScheduleRecoveryVerificationKey key)
    {
        if (!ValidText(key.Id) || key.Version <= 0 || key.PublicKey.Length != Ed25519.PublicKeySize
            || key.ActiveFrom < 0 || key.RetireAfter is not null && key.RetireAfter <= key.ActiveFrom
            || key.Checksum.Length != 32) return false;
        BaseScheduleRecoveryVerificationKey expected = CreateVerificationKey(
            key.Id, key.Version, key.PublicKey.AsSpan(), key.ActiveFrom, key.RetireAfter);
        return Fixed(key.Checksum, expected.Checksum);
    }
    private static bool ExactCoverage(ImmutableArray<BaseScheduleRecoveryFloor> floors, ImmutableArray<ImmutableArray<byte>> expected)
    {
        if (expected.IsDefault || floors.Length != expected.Length) return false;
        ImmutableArray<byte>[] ordered = expected.Order(ImmutableByteComparer.Instance).ToArray();
        if (ordered.Distinct(ImmutableByteComparer.Instance).Count() != ordered.Length) return false;
        for (int index = 0; index < floors.Length; index++)
            if (!Fixed(floors[index].ProtectedScheduleKeyDigest, ordered[index])) return false;
        return true;
    }
    private static BaseScheduleRecoveryManifest Clone(BaseScheduleRecoveryManifest value) => value with
    {
        ApplicationId = Copy(value.ApplicationId), LogicalStoreId = Copy(value.LogicalStoreId),
        BackupArtifactId = Copy(value.BackupArtifactId), BackupArtifactChecksum = Copy(value.BackupArtifactChecksum),
        SourceStoreInstanceId = Copy(value.SourceStoreInstanceId), Nonce = Copy(value.Nonce),
        SigningKeyId = Copy(value.SigningKeyId), ManifestChecksum = Copy(value.ManifestChecksum), Signature = Copy(value.Signature),
        Floors = value.Floors.Select(static floor => floor with
        {
            ProtectedScheduleKeyDigest = Copy(floor.ProtectedScheduleKeyDigest), OccurrenceChecksum = Copy(floor.OccurrenceChecksum),
            LatestActivationLineageChecksum = Copy(floor.LatestActivationLineageChecksum),
        }).ToImmutableArray(),
    };
    private static ImmutableArray<byte> Copy(ImmutableArray<byte> value) => value.IsDefault ? [] : value.ToArray().ToImmutableArray();
    private static string Copy(string value) => new(value.AsSpan());
    private static bool Fixed(ImmutableArray<byte> left, ImmutableArray<byte> right) => left.Length == right.Length && CryptographicOperations.FixedTimeEquals(left.AsSpan(), right.AsSpan());
    private static bool ValidText(string value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 256 && value.IsNormalized(NormalizationForm.FormC);
    private static InvalidOperationException Invalid() => new("base.activation.recoveryManifestInvalid");
    private static void OptionalI64(Stream stream, long? value) { stream.WriteByte(value.HasValue ? (byte)1 : (byte)0); if (value.HasValue) I64(stream, value.Value); }
    private static void I64(Stream stream, long value) { Span<byte> bytes = stackalloc byte[8]; BinaryPrimitives.WriteInt64BigEndian(bytes, value); stream.Write(bytes); }
    private static void U32(Stream stream, int value) { Span<byte> bytes = stackalloc byte[4]; BinaryPrimitives.WriteUInt32BigEndian(bytes, checked((uint)value)); stream.Write(bytes); }
    private static void Bytes(Stream stream, ReadOnlySpan<byte> value) { U32(stream, value.Length); stream.Write(value); }
    private static void String(Stream stream, string value) { byte[] bytes = Encoding.UTF8.GetBytes(value); Bytes(stream, bytes); }

    private sealed class ImmutableByteComparer : IComparer<ImmutableArray<byte>>, IEqualityComparer<ImmutableArray<byte>>
    {
        internal static readonly ImmutableByteComparer Instance = new();
        public int Compare(ImmutableArray<byte> x, ImmutableArray<byte> y) => x.AsSpan().SequenceCompareTo(y.AsSpan());
        public bool Equals(ImmutableArray<byte> x, ImmutableArray<byte> y) => x.AsSpan().SequenceEqual(y.AsSpan());
        public int GetHashCode(ImmutableArray<byte> obj) { var hash = new HashCode(); foreach (byte value in obj) hash.Add(value); return hash.ToHashCode(); }
    }
}
