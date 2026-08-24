using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;

namespace HPD.Base;

/// <summary>Bounds one provider-owned read-only capture of dynamic store authority.</summary>
public sealed record BaseStudioDynamicStoreAuthorityRequest
{
    /// <summary>Gets the installed application identity.</summary>
    public required string ApplicationId { get; init; }
    /// <summary>Gets the maximum evidence bytes.</summary>
    public required int MaximumEvidenceBytes { get; init; }
    /// <summary>Gets the maximum transient bytes.</summary>
    public required int MaximumTransientBytes { get; init; }
    /// <summary>Gets the provider deadline.</summary>
    public required TimeSpan Deadline { get; init; }
}

/// <summary>Reports exact dynamic authority capture accounting.</summary>
public sealed record BaseStudioDynamicStoreAuthorityAccounting
{
    /// <summary>Gets authoritative reads performed.</summary>
    public required int AuthorityReads { get; init; }
    /// <summary>Gets canonical evidence bytes.</summary>
    public required int EvidenceBytes { get; init; }
    /// <summary>Gets complete transient bytes.</summary>
    public required int TransientBytes { get; init; }
}

/// <summary>Contains coherent provider-owned store, restore, and schema facts without static provider authority.</summary>
public sealed record BaseStudioDynamicStoreAuthority
{
    /// <summary>Gets the installed application identity.</summary>
    public required string ApplicationId { get; init; }
    /// <summary>Gets the persistent store-instance identity.</summary>
    public required string StoreInstanceId { get; init; }
    /// <summary>Gets the current restore epoch.</summary>
    public required long RestoreEpoch { get; init; }
    /// <summary>Gets the current schema generation.</summary>
    public required long SchemaGeneration { get; init; }
    /// <summary>Gets the purpose-bound dynamic evidence checksum.</summary>
    public required ImmutableArray<byte> EvidenceChecksum { get; init; }
    /// <summary>Gets exact provider accounting.</summary>
    public required BaseStudioDynamicStoreAuthorityAccounting Accounting { get; init; }
}

/// <summary>Captures only provider-owned dynamic store authority without exposing data or transactions.</summary>
public interface IBaseStudioDynamicStoreAuthoritySource
{
    /// <summary>Captures coherent store/restore/schema authority under exact independent bounds.</summary>
    ValueTask<OperationResult<BaseStudioDynamicStoreAuthority>> CaptureStudioDynamicStoreAuthorityAsync(
        BaseStudioDynamicStoreAuthorityRequest request, CancellationToken cancellationToken = default);
}

/// <summary>Creates and validates canonical dynamic authority evidence returned by providers.</summary>
public static class BaseStudioDynamicStoreAuthorityContract
{
    /// <summary>Returns whether request bounds and deadline are valid.</summary>
    public static bool IsValid(BaseStudioDynamicStoreAuthorityRequest? request) => request is not null &&
        !string.IsNullOrWhiteSpace(request.ApplicationId) && request.MaximumEvidenceBytes is > 0 and <= 65_536 &&
        request.MaximumTransientBytes >= request.MaximumEvidenceBytes && request.MaximumTransientBytes <= 262_144 &&
        request.Deadline > TimeSpan.Zero && request.Deadline <= TimeSpan.FromSeconds(5);

    /// <summary>Creates one deeply owned dynamic capture.</summary>
    public static BaseStudioDynamicStoreAuthority Create(string applicationId, string storeId, long restoreEpoch, long schemaGeneration)
    {
        if (string.IsNullOrWhiteSpace(applicationId) || string.IsNullOrWhiteSpace(storeId) || restoreEpoch < 0 || schemaGeneration < 0)
            throw new ArgumentException("Dynamic Studio store authority is invalid.");
        byte[] checksum = Hash(applicationId, storeId, restoreEpoch, schemaGeneration);
        int evidence = checked(Encoding.UTF8.GetByteCount(applicationId) + Encoding.UTF8.GetByteCount(storeId) + 16 + checksum.Length);
        return new() { ApplicationId = new(applicationId.AsSpan()), StoreInstanceId = new(storeId.AsSpan()), RestoreEpoch = restoreEpoch,
            SchemaGeneration = schemaGeneration, EvidenceChecksum = [.. checksum],
            Accounting = new() { AuthorityReads = 1, EvidenceBytes = evidence, TransientBytes = evidence } };
    }

    /// <summary>Validates exact request correspondence, accounting, bounds, and evidence checksum.</summary>
    public static bool IsValidResult(BaseStudioDynamicStoreAuthorityRequest request, BaseStudioDynamicStoreAuthority? value)
    {
        if (!IsValid(request) || value is null || !StringComparer.Ordinal.Equals(request.ApplicationId, value.ApplicationId) ||
            string.IsNullOrWhiteSpace(value.StoreInstanceId) || value.RestoreEpoch < 0 || value.SchemaGeneration < 0 ||
            value.EvidenceChecksum.Length != 32 || value.Accounting is null || value.Accounting.AuthorityReads != 1 ||
            value.Accounting.EvidenceBytes is < 1 || value.Accounting.TransientBytes < value.Accounting.EvidenceBytes ||
            value.Accounting.EvidenceBytes > request.MaximumEvidenceBytes || value.Accounting.TransientBytes > request.MaximumTransientBytes) return false;
        BaseStudioDynamicStoreAuthority expected = Create(value.ApplicationId, value.StoreInstanceId, value.RestoreEpoch, value.SchemaGeneration);
        return CryptographicOperations.FixedTimeEquals(value.EvidenceChecksum.AsSpan(), expected.EvidenceChecksum.AsSpan()) && value.Accounting == expected.Accounting;
    }

    private static byte[] Hash(string applicationId, string storeId, long restore, long schema)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Add("base.studio.dynamic-store-authority.v1"); Add(applicationId); Add(storeId); Number(restore); Number(schema); return hash.GetHashAndReset();
        void Add(string value) { byte[] bytes = Encoding.UTF8.GetBytes(value); Span<byte> length = stackalloc byte[4];
            System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length); hash.AppendData(length); hash.AppendData(bytes); }
        void Number(long value) { Span<byte> bytes = stackalloc byte[8]; System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(bytes, value); hash.AppendData(bytes); }
    }
}
