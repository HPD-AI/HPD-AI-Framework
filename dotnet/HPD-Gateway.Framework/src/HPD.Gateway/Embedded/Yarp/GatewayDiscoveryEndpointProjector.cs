using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Yarp.ReverseProxy.Configuration;

namespace HPD.Gateway;

internal static class GatewayDiscoveryEndpointProjector
{
    private const int MaximumAddressUtf8Bytes = 2_048;
    private const int MaximumHostUtf8Bytes = 253;

    internal static ImmutableDictionary<string, DestinationConfig> Project(
        GatewayRuntimeDependencyBinding dependency,
        DiscoveryProfileCapability capability,
        IEnumerable<GatewayDiscoveryEndpoint> endpoints)
    {
        ArgumentNullException.ThrowIfNull(dependency);
        ArgumentNullException.ThrowIfNull(capability);
        ArgumentNullException.ThrowIfNull(endpoints);
        var bounded = new List<GatewayDiscoveryEndpoint>(dependency.MaximumEndpoints + 1);
        using IEnumerator<GatewayDiscoveryEndpoint> enumerator = endpoints.GetEnumerator();
        while (bounded.Count <= dependency.MaximumEndpoints && enumerator.MoveNext())
            bounded.Add(enumerator.Current ?? throw new InvalidOperationException("Discovery results cannot contain null endpoints."));
        if (bounded.Count > dependency.MaximumEndpoints)
            throw new InvalidOperationException("Discovery result exceeds the accepted endpoint bound.");

        Projected[] projected = bounded.Select(value => ProjectOne(dependency, capability, value))
            .OrderBy(static value => value.Address, StringComparer.Ordinal)
            .ThenBy(static value => value.Health, StringComparer.Ordinal)
            .ThenBy(static value => value.Host, StringComparer.Ordinal)
            .ThenBy(static value => value.FeatureIdentity, StringComparer.Ordinal)
            .ToArray();
        for (var index = 1; index < projected.Length; index++)
            if (projected[index - 1] == projected[index])
                throw new InvalidOperationException("Discovery result contains an exact duplicate endpoint tuple.");

        var builder = ImmutableDictionary.CreateBuilder<string, DestinationConfig>(StringComparer.Ordinal);
        foreach (Projected value in projected)
        {
            string id = ComputeId(dependency.UpstreamId, value);
            if (!builder.TryAdd(id, new DestinationConfig
                {
                    Address = value.Address,
                    Health = value.Health,
                    Host = value.Host,
                }))
                throw new InvalidOperationException("Discovery destination identity collision detected.");
        }
        return builder.ToImmutable();
    }

    private static Projected ProjectOne(
        GatewayRuntimeDependencyBinding dependency,
        DiscoveryProfileCapability capability,
        GatewayDiscoveryEndpoint endpoint)
    {
        Uri address;
        string? featureHost;
        switch (endpoint)
        {
            case GatewayUriDiscoveryEndpoint uri:
                address = uri.Address;
                featureHost = uri.HostName;
                break;
            case GatewayDnsDiscoveryEndpoint dns:
                if (!CanonicalDns(dns.Host) || dns.Port is < 1 or > 65_535)
                    throw new InvalidOperationException("DNS discovery endpoint is invalid.");
                address = Build(dependency.Schemes[0], dns.Host, dns.Port);
                featureHost = dns.HostName ?? dns.Host;
                break;
            case GatewayIpDiscoveryEndpoint ip:
                if (ip.Port is < 1 or > 65_535 || dependency.Schemes[0] != ServiceDiscoveryScheme.Http)
                    throw new InvalidOperationException("Literal-IP discovery is supported only for HTTP.");
                address = Build(ServiceDiscoveryScheme.Http, ip.Address.ToString(), ip.Port);
                featureHost = ip.HostName;
                break;
            default:
                throw new InvalidOperationException("Discovery endpoint kind is unsupported.");
        }

        ServiceDiscoveryScheme scheme = address.Scheme switch
        {
            "http" => ServiceDiscoveryScheme.Http,
            "https" => ServiceDiscoveryScheme.Https,
            _ => throw new InvalidOperationException("Discovery URI scheme is unsupported."),
        };
        if (!dependency.Schemes.Contains(scheme) || !address.IsAbsoluteUri || address.UserInfo.Length != 0 ||
            address.Query.Length != 0 || address.Fragment.Length != 0 || address.Port is < 1 or > 65_535 ||
            Encoding.UTF8.GetByteCount(address.AbsoluteUri) > MaximumAddressUtf8Bytes)
            throw new InvalidOperationException("Discovery URI violates the governed address contract.");
        if (scheme == ServiceDiscoveryScheme.Https &&
            (dependency.TlsServerName is not { } tls || IPAddress.TryParse(address.Host, out _) ||
             !StringComparer.OrdinalIgnoreCase.Equals(address.Host, tls)))
            throw new InvalidOperationException("HTTPS discovery authority does not match the accepted TLS identity.");

        string? host = null;
        if (capability.SupportsHttpAuthorityProjection && featureHost is not null)
        {
            if (!ValidHost(featureHost)) throw new InvalidOperationException("Discovery HTTP authority is invalid.");
            host = featureHost;
        }
        return new Projected(address.AbsoluteUri, null, host, host ?? string.Empty);
    }

    private static Uri Build(ServiceDiscoveryScheme scheme, string host, int port) =>
        new UriBuilder(scheme == ServiceDiscoveryScheme.Https ? "https" : "http", host, port).Uri;

    private static bool CanonicalDns(string value) =>
        !string.IsNullOrEmpty(value) && value.Length <= MaximumHostUtf8Bytes &&
        value.All(static c => c <= 0x7f && (char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c is '-' or '.')) &&
        Uri.CheckHostName(value) == UriHostNameType.Dns && value.TrimEnd('.') == value;

    private static bool ValidHost(string value) => !string.IsNullOrWhiteSpace(value) &&
        Encoding.UTF8.GetByteCount(value) <= MaximumHostUtf8Bytes && !value.Any(char.IsControl) &&
        value.IndexOfAny(['/', '\\', '?', '#', '@']) < 0;

    private static string ComputeId(string symbolicId, Projected value)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Add("hpd.gateway.discovery-destination.v1");
        Add(symbolicId);
        Add(value.Address);
        Add(value.Health);
        Add(value.Host);
        Add(value.FeatureIdentity);
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();

        void Add(string? text)
        {
            if (text is null) { Span<byte> absent = stackalloc byte[4]; absent.Fill(0xff); hash.AppendData(absent); return; }
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            Span<byte> length = stackalloc byte[4];
            BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length);
            hash.AppendData(length); hash.AppendData(bytes);
        }
    }

    private sealed record Projected(string Address, string? Health, string? Host, string FeatureIdentity);
}
