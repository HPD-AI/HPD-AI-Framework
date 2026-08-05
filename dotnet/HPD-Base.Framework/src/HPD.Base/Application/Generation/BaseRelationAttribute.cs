namespace HPD.Base;

/// <summary>Declares a generated relation stored by a typed record-id field.</summary>
/// <param name="id">The stable relation and owning-navigation identifier.</param>
/// <param name="targetRecordType">The target generated collection record type.</param>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class BaseRelationAttribute(string id, Type targetRecordType) : Attribute
{
    /// <summary>Gets the stable relation identifier.</summary>
    public string Id { get; } = id;

    /// <summary>Gets the target generated collection record type.</summary>
    public Type TargetRecordType { get; } = targetRecordType;

    /// <summary>Gets or sets the stable target field identifier.</summary>
    public string TargetFieldId { get; set; } = "base.recordId";

    /// <summary>Gets or sets local multiplicity.</summary>
    public BaseRelationMultiplicity LocalMultiplicity { get; set; } = BaseRelationMultiplicity.ZeroOrOne;

    /// <summary>Gets or sets inverse multiplicity.</summary>
    public BaseRelationMultiplicity InverseMultiplicity { get; set; } = BaseRelationMultiplicity.Many;

    /// <summary>Gets or sets the optional minimum number of targets for a many-valued relation.</summary>
    public int MinimumCount { get; set; } = -1;

    /// <summary>Gets or sets the optional maximum number of targets for a many-valued relation.</summary>
    public int MaximumCount { get; set; } = -1;

    /// <summary>Gets or sets the optional stable inverse-navigation identifier.</summary>
    public string? InverseNavigationId { get; set; }

    /// <summary>Gets or sets delete behavior.</summary>
    public BaseRelationDeleteBehavior DeleteBehavior { get; set; } = BaseRelationDeleteBehavior.Restrict;

    /// <summary>Gets or sets whether ordinary includes are allowed.</summary>
    public bool IncludeAllowed { get; set; }

    /// <summary>Gets or sets whether callers may filter this include.</summary>
    public bool IncludeFilterAllowed { get; set; }

    /// <summary>Gets or sets whether callers may sort this include.</summary>
    public bool IncludeSortAllowed { get; set; }

    /// <summary>Gets or sets an optional relation-specific include depth bound.</summary>
    public int IncludeMaximumDepth { get; set; } = -1;
}
