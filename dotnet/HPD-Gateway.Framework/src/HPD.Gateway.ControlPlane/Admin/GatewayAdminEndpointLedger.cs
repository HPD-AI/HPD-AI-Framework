using System.Collections.Immutable;

namespace HPD.Gateway.ControlPlane;

internal sealed record GatewayAdminEndpointDescriptor(
    string Operation,
    string Method,
    string Pattern,
    string Capability,
    string? ResourcePolicy,
    GatewayAdminResourceKind? ResourceKind,
    bool Mutation);

internal static class GatewayAdminEndpointLedger
{
    public static ImmutableArray<GatewayAdminEndpointDescriptor> V1 { get; } =
    [
        new("capabilities", "GET", "/capabilities", GatewayAdminCapabilities.CapabilityRead, null, null, false),
        new("host-capabilities", "GET", "/host-capabilities", GatewayAdminCapabilities.HostCapabilityRead, null, null, false),
        new("validate", "POST", "/candidates:validate", GatewayAdminCapabilities.RevisionValidate, null, null, false),
        new("provision", "POST", "/namespaces/{ns}/targets/{target}:provision", GatewayAdminCapabilities.TargetProvision, GatewayAdminResourcePolicies.Target, GatewayAdminResourceKind.Target, true),
        new("desired", "GET", "/namespaces/{ns}/targets/{target}/desired", GatewayAdminCapabilities.RevisionRead, GatewayAdminResourcePolicies.Target, GatewayAdminResourceKind.Target, false),
        new("status", "GET", "/namespaces/{ns}/targets/{target}/status", GatewayAdminCapabilities.StatusRead, GatewayAdminResourcePolicies.Target, GatewayAdminResourceKind.Target, false),
        new("effective", "GET", "/namespaces/{ns}/targets/{target}/effective", GatewayAdminCapabilities.EffectiveRead, GatewayAdminResourcePolicies.Target, GatewayAdminResourceKind.Target, false),
        new("submit", "POST", "/namespaces/{ns}/targets/{target}/revisions", GatewayAdminCapabilities.RevisionWrite, GatewayAdminResourcePolicies.Target, GatewayAdminResourceKind.Target, true),
        new("submit-and-activate", "POST", "/namespaces/{ns}/targets/{target}/revisions:submitAndActivate", GatewayAdminCapabilities.RevisionSubmitAndActivate, GatewayAdminResourcePolicies.Target, GatewayAdminResourceKind.Target, true),
        new("revisions", "GET", "/namespaces/{ns}/targets/{target}/revisions", GatewayAdminCapabilities.RevisionRead, GatewayAdminResourcePolicies.Target, GatewayAdminResourceKind.Target, false),
        new("revision", "GET", "/namespaces/{ns}/targets/{target}/revisions/{revision}", GatewayAdminCapabilities.RevisionRead, GatewayAdminResourcePolicies.Target, GatewayAdminResourceKind.Target, false),
        new("validation", "GET", "/namespaces/{ns}/targets/{target}/validations/{validation}", GatewayAdminCapabilities.RevisionRead, GatewayAdminResourcePolicies.Target, GatewayAdminResourceKind.Target, false),
        new("activate", "POST", "/namespaces/{ns}/targets/{target}/revisions/{revision}:activate", GatewayAdminCapabilities.ActivationWrite, GatewayAdminResourcePolicies.Target, GatewayAdminResourceKind.Target, true),
        new("rollback", "POST", "/namespaces/{ns}/targets/{target}/revisions/{revision}:rollback", GatewayAdminCapabilities.ActivationWrite, GatewayAdminResourcePolicies.Target, GatewayAdminResourceKind.Target, true),
        new("activations", "GET", "/namespaces/{ns}/targets/{target}/activations", GatewayAdminCapabilities.StatusRead, GatewayAdminResourcePolicies.Target, GatewayAdminResourceKind.Target, false),
        new("compare", "POST", "/namespaces/{ns}/targets/{target}/revisions:compare", GatewayAdminCapabilities.RevisionRead, GatewayAdminResourcePolicies.Target, GatewayAdminResourceKind.Target, false),
        new("export", "GET", "/namespaces/{ns}/targets/{target}/revisions/{revision}:export", GatewayAdminCapabilities.ExportRead, GatewayAdminResourcePolicies.Target, GatewayAdminResourceKind.Target, false),
        new("import", "POST", "/namespaces/{ns}/targets/{target}/revisions:import", GatewayAdminCapabilities.ImportWrite, GatewayAdminResourcePolicies.Target, GatewayAdminResourceKind.Target, true),
        new("import-and-activate", "POST", "/namespaces/{ns}/targets/{target}/revisions:importAndActivate", GatewayAdminCapabilities.ImportAndActivate, GatewayAdminResourcePolicies.Target, GatewayAdminResourceKind.Target, true),
        new("operation", "GET", "/namespaces/{ns}/operations/{operation}", GatewayAdminCapabilities.OperationRead, GatewayAdminResourcePolicies.Namespace, GatewayAdminResourceKind.Namespace, false),
        new("audit", "GET", "/namespaces/{ns}/audit", GatewayAdminCapabilities.AuditRead, GatewayAdminResourcePolicies.Namespace, GatewayAdminResourceKind.Namespace, false),
        new("backup", "POST", "/namespaces/{ns}/administration/backups", GatewayAdminCapabilities.BackupWrite, GatewayAdminResourcePolicies.Administration, GatewayAdminResourceKind.Administration, true),
        new("purge", "POST", "/namespaces/{ns}/administration/purges", GatewayAdminCapabilities.PurgeWrite, GatewayAdminResourcePolicies.Administration, GatewayAdminResourceKind.Administration, true),
    ];
}
