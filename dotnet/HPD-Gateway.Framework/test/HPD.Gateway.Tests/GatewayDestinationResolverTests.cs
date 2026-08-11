using System.Collections.Immutable;
using System.Net;
using System.Text.Json;
using FluentAssertions;
using HPD.Gateway;
using Microsoft.Extensions.Primitives;
using Xunit;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.ServiceDiscovery;

namespace HPD.Gateway.Tests;

public sealed class GatewayDestinationResolverTests
{
    [Fact]
    public async Task PreparedMembershipIsConsumedOnceThenDynamicResolutionUsesANewGeneration()
    {
        var profile = new ScriptedProfile(Capability(),
            new GatewayDiscoveryResult([new GatewayDnsDiscoveryEndpoint("orders.internal", 8080)]),
            new GatewayDiscoveryResult([new GatewayDnsDiscoveryEndpoint("orders.internal", 8081)]));
        using var resolver = Resolver(profile);
        GatewayRuntimePlan plan = await Plan(DiscoveryStaleBehavior.RejectActivationUntilFresh);

        var prepared = await resolver.PrepareAsync(plan, "native", CancellationToken.None);
        prepared.Application.Should().NotBeNull();
        resolver.RegisterForExchange(prepared.Application!).Should().BeTrue();

        var initial = await resolver.ResolveDestinationsAsync(plan.Clusters.Single().Destinations!, CancellationToken.None);
        initial.Destinations.Values.Should().ContainSingle().Which.Address.Should().Be("http://orders.internal:8080/");
        profile.CallCount.Should().Be(1);
        resolver.CompleteApplication(prepared.Application!, applied: true);

        var refreshed = await resolver.ResolveDestinationsAsync(plan.Clusters.Single().Destinations!, CancellationToken.None);
        refreshed.Destinations.Values.Should().ContainSingle().Which.Address.Should().Be("http://orders.internal:8081/");
        profile.CallCount.Should().Be(2);
        GatewayPreparedDependencyResolution pending = resolver.GetPendingResolutions(prepared.Application!.ApplicationId).Should().ContainSingle().Which;
        pending.MembershipGeneration.Should().BeGreaterThan(prepared.Application.Resolutions.Single().MembershipGeneration);
        pending.MembershipIdentity.Should().Be(GatewayRuntimeGraphIdentity.ComputeMembership(
            refreshed.Destinations, GatewayPreparedMembershipDisposition.Fresh));
    }

    [Fact]
    public async Task PublisherExchangesSymbolicGraphAndAcknowledgesTheExactResolvedGraph()
    {
        var profile = new ScriptedProfile(Capability(),
            new GatewayDiscoveryResult([new GatewayDnsDiscoveryEndpoint("orders.internal", 8080)]));
        using var resolver = Resolver(profile);
        GatewayRuntimePlan plan = await Plan(DiscoveryStaleBehavior.RejectActivationUntilFresh);
        GatewayPreparedApplication prepared = (await resolver.PrepareAsync(plan, "native", CancellationToken.None)).Application!;
        using var provider = new HpdProxyConfigProvider();
        using var listener = new HpdConfigChangeListener(provider);
        using var publisher = new GatewayRuntimePublisher(provider, listener, [provider], resolver);

        Task<GatewayPublicationOutcome> publication = publisher.PublishAsync(prepared, TimeSpan.FromSeconds(2));
        OwnedProxyConfig exchanged = await WaitForNative(provider, prepared.NativeRevisionId);
        exchanged.Clusters.Single().Destinations!.Single().Value.Metadata!
            .Should().ContainKey(GatewayRuntimePlanner.SymbolicDestinationMetadata);
        var resolved = await resolver.ResolveDestinationsAsync(exchanged.Clusters.Single().Destinations!, CancellationToken.None);
        ClusterConfig appliedCluster = exchanged.Clusters.Single() with { Destinations = resolved.Destinations };
        listener.ConfigurationApplied([new TestConfig(exchanged.RevisionId, exchanged.Routes, [appliedCluster])]);

        (await publication).State.Should().Be(GatewayPublicationState.ActiveAcknowledged);
        prepared.Clusters.Single().Destinations.Should().Equal(resolved.Destinations);
    }

