using System.Collections.Immutable;
using FluentAssertions;
using HPD.Gateway.ControlPlane;
using HPD.Gateway;
using Xunit;

namespace HPD.Gateway.Tests;

public sealed class GatewayHostCapabilityProjectionTests
{
    [Fact]
    public void Semantically_equivalent_registrations_have_the_same_canonical_snapshot_identity()
    {
        GatewayHostCapabilitySnapshotResponse first = GatewayHostCapabilityProjector.Project(
            HostCapabilitySnapshot.Create(Registration(reverse: false)));
        GatewayHostCapabilitySnapshotResponse second = GatewayHostCapabilityProjector.Project(
            HostCapabilitySnapshot.Create(Registration(reverse: true)));

        first.SnapshotAlgorithm.Should().Be("sha-256");
        first.SnapshotValue.Should().HaveLength(64).And.Be(second.SnapshotValue);
        first.Capabilities.Should().BeEquivalentTo(second.Capabilities, options => options.WithStrictOrdering());
        first.Capabilities.InstalledFamilies.Should().BeInAscendingOrder(StringComparer.Ordinal);
        first.Capabilities.Listeners.Select(static value => value.Id).Should().Equal("admin", "public");
        first.Capabilities.ProtectedCredentialHeaders.Should().Equal(
            "authorization", "cookie", "proxy-authorization", "x-api-key");
    }

    [Theory]
    [MemberData(nameof(BehaviorAffectingMutations))]
    public void Every_projected_behavior_affecting_change_changes_snapshot_identity(
        string field, HostCapabilityRegistration changed)
    {
        HostCapabilityRegistration baseline = Registration(reverse: false);
        string original = Identity(baseline);
        string updated = Identity(changed);

        updated.Should().NotBe(original, field);
    }

    [Fact]
    public void Sub_millisecond_profile_changes_remain_part_of_snapshot_identity()
    {
        HostCapabilityRegistration baseline = Registration(reverse: false);
        OutputCacheCapability profile = baseline.OutputCacheProfiles.Single();
        string original = Identity(baseline);

        HostCapabilityRegistration changed = baseline with
        {
            OutputCacheProfiles =
            [
                profile with { Expiration = profile.Expiration.Add(TimeSpan.FromTicks(1)) },
            ],
        };
        string updated = Identity(changed);

        updated.Should().NotBe(original);
    }

    [Fact]
    public void Host_registration_rejects_catalogs_that_cannot_be_bounded_for_projection()
    {
        Action tooMany = () => HostCapabilitySnapshot.Create(new HostCapabilityRegistration
        {
            AuthorizationPolicies = Enumerable.Range(0, 257).Select(static index => $"policy-{index}"),
        });
        Action longName = () => HostCapabilitySnapshot.Create(new HostCapabilityRegistration
        {
            CorsPolicies = [new string('a', 129)],
        });
        Action tooManyHosts = () => HostCapabilitySnapshot.Create(new HostCapabilityRegistration
        {
            Listeners =
            [
                new ListenerCapability(
                    new ListenerId("public"),
                    ListenerRole.DataPlane,
                    ListenerProtocols.Http1,
                    Enumerable.Range(0, 65).Select(static index => $"h{index}.example.com").ToImmutableArray(),
                    true),
            ],
        });

        tooMany.Should().Throw<ArgumentException>().WithMessage("*maximum of 256*");
        longName.Should().Throw<ArgumentException>().WithMessage("*bounded*");
        tooManyHosts.Should().Throw<ArgumentException>().WithMessage("*hostnames*");
    }

    [Fact]
    public void Discovery_profile_and_provider_catalogs_are_hard_bounded()
    {
        DiscoveryProfileCapability template = Registration(reverse: false).DiscoveryProfiles
            .Cast<DiscoveryProfileCapability>().First();
        Action tooManyProfiles = () => HostCapabilitySnapshot.Create(new HostCapabilityRegistration
        {
            DiscoveryProfiles = Enumerable.Range(0, 33)
                .Select(index => template with { Id = new DiscoveryProfileId($"profile-{index}") }),
        });
        Action tooManyProviders = () => HostCapabilitySnapshot.Create(new HostCapabilityRegistration
        {
            DiscoveryProfiles =
            [
                template with
                {
                    Providers = Enumerable.Repeat(DiscoveryProviderKind.Configuration, 65).ToImmutableArray(),
                },
            ],
        });

        tooManyProfiles.Should().Throw<ArgumentException>().WithMessage("*maximum of 32*");
        tooManyProviders.Should().Throw<ArgumentException>().WithMessage("*Discovery profile*");
    }

