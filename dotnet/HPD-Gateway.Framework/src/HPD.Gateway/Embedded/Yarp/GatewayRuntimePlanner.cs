using System.Collections.Immutable;
using System.Net;
using System.Net.Http;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Forwarder;
using Yarp.ReverseProxy.LoadBalancing;
using Yarp.ReverseProxy.Transforms;

namespace HPD.Gateway;

internal sealed record GatewayRuntimePlanningDiagnostic(string Code, string Path, string SafeMessage);

internal sealed class GatewayRuntimePlanningResult
{
    private GatewayRuntimePlanningResult(GatewayRuntimePlan? plan, GatewayPreparedApplication? preparedApplication, ImmutableArray<GatewayRuntimePlanningDiagnostic> diagnostics)
    {
        Plan = plan;
        PreparedApplication = preparedApplication;
        Diagnostics = diagnostics;
    }

    internal GatewayRuntimePlan? Plan { get; }
    internal GatewayPreparedApplication? PreparedApplication { get; }
    internal GatewayPreparedProjectionSnapshot? PreparedProjectionSnapshot => Plan?.Effective.Snapshot;
    internal ImmutableArray<GatewayRuntimePlanningDiagnostic> Diagnostics { get; }
    internal bool IsPlanned => Plan is not null && Diagnostics.IsEmpty;

    internal static GatewayRuntimePlanningResult Accepted(GatewayRuntimePlan plan, GatewayPreparedApplication? preparedApplication) => new(plan, preparedApplication, []);
    internal static GatewayRuntimePlanningResult Rejected(ImmutableArray<GatewayRuntimePlanningDiagnostic> diagnostics) => new(null, null, diagnostics);
}

