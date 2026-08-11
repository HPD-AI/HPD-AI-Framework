using System.Collections.Immutable;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Threading.RateLimiting;
using HPD.Base;
using HPD.Gateway;
using HPD.Gateway.ControlPlane;
using HPD.Gateway.ControlPlane.Sqlite;
using HPD.Gateway.Discovery.Microsoft;
using HPD.Gateway.Admission.Redis;
using HPD.Gateway.ControlPlane.HPDAuth;
using HPD.Auth.ControlPlane;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Yarp.ReverseProxy;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Model;

await SmokeManagementRuntimeAsync();

var redisAdmission = new GatewayTrafficAdmissionRegistryBuilder();
redisAdmission.UseRedis("aot-redis", options =>
{
    options.AuthorityId = "aot-deployment";
    options.Configuration = "127.0.0.1:1,abortConnect=false,connectTimeout=10";
});
redisAdmission.AddSharedFixedWindow("aot-redis-rate", "aot-redis");
using (GatewayTrafficAdmissionRegistry redisRegistry = redisAdmission.Build())
    if (redisRegistry.Capabilities.Length != 1 || redisRegistry.Capabilities[0].Name != "aot-redis-rate")
        throw new InvalidOperationException("Redis admission AOT composition failed.");
if (Environment.GetEnvironmentVariable("HPD_GATEWAY_REDIS_AOT") is { } redisEndpoint)
    await SmokeRedisAdmissionAsync(redisEndpoint);

var authAdapterServices = new ServiceCollection();
authAdapterServices.AddLogging();
authAdapterServices.AddHpdGateway(static gateway => gateway.EnableCoreDeclarations());
authAdapterServices.AddHPDControlPlane(options =>
{
    options.AddProfile("aot-admin", profile =>
    {
        profile.AuthenticationScheme = "aot-auth";
        profile.AuthenticationProfile = "aot-admin";
        profile.ActorIdentifierClaim = "sub";
        profile.RateLimitPolicy = "aot-rate";
        profile.RequestTimeoutPolicy = "aot-timeout";
        profile.OpenApiSecurityScheme = "Bearer";
    });
    foreach (string capability in GatewayAdminCapabilities.All)
        options.MapCapability(capability, "aot-policy");
});
authAdapterServices.AddHpdGatewayControlPlane(controlPlane => controlPlane
    .UseProcessLocalAuthority()
    .AddAdminApi(options =>
    {
        options.AuthenticationScheme = "aot-auth";
        options.RateLimitPolicy = "aot-rate";
        options.RequestTimeoutPolicy = "aot-timeout";
        options.OpenApiSecurityScheme = "Bearer";
        options.CapabilityPolicies = GatewayAdminCapabilities.All.ToImmutableDictionary(
            static capability => capability, static _ => "aot-policy", StringComparer.Ordinal);
    })
    .AddHpdAuth("aot-admin"));
await using (ServiceProvider authAdapterProvider = authAdapterServices.BuildServiceProvider())
    _ = authAdapterProvider.GetRequiredService<IGatewayAdminActorProjector>();

