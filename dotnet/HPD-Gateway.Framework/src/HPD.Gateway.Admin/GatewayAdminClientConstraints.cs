using System.Collections.Immutable;

namespace HPD.Gateway.Admin;

internal enum GatewayAdminClientParameterLocation : byte { Path, Query, Header }
internal enum GatewayAdminClientStringBrand : byte
{
    None, NamespaceId, TargetNodeId, RevisionId, ValidationId, OperationId,
    CandidateId, ContinuationToken, DesiredStateToken, IdempotencyKey, CorrelationId,
}
internal enum GatewayAdminClientNormalization : byte { None, Nfc }
internal enum GatewayAdminClientCharacterSet : byte
{
    Unicode, VisibleAscii, LowercaseAsciiName, AsciiArtifactLabel, StrongEntityTag,
}
internal enum GatewayAdminClientUniqueness : byte { None, Ordinal, OrdinalIgnoreCase }
internal enum GatewayAdminClientOrdering : byte { None, OrdinalAscending, NumericAscending }
internal enum GatewayAdminClientCardinality : byte { Single, Multiple }

internal sealed record GatewayAdminClientConstraintRules(
    int? MinimumUtf8Bytes = null,
    int? MaximumUtf8Bytes = null,
    GatewayAdminClientNormalization Normalization = GatewayAdminClientNormalization.None,
    GatewayAdminClientCharacterSet CharacterSet = GatewayAdminClientCharacterSet.Unicode,
    bool RejectUnicodeControls = false,
    int? CollectionMinimum = null,
    int? CollectionMaximum = null,
    GatewayAdminClientUniqueness Uniqueness = GatewayAdminClientUniqueness.None,
    GatewayAdminClientOrdering Ordering = GatewayAdminClientOrdering.None,
    GatewayAdminClientCardinality Cardinality = GatewayAdminClientCardinality.Single);