    [Theory]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaAaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("gggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggg")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public void Discovery_behavior_identity_rejects_noncanonical_hash_text(string hash)
    {
        DiscoveryProfileCapability profile = Registration(reverse: false).DiscoveryProfiles
            .Cast<DiscoveryProfileCapability>().First();
        Action action = () => HostCapabilitySnapshot.Create(new HostCapabilityRegistration
        {
            DiscoveryProfiles = [profile with { BehaviorIdentity = new ContentHash("sha-256", hash) }],
        });

        action.Should().Throw<ArgumentException>().WithMessage("*Discovery profile*");
    }

    [Fact]
    public void Discovery_behavior_identity_preserves_one_exact_lowercase_projection()
    {
        DiscoveryProfileCapability profile = Registration(reverse: false).DiscoveryProfiles
            .Cast<DiscoveryProfileCapability>().First() with
        {
            BehaviorIdentity = new ContentHash("sha-256", new string('a', 64)),
        };

        GatewayHostCapabilitySnapshotResponse projection = GatewayHostCapabilityProjector.Project(
            HostCapabilitySnapshot.Create(new HostCapabilityRegistration { DiscoveryProfiles = [profile] }));

        projection.Capabilities.DiscoveryProfiles.Should().ContainSingle()
            .Which.BehaviorIdentityValue.Should().Be(new string('a', 64));
    }

    [Fact]
    public void Registration_materialization_is_single_pass_and_stops_at_maximum_plus_one()
    {
        var infinite = new CountingInfiniteEnumerable<string>("policy");
        Action veryLarge = () => HostCapabilitySnapshot.Create(new HostCapabilityRegistration
        {
            AuthorizationPolicies = Enumerable.Range(0, int.MaxValue).Select(static index => $"policy-{index}"),
        });
        Action unbounded = () => HostCapabilitySnapshot.Create(new HostCapabilityRegistration
        {
            AuthorizationPolicies = infinite,
        });
        var singleUse = new SingleUseEnumerable<string>(["policy"]);

        veryLarge.Should().Throw<ArgumentException>().WithMessage("*maximum of 256*");
        unbounded.Should().Throw<ArgumentException>().WithMessage("*maximum of 256*");
        infinite.MoveNextCount.Should().Be(257);
        HostCapabilitySnapshot.Create(new HostCapabilityRegistration
        {
            AuthorizationPolicies = singleUse,
        }).AuthorizationPolicies.Should().ContainSingle("policy");
        singleUse.GetEnumeratorCount.Should().Be(1);
    }

    [Fact]
    public void Registration_does_not_swallow_source_enumeration_failures()
    {
        static IEnumerable<string> Throwing()
        {
            yield return "first";
            throw new InvalidOperationException("source-failed");
        }

        Action action = () => HostCapabilitySnapshot.Create(new HostCapabilityRegistration
        {
            AuthorizationPolicies = Throwing(),
        });

        action.Should().Throw<InvalidOperationException>().WithMessage("source-failed");
    }

    [Fact]
    public void Normalization_only_registration_changes_preserve_snapshot_identity()
    {
        HostCapabilityRegistration baseline = Registration(reverse: false);
        ListenerCapability[] listeners = baseline.Listeners.Cast<ListenerCapability>().Reverse()
            .Select(static listener => listener with
            {
                Hostnames = listener.Hostnames.Select(static host => host.ToLowerInvariant()).Reverse().ToImmutableArray(),
            }).ToArray();
        OutputCacheCapability cache = baseline.OutputCacheProfiles.Single();
        HostCapabilityRegistration normalized = baseline with
        {
            Listeners = listeners,
            DiscoveryProfiles = baseline.DiscoveryProfiles.Reverse(),
            SecretProviders = baseline.SecretProviders.Reverse(),
            AuthorizationPolicies = baseline.AuthorizationPolicies.Reverse(),
            OutputCacheProfiles = [cache with { HeaderNames = ["accept"] }],
            ProtectedCredentialHeaders = ["x-api-key"],
        };

        Identity(normalized).Should().Be(Identity(baseline));
    }