var projectedActor = new AuthenticatedActorProjection
{
    ActorId = "aot-operator",
    AuthenticationProfile = "control-plane"
}.ToGatewayActor("GatewayOperators");
if (projectedActor.ActorId != "aot-operator")
    throw new InvalidOperationException("HPD.Auth Gateway actor projection failed.");

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
            new DeclarationDefinition<TrafficAdmissionPlan>
            {
                Id = new DefinitionId("admission"),
                Specification = new TrafficAdmissionPlan { Entries = [new FixedWindowAdmissionEntry { Profile = "gateway-admission", PermitLimit = 100, Window = TimeSpan.FromMinutes(1) }] }
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
                Specification = new OutputCacheBinding("gateway-cache")
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
        ],
        CredentialDisposition =
        [
            new DeclarationDefinition<CredentialDispositionBinding>
            {
                Id = new DefinitionId("strip-credentials"),
                Specification = new CredentialDispositionBinding { Kind = CredentialDispositionKind.Strip }
            }
        ]
    },
    RootDefaults = new GatewayRootDeclarations
    {
        Cors = new DeclarationReference<CorsPolicyBinding> { Definition = new DefinitionId("cors") },
        TrafficAdmission = new DeclarationReference<TrafficAdmissionPlan> { Definition = new DefinitionId("admission") },
        RequestTimeout = new DeclarationReference<RequestTimeoutBinding> { Definition = new DefinitionId("timeout") },
        Telemetry = new DeclarationReference<TelemetryEnrichment> { Definition = new DefinitionId("telemetry") },
        CredentialDisposition = new DeclarationReference<CredentialDispositionBinding> { Definition = new DefinitionId("strip-credentials") }
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
            Endpoints = new ServiceDiscoveryEndpointSource
            {
                Profile = new DiscoveryProfileId("dns"),
                Service = new ServiceDiscoveryName("orders"),
                Schemes = [ServiceDiscoveryScheme.Https],
                StaleBehavior = DiscoveryStaleBehavior.RejectActivationUntilFresh
            },
            Transport = new UpstreamTransportDeclaration
            {
                Tls = new UpstreamTlsDeclaration { ServerName = "orders" }
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

var portableConfiguration = GatewayConfigurationCanonicalizer.TryCanonicalize(configuration);
if (!portableConfiguration.IsCanonicalized)
    throw new InvalidOperationException($"Portable AOT candidate was rejected: {string.Join(", ", portableConfiguration.Errors.Select(static item => $"{item.Code}@{item.Path}"))}");
var json = portableConfiguration.Document!.Utf8Json.ToArray();
var capabilities = HostCapabilitySnapshot.Create(new HostCapabilityRegistration
{
    InstalledFamilies = GatewayDeclarationFamilies.All,
    AuthorizationPolicies = ["GatewayUsers"],
    CorsPolicies = ["GatewayCors"],
    TrafficAdmissionProfiles = [new TrafficAdmissionCapability("gateway-admission", 1, TrafficAdmissionScope.ProcessLocal,
        TrafficAdmissionKind.RequestRate, TrafficAdmissionRateAlgorithm.FixedWindow, TrafficAdmissionPartitionKind.Global,
        TrafficAdmissionFailureDisposition.Reject,
        new TrafficAdmissionLimits(1, 100_000_000, TimeSpan.FromSeconds(1), TimeSpan.FromDays(1), 0, 0, 0, 0),
        "hpd.gateway/process-local", new ContentHash("sha-256", new string('a', 64)), null)],
    OutputCacheProfiles =
    [
        new OutputCacheCapability(
            "gateway-cache",
            1,
            true,
            "memory",
            OutputCacheStoreScope.ProcessLocal,
            TimeSpan.FromMinutes(1),
            1_048_576,
            16_777_216,
            [],
            [])
    ],
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
    DiscoveryProfiles = [new DiscoveryProfileCapability(
        new DiscoveryProfileId("dns"), 1, DiscoveryRuntimeKind.Microsoft,
        [DiscoveryProviderKind.Configuration], [ServiceDiscoveryScheme.Https],
        [DiscoveryStaleBehavior.RejectActivationUntilFresh], 256, true, true, true, true,
        new ContentHash("sha-256", new string('a', 64)))],
    SecretProviders = [new ProviderId("secrets")]
});
var read = GatewayCandidateReader.Read(json, capabilities);
if (!read.IsAccepted)
{
    throw new InvalidOperationException($"Strict native candidate reading failed with {read.Errors.Length} error(s).");
}

var hostCapabilityProjection = GatewayHostCapabilityProjector.Project(capabilities);
if (hostCapabilityProjection.SnapshotAlgorithm != "sha-256" ||
    hostCapabilityProjection.SnapshotValue.Length != 64 ||
    hostCapabilityProjection.Capabilities.Listeners.Length != 1 ||
    hostCapabilityProjection.Capabilities.UpstreamResilienceProfiles.Length != 1)
    throw new InvalidOperationException("Native AOT host-capability projection was incomplete.");
_ = JsonSerializer.SerializeToUtf8Bytes(
    hostCapabilityProjection,
    GatewayAdminJsonContext.Default.GatewayHostCapabilitySnapshotResponse);

var adminValidationEvidence = new GatewayValidationResponse(
    true,
    [],
    "1.0",
    "1",
    read.CanonicalDocument!.ContentHash.Algorithm,
    read.CanonicalDocument.ContentHash.Value,
    hostCapabilityProjection.SnapshotAlgorithm,
    hostCapabilityProjection.SnapshotValue,
    "aot-correlation",
    DateTimeOffset.UtcNow);
_ = JsonSerializer.SerializeToUtf8Bytes(
    adminValidationEvidence,
    GatewayAdminJsonContext.Default.GatewayValidationResponse);

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
        Inspection = configuration.Definitions.Inspection,
        CredentialDisposition = configuration.Definitions.CredentialDisposition
    },
    RootDefaults = new GatewayRootDeclarations
    {
        Cors = configuration.RootDefaults!.Cors,
        TrafficAdmission = configuration.RootDefaults.TrafficAdmission,
        RequestTimeout = configuration.RootDefaults.RequestTimeout,
        CredentialDisposition = configuration.RootDefaults.CredentialDisposition
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
                RequestTransforms = supportedRouteDeclarations.RequestTransforms,
                ResponseTransforms = supportedRouteDeclarations.ResponseTransforms
            }
        },
        configuration.Routes[0] with
        {
            Id = new RouteId("inspection-route"),
            Listener = null,
            Match = new HttpRouteMatch { Path = "/inspect/{**catch-all}", Methods = ["GET"] },
            Declarations = new RouteDeclarations
            {
                Inspection = new DeclarationReference<RequestInspectionBinding> { Definition = new DefinitionId("inspection") }
            }
        }
    ]
};
var materializableJson = JsonSerializer.SerializeToUtf8Bytes(materializableConfiguration, GatewayJsonSerializerContext.Default.GatewayConfiguration);
var materializableRead = GatewayCandidateReader.Read(materializableJson, capabilities);
if (!materializableRead.IsAccepted) throw new InvalidOperationException($"Materializable AOT candidate was rejected: {string.Join(", ", materializableRead.Errors.Select(static item => $"{item.Code}@{item.Path}"))}");

var services = new ServiceCollection();
services.AddLogging();
services.AddAuthorizationBuilder().AddPolicy("GatewayUsers", policy => policy.RequireAssertion(_ => true));
services.AddCors(options => options.AddPolicy("GatewayCors", policy => policy.AllowAnyOrigin()));
services.AddHpdGatewayOutputCaching(builder => builder.Add(new GatewayOutputCacheProfile
{
    Name = "gateway-cache",
    Version = 1,
    Expiration = TimeSpan.FromMinutes(1)
}));
services.AddReverseProxy();
services.AddHpdGatewayYarpPublication();
services.AddHpdGatewayYarpResilience(registry => registry.Add(smokeResilienceProfile));
services.AddHpdGatewayYarpMaterialization();
var smokeInspector = new SmokeInspector();
services.AddHpdGatewayYarpInspection(registry => registry.Add("smoke-inspector", smokeInspector));
await using var serviceProvider = services.BuildServiceProvider();
var planner = serviceProvider.GetRequiredService<GatewayRuntimePlanner>();
var planned = await planner.PlanAsync(
    materializableRead,
    new PublicationCandidateIdentity(
        new CandidateId("aot-smoke"),
        "aot-authority",
        "epoch-1",
        1,
        materializableRead.CanonicalDocument!.ContentHash),
    "native-aot-smoke");
if (!planned.IsPlanned)
    throw new InvalidOperationException($"Native AOT planning failed: {string.Join(", ", planned.Diagnostics.Select(item => $"{item.Code}@{item.Path}"))}");
if (planned.PreparedProjectionSnapshot is null || planned.PreparedProjectionSnapshot.Records.IsEmpty)
    throw new InvalidOperationException("Native AOT effective provenance was not produced.");
