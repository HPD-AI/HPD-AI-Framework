
namespace HPD.Base;

/// <summary>Represents a policy evaluation request.</summary>
public sealed record PolicyEvaluationRequest
{
    /// <summary>Gets or sets the operation.</summary>
    public required OperationContext Operation { get; init; }
    /// <summary>Gets or sets the principal.</summary>
    public required PrincipalContext Principal { get; init; }
    /// <summary>Gets or sets the collection.</summary>
    public CollectionDefinition? Collection { get; init; }
    /// <summary>Gets or sets the resource.</summary>
    public required PolicyResource Resource { get; init; }
    /// <summary>Gets or sets the grants.</summary>
    public AccessGrant[]? Grants { get; init; }
    /// <summary>Gets or sets the policy refs.</summary>
    public Dictionary<string, string>? PolicyRefs { get; init; }
}

/// <summary>Represents a policy resource.</summary>
public sealed record PolicyResource
{
    /// <summary>Gets or sets the kind.</summary>
    public required PolicyResourceKind Kind { get; init; }
    /// <summary>Gets or sets the query.</summary>
    public RecordQuery? Query { get; init; }
    /// <summary>Gets or sets the existing record.</summary>
    public RecordEnvelope? ExistingRecord { get; init; }
    /// <summary>Gets or sets the proposed payload.</summary>
    public RecordPayload? ProposedPayload { get; init; }
    /// <summary>Gets or sets the proposed record.</summary>
    public RecordEnvelope? ProposedRecord { get; init; }
    /// <summary>Gets or sets the record ID.</summary>
    public string? RecordId { get; init; }
    /// <summary>Gets or sets the field path.</summary>
    public string? FieldPath { get; init; }
    /// <summary>Gets the stable vector-index identifier for vector resources.</summary>
    public string? VectorIndexId { get; init; }
    /// <summary>Gets the stable vector-space identifier for vector resources.</summary>
    public string? VectorSpaceId { get; init; }
    /// <summary>Gets the stable lexical-index identifier for lexical resources.</summary>
    public string? TextIndexId { get; init; }
    /// <summary>Gets the stable exported logical-subject contract identifier.</summary>
    public string? SubjectContractId { get; init; }
    /// <summary>Gets the positive exported logical-subject contract version.</summary>
    public int? SubjectContractVersion { get; init; }
    /// <summary>Gets the exact fixed Studio operation identity.</summary>
    public string? StudioOperationId { get; init; }
    /// <summary>Gets the owning Studio module identity.</summary>
    public string? StudioModuleId { get; init; }
    /// <summary>Gets the closed Studio resource-kind discriminator.</summary>
    public string? StudioResourceKind { get; init; }
    /// <summary>Gets the exact opaque Studio resource identity.</summary>
    public string? StudioResourceIdentity { get; init; }
}