    [Fact]
    public async Task AppliedObserverPromotesInitialAndDynamicMembershipWithoutNewCandidateRevision()
    {
        var profile = new ScriptedProfile(Capability(),
            new GatewayDiscoveryResult([new GatewayDnsDiscoveryEndpoint("orders.internal", 8080)]),
            new GatewayDiscoveryResult([new GatewayDnsDiscoveryEndpoint("orders.internal", 8081)]));
        using var resolver = Resolver(profile);
        GatewayRuntimePlan plan = await Plan(DiscoveryStaleBehavior.RejectActivationUntilFresh);
        GatewayPreparedApplication prepared = (await resolver.PrepareAsync(plan, "native", CancellationToken.None)).Application!;
        using var provider = new HpdProxyConfigProvider();
        using var observer = new GatewayRuntimeApplicationObserver(resolver, TimeProvider.System);
        using var listener = new HpdConfigChangeListener(provider, observer);
        using var publisher = new GatewayRuntimePublisher(provider, listener, [provider], resolver, observer);

        Task<GatewayPublicationOutcome> publication = publisher.PublishAsync(
            prepared, "namespace-a", "node-a", TimeSpan.FromSeconds(2));
        OwnedProxyConfig exchanged = await WaitForNative(provider, prepared.NativeRevisionId);
        ResolvedDestinationCollection initial = await resolver.ResolveDestinationsAsync(
            exchanged.Clusters.Single().Destinations!, CancellationToken.None);
        var initialConfig = new TestConfig(exchanged.RevisionId, exchanged.Routes,
            [exchanged.Clusters.Single() with { Destinations = initial.Destinations }]);
        listener.ConfigurationApplied([initialConfig]);
        await publication;
        GatewayAppliedRuntimeSnapshot first = observer.GetCurrent()!.Snapshot;

        ResolvedDestinationCollection refreshed = await resolver.ResolveDestinationsAsync(
            exchanged.Clusters.Single().Destinations!, CancellationToken.None);
        var refreshedConfig = new TestConfig(exchanged.RevisionId, exchanged.Routes,
            [exchanged.Clusters.Single() with { Destinations = refreshed.Destinations }]);
        listener.ConfigurationApplied([refreshedConfig]);
        GatewayAppliedRuntimeSnapshot second = observer.GetCurrent()!.Snapshot;

        second.CandidateId.Should().Be(first.CandidateId);
        second.ApplicationId.Should().Be(first.ApplicationId);
        second.Upstreams.Single().MembershipGeneration.Should().BeGreaterThan(
            first.Upstreams.Single().MembershipGeneration!.Value);
        second.Upstreams.Single().MembershipIdentity.Should().NotBe(first.Upstreams.Single().MembershipIdentity);
        second.Upstreams.Single().DestinationCount.Should().Be(1);
    }

    [Fact]
    public async Task SymbolicMembershipCannotBeConsumedBeforeExchangeRegistrationOrTwiceInitially()
    {
        var profile = new ScriptedProfile(Capability(),
            new GatewayDiscoveryResult([new GatewayDnsDiscoveryEndpoint("orders.internal", 8080)]),
            new GatewayDiscoveryResult([new GatewayDnsDiscoveryEndpoint("orders.internal", 8081)]));
        using var resolver = Resolver(profile);
        GatewayRuntimePlan plan = await Plan(DiscoveryStaleBehavior.RejectActivationUntilFresh);
        var prepared = await resolver.PrepareAsync(plan, "native", CancellationToken.None);

        Func<Task> beforeRegistration = async () => await resolver.ResolveDestinationsAsync(
            plan.Clusters.Single().Destinations!, CancellationToken.None);
        await beforeRegistration.Should().ThrowAsync<InvalidOperationException>();

        resolver.RegisterForExchange(prepared.Application!).Should().BeTrue();
        await resolver.ResolveDestinationsAsync(plan.Clusters.Single().Destinations!, CancellationToken.None);
        await resolver.ResolveDestinationsAsync(plan.Clusters.Single().Destinations!, CancellationToken.None);
        profile.CallCount.Should().Be(2);
    }