var effectiveFamilies = planned.PreparedProjectionSnapshot.Records.Select(static item => item.Family).ToHashSet(StringComparer.Ordinal);
foreach (var requiredFamily in new[]
{
    "hpd.gateway/authorization",
    "hpd.gateway/cors",
    "hpd.gateway/traffic-admission",
    "hpd.gateway/request-timeout",
    "hpd.gateway/output-cache",
    "hpd.gateway/inspection",
    "hpd.gateway/credential-disposition",
    "hpd.gateway/request-header-transforms",
    "hpd.gateway/response-header-transforms",
    "hpd.gateway/response-trailer-transforms"
})
{
    if (!effectiveFamilies.Contains(requiredFamily))
        throw new InvalidOperationException($"Native AOT effective provenance omitted {requiredFamily}.");
}
foreach (var correlatedFamily in new[]
{
    "hpd.gateway/output-cache",
    "hpd.gateway/inspection",
    "hpd.gateway/credential-disposition"
})
{
    if (!planned.PreparedProjectionSnapshot.Records.Where(item => item.Family == correlatedFamily)
        .All(static record => record.Contributions.Any(static item => item.SourceKind == HPD.Gateway.GatewayContributionSourceKind.HostProfile)))
        throw new InvalidOperationException($"Native AOT effective provenance omitted host correlation for {correlatedFamily}.");
}
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

var publicationBundle = planned.PreparedApplication!;
var proxyProvider = serviceProvider.GetRequiredService<HpdProxyConfigProvider>();
var changeListener = serviceProvider.GetRequiredService<HpdConfigChangeListener>();
var publisher = serviceProvider.GetRequiredService<GatewayRuntimePublisher>();
var publication = publisher.PublishAsync(publicationBundle, TimeSpan.FromSeconds(5));
IProxyConfig? publishedSnapshot = null;
for (var index = 0; index < 1_000; index++)
{
    var current = proxyProvider.GetConfig();
    if (current is OwnedProxyConfig owned && owned.NativeRevisionId == publicationBundle.NativeRevisionId)
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
var liveCredentialLeak = false;
var liveUpstreamBuilder = WebApplication.CreateSlimBuilder();
liveUpstreamBuilder.WebHost.UseUrls("http://127.0.0.1:0");
await using var liveUpstream = liveUpstreamBuilder.Build();
liveUpstream.Run(context =>
{
    liveCredentialLeak |= context.Request.Headers.ContainsKey("Authorization") ||
        context.Request.Headers.ContainsKey("Cookie") ||
        context.Request.Headers.ContainsKey("X-Aot-Key");
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
liveProxyBuilder.Services.AddHpdGatewayStatus();
liveProxyBuilder.Services.AddHpdGatewayOutputCaching(builder =>
{
    builder.MaximumBodyBytes = 1_024;
    builder.StoreCapacityBytes = 65_536;
    builder.Add(new GatewayOutputCacheProfile { Name = "aot-cache", Version = 1, Expiration = TimeSpan.FromMinutes(1) });
});
await using var liveProxy = liveProxyBuilder.Build();
liveProxy.UseHpdGatewayOutputCaching();
liveProxy.MapHpdGatewayHealth();
liveProxy.MapHpdGatewayReverseProxy();
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
            Declarations = new RouteDeclarations
            {
                OutputCache = new DeclarationReference<OutputCacheBinding>
                {
                    Inline = new OutputCacheBinding("aot-cache")
                },
                CredentialDisposition = new DeclarationReference<CredentialDispositionBinding>
                {
                    Inline = new CredentialDispositionBinding { Kind = CredentialDispositionKind.Strip }
                }
            }
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
    InstalledFamilies = GatewayDeclarationFamilies.UpstreamResilience | GatewayDeclarationFamilies.CredentialDisposition | GatewayDeclarationFamilies.OutputCache,
    ProtectedCredentialHeaders = ["X-Aot-Key"],
    OutputCacheProfiles = liveProxy.Services.GetHpdGatewayOutputCacheCapabilities(),
    UpstreamResilienceProfiles = liveProxy.Services.GetHpdGatewayResilienceCapabilities()
});
var liveJson = JsonSerializer.SerializeToUtf8Bytes(liveConfiguration, GatewayJsonSerializerContext.Default.GatewayConfiguration);
var liveAccepted = GatewayCandidateReader.Read(liveJson, liveCapabilities);
if (!liveAccepted.IsAccepted) throw new InvalidOperationException("Native AOT live resilience candidate was rejected.");
var liveIdentity = new PublicationCandidateIdentity(new CandidateId("aot-live"), "aot-live-authority", "epoch-1", 1, liveAccepted.CanonicalDocument!.ContentHash);
var livePlanned = await liveProxy.Services.GetRequiredService<GatewayRuntimePlanner>()
    .PlanAsync(liveAccepted, liveIdentity, "aot-live-native");
if (!livePlanned.IsPlanned) throw new InvalidOperationException("Native AOT live resilience planning failed.");
var livePublication = await liveProxy.Services.GetRequiredService<GatewayRuntimePublisher>()
    .PublishAsync(livePlanned.PreparedApplication!, "aot", "live", TimeSpan.FromSeconds(5));
if (livePublication.State != GatewayPublicationState.ActiveAcknowledged)
    throw new InvalidOperationException("Native AOT live resilience publication was not acknowledged.");
using var liveClient = new HttpClient { BaseAddress = new Uri(Address(liveProxy)) };
using var readyResponse = await liveClient.GetAsync("/health/ready");
if (readyResponse.StatusCode != HttpStatusCode.OK)
    throw new InvalidOperationException("Native AOT readiness did not become ready after exact publication.");
var statusSnapshot = liveProxy.Services.GetRequiredService<IGatewayStatusReader>().GetCurrent();
_ = JsonSerializer.SerializeToUtf8Bytes(statusSnapshot, GatewayStatusJsonContext.Default.GatewayStatusSnapshot);
if (statusSnapshot.Readiness.Serving != GatewayReadinessState.Ready || statusSnapshot.Conditions.Length != 7)
    throw new InvalidOperationException("Native AOT status snapshot was not ready or complete.");
using var liveRequest = new HttpRequestMessage(HttpMethod.Get, "/retry");
liveRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "native-aot-secret");
liveRequest.Headers.TryAddWithoutValidation("Cookie", "session=native-aot-secret");
liveRequest.Headers.TryAddWithoutValidation("X-Aot-Key", "native-aot-secret");
using var liveResponse = await liveClient.SendAsync(liveRequest);
if (liveResponse.StatusCode != HttpStatusCode.OK || liveAttempts != 2 || liveCredentialLeak)
    throw new InvalidOperationException("Native AOT real YARP resilience and credential stripping forwarding failed.");
