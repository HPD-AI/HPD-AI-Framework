using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using HPD.Gateway;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Microsoft.Extensions.ServiceDiscovery;
using Microsoft.Extensions.ServiceDiscovery.Dns;

namespace HPD.Gateway.Discovery.Microsoft;

public static class GatewayMicrosoftDiscoveryExtensions
{
    public static GatewayBuilder AddMicrosoftDiscovery(
        this GatewayBuilder gateway,
        string profileId,
        Action<GatewayMicrosoftDiscoveryProfileBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(gateway);
        gateway.ThrowIfSealed();
        if (gateway.Services.Any(static descriptor => descriptor.ServiceType == typeof(GatewayMicrosoftDiscoveryMarker)))
            throw new InvalidOperationException("Only one Microsoft discovery profile may be registered in a governed Gateway composition.");

        var builder = new GatewayMicrosoftDiscoveryProfileBuilder();
        configure?.Invoke(builder);
        if (configure is null) builder.AddConfiguration();
        GatewayMicrosoftDiscoveryProfileSnapshot snapshot = builder.Seal(new DiscoveryProfileId(profileId));
        IServiceCollection services = gateway.Services;
        services.AddServiceDiscoveryCore();
        foreach (GatewayMicrosoftProviderRegistration provider in snapshot.Providers)
        {
            switch (provider.Kind)
            {
                case GatewayMicrosoftProviderRegistrationKind.Configuration:
                    services.AddConfigurationServiceEndpointProvider();
                    break;
                case GatewayMicrosoftProviderRegistrationKind.Dns:
                    services.AddDnsServiceEndpointProvider();
                    break;
                case GatewayMicrosoftProviderRegistrationKind.DnsSrv:
                    services.AddDnsSrvServiceEndpointProvider();
                    break;
                default:
                    throw new InvalidOperationException("Unknown Microsoft discovery provider registration.");
            }
        }

        services.AddSingleton(snapshot);
        services.AddSingleton<IOptions<ServiceDiscoveryOptions>>(
            new FrozenOptions<ServiceDiscoveryOptions>(() => ProjectServiceDiscovery(snapshot)));
        services.AddSingleton<IOptions<ConfigurationServiceEndpointProviderOptions>>(
            new FrozenOptions<ConfigurationServiceEndpointProviderOptions>(() => ProjectConfiguration(snapshot)));
        var dns = new FrozenOptions<DnsServiceEndpointProviderOptions>(() => ProjectDns(snapshot));
        services.AddSingleton<IOptions<DnsServiceEndpointProviderOptions>>(dns);
        services.AddSingleton<IOptionsMonitor<DnsServiceEndpointProviderOptions>>(dns);
        var dnsSrv = new FrozenOptions<DnsSrvServiceEndpointProviderOptions>(() => ProjectDnsSrv(snapshot));
        services.AddSingleton<IOptions<DnsSrvServiceEndpointProviderOptions>>(dnsSrv);
        services.AddSingleton<IOptionsMonitor<DnsSrvServiceEndpointProviderOptions>>(dnsSrv);
        services.AddSingleton<GatewayMicrosoftDiscoveryRuntimeProfile>();
        services.AddSingleton<IGatewayDiscoveryRuntimeProfile>(static provider =>
            provider.GetRequiredService<GatewayMicrosoftDiscoveryRuntimeProfile>());
        services.AddSingleton<GatewayMicrosoftDiscoveryMarker>();
        services.AddSingleton<IHostedService, GatewayMicrosoftDiscoveryOwnershipGuard>();
        gateway.AddDiscoveryCapabilities([snapshot.Capability]);
        return gateway;
    }

    private static ServiceDiscoveryOptions ProjectServiceDiscovery(GatewayMicrosoftDiscoveryProfileSnapshot snapshot) => new()
    {
        AllowAllSchemes = false,
        RefreshPeriod = snapshot.RefreshPeriod,
        AllowedSchemes = snapshot.Schemes.Select(static scheme => scheme == ServiceDiscoveryScheme.Https ? "https" : "http").ToList(),
    };

    private static ConfigurationServiceEndpointProviderOptions ProjectConfiguration(GatewayMicrosoftDiscoveryProfileSnapshot snapshot)
    {
        GatewayMicrosoftConfigurationOptions options = snapshot.Providers
            .FirstOrDefault(static provider => provider.Kind == GatewayMicrosoftProviderRegistrationKind.Configuration)?.Configuration
            ?? new GatewayMicrosoftConfigurationOptions();
        return new()
        {
            SectionName = options.SectionName,
            ShouldApplyHostNameMetadata = HostPolicy(options.HostNameMetadata),
        };
    }

