using System.Collections.Immutable;
using FluentAssertions;
using HPD.Gateway;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;
using System.Net.Http;
using Xunit;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Forwarder;

namespace HPD.Gateway.Tests;

public sealed class YarpPublicationTests
{
    [Fact]
    public async Task RealYarpManagerAcknowledgesExactHpdSnapshot()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddReverseProxy();
        builder.Services.AddHpdGatewayYarpPublication();
        await using var application = builder.Build();
        application.MapReverseProxy();
        await application.StartAsync();
        try
        {
            var publisher = application.Services.GetRequiredService<GatewayRuntimePublisher>();
            var outcome = await publisher.PublishAsync(PreparedApplication(1), TimeSpan.FromSeconds(5));

            outcome.State.Should().Be(GatewayPublicationState.ActiveAcknowledged);
        }
        finally
        {
            await application.StopAsync();
        }
    }

    [Fact]
    public async Task ExactSnapshotAcknowledgementActivatesCandidate()
    {
        using var fixture = new PublisherFixture();
        var bundle = PreparedApplication(1);

        var publication = fixture.Publisher.PublishAsync(bundle, TimeSpan.FromSeconds(2));
        var snapshot = await fixture.WaitForRevision(bundle.NativeRevisionId);
        fixture.Listener.ConfigurationApplied([snapshot]);

        var outcome = await publication;
        outcome.State.Should().Be(GatewayPublicationState.ActiveAcknowledged);
        outcome.Active!.NativeRevisionId.Should().Be(bundle.NativeRevisionId);
        outcome.LastKnownGood.Should().Be(outcome.Active);
    }

    [Fact]
    public async Task EquivalentWrappedGraphAcknowledgesWithoutSourceReferenceIdentity()
    {
        using var fixture = new PublisherFixture();
        GatewayPreparedApplication prepared = PreparedApplicationWithGraph(1);
        Task<GatewayPublicationOutcome> publication = fixture.Publisher.PublishAsync(prepared, TimeSpan.FromSeconds(2));
        IProxyConfig installed = await fixture.WaitForRevision(prepared.NativeRevisionId);
        var wrapped = new TestProxyConfig(
            "yarp-wrapped",
            installed.Routes.Select(static route => route with { }).ToArray(),
            installed.Clusters.Select(static cluster => cluster with { }).ToArray());

        fixture.Listener.ConfigurationApplied([wrapped]);

        (await publication).State.Should().Be(GatewayPublicationState.ActiveAcknowledged);
    }

    [Fact]
    public async Task EmptyLogicalGenerationsUseThePortableRevisionCarrierNotReferenceIdentity()
    {
        using var fixture = new PublisherFixture();
        GatewayPreparedApplication first = PreparedApplication(1);
        Task<GatewayPublicationOutcome> firstPublication = fixture.Publisher.PublishAsync(first, TimeSpan.FromSeconds(2));
        IProxyConfig firstInstalled = await fixture.WaitForRevision(first.NativeRevisionId);
        var firstWrapped = new TestProxyConfig(firstInstalled.RevisionId, [], []);
        fixture.Listener.ConfigurationApplied([firstWrapped]);
        (await firstPublication).State.Should().Be(GatewayPublicationState.ActiveAcknowledged);

        GatewayPreparedApplication second = PreparedApplication(2);
        Task<GatewayPublicationOutcome> secondPublication = fixture.Publisher.PublishAsync(second, TimeSpan.FromSeconds(2));
        IProxyConfig secondInstalled = await fixture.WaitForRevision(second.NativeRevisionId);
        var secondWrapped = new TestProxyConfig(secondInstalled.RevisionId, [], []);
        fixture.Listener.ConfigurationApplied([firstWrapped]);
        fixture.Listener.ConfigurationApplied([new TestProxyConfig("wrong-empty", [], [])]);
        fixture.Listener.ConfigurationApplied([secondWrapped, secondWrapped]);
        secondPublication.IsCompleted.Should().BeFalse();

        fixture.Listener.ConfigurationApplied([secondWrapped]);
        (await secondPublication).State.Should().Be(GatewayPublicationState.ActiveAcknowledged);
    }

    [Fact]
    public async Task WrongMixedMissingDuplicateOrChangedLogicalGenerationsCannotAcknowledge()
    {
        using var fixture = new PublisherFixture();
        GatewayPreparedApplication prepared = PreparedApplicationWithGraph(1);
        Task<GatewayPublicationOutcome> publication = fixture.Publisher.PublishAsync(prepared, TimeSpan.FromSeconds(2));
        IProxyConfig installed = await fixture.WaitForRevision(prepared.NativeRevisionId);
        RouteConfig route = installed.Routes.Single();
        ClusterConfig cluster = installed.Clusters.Single();
        var wrongRoute = route with
        {
            Metadata = route.Metadata!.ToImmutableDictionary(StringComparer.Ordinal)
                .SetItem(GatewayRuntimePlanner.ApplicationIdMetadata, "wrong-application"),
        };
        var changedCluster = cluster with { LoadBalancingPolicy = "RoundRobin" };

        fixture.Listener.ConfigurationApplied([new TestProxyConfig("wrong", [wrongRoute], [cluster])]);
        fixture.Listener.ConfigurationApplied([new TestProxyConfig("mixed", [route], [changedCluster])]);
        fixture.Listener.ConfigurationApplied([new TestProxyConfig("missing", [], [cluster])]);
        fixture.Listener.ConfigurationApplied([new TestProxyConfig("duplicate", [route, route], [cluster])]);
        fixture.Listener.ConfigurationApplied([
            new TestProxyConfig("first", [route], [cluster]),
            new TestProxyConfig("second", [route], [cluster]),
        ]);
        publication.IsCompleted.Should().BeFalse();

        fixture.Listener.ConfigurationApplied([new TestProxyConfig("exact", [route], [cluster])]);
        (await publication).State.Should().Be(GatewayPublicationState.ActiveAcknowledged);
    }

    [Fact]
    public async Task WrongSnapshotCannotAcknowledgeAttempt()
    {
        using var fixture = new PublisherFixture();
        var bootstrap = fixture.Provider.GetConfig();
        var bundle = PreparedApplication(1);
        var publication = fixture.Publisher.PublishAsync(bundle, TimeSpan.FromSeconds(2));
        var snapshot = await fixture.WaitForRevision(bundle.NativeRevisionId);

        fixture.Listener.ConfigurationApplied([bootstrap]);
        publication.IsCompleted.Should().BeFalse();
        fixture.Listener.ConfigurationApplied([snapshot]);

        (await publication).State.Should().Be(GatewayPublicationState.ActiveAcknowledged);
    }

    [Fact]
    public async Task ApplyingFailureAndTimeoutAreIndeterminate()
    {
        using var failed = new PublisherFixture();
        var failedBundle = PreparedApplication(1);
        var failedPublication = failed.Publisher.PublishAsync(failedBundle, TimeSpan.FromSeconds(2));
        var failedSnapshot = await failed.WaitForRevision(failedBundle.NativeRevisionId);
        failed.Listener.ConfigurationApplyingFailed([failedSnapshot], new InvalidOperationException());
        (await failedPublication).State.Should().Be(GatewayPublicationState.PublicationIndeterminate);

        using var timedOut = new PublisherFixture();
        var timeoutBundle = PreparedApplication(1);
        var timeout = await timedOut.Publisher.PublishAsync(timeoutBundle, TimeSpan.FromMilliseconds(20));
        timeout.State.Should().Be(GatewayPublicationState.PublicationIndeterminate);
        timeout.Diagnostics.Should().ContainSingle(item => item.Code == "publication.timeout");
    }

    [Fact]
    public async Task NotificationFailureAfterExchangeIsIndeterminate()
    {
        using var fixture = new PublisherFixture();
        using var registration = fixture.Provider.GetConfig().ChangeToken.RegisterChangeCallback(
            static _ => throw new InvalidOperationException("observer failure"), null);
        var bundle = PreparedApplication(1);

        var outcome = await fixture.Publisher.PublishAsync(bundle, TimeSpan.FromSeconds(2));

        outcome.State.Should().Be(GatewayPublicationState.PublicationIndeterminate);
        ((OwnedProxyConfig)fixture.Provider.GetConfig()).NativeRevisionId.Should().Be(bundle.NativeRevisionId);
    }

    [Fact]
    public async Task CallerCancellationBeforeExchangeIsSafe()
    {
        using var fixture = new PublisherFixture();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var outcome = await fixture.Publisher.PublishAsync(PreparedApplication(1), TimeSpan.FromSeconds(2), cancellation.Token);

        outcome.State.Should().Be(GatewayPublicationState.CanceledBeforePublish);
        fixture.Provider.GetConfig().RevisionId.Should().StartWith("hpd-bootstrap-");
    }

    [Fact]
    public async Task ListenerRegistrationFailureIsRejectedBeforePublish()
    {
        using var fixture = new PublisherFixture();
        fixture.Listener.Dispose();
        var outcome = await fixture.Publisher.PublishAsync(PreparedApplication(1), TimeSpan.FromSeconds(2));

        outcome.State.Should().Be(GatewayPublicationState.RejectedBeforePublish);
        outcome.Diagnostics.Should().ContainSingle(item => item.Code == "publication.preparation-failed");
        fixture.Provider.GetConfig().RevisionId.Should().StartWith("hpd-bootstrap-");
    }

    [Fact]
    public async Task CallerCancellationAfterExchangeCannotCancelPublication()
    {
        using var fixture = new PublisherFixture();
        using var cancellation = new CancellationTokenSource();
        var bundle = PreparedApplication(1);
        var publication = fixture.Publisher.PublishAsync(bundle, TimeSpan.FromSeconds(2), cancellation.Token);
        var snapshot = await fixture.WaitForRevision(bundle.NativeRevisionId);

        cancellation.Cancel();
        fixture.Listener.ConfigurationApplied([snapshot]);

        (await publication).State.Should().Be(GatewayPublicationState.ActiveAcknowledged);
    }

    [Fact]
    public async Task CancellationWhileWaitingForLeaseDoesNotMutateSecondCandidate()
    {
        using var fixture = new PublisherFixture();
        var first = PreparedApplication(1);
        var firstTask = fixture.Publisher.PublishAsync(first, TimeSpan.FromSeconds(2));
        var firstSnapshot = await fixture.WaitForRevision(first.NativeRevisionId);
        using var cancellation = new CancellationTokenSource();
        var second = PreparedApplication(2);
        var secondTask = fixture.Publisher.PublishAsync(second, TimeSpan.FromSeconds(2), cancellation.Token);
        cancellation.Cancel();

        fixture.Listener.ConfigurationApplied([firstSnapshot]);

        (await firstTask).State.Should().Be(GatewayPublicationState.ActiveAcknowledged);
        (await secondTask).State.Should().Be(GatewayPublicationState.CanceledBeforePublish);
        ((OwnedProxyConfig)fixture.Provider.GetConfig()).NativeRevisionId.Should().Be(first.NativeRevisionId);
    }

    [Fact]
    public async Task LkgIsHistoricalDuringIndeterminateAndRecoveryRequiresRepublish()
    {
        using var fixture = new PublisherFixture();
        var first = PreparedApplication(1);
        var firstTask = fixture.Publisher.PublishAsync(first, TimeSpan.FromSeconds(2));
        fixture.Listener.ConfigurationApplied([await fixture.WaitForRevision(first.NativeRevisionId)]);
        var acknowledged = await firstTask;

        var second = PreparedApplication(2);
        var secondTask = fixture.Publisher.PublishAsync(second, TimeSpan.FromSeconds(2));
        var secondSnapshot = await fixture.WaitForRevision(second.NativeRevisionId);
        fixture.Listener.ConfigurationApplyingFailed([secondSnapshot], new InvalidOperationException());
        var indeterminate = await secondTask;
        indeterminate.State.Should().Be(GatewayPublicationState.PublicationIndeterminate);
        indeterminate.Active.Should().BeNull();
        indeterminate.LastKnownGood.Should().Be(acknowledged.Active);

        var recovery = PreparedApplication(3);
        var recoveryTask = fixture.Publisher.PublishAsync(recovery, TimeSpan.FromSeconds(2));
        fixture.Listener.ConfigurationApplied([await fixture.WaitForRevision(recovery.NativeRevisionId)]);
        (await recoveryTask).State.Should().Be(GatewayPublicationState.ActiveAcknowledged);
    }

    [Fact]
    public async Task DuplicateStaleAndIdentityConflictDoNotRepublish()
    {
        using var fixture = new PublisherFixture();
        var original = PreparedApplication(2);
        var originalTask = fixture.Publisher.PublishAsync(original, TimeSpan.FromSeconds(2));
        fixture.Listener.ConfigurationApplied([await fixture.WaitForRevision(original.NativeRevisionId)]);
        await originalTask;

        (await fixture.Publisher.PublishAsync(PreparedApplication(2, hash: Hash('a')), TimeSpan.FromSeconds(2))).State
            .Should().Be(GatewayPublicationState.Duplicate);
        (await fixture.Publisher.PublishAsync(PreparedApplication(2, hash: Hash('b')), TimeSpan.FromSeconds(2))).State
            .Should().Be(GatewayPublicationState.IdentityConflict);
        (await fixture.Publisher.PublishAsync(PreparedApplicationWithGraph(2), TimeSpan.FromSeconds(2))).State
            .Should().Be(GatewayPublicationState.IdentityConflict);
        (await fixture.Publisher.PublishAsync(PreparedApplication(1), TimeSpan.FromSeconds(2))).State
            .Should().Be(GatewayPublicationState.Stale);
        ((OwnedProxyConfig)fixture.Provider.GetConfig()).NativeRevisionId.Should().Be(original.NativeRevisionId);
    }

    [Fact]
    public async Task NewerWaitingCandidateSupersedesOlderWaitingCandidate()
    {
        using var fixture = new PublisherFixture();
        var first = PreparedApplication(1);
        var firstTask = fixture.Publisher.PublishAsync(first, TimeSpan.FromSeconds(2));
        var firstSnapshot = await fixture.WaitForRevision(first.NativeRevisionId);

        var second = PreparedApplication(2);
        var secondTask = fixture.Publisher.PublishAsync(second, TimeSpan.FromSeconds(2));
        var third = PreparedApplication(3);
        var thirdTask = fixture.Publisher.PublishAsync(third, TimeSpan.FromSeconds(2));
        fixture.Listener.ConfigurationApplied([firstSnapshot]);

        (await secondTask).State.Should().Be(GatewayPublicationState.Superseded);
        var thirdSnapshot = await fixture.WaitForRevision(third.NativeRevisionId);
        fixture.Listener.ConfigurationApplied([thirdSnapshot]);
        (await firstTask).State.Should().Be(GatewayPublicationState.ActiveAcknowledged);
        (await thirdTask).State.Should().Be(GatewayPublicationState.ActiveAcknowledged);
    }

    [Fact]
    public void MultipleProviderOwnershipIsRejected()
    {
        using var provider = new HpdProxyConfigProvider();
        using var listener = new HpdConfigChangeListener(provider);
        var other = new StubProvider();

        var action = () => new GatewayRuntimePublisher(provider, listener, [provider, other]);

        action.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public async Task HostStartupRejectsAnotherProxyConfigProvider()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddReverseProxy();
        builder.Services.AddHpdGatewayYarpPublication();
        builder.Services.AddSingleton<IProxyConfigProvider>(new StubProvider());
        await using var application = builder.Build();
        application.MapReverseProxy();

        var action = () => application.StartAsync();

        await action.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task HostStartupRejectsCompetingForwarderClientFactory()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddReverseProxy();
        builder.Services.AddHpdGatewayYarpPublication();
        builder.Services.AddHpdGatewayYarpMaterialization();
        builder.Services.AddSingleton<IForwarderHttpClientFactory, StubClientFactory>();
        await using var application = builder.Build();
        application.MapReverseProxy();

        var action = () => application.StartAsync();

        await action.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task DisposalWithPendingAcknowledgementIsIndeterminate()
    {
        var fixture = new PublisherFixture();
        var bundle = PreparedApplication(1);
        var publication = fixture.Publisher.PublishAsync(bundle, TimeSpan.FromSeconds(2));
        await fixture.WaitForRevision(bundle.NativeRevisionId);

        fixture.Publisher.Dispose();

        (await publication).State.Should().Be(GatewayPublicationState.PublicationIndeterminate);
        fixture.Dispose();
    }

    [Fact]
    public async Task DisposalMakesQueuedAttemptSafeWithoutMutatingNativeState()
    {
        var fixture = new PublisherFixture();
        var first = PreparedApplication(1);
        var firstTask = fixture.Publisher.PublishAsync(first, TimeSpan.FromSeconds(2));
        await fixture.WaitForRevision(first.NativeRevisionId);
        var second = PreparedApplication(2);
        var secondTask = fixture.Publisher.PublishAsync(second, TimeSpan.FromSeconds(2));

        fixture.Publisher.Dispose();

        (await firstTask).State.Should().Be(GatewayPublicationState.PublicationIndeterminate);
        (await secondTask).State.Should().Be(GatewayPublicationState.CanceledBeforePublish);
        ((OwnedProxyConfig)fixture.Provider.GetConfig()).NativeRevisionId.Should().Be(first.NativeRevisionId);
        fixture.Dispose();
    }

    [Fact]
    public async Task DistinctAuthorityAdmissionIsBounded()
    {
        var fixture = new PublisherFixture();
        var admitted = new List<Task<GatewayPublicationOutcome>>(4_096);
        for (var index = 0; index < 4_096; index++)
            admitted.Add(fixture.Publisher.PublishAsync(AuthorityBundle(index), TimeSpan.FromMinutes(1)));

        var rejected = await fixture.Publisher.PublishAsync(AuthorityBundle(4_096), TimeSpan.FromMinutes(1));
        rejected.State.Should().Be(GatewayPublicationState.RejectedBeforePublish);
        rejected.Diagnostics.Should().ContainSingle(item => item.Code == "publication.admission-capacity-exceeded");

        fixture.Publisher.Dispose();
        var outcomes = await Task.WhenAll(admitted);
        outcomes.Should().ContainSingle(item => item.State == GatewayPublicationState.PublicationIndeterminate);
        outcomes.Count(item => item.State == GatewayPublicationState.CanceledBeforePublish).Should().Be(4_095);
        fixture.Dispose();
    }

    [Theory]
    [InlineData("")]
    [InlineData("native\nrevision")]
    public void NativeRevisionIdentityMustBeBoundedAndSafe(string revision)
    {
        var identity = new PublicationCandidateIdentity(new CandidateId("candidate"), "authority", "epoch", 1, Hash('a'));
        var action = () => PreparedApplicationTestFactory.Create(identity, [], [], revision, Effective(identity));

        action.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void NativeRevisionIdentityRejectsOversizedValue()
    {
        var identity = new PublicationCandidateIdentity(new CandidateId("candidate"), "authority", "epoch", 1, Hash('a'));
        var action = () => PreparedApplicationTestFactory.Create(identity, [], [],
            new string('r', GatewayPreparedApplication.MaximumNativeRevisionIdLength + 1), Effective(identity));

        action.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task FreshProcessPublisherCanReplayDurableCandidateIntent()
    {
        var identity = new PublicationCandidateIdentity(new CandidateId("replay"), "authority", "epoch-1", 7, Hash('a'));
        using var first = new PublisherFixture();
        var firstBundle = PreparedApplicationTestFactory.Create(identity, [], [], "native-before-restart", Effective(identity));
        var firstTask = first.Publisher.PublishAsync(firstBundle, TimeSpan.FromSeconds(2));
        first.Listener.ConfigurationApplied([await first.WaitForRevision(firstBundle.NativeRevisionId)]);
        (await firstTask).State.Should().Be(GatewayPublicationState.ActiveAcknowledged);

        using var restarted = new PublisherFixture();
        var replay = PreparedApplicationTestFactory.Create(identity, [], [], "native-after-restart", Effective(identity));
        var replayTask = restarted.Publisher.PublishAsync(replay, TimeSpan.FromSeconds(2));
        restarted.Listener.ConfigurationApplied([await restarted.WaitForRevision(replay.NativeRevisionId)]);

        var outcome = await replayTask;
        outcome.State.Should().Be(GatewayPublicationState.ActiveAcknowledged);
        outcome.Active!.NativeRevisionId.Should().Be("native-after-restart");
    }

    private static GatewayPreparedApplication PreparedApplication(ulong version, ContentHash? hash = null)
    {
        var identity = new PublicationCandidateIdentity(new CandidateId($"candidate-{version}"), "authority", "epoch-1", version, hash ?? Hash('a'));
        return PreparedApplicationTestFactory.Create(identity, [], [], $"native-{version}-{Guid.NewGuid():N}", Effective(identity));
    }

    private static GatewayPreparedApplication PreparedApplicationWithGraph(ulong version)
    {
        var identity = new PublicationCandidateIdentity(new CandidateId($"candidate-{version}"), "authority", "epoch-1", version, Hash('a'));
        ImmutableArray<RouteConfig> routes = [new RouteConfig { RouteId = "route", ClusterId = "upstream", Match = new RouteMatch { Path = "/{**catch-all}" } }];
        ImmutableArray<ClusterConfig> clusters = [new ClusterConfig
        {
            ClusterId = "upstream",
            LoadBalancingPolicy = "PowerOfTwoChoices",
            Destinations = ImmutableDictionary<string, DestinationConfig>.Empty.Add(
                "destination", new DestinationConfig { Address = "http://127.0.0.1:8080/" }),
        }];
        return PreparedApplicationTestFactory.Create(
            identity, routes, clusters, $"native-graph-{version}-{Guid.NewGuid():N}", Effective(identity));
    }

    private static GatewayPreparedApplication AuthorityBundle(int authority)
    {
        var identity = new PublicationCandidateIdentity(new CandidateId($"candidate-{authority}"), $"authority-{authority}", "epoch-1", 1, Hash('a'));
        return PreparedApplicationTestFactory.Create(identity, [], [], $"native-authority-{authority}", Effective(identity));
    }

    private static GatewayPreparedProjectionSnapshot Effective(PublicationCandidateIdentity identity) =>
        new(1, identity.CandidateId, identity.ContentHash, [], false);

    private static ContentHash Hash(char value) => new("sha-256", new string(value, 64));

    private sealed class PublisherFixture : IDisposable
    {
        internal HpdProxyConfigProvider Provider { get; } = new();
        internal HpdConfigChangeListener Listener { get; }
        internal GatewayRuntimePublisher Publisher { get; }

        internal PublisherFixture()
        {
            Listener = new HpdConfigChangeListener(Provider);
            Publisher = new GatewayRuntimePublisher(Provider, Listener, [Provider]);
        }

        internal async Task<IProxyConfig> WaitForRevision(string revision)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (!timeout.IsCancellationRequested)
            {
                var current = Provider.GetConfig();
                if (current is OwnedProxyConfig owned && owned.NativeRevisionId == revision) return current;
                await Task.Delay(1, timeout.Token).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            }
            throw new TimeoutException($"Revision '{revision}' was not installed.");
        }

        public void Dispose()
        {
            Publisher.Dispose();
            Listener.Dispose();
            Provider.Dispose();
        }
    }

    private sealed class StubProvider : IProxyConfigProvider
    {
        private readonly IProxyConfig _config = new StubConfig();
        public IProxyConfig GetConfig() => _config;
    }

    private sealed class StubConfig : IProxyConfig
    {
        public string RevisionId => "stub";
        public IReadOnlyList<RouteConfig> Routes => [];
        public IReadOnlyList<ClusterConfig> Clusters => [];
        public IChangeToken ChangeToken { get; } = new CancellationChangeToken(CancellationToken.None);
    }

    private sealed class TestProxyConfig(
        string revisionId,
        IReadOnlyList<RouteConfig> routes,
        IReadOnlyList<ClusterConfig> clusters) : IProxyConfig
    {
        public string RevisionId { get; } = revisionId;
        public IReadOnlyList<RouteConfig> Routes { get; } = routes;
        public IReadOnlyList<ClusterConfig> Clusters { get; } = clusters;
        public IChangeToken ChangeToken { get; } = new CancellationChangeToken(CancellationToken.None);
    }

    private sealed class StubClientFactory : IForwarderHttpClientFactory
    {
        public HttpMessageInvoker CreateClient(ForwarderHttpClientContext context) => new(new HttpClientHandler());
    }
}