    [Fact]
    public async Task FailedInitialResolutionHonorsAllThreeStalePolicies()
    {
        foreach (DiscoveryStaleBehavior behavior in Enum.GetValues<DiscoveryStaleBehavior>())
        {
            var profile = new ScriptedProfile(Capability(), new InvalidOperationException("unavailable"));
            using var resolver = Resolver(profile);
            GatewayRuntimePlan plan = await Plan(behavior);
            var prepared = await resolver.PrepareAsync(plan, "native", CancellationToken.None);

            if (behavior == DiscoveryStaleBehavior.ServeUnavailableWhenStale)
            {
                prepared.Application.Should().NotBeNull();
                prepared.Application!.Clusters.Single().Destinations.Should().BeEmpty();
                prepared.Application.Resolutions.Single().Disposition.Should().Be(GatewayPreparedMembershipDisposition.UnavailableWhenStale);
            }
            else
            {
                prepared.Application.Should().BeNull();
                prepared.Diagnostics.Should().ContainSingle(item => item.Code == "discovery.preparation-failed");
            }
        }
    }

    [Fact]
    public async Task PreparedSourceInvalidationRejectsExchangeBeforePublication()
    {
        using var changed = new CancellationTokenSource();
        var profile = new ScriptedProfile(Capability(), new GatewayDiscoveryResult(
            [new GatewayDnsDiscoveryEndpoint("orders.internal", 8080)],
            new CancellationChangeToken(changed.Token)));
        using var resolver = Resolver(profile);
        GatewayRuntimePlan plan = await Plan(DiscoveryStaleBehavior.RejectActivationUntilFresh);
        GatewayPreparedApplication prepared = (await resolver.PrepareAsync(plan, "native", CancellationToken.None)).Application!;

        changed.Cancel();

        resolver.RegisterForExchange(prepared).Should().BeFalse();
        resolver.SourcesUnchanged(prepared).Should().BeFalse();
    }

    [Theory]
    [InlineData(DiscoveryStaleBehavior.RejectActivationUntilFresh, false, 3)]
    [InlineData(DiscoveryStaleBehavior.PermitLastKnownMembership, false, 1)]
    [InlineData(DiscoveryStaleBehavior.ServeUnavailableWhenStale, true, 2)]
    public async Task FailedDynamicRefreshUsesTheExactStalePolicy(
        DiscoveryStaleBehavior behavior,
        bool expectedEmpty,
        int expectedDisposition)
    {
        var profile = new ScriptedProfile(Capability(),
            new GatewayDiscoveryResult([new GatewayDnsDiscoveryEndpoint("orders.internal", 8080)]),
            new InvalidOperationException("refresh failed"));
        using var resolver = Resolver(profile);
        GatewayRuntimePlan plan = await Plan(behavior);
        GatewayPreparedApplication prepared = (await resolver.PrepareAsync(plan, "native", CancellationToken.None)).Application!;
        resolver.RegisterForExchange(prepared).Should().BeTrue();
        var initial = await resolver.ResolveDestinationsAsync(plan.Clusters.Single().Destinations!, CancellationToken.None);
        resolver.CompleteApplication(prepared, applied: true);

        var failed = await resolver.ResolveDestinationsAsync(plan.Clusters.Single().Destinations!, CancellationToken.None);

        failed.Destinations.Should().HaveCount(expectedEmpty ? 0 : 1);
        if (!expectedEmpty) failed.Destinations.Should().Equal(initial.Destinations);
        failed.ChangeToken.Should().NotBeNull();
        failed.ChangeToken!.ActiveChangeCallbacks.Should().BeTrue();
        resolver.GetPendingResolutions(prepared.ApplicationId).Should().ContainSingle()
            .Which.Disposition.Should().Be((GatewayPreparedMembershipDisposition)expectedDisposition);
    }

