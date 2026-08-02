using System.Collections.Immutable;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using FluentAssertions;
using HPD.Gateway.Abstractions;
using HPD.Gateway.Abstractions.Serialization;
using HPD.Gateway.Core;
using HPD.Gateway.Yarp;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Forwarder;

namespace HPD.Gateway.Tests;

public sealed class YarpMaterializationTests
{
    [Fact]
    public async Task MaterializesCompleteSupportedRouteAndClusterGraph()
    {
        var configuration = Configuration() with
        {
            RootDefaults = new GatewayRootDeclarations
            {
                Authorization = Inline(new NamedAuthorizationPolicy("root-auth")),
                Cors = Inline(new CorsPolicyBinding("root-cors")),
                TrafficAdmission = Inline(new TrafficAdmissionBinding("root-admission")),
                RequestTimeout = Inline(new RequestTimeoutBinding { PolicyName = "root-timeout" }),
                OutputCache = Inline(new OutputCacheBinding("root-cache"))
            },
            Routes =
            [
                Route("disabled") with { Enabled = false },
                Route("orders") with
                {
                    Order = -10,
                    Match = new HttpRouteMatch
                    {
                        Methods = ["post", "GET"],
                        Hosts = ["API.EXAMPLE.COM"],
                        Path = "/orders/{id}",
                        Headers = [new HttpHeaderMatch { Name = "X-Tenant", Kind = TextMatchKind.Prefix, Values = ["b", "a"], CaseSensitive = true }],
                        Query = [new HttpQueryMatch { Name = "trace", Kind = TextMatchKind.Exists }]
                    },
                    Declarations = new RouteDeclarations
                    {
                        Authorization = Inline(new NamedAuthorizationPolicy("route-auth")),
                        RequestTimeout = Inline(new RequestTimeoutBinding { Timeout = TimeSpan.FromSeconds(7) }),
                        RequestTransforms = new OrderedRequestTransforms
                        {
                            Headers =
                            [
                                new RequestHeaderTransform { Kind = HeaderTransformKind.Set, Name = "X-One", Value = "1" },
                                new RequestHeaderTransform { Kind = HeaderTransformKind.Append, Name = "X-Two", Value = "2" },
                                new RequestHeaderTransform { Kind = HeaderTransformKind.Remove, Name = "X-Remove" }
                            ]
                        },
                        ResponseTransforms = new OrderedResponseTransforms
                        {
                            Headers = [new ResponseHeaderTransform { Kind = HeaderTransformKind.Set, Name = "X-Response", Value = "yes" }],
                            Trailers = [new ResponseHeaderTransform { Kind = HeaderTransformKind.Remove, Name = "X-Trailer" }]
                        }
                    }
                }
            ],
            Upstreams =
            [
                Upstream() with
                {
                    LoadBalancing = new LoadBalancingDeclaration(LoadBalancingKind.RoundRobin),
                    SessionAffinity = new SessionAffinityDeclaration { Policy = "Cookie", FailurePolicy = "Redistribute", CookieName = "hpd-session" },
                    HealthChecks = new HealthCheckDeclaration
                    {
                        Passive = new PassiveHealthCheckDeclaration { Enabled = true, Policy = "TransportFailureRate", ReactivationPeriod = TimeSpan.FromSeconds(30) },
                        Active = new ActiveHealthCheckDeclaration { Enabled = true, Policy = "ConsecutiveFailures", Interval = TimeSpan.FromSeconds(10), Timeout = TimeSpan.FromSeconds(2), Path = "/health" }
                    },
                    Transport = new UpstreamTransportDeclaration
                    {
                        UseProxy = false,
                        ConnectTimeout = TimeSpan.FromSeconds(3),
                        MaxConnectionsPerServer = 17,
                        EnableMultipleHttp2Connections = true,
                        RequestHeaderEncodingLatin1 = true
                    },
                    Request = new UpstreamRequestDeclaration
                    {
                        ActivityTimeout = TimeSpan.FromSeconds(20),
                        Version = UpstreamHttpVersion.Http11,
                        VersionSelection = HttpVersionSelection.Exact,
                        AllowResponseBuffering = true
                    }
                }
            ]
        };

        var result = await Materialize(configuration);

        result.IsMaterialized.Should().BeTrue();
        result.Bundle!.Routes.Should().ContainSingle();
        var route = result.Bundle.Routes[0];
        route.RouteId.Should().Be("orders");
        route.ClusterId.Should().Be("backend");
        route.Order.Should().Be(-10);
        route.AuthorizationPolicy.Should().Be("route-auth");
        route.CorsPolicy.Should().Be("root-cors");
        route.RateLimiterPolicy.Should().Be("root-admission");
        route.OutputCachePolicy.Should().Be("root-cache");
        route.Timeout.Should().Be(TimeSpan.FromSeconds(7));
        route.TimeoutPolicy.Should().BeNull();
        route.Match.Methods.Should().Equal("GET", "POST");
        route.Match.Hosts.Should().Equal("api.example.com");
        route.Match.Headers.Should().ContainSingle(item => item.Name == "x-tenant" && item.Mode == HeaderMatchMode.HeaderPrefix);
        route.Match.QueryParameters.Should().ContainSingle(item => item.Name == "trace" && item.Mode == QueryParameterMatchMode.Exists);
        route.Transforms.Should().HaveCount(5);

        var cluster = result.Bundle.Clusters.Should().ContainSingle().Subject;
        cluster.ClusterId.Should().Be("backend");
        cluster.LoadBalancingPolicy.Should().Be("RoundRobin");
        cluster.Destinations!.Keys.Should().Equal("a", "b");
        cluster.SessionAffinity!.AffinityKeyName.Should().Be("hpd-session");
        cluster.HealthCheck!.Passive!.Policy.Should().Be("TransportFailureRate");
        cluster.HealthCheck.Active!.Path.Should().Be("/health");
        cluster.HttpClient!.MaxConnectionsPerServer.Should().Be(17);
        cluster.HttpClient.RequestHeaderEncoding.Should().Be("iso-8859-1");
        cluster.HttpRequest!.Version.Should().Be(HttpVersion.Version11);
        cluster.HttpRequest.VersionPolicy.Should().Be(HttpVersionPolicy.RequestVersionExact);
        cluster.Metadata![HpdForwarderHttpClientFactory.UseProxyMetadata].Should().Be("False");
        cluster.Metadata[HpdForwarderHttpClientFactory.ConnectTimeoutTicksMetadata].Should().Be(TimeSpan.FromSeconds(3).Ticks.ToString());
    }

