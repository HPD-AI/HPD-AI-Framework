using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using HPD.Gateway;

namespace HPD.Gateway.Discovery.Microsoft;

internal sealed class GatewayMicrosoftDiscoveryProfileSnapshot
{
    private const int MaximumSectionNameBytes = 128;
    private const int MaximumSuffixBytes = 253;
    private static readonly TimeSpan MinimumPeriod = TimeSpan.FromMilliseconds(10);
    private static readonly TimeSpan MaximumPeriod = TimeSpan.FromDays(1);

    private GatewayMicrosoftDiscoveryProfileSnapshot(
        DiscoveryProfileId id,
        TimeSpan refreshPeriod,
        ImmutableArray<ServiceDiscoveryScheme> schemes,
        int maximumEndpoints,
        ImmutableArray<GatewayMicrosoftProviderRegistration> providers,
        DiscoveryProfileCapability capability)
    {
        Id = id;
        RefreshPeriod = refreshPeriod;
        Schemes = schemes;
        MaximumEndpoints = maximumEndpoints;
        Providers = providers;
        Capability = capability;
    }

    internal DiscoveryProfileId Id { get; }
    internal TimeSpan RefreshPeriod { get; }
    internal ImmutableArray<ServiceDiscoveryScheme> Schemes { get; }
    internal int MaximumEndpoints { get; }
    internal ImmutableArray<GatewayMicrosoftProviderRegistration> Providers { get; }
    internal DiscoveryProfileCapability Capability { get; }

    internal static GatewayMicrosoftDiscoveryProfileSnapshot Create(
        DiscoveryProfileId id,
        TimeSpan refreshPeriod,
        ImmutableArray<ServiceDiscoveryScheme> schemes,
        int maximumEndpoints,
        IEnumerable<GatewayMicrosoftProviderRegistration> registrations)
    {
        if (!GatewayIdentifier.IsCanonical(id.Value)) throw new ArgumentException("The discovery profile ID is not canonical.", nameof(id));
        ValidatePeriod(refreshPeriod, nameof(refreshPeriod));
        if (schemes.IsDefaultOrEmpty || schemes.Length > 2 || schemes.Any(static value => !Enum.IsDefined(value)) || schemes.Distinct().Count() != schemes.Length)
            throw new ArgumentException("Allowed schemes must be a nonempty, unique, ordered HTTP/HTTPS set.", nameof(schemes));
        if (maximumEndpoints is < 1 or > 256) throw new ArgumentOutOfRangeException(nameof(maximumEndpoints));
        ImmutableArray<GatewayMicrosoftProviderRegistration> providers = Materialize(registrations);
        if (providers.IsEmpty) throw new ArgumentException("At least one Microsoft discovery provider is required.", nameof(registrations));
        foreach (GatewayMicrosoftProviderRegistration provider in providers) Validate(provider);

        ContentHash identity = ComputeIdentity(refreshPeriod, schemes, maximumEndpoints, providers);
        ImmutableArray<DiscoveryProviderKind> providerKinds = providers.Select(static provider => provider.Kind switch
        {
            GatewayMicrosoftProviderRegistrationKind.Configuration => DiscoveryProviderKind.Configuration,
            GatewayMicrosoftProviderRegistrationKind.Dns => DiscoveryProviderKind.Dns,
            GatewayMicrosoftProviderRegistrationKind.DnsSrv => DiscoveryProviderKind.DnsSrv,
            _ => throw new InvalidOperationException(),
        }).ToImmutableArray();
        bool named = providerKinds.All(static provider => provider is DiscoveryProviderKind.Configuration or DiscoveryProviderKind.DnsSrv);
        bool hostMetadata = providers.Any(static provider => HostPolicy(provider) == GatewayHostNameMetadataPolicy.AllEligibleEndpoints);
        var capability = new DiscoveryProfileCapability(
            id, 1, DiscoveryRuntimeKind.Microsoft, providerKinds, schemes,
            [DiscoveryStaleBehavior.RejectActivationUntilFresh, DiscoveryStaleBehavior.PermitLastKnownMembership, DiscoveryStaleBehavior.ServeUnavailableWhenStale],
            maximumEndpoints, named, true, hostMetadata, schemes.Contains(ServiceDiscoveryScheme.Https), identity);
        _ = HostCapabilitySnapshot.Create(new HostCapabilityRegistration { DiscoveryProfiles = [capability] });
        return new(id, refreshPeriod, schemes, maximumEndpoints, providers, capability);
    }

