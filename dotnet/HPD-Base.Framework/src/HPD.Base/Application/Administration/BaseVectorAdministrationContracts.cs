using System.Security.Cryptography;
using System.Text;

namespace HPD.Base;

/// <summary>Contains one owned, opaque, authenticated vector consistency token.</summary>
[System.Text.Json.Serialization.JsonConverter(typeof(BaseVectorConsistencyTokenJsonConverter))]
public readonly struct BaseVectorConsistencyToken : IEquatable<BaseVectorConsistencyToken>
{
    private readonly string? _encoded;
    private BaseVectorConsistencyToken(string encoded) => _encoded = new string(encoded.AsSpan());
    /// <summary>Parses bounded ASCII wire shape without authenticating it.</summary>
    public static BaseVectorConsistencyToken Parse(string encoded) => TryParse(encoded, out var token) ? token : throw new FormatException("The vector consistency token is malformed.");
    /// <summary>Attempts to parse bounded ASCII wire shape without authenticating it.</summary>
    public static bool TryParse(string? encoded, out BaseVectorConsistencyToken token)
    { token = default; if (string.IsNullOrEmpty(encoded) || encoded.Length > 2048 || encoded.Any(static c => c is < '!' or > '~')) return false; token = new(encoded); return true; }
    /// <summary>Returns the owned encoded token.</summary>
    public string Encode() => _encoded ?? throw new InvalidOperationException("The default vector consistency token is invalid.");
    /// <inheritdoc />
    public bool Equals(BaseVectorConsistencyToken other)
    { if (_encoded is null || other._encoded is null) return false; byte[] left = Encoding.ASCII.GetBytes(_encoded); byte[] right = Encoding.ASCII.GetBytes(other._encoded); return left.Length == right.Length && CryptographicOperations.FixedTimeEquals(left, right); }
    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is BaseVectorConsistencyToken other && Equals(other);
    /// <inheritdoc />
    public override int GetHashCode() => _encoded is null ? 0 : StringComparer.Ordinal.GetHashCode(_encoded);
    /// <inheritdoc />
    public override string ToString() => "BaseVectorConsistencyToken[redacted]";
}

/// <summary>Binds vector ranking and hydration to one finite authoritative snapshot.</summary>
public sealed record BaseVectorAuthoritySnapshot
{
    /// <summary>Gets the store identity digest.</summary>
    public required string StoreIdentityDigest { get; init; }
    /// <summary>Gets the restore epoch.</summary>
    public required long RestoreEpoch { get; init; }
    /// <summary>Gets the schema generation.</summary>
    public required long SchemaGeneration { get; init; }
    /// <summary>Gets the collection identifier.</summary>
    public required string CollectionId { get; init; }
    /// <summary>Gets the collection purge generation.</summary>
    public required long PurgeGeneration { get; init; }
    /// <summary>Gets the vector-index identifier.</summary>
    public required string VectorIndexId { get; init; }
    /// <summary>Gets the vector-index generation.</summary>
    public required long VectorIndexGeneration { get; init; }
    /// <summary>Gets the vector-space identifier.</summary>
    public required string VectorSpaceId { get; init; }
    /// <summary>Gets the finite mutation-journal high-watermark.</summary>
    public required BaseMutationJournalPosition HighWatermark { get; init; }
}

/// <summary>Requests a generation-safe rebuild of one vector index.</summary>
public sealed record BaseVectorRebuildRequest
{
    /// <summary>Gets the configured store identifier.</summary>
    public required string StoreId { get; init; }
    /// <summary>Gets the principal requesting the operation.</summary>
    public required PrincipalContext Principal { get; init; }
    /// <summary>Gets the collection identifier.</summary>
    public required string CollectionId { get; init; }
    /// <summary>Gets the vector-index identifier.</summary>
    public required string VectorIndexId { get; init; }
    /// <summary>Gets the expected published index generation.</summary>
    public required long ExpectedGeneration { get; init; }
    /// <summary>Gets the expected collection purge generation.</summary>
    public required long ExpectedPurgeGeneration { get; init; }
    /// <summary>Gets the exact destructive confirmation phrase.</summary>
    public required string Confirmation { get; init; }
}

/// <summary>Describes one successfully published vector-index generation.</summary>
public sealed record BaseVectorRebuildResult
{
    /// <summary>Gets the configured store identifier.</summary>
    public required string StoreId { get; init; }
    /// <summary>Gets the collection identifier.</summary>
    public required string CollectionId { get; init; }
    /// <summary>Gets the vector-index identifier.</summary>
    public required string VectorIndexId { get; init; }
    /// <summary>Gets the previous generation.</summary>
    public required long PreviousGeneration { get; init; }
    /// <summary>Gets the newly published generation.</summary>
    public required long PublishedGeneration { get; init; }
    /// <summary>Gets the source authority snapshot.</summary>
    public required BaseVectorAuthoritySnapshot SourceSnapshot { get; init; }
    /// <summary>Gets the consistency evidence through which the rebuild was applied.</summary>
    public required BaseVectorConsistencyToken AppliedThrough { get; init; }
    /// <summary>Gets the completion time.</summary>
    public required DateTimeOffset CompletedAt { get; init; }
}

/// <summary>Executes the vector-specific portion of canonical BASE administration.</summary>
internal interface IBaseVectorRebuildService
{
    /// <summary>Rebuilds and atomically publishes one vector-index generation.</summary>
    ValueTask<OperationResult<BaseVectorRebuildResult>> RebuildAsync(BaseVectorRebuildRequest request, CancellationToken cancellationToken = default);
}
