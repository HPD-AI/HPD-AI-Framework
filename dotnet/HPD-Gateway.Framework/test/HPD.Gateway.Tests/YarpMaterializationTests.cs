using System.Collections.Immutable;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using FluentAssertions;
using HPD.Gateway.Abstractions;
using HPD.Gateway.Abstractions.Serialization;
using HPD.Gateway.Core;
using HPD.Gateway.Inspection;
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
                OutputCache = Inline(new OutputCacheBinding("root-cache")),
                CredentialDisposition = Inline(new CredentialDispositionBinding { Kind = CredentialDispositionKind.Strip })
            },
            Routes =
            [
                Route("disabled") with { Enabled = false },
                Route("orders") with
                {
                    Order = -10,
                    Match = new HttpRouteMatch
                    {
                        Methods = ["GET", "HEAD"],
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
        route.Match.Methods.Should().Equal("GET", "HEAD");
        route.Match.Hosts.Should().Equal("api.example.com");
        route.Match.Headers.Should().ContainSingle(item => item.Name == "x-tenant" && item.Mode == HeaderMatchMode.HeaderPrefix);
        route.Match.QueryParameters.Should().ContainSingle(item => item.Name == "trace" && item.Mode == QueryParameterMatchMode.Exists);
        route.Transforms.Should().HaveCount(8);

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
    public async Task InspectionRootInheritanceAndRouteReplacementProduceClosedMetadata()
    {
        var prefix = new RequestInspectionBinding
        {
            InspectorName = "inspector",
            Mode = RequestInspectionMode.BoundedPrefix,
            MaximumAcceptedBodyBytes = 1024,
            MaximumInspectedBytes = 64
        };
        var complete = new RequestInspectionBinding
        {
            InspectorName = "inspector",
            Mode = RequestInspectionMode.CompleteBody,
            MaximumAcceptedBodyBytes = 2048,
            MemoryThresholdBytes = 256,
            SpillPolicy = RequestInspectionSpillPolicy.Allowed
        };
        var configuration = Configuration() with
        {
            RootDefaults = new GatewayRootDeclarations { Inspection = Inline(prefix) },
            Routes =
            [
                Route("inherited") with { Match = new HttpRouteMatch { Path = "/inherited" } },
                Route("replaced") with
                {
                    Match = new HttpRouteMatch { Path = "/replaced" },
                    Declarations = new RouteDeclarations { Inspection = Inline(complete) }
                }
            ]
        };
        var capabilities = HostCapabilitySnapshot.Create(new HostCapabilityRegistration
        {
            InstalledFamilies = GatewayDeclarationFamilies.Inspection,
            RequestInspectors = ["inspector"],
            AllowInspectionFileSpill = true
        });
        var accepted = Read(configuration, capabilities);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddReverseProxy();
        services.AddHpdGatewayYarpMaterialization();
        services.AddHpdGatewayYarpInspection(registry => registry.Add("inspector", new AllowingInspector()));
        await using var provider = services.BuildServiceProvider();

        var result = await provider.GetRequiredService<GatewayNativeMaterializer>()
            .MaterializeAsync(accepted, Identity(accepted), "native-inspection");

        var inherited = result.Bundle!.Routes.Single(route => route.RouteId == "inherited").Metadata!;
        var replaced = result.Bundle.Routes.Single(route => route.RouteId == "replaced").Metadata!;
        inherited[GatewayInspectionMetadata.Mode].Should().Be(nameof(RequestInspectionMode.BoundedPrefix));
        inherited[GatewayInspectionMetadata.MaximumInspected].Should().Be("64");
        inherited.Should().NotContainKey(GatewayInspectionMetadata.MemoryThreshold);
        replaced[GatewayInspectionMetadata.Mode].Should().Be(nameof(RequestInspectionMode.CompleteBody));
        replaced[GatewayInspectionMetadata.MemoryThreshold].Should().Be("256");
        replaced[GatewayInspectionMetadata.Spill].Should().Be(nameof(RequestInspectionSpillPolicy.Allowed));
        replaced.Should().NotContainKey(GatewayInspectionMetadata.MaximumInspected);
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
                OutputCache = Inline(new OutputCacheBinding("root-cache")),
                CredentialDisposition = Inline(new CredentialDispositionBinding { Kind = CredentialDispositionKind.Strip })
            },
            Routes = [Route("route") with { Match = new HttpRouteMatch { Path = "/{**catch-all}", Methods = ["GET", "HEAD"] } }]
        };
        var accepted = Read(configuration, Capabilities());

        var result = await provider.GetRequiredService<GatewayNativeMaterializer>()
            .MaterializeAsync(accepted, Identity(accepted), "native-policies");

        result.IsMaterialized.Should().BeTrue(string.Join(", ", result.Diagnostics.Select(item => item.Code)));
    }

    [Fact]
    public async Task UnrealizedDiscoveryInputRejectsWholeBundle()
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
            ]
        };

        var result = await Materialize(configuration, Capabilities(withDiscovery: true));

        result.IsMaterialized.Should().BeFalse();
        result.Bundle.Should().BeNull();
        result.Diagnostics.Should().Contain(item => item.Code == "materialization.discovery-observation-required");
    }

    [Fact]
    public async Task AcceptedInspectionStillRequiresMatchingRuntimeRegistry()
    {
        var configuration = Configuration() with
        {
            RootDefaults = new GatewayRootDeclarations
            {
                Inspection = Inline(new RequestInspectionBinding
                {
                    InspectorName = "inspector",
                    Mode = RequestInspectionMode.BoundedPrefix,
                    MaximumAcceptedBodyBytes = 1024,
                    MaximumInspectedBytes = 128
                })
            }
        };
        var accepted = Read(configuration, Capabilities());

        var result = await new GatewayNativeMaterializer(new AcceptingConfigValidator())
            .MaterializeAsync(accepted, Identity(accepted), "native-missing-inspector");

        result.Bundle.Should().BeNull();
        result.Diagnostics.Should().ContainSingle(item => item.Code == "materialization.inspector-not-installed" && item.Path == "routes[id=route].declarations.inspection");
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
    public async Task NativeValidationDiagnosticsUseStableResourceIdsAfterSorting()
    {
        var configuration = Configuration() with
        {
            Routes =
            [
                Route("z") with { Match = new HttpRouteMatch { Path = "/z" } },
                Route("a") with { Match = new HttpRouteMatch { Path = "/a" } }
            ],
            Upstreams =
            [
                Upstream() with { Id = new UpstreamId("z") },
                Upstream() with { Id = new UpstreamId("a") }
            ]
        };
        configuration = configuration with
        {
            Routes =
            [
                configuration.Routes[0] with { Upstream = new UpstreamId("z") },
                configuration.Routes[1] with { Upstream = new UpstreamId("a") }
            ]
        };
        var accepted = Read(configuration, Capabilities());

        var result = await new GatewayNativeMaterializer(new SelectiveRejectingConfigValidator("a"))
            .MaterializeAsync(accepted, Identity(accepted), "native-correlated");

        result.Diagnostics.Should().Contain(item => item.Path == "upstreams[id=a]");
        result.Diagnostics.Should().Contain(item => item.Path == "routes[id=a]");
        result.Diagnostics.Should().NotContain(item => item.Path.EndsWith("[0]", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("tls")]
    [InlineData("telemetry")]
    public async Task EachDeferredRuntimeSelectionFailsClosed(string selection)
    {
        var configuration = selection == "tls"
            ? Configuration("https://127.0.0.1:5001/") with
            {
                Upstreams =
                [
                    Upstream("https://127.0.0.1:5001/") with
                    {
                        Transport = new UpstreamTransportDeclaration
                        {
                            UseProxy = false,
                            Tls = new UpstreamTlsDeclaration { ServerName = "backend.local" }
                        }
                    }
                ]
            }
            : Configuration() with
            {
                RootDefaults = new GatewayRootDeclarations
                {
                    Telemetry = Inline(new TelemetryEnrichment { Attributes = [new MetadataEntry("area", "orders")] })
                }
            };

        var result = await Materialize(configuration);

        result.Bundle.Should().BeNull();
        result.Diagnostics.Should().ContainSingle(item => item.Code == $"materialization.{selection}-runtime-required" ||
            item.Code == $"materialization.{selection}-resolution-required");
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
    public async Task HpdMaterializedAndDirectYarpForwardAndReloadIdentically()
    {
        await using var firstBackend = await StartBackend("first");
        await using var secondBackend = await StartBackend("second");
        var added = ForwardingConfiguration(Address(firstBackend), "one");
        var changed = ForwardingConfiguration(Address(secondBackend), "two");
        var removed = changed with { Routes = [] };
        var readded = ForwardingConfiguration(Address(firstBackend), "three");

        var firstNative = await Materialize(added);
        await using var hpd = await StartHpdProxy();
        await using var direct = await StartDirectProxy(firstNative.Bundle!);
        using var client = new HttpClient();

        await PublishHpd(hpd, added, 1);
        await AssertEquivalentForwarding(client, hpd, direct, "first:/items/value:one", "one");

        await ReloadBoth(hpd, direct, changed, 2);
        await AssertEquivalentForwarding(client, hpd, direct, "second:/items/value:two", "two");

        await ReloadBoth(hpd, direct, removed, 3);
        await AssertEquivalentStatus(client, hpd, direct, HttpStatusCode.NotFound);

        await ReloadBoth(hpd, direct, readded, 4);
        await AssertEquivalentForwarding(client, hpd, direct, "first:/items/value:three", "three");
    }

    private static GatewayConfiguration ForwardingConfiguration(string address, string transformValue) => Configuration(address) with
    {
        Routes =
        [
            Route("route") with
            {
                Match = new HttpRouteMatch
                {
                    Path = "/items/{**rest}",
                    Methods = ["GET"],
                    Hosts = ["gateway.local"],
                    Headers = [new HttpHeaderMatch { Name = "X-Tenant", Kind = TextMatchKind.Exact, Values = ["yes"] }],
                    Query = [new HttpQueryMatch { Name = "ready", Kind = TextMatchKind.Exists }]
                },
                Declarations = new RouteDeclarations
                {
                    Authorization = Inline(new NamedAuthorizationPolicy("route-auth")),
                    RequestTransforms = new OrderedRequestTransforms
                    {
                        Headers = [new RequestHeaderTransform { Kind = HeaderTransformKind.Set, Name = "X-Gateway", Value = transformValue }]
                    },
                    ResponseTransforms = new OrderedResponseTransforms
                    {
                        Headers = [new ResponseHeaderTransform { Kind = HeaderTransformKind.Set, Name = "X-Reload", Value = transformValue }]
                    }
                }
            }
        ]
    };

    private static async Task ReloadBoth(WebApplication hpd, WebApplication direct, GatewayConfiguration configuration, ulong version)
    {
        await PublishHpd(hpd, configuration, version);
        var native = await Materialize(configuration);
        direct.Services.GetRequiredService<InMemoryConfigProvider>().Update(native.Bundle!.Routes, native.Bundle.Clusters);
    }

    private static async Task PublishHpd(WebApplication app, GatewayConfiguration configuration, ulong version)
    {
        var accepted = Read(configuration, Capabilities());
        var identity = new PublicationCandidateIdentity(new CandidateId($"candidate-{version}"), "authority", "epoch", version, accepted.CanonicalDocument!.ContentHash);
        var result = await app.Services.GetRequiredService<GatewayNativeMaterializer>()
            .MaterializeAsync(accepted, identity, $"native-forward-{version}");
        result.IsMaterialized.Should().BeTrue(string.Join(", ", result.Diagnostics.Select(item => item.Code)));
        var outcome = await app.Services.GetRequiredService<GatewayYarpPublisher>().PublishAsync(result.Bundle!, TimeSpan.FromSeconds(5));
        outcome.State.Should().Be(GatewayPublicationState.ActiveAcknowledged);
    }

    private static async Task AssertEquivalentForwarding(HttpClient client, WebApplication hpd, WebApplication direct, string expectedBody, string expectedHeader)
    {
        var hpdResponse = await SendMatched(client, hpd);
        var directResponse = await SendMatched(client, direct);
        hpdResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        directResponse.StatusCode.Should().Be(hpdResponse.StatusCode);
        (await hpdResponse.Content.ReadAsStringAsync()).Should().Be(expectedBody);
        (await directResponse.Content.ReadAsStringAsync()).Should().Be(expectedBody);
        hpdResponse.Headers.GetValues("X-Reload").Should().Equal(expectedHeader);
        directResponse.Headers.GetValues("X-Reload").Should().Equal(expectedHeader);
    }

    private static async Task AssertEquivalentStatus(HttpClient client, WebApplication hpd, WebApplication direct, HttpStatusCode expected)
    {
        (await SendMatched(client, hpd)).StatusCode.Should().Be(expected);
        (await SendMatched(client, direct)).StatusCode.Should().Be(expected);
    }

    private static Task<HttpResponseMessage> SendMatched(HttpClient client, WebApplication app)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, new Uri(new Uri(Address(app)), "/items/value?ready=1"));
        request.Headers.Host = "gateway.local";
        request.Headers.Add("X-Tenant", "yes");
        return client.SendAsync(request);
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
        InstalledFamilies = GatewayDeclarationFamilies.AllBaseline | GatewayDeclarationFamilies.CredentialDisposition,
        AuthorizationPolicies = ["root-auth", "route-auth", "orders.read"],
        CorsPolicies = ["root-cors"],
        TrafficAdmissionPolicies = ["root-admission"],
        RequestTimeoutPolicies = ["root-timeout"],
        OutputCacheProfiles = [CacheCapability("root-cache")],
        SessionAffinityPolicies = ["Cookie"],
        SessionAffinityFailurePolicies = ["Redistribute"],
        PassiveHealthPolicies = ["TransportFailureRate"],
        ActiveHealthPolicies = ["ConsecutiveFailures"],
        RequestInspectors = ["inspector"],
        DiscoveryProviders = withDiscovery
            ? [new DiscoveryProviderCapability(new ProviderId("dns"), [], [], true, false)]
            : []
    });

    private static OutputCacheCapability CacheCapability(string name) => new(
        name,
        1,
        true,
        "memory",
        OutputCacheStoreScope.ProcessLocal,
        1_048_576,
        16_777_216,
        [],
        []);

    private static async Task<WebApplication> StartBackend(string name)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        var app = builder.Build();
        app.Map("/{**path}", (HttpContext context) => $"{name}:{context.Request.Path}:{context.Request.Headers["X-Gateway"]}");
        await app.StartAsync();
        return app;
    }

    private static async Task<WebApplication> StartHpdProxy()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddAuthorizationBuilder().AddPolicy("route-auth", policy => policy.RequireAssertion(_ => true));
        builder.Services.AddReverseProxy();
        builder.Services.AddHpdGatewayYarpPublication();
        builder.Services.AddHpdGatewayYarpMaterialization();
        var app = builder.Build();
        app.UseAuthorization();
        app.MapReverseProxy();
        await app.StartAsync();
        return app;
    }

    private static async Task<WebApplication> StartDirectProxy(NativePublicationBundle bundle)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddAuthorizationBuilder().AddPolicy("route-auth", policy => policy.RequireAssertion(_ => true));
        builder.Services.AddReverseProxy().LoadFromMemory(bundle.Routes, bundle.Clusters);
        var app = builder.Build();
        app.UseAuthorization();
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

    private sealed class SelectiveRejectingConfigValidator(string rejectedId) : IConfigValidator
    {
        public ValueTask<IList<Exception>> ValidateRouteAsync(RouteConfig route) =>
            ValueTask.FromResult<IList<Exception>>(route.RouteId == rejectedId ? [new InvalidOperationException()] : []);

        public ValueTask<IList<Exception>> ValidateClusterAsync(ClusterConfig cluster) =>
            ValueTask.FromResult<IList<Exception>>(cluster.ClusterId == rejectedId ? [new InvalidOperationException()] : []);
    }

    private sealed class ThrowingConfigValidator : IConfigValidator
    {
        public ValueTask<IList<Exception>> ValidateRouteAsync(RouteConfig route) => throw new InvalidOperationException();
        public ValueTask<IList<Exception>> ValidateClusterAsync(ClusterConfig cluster) => throw new InvalidOperationException();
    }

    private sealed class AllowingInspector : IGatewayRequestInspector
    {
        public ValueTask<GatewayInspectionDecision> InspectAsync(GatewayInspectionContext context, CancellationToken cancellationToken) =>
            ValueTask.FromResult(GatewayInspectionDecision.Allow());
    }
}
