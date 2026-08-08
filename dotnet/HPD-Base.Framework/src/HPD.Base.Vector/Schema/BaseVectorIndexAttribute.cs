namespace HPD.Base;

/// <summary>Declares one generated vector index over an immutable <see cref="BaseVector"/> field.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class BaseVectorIndexAttribute(
    string id,
    string vectorField) : Attribute
{
    /// <summary>Gets the stable vector-index identifier.</summary>
    public string Id { get; } = id;
    /// <summary>Gets the source property containing the vector.</summary>
    public string VectorField { get; } = vectorField;
    /// <summary>Gets or sets the stable semantic vector-space identifier.</summary>
    public required string VectorSpace { get; set; }
    /// <summary>Gets or sets the exact vector dimensions.</summary>
    public required int Dimensions { get; set; }
    /// <summary>Gets or sets the portable comparison function.</summary>
    public BaseVectorFunction Function { get; set; } = BaseVectorFunction.CosineSimilarity;
    /// <summary>Gets or sets the source properties available to pre-ranking filtering.</summary>
    public string[] FilterFields { get; set; } = [];
}

/// <summary>Contains one generated closed vector-index definition.</summary>
/// <typeparam name="T">The generated record payload type.</typeparam>
public sealed record BaseVectorIndex<T>
{
    /// <summary>Gets the stable collection identifier.</summary>
    public required string CollectionId { get; init; }
    /// <summary>Gets the stable vector-index identifier.</summary>
    public required string Id { get; init; }
    /// <summary>Gets the stable vector-field identifier.</summary>
    public required string VectorFieldId { get; init; }
    /// <summary>Gets the stable semantic vector-space identifier.</summary>
    public required string VectorSpaceId { get; init; }
    /// <summary>Gets the exact vector dimensions.</summary>
    public required int Dimensions { get; init; }
    /// <summary>Gets the declared comparison function.</summary>
    public required BaseVectorFunction Function { get; init; }
    /// <summary>Gets the stable filter-field identifiers.</summary>
    public required string[] FilterFieldIds { get; init; }
}