internal sealed class GatewayRuntimePlanner(
    IConfigValidator nativeValidator,
    GatewayInspectionRegistry? inspectionRegistry = null,
    GatewayUpstreamResilienceProvider? resilienceRegistry = null,
    IGatewayOutputCacheRuntimeCapabilityProvider? outputCacheRuntime = null,
    GatewayDestinationResolver? destinationResolver = null)
{
    internal const string ApplicationIdMetadata = "hpd.gateway.application-id";
    internal const string SymbolicPlanIdentityMetadata = "hpd.gateway.symbolic-plan-id";
    internal const string SymbolicDestinationMetadata = "hpd.gateway.symbolic-destination";
    internal const string DiscoveryProfileMetadata = "hpd.gateway.discovery-profile";
    internal const string DiscoveryCapabilityIdentityMetadata = "hpd.gateway.discovery-capability-id";
    internal const string DiscoveryServiceMetadata = "hpd.gateway.discovery-service";
    internal const string DiscoveryEndpointMetadata = "hpd.gateway.discovery-endpoint";
    internal const string DiscoverySchemesMetadata = "hpd.gateway.discovery-schemes";
    internal const string DiscoveryStaleBehaviorMetadata = "hpd.gateway.discovery-stale-behavior";
    internal const string ResilienceCapabilityIdentityMetadata = "hpd.gateway.resilience-capability-id";
    private const int MaximumDiagnostics = 256;
    private readonly GatewayRuntimeApplicationPreparer _preparer = new(nativeValidator);
    private readonly GatewayInspectionRegistry? _inspectionRegistry = inspectionRegistry;
    private readonly GatewayUpstreamResilienceProvider? _resilienceRegistry = resilienceRegistry;
    private readonly IGatewayOutputCacheRuntimeCapabilityProvider? _outputCacheRuntime = outputCacheRuntime;
    private readonly GatewayDestinationResolver? _destinationResolver = destinationResolver;

    internal async ValueTask<GatewayRuntimePlanningResult> PlanAsync(
        GatewayCandidateReadResult candidate,
        PublicationCandidateIdentity identity,
        string nativeRevisionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(identity);
        if (!candidate.IsAccepted || candidate.Configuration is null || candidate.CanonicalDocument is null)
            return Reject("planning.candidate-not-accepted", "$", "Only an authoritative accepted candidate can be planned.");
        if (candidate.CanonicalDocument.ContentHash != identity.ContentHash)
            return Reject("planning.content-identity-mismatch", "$", "Publication identity does not match the accepted canonical content.");
        var configuration = candidate.Configuration;
        var diagnostics = ImmutableArray.CreateBuilder<GatewayRuntimePlanningDiagnostic>();
        RejectUnrealizedSelections(configuration, candidate.ProtectedCredentialHeaders, diagnostics);
        if (diagnostics.Count > 0) return GatewayRuntimePlanningResult.Rejected(diagnostics.ToImmutable());

        ImmutableArray<ClusterConfig> clusters;
        ImmutableArray<RouteConfig> routes;
        ImmutableArray<GatewayRuntimeDependencyBinding> dependencies;
        ImmutableDictionary<string, GatewayRuntimeDependencyBinding> dependencyMap;
        GatewayEffectiveProjectionBuilder.PreparedProjection preparedEffective;
        string applicationId;
        ContentHash symbolicPlanIdentity;
        try
        {
            var effective = new GatewayEffectiveProjectionBuilder(candidate, identity);
            dependencies = configuration.Upstreams
                .Where(static upstream => upstream.Endpoints is ServiceDiscoveryEndpointSource)
                .OrderBy(static upstream => upstream.Id.Value, StringComparer.Ordinal)
                .Select(upstream => CreateDependency(upstream, candidate.DiscoveryProfiles))
                .ToImmutableArray();
            dependencyMap = dependencies.ToImmutableDictionary(static value => value.UpstreamId, StringComparer.Ordinal);
            applicationId = GatewayRuntimePlan.CreateApplicationId();
            symbolicPlanIdentity = new ContentHash("sha-256", new string('0', 64));
            clusters = configuration.Upstreams
                .OrderBy(static upstream => upstream.Id.Value, StringComparer.Ordinal)
                .Select(upstream => MaterializeCluster(
                    upstream,
                    applicationId,
                    symbolicPlanIdentity,
                    dependencyMap.GetValueOrDefault(upstream.Id.Value),
                    candidate.UpstreamResilienceProfiles))
                .ToImmutableArray();
            routes = configuration.Routes
                .Where(static route => route.Enabled)
                .OrderBy(static route => route.Id.Value, StringComparer.Ordinal)
                .Select(route => MaterializeRoute(route, configuration.RootDefaults!, configuration.Definitions!,
                    candidate.ProtectedCredentialHeaders, effective, applicationId, symbolicPlanIdentity))
                .ToImmutableArray();
            if (!OutputCacheCapabilitiesMatch(
                    candidate.OutputCacheProfiles,
                    _outputCacheRuntime?.Capabilities,
                    routes.Where(static route => route.OutputCachePolicy is not null)
                        .Select(static route => route.OutputCachePolicy!)
                        .Distinct(StringComparer.Ordinal)))
                return Reject("planning.output-cache-capability-mismatch", "$", "Accepted selected Output Cache capabilities do not match the installed runtime registry.");
            preparedEffective = effective.Build(routes);
            symbolicPlanIdentity = GatewayRuntimePlan.ComputeIdentity(
                identity, routes, clusters, dependencies, preparedEffective.Snapshot);
            routes = routes.Select(route => route with
            {
                Metadata = WithPlanIdentity(route.Metadata, applicationId, symbolicPlanIdentity.Value),
            }).ToImmutableArray();
            clusters = clusters.Select(cluster => cluster with
            {
                Metadata = WithPlanIdentity(cluster.Metadata, applicationId, symbolicPlanIdentity.Value),
                Destinations = cluster.Destinations?.ToImmutableSortedDictionary(
                    static pair => pair.Key,
                    pair => pair.Value with
                    {
                        Metadata = pair.Value.Metadata?.ContainsKey(SymbolicDestinationMetadata) == true
                            ? WithPlanIdentity(pair.Value.Metadata, applicationId, symbolicPlanIdentity.Value)
                            : pair.Value.Metadata,
                    },
                    StringComparer.Ordinal),
            }).ToImmutableArray();
            preparedEffective = effective.Build(routes);
        }
        catch (Exception)
        {
            return Reject("planning.failed", "$", "The accepted candidate could not be converted into native configuration.");
        }

        if (cancellationToken.IsCancellationRequested)
            return Reject("planning.canceled", "$", "Planning was canceled before a runtime plan was admitted.");
        try
        {
            var plan = new GatewayRuntimePlan(identity, routes, clusters, dependencies, preparedEffective, applicationId, symbolicPlanIdentity);
            if (!dependencies.IsEmpty && _destinationResolver is null)
                return GatewayRuntimePlanningResult.Accepted(plan, null);
            var preparation = dependencies.IsEmpty
                ? await _preparer.PrepareAsync(plan, nativeRevisionId, cancellationToken).ConfigureAwait(false)
                : await _destinationResolver!.PrepareAsync(plan, nativeRevisionId, cancellationToken).ConfigureAwait(false);
            return preparation.Application is null
                ? GatewayRuntimePlanningResult.Rejected(preparation.Diagnostics)
                : GatewayRuntimePlanningResult.Accepted(plan, preparation.Application);
        }
        catch (Exception)
        {
            return Reject("planning.application-invalid", "$", "The runtime plan or prepared application is invalid.");
        }
    }

    private static bool OutputCacheCapabilitiesMatch(
        ImmutableDictionary<string, OutputCacheCapability> accepted,
        ImmutableDictionary<string, OutputCacheCapability>? runtime,
        IEnumerable<string> selectedProfiles)
    {
        foreach (var name in selectedProfiles)
        {
            if (runtime is null || !accepted.TryGetValue(name, out var expected) || !runtime.TryGetValue(name, out var installed)) return false;
            if (!StringComparer.Ordinal.Equals(expected.Name, installed.Name) ||
                expected.Version != installed.Version ||
                expected.RetainsDefaultSafetyPolicy != installed.RetainsDefaultSafetyPolicy ||
                !StringComparer.Ordinal.Equals(expected.StoreId, installed.StoreId) ||
                expected.StoreScope != installed.StoreScope ||
                expected.Expiration != installed.Expiration ||
                expected.MaximumBodyBytes != installed.MaximumBodyBytes ||
                expected.StoreCapacityBytes != installed.StoreCapacityBytes ||
                !expected.QueryKeys.SequenceEqual(installed.QueryKeys, StringComparer.Ordinal) ||
                !expected.HeaderNames.SequenceEqual(installed.HeaderNames, StringComparer.Ordinal))
                return false;
        }
        return true;
    }

    private RouteConfig MaterializeRoute(
        RouteDeclaration route,
        GatewayRootDeclarations root,
        GatewayDefinitions definitions,
        ImmutableArray<string> protectedCredentialHeaders,
        GatewayEffectiveProjectionBuilder effective,
        string applicationId,
        ContentHash symbolicPlanIdentity)
    {
        var declarations = route.Declarations!;
        var authorization = effective.Resolve(route.Id, GatewayEffectiveFamilies.Authorization, "RouteConfig.AuthorizationPolicy", root.Authorization, declarations.Authorization, definitions.Authorization,
            static value => GatewayEffectiveProjectionBuilder.Hash("authorization/v1", value.PolicyName));
        var cors = effective.Resolve(route.Id, GatewayEffectiveFamilies.Cors, "RouteConfig.CorsPolicy", root.Cors, declarations.Cors, definitions.Cors,
            static value => GatewayEffectiveProjectionBuilder.Hash("cors/v1", value.PolicyName));
        var admission = effective.Resolve(route.Id, GatewayEffectiveFamilies.TrafficAdmission, "RouteConfig.RateLimiterPolicy", root.TrafficAdmission, declarations.TrafficAdmission, definitions.TrafficAdmission,
            static value => GatewayEffectiveProjectionBuilder.Hash("traffic-admission/v1", value.PolicyName));
        var timeoutSelection = effective.Resolve(route.Id, GatewayEffectiveFamilies.RequestTimeout, "RouteConfig.TimeoutPolicy/Timeout", root.RequestTimeout, declarations.RequestTimeout, definitions.RequestTimeout,
            static value => GatewayEffectiveProjectionBuilder.Hash("request-timeout/v1", value.PolicyName, value.Timeout?.Ticks.ToString()));
        var outputCache = effective.Resolve(route.Id, GatewayEffectiveFamilies.OutputCache, "RouteConfig.OutputCachePolicy", root.OutputCache, declarations.OutputCache, definitions.OutputCache,
            static value => GatewayEffectiveProjectionBuilder.Hash("output-cache/v1", value.PolicyName), effective.OutputCacheProfile);
        var inspectionSelection = effective.Resolve(route.Id, GatewayEffectiveFamilies.Inspection, "RouteConfig.Metadata/HPD inspection", root.Inspection, declarations.Inspection, definitions.Inspection,
            static value => GatewayEffectiveProjectionBuilder.Hash("inspection/v1", value.InspectorName, value.Mode.ToString(), value.MaximumAcceptedBodyBytes.ToString(), value.MaximumInspectedBytes?.ToString(), value.MemoryThresholdBytes?.ToString(), value.SpillPolicy.ToString()),
            value => effective.InspectorProfile(value));
        var credentialSelection = effective.Resolve(route.Id, GatewayEffectiveFamilies.CredentialDisposition, "RouteConfig.Transforms/request-header-remove", root.CredentialDisposition, declarations.CredentialDisposition, definitions.CredentialDisposition,
            static value => GatewayEffectiveProjectionBuilder.Hash("credential-disposition/v1", value.Kind.ToString()), value => effective.CredentialCatalog(value));
        effective.AddTransforms(route.Id, declarations.RequestTransforms, declarations.ResponseTransforms);
        var timeout = timeoutSelection.Value;
        var inspection = inspectionSelection.Value;
        var credentialDisposition = credentialSelection.Value;
        var metadata = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
        metadata.Add("hpd.gateway.route-id", route.Id.Value);
        metadata.Add(ApplicationIdMetadata, applicationId);
        metadata.Add(SymbolicPlanIdentityMetadata, symbolicPlanIdentity.Value);
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
            AuthorizationPolicy = authorization.Value?.PolicyName,
            CorsPolicy = cors.Value?.PolicyName,
            RateLimiterPolicy = admission.Value?.PolicyName,
            OutputCachePolicy = outputCache.Value?.PolicyName,
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
        if (credentialDisposition?.Kind == CredentialDispositionKind.Strip)
        {
            if (protectedCredentialHeaders.IsDefaultOrEmpty)
                throw new InvalidOperationException("Accepted protected credential header catalog is unavailable.");
            foreach (var header in protectedCredentialHeaders)
                native = native.WithTransformRequestHeaderRemove(header);
        }
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

    private static GatewayRuntimeDependencyBinding CreateDependency(
        UpstreamDeclaration upstream,
        ImmutableDictionary<DiscoveryProfileId, DiscoveryProfileCapability> profiles)
    {
        var source = (ServiceDiscoveryEndpointSource)upstream.Endpoints;
        if (!profiles.TryGetValue(source.Profile, out DiscoveryProfileCapability? capability))
            throw new InvalidOperationException("The accepted discovery capability is unavailable during planning.");
        return new GatewayRuntimeDependencyBinding(
            upstream.Id.Value,
            source.Profile,
            source.Service,
            source.Endpoint,
            source.Schemes,
            upstream.Transport.Tls?.ServerName,
            source.StaleBehavior,
            capability.BehaviorIdentity,
            capability.MaximumEndpoints);
    }

    private static ImmutableDictionary<string, string> WithPlanIdentity(
        IReadOnlyDictionary<string, string>? metadata,
        string applicationId,
        string symbolicPlanIdentity)
    {
        var builder = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
        if (metadata is not null)
            foreach (var pair in metadata)
                builder[pair.Key] = pair.Value;
        builder[ApplicationIdMetadata] = applicationId;
        builder[SymbolicPlanIdentityMetadata] = symbolicPlanIdentity;
        return builder.ToImmutable();
    }

    private ClusterConfig MaterializeCluster(
        UpstreamDeclaration upstream,
        string applicationId,
        ContentHash symbolicPlanIdentity,
        GatewayRuntimeDependencyBinding? dependency,
        ImmutableDictionary<string, UpstreamResilienceCapability> acceptedResilienceProfiles)
    {
        var metadata = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
        metadata.Add("hpd.gateway.upstream-id", upstream.Id.Value);
        metadata.Add(ApplicationIdMetadata, applicationId);
        metadata.Add(SymbolicPlanIdentityMetadata, symbolicPlanIdentity.Value);
        metadata.Add(HpdForwarderHttpClientFactory.UseProxyMetadata, upstream.Transport.UseProxy ? bool.TrueString : bool.FalseString);
        if (upstream.Transport.ConnectTimeout is { } connectTimeout)
            metadata.Add(HpdForwarderHttpClientFactory.ConnectTimeoutTicksMetadata, connectTimeout.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (upstream.Resilience is { } resilience)
        {
            if (_resilienceRegistry is null ||
                !acceptedResilienceProfiles.TryGetValue(resilience.ProfileName, out UpstreamResilienceCapability? acceptedProfile) ||
                !_resilienceRegistry.Capabilities.Any(runtime => Equivalent(acceptedProfile, runtime)))
                throw new InvalidOperationException("Accepted resilience profile is not installed in the runtime registry.");
            metadata.Add(HpdForwarderHttpClientFactory.ResilienceProfileMetadata, resilience.ProfileName);
            metadata.Add(HpdForwarderHttpClientFactory.ResilienceVersionMetadata, resilience.ProfileVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));
            metadata.Add(ResilienceCapabilityIdentityMetadata, GatewayEffectiveProjectionBuilder.Hash(
                "hpd.gateway/resilience-profile/v1",
                acceptedProfile.Name,
                acceptedProfile.Version.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ((int)acceptedProfile.Strategies).ToString(System.Globalization.CultureInfo.InvariantCulture),
                string.Join(',', acceptedProfile.RetryStatusCodes),
                acceptedProfile.MaximumRetryAttempts.ToString(System.Globalization.CultureInfo.InvariantCulture)).Value);
        }

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
            Destinations = upstream.Endpoints switch
            {
                StaticEndpointSource source => source.Destinations.ToImmutableSortedDictionary(
                    static destination => destination.Id.Value,
                    static destination => new DestinationConfig
                    {
                        Address = destination.Address.AbsoluteUri,
                        Health = destination.HealthAddress?.AbsoluteUri,
                        Host = destination.HostOverride
                    },
                    StringComparer.Ordinal),
                ServiceDiscoveryEndpointSource discovery => ImmutableDictionary<string, DestinationConfig>.Empty
                    .Add("__hpd_symbolic__", new DestinationConfig
                    {
                        Address = "http://127.0.0.1:1/",
                        Metadata = ImmutableDictionary<string, string>.Empty
                            .Add(SymbolicDestinationMetadata, bool.TrueString)
                            .Add(ApplicationIdMetadata, applicationId)
                            .Add(SymbolicPlanIdentityMetadata, symbolicPlanIdentity.Value)
                            .Add("hpd.gateway.upstream-id", upstream.Id.Value)
                            .Add(DiscoveryProfileMetadata, discovery.Profile.Value)
                            .Add(DiscoveryCapabilityIdentityMetadata, dependency?.CapabilityIdentity.Value
                                ?? throw new InvalidOperationException("Symbolic discovery dependency is missing."))
                            .Add(DiscoveryServiceMetadata, discovery.Service.Value)
                            .Add(DiscoveryEndpointMetadata, discovery.Endpoint?.Value ?? string.Empty)
                            .Add(DiscoverySchemesMetadata, string.Join(',', discovery.Schemes.Select(static value => (byte)value)))
                            .Add(DiscoveryStaleBehaviorMetadata, ((byte)discovery.StaleBehavior).ToString(System.Globalization.CultureInfo.InvariantCulture)),
                    }),
                _ => throw new InvalidOperationException("Unknown accepted endpoint source."),
            },
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

    private static bool Equivalent(UpstreamResilienceCapability expected, UpstreamResilienceCapability actual) =>
        StringComparer.Ordinal.Equals(expected.Name, actual.Name) &&
        expected.Version == actual.Version &&
        expected.Strategies == actual.Strategies &&
        expected.MaximumRetryAttempts == actual.MaximumRetryAttempts &&
        expected.RetryStatusCodes.SequenceEqual(actual.RetryStatusCodes);

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

    private void RejectUnrealizedSelections(
        GatewayConfiguration configuration,
        ImmutableArray<string> protectedCredentialHeaders,
        ImmutableArray<GatewayRuntimePlanningDiagnostic>.Builder diagnostics)
    {
        for (var index = 0; index < configuration.Upstreams.Length; index++)
        {
            var upstream = configuration.Upstreams[index];
            if (upstream.Endpoints is StaticEndpointSource && upstream.Transport.Tls is not null)
                Add(diagnostics, "planning.tls-resolution-required", $"upstreams[{index}].transport.tls", "TLS requires resolved ephemeral material and a compatible static client factory.");
            if (upstream.Resilience is { } resilience &&
                (_resilienceRegistry is null || !_resilienceRegistry.IsInstalled(resilience.ProfileName, resilience.ProfileVersion)))
                Add(diagnostics, "planning.resilience-profile-not-installed", $"upstreams[id={upstream.Id.Value}].resilience", "The selected Upstream resilience profile is not installed at its accepted version.");
        }

        if (configuration.RootDefaults!.Telemetry is not null && configuration.Routes.Any(static route => route.Enabled))
            Add(diagnostics, "planning.telemetry-runtime-required", "rootDefaults.telemetry", "Telemetry enrichment requires statically installed instrumentation.");

        for (var index = 0; index < configuration.Routes.Length; index++)
        {
            if (!configuration.Routes[index].Enabled) continue;
            var declarations = configuration.Routes[index].Declarations!;
            if (declarations.Telemetry is not null)
                Add(diagnostics, "planning.telemetry-runtime-required", $"routes[{index}].declarations.telemetry", "Telemetry enrichment requires statically installed instrumentation.");
            var inspection = Resolve(declarations.Inspection ?? configuration.RootDefaults.Inspection, configuration.Definitions!.Inspection);
            if (inspection is not null && (_inspectionRegistry is null || !_inspectionRegistry.TryGet(inspection.InspectorName, out _)))
                Add(diagnostics, "planning.inspector-not-installed", $"routes[id={configuration.Routes[index].Id.Value}].declarations.inspection", "The selected request inspector is not installed in the runtime registry.");
            var credentialDisposition = Resolve(declarations.CredentialDisposition ?? configuration.RootDefaults.CredentialDisposition, configuration.Definitions.CredentialDisposition);
            if (credentialDisposition is not null && protectedCredentialHeaders.IsDefaultOrEmpty)
                Add(diagnostics, "planning.credential-catalog-unavailable", $"routes[id={configuration.Routes[index].Id.Value}].declarations.credentialDisposition", "The accepted protected credential header catalog is unavailable.");
        }
    }

    private static GatewayRuntimePlanningResult Reject(string code, string path, string message) =>
        GatewayRuntimePlanningResult.Rejected([new GatewayRuntimePlanningDiagnostic(code, path, message)]);

    private static void Add(ImmutableArray<GatewayRuntimePlanningDiagnostic>.Builder diagnostics, string code, string path, string message)
    {
        if (diagnostics.Count < MaximumDiagnostics) diagnostics.Add(new GatewayRuntimePlanningDiagnostic(code, path, message));
    }
}
