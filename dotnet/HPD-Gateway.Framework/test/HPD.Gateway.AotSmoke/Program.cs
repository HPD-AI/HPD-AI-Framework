using System.Text.Json;
using HPD.Gateway.Abstractions;
using HPD.Gateway.Abstractions.Serialization;
using HPD.Gateway.Core;
using HPD.Gateway.Yarp;
using Microsoft.Extensions.DependencyInjection;
using Yarp.ReverseProxy.Configuration;

var configuration = new GatewayConfiguration
{
    SchemaVersion = new GatewaySchemaVersion(1, 0),
    CanonicalizationVersion = 1,
    Metadata = new ResourceMetadata
    {
        DisplayName = "AOT smoke",
        Labels = [new MetadataEntry("environment", "smoke")]
    },
    Definitions = new GatewayDefinitions
    {
        Authorization =
        [
            new DeclarationDefinition<NamedAuthorizationPolicy>
            {
                Id = new DefinitionId("authorized"),
                Specification = new NamedAuthorizationPolicy("GatewayUsers")
            }
        ],
        Cors =
        [
            new DeclarationDefinition<CorsPolicyBinding>
            {
                Id = new DefinitionId("cors"),
                Specification = new CorsPolicyBinding("GatewayCors")
            }
        ],
        TrafficAdmission =
        [
            new DeclarationDefinition<TrafficAdmissionBinding>
            {
                Id = new DefinitionId("admission"),
                Specification = new TrafficAdmissionBinding("GatewayAdmission")
            }
        ],
        RequestTimeout =
        [
            new DeclarationDefinition<RequestTimeoutBinding>
            {
                Id = new DefinitionId("timeout"),
                Specification = new RequestTimeoutBinding { Timeout = TimeSpan.FromSeconds(10) }
            }
        ],
        OutputCache =
        [
            new DeclarationDefinition<OutputCacheBinding>
            {
                Id = new DefinitionId("cache"),
                Specification = new OutputCacheBinding("GatewayCache")
            }
        ],
        Telemetry =
        [
            new DeclarationDefinition<TelemetryEnrichment>
            {
                Id = new DefinitionId("telemetry"),
                Specification = new TelemetryEnrichment
                {
                    Attributes = [new MetadataEntry("gateway.area", "smoke")]
                }
            }
        ],
        Inspection =
        [
            new DeclarationDefinition<RequestInspectionBinding>
            {
                Id = new DefinitionId("inspection"),
                Specification = new RequestInspectionBinding
                {
                    MaximumBodyBytes = 65_536,
                    MaximumInspectionBytes = 1_024,
                    AllowDiskSpill = false
                }
            }
        ]
    },
    RootDefaults = new GatewayRootDeclarations
    {
        Cors = new DeclarationReference<CorsPolicyBinding> { Definition = new DefinitionId("cors") },
        TrafficAdmission = new DeclarationReference<TrafficAdmissionBinding> { Definition = new DefinitionId("admission") },
        RequestTimeout = new DeclarationReference<RequestTimeoutBinding> { Definition = new DefinitionId("timeout") },
        Telemetry = new DeclarationReference<TelemetryEnrichment> { Definition = new DefinitionId("telemetry") }
    },
    Upstreams =
    [
        new UpstreamDeclaration
        {
            Id = new UpstreamId("static"),
            Endpoints = new StaticEndpointSource
            {
                Destinations =
                [
                    new DestinationDeclaration
                    {
                        Id = new DestinationId("one"),
                        Address = new Uri("https://127.0.0.1"),
                        HealthAddress = new Uri("https://127.0.0.1/health"),
                        HostOverride = "backend.internal"
                    }
                ]
            },
            LoadBalancing = new LoadBalancingDeclaration(LoadBalancingKind.RoundRobin),
            SessionAffinity = new SessionAffinityDeclaration
            {
                Policy = "Cookie",
                FailurePolicy = "Redistribute",
                CookieName = "hpd-affinity"
            },
            HealthChecks = new HealthCheckDeclaration
            {
                Passive = new PassiveHealthCheckDeclaration
                {
                    Enabled = true,
                    Policy = "TransportFailureRate",
                    ReactivationPeriod = TimeSpan.FromSeconds(30)
                },
                Active = new ActiveHealthCheckDeclaration
                {
                    Enabled = true,
                    Interval = TimeSpan.FromSeconds(10),
                    Timeout = TimeSpan.FromSeconds(2),
                    Policy = "ConsecutiveFailures",
                    Path = "/health"
                }
            },
            Transport = new UpstreamTransportDeclaration
            {
                MaxConnectionsPerServer = 32,
                ConnectTimeout = TimeSpan.FromSeconds(3),
                EnableMultipleHttp2Connections = true,
                Tls = new UpstreamTlsDeclaration
                {
                    ServerName = "backend.internal",
                    ClientCertificate = new SecretReference(
                        new ProviderId("secrets"),
                        new ProviderObjectId("client-cert"),
                        "v1")
                }
            },
            Request = new UpstreamRequestDeclaration
            {
                ActivityTimeout = TimeSpan.FromSeconds(30),
                Version = UpstreamHttpVersion.Http2,
                VersionSelection = HttpVersionSelection.RequestVersionOrLower
            }
        },
        new UpstreamDeclaration
        {
            Id = new UpstreamId("discovered"),
            Endpoints = new DiscoveredEndpointSource
            {
                Provider = new ProviderId("dns"),
                Service = new ProviderObjectId("orders"),
                Parameters = [new ProviderParameter("region", "local")],
                StaleBehavior = DiscoveryStaleBehavior.RejectActivationUntilFresh
            }
        }
    ],
    Routes =
    [
        new RouteDeclaration
        {
            Id = new RouteId("smoke"),
            Listener = new ListenerId("https"),
            Match = new HttpRouteMatch
            {
                Path = "/{**catch-all}",
                Methods = ["GET"],
                Hosts = ["gateway.local"],
                Headers =
                [
                    new HttpHeaderMatch
                    {
                        Name = "X-Smoke",
                        Kind = TextMatchKind.Exact,
                        Values = ["yes"]
                    }
                ],
                Query =
                [
                    new HttpQueryMatch
                    {
                        Name = "ready",
                        Kind = TextMatchKind.Exists
                    }
                ]
            },
            Upstream = new UpstreamId("static"),
            Declarations = new RouteDeclarations
            {
                Authorization = new DeclarationReference<NamedAuthorizationPolicy>
                {
                    Definition = new DefinitionId("authorized")
                },
                OutputCache = new DeclarationReference<OutputCacheBinding>
                {
                    Definition = new DefinitionId("cache")
                },
                Inspection = new DeclarationReference<RequestInspectionBinding>
                {
                    Definition = new DefinitionId("inspection")
                },
                RequestTransforms = new OrderedRequestTransforms
                {
                    Headers =
                    [
                        new RequestHeaderTransform
                        {
                            Kind = HeaderTransformKind.Set,
                            Name = "X-Gateway",
                            Value = "HPD"
                        }
                    ]
                },
                ResponseTransforms = new OrderedResponseTransforms
                {
                    Headers =
                    [
                        new ResponseHeaderTransform
                        {
                            Kind = HeaderTransformKind.Append,
                            Name = "X-Gateway",
                            Value = "HPD"
                        }
                    ],
                    Trailers =
                    [
                        new ResponseHeaderTransform
                        {
                            Kind = HeaderTransformKind.Set,
                            Name = "X-Gateway-Complete",
                            Value = "true"
                        }
                    ]
                }
            }
        }
    ]
};

