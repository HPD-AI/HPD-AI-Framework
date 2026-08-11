using System.Collections.Immutable;
using FluentAssertions;
using HPD.Gateway;
using Yarp.ReverseProxy.Configuration;
using Xunit;

namespace HPD.Gateway.Tests;

public sealed class GatewayAppliedRuntimeTests
{
    [Fact]
    public void PreparationOwnedEffectiveContractsAreRemovedWithoutCompatibilityAliases()
    {
        typeof(GatewayAppliedRuntimeSnapshot).Assembly
            .GetType("HPD.Gateway.GatewayEffectiveSnapshot", throwOnError: false)
            .Should().BeNull();
        typeof(GatewayNodeActivationResult).GetProperties()
            .Select(static property => property.Name)
            .Should().NotContain("EffectiveSnapshot");
        typeof(GatewayNodeActivationResult).GetProperty("ApplicationId").Should().NotBeNull();
        typeof(GatewayNodeActivationResult).GetProperty("SymbolicPlanIdentity").Should().NotBeNull();
    }

    [Fact]
    public async Task PreparationIsNotAppliedTruthAndExactCallbackPromotesCompleteStaticGraph()
    {
        using var fixture = new Fixture();
        GatewayPreparedApplication application = Application(1, withGraph: true);
        CancellationToken changed = fixture.Observer.GetChangeToken();

        Task<GatewayPublicationOutcome> publication = fixture.Publisher.PublishAsync(
            application, "namespace-a", "node-a", TimeSpan.FromSeconds(2));
        IProxyConfig config = await fixture.WaitForRevision(application.NativeRevisionId);

        fixture.Observer.GetCurrent().Should().BeNull("preparation and exchange are not applied runtime truth");
        changed.IsCancellationRequested.Should().BeFalse();
        fixture.Listener.ConfigurationApplied([config]);
        (await publication!).State.Should().Be(GatewayPublicationState.ActiveAcknowledged);

        GatewayAppliedRuntimeObservation observation = fixture.Observer.GetCurrent()!;
        observation.Should().NotBeNull();
        observation.NamespaceId.Should().Be("namespace-a");
        observation.TargetNodeId.Should().Be("node-a");
        observation.Snapshot.ApplicationId.Should().Be(application.ApplicationId);
        observation.Snapshot.SymbolicPlanIdentity.Should().Be(application.SymbolicPlanIdentity);
        observation.Snapshot.IsComplete.Should().BeTrue();
        observation.Snapshot.IsTruncated.Should().BeFalse();
        observation.Snapshot.Routes.Should().ContainSingle().Which.RouteId.Should().Be("route");
        observation.Snapshot.Routes.Single().Contributions.Should().BeEmpty();
        observation.Snapshot.Upstreams.Should().ContainSingle();
        GatewayAppliedUpstream upstream = observation.Snapshot.Upstreams.Single();
        upstream.Kind.Should().Be(GatewayAppliedUpstreamKind.Static);
        upstream.DestinationCount.Should().Be(1);
        upstream.MembershipGeneration.Should().BeNull();
        changed.IsCancellationRequested.Should().BeTrue();
    }

    [Fact]
    public async Task Traffic_admission_truth_appears_only_with_the_exact_acknowledged_generation()
    {
        using var fixture = new Fixture();
        GatewayPreparedApplication application = ApplicationWithAdmission(7);
        Task<GatewayPublicationOutcome> publication = fixture.Publisher.PublishAsync(
            application, "namespace-a", "node-a", TimeSpan.FromSeconds(2));
        IProxyConfig config = await fixture.WaitForRevision(application.NativeRevisionId);

        fixture.Observer.GetCurrent().Should().BeNull();
        fixture.Listener.ConfigurationApplied([new TestConfig("forged", config.Routes.Select(route => route with
        {
            Metadata = route.Metadata!.ToImmutableDictionary(StringComparer.Ordinal)
                .SetItem(GatewayTrafficAdmissionMetadataCodec.PlanIdentity, new string('f', 64))
        }).ToArray(), config.Clusters)]);
        fixture.Observer.GetCurrent().Should().BeNull();

        fixture.Listener.ConfigurationApplied([config]);
        (await publication).State.Should().Be(GatewayPublicationState.ActiveAcknowledged);
        GatewayAppliedRoute route = fixture.Observer.GetCurrent()!.Snapshot.Routes.Should().ContainSingle().Subject;
        route.Contributions.Should().ContainSingle(record => record.Family == GatewayEffectiveFamilies.TrafficAdmission);
    }

