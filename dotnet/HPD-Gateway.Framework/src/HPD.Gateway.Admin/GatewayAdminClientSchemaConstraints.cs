using System.Collections.Immutable;
using HPD.Gateway.Effective;
using HPD.Gateway.Management;
using HPD.Gateway.Status;

namespace HPD.Gateway.Admin;

internal enum GatewayAdminClientSchemaConstraintTarget : byte { Value, Collection, Items }
internal enum GatewayAdminClientSchemaValueKind : byte { String, CandidateId, StringArray }

internal sealed record GatewayAdminClientSchemaConstraint(
    Type SchemaType,
    string PropertyName,
    GatewayAdminClientSchemaValueKind ValueKind,
    GatewayAdminClientSchemaConstraintTarget AppliesTo,
    GatewayAdminClientStringBrand Brand,
    GatewayAdminClientConstraintRules Rules)
{
    internal const int MaximumCandidateUtf8Bytes = 4 * 1024 * 1024;

    internal void Validate()
    {
        ArgumentNullException.ThrowIfNull(SchemaType);
        if (!Enum.IsDefined(ValueKind) || !Enum.IsDefined(AppliesTo) || !Enum.IsDefined(Brand) ||
            string.IsNullOrWhiteSpace(PropertyName) || Rules is null)
            throw new InvalidOperationException("Gateway client schema constraint target is invalid.");
        if (AppliesTo == GatewayAdminClientSchemaConstraintTarget.Collection && ValueKind != GatewayAdminClientSchemaValueKind.StringArray)
            throw new InvalidOperationException("Collection rules require an immutable-array property.");
        if (AppliesTo == GatewayAdminClientSchemaConstraintTarget.Items && ValueKind != GatewayAdminClientSchemaValueKind.StringArray)
            throw new InvalidOperationException("Item rules require an immutable-array property.");
        if (Brand != GatewayAdminClientStringBrand.None && ValueKind is not
            (GatewayAdminClientSchemaValueKind.String or GatewayAdminClientSchemaValueKind.CandidateId or GatewayAdminClientSchemaValueKind.StringArray))
            throw new InvalidOperationException("Gateway identity brands require a string-compatible target.");
        if (AppliesTo == GatewayAdminClientSchemaConstraintTarget.Collection && Brand != GatewayAdminClientStringBrand.None)
            throw new InvalidOperationException("Collection rules cannot carry an identity brand.");

        ValidateRules();
    }

    private void ValidateRules()
    {
        if (!Enum.IsDefined(Rules.Normalization) || !Enum.IsDefined(Rules.CharacterSet) ||
            !Enum.IsDefined(Rules.Uniqueness) || !Enum.IsDefined(Rules.Ordering) ||
            !Enum.IsDefined(Rules.Cardinality))
            throw new InvalidOperationException("Gateway client schema rules contain an unknown value.");
        if (Rules.MinimumUtf8Bytes is < 0 || Rules.MaximumUtf8Bytes is < 1 or > MaximumCandidateUtf8Bytes ||
            Rules.MinimumUtf8Bytes > Rules.MaximumUtf8Bytes)
            throw new InvalidOperationException("Gateway client schema byte bounds are invalid.");
        if (Rules.CollectionMinimum is < 0 ||
            Rules.CollectionMaximum is < 1 or > GatewayAdminClientParameterConstraint.MaximumCollectionItems ||
            Rules.CollectionMinimum > Rules.CollectionMaximum)
            throw new InvalidOperationException("Gateway client schema collection bounds are invalid.");
        if (AppliesTo == GatewayAdminClientSchemaConstraintTarget.Collection)
        {
            if (Rules.Cardinality != GatewayAdminClientCardinality.Multiple ||
                Rules.MinimumUtf8Bytes is not null || Rules.MaximumUtf8Bytes is not null)
                throw new InvalidOperationException("Collection targets require collection-only rules.");
            return;
        }
        if (Rules.Cardinality != GatewayAdminClientCardinality.Single ||
            Rules.CollectionMinimum is not null || Rules.CollectionMaximum is not null ||
            Rules.Uniqueness != GatewayAdminClientUniqueness.None || Rules.Ordering != GatewayAdminClientOrdering.None)
            throw new InvalidOperationException("Value and item targets require scalar-only rules.");
        if (Rules.MaximumUtf8Bytes is null)
            throw new InvalidOperationException("Gateway client string schema targets require an explicit UTF-8 maximum.");
    }
}