var json = JsonSerializer.SerializeToUtf8Bytes(configuration, GatewayJsonSerializerContext.Default.GatewayConfiguration);
var capabilities = HostCapabilitySnapshot.Create(new HostCapabilityRegistration
{
    InstalledFamilies = GatewayDeclarationFamilies.AllBaseline,
    AuthorizationPolicies = ["GatewayUsers"],
    CorsPolicies = ["GatewayCors"],
    TrafficAdmissionPolicies = ["GatewayAdmission"],
    OutputCachePolicies = ["GatewayCache"],
    SessionAffinityPolicies = ["Cookie"],
    SessionAffinityFailurePolicies = ["Redistribute"],
    PassiveHealthPolicies = ["TransportFailureRate"],
    ActiveHealthPolicies = ["ConsecutiveFailures"],
    Listeners = [new ListenerCapability(new ListenerId("https"), ListenerRole.DataPlane, ListenerProtocols.Http1 | ListenerProtocols.Http2, ["gateway.local"], true)],
    DiscoveryProviders = [new DiscoveryProviderCapability(new ProviderId("dns"), ["region"], ["region"], false, true)],
    SecretProviders = [new ProviderId("secrets")]
});
var read = GatewayCandidateReader.Read(json, capabilities);
if (!read.IsAccepted)
{
    throw new InvalidOperationException($"Strict native candidate reading failed with {read.Errors.Length} error(s).");
}

