using System.Collections.Immutable;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using HPD.Gateway;
using HPD.Gateway.ControlPlane;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Builder;

namespace HPD.Gateway.ControlPlane;

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
    void Validate(GatewayAdminApiOptions options);
    void ApplyGroup(IEndpointConventionBuilder group);
    void ApplyEndpoint(IEndpointConventionBuilder endpoint, string capability);
}

public sealed class GatewayAdminApiOptions
{
    public string RoutePrefix { get; set; } = "/management/gateway/v1";
    public string AuthenticationScheme { get; set; } = "Bearer";
    public string AuthorizationPolicy { get; set; } = "gateway-admin";
    public string RateLimitPolicy { get; set; } = "gateway-management";
    public string RequestTimeoutPolicy { get; set; } = "gateway-management";
    public string OpenApiSecurityScheme { get; set; } = "Bearer";
    public bool RequireManagementListener { get; set; } = true;
    public string EndpointSurfaceId { get; set; } = "gateway-admin-v1";
    public ImmutableDictionary<string, string> CapabilityPolicies { get; set; } =
        ImmutableDictionary<string, string>.Empty.WithComparers(StringComparer.Ordinal);

    internal void ApplyAuthorizationPolicy()
    {
        if (CapabilityPolicies.Count == 0)
            CapabilityPolicies = GatewayAdminCapabilities.All.ToImmutableDictionary(
                static capability => capability,
                _ => AuthorizationPolicy,
                StringComparer.Ordinal);
    }

    internal GatewayAdminApiOptions Snapshot() => new()
    {
        RoutePrefix = RoutePrefix,
        AuthenticationScheme = AuthenticationScheme,
        AuthorizationPolicy = AuthorizationPolicy,
        RateLimitPolicy = RateLimitPolicy,
        RequestTimeoutPolicy = RequestTimeoutPolicy,
        OpenApiSecurityScheme = OpenApiSecurityScheme,
        RequireManagementListener = RequireManagementListener,
        EndpointSurfaceId = EndpointSurfaceId,
        CapabilityPolicies = CapabilityPolicies.ToImmutableDictionary(StringComparer.Ordinal),
    };
}

internal static class GatewayAdminSchemaConstraints
{
    internal const string ComponentPattern = "^[^\\u0000-\\u001F\\u007F-\\u009F]+$";
    internal const string BackupSinkPattern = "^[a-z0-9.-]+$";
    internal const string ArtifactLabelPattern = "^[A-Za-z0-9][A-Za-z0-9._-]*$";
}

internal sealed record GatewayRevisionRequest
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

internal sealed record GatewayActivationRequest(
    [property: StringLength(1024)] string? Description = null);
internal sealed record GatewayCompareRequest(
    [property: Required, StringLength(128, MinimumLength = 1), RegularExpression(GatewayAdminSchemaConstraints.ComponentPattern)] string LeftRevisionId,
    [property: Required, StringLength(128, MinimumLength = 1), RegularExpression(GatewayAdminSchemaConstraints.ComponentPattern)] string RightRevisionId);
internal sealed record GatewayImportRequest(
    [property: Required, StringLength(4 * 1024 * 1024, MinimumLength = 1)] string ConfigurationJson,
    [property: Required, StringLength(128, MinimumLength = 1), RegularExpression(GatewayAdminSchemaConstraints.ComponentPattern)] string SourceId,
    [property: StringLength(1024)] string? Description = null);
internal sealed record GatewayBackupRequest(
    [property: Required, StringLength(128, MinimumLength = 1), RegularExpression(GatewayAdminSchemaConstraints.BackupSinkPattern)] string SinkName,
    [property: StringLength(128, MinimumLength = 1), RegularExpression(GatewayAdminSchemaConstraints.ArtifactLabelPattern)] string? ArtifactLabel = null);
internal enum GatewayPurgeCategory : byte { RevisionContent, ValidationContent, ActivationOutcomeHistory, AuditHistory }
internal sealed record GatewayPurgeRequest(
    GatewayPurgeCategory Category,
    ImmutableArray<string> ResourceIds);
