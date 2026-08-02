using System.Collections.Immutable;
using HPD.Gateway.Abstractions;

namespace HPD.Gateway.Core;

public sealed record HostCapabilitySnapshot
{
    public ImmutableHashSet<string> AuthorizationPolicies { get; init; } = ImmutableHashSet<string>.Empty.WithComparer(StringComparer.Ordinal);
    public ImmutableHashSet<string> CorsPolicies { get; init; } = ImmutableHashSet<string>.Empty.WithComparer(StringComparer.Ordinal);
    public ImmutableHashSet<string> TrafficAdmissionPolicies { get; init; } = ImmutableHashSet<string>.Empty.WithComparer(StringComparer.Ordinal);
    public ImmutableHashSet<string> RequestTimeoutPolicies { get; init; } = ImmutableHashSet<string>.Empty.WithComparer(StringComparer.Ordinal);
    public ImmutableHashSet<string> OutputCachePolicies { get; init; } = ImmutableHashSet<string>.Empty.WithComparer(StringComparer.Ordinal);
    public ImmutableHashSet<string> SessionAffinityPolicies { get; init; } = ImmutableHashSet<string>.Empty.WithComparer(StringComparer.Ordinal);
    public ImmutableHashSet<string> SessionAffinityFailurePolicies { get; init; } = ImmutableHashSet<string>.Empty.WithComparer(StringComparer.Ordinal);
    public ImmutableHashSet<string> PassiveHealthPolicies { get; init; } = ImmutableHashSet<string>.Empty.WithComparer(StringComparer.Ordinal);
    public ImmutableHashSet<string> ActiveHealthPolicies { get; init; } = ImmutableHashSet<string>.Empty.WithComparer(StringComparer.Ordinal);
    public ImmutableHashSet<ListenerId> Listeners { get; init; } = [];
    public ImmutableHashSet<ProviderId> TlsCompatibleDiscoveryProviders { get; init; } = [];
}