    private static DnsServiceEndpointProviderOptions ProjectDns(GatewayMicrosoftDiscoveryProfileSnapshot snapshot)
    {
        GatewayMicrosoftDnsOptions options = snapshot.Providers
            .FirstOrDefault(static provider => provider.Kind == GatewayMicrosoftProviderRegistrationKind.Dns)?.Dns
            ?? new GatewayMicrosoftDnsOptions();
        return new()
        {
            DefaultRefreshPeriod = options.DefaultRefreshPeriod,
            MinRetryPeriod = options.MinimumRetryPeriod,
            MaxRetryPeriod = options.MaximumRetryPeriod,
            RetryBackOffFactor = options.RetryBackOffFactor,
            ShouldApplyHostNameMetadata = HostPolicy(options.HostNameMetadata),
        };
    }

    private static DnsSrvServiceEndpointProviderOptions ProjectDnsSrv(GatewayMicrosoftDiscoveryProfileSnapshot snapshot)
    {
        GatewayMicrosoftDnsSrvOptions options = snapshot.Providers
            .FirstOrDefault(static provider => provider.Kind == GatewayMicrosoftProviderRegistrationKind.DnsSrv)?.DnsSrv
            ?? new GatewayMicrosoftDnsSrvOptions("invalid.local");
        return new()
        {
            DefaultRefreshPeriod = options.DefaultRefreshPeriod,
            MinRetryPeriod = options.MinimumRetryPeriod,
            MaxRetryPeriod = options.MaximumRetryPeriod,
            RetryBackOffFactor = options.RetryBackOffFactor,
            QuerySuffix = options.QuerySuffix,
            ServiceDomainNameCallback = null,
            ShouldApplyHostNameMetadata = HostPolicy(options.HostNameMetadata),
        };
    }

    private static Func<ServiceEndpoint, bool> HostPolicy(GatewayHostNameMetadataPolicy policy) => policy switch
    {
        GatewayHostNameMetadataPolicy.Never => NeverApplyHostName,
        GatewayHostNameMetadataPolicy.AllEligibleEndpoints => AlwaysApplyHostName,
        _ => throw new InvalidOperationException(),
    };

    private static bool NeverApplyHostName(ServiceEndpoint endpoint) => false;
    private static bool AlwaysApplyHostName(ServiceEndpoint endpoint) => true;
}

internal sealed class GatewayMicrosoftDiscoveryRuntimeProfile(
    GatewayMicrosoftDiscoveryProfileSnapshot snapshot,
    ServiceEndpointResolver resolver) : IGatewayDiscoveryRuntimeProfile
{
    public DiscoveryProfileCapability Capability => snapshot.Capability;

    public async ValueTask<GatewayDiscoveryResult> ResolveAsync(
        GatewayDiscoveryRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Profile != snapshot.Id || request.Schemes.IsDefaultOrEmpty ||
            request.Schemes.Any(scheme => !snapshot.Schemes.Contains(scheme)))
            throw new InvalidOperationException("The Microsoft discovery request does not match the sealed profile.");
        string queryText = Query(request);
        if (!ServiceEndpointQuery.TryParse(queryText, out ServiceEndpointQuery? parsed) ||
            !StringComparer.Ordinal.Equals(parsed.ToString(), queryText) ||
            !StringComparer.Ordinal.Equals(parsed.ServiceName, request.Service.Value) ||
            !StringComparer.Ordinal.Equals(parsed.EndpointName, request.Endpoint?.Value) ||
            !parsed.IncludedSchemes.SequenceEqual(request.Schemes.Select(static value => value == ServiceDiscoveryScheme.Https ? "https" : "http"), StringComparer.Ordinal))
            throw new InvalidOperationException("The typed Gateway query cannot be represented injectively by Microsoft Service Discovery.");

        ServiceEndpointSource source = await resolver.GetEndpointsAsync(queryText, cancellationToken).ConfigureAwait(false);
        return new GatewayDiscoveryResult(Project(source.Endpoints, request.Schemes[0]), source.ChangeToken);
    }

    private static string Query(GatewayDiscoveryRequest request)
    {
        string schemes = string.Join('+', request.Schemes.Select(static value => value == ServiceDiscoveryScheme.Https ? "https" : "http"));
        string endpoint = request.Endpoint is { } named ? $"_{named.Value}." : string.Empty;
        return $"{schemes}://{endpoint}{request.Service.Value}";
    }

    private static IEnumerable<GatewayDiscoveryEndpoint> Project(
        IReadOnlyList<ServiceEndpoint> endpoints,
        ServiceDiscoveryScheme preferredScheme)
    {
        foreach (ServiceEndpoint endpoint in endpoints)
        {
            string? hostName = endpoint.Features.Get<IHostNameFeature>()?.HostName;
            yield return endpoint.EndPoint switch
            {
                global::Microsoft.Extensions.ServiceDiscovery.UriEndPoint uri => new GatewayUriDiscoveryEndpoint(uri.Uri, hostName),
                DnsEndPoint dns => new GatewayDnsDiscoveryEndpoint(dns.Host, EffectivePort(dns.Port, preferredScheme), hostName),
                IPEndPoint ip => new GatewayIpDiscoveryEndpoint(ip.Address, EffectivePort(ip.Port, preferredScheme), hostName),
                _ => throw new InvalidOperationException("The Microsoft provider returned an unsupported endpoint kind."),
            };
        }
    }

    private static int EffectivePort(int port, ServiceDiscoveryScheme scheme) => port != 0
        ? port
        : scheme == ServiceDiscoveryScheme.Https ? 443 : 80;
}