internal sealed record GatewayOperationResponse(string OperationId, string State, string Code, bool Duplicate = false);
internal sealed record GatewayActivationHistoryResponse(
    GatewayAdminPage<GatewayActivationProjection> Intents,
    GatewayAdminPage<GatewayOutcomeProjection> Outcomes);
internal sealed record GatewayActivationProjection(
    string IntentId, string RevisionId, string CandidateId, string ContentHashValue,
    [property: JsonNumberHandling(JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString)] ulong AuthorityVersion,
    DateTimeOffset? AcceptedAt);
internal sealed record GatewayOutcomeProjection(
    string OutcomeId, string ActivationIntentId,
    [property: JsonNumberHandling(JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString)] ulong AuthorityVersion,
    GatewayNodeOutcomeKind Kind, string Code, string? ApplicationId,
    ContentHash? SymbolicPlanIdentity, DateTimeOffset? ObservedAt);
internal enum GatewayNodeObservationState : byte
{
    NotAttempted,
    Observed,
    ObservedWithoutEffectiveProjection,
    NotObserved,
    Indeterminate,
}
internal sealed record GatewayTargetStatusResponse(
    GatewayManagementStatusSnapshot Management,
    GatewayNodeObservationState NodeObservation,
    GatewayStatusSnapshot? Node,
    DateTimeOffset ObservedAt,
    bool IsTruncated);
internal sealed record GatewayExportResponse(
    string SchemaVersion, string RevisionId, string ContentHashAlgorithm,
    string ContentHashValue, string ConfigurationJson);
internal sealed record GatewayAdministrativeResponse(
    string OperationId, GatewayAdministrativeCompletionState State, string Code, string? ArtifactReference = null);
internal sealed record GatewayAdminPage<T>(ImmutableArray<T> Items, string? ContinuationToken, bool HasMore);
internal sealed record GatewayRevisionProjection(
    string RevisionId, string ContentHashAlgorithm, string ContentHashValue,
    string SchemaVersion, string CanonicalizationVersion, string? ParentRevisionId,
    string? DerivedFromRevisionId, string ValidationId, string SourceKind,
    string SourceId, string? Description, DateTimeOffset? AcceptedAt);
internal sealed record GatewayValidationProjection(
    string ValidationId, GatewayValidationOutcome Outcome, string? ContentHashValue,
    ImmutableArray<GatewayAdminDiagnostic> Diagnostics, DateTimeOffset? ValidatedAt);
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(GatewayCommandOperationProjection), "command")]
[JsonDerivedType(typeof(GatewayAdministrativeOperationProjection), "administration")]
internal abstract record GatewayOperationProjection;
internal sealed record GatewayCommandOperationProjection : GatewayOperationProjection
{
    public required string OperationId { get; init; }
    public required string Operation { get; init; }
    public required string ResultCode { get; init; }
    public required string? DesiredStateToken { get; init; }
    public required DateTimeOffset? AcceptedAt { get; init; }
}
internal sealed record GatewayAdministrativeOperationProjection : GatewayOperationProjection
{
    public required string OperationId { get; init; }
    public required GatewayAdministrativeOperationKind Operation { get; init; }
    public required GatewayAdministrativeOperationReadState State { get; init; }
    public required string Code { get; init; }
    public required string? ArtifactReference { get; init; }
    public required DateTimeOffset? ObservedAt { get; init; }
}
internal sealed record GatewayAuditProjection(
    string AuditId, string ActorId, string Operation, string ResultCode,
    string CorrelationId, string SubjectId, DateTimeOffset? RecordedAt);

internal sealed record GatewayProvisionResponse(string OperationId, bool Duplicate);
internal sealed record GatewayRevisionResponse(
    string OperationId,
    string? RevisionId,
    string? ActivationIntentId,
    string? DesiredStateToken,
    bool Duplicate);
