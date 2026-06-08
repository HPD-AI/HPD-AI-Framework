namespace HPD.Agent.Sandbox.Policy;

using HPD.Environment.Contracts;

internal enum NetworkPolicyDecisionKind
{
    Allow,
    Deny
}

internal sealed record NetworkPolicyDecision
{
    public required NetworkPolicyDecisionKind Kind { get; init; }
    public string? Reason { get; init; }

    public static NetworkPolicyDecision Allow(string? reason = null) =>
        new() { Kind = NetworkPolicyDecisionKind.Allow, Reason = reason };

    public static NetworkPolicyDecision Deny(string? reason = null) =>
        new() { Kind = NetworkPolicyDecisionKind.Deny, Reason = reason };
}

/// <summary>
/// Applies normalized network policy to canonicalized request hosts.
/// </summary>
internal sealed class NetworkPolicyEvaluator
{
    private readonly NetworkPolicy _policy;

    public NetworkPolicyEvaluator(NetworkPolicy policy)
    {
        _policy = policy;
    }

    public NetworkPolicyDecision Evaluate(string host)
    {
        if (_policy.Mode == NetworkEgressMode.Unrestricted)
            return NetworkPolicyDecision.Allow("network unrestricted");

        if (!HostCanonicalizer.TryCanonicalize(host, out var canonical, out var error))
            return NetworkPolicyDecision.Deny(error);

        if (_policy.Mode == NetworkEgressMode.Blocked)
            return NetworkPolicyDecision.Deny("network blocked");

        foreach (var denied in _policy.DeniedDomains)
        {
            if (denied.Matches(canonical))
                return NetworkPolicyDecision.Deny($"denied by rule {denied.Raw}");
        }

        foreach (var allowed in _policy.AllowedDomains)
        {
            if (allowed.Matches(canonical))
                return NetworkPolicyDecision.Allow($"allowed by rule {allowed.Raw}");
        }

        return NetworkPolicyDecision.Deny("no matching allow rule");
    }
}