internal sealed record GatewayAdminClientParameterConstraint(
    GatewayAdminClientParameterLocation Location,
    string Name,
    bool Required,
    GatewayAdminClientStringBrand Brand,
    GatewayAdminClientConstraintRules Rules)
{
    internal const int MaximumOrdinaryStringUtf8Bytes = 16 * 1024;
    internal const int MaximumCollectionItems = 10_000;

    internal void Validate()
    {
        if (!Enum.IsDefined(Location) || !Enum.IsDefined(Brand) || string.IsNullOrWhiteSpace(Name) ||
            Name.Any(static value => value is < '!' or > '~'))
            throw new InvalidOperationException("Gateway client parameter target is invalid.");
        if (Rules is null)
            throw new InvalidOperationException("Gateway client parameter rules are required.");
        if (Location == GatewayAdminClientParameterLocation.Path && !Required)
            throw new InvalidOperationException("Gateway client path parameters must be required.");
        if (!Enum.IsDefined(Rules.Normalization) || !Enum.IsDefined(Rules.CharacterSet) ||
            !Enum.IsDefined(Rules.Uniqueness) || !Enum.IsDefined(Rules.Ordering) ||
            !Enum.IsDefined(Rules.Cardinality))
            throw new InvalidOperationException("Gateway client parameter rules contain an unknown value.");
        if (Rules.MinimumUtf8Bytes is < 0 || Rules.MaximumUtf8Bytes is < 1 or > MaximumOrdinaryStringUtf8Bytes ||
            Rules.MinimumUtf8Bytes > Rules.MaximumUtf8Bytes)
            throw new InvalidOperationException("Gateway client parameter byte bounds are invalid.");
        if (Rules.CollectionMinimum is < 0 || Rules.CollectionMaximum is < 1 or > MaximumCollectionItems ||
            Rules.CollectionMinimum > Rules.CollectionMaximum)
            throw new InvalidOperationException("Gateway client parameter collection bounds are invalid.");
        if (Rules.Cardinality == GatewayAdminClientCardinality.Single &&
            (Rules.CollectionMinimum is not null || Rules.CollectionMaximum is not null ||
             Rules.Uniqueness != GatewayAdminClientUniqueness.None || Rules.Ordering != GatewayAdminClientOrdering.None))
            throw new InvalidOperationException("Single Gateway client parameters cannot declare collection semantics.");

        bool paginationMaximum = Location == GatewayAdminClientParameterLocation.Query &&
            StringComparer.Ordinal.Equals(Name, "maximum") && Brand == GatewayAdminClientStringBrand.None;
        if (!paginationMaximum && Rules.MaximumUtf8Bytes is null)
            throw new InvalidOperationException("Gateway client string parameters require an explicit UTF-8 byte maximum.");
        if (Brand == GatewayAdminClientStringBrand.None != paginationMaximum)
            throw new InvalidOperationException("Gateway client parameter brand is incompatible with its target.");
        if (Rules.CharacterSet == GatewayAdminClientCharacterSet.StrongEntityTag &&
            (Brand != GatewayAdminClientStringBrand.DesiredStateToken ||
             Location != GatewayAdminClientParameterLocation.Header ||
             Rules.MinimumUtf8Bytes is null || Rules.MaximumUtf8Bytes is null))
            throw new InvalidOperationException("Strong entity-tag rules require a bounded desired-state header.");
        if (Brand == GatewayAdminClientStringBrand.ContinuationToken &&
            (Location != GatewayAdminClientParameterLocation.Query ||
             Rules.MaximumUtf8Bytes is null))
            throw new InvalidOperationException("Continuation tokens require a bounded query target.");

        switch (Brand)
        {
            case GatewayAdminClientStringBrand.None when
                !paginationMaximum || Required || Rules != new GatewayAdminClientConstraintRules():
                throw new InvalidOperationException("The unbranded maximum parameter has one closed shape.");
            case GatewayAdminClientStringBrand.NamespaceId:
                RequireResourcePath("ns");
                break;
            case GatewayAdminClientStringBrand.TargetNodeId:
                RequireResourcePath("target");
                break;
            case GatewayAdminClientStringBrand.RevisionId:
                RequireResourcePath("revision");
                break;
            case GatewayAdminClientStringBrand.ValidationId:
                RequireResourcePath("validation");
                break;
            case GatewayAdminClientStringBrand.OperationId:
                RequireResourcePath("operation");
                break;
            case GatewayAdminClientStringBrand.ContinuationToken when
                !StringComparer.Ordinal.Equals(Name, "cursor") || Required ||
                Rules.CharacterSet != GatewayAdminClientCharacterSet.Unicode:
                throw new InvalidOperationException("Continuation-token parameters have one closed target shape.");
            case GatewayAdminClientStringBrand.DesiredStateToken when
                !StringComparer.OrdinalIgnoreCase.Equals(Name, "If-Match") || Required:
                throw new InvalidOperationException("Desired-state tokens have one closed target shape.");
            case GatewayAdminClientStringBrand.IdempotencyKey:
                RequireVisibleHeader("Idempotency-Key", required: true);
                break;
            case GatewayAdminClientStringBrand.CorrelationId:
                RequireVisibleHeader("X-Correlation-ID", required: false);
                break;
            case GatewayAdminClientStringBrand.CandidateId:
                throw new InvalidOperationException("Candidate identity is not a V1 HTTP parameter target.");
        }

        void RequireResourcePath(string expectedName)
        {
            if (Location != GatewayAdminClientParameterLocation.Path || !Required ||
                !StringComparer.Ordinal.Equals(Name, expectedName) ||
                Rules.Normalization != GatewayAdminClientNormalization.Nfc ||
                Rules.CharacterSet != GatewayAdminClientCharacterSet.Unicode ||
                !Rules.RejectUnicodeControls || Rules.MinimumUtf8Bytes is null)
                throw new InvalidOperationException("Gateway resource parameters have one closed target and rule shape.");
        }

        void RequireVisibleHeader(string expectedName, bool required)
        {
            if (Location != GatewayAdminClientParameterLocation.Header || Required != required ||
                !StringComparer.OrdinalIgnoreCase.Equals(Name, expectedName) ||
                Rules.CharacterSet != GatewayAdminClientCharacterSet.VisibleAscii ||
                Rules.MinimumUtf8Bytes is null)
                throw new InvalidOperationException("Gateway visible-ASCII headers have one closed target and rule shape.");
        }
    }
}

internal static class GatewayAdminClientParameterConstraintValidator
{
    internal static void Validate(ImmutableArray<GatewayAdminClientParameterConstraint> constraints)
    {
        if (constraints.IsDefault)
            throw new InvalidOperationException("Gateway client parameter constraints must be initialized.");
        var targets = new HashSet<string>(StringComparer.Ordinal);
        foreach (GatewayAdminClientParameterConstraint constraint in constraints)
        {
            if (constraint is null)
                throw new InvalidOperationException("Gateway client parameter constraints cannot contain null.");
            constraint.Validate();
            string canonicalName = constraint.Location == GatewayAdminClientParameterLocation.Header
                ? constraint.Name.ToUpperInvariant()
                : constraint.Name;
            if (!targets.Add($"{(byte)constraint.Location}:{canonicalName}"))
                throw new InvalidOperationException("Gateway client parameter targets must be unique.");
        }
    }
}

