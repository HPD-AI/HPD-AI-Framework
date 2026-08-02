using System.Collections.Immutable;
using System.Net;
using System.Text.Json;
using HPD.Gateway.Abstractions;
using HPD.Gateway.Abstractions.Serialization;
using HPD.Gateway.Core;
using HPD.Gateway.Inspection;
using HPD.Gateway.Resilience;
using HPD.Gateway.Yarp;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Yarp.ReverseProxy.Configuration;

var smokeResilienceProfile = new GatewayResilienceProfile
{
    Name = "smoke-resilience",
    Version = 1,
    Retry = new GatewayResponseRetryProfile
    {
        StatusCodes = [HttpStatusCode.ServiceUnavailable],
        MaximumRetryAttempts = 1,
        Delay = TimeSpan.Zero,
        MaximumRetryAfter = TimeSpan.FromMilliseconds(10)
    },
    CircuitBreaker = new GatewayCircuitBreakerProfile
    {
        StatusCodes = [HttpStatusCode.ServiceUnavailable],
        FailureRatio = 1,
        MinimumThroughput = 2,
        SamplingDuration = TimeSpan.FromSeconds(10),
        BreakDuration = TimeSpan.FromSeconds(2)
    },
    ConcurrencyLimiter = new GatewayOutboundConcurrencyProfile { PermitLimit = 8, QueueLimit = 0 },
    AttemptTimeout = new GatewayAttemptTimeoutProfile { Timeout = TimeSpan.FromSeconds(1) }
};

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
                    InspectorName = "smoke-inspector",
                    Mode = RequestInspectionMode.BoundedPrefix,
                    MaximumAcceptedBodyBytes = 65_536,
                    MaximumInspectedBytes = 1_024
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
            },
            Resilience = new UpstreamResilienceBinding { ProfileName = "smoke-resilience", ProfileVersion = 1 }
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
    InstalledFamilies = GatewayDeclarationFamilies.All,
    AuthorizationPolicies = ["GatewayUsers"],
    CorsPolicies = ["GatewayCors"],
    TrafficAdmissionPolicies = ["GatewayAdmission"],
    OutputCachePolicies = ["GatewayCache"],
    SessionAffinityPolicies = ["Cookie"],
    SessionAffinityFailurePolicies = ["Redistribute"],
    PassiveHealthPolicies = ["TransportFailureRate"],
    ActiveHealthPolicies = ["ConsecutiveFailures"],
    RequestInspectors = ["smoke-inspector"],
    UpstreamResilienceProfiles =
    [
        new UpstreamResilienceCapability(
            "smoke-resilience",
            1,
            UpstreamResilienceStrategies.SelectedResponseRetry | UpstreamResilienceStrategies.CircuitBreaker |
            UpstreamResilienceStrategies.OutboundConcurrencyLimiter | UpstreamResilienceStrategies.PerAttemptTimeout,
            [503],
            1)
    ],
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

var supportedRouteDeclarations = configuration.Routes[0].Declarations!;
var materializableConfiguration = configuration with
{
    Definitions = new GatewayDefinitions
    {
        Authorization = configuration.Definitions!.Authorization,
        Cors = configuration.Definitions.Cors,
        TrafficAdmission = configuration.Definitions.TrafficAdmission,
        RequestTimeout = configuration.Definitions.RequestTimeout,
        OutputCache = configuration.Definitions.OutputCache,
        Inspection = configuration.Definitions.Inspection
    },
    RootDefaults = new GatewayRootDeclarations
    {
        Cors = configuration.RootDefaults!.Cors,
        TrafficAdmission = configuration.RootDefaults.TrafficAdmission,
        RequestTimeout = configuration.RootDefaults.RequestTimeout
    },
    Upstreams =
    [
        configuration.Upstreams[0] with
        {
            Transport = configuration.Upstreams[0].Transport with { Tls = null }
        }
    ],
    Routes =
    [
        configuration.Routes[0] with
        {
            Declarations = new RouteDeclarations
            {
                Authorization = supportedRouteDeclarations.Authorization,
                OutputCache = supportedRouteDeclarations.OutputCache,
                Inspection = supportedRouteDeclarations.Inspection,
                RequestTransforms = supportedRouteDeclarations.RequestTransforms,
                ResponseTransforms = supportedRouteDeclarations.ResponseTransforms
            }
        }
    ]
};
var materializableJson = JsonSerializer.SerializeToUtf8Bytes(materializableConfiguration, GatewayJsonSerializerContext.Default.GatewayConfiguration);
var materializableRead = GatewayCandidateReader.Read(materializableJson, capabilities);
if (!materializableRead.IsAccepted) throw new InvalidOperationException("Materializable AOT candidate was rejected.");

