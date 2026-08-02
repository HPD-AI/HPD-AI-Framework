using System.Collections.Immutable;
using System.Text.Json.Serialization;
using HPD.Gateway.Abstractions.Serialization;

namespace HPD.Gateway.Abstractions;

public sealed record DeclarationDefinition<TSpecification>
    where TSpecification : class
{
    public required DefinitionId Id { get; init; }

    public required TSpecification Specification { get; init; }

    public ResourceMetadata Metadata { get; init; } = ResourceMetadata.Empty;
}

public sealed record DeclarationReference<TSpecification>
    where TSpecification : class
{
    public TSpecification? Inline { get; init; }

    public DefinitionId? Definition { get; init; }
}

public sealed record GatewayDefinitions
{
    public ImmutableArray<DeclarationDefinition<NamedAuthorizationPolicy>> Authorization { get; init; } = [];

    public ImmutableArray<DeclarationDefinition<CorsPolicyBinding>> Cors { get; init; } = [];

    public ImmutableArray<DeclarationDefinition<TrafficAdmissionBinding>> TrafficAdmission { get; init; } = [];

    public ImmutableArray<DeclarationDefinition<RequestTimeoutBinding>> RequestTimeout { get; init; } = [];

    public ImmutableArray<DeclarationDefinition<OutputCacheBinding>> OutputCache { get; init; } = [];

    public ImmutableArray<DeclarationDefinition<TelemetryEnrichment>> Telemetry { get; init; } = [];

    public ImmutableArray<DeclarationDefinition<RequestInspectionBinding>> Inspection { get; init; } = [];
}

public sealed record RouteDeclarations
{
    public DeclarationReference<NamedAuthorizationPolicy>? Authorization { get; init; }

    public DeclarationReference<CorsPolicyBinding>? Cors { get; init; }

    public DeclarationReference<TrafficAdmissionBinding>? TrafficAdmission { get; init; }

    public DeclarationReference<RequestTimeoutBinding>? RequestTimeout { get; init; }

    public DeclarationReference<OutputCacheBinding>? OutputCache { get; init; }

    public OrderedRequestTransforms? RequestTransforms { get; init; }

    public OrderedResponseTransforms? ResponseTransforms { get; init; }

    public DeclarationReference<TelemetryEnrichment>? Telemetry { get; init; }

    public DeclarationReference<RequestInspectionBinding>? Inspection { get; init; }
}

public sealed record GatewayRootDeclarations
{
    public DeclarationReference<NamedAuthorizationPolicy>? Authorization { get; init; }

    public DeclarationReference<CorsPolicyBinding>? Cors { get; init; }

    public DeclarationReference<TrafficAdmissionBinding>? TrafficAdmission { get; init; }

    public DeclarationReference<RequestTimeoutBinding>? RequestTimeout { get; init; }

    public DeclarationReference<OutputCacheBinding>? OutputCache { get; init; }

    public DeclarationReference<TelemetryEnrichment>? Telemetry { get; init; }

    public DeclarationReference<RequestInspectionBinding>? Inspection { get; init; }
}

public sealed record NamedAuthorizationPolicy(string PolicyName);

public sealed record CorsPolicyBinding(string PolicyName);

public sealed record TrafficAdmissionBinding(string PolicyName);

public sealed record RequestTimeoutBinding
{
    public string? PolicyName { get; init; }

    public TimeSpan? Timeout { get; init; }
}

public sealed record OutputCacheBinding(string PolicyName);

public sealed record RequestInspectionBinding
{
    public required long MaximumBodyBytes { get; init; }

    public required int MaximumInspectionBytes { get; init; }

    public bool RequireCompleteBody { get; init; }

    public bool AllowDiskSpill { get; init; }
}

public sealed record TelemetryEnrichment
{
    public ImmutableArray<MetadataEntry> Attributes { get; init; } = [];
}

public sealed record OrderedRequestTransforms
{
    public ImmutableArray<RequestHeaderTransform> Headers { get; init; } = [];
}

public sealed record OrderedResponseTransforms
{
    public ImmutableArray<ResponseHeaderTransform> Headers { get; init; } = [];

    public ImmutableArray<ResponseHeaderTransform> Trailers { get; init; } = [];
}

[JsonConverter(typeof(StrictStringEnumJsonConverter<HeaderTransformKind>))]
public enum HeaderTransformKind
{
    Set = 0,
    Append = 1,
    Remove = 2
}

public sealed record RequestHeaderTransform
{
    public required HeaderTransformKind Kind { get; init; }

    public required string Name { get; init; }

    public string? Value { get; init; }
}

public sealed record ResponseHeaderTransform
{
    public required HeaderTransformKind Kind { get; init; }

    public required string Name { get; init; }

    public string? Value { get; init; }
}
