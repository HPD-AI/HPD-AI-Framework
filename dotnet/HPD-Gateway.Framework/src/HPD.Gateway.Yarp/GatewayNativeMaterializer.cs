using System.Collections.Immutable;
using System.Net;
using System.Net.Http;
using HPD.Gateway.Abstractions;
using HPD.Gateway.Core;
using HPD.Gateway.Inspection;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Forwarder;
using Yarp.ReverseProxy.LoadBalancing;
using Yarp.ReverseProxy.Transforms;

namespace HPD.Gateway.Yarp;

internal sealed record GatewayMaterializationDiagnostic(string Code, string Path, string SafeMessage);

internal sealed class GatewayMaterializationResult
{
    private GatewayMaterializationResult(NativePublicationBundle? bundle, ImmutableArray<GatewayMaterializationDiagnostic> diagnostics)
    {
        Bundle = bundle;
        Diagnostics = diagnostics;
    }

    internal NativePublicationBundle? Bundle { get; }
    internal ImmutableArray<GatewayMaterializationDiagnostic> Diagnostics { get; }
    internal bool IsMaterialized => Bundle is not null && Diagnostics.IsEmpty;

    internal static GatewayMaterializationResult Accepted(NativePublicationBundle bundle) => new(bundle, []);
    internal static GatewayMaterializationResult Rejected(ImmutableArray<GatewayMaterializationDiagnostic> diagnostics) => new(null, diagnostics);
}

internal sealed class GatewayNativeMaterializer(IConfigValidator nativeValidator, GatewayInspectionRegistry? inspectionRegistry = null)
{
    private const int MaximumDiagnostics = 256;
    private readonly IConfigValidator _nativeValidator = nativeValidator;
    private readonly GatewayInspectionRegistry? _inspectionRegistry = inspectionRegistry;

    internal async ValueTask<GatewayMaterializationResult> MaterializeAsync(
        GatewayCandidateReadResult candidate,
        PublicationCandidateIdentity identity,
        string nativeRevisionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(identity);
        if (!candidate.IsAccepted || candidate.Configuration is null || candidate.CanonicalDocument is null)
            return Reject("materialization.candidate-not-accepted", "$", "Only an authoritative accepted candidate can be materialized.");
        if (candidate.CanonicalDocument.ContentHash != identity.ContentHash)
            return Reject("materialization.content-identity-mismatch", "$", "Publication identity does not match the accepted canonical content.");

        var configuration = candidate.Configuration;
        var diagnostics = ImmutableArray.CreateBuilder<GatewayMaterializationDiagnostic>();
        RejectUnrealizedSelections(configuration, diagnostics);
        if (diagnostics.Count > 0) return GatewayMaterializationResult.Rejected(diagnostics.ToImmutable());

        ImmutableArray<ClusterConfig> clusters;
        ImmutableArray<RouteConfig> routes;
        try
        {
            clusters = configuration.Upstreams
                .OrderBy(static upstream => upstream.Id.Value, StringComparer.Ordinal)
                .Select(MaterializeCluster)
                .ToImmutableArray();
            routes = configuration.Routes
                .Where(static route => route.Enabled)
                .OrderBy(static route => route.Id.Value, StringComparer.Ordinal)
                .Select(route => MaterializeRoute(route, configuration.RootDefaults!, configuration.Definitions!))
                .ToImmutableArray();
        }
        catch (Exception)
        {
            return Reject("materialization.failed", "$", "The accepted candidate could not be converted into native configuration.");
        }

        for (var index = 0; index < clusters.Length && diagnostics.Count < MaximumDiagnostics; index++)
        {
            if (cancellationToken.IsCancellationRequested)
                return Reject("materialization.canceled", "$", "Materialization was canceled before native validation completed.");
            try
            {
                var errors = await _nativeValidator.ValidateClusterAsync(clusters[index]).ConfigureAwait(false);
                if (errors.Count > 0) Add(diagnostics, "native.cluster-validation-failed", $"upstreams[id={clusters[index].ClusterId}]", "YARP rejected the materialized Cluster configuration.");
            }
            catch (Exception)
            {
                Add(diagnostics, "native.cluster-validation-failed", $"upstreams[id={clusters[index].ClusterId}]", "YARP native Cluster validation failed unexpectedly.");
            }
        }
        for (var index = 0; index < routes.Length && diagnostics.Count < MaximumDiagnostics; index++)
        {
            if (cancellationToken.IsCancellationRequested)
                return Reject("materialization.canceled", "$", "Materialization was canceled before native validation completed.");
            try
            {
                var errors = await _nativeValidator.ValidateRouteAsync(routes[index]).ConfigureAwait(false);
                if (errors.Count > 0) Add(diagnostics, "native.route-validation-failed", $"routes[id={routes[index].RouteId}]", "YARP rejected the materialized Route configuration.");
            }
            catch (Exception)
            {
                Add(diagnostics, "native.route-validation-failed", $"routes[id={routes[index].RouteId}]", "YARP native Route validation failed unexpectedly.");
            }
        }
        if (diagnostics.Count > 0) return GatewayMaterializationResult.Rejected(diagnostics.ToImmutable());

        try
        {
            return GatewayMaterializationResult.Accepted(NativePublicationBundle.Create(identity, routes, clusters, nativeRevisionId));
        }
        catch (Exception)
        {
            return Reject("materialization.bundle-invalid", "$", "Native publication identity or bundle data is invalid.");
        }
    }