var services = new ServiceCollection();
services.AddLogging();
services.AddAuthorizationBuilder().AddPolicy("GatewayUsers", policy => policy.RequireAssertion(_ => true));
services.AddCors(options => options.AddPolicy("GatewayCors", policy => policy.AllowAnyOrigin()));
services.AddRateLimiter(options => options.AddFixedWindowLimiter("GatewayAdmission", limiter =>
{
    limiter.PermitLimit = 10;
    limiter.Window = TimeSpan.FromMinutes(1);
}));
services.AddOutputCache(options => options.AddPolicy("GatewayCache", policy => policy.Expire(TimeSpan.FromSeconds(10))));
services.AddReverseProxy();
services.AddHpdGatewayYarpPublication();
services.AddHpdGatewayYarpResilience(registry => registry.Add(smokeResilienceProfile));
services.AddHpdGatewayYarpMaterialization();
var smokeInspector = new SmokeInspector();
services.AddHpdGatewayYarpInspection(registry => registry.Add("smoke-inspector", smokeInspector));
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
if (!materialized.IsMaterialized)
    throw new InvalidOperationException($"Native AOT materialization failed: {string.Join(", ", materialized.Diagnostics.Select(item => $"{item.Code}@{item.Path}"))}");

var inspectionExecutor = serviceProvider.GetRequiredService<GatewayInspectionExecutor>();
var inspectionContext = new DefaultHttpContext();
inspectionContext.Request.ContentLength = 4;
inspectionContext.Request.Body = new MemoryStream("aot!"u8.ToArray());
var forwardedBody = string.Empty;
await inspectionExecutor.ExecuteAsync(
    inspectionContext,
    new GatewayInspectionSelection("smoke-inspector", RequestInspectionMode.BoundedPrefix, 16, 2, null, RequestInspectionSpillPolicy.Disabled),
    async context =>
    {
        using var reader = new StreamReader(context.Request.Body);
        forwardedBody = await reader.ReadToEndAsync();
    });
if (forwardedBody != "aot!") throw new InvalidOperationException("Native AOT prefix inspection replay failed.");

var completeMemoryContext = new DefaultHttpContext();
completeMemoryContext.Request.ContentLength = null;
completeMemoryContext.Request.Body = new NonSeekableReadStream("complete-memory"u8.ToArray());
var completeMemoryBody = string.Empty;
await inspectionExecutor.ExecuteAsync(
    completeMemoryContext,
    new GatewayInspectionSelection("smoke-inspector", RequestInspectionMode.CompleteBody, 64, null, 64, RequestInspectionSpillPolicy.Disabled),
    async context =>
    {
        using var reader = new StreamReader(context.Request.Body, leaveOpen: true);
        completeMemoryBody = await reader.ReadToEndAsync();
    });
if (completeMemoryBody != "complete-memory") throw new InvalidOperationException("Native AOT complete-memory inspection replay failed.");
await completeMemoryContext.Request.Body.DisposeAsync();

var spillBytes = new byte[8 * 1024];
Array.Fill(spillBytes, (byte)'s');
var completeSpillContext = new DefaultHttpContext();
completeSpillContext.Request.ContentLength = null;
completeSpillContext.Request.Body = new NonSeekableReadStream(spillBytes);
var completeSpillLength = 0;
await inspectionExecutor.ExecuteAsync(
    completeSpillContext,
    new GatewayInspectionSelection("smoke-inspector", RequestInspectionMode.CompleteBody, 16 * 1024, null, 128, RequestInspectionSpillPolicy.Allowed),
    async context =>
    {
        using var buffer = new MemoryStream();
        await context.Request.Body.CopyToAsync(buffer);
        completeSpillLength = checked((int)buffer.Length);
    });
if (completeSpillLength != spillBytes.Length || !smokeInspector.SpillExistedDuringInspection || smokeInspector.SpillPath is null)
    throw new InvalidOperationException("Native AOT complete-body spill inspection failed.");
var spillPath = smokeInspector.SpillPath;
await completeSpillContext.Request.Body.DisposeAsync();
if (File.Exists(spillPath)) throw new InvalidOperationException("Native AOT inspection spill file was not cleaned up.");

var resilienceRegistry = serviceProvider.GetRequiredService<GatewayResilienceRegistry>();
var retryTerminal = new AotRetryHandler();
using (var resilientInvoker = new HttpMessageInvoker(resilienceRegistry.Wrap(smokeResilienceProfile.Name, smokeResilienceProfile.Version, retryTerminal)))
using (var resilientRequest = new HttpRequestMessage(HttpMethod.Get, "http://localhost/aot-resilience"))
using (var resilientResponse = await resilientInvoker.SendAsync(resilientRequest, CancellationToken.None))
{
    if (resilientResponse.StatusCode != HttpStatusCode.OK || retryTerminal.Attempts != 2)
        throw new InvalidOperationException("Native AOT selected-response resilience execution failed.");
}

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

var liveAttempts = 0;
var liveUpstreamBuilder = WebApplication.CreateSlimBuilder();
liveUpstreamBuilder.WebHost.UseUrls("http://127.0.0.1:0");
await using var liveUpstream = liveUpstreamBuilder.Build();
liveUpstream.Run(context =>
{
    var attempt = Interlocked.Increment(ref liveAttempts);
    context.Response.StatusCode = attempt == 1 ? StatusCodes.Status503ServiceUnavailable : StatusCodes.Status200OK;
    return Task.CompletedTask;
});
await liveUpstream.StartAsync();

