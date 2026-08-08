using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
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
    public required ImmutableDictionary<string, string> CapabilityPolicies { get; init; }
}

public sealed record GatewayRevisionRequest
{
    public required JsonElement Configuration { get; init; }
    public required string SourceKind { get; init; }
    public required string SourceId { get; init; }
    public string? Description { get; init; }
}

public sealed record GatewayProvisionResponse(string OperationId, bool Duplicate);
public sealed record GatewayRevisionResponse(string RevisionId, string? DesiredStateToken, bool Duplicate);
public sealed record GatewayValidationResponse(bool IsValid, ImmutableArray<GatewayAdminDiagnostic> Diagnostics);
public sealed record GatewayAdminDiagnostic(string Code, string Path, string SafeMessage);
public sealed record GatewayAdminError(string Code, string Title, string? CorrelationId = null);
public sealed record GatewayCapabilityCatalog(ImmutableArray<string> Capabilities, string ApiVersion);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UseStringEnumConverter = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(GatewayRevisionRequest))]
[JsonSerializable(typeof(GatewayProvisionResponse))]
[JsonSerializable(typeof(GatewayRevisionResponse))]
[JsonSerializable(typeof(GatewayValidationResponse))]
[JsonSerializable(typeof(GatewayAdminDiagnostic))]
[JsonSerializable(typeof(GatewayAdminError))]
[JsonSerializable(typeof(GatewayCapabilityCatalog))]
[JsonSerializable(typeof(GatewayManagementStatusSnapshot))]
[JsonSerializable(typeof(GatewayManagedRecord<GatewayDesiredState>))]
[JsonSerializable(typeof(GatewayManagedPage<GatewayAcceptedRevision>))]
[JsonSerializable(typeof(GatewayManagedPage<GatewayAdministrativeAuditRecord>))]
[JsonSerializable(typeof(JsonElement))]
public sealed partial class GatewayAdminJsonContext : JsonSerializerContext;
