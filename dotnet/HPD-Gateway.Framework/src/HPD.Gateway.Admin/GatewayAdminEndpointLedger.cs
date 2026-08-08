using System.Collections.Immutable;

namespace HPD.Gateway.Admin;

public sealed record GatewayAdminEndpointDescriptor(
    string Operation,
    string Method,
    string Pattern,
    string Capability,
    string? ResourcePolicy,
    GatewayAdminResourceKind? ResourceKind,
    bool Mutation);

public static class GatewayAdminEndpointLedger
{
    public static ImmutableArray<GatewayAdminEndpointDescriptor> V1 { get; } =
    [
        new("capabilities", "GET", "/capabilities", GatewayAdminCapabilities.CapabilityRead, null, null, false),
        new("validate", "POST", "/candidates:validate", GatewayAdminCapabilities.RevisionValidate, null, null, false),
        new("provision", "POST", "/namespaces/{ns}/targets/{target}:provision", GatewayAdminCapabilities.TargetProvision, GatewayAdminResourcePolicies.Target, GatewayAdminResourceKind.Target, true),
        new("desired", "GET", "/namespaces/{ns}/targets/{target}/desired", GatewayAdminCapabilities.RevisionRead, GatewayAdminResourcePolicies.Target, GatewayAdminResourceKind.Target, false),
        new("status", "GET", "/namespaces/{ns}/targets/{target}/status", GatewayAdminCapabilities.StatusRead, GatewayAdminResourcePolicies.Target, GatewayAdminResourceKind.Target, false),
        new("submit", "POST", "/namespaces/{ns}/targets/{target}/revisions", GatewayAdminCapabilities.RevisionWrite, GatewayAdminResourcePolicies.Target, GatewayAdminResourceKind.Target, true),
        new("submit-and-activate", "POST", "/namespaces/{ns}/targets/{target}/revisions:submitAndActivate", GatewayAdminCapabilities.RevisionSubmitAndActivate, GatewayAdminResourcePolicies.Target, GatewayAdminResourceKind.Target, true),
        new("revisions", "GET", "/namespaces/{ns}/targets/{target}/revisions", GatewayAdminCapabilities.RevisionRead, GatewayAdminResourcePolicies.Target, GatewayAdminResourceKind.Target, false),
        new("audit", "GET", "/namespaces/{ns}/audit", GatewayAdminCapabilities.AuditRead, GatewayAdminResourcePolicies.Namespace, GatewayAdminResourceKind.Namespace, false),
    ];
}
