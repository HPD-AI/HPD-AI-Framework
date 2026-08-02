using System.Collections.Immutable;
using HPD.Gateway.Abstractions;
using Microsoft.AspNetCore.Routing.Patterns;

namespace HPD.Gateway.Core;

public static class GatewayCandidateValidator
{
    public static GatewayValidationResult Validate(GatewayConfiguration? configuration, HostCapabilitySnapshot capabilities)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        var structural = GatewayConfigurationValidator.Validate(configuration);
        if (configuration is null) return structural;

        var errors = structural.Errors.ToBuilder();
        for (var index = 0; index < configuration.Routes.Length; index++)
        {
            var route = configuration.Routes[index];
            if (route is null) continue;
            if (route.Match?.Path is { } path)
            {
                try { _ = RoutePatternFactory.Parse(path); }
                catch (RoutePatternException) { Add(errors, GatewayValidationErrorCode.InvalidRouteMatch, $"routes[{index}].match.path", "Path is not accepted by ASP.NET route-pattern parsing."); }
            }
            if (route.Listener is { } listener && !capabilities.Listeners.Contains(listener))
            {
                Add(errors, $"routes[{index}].listener", "Listener is not registered by the host capability snapshot.");
            }
            ValidateDeclarations(route.Declarations, $"routes[{index}].declarations", configuration.Definitions, capabilities, errors);
        }

        ValidateRoot(configuration.RootDefaults, configuration.Definitions, capabilities, errors);
        ValidateDefinitionPolicies(configuration.Definitions, capabilities, errors);
        ValidateUpstreamCapabilities(configuration.Upstreams, capabilities, errors);
        return new GatewayValidationResult { Errors = errors.ToImmutable() };
    }

    private static void ValidateUpstreamCapabilities(ImmutableArray<UpstreamDeclaration> upstreams, HostCapabilitySnapshot capabilities, ImmutableArray<GatewayValidationError>.Builder errors)
    {
        if (upstreams.IsDefault) return;
        for (var index = 0; index < upstreams.Length; index++)
        {
            var upstream = upstreams[index];
            if (upstream is null) continue;
            var path = $"upstreams[{index}]";
            if (upstream.Transport?.Tls is not null)
            {
                if (upstream.Endpoints is StaticEndpointSource source && source.Destinations.Any(static d => d?.Address?.Scheme != Uri.UriSchemeHttps))
                    Add(errors, $"{path}.transport.tls", "Upstream TLS requires every static destination to use HTTPS.");
                else if (upstream.Endpoints is DiscoveredEndpointSource discovered && !capabilities.TlsCompatibleDiscoveryProviders.Contains(discovered.Provider))
                    Add(errors, $"{path}.transport.tls", "Discovery provider does not declare TLS-compatible endpoints.");
            }
            Resolve(upstream.SessionAffinity?.Policy, capabilities.SessionAffinityPolicies, $"{path}.sessionAffinity.policy", errors);
            Resolve(upstream.SessionAffinity?.FailurePolicy, capabilities.SessionAffinityFailurePolicies, $"{path}.sessionAffinity.failurePolicy", errors);
            Resolve(upstream.HealthChecks?.Passive?.Policy, capabilities.PassiveHealthPolicies, $"{path}.healthChecks.passive.policy", errors);
            Resolve(upstream.HealthChecks?.Active?.Policy, capabilities.ActiveHealthPolicies, $"{path}.healthChecks.active.policy", errors);
        }
    }

    private static void ValidateDefinitionPolicies(GatewayDefinitions? definitions, HostCapabilitySnapshot c, ImmutableArray<GatewayValidationError>.Builder errors)
    {
        if (definitions is null) return;
        Each(definitions.Authorization, "definitions.authorization", x => x.PolicyName, c.AuthorizationPolicies, errors);
        Each(definitions.Cors, "definitions.cors", x => x.PolicyName, c.CorsPolicies, errors);
        Each(definitions.TrafficAdmission, "definitions.trafficAdmission", x => x.PolicyName, c.TrafficAdmissionPolicies, errors);
        Each(definitions.RequestTimeout, "definitions.requestTimeout", x => x.PolicyName, c.RequestTimeoutPolicies, errors);
        Each(definitions.OutputCache, "definitions.outputCache", x => x.PolicyName, c.OutputCachePolicies, errors);
    }

    private static void Each<T>(ImmutableArray<DeclarationDefinition<T>> values, string path, Func<T, string?> selector, ImmutableHashSet<string> available, ImmutableArray<GatewayValidationError>.Builder errors) where T : class
    {
        if (values.IsDefault) return;
        for (var i = 0; i < values.Length; i++) if (values[i]?.Specification is { } value) Resolve(selector(value), available, $"{path}[{i}].specification.policyName", errors);
    }

    private static void ValidateDeclarations(RouteDeclarations? d, string path, GatewayDefinitions? definitions, HostCapabilitySnapshot c, ImmutableArray<GatewayValidationError>.Builder errors)
    {
        if (d is null) return;
        ResolveReference(d.Authorization, definitions?.Authorization, x => x.PolicyName, c.AuthorizationPolicies, $"{path}.authorization", errors);
        ResolveReference(d.Cors, definitions?.Cors, x => x.PolicyName, c.CorsPolicies, $"{path}.cors", errors);
        ResolveReference(d.TrafficAdmission, definitions?.TrafficAdmission, x => x.PolicyName, c.TrafficAdmissionPolicies, $"{path}.trafficAdmission", errors);
        ResolveReference(d.RequestTimeout, definitions?.RequestTimeout, x => x.PolicyName, c.RequestTimeoutPolicies, $"{path}.requestTimeout", errors);
        ResolveReference(d.OutputCache, definitions?.OutputCache, x => x.PolicyName, c.OutputCachePolicies, $"{path}.outputCache", errors);
    }

    private static void ValidateRoot(GatewayRootDeclarations? d, GatewayDefinitions? definitions, HostCapabilitySnapshot c, ImmutableArray<GatewayValidationError>.Builder errors)
    {
        if (d is null) return;
        ResolveReference(d.Authorization, definitions?.Authorization, x => x.PolicyName, c.AuthorizationPolicies, "rootDefaults.authorization", errors);
        ResolveReference(d.Cors, definitions?.Cors, x => x.PolicyName, c.CorsPolicies, "rootDefaults.cors", errors);
        ResolveReference(d.TrafficAdmission, definitions?.TrafficAdmission, x => x.PolicyName, c.TrafficAdmissionPolicies, "rootDefaults.trafficAdmission", errors);
        ResolveReference(d.RequestTimeout, definitions?.RequestTimeout, x => x.PolicyName, c.RequestTimeoutPolicies, "rootDefaults.requestTimeout", errors);
        ResolveReference(d.OutputCache, definitions?.OutputCache, x => x.PolicyName, c.OutputCachePolicies, "rootDefaults.outputCache", errors);
    }

    private static void ResolveReference<T>(DeclarationReference<T>? reference, ImmutableArray<DeclarationDefinition<T>>? definitions, Func<T, string?> selector, ImmutableHashSet<string> available, string path, ImmutableArray<GatewayValidationError>.Builder errors) where T : class
    {
        if (reference?.Inline is { } inline) Resolve(selector(inline), available, $"{path}.inline.policyName", errors);
        if (reference?.Definition is { } id && definitions is { } values && !values.IsDefault)
        {
            var specification = values.FirstOrDefault(x => x?.Id == id)?.Specification;
            if (specification is not null) Resolve(selector(specification), available, $"{path}.definition", errors);
        }
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
