using System.Collections.Immutable;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using HPD.Gateway.Effective;
using HPD.Gateway.Status;
using HPD.Gateway.Management;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Builder;

namespace HPD.Gateway.Admin;

public static class GatewayAdminCapabilities
{
    public const string CapabilityRead = "gateway.management.capability.read";
    public const string HostCapabilityRead = "gateway.management.host-capability.read";
    public const string TargetProvision = "gateway.management.target.provision";
    public const string RevisionValidate = "gateway.management.revision.validate";
    public const string RevisionRead = "gateway.management.revision.read";
    public const string RevisionWrite = "gateway.management.revision.write";
    public const string RevisionSubmitAndActivate = "gateway.management.revision.submit-and-activate";
    public const string ActivationWrite = "gateway.management.activation.write";
    public const string OperationRead = "gateway.management.operation.read";
    public const string EffectiveRead = "gateway.management.effective.read";
    public const string StatusRead = "gateway.management.status.read";
    public const string AuditRead = "gateway.management.audit.read";
    public const string ExportRead = "gateway.management.export.read";
    public const string ImportWrite = "gateway.management.import.write";
    public const string ImportAndActivate = "gateway.management.revision.import-and-activate";
    public const string BackupWrite = "gateway.management.backup.write";
    public const string PurgeWrite = "gateway.management.purge.write";

    public static ImmutableArray<string> All { get; } =
    [
        CapabilityRead, HostCapabilityRead, TargetProvision, RevisionValidate, RevisionRead,
        RevisionWrite, RevisionSubmitAndActivate, ActivationWrite,
        OperationRead, EffectiveRead, StatusRead, AuditRead, ExportRead,
        ImportWrite, ImportAndActivate, BackupWrite, PurgeWrite,
    ];
}

public static class GatewayAdminResourcePolicies
{
    public const string Namespace = "gateway.management.resource.namespace";
    public const string Target = "gateway.management.resource.target";
    public const string Administration = "gateway.management.resource.administration";
}

public enum GatewayAdminResourceKind : byte { Namespace, Target, Administration }

public sealed record GatewayAdminResource(
    string NamespaceId,
    string? TargetNodeId,
    GatewayAdminResourceKind Kind);

public sealed record GatewayAdminRequestAttribution(
    string ActorId,
    string AuthenticationScheme,
    string AuthorizationPolicy,
    string CorrelationId,
    string? NamespaceId = null)
{
    public GatewayManagementActor ToActor() =>
        new(ActorId, AuthenticationScheme, AuthorizationPolicy);
}

public interface IGatewayAdminActorProjector
{
    ValueTask<GatewayAdminRequestAttribution> ProjectAsync(
        HttpContext context,
        string capability,
        CancellationToken cancellationToken = default);
}

public interface IGatewayAdminSecurityMetadataProvider
{
    void Validate(GatewayAdminEndpointOptions options);
    void ApplyGroup(IEndpointConventionBuilder group);
    void ApplyEndpoint(IEndpointConventionBuilder endpoint, string capability);
}

public sealed record GatewayAdminEndpointOptions
{
    public string RoutePrefix { get; init; } = "/management/gateway/v1";
    public string AuthenticationScheme { get; init; } = "gateway-management";
    public string RateLimitPolicy { get; init; } = "gateway-management";
    public string RequestTimeoutPolicy { get; init; } = "gateway-management";
    public required string OpenApiSecurityScheme { get; init; }
    public bool RequireManagementListener { get; init; } = true;
    public string EndpointSurfaceId { get; init; } = "gateway-admin-v1";
    public required ImmutableDictionary<string, string> CapabilityPolicies { get; init; }
}

internal static class GatewayAdminSchemaConstraints
{
    internal const string ComponentPattern = "^[^\\u0000-\\u001F\\u007F-\\u009F]+$";
    internal const string BackupSinkPattern = "^[a-z0-9.-]+$";
    internal const string ArtifactLabelPattern = "^[A-Za-z0-9][A-Za-z0-9._-]*$";
}

public sealed record GatewayRevisionRequest
{
    [Required, StringLength(4 * 1024 * 1024, MinimumLength = 1)]
    public required string ConfigurationJson { get; init; }
    [Required, StringLength(128, MinimumLength = 1), RegularExpression(GatewayAdminSchemaConstraints.ComponentPattern)]
    public required string SourceKind { get; init; }
    [Required, StringLength(128, MinimumLength = 1), RegularExpression(GatewayAdminSchemaConstraints.ComponentPattern)]
    public required string SourceId { get; init; }
    [StringLength(1024)]
    public string? Description { get; init; }
}

public sealed record GatewayActivationRequest(
    [property: StringLength(1024)] string? Description = null);
public sealed record GatewayCompareRequest(
    [property: Required, StringLength(128, MinimumLength = 1), RegularExpression(GatewayAdminSchemaConstraints.ComponentPattern)] string LeftRevisionId,
    [property: Required, StringLength(128, MinimumLength = 1), RegularExpression(GatewayAdminSchemaConstraints.ComponentPattern)] string RightRevisionId);