    private RouteConfig MaterializeRoute(RouteDeclaration route, GatewayRootDeclarations root, GatewayDefinitions definitions)
    {
        var declarations = route.Declarations!;
        var timeout = Resolve(declarations.RequestTimeout ?? root.RequestTimeout, definitions.RequestTimeout);
        var inspection = Resolve(declarations.Inspection ?? root.Inspection, definitions.Inspection);
        var metadata = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
        metadata.Add("hpd.gateway.route-id", route.Id.Value);
        if (inspection is not null)
        {
            if (_inspectionRegistry is null || !_inspectionRegistry.TryGet(inspection.InspectorName, out _))
                throw new InvalidOperationException("Accepted inspector is not installed in the runtime registry.");
            metadata.Add(GatewayInspectionMetadata.Inspector, inspection.InspectorName);
            metadata.Add(GatewayInspectionMetadata.Mode, inspection.Mode.ToString());
            metadata.Add(GatewayInspectionMetadata.MaximumAccepted, inspection.MaximumAcceptedBodyBytes.ToString(System.Globalization.CultureInfo.InvariantCulture));
            metadata.Add(GatewayInspectionMetadata.Spill, inspection.SpillPolicy.ToString());
            if (inspection.MaximumInspectedBytes is { } inspected)
                metadata.Add(GatewayInspectionMetadata.MaximumInspected, inspected.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (inspection.MemoryThresholdBytes is { } threshold)
                metadata.Add(GatewayInspectionMetadata.MemoryThreshold, threshold.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        var native = new RouteConfig
        {
            RouteId = route.Id.Value,
            ClusterId = route.Upstream.Value,
            Order = route.Order,
            Match = MaterializeMatch(route.Match),
            AuthorizationPolicy = Resolve(declarations.Authorization ?? root.Authorization, definitions.Authorization)?.PolicyName,
            CorsPolicy = Resolve(declarations.Cors ?? root.Cors, definitions.Cors)?.PolicyName,
            RateLimiterPolicy = Resolve(declarations.TrafficAdmission ?? root.TrafficAdmission, definitions.TrafficAdmission)?.PolicyName,
            OutputCachePolicy = Resolve(declarations.OutputCache ?? root.OutputCache, definitions.OutputCache)?.PolicyName,
            TimeoutPolicy = timeout?.PolicyName,
            Timeout = timeout?.Timeout,
            Metadata = metadata.ToImmutable()
        };

        foreach (var transform in declarations.RequestTransforms?.Headers ?? [])
            native = transform.Kind switch
            {
                HeaderTransformKind.Set => native.WithTransformRequestHeader(transform.Name, transform.Value!, append: false),
                HeaderTransformKind.Append => native.WithTransformRequestHeader(transform.Name, transform.Value!, append: true),
                HeaderTransformKind.Remove => native.WithTransformRequestHeaderRemove(transform.Name),
                _ => throw new InvalidOperationException()
            };
        foreach (var transform in declarations.ResponseTransforms?.Headers ?? [])
            native = transform.Kind switch
            {
                HeaderTransformKind.Set => native.WithTransformResponseHeader(transform.Name, transform.Value!, append: false),
                HeaderTransformKind.Append => native.WithTransformResponseHeader(transform.Name, transform.Value!, append: true),
                HeaderTransformKind.Remove => native.WithTransformResponseHeaderRemove(transform.Name),
                _ => throw new InvalidOperationException()
            };
        foreach (var transform in declarations.ResponseTransforms?.Trailers ?? [])
            native = transform.Kind switch
            {
                HeaderTransformKind.Set => native.WithTransformResponseTrailer(transform.Name, transform.Value!, append: false),
                HeaderTransformKind.Append => native.WithTransformResponseTrailer(transform.Name, transform.Value!, append: true),
                HeaderTransformKind.Remove => native.WithTransformResponseTrailerRemove(transform.Name),
                _ => throw new InvalidOperationException()
            };
        return native;
    }

    private static RouteMatch MaterializeMatch(HttpRouteMatch match) => new()
    {
        Methods = match.Methods.Select(static method => method.ToUpperInvariant()).Order(StringComparer.Ordinal).ToImmutableArray(),
        Hosts = match.Hosts.Select(static host => host.ToLowerInvariant()).Order(StringComparer.Ordinal).ToImmutableArray(),
        Path = match.Path,
        Headers = match.Headers
            .OrderBy(static header => header.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static header => header.Kind)
            .Select(static header => new RouteHeader
            {
                Name = header.Name.ToLowerInvariant(),
                Mode = header.Kind switch
                {
                    TextMatchKind.Exact => HeaderMatchMode.ExactHeader,
                    TextMatchKind.Prefix => HeaderMatchMode.HeaderPrefix,
                    TextMatchKind.Contains => HeaderMatchMode.Contains,
                    TextMatchKind.Exists => HeaderMatchMode.Exists,
                    TextMatchKind.NotExists => HeaderMatchMode.NotExists,
                    _ => throw new InvalidOperationException()
                },
                Values = SortValues(header.Values, header.CaseSensitive),
                IsCaseSensitive = header.CaseSensitive
            }).ToImmutableArray(),
        QueryParameters = match.Query
            .OrderBy(static query => query.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static query => query.Kind)
            .Select(static query => new RouteQueryParameter
            {
                Name = query.Name.ToLowerInvariant(),
                Mode = query.Kind switch
                {
                    TextMatchKind.Exact => QueryParameterMatchMode.Exact,
                    TextMatchKind.Prefix => QueryParameterMatchMode.Prefix,
                    TextMatchKind.Contains => QueryParameterMatchMode.Contains,
                    TextMatchKind.Exists => QueryParameterMatchMode.Exists,
                    _ => throw new InvalidOperationException()
                },
                Values = SortValues(query.Values, query.CaseSensitive),
                IsCaseSensitive = query.CaseSensitive
            }).ToImmutableArray()
    };

    private static ImmutableArray<string> SortValues(ImmutableArray<string> values, bool caseSensitive) => values
        .Order(caseSensitive ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase)
        .ToImmutableArray();

    private static ClusterConfig MaterializeCluster(UpstreamDeclaration upstream)
    {
        var source = (StaticEndpointSource)upstream.Endpoints;
        var metadata = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
        metadata.Add("hpd.gateway.upstream-id", upstream.Id.Value);
        metadata.Add(HpdForwarderHttpClientFactory.UseProxyMetadata, upstream.Transport.UseProxy ? bool.TrueString : bool.FalseString);
        if (upstream.Transport.ConnectTimeout is { } connectTimeout)
            metadata.Add(HpdForwarderHttpClientFactory.ConnectTimeoutTicksMetadata, connectTimeout.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture));

        return new ClusterConfig
        {
            ClusterId = upstream.Id.Value,
            LoadBalancingPolicy = upstream.LoadBalancing.Kind switch
            {
                LoadBalancingKind.PowerOfTwoChoices => LoadBalancingPolicies.PowerOfTwoChoices,
                LoadBalancingKind.RoundRobin => LoadBalancingPolicies.RoundRobin,
                LoadBalancingKind.LeastRequests => LoadBalancingPolicies.LeastRequests,
                LoadBalancingKind.Random => LoadBalancingPolicies.Random,
                _ => throw new InvalidOperationException()
            },
            Destinations = source.Destinations
                .ToImmutableSortedDictionary(
                    static destination => destination.Id.Value,
                    static destination => new DestinationConfig
                    {
                        Address = destination.Address.AbsoluteUri,
                        Health = destination.HealthAddress?.AbsoluteUri,
                        Host = destination.HostOverride
                    },
                    StringComparer.Ordinal),
            SessionAffinity = upstream.SessionAffinity is { } affinity ? new SessionAffinityConfig
            {
                Enabled = true,
                Policy = affinity.Policy,
                FailurePolicy = affinity.FailurePolicy,
                AffinityKeyName = affinity.CookieName ?? $"hpd-affinity-{upstream.Id.Value}",
                Cookie = affinity.CookieName is null ? null : new SessionAffinityCookieConfig { HttpOnly = true }
            } : null,
            HealthCheck = MaterializeHealth(upstream.HealthChecks),
            HttpClient = new HttpClientConfig
            {
                MaxConnectionsPerServer = upstream.Transport.MaxConnectionsPerServer,
                EnableMultipleHttp2Connections = upstream.Transport.EnableMultipleHttp2Connections,
                RequestHeaderEncoding = upstream.Transport.RequestHeaderEncodingLatin1 ? "iso-8859-1" : null
            },
            HttpRequest = new ForwarderRequestConfig
            {
                ActivityTimeout = upstream.Request.ActivityTimeout,
                Version = upstream.Request.Version switch
                {
                    UpstreamHttpVersion.Http11 => HttpVersion.Version11,
                    UpstreamHttpVersion.Http2 => HttpVersion.Version20,
                    UpstreamHttpVersion.Http3 => HttpVersion.Version30,
                    _ => throw new InvalidOperationException()
                },
                VersionPolicy = upstream.Request.VersionSelection switch
                {
                    HttpVersionSelection.RequestVersionOrLower => HttpVersionPolicy.RequestVersionOrLower,
                    HttpVersionSelection.RequestVersionOrHigher => HttpVersionPolicy.RequestVersionOrHigher,
                    HttpVersionSelection.Exact => HttpVersionPolicy.RequestVersionExact,
                    _ => throw new InvalidOperationException()
                },
                AllowResponseBuffering = upstream.Request.AllowResponseBuffering
            },
            Metadata = metadata.ToImmutable()
        };
    }

    private static HealthCheckConfig? MaterializeHealth(HealthCheckDeclaration? health) => health is null ? null : new HealthCheckConfig
    {
        Passive = health.Passive is { } passive ? new PassiveHealthCheckConfig
        {
            Enabled = passive.Enabled,
            Policy = passive.Policy,
            ReactivationPeriod = passive.ReactivationPeriod
        } : null,
        Active = health.Active is { } active ? new ActiveHealthCheckConfig
        {
            Enabled = active.Enabled,
            Interval = active.Interval,
            Timeout = active.Timeout,
            Policy = active.Policy,
            Path = active.Path
        } : null
    };

    private static T? Resolve<T>(DeclarationReference<T>? reference, ImmutableArray<DeclarationDefinition<T>> definitions)
        where T : class
    {
        if (reference is null) return null;
        if (reference.Inline is not null) return reference.Inline;
        if (reference.Definition is { } id)
            return definitions.FirstOrDefault(definition => definition.Id == id)?.Specification
                ?? throw new InvalidOperationException("Accepted definition reference is unresolved.");
        throw new InvalidOperationException("Accepted declaration reference is empty.");
    }

    private void RejectUnrealizedSelections(GatewayConfiguration configuration, ImmutableArray<GatewayMaterializationDiagnostic>.Builder diagnostics)
    {
        for (var index = 0; index < configuration.Upstreams.Length; index++)
        {
            var upstream = configuration.Upstreams[index];
            if (upstream.Endpoints is DiscoveredEndpointSource)
                Add(diagnostics, "materialization.discovery-observation-required", $"upstreams[{index}].endpoints", "Discovery requires an immutable provider observation before materialization.");
            if (upstream.Transport.Tls is not null)
                Add(diagnostics, "materialization.tls-resolution-required", $"upstreams[{index}].transport.tls", "TLS requires resolved ephemeral material and a compatible static client factory.");
        }

        if (configuration.RootDefaults!.Telemetry is not null && configuration.Routes.Any(static route => route.Enabled))
            Add(diagnostics, "materialization.telemetry-runtime-required", "rootDefaults.telemetry", "Telemetry enrichment requires statically installed instrumentation.");

        for (var index = 0; index < configuration.Routes.Length; index++)
        {
            if (!configuration.Routes[index].Enabled) continue;
            var declarations = configuration.Routes[index].Declarations!;
            if (declarations.Telemetry is not null)
                Add(diagnostics, "materialization.telemetry-runtime-required", $"routes[{index}].declarations.telemetry", "Telemetry enrichment requires statically installed instrumentation.");
            var inspection = Resolve(declarations.Inspection ?? configuration.RootDefaults.Inspection, configuration.Definitions!.Inspection);
            if (inspection is not null && (_inspectionRegistry is null || !_inspectionRegistry.TryGet(inspection.InspectorName, out _)))
                Add(diagnostics, "materialization.inspector-not-installed", $"routes[id={configuration.Routes[index].Id.Value}].declarations.inspection", "The selected request inspector is not installed in the runtime registry.");
        }
    }

    private static GatewayMaterializationResult Reject(string code, string path, string message) =>
        GatewayMaterializationResult.Rejected([new GatewayMaterializationDiagnostic(code, path, message)]);

    private static void Add(ImmutableArray<GatewayMaterializationDiagnostic>.Builder diagnostics, string code, string path, string message)
    {
        if (diagnostics.Count < MaximumDiagnostics) diagnostics.Add(new GatewayMaterializationDiagnostic(code, path, message));
    }
}
