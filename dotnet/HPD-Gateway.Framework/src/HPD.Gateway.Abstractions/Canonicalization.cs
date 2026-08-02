using System.Collections.Immutable;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using HPD.Gateway.Abstractions.Serialization;

namespace HPD.Gateway.Abstractions;

public readonly record struct ContentHash(string Algorithm, string Value);

public sealed class GatewayCanonicalDocument
{
    internal GatewayCanonicalDocument(ImmutableArray<byte> utf8Json, ContentHash contentHash)
    {
        Utf8Json = utf8Json;
        ContentHash = contentHash;
    }

    public ImmutableArray<byte> Utf8Json { get; }

    public ContentHash ContentHash { get; }
}

public sealed record GatewayCanonicalizationResult
{
    public GatewayCanonicalDocument? Document { get; init; }

    public required ImmutableArray<GatewayValidationError> Errors { get; init; }

    public bool IsCanonicalized => Document is not null && Errors.IsEmpty;
}

public static class GatewayConfigurationCanonicalizer
{
    public static GatewayCanonicalizationResult TryCanonicalize(GatewayConfiguration? configuration)
    {
        var validation = GatewayConfigurationValidator.Validate(configuration);
        if (!validation.IsValid || configuration is null)
        {
            return new GatewayCanonicalizationResult { Errors = validation.Errors };
        }

        var canonical = configuration with
        {
            Metadata = CanonicalMetadata(configuration.Metadata!),
            Routes = configuration.Routes
                .OrderBy(static route => route.Id.Value, StringComparer.Ordinal)
                .Select(CanonicalRoute)
                .ToImmutableArray(),
            Upstreams = configuration.Upstreams
                .OrderBy(static upstream => upstream.Id.Value, StringComparer.Ordinal)
                .Select(CanonicalUpstream)
                .ToImmutableArray(),
            Definitions = CanonicalDefinitions(configuration.Definitions!),
            RootDefaults = CanonicalRootDeclarations(configuration.RootDefaults!)
        };

        var json = JsonSerializer.SerializeToUtf8Bytes(canonical, GatewayJsonSerializerContext.Default.GatewayConfiguration);
        var framed = new byte[6 + json.Length];
        BinaryPrimitives.WriteUInt16BigEndian(framed.AsSpan(0, 2), configuration.SchemaVersion.Major);
        BinaryPrimitives.WriteUInt16BigEndian(framed.AsSpan(2, 2), configuration.SchemaVersion.Minor);
        BinaryPrimitives.WriteUInt16BigEndian(framed.AsSpan(4, 2), configuration.CanonicalizationVersion);
        json.CopyTo(framed.AsSpan(6));
        var hash = new ContentHash("sha-256", Convert.ToHexStringLower(SHA256.HashData(framed)));
        return new GatewayCanonicalizationResult
        {
            Document = new GatewayCanonicalDocument(ImmutableArray.Create(json), hash),
            Errors = []
        };
    }

    private static RouteDeclaration CanonicalRoute(RouteDeclaration value) => value with
    {
        Match = value.Match! with
        {
            Methods = Sort(value.Match.Methods.Select(static method => method.ToUpperInvariant()).ToImmutableArray()),
            Hosts = Sort(value.Match.Hosts.Select(static host => host.ToLowerInvariant()).ToImmutableArray()),
            Headers = value.Match.Headers
                .OrderBy(static item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static item => item.Kind)
                .Select(static item => item with { Name = item.Name.ToLowerInvariant(), Values = SortMatchValues(item.Values, item.CaseSensitive) })
                .ToImmutableArray(),
            Query = value.Match.Query
                .OrderBy(static item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static item => item.Kind)
                .Select(static item => item with { Name = item.Name.ToLowerInvariant(), Values = SortMatchValues(item.Values, item.CaseSensitive) })
                .ToImmutableArray()
        },
        Declarations = CanonicalRouteDeclarations(value.Declarations!),
        Metadata = CanonicalMetadata(value.Metadata!)
    };