using var cachedResponse = await liveClient.GetAsync("/retry");
using var cachedHit = await liveClient.GetAsync("/retry");
if (cachedResponse.StatusCode != HttpStatusCode.OK || cachedHit.StatusCode != HttpStatusCode.OK || liveAttempts != 3)
    throw new InvalidOperationException("Native AOT real Output Cache hit did not suppress upstream forwarding.");

var uncachedConfiguration = liveConfiguration with
{
    Routes =
    [
        liveConfiguration.Routes[0] with
        {
            Declarations = new RouteDeclarations
            {
                CredentialDisposition = liveConfiguration.Routes[0].Declarations!.CredentialDisposition
            }
        }
    ]
};
var uncachedJson = JsonSerializer.SerializeToUtf8Bytes(uncachedConfiguration, GatewayJsonSerializerContext.Default.GatewayConfiguration);
var uncachedAccepted = GatewayCandidateReader.Read(uncachedJson, liveCapabilities);
if (!uncachedAccepted.IsAccepted) throw new InvalidOperationException("Native AOT uncached replacement was rejected.");
var uncachedPlanned = await liveProxy.Services.GetRequiredService<GatewayRuntimePlanner>().PlanAsync(
    uncachedAccepted,
    new PublicationCandidateIdentity(new CandidateId("aot-live-2"), "aot-live-authority", "epoch-1", 2, uncachedAccepted.CanonicalDocument!.ContentHash),
    "aot-live-native-2");
if (!uncachedPlanned.IsPlanned ||
    (await liveProxy.Services.GetRequiredService<GatewayRuntimePublisher>().PublishAsync(uncachedPlanned.PreparedApplication!, "aot", "live", TimeSpan.FromSeconds(5))).State != GatewayPublicationState.ActiveAcknowledged)
    throw new InvalidOperationException("Native AOT Output Cache removal was not acknowledged.");
using var uncachedResponse = await liveClient.GetAsync("/retry");
if (uncachedResponse.StatusCode != HttpStatusCode.OK || liveAttempts != 4)
    throw new InvalidOperationException("Native AOT Output Cache removal did not restore forwarding.");

var readdedPlanned = await liveProxy.Services.GetRequiredService<GatewayRuntimePlanner>().PlanAsync(
    liveAccepted,
    new PublicationCandidateIdentity(new CandidateId("aot-live-3"), "aot-live-authority", "epoch-1", 3, liveAccepted.CanonicalDocument!.ContentHash),
    "aot-live-native-3");
if (!readdedPlanned.IsPlanned ||
    (await liveProxy.Services.GetRequiredService<GatewayRuntimePublisher>().PublishAsync(readdedPlanned.PreparedApplication!, "aot", "live", TimeSpan.FromSeconds(5))).State != GatewayPublicationState.ActiveAcknowledged)
    throw new InvalidOperationException("Native AOT Output Cache re-add was not acknowledged.");
using var readdedResponse = await liveClient.GetAsync("/retry");
if (readdedResponse.StatusCode != HttpStatusCode.OK || liveAttempts != 4)
    throw new InvalidOperationException("Native AOT Output Cache re-add did not restore the store-owned cached entry.");

var proxyLookup = liveProxy.Services.GetRequiredService<IProxyStateLookup>();
if (!proxyLookup.TryGetCluster("live", out var liveCluster))
    throw new InvalidOperationException("Native AOT status could not observe the active Cluster.");
var priorDestinations = liveCluster.DestinationsState;
liveCluster.DestinationsState = new ClusterDestinationsState(priorDestinations.AllDestinations, []);
using var notReadyResponse = await liveClient.GetAsync("/health/ready");
if (notReadyResponse.StatusCode != HttpStatusCode.ServiceUnavailable)
    throw new InvalidOperationException("Native AOT readiness ignored zero native eligible destinations.");
liveCluster.DestinationsState = priorDestinations;

