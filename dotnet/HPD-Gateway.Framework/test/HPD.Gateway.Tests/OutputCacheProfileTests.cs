using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using HPD.Gateway.Abstractions;
using HPD.Gateway.Abstractions.Serialization;
using HPD.Gateway.Core;
using HPD.Gateway.OutputCaching;
using HPD.Gateway.Yarp;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Yarp.ReverseProxy.Configuration;

namespace HPD.Gateway.Tests;

public sealed class OutputCacheProfileTests
{
    [Fact]
    public void RegistryNormalizesAndPublishesAnExactConservativeCapability()
    {
        var services = new ServiceCollection();
        services.AddHpdGatewayOutputCaching(builder =>
        {
            builder.MaximumBodyBytes = 4_096;
            builder.StoreCapacityBytes = 65_536;
            builder.Add(new GatewayOutputCacheProfile
            {
                Name = "public-cache",
                Version = 7,
                Expiration = TimeSpan.FromMinutes(2),
                QueryKeys = ["Version", "lang"],
                HeaderNames = ["X-Tenant", "Accept-Language"]
            });
        });
        using var provider = services.BuildServiceProvider();

        var capability = provider.GetHpdGatewayOutputCacheCapabilities().Single();

        capability.Name.Should().Be("public-cache");
        capability.Version.Should().Be(7);
        capability.RetainsDefaultSafetyPolicy.Should().BeTrue();
        capability.StoreId.Should().Be("memory");
        capability.StoreScope.Should().Be(OutputCacheStoreScope.ProcessLocal);
        capability.QueryKeys.Should().Equal("lang", "version");
        capability.HeaderNames.Should().Equal("accept-language", "x-tenant");
    }

