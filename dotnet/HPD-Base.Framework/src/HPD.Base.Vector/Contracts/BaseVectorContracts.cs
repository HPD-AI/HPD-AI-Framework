namespace HPD.Base;

/// <summary>Identifies whether a larger or smaller vector measure is nearer.</summary>
public enum BaseVectorMeasureDirection
{
    /// <summary>A larger value is nearer.</summary>
    HigherIsNearer,
    /// <summary>A smaller value is nearer.</summary>
    LowerIsNearer,
}

/// <summary>Classifies whether vector ranking is mathematically exact or approximate.</summary>
public enum BaseVectorResultAccuracy
{
    /// <summary>The provider evaluated the complete eligible candidate relation.</summary>
    Exact,
    /// <summary>The provider used a declared approximate retrieval algorithm.</summary>
    Approximate,
}

/// <summary>Contains one finite, function-labeled vector measure.</summary>
public readonly record struct BaseVectorMeasure
{
    /// <summary>Gets the portable vector function.</summary>
    public required BaseVectorFunction Function { get; init; }
    /// <summary>Gets the finite measure value.</summary>
    public required double Value { get; init; }
    /// <summary>Gets the direction in which results become nearer.</summary>
    public required BaseVectorMeasureDirection Direction { get; init; }
    /// <summary>Gets optional provider-certified normalized relevance.</summary>
    public double? NormalizedRelevance { get; init; }
}

/// <summary>Contains one ranked, authoritative, policy-projected BASE record.</summary>
/// <typeparam name="T">The generated record payload type.</typeparam>
public sealed record BaseVectorMatch<T>
{
    /// <summary>Gets the authoritative projected record.</summary>
    public required BaseRecord<T> Record { get; init; }
    /// <summary>Gets the one-based final rank.</summary>
    public required int Rank { get; init; }
    /// <summary>Gets the labeled provider measure.</summary>
    public required BaseVectorMeasure Measure { get; init; }
}

/// <summary>Lists stable vector error codes.</summary>
public static class BaseVectorErrorCodes
{
    /// <summary>The vector value is invalid.</summary>
    public const string Invalid = "base.vector.invalid";
    /// <summary>The vector dimensions do not match the index.</summary>
    public const string DimensionMismatch = "base.vector.dimensionMismatch";
    /// <summary>The vector contains a non-finite element.</summary>
    public const string NonFinite = "base.vector.nonFinite";
    /// <summary>A cosine vector has zero norm.</summary>
    public const string ZeroNorm = "base.vector.zeroNorm";
    /// <summary>The requested bound exceeds a configured limit.</summary>
    public const string LimitExceeded = "base.vector.limitExceeded";
    /// <summary>The provider is unavailable.</summary>
    public const string ProviderUnavailable = "base.vector.providerUnavailable";
    /// <summary>The requested vector index was not found.</summary>
    public const string IndexNotFound = "base.vector.indexNotFound";
    /// <summary>The requested vector index is unavailable.</summary>
    public const string IndexUnavailable = "base.vector.indexUnavailable";
    /// <summary>The requested vector index is still building.</summary>
    public const string IndexBuilding = "base.vector.indexBuilding";
    /// <summary>The requested vector index is stale.</summary>
    public const string IndexStale = "base.vector.indexStale";
    /// <summary>The requested vector index requires an explicit rebuild.</summary>
    public const string RebuildRequired = "base.vector.rebuildRequired";
    /// <summary>The installed provider cannot supply the requested capability.</summary>
    public const string CapabilityUnavailable = "base.vector.capabilityUnavailable";
    /// <summary>The caller filter cannot be represented by this provider.</summary>
    public const string FilterUnsupported = "base.vector.filterUnsupported";
    /// <summary>The effective policy constraint cannot be enforced exactly.</summary>
    public const string PolicyConstraintUnsupported = "base.vector.policyConstraintUnsupported";
    /// <summary>The opaque consistency token is invalid.</summary>
    public const string ConsistencyInvalid = "base.vector.consistencyInvalid";
    /// <summary>The opaque consistency token expired.</summary>
    public const string ConsistencyExpired = "base.vector.consistencyExpired";
    /// <summary>The opaque consistency token belongs to another authority scope.</summary>
    public const string ConsistencyScopeMismatch = "base.vector.consistencyScopeMismatch";
    /// <summary>The requested consistency point cannot be satisfied.</summary>
    public const string ConsistencyUnavailable = "base.vector.consistencyUnavailable";
    /// <summary>The authority snapshot changed before hydration completed.</summary>
    public const string SnapshotChanged = "base.vector.snapshotChanged";
    /// <summary>The provider returned invalid result evidence.</summary>
    public const string ProviderResultInvalid = "base.vector.providerResultInvalid";
    /// <summary>The native provider is unavailable on the current platform.</summary>
    public const string ProviderUnsupportedPlatform = "base.vector.providerUnsupportedPlatform";
    /// <summary>Persistent token protection is required.</summary>
    public const string TokenProtectionRequired = "base.vector.tokenProtectionRequired";
    /// <summary>Authoritative candidate hydration failed.</summary>
    public const string HydrationFailed = "base.vector.hydrationFailed";
    /// <summary>The operation exceeded its deadline.</summary>
    public const string Timeout = "base.vector.timeout";
    /// <summary>The caller cancelled its wait.</summary>
    public const string Cancelled = "base.vector.cancelled";
    /// <summary>The requested rebuild conflicts with current index state.</summary>
    public const string RebuildConflict = "base.vector.rebuildConflict";
    /// <summary>The rebuild publication outcome cannot be determined safely.</summary>
    public const string RebuildIndeterminate = "base.vector.rebuildIndeterminate";
    /// <summary>The vector administration operation failed.</summary>
    public const string AdministrationFailed = "base.vector.administrationFailed";
}
