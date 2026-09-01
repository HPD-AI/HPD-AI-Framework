namespace HPD.Base;

/// <summary>Declares one stable node in a generated logical-index membership predicate.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class BaseIndexPredicateAttribute(string indexId, string nodeId, BaseIndexPredicateNodeKind kind) : Attribute
{
    /// <summary>Gets the owning logical index ID.</summary>
    public string IndexId { get; } = indexId;
    /// <summary>Gets the stable predicate node ID.</summary>
    public string NodeId { get; } = nodeId;
    /// <summary>Gets the closed predicate node kind.</summary>
    public BaseIndexPredicateNodeKind Kind { get; } = kind;
    /// <summary>Gets or sets the referenced CLR property.</summary>
    public string? Field { get; set; }
    /// <summary>Gets or sets ordered child node IDs.</summary>
    public string[] Children { get; set; } = [];
    /// <summary>Gets or sets the exact canonical JSON scalar token used by an Equal node.</summary>
    public string? Literal { get; set; }
}
