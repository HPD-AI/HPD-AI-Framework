using System.Collections.Immutable;
using FluentAssertions;
using HPD.Gateway.Admin;
using HPD.Gateway.Abstractions;
using HPD.Gateway.Core;
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

    [Fact]
    public void Every_behavior_affecting_change_changes_snapshot_identity()
    {
        HostCapabilityRegistration baseline = Registration(reverse: false);
        string original = GatewayHostCapabilityProjector.Project(
            HostCapabilitySnapshot.Create(baseline)).SnapshotValue;

        HostCapabilityRegistration changed = baseline with { AllowInspectionFileSpill = false };
        string updated = GatewayHostCapabilityProjector.Project(
            HostCapabilitySnapshot.Create(changed)).SnapshotValue;

        updated.Should().NotBe(original);
    }

    [Fact]
    public void Sub_millisecond_profile_changes_remain_part_of_snapshot_identity()
    {
        HostCapabilityRegistration baseline = Registration(reverse: false);
        OutputCacheCapability profile = baseline.OutputCacheProfiles.Single();
        string original = GatewayHostCapabilityProjector.Project(
            HostCapabilitySnapshot.Create(baseline)).SnapshotValue;

        HostCapabilityRegistration changed = baseline with
        {
            OutputCacheProfiles =
            [
                profile with { Expiration = profile.Expiration.Add(TimeSpan.FromTicks(1)) },
            ],
        };
        string updated = GatewayHostCapabilityProjector.Project(
            HostCapabilitySnapshot.Create(changed)).SnapshotValue;

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

    private static HostCapabilityRegistration Registration(bool reverse)
    {
        ListenerCapability[] listeners =
        [
            new(new ListenerId("public"), ListenerRole.DataPlane,
                ListenerProtocols.Http1 | ListenerProtocols.Http2, ["API.EXAMPLE.COM", "*.example.com"], true),
            new(new ListenerId("admin"), ListenerRole.Management, ListenerProtocols.Http1, ["admin.example.com"], true),
        ];
        DiscoveryProviderCapability[] discoveries =
        [
            new(new ProviderId("z-discovery"), ["zone", "region"], ["region"], false, true),
            new(new ProviderId("a-discovery"), [], [], false, false),
        ];
        string[] authorization = ["orders.write", "orders.read"];

        return new HostCapabilityRegistration
        {
            InstalledFamilies = GatewayDeclarationFamilies.All,
            Listeners = reverse ? listeners.Reverse() : listeners,
            DiscoveryProviders = reverse ? discoveries.Reverse() : discoveries,
            SecretProviders = reverse
                ? [new ProviderId("vault"), new ProviderId("files")]
                : [new ProviderId("files"), new ProviderId("vault")],
            AuthorizationPolicies = reverse ? authorization.Reverse() : authorization,
            CorsPolicies = ["cors"],
            TrafficAdmissionPolicies = ["admission"],
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
}
