using System.Collections.Immutable;
using FluentAssertions;
using HPD.Gateway;
using HPD.Gateway.Discovery.Microsoft;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.ServiceDiscovery;
using Microsoft.Extensions.ServiceDiscovery.Dns;
using Xunit;

namespace HPD.Gateway.Tests;

public sealed class GatewayMicrosoftDiscoveryProfileTests
{
    [Fact]
    public void Public_surface_is_exact_and_provider_specific()
    {
        typeof(GatewayMicrosoftDiscoveryExtensions).Assembly.GetExportedTypes()
            .Should().BeEquivalentTo([
                typeof(GatewayHostNameMetadataPolicy),
                typeof(GatewayMicrosoftConfigurationOptions),
                typeof(GatewayMicrosoftDiscoveryExtensions),
                typeof(GatewayMicrosoftDiscoveryProfileBuilder),
                typeof(GatewayMicrosoftDnsOptions),
                typeof(GatewayMicrosoftDnsSrvOptions),
            ]);
        typeof(GatewayMicrosoftDiscoveryExtensions).Namespace
            .Should().Be("HPD.Gateway.Discovery.Microsoft");
    }

    [Fact]
    public void ConfigurationOnlyIsTheDefaultAndCapabilityMatchesTheRuntimeSnapshot()
    {
        ServiceProvider services = Build(static gateway => gateway.AddMicrosoftDiscovery("aspire"));

        DiscoveryProfileCapability capability = services.GetRequiredService<HostCapabilitySnapshot>()
            .DiscoveryProfiles[new DiscoveryProfileId("aspire")];
        IGatewayDiscoveryRuntimeProfile runtime = services.GetRequiredService<IGatewayDiscoveryRuntimeProfile>();

        runtime.Capability.Should().Be(capability);
        capability.Providers.Should().Equal(DiscoveryProviderKind.Configuration);
        capability.BehaviorIdentity.Algorithm.Should().Be("sha-256");
        capability.BehaviorIdentity.Value.Should().MatchRegex("^[0-9a-f]{64}$");
        services.GetServices<IServiceEndpointProviderFactory>().Should().ContainSingle()
            .Which.GetType().Name.Should().Be("ConfigurationServiceEndpointProviderFactory");
        var sameBehavior = new GatewayMicrosoftDiscoveryProfileBuilder();
        sameBehavior.AddConfiguration();
        sameBehavior.Seal(new DiscoveryProfileId("another")).Capability.BehaviorIdentity
            .Should().Be(capability.BehaviorIdentity);
    }

