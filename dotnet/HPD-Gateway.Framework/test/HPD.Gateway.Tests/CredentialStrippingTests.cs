using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using FluentAssertions;
using HPD.Gateway;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Transforms.Builder;
using Xunit;

namespace HPD.Gateway.Tests;

public sealed class CredentialStrippingTests
{
    [Fact]
    public void StrictWireRoundTripsTheClosedStripKindAndRejectsUnknownKinds()
    {
        var configuration = Configuration("http://127.0.0.1:5001", rootDisposition: StripReference()) with
        {
            Definitions = new GatewayDefinitions
            {
                CredentialDisposition =
                [
                    new DeclarationDefinition<CredentialDispositionBinding>
                    {
                        Id = new DefinitionId("strip"),
                        Specification = Strip()
                    }
                ]
            },
            RootDefaults = new GatewayRootDeclarations
            {
                CredentialDisposition = new DeclarationReference<CredentialDispositionBinding>
                {
                    Definition = new DefinitionId("strip")
                }
            }
        };
        var json = JsonSerializer.SerializeToUtf8Bytes(configuration, GatewayJsonSerializerContext.Default.GatewayConfiguration);

        var parsed = GatewayConfigurationParser.Parse(json);
        var unknown = GatewayConfigurationParser.Parse(json.AsSpan().ToArray()
            .ReplaceUtf8("\"kind\":\"strip\"", "\"kind\":\"replaceWithDelegatedCredential\""));

        parsed.IsParsed.Should().BeTrue();
        parsed.Configuration!.RootDefaults!.CredentialDisposition!.Definition.Should().Be(new DefinitionId("strip"));
        unknown.IsParsed.Should().BeFalse();
    }

    [Fact]
    public void CanonicalizationSortsCredentialDefinitionsAndPreservesMeaning()
    {
        var first = Configuration("http://127.0.0.1:5001") with
        {
            Definitions = new GatewayDefinitions
            {
                CredentialDisposition =
                [
                    Definition("z"),
                    Definition("a")
                ]
            }
        };
        var second = first with
        {
            Definitions = first.Definitions! with
            {
                CredentialDisposition = first.Definitions.CredentialDisposition.Reverse().ToImmutableArray()
            }
        };

        var firstCanonical = GatewayConfigurationCanonicalizer.TryCanonicalize(first);
        var secondCanonical = GatewayConfigurationCanonicalizer.TryCanonicalize(second);

        firstCanonical.IsCanonicalized.Should().BeTrue();
        secondCanonical.Document!.Utf8Json.Should().Equal(firstCanonical.Document!.Utf8Json);
        secondCanonical.Document.ContentHash.Should().Be(firstCanonical.Document.ContentHash);
    }

    [Fact]
    public void HostCatalogAlwaysContainsNormalizedFixedHeadersAndBoundedCustomHeaders()
    {
        var capabilities = Capabilities("X-Api-Key");

        capabilities.ProtectedCredentialHeaders.Should().Equal(
            "authorization",
            "cookie",
            "proxy-authorization",
            "x-api-key");
    }

