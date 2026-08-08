using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using HPD.Gateway.Core;

namespace HPD.Gateway.Admin;

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
            snapshot.DiscoveryProviders.Values
                .OrderBy(static value => value.Id.Value, StringComparer.Ordinal)
                .Select(static value => new GatewayDiscoveryProviderCapabilityProjection(
                    value.Id.Value,
                    Sort(value.SupportedParameters),
                    Sort(value.RequiredParameters),
                    value.AllowUnknownParameters,
                    value.ProducesHttpsEndpoints))
                .ToImmutableArray(),
            snapshot.SecretProviders.Select(static value => value.Value)
                .Order(StringComparer.Ordinal).ToImmutableArray(),
            Sort(snapshot.AuthorizationPolicies),
            Sort(snapshot.CorsPolicies),
            Sort(snapshot.TrafficAdmissionPolicies),
            Sort(snapshot.RequestTimeoutPolicies),
            snapshot.OutputCacheProfiles.Values
                .OrderBy(static value => value.Name, StringComparer.Ordinal)
                .Select(static value => new GatewayOutputCacheCapabilityProjection(
                    value.Name,
                    value.Version,
                    value.RetainsDefaultSafetyPolicy,
                    value.StoreId,
                    value.StoreScope.ToString(),
                    value.Expiration.Ticks,
                    value.MaximumBodyBytes,
                    value.StoreCapacityBytes,
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