    [Fact]
    public async Task DefinitionAndInlineProduceEquivalentNativeSelection()
    {
        var definition = new DeclarationDefinition<NamedAuthorizationPolicy>
        {
            Id = new DefinitionId("auth"),
            Specification = new NamedAuthorizationPolicy("orders.read")
        };
        var fromDefinition = Configuration() with
        {
            Definitions = new GatewayDefinitions { Authorization = [definition] },
            RootDefaults = new GatewayRootDeclarations { Authorization = new DeclarationReference<NamedAuthorizationPolicy> { Definition = definition.Id } }
        };
        var inline = Configuration() with
        {
            RootDefaults = new GatewayRootDeclarations { Authorization = Inline(new NamedAuthorizationPolicy("orders.read")) }
        };

        var first = await Materialize(fromDefinition);
        var second = await Materialize(inline);

        first.Bundle!.Routes[0].AuthorizationPolicy.Should().Be(second.Bundle!.Routes[0].AuthorizationPolicy);
    }

    [Fact]
    public async Task RealYarpValidatorAcceptsInstalledNamedPolicies()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthorizationBuilder().AddPolicy("root-auth", policy => policy.RequireAssertion(_ => true));
        services.AddCors(options => options.AddPolicy("root-cors", policy => policy.AllowAnyOrigin()));
        services.AddRateLimiter(options => options.AddFixedWindowLimiter("root-admission", limiter =>
        {
            limiter.PermitLimit = 10;
            limiter.Window = TimeSpan.FromMinutes(1);
        }));
        services.AddRequestTimeouts(options => options.AddPolicy("root-timeout", TimeSpan.FromSeconds(10)));
        services.AddOutputCache(options => options.AddPolicy("root-cache", policy => policy.Expire(TimeSpan.FromSeconds(10))));
        services.AddReverseProxy();
        services.AddHpdGatewayYarpPublication();
        services.AddHpdGatewayYarpMaterialization();
        await using var provider = services.BuildServiceProvider();
        var configuration = Configuration() with
        {
            RootDefaults = new GatewayRootDeclarations
            {
                Authorization = Inline(new NamedAuthorizationPolicy("root-auth")),
                Cors = Inline(new CorsPolicyBinding("root-cors")),
                TrafficAdmission = Inline(new TrafficAdmissionBinding("root-admission")),
                RequestTimeout = Inline(new RequestTimeoutBinding { PolicyName = "root-timeout" }),
                OutputCache = Inline(new OutputCacheBinding("root-cache"))
            }
        };
        var accepted = Read(configuration, Capabilities());