    [Theory]
    [InlineData(DiscoveryStaleBehavior.RejectActivationUntilFresh, GatewayAppliedMembershipDisposition.RefreshFailed)]
    [InlineData(DiscoveryStaleBehavior.PermitLastKnownMembership, GatewayAppliedMembershipDisposition.LastKnownMembership)]
    [InlineData(DiscoveryStaleBehavior.ServeUnavailableWhenStale, GatewayAppliedMembershipDisposition.UnavailableWhenStale)]
    public async Task FailedRefreshBecomesDistinctAppliedRuntimeTruth(
        DiscoveryStaleBehavior behavior,
        GatewayAppliedMembershipDisposition expectedDisposition)
    {
        var profile = new ScriptedProfile(Capability(),
            new GatewayDiscoveryResult([new GatewayDnsDiscoveryEndpoint("orders.internal", 8080)]),
            new InvalidOperationException("refresh failed"));
        using var resolver = Resolver(profile);
        GatewayPreparedApplication prepared = (await resolver.PrepareAsync(await Plan(behavior), "native", CancellationToken.None)).Application!;
        using var provider = new HpdProxyConfigProvider();
        using var observer = new GatewayRuntimeApplicationObserver(resolver, TimeProvider.System);
        using var listener = new HpdConfigChangeListener(provider, observer);
        using var publisher = new GatewayRuntimePublisher(provider, listener, [provider], resolver, observer);
        Task<GatewayPublicationOutcome> publication = publisher.PublishAsync(prepared, "namespace", "node", TimeSpan.FromSeconds(2));
        OwnedProxyConfig exchanged = await WaitForNative(provider, prepared.NativeRevisionId);
        ResolvedDestinationCollection initial = await resolver.ResolveDestinationsAsync(exchanged.Clusters.Single().Destinations!, CancellationToken.None);
        listener.ConfigurationApplied([new TestConfig(exchanged.RevisionId, exchanged.Routes,
            [exchanged.Clusters.Single() with { Destinations = initial.Destinations }])]);
        (await publication).State.Should().Be(GatewayPublicationState.ActiveAcknowledged);

        ResolvedDestinationCollection failed = await resolver.ResolveDestinationsAsync(exchanged.Clusters.Single().Destinations!, CancellationToken.None);
        listener.ConfigurationApplied([new TestConfig(exchanged.RevisionId, exchanged.Routes,
            [exchanged.Clusters.Single() with { Destinations = failed.Destinations }])]);

        GatewayAppliedUpstream applied = observer.GetCurrent()!.Snapshot.Upstreams.Single();
        applied.Disposition.Should().Be(expectedDisposition);
        applied.DestinationCount.Should().Be(behavior == DiscoveryStaleBehavior.ServeUnavailableWhenStale ? 0 : 1);
    }

