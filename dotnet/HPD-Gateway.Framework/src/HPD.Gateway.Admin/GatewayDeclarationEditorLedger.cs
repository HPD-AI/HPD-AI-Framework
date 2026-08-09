using System.Collections.Immutable;

namespace HPD.Gateway.Admin;

internal sealed record GatewayDeclarationEditorLedgerEnvelope(
    ushort SchemaVersion,
    string DeclarationSchemaRef,
    ImmutableArray<GatewayEditorFieldRecord> Records);

internal sealed record GatewayEditorFieldTarget(
    ImmutableArray<GatewayEditorOccurrenceStep> OccurrencePath,
    string ComponentSchemaRef,
    string ComponentSchemaPointer,
    ImmutableArray<GatewayEditorConstraintTarget> ConstraintTargets);

internal sealed record GatewayEditorOccurrenceStep(
    GatewayEditorOccurrenceStepKind Kind,
    string? Value,
    string? SecondaryValue);

internal sealed record GatewayEditorConstraintTarget(
    string SchemaRef,
    string PropertyPointer,
    GatewayEditorConstraintAppliesTo AppliesTo);

internal sealed record GatewayEditorFieldRecord(
    GatewayEditorFieldTarget Target,
    GatewayEditorFieldDisposition Disposition,
    GatewayEditorCompositionScope CompositionScope,
    GatewayEditorOmittedValueKind OmittedValueKind,
    string? OmittedValueJson,
    GatewayEditorInheritanceKind Inheritance,
    ImmutableArray<GatewayEditorOccurrenceStep> InheritanceSourceOccurrencePath,
    GatewayEditorDeclarationFamily Family,
    GatewayEditorCapabilitySelector Capability,
    GatewayEditorPresentationGroup PresentationGroup,
    string HelpCode,
    GatewayEditorQuickRouteStep QuickRouteStep,
    GatewayEditorStructuralReason StructuralReason);

internal sealed record GatewayEditorCapabilitySelector(
    GatewayEditorCapabilityKind Kind,
    ImmutableArray<string> RelativeValuePointers);

internal sealed record GatewayDeclarationEditorLedgerExportV1(
    ushort ExportVersion,
    string HashAlgorithm,
    string EnvelopeSha256,
    GatewayDeclarationEditorLedgerEnvelope Envelope);

internal enum GatewayEditorFieldDisposition : byte { Editable, StructuralOnly }
internal enum GatewayEditorCompositionScope : byte
{
    Document, RootDefaults, Route, RouteMatch, Upstream, EndpointSource,
    Destination, Definition, Metadata, Transform,
}
internal enum GatewayEditorOmittedValueKind : byte { Absent, CanonicalJson }
internal enum GatewayEditorInheritanceKind : byte { None, RootInheritedAndRouteReplaced }
internal enum GatewayEditorDeclarationFamily : byte
{
    None, Routing, Authorization, Cors, TrafficAdmission, RequestTimeout,
    OutputCache, Telemetry, Inspection, CredentialDisposition, RequestTransform,
    ResponseTransform, Discovery, Secret, Tls, Resilience, ActiveHealth,
    PassiveHealth, SessionAffinity, Listener, Transport, Metadata,
}
internal enum GatewayEditorCapabilityKind : byte
{
    None, InstalledFamily, Listener, DiscoveryProvider, SecretProvider,
    AuthorizationPolicy, CorsPolicy, TrafficAdmissionPolicy, RequestTimeoutPolicy,
    OutputCacheProfile, ResilienceProfile, RequestInspector, InspectionSpill,
    ActiveHealthPolicy, PassiveHealthPolicy, SessionAffinityPolicy,
    SessionAffinityFailurePolicy,
}
internal enum GatewayEditorPresentationGroup : byte
{
    Document, Identity, Match, Endpoint, Policies, Reliability, Security,
    Transport, Metadata, Advanced,
}
internal enum GatewayEditorQuickRouteStep : byte
{
    None, RequestMatch, Upstream, Destination, OptionalPolicy,
}
internal enum GatewayEditorStructuralReason : byte
{
    None, Container, Collection, CollectionItem, IdentityWrapper, UnionBoundary,
}
internal enum GatewayEditorConstraintAppliesTo : byte { Value, Collection, Items }
internal enum GatewayEditorOccurrenceStepKind : byte { Property, Items, UnionBranch, Reference }

internal sealed record GatewayDeclarationEditorLedgerExportDocument(
    GatewayDeclarationEditorLedgerExportV1 Value,
    ImmutableArray<byte> Utf8);
