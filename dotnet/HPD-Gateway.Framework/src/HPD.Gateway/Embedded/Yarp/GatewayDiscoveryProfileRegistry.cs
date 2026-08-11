using System.Collections.Immutable;

namespace HPD.Gateway;

internal sealed class GatewayDiscoveryProfileRegistry
{
    private const int MaximumProfiles = 32;
    private readonly ImmutableDictionary<DiscoveryProfileId, IGatewayDiscoveryRuntimeProfile> _profiles;

    internal GatewayDiscoveryProfileRegistry(IEnumerable<IGatewayDiscoveryRuntimeProfile> profiles)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        var bounded = new List<IGatewayDiscoveryRuntimeProfile>(MaximumProfiles + 1);
        using IEnumerator<IGatewayDiscoveryRuntimeProfile> enumerator = profiles.GetEnumerator();
        while (bounded.Count <= MaximumProfiles && enumerator.MoveNext())
            bounded.Add(enumerator.Current ?? throw new ArgumentException("Discovery runtime profiles cannot contain null entries.", nameof(profiles)));
        if (bounded.Count > MaximumProfiles)
            throw new ArgumentException($"At most {MaximumProfiles} discovery runtime profiles may be installed.", nameof(profiles));

        var builder = ImmutableDictionary.CreateBuilder<DiscoveryProfileId, IGatewayDiscoveryRuntimeProfile>();
        foreach (IGatewayDiscoveryRuntimeProfile profile in bounded.OrderBy(static value => value.Capability.Id.Value, StringComparer.Ordinal))
        {
            _ = HostCapabilitySnapshot.Create(new HostCapabilityRegistration { DiscoveryProfiles = [profile.Capability] });
            if (!builder.TryAdd(profile.Capability.Id, profile))
                throw new ArgumentException($"Discovery runtime profile '{profile.Capability.Id.Value}' is duplicated.", nameof(profiles));
        }
        _profiles = builder.ToImmutable();
    }

    internal int Count => _profiles.Count;

    internal bool TryGet(GatewayRuntimeDependencyBinding dependency, out IGatewayDiscoveryRuntimeProfile? profile)
    {
        if (!_profiles.TryGetValue(dependency.Profile, out profile)) return false;
        DiscoveryProfileCapability capability = profile.Capability;
        return capability.BehaviorIdentity == dependency.CapabilityIdentity &&
            capability.MaximumEndpoints == dependency.MaximumEndpoints &&
            dependency.Schemes.All(capability.Schemes.Contains) &&
            capability.StaleBehaviors.Contains(dependency.StaleBehavior);
    }
}
