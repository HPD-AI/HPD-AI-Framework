using System.Collections.Immutable;
using Microsoft.AspNetCore.Routing.Patterns;

namespace HPD.Gateway;

internal static class GatewayCandidateValidator
{
    public static GatewayValidationResult Validate(GatewayConfiguration? configuration, HostCapabilitySnapshot capabilities)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        var structural = GatewayConfigurationValidator.Validate(configuration);
        if (!structural.IsValid || configuration is null) return structural;

        var errors = ImmutableArray.CreateBuilder<GatewayValidationError>();
        for (var index = 0; index < configuration.Routes.Length; index++)
        {
            var route = configuration.Routes[index];
            if (route is null) continue;
            if (route.Match?.Path is { } path)
            {
                try { _ = RoutePatternFactory.Parse(path); }
                catch (RoutePatternException) { Add(errors, GatewayValidationErrorCode.InvalidRouteMatch, $"routes[{index}].match.path", "Path is not accepted by ASP.NET route-pattern parsing."); }
            }
            if (route.Listener is { } listener)
            {
                ValidateListener(listener, route.Match, $"routes[{index}].listener", capabilities, errors);
            }
            ValidateDeclarations(route.Declarations, $"routes[{index}].declarations", configuration.Definitions, capabilities, errors);
        }