internal sealed record GatewayValidationResponse(
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
internal sealed record GatewayAdminDiagnostic(string Code, string Path, string SafeMessage);
internal sealed record GatewayAdminError(string Code, string Title, string? CorrelationId = null);
internal sealed record GatewayCapabilityCatalog(ImmutableArray<string> Capabilities, string ApiVersion);

internal sealed record GatewayHostCapabilitySnapshotResponse(
    string SchemaVersion,
    string SnapshotAlgorithm,
    string SnapshotValue,
    GatewayHostCapabilityProjection Capabilities);

internal sealed record GatewayHostCapabilityProjection(
    ImmutableArray<string> InstalledFamilies,
    ImmutableArray<GatewayListenerCapabilityProjection> Listeners,
    ImmutableArray<GatewayDiscoveryProfileCapabilityProjection> DiscoveryProfiles,
    ImmutableArray<string> SecretProviders,
    ImmutableArray<string> AuthorizationPolicies,
    ImmutableArray<string> CorsPolicies,
    ImmutableArray<GatewayTrafficAdmissionCapabilityProjection> TrafficAdmissionProfiles,
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

internal sealed record GatewayListenerCapabilityProjection(
    string Id,
    string Role,
    ImmutableArray<string> Protocols,
    ImmutableArray<string> Hostnames,
    bool Tls);

internal sealed record GatewayTrafficAdmissionCapabilityProjection(
    string Name,
    ushort ContractVersion,
    string Scope,
    string Kind,
    string? RateAlgorithm,
    string Partition,
    string FailureDisposition,
    string MinimumLimit,
    string MaximumLimit,
    string? MinimumPeriodTicks,
    string? MaximumPeriodTicks,
    int MinimumSegments,
    int MaximumSegments,
    int MinimumQueue,
    int MaximumQueue,
    string AuthorityId,
    string BehaviorHashAlgorithm,
    string BehaviorHashValue,
    int? AcquisitionOrdinal,
    string? PartitionProjectorId,
    string? PartitionProjectorHashAlgorithm,
    string? PartitionProjectorHashValue,
    string? ProviderId,
    string? ProviderBehaviorHashAlgorithm,
    string? ProviderBehaviorHashValue,
    string? OperationTimeoutTicks,
    int? MaximumConcurrentInvocations,
    string? LocalFallbackProfile,
    string? LocalFallbackHashAlgorithm,
    string? LocalFallbackHashValue);

internal sealed record GatewayDiscoveryProfileCapabilityProjection(
    string Id,
    ushort ContractVersion,
    string RuntimeKind,
    ImmutableArray<string> Providers,
    ImmutableArray<string> Schemes,
    ImmutableArray<string> StaleBehaviors,
    int MaximumEndpoints,
    bool SupportsNamedEndpoints,
    bool SupportsDynamicRefresh,
    bool SupportsHttpAuthorityProjection,
    bool RequiresExplicitTlsServerName,
    string BehaviorIdentityAlgorithm,
    string BehaviorIdentityValue);

internal sealed record GatewayOutputCacheCapabilityProjection(
    string Name,
    int Version,
    bool RetainsDefaultSafetyPolicy,
    string StoreId,
    string StoreScope,
    string ExpirationTicks,
    string MaximumBodyBytes,
    string StoreCapacityBytes,
    ImmutableArray<string> QueryKeys,
    ImmutableArray<string> HeaderNames);

internal sealed record GatewayResilienceCapabilityProjection(
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
[JsonSerializable(typeof(GatewayCommandOperationProjection))]
[JsonSerializable(typeof(GatewayAdministrativeOperationProjection))]
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
[JsonSerializable(typeof(GatewayDiscoveryProfileCapabilityProjection))]
[JsonSerializable(typeof(GatewayManagementStatusSnapshot))]
[JsonSerializable(typeof(GatewayRevisionComparison))]
[JsonSerializable(typeof(GatewayAppliedRuntimeSnapshot))]
[JsonSerializable(typeof(GatewayStatusSnapshot))]
[JsonSerializable(typeof(GatewayDiscoveryStatus))]
internal sealed partial class GatewayAdminJsonContext : JsonSerializerContext;
