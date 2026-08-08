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
    GatewayAdminClientConstraintRules Rules);

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