    [Fact]
    public async Task WrongMixedAndFailedCallbacksCannotReplaceLastAppliedTruth()
    {
        using var fixture = new Fixture();
        GatewayPreparedApplication first = Application(1, withGraph: true);
        Task<GatewayPublicationOutcome> firstPublication = fixture.Publisher.PublishAsync(
            first, "namespace-a", "node-a", TimeSpan.FromSeconds(2));
        IProxyConfig firstConfig = await fixture.WaitForRevision(first.NativeRevisionId);
        fixture.Listener.ConfigurationApplied([firstConfig]);
        await firstPublication;

        GatewayPreparedApplication second = Application(2, withGraph: true);
        Task<GatewayPublicationOutcome> secondPublication = fixture.Publisher.PublishAsync(
            second, "namespace-a", "node-a", TimeSpan.FromSeconds(2));
        IProxyConfig secondConfig = await fixture.WaitForRevision(second.NativeRevisionId);
        fixture.Listener.ConfigurationApplied([firstConfig, secondConfig]);
        fixture.Observer.GetCurrent()!.Snapshot.ApplicationId.Should().Be(first.ApplicationId);

        fixture.Listener.ConfigurationApplyingFailed([secondConfig], new InvalidOperationException("test"));
        (await secondPublication).State.Should().Be(GatewayPublicationState.PublicationIndeterminate);
        fixture.Listener.ConfigurationApplied([firstConfig]);
        fixture.Observer.GetCurrent()!.Snapshot.ApplicationId.Should().Be(first.ApplicationId);
    }

    [Fact]
    public async Task EquivalentWrappedAppliedGraphIsCorrelatedWithoutReferenceAuthority()
    {
        using var fixture = new Fixture();
        GatewayPreparedApplication application = Application(1, withGraph: true);
        Task<GatewayPublicationOutcome> publication = fixture.Publisher.PublishAsync(
            application, "namespace-a", "node-a", TimeSpan.FromSeconds(2));
        IProxyConfig installed = await fixture.WaitForRevision(application.NativeRevisionId);
        var wrapped = new TestConfig("wrapped", installed.Routes, installed.Clusters);

        fixture.Listener.ConfigurationApplied([wrapped]);

        (await publication!).State.Should().Be(GatewayPublicationState.ActiveAcknowledged);
        fixture.Observer.GetCurrent()!.Snapshot.ApplicationId.Should().Be(application.ApplicationId);
    }

    [Fact]
    public async Task EmptyApplicationRequiresItsPortableGenerationCarrier()
    {
        using var fixture = new Fixture();
        GatewayPreparedApplication application = Application(1, withGraph: false);
        Task<GatewayPublicationOutcome> publication = fixture.Publisher.PublishAsync(
            application, "namespace-a", "node-a", TimeSpan.FromSeconds(2));
        IProxyConfig installed = await fixture.WaitForRevision(application.NativeRevisionId);

        fixture.Listener.ConfigurationApplied([new TestConfig("wrong-empty", [], [])]);
        fixture.Observer.GetCurrent().Should().BeNull();
        fixture.Listener.ConfigurationApplied([new TestConfig(installed.RevisionId, [], [])]);

        (await publication).State.Should().Be(GatewayPublicationState.ActiveAcknowledged);
        fixture.Observer.GetCurrent()!.Snapshot.Routes.Should().BeEmpty();
        fixture.Observer.GetCurrent()!.Snapshot.Upstreams.Should().BeEmpty();
    }

    [Fact]
    public async Task ThrowingChangeSubscriberCannotSplitAcknowledgementFromAppliedTruth()
    {
        using var fixture = new Fixture();
        GatewayPreparedApplication application = Application(1, withGraph: true);
        Task<GatewayPublicationOutcome>? publication = null;
        bool publicationCompletedDuringNotification = true;
        bool appliedVisibleDuringNotification = false;
        using CancellationTokenRegistration registration = fixture.Observer.GetChangeToken()
            .Register(() =>
            {
                publicationCompletedDuringNotification = publication?.IsCompleted ?? true;
                appliedVisibleDuringNotification = fixture.Observer.GetCurrent()?.Snapshot.ApplicationId == application.ApplicationId;
                throw new InvalidOperationException("subscriber failure");
            });
        publication = fixture.Publisher.PublishAsync(
            application, "namespace-a", "node-a", TimeSpan.FromSeconds(2));
        IProxyConfig config = await fixture.WaitForRevision(application.NativeRevisionId);

        Action callback = () => fixture.Listener.ConfigurationApplied([config]);

        callback.Should().NotThrow();
        publicationCompletedDuringNotification.Should().BeFalse("publication is signaled only after applied truth commits");
        appliedVisibleDuringNotification.Should().BeTrue();
        (await publication).State.Should().Be(GatewayPublicationState.ActiveAcknowledged);
        fixture.Observer.GetCurrent()!.Snapshot.ApplicationId.Should().Be(application.ApplicationId);
    }

    private static GatewayPreparedApplication Application(ulong version, bool withGraph)
    {
        var identity = new PublicationCandidateIdentity(
            new CandidateId($"candidate-{version}"), "authority", "epoch", version,
            new ContentHash("sha-256", new string('a', 64)));
        ImmutableArray<RouteConfig> routes = withGraph
            ? [new RouteConfig { RouteId = "route", ClusterId = "upstream", Match = new RouteMatch { Path = "/{**catch-all}" } }]
            : [];
        ImmutableArray<ClusterConfig> clusters = withGraph
            ? [new ClusterConfig
            {
                ClusterId = "upstream",
                Destinations = ImmutableDictionary<string, DestinationConfig>.Empty.Add(
                    "destination", new DestinationConfig { Address = "http://127.0.0.1:8080/" }),
            }]
            : [];
        return PreparedApplicationTestFactory.Create(identity, routes, clusters,
            $"native-{version}-{Guid.NewGuid():N}", new GatewayPreparedProjectionSnapshot(1, identity.CandidateId, identity.ContentHash, [], false));
    }

