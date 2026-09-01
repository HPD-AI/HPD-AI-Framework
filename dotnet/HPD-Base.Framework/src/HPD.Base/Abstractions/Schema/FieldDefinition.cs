using System.Text.Json;

namespace HPD.Base;
/// <summary>Represents field Definition.</summary>
public sealed record FieldDefinition
{
    /// <summary>Gets or sets id.</summary>
    public required string Id { get; init; }
    /// <summary>Gets the exact application-facing property identity.</summary>
    public required string ApplicationName { get; init; }
    /// <summary>Gets the exact serializer-owned wire identity.</summary>
    public required string WireName { get; init; }
    /// <summary>Gets or sets display Name.</summary>
    public string? DisplayName { get; init; }
    /// <summary>Gets or sets type.</summary>
    public required string Type { get; init; }
    /// <summary>Gets or sets format.</summary>
    public string? Format { get; init; }
    /// <summary>Gets or sets cardinality.</summary>
    public CardinalityDescriptor? Cardinality { get; init; }
    /// <summary>Gets the independent canonical field-presence contract.</summary>
    public BaseFieldPresence Presence { get; init; } = BaseFieldPresence.Optional;
    /// <summary>Gets the independent canonical explicit-null contract.</summary>
    public BaseFieldNullability Nullability { get; init; } = BaseFieldNullability.Nullable;
    /// <summary>Gets the closed scalar kind when the field participates in L54 authority.</summary>
    public BaseScalarKind? ScalarKind { get; init; }
    /// <summary>Gets the graph-owned scalar codec authority.</summary>
    public BaseScalarCodecAuthority? ScalarCodec { get; init; }
    /// <summary>Gets the complete canonical scalar constraints.</summary>
    public BaseScalarConstraintSet? ScalarConstraints { get; init; }
    /// <summary>Gets the purpose-bound scalar-constraint checksum.</summary>
    public BaseScalarConstraintChecksum? ScalarConstraintChecksum { get; init; }
    /// <summary>Gets the exact target collection for a typed record-ID scalar.</summary>
    public string? RecordTargetCollectionId { get; init; }
    /// <summary>Gets or sets system.</summary>
    public bool System { get; init; }
    /// <summary>Gets or sets hidden.</summary>
    public bool Hidden { get; init; }
    /// <summary>Gets or sets read Only.</summary>
    public bool ReadOnly { get; init; }
    /// <summary>Gets the maximum disclosure classification.</summary>
    public BaseFieldConfidentiality Confidentiality { get; init; } = BaseFieldConfidentiality.Public;
    /// <summary>Gets the normalized complete disclosure policy.</summary>
    public BaseFieldDisclosurePolicy? Disclosure { get; init; }
    /// <summary>Gets the decoded byte minimum for a binary field.</summary>
    public int? MinimumBytes { get; init; }
    /// <summary>Gets the decoded byte maximum for a binary field.</summary>
    public int? MaximumBytes { get; init; }
    /// <summary>Gets or sets default.</summary>
    public DefaultValueDescriptor? Default { get; init; }
    /// <summary>Gets or sets generated.</summary>
    public GenerationDescriptor? Generated { get; init; }
    /// <summary>Gets or sets relation.</summary>
    public RelationDefinition? Relation { get; init; }
    /// <summary>Gets the scalar exported-subject reference contract, when declared.</summary>
    public BaseSubjectReferenceDefinition? SubjectReference { get; init; }
    /// <summary>Gets or sets file.</summary>
    public FileAnnotation? File { get; init; }
    /// <summary>Gets or sets visibility.</summary>
    public FieldVisibilityAnnotation? Visibility { get; init; }
    /// <summary>Gets or sets ui.</summary>
    public UiAnnotation? Ui { get; init; }
    /// <summary>Gets or sets sdk.</summary>
    public SdkAnnotation? Sdk { get; init; }
    /// <summary>Gets or sets store.</summary>
    public StoreAnnotation? Store { get; init; }
    /// <summary>Gets or sets required Capabilities.</summary>
    public string[]? RequiredCapabilities { get; init; }
    /// <summary>Gets or sets extensions.</summary>
    public Dictionary<string, JsonElement>? Extensions { get; init; }
}