    public static IEnumerable<object[]> BehaviorAffectingMutations()
    {
        HostCapabilityRegistration baseline = Registration(reverse: false);
        ListenerCapability[] listeners = baseline.Listeners.Cast<ListenerCapability>().ToArray();
        DiscoveryProfileCapability[] discoveries = baseline.DiscoveryProfiles.Cast<DiscoveryProfileCapability>().ToArray();
        OutputCacheCapability cache = baseline.OutputCacheProfiles.Single();
        UpstreamResilienceCapability resilience = baseline.UpstreamResilienceProfiles.Single();

        yield return Case("installed families", baseline with { InstalledFamilies = GatewayDeclarationFamilies.AllBaseline });
        yield return Case("listener id", baseline with { Listeners = [listeners[0] with { Id = new("edge") }, listeners[1]] });
        yield return Case("listener role", baseline with { Listeners = [listeners[0] with { Role = ListenerRole.Management }, listeners[1]] });
        yield return Case("listener protocols", baseline with { Listeners = [listeners[0] with { Protocols = ListenerProtocols.Http1 }, listeners[1]] });
        yield return Case("listener hostname", baseline with { Listeners = [listeners[0] with { Hostnames = ["changed.example.com"] }, listeners[1]] });
        yield return Case("listener tls", baseline with { Listeners = [listeners[0] with { Tls = false }, listeners[1]] });
        yield return Case("discovery id", baseline with { DiscoveryProfiles = [discoveries[0] with { Id = new("changed") }, discoveries[1]] });
        yield return Case("discovery version", baseline with { DiscoveryProfiles = [discoveries[0] with { ContractVersion = 2 }, discoveries[1]] });
        yield return Case("discovery runtime", baseline with { DiscoveryProfiles = [discoveries[0] with { RuntimeKind = DiscoveryRuntimeKind.Governed }, discoveries[1]] });
        yield return Case("discovery providers", baseline with { DiscoveryProfiles = [discoveries[0] with { Providers = [DiscoveryProviderKind.Dns, DiscoveryProviderKind.Configuration] }, discoveries[1]] });
        yield return Case("discovery schemes", baseline with { DiscoveryProfiles = [discoveries[0] with { Schemes = [ServiceDiscoveryScheme.Http], RequiresExplicitTlsServerName = false }, discoveries[1]] });
        yield return Case("discovery stale", baseline with { DiscoveryProfiles = [discoveries[0] with { StaleBehaviors = [DiscoveryStaleBehavior.PermitLastKnownMembership] }, discoveries[1]] });
        yield return Case("discovery endpoint bound", baseline with { DiscoveryProfiles = [discoveries[0] with { MaximumEndpoints = 128 }, discoveries[1]] });
        yield return Case("discovery named", baseline with { DiscoveryProfiles = [discoveries[0] with { SupportsNamedEndpoints = false }, discoveries[1]] });
        yield return Case("discovery refresh", baseline with { DiscoveryProfiles = [discoveries[0] with { SupportsDynamicRefresh = false }, discoveries[1]] });
        yield return Case("discovery authority", baseline with { DiscoveryProfiles = [discoveries[0] with { SupportsHttpAuthorityProjection = false }, discoveries[1]] });
        yield return Case("discovery identity", baseline with { DiscoveryProfiles = [discoveries[0] with { BehaviorIdentity = new("sha-256", new string('c', 64)) }, discoveries[1]] });
        yield return Case("secret provider", baseline with { SecretProviders = [new ProviderId("other")] });
        yield return Case("authorization policy", baseline with { AuthorizationPolicies = ["other"] });
        yield return Case("cors policy", baseline with { CorsPolicies = ["other"] });
        yield return Case("admission policy", baseline with { TrafficAdmissionProfiles = [TrafficAdmissionTestData.Capability("other")] });
        yield return Case("timeout policy", baseline with { RequestTimeoutPolicies = ["other"] });
        yield return Case("affinity policy", baseline with { SessionAffinityPolicies = ["other"] });
        yield return Case("affinity failure policy", baseline with { SessionAffinityFailurePolicies = ["other"] });
        yield return Case("passive health policy", baseline with { PassiveHealthPolicies = ["other"] });
        yield return Case("active health policy", baseline with { ActiveHealthPolicies = ["other"] });
        yield return Case("inspector", baseline with { RequestInspectors = ["other"] });
        yield return Case("cache name", baseline with { OutputCacheProfiles = [cache with { Name = "other" }] });
        yield return Case("cache version", baseline with { OutputCacheProfiles = [cache with { Version = 2 }] });
        yield return Case("cache store", baseline with { OutputCacheProfiles = [cache with { StoreId = "other" }] });
        yield return Case("cache expiration", baseline with { OutputCacheProfiles = [cache with { Expiration = TimeSpan.FromSeconds(61) }] });
        yield return Case("cache body bound", baseline with { OutputCacheProfiles = [cache with { MaximumBodyBytes = 2_048 }] });
        yield return Case("cache capacity", baseline with { OutputCacheProfiles = [cache with { StoreCapacityBytes = 8_192 }] });
        yield return Case("cache query dimensions", baseline with { OutputCacheProfiles = [cache with { QueryKeys = ["country"] }] });
        yield return Case("cache header dimensions", baseline with { OutputCacheProfiles = [cache with { HeaderNames = ["Content-Type"] }] });
        yield return Case("resilience name", baseline with { UpstreamResilienceProfiles = [resilience with { Name = "other" }] });
        yield return Case("resilience version", baseline with { UpstreamResilienceProfiles = [resilience with { Version = 2 }] });
        yield return Case("resilience strategies", baseline with { UpstreamResilienceProfiles = [resilience with { Strategies = UpstreamResilienceStrategies.SelectedResponseRetry }] });
        yield return Case("resilience statuses", baseline with { UpstreamResilienceProfiles = [resilience with { RetryStatusCodes = [408, 504] }] });
        yield return Case("resilience attempts", baseline with { UpstreamResilienceProfiles = [resilience with { MaximumRetryAttempts = 3 }] });
        yield return Case("protected header", baseline with { ProtectedCredentialHeaders = ["X-Other-Credential"] });
        yield return Case("inspection spill", baseline with { AllowInspectionFileSpill = false });
    }