    private static UpstreamDeclaration CanonicalUpstream(UpstreamDeclaration value) => value with
    {
        Endpoints = value.Endpoints switch
        {
            StaticEndpointSource source => source with
            {
                Destinations = source.Destinations
                    .OrderBy(static destination => destination.Id.Value, StringComparer.Ordinal)
                    .Select(static destination => destination with { Metadata = CanonicalMetadata(destination.Metadata!) })
                    .ToImmutableArray()
            },
            DiscoveredEndpointSource source => source with
            {
                Parameters = source.Parameters
                    .OrderBy(static parameter => parameter.Name, StringComparer.Ordinal)
                    .ThenBy(static parameter => parameter.Value, StringComparer.Ordinal)
                    .ToImmutableArray()
            },
            _ => value.Endpoints
        },
        Metadata = CanonicalMetadata(value.Metadata!)
    };

    private static GatewayDefinitions CanonicalDefinitions(GatewayDefinitions value) => value with
    {
        Authorization = CanonicalDefinitions(value.Authorization, static item => item),
        Cors = CanonicalDefinitions(value.Cors, static item => item),
        TrafficAdmission = CanonicalDefinitions(value.TrafficAdmission, static item => item),
        RequestTimeout = CanonicalDefinitions(value.RequestTimeout, static item => item),
        OutputCache = CanonicalDefinitions(value.OutputCache, static item => item),
        Telemetry = CanonicalDefinitions(value.Telemetry, CanonicalTelemetry),
        Inspection = CanonicalDefinitions(value.Inspection, static item => item)
    };

    private static ImmutableArray<DeclarationDefinition<T>> CanonicalDefinitions<T>(
        ImmutableArray<DeclarationDefinition<T>> definitions,
        Func<T, T> canonicalize)
        where T : class => definitions
            .OrderBy(static definition => definition.Id.Value, StringComparer.Ordinal)
            .Select(definition => definition with
            {
                Specification = canonicalize(definition.Specification),
                Metadata = CanonicalMetadata(definition.Metadata!)
            })
            .ToImmutableArray();

    private static GatewayRootDeclarations CanonicalRootDeclarations(GatewayRootDeclarations value) => value with
    {
        Telemetry = CanonicalReference(value.Telemetry, CanonicalTelemetry)
    };

    private static RouteDeclarations CanonicalRouteDeclarations(RouteDeclarations value) => value with
    {
        Telemetry = CanonicalReference(value.Telemetry, CanonicalTelemetry)
    };

    private static DeclarationReference<T>? CanonicalReference<T>(DeclarationReference<T>? value, Func<T, T> canonicalize)
        where T : class => value?.Inline is null ? value : value with { Inline = canonicalize(value.Inline) };

    private static TelemetryEnrichment CanonicalTelemetry(TelemetryEnrichment value) => value with
    {
        Attributes = CanonicalEntries(value.Attributes)
    };

    private static ResourceMetadata CanonicalMetadata(ResourceMetadata value) => value with
    {
        Labels = CanonicalEntries(value.Labels),
        Annotations = CanonicalEntries(value.Annotations)
    };

    private static ImmutableArray<MetadataEntry> CanonicalEntries(ImmutableArray<MetadataEntry> values) => values
        .OrderBy(static value => value.Name, StringComparer.Ordinal)
        .ThenBy(static value => value.Value, StringComparer.Ordinal)
        .ToImmutableArray();

    private static ImmutableArray<string> Sort(ImmutableArray<string> values) => values
        .OrderBy(static value => value, StringComparer.Ordinal)
        .ToImmutableArray();

    private static ImmutableArray<string> SortMatchValues(ImmutableArray<string> values, bool caseSensitive) => values
        .OrderBy(static value => value, caseSensitive ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase)
        .ToImmutableArray();
}