var tlsDirectory = Directory.CreateTempSubdirectory("hpd-gateway-aot-sni-");
try
{
    var exactCertificate = CreateAotCertificate("aot.example", Path.Combine(tlsDirectory.FullName, "exact.pfx"));
    var wildcardCertificate = CreateAotCertificate("*.example", Path.Combine(tlsDirectory.FullName, "wildcard.pfx"));
    var tlsPort = AvailableAotPort();
    var tlsConfiguration = new GatewayHostConfiguration
    {
        SchemaVersion = new(1, 0),
        CanonicalizationVersion = 1,
        HostId = new("aot-host"),
        DataListeners =
        [
            new GatewayHttpsListenerDeclaration
            {
                Id = new ListenerId("aot-https"),
                Binding = GatewayListenerBindingKind.Loopback,
                Port = checked((ushort)tlsPort),
                Protocols = GatewayListenerProtocols.Http1 | GatewayListenerProtocols.Http2,
                Tls = new GatewayInboundTlsDeclaration
                {
                    Sni =
                    [
                        new GatewaySniTlsDeclaration { HostnamePattern = "aot.example", Certificate = new(new ProviderId("aot"), new ProviderObjectId("exact"), "v1") },
                        new GatewaySniTlsDeclaration { HostnamePattern = "*.example", Certificate = new(new ProviderId("aot"), new ProviderObjectId("wildcard"), "v1") }
                    ]
                }
            }
        ]
    };
    var tlsAccepted = GatewayHostCandidateReader.Create(tlsConfiguration);
    if (!tlsAccepted.IsAccepted) throw new InvalidOperationException("Native AOT host candidate was rejected.");
    var tlsExecutions = 0;
    var tlsBuilder = WebApplication.CreateSlimBuilder();
    tlsBuilder.WebHost.UseHpdGatewayHost(tlsAccepted.Candidate!, certificates =>
    {
        certificates.Add(new(new ProviderId("aot"), new ProviderObjectId("exact"), "v1"), new GatewayPfxCertificateSource { Path = exactCertificate.Path, Password = exactCertificate.Password });
        certificates.Add(new(new ProviderId("aot"), new ProviderObjectId("wildcard"), "v1"), new GatewayPfxCertificateSource { Path = wildcardCertificate.Path, Password = wildcardCertificate.Password });
    });
    GatewayHostRuntimeStatus? tlsStatus = null;
    await using (var tlsHost = tlsBuilder.Build())
    {
        tlsHost.Run(context => { Interlocked.Increment(ref tlsExecutions); return context.Response.WriteAsync("aot-tls"); });
        await tlsHost.StartHpdGatewayAsync();
        if (await SendAotTls(tlsPort, "aot.example") != exactCertificate.Thumbprint ||
            await SendAotTls(tlsPort, "other.example") != wildcardCertificate.Thumbprint)
            throw new InvalidOperationException("Native AOT Kestrel SNI certificate selection failed.");
        try { _ = await SendAotTls(tlsPort, "unknown.test"); throw new InvalidOperationException("Unknown SNI unexpectedly succeeded."); }
        catch (HttpRequestException) { }
        try { await SendAotTlsWithoutSni(tlsPort); throw new InvalidOperationException("Missing SNI unexpectedly succeeded."); }
        catch (IOException) { }
        if (tlsExecutions != 2) throw new InvalidOperationException("Rejected Native AOT TLS handshakes reached HTTP execution.");
        tlsStatus = tlsHost.Services.GetRequiredService<GatewayHostRuntimeStatus>();
        if (tlsStatus.GetSnapshot().State != GatewayHostRealizationState.Ready)
            throw new InvalidOperationException("Native AOT host did not reach Ready.");
        var changedConfiguration = tlsConfiguration with
        {
            DataListeners = [tlsConfiguration.DataListeners[0] with { Port = checked((ushort)AvailableAotPort()) }]
        };
        var changedCandidate = GatewayHostCandidateReader.Create(changedConfiguration);
        if (!changedCandidate.IsAccepted || tlsStatus.EvaluateDesired(changedCandidate.Candidate!).State != GatewayHostRealizationState.RestartRequired)
            throw new InvalidOperationException("Native AOT host did not report RestartRequired for changed host identity.");
        if (tlsStatus.GetSnapshot().State != GatewayHostRealizationState.RestartRequired)
            throw new InvalidOperationException("Native AOT host did not persist RestartRequired.");
        await tlsHost.StopHpdGatewayAsync();
    }
    if (tlsStatus?.GetSnapshot().State != GatewayHostRealizationState.Stopped)
        throw new InvalidOperationException("Native AOT host did not report Stopped after disposal.");
}
finally
{
    tlsDirectory.Delete(recursive: true);
}

var shutdownStatus = liveProxy.Services.GetRequiredService<IGatewayStatusReader>();
if (shutdownStatus.GetCurrent().Readiness.Serving != GatewayReadinessState.Ready)
    throw new InvalidOperationException("Native AOT readiness did not recover before shutdown.");
var shutdownSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
using (shutdownStatus.GetChangeToken().RegisterChangeCallback(
    static state => ((TaskCompletionSource)state!).TrySetResult(), shutdownSignal))
{
    await liveProxy.StopAsync();
    await shutdownSignal.Task.WaitAsync(TimeSpan.FromSeconds(5));
}
if (shutdownStatus.GetCurrent().Readiness.Serving != GatewayReadinessState.NotReady)
    throw new InvalidOperationException("Native AOT shutdown left readiness published as ready.");

_ = JsonSerializer.SerializeToUtf8Bytes(validation, GatewayJsonSerializerContext.Default.GatewayValidationResult);
_ = JsonSerializer.SerializeToUtf8Bytes(canonical, GatewayJsonSerializerContext.Default.GatewayCanonicalizationResult);

Console.WriteLine(
    $"HPD.Gateway AOT smoke passed: {read.Configuration!.Routes.Length} route(s), " +
    $"{read.Configuration.Upstreams.Length} upstream(s), sha256={canonical.Document.ContentHash.Value}.");

static string Address(WebApplication application) => application.Services
    .GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();

