using System.Collections.Immutable;
using System.Text.Json.Serialization;
using HPD.Gateway.Abstractions.Serialization;

namespace HPD.Gateway.Abstractions;

[JsonConverter(typeof(StrictStringEnumJsonConverter<GatewayValidationErrorCode>))]
public enum GatewayValidationErrorCode
{
    InvalidIdentifier = 0,
    DuplicateIdentity = 1,
    UnresolvedReference = 2,
    InvalidRouteMatch = 3,
    InvalidEndpointSource = 4,
    InvalidDeclarationReference = 5,
    InvalidValue = 6,
    UnsupportedVersion = 7,
    MissingRequiredValue = 8,
    InvalidEnumValue = 9,
    BoundExceeded = 10,
    AmbiguousRoute = 11
}

public sealed record GatewayValidationError(
    GatewayValidationErrorCode Code,
    string Path,
    string Message);

public sealed record GatewayValidationResult
{
    public required ImmutableArray<GatewayValidationError> Errors { get; init; }

    public bool IsValid => Errors.IsEmpty;
}

public static class GatewayConfigurationValidator
{
    public static readonly GatewaySchemaVersion SupportedSchemaVersion = new(1, 0);
    public const ushort SupportedCanonicalizationVersion = 1;
    public const int MaximumRoutes = 10_000;
    public const int MaximumUpstreams = 10_000;
    public const int MaximumDestinationsPerUpstream = 10_000;
    public const int MaximumMatchItems = 64;
    public const int MaximumMetadataEntries = 64;
    public const int MaximumParameters = 64;
    public const int MaximumTransforms = 64;
    public const int MaximumTextLength = 2_048;
    public const int MaximumMetadataValueLength = 4_096;
    public static readonly TimeSpan MaximumOperationalDuration = TimeSpan.FromDays(1);