    [Theory]
    [InlineData("Authorization")]
    [InlineData("Cookie")]
    [InlineData("Proxy-Authorization")]
    [InlineData("Bad Header")]
    public void RegistryRejectsCredentialOrInvalidHeaderDimensions(string name)
    {
        Action build = () => new GatewayOutputCacheRegistryBuilder().Add(new GatewayOutputCacheProfile
        {
            Name = "safe",
            Version = 1,
            HeaderNames = [name]
        });

        build.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void RegistryRejectsInvalidBoundsDuplicatesAndEmptyCatalogs()
    {
        var duplicate = new GatewayOutputCacheRegistryBuilder();
        duplicate.Add(Profile("safe"));
        Action duplicateName = () => duplicate.Add(Profile("safe"));
        Action noProfiles = () => new GatewayOutputCacheRegistryBuilder().Build();
        var badBoundsBuilder = new GatewayOutputCacheRegistryBuilder { MaximumBodyBytes = 100, StoreCapacityBytes = 50 };
        badBoundsBuilder.Add(Profile("safe"));
        Action badBounds = () => badBoundsBuilder.Build();

        duplicateName.Should().Throw<ArgumentException>();
        noProfiles.Should().Throw<InvalidOperationException>();
        badBounds.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void HostCapabilitiesRejectUnsafeWrongStoreAndProtectedDimensions()
    {
        var safe = Capability("safe") with { HeaderNames = ["x-api-key"] };
        Action protectedDimension = () => HostCapabilitySnapshot.Create(new()
        {
            ProtectedCredentialHeaders = ["X-Api-Key"],
            OutputCacheProfiles = [safe]
        });
        Action unsafeDefault = () => HostCapabilitySnapshot.Create(new()
        {
            OutputCacheProfiles = [Capability("safe") with { RetainsDefaultSafetyPolicy = false }]
        });
        Action sharedStore = () => HostCapabilitySnapshot.Create(new()
        {
            OutputCacheProfiles = [Capability("safe") with { StoreScope = (OutputCacheStoreScope)1 }]
        });

        protectedDimension.Should().Throw<ArgumentException>();
        unsafeDefault.Should().Throw<ArgumentException>();
        sharedStore.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void CandidateRetainsExactCapabilityAndEnforcesMethodStripAndInspectionGates()
    {
        var capabilities = Capabilities();
        var accepted = Read(Configuration("http://127.0.0.1:5001", strip: true), capabilities);
        var unsafeMethods = GatewayCandidateValidator.Validate(
            Configuration("http://127.0.0.1:5001", strip: true) with
            {
                Routes = [Route(new RouteDeclarations { OutputCache = Cache(), CredentialDisposition = Strip() }, ["GET", "POST"])]
            }, capabilities);
        var missingStrip = GatewayCandidateValidator.Validate(Configuration("http://127.0.0.1:5001", strip: false), capabilities);
        var inspection = GatewayCandidateValidator.Validate(
            Configuration("http://127.0.0.1:5001", strip: true) with
            {
                Routes =
                [
                    Route(new RouteDeclarations
                    {
                        OutputCache = Cache(),
                        CredentialDisposition = Strip(),
                        Inspection = new DeclarationReference<RequestInspectionBinding>
                        {
                            Inline = new RequestInspectionBinding
                            {
                                InspectorName = "inspect",
                                Mode = RequestInspectionMode.BoundedPrefix,
                                MaximumAcceptedBodyBytes = 1024,
                                MaximumInspectedBytes = 16
                            }
                        }
                    }, ["GET"])
                ]
            }, HostCapabilitySnapshot.Create(new()
            {
                InstalledFamilies = GatewayDeclarationFamilies.OutputCache | GatewayDeclarationFamilies.CredentialDisposition | GatewayDeclarationFamilies.Inspection,
                OutputCacheProfiles = [Capability("public-cache")],
                RequestInspectors = ["inspect"]
            }));

        accepted.OutputCacheProfiles["public-cache"].Should().BeEquivalentTo(Capability("public-cache"));
        unsafeMethods.Errors.Should().Contain(error => error.Path.EndsWith("match.methods", StringComparison.Ordinal));
        missingStrip.Errors.Should().Contain(error => error.Path.EndsWith("credentialDisposition", StringComparison.Ordinal));
        inspection.Errors.Should().Contain(error => error.Path.EndsWith("inspection", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("version")]
    [InlineData("expiration")]
    [InlineData("query")]
    [InlineData("header")]
    [InlineData("body-bound")]
    [InlineData("store-bound")]
    [InlineData("store-id")]
    public async Task MaterializationRejectsAnAcceptedCatalogThatDiffersFromTheInstalledRuntimeRegistry(string difference)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddReverseProxy();
        services.AddHpdGatewayYarpMaterialization();
        services.AddHpdGatewayOutputCaching(builder =>
        {
            builder.MaximumBodyBytes = 1_024;
            builder.StoreCapacityBytes = 65_536;
            builder.Add(new GatewayOutputCacheProfile
            {
                Name = "public-cache",
                Version = 1,
                QueryKeys = ["lang"],
                HeaderNames = ["accept-language"]
            });
        });
        using var provider = services.BuildServiceProvider();
        var capability = Capability("public-cache");
        var mismatched = difference switch
        {
            "version" => capability with { Version = 2 },
            "expiration" => capability with { Expiration = TimeSpan.FromMinutes(2) },
            "query" => capability with { QueryKeys = ["other"] },
            "header" => capability with { HeaderNames = ["x-other"] },
            "body-bound" => capability with { MaximumBodyBytes = 2_048 },
            "store-bound" => capability with { StoreCapacityBytes = 131_072 },
            "store-id" => capability with { StoreId = "other-memory" },
            _ => throw new InvalidOperationException()
        };
        var accepted = Read(Configuration("http://127.0.0.1:5001", strip: true), HostCapabilitySnapshot.Create(new()
        {
            InstalledFamilies = GatewayDeclarationFamilies.OutputCache | GatewayDeclarationFamilies.CredentialDisposition,
            OutputCacheProfiles = [mismatched],
            ProtectedCredentialHeaders = ["X-Api-Key"]
        }));
        var result = await provider.GetRequiredService<GatewayNativeMaterializer>().MaterializeAsync(
            accepted,
            new PublicationCandidateIdentity(new CandidateId("mismatch"), "authority", "epoch", 1, accepted.CanonicalDocument!.ContentHash),
            "mismatch-native");

        result.IsMaterialized.Should().BeFalse();
        result.Diagnostics.Should().ContainSingle(error => error.Code == "materialization.output-cache-capability-mismatch");
    }

    [Fact]
    public async Task RealYarpCachesAnonymousSafeResponsesAndVariesByDeclaredDimensions()
    {
        await using var fixture = await CacheFixture.Start();
        await fixture.Publish(cached: true, version: 1);

        (await fixture.Get("/value?lang=en", language: "en")).Body.Should().Be("1");
        (await fixture.Get("/value?lang=en", language: "en")).Body.Should().Be("1");
        (await fixture.Get("/value?lang=fr", language: "en")).Body.Should().Be("2");
        (await fixture.Get("/value?lang=en", language: "fr")).Body.Should().Be("3");

        fixture.UpstreamCalls.Should().Be(3);
    }

    [Fact]
    public async Task ConcurrentMissesUseNativeResourceLockingAndOneUpstreamFill()
    {
        await using var fixture = await CacheFixture.Start();
        await fixture.Publish(cached: true, version: 1);

        var responses = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => fixture.Get("/slow")));

        responses.Select(static response => response.Body).Should().OnlyContain(body => body == responses[0].Body);
        fixture.UpstreamCalls.Should().Be(1);
    }

    [Fact]
    public async Task InFlightFillCompletesOnItsOriginalGenerationWhileNewRequestsUseRemovedMetadata()
    {
        await using var fixture = await CacheFixture.Start();
        await fixture.Publish(cached: true, version: 1);

        var oldGeneration = fixture.Get("/hold");
        await fixture.WaitUntilHeld();
        await fixture.Publish(cached: false, version: 2);
        var newGeneration = await fixture.Get("/value");
        newGeneration.Body.Should().Be("2");

        fixture.ReleaseHeld();
        (await oldGeneration).Body.Should().Be("1");
        (await fixture.Get("/hold")).Body.Should().Be("3");
    }

    [Fact]
    public async Task HpdMaterializedCachingMatchesDirectYarpWithTheIdenticalNativePolicy()
    {
        await using var fixture = await CacheFixture.Start();
        await fixture.Publish(cached: true, version: 1);
        var hpdFirst = await fixture.Get("/value");
        var hpdSecond = await fixture.Get("/value");
        hpdSecond.Body.Should().Be(hpdFirst.Body);

        await using var direct = await fixture.StartDirectProxy();
        var directFirst = await direct.Client.GetStringAsync("/value");
        var directSecond = await direct.Client.GetStringAsync("/value");

        directSecond.Should().Be(directFirst);
        fixture.UpstreamCalls.Should().Be(2, "each independent native store performs exactly one fill");
    }

    [Theory]
    [InlineData("CONNECT", false, "HTTP/1.1", null, null)]
    [InlineData("GET", true, "HTTP/1.1", null, null)]
    [InlineData("GET", false, "HTTP/3", null, null)]
    [InlineData("GET", false, "HTTP/1.1", "text/event-stream", null)]
    [InlineData("GET", false, "HTTP/1.1", "application/grpc", null)]
    [InlineData("GET", false, "HTTP/1.1", null, "application/grpc+proto")]
    public async Task ConservativePolicyFailsClosedForUnsupportedRequestShapes(
        string method,
        bool upgrade,
        string protocol,
        string? accept,
        string? contentType)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Protocol = protocol;
        if (accept is not null) context.Request.Headers.Accept = accept;
        if (contentType is not null) context.Request.ContentType = contentType;
        if (upgrade)
        {
            context.Request.Headers.Connection = "Upgrade";
            context.Request.Headers.Upgrade = "h2c";
        }
        var output = new Microsoft.AspNetCore.OutputCaching.OutputCacheContext { HttpContext = context };
        output.EnableOutputCaching = output.AllowCacheLookup = output.AllowCacheStorage = output.AllowLocking = true;

        await new GatewayConservativeOutputCachePolicy().CacheRequestAsync(output, CancellationToken.None);

        output.EnableOutputCaching.Should().BeFalse();
        output.AllowCacheLookup.Should().BeFalse();
        output.AllowCacheStorage.Should().BeFalse();
        output.AllowLocking.Should().BeFalse();
    }

    [Theory]
    [InlineData("text/event-stream", null)]
    [InlineData("application/grpc", null)]
    [InlineData(null, "application/grpc+proto")]
    public async Task UnsupportedNegotiationCannotReadAnOrdinaryCachedRepresentation(string? accept, string? contentType)
    {
        await using var fixture = await CacheFixture.Start();
        await fixture.Publish(cached: true, version: 1);
        (await fixture.Get("/negotiated")).Body.Should().Be("1");
        (await fixture.Get("/negotiated")).Body.Should().Be("1");

        var unsupported = await fixture.Get("/negotiated", accept: accept, contentType: contentType);

        unsupported.Body.Should().Be("2");
        fixture.UpstreamCalls.Should().Be(2);
    }

    [Fact]
    public async Task RealYarpPreservesDefaultSecurityAndStripsCookieAndCustomCredentialsBeforeFill()
    {
        await using var fixture = await CacheFixture.Start();
        await fixture.Publish(cached: true, version: 1);

        (await fixture.Get("/value", authorization: true)).Body.Should().Be("1");
        (await fixture.Get("/value", authorization: true)).Body.Should().Be("2");
        (await fixture.Get("/value", authenticated: true)).Body.Should().Be("3");
        (await fixture.Get("/value", authenticated: true)).Body.Should().Be("4");
        (await fixture.Get("/cookie")).Body.Should().Be("5");
        (await fixture.Get("/cookie")).Body.Should().Be("6");
        (await fixture.Get("/value", credentials: true)).Body.Should().Be("7");
        (await fixture.Get("/value", credentials: true)).Body.Should().Be("7");

        fixture.ObservedCredentials.Should().OnlyContain(static observed => observed == string.Empty);
    }

    [Theory]
    [InlineData("/oversize")]
    [InlineData("/created")]
    [InlineData("/range")]
    [InlineData("/sse")]
    [InlineData("/grpc")]
    [InlineData("/trailer")]
    public async Task UnsupportedOrUnstorableResponsesBypassWithoutFailure(string path)
    {
        await using var fixture = await CacheFixture.Start();
        await fixture.Publish(cached: true, version: 1);

        var first = await fixture.Get(path, range: path == "/range");
        var second = await fixture.Get(path, range: path == "/range");

        first.Status.Should().Be(path == "/created" ? HttpStatusCode.Created : HttpStatusCode.OK);
        second.Status.Should().Be(first.Status);
        second.Body.Should().NotBe(first.Body);
    }

    [Theory]
    [InlineData("/mismatch")]
    [InlineData("/abort")]
    public async Task PartialOrAbortedUpstreamResponsesAreNeverReused(string path)
    {
        await using var fixture = await CacheFixture.Start();
        await fixture.Publish(cached: true, version: 1);

        await fixture.SendPartial(path);
        await fixture.SendPartial(path);

        fixture.UpstreamCalls.Should().Be(2);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task NativeStoreReadOrWriteFailureDegradesToForwarding(bool failRead, bool failWrite)
    {
        await using var fixture = await NativeFailureFixture.Start(failRead, failWrite);

        using var response = await fixture.Client.GetAsync("/value");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        fixture.UpstreamCalls.Should().Be(1);
    }

    [Fact]
    public async Task ReloadRemovalAndReaddUseEndpointMetadataWithoutPurgingOrChangingNativeNoBindingBehavior()
    {
        await using var fixture = await CacheFixture.Start();
        await fixture.Publish(cached: true, version: 1);
        (await fixture.Get("/value")).Body.Should().Be("1");
        (await fixture.Get("/value")).Body.Should().Be("1");

        await fixture.Publish(cached: false, version: 2);
        (await fixture.Get("/value")).Body.Should().Be("2");
        (await fixture.Get("/value")).Body.Should().Be("3");

        await fixture.Publish(cached: true, version: 3);
        (await fixture.Get("/value")).Body.Should().Be("1", "re-adding identical profile metadata may make the still store-owned entry reachable again");
    }

    [Fact]
    public async Task StartupRequiresExactlyOnePipelineInstallationAndFrameworkMemoryStore()
    {
        var missingBuilder = WebApplication.CreateSlimBuilder();
        missingBuilder.WebHost.UseUrls("http://127.0.0.1:0");
        missingBuilder.Services.AddHpdGatewayOutputCaching(builder => builder.Add(Profile("safe")));
        await using var missing = missingBuilder.Build();
        Func<Task> startMissing = () => missing.StartAsync();
        await startMissing.Should().ThrowAsync<InvalidOperationException>();

        var duplicateBuilder = WebApplication.CreateSlimBuilder();
        duplicateBuilder.Services.AddHpdGatewayOutputCaching(builder => builder.Add(Profile("safe")));
        await using var duplicate = duplicateBuilder.Build();
        duplicate.UseHpdGatewayOutputCaching();
        Action useTwice = () => duplicate.UseHpdGatewayOutputCaching();
        useTwice.Should().Throw<InvalidOperationException>();

        var wrongStoreBuilder = WebApplication.CreateSlimBuilder();
        wrongStoreBuilder.WebHost.UseUrls("http://127.0.0.1:0");
        wrongStoreBuilder.Services.AddSingleton<IOutputCacheStore, TestOutputCacheStore>();
        wrongStoreBuilder.Services.AddHpdGatewayOutputCaching(builder => builder.Add(Profile("safe")));
        wrongStoreBuilder.Services.AddReverseProxy().LoadFromMemory([], []);
        await using var wrongStore = wrongStoreBuilder.Build();
        wrongStore.UseHpdGatewayOutputCaching();
        wrongStore.MapHpdGatewayReverseProxy();
        Func<Task> startWrongStore = () => wrongStore.StartAsync();
        await startWrongStore.Should().ThrowAsync<InvalidOperationException>();
    }

    private static GatewayOutputCacheProfile Profile(string name) => new() { Name = name, Version = 1 };

    private static OutputCacheCapability Capability(string name) => new(
        name, 1, true, "memory", OutputCacheStoreScope.ProcessLocal, TimeSpan.FromMinutes(1), 1_024, 65_536, ["lang"], ["accept-language"]);

    private static HostCapabilitySnapshot Capabilities() => HostCapabilitySnapshot.Create(new()
    {
        InstalledFamilies = GatewayDeclarationFamilies.OutputCache | GatewayDeclarationFamilies.CredentialDisposition,
        OutputCacheProfiles = [Capability("public-cache")],
        ProtectedCredentialHeaders = ["X-Api-Key"]
    });

    private static DeclarationReference<OutputCacheBinding> Cache() => new() { Inline = new OutputCacheBinding("public-cache") };
    private static DeclarationReference<CredentialDispositionBinding> Strip() => new() { Inline = new CredentialDispositionBinding { Kind = CredentialDispositionKind.Strip } };

    private static GatewayConfiguration Configuration(string address, bool strip) => new()
    {
        SchemaVersion = new GatewaySchemaVersion(1, 0),
        CanonicalizationVersion = 1,
        Routes = [Route(new RouteDeclarations { OutputCache = Cache(), CredentialDisposition = strip ? Strip() : null }, ["GET", "HEAD"])],
        Upstreams =
        [
            new UpstreamDeclaration
            {
                Id = new UpstreamId("backend"),
                Endpoints = new StaticEndpointSource
                {
                    Destinations = [new DestinationDeclaration { Id = new DestinationId("one"), Address = new Uri(address) }]
                },
                Transport = new UpstreamTransportDeclaration { UseProxy = false }
            }
        ]
    };

    private static RouteDeclaration Route(RouteDeclarations declarations, ImmutableArray<string> methods) => new()
    {
        Id = new RouteId("route"),
        Match = new HttpRouteMatch { Path = "/{**path}", Methods = methods },
        Upstream = new UpstreamId("backend"),
        Declarations = declarations
    };

    private static GatewayCandidateReadResult Read(GatewayConfiguration configuration, HostCapabilitySnapshot capabilities)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(configuration, GatewayJsonSerializerContext.Default.GatewayConfiguration);
        var result = GatewayCandidateReader.Read(bytes, capabilities);
        result.IsAccepted.Should().BeTrue(string.Join(", ", result.Errors.Select(error => $"{error.Path}: {error.Message}")));
        return result;
    }

    private sealed class CacheFixture : IAsyncDisposable
    {
        private readonly WebApplication _backend;
        private readonly WebApplication _proxy;
        private readonly TaskCompletionSource _held;
        private readonly TaskCompletionSource _release;
        private int _calls;

        private CacheFixture(
            WebApplication backend,
            WebApplication proxy,
            ConcurrentQueue<string> observedCredentials,
            TaskCompletionSource held,
            TaskCompletionSource release)
        {
            _backend = backend;
            _proxy = proxy;
            ObservedCredentials = observedCredentials;
            _held = held;
            _release = release;
        }

        internal HttpClient Client { get; private set; } = null!;
        internal ConcurrentQueue<string> ObservedCredentials { get; }
        internal int UpstreamCalls => Volatile.Read(ref _calls);

        internal static async Task<CacheFixture> Start()
        {
            CacheFixture? fixture = null;
            var observed = new ConcurrentQueue<string>();
            var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var backendBuilder = WebApplication.CreateSlimBuilder();
            backendBuilder.WebHost.UseUrls("http://127.0.0.1:0");
            var backend = backendBuilder.Build();
            backend.Run(async context =>
            {
                var call = Interlocked.Increment(ref fixture!._calls);
                observed.Enqueue($"{context.Request.Headers.Cookie}{context.Request.Headers["X-Api-Key"]}");
                var path = context.Request.Path.Value;
                if (path == "/created") context.Response.StatusCode = StatusCodes.Status201Created;
                if (path == "/cookie") context.Response.Headers.SetCookie = "session=outbound";
                if (path == "/sse") context.Response.ContentType = "text/event-stream";
                if (path == "/grpc") context.Response.ContentType = "application/grpc";
                if (path == "/trailer") context.Response.Headers.Trailer = "X-Final";
                if (path == "/slow") await Task.Delay(150);
                if (path == "/hold")
                {
                    held.TrySetResult();
                    await release.Task;
                }
                if (path == "/mismatch")
                {
                    context.Response.ContentLength = 100;
                    await context.Response.WriteAsync("partial");
                    return;
                }
                if (path == "/abort")
                {
                    context.Response.ContentLength = 100;
                    await context.Response.WriteAsync("partial");
                    context.Abort();
                    return;
                }
                var body = path == "/oversize" ? new string('x', 2_048) + call : call.ToString(System.Globalization.CultureInfo.InvariantCulture);
                await context.Response.WriteAsync(body);
            });
            await backend.StartAsync();

            var proxyBuilder = WebApplication.CreateSlimBuilder();
            proxyBuilder.WebHost.UseUrls("http://127.0.0.1:0");
            proxyBuilder.Services.AddReverseProxy();
            proxyBuilder.Services.AddHpdGatewayYarpPublication();
            proxyBuilder.Services.AddHpdGatewayYarpMaterialization();
            proxyBuilder.Services.AddHpdGatewayOutputCaching(builder =>
            {
                builder.MaximumBodyBytes = 1_024;
                builder.StoreCapacityBytes = 65_536;
                builder.Add(new GatewayOutputCacheProfile
                {
                    Name = "public-cache",
                    Version = 1,
                    Expiration = TimeSpan.FromMinutes(5),
                    QueryKeys = ["lang"],
                    HeaderNames = ["Accept-Language"]
                });
            });
            var proxy = proxyBuilder.Build();
            proxy.Use(async (context, next) =>
            {
                if (context.Request.Headers.ContainsKey("X-Authenticated"))
                    context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "test")], "test"));
                await next();
            });
            proxy.UseHpdGatewayOutputCaching();
            proxy.MapHpdGatewayReverseProxy();
            fixture = new CacheFixture(backend, proxy, observed, held, release);
            await proxy.StartAsync();
            fixture.Client = new HttpClient { BaseAddress = new Uri(Address(proxy)) };
            return fixture;
        }

        internal Task WaitUntilHeld() => _held.Task.WaitAsync(TimeSpan.FromSeconds(5));

        internal void ReleaseHeld() => _release.TrySetResult();

        internal async Task<DirectFixture> StartDirectProxy()
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Services.AddOutputCache(options =>
            {
                options.MaximumBodySize = 1_024;
                options.SizeLimit = 65_536;
                options.AddPolicy("public-cache", policy =>
                {
                    policy.SetCacheKeyPrefix("hpd:public-cache:v1");
                    policy.SetVaryByHost(true);
                    policy.SetVaryByQuery(["lang"]);
                    policy.SetVaryByHeader(["accept-language"]);
                    policy.Expire(TimeSpan.FromMinutes(5));
                    policy.SetLocking(true);
                    policy.AddPolicy<GatewayConservativeOutputCachePolicy>();
                });
            });
            builder.Services.AddSingleton<GatewayConservativeOutputCachePolicy>();
            builder.Services.AddReverseProxy().LoadFromMemory(
                [new RouteConfig { RouteId = "direct", ClusterId = "direct", Match = new RouteMatch { Path = "/{**path}" }, OutputCachePolicy = "public-cache" }],
                [new ClusterConfig { ClusterId = "direct", Destinations = new Dictionary<string, DestinationConfig> { ["one"] = new() { Address = Address(_backend) } } }]);
            var application = builder.Build();
            application.UseOutputCache();
            application.MapReverseProxy();
            await application.StartAsync();
            return new DirectFixture(application);
        }

        internal async Task Publish(bool cached, ulong version)
        {
            var capabilities = HostCapabilitySnapshot.Create(new()
            {
                InstalledFamilies = GatewayDeclarationFamilies.OutputCache | GatewayDeclarationFamilies.CredentialDisposition,
                OutputCacheProfiles = _proxy.Services.GetHpdGatewayOutputCacheCapabilities(),
                ProtectedCredentialHeaders = ["X-Api-Key"]
            });
            var configuration = cached
                ? Configuration(Address(_backend), strip: true)
                : Configuration(Address(_backend), strip: false) with
                {
                    Routes = [Route(new RouteDeclarations(), ["GET", "HEAD"])]
                };
            var accepted = Read(configuration, capabilities);
            var identity = new PublicationCandidateIdentity(
                new CandidateId($"cache-{version}"),
                "cache-authority",
                "epoch",
                version,
                accepted.CanonicalDocument!.ContentHash);
            var materialized = await _proxy.Services.GetRequiredService<GatewayNativeMaterializer>()
                .MaterializeAsync(accepted, identity, $"cache-native-{version}");
            materialized.IsMaterialized.Should().BeTrue(string.Join(", ", materialized.Diagnostics.Select(error => error.Code)));
            var outcome = await _proxy.Services.GetRequiredService<GatewayYarpPublisher>()
                .PublishAsync(materialized.Bundle!, TimeSpan.FromSeconds(5));
            outcome.State.Should().Be(GatewayPublicationState.ActiveAcknowledged);
        }

        internal async Task<(HttpStatusCode Status, string Body)> Get(
            string path,
            string? language = null,
            bool authorization = false,
            bool authenticated = false,
            bool credentials = false,
            bool range = false,
            string? accept = null,
            string? contentType = null)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            if (language is not null) request.Headers.TryAddWithoutValidation("Accept-Language", language);
            if (authorization) request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "secret");
            if (authenticated) request.Headers.TryAddWithoutValidation("X-Authenticated", "yes");
            if (credentials)
            {
                request.Headers.TryAddWithoutValidation("Cookie", "session=secret");
                request.Headers.TryAddWithoutValidation("X-Api-Key", "secret");
            }
            if (range) request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 1);
            if (accept is not null) request.Headers.TryAddWithoutValidation("Accept", accept);
            if (contentType is not null) request.Content = new ByteArrayContent([]) { Headers = { ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse(contentType) } };
            using var response = await Client.SendAsync(request);
            return (response.StatusCode, await response.Content.ReadAsStringAsync());
        }

        internal async Task SendPartial(string path)
        {
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            try
            {
                using var response = await Client.GetAsync(path, cancellation.Token);
                _ = await response.Content.ReadAsByteArrayAsync(cancellation.Token);
            }
            catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException)
            {
                // A length mismatch may surface to the downstream client. The
                // cache invariant is that the incomplete result is not reused.
            }
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _proxy.DisposeAsync();
            await _backend.DisposeAsync();
        }

        internal static string Address(WebApplication application) => application.Services
            .GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
    }

    private sealed class DirectFixture : IAsyncDisposable
    {
        private readonly WebApplication _application;

        internal DirectFixture(WebApplication application)
        {
            _application = application;
            Client = new HttpClient { BaseAddress = new Uri(CacheFixture.Address(application)) };
        }

        internal HttpClient Client { get; }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _application.DisposeAsync();
        }
    }

    private sealed class TestOutputCacheStore : IOutputCacheStore
    {
        public ValueTask<byte[]?> GetAsync(string key, CancellationToken cancellationToken) => ValueTask.FromResult<byte[]?>(null);
        public ValueTask SetAsync(string key, byte[] value, string[]? tags, TimeSpan validFor, CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask EvictByTagAsync(string tag, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }

    private sealed class NativeFailureFixture : IAsyncDisposable
    {
        private readonly WebApplication _backend;
        private readonly WebApplication _proxy;
        private int _calls;

        private NativeFailureFixture(WebApplication backend, WebApplication proxy)
        {
            _backend = backend;
            _proxy = proxy;
            Client = new HttpClient { BaseAddress = new Uri(CacheFixture.Address(proxy)) };
        }

        internal HttpClient Client { get; }
        internal int UpstreamCalls => Volatile.Read(ref _calls);

        internal static async Task<NativeFailureFixture> Start(bool failRead, bool failWrite)
        {
            NativeFailureFixture? fixture = null;
            var backendBuilder = WebApplication.CreateSlimBuilder();
            backendBuilder.WebHost.UseUrls("http://127.0.0.1:0");
            var backend = backendBuilder.Build();
            backend.Run(async context =>
            {
                Interlocked.Increment(ref fixture!._calls);
                await context.Response.WriteAsync("ok");
            });
            await backend.StartAsync();

            var proxyBuilder = WebApplication.CreateSlimBuilder();
            proxyBuilder.WebHost.UseUrls("http://127.0.0.1:0");
            proxyBuilder.Services.AddSingleton<IOutputCacheStore>(new FailingOutputCacheStore(failRead, failWrite));
            proxyBuilder.Services.AddOutputCache(options => options.AddPolicy("safe", policy =>
            {
                policy.SetCacheKeyPrefix("hpd:safe:v1");
                policy.AddPolicy<GatewayConservativeOutputCachePolicy>();
            }));
            proxyBuilder.Services.AddSingleton<GatewayConservativeOutputCachePolicy>();
            proxyBuilder.Services.AddReverseProxy().LoadFromMemory(
                [new RouteConfig { RouteId = "direct", ClusterId = "direct", Match = new RouteMatch { Path = "/{**path}" }, OutputCachePolicy = "safe" }],
                [new ClusterConfig { ClusterId = "direct", Destinations = new Dictionary<string, DestinationConfig> { ["one"] = new() { Address = CacheFixture.Address(backend) } } }]);
            var proxy = proxyBuilder.Build();
            proxy.UseOutputCache();
            proxy.MapReverseProxy();
            await proxy.StartAsync();
            fixture = new NativeFailureFixture(backend, proxy);
            return fixture;
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _proxy.DisposeAsync();
            await _backend.DisposeAsync();
        }
    }

    private sealed class FailingOutputCacheStore(bool failRead, bool failWrite) : IOutputCacheStore
    {
        public ValueTask<byte[]?> GetAsync(string key, CancellationToken cancellationToken) =>
            failRead ? ValueTask.FromException<byte[]?>(new IOException("bounded read failure")) : ValueTask.FromResult<byte[]?>(null);

        public ValueTask SetAsync(string key, byte[] value, string[]? tags, TimeSpan validFor, CancellationToken cancellationToken) =>
            failWrite ? ValueTask.FromException(new IOException("bounded write failure")) : ValueTask.CompletedTask;

        public ValueTask EvictByTagAsync(string tag, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }
}