    private static object[] Case(string field, HostCapabilityRegistration registration) => [field, registration];

    private static string Identity(HostCapabilityRegistration registration) =>
        GatewayHostCapabilityProjector.Project(HostCapabilitySnapshot.Create(registration)).SnapshotValue;

    private static HostCapabilityRegistration Registration(bool reverse)
    {
        ListenerCapability[] listeners =
        [
            new(new ListenerId("public"), ListenerRole.DataPlane,
                ListenerProtocols.Http1 | ListenerProtocols.Http2, ["API.EXAMPLE.COM", "*.example.com"], true),
            new(new ListenerId("admin"), ListenerRole.Management, ListenerProtocols.Http1, ["admin.example.com"], true),
        ];
        DiscoveryProfileCapability[] discoveries =
        [
            new(new DiscoveryProfileId("z-discovery"), 1, DiscoveryRuntimeKind.Microsoft,
                [DiscoveryProviderKind.Configuration, DiscoveryProviderKind.Dns],
                [ServiceDiscoveryScheme.Https, ServiceDiscoveryScheme.Http],
                [DiscoveryStaleBehavior.RejectActivationUntilFresh, DiscoveryStaleBehavior.PermitLastKnownMembership],
                256, true, true, true, true, new("sha-256", new string('a', 64))),
            new(new DiscoveryProfileId("a-discovery"), 1, DiscoveryRuntimeKind.Governed,
                [DiscoveryProviderKind.Configuration], [ServiceDiscoveryScheme.Http],
                [DiscoveryStaleBehavior.ServeUnavailableWhenStale],
                64, false, true, false, false, new("sha-256", new string('b', 64))),
        ];
        string[] authorization = ["orders.write", "orders.read"];

        return new HostCapabilityRegistration
        {
            InstalledFamilies = GatewayDeclarationFamilies.All,
            Listeners = reverse ? listeners.Reverse() : listeners,
            DiscoveryProfiles = reverse ? discoveries.Reverse() : discoveries,
            SecretProviders = reverse
                ? [new ProviderId("vault"), new ProviderId("files")]
                : [new ProviderId("files"), new ProviderId("vault")],
            AuthorizationPolicies = reverse ? authorization.Reverse() : authorization,
            CorsPolicies = ["cors"],
            TrafficAdmissionProfiles = [TrafficAdmissionTestData.Capability("admission")],
            RequestTimeoutPolicies = ["timeout"],
            OutputCacheProfiles =
            [
                new OutputCacheCapability("cache", 1, true, "memory", OutputCacheStoreScope.ProcessLocal,
                    TimeSpan.FromMinutes(1), 1_024, 4_096, ["region"], ["Accept"]),
            ],
            SessionAffinityPolicies = ["cookie"],
            SessionAffinityFailurePolicies = ["redistribute"],
            PassiveHealthPolicies = ["passive"],
            ActiveHealthPolicies = ["active"],
            RequestInspectors = ["inspector"],
            UpstreamResilienceProfiles =
            [
                new UpstreamResilienceCapability("safe", 1,
                    UpstreamResilienceStrategies.SelectedResponseRetry | UpstreamResilienceStrategies.CircuitBreaker,
                    [408, 503], 2),
            ],
            ProtectedCredentialHeaders = ["X-Api-Key"],
            AllowInspectionFileSpill = true,
        };
    }

    private sealed class CountingInfiniteEnumerable<T>(T value) : IEnumerable<T>
    {
        public int MoveNextCount { get; private set; }

        public IEnumerator<T> GetEnumerator()
        {
            while (true)
            {
                MoveNextCount++;
                yield return value;
            }
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class SingleUseEnumerable<T>(IEnumerable<T> source) : IEnumerable<T>
    {
        public int GetEnumeratorCount { get; private set; }

        public IEnumerator<T> GetEnumerator()
        {
            GetEnumeratorCount++;
            if (GetEnumeratorCount != 1) throw new InvalidOperationException("enumerated twice");
            return source.GetEnumerator();
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