static async Task SmokeManagementRuntimeAsync()
{
    _ = GatewayAcceptedRevision.Collection.Definition;
    _ = GatewayValidationRecord.Collection.Definition;
    _ = GatewayAdministrativeAuditRecord.Collection.Definition;
    _ = GatewayTargetOwnership.Collection.Definition;
    _ = GatewayTargetEpochReservation.Collection.Definition;
    _ = GatewayTargetEpochReservationReceipt.Collection.Definition;
    _ = GatewayDesiredState.Collection.Definition;
    _ = GatewayNodeDeliveryAuthorityState.Collection.Definition;
    _ = GatewayActivationIntent.Collection.Definition;
    _ = GatewayDeliveryOutboxItem.Collection.Definition;
    _ = GatewayNodeActivationOutcome.Collection.Definition;
    _ = GatewayCommandReceipt.Collection.Definition;
    _ = GatewayAdministrativeOperationIntent.Collection.Definition;
    _ = GatewayAdministrativeOperationObservation.Collection.Definition;
    _ = GatewayAdministrativeOperationCompletion.Collection.Definition;
    _ = GatewayPurgeAuthorityState.Collection.Definition;

    object[] records =
    [
        new GatewayAcceptedRevision { NamespaceId = "ns", TargetNodeId = "node", ContentHashAlgorithm = "sha-256", ContentHashValue = "hash", CanonicalConfigurationUtf8 = [1], SchemaVersion = "1.0", CanonicalizationVersion = "1", ValidationId = "validation", ActorId = "actor", SourceKind = "code", SourceId = "source", CorrelationId = "correlation" },
        new GatewayValidationRecord { NamespaceId = "ns", TargetNodeId = "node", Outcome = GatewayValidationOutcome.Valid, ContentHashValue = "hash", DiagnosticsJson = [], CorrelationId = "correlation" },
        new GatewayAdministrativeAuditRecord { NamespaceId = "ns", ActorId = "actor", AuthenticationScheme = "test", AuthorizationPolicy = "admin", Operation = "submit", ResultCode = "accepted", CorrelationId = "correlation", SubjectId = "revision" },
        new GatewayTargetOwnership { ManagementAuthorityId = "management", TargetNodeId = "node", NamespaceId = "ns" },
        new GatewayTargetEpochReservation { ManagementAuthorityId = "management", TargetNodeId = "node", AuthorityEpoch = "epoch", ContractVersion = "gateway.management.epoch-reservation.v1" },
        new GatewayTargetEpochReservationReceipt { ReservationId = "reservation", EpochDigest = new string('a', 64), StableResultCode = "accepted", ContractVersion = "gateway.management.epoch-reservation.v1" },
        new GatewayDesiredState { ManagementAuthorityId = "management", TargetNodeId = "node", NamespaceId = "ns", ActivationIntentId = "intent", RevisionId = "revision", CandidateId = "candidate" },
        new GatewayNodeDeliveryAuthorityState { ManagementAuthorityId = "management", TargetNodeId = "node", NamespaceId = "ns", AuthorityId = "authority", AuthorityEpoch = "epoch", NextAuthorityVersion = 2 },
        new GatewayActivationIntent { NamespaceId = "ns", TargetNodeId = "node", RevisionId = "revision", CandidateId = "candidate", ContentHashValue = "hash", AuthorityId = "authority", AuthorityEpoch = "epoch", AuthorityVersion = 1 },
        new GatewayDeliveryOutboxItem { NamespaceId = "ns", TargetNodeId = "node", ActivationIntentId = "intent", State = GatewayDeliveryState.Immediate, AttemptCount = 0 },
        new GatewayNodeActivationOutcome { NamespaceId = "ns", TargetNodeId = "node", ActivationIntentId = "intent", AuthorityId = "authority", AuthorityEpoch = "epoch", AuthorityVersion = 1, Kind = GatewayNodeOutcomeKind.ActiveAcknowledged, Code = "active" },
        new GatewayCommandReceipt { NamespaceId = "ns", TargetNodeId = "node", Operation = "submit", IdempotencyKey = "key", Fingerprint = new byte[32], StableResultCode = "accepted", StableOperationId = "revision" },
        new GatewayAdministrativeOperationIntent { NamespaceId = "ns", Operation = GatewayAdministrativeOperationKind.Backup, ActorId = "actor", AuthenticationScheme = "test", AuthorizationPolicy = "admin", SubjectDigest = "digest" },
        new GatewayAdministrativeExecutionState { IntentId = "admin", Phase = GatewayAdministrativeExecutionPhase.BoundaryCrossed, StateRevision = 2, ClaimId = "claim", BoundaryCrossedAt = DateTimeOffset.UnixEpoch },
        new GatewayAdministrativeArtifactObservation { IntentId = "admin", SinkName = "archive", PublicReference = "artifact:admin", ObservedAt = DateTimeOffset.UnixEpoch },
        new GatewayAdministrativeOperationObservation { IntentId = "admin", Kind = GatewayAdministrativeObservationKind.Succeeded, ResultCode = "created", ResultJson = [] },
        new GatewayAdministrativeOperationCompletion { IntentId = "admin", ObservationId = "observation", State = GatewayAdministrativeCompletionState.Completed },
        new GatewayPurgeAuthorityState { ManagementAuthorityId = "management", CollectionId = GatewayAuthoritySchema.AdministrativeAudit, ConfirmedGeneration = 0 },
    ];

    foreach (object record in records)
    {
        var typeInfo = GatewayManagementJsonContext.Default.GetTypeInfo(record.GetType())
            ?? throw new InvalidOperationException("Management JSON metadata is unavailable.");
        if (JsonSerializer.SerializeToUtf8Bytes(record, typeInfo).Length == 0)
            throw new InvalidOperationException("Management record serialization failed.");
    }

    object[] adminDtos =
    [
        new GatewayRevisionRequest { ConfigurationJson = "{}", SourceKind = "code", SourceId = "source" },
        new GatewayActivationRequest("description"),
        new GatewayCompareRequest("left", "right"),
        new GatewayImportRequest("{}", "artifact"),
        new GatewayBackupRequest("sink", "artifact"),
        new GatewayPurgeRequest(GatewayPurgeCategory.AuditHistory, ["audit"]),
        new GatewayOperationResponse("operation", "accepted", "code"),
        new GatewayCommandOperationProjection { OperationId = "operation", Operation = "submit", ResultCode = "accepted", DesiredStateToken = "token", AcceptedAt = DateTimeOffset.UnixEpoch },
        new GatewayAdministrativeOperationProjection { OperationId = "administration", Operation = GatewayAdministrativeOperationKind.Backup, State = GatewayAdministrativeOperationReadState.IndeterminatePending, Code = "pending", ArtifactReference = "artifact:admin", ObservedAt = null },
        new GatewayExportResponse("v1", "revision", "sha-256", "hash", "{}"),
        new GatewayAdministrativeResponse("operation", GatewayAdministrativeCompletionState.Completed, "completed"),
    ];
    foreach (object dto in adminDtos)
    {
        var typeInfo = GatewayAdminJsonContext.Default.GetTypeInfo(dto.GetType())
            ?? throw new InvalidOperationException("Admin JSON metadata is unavailable.");
        if (JsonSerializer.SerializeToUtf8Bytes(dto, typeInfo).Length == 0)
            throw new InvalidOperationException("Admin DTO serialization failed.");
    }

    var services = new ServiceCollection();
    services.AddLogging();
    var discoveryConfiguration = new ConfigurationManager();
    discoveryConfiguration.AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["Services:aot-backend:http:0"] = "http://127.0.0.1:5080",
    });
    services.AddSingleton<IConfiguration>(discoveryConfiguration);
    services.AddHpdGateway(static gateway =>
    {
        gateway.EnableCoreDeclarations();
        gateway.AddTrafficAdmission(admission => admission
            .AddPartitionProjector("aot-subject", new ContentHash("sha-256", new string('b', 64)), new AotAdmissionProjector())
            .AddLocalFixedWindow("aot-rate", options =>
            {
                options.Partition = TrafficAdmissionPartitionKind.AuthenticatedSubject;
                options.PartitionProjector = "aot-subject";
            })
            .AddLocalSlidingWindow("aot-sliding")
            .AddLocalTokenBucket("aot-token")
            .AddLocalConcurrency("aot-concurrency")
            .AddSharedProvider("aot-shared", new AotSharedAdmissionProvider(), options =>
            {
                options.AuthorityId = "aot-deployment";
                options.BehaviorIdentity = new ContentHash("sha-256", new string('c', 64));
                options.OperationTimeout = TimeSpan.FromSeconds(1);
                options.MaximumConcurrentInvocations = 4;
            })
            .AddSharedFixedWindow("aot-shared-rate", "aot-shared"));
        gateway.AddMicrosoftDiscovery("aot-discovery", profile =>
        {
            profile.Schemes = [ServiceDiscoveryScheme.Http];
            profile.AddConfiguration();
        });
    });
    services.AddHpdGatewayControlPlane(controlPlane => controlPlane
        .UseProcessLocalAuthority(options => options.ManagementAuthorityId = "aot-authority"));
    await using ServiceProvider provider = services.BuildServiceProvider();
    TrafficAdmissionPlan sharedPlan = new()
    {
        Entries = [new FixedWindowAdmissionEntry { Profile = "aot-shared-rate", PermitLimit = 10, Window = TimeSpan.FromSeconds(1) }]
    };
    var sharedContext = new DefaultHttpContext();
    sharedContext.SetEndpoint(new Endpoint(null, new EndpointMetadataCollection(GatewayTrafficAdmissionMetadata.Create(
        new string('a', 32), new ContentHash("sha-256", new string('b', 64)), new RouteId("aot-route"),
        GatewayRuntimePlanner.HashTrafficAdmission(sharedPlan), sharedPlan)), "aot-shared"));
    using RateLimitLease sharedLease = await new GatewayTrafficAdmissionLimiter(
        provider.GetRequiredService<GatewayTrafficAdmissionRegistry>()).AcquireAsync(sharedContext);
    if (!sharedLease.IsAcquired)
        throw new InvalidOperationException("AOT shared-admission execution failed.");
    GatewayDiscoveryResult discovery = await provider.GetRequiredService<IGatewayDiscoveryRuntimeProfile>()
        .ResolveAsync(new GatewayDiscoveryRequest(
            new DiscoveryProfileId("aot-discovery"), new ServiceDiscoveryName("aot-backend"), null,
            [ServiceDiscoveryScheme.Http], null));
    if (discovery.Endpoints.Single() is not GatewayUriDiscoveryEndpoint { Address.Port: 5080 })
        throw new InvalidOperationException("AOT Microsoft configuration discovery failed.");
    GatewayAuthorityCapabilitySnapshot capabilities = await provider
        .GetRequiredService<IGatewayAuthorityRuntime>().InitializeAsync();
    if (capabilities.Durability != GatewayAuthorityDurability.ProcessLocal)
        throw new InvalidOperationException("AOT InMemory authority durability is incorrect.");
    GatewayManagementCommandResult provisioned = await provider
        .GetRequiredService<IGatewayManagementCommandCoordinator>()
        .ProvisionLocalTargetAsync(new GatewayLocalProvisionTargetCommand(
            "aot-ns", "aot-node", "aot-key",
            new GatewayManagementActor("aot-actor", "aot", "manage"),
            "aot-correlation"));
    if (!provisioned.IsAccepted)
        throw new InvalidOperationException("AOT authority provisioning failed: " + provisioned.Code);

    string database = Path.Combine(Path.GetTempPath(), $"hpd-gateway-aot-{Guid.NewGuid():N}.db");
    try
    {
        GatewayProvisionTargetCommand durableCommand = new(
            "aot-durable-ns", "aot-durable-node", "aot-durable-key",
            new GatewayManagementActor("aot-actor", "aot", "manage"),
            "aot-durable-correlation", "aot-durable-epoch");
        await using (ServiceProvider durable = BuildDurable(database))
        {
            await PrepareSqlite(durable);
            GatewayAuthorityCapabilitySnapshot durableCapabilities = await durable
                .GetRequiredService<IGatewayAuthorityRuntime>().InitializeAsync();
            if (durableCapabilities.Durability != GatewayAuthorityDurability.RestartDurable)
                throw new InvalidOperationException("AOT SQLite durability is incorrect.");
            if (!(await durable.GetRequiredService<IGatewayManagementCommandCoordinator>()
                    .ProvisionTargetAsync(durableCommand)).IsAccepted)
                throw new InvalidOperationException("AOT SQLite provisioning failed.");
        }
        await using (ServiceProvider restarted = BuildDurable(database))
        {
            await PrepareSqlite(restarted);
            GatewayManagementCommandResult replay = await restarted
                .GetRequiredService<IGatewayManagementCommandCoordinator>()
                .ProvisionTargetAsync(durableCommand);
            if (replay.State != GatewayManagementCommandState.Duplicate)
                throw new InvalidOperationException("AOT SQLite restart replay failed: " + replay.Code);
        }
    }
    finally
    {
        foreach (string suffix in new[] { "", "-wal", "-shm" })
            if (File.Exists(database + suffix)) File.Delete(database + suffix);
    }
}

