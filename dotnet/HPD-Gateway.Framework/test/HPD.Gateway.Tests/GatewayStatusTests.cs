using System.Collections.Immutable;
using System.Net;
using System.Text.Json;
using FluentAssertions;
using HPD.Gateway.Abstractions;
using HPD.Gateway.Hosting;
using HPD.Gateway.Status;
using HPD.Gateway.Yarp;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;
using Xunit;
using Yarp.ReverseProxy;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Model;

namespace HPD.Gateway.Tests;

public sealed class GatewayStatusTests
{
    [Fact]
    public async Task HealthEndpointsTransitionAfterExactPublicationAndRemainReadyAfterDuplicate()
    {
        await using var application = await StartApplication();
        using var client = new HttpClient { BaseAddress = Address(application) };

        (await client.GetAsync("/health/live")).StatusCode.Should().Be(HttpStatusCode.OK);
        var initial = await client.GetAsync("/health/ready");
        initial.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        var initialBody = await JsonSerializer.DeserializeAsync(initial.Content.ReadAsStream(), GatewayStatusJsonContext.Default.GatewayReadinessResponse);
        initialBody!.Ready.Should().BeFalse();
        initialBody.Reasons.Should().Contain("gateway.config.no_active_acknowledgement");

        var publisher = application.Services.GetRequiredService<GatewayYarpPublisher>();
        var bundle = Bundle(1, destination: true);
        (await publisher.PublishAsync(bundle, TimeSpan.FromSeconds(5))).State.Should().Be(GatewayPublicationState.ActiveAcknowledged);

        var ready = await client.GetAsync("/health/ready");
        ready.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await ready.Content.ReadAsStringAsync();
        body.Should().NotContain("candidate-1").And.NotContain("native-").And.NotContain("orders");

        (await publisher.PublishAsync(bundle, TimeSpan.FromSeconds(5))).State.Should().Be(GatewayPublicationState.Duplicate);
        var snapshot = application.Services.GetRequiredService<IGatewayStatusReader>().GetCurrent();
        snapshot.Publication.State.Should().Be(GatewayStatusPublicationState.Duplicate);
        snapshot.Publication.Active.Should().NotBeNull();
        snapshot.Readiness.Serving.Should().Be(GatewayReadinessState.Ready);
        snapshot.Upstreams.Should().ContainSingle(item => item.UpstreamId == "orders" && item.AvailableDestinationCount == 1);
        snapshot.Conditions.Should().HaveCount(7).And.OnlyHaveUniqueItems(item => item.Type);
    }

    [Fact]
    public async Task ZeroAndUnhealthyDestinationStateAreNotReadyAndChangeTokenSignalsAfterPublication()
    {
        await using var application = await StartApplication();
        var reader = application.Services.GetRequiredService<IGatewayStatusReader>();
        var publisher = application.Services.GetRequiredService<GatewayYarpPublisher>();
        var signaled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = reader.GetChangeToken().RegisterChangeCallback(static state =>
        {
            var pair = ((IGatewayStatusReader Reader, TaskCompletionSource Signal))state!;
            _ = pair.Reader.GetCurrent();
            pair.Signal.TrySetResult();
        }, (reader, signaled));

        (await publisher.PublishAsync(Bundle(1, destination: false), TimeSpan.FromSeconds(5))).State.Should().Be(GatewayPublicationState.ActiveAcknowledged);
        await signaled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var empty = reader.GetCurrent();
        empty.Readiness.Configuration.Should().Be(GatewayReadinessState.Ready);
        empty.Readiness.Serving.Should().Be(GatewayReadinessState.NotReady);
        empty.Upstreams.Should().ContainSingle(item => item.Eligibility == GatewayNativeEligibilityState.NoEligibleDestinations);

        var lookup = application.Services.GetRequiredService<IProxyStateLookup>();
        lookup.TryGetCluster("orders", out var cluster).Should().BeTrue();
        var destination = new DestinationState("one");
        destination.Health.Active = DestinationHealth.Unhealthy;
        cluster!.DestinationsState = new ClusterDestinationsState([destination], []);
        var unhealthy = reader.GetCurrent();
        unhealthy.Upstreams.Should().ContainSingle(item =>
            item.ActiveUnhealthyCount == 1 && item.AvailableDestinationCount == 0);
    }