    private static GatewayPreparedApplication ApplicationWithAdmission(ulong version)
    {
        var identity = new PublicationCandidateIdentity(
            new CandidateId($"candidate-{version}"), "authority", "epoch", version,
            new ContentHash("sha-256", new string('a', 64)));
        TrafficAdmissionPlan plan = new()
        {
            Entries = [new FixedWindowAdmissionEntry { Profile = "shared", PermitLimit = 10, Window = TimeSpan.FromSeconds(1) }]
        };
        ContentHash planIdentity = GatewayRuntimePlanner.HashTrafficAdmission(plan);
        var route = new RouteConfig
        {
            RouteId = "route",
            ClusterId = "upstream",
            Match = new RouteMatch { Path = "/{**catch-all}" },
            Metadata = ImmutableDictionary<string, string>.Empty
                .Add(GatewayTrafficAdmissionMetadataCodec.Plan, GatewayTrafficAdmissionMetadataCodec.Encode(plan))
                .Add(GatewayTrafficAdmissionMetadataCodec.PlanIdentity, planIdentity.Value),
        };
        var cluster = new ClusterConfig
        {
            ClusterId = "upstream",
            Destinations = ImmutableDictionary<string, DestinationConfig>.Empty.Add(
                "destination", new DestinationConfig { Address = "http://127.0.0.1:8080/" }),
        };
        ImmutableArray<GatewayEffectiveContribution> contributions =
        [
            new(GatewayContributionSourceKind.Inline, GatewayContributionScope.RouteLocal,
                GatewayContributionDisposition.Selected, "routes/route", null, 0, planIdentity),
            new(GatewayContributionSourceKind.HostProfile, GatewayContributionScope.Host,
                GatewayContributionDisposition.Correlated, "profiles/shared", null, 1,
                new ContentHash("sha-256", new string('b', 64))),
        ];
        var record = new GatewayEffectiveRecord(1, GatewayEffectiveTargetKind.Route, "route",
            GatewayEffectiveFamilies.TrafficAdmission, GatewayEffectiveComposition.ReplaceMoreSpecific,
            contributions, new GatewayNativeProjection("HPD.Gateway", "RouteConfig.Metadata/HPD traffic admission", "HPD.Gateway"),
            "HPD.Gateway", "1.0.0", GatewayMaterializationDisposition.Materialized, planIdentity, []);
        var snapshot = new GatewayPreparedProjectionSnapshot(1, identity.CandidateId, identity.ContentHash, [record], false);
        return PreparedApplicationTestFactory.Create(identity, [route], [cluster],
            $"native-{version}-{Guid.NewGuid():N}", snapshot);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly GatewayDestinationResolver _resolver;
        internal HpdProxyConfigProvider Provider { get; } = new();
        internal GatewayRuntimeApplicationObserver Observer { get; }
        internal HpdConfigChangeListener Listener { get; }
        internal GatewayRuntimePublisher Publisher { get; }

        internal Fixture()
        {
            _resolver = new GatewayDestinationResolver(new GatewayDiscoveryProfileRegistry([]), new PassthroughConfigValidator(), TimeProvider.System);
            Observer = new GatewayRuntimeApplicationObserver(_resolver, TimeProvider.System);
            Listener = new HpdConfigChangeListener(Provider, Observer);
            Publisher = new GatewayRuntimePublisher(Provider, Listener, [Provider], _resolver, Observer);
        }

        internal async Task<IProxyConfig> WaitForRevision(string revision)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (!timeout.IsCancellationRequested)
            {
                if (Provider.GetConfig() is OwnedProxyConfig owned && owned.NativeRevisionId == revision) return owned;
                await Task.Delay(1, timeout.Token);
            }
            throw new TimeoutException();
        }

        public void Dispose()
        {
            Publisher.Dispose();
            Listener.Dispose();
            Observer.Dispose();
            _resolver.Dispose();
            Provider.Dispose();
        }
    }

    private sealed class TestConfig(
        string revisionId,
        IReadOnlyList<RouteConfig> routes,
        IReadOnlyList<ClusterConfig> clusters) : IProxyConfig
    {
        public string RevisionId { get; } = revisionId;
        public IReadOnlyList<RouteConfig> Routes { get; } = routes;
        public IReadOnlyList<ClusterConfig> Clusters { get; } = clusters;
        public Microsoft.Extensions.Primitives.IChangeToken ChangeToken { get; } =
            new Microsoft.Extensions.Primitives.CancellationChangeToken(CancellationToken.None);
    }
}
