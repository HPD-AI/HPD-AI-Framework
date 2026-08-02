using System.Collections.Immutable;
using System.Text.Json.Serialization;
using HPD.Gateway.Abstractions.Serialization;

namespace HPD.Gateway.Abstractions;

public sealed record UpstreamDeclaration
{
    public required UpstreamId Id { get; init; }

    public required UpstreamEndpointSource Endpoints { get; init; }

    public LoadBalancingDeclaration LoadBalancing { get; init; } =
        new(LoadBalancingKind.PowerOfTwoChoices);

    public SessionAffinityDeclaration? SessionAffinity { get; init; }

    public HealthCheckDeclaration? HealthChecks { get; init; }

    public UpstreamTransportDeclaration Transport { get; init; } = new();

    public UpstreamRequestDeclaration Request { get; init; } = new();

    public UpstreamResilienceBinding? Resilience { get; init; }

    public ResourceMetadata Metadata { get; init; } = ResourceMetadata.Empty;
}

public sealed record UpstreamResilienceBinding
{
    public required string ProfileName { get; init; }

    public required int ProfileVersion { get; init; }
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(StaticEndpointSource), "static")]
[JsonDerivedType(typeof(DiscoveredEndpointSource), "discovery")]
public abstract record UpstreamEndpointSource;

public sealed record StaticEndpointSource : UpstreamEndpointSource
{
    public ImmutableArray<DestinationDeclaration> Destinations { get; init; } = [];
}

public sealed record DiscoveredEndpointSource : UpstreamEndpointSource
{
    public required ProviderId Provider { get; init; }

    public required ProviderObjectId Service { get; init; }

    public ImmutableArray<ProviderParameter> Parameters { get; init; } = [];

    public required DiscoveryStaleBehavior StaleBehavior { get; init; }
}

[JsonConverter(typeof(StrictStringEnumJsonConverter<DiscoveryStaleBehavior>))]
public enum DiscoveryStaleBehavior
{
    RejectActivationUntilFresh = 0,
    PermitLastKnownMembership = 1,
    ServeUnavailableWhenStale = 2
}

public sealed record ProviderParameter(string Name, string Value);

public sealed record DestinationDeclaration
{
    public required DestinationId Id { get; init; }

    public required Uri Address { get; init; }

    public Uri? HealthAddress { get; init; }

    public string? HostOverride { get; init; }

    public ResourceMetadata Metadata { get; init; } = ResourceMetadata.Empty;
}

[JsonConverter(typeof(StrictStringEnumJsonConverter<LoadBalancingKind>))]
public enum LoadBalancingKind
{
    PowerOfTwoChoices = 0,
    RoundRobin = 1,
    LeastRequests = 2,
    Random = 3
}

public sealed record LoadBalancingDeclaration(LoadBalancingKind Kind);

public sealed record SessionAffinityDeclaration
{
    public required string Policy { get; init; }

    public required string FailurePolicy { get; init; }

    public string? CookieName { get; init; }
}

public sealed record HealthCheckDeclaration
{
    public PassiveHealthCheckDeclaration? Passive { get; init; }

    public ActiveHealthCheckDeclaration? Active { get; init; }
}

public sealed record PassiveHealthCheckDeclaration
{
    public required bool Enabled { get; init; }

    public required string Policy { get; init; }

    public TimeSpan? ReactivationPeriod { get; init; }
}

public sealed record ActiveHealthCheckDeclaration
{
    public required bool Enabled { get; init; }

    public required TimeSpan Interval { get; init; }

    public required TimeSpan Timeout { get; init; }

    public required string Policy { get; init; }

    public string? Path { get; init; }
}

[JsonConverter(typeof(StrictStringEnumJsonConverter<UpstreamHttpVersion>))]
public enum UpstreamHttpVersion
{
    Http11 = 0,
    Http2 = 1,
    Http3 = 2
}

[JsonConverter(typeof(StrictStringEnumJsonConverter<HttpVersionSelection>))]
public enum HttpVersionSelection
{
    RequestVersionOrLower = 0,
    RequestVersionOrHigher = 1,
    Exact = 2
}

public sealed record UpstreamTransportDeclaration
{
    public bool UseProxy { get; init; } = true;

    public int? MaxConnectionsPerServer { get; init; }

    public TimeSpan? ConnectTimeout { get; init; }

    public bool EnableMultipleHttp2Connections { get; init; }

    public bool RequestHeaderEncodingLatin1 { get; init; }

    public UpstreamTlsDeclaration? Tls { get; init; }
}

public sealed record UpstreamTlsDeclaration
{
    public required string ServerName { get; init; }

    public SecretReference? ClientCertificate { get; init; }

    public SecretReference? TrustBundle { get; init; }
}

public sealed record UpstreamRequestDeclaration
{
    public TimeSpan? ActivityTimeout { get; init; }

    public UpstreamHttpVersion Version { get; init; } = UpstreamHttpVersion.Http2;

    public HttpVersionSelection VersionSelection { get; init; } =
        HttpVersionSelection.RequestVersionOrLower;

    public bool AllowResponseBuffering { get; init; }
}
