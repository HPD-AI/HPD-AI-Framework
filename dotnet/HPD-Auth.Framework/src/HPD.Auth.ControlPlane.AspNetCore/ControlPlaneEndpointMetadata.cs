namespace HPD.Auth.ControlPlane;

/// <summary>Marks an endpoint as part of a validated HPD control plane.</summary>
public sealed record ControlPlaneEndpointMetadata(string Profile);

/// <summary>Identifies the product-owned capability required by an endpoint.</summary>
public sealed record ControlPlaneCapabilityMetadata(string Capability);