    [Fact]
    public async Task PanicFallbackAndRestartRequiredRemainServingWhileHostFailureDoesNot()
    {
        var hostCandidate = HostCandidate(443);
        var host = new GatewayHostRuntimeStatus(hostCandidate);
        host.SetState(GatewayHostRealizationState.Ready);
        await using var application = await StartApplication(host);
        var publisher = application.Services.GetRequiredService<GatewayYarpPublisher>();
        (await publisher.PublishAsync(Bundle(1, destination: true, healthEnabled: true), TimeSpan.FromSeconds(5))).State
            .Should().Be(GatewayPublicationState.ActiveAcknowledged);

        var lookup = application.Services.GetRequiredService<IProxyStateLookup>();
        lookup.TryGetCluster("orders", out var cluster).Should().BeTrue();
        var destination = new DestinationState("one");
        destination.Health.Active = DestinationHealth.Unhealthy;
        cluster!.DestinationsState = new ClusterDestinationsState([destination], [destination]);
        var reader = application.Services.GetRequiredService<IGatewayStatusReader>();
        var panic = reader.GetCurrent();
        panic.Upstreams.Should().ContainSingle(item => item.Eligibility == GatewayNativeEligibilityState.PanicFallbackInUse);
        panic.Readiness.Serving.Should().Be(GatewayReadinessState.Ready);

        host.EvaluateDesired(HostCandidate(444));
        var restart = reader.GetCurrent();
        restart.Host.State.Should().Be(GatewayStatusHostState.RestartRequired);
        restart.Readiness.Serving.Should().Be(GatewayReadinessState.Ready);

        host.SetState(GatewayHostRealizationState.Failed);
        var failed = reader.GetCurrent();
        failed.Host.State.Should().Be(GatewayStatusHostState.Failed);
        failed.Readiness.Serving.Should().Be(GatewayReadinessState.NotReady);
    }

    [Fact]
    public async Task PublicationIndeterminateClearsActiveReadinessButPreservesHistoricalLkg()
    {
        await using var application = await StartApplication();
        var publisher = application.Services.GetRequiredService<GatewayYarpPublisher>();
        (await publisher.PublishAsync(Bundle(1, destination: true), TimeSpan.FromSeconds(5))).State.Should().Be(GatewayPublicationState.ActiveAcknowledged);

        var uncertain = Bundle(2, destination: true);
        using var throwing = application.Services.GetRequiredService<IProxyConfigProvider>().GetConfig().ChangeToken
            .RegisterChangeCallback(static _ => throw new InvalidOperationException("test"), null);
        (await publisher.PublishAsync(uncertain, TimeSpan.FromSeconds(5))).State.Should().Be(GatewayPublicationState.PublicationIndeterminate);

        var snapshot = application.Services.GetRequiredService<IGatewayStatusReader>().GetCurrent();
        snapshot.Publication.State.Should().Be(GatewayStatusPublicationState.PublicationIndeterminate);
        snapshot.Publication.Active.Should().BeNull();
        snapshot.Publication.LastKnownGood.Should().NotBeNull();
        snapshot.Readiness.Configuration.Should().Be(GatewayReadinessState.NotReady);
        snapshot.Readiness.Serving.Should().Be(GatewayReadinessState.NotReady);
    }

    private static async Task<WebApplication> StartApplication(GatewayHostRuntimeStatus? host = null)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddReverseProxy();
        builder.Services.AddHpdGatewayYarpPublication();
        if (host is not null) builder.Services.AddSingleton(host);
        builder.Services.AddHpdGatewayStatus();
        var application = builder.Build();
        application.MapHpdGatewayHealth();
        application.MapReverseProxy();
        await application.StartAsync();
        return application;
    }

    private static Uri Address(WebApplication application) =>
        new(application.Urls.Single(static value => value.StartsWith("http://", StringComparison.Ordinal)));

    private static NativePublicationBundle Bundle(ulong version, bool destination, bool healthEnabled = false)
    {
        var route = new RouteConfig { RouteId = "route", ClusterId = "orders", Match = new RouteMatch { Path = "/{**catch-all}" } };
        var destinations = destination
            ? new Dictionary<string, DestinationConfig> { ["one"] = new() { Address = "http://127.0.0.1:1/" } }
            : new Dictionary<string, DestinationConfig>();
        var cluster = new ClusterConfig
        {
            ClusterId = "orders",
            Destinations = destinations,
            HealthCheck = healthEnabled ? new HealthCheckConfig
            {
                AvailableDestinationsPolicy = "HealthyOrPanic",
                Active = new ActiveHealthCheckConfig { Enabled = true, Interval = TimeSpan.FromHours(1), Timeout = TimeSpan.FromSeconds(1), Path = "/health", Policy = "ConsecutiveFailures" }
            } : null
        };
        return NativePublicationBundle.Create(
            new PublicationCandidateIdentity(new CandidateId($"candidate-{version}"), "authority", "epoch", version, new ContentHash("sha-256", new string((char)('a' + version - 1), 64))),
            [route], [cluster], $"native-{version}-{Guid.NewGuid():N}");
    }

    private static GatewayHostCandidate HostCandidate(ushort port) => GatewayHostCandidateReader.Create(new GatewayHostConfiguration
    {
        SchemaVersion = new(1, 0),
        CanonicalizationVersion = 1,
        HostId = new("host"),
        DataListeners =
        [
            new GatewayHttpsListenerDeclaration
            {
                Id = new ListenerId("https"),
                Binding = GatewayListenerBindingKind.Loopback,
                Port = port,
                Protocols = GatewayListenerProtocols.Http1,
                Tls = new GatewayInboundTlsDeclaration
                {
                    Sni = [new GatewaySniTlsDeclaration
                    {
                        HostnamePattern = "status.example",
                        Certificate = new(new ProviderId("test"), new ProviderObjectId("certificate"), "v1")
                    }]
                }
            }
        ]
    }).Candidate!;
}
