using System.Collections.Immutable;
using HPD.Gateway;

namespace HPD.Gateway.Discovery.Microsoft;

public enum GatewayHostNameMetadataPolicy : byte
{
    Never = 0,
    AllEligibleEndpoints = 1,
}

public sealed record GatewayMicrosoftConfigurationOptions(
    string SectionName = "Services",
    GatewayHostNameMetadataPolicy HostNameMetadata = GatewayHostNameMetadataPolicy.Never);

public sealed record GatewayMicrosoftDnsOptions(
    TimeSpan DefaultRefreshPeriod,
    TimeSpan MinimumRetryPeriod,
    TimeSpan MaximumRetryPeriod,
    double RetryBackOffFactor,
    GatewayHostNameMetadataPolicy HostNameMetadata = GatewayHostNameMetadataPolicy.Never)
{
    public GatewayMicrosoftDnsOptions() : this(
        TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(30), 2) { }
}

public sealed record GatewayMicrosoftDnsSrvOptions(
    TimeSpan DefaultRefreshPeriod,
    TimeSpan MinimumRetryPeriod,
    TimeSpan MaximumRetryPeriod,
    double RetryBackOffFactor,
    string QuerySuffix,
    GatewayHostNameMetadataPolicy HostNameMetadata = GatewayHostNameMetadataPolicy.Never)
{
    public GatewayMicrosoftDnsSrvOptions(string querySuffix) : this(
        TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(30), 2, querySuffix) { }
}

public sealed class GatewayMicrosoftDiscoveryProfileBuilder
{
    private readonly List<GatewayMicrosoftProviderRegistration> _providers = [];
    private bool _sealed;

    public TimeSpan RefreshPeriod { get; set; } = TimeSpan.FromMinutes(1);
    public ImmutableArray<ServiceDiscoveryScheme> Schemes { get; set; } =
        [ServiceDiscoveryScheme.Https, ServiceDiscoveryScheme.Http];
    public int MaximumEndpoints { get; set; } = 256;

    public GatewayMicrosoftDiscoveryProfileBuilder AddConfiguration(
        GatewayMicrosoftConfigurationOptions? options = null)
    {
        Add(GatewayMicrosoftProviderRegistration.CreateConfiguration(options ?? new()));
        return this;
    }

    public GatewayMicrosoftDiscoveryProfileBuilder AddDns(GatewayMicrosoftDnsOptions? options = null)
    {
        Add(GatewayMicrosoftProviderRegistration.CreateDns(options ?? new()));
        return this;
    }

    public GatewayMicrosoftDiscoveryProfileBuilder AddDnsSrv(GatewayMicrosoftDnsSrvOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        Add(GatewayMicrosoftProviderRegistration.CreateDnsSrv(options));
        return this;
    }

    internal GatewayMicrosoftDiscoveryProfileSnapshot Seal(DiscoveryProfileId id)
    {
        if (_sealed) throw new InvalidOperationException("The Microsoft discovery profile is already sealed.");
        _sealed = true;
        return GatewayMicrosoftDiscoveryProfileSnapshot.Create(
            id, RefreshPeriod, Schemes, MaximumEndpoints, _providers);
    }

    private void Add(GatewayMicrosoftProviderRegistration registration)
    {
        if (_sealed) throw new InvalidOperationException("The Microsoft discovery profile is already sealed.");
        if (_providers.Count >= 64) throw new InvalidOperationException("A Microsoft discovery profile supports at most 64 provider registrations.");
        if (_providers.Any(existing => existing.Kind == registration.Kind))
            throw new InvalidOperationException($"The {registration.Kind} provider is already registered.");
        _providers.Add(registration);
    }
}

internal enum GatewayMicrosoftProviderRegistrationKind : byte { Configuration, Dns, DnsSrv }

internal sealed record GatewayMicrosoftProviderRegistration(
    GatewayMicrosoftProviderRegistrationKind Kind,
    GatewayMicrosoftConfigurationOptions? Configuration,
    GatewayMicrosoftDnsOptions? Dns,
    GatewayMicrosoftDnsSrvOptions? DnsSrv)
{
    internal static GatewayMicrosoftProviderRegistration CreateConfiguration(GatewayMicrosoftConfigurationOptions value) =>
        new(GatewayMicrosoftProviderRegistrationKind.Configuration, value with { }, null, null);
    internal static GatewayMicrosoftProviderRegistration CreateDns(GatewayMicrosoftDnsOptions value) =>
        new(GatewayMicrosoftProviderRegistrationKind.Dns, null, value with { }, null);
    internal static GatewayMicrosoftProviderRegistration CreateDnsSrv(GatewayMicrosoftDnsSrvOptions value) =>
        new(GatewayMicrosoftProviderRegistrationKind.DnsSrv, null, null, value with { });
}
