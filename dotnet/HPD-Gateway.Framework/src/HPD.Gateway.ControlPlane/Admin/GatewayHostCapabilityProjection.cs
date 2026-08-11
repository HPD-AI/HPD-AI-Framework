using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using HPD.Gateway;

namespace HPD.Gateway.ControlPlane;

internal static class GatewayHostCapabilityProjector
{
    private const string SchemaVersion = "1";
    private const string HashAlgorithm = "sha-256";
    private const int MaximumProjectionBytes = 4 * 1024 * 1024;
    private static readonly byte[] Domain = "hpd.gateway.host-capability.v1\0"u8.ToArray();

    internal static GatewayHostCapabilitySnapshotResponse Project(HostCapabilitySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var projection = new GatewayHostCapabilityProjection(
            FlagNames(snapshot.InstalledFamilies),
            snapshot.Listeners.Values
                .OrderBy(static value => value.Id.Value, StringComparer.Ordinal)
                .Select(static value => new GatewayListenerCapabilityProjection(
                    value.Id.Value,
                    value.Role.ToString(),
                    FlagNames(value.Protocols),
                    value.Hostnames.Select(static host => host.ToLowerInvariant())
                        .Order(StringComparer.Ordinal).ToImmutableArray(),
                    value.Tls))
                .ToImmutableArray(),
            snapshot.DiscoveryProfiles.Values
                .OrderBy(static value => value.Id.Value, StringComparer.Ordinal)
                .Select(static value => new GatewayDiscoveryProfileCapabilityProjection(
                    value.Id.Value,
                    value.ContractVersion,
                    value.RuntimeKind.ToString(),
                    value.Providers.Select(static provider => provider.ToString()).ToImmutableArray(),
                    value.Schemes.Select(static scheme => scheme.ToString()).ToImmutableArray(),
                    value.StaleBehaviors.Select(static behavior => behavior.ToString()).ToImmutableArray(),
                    value.MaximumEndpoints,
                    value.SupportsNamedEndpoints,
                    value.SupportsDynamicRefresh,
                    value.SupportsHttpAuthorityProjection,
                    value.RequiresExplicitTlsServerName,
                    value.BehaviorIdentity.Algorithm,
                    value.BehaviorIdentity.Value))
                .ToImmutableArray(),
            snapshot.SecretProviders.Select(static value => value.Value)
                .Order(StringComparer.Ordinal).ToImmutableArray(),
            Sort(snapshot.AuthorizationPolicies),
            Sort(snapshot.CorsPolicies),
            snapshot.TrafficAdmissionProfiles.Values.OrderBy(static value => value.Name, StringComparer.Ordinal)
                .Select(static value => new GatewayTrafficAdmissionCapabilityProjection(
                    value.Name, value.ContractVersion, value.Scope.ToString(), value.Kind.ToString(), value.RateAlgorithm?.ToString(),
                    value.Partition.ToString(), value.FailureDisposition.ToString(),
                    value.Limits.MinimumLimit.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    value.Limits.MaximumLimit.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    value.Limits.MinimumPeriod?.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    value.Limits.MaximumPeriod?.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    value.Limits.MinimumSegments, value.Limits.MaximumSegments, value.Limits.MinimumQueue, value.Limits.MaximumQueue,
                    value.AuthorityId, value.BehaviorIdentity.Algorithm, value.BehaviorIdentity.Value, value.AcquisitionOrdinal))
                .ToImmutableArray(),
            Sort(snapshot.RequestTimeoutPolicies),
            snapshot.OutputCacheProfiles.Values
                .OrderBy(static value => value.Name, StringComparer.Ordinal)
                .Select(static value => new GatewayOutputCacheCapabilityProjection(
                    value.Name,
                    value.Version,
                    value.RetainsDefaultSafetyPolicy,
                    value.StoreId,
                    value.StoreScope.ToString(),
                    value.Expiration.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    value.MaximumBodyBytes.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    value.StoreCapacityBytes.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    Sort(value.QueryKeys),
                    value.HeaderNames.Select(static name => name.ToLowerInvariant())
                        .Order(StringComparer.Ordinal).ToImmutableArray()))
                .ToImmutableArray(),
            Sort(snapshot.SessionAffinityPolicies),
            Sort(snapshot.SessionAffinityFailurePolicies),
            Sort(snapshot.PassiveHealthPolicies),
            Sort(snapshot.ActiveHealthPolicies),
            Sort(snapshot.RequestInspectors),
            snapshot.UpstreamResilienceProfiles.Values
                .OrderBy(static value => value.Name, StringComparer.Ordinal)
                .Select(static value => new GatewayResilienceCapabilityProjection(
                    value.Name,
                    value.Version,
                    FlagNames(value.Strategies),
                    value.RetryStatusCodes.Order().ToImmutableArray(),
                    value.MaximumRetryAttempts))
                .ToImmutableArray(),
            snapshot.ProtectedCredentialHeaders.Select(static name => name.ToLowerInvariant())
                .Order(StringComparer.Ordinal).ToImmutableArray(),
            snapshot.AllowInspectionFileSpill);

        byte[] json = JsonSerializer.SerializeToUtf8Bytes(
            projection,
            GatewayAdminJsonContext.Default.GatewayHostCapabilityProjection);
        if (json.Length > MaximumProjectionBytes)
        {
            throw new InvalidOperationException("The bounded host-capability projection exceeded its invariant byte limit.");
        }

        byte[] framed = new byte[Domain.Length + json.Length];
        Domain.CopyTo(framed, 0);
        json.CopyTo(framed, Domain.Length);
        string value = Convert.ToHexStringLower(SHA256.HashData(framed));
        return new GatewayHostCapabilitySnapshotResponse(SchemaVersion, HashAlgorithm, value, projection);
    }

    private static ImmutableArray<string> Sort(IEnumerable<string> values) =>
        values.Order(StringComparer.Ordinal).ToImmutableArray();

    private static ImmutableArray<string> FlagNames<T>(T value) where T : struct, Enum
    {
        ulong bits = Convert.ToUInt64(value);
        return Enum.GetValues<T>()
            .Select(static item => (Item: item, Bits: Convert.ToUInt64(item)))
            .Where(item => item.Bits != 0 && (item.Bits & (item.Bits - 1)) == 0 && (bits & item.Bits) != 0)
            .Select(static item => item.Item.ToString())
            .Order(StringComparer.Ordinal)
            .ToImmutableArray();
    }
}