internal static class GatewayAdminClientParameterProfiles
{
    private static readonly GatewayAdminClientConstraintRules Resource = new(
        1, 128, GatewayAdminClientNormalization.Nfc, RejectUnicodeControls: true);
    private static readonly GatewayAdminClientConstraintRules VisibleIdentity = new(
        1, 128, CharacterSet: GatewayAdminClientCharacterSet.VisibleAscii);
    private static readonly GatewayAdminClientConstraintRules DesiredPrecondition = new(
        1, 512, CharacterSet: GatewayAdminClientCharacterSet.StrongEntityTag);
    private static readonly GatewayAdminClientConstraintRules Cursor = new(MaximumUtf8Bytes: 4096);

    private static GatewayAdminClientParameterConstraint P(
        GatewayAdminClientParameterLocation location, string name, bool required,
        GatewayAdminClientStringBrand brand, GatewayAdminClientConstraintRules rules) =>
        new(location, name, required, brand, rules);

    private static GatewayAdminClientParameterConstraint Namespace() =>
        P(GatewayAdminClientParameterLocation.Path, "ns", true, GatewayAdminClientStringBrand.NamespaceId, Resource);
    private static GatewayAdminClientParameterConstraint Target() =>
        P(GatewayAdminClientParameterLocation.Path, "target", true, GatewayAdminClientStringBrand.TargetNodeId, Resource);
    private static GatewayAdminClientParameterConstraint Revision() =>
        P(GatewayAdminClientParameterLocation.Path, "revision", true, GatewayAdminClientStringBrand.RevisionId, Resource);
    private static GatewayAdminClientParameterConstraint Validation() =>
        P(GatewayAdminClientParameterLocation.Path, "validation", true, GatewayAdminClientStringBrand.ValidationId, Resource);
    private static GatewayAdminClientParameterConstraint Operation() =>
        P(GatewayAdminClientParameterLocation.Path, "operation", true, GatewayAdminClientStringBrand.OperationId, Resource);
    private static GatewayAdminClientParameterConstraint Correlation() =>
        P(GatewayAdminClientParameterLocation.Header, "X-Correlation-ID", false, GatewayAdminClientStringBrand.CorrelationId, VisibleIdentity);
    private static GatewayAdminClientParameterConstraint Idempotency() =>
        P(GatewayAdminClientParameterLocation.Header, "Idempotency-Key", true, GatewayAdminClientStringBrand.IdempotencyKey, VisibleIdentity);
    private static GatewayAdminClientParameterConstraint IfMatch() =>
        P(GatewayAdminClientParameterLocation.Header, "If-Match", false, GatewayAdminClientStringBrand.DesiredStateToken, DesiredPrecondition);
    private static GatewayAdminClientParameterConstraint Maximum() =>
        P(GatewayAdminClientParameterLocation.Query, "maximum", false, GatewayAdminClientStringBrand.None, new());
    private static GatewayAdminClientParameterConstraint Continuation() =>
        P(GatewayAdminClientParameterLocation.Query, "cursor", false, GatewayAdminClientStringBrand.ContinuationToken, Cursor);

    internal static ImmutableArray<GatewayAdminClientParameterConstraint> Global { get; } = [Correlation()];
    internal static ImmutableArray<GatewayAdminClientParameterConstraint> TargetRead { get; } =
        [Namespace(), Target(), Correlation()];
    internal static ImmutableArray<GatewayAdminClientParameterConstraint> TargetMutation { get; } =
        [Namespace(), Target(), Correlation(), Idempotency()];
    internal static ImmutableArray<GatewayAdminClientParameterConstraint> TargetCas { get; } =
        [Namespace(), Target(), Correlation(), Idempotency(), IfMatch()];
    internal static ImmutableArray<GatewayAdminClientParameterConstraint> TargetRevisionRead { get; } =
        [Namespace(), Target(), Revision(), Correlation()];
    internal static ImmutableArray<GatewayAdminClientParameterConstraint> TargetValidationRead { get; } =
        [Namespace(), Target(), Validation(), Correlation()];
    internal static ImmutableArray<GatewayAdminClientParameterConstraint> TargetRevisionCas { get; } =
        [Namespace(), Target(), Revision(), Correlation(), Idempotency(), IfMatch()];
    internal static ImmutableArray<GatewayAdminClientParameterConstraint> TargetPage { get; } =
        [Namespace(), Target(), Maximum(), Continuation(), Correlation()];
    internal static ImmutableArray<GatewayAdminClientParameterConstraint> NamespaceOperation { get; } =
        [Namespace(), Operation(), Correlation()];
    internal static ImmutableArray<GatewayAdminClientParameterConstraint> NamespacePage { get; } =
        [Namespace(), Maximum(), Continuation(), Correlation()];
    internal static ImmutableArray<GatewayAdminClientParameterConstraint> NamespaceMutation { get; } =
        [Namespace(), Correlation(), Idempotency()];
}