static ServiceProvider BuildDurable(string database)
{
    var services = new ServiceCollection();
    services.AddLogging();
    services.AddHpdGateway(static gateway => gateway.EnableCoreDeclarations());
    services.AddHpdGatewayControlPlane(controlPlane => controlPlane.UseSqlite(sqlite =>
    {
        sqlite.PlanProtectionKey = Enumerable.Repeat((byte)0x60, 32).ToArray();
        sqlite.TokenProtectionKey = Enumerable.Repeat((byte)0x63, 32).ToArray();
        sqlite.DesiredStateTokenKey = Enumerable.Repeat((byte)0x61, 32).ToArray();
        sqlite.EpochReservationKey = Enumerable.Repeat((byte)0x62, 32).ToArray();
        sqlite.DataSource = database;
    }));
    return services.BuildServiceProvider();
}

static async Task PrepareSqlite(ServiceProvider provider)
{
    IBaseSchemaManager schemas = provider.GetRequiredService<IBaseSchemaManager>();
    BaseSchemaPlan plan = (await schemas.PlanAsync(new BaseSchemaPlanRequest { StoreId = "gateway-management" })).Value!;
    if (!(await schemas.ApplyAsync(new BaseSchemaApplyRequest { ProtectedArtifact = plan.ProtectedArtifact })).IsSuccess())
        throw new InvalidOperationException("AOT SQLite schema apply failed.");
}

