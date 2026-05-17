namespace HPD.Sandbox.Local.Policy;

using SandboxNetworkMode = HPD.Agent.Sandbox.SandboxNetworkMode;

/// <summary>
/// Normalized network policy for sandbox proxy and platform emitters.
/// </summary>
internal sealed record NetworkPolicy
{
    public required SandboxNetworkMode Mode { get; init; }
    public IReadOnlyList<DomainPattern> AllowedDomains { get; init; } = [];
    public IReadOnlyList<DomainPattern> DeniedDomains { get; init; } = [];

    public static NetworkPolicy Blocked { get; } = new()
    {
        Mode = SandboxNetworkMode.Blocked,
    };

    public static NetworkPolicy Unrestricted { get; } = new()
    {
        Mode = SandboxNetworkMode.Unrestricted,
    };

    public static NetworkPolicy Filtered(
        IReadOnlyList<DomainPattern> allowedDomains,
        IReadOnlyList<DomainPattern>? deniedDomains = null) =>
        new()
        {
            Mode = SandboxNetworkMode.Filtered,
            AllowedDomains = allowedDomains,
            DeniedDomains = deniedDomains ?? [],
        };
}