internal static class GatewayAdminClientSchemaConstraintLedger
{
    private static readonly GatewayAdminClientConstraintRules Resource = new(
        1, 128, GatewayAdminClientNormalization.Nfc, RejectUnicodeControls: true);
    private static readonly GatewayAdminClientConstraintRules Ordinary = new(MaximumUtf8Bytes: 16 * 1024);
    private static readonly GatewayAdminClientConstraintRules CandidateText = new(1, 4 * 1024 * 1024);
    private static readonly GatewayAdminClientConstraintRules VisibleIdentity = new(
        1, 128, CharacterSet: GatewayAdminClientCharacterSet.VisibleAscii);
    private static readonly GatewayAdminClientConstraintRules DesiredToken = new(
        1, 512, CharacterSet: GatewayAdminClientCharacterSet.VisibleAscii);
    private static readonly GatewayAdminClientConstraintRules Cursor = new(MaximumUtf8Bytes: 4096);
    private static readonly GatewayAdminClientConstraintRules LowercaseName = new(
        1, 128, CharacterSet: GatewayAdminClientCharacterSet.LowercaseAsciiName);
    private static readonly GatewayAdminClientConstraintRules ArtifactLabel = new(
        1, 128, CharacterSet: GatewayAdminClientCharacterSet.AsciiArtifactLabel);
    private static readonly GatewayAdminClientConstraintRules PurgeCollection = new(
        CollectionMinimum: 1, CollectionMaximum: 256,
        Uniqueness: GatewayAdminClientUniqueness.Ordinal,
        Ordering: GatewayAdminClientOrdering.OrdinalAscending,
        Cardinality: GatewayAdminClientCardinality.Multiple);

    internal static ImmutableArray<GatewayAdminClientSchemaConstraint> V1 { get; } = Validate(
    [
        V<GatewayRevisionRequest>("configurationJson", rules: CandidateText),
        V<GatewayRevisionRequest>("sourceKind", rules: Resource),
        V<GatewayRevisionRequest>("sourceId", rules: Resource),
        V<GatewayCompareRequest>("leftRevisionId", GatewayAdminClientStringBrand.RevisionId, Resource),
        V<GatewayCompareRequest>("rightRevisionId", GatewayAdminClientStringBrand.RevisionId, Resource),
        V<GatewayImportRequest>("configurationJson", rules: CandidateText),
        V<GatewayImportRequest>("sourceId", rules: Resource),
        V<GatewayBackupRequest>("sinkName", rules: LowercaseName),
        V<GatewayBackupRequest>("artifactLabel", rules: ArtifactLabel),
        C<GatewayPurgeRequest>("resourceIds", PurgeCollection),
        I<GatewayPurgeRequest>("resourceIds", rules: Resource),

        V<GatewayDesiredProjection>("targetNodeId", GatewayAdminClientStringBrand.TargetNodeId, Resource),
        V<GatewayDesiredProjection>("namespaceId", GatewayAdminClientStringBrand.NamespaceId, Resource),
        V<GatewayDesiredProjection>("revisionId", GatewayAdminClientStringBrand.RevisionId, Resource),
        V<GatewayDesiredProjection>("candidateId", GatewayAdminClientStringBrand.CandidateId, Resource),
        V<GatewayDesiredProjection>("desiredStateToken", GatewayAdminClientStringBrand.DesiredStateToken, DesiredToken),
        V<GatewayActivationProjection>("revisionId", GatewayAdminClientStringBrand.RevisionId, Resource),
        V<GatewayActivationProjection>("candidateId", GatewayAdminClientStringBrand.CandidateId, Resource),
        V<GatewayExportResponse>("revisionId", GatewayAdminClientStringBrand.RevisionId, Resource),
        V<GatewayAdministrativeResponse>("operationId", GatewayAdminClientStringBrand.OperationId, Resource),
        V<GatewayRevisionProjection>("revisionId", GatewayAdminClientStringBrand.RevisionId, Resource),
        V<GatewayRevisionProjection>("parentRevisionId", GatewayAdminClientStringBrand.RevisionId, Resource),
        V<GatewayRevisionProjection>("derivedFromRevisionId", GatewayAdminClientStringBrand.RevisionId, Resource),
        V<GatewayRevisionProjection>("validationId", GatewayAdminClientStringBrand.ValidationId, Resource),
        V<GatewayValidationProjection>("validationId", GatewayAdminClientStringBrand.ValidationId, Resource),
        V<GatewayOperationProjection>("operationId", GatewayAdminClientStringBrand.OperationId, Resource),
        V<GatewayOperationProjection>("desiredStateToken", GatewayAdminClientStringBrand.DesiredStateToken, DesiredToken),
        V<GatewayAuditProjection>("correlationId", GatewayAdminClientStringBrand.CorrelationId, VisibleIdentity),
        V<GatewayProvisionResponse>("operationId", GatewayAdminClientStringBrand.OperationId, Resource),
        V<GatewayRevisionResponse>("revisionId", GatewayAdminClientStringBrand.RevisionId, Resource),
        V<GatewayRevisionResponse>("desiredStateToken", GatewayAdminClientStringBrand.DesiredStateToken, DesiredToken),
        V<GatewayValidationResponse>("correlationId", GatewayAdminClientStringBrand.CorrelationId, VisibleIdentity),
        V<GatewayAdminError>("correlationId", GatewayAdminClientStringBrand.CorrelationId, VisibleIdentity),
        V<GatewayRevisionComparison>("leftRevisionId", GatewayAdminClientStringBrand.RevisionId, Resource),
        V<GatewayRevisionComparison>("rightRevisionId", GatewayAdminClientStringBrand.RevisionId, Resource),
        V<GatewayPreparationStatus>("candidateId", GatewayAdminClientStringBrand.CandidateId, Resource),
        V<GatewayActiveConfigurationIdentity>("candidateId", GatewayAdminClientStringBrand.CandidateId, Resource),
        V<GatewayPublicationStatus>("attemptedCandidateId", GatewayAdminClientStringBrand.CandidateId, Resource),
        VC<GatewayEffectiveSnapshot>("candidateId", GatewayAdminClientStringBrand.CandidateId, Resource),

        V<GatewayAdminPage<GatewayRevisionProjection>>("continuationToken", GatewayAdminClientStringBrand.ContinuationToken, Cursor),
        V<GatewayAdminPage<GatewayAuditProjection>>("continuationToken", GatewayAdminClientStringBrand.ContinuationToken, Cursor),
        V<GatewayAdminPage<GatewayActivationProjection>>("continuationToken", GatewayAdminClientStringBrand.ContinuationToken, Cursor),
        V<GatewayAdminPage<GatewayOutcomeProjection>>("continuationToken", GatewayAdminClientStringBrand.ContinuationToken, Cursor),
    ]);