        ValidateRoot(configuration.RootDefaults, configuration.Definitions, capabilities, errors);
        ValidateDefinitionPolicies(configuration.Definitions, capabilities, errors);
        ValidateInstalledFamilies(configuration, capabilities, errors);
        ValidateCredentialDisposition(configuration, capabilities, errors);
        ValidateOutputCache(configuration, capabilities, errors);
        ValidateUpstreamCapabilities(configuration, capabilities, errors);
        return new GatewayValidationResult { Errors = errors.ToImmutable() };
    }

    private static void ValidateUpstreamCapabilities(GatewayConfiguration configuration, HostCapabilitySnapshot capabilities, ImmutableArray<GatewayValidationError>.Builder errors)
    {
        var upstreams = configuration.Upstreams;
        if (upstreams.IsDefault) return;
        for (var index = 0; index < upstreams.Length; index++)
        {
            var upstream = upstreams[index];
            if (upstream is null) continue;
            var path = $"upstreams[{index}]";
            if (upstream.Endpoints is ServiceDiscoveryEndpointSource discovered)
            {
                if (!capabilities.DiscoveryProfiles.TryGetValue(discovered.Profile, out var profile))
                {
                    Add(errors, $"{path}.endpoints.profile", "Discovery profile is not installed.");
                }
                else
                {
                    if (discovered.Schemes.Any(scheme => !profile.Schemes.Contains(scheme)))
                        Add(errors, $"{path}.endpoints.schemes", "A selected discovery scheme is not supported by the installed profile.");
                    if (!profile.StaleBehaviors.Contains(discovered.StaleBehavior))
                        Add(errors, $"{path}.endpoints.staleBehavior", "The selected stale behavior is not supported by the installed profile.");
                    if (discovered.Endpoint is not null && !profile.SupportsNamedEndpoints)
                        Add(errors, $"{path}.endpoints.endpoint", "The installed discovery profile does not support named endpoints.");
                }

                var selectsHttps = discovered.Schemes.Contains(ServiceDiscoveryScheme.Https);
                if (selectsHttps && upstream.Transport?.Tls is null)
                    Add(errors, $"{path}.transport.tls", "HTTPS service discovery requires one explicit Upstream TLS server name.");
                else if (selectsHttps && !IsCanonicalDiscoveryTlsHost(upstream.Transport!.Tls!.ServerName))
                    Add(errors, $"{path}.transport.tls.serverName", "HTTPS service discovery requires one canonical lowercase DNS server name.");
                if (!selectsHttps && upstream.Transport?.Tls is not null)
                    Add(errors, $"{path}.transport.tls", "A TLS declaration requires HTTPS to be selected by the discovery source.");
            }
            if (upstream.Transport?.Tls is not null)
            {
                if (upstream.Endpoints is StaticEndpointSource source && source.Destinations.Any(static d => d?.Address?.Scheme != Uri.UriSchemeHttps))
                    Add(errors, $"{path}.transport.tls", "Upstream TLS requires every static destination to use HTTPS.");
                ValidateSecret(upstream.Transport.Tls.ClientCertificate, $"{path}.transport.tls.clientCertificate", capabilities, errors);
                ValidateSecret(upstream.Transport.Tls.TrustBundle, $"{path}.transport.tls.trustBundle", capabilities, errors);
            }
            Resolve(upstream.SessionAffinity?.Policy, capabilities.SessionAffinityPolicies, $"{path}.sessionAffinity.policy", errors);
            Resolve(upstream.SessionAffinity?.FailurePolicy, capabilities.SessionAffinityFailurePolicies, $"{path}.sessionAffinity.failurePolicy", errors);
            Resolve(upstream.HealthChecks?.Passive?.Policy, capabilities.PassiveHealthPolicies, $"{path}.healthChecks.passive.policy", errors);
            Resolve(upstream.HealthChecks?.Active?.Policy, capabilities.ActiveHealthPolicies, $"{path}.healthChecks.active.policy", errors);
            if (upstream.Resilience is { } resilience)
            {
                if (!capabilities.UpstreamResilienceProfiles.TryGetValue(resilience.ProfileName, out var profile))
                {
                    Add(errors, $"{path}.resilience.profileName", "Upstream resilience profile is not registered by the host capability snapshot.");
                }
                else if (profile.Version != resilience.ProfileVersion)
                {
                    Add(errors, $"{path}.resilience.profileVersion", "Upstream resilience profile version is not installed by the host capability snapshot.");
                }
                else if (profile.Strategies.HasFlag(UpstreamResilienceStrategies.SelectedResponseRetry))
                {
                    ValidateRetryRoutes(configuration.Routes, upstream, path, errors);
                    if (upstream.Request?.Version == UpstreamHttpVersion.Http3)
                        Add(errors, $"{path}.request.version", "Selected-response retry does not support HTTP/3 upstream requests.");
                }
            }
        }
    }

    private static bool IsCanonicalDiscoveryTlsHost(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 253 ||
            System.Net.IPAddress.TryParse(value, out _) ||
            value.Any(static character => character > 0x7f))
            return false;

        string[] labels = value.Split('.');
        return labels.All(static label =>
            label.Length is >= 1 and <= 63 &&
            !label.StartsWith("xn--", StringComparison.Ordinal) &&
            IsAsciiLowerAlphaNumeric(label[0]) &&
            IsAsciiLowerAlphaNumeric(label[^1]) &&
            label.All(static character => IsAsciiLowerAlphaNumeric(character) || character == '-'));
    }

    private static bool IsAsciiLowerAlphaNumeric(char value) =>
        value is >= 'a' and <= 'z' or >= '0' and <= '9';

    private static void ValidateRetryRoutes(ImmutableArray<RouteDeclaration> routes, UpstreamDeclaration upstream, string upstreamPath, ImmutableArray<GatewayValidationError>.Builder errors)
    {
        if (routes.IsDefault) return;
        for (var index = 0; index < routes.Length; index++)
        {
            var route = routes[index];
            if (route is null || !route.Enabled || route.Upstream != upstream.Id) continue;
            var methods = route.Match?.Methods ?? [];
            if (methods.IsDefaultOrEmpty || methods.Any(static method => !IsRetrySafeMethod(method)))
                Add(errors, GatewayValidationErrorCode.InvalidValue, $"routes[{index}].match.methods", "Retry-enabled Upstreams require an explicit bodyless-safe method set (GET, HEAD, OPTIONS, or TRACE). Runtime requests with content are never retried.");
        }
    }

    private static bool IsRetrySafeMethod(string method) =>
        method.Equals("GET", StringComparison.OrdinalIgnoreCase) ||
        method.Equals("HEAD", StringComparison.OrdinalIgnoreCase) ||
        method.Equals("OPTIONS", StringComparison.OrdinalIgnoreCase) ||
        method.Equals("TRACE", StringComparison.OrdinalIgnoreCase);

    private static void ValidateListener(ListenerId id, HttpRouteMatch? match, string path, HostCapabilitySnapshot capabilities, ImmutableArray<GatewayValidationError>.Builder errors)
    {
        if (!capabilities.Listeners.TryGetValue(id, out var listener))
        {
            Add(errors, path, "Listener is not registered by the host capability snapshot.");
            return;
        }
        if (listener.Role != ListenerRole.DataPlane) Add(errors, path, "Proxy routes cannot attach to a management listener.");
        if ((listener.Protocols & (ListenerProtocols.Http1 | ListenerProtocols.Http2 | ListenerProtocols.Http3)) == 0)
            Add(errors, path, "Listener has no compatible HTTP protocol.");
        if (match is null || listener.Hostnames.IsEmpty) return;
        if (match.Hosts.IsDefaultOrEmpty)
        {
            Add(errors, path, "A hostless route cannot attach to a hostname-restricted listener.");
            return;
        }
        foreach (var host in match.Hosts)
            if (!listener.Hostnames.Any(listenerHost => HostContains(listenerHost, host)))
                Add(errors, path, $"Route host '{host}' is outside the listener hostname exposure.");
    }

    private static bool HostContains(string listenerPattern, string routePattern)
    {
        static string Normalize(string value) => value.TrimEnd('.').ToLowerInvariant();
        var listener = Normalize(listenerPattern);
        var route = Normalize(routePattern);
        if (listener == "*") return true;
        if (listener == route) return true;
        if (!listener.StartsWith("*.", StringComparison.Ordinal) || route.StartsWith("*.", StringComparison.Ordinal)) return false;
        var suffix = listener[1..];
        return route.EndsWith(suffix, StringComparison.Ordinal) && route.Count(static c => c == '.') == suffix.Count(static c => c == '.');
    }

    private static void ValidateSecret(SecretReference? reference, string path, HostCapabilitySnapshot capabilities, ImmutableArray<GatewayValidationError>.Builder errors)
    {
        if (reference is not null && !capabilities.SecretProviders.Contains(reference.Provider))
            Add(errors, $"{path}.provider", "Secret provider is not installed.");
    }

    private static void ValidateInstalledFamilies(GatewayConfiguration configuration, HostCapabilitySnapshot capabilities, ImmutableArray<GatewayValidationError>.Builder errors)
    {
        var definitions = configuration.Definitions;
        Require(definitions?.Authorization.Length > 0 || Uses(configuration, static d => d.Authorization is not null, static d => d.Authorization is not null), GatewayDeclarationFamilies.Authorization, capabilities, "authorization", errors);
        Require(definitions?.Cors.Length > 0 || Uses(configuration, static d => d.Cors is not null, static d => d.Cors is not null), GatewayDeclarationFamilies.Cors, capabilities, "cors", errors);
        Require(definitions?.TrafficAdmission.Length > 0 || Uses(configuration, static d => d.TrafficAdmission is not null, static d => d.TrafficAdmission is not null), GatewayDeclarationFamilies.TrafficAdmission, capabilities, "trafficAdmission", errors);
        Require(definitions?.RequestTimeout.Length > 0 || Uses(configuration, static d => d.RequestTimeout is not null, static d => d.RequestTimeout is not null), GatewayDeclarationFamilies.RequestTimeout, capabilities, "requestTimeout", errors);
        Require(definitions?.OutputCache.Length > 0 || Uses(configuration, static d => d.OutputCache is not null, static d => d.OutputCache is not null), GatewayDeclarationFamilies.OutputCache, capabilities, "outputCache", errors);
        Require(definitions?.Telemetry.Length > 0 || Uses(configuration, static d => d.Telemetry is not null, static d => d.Telemetry is not null), GatewayDeclarationFamilies.Telemetry, capabilities, "telemetry", errors);
        Require(definitions?.Inspection.Length > 0 || Uses(configuration, static d => d.Inspection is not null, static d => d.Inspection is not null), GatewayDeclarationFamilies.Inspection, capabilities, "inspection", errors);
        Require(configuration.Routes.Any(static route => route?.Declarations?.RequestTransforms is not null), GatewayDeclarationFamilies.RequestTransforms, capabilities, "requestTransforms", errors);
        Require(configuration.Routes.Any(static route => route?.Declarations?.ResponseTransforms is not null), GatewayDeclarationFamilies.ResponseTransforms, capabilities, "responseTransforms", errors);
        Require(configuration.Upstreams.Any(static upstream => upstream?.Resilience is not null), GatewayDeclarationFamilies.UpstreamResilience, capabilities, "upstreamResilience", errors);
        Require(definitions?.CredentialDisposition.Length > 0 || Uses(configuration, static d => d.CredentialDisposition is not null, static d => d.CredentialDisposition is not null), GatewayDeclarationFamilies.CredentialDisposition, capabilities, "credentialDisposition", errors);
    }

    private static void ValidateCredentialDisposition(GatewayConfiguration configuration, HostCapabilitySnapshot capabilities, ImmutableArray<GatewayValidationError>.Builder errors)
    {
        var definitions = configuration.Definitions?.CredentialDisposition ?? [];
        for (var index = 0; index < configuration.Routes.Length; index++)
        {
            var route = configuration.Routes[index];
            if (route?.Declarations is null) continue;
            var reference = route.Declarations.CredentialDisposition ?? configuration.RootDefaults?.CredentialDisposition;
            var disposition = ResolveValue(reference, definitions);
            if (disposition?.Kind != CredentialDispositionKind.Strip) continue;

            var transforms = route.Declarations.RequestTransforms?.Headers ?? [];
            for (var transformIndex = 0; transformIndex < transforms.Length; transformIndex++)
            {
                var transform = transforms[transformIndex];
                if (transform is not null && transform.Kind is HeaderTransformKind.Set or HeaderTransformKind.Append &&
                    capabilities.ProtectedCredentialHeaders.Contains(transform.Name, StringComparer.OrdinalIgnoreCase))
                    Add(errors, GatewayValidationErrorCode.InvalidValue, $"routes[{index}].declarations.requestTransforms.headers[{transformIndex}].name", "A protected credential header cannot be set or appended after credential stripping is selected.");
            }
        }
    }

    private static void ValidateOutputCache(GatewayConfiguration configuration, HostCapabilitySnapshot capabilities, ImmutableArray<GatewayValidationError>.Builder errors)
    {
        var definitions = configuration.Definitions;
        var cacheDefinitions = definitions?.OutputCache ?? [];
        var credentialDefinitions = definitions?.CredentialDisposition ?? [];
        var inspectionDefinitions = definitions?.Inspection ?? [];
        for (var index = 0; index < configuration.Routes.Length; index++)
        {
            var route = configuration.Routes[index];
            if (route?.Declarations is null || !route.Enabled) continue;
            var cache = ResolveValue(route.Declarations.OutputCache ?? configuration.RootDefaults?.OutputCache, cacheDefinitions);
            if (cache is null) continue;
            var path = $"routes[{index}]";
            if (!capabilities.OutputCacheProfiles.TryGetValue(cache.PolicyName, out var profile) || !profile.RetainsDefaultSafetyPolicy)
                Add(errors, GatewayValidationErrorCode.InvalidValue, $"{path}.declarations.outputCache", "Output Cache profile is not installed with the conservative default safety policy.");

            var methods = route.Match?.Methods ?? [];
            if (methods.IsDefaultOrEmpty || methods.Any(static method => !method.Equals("GET", StringComparison.OrdinalIgnoreCase) && !method.Equals("HEAD", StringComparison.OrdinalIgnoreCase)))
                Add(errors, GatewayValidationErrorCode.InvalidValue, $"{path}.match.methods", "Output Cache requires an explicit GET/HEAD-only method set.");

            var credential = ResolveValue(route.Declarations.CredentialDisposition ?? configuration.RootDefaults?.CredentialDisposition, credentialDefinitions);
            if (credential?.Kind != CredentialDispositionKind.Strip)
                Add(errors, GatewayValidationErrorCode.InvalidValue, $"{path}.declarations.credentialDisposition", "Output Cache requires effective protected-credential stripping.");

            var inspection = ResolveValue(route.Declarations.Inspection ?? configuration.RootDefaults?.Inspection, inspectionDefinitions);
            if (inspection is not null)
                Add(errors, GatewayValidationErrorCode.InvalidValue, $"{path}.declarations.inspection", "Output Cache and request inspection cannot be selected on the same Route because cache hits bypass the inspector.");
        }
    }

    private static T? ResolveValue<T>(DeclarationReference<T>? reference, ImmutableArray<DeclarationDefinition<T>> definitions) where T : class
    {
        if (reference?.Inline is { } inline) return inline;
        if (reference?.Definition is { } id && !definitions.IsDefault)
            return definitions.FirstOrDefault(definition => definition?.Id == id)?.Specification;
        return null;
    }

    private static bool Uses(
        GatewayConfiguration configuration,
        Func<GatewayRootDeclarations, bool> root,
        Func<RouteDeclarations, bool> route) =>
        configuration.RootDefaults is { } defaults && root(defaults) ||
        configuration.Routes.Any(candidate => candidate?.Declarations is { } declarations && route(declarations));

    private static void Require(bool used, GatewayDeclarationFamilies family, HostCapabilitySnapshot capabilities, string path, ImmutableArray<GatewayValidationError>.Builder errors)
    {
        if (used && !capabilities.InstalledFamilies.HasFlag(family)) Add(errors, path, "Declaration family is not installed in the host capability snapshot.");
    }

    private static void ValidateDefinitionPolicies(GatewayDefinitions? definitions, HostCapabilitySnapshot c, ImmutableArray<GatewayValidationError>.Builder errors)
    {
        if (definitions is null) return;
        Each(definitions.Authorization, "definitions.authorization", x => x.PolicyName, c.AuthorizationPolicies, errors);
        Each(definitions.Cors, "definitions.cors", x => x.PolicyName, c.CorsPolicies, errors);
        Each(definitions.TrafficAdmission, "definitions.trafficAdmission", x => x.PolicyName, c.TrafficAdmissionPolicies, errors);
        Each(definitions.RequestTimeout, "definitions.requestTimeout", x => x.PolicyName, c.RequestTimeoutPolicies, errors);
        Each(definitions.OutputCache, "definitions.outputCache", x => x.PolicyName, c.OutputCachePolicies, errors);
        Each(definitions.Inspection, "definitions.inspection", x => x.InspectorName, c.RequestInspectors, errors, "inspectorName");
    }

    private static void Each<T>(ImmutableArray<DeclarationDefinition<T>> values, string path, Func<T, string?> selector, ImmutableHashSet<string> available, ImmutableArray<GatewayValidationError>.Builder errors, string member = "policyName") where T : class
    {
        if (values.IsDefault) return;
        for (var i = 0; i < values.Length; i++) if (values[i]?.Specification is { } value) Resolve(selector(value), available, $"{path}[{i}].specification.{member}", errors);
    }

    private static void ValidateDeclarations(RouteDeclarations? d, string path, GatewayDefinitions? definitions, HostCapabilitySnapshot c, ImmutableArray<GatewayValidationError>.Builder errors)
    {
        if (d is null) return;
        ResolveReference(d.Authorization, definitions?.Authorization, x => x.PolicyName, c.AuthorizationPolicies, $"{path}.authorization", errors);
        ResolveReference(d.Cors, definitions?.Cors, x => x.PolicyName, c.CorsPolicies, $"{path}.cors", errors);
        ResolveReference(d.TrafficAdmission, definitions?.TrafficAdmission, x => x.PolicyName, c.TrafficAdmissionPolicies, $"{path}.trafficAdmission", errors);
        ResolveReference(d.RequestTimeout, definitions?.RequestTimeout, x => x.PolicyName, c.RequestTimeoutPolicies, $"{path}.requestTimeout", errors);
        ResolveReference(d.OutputCache, definitions?.OutputCache, x => x.PolicyName, c.OutputCachePolicies, $"{path}.outputCache", errors);
        ResolveReference(d.Inspection, definitions?.Inspection, x => x.InspectorName, c.RequestInspectors, $"{path}.inspection", errors, "inspectorName");
        ValidateInspectionSpill(d.Inspection, definitions?.Inspection, c, $"{path}.inspection", errors);
    }

    private static void ValidateRoot(GatewayRootDeclarations? d, GatewayDefinitions? definitions, HostCapabilitySnapshot c, ImmutableArray<GatewayValidationError>.Builder errors)
    {
        if (d is null) return;
        ResolveReference(d.Authorization, definitions?.Authorization, x => x.PolicyName, c.AuthorizationPolicies, "rootDefaults.authorization", errors);
        ResolveReference(d.Cors, definitions?.Cors, x => x.PolicyName, c.CorsPolicies, "rootDefaults.cors", errors);
        ResolveReference(d.TrafficAdmission, definitions?.TrafficAdmission, x => x.PolicyName, c.TrafficAdmissionPolicies, "rootDefaults.trafficAdmission", errors);
        ResolveReference(d.RequestTimeout, definitions?.RequestTimeout, x => x.PolicyName, c.RequestTimeoutPolicies, "rootDefaults.requestTimeout", errors);
        ResolveReference(d.OutputCache, definitions?.OutputCache, x => x.PolicyName, c.OutputCachePolicies, "rootDefaults.outputCache", errors);
        ResolveReference(d.Inspection, definitions?.Inspection, x => x.InspectorName, c.RequestInspectors, "rootDefaults.inspection", errors, "inspectorName");
        ValidateInspectionSpill(d.Inspection, definitions?.Inspection, c, "rootDefaults.inspection", errors);
    }

    private static void ResolveReference<T>(DeclarationReference<T>? reference, ImmutableArray<DeclarationDefinition<T>>? definitions, Func<T, string?> selector, ImmutableHashSet<string> available, string path, ImmutableArray<GatewayValidationError>.Builder errors, string member = "policyName") where T : class
    {
        if (reference?.Inline is { } inline) Resolve(selector(inline), available, $"{path}.inline.{member}", errors);
        if (reference?.Definition is { } id && definitions is { } values && !values.IsDefault)
        {
            var specification = values.FirstOrDefault(x => x?.Id == id)?.Specification;
            if (specification is not null) Resolve(selector(specification), available, $"{path}.definition", errors);
        }
    }

    private static void ValidateInspectionSpill(DeclarationReference<RequestInspectionBinding>? reference, ImmutableArray<DeclarationDefinition<RequestInspectionBinding>>? definitions, HostCapabilitySnapshot capabilities, string path, ImmutableArray<GatewayValidationError>.Builder errors)
    {
        var value = reference?.Inline;
        if (value is null && reference?.Definition is { } id && definitions is { } values && !values.IsDefault)
            value = values.FirstOrDefault(x => x?.Id == id)?.Specification;
        if (value?.SpillPolicy == RequestInspectionSpillPolicy.Allowed && !capabilities.AllowInspectionFileSpill)
            Add(errors, $"{path}.spillPolicy", "Inspection file spill is not permitted by the host capability snapshot.");
    }

    private static void Resolve(string? name, ImmutableHashSet<string> available, string path, ImmutableArray<GatewayValidationError>.Builder errors)
    {
        if (name is not null && !available.Contains(name)) Add(errors, path, "Named policy is not registered by the host capability snapshot.");
    }

    private static void Add(ImmutableArray<GatewayValidationError>.Builder errors, string path, string message) =>
        Add(errors, GatewayValidationErrorCode.UnresolvedReference, path, message);

    private static void Add(ImmutableArray<GatewayValidationError>.Builder errors, GatewayValidationErrorCode code, string path, string message)
    {
        if (errors.Count < 256) errors.Add(new GatewayValidationError(code, path, message));
    }
}
