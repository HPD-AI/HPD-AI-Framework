namespace HPD.Agent;

public sealed record HpdContributionOwner(
    string Id,
    string Scope,
    string? Version = null,
    string? DisplayName = null)
{
    public static HpdContributionOwner Framework { get; } = new(
        "hpd.framework",
        "framework",
        DisplayName: "HPD Framework");

    public static HpdContributionOwner App { get; } = new(
        "hpd.app",
        "app",
        DisplayName: "Host Application");
}
