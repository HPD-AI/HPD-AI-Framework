namespace HPD.Agent.Sandbox.Policy;

using HPD.Execution.Contracts;

internal sealed record NetworkPolicy
{
    public required NetworkEgressMode Mode { get; init; }
    public IReadOnlyList<DomainPattern> AllowedDomains { get; init; } = [];
    public IReadOnlyList<DomainPattern> DeniedDomains { get; init; } = [];

    public static NetworkPolicy Blocked { get; } = new()
    {
        Mode = NetworkEgressMode.Blocked,
    };

    public static NetworkPolicy Unrestricted { get; } = new()
    {
        Mode = NetworkEgressMode.Unrestricted,
    };

    public static NetworkPolicy Filtered(
        IReadOnlyList<DomainPattern> allowedDomains,
        IReadOnlyList<DomainPattern>? deniedDomains = null) =>
        new()
        {
            Mode = NetworkEgressMode.Filtered,
            AllowedDomains = allowedDomains,
            DeniedDomains = deniedDomains ?? [],
        };
}