internal sealed class FrozenOptions<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] T>(Func<T> projector) : IOptions<T>, IOptionsMonitor<T> where T : class
{
    public T Value => projector();
    public T CurrentValue => projector();
    public T Get(string? name) => string.IsNullOrEmpty(name)
        ? projector()
        : throw new InvalidOperationException("Named Microsoft discovery options are not supported by the governed profile.");
    public IDisposable? OnChange(Action<T, string?> listener) => FrozenChangeRegistration.Instance;
    private sealed class FrozenChangeRegistration : IDisposable
    {
        internal static FrozenChangeRegistration Instance { get; } = new();
        public void Dispose() { }
    }
}

internal sealed class GatewayMicrosoftDiscoveryOwnershipGuard(
    IEnumerable<IGatewayDiscoveryRuntimeProfile> profiles,
    IEnumerable<IServiceEndpointProviderFactory> providerFactories,
    IEnumerable<ServiceEndpointResolver> resolvers,
    GatewayMicrosoftDiscoveryProfileSnapshot snapshot,
    IOptions<ServiceDiscoveryOptions> serviceOptions,
    IOptions<ConfigurationServiceEndpointProviderOptions> configurationOptions,
    IOptionsMonitor<DnsServiceEndpointProviderOptions> dnsOptions,
    IOptionsMonitor<DnsSrvServiceEndpointProviderOptions> dnsSrvOptions) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        string[] actualFactories = providerFactories.Select(static factory => factory.GetType().FullName ?? string.Empty).ToArray();
        string[] expectedFactories = snapshot.Providers.Select(static provider => provider.Kind switch
        {
            GatewayMicrosoftProviderRegistrationKind.Configuration => "Microsoft.Extensions.ServiceDiscovery.Configuration.ConfigurationServiceEndpointProviderFactory",
            GatewayMicrosoftProviderRegistrationKind.Dns => "Microsoft.Extensions.ServiceDiscovery.Dns.DnsServiceEndpointProviderFactory",
            GatewayMicrosoftProviderRegistrationKind.DnsSrv => "Microsoft.Extensions.ServiceDiscovery.Dns.DnsSrvServiceEndpointProviderFactory",
            _ => string.Empty,
        }).ToArray();
        if (profiles.Count(static profile => profile is GatewayMicrosoftDiscoveryRuntimeProfile) != 1 ||
            resolvers.Count() != 1 ||
            !actualFactories.SequenceEqual(expectedFactories, StringComparer.Ordinal) ||
            serviceOptions is not FrozenOptions<ServiceDiscoveryOptions> ||
            configurationOptions is not FrozenOptions<ConfigurationServiceEndpointProviderOptions> ||
            dnsOptions is not FrozenOptions<DnsServiceEndpointProviderOptions> ||
            dnsSrvOptions is not FrozenOptions<DnsSrvServiceEndpointProviderOptions>)
            throw new InvalidOperationException("The governed Microsoft discovery option/runtime ownership graph was replaced.");
        return Task.CompletedTask;
    }
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

internal sealed class GatewayMicrosoftDiscoveryMarker;