    internal static ImmutableArray<GatewayAdminClientSchemaConstraint> For(Type declaringType, string propertyName) =>
        V1.Where(value => value.SchemaType == declaringType && StringComparer.Ordinal.Equals(value.PropertyName, propertyName))
            .ToImmutableArray();

    private static GatewayAdminClientSchemaConstraint V<T>(string property, GatewayAdminClientStringBrand brand = GatewayAdminClientStringBrand.None,
        GatewayAdminClientConstraintRules? rules = null) =>
        new(typeof(T), property, GatewayAdminClientSchemaValueKind.String,
            GatewayAdminClientSchemaConstraintTarget.Value, brand, rules ?? Ordinary);
    private static GatewayAdminClientSchemaConstraint VC<T>(string property, GatewayAdminClientStringBrand brand,
        GatewayAdminClientConstraintRules rules) =>
        new(typeof(T), property, GatewayAdminClientSchemaValueKind.CandidateId,
            GatewayAdminClientSchemaConstraintTarget.Value, brand, rules);
    private static GatewayAdminClientSchemaConstraint C<T>(string property, GatewayAdminClientConstraintRules rules) =>
        new(typeof(T), property, GatewayAdminClientSchemaValueKind.StringArray,
            GatewayAdminClientSchemaConstraintTarget.Collection, GatewayAdminClientStringBrand.None, rules);
    private static GatewayAdminClientSchemaConstraint I<T>(string property, GatewayAdminClientStringBrand brand = GatewayAdminClientStringBrand.None,
        GatewayAdminClientConstraintRules? rules = null) =>
        new(typeof(T), property, GatewayAdminClientSchemaValueKind.StringArray,
            GatewayAdminClientSchemaConstraintTarget.Items, brand, rules ?? Ordinary);

    private static ImmutableArray<GatewayAdminClientSchemaConstraint> Validate(ImmutableArray<GatewayAdminClientSchemaConstraint> constraints)
    {
        var targets = new HashSet<string>(StringComparer.Ordinal);
        foreach (GatewayAdminClientSchemaConstraint constraint in constraints)
        {
            constraint.Validate();
            string target = $"{constraint.SchemaType.AssemblyQualifiedName}|{constraint.PropertyName}|{(byte)constraint.AppliesTo}";
            if (!targets.Add(target))
                throw new InvalidOperationException("Gateway client schema constraint targets must be unique.");
        }
        return constraints;
    }
}
