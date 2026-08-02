using System.Collections.Immutable;
using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using HPD.Gateway.Abstractions;
using HPD.Gateway.Abstractions.Serialization;
using HPD.Gateway.Core;
using HPD.Gateway.Resilience;
using HPD.Gateway.Yarp;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HPD.Gateway.Tests;

public sealed class UpstreamResilienceTests
{
    [Fact]
    public void RegistryRejectsOpenInvalidOrDuplicateProfiles()
    {
        var builder = new GatewayResilienceRegistryBuilder();
        builder.Add(RetryProfile("safe", 1));

        Action duplicate = () => builder.Add(RetryProfile("safe", 2));
        Action invalidName = () => new GatewayResilienceRegistryBuilder().Add(RetryProfile("Not Safe", 1));
        Action empty = () => new GatewayResilienceRegistryBuilder().Add(new GatewayResilienceProfile { Name = "empty", Version = 1 });
        Action tooMany = () => new GatewayResilienceRegistryBuilder().Add(RetryProfile("too-many", 1) with
        {
            Retry = new GatewayResponseRetryProfile { StatusCodes = [HttpStatusCode.ServiceUnavailable], MaximumRetryAttempts = 6 }
        });

        duplicate.Should().Throw<ArgumentException>();
        invalidName.Should().Throw<ArgumentException>();
        empty.Should().Throw<ArgumentException>();
        tooMany.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(null, "GET", UpstreamHttpVersion.Http2)]
    [InlineData("POST", "POST", UpstreamHttpVersion.Http2)]
    [InlineData("GET", "GET", UpstreamHttpVersion.Http3)]
    public void CandidateRejectsUnsafeRetryShapes(string? configuredMethod, string requestMethod, UpstreamHttpVersion version)
    {
        _ = requestMethod;
        var configuration = Configuration("http://127.0.0.1:5001", configuredMethod is null ? [] : [configuredMethod], version);
        var capabilities = Capabilities(new UpstreamResilienceCapability("safe", 1, UpstreamResilienceStrategies.SelectedResponseRetry, [503], 1));

        var result = GatewayCandidateValidator.Validate(configuration, capabilities);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.Path.Contains("methods", StringComparison.Ordinal) || error.Path.EndsWith("request.version", StringComparison.Ordinal));
    }

    [Fact]
    public void CandidateRequiresExactInstalledProfileVersion()
    {
        var configuration = Configuration("http://127.0.0.1:5001", ["GET"]);

        var missing = GatewayCandidateValidator.Validate(configuration, Capabilities());
        var wrongVersion = GatewayCandidateValidator.Validate(configuration,
            Capabilities(new UpstreamResilienceCapability("safe", 2, UpstreamResilienceStrategies.SelectedResponseRetry, [503], 1)));

        missing.IsValid.Should().BeFalse();
        wrongVersion.IsValid.Should().BeFalse();
        wrongVersion.Errors.Should().Contain(error => error.Path.EndsWith("profileVersion", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SelectedResponseRetriesBodylessSafeRequestButNeverRequestContent()
    {
        await using var fixture = await ResilienceFixture.Start(RetryProfile("safe", 1));

        using var bodyless = await fixture.Client.GetAsync("/retry");
        using var bodyRequest = new HttpRequestMessage(HttpMethod.Get, "/retry-body")
        {
            Content = new StringContent("body", Encoding.UTF8, "text/plain")
        };
        using var withBody = await fixture.Client.SendAsync(bodyRequest);

        bodyless.StatusCode.Should().Be(HttpStatusCode.OK);
        fixture.Hits("/retry").Should().Be(2);
        withBody.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        fixture.Hits("/retry-body").Should().Be(1);
    }

    [Fact]
    public async Task RetryDisposesPriorResponseAndHonorsBoundedAttempts()
    {
        await using var fixture = await ResilienceFixture.Start(RetryProfile("safe", 1) with
        {
            Retry = new GatewayResponseRetryProfile
            {
                StatusCodes = [HttpStatusCode.ServiceUnavailable],
                MaximumRetryAttempts = 2,
                MaximumRetryAfter = TimeSpan.FromMilliseconds(10)
            }
        });

        using var response = await fixture.Client.GetAsync("/always-fail");

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        fixture.Hits("/always-fail").Should().Be(3);
    }

    [Fact]
    public async Task RetryDisposesPriorResponseBeforeNextAttempt()
    {
        var registryBuilder = new GatewayResilienceRegistryBuilder();
        var profile = RetryProfile("safe", 1);
        registryBuilder.Add(profile);
        var registry = registryBuilder.Build();
        var terminal = new DisposalTrackingHandler();
        using var invoker = new HttpMessageInvoker(registry.Wrap(profile.Name, profile.Version, terminal));
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/test");

        using var response = await invoker.SendAsync(request, CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        terminal.Attempts.Should().Be(2);
        terminal.FirstContent!.IsDisposed.Should().BeTrue();
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task RetryRejectsUpgradeAndHttp3AtRuntime(bool upgrade, bool http3)
    {
        var profile = RetryProfile("safe", 1);
        var builder = new GatewayResilienceRegistryBuilder();
        builder.Add(profile);
        var registry = builder.Build();
        var terminal = new StatusSequenceHandler();
        using var invoker = new HttpMessageInvoker(registry.Wrap(profile.Name, profile.Version, terminal));
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/runtime-gate");
        if (upgrade)
        {
            request.Headers.Connection.Add("Upgrade");
            request.Headers.TryAddWithoutValidation("Upgrade", "h2c");
        }
        if (http3) request.Version = HttpVersion.Version30;

        using var response = await invoker.SendAsync(request, CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        terminal.Attempts.Should().Be(1);
    }

    [Fact]
    public async Task RetryNeverHandlesExceptionsAndBoundsRetryAfter()
    {
        var profile = RetryProfile("safe", 1);
        var builder = new GatewayResilienceRegistryBuilder();
        builder.Add(profile);
        var registry = builder.Build();
        var throwing = new ThrowingHandler();
        using (var throwingInvoker = new HttpMessageInvoker(registry.Wrap(profile.Name, profile.Version, throwing)))
        using (var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/exception"))
        {
            var action = () => throwingInvoker.SendAsync(request, CancellationToken.None);
            await action.Should().ThrowAsync<HttpRequestException>();
            throwing.Attempts.Should().Be(1);
        }

        var retryAfter = new RetryAfterHandler();
        using var retryInvoker = new HttpMessageInvoker(registry.Wrap(profile.Name, profile.Version, retryAfter));
        using var retryRequest = new HttpRequestMessage(HttpMethod.Get, "http://localhost/retry-after");
        var started = DateTime.UtcNow;
        using var response = await retryInvoker.SendAsync(retryRequest, CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        retryAfter.Attempts.Should().Be(2);
        (DateTime.UtcNow - started).Should().BeLessThan(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task CircuitOpensWithoutAdditionalUpstreamAttempt()
    {
        await using var fixture = await ResilienceFixture.Start(new GatewayResilienceProfile
        {
            Name = "breaker",
            Version = 1,
            CircuitBreaker = new GatewayCircuitBreakerProfile
            {
                StatusCodes = [HttpStatusCode.ServiceUnavailable],
                FailureRatio = 1,
                MinimumThroughput = 2,
                SamplingDuration = TimeSpan.FromSeconds(10),
                BreakDuration = TimeSpan.FromSeconds(2)
            }
        }, profileName: "breaker");

        using var first = await fixture.Client.GetAsync("/always-fail");
        using var second = await fixture.Client.GetAsync("/always-fail");
        using var third = await fixture.Client.GetAsync("/always-fail");

        first.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        second.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        third.StatusCode.Should().Be(HttpStatusCode.BadGateway);
        fixture.Hits("/always-fail").Should().Be(2);
    }

    [Fact]
    public async Task OutboundLimiterRejectsSaturationWithoutInvokingInnerHandler()
    {
        var profile = new GatewayResilienceProfile
        {
            Name = "limited",
            Version = 1,
            ConcurrencyLimiter = new GatewayOutboundConcurrencyProfile { PermitLimit = 1, QueueLimit = 0 }
        };
        var builder = new GatewayResilienceRegistryBuilder();
        builder.Add(profile);
        var registry = builder.Build();
        var terminal = new BlockingHandler();
        using var invoker = new HttpMessageInvoker(registry.Wrap(profile.Name, profile.Version, terminal));
        using var firstRequest = new HttpRequestMessage(HttpMethod.Get, "http://localhost/first");
        var first = invoker.SendAsync(firstRequest, CancellationToken.None);
        await terminal.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        using var secondRequest = new HttpRequestMessage(HttpMethod.Get, "http://localhost/second");

        using var second = await invoker.SendAsync(secondRequest, CancellationToken.None);
        terminal.Release.TrySetResult();
        using var firstResponse = await first;

        second.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        firstResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        terminal.Attempts.Should().Be(1);
    }

    [Fact]
    public async Task OutboundLimiterQueueObservesCallerCancellation()
    {
        var profile = new GatewayResilienceProfile
        {
            Name = "queued",
            Version = 1,
            ConcurrencyLimiter = new GatewayOutboundConcurrencyProfile { PermitLimit = 1, QueueLimit = 1 }
        };
        var builder = new GatewayResilienceRegistryBuilder();
        builder.Add(profile);
        var registry = builder.Build();
        var terminal = new BlockingHandler();
        using var invoker = new HttpMessageInvoker(registry.Wrap(profile.Name, profile.Version, terminal));
        using var firstRequest = new HttpRequestMessage(HttpMethod.Get, "http://localhost/first");
        var first = invoker.SendAsync(firstRequest, CancellationToken.None);
        await terminal.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        using var queuedRequest = new HttpRequestMessage(HttpMethod.Get, "http://localhost/queued");

        var queued = () => invoker.SendAsync(queuedRequest, cancellation.Token);
        await queued.Should().ThrowAsync<OperationCanceledException>();
        terminal.Release.TrySetResult();
        using var firstResponse = await first;
        terminal.Attempts.Should().Be(1);
    }

    [Fact]
    public async Task TimeoutDoesNotRetry()
    {
        await using var fixture = await ResilienceFixture.Start(new GatewayResilienceProfile
        {
            Name = "timeout",
            Version = 1,
            Retry = new GatewayResponseRetryProfile { StatusCodes = [HttpStatusCode.ServiceUnavailable], MaximumRetryAttempts = 2 },
            AttemptTimeout = new GatewayAttemptTimeoutProfile { Timeout = TimeSpan.FromMilliseconds(50) }
        }, profileName: "timeout");

        using var response = await fixture.Client.GetAsync("/slow");

        response.StatusCode.Should().Be(HttpStatusCode.BadGateway);
        fixture.Hits("/slow").Should().Be(1);
    }

    [Fact]
    public void FactoryReuseIncludesProfileIdentityAndVersion()
    {
        var registryBuilder = new GatewayResilienceRegistryBuilder();
        registryBuilder.Add(RetryProfile("safe", 1));
        var factory = new HpdForwarderHttpClientFactory([registryBuilder.Build()]);
        var config = new global::Yarp.ReverseProxy.Configuration.HttpClientConfig();
        using var first = factory.CreateClient(new()
        {
            ClusterId = "backend",
            NewConfig = config,
            NewMetadata = ResilienceMetadata("safe", 1)
        });
        var reused = factory.CreateClient(new()
        {
            ClusterId = "backend",
            OldClient = first,
            OldConfig = config,
            NewConfig = config,
            OldMetadata = ResilienceMetadata("safe", 1),
            NewMetadata = ResilienceMetadata("safe", 1)
        });

        ReferenceEquals(first, reused).Should().BeTrue();
        Action changed = () => factory.CreateClient(new()
        {
            ClusterId = "backend",
            OldClient = first,
            OldConfig = config,
            NewConfig = config,
            OldMetadata = ResilienceMetadata("safe", 1),
            NewMetadata = ResilienceMetadata("safe", 2)
        });
        changed.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public async Task LiveChangeRemoveAndReAddReplaceOnlySelectedProfileBehavior()
    {
        var first = RetryProfile("safe", 1);
        var second = RetryProfile("alternate", 2) with
        {
            Retry = new GatewayResponseRetryProfile { StatusCodes = [HttpStatusCode.BadGateway], MaximumRetryAttempts = 1 }
        };
        await using var fixture = await ResilienceFixture.Start(first, "safe", second);

        using (var initial = await fixture.Client.GetAsync("/always-fail"))
            initial.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        fixture.Hits("/always-fail").Should().Be(2);

        await fixture.Reload("alternate", 2, 2);
        using (var changed = await fixture.Client.GetAsync("/always-fail"))
            changed.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        fixture.Hits("/always-fail").Should().Be(3);

        await fixture.Reload(null, 0, 3);
        using (var removed = await fixture.Client.GetAsync("/always-fail"))
            removed.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        fixture.Hits("/always-fail").Should().Be(4);

        await fixture.Reload("safe", 1, 4);
        using (var readded = await fixture.Client.GetAsync("/always-fail"))
            readded.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        fixture.Hits("/always-fail").Should().Be(6);
    }

    private static IReadOnlyDictionary<string, string> ResilienceMetadata(string name, int version) =>
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [HpdForwarderHttpClientFactory.ResilienceProfileMetadata] = name,
            [HpdForwarderHttpClientFactory.ResilienceVersionMetadata] = version.ToString()
        };

    private static GatewayResilienceProfile RetryProfile(string name, int version) => new()
    {
        Name = name,
        Version = version,
        Retry = new GatewayResponseRetryProfile
        {
            StatusCodes = [HttpStatusCode.ServiceUnavailable],
            MaximumRetryAttempts = 1,
            Delay = TimeSpan.Zero,
            MaximumRetryAfter = TimeSpan.FromMilliseconds(10)
        }
    };

    private static HostCapabilitySnapshot Capabilities(params UpstreamResilienceCapability[] profiles) => HostCapabilitySnapshot.Create(new()
    {
        InstalledFamilies = GatewayDeclarationFamilies.UpstreamResilience,
        UpstreamResilienceProfiles = profiles
    });

    private static GatewayConfiguration Configuration(
        string destination,
        ImmutableArray<string> methods,
        UpstreamHttpVersion version = UpstreamHttpVersion.Http2,
        string? profileName = "safe",
        int profileVersion = 1) => new()
    {
        SchemaVersion = new GatewaySchemaVersion(1, 0),
        CanonicalizationVersion = 1,
        Routes =
        [
            new RouteDeclaration
            {
                Id = new RouteId("route"),
                Match = new HttpRouteMatch { Path = "/{**path}", Methods = methods },
                Upstream = new UpstreamId("backend"),
                Declarations = new RouteDeclarations()
            }
        ],
        Upstreams =
        [
            new UpstreamDeclaration
            {
                Id = new UpstreamId("backend"),
                Endpoints = new StaticEndpointSource
                {
                    Destinations = [new DestinationDeclaration { Id = new DestinationId("one"), Address = new Uri(destination) }]
                },
                Request = new UpstreamRequestDeclaration { Version = version },
                Resilience = profileName is null ? null : new UpstreamResilienceBinding { ProfileName = profileName, ProfileVersion = profileVersion }
            }
        ]
    };

    private sealed class ResilienceFixture : IAsyncDisposable
    {
        private readonly WebApplication _proxy;
        private readonly WebApplication _backend;
        private readonly Dictionary<string, int> _hits;
        private readonly object _gate = new();

        private ResilienceFixture(WebApplication proxy, WebApplication backend, Dictionary<string, int> hits)
        {
            _proxy = proxy;
            _backend = backend;
            _hits = hits;
            Client = new HttpClient { BaseAddress = new Uri(Address(proxy)) };
        }

        internal HttpClient Client { get; }
        internal int Hits(string path) { lock (_gate) return _hits.GetValueOrDefault(path); }

        internal static async Task<ResilienceFixture> Start(
            GatewayResilienceProfile profile,
            string profileName = "safe",
            params GatewayResilienceProfile[] additionalProfiles)
        {
            var hits = new Dictionary<string, int>(StringComparer.Ordinal);
            var gate = new object();
            var backendBuilder = WebApplication.CreateSlimBuilder();
            backendBuilder.WebHost.UseUrls("http://127.0.0.1:0");
            var backend = backendBuilder.Build();
            backend.Run(async context =>
            {
                int count;
                lock (gate) { hits.TryGetValue(context.Request.Path, out count); hits[context.Request.Path] = ++count; }
                if (context.Request.Path == "/slow") await Task.Delay(500, context.RequestAborted);
                context.Response.StatusCode = context.Request.Path.Value switch
                {
                    "/retry" when count > 1 => StatusCodes.Status200OK,
                    "/retry" or "/retry-body" or "/always-fail" => StatusCodes.Status503ServiceUnavailable,
                    _ => StatusCodes.Status200OK
                };
            });
            await backend.StartAsync();

            var proxyBuilder = WebApplication.CreateSlimBuilder();
            proxyBuilder.WebHost.UseUrls("http://127.0.0.1:0");
            proxyBuilder.Services.AddReverseProxy();
            proxyBuilder.Services.AddHpdGatewayYarpPublication();
            proxyBuilder.Services.AddHpdGatewayYarpResilience(registry =>
            {
                registry.Add(profile);
                foreach (var additional in additionalProfiles) registry.Add(additional);
            });
            proxyBuilder.Services.AddHpdGatewayYarpMaterialization();
            var proxy = proxyBuilder.Build();
            proxy.MapReverseProxy();
            await proxy.StartAsync();

            var fixture = new ResilienceFixture(proxy, backend, hits);
            await fixture.Reload(profileName, profile.Version, 1);
            return fixture;
        }

        internal async Task Reload(string? profileName, int profileVersion, ulong version)
        {
            var capabilities = HostCapabilitySnapshot.Create(new()
            {
                InstalledFamilies = profileName is null ? GatewayDeclarationFamilies.None : GatewayDeclarationFamilies.UpstreamResilience,
                UpstreamResilienceProfiles = _proxy.Services.GetHpdGatewayResilienceCapabilities()
            });
            var configuration = Configuration(Address(_backend), ["GET"], profileName: profileName, profileVersion: profileVersion);
            var json = JsonSerializer.SerializeToUtf8Bytes(configuration, GatewayJsonSerializerContext.Default.GatewayConfiguration);
            var accepted = GatewayCandidateReader.Read(json, capabilities);
            accepted.IsAccepted.Should().BeTrue(string.Join(", ", accepted.Errors.Select(error => error.Message)));
            var identity = new PublicationCandidateIdentity(new CandidateId($"resilience-{version}"), "authority", "epoch", version, accepted.CanonicalDocument!.ContentHash);
            var materialized = await _proxy.Services.GetRequiredService<GatewayNativeMaterializer>().MaterializeAsync(accepted, identity, $"resilience-native-{version}");
            materialized.IsMaterialized.Should().BeTrue(string.Join(", ", materialized.Diagnostics.Select(error => error.Code)));
            var outcome = await _proxy.Services.GetRequiredService<GatewayYarpPublisher>().PublishAsync(materialized.Bundle!, TimeSpan.FromSeconds(5));
            outcome.State.Should().Be(GatewayPublicationState.ActiveAcknowledged);
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

    private sealed class DisposalTrackingHandler : HttpMessageHandler
    {
        internal int Attempts { get; private set; }
        internal TrackingContent? FirstContent { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Attempts++;
            if (Attempts == 1)
            {
                FirstContent = new TrackingContent();
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                {
                    RequestMessage = request,
                    Content = FirstContent
                });
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = request });
        }
    }

    private sealed class StatusSequenceHandler : HttpMessageHandler
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

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        internal int Attempts { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Attempts++;
            throw new HttpRequestException("untrusted runtime exception");
        }
    }

    private sealed class RetryAfterHandler : HttpMessageHandler
    {
        internal int Attempts { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Attempts++;
            var response = new HttpResponseMessage(Attempts == 1 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK)
            {
                RequestMessage = request
            };
            if (Attempts == 1) response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromHours(1));
            return Task.FromResult(response);
        }
    }

    private sealed class TrackingContent : ByteArrayContent
    {
        internal TrackingContent() : base([1, 2, 3]) { }
        internal bool IsDisposed { get; private set; }
        protected override void Dispose(bool disposing)
        {
            IsDisposed = true;
            base.Dispose(disposing);
        }
    }

    private sealed class BlockingHandler : HttpMessageHandler
    {
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int Attempts { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Attempts++;
            Entered.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = request };
        }
    }
}
