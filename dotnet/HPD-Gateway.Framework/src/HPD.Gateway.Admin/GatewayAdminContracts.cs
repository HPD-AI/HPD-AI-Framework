using System.Collections.Immutable;
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
        CapabilityRead, TargetProvision, RevisionValidate, RevisionRead,
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
    public bool RequireManagementListener { get; init; } = true;
    public string EndpointSurfaceId { get; init; } = "gateway-admin-v1";
    public required ImmutableDictionary<string, string> CapabilityPolicies { get; init; }
}

public sealed record GatewayRevisionRequest
{
    public required string ConfigurationJson { get; init; }
    public required string SourceKind { get; init; }
    public required string SourceId { get; init; }
    public string? Description { get; init; }
}

public sealed record GatewayActivationRequest(string? Description = null);
public sealed record GatewayCompareRequest(string LeftRevisionId, string RightRevisionId);
public sealed record GatewayImportRequest(string ConfigurationJson, string SourceId, string? Description = null);
public sealed record GatewayBackupRequest(string SinkName, string? ArtifactLabel = null);
public enum GatewayPurgeCategory : byte { RevisionContent, ValidationContent, ActivationOutcomeHistory, AuditHistory }
public sealed record GatewayPurgeRequest(GatewayPurgeCategory Category, ImmutableArray<string> ResourceIds);
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
public sealed record GatewayTargetStatusResponse(
    GatewayManagementStatusSnapshot Management,
    GatewayStatusSnapshot Node,
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
public sealed record GatewayValidationResponse(bool IsValid, ImmutableArray<GatewayAdminDiagnostic> Diagnostics);
public sealed record GatewayAdminDiagnostic(string Code, string Path, string SafeMessage);
public sealed record GatewayAdminError(string Code, string Title, string? CorrelationId = null);
public sealed record GatewayCapabilityCatalog(ImmutableArray<string> Capabilities, string ApiVersion);

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
[JsonSerializable(typeof(GatewayManagementStatusSnapshot))]
[JsonSerializable(typeof(GatewayRevisionComparison))]
[JsonSerializable(typeof(GatewayEffectiveSnapshot))]
[JsonSerializable(typeof(GatewayStatusSnapshot))]
public sealed partial class GatewayAdminJsonContext : JsonSerializerContext;