var liveProxyBuilder = WebApplication.CreateSlimBuilder();
liveProxyBuilder.WebHost.UseUrls("http://127.0.0.1:0");
liveProxyBuilder.Services.AddReverseProxy();
liveProxyBuilder.Services.AddHpdGatewayYarpPublication();
liveProxyBuilder.Services.AddHpdGatewayYarpResilience(registry => registry.Add(smokeResilienceProfile));
liveProxyBuilder.Services.AddHpdGatewayYarpMaterialization();
await using var liveProxy = liveProxyBuilder.Build();
liveProxy.MapReverseProxy();
await liveProxy.StartAsync();

var liveConfiguration = new GatewayConfiguration
{
    SchemaVersion = new GatewaySchemaVersion(1, 0),
    CanonicalizationVersion = 1,
    Routes =
    [
        new RouteDeclaration
        {
            Id = new RouteId("live"),
            Match = new HttpRouteMatch { Path = "/{**path}", Methods = ["GET"] },
            Upstream = new UpstreamId("live"),
            Declarations = new RouteDeclarations()
        }
    ],
    Upstreams =
    [
        new UpstreamDeclaration
        {
            Id = new UpstreamId("live"),
            Endpoints = new StaticEndpointSource
            {
                Destinations = [new DestinationDeclaration { Id = new DestinationId("one"), Address = new Uri(Address(liveUpstream)) }]
            },
            Resilience = new UpstreamResilienceBinding { ProfileName = smokeResilienceProfile.Name, ProfileVersion = smokeResilienceProfile.Version }
        }
    ]
};
var liveCapabilities = HostCapabilitySnapshot.Create(new HostCapabilityRegistration
{
    InstalledFamilies = GatewayDeclarationFamilies.UpstreamResilience,
    UpstreamResilienceProfiles = liveProxy.Services.GetHpdGatewayResilienceCapabilities()
});
var liveJson = JsonSerializer.SerializeToUtf8Bytes(liveConfiguration, GatewayJsonSerializerContext.Default.GatewayConfiguration);
var liveAccepted = GatewayCandidateReader.Read(liveJson, liveCapabilities);
if (!liveAccepted.IsAccepted) throw new InvalidOperationException("Native AOT live resilience candidate was rejected.");
var liveIdentity = new PublicationCandidateIdentity(new CandidateId("aot-live"), "aot-live-authority", "epoch-1", 1, liveAccepted.CanonicalDocument!.ContentHash);
var liveMaterialized = await liveProxy.Services.GetRequiredService<GatewayNativeMaterializer>()
    .MaterializeAsync(liveAccepted, liveIdentity, "aot-live-native");
if (!liveMaterialized.IsMaterialized) throw new InvalidOperationException("Native AOT live resilience materialization failed.");
var livePublication = await liveProxy.Services.GetRequiredService<GatewayYarpPublisher>()
    .PublishAsync(liveMaterialized.Bundle!, TimeSpan.FromSeconds(5));
if (livePublication.State != GatewayPublicationState.ActiveAcknowledged)
    throw new InvalidOperationException("Native AOT live resilience publication was not acknowledged.");
using var liveClient = new HttpClient { BaseAddress = new Uri(Address(liveProxy)) };
using var liveResponse = await liveClient.GetAsync("/retry");
if (liveResponse.StatusCode != HttpStatusCode.OK || liveAttempts != 2)
    throw new InvalidOperationException("Native AOT real YARP resilience forwarding failed.");

_ = JsonSerializer.SerializeToUtf8Bytes(validation, GatewayJsonSerializerContext.Default.GatewayValidationResult);
_ = JsonSerializer.SerializeToUtf8Bytes(canonical, GatewayJsonSerializerContext.Default.GatewayCanonicalizationResult);

Console.WriteLine(
    $"HPD.Gateway AOT smoke passed: {read.Configuration!.Routes.Length} route(s), " +
    $"{read.Configuration.Upstreams.Length} upstream(s), sha256={canonical.Document.ContentHash.Value}.");

static string Address(WebApplication application) => application.Services
    .GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();

file sealed class SmokeInspector : IGatewayRequestInspector
{
    internal string? SpillPath { get; private set; }
    internal bool SpillExistedDuringInspection { get; private set; }

    public ValueTask<GatewayInspectionDecision> InspectAsync(GatewayInspectionContext context, CancellationToken cancellationToken)
    {
        if (context.Body is FileBufferingReadStream buffering && buffering.TempFileName is { } path)
        {
            SpillPath = path;
            SpillExistedDuringInspection = File.Exists(path);
        }
        return ValueTask.FromResult(GatewayInspectionDecision.Allow());
    }
}

file sealed class NonSeekableReadStream(byte[] bytes) : MemoryStream(bytes)
{
    public override bool CanSeek => false;
    public override long Length => throw new NotSupportedException();
}

file sealed class AotRetryHandler : HttpMessageHandler
{
    internal int Attempts { get; private set; }
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Attempts++;
        return Task.FromResult(new HttpResponseMessage(Attempts == 1 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK)
        {
            RequestMessage = request
        });
    }
}