    [Fact]
    public async Task ConfigurationProviderUsesTheRealMicrosoftResolverAndReloadToken()
    {
        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Services:orders:http:0"] = "http://127.0.0.1:5080",
        });
        ServiceProvider services = Build(
            gateway => gateway.AddMicrosoftDiscovery("aspire", profile =>
            {
                profile.Schemes = [ServiceDiscoveryScheme.Http];
                profile.AddConfiguration(new("Services", GatewayHostNameMetadataPolicy.AllEligibleEndpoints));
            }), configuration);
        IGatewayDiscoveryRuntimeProfile runtime = services.GetRequiredService<IGatewayDiscoveryRuntimeProfile>();

        GatewayDiscoveryResult result = await runtime.ResolveAsync(new(
            new DiscoveryProfileId("aspire"), new ServiceDiscoveryName("orders"), null,
            [ServiceDiscoveryScheme.Http], null));

        GatewayUriDiscoveryEndpoint endpoint = result.Endpoints.Should().ContainSingle().Which
            .Should().BeOfType<GatewayUriDiscoveryEndpoint>().Which;
        endpoint.Address.Should().Be(new Uri("http://127.0.0.1:5080/"));
        endpoint.HostName.Should().Be("orders");
        result.ChangeToken.Should().NotBeNull();

        configuration["Services:orders:http:0"] = "http://127.0.0.1:5081";
        ((IConfigurationRoot)configuration).Reload();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while (result.ChangeToken?.HasChanged != true)
            await Task.Delay(10, timeout.Token);
        GatewayDiscoveryResult refreshed = await runtime.ResolveAsync(new(
            new DiscoveryProfileId("aspire"), new ServiceDiscoveryName("orders"), null,
            [ServiceDiscoveryScheme.Http], null));
        refreshed.Endpoints.Should().ContainSingle().Which.Should().BeOfType<GatewayUriDiscoveryEndpoint>()
            .Which.Address.Should().Be(new Uri("http://127.0.0.1:5081/"));
    }

    [Fact]
    public async Task DnsProfileUsesMicrosoftResolutionAndProjectsTheSchemeDefaultPort()
    {
        ServiceProvider services = Build(gateway => gateway.AddMicrosoftDiscovery("dns", profile =>
        {
            profile.Schemes = [ServiceDiscoveryScheme.Http];
            profile.AddDns();
        }));
        IGatewayDiscoveryRuntimeProfile runtime = services.GetRequiredService<IGatewayDiscoveryRuntimeProfile>();

        GatewayDiscoveryResult result = await runtime.ResolveAsync(new(
            new DiscoveryProfileId("dns"), new ServiceDiscoveryName("localhost"), null,
            [ServiceDiscoveryScheme.Http], null));

        result.Endpoints.Should().NotBeEmpty();
        result.Endpoints.Should().AllSatisfy(endpoint => endpoint.Should().BeOfType<GatewayIpDiscoveryEndpoint>()
            .Which.Port.Should().Be(80));
        result.ChangeToken.Should().NotBeNull();
    }

    [Fact]
    public void ProviderOrderAndEveryProjectedOptionAreFrozen()
    {
        ServiceProvider services = Build(gateway => gateway.AddMicrosoftDiscovery("cluster", profile =>
        {
            profile.RefreshPeriod = TimeSpan.FromSeconds(17);
            profile.Schemes = [ServiceDiscoveryScheme.Http];
            profile.MaximumEndpoints = 111;
            profile.AddConfiguration(new("Backends", GatewayHostNameMetadataPolicy.AllEligibleEndpoints));
            profile.AddDns(new(TimeSpan.FromSeconds(19), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(23), 3,
                GatewayHostNameMetadataPolicy.Never));
            profile.AddDnsSrv(new(TimeSpan.FromSeconds(29), TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(31), 4,
                "svc.cluster.local", GatewayHostNameMetadataPolicy.AllEligibleEndpoints));
        }));

        services.GetServices<IServiceEndpointProviderFactory>().Select(static value => value.GetType().Name)
            .Should().Equal("ConfigurationServiceEndpointProviderFactory", "DnsServiceEndpointProviderFactory", "DnsSrvServiceEndpointProviderFactory");
        ServiceDiscoveryOptions root = services.GetRequiredService<IOptions<ServiceDiscoveryOptions>>().Value;
        root.AllowAllSchemes.Should().BeFalse();
        root.RefreshPeriod.Should().Be(TimeSpan.FromSeconds(17));
        root.AllowedSchemes.Should().Equal("http");
        ConfigurationServiceEndpointProviderOptions configuration = services.GetRequiredService<IOptions<ConfigurationServiceEndpointProviderOptions>>().Value;
        configuration.SectionName.Should().Be("Backends");
        DnsServiceEndpointProviderOptions dns = services.GetRequiredService<IOptionsMonitor<DnsServiceEndpointProviderOptions>>().CurrentValue;
        dns.DefaultRefreshPeriod.Should().Be(TimeSpan.FromSeconds(19));
        dns.MinRetryPeriod.Should().Be(TimeSpan.FromSeconds(2));
        dns.MaxRetryPeriod.Should().Be(TimeSpan.FromSeconds(23));
        dns.RetryBackOffFactor.Should().Be(3);
        DnsSrvServiceEndpointProviderOptions srv = services.GetRequiredService<IOptionsMonitor<DnsSrvServiceEndpointProviderOptions>>().CurrentValue;
        srv.QuerySuffix.Should().Be("svc.cluster.local");
        srv.ServiceDomainNameCallback.Should().BeNull();
    }

    [Fact]
    public void ResolvedMicrosoftOptionsAreCopiesAndCannotMutateRuntimeAuthority()
    {
        ServiceProvider services = Build(gateway => gateway.AddMicrosoftDiscovery("cluster", profile =>
        {
            profile.Schemes = [ServiceDiscoveryScheme.Http];
            profile.AddDns();
        }));
        IOptionsMonitor<DnsServiceEndpointProviderOptions> monitor = services.GetRequiredService<IOptionsMonitor<DnsServiceEndpointProviderOptions>>();
        DnsServiceEndpointProviderOptions first = monitor.CurrentValue;
        first.DefaultRefreshPeriod = TimeSpan.FromDays(7);
        first.ShouldApplyHostNameMetadata = static _ => true;

        DnsServiceEndpointProviderOptions second = monitor.CurrentValue;
        second.Should().NotBeSameAs(first);
        second.DefaultRefreshPeriod.Should().Be(TimeSpan.FromMinutes(1));
        second.ShouldApplyHostNameMetadata(ServiceEndpoint.Create(new System.Net.DnsEndPoint("orders", 80))).Should().BeFalse();
        Action named = () => monitor.Get("overlay");
        named.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void EveryBehaviorFieldChangesIdentityWhileEquivalentConstructionIsStable()
    {
        ContentHash baseline = Snapshot().Capability.BehaviorIdentity;
        Snapshot().Capability.BehaviorIdentity.Should().Be(baseline);
        var mutations = new[]
        {
            Snapshot(refresh: TimeSpan.FromSeconds(61)),
            Snapshot(schemes: [ServiceDiscoveryScheme.Http]),
            Snapshot(maximum: 255),
            Snapshot(section: "Backends"),
            Snapshot(configurationHost: GatewayHostNameMetadataPolicy.AllEligibleEndpoints),
            Snapshot(dnsRefresh: TimeSpan.FromSeconds(61)),
            Snapshot(dnsMinimum: TimeSpan.FromSeconds(2)),
            Snapshot(dnsMaximum: TimeSpan.FromSeconds(31)),
            Snapshot(dnsFactor: 3),
            Snapshot(dnsHost: GatewayHostNameMetadataPolicy.AllEligibleEndpoints),
            Snapshot(srvRefresh: TimeSpan.FromSeconds(61)),
            Snapshot(srvMinimum: TimeSpan.FromSeconds(2)),
            Snapshot(srvMaximum: TimeSpan.FromSeconds(31)),
            Snapshot(srvFactor: 3),
            Snapshot(suffix: "apps.cluster.local"),
            Snapshot(srvHost: GatewayHostNameMetadataPolicy.AllEligibleEndpoints),
            Snapshot(reverseProviders: true),
        };
        mutations.Select(static snapshot => snapshot.Capability.BehaviorIdentity)
            .Should().OnlyContain(identity => identity != baseline);
    }

    [Theory]
    [InlineData("")]
    [InlineData("svc_cluster.local")]
    [InlineData("Svc.cluster.local")]
    [InlineData("svc.cluster.local.")]
    [InlineData("-svc.cluster.local")]
    public void InvalidDnsSrvSuffixesFailBeforeRegistration(string suffix)
    {
        Action action = () => Snapshot(suffix: suffix);
        action.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void NumericAndCollectionBoundsFailBeforeDiRegistration()
    {
        Action noSchemes = () => Snapshot(schemes: []);
        Action duplicateSchemes = () => Snapshot(schemes: [ServiceDiscoveryScheme.Http, ServiceDiscoveryScheme.Http]);
        Action zeroEndpoints = () => Snapshot(maximum: 0);
        Action tooManyEndpoints = () => Snapshot(maximum: 257);
        Action tinyRefresh = () => Snapshot(refresh: TimeSpan.FromMilliseconds(9));
        Action hugeRefresh = () => Snapshot(refresh: TimeSpan.FromDays(1) + TimeSpan.FromTicks(1));
        Action invertedRetry = () => Snapshot(dnsMinimum: TimeSpan.FromSeconds(31), dnsMaximum: TimeSpan.FromSeconds(30));
        Action nonFiniteFactor = () => Snapshot(dnsFactor: double.NaN);

        noSchemes.Should().Throw<ArgumentException>();
        duplicateSchemes.Should().Throw<ArgumentException>();
        zeroEndpoints.Should().Throw<ArgumentOutOfRangeException>();
        tooManyEndpoints.Should().Throw<ArgumentOutOfRangeException>();
        tinyRefresh.Should().Throw<ArgumentOutOfRangeException>();
        hugeRefresh.Should().Throw<ArgumentOutOfRangeException>();
        invertedRetry.Should().Throw<ArgumentException>();
        nonFiniteFactor.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void DuplicateOrSecondMicrosoftProfilesFailDeterministically()
    {
        Action duplicateProvider = () => Snapshot(duplicateConfiguration: true);
        duplicateProvider.Should().Throw<InvalidOperationException>();

        var services = new ServiceCollection();
        Action duplicateProfile = () => services.AddHpdGateway(gateway =>
        {
            gateway.AddMicrosoftDiscovery("first");
            gateway.AddMicrosoftDiscovery("second");
        });
        duplicateProfile.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void NamedEndpointCapabilityIsConservativeAcrossTheCompleteProviderChain()
    {
        GatewayMicrosoftDiscoveryProfileSnapshot configurationAndSrv = SnapshotWithoutDns();
        configurationAndSrv.Capability.SupportsNamedEndpoints.Should().BeTrue();
        Snapshot().Capability.SupportsNamedEndpoints.Should().BeFalse("DNS A/AAAA cannot preserve a named endpoint");
    }

    [Fact]
    public async Task OwnershipGuardRejectsAReplacementOptionsAuthority()
    {
        await using ServiceProvider provider = Build(gateway => gateway.AddMicrosoftDiscovery("aspire"));
        var guard = new GatewayMicrosoftDiscoveryOwnershipGuard(
            provider.GetServices<IGatewayDiscoveryRuntimeProfile>(),
            provider.GetServices<IServiceEndpointProviderFactory>(),
            provider.GetServices<ServiceEndpointResolver>(),
            provider.GetRequiredService<GatewayMicrosoftDiscoveryProfileSnapshot>(),
            Options.Create(new ServiceDiscoveryOptions()),
            provider.GetRequiredService<IOptions<ConfigurationServiceEndpointProviderOptions>>(),
            provider.GetRequiredService<IOptionsMonitor<DnsServiceEndpointProviderOptions>>(),
            provider.GetRequiredService<IOptionsMonitor<DnsSrvServiceEndpointProviderOptions>>());

        Func<Task> start = () => guard.StartAsync(CancellationToken.None);
        await start.Should().ThrowAsync<InvalidOperationException>();
    }

    private static GatewayMicrosoftDiscoveryProfileSnapshot Snapshot(
        TimeSpan? refresh = null,
        ImmutableArray<ServiceDiscoveryScheme>? schemes = null,
        int maximum = 256,
        string section = "Services",
        GatewayHostNameMetadataPolicy configurationHost = GatewayHostNameMetadataPolicy.Never,
        TimeSpan? dnsRefresh = null,
        TimeSpan? dnsMinimum = null,
        TimeSpan? dnsMaximum = null,
        double dnsFactor = 2,
        GatewayHostNameMetadataPolicy dnsHost = GatewayHostNameMetadataPolicy.Never,
        TimeSpan? srvRefresh = null,
        TimeSpan? srvMinimum = null,
        TimeSpan? srvMaximum = null,
        double srvFactor = 2,
        string suffix = "svc.cluster.local",
        GatewayHostNameMetadataPolicy srvHost = GatewayHostNameMetadataPolicy.Never,
        bool reverseProviders = false,
        bool duplicateConfiguration = false)
    {
        var builder = new GatewayMicrosoftDiscoveryProfileBuilder
        {
            RefreshPeriod = refresh ?? TimeSpan.FromMinutes(1),
            Schemes = schemes ?? [ServiceDiscoveryScheme.Https, ServiceDiscoveryScheme.Http],
            MaximumEndpoints = maximum,
        };
        Action configuration = () => builder.AddConfiguration(new(section, configurationHost));
        Action dns = () => builder.AddDns(new(dnsRefresh ?? TimeSpan.FromMinutes(1), dnsMinimum ?? TimeSpan.FromSeconds(1),
            dnsMaximum ?? TimeSpan.FromSeconds(30), dnsFactor, dnsHost));
        Action srv = () => builder.AddDnsSrv(new(srvRefresh ?? TimeSpan.FromMinutes(1), srvMinimum ?? TimeSpan.FromSeconds(1),
            srvMaximum ?? TimeSpan.FromSeconds(30), srvFactor, suffix, srvHost));
        if (reverseProviders) { srv(); dns(); configuration(); }
        else { configuration(); dns(); srv(); }
        if (duplicateConfiguration) configuration();
        return builder.Seal(new DiscoveryProfileId("profile"));
    }

    private static GatewayMicrosoftDiscoveryProfileSnapshot SnapshotWithoutDns()
    {
        var builder = new GatewayMicrosoftDiscoveryProfileBuilder();
        builder.AddConfiguration();
        builder.AddDnsSrv(new("svc.cluster.local"));
        return builder.Seal(new DiscoveryProfileId("profile"));
    }

    private static ServiceProvider Build(Action<GatewayBuilder> configure, IConfiguration? configuration = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(configuration ?? new ConfigurationManager());
        services.AddHpdGateway(configure);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }
}