public sealed record GatewayImportRequest(
    [property: Required, StringLength(4 * 1024 * 1024, MinimumLength = 1)] string ConfigurationJson,
    [property: Required, StringLength(128, MinimumLength = 1), RegularExpression(GatewayAdminSchemaConstraints.ComponentPattern)] string SourceId,
    [property: StringLength(1024)] string? Description = null);
public sealed record GatewayBackupRequest(
    [property: Required, StringLength(128, MinimumLength = 1), RegularExpression(GatewayAdminSchemaConstraints.BackupSinkPattern)] string SinkName,
    [property: StringLength(128, MinimumLength = 1), RegularExpression(GatewayAdminSchemaConstraints.ArtifactLabelPattern)] string? ArtifactLabel = null);
public enum GatewayPurgeCategory : byte { RevisionContent, ValidationContent, ActivationOutcomeHistory, AuditHistory }
public sealed record GatewayPurgeRequest(
    GatewayPurgeCategory Category,
    ImmutableArray<string> ResourceIds);
public sealed record GatewayOperationResponse(string OperationId, string State, string Code, bool Duplicate = false);
public sealed record GatewayActivationHistoryResponse(
    GatewayAdminPage<GatewayActivationProjection> Intents,
    GatewayAdminPage<GatewayOutcomeProjection> Outcomes);
public sealed record GatewayActivationProjection(
    string IntentId, string RevisionId, string CandidateId, string ContentHashValue,
    ulong AuthorityVersion, DateTimeOffset? AcceptedAt);
public sealed record GatewayOutcomeProjection(
    string OutcomeId, string ActivationIntentId, ulong AuthorityVersion,
    GatewayNodeOutcomeKind Kind, string Code, DateTimeOffset? ObservedAt);
public enum GatewayNodeObservationState : byte
{
    NotAttempted,
    Observed,
    ObservedWithoutEffectiveProjection,
    NotObserved,
    Indeterminate,
}
public sealed record GatewayTargetStatusResponse(
    GatewayManagementStatusSnapshot Management,
    GatewayNodeObservationState NodeObservation,
    GatewayStatusSnapshot? Node,
    DateTimeOffset ObservedAt,
    bool IsTruncated);
public sealed record GatewayExportResponse(
    string SchemaVersion, string RevisionId, string ContentHashAlgorithm,
    string ContentHashValue, string ConfigurationJson);
public sealed record GatewayAdministrativeResponse(
    string OperationId, GatewayAdministrativeCompletionState State, string Code, string? ArtifactReference = null);
public sealed record GatewayAdminPage<T>(ImmutableArray<T> Items, string? ContinuationToken, bool HasMore);
public sealed record GatewayRevisionProjection(
    string RevisionId, string ContentHashAlgorithm, string ContentHashValue,
    string SchemaVersion, string CanonicalizationVersion, string? ParentRevisionId,
    string? DerivedFromRevisionId, string ValidationId, string SourceKind,
    string SourceId, string? Description, DateTimeOffset? AcceptedAt);
public sealed record GatewayValidationProjection(
    string ValidationId, GatewayValidationOutcome Outcome, string? ContentHashValue,
    ImmutableArray<GatewayAdminDiagnostic> Diagnostics, DateTimeOffset? ValidatedAt);
public sealed record GatewayOperationProjection(
    string OperationId, string Operation, string ResultCode,
    string? DesiredStateToken, DateTimeOffset? AcceptedAt);
public sealed record GatewayAuditProjection(
    string AuditId, string ActorId, string Operation, string ResultCode,
    string CorrelationId, string SubjectId, DateTimeOffset? RecordedAt);

public sealed record GatewayProvisionResponse(string OperationId, bool Duplicate);
public sealed record GatewayRevisionResponse(string RevisionId, string? DesiredStateToken, bool Duplicate);
public sealed record GatewayValidationResponse(
    bool IsValid,
    ImmutableArray<GatewayAdminDiagnostic> Diagnostics,
    string? SchemaVersion,
    string? CanonicalizationVersion,
    string? ContentHashAlgorithm,
    string? ContentHashValue,
    string HostCapabilitySnapshotAlgorithm,
    string HostCapabilitySnapshotValue,
    string CorrelationId,
    DateTimeOffset ObservedAt);
public sealed record GatewayAdminDiagnostic(string Code, string Path, string SafeMessage);
public sealed record GatewayAdminError(string Code, string Title, string? CorrelationId = null);
public sealed record GatewayCapabilityCatalog(ImmutableArray<string> Capabilities, string ApiVersion);

public sealed record GatewayHostCapabilitySnapshotResponse(
    string SchemaVersion,
    string SnapshotAlgorithm,
    string SnapshotValue,
    GatewayHostCapabilityProjection Capabilities);