    private static ImmutableArray<GatewayMicrosoftProviderRegistration> Materialize(IEnumerable<GatewayMicrosoftProviderRegistration> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var builder = ImmutableArray.CreateBuilder<GatewayMicrosoftProviderRegistration>();
        using IEnumerator<GatewayMicrosoftProviderRegistration> enumerator = values.GetEnumerator();
        while (enumerator.MoveNext())
        {
            if (builder.Count == 64) throw new ArgumentException("A Microsoft discovery profile supports at most 64 providers.", nameof(values));
            builder.Add(enumerator.Current ?? throw new ArgumentException("Provider registrations cannot contain null.", nameof(values)));
        }
        ImmutableArray<GatewayMicrosoftProviderRegistration> result = builder.ToImmutable();
        if (result.Select(static value => value.Kind).Distinct().Count() != result.Length)
            throw new ArgumentException("Provider kinds must be unique.", nameof(values));
        return result;
    }

    private static void Validate(GatewayMicrosoftProviderRegistration provider)
    {
        if (!Enum.IsDefined(provider.Kind)) throw new ArgumentException("Provider kind is invalid.");
        switch (provider.Kind)
        {
            case GatewayMicrosoftProviderRegistrationKind.Configuration:
                GatewayMicrosoftConfigurationOptions configuration = provider.Configuration ?? throw new ArgumentException("Configuration options are required.");
                if (!VisibleAscii(configuration.SectionName, MaximumSectionNameBytes) || configuration.SectionName.Contains(':'))
                    throw new ArgumentException("The configuration section name is invalid or unbounded.");
                ValidatePolicy(configuration.HostNameMetadata);
                break;
            case GatewayMicrosoftProviderRegistrationKind.Dns:
                ValidateDns(provider.Dns ?? throw new ArgumentException("DNS options are required."));
                break;
            case GatewayMicrosoftProviderRegistrationKind.DnsSrv:
                GatewayMicrosoftDnsSrvOptions srv = provider.DnsSrv ?? throw new ArgumentException("DNS-SRV options are required.");
                ValidateDns(new(srv.DefaultRefreshPeriod, srv.MinimumRetryPeriod, srv.MaximumRetryPeriod, srv.RetryBackOffFactor, srv.HostNameMetadata));
                if (!CanonicalDnsName(srv.QuerySuffix) || Encoding.UTF8.GetByteCount(srv.QuerySuffix) > MaximumSuffixBytes)
                    throw new ArgumentException("The DNS-SRV query suffix must be a canonical lowercase DNS name.");
                break;
        }
    }

    private static void ValidateDns(GatewayMicrosoftDnsOptions options)
    {
        ValidatePeriod(options.DefaultRefreshPeriod, nameof(options.DefaultRefreshPeriod));
        ValidatePeriod(options.MinimumRetryPeriod, nameof(options.MinimumRetryPeriod));
        ValidatePeriod(options.MaximumRetryPeriod, nameof(options.MaximumRetryPeriod));
        if (options.MinimumRetryPeriod > options.MaximumRetryPeriod || !double.IsFinite(options.RetryBackOffFactor) || options.RetryBackOffFactor is < 1 or > 100)
            throw new ArgumentException("DNS retry settings are invalid or unbounded.");
        ValidatePolicy(options.HostNameMetadata);
    }