    [Theory]
    [InlineData("Authorization")]
    [InlineData("Bad Header")]
    [InlineData("Host")]
    [InlineData("Transfer-Encoding")]
    public void HostCatalogRejectsDuplicateInvalidOrProhibitedCustomHeaders(string header)
    {
        Action create = () => Capabilities(header);

        create.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void HostCatalogRejectsTooManyCustomHeaders()
    {
        var headers = Enumerable.Range(0, 33).Select(index => $"X-Key-{index}").ToArray();

        Action create = () => HostCapabilitySnapshot.Create(new()
        {
            InstalledFamilies = GatewayDeclarationFamilies.CredentialDisposition,
            ProtectedCredentialHeaders = headers
        });

        create.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void CandidateRequiresInstalledFamilyAndRetainsTheExactAcceptedCatalog()
    {
        var configuration = Configuration("http://127.0.0.1:5001", routeDisposition: StripReference());
        var json = Serialize(configuration);

        var missing = GatewayCandidateReader.Read(json, HostCapabilitySnapshot.Create(new()));
        var accepted = GatewayCandidateReader.Read(json, Capabilities("X-Api-Key"));

        missing.IsAccepted.Should().BeFalse();
        missing.Errors.Should().Contain(error => error.Path == "credentialDisposition");
        accepted.IsAccepted.Should().BeTrue();
        accepted.ProtectedCredentialHeaders.Should().Equal("authorization", "cookie", "proxy-authorization", "x-api-key");
    }

    [Theory]
    [InlineData("Authorization", HeaderTransformKind.Set)]
    [InlineData("cookie", HeaderTransformKind.Append)]
    [InlineData("X-API-KEY", HeaderTransformKind.Set)]
    public void EffectiveStripRejectsTransformsThatRestoreProtectedHeaders(string header, HeaderTransformKind kind)
    {
        var configuration = Configuration("http://127.0.0.1:5001", rootDisposition: StripReference()) with
        {
            Routes =
            [
                Route(new RouteDeclarations
                {
                    RequestTransforms = new OrderedRequestTransforms
                    {
                        Headers = [new RequestHeaderTransform { Kind = kind, Name = header, Value = "secret" }]
                    }
                })
            ]
        };

        var result = GatewayCandidateValidator.Validate(configuration, Capabilities("X-Api-Key"));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.Message.Contains("protected credential", StringComparison.Ordinal));
    }

    [Fact]
    public void ExplicitRemoveRemainsValidUnderEffectiveStrip()
    {
        var configuration = Configuration("http://127.0.0.1:5001", routeDisposition: StripReference()) with
        {
            Routes =
            [
                Route(new RouteDeclarations
                {
                    CredentialDisposition = StripReference(),
                    RequestTransforms = new OrderedRequestTransforms
                    {
                        Headers = [new RequestHeaderTransform { Kind = HeaderTransformKind.Remove, Name = "Authorization" }]
                    }
                })
            ]
        };

        GatewayCandidateValidator.Validate(configuration, Capabilities()).IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task PlannerPlacesTheSortedProtectedRemovalSetAfterOrdinaryTransforms()
    {
        var configuration = Configuration("http://127.0.0.1:5001") with
        {
            Routes =
            [
                Route(new RouteDeclarations
                {
                    CredentialDisposition = StripReference(),
                    RequestTransforms = new OrderedRequestTransforms
                    {
                        Headers = [new RequestHeaderTransform { Kind = HeaderTransformKind.Set, Name = "X-Safe", Value = "yes" }]
                    }
                })
            ]
        };
        var accepted = Read(configuration, Capabilities("X-Api-Key"));

        var planned = await new GatewayRuntimePlanner(new AcceptingConfigValidator()).PlanAsync(
            accepted,
            Identity(accepted, 1),
            "credential-native-1");

        planned.IsPlanned.Should().BeTrue();
        var transforms = planned.PreparedApplication!.Routes.Single().Transforms!;
        transforms.Should().HaveCount(5);
        transforms[0].Values.Should().Contain("X-Safe");
        transforms.Skip(1).Select(transform => transform.Values.Last()).Should().Equal(
            "authorization",
            "cookie",
            "proxy-authorization",
            "x-api-key");
    }

    [Fact]
    public async Task PlannerRejectsAnAcceptedStripCandidateWithoutItsHostCatalog()
    {
        var configuration = Configuration("http://127.0.0.1:5001", routeDisposition: StripReference());
        var accepted = Read(configuration, Capabilities());
        var constructor = typeof(GatewayCandidateReadResult).GetConstructors(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Single();
        var strippedResult = (GatewayCandidateReadResult)constructor.Invoke([
            accepted.Configuration,
            accepted.CanonicalDocument,
            ImmutableArray<GatewayValidationError>.Empty,
            default(ImmutableArray<string>),
            accepted.OutputCacheProfiles,
            accepted.DiscoveryProfiles,
            accepted.UpstreamResilienceProfiles
        ]);

        var planned = await new GatewayRuntimePlanner(new AcceptingConfigValidator()).PlanAsync(
            strippedResult,
            Identity(accepted, 1),
            "credential-native-missing");

        planned.IsPlanned.Should().BeFalse();
        planned.Diagnostics.Should().Contain(error => error.Code == "planning.credential-catalog-unavailable");
    }

    [Fact]
    public async Task RealYarpStripsFixedCustomCasedAndDuplicateCredentialsAndReloadRestoresNativeBehavior()
    {
        await using var fixture = await CredentialFixture.Start();
        await fixture.Publish(strip: true, 1);

        using (var stripped = await fixture.Send("/observe"))
            stripped.StatusCode.Should().Be(HttpStatusCode.OK);
        fixture.Observations.Last().Should().Be(Observed.Empty);

        await fixture.Publish(strip: false, 2);
        using (var native = await fixture.Send("/observe"))
            native.StatusCode.Should().Be(HttpStatusCode.OK);
        var nativeObservation = fixture.Observations.Last();
        nativeObservation.Authorization.Should().Be("Bearer inbound");
        nativeObservation.Cookie.Should().Contain("session=one").And.Contain("session=two");
        nativeObservation.Custom.Should().Contain("first").And.Contain("second");
        nativeObservation.ProxyAuthorization.Should().BeEmpty();

        await fixture.Publish(strip: true, 3);
        using var readded = await fixture.Send("/observe");
        fixture.Observations.Last().Should().Be(Observed.Empty);
    }

    [Fact]
    public async Task InFlightRequestKeepsItsStrippingGenerationWhileNewRequestsUseTheReplacement()
    {
        await using var fixture = await CredentialFixture.Start();
        await fixture.Publish(strip: true, 1);

        var oldGeneration = fixture.Send("/hold");
        await fixture.WaitUntilHeld();
        fixture.Observations.Last().Should().Be(Observed.Empty);

        await fixture.Publish(strip: false, 2);
        using var newGeneration = await fixture.Send("/observe");
        fixture.Observations.Last().Authorization.Should().Be("Bearer inbound");

        fixture.ReleaseHeld();
        using var completedOldGeneration = await oldGeneration;
        completedOldGeneration.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task UpgradePathStripsBeforeUpstreamSend()
    {
        await using var fixture = await CredentialFixture.Start();
        await fixture.Publish(strip: true, 1);

        using var response = await fixture.Send("/protocol", upgrade: true);

        fixture.Observations.Should().NotBeEmpty();
        fixture.Observations.Last().Should().Be(Observed.Empty);
    }

    [Fact]
    public async Task ConnectPathStripsBeforeUpstreamSend()
    {
        var accepted = Read(Configuration("http://127.0.0.1:5001", routeDisposition: StripReference()), Capabilities("X-Api-Key"));
        var planned = await new GatewayRuntimePlanner(new AcceptingConfigValidator()).PlanAsync(
            accepted,
            Identity(accepted, 1),
            "credential-connect");
        await using var services = new ServiceCollection().AddLogging().AddReverseProxy().Services.BuildServiceProvider();
        var transformer = services.GetRequiredService<ITransformBuilder>().Build(
            planned.PreparedApplication!.Routes.Single(),
            planned.PreparedApplication.Clusters.Single());
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Connect;
        context.Request.Headers.Authorization = "Bearer inbound";
        context.Request.Headers.Cookie = "session=one";
        context.Request.Headers["Proxy-Authorization"] = "Basic inbound";
        context.Request.Headers["X-Api-Key"] = "first";
        using var outbound = new HttpRequestMessage(HttpMethod.Connect, "http://127.0.0.1:5001");

        await transformer.TransformRequestAsync(context, outbound, "http://127.0.0.1:5001", CancellationToken.None);

        outbound.Headers.Contains("Authorization").Should().BeFalse();
        outbound.Headers.Contains("Cookie").Should().BeFalse();
        outbound.Headers.Contains("Proxy-Authorization").Should().BeFalse();
        outbound.Headers.Contains("X-Api-Key").Should().BeFalse();
    }

    [Fact]
    public async Task MaterializedStrippingIsDifferentiallyEquivalentToDirectYarpTransforms()
    {
        var accepted = Read(Configuration("http://127.0.0.1:5001", routeDisposition: StripReference()), Capabilities("X-Api-Key"));
        var planned = await new GatewayRuntimePlanner(new AcceptingConfigValidator()).PlanAsync(
            accepted,
            Identity(accepted, 1),
            "credential-differential");
        var direct = new RouteConfig
        {
            RouteId = "direct",
            ClusterId = "backend",
            Match = new RouteMatch { Path = "/{**path}" }
        };
        foreach (var header in accepted.ProtectedCredentialHeaders)
            direct = global::Yarp.ReverseProxy.Transforms.RequestHeadersTransformExtensions.WithTransformRequestHeaderRemove(direct, header);

        planned.PreparedApplication!.Routes.Single().Transforms.Should().BeEquivalentTo(direct.Transforms, options => options.WithStrictOrdering());

        await using var services = new ServiceCollection().AddLogging().AddReverseProxy().Services.BuildServiceProvider();
        var builder = services.GetRequiredService<ITransformBuilder>();
        var hpdHeaders = await Execute(builder.Build(planned.PreparedApplication.Routes.Single(), planned.PreparedApplication.Clusters.Single()));
        var directHeaders = await Execute(builder.Build(direct, planned.PreparedApplication.Clusters.Single()));
        hpdHeaders.Should().Equal(directHeaders).And.BeEmpty();
    }

    private static CredentialDispositionBinding Strip() => new() { Kind = CredentialDispositionKind.Strip };

    private static DeclarationDefinition<CredentialDispositionBinding> Definition(string id) => new()
    {
        Id = new DefinitionId(id),
        Specification = Strip()
    };

    private static DeclarationReference<CredentialDispositionBinding> StripReference() => new() { Inline = Strip() };

    private static GatewayConfiguration Configuration(
        string address,
        DeclarationReference<CredentialDispositionBinding>? routeDisposition = null,
        DeclarationReference<CredentialDispositionBinding>? rootDisposition = null) => new()
    {
        SchemaVersion = new GatewaySchemaVersion(1, 0),
        CanonicalizationVersion = 1,
        Routes = [Route(new RouteDeclarations { CredentialDisposition = routeDisposition })],
        Upstreams = [Upstream(address)],
        RootDefaults = new GatewayRootDeclarations { CredentialDisposition = rootDisposition }
    };

    private static RouteDeclaration Route(RouteDeclarations declarations) => new()
    {
        Id = new RouteId("route"),
        Match = new HttpRouteMatch { Path = "/{**path}" },
        Upstream = new UpstreamId("backend"),
        Declarations = declarations
    };

    private static UpstreamDeclaration Upstream(string address) => new()
    {
        Id = new UpstreamId("backend"),
        Endpoints = new StaticEndpointSource
        {
            Destinations = [new DestinationDeclaration { Id = new DestinationId("one"), Address = new Uri(address) }]
        },
        Transport = new UpstreamTransportDeclaration { UseProxy = false }
    };

    private static HostCapabilitySnapshot Capabilities(params string[] customHeaders) => HostCapabilitySnapshot.Create(new()
    {
        InstalledFamilies = GatewayDeclarationFamilies.CredentialDisposition | GatewayDeclarationFamilies.RequestTransforms,
        ProtectedCredentialHeaders = customHeaders
    });

    private static byte[] Serialize(GatewayConfiguration configuration) =>
        JsonSerializer.SerializeToUtf8Bytes(configuration, GatewayJsonSerializerContext.Default.GatewayConfiguration);

    private static GatewayCandidateReadResult Read(GatewayConfiguration configuration, HostCapabilitySnapshot capabilities)
    {
        var accepted = GatewayCandidateReader.Read(Serialize(configuration), capabilities);
        accepted.IsAccepted.Should().BeTrue(string.Join(", ", accepted.Errors.Select(error => $"{error.Path}: {error.Message}")));
        return accepted;
    }

    private static PublicationCandidateIdentity Identity(GatewayCandidateReadResult accepted, ulong version) =>
        new(new CandidateId($"credential-{version}"), "authority", "epoch", version, accepted.CanonicalDocument!.ContentHash);

    private static async Task<string[]> Execute(global::Yarp.ReverseProxy.Forwarder.HttpTransformer transformer)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Get;
        context.Request.Headers.Authorization = "Bearer inbound";
        context.Request.Headers.Cookie = "session=one";
        context.Request.Headers["Proxy-Authorization"] = "Basic inbound";
        context.Request.Headers["X-Api-Key"] = "first";
        using var outbound = new HttpRequestMessage(HttpMethod.Get, "http://127.0.0.1:5001");
        await transformer.TransformRequestAsync(context, outbound, "http://127.0.0.1:5001", CancellationToken.None);
        return outbound.Headers
            .Where(header => header.Key is "Authorization" or "Cookie" or "Proxy-Authorization" or "X-Api-Key")
            .Select(header => header.Key)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private sealed class CredentialFixture : IAsyncDisposable
    {
        private readonly WebApplication _backend;
        private readonly WebApplication _proxy;
        private readonly TaskCompletionSource _held = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        private CredentialFixture(
            WebApplication backend,
            WebApplication proxy,
            ConcurrentQueue<Observed> observations,
            TaskCompletionSource held,
            TaskCompletionSource release)
        {
            _backend = backend;
            _proxy = proxy;
            Observations = observations;
            _held = held;
            _release = release;
            Client = new HttpClient { BaseAddress = new Uri(Address(proxy)) };
        }

        internal HttpClient Client { get; }
        internal ConcurrentQueue<Observed> Observations { get; }

        internal static async Task<CredentialFixture> Start()
        {
            var observations = new ConcurrentQueue<Observed>();
            var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var backendBuilder = WebApplication.CreateSlimBuilder();
            backendBuilder.WebHost.UseUrls("http://127.0.0.1:0");
            var backend = backendBuilder.Build();
            backend.Run(async context =>
            {
                observations.Enqueue(new Observed(
                    context.Request.Headers.Authorization.ToString(),
                    context.Request.Headers.Cookie.ToString(),
                    context.Request.Headers["Proxy-Authorization"].ToString(),
                    context.Request.Headers["X-Api-Key"].ToString()));
                if (context.Request.Path == "/hold")
                {
                    held.TrySetResult();
                    await release.Task;
                }
                context.Response.StatusCode = StatusCodes.Status200OK;
            });
            await backend.StartAsync();

            var proxyBuilder = WebApplication.CreateSlimBuilder();
            proxyBuilder.WebHost.UseUrls("http://127.0.0.1:0");
            proxyBuilder.Services.AddReverseProxy();
            proxyBuilder.Services.AddHpdGatewayYarpPublication();
            proxyBuilder.Services.AddHpdGatewayYarpMaterialization();
            var proxy = proxyBuilder.Build();
            proxy.MapReverseProxy();
            await proxy.StartAsync();
            return new CredentialFixture(backend, proxy, observations, held, release);
        }

        internal Task WaitUntilHeld() => _held.Task.WaitAsync(TimeSpan.FromSeconds(5));

        internal void ReleaseHeld() => _release.TrySetResult();

        internal async Task Publish(bool strip, ulong version)
        {
            var capabilities = strip ? Capabilities("X-Api-Key") : HostCapabilitySnapshot.Create(new() { ProtectedCredentialHeaders = ["X-Api-Key"] });
            var configuration = Configuration(Address(_backend), routeDisposition: strip ? StripReference() : null) with
            {
                Routes =
                [
                    Route(new RouteDeclarations { CredentialDisposition = strip ? StripReference() : null }) with
                    {
                        Match = new HttpRouteMatch { Hosts = ["*"] }
                    }
                ]
            };
            var accepted = Read(configuration, capabilities);
            var planned = await _proxy.Services.GetRequiredService<GatewayRuntimePlanner>().PlanAsync(
                accepted,
                Identity(accepted, version),
                $"credential-native-{version}");
            planned.IsPlanned.Should().BeTrue(string.Join(", ", planned.Diagnostics.Select(error => error.Code)));
            var outcome = await _proxy.Services.GetRequiredService<GatewayRuntimePublisher>().PublishAsync(planned.PreparedApplication!, TimeSpan.FromSeconds(5));
            outcome.State.Should().Be(GatewayPublicationState.ActiveAcknowledged);
        }

        internal Task<HttpResponseMessage> Send(string path, string method = "GET", bool upgrade = false)
        {
            var request = new HttpRequestMessage(new HttpMethod(method), path);
            if (method == "CONNECT") request.Headers.Host = "gateway.local";
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "inbound");
            request.Headers.TryAddWithoutValidation("Cookie", ["session=one", "session=two"]);
            request.Headers.TryAddWithoutValidation("Proxy-Authorization", "Basic inbound");
            request.Headers.TryAddWithoutValidation("X-API-KEY", ["first", "second"]);
            if (upgrade)
            {
                request.Headers.TryAddWithoutValidation("Connection", "Upgrade");
                request.Headers.TryAddWithoutValidation("Upgrade", "h2c");
            }
            return Client.SendAsync(request);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _proxy.DisposeAsync();
            await _backend.DisposeAsync();
        }

        private static string Address(WebApplication application) => application.Services
            .GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
    }

    private sealed record Observed(string Authorization, string Cookie, string ProxyAuthorization, string Custom)
    {
        internal static Observed Empty { get; } = new(string.Empty, string.Empty, string.Empty, string.Empty);
    }

    private sealed class AcceptingConfigValidator : global::Yarp.ReverseProxy.Configuration.IConfigValidator
    {
        public ValueTask<IList<Exception>> ValidateRouteAsync(global::Yarp.ReverseProxy.Configuration.RouteConfig route) =>
            ValueTask.FromResult<IList<Exception>>([]);

        public ValueTask<IList<Exception>> ValidateClusterAsync(global::Yarp.ReverseProxy.Configuration.ClusterConfig cluster) =>
            ValueTask.FromResult<IList<Exception>>([]);
    }
}

internal static class CredentialTestUtf8Extensions
{
    internal static byte[] ReplaceUtf8(this byte[] source, string oldValue, string newValue) =>
        System.Text.Encoding.UTF8.GetBytes(System.Text.Encoding.UTF8.GetString(source).Replace(oldValue, newValue, StringComparison.Ordinal));
}
