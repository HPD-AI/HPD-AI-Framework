using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Text.Json;
using FluentAssertions;
using HPD.Gateway;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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

        var publisher = application.Services.GetRequiredService<GatewayRuntimePublisher>();
        var bundle = PreparedApplication(1, destination: true);
        (await Publish(publisher, bundle)).State.Should().Be(GatewayPublicationState.ActiveAcknowledged);

        var ready = await client.GetAsync("/health/ready");
        ready.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await ready.Content.ReadAsStringAsync();
        body.Should().NotContain("candidate-1").And.NotContain("native-").And.NotContain("orders");

        (await Publish(publisher, bundle)).State.Should().Be(GatewayPublicationState.Duplicate);
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
        var publisher = application.Services.GetRequiredService<GatewayRuntimePublisher>();
        var signaled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = reader.GetChangeToken().RegisterChangeCallback(static state =>
        {
            var pair = ((IGatewayStatusReader Reader, TaskCompletionSource Signal))state!;
            _ = pair.Reader.GetCurrent();
            pair.Signal.TrySetResult();
        }, (reader, signaled));

        (await Publish(publisher, PreparedApplication(1, destination: false))).State.Should().Be(GatewayPublicationState.ActiveAcknowledged);
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
        var publisher = application.Services.GetRequiredService<GatewayRuntimePublisher>();
        (await Publish(publisher, PreparedApplication(1, destination: true, healthEnabled: true))).State
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
        var publisher = application.Services.GetRequiredService<GatewayRuntimePublisher>();
        (await Publish(publisher, PreparedApplication(1, destination: true))).State.Should().Be(GatewayPublicationState.ActiveAcknowledged);

        var uncertain = PreparedApplication(2, destination: true);
        using var throwing = application.Services.GetRequiredService<IProxyConfigProvider>().GetConfig().ChangeToken
            .RegisterChangeCallback(static _ => throw new InvalidOperationException("test"), null);
        (await Publish(publisher, uncertain)).State.Should().Be(GatewayPublicationState.PublicationIndeterminate);

        var snapshot = application.Services.GetRequiredService<IGatewayStatusReader>().GetCurrent();
        snapshot.Publication.State.Should().Be(GatewayStatusPublicationState.PublicationIndeterminate);
        snapshot.Publication.Active.Should().BeNull();
        snapshot.Publication.LastKnownGood.Should().NotBeNull();
        snapshot.Readiness.Configuration.Should().Be(GatewayReadinessState.NotReady);
        snapshot.Readiness.Serving.Should().Be(GatewayReadinessState.NotReady);
    }

    [Fact]
    public async Task PublicationObserversCannotCorruptAcknowledgementAndSeePublishedObservation()
    {
        await using var application = await StartApplication();
        var observation = application.Services.GetRequiredService<IGatewayPublicationObservationReader>();
        GatewayPublicationObservation? seen = null;
        using var registration = observation.GetChangeToken().RegisterChangeCallback(_ =>
        {
            seen = observation.GetCurrent();
            throw new InvalidOperationException("observer must be isolated");
        }, null);

        var outcome = await application.Services.GetRequiredService<GatewayRuntimePublisher>()
            .PublishAsync(PreparedApplication(1, destination: true), "namespace", "node", TimeSpan.FromSeconds(5));

        outcome.State.Should().Be(GatewayPublicationState.ActiveAcknowledged);
        seen.Should().NotBeNull();
        seen!.LatestOutcome!.State.Should().Be(GatewayPublicationState.ActiveAcknowledged);
        seen.Active.Should().NotBeNull();
    }

    [Fact]
    public void StatusProjectionTruncatesDeterministicallyAtBound()
    {
        var active = new ActivePublicationIdentity(
            new PublicationCandidateIdentity(new CandidateId("candidate"), "authority", "epoch", 1, new ContentHash("sha-256", new string('a', 64))),
            "0123456789abcdef0123456789abcdef", new ContentHash("sha-256", new string('b', 64)),
            "native", DateTimeOffset.UtcNow);
        var upstreams = Enumerable.Range(0, 4_097)
            .Select(index => new GatewayPublishedUpstream($"upstream-{index:D5}", "HealthyOrPanic"))
            .ToImmutableArray();
        using var publication = new FixedPublicationReader(new(1, DateTimeOffset.UtcNow,
            new GatewayPublicationOutcome(GatewayPublicationState.ActiveAcknowledged, active.Candidate, active, active, active.NativeRevisionId, []),
            active, active, upstreams));
        using var applied = new FixedAppliedReader(Applied(active, upstreams.Select(static value => value.UpstreamId)));
        using var coordinator = new GatewayStatusCoordinator([publication], [applied], new EmptyProxyLookup(), [], new TestLifetime());

        var snapshot = coordinator.GetCurrent();

        snapshot.Upstreams.Should().HaveCount(4_096);
        snapshot.Upstreams.Select(static item => item.UpstreamId).Should().BeInAscendingOrder(StringComparer.Ordinal);
        snapshot.DetailsTruncated.Should().BeTrue();
        snapshot.Readiness.Reasons.Should().Contain(item => item.Code == "gateway.status.details_truncated");
        JsonSerializer.SerializeToUtf8Bytes(snapshot, GatewayStatusJsonContext.Default.GatewayStatusSnapshot).Should().NotBeEmpty();
    }

    [Fact]
    public async Task MultiplePublicationStatusAuthoritiesFailHostStartup()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddReverseProxy();
        builder.Services.AddHpdGatewayYarpPublication();
        builder.Services.AddSingleton<IGatewayPublicationObservationReader>(new FixedPublicationReader(
            new GatewayPublicationObservation(0, DateTimeOffset.UtcNow, null, null, null, [])));
        builder.Services.AddHpdGatewayStatus();
        await using var application = builder.Build();

        await FluentActions.Awaiting(() => application.StartAsync()).Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task EmbeddedShutdownPublishesNotReadyAndSignalsExistingToken()
    {
        await using var application = await StartApplication();
        var publisher = application.Services.GetRequiredService<GatewayRuntimePublisher>();
        (await Publish(publisher, PreparedApplication(1, destination: true))).State
            .Should().Be(GatewayPublicationState.ActiveAcknowledged);
        var reader = application.Services.GetRequiredService<IGatewayStatusReader>();
        reader.GetCurrent().Readiness.Serving.Should().Be(GatewayReadinessState.Ready);
        var signaled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = reader.GetChangeToken().RegisterChangeCallback(
            static state => ((TaskCompletionSource)state!).TrySetResult(), signaled);

        await application.StopAsync();
        await signaled.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var stopped = reader.GetCurrent();
        stopped.Readiness.Serving.Should().Be(GatewayReadinessState.NotReady);
        stopped.Conditions.Should().ContainSingle(condition =>
            condition.Type == GatewayConditionType.ServingReady && condition.Value == GatewayConditionValue.False);
    }

    [Fact]
    public void ThrowingNativeLookupReturnsBoundedNotObservedStatus()
    {
        var active = ActiveIdentity();
        using var publication = new FixedPublicationReader(new(1, DateTimeOffset.UtcNow,
            new GatewayPublicationOutcome(GatewayPublicationState.ActiveAcknowledged, active.Candidate, active, active, active.NativeRevisionId, []),
            active, active, [new GatewayPublishedUpstream("orders", "HealthyOrPanic")]));
        using var applied = new FixedAppliedReader(Applied(active, ["orders"]));
        using var coordinator = new GatewayStatusCoordinator([publication], [applied], new ThrowingProxyLookup(), [], new TestLifetime());

        var snapshot = coordinator.GetCurrent();

        snapshot.Upstreams.Should().ContainSingle(item =>
            item.UpstreamId == "orders" && item.Eligibility == GatewayNativeEligibilityState.NotObserved &&
            item.Reasons.Any(reason => reason.Code == "gateway.destination.observation_failed"));
        snapshot.Readiness.Serving.Should().Be(GatewayReadinessState.NotReady);
    }

    [Fact]
    public void ActivePublicationWithDifferentAppliedApplicationFailsReadinessClosed()
    {
        var active = ActiveIdentity();
        using var publication = new FixedPublicationReader(new(1, DateTimeOffset.UtcNow,
            new GatewayPublicationOutcome(GatewayPublicationState.ActiveAcknowledged, active.Candidate, active, active, active.NativeRevisionId, []),
            active, active, [new GatewayPublishedUpstream("orders", "HealthyOrPanic")]));
        GatewayAppliedRuntimeObservation wrong = Applied(active with
        {
            ApplicationId = "ffffffffffffffffffffffffffffffff",
        }, ["orders"]);
        using var applied = new FixedAppliedReader(wrong);
        using var coordinator = new GatewayStatusCoordinator([publication], [applied], new EmptyProxyLookup(), [], new TestLifetime());

        GatewayStatusSnapshot snapshot = coordinator.GetCurrent();

        snapshot.Readiness.Configuration.Should().Be(GatewayReadinessState.NotReady);
        snapshot.Readiness.Serving.Should().Be(GatewayReadinessState.NotReady);
        snapshot.Readiness.Reasons.Should().ContainSingle(reason => reason.Code == "gateway.runtime.applied_mismatch");
        snapshot.Upstreams.Should().ContainSingle(item => item.Discovery.State == GatewayDiscoveryObservationState.Indeterminate);
    }

    [Theory]
    [InlineData(GatewayAppliedMembershipDisposition.Fresh, 1, GatewayDiscoveryObservationState.AppliedFresh, 1, true)]
    [InlineData(GatewayAppliedMembershipDisposition.Fresh, 0, GatewayDiscoveryObservationState.AppliedFreshEmpty, 0, false)]
    [InlineData(GatewayAppliedMembershipDisposition.LastKnownMembership, 1, GatewayDiscoveryObservationState.AppliedLastKnownDegraded, 1, true)]
    [InlineData(GatewayAppliedMembershipDisposition.UnavailableWhenStale, 0, GatewayDiscoveryObservationState.AppliedUnavailable, 0, true)]
    [InlineData(GatewayAppliedMembershipDisposition.RefreshFailed, 1, GatewayDiscoveryObservationState.RefreshFailed, 1, false)]
    public void DiscoveryDispositionHasExactObservationAndReadinessRule(
        GatewayAppliedMembershipDisposition disposition,
        int destinationCount,
        GatewayDiscoveryObservationState expectedState,
        int available,
        bool expectedReady)
    {
        var upstream = new GatewayAppliedUpstream("orders", GatewayAppliedUpstreamKind.ServiceDiscovery,
            "aspire", "orders", null, 7, new ContentHash("sha-256", new string('c', 64)),
            destinationCount, disposition, "safe");

        GatewayDiscoveryStatus status = GatewayStatusCoordinator.BuildDiscoveryStatus(upstream, DateTimeOffset.UnixEpoch);

        status.State.Should().Be(expectedState);
        GatewayStatusCoordinator.IsUpstreamReady(status.State, available).Should().Be(expectedReady);
    }

    [Theory]
    [InlineData(GatewayAppliedMembershipDisposition.Fresh, true, GatewayDiscoveryObservationState.AppliedFresh, true, true)]
    [InlineData(GatewayAppliedMembershipDisposition.Fresh, false, GatewayDiscoveryObservationState.AppliedFreshEmpty, false, false)]
    [InlineData(GatewayAppliedMembershipDisposition.LastKnownMembership, true, GatewayDiscoveryObservationState.AppliedLastKnownDegraded, true, true)]
    [InlineData(GatewayAppliedMembershipDisposition.UnavailableWhenStale, false, GatewayDiscoveryObservationState.AppliedUnavailable, true, false)]
    [InlineData(GatewayAppliedMembershipDisposition.RefreshFailed, true, GatewayDiscoveryObservationState.RefreshFailed, false, true)]
    public async Task RealNativeStateUsesExactDiscoveryReadinessRule(
        GatewayAppliedMembershipDisposition disposition,
        bool hasDestination,
        GatewayDiscoveryObservationState expectedState,
        bool expectedServing,
        bool expectedEligibleCondition)
    {
        await using WebApplication application = await StartApplication();
        GatewayRuntimePublisher publisher = application.Services.GetRequiredService<GatewayRuntimePublisher>();
        (await Publish(publisher, PreparedApplication(1, hasDestination))).State.Should().Be(GatewayPublicationState.ActiveAcknowledged);
        GatewayPublicationObservation source = publisher.GetCurrent();
        ActivePublicationIdentity active = source.Active!;
        IProxyStateLookup lookup = application.Services.GetRequiredService<IProxyStateLookup>();
        lookup.TryGetCluster("orders", out ClusterState? cluster).Should().BeTrue();
        GatewayPreparedMembershipDisposition preparedDisposition = disposition switch
        {
            GatewayAppliedMembershipDisposition.LastKnownMembership => GatewayPreparedMembershipDisposition.LastKnownMembership,
            GatewayAppliedMembershipDisposition.UnavailableWhenStale => GatewayPreparedMembershipDisposition.UnavailableWhenStale,
            GatewayAppliedMembershipDisposition.RefreshFailed => GatewayPreparedMembershipDisposition.RefreshFailed,
            _ => GatewayPreparedMembershipDisposition.Fresh,
        };
        IReadOnlyDictionary<string, DestinationConfig> destinations = cluster!.Model.Config.Destinations ??
            ImmutableDictionary<string, DestinationConfig>.Empty;
        var upstream = new GatewayAppliedUpstream("orders", GatewayAppliedUpstreamKind.ServiceDiscovery,
            "aspire", "orders", null, 9, GatewayRuntimeGraphIdentity.ComputeMembership(destinations, preparedDisposition),
            destinations.Count, disposition, "safe");
        var observed = new GatewayAppliedRuntimeObservation("namespace", "node", new GatewayAppliedRuntimeSnapshot(
            1, active.Candidate.CandidateId, active.Candidate.ContentHash, active.ApplicationId,
            active.SymbolicPlanIdentity, DateTimeOffset.UtcNow, [], [upstream], true, false));
        using var publication = new FixedPublicationReader(source);
        using var applied = new FixedAppliedReader(observed);
        using var coordinator = new GatewayStatusCoordinator([publication], [applied], lookup, [], new TestLifetime());

        GatewayStatusSnapshot snapshot = coordinator.GetCurrent();

        snapshot.Upstreams.Should().ContainSingle(item => item.Discovery.State == expectedState);
        snapshot.Readiness.Serving.Should().Be(expectedServing ? GatewayReadinessState.Ready : GatewayReadinessState.NotReady);
        snapshot.Conditions.Single(item => item.Type == GatewayConditionType.DestinationsEligible).Value
            .Should().Be(expectedEligibleCondition ? GatewayConditionValue.True : GatewayConditionValue.False);
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

    private static Task<GatewayPublicationOutcome> Publish(
        GatewayRuntimePublisher publisher,
        GatewayPreparedApplication application) =>
        publisher.PublishAsync(application, "namespace", "node", TimeSpan.FromSeconds(5));

    private static GatewayPreparedApplication PreparedApplication(ulong version, bool destination, bool healthEnabled = false)
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
        var identity = new PublicationCandidateIdentity(new CandidateId($"candidate-{version}"), "authority", "epoch", version, new ContentHash("sha-256", new string((char)('a' + version - 1), 64)));
        return PreparedApplicationTestFactory.Create(identity, [route], [cluster], $"native-{version}-{Guid.NewGuid():N}",
            new GatewayPreparedProjectionSnapshot(1, identity.CandidateId, identity.ContentHash, [], false));
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

    private static ActivePublicationIdentity ActiveIdentity() => new(
        new PublicationCandidateIdentity(new CandidateId("candidate"), "authority", "epoch", 1,
            new ContentHash("sha-256", new string('a', 64))),
        "0123456789abcdef0123456789abcdef", new ContentHash("sha-256", new string('b', 64)),
        "native", DateTimeOffset.UtcNow);

    private static GatewayAppliedRuntimeObservation Applied(
        ActivePublicationIdentity active,
        IEnumerable<string> upstreamIds) => new("namespace", "node", new GatewayAppliedRuntimeSnapshot(
            1, active.Candidate.CandidateId, active.Candidate.ContentHash, active.ApplicationId,
            active.SymbolicPlanIdentity, DateTimeOffset.UtcNow, [], upstreamIds
                .Order(StringComparer.Ordinal)
                .Select(static id => new GatewayAppliedUpstream(id, GatewayAppliedUpstreamKind.Static,
                    null, null, null, null,
                    GatewayRuntimeGraphIdentity.ComputeMembership(
                        ImmutableDictionary<string, DestinationConfig>.Empty,
                        GatewayPreparedMembershipDisposition.Fresh),
                    0, GatewayAppliedMembershipDisposition.Static, "Static destination membership is applied."))
                .ToImmutableArray(), true, false));

    private sealed class FixedPublicationReader(GatewayPublicationObservation observation) : IGatewayPublicationObservationReader, IDisposable
    {
        public GatewayPublicationObservation GetCurrent() => observation;
        public IChangeToken GetChangeToken() => new CancellationChangeToken(CancellationToken.None);
        public void Dispose() { }
    }

    private sealed class FixedAppliedReader(GatewayAppliedRuntimeObservation? observation) : IGatewayNodeAppliedRuntimeReader, IDisposable
    {
        public GatewayAppliedRuntimeObservation? GetCurrent() => observation;
        public CancellationToken GetChangeToken() => CancellationToken.None;
        public void Dispose() { }
    }

    private sealed class EmptyProxyLookup : IProxyStateLookup
    {
        public bool TryGetRoute(string id, [NotNullWhen(true)] out RouteModel? route) { route = null; return false; }
        public IEnumerable<RouteModel> GetRoutes() => [];
        public bool TryGetCluster(string id, [NotNullWhen(true)] out ClusterState? cluster) { cluster = null; return false; }
        public IEnumerable<ClusterState> GetClusters() => [];
    }

    private sealed class ThrowingProxyLookup : IProxyStateLookup
    {
        public bool TryGetRoute(string id, [NotNullWhen(true)] out RouteModel? route) => throw new InvalidOperationException("native lookup failed");
        public IEnumerable<RouteModel> GetRoutes() => throw new InvalidOperationException("native lookup failed");
        public bool TryGetCluster(string id, [NotNullWhen(true)] out ClusterState? cluster) => throw new InvalidOperationException("native lookup failed");
        public IEnumerable<ClusterState> GetClusters() => throw new InvalidOperationException("native lookup failed");
    }

    private sealed class TestLifetime : IHostApplicationLifetime
    {
        public CancellationToken ApplicationStarted => CancellationToken.None;
        public CancellationToken ApplicationStopping => CancellationToken.None;
        public CancellationToken ApplicationStopped => CancellationToken.None;
        public void StopApplication() { }
    }
}