public sealed record GatewayHostCapabilityProjection(
    ImmutableArray<string> InstalledFamilies,
    ImmutableArray<GatewayListenerCapabilityProjection> Listeners,
    ImmutableArray<GatewayDiscoveryProviderCapabilityProjection> DiscoveryProviders,
    ImmutableArray<string> SecretProviders,
    ImmutableArray<string> AuthorizationPolicies,
    ImmutableArray<string> CorsPolicies,
    ImmutableArray<string> TrafficAdmissionPolicies,
    ImmutableArray<string> RequestTimeoutPolicies,
    ImmutableArray<GatewayOutputCacheCapabilityProjection> OutputCacheProfiles,
    ImmutableArray<string> SessionAffinityPolicies,
    ImmutableArray<string> SessionAffinityFailurePolicies,
    ImmutableArray<string> PassiveHealthPolicies,
    ImmutableArray<string> ActiveHealthPolicies,
    ImmutableArray<string> RequestInspectors,
    ImmutableArray<GatewayResilienceCapabilityProjection> UpstreamResilienceProfiles,
    ImmutableArray<string> ProtectedCredentialHeaders,
    bool AllowInspectionFileSpill);

public sealed record GatewayListenerCapabilityProjection(
    string Id,
    string Role,
    ImmutableArray<string> Protocols,
    ImmutableArray<string> Hostnames,
    bool Tls);

public sealed record GatewayDiscoveryProviderCapabilityProjection(
    string Id,
    ImmutableArray<string> SupportedParameters,
    ImmutableArray<string> RequiredParameters,
    bool AllowUnknownParameters,
    bool ProducesHttpsEndpoints);

public sealed record GatewayOutputCacheCapabilityProjection(
    string Name,
    int Version,
    bool RetainsDefaultSafetyPolicy,
    string StoreId,
    string StoreScope,
    long ExpirationTicks,
    long MaximumBodyBytes,
    long StoreCapacityBytes,
    ImmutableArray<string> QueryKeys,
    ImmutableArray<string> HeaderNames);

public sealed record GatewayResilienceCapabilityProjection(
    string Name,
    int Version,
    ImmutableArray<string> Strategies,
    ImmutableArray<int> RetryStatusCodes,
    int MaximumRetryAttempts);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UseStringEnumConverter = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(GatewayRevisionRequest))]
[JsonSerializable(typeof(GatewayActivationRequest))]
[JsonSerializable(typeof(GatewayCompareRequest))]
[JsonSerializable(typeof(GatewayImportRequest))]
[JsonSerializable(typeof(GatewayBackupRequest))]
[JsonSerializable(typeof(GatewayPurgeRequest))]
[JsonSerializable(typeof(GatewayOperationResponse))]
[JsonSerializable(typeof(GatewayActivationHistoryResponse))]
[JsonSerializable(typeof(GatewayActivationProjection))]
[JsonSerializable(typeof(GatewayOutcomeProjection))]
[JsonSerializable(typeof(GatewayAdminPage<GatewayActivationProjection>))]
[JsonSerializable(typeof(GatewayAdminPage<GatewayOutcomeProjection>))]
[JsonSerializable(typeof(GatewayTargetStatusResponse))]
[JsonSerializable(typeof(GatewayExportResponse))]
[JsonSerializable(typeof(GatewayAdministrativeResponse))]
[JsonSerializable(typeof(GatewayDesiredProjection))]
[JsonSerializable(typeof(GatewayRevisionProjection))]
[JsonSerializable(typeof(GatewayValidationProjection))]
[JsonSerializable(typeof(GatewayOperationProjection))]
[JsonSerializable(typeof(GatewayAuditProjection))]
[JsonSerializable(typeof(GatewayAdminPage<GatewayRevisionProjection>))]
[JsonSerializable(typeof(GatewayAdminPage<GatewayAuditProjection>))]
[JsonSerializable(typeof(GatewayProvisionResponse))]
[JsonSerializable(typeof(GatewayRevisionResponse))]
[JsonSerializable(typeof(GatewayValidationResponse))]
[JsonSerializable(typeof(GatewayAdminDiagnostic))]
[JsonSerializable(typeof(ImmutableArray<GatewayAdminDiagnostic>))]
[JsonSerializable(typeof(GatewayAdminError))]
[JsonSerializable(typeof(GatewayCapabilityCatalog))]
[JsonSerializable(typeof(GatewayHostCapabilitySnapshotResponse))]
[JsonSerializable(typeof(GatewayHostCapabilityProjection))]
[JsonSerializable(typeof(GatewayManagementStatusSnapshot))]
[JsonSerializable(typeof(GatewayRevisionComparison))]
[JsonSerializable(typeof(GatewayEffectiveSnapshot))]
[JsonSerializable(typeof(GatewayStatusSnapshot))]
public sealed partial class GatewayAdminJsonContext : JsonSerializerContext;