static (string Path, string Password, string Thumbprint) CreateAotCertificate(string dnsName, string path)
{
    using var key = RSA.Create(2048);
    var request = new CertificateRequest($"CN={dnsName}", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
    request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
    request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
    request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.1")], true));
    var san = new SubjectAlternativeNameBuilder();
    san.AddDnsName(dnsName);
    request.CertificateExtensions.Add(san.Build());
    using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(1));
    const string password = "aot-test-password";
    File.WriteAllBytes(path, certificate.Export(X509ContentType.Pfx, password));
    return (path, password, certificate.Thumbprint);
}

static int AvailableAotPort()
{
    using var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    return ((IPEndPoint)listener.LocalEndpoint).Port;
}

static async Task SmokeRedisAdmissionAsync(string endpoint)
{
    GatewayRedisAdmissionSnapshot snapshot = GatewayRedisAdmissionSnapshot.Create("aot-redis", new GatewayRedisAdmissionOptions
    {
        AuthorityId = "aot-deployment",
        Configuration = endpoint,
        KeyPrefix = "hpd:aot:admission",
    });
    using var provider = new GatewayRedisAdmissionProvider(snapshot, null);
    foreach (TrafficAdmissionRateAlgorithm algorithm in Enum.GetValues<TrafficAdmissionRateAlgorithm>())
    {
        var request = new GatewaySharedAdmissionRequest(1, "aot-redis", "aot-deployment",
            $"aot-{algorithm.ToString().ToLowerInvariant()}-{Guid.NewGuid():N}",
            new ContentHash("sha-256", new string('a', 64)), "aot-partition", algorithm,
            1, algorithm == TrafficAdmissionRateAlgorithm.TokenBucket ? 1 : 0, 1_000,
            algorithm == TrafficAdmissionRateAlgorithm.SlidingWindow ? 2 : 0, 1, new string('b', 32));
        GatewaySharedAdmissionDecision acquired = await provider.AcquireAsync(request, default);
        GatewaySharedAdmissionDecision rejected = await provider.AcquireAsync(
            request with { AttemptId = new string('c', 32) }, default);
        GatewaySharedAdmissionRetainedState state = await provider.ObserveStateAsync(request, default);
        if (acquired.Kind != GatewaySharedAdmissionDecisionKind.Acquired ||
            rejected.Kind != GatewaySharedAdmissionDecisionKind.Rejected ||
            !GatewaySharedAdmissionContract.IsValidState(request, state))
            throw new InvalidOperationException($"Redis admission AOT execution failed for {algorithm}.");
    }
}

static async Task<string> SendAotTls(int port, string serverName)
{
    string? thumbprint = null;
    var handler = new SocketsHttpHandler
    {
        ConnectCallback = async (_, cancellationToken) =>
        {
            var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
            await socket.ConnectAsync(IPAddress.Loopback, port, cancellationToken);
            return new NetworkStream(socket, ownsSocket: true);
        },
        SslOptions = new SslClientAuthenticationOptions
        {
            RemoteCertificateValidationCallback = (_, certificate, _, _) => { thumbprint = certificate?.GetCertHashString(); return true; }
        }
    };
    using var client = new HttpClient(handler);
    using var response = await client.GetAsync($"https://{serverName}:{port}/");
    response.EnsureSuccessStatusCode();
    return thumbprint!;
}

static async Task SendAotTlsWithoutSni(int port)
{
    using var client = new TcpClient();
    await client.ConnectAsync(IPAddress.Loopback, port);
    using var stream = new SslStream(client.GetStream(), false, static (_, _, _, _) => true);
    await stream.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = string.Empty });
}

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

file sealed class AotAdmissionProjector : IGatewayAdmissionPartitionProjector
{
    public ValueTask<GatewayAdmissionPartitionResult> ProjectAsync(
        GatewayAdmissionPartitionContext context,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult(GatewayAdmissionPartitionResult.Success("aot-subject"));
}

file sealed class AotSharedAdmissionProvider : IGatewaySharedAdmissionProvider
{
    public ValueTask<GatewaySharedAdmissionDecision> AcquireAsync(
        GatewaySharedAdmissionRequest request,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult(new GatewaySharedAdmissionDecision(
            GatewaySharedAdmissionDecisionKind.Acquired, request.PermitLimit - request.PermitCount,
            null, request.WindowMilliseconds, "aot-observation", null));
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