        var result = await provider.GetRequiredService<GatewayNativeMaterializer>()
            .MaterializeAsync(accepted, Identity(accepted), "native-policies");

        result.IsMaterialized.Should().BeTrue(string.Join(", ", result.Diagnostics.Select(item => item.Code)));
    }

    [Fact]
    public async Task UnrealizedRuntimeInputsRejectWholeBundle()
    {
        var configuration = Configuration() with
        {
            Upstreams =
            [
                Upstream(),
                Upstream() with
                {
                    Id = new UpstreamId("discovered"),
                    Endpoints = new DiscoveredEndpointSource
                    {
                        Provider = new ProviderId("dns"),
                        Service = new ProviderObjectId("orders"),
                        StaleBehavior = DiscoveryStaleBehavior.RejectActivationUntilFresh
                    }
                }
            ],
            RootDefaults = new GatewayRootDeclarations
            {
                Inspection = Inline(new RequestInspectionBinding { MaximumBodyBytes = 1024, MaximumInspectionBytes = 128 })
            }
        };

        var result = await Materialize(configuration, Capabilities(withDiscovery: true));

        result.IsMaterialized.Should().BeFalse();
        result.Bundle.Should().BeNull();
        result.Diagnostics.Should().Contain(item => item.Code == "materialization.discovery-observation-required");
        result.Diagnostics.Should().Contain(item => item.Code == "materialization.inspection-runtime-required");
    }

    [Fact]
    public async Task NativeValidationFailureReturnsNoPartialBundle()
    {
        var accepted = Read(Configuration(), Capabilities());
        var identity = Identity(accepted);
        var materializer = new GatewayNativeMaterializer(new RejectingConfigValidator());

        var result = await materializer.MaterializeAsync(accepted, identity, "native-rejected");

        result.IsMaterialized.Should().BeFalse();
        result.Bundle.Should().BeNull();
        result.Diagnostics.Should().Contain(item => item.Code == "native.cluster-validation-failed");
        result.Diagnostics.Should().Contain(item => item.Code == "native.route-validation-failed");
    }

    [Fact]
    public async Task NativeValidationExceptionAndCancellationAreBoundedRejections()
    {
        var accepted = Read(Configuration(), Capabilities());
        var identity = Identity(accepted);
        var thrown = await new GatewayNativeMaterializer(new ThrowingConfigValidator())
            .MaterializeAsync(accepted, identity, "native-throws");
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        var canceledResult = await new GatewayNativeMaterializer(new AcceptingConfigValidator())
            .MaterializeAsync(accepted, identity, "native-canceled", canceled.Token);

        thrown.Bundle.Should().BeNull();
        thrown.Diagnostics.Should().Contain(item => item.Code == "native.cluster-validation-failed");
        canceledResult.Bundle.Should().BeNull();
        canceledResult.Diagnostics.Should().ContainSingle(item => item.Code == "materialization.canceled");
    }

    [Fact]
    public async Task ReorderedSemanticInputProducesEqualNativeArtifacts()
    {
        var first = Configuration() with
        {
            Routes =
            [
                Route("z") with { Match = new HttpRouteMatch { Path = "/z" } },
                Route("a") with { Match = new HttpRouteMatch { Path = "/a" } }
            ],
            Upstreams = [Upstream()]
        };
        var second = first with
        {
            Routes =
            [
                Route("a") with { Match = new HttpRouteMatch { Path = "/a" } },
                Route("z") with { Match = new HttpRouteMatch { Path = "/z" } }
            ],
            Upstreams =
            [
                Upstream() with
                {
                    Endpoints = new StaticEndpointSource
                    {
                        Destinations =
                        [
                            new DestinationDeclaration { Id = new DestinationId("a"), Address = new Uri("http://127.0.0.1:5001/") },
                            new DestinationDeclaration { Id = new DestinationId("b"), Address = new Uri("http://127.0.0.1:5001/") }
                        ]
                    }
                }
            ]
        };

        var firstResult = await Materialize(first);
        var secondResult = await Materialize(second);

        firstResult.Bundle!.Routes.Should().Equal(secondResult.Bundle!.Routes);
        firstResult.Bundle.Clusters.Should().Equal(secondResult.Bundle.Clusters);
    }

    [Fact]
    public void ReservedTransportMetadataConfiguresHandler()
    {
        var metadata = new Dictionary<string, string>
        {
            [HpdForwarderHttpClientFactory.UseProxyMetadata] = "true",
            [HpdForwarderHttpClientFactory.ConnectTimeoutTicksMetadata] = TimeSpan.FromSeconds(4).Ticks.ToString()
        };
        using var handler = new SocketsHttpHandler { UseProxy = false, ConnectTimeout = TimeSpan.FromSeconds(15) };

        HpdForwarderHttpClientFactory.ApplyReservedSettings(metadata, handler);

        handler.UseProxy.Should().BeTrue();
        handler.ConnectTimeout.Should().Be(TimeSpan.FromSeconds(4));
    }

    [Fact]
    public async Task HpdMaterializedAndDirectYarpForwardIdentically()
    {
        await using var backend = await StartBackend();
        var backendAddress = Address(backend);
        var configuration = Configuration(backendAddress);
        var accepted = Read(configuration, Capabilities());

        await using var hpd = await StartHpdProxy(accepted);

        var native = await Materialize(configuration);
        await using var direct = await StartDirectProxy(native.Bundle!);
        using var client = new HttpClient();

        var hpdResponse = await client.GetStringAsync(new Uri(new Uri(Address(hpd)), "/proxy/value"));
        var directResponse = await client.GetStringAsync(new Uri(new Uri(Address(direct)), "/proxy/value"));

        hpdResponse.Should().Be("backend:/proxy/value");
        directResponse.Should().Be(hpdResponse);
    }

    private static async Task<GatewayMaterializationResult> Materialize(GatewayConfiguration configuration, HostCapabilitySnapshot? capabilities = null)
    {
        var accepted = Read(configuration, capabilities ?? Capabilities());
        return await new GatewayNativeMaterializer(new AcceptingConfigValidator())
            .MaterializeAsync(accepted, Identity(accepted), $"native-{Guid.NewGuid():N}");
    }

    private static GatewayCandidateReadResult Read(GatewayConfiguration configuration, HostCapabilitySnapshot capabilities)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(configuration, GatewayJsonSerializerContext.Default.GatewayConfiguration);
        var accepted = GatewayCandidateReader.Read(json, capabilities);
        accepted.IsAccepted.Should().BeTrue(string.Join(", ", accepted.Errors.Select(error => $"{error.Path}: {error.Message}")));
        return accepted;
    }

    private static PublicationCandidateIdentity Identity(GatewayCandidateReadResult accepted) =>
        new(new CandidateId("candidate"), "authority", "epoch", 1, accepted.CanonicalDocument!.ContentHash);

    private static GatewayConfiguration Configuration(string address = "http://127.0.0.1:5001/") => new()
    {
        SchemaVersion = new GatewaySchemaVersion(1, 0),
        CanonicalizationVersion = 1,
        Routes = [Route("route")],
        Upstreams = [Upstream(address)]
    };

    private static RouteDeclaration Route(string id) => new()
    {
        Id = new RouteId(id),
        Match = new HttpRouteMatch { Path = "/{**catch-all}" },
        Upstream = new UpstreamId("backend")
    };

    private static UpstreamDeclaration Upstream(string address = "http://127.0.0.1:5001/") => new()
    {
        Id = new UpstreamId("backend"),
        Endpoints = new StaticEndpointSource
        {
            Destinations =
            [
                new DestinationDeclaration { Id = new DestinationId("b"), Address = new Uri(address) },
                new DestinationDeclaration { Id = new DestinationId("a"), Address = new Uri(address) }
            ]
        },
        Transport = new UpstreamTransportDeclaration { UseProxy = false }
    };

    private static DeclarationReference<T> Inline<T>(T value) where T : class => new() { Inline = value };

    private static HostCapabilitySnapshot Capabilities(bool withDiscovery = false) => HostCapabilitySnapshot.Create(new HostCapabilityRegistration
    {
        InstalledFamilies = GatewayDeclarationFamilies.AllBaseline,
        AuthorizationPolicies = ["root-auth", "route-auth", "orders.read"],
        CorsPolicies = ["root-cors"],
        TrafficAdmissionPolicies = ["root-admission"],
        RequestTimeoutPolicies = ["root-timeout"],
        OutputCachePolicies = ["root-cache"],
        SessionAffinityPolicies = ["Cookie"],
        SessionAffinityFailurePolicies = ["Redistribute"],
        PassiveHealthPolicies = ["TransportFailureRate"],
        ActiveHealthPolicies = ["ConsecutiveFailures"],
        DiscoveryProviders = withDiscovery
            ? [new DiscoveryProviderCapability(new ProviderId("dns"), [], [], true, false)]
            : []
    });

    private static async Task<WebApplication> StartBackend()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        var app = builder.Build();
        app.Map("/{**path}", (HttpContext context) => $"backend:{context.Request.Path}");
        await app.StartAsync();
        return app;
    }

    private static async Task<WebApplication> StartHpdProxy(GatewayCandidateReadResult accepted)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddReverseProxy();
        builder.Services.AddHpdGatewayYarpPublication();
        builder.Services.AddHpdGatewayYarpMaterialization();
        var app = builder.Build();
        app.MapReverseProxy();
        await app.StartAsync();
        var materializer = app.Services.GetRequiredService<GatewayNativeMaterializer>();
        var result = await materializer.MaterializeAsync(accepted, Identity(accepted), "native-forward");
        result.IsMaterialized.Should().BeTrue(string.Join(", ", result.Diagnostics.Select(item => item.Code)));
        var publisher = app.Services.GetRequiredService<GatewayYarpPublisher>();
        var publication = publisher.PublishAsync(result.Bundle!, TimeSpan.FromSeconds(5));
        (await publication).State.Should().Be(GatewayPublicationState.ActiveAcknowledged);
        return app;
    }

    private static async Task<WebApplication> StartDirectProxy(NativePublicationBundle bundle)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddReverseProxy().LoadFromMemory(bundle.Routes, bundle.Clusters);
        var app = builder.Build();
        app.MapReverseProxy();
        await app.StartAsync();
        return app;
    }

    private static string Address(WebApplication application) => application.Services
        .GetRequiredService<IServer>()
        .Features.Get<IServerAddressesFeature>()!
        .Addresses.Single();

    private sealed class AcceptingConfigValidator : IConfigValidator
    {
        public ValueTask<IList<Exception>> ValidateRouteAsync(RouteConfig route) => ValueTask.FromResult<IList<Exception>>([]);
        public ValueTask<IList<Exception>> ValidateClusterAsync(ClusterConfig cluster) => ValueTask.FromResult<IList<Exception>>([]);
    }

    private sealed class RejectingConfigValidator : IConfigValidator
    {
        public ValueTask<IList<Exception>> ValidateRouteAsync(RouteConfig route) => ValueTask.FromResult<IList<Exception>>([new InvalidOperationException()]);
        public ValueTask<IList<Exception>> ValidateClusterAsync(ClusterConfig cluster) => ValueTask.FromResult<IList<Exception>>([new InvalidOperationException()]);
    }

    private sealed class ThrowingConfigValidator : IConfigValidator
    {
        public ValueTask<IList<Exception>> ValidateRouteAsync(RouteConfig route) => throw new InvalidOperationException();
        public ValueTask<IList<Exception>> ValidateClusterAsync(ClusterConfig cluster) => throw new InvalidOperationException();
    }
}