    private static readonly HashSet<string> ForbiddenTransformHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Connection", "Content-Length", "Host", "Keep-Alive", "Proxy-Authenticate",
        "Proxy-Authorization", "Proxy-Connection", "TE", "Trailer", "Transfer-Encoding", "Upgrade"
    };

    private static readonly HashSet<string> ForbiddenTrailerHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Authorization", "Cache-Control", "Content-Encoding", "Content-Range", "Content-Type",
        "Cookie", "Expires", "Location", "Range", "Retry-After", "Set-Cookie", "Vary", "WWW-Authenticate"
    };

    public static GatewayValidationResult Validate(GatewayConfiguration? configuration)
    {
        var errors = ImmutableArray.CreateBuilder<GatewayValidationError>();
        if (configuration is null)
        {
            Add(errors, GatewayValidationErrorCode.MissingRequiredValue, "$", "Gateway configuration is required.");
            return Result(errors);
        }

        if (configuration.SchemaVersion != SupportedSchemaVersion)
        {
            Add(errors, GatewayValidationErrorCode.UnsupportedVersion, "schemaVersion", "Gateway schema version is unsupported.");
        }

        if (configuration.CanonicalizationVersion != SupportedCanonicalizationVersion)
        {
            Add(errors, GatewayValidationErrorCode.UnsupportedVersion, "canonicalizationVersion", "Canonicalization version is unsupported.");
        }

        ValidateMetadata(configuration.Metadata, "metadata", errors);

        if (configuration.Definitions is null)
        {
            Add(errors, GatewayValidationErrorCode.MissingRequiredValue, "definitions", "Definitions container is required.");
        }

        var definitions = configuration.Definitions ?? new GatewayDefinitions();
        var authorizationDefinitions = ValidateDefinitions(definitions.Authorization, "definitions.authorization", errors);
        var corsDefinitions = ValidateDefinitions(definitions.Cors, "definitions.cors", errors);
        var trafficAdmissionDefinitions = ValidateDefinitions(definitions.TrafficAdmission, "definitions.trafficAdmission", errors);
        var requestTimeoutDefinitions = ValidateDefinitions(definitions.RequestTimeout, "definitions.requestTimeout", errors);
        var outputCacheDefinitions = ValidateDefinitions(definitions.OutputCache, "definitions.outputCache", errors);
        var telemetryDefinitions = ValidateDefinitions(definitions.Telemetry, "definitions.telemetry", errors);
        var inspectionDefinitions = ValidateDefinitions(definitions.Inspection, "definitions.inspection", errors);

        ValidateSpecifications(definitions, errors);

        if (configuration.Routes.IsDefault)
        {
            Add(errors, GatewayValidationErrorCode.MissingRequiredValue, "routes", "Routes must be an initialized collection.");
        }
        else if (configuration.Routes.Length > MaximumRoutes)
        {
            Add(errors, GatewayValidationErrorCode.BoundExceeded, "routes", "Route count exceeds the configured bound.");
        }

        if (configuration.Upstreams.IsDefault)
        {
            Add(errors, GatewayValidationErrorCode.MissingRequiredValue, "upstreams", "Upstreams must be an initialized collection.");
        }
        else if (configuration.Upstreams.Length > MaximumUpstreams)
        {
            Add(errors, GatewayValidationErrorCode.BoundExceeded, "upstreams", "Upstream count exceeds the configured bound.");
        }

        var upstreams = ValidateUpstreams(configuration.Upstreams, errors);
        ValidateRoutes(
            configuration.Routes,
            upstreams,
            authorizationDefinitions,
            corsDefinitions,
            trafficAdmissionDefinitions,
            requestTimeoutDefinitions,
            outputCacheDefinitions,
            telemetryDefinitions,
            inspectionDefinitions,
            errors);

        if (configuration.RootDefaults is null)
        {
            Add(errors, GatewayValidationErrorCode.MissingRequiredValue, "rootDefaults", "Root defaults container is required.");
        }
        else
        {
            ValidateRootDeclarations(
                configuration.RootDefaults,
                "rootDefaults",
                authorizationDefinitions,
                corsDefinitions,
                trafficAdmissionDefinitions,
                requestTimeoutDefinitions,
                outputCacheDefinitions,
                telemetryDefinitions,
                inspectionDefinitions,
                errors);
        }

        return Result(errors);
    }

    private static HashSet<string> ValidateUpstreams(
        ImmutableArray<UpstreamDeclaration> declarations,
        ImmutableArray<GatewayValidationError>.Builder errors)
    {
        var upstreams = new HashSet<string>(StringComparer.Ordinal);
        if (declarations.IsDefault)
        {
            return upstreams;
        }

        for (var index = 0; index < declarations.Length; index++)
        {
            var upstream = declarations[index];
            var path = $"upstreams[{index}]";
            if (upstream is null)
            {
                Add(errors, GatewayValidationErrorCode.MissingRequiredValue, path, "Upstream is required.");
                continue;
            }

            ValidateId(upstream.Id.Value, $"{path}.id", errors);
            if (!upstreams.Add(upstream.Id.Value))
            {
                Add(errors, GatewayValidationErrorCode.DuplicateIdentity, $"{path}.id", "Duplicate Upstream identity.");
            }

            if (upstream.LoadBalancing is null)
            {
                Add(errors, GatewayValidationErrorCode.MissingRequiredValue, $"{path}.loadBalancing", "Load-balancing declaration is required.");
            }
            else
            {
                ValidateEnum(upstream.LoadBalancing.Kind, $"{path}.loadBalancing.kind", errors);
            }
            ValidateAffinity(upstream.SessionAffinity, $"{path}.sessionAffinity", errors);
            ValidateHealth(upstream.HealthChecks, $"{path}.healthChecks", errors);
            ValidateTransport(upstream.Transport, $"{path}.transport", errors);
            ValidateRequest(upstream.Request, $"{path}.request", errors);
            ValidateMetadata(upstream.Metadata, $"{path}.metadata", errors);
            ValidateEndpointSource(upstream.Endpoints, path, errors);
        }

        return upstreams;
    }

    private static void ValidateEndpointSource(
        UpstreamEndpointSource? source,
        string upstreamPath,
        ImmutableArray<GatewayValidationError>.Builder errors)
    {
        var path = $"{upstreamPath}.endpoints";
        switch (source)
        {
            case StaticEndpointSource staticEndpoints:
                if (staticEndpoints.Destinations.IsDefaultOrEmpty || staticEndpoints.Destinations.Length > MaximumDestinationsPerUpstream)
                {
                    Add(errors, GatewayValidationErrorCode.InvalidEndpointSource, path, "Static endpoints require a bounded, non-empty destination list.");
                }

                var destinations = new HashSet<string>(StringComparer.Ordinal);
                for (var index = 0; !staticEndpoints.Destinations.IsDefault && index < staticEndpoints.Destinations.Length; index++)
                {
                    var destination = staticEndpoints.Destinations[index];
                    var destinationPath = $"{path}.destinations[{index}]";
                    if (destination is null)
                    {
                        Add(errors, GatewayValidationErrorCode.MissingRequiredValue, destinationPath, "Destination is required.");
                        continue;
                    }

                    ValidateId(destination.Id.Value, $"{destinationPath}.id", errors);
                    if (!destinations.Add(destination.Id.Value))
                    {
                        Add(errors, GatewayValidationErrorCode.DuplicateIdentity, $"{destinationPath}.id", "Duplicate Destination identity within its Upstream.");
                    }

                    ValidateHttpUri(destination.Address, $"{destinationPath}.address", true, errors);
                    ValidateHttpUri(destination.HealthAddress, $"{destinationPath}.healthAddress", false, errors);
                    ValidateOptionalText(destination.HostOverride, $"{destinationPath}.hostOverride", errors);
                    if (destination.HostOverride is not null && !IsSupportedHostPattern(destination.HostOverride))
                    {
                        Add(errors, GatewayValidationErrorCode.InvalidValue, $"{destinationPath}.hostOverride", "Host override is not a legal Host header value.");
                    }
                    ValidateMetadata(destination.Metadata, $"{destinationPath}.metadata", errors);
                }
                break;

            case DiscoveredEndpointSource discovered:
                ValidateId(discovered.Provider.Value, $"{path}.provider", errors);
                ValidateId(discovered.Service.Value, $"{path}.service", errors);
                ValidateEnum(discovered.StaleBehavior, $"{path}.staleBehavior", errors);
                if (discovered.Parameters.IsDefault || discovered.Parameters.Length > MaximumParameters)
                {
                    Add(errors, GatewayValidationErrorCode.BoundExceeded, $"{path}.parameters", "Discovery parameters exceed their bound or are uninitialized.");
                    break;
                }

                var parameterNames = new HashSet<string>(StringComparer.Ordinal);
                for (var index = 0; index < discovered.Parameters.Length; index++)
                {
                    var parameter = discovered.Parameters[index];
                    if (parameter is null)
                    {
                        Add(errors, GatewayValidationErrorCode.MissingRequiredValue, $"{path}.parameters[{index}]", "Discovery parameter is required.");
                        continue;
                    }

                    ValidateRequiredText(parameter.Name, $"{path}.parameters[{index}].name", errors);
                    ValidateRequiredText(parameter.Value, $"{path}.parameters[{index}].value", errors);
                    if (!parameterNames.Add(parameter.Name))
                    {
                        Add(errors, GatewayValidationErrorCode.DuplicateIdentity, $"{path}.parameters[{index}].name", "Discovery parameter names must be unique.");
                    }
                }
                break;

            case null:
                Add(errors, GatewayValidationErrorCode.MissingRequiredValue, path, "Endpoint source is required.");
                break;

            default:
                Add(errors, GatewayValidationErrorCode.InvalidEndpointSource, path, "Unknown endpoint-source kind.");
                break;
        }
    }

    private static void ValidateRoutes(
        ImmutableArray<RouteDeclaration> declarations,
        HashSet<string> upstreams,
        HashSet<string> authorizationDefinitions,
        HashSet<string> corsDefinitions,
        HashSet<string> trafficAdmissionDefinitions,
        HashSet<string> requestTimeoutDefinitions,
        HashSet<string> outputCacheDefinitions,
        HashSet<string> telemetryDefinitions,
        HashSet<string> inspectionDefinitions,
        ImmutableArray<GatewayValidationError>.Builder errors)
    {
        if (declarations.IsDefault)
        {
            return;
        }

        var routes = new HashSet<string>(StringComparer.Ordinal);
        var routeShapes = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < declarations.Length; index++)
        {
            var route = declarations[index];
            var path = $"routes[{index}]";
            if (route is null)
            {
                Add(errors, GatewayValidationErrorCode.MissingRequiredValue, path, "Route is required.");
                continue;
            }

            ValidateId(route.Id.Value, $"{path}.id", errors);
            if (!routes.Add(route.Id.Value))
            {
                Add(errors, GatewayValidationErrorCode.DuplicateIdentity, $"{path}.id", "Duplicate Route identity.");
            }

            if (route.Listener is { } listener)
            {
                ValidateId(listener.Value, $"{path}.listener", errors);
            }

            ValidateId(route.Upstream.Value, $"{path}.upstream", errors);
            if (!upstreams.Contains(route.Upstream.Value))
            {
                Add(errors, GatewayValidationErrorCode.UnresolvedReference, $"{path}.upstream", "Route references an unknown Upstream.");
            }

            ValidateRouteMatch(route.Match, $"{path}.match", errors);
            if (route.Match is not null && !route.Match.Methods.IsDefault && !route.Match.Hosts.IsDefault &&
                !route.Match.Headers.IsDefault && !route.Match.Query.IsDefault)
            {
                var shape = CreateRouteShape(route.Order ?? 0, route.Match);
                if (!routeShapes.Add(shape))
                {
                    Add(errors, GatewayValidationErrorCode.AmbiguousRoute, $"{path}.match", "Another route has the same effective match and order.");
                }
            }
            ValidateMetadata(route.Metadata, $"{path}.metadata", errors);
            if (route.Declarations is null)
            {
                Add(errors, GatewayValidationErrorCode.MissingRequiredValue, $"{path}.declarations", "Route declarations container is required.");
                continue;
            }

            ValidateRouteDeclarations(
                route.Declarations,
                $"{path}.declarations",
                authorizationDefinitions,
                corsDefinitions,
                trafficAdmissionDefinitions,
                requestTimeoutDefinitions,
                outputCacheDefinitions,
                telemetryDefinitions,
                inspectionDefinitions,
                errors);
        }
    }

    private static void ValidateRouteMatch(HttpRouteMatch? match, string path, ImmutableArray<GatewayValidationError>.Builder errors)
    {
        if (match is null)
        {
            Add(errors, GatewayValidationErrorCode.MissingRequiredValue, path, "Route match is required.");
            return;
        }

        if (string.IsNullOrWhiteSpace(match.Path) && !match.Hosts.IsDefault && !match.Hosts.Any(static host => !string.IsNullOrWhiteSpace(host)))
        {
            Add(errors, GatewayValidationErrorCode.InvalidRouteMatch, path, "Route match requires a path or at least one host.");
        }

        ValidateMethods(match.Methods, $"{path}.methods", errors);
        ValidateHosts(match.Hosts, $"{path}.hosts", errors);
        ValidateOptionalText(match.Path, $"{path}.path", errors);
        ValidateMatches(match.Headers, $"{path}.headers", true, errors);
        ValidateMatches(match.Query, $"{path}.query", false, errors);
    }

    private static void ValidateMatches<T>(ImmutableArray<T> matches, string path, bool header, ImmutableArray<GatewayValidationError>.Builder errors)
    {
        if (matches.IsDefault || matches.Length > MaximumMatchItems)
        {
            Add(errors, GatewayValidationErrorCode.BoundExceeded, path, "Match collection exceeds its bound or is uninitialized.");
            return;
        }

        var predicates = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < matches.Length; index++)
        {
            object? item = matches[index];
            string? name;
            TextMatchKind kind;
            ImmutableArray<string> values;
            if (header && item is HttpHeaderMatch headerMatch)
            {
                name = headerMatch.Name;
                kind = headerMatch.Kind;
                values = headerMatch.Values;
            }
            else if (!header && item is HttpQueryMatch queryMatch)
            {
                name = queryMatch.Name;
                kind = queryMatch.Kind;
                values = queryMatch.Values;
            }
            else
            {
                Add(errors, GatewayValidationErrorCode.MissingRequiredValue, $"{path}[{index}]", "Match item is required.");
                continue;
            }

            ValidateRequiredText(name, $"{path}[{index}].name", errors);
            if (name is not null && (header ? !IsHttpToken(name) : ContainsProhibitedQueryNameCharacter(name)))
            {
                Add(errors, GatewayValidationErrorCode.InvalidRouteMatch, $"{path}[{index}].name", header
                    ? "Header match name is not a valid HTTP field name."
                    : "Query match name contains a control or query-delimiter character.");
            }
            ValidateEnum(kind, $"{path}[{index}].kind", errors);
            if (!header && kind == TextMatchKind.NotExists)
            {
                Add(errors, GatewayValidationErrorCode.InvalidRouteMatch, $"{path}[{index}].kind", "NotExists is not supported by the native query matcher.");
            }
            ValidateTextArray(values, $"{path}[{index}].values", MaximumMatchItems, errors);
            var requiresValues = kind is TextMatchKind.Exact or TextMatchKind.Prefix or TextMatchKind.Contains;
            if (requiresValues == values.IsDefaultOrEmpty)
            {
                Add(errors, GatewayValidationErrorCode.InvalidRouteMatch, $"{path}[{index}].values", requiresValues ? "This match kind requires values." : "Exists and NotExists must not contain values.");
            }

            var comparison = item is HttpHeaderMatch hm && hm.CaseSensitive || item is HttpQueryMatch qm && qm.CaseSensitive
                ? StringComparer.Ordinal
                : StringComparer.OrdinalIgnoreCase;
            if (!values.IsDefault && values.Distinct(comparison).Count() != values.Length)
            {
                Add(errors, GatewayValidationErrorCode.InvalidRouteMatch, $"{path}[{index}].values", "Match values must be semantically unique.");
            }
            if (name is not null)
            {
                var orderedValues = values.IsDefault ? Enumerable.Empty<string>() : values.OrderBy(static v => v, comparison);
                var predicate = $"{name.ToUpperInvariant()}\u001f{kind}\u001f{(item is HttpHeaderMatch h && h.CaseSensitive || item is HttpQueryMatch q && q.CaseSensitive)}\u001f{string.Join('\u001e', orderedValues)}";
                if (!predicates.Add(predicate))
                {
                    Add(errors, GatewayValidationErrorCode.InvalidRouteMatch, $"{path}[{index}]", "Duplicate semantic match predicate.");
                }
            }
        }
    }

    private static HashSet<string> ValidateDefinitions<T>(ImmutableArray<DeclarationDefinition<T>> definitions, string path, ImmutableArray<GatewayValidationError>.Builder errors)
        where T : class
    {
        var identities = new HashSet<string>(StringComparer.Ordinal);
        if (definitions.IsDefault)
        {
            Add(errors, GatewayValidationErrorCode.MissingRequiredValue, path, "Definition collection must be initialized.");
            return identities;
        }

        for (var index = 0; index < definitions.Length; index++)
        {
            var definition = definitions[index];
            var definitionPath = $"{path}[{index}]";
            if (definition is null)
            {
                Add(errors, GatewayValidationErrorCode.MissingRequiredValue, definitionPath, "Definition is required.");
                continue;
            }

            ValidateId(definition.Id.Value, $"{definitionPath}.id", errors);
            if (!identities.Add(definition.Id.Value))
            {
                Add(errors, GatewayValidationErrorCode.DuplicateIdentity, $"{definitionPath}.id", "Duplicate definition identity within its family.");
            }

            if (definition.Specification is null)
            {
                Add(errors, GatewayValidationErrorCode.MissingRequiredValue, $"{definitionPath}.specification", "Definition specification is required.");
            }
            ValidateMetadata(definition.Metadata, $"{definitionPath}.metadata", errors);
        }

        return identities;
    }

    private static void ValidateSpecifications(GatewayDefinitions definitions, ImmutableArray<GatewayValidationError>.Builder errors)
    {
        ValidateDefinitionSpecifications(definitions.Authorization, "definitions.authorization", static (value, path, e) => ValidatePolicyName(value.PolicyName, $"{path}.policyName", e), errors);
        ValidateDefinitionSpecifications(definitions.Cors, "definitions.cors", static (value, path, e) => ValidatePolicyName(value.PolicyName, $"{path}.policyName", e), errors);
        ValidateDefinitionSpecifications(definitions.TrafficAdmission, "definitions.trafficAdmission", static (value, path, e) => ValidatePolicyName(value.PolicyName, $"{path}.policyName", e), errors);
        ValidateDefinitionSpecifications(definitions.RequestTimeout, "definitions.requestTimeout", ValidateTimeout, errors);
        ValidateDefinitionSpecifications(definitions.OutputCache, "definitions.outputCache", static (value, path, e) => ValidatePolicyName(value.PolicyName, $"{path}.policyName", e), errors);
        ValidateDefinitionSpecifications(definitions.Telemetry, "definitions.telemetry", ValidateTelemetry, errors);
        ValidateDefinitionSpecifications(definitions.Inspection, "definitions.inspection", ValidateInspection, errors);
    }

    private static void ValidateDefinitionSpecifications<T>(ImmutableArray<DeclarationDefinition<T>> definitions, string path, Action<T, string, ImmutableArray<GatewayValidationError>.Builder> validate, ImmutableArray<GatewayValidationError>.Builder errors)
        where T : class
    {
        if (definitions.IsDefault)
        {
            return;
        }

        for (var index = 0; index < definitions.Length; index++)
        {
            if (definitions[index]?.Specification is { } specification)
            {
                validate(specification, $"{path}[{index}].specification", errors);
            }
        }
    }

    private static void ValidateRouteDeclarations(
        RouteDeclarations declarations,
        string path,
        HashSet<string> authorizationDefinitions,
        HashSet<string> corsDefinitions,
        HashSet<string> trafficAdmissionDefinitions,
        HashSet<string> requestTimeoutDefinitions,
        HashSet<string> outputCacheDefinitions,
        HashSet<string> telemetryDefinitions,
        HashSet<string> inspectionDefinitions,
        ImmutableArray<GatewayValidationError>.Builder errors)
    {
        ValidateCommonDeclarations(declarations.Authorization, declarations.Cors, declarations.TrafficAdmission, declarations.RequestTimeout, declarations.OutputCache, declarations.Telemetry, declarations.Inspection, path, authorizationDefinitions, corsDefinitions, trafficAdmissionDefinitions, requestTimeoutDefinitions, outputCacheDefinitions, telemetryDefinitions, inspectionDefinitions, errors);
        ValidateTransforms(declarations.RequestTransforms, declarations.ResponseTransforms, path, errors);
    }

    private static void ValidateRootDeclarations(
        GatewayRootDeclarations declarations,
        string path,
        HashSet<string> authorizationDefinitions,
        HashSet<string> corsDefinitions,
        HashSet<string> trafficAdmissionDefinitions,
        HashSet<string> requestTimeoutDefinitions,
        HashSet<string> outputCacheDefinitions,
        HashSet<string> telemetryDefinitions,
        HashSet<string> inspectionDefinitions,
        ImmutableArray<GatewayValidationError>.Builder errors) =>
        ValidateCommonDeclarations(declarations.Authorization, declarations.Cors, declarations.TrafficAdmission, declarations.RequestTimeout, declarations.OutputCache, declarations.Telemetry, declarations.Inspection, path, authorizationDefinitions, corsDefinitions, trafficAdmissionDefinitions, requestTimeoutDefinitions, outputCacheDefinitions, telemetryDefinitions, inspectionDefinitions, errors);

    private static void ValidateCommonDeclarations(
        DeclarationReference<NamedAuthorizationPolicy>? authorization,
        DeclarationReference<CorsPolicyBinding>? cors,
        DeclarationReference<TrafficAdmissionBinding>? trafficAdmission,
        DeclarationReference<RequestTimeoutBinding>? requestTimeout,
        DeclarationReference<OutputCacheBinding>? outputCache,
        DeclarationReference<TelemetryEnrichment>? telemetry,
        DeclarationReference<RequestInspectionBinding>? inspection,
        string path,
        HashSet<string> authorizationDefinitions,
        HashSet<string> corsDefinitions,
        HashSet<string> trafficAdmissionDefinitions,
        HashSet<string> requestTimeoutDefinitions,
        HashSet<string> outputCacheDefinitions,
        HashSet<string> telemetryDefinitions,
        HashSet<string> inspectionDefinitions,
        ImmutableArray<GatewayValidationError>.Builder errors)
    {
        ValidateReference(authorization, $"{path}.authorization", authorizationDefinitions, static (value, p, e) => ValidatePolicyName(value.PolicyName, $"{p}.policyName", e), errors);
        ValidateReference(cors, $"{path}.cors", corsDefinitions, static (value, p, e) => ValidatePolicyName(value.PolicyName, $"{p}.policyName", e), errors);
        ValidateReference(trafficAdmission, $"{path}.trafficAdmission", trafficAdmissionDefinitions, static (value, p, e) => ValidatePolicyName(value.PolicyName, $"{p}.policyName", e), errors);
        ValidateReference(requestTimeout, $"{path}.requestTimeout", requestTimeoutDefinitions, ValidateTimeout, errors);
        ValidateReference(outputCache, $"{path}.outputCache", outputCacheDefinitions, static (value, p, e) => ValidatePolicyName(value.PolicyName, $"{p}.policyName", e), errors);
        ValidateReference(telemetry, $"{path}.telemetry", telemetryDefinitions, ValidateTelemetry, errors);
        ValidateReference(inspection, $"{path}.inspection", inspectionDefinitions, ValidateInspection, errors);
    }

    private static void ValidateReference<T>(DeclarationReference<T>? reference, string path, HashSet<string> definitions, Action<T, string, ImmutableArray<GatewayValidationError>.Builder> validateInline, ImmutableArray<GatewayValidationError>.Builder errors)
        where T : class
    {
        if (reference is null)
        {
            return;
        }

        if ((reference.Inline is null) == (reference.Definition is null))
        {
            Add(errors, GatewayValidationErrorCode.InvalidDeclarationReference, path, "Exactly one of inline or definition must be supplied.");
        }

        if (reference.Inline is { } inline)
        {
            validateInline(inline, $"{path}.inline", errors);
        }

        if (reference.Definition is { } definition)
        {
            ValidateId(definition.Value, $"{path}.definition", errors);
            if (!definitions.Contains(definition.Value))
            {
                Add(errors, GatewayValidationErrorCode.UnresolvedReference, $"{path}.definition", "Declaration references an unknown definition in its family.");
            }
        }
    }

    private static void ValidateTimeout(RequestTimeoutBinding value, string path, ImmutableArray<GatewayValidationError>.Builder errors)
    {
        var count = (value.PolicyName is null ? 0 : 1) + (value.Timeout is null ? 0 : 1);
        if (count != 1)
        {
            Add(errors, GatewayValidationErrorCode.InvalidValue, path, "Request timeout requires exactly one named policy or positive duration.");
        }
        ValidateOptionalText(value.PolicyName, $"{path}.policyName", errors);
        ValidatePositive(value.Timeout, $"{path}.timeout", errors);
    }

    private static void ValidateInspection(RequestInspectionBinding value, string path, ImmutableArray<GatewayValidationError>.Builder errors)
    {
        ValidatePolicyName(value.InspectorName, $"{path}.inspectorName", errors);
        if (!Enum.IsDefined(value.Mode) || !Enum.IsDefined(value.SpillPolicy) || value.MaximumAcceptedBodyBytes <= 0)
        {
            Add(errors, GatewayValidationErrorCode.InvalidValue, path, "Inspection mode, spill policy, and accepted-body bound must be valid.");
            return;
        }
        if (value.Mode == RequestInspectionMode.BoundedPrefix)
        {
            if (value.MaximumInspectedBytes is not > 0 || value.MaximumInspectedBytes > value.MaximumAcceptedBodyBytes ||
                value.MemoryThresholdBytes is not null || value.SpillPolicy != RequestInspectionSpillPolicy.Disabled)
                Add(errors, GatewayValidationErrorCode.InvalidValue, path, "Prefix inspection requires a positive inspected-byte bound not exceeding the accepted-body bound and cannot select complete-body memory or spill settings.");
        }
        else if (value.MaximumInspectedBytes is not null || value.MemoryThresholdBytes is not > 0 ||
                 value.MemoryThresholdBytes > value.MaximumAcceptedBodyBytes)
        {
            Add(errors, GatewayValidationErrorCode.InvalidValue, path, "Complete inspection requires a positive memory threshold not exceeding the accepted-body bound and cannot select a prefix bound.");
        }
    }

    private static void ValidateTelemetry(TelemetryEnrichment value, string path, ImmutableArray<GatewayValidationError>.Builder errors) =>
        ValidateMetadataEntries(value.Attributes, $"{path}.attributes", MaximumMetadataEntries, errors);

    private static void ValidateTransforms(OrderedRequestTransforms? request, OrderedResponseTransforms? response, string path, ImmutableArray<GatewayValidationError>.Builder errors)
    {
        if (request is not null)
        {
            ValidateHeaderTransforms(request.Headers, $"{path}.requestTransforms.headers", false, errors);
        }
        if (response is not null)
        {
            ValidateHeaderTransforms(response.Headers, $"{path}.responseTransforms.headers", false, errors);
            ValidateHeaderTransforms(response.Trailers, $"{path}.responseTransforms.trailers", true, errors);
        }
    }

    private static void ValidateHeaderTransforms<T>(ImmutableArray<T> transforms, string path, bool trailer, ImmutableArray<GatewayValidationError>.Builder errors)
    {
        if (transforms.IsDefault || transforms.Length > MaximumTransforms)
        {
            Add(errors, GatewayValidationErrorCode.BoundExceeded, path, "Transform collection exceeds its bound or is uninitialized.");
            return;
        }

        for (var index = 0; index < transforms.Length; index++)
        {
            object? item = transforms[index];
            HeaderTransformKind kind;
            string? name;
            string? value;
            if (item is RequestHeaderTransform request)
            {
                kind = request.Kind; name = request.Name; value = request.Value;
            }
            else if (item is ResponseHeaderTransform response)
            {
                kind = response.Kind; name = response.Name; value = response.Value;
            }
            else
            {
                Add(errors, GatewayValidationErrorCode.MissingRequiredValue, $"{path}[{index}]", "Transform is required.");
                continue;
            }

            ValidateEnum(kind, $"{path}[{index}].kind", errors);
            ValidateRequiredText(name, $"{path}[{index}].name", errors);
            if (name is not null && !IsHttpToken(name))
            {
                Add(errors, GatewayValidationErrorCode.InvalidValue, $"{path}[{index}].name", "Transform name is not a valid HTTP field name.");
            }
            var forbidden = name is not null && ForbiddenTransformHeaders.Contains(name);
            if (forbidden || (trailer && name is not null && ForbiddenTrailerHeaders.Contains(name)))
            {
                Add(errors, GatewayValidationErrorCode.InvalidValue, $"{path}[{index}].name", "Hop-by-hop, framing, Host, and trailer-control fields cannot be transformed.");
            }

            if (kind == HeaderTransformKind.Remove)
            {
                if (value is not null)
                {
                    Add(errors, GatewayValidationErrorCode.InvalidValue, $"{path}[{index}].value", "Remove transforms must not contain a value.");
                }
            }
            else
            {
                ValidateRequiredText(value, $"{path}[{index}].value", errors);
                if (value is not null && ContainsInvalidFieldValueCharacter(value))
                {
                    Add(errors, GatewayValidationErrorCode.InvalidValue, $"{path}[{index}].value", "Transform value contains CR, LF, NUL, or a prohibited control character.");
                }
            }
        }
    }

    private static void ValidateAffinity(SessionAffinityDeclaration? value, string path, ImmutableArray<GatewayValidationError>.Builder errors)
    {
        if (value is null) return;
        ValidatePolicyName(value.Policy, $"{path}.policy", errors);
        ValidatePolicyName(value.FailurePolicy, $"{path}.failurePolicy", errors);
        ValidateOptionalText(value.CookieName, $"{path}.cookieName", errors);
    }

    private static void ValidateHealth(HealthCheckDeclaration? value, string path, ImmutableArray<GatewayValidationError>.Builder errors)
    {
        if (value?.Passive is { } passive)
        {
            ValidatePolicyName(passive.Policy, $"{path}.passive.policy", errors);
            ValidateDuration(passive.ReactivationPeriod, $"{path}.passive.reactivationPeriod", errors);
        }
        if (value?.Active is { } active)
        {
            ValidatePolicyName(active.Policy, $"{path}.active.policy", errors);
            ValidateDuration(active.Interval, $"{path}.active.interval", errors);
            ValidateDuration(active.Timeout, $"{path}.active.timeout", errors);
            ValidateOptionalText(active.Path, $"{path}.active.path", errors);
            if (active.Path is not null && !active.Path.StartsWith("/", StringComparison.Ordinal))
            {
                Add(errors, GatewayValidationErrorCode.InvalidValue, $"{path}.active.path", "Active health path must begin with '/'.");
            }
        }
    }

    private static void ValidateTransport(UpstreamTransportDeclaration? value, string path, ImmutableArray<GatewayValidationError>.Builder errors)
    {
        if (value is null)
        {
            Add(errors, GatewayValidationErrorCode.MissingRequiredValue, path, "Upstream transport is required.");
            return;
        }
        if (value.MaxConnectionsPerServer is <= 0)
        {
            Add(errors, GatewayValidationErrorCode.InvalidValue, $"{path}.maxConnectionsPerServer", "Maximum connections must be positive.");
        }
        ValidateDuration(value.ConnectTimeout, $"{path}.connectTimeout", errors);
        if (value.Tls is { } tls)
        {
            ValidateRequiredText(tls.ServerName, $"{path}.tls.serverName", errors);
            ValidateSecret(tls.ClientCertificate, $"{path}.tls.clientCertificate", errors);
            ValidateSecret(tls.TrustBundle, $"{path}.tls.trustBundle", errors);
        }
    }

    private static void ValidateRequest(UpstreamRequestDeclaration? value, string path, ImmutableArray<GatewayValidationError>.Builder errors)
    {
        if (value is null)
        {
            Add(errors, GatewayValidationErrorCode.MissingRequiredValue, path, "Upstream request configuration is required.");
            return;
        }
        ValidateDuration(value.ActivityTimeout, $"{path}.activityTimeout", errors);
        ValidateEnum(value.Version, $"{path}.version", errors);
        ValidateEnum(value.VersionSelection, $"{path}.versionSelection", errors);
    }

    private static void ValidateSecret(SecretReference? value, string path, ImmutableArray<GatewayValidationError>.Builder errors)
    {
        if (value is null) return;
        ValidateId(value.Provider.Value, $"{path}.provider", errors);
        ValidateId(value.Name.Value, $"{path}.name", errors);
        ValidateOptionalText(value.Version, $"{path}.version", errors);
    }

    private static void ValidateMetadata(ResourceMetadata? metadata, string path, ImmutableArray<GatewayValidationError>.Builder errors)
    {
        if (metadata is null)
        {
            Add(errors, GatewayValidationErrorCode.MissingRequiredValue, path, "Metadata container is required.");
            return;
        }
        ValidateOptionalText(metadata.DisplayName, $"{path}.displayName", errors);
        ValidateOptionalText(metadata.Description, $"{path}.description", errors, MaximumMetadataValueLength);
        ValidateMetadataEntries(metadata.Labels, $"{path}.labels", MaximumMetadataEntries, errors);
        ValidateMetadataEntries(metadata.Annotations, $"{path}.annotations", MaximumMetadataEntries, errors);
    }

    private static void ValidateMetadataEntries(ImmutableArray<MetadataEntry> entries, string path, int maximum, ImmutableArray<GatewayValidationError>.Builder errors)
    {
        if (entries.IsDefault || entries.Length > maximum)
        {
            Add(errors, GatewayValidationErrorCode.BoundExceeded, path, "Metadata collection exceeds its bound or is uninitialized.");
            return;
        }
        var names = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < entries.Length; index++)
        {
            var entry = entries[index];
            if (entry is null)
            {
                Add(errors, GatewayValidationErrorCode.MissingRequiredValue, $"{path}[{index}]", "Metadata entry is required.");
                continue;
            }
            ValidateRequiredText(entry.Name, $"{path}[{index}].name", errors);
            ValidateRequiredText(entry.Value, $"{path}[{index}].value", errors, MaximumMetadataValueLength);
            if (!names.Add(entry.Name))
            {
                Add(errors, GatewayValidationErrorCode.DuplicateIdentity, $"{path}[{index}].name", "Metadata keys must be unique.");
            }
        }
    }

    private static void ValidateHttpUri(Uri? value, string path, bool required, ImmutableArray<GatewayValidationError>.Builder errors)
    {
        if (value is null)
        {
            if (required) Add(errors, GatewayValidationErrorCode.MissingRequiredValue, path, "URI is required.");
            return;
        }
        if (!value.IsAbsoluteUri || value.Scheme is not ("http" or "https"))
        {
            Add(errors, GatewayValidationErrorCode.InvalidValue, path, "URI must be absolute HTTP or HTTPS.");
        }
        if (!string.IsNullOrEmpty(value.UserInfo) || !string.IsNullOrEmpty(value.Query) || !string.IsNullOrEmpty(value.Fragment))
        {
            Add(errors, GatewayValidationErrorCode.InvalidValue, path, "URI must not contain user-info, a query, or a fragment.");
        }
    }

    private static void ValidateTextArray(ImmutableArray<string> values, string path, int maximum, ImmutableArray<GatewayValidationError>.Builder errors)
    {
        if (values.IsDefault || values.Length > maximum)
        {
            Add(errors, GatewayValidationErrorCode.BoundExceeded, path, "String collection exceeds its bound or is uninitialized.");
            return;
        }
        for (var index = 0; index < values.Length; index++)
        {
            ValidateRequiredText(values[index], $"{path}[{index}]", errors);
        }
    }

    private static void ValidateId(string? value, string path, ImmutableArray<GatewayValidationError>.Builder errors)
    {
        if (!GatewayIdentifier.IsCanonical(value))
        {
            Add(errors, GatewayValidationErrorCode.InvalidIdentifier, path, "Identifier is not in canonical lowercase ASCII form.");
        }
    }

    private static void ValidatePolicyName(string? value, string path, ImmutableArray<GatewayValidationError>.Builder errors) =>
        ValidateRequiredText(value, path, errors);

    private static void ValidateOptionalText(string? value, string path, ImmutableArray<GatewayValidationError>.Builder errors, int maximum = MaximumTextLength)
    {
        if (value is not null) ValidateRequiredText(value, path, errors, maximum);
    }

    private static void ValidateRequiredText(string? value, string path, ImmutableArray<GatewayValidationError>.Builder errors, int maximum = MaximumTextLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            Add(errors, GatewayValidationErrorCode.MissingRequiredValue, path, "A nonblank value is required.");
        }
        else if (value.Length > maximum)
        {
            Add(errors, GatewayValidationErrorCode.BoundExceeded, path, "Value exceeds its length bound.");
        }
    }

    private static void ValidatePositive(TimeSpan? value, string path, ImmutableArray<GatewayValidationError>.Builder errors)
    {
        if (value is { } duration && duration <= TimeSpan.Zero)
        {
            Add(errors, GatewayValidationErrorCode.InvalidValue, path, "Duration must be positive.");
        }
    }

    private static void ValidateDuration(TimeSpan? value, string path, ImmutableArray<GatewayValidationError>.Builder errors)
    {
        ValidatePositive(value, path, errors);
        if (value > MaximumOperationalDuration)
        {
            Add(errors, GatewayValidationErrorCode.InvalidValue, path, "Duration exceeds the family maximum of one day.");
        }
    }

    private static void ValidateMethods(ImmutableArray<string> methods, string path, ImmutableArray<GatewayValidationError>.Builder errors)
    {
        ValidateTextArray(methods, path, MaximumMatchItems, errors);
        if (methods.IsDefault) return;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < methods.Length; index++)
        {
            var method = methods[index];
            if (!string.IsNullOrEmpty(method) && !IsHttpToken(method)) Add(errors, GatewayValidationErrorCode.InvalidRouteMatch, $"{path}[{index}]", "HTTP method is not a valid token.");
            if (!seen.Add(method ?? string.Empty)) Add(errors, GatewayValidationErrorCode.InvalidRouteMatch, $"{path}[{index}]", "HTTP methods must be unique ignoring case.");
        }
    }

    private static void ValidateHosts(ImmutableArray<string> hosts, string path, ImmutableArray<GatewayValidationError>.Builder errors)
    {
        ValidateTextArray(hosts, path, MaximumMatchItems, errors);
        if (hosts.IsDefault) return;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < hosts.Length; index++)
        {
            var host = hosts[index];
            if (!seen.Add(host ?? string.Empty)) Add(errors, GatewayValidationErrorCode.InvalidRouteMatch, $"{path}[{index}]", "Hosts must be unique ignoring case.");
            if (!IsSupportedHostPattern(host)) Add(errors, GatewayValidationErrorCode.InvalidRouteMatch, $"{path}[{index}]", "Host is not a supported ASP.NET host pattern.");
        }
    }

    private static bool IsSupportedHostPattern(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Contains("xn--", StringComparison.OrdinalIgnoreCase) || value.Any(static c => char.IsControl(c) || char.IsWhiteSpace(c))) return false;
        if (value == "*") return true;
        var host = value.StartsWith("*.", StringComparison.Ordinal) ? value[2..] : value;
        if (host.Contains('*') || host.Contains('/') || host.Contains('?') || host.Contains('#') || host.Contains('@')) return false;
        if (host.StartsWith("[", StringComparison.Ordinal)) return Uri.TryCreate($"http://{host}", UriKind.Absolute, out _);
        var colon = host.LastIndexOf(':');
        if (colon >= 0 && (!int.TryParse(host[(colon + 1)..], out var port) || port is < 1 or > 65535)) return false;
        var hostname = colon < 0 ? host : host[..colon];
        return Uri.CheckHostName(hostname) is UriHostNameType.Dns or UriHostNameType.IPv4;
    }

    private static bool IsHttpToken(string value) => value.Length > 0 && value.All(static c =>
        char.IsAsciiLetterOrDigit(c) || c is '!' or '#' or '$' or '%' or '&' or '\'' or '*' or '+' or '-' or '.' or '^' or '_' or '`' or '|' or '~');

    private static bool ContainsInvalidFieldValueCharacter(string value) => value.Any(static c => c is '\r' or '\n' or '\0' || c < ' ' && c != '\t' || c == '\u007f');

    private static bool ContainsProhibitedQueryNameCharacter(string value) => value.Any(static c => char.IsControl(c) || c is '&' or '=' or '#');

    private static string CreateRouteShape(int order, HttpRouteMatch match)
    {
        static string Join(IEnumerable<string> values) => string.Join('\u001d', values);
        var methods = Join(match.Methods.Select(static value => value.ToUpperInvariant()).OrderBy(static value => value, StringComparer.Ordinal));
        var hosts = Join(match.Hosts.Select(static value => value.ToLowerInvariant()).OrderBy(static value => value, StringComparer.Ordinal));
        var headers = Join(match.Headers.Select(static value => $"{value.Name.ToUpperInvariant()}:{value.Kind}:{value.CaseSensitive}:{Join(value.Values.OrderBy(static item => item, StringComparer.Ordinal))}").OrderBy(static value => value, StringComparer.Ordinal));
        var query = Join(match.Query.Select(static value => $"{value.Name.ToUpperInvariant()}:{value.Kind}:{value.CaseSensitive}:{Join(value.Values.OrderBy(static item => item, StringComparer.Ordinal))}").OrderBy(static value => value, StringComparer.Ordinal));
        return $"{order}\u001f{match.Path}\u001f{methods}\u001f{hosts}\u001f{headers}\u001f{query}";
    }

    private static void ValidateEnum<T>(T value, string path, ImmutableArray<GatewayValidationError>.Builder errors)
        where T : struct, Enum
    {
        if (!Enum.IsDefined(value))
        {
            Add(errors, GatewayValidationErrorCode.InvalidEnumValue, path, "Enum value is unsupported.");
        }
    }

    private static GatewayValidationResult Result(ImmutableArray<GatewayValidationError>.Builder errors) =>
        new() { Errors = errors.ToImmutable() };

    private static void Add(ImmutableArray<GatewayValidationError>.Builder errors, GatewayValidationErrorCode code, string path, string message)
    {
        if (errors.Count < 256)
        {
            errors.Add(new GatewayValidationError(code, path, message));
        }
    }
}
