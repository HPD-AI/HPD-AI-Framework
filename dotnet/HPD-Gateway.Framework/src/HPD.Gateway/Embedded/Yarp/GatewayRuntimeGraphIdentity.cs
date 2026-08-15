using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Yarp.ReverseProxy.Configuration;

namespace HPD.Gateway;

internal static class GatewayRuntimeGraphIdentity
{
    internal static ContentHash ComputePlan(
        PublicationCandidateIdentity identity,
        ImmutableArray<RouteConfig> routes,
        ImmutableArray<ClusterConfig> clusters,
        ImmutableArray<GatewayRuntimeDependencyBinding> dependencies,
        GatewayPreparedProjectionSnapshot effective)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(effective);
        if (routes.IsDefault || clusters.IsDefault || dependencies.IsDefault || effective.Records.IsDefault)
            throw new ArgumentException("Runtime graph identity inputs must be initialized.");
        using var writer = new Writer("hpd.gateway.runtime-plan.v2");
        writer.Add(identity.CandidateId.Value);
        writer.Add(identity.ContentHash.Algorithm);
        writer.Add(identity.ContentHash.Value);
        writer.Add(identity.AuthorityId);
        writer.Add(identity.AuthorityEpoch);
        writer.Add(identity.AuthorityVersion);
        writer.Add(routes.Length);
        foreach (RouteConfig route in routes) Add(writer, route);
        writer.Add(clusters.Length);
        foreach (ClusterConfig cluster in clusters) Add(writer, cluster);
        writer.Add(dependencies.Length);
        foreach (GatewayRuntimeDependencyBinding dependency in dependencies) Add(writer, dependency);
        Add(writer, effective);
        return writer.Complete();
    }

    internal static ContentHash ComputeMembership(
        IReadOnlyDictionary<string, DestinationConfig> destinations,
        GatewayPreparedMembershipDisposition disposition)
    {
        using var writer = new Writer("hpd.gateway.runtime-membership.v1");
        writer.Add((byte)disposition);
        Add(writer, destinations);
        return writer.Complete();
    }

    internal static ContentHash ComputeNativeGeneration(
        IReadOnlyList<RouteConfig> routes,
        IReadOnlyList<ClusterConfig> clusters)
    {
        ArgumentNullException.ThrowIfNull(routes);
        ArgumentNullException.ThrowIfNull(clusters);
        if (routes.Any(static value => value is null) || clusters.Any(static value => value is null))
            throw new ArgumentException("Native generation resources cannot contain null entries.");
        RouteConfig[] orderedRoutes = routes.OrderBy(static value => value.RouteId, StringComparer.Ordinal).ToArray();
        ClusterConfig[] orderedClusters = clusters.OrderBy(static value => value.ClusterId, StringComparer.Ordinal).ToArray();
        if (orderedRoutes.Select(static value => value.RouteId).Distinct(StringComparer.Ordinal).Count() != orderedRoutes.Length ||
            orderedClusters.Select(static value => value.ClusterId).Distinct(StringComparer.Ordinal).Count() != orderedClusters.Length)
            throw new ArgumentException("Native generation resource identities must be unique.");
        using var writer = new Writer("hpd.gateway.native-generation.v1");
        writer.Add(orderedRoutes.Length);
        foreach (RouteConfig route in orderedRoutes) Add(writer, route);
        writer.Add(orderedClusters.Length);
        foreach (ClusterConfig cluster in orderedClusters) Add(writer, cluster);
        return writer.Complete();
    }

    private static void Add(Writer writer, RouteConfig value)
    {
        writer.Add(value.RouteId);
        writer.Add(value.ClusterId);
        writer.Add(value.Order);
        writer.Add(value.AuthorizationPolicy);
        writer.Add(value.OutputCachePolicy);
        writer.Add(value.TimeoutPolicy);
        writer.Add(value.Timeout?.Ticks);
        writer.Add(value.CorsPolicy);
        writer.Add(value.MaxRequestBodySize);
        Add(writer, value.Match);
        Add(writer, value.Metadata, excludePlanIdentity: true);
        if (value.Transforms is null) { writer.AddNull(); return; }
        writer.Add(value.Transforms.Count);
        foreach (IReadOnlyDictionary<string, string> transform in value.Transforms)
            Add(writer, transform, excludePlanIdentity: false);
    }

    private static void Add(Writer writer, RouteMatch? value)
    {
        if (value is null) { writer.AddNull(); return; }
        Add(writer, value.Methods);
        Add(writer, value.Hosts);
        writer.Add(value.Path);
        if (value.Headers is null) writer.AddNull();
        else
        {
            writer.Add(value.Headers.Count);
            foreach (RouteHeader header in value.Headers)
            {
                writer.Add(header.Name);
                writer.Add((int)header.Mode);
                writer.Add(header.IsCaseSensitive);
                Add(writer, header.Values);
            }
        }
        if (value.QueryParameters is null) writer.AddNull();
        else
        {
            writer.Add(value.QueryParameters.Count);
            foreach (RouteQueryParameter query in value.QueryParameters)
            {
                writer.Add(query.Name);
                writer.Add((int)query.Mode);
                writer.Add(query.IsCaseSensitive);
                Add(writer, query.Values);
            }
        }
    }

    private static void Add(Writer writer, ClusterConfig value)
    {
        writer.Add(value.ClusterId);
        writer.Add(value.LoadBalancingPolicy);
        Add(writer, value.SessionAffinity);
        Add(writer, value.HealthCheck);
        Add(writer, value.HttpClient);
        Add(writer, value.HttpRequest);
        Add(writer, value.Destinations);
        Add(writer, value.Metadata, excludePlanIdentity: true);
    }

    private static void Add(Writer writer, SessionAffinityConfig? value)
    {
        if (value is null) { writer.AddNull(); return; }
        writer.Add(value.Enabled);
        writer.Add(value.Policy);
        writer.Add(value.FailurePolicy);
        writer.Add(value.AffinityKeyName);
        SessionAffinityCookieConfig? cookie = value.Cookie;
        if (cookie is null) { writer.AddNull(); return; }
        writer.Add(cookie.Path);
        writer.Add(cookie.Domain);
        writer.Add(cookie.HttpOnly);
        writer.Add(cookie.SecurePolicy is null ? null : (int)cookie.SecurePolicy.Value);
        writer.Add(cookie.SameSite is null ? null : (int)cookie.SameSite.Value);
        writer.Add(cookie.Expiration?.Ticks);
        writer.Add(cookie.MaxAge?.Ticks);
        writer.Add(cookie.IsEssential);
    }

    private static void Add(Writer writer, HealthCheckConfig? value)
    {
        if (value is null) { writer.AddNull(); return; }
        PassiveHealthCheckConfig? passive = value.Passive;
        if (passive is null) writer.AddNull();
        else
        {
            writer.Add(passive.Enabled);
            writer.Add(passive.Policy);
            writer.Add(passive.ReactivationPeriod?.Ticks);
        }
        ActiveHealthCheckConfig? active = value.Active;
        if (active is null) writer.AddNull();
        else
        {
            writer.Add(active.Enabled);
            writer.Add(active.Interval?.Ticks);
            writer.Add(active.Timeout?.Ticks);
            writer.Add(active.Policy);
            writer.Add(active.Path);
            writer.Add(active.Query);
        }
        writer.Add(value.AvailableDestinationsPolicy);
    }

    private static void Add(Writer writer, HttpClientConfig? value)
    {
        if (value is null) { writer.AddNull(); return; }
        writer.Add(value.SslProtocols is null ? null : (int)value.SslProtocols.Value);
        writer.Add(value.DangerousAcceptAnyServerCertificate);
        writer.Add(value.MaxConnectionsPerServer);
        writer.Add(value.EnableMultipleHttp2Connections);
        writer.Add(value.RequestHeaderEncoding);
        writer.Add(value.ResponseHeaderEncoding);
        if (value.WebProxy is null) writer.AddNull();
        else
        {
            writer.Add(value.WebProxy.Address?.AbsoluteUri);
            writer.Add(value.WebProxy.BypassOnLocal);
            writer.Add(value.WebProxy.UseDefaultCredentials);
        }
    }

    private static void Add(Writer writer, global::Yarp.ReverseProxy.Forwarder.ForwarderRequestConfig? value)
    {
        if (value is null) { writer.AddNull(); return; }
        writer.Add(value.ActivityTimeout?.Ticks);
        writer.Add(value.Version?.ToString());
        writer.Add(value.VersionPolicy is null ? null : (int)value.VersionPolicy.Value);
        writer.Add(value.AllowResponseBuffering);
    }

    private static void Add(Writer writer, IReadOnlyDictionary<string, DestinationConfig>? values)
    {
        if (values is null) { writer.AddNull(); return; }
        writer.Add(values.Count);
        foreach (KeyValuePair<string, DestinationConfig> pair in values.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            writer.Add(pair.Key);
            writer.Add(pair.Value.Address);
            writer.Add(pair.Value.Health);
            writer.Add(pair.Value.Host);
            Add(writer, pair.Value.Metadata, excludePlanIdentity: true);
        }
    }

    private static void Add(Writer writer, IReadOnlyDictionary<string, string>? values, bool excludePlanIdentity)
    {
        if (values is null) { writer.AddNull(); return; }
        KeyValuePair<string, string>[] admitted = values
            .Where(pair => !excludePlanIdentity || pair.Key is not (
                GatewayRuntimePlanner.ApplicationIdMetadata or GatewayRuntimePlanner.SymbolicPlanIdentityMetadata))
            .OrderBy(static pair => pair.Key, StringComparer.Ordinal)
            .ToArray();
        writer.Add(admitted.Length);
        foreach (KeyValuePair<string, string> pair in admitted)
        {
            writer.Add(pair.Key);
            writer.Add(pair.Value);
        }
    }

    private static void Add(Writer writer, IReadOnlyList<string>? values)
    {
        if (values is null) { writer.AddNull(); return; }
        writer.Add(values.Count);
        foreach (string value in values) writer.Add(value);
    }

    private static void Add(Writer writer, GatewayRuntimeDependencyBinding value)
    {
        writer.Add(value.UpstreamId);
        writer.Add(value.Profile.Value);
        writer.Add(value.Service.Value);
        writer.Add(value.Endpoint?.Value);
        writer.Add(value.Schemes.Length);
        foreach (ServiceDiscoveryScheme scheme in value.Schemes) writer.Add((byte)scheme);
        writer.Add(value.TlsServerName);
        writer.Add((byte)value.StaleBehavior);
        writer.Add(value.CapabilityIdentity.Algorithm);
        writer.Add(value.CapabilityIdentity.Value);
        writer.Add(value.MaximumEndpoints);
    }

    private static void Add(Writer writer, GatewayPreparedProjectionSnapshot value)
    {
        writer.Add(value.SchemaVersion);
        writer.Add(value.CandidateId.Value);
        writer.Add(value.CandidateContentHash.Algorithm);
        writer.Add(value.CandidateContentHash.Value);
        writer.Add(value.IsTruncated);
        writer.Add(value.Records.Length);
        foreach (GatewayEffectiveRecord record in value.Records)
        {
            writer.Add(record.SchemaVersion);
            writer.Add((byte)record.TargetKind);
            writer.Add(record.TargetId);
            writer.Add(record.Family);
            writer.Add((byte)record.Composition);
            writer.Add(record.Contributions.Length);
            foreach (GatewayEffectiveContribution contribution in record.Contributions)
            {
                writer.Add((byte)contribution.SourceKind);
                writer.Add((byte)contribution.Scope);
                writer.Add((byte)contribution.Disposition);
                writer.Add(contribution.SourceIdentity);
                writer.Add(contribution.Definition?.Value);
                writer.Add(contribution.DeterministicOrder);
                writer.Add(contribution.ContentHash.Algorithm);
                writer.Add(contribution.ContentHash.Value);
            }
            writer.Add(record.NativeProjection.Owner);
            writer.Add(record.NativeProjection.Seam);
            writer.Add(record.NativeProjection.PackageIdentity);
            writer.Add(record.CompilerPackage);
            writer.Add(record.CompilerVersion);
            writer.Add((byte)record.Disposition);
            writer.Add(record.EffectiveContentHash.Algorithm);
            writer.Add(record.EffectiveContentHash.Value);
            writer.Add(record.Diagnostics.Length);
            foreach (GatewayEffectiveDiagnostic diagnostic in record.Diagnostics)
            {
                writer.Add(diagnostic.Code);
                writer.Add(diagnostic.SafeMessage);
            }
        }
    }

    private sealed class Writer : IDisposable
    {
        private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        internal Writer(string domain) => Add(domain);

        internal void AddNull() => Add("<null>");
        internal void Add(string? value)
        {
            if (value is null) { AddNull(); return; }
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            Span<byte> length = stackalloc byte[4];
            BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length);
            _hash.AppendData(length);
            _hash.AppendData(bytes);
        }
        internal void Add(bool? value) => Add(value?.ToString(CultureInfo.InvariantCulture));
        internal void Add(bool value) => Add(value.ToString(CultureInfo.InvariantCulture));
        internal void Add(byte value) => Add(value.ToString(CultureInfo.InvariantCulture));
        internal void Add(ushort value) => Add(value.ToString(CultureInfo.InvariantCulture));
        internal void Add(int value) => Add(value.ToString(CultureInfo.InvariantCulture));
        internal void Add(int? value) => Add(value?.ToString(CultureInfo.InvariantCulture));
        internal void Add(long value) => Add(value.ToString(CultureInfo.InvariantCulture));
        internal void Add(long? value) => Add(value?.ToString(CultureInfo.InvariantCulture));
        internal void Add(ulong value) => Add(value.ToString(CultureInfo.InvariantCulture));
        internal ContentHash Complete() => new("sha-256", Convert.ToHexStringLower(_hash.GetHashAndReset()));
        public void Dispose() => _hash.Dispose();
    }
}