var validation = GatewayCandidateValidator.Validate(read.Configuration, capabilities);

var canonical = GatewayConfigurationCanonicalizer.TryCanonicalize(read.Configuration);
if (!canonical.IsCanonicalized || canonical.Document!.ContentHash.Value.Length != 64)
{
    throw new InvalidOperationException("Canonicalization or content hashing failed.");
}

var materializableConfiguration = configuration with
{
    Definitions = new GatewayDefinitions(),
    RootDefaults = new GatewayRootDeclarations(),
    Upstreams =
    [
        configuration.Upstreams[0] with
        {
            SessionAffinity = null,
            HealthChecks = null,
            Transport = configuration.Upstreams[0].Transport with { Tls = null }
        }
    ],
    Routes =
    [
        configuration.Routes[0] with
        {
            Listener = null,
            Declarations = new RouteDeclarations
            {
                RequestTransforms = configuration.Routes[0].Declarations!.RequestTransforms,
                ResponseTransforms = configuration.Routes[0].Declarations!.ResponseTransforms
            }
        }
    ]
};
var materializableJson = JsonSerializer.SerializeToUtf8Bytes(materializableConfiguration, GatewayJsonSerializerContext.Default.GatewayConfiguration);
var materializableRead = GatewayCandidateReader.Read(materializableJson, capabilities);
if (!materializableRead.IsAccepted) throw new InvalidOperationException("Materializable AOT candidate was rejected.");

var services = new ServiceCollection();
services.AddLogging();
services.AddReverseProxy();
services.AddHpdGatewayYarpPublication();
services.AddHpdGatewayYarpMaterialization();
await using var serviceProvider = services.BuildServiceProvider();
var materializer = serviceProvider.GetRequiredService<GatewayNativeMaterializer>();
var materialized = await materializer.MaterializeAsync(
    materializableRead,
    new PublicationCandidateIdentity(
        new CandidateId("aot-smoke"),
        "aot-authority",
        "epoch-1",
        1,
        materializableRead.CanonicalDocument!.ContentHash),
    "native-aot-smoke");
if (!materialized.IsMaterialized) throw new InvalidOperationException("Native AOT materialization failed.");

var publicationBundle = materialized.Bundle!;
var proxyProvider = serviceProvider.GetRequiredService<HpdProxyConfigProvider>();
var changeListener = serviceProvider.GetRequiredService<HpdConfigChangeListener>();
var publisher = serviceProvider.GetRequiredService<GatewayYarpPublisher>();
var publication = publisher.PublishAsync(publicationBundle, TimeSpan.FromSeconds(5));
IProxyConfig? publishedSnapshot = null;
for (var index = 0; index < 1_000; index++)
{
    var current = proxyProvider.GetConfig();
    if (current.RevisionId == publicationBundle.NativeRevisionId)
    {
        publishedSnapshot = current;
        break;
    }
    await Task.Yield();
}
if (publishedSnapshot is null)
{
    throw new InvalidOperationException("The serialized publisher did not install its native snapshot.");
}
changeListener.ConfigurationApplied([publishedSnapshot]);
if ((await publication).State != GatewayPublicationState.ActiveAcknowledged)
{
    throw new InvalidOperationException("The serialized publisher did not acknowledge the exact native revision.");
}

_ = JsonSerializer.SerializeToUtf8Bytes(validation, GatewayJsonSerializerContext.Default.GatewayValidationResult);
_ = JsonSerializer.SerializeToUtf8Bytes(canonical, GatewayJsonSerializerContext.Default.GatewayCanonicalizationResult);

Console.WriteLine(
    $"HPD.Gateway AOT smoke passed: {read.Configuration!.Routes.Length} route(s), " +
    $"{read.Configuration.Upstreams.Length} upstream(s), sha256={canonical.Document.ContentHash.Value}.");
