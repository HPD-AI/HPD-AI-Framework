namespace HPD.Base;

/// <summary>Identifies one portable vector comparison function.</summary>
public enum BaseVectorFunction
{
    /// <summary>Ranks larger cosine similarity as nearer.</summary>
    CosineSimilarity,
    /// <summary>Ranks larger dot-product similarity as nearer.</summary>
    DotProductSimilarity,
    /// <summary>Ranks smaller Euclidean distance as nearer.</summary>
    EuclideanDistance,
}

/// <summary>Contains one provider-neutral logical vector-index definition.</summary>
public sealed record VectorIndexDefinition
{
    /// <summary>Gets the stable vector-index identifier.</summary>
    public required string Id { get; init; }
    /// <summary>Gets the stable collection identifier.</summary>
    public required string CollectionId { get; init; }
    /// <summary>Gets the stable vector-field identifier.</summary>
    public required string VectorFieldId { get; init; }
    /// <summary>Gets the stable semantic vector-space identifier.</summary>
    public required string VectorSpaceId { get; init; }
    /// <summary>Gets the exact vector dimensions.</summary>
    public required int Dimensions { get; init; }
    /// <summary>Gets the portable comparison function.</summary>
    public required BaseVectorFunction Function { get; init; }
    /// <summary>Gets the stable fields permitted in pre-ranking constraints.</summary>
    public required string[] FilterFieldIds { get; init; }
}