    private static void ValidatePeriod(TimeSpan value, string name)
    {
        if (value < MinimumPeriod || value > MaximumPeriod) throw new ArgumentOutOfRangeException(name);
    }

    private static void ValidatePolicy(GatewayHostNameMetadataPolicy value)
    {
        if (!Enum.IsDefined(value)) throw new ArgumentOutOfRangeException(nameof(value));
    }

    private static GatewayHostNameMetadataPolicy HostPolicy(GatewayMicrosoftProviderRegistration provider) => provider.Kind switch
    {
        GatewayMicrosoftProviderRegistrationKind.Configuration => provider.Configuration!.HostNameMetadata,
        GatewayMicrosoftProviderRegistrationKind.Dns => provider.Dns!.HostNameMetadata,
        GatewayMicrosoftProviderRegistrationKind.DnsSrv => provider.DnsSrv!.HostNameMetadata,
        _ => throw new InvalidOperationException(),
    };

    private static bool VisibleAscii(string value, int maximumBytes) => !string.IsNullOrWhiteSpace(value) &&
        value.Length <= maximumBytes && value.All(static character => character is >= (char)0x21 and <= (char)0x7e);

    private static bool CanonicalDnsName(string value)
    {
        if (string.IsNullOrEmpty(value) || value[^1] == '.' || value.Length > 253) return false;
        string[] labels = value.Split('.');
        return labels.All(static label => label.Length is >= 1 and <= 63 && label[0] != '-' && label[^1] != '-' &&
            label.All(static character => character is >= 'a' and <= 'z' or >= '0' and <= '9' or '-'));
    }

    private static ContentHash ComputeIdentity(
        TimeSpan refreshPeriod,
        ImmutableArray<ServiceDiscoveryScheme> schemes,
        int maximumEndpoints,
        ImmutableArray<GatewayMicrosoftProviderRegistration> providers)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        Write(writer, "hpd.gateway.discovery.microsoft.profile.v1");
        writer.Write(refreshPeriod.Ticks);
        writer.Write(maximumEndpoints);
        writer.Write(schemes.Length);
        foreach (ServiceDiscoveryScheme scheme in schemes) writer.Write((byte)scheme);
        writer.Write(providers.Length);
        foreach (GatewayMicrosoftProviderRegistration provider in providers)
        {
            writer.Write((byte)provider.Kind);
            switch (provider.Kind)
            {
                case GatewayMicrosoftProviderRegistrationKind.Configuration:
                    Write(writer, provider.Configuration!.SectionName);
                    writer.Write((byte)provider.Configuration.HostNameMetadata);
                    break;
                case GatewayMicrosoftProviderRegistrationKind.Dns:
                    WriteDns(writer, provider.Dns!);
                    break;
                case GatewayMicrosoftProviderRegistrationKind.DnsSrv:
                    GatewayMicrosoftDnsSrvOptions srv = provider.DnsSrv!;
                    WriteDns(writer, new(srv.DefaultRefreshPeriod, srv.MinimumRetryPeriod, srv.MaximumRetryPeriod, srv.RetryBackOffFactor, srv.HostNameMetadata));
                    Write(writer, srv.QuerySuffix);
                    break;
            }
        }
        writer.Flush();
        return new ContentHash("sha-256", Convert.ToHexStringLower(SHA256.HashData(stream.GetBuffer().AsSpan(0, checked((int)stream.Length)))));
    }

    private static void WriteDns(BinaryWriter writer, GatewayMicrosoftDnsOptions options)
    {
        writer.Write(options.DefaultRefreshPeriod.Ticks);
        writer.Write(options.MinimumRetryPeriod.Ticks);
        writer.Write(options.MaximumRetryPeriod.Ticks);
        writer.Write(BitConverter.DoubleToInt64Bits(options.RetryBackOffFactor));
        writer.Write((byte)options.HostNameMetadata);
    }

    private static void Write(BinaryWriter writer, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        writer.Write(bytes.Length);
        writer.Write(bytes);
    }
}
