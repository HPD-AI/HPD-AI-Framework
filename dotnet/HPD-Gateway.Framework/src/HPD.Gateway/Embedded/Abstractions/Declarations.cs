using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace HPD.Gateway;

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

    public ImmutableArray<DeclarationDefinition<TrafficAdmissionPlan>> TrafficAdmission { get; init; } = [];

    public ImmutableArray<DeclarationDefinition<RequestTimeoutBinding>> RequestTimeout { get; init; } = [];

    public ImmutableArray<DeclarationDefinition<OutputCacheBinding>> OutputCache { get; init; } = [];

    public ImmutableArray<DeclarationDefinition<TelemetryEnrichment>> Telemetry { get; init; } = [];

    public ImmutableArray<DeclarationDefinition<RequestInspectionBinding>> Inspection { get; init; } = [];

    public ImmutableArray<DeclarationDefinition<CredentialDispositionBinding>> CredentialDisposition { get; init; } = [];
}

public sealed record RouteDeclarations
{
    public DeclarationReference<NamedAuthorizationPolicy>? Authorization { get; init; }

    public DeclarationReference<CorsPolicyBinding>? Cors { get; init; }

    public DeclarationReference<TrafficAdmissionPlan>? TrafficAdmission { get; init; }

    public DeclarationReference<RequestTimeoutBinding>? RequestTimeout { get; init; }

    public DeclarationReference<OutputCacheBinding>? OutputCache { get; init; }

    public OrderedRequestTransforms? RequestTransforms { get; init; }

    public OrderedResponseTransforms? ResponseTransforms { get; init; }

    public DeclarationReference<TelemetryEnrichment>? Telemetry { get; init; }

    public DeclarationReference<RequestInspectionBinding>? Inspection { get; init; }

    public DeclarationReference<CredentialDispositionBinding>? CredentialDisposition { get; init; }
}

public sealed record GatewayRootDeclarations
{
    public DeclarationReference<NamedAuthorizationPolicy>? Authorization { get; init; }

    public DeclarationReference<CorsPolicyBinding>? Cors { get; init; }

    public DeclarationReference<TrafficAdmissionPlan>? TrafficAdmission { get; init; }

    public DeclarationReference<RequestTimeoutBinding>? RequestTimeout { get; init; }

    public DeclarationReference<OutputCacheBinding>? OutputCache { get; init; }

    public DeclarationReference<TelemetryEnrichment>? Telemetry { get; init; }

    public DeclarationReference<RequestInspectionBinding>? Inspection { get; init; }

    public DeclarationReference<CredentialDispositionBinding>? CredentialDisposition { get; init; }
}

public sealed record NamedAuthorizationPolicy(string PolicyName);

public sealed record CorsPolicyBinding(string PolicyName);

public sealed record RequestTimeoutBinding
{
    public string? PolicyName { get; init; }

    public TimeSpan? Timeout { get; init; }
}

public sealed record OutputCacheBinding(string PolicyName);

public sealed record RequestInspectionBinding
{
    public required string InspectorName { get; init; }

    public required RequestInspectionMode Mode { get; init; }

    public required long MaximumAcceptedBodyBytes { get; init; }

    public int? MaximumInspectedBytes { get; init; }

    public int? MemoryThresholdBytes { get; init; }

    public RequestInspectionSpillPolicy SpillPolicy { get; init; }
}

[JsonConverter(typeof(StrictStringEnumJsonConverter<CredentialDispositionKind>))]
public enum CredentialDispositionKind
{
    Strip = 0
}

public sealed record CredentialDispositionBinding
{
    public required CredentialDispositionKind Kind { get; init; }
}

[JsonConverter(typeof(StrictStringEnumJsonConverter<RequestInspectionMode>))]
public enum RequestInspectionMode
{
    BoundedPrefix = 0,
    CompleteBody = 1
}

[JsonConverter(typeof(StrictStringEnumJsonConverter<RequestInspectionSpillPolicy>))]
public enum RequestInspectionSpillPolicy
{
    Disabled = 0,
    Allowed = 1
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