    [Fact]
    public async Task StaticAndMalformedSymbolicDictionariesAreHandledFailClosed()
    {
        using var resolver = Resolver();
        ImmutableDictionary<string, DestinationConfig> ordinary = ImmutableDictionary<string, DestinationConfig>.Empty.Add(
            "static", new DestinationConfig { Address = "http://127.0.0.1:8080/" });
        var unchanged = await resolver.ResolveDestinationsAsync(ordinary, CancellationToken.None);
        unchanged.Destinations.Should().BeSameAs(ordinary);

        ImmutableDictionary<string, DestinationConfig> forged = ordinary.SetItem("static", ordinary["static"] with
        {
            Metadata = ImmutableDictionary<string, string>.Empty.Add("hpd.gateway.forged", "true"),
        });
        Func<Task> forgedCall = async () => await resolver.ResolveDestinationsAsync(forged, CancellationToken.None);
        await forgedCall.Should().ThrowAsync<InvalidOperationException>();

        GatewayRuntimePlan plan = await Plan(DiscoveryStaleBehavior.RejectActivationUntilFresh);
        ImmutableDictionary<string, DestinationConfig> symbolic = plan.Clusters.Single().Destinations!
            .ToImmutableDictionary(StringComparer.Ordinal);
        ImmutableDictionary<string, DestinationConfig> mixed = symbolic.Add("ordinary", ordinary["static"]);
        Func<Task> mixedCall = async () => await resolver.ResolveDestinationsAsync(mixed, CancellationToken.None);
        await mixedCall.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task CancellationInsensitiveProviderCannotHoldPreparationOpen()
    {
        var profile = new HangingProfile(Capability());
        using var resolver = Resolver(profile);
        GatewayRuntimePlan plan = await Plan(DiscoveryStaleBehavior.RejectActivationUntilFresh);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        var result = await resolver.PrepareAsync(plan, "native", cancellation.Token).AsTask()
            .WaitAsync(TimeSpan.FromSeconds(2));

        result.Application.Should().BeNull();
        result.Diagnostics.Should().ContainSingle(item => item.Code == "discovery.preparation-canceled");
    }

    [Fact]
    public async Task UnderlyingCancellationInsensitiveProviderConcurrencyNeverExceedsFanOut()
    {
        var profile = new ControlledHangingProfile(Capability());
        using var resolver = Resolver(profile);
        GatewayRuntimePlan plan = await Plan(DiscoveryStaleBehavior.RejectActivationUntilFresh);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        Task[] attempts = Enumerable.Range(0, 40).Select(async index =>
        {
            _ = await resolver.PrepareAsync(plan, "native", cancellation.Token);
        }).ToArray();
        await Task.WhenAll(attempts).WaitAsync(TimeSpan.FromSeconds(2));

        profile.CallCount.Should().Be(32);
        profile.MaximumConcurrency.Should().Be(32);
        profile.CompleteAll();
        await profile.AllCompleted.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task ConsecutiveFailedRefreshesReceiveDistinctRetryGenerationsBeforeRecovery()
    {
        var time = new ManualTimeProvider();
        var profile = new ScriptedProfile(Capability(),
            new GatewayDiscoveryResult([new GatewayDnsDiscoveryEndpoint("orders.internal", 8080)]),
            new InvalidOperationException("first failure"),
            new InvalidOperationException("second failure"),
            new GatewayDiscoveryResult([new GatewayDnsDiscoveryEndpoint("orders.internal", 8081)]));
        using var resolver = Resolver(time, profile);
        GatewayRuntimePlan plan = await Plan(DiscoveryStaleBehavior.PermitLastKnownMembership);
        GatewayPreparedApplication prepared = (await resolver.PrepareAsync(plan, "native", CancellationToken.None)).Application!;
        resolver.RegisterForExchange(prepared).Should().BeTrue();
        await resolver.ResolveDestinationsAsync(plan.Clusters.Single().Destinations!, CancellationToken.None);
        resolver.CompleteApplication(prepared, applied: true);

        ResolvedDestinationCollection firstFailure = await resolver.ResolveDestinationsAsync(
            plan.Clusters.Single().Destinations!, CancellationToken.None);
        firstFailure.ChangeToken!.HasChanged.Should().BeFalse();
        time.Advance(TimeSpan.FromSeconds(5));
        firstFailure.ChangeToken.HasChanged.Should().BeTrue();

        ResolvedDestinationCollection secondFailure = await resolver.ResolveDestinationsAsync(
            plan.Clusters.Single().Destinations!, CancellationToken.None);
        secondFailure.ChangeToken.Should().NotBeSameAs(firstFailure.ChangeToken);
        secondFailure.ChangeToken!.HasChanged.Should().BeFalse();
        time.Advance(TimeSpan.FromSeconds(5));
        secondFailure.ChangeToken.HasChanged.Should().BeTrue();

        ResolvedDestinationCollection recovered = await resolver.ResolveDestinationsAsync(
            plan.Clusters.Single().Destinations!, CancellationToken.None);
        recovered.Destinations.Values.Should().ContainSingle().Which.Address.Should().Be("http://orders.internal:8081/");
        recovered.ChangeToken!.HasChanged.Should().BeFalse();
        profile.CallCount.Should().Be(4);
    }

    [Fact]
    public void ProjectionIsDeterministicSinglePassBoundedAndRejectsDuplicates()
    {
        DiscoveryProfileCapability capability = Capability(maximum: 2);
        GatewayRuntimeDependencyBinding dependency = Dependency(capability);
        var singleUse = new SingleUseEnumerable<GatewayDiscoveryEndpoint>(
            [new GatewayDnsDiscoveryEndpoint("b.internal", 80), new GatewayDnsDiscoveryEndpoint("a.internal", 80)]);

        ImmutableDictionary<string, DestinationConfig> first = GatewayDiscoveryEndpointProjector.Project(dependency, capability, singleUse);
        ImmutableDictionary<string, DestinationConfig> reordered = GatewayDiscoveryEndpointProjector.Project(
            dependency, capability, [new GatewayDnsDiscoveryEndpoint("a.internal", 80), new GatewayDnsDiscoveryEndpoint("b.internal", 80)]);

        first.Should().Equal(reordered);
        singleUse.EnumerationCount.Should().Be(1);
        Action oversized = () => GatewayDiscoveryEndpointProjector.Project(dependency, capability,
            [new GatewayDnsDiscoveryEndpoint("a.internal", 80), new GatewayDnsDiscoveryEndpoint("b.internal", 80), new GatewayDnsDiscoveryEndpoint("c.internal", 80)]);
        oversized.Should().Throw<InvalidOperationException>().WithMessage("*bound*");
        Action duplicate = () => GatewayDiscoveryEndpointProjector.Project(dependency, capability,
            [new GatewayDnsDiscoveryEndpoint("a.internal", 80), new GatewayDnsDiscoveryEndpoint("a.internal", 80)]);
        duplicate.Should().Throw<InvalidOperationException>().WithMessage("*duplicate*");
    }

    [Fact]
    public void ProjectionEnforcesUriDnsIpSchemeAndTlsAuthority()
    {
        DiscoveryProfileCapability httpsCapability = Capability(schemes: [ServiceDiscoveryScheme.Https], tls: true);
        GatewayRuntimeDependencyBinding https = Dependency(httpsCapability, tlsServerName: "orders.internal");
        GatewayDiscoveryEndpointProjector.Project(https, httpsCapability,
            [new GatewayUriDiscoveryEndpoint(new Uri("https://orders.internal:443/"))]).Should().ContainSingle();

        Action ipHttps = () => GatewayDiscoveryEndpointProjector.Project(https, httpsCapability,
            [new GatewayIpDiscoveryEndpoint(IPAddress.Loopback, 443)]);
        Action wrongTls = () => GatewayDiscoveryEndpointProjector.Project(https, httpsCapability,
            [new GatewayUriDiscoveryEndpoint(new Uri("https://other.internal:443/"))]);
        ipHttps.Should().Throw<InvalidOperationException>();
        wrongTls.Should().Throw<InvalidOperationException>();

        DiscoveryProfileCapability httpCapability = Capability();
        GatewayDiscoveryEndpointProjector.Project(Dependency(httpCapability), httpCapability,
            [new GatewayIpDiscoveryEndpoint(IPAddress.Loopback, 8080, "orders.internal")])
            .Single().Value.Host.Should().Be("orders.internal");
    }

    [Fact]
    public void ProfileRegistryMaterializesOnceAndRejectsMaximumPlusOneAndDuplicates()
    {
        var singleUse = new SingleUseEnumerable<IGatewayDiscoveryRuntimeProfile>([new ScriptedProfile(Capability())]);
        var registry = new GatewayDiscoveryProfileRegistry(singleUse);
        registry.Count.Should().Be(1);
        singleUse.EnumerationCount.Should().Be(1);

        Action duplicates = () => new GatewayDiscoveryProfileRegistry([
            new ScriptedProfile(Capability()), new ScriptedProfile(Capability())]);
        duplicates.Should().Throw<ArgumentException>().WithMessage("*duplicated*");
        Action oversized = () => new GatewayDiscoveryProfileRegistry(Enumerable.Range(0, 33)
            .Select(index => new ScriptedProfile(Capability(id: $"profile-{index:D2}"))));
        oversized.Should().Throw<ArgumentException>().WithMessage("*32*");
    }

    private static GatewayDestinationResolver Resolver(params IGatewayDiscoveryRuntimeProfile[] profiles) =>
        new(new GatewayDiscoveryProfileRegistry(profiles), new AcceptingConfigValidator(), TimeProvider.System);

    private static GatewayDestinationResolver Resolver(TimeProvider timeProvider, params IGatewayDiscoveryRuntimeProfile[] profiles) =>
        new(new GatewayDiscoveryProfileRegistry(profiles), new AcceptingConfigValidator(), timeProvider);

    private static async Task<OwnedProxyConfig> WaitForNative(HpdProxyConfigProvider provider, string nativeRevisionId)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!timeout.IsCancellationRequested)
        {
            if (provider.GetConfig() is OwnedProxyConfig owned && owned.NativeRevisionId == nativeRevisionId) return owned;
            await Task.Delay(1, timeout.Token).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
        throw new TimeoutException();
    }

    private static async Task<GatewayRuntimePlan> Plan(DiscoveryStaleBehavior staleBehavior)
    {
        DiscoveryProfileCapability capability = Capability();
        HostCapabilitySnapshot capabilities = HostCapabilitySnapshot.Create(new HostCapabilityRegistration
        {
            DiscoveryProfiles = [capability],
        });
        GatewayConfiguration configuration = new()
        {
            SchemaVersion = new GatewaySchemaVersion(1, 0),
            CanonicalizationVersion = 1,
            Routes = [new RouteDeclaration
            {
                Id = new RouteId("route"),
                Match = new HttpRouteMatch { Path = "/{**catch-all}" },
                Upstream = new UpstreamId("backend"),
            }],
            Upstreams = [new UpstreamDeclaration
            {
                Id = new UpstreamId("backend"),
                Endpoints = new ServiceDiscoveryEndpointSource
                {
                    Profile = capability.Id,
                    Service = new ServiceDiscoveryName("orders"),
                    Schemes = [ServiceDiscoveryScheme.Http],
                    StaleBehavior = staleBehavior,
                },
                Transport = new UpstreamTransportDeclaration { UseProxy = false },
            }],
        };
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(configuration, GatewayJsonSerializerContext.Default.GatewayConfiguration);
        GatewayCandidateReadResult accepted = GatewayCandidateReader.Read(json, capabilities);
        accepted.IsAccepted.Should().BeTrue(string.Join(", ", accepted.Errors.Select(static error => error.Message)));
        var identity = new PublicationCandidateIdentity(new CandidateId("candidate"), "authority", "epoch", 1, accepted.CanonicalDocument!.ContentHash);
        GatewayRuntimePlanningResult result = await new GatewayRuntimePlanner(new AcceptingConfigValidator())
            .PlanAsync(accepted, identity, "native");
        return result.Plan!;
    }

    private static DiscoveryProfileCapability Capability(
        string id = "dns",
        int maximum = 256,
        ImmutableArray<ServiceDiscoveryScheme>? schemes = null,
        bool tls = false) => new(
            new DiscoveryProfileId(id), 1, DiscoveryRuntimeKind.Microsoft,
            [DiscoveryProviderKind.Configuration], schemes ?? [ServiceDiscoveryScheme.Http],
            [DiscoveryStaleBehavior.RejectActivationUntilFresh, DiscoveryStaleBehavior.PermitLastKnownMembership, DiscoveryStaleBehavior.ServeUnavailableWhenStale],
            maximum, true, true, true, tls, new ContentHash("sha-256", new string('a', 64)));

    private static GatewayRuntimeDependencyBinding Dependency(
        DiscoveryProfileCapability capability,
        string? tlsServerName = null) => new(
            "backend", capability.Id, new ServiceDiscoveryName("orders"), null,
            capability.Schemes, tlsServerName, DiscoveryStaleBehavior.RejectActivationUntilFresh,
            capability.BehaviorIdentity, capability.MaximumEndpoints);

    private sealed class ScriptedProfile : IGatewayDiscoveryRuntimeProfile
    {
        private readonly Queue<object> _results;
        internal ScriptedProfile(DiscoveryProfileCapability capability, params object[] results)
        {
            Capability = capability;
            _results = new(results);
        }
        public DiscoveryProfileCapability Capability { get; }
        internal int CallCount { get; private set; }
        public ValueTask<GatewayDiscoveryResult> ResolveAsync(GatewayDiscoveryRequest request, CancellationToken cancellationToken = default)
        {
            CallCount++;
            if (_results.Count == 0) return ValueTask.FromResult(new GatewayDiscoveryResult([]));
            object value = _results.Dequeue();
            return value is Exception exception
                ? ValueTask.FromException<GatewayDiscoveryResult>(exception)
                : ValueTask.FromResult((GatewayDiscoveryResult)value);
        }
    }

    private sealed class SingleUseEnumerable<T>(IEnumerable<T> values) : IEnumerable<T>
    {
        internal int EnumerationCount { get; private set; }
        public IEnumerator<T> GetEnumerator()
        {
            if (++EnumerationCount != 1) throw new InvalidOperationException("enumerated twice");
            return values.GetEnumerator();
        }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class HangingProfile(DiscoveryProfileCapability capability) : IGatewayDiscoveryRuntimeProfile
    {
        public DiscoveryProfileCapability Capability { get; } = capability;
        public ValueTask<GatewayDiscoveryResult> ResolveAsync(
            GatewayDiscoveryRequest request,
            CancellationToken cancellationToken = default) =>
            new(new TaskCompletionSource<GatewayDiscoveryResult>(TaskCreationOptions.RunContinuationsAsynchronously).Task);
    }

    private sealed class ControlledHangingProfile(DiscoveryProfileCapability capability) : IGatewayDiscoveryRuntimeProfile
    {
        private readonly object _gate = new();
        private readonly List<TaskCompletionSource<GatewayDiscoveryResult>> _calls = [];
        private readonly TaskCompletionSource _allCompleted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _active;
        private int _maximum;
        public DiscoveryProfileCapability Capability { get; } = capability;
        internal int CallCount { get; private set; }
        internal int MaximumConcurrency => Volatile.Read(ref _maximum);
        internal Task AllCompleted => _allCompleted.Task;

        public ValueTask<GatewayDiscoveryResult> ResolveAsync(GatewayDiscoveryRequest request, CancellationToken cancellationToken = default)
        {
            var completion = new TaskCompletionSource<GatewayDiscoveryResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_gate)
            {
                _calls.Add(completion);
                CallCount++;
                int active = Interlocked.Increment(ref _active);
                int observed;
                while (active > (observed = Volatile.Read(ref _maximum)))
                    if (Interlocked.CompareExchange(ref _maximum, active, observed) == observed) break;
            }
            return new ValueTask<GatewayDiscoveryResult>(Complete(completion.Task));
        }

        internal void CompleteAll()
        {
            TaskCompletionSource<GatewayDiscoveryResult>[] calls;
            lock (_gate) calls = [.. _calls];
            foreach (TaskCompletionSource<GatewayDiscoveryResult> call in calls)
                call.TrySetResult(new GatewayDiscoveryResult([]));
        }

        private async Task<GatewayDiscoveryResult> Complete(Task<GatewayDiscoveryResult> task)
        {
            try { return await task; }
            finally
            {
                if (Interlocked.Decrement(ref _active) == 0) _allCompleted.TrySetResult();
            }
        }
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private readonly object _gate = new();
        private readonly List<ManualTimer> _timers = [];
        private DateTimeOffset _utcNow = DateTimeOffset.UnixEpoch;
        public override DateTimeOffset GetUtcNow() => _utcNow;
        public override long GetTimestamp() => _utcNow.UtcTicks;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ManualTimer(this, callback, state, _utcNow + dueTime);
            lock (_gate) _timers.Add(timer);
            return timer;
        }
        internal void Advance(TimeSpan amount)
        {
            ManualTimer[] due;
            lock (_gate)
            {
                _utcNow += amount;
                due = _timers.Where(timer => !timer.Disposed && timer.Due <= _utcNow).ToArray();
            }
            foreach (ManualTimer timer in due) timer.Fire();
        }
        private sealed class ManualTimer(ManualTimeProvider owner, TimerCallback callback, object? state, DateTimeOffset due) : ITimer
        {
            internal DateTimeOffset Due { get; private set; } = due;
            internal bool Disposed { get; private set; }
            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                if (Disposed) return false;
                Due = owner._utcNow + dueTime;
                return true;
            }
            internal void Fire()
            {
                if (Disposed) return;
                Disposed = true;
                callback(state);
            }
            public void Dispose() => Disposed = true;
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }

    private sealed class AcceptingConfigValidator : IConfigValidator
    {
        public ValueTask<IList<Exception>> ValidateClusterAsync(ClusterConfig cluster) => ValueTask.FromResult<IList<Exception>>([]);
        public ValueTask<IList<Exception>> ValidateRouteAsync(RouteConfig route) => ValueTask.FromResult<IList<Exception>>([]);
    }

    private sealed class TestConfig(
        string revisionId,
        IReadOnlyList<RouteConfig> routes,
        IReadOnlyList<ClusterConfig> clusters) : IProxyConfig
    {
        public string RevisionId { get; } = revisionId;
        public IReadOnlyList<RouteConfig> Routes { get; } = routes;
        public IReadOnlyList<ClusterConfig> Clusters { get; } = clusters;
        public IChangeToken ChangeToken { get; } = new CancellationChangeToken(CancellationToken.None);
    }
}
