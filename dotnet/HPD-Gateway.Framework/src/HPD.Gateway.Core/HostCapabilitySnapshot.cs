using System.Collections.Immutable;
using HPD.Gateway.Abstractions;

namespace HPD.Gateway.Core;

[Flags]
public enum ListenerProtocols : byte
{
    None = 0,
    Http1 = 1,
    Http2 = 2,
    Http3 = 4
}

public enum ListenerRole : byte
{
    DataPlane = 0,
    Management = 1
}

[Flags]
public enum GatewayDeclarationFamilies : ushort
{
    None = 0,
    Authorization = 1 << 0,
    Cors = 1 << 1,
    TrafficAdmission = 1 << 2,
    RequestTimeout = 1 << 3,
    OutputCache = 1 << 4,
    Telemetry = 1 << 5,
    Inspection = 1 << 6,
    RequestTransforms = 1 << 7,
    ResponseTransforms = 1 << 8,
    AllBaseline = Authorization | Cors | TrafficAdmission | RequestTimeout | OutputCache |
        Telemetry | Inspection | RequestTransforms | ResponseTransforms
}

public sealed record ListenerCapability(
    ListenerId Id,
    ListenerRole Role,
    ListenerProtocols Protocols,
    ImmutableArray<string> Hostnames,
    bool Tls);

public sealed record DiscoveryProviderCapability(
    ProviderId Id,
    ImmutableArray<string> SupportedParameters,
    ImmutableArray<string> RequiredParameters,
    bool AllowUnknownParameters,
    bool ProducesHttpsEndpoints);

public sealed record HostCapabilityRegistration
{
    public IEnumerable<ListenerCapability> Listeners { get; init; } = [];
    public IEnumerable<DiscoveryProviderCapability> DiscoveryProviders { get; init; } = [];
    public IEnumerable<ProviderId> SecretProviders { get; init; } = [];
    public GatewayDeclarationFamilies InstalledFamilies { get; init; }
    public IEnumerable<string> AuthorizationPolicies { get; init; } = [];
    public IEnumerable<string> CorsPolicies { get; init; } = [];
    public IEnumerable<string> TrafficAdmissionPolicies { get; init; } = [];
    public IEnumerable<string> RequestTimeoutPolicies { get; init; } = [];
    public IEnumerable<string> OutputCachePolicies { get; init; } = [];
    public IEnumerable<string> SessionAffinityPolicies { get; init; } = [];
    public IEnumerable<string> SessionAffinityFailurePolicies { get; init; } = [];
    public IEnumerable<string> PassiveHealthPolicies { get; init; } = [];
    public IEnumerable<string> ActiveHealthPolicies { get; init; } = [];
}

public sealed class HostCapabilitySnapshot
{
    private HostCapabilitySnapshot(
        ImmutableDictionary<ListenerId, ListenerCapability> listeners,
        ImmutableDictionary<ProviderId, DiscoveryProviderCapability> discoveryProviders,
        ImmutableHashSet<ProviderId> secretProviders,
        HostCapabilityRegistration registration)
    {
        Listeners = listeners;
        DiscoveryProviders = discoveryProviders;
        SecretProviders = secretProviders;
        InstalledFamilies = registration.InstalledFamilies;
        AuthorizationPolicies = Names(registration.AuthorizationPolicies);
        CorsPolicies = Names(registration.CorsPolicies);
        TrafficAdmissionPolicies = Names(registration.TrafficAdmissionPolicies);
        RequestTimeoutPolicies = Names(registration.RequestTimeoutPolicies);
        OutputCachePolicies = Names(registration.OutputCachePolicies);
        SessionAffinityPolicies = Names(registration.SessionAffinityPolicies);
        SessionAffinityFailurePolicies = Names(registration.SessionAffinityFailurePolicies);
        PassiveHealthPolicies = Names(registration.PassiveHealthPolicies);
        ActiveHealthPolicies = Names(registration.ActiveHealthPolicies);
    }

    public ImmutableDictionary<ListenerId, ListenerCapability> Listeners { get; }
    public ImmutableDictionary<ProviderId, DiscoveryProviderCapability> DiscoveryProviders { get; }
    public ImmutableHashSet<ProviderId> SecretProviders { get; }
    public GatewayDeclarationFamilies InstalledFamilies { get; }
    public ImmutableHashSet<string> AuthorizationPolicies { get; }
    public ImmutableHashSet<string> CorsPolicies { get; }
    public ImmutableHashSet<string> TrafficAdmissionPolicies { get; }
    public ImmutableHashSet<string> RequestTimeoutPolicies { get; }
    public ImmutableHashSet<string> OutputCachePolicies { get; }
    public ImmutableHashSet<string> SessionAffinityPolicies { get; }
    public ImmutableHashSet<string> SessionAffinityFailurePolicies { get; }
    public ImmutableHashSet<string> PassiveHealthPolicies { get; }
    public ImmutableHashSet<string> ActiveHealthPolicies { get; }

    public static HostCapabilitySnapshot Create(HostCapabilityRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        if ((registration.InstalledFamilies & ~GatewayDeclarationFamilies.AllBaseline) != 0)
            throw new ArgumentException("Installed declaration-family flags are invalid.", nameof(registration));

        var listeners = ImmutableDictionary.CreateBuilder<ListenerId, ListenerCapability>();
        foreach (var listener in Required(registration.Listeners, nameof(registration.Listeners)))
        {
            if (!GatewayIdentifier.IsCanonical(listener.Id.Value)) throw new ArgumentException("Listener identity is not canonical.", nameof(registration));
            if (!Enum.IsDefined(listener.Role) || listener.Protocols == ListenerProtocols.None || (listener.Protocols & ~(ListenerProtocols.Http1 | ListenerProtocols.Http2 | ListenerProtocols.Http3)) != 0)
                throw new ArgumentException("Listener role or protocols are invalid.", nameof(registration));
            if (listener.Hostnames.IsDefault || listener.Hostnames.Any(static host => !IsHostPattern(host)) ||
                listener.Hostnames.Distinct(StringComparer.OrdinalIgnoreCase).Count() != listener.Hostnames.Length)
                throw new ArgumentException("Listener hostnames are invalid or duplicated.", nameof(registration));
            if (!listeners.TryAdd(listener.Id, listener)) throw new ArgumentException("Listener identities must be unique.", nameof(registration));
        }

        var discoveries = ImmutableDictionary.CreateBuilder<ProviderId, DiscoveryProviderCapability>();
        foreach (var provider in Required(registration.DiscoveryProviders, nameof(registration.DiscoveryProviders)))
        {
            if (!GatewayIdentifier.IsCanonical(provider.Id.Value)) throw new ArgumentException("Discovery provider identity is not canonical.", nameof(registration));
            var supported = ValidateParameterNames(provider.SupportedParameters, nameof(registration));
            var required = ValidateParameterNames(provider.RequiredParameters, nameof(registration));
            if (!provider.AllowUnknownParameters && required.Except(supported, StringComparer.Ordinal).Any())
                throw new ArgumentException("Required discovery parameters must be supported.", nameof(registration));
            if (!discoveries.TryAdd(provider.Id, provider with { SupportedParameters = supported, RequiredParameters = required }))
                throw new ArgumentException("Discovery provider identities must be unique.", nameof(registration));
        }

        var secrets = ImmutableHashSet.CreateBuilder<ProviderId>();
        foreach (var provider in Required(registration.SecretProviders, nameof(registration.SecretProviders)))
        {
            if (!GatewayIdentifier.IsCanonical(provider.Value) || !secrets.Add(provider))
                throw new ArgumentException("Secret provider identities must be canonical and unique.", nameof(registration));
        }

        ValidateNames(registration.AuthorizationPolicies, nameof(registration.AuthorizationPolicies));
        ValidateNames(registration.CorsPolicies, nameof(registration.CorsPolicies));
        ValidateNames(registration.TrafficAdmissionPolicies, nameof(registration.TrafficAdmissionPolicies));
        ValidateNames(registration.RequestTimeoutPolicies, nameof(registration.RequestTimeoutPolicies));
        ValidateNames(registration.OutputCachePolicies, nameof(registration.OutputCachePolicies));
        ValidateNames(registration.SessionAffinityPolicies, nameof(registration.SessionAffinityPolicies));
        ValidateNames(registration.SessionAffinityFailurePolicies, nameof(registration.SessionAffinityFailurePolicies));
        ValidateNames(registration.PassiveHealthPolicies, nameof(registration.PassiveHealthPolicies));
        ValidateNames(registration.ActiveHealthPolicies, nameof(registration.ActiveHealthPolicies));
        return new HostCapabilitySnapshot(listeners.ToImmutable(), discoveries.ToImmutable(), secrets.ToImmutable(), registration);
    }

    private static ImmutableHashSet<string> Names(IEnumerable<string> values) => values.ToImmutableHashSet(StringComparer.Ordinal);

    private static void ValidateNames(IEnumerable<string>? values, string name)
    {
        var materialized = Required(values, name).ToArray();
        if (materialized.Any(static value => string.IsNullOrWhiteSpace(value)) || materialized.Distinct(StringComparer.Ordinal).Count() != materialized.Length)
            throw new ArgumentException("Capability names must be nonblank and unique using ordinal equality.", name);
    }

    private static ImmutableArray<string> ValidateParameterNames(ImmutableArray<string> values, string name)
    {
        if (values.IsDefault || values.Any(static value => string.IsNullOrWhiteSpace(value)) || values.Distinct(StringComparer.Ordinal).Count() != values.Length)
            throw new ArgumentException("Provider parameter names must be initialized, nonblank, and unique.", name);
        return values;
    }

    private static IEnumerable<T> Required<T>(IEnumerable<T>? values, string name) => values ?? throw new ArgumentException("Capability collection cannot be null.", name);

    private static bool IsHostPattern(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Any(static c => char.IsControl(c) || char.IsWhiteSpace(c))) return false;
        if (value == "*") return true;
        var host = value.StartsWith("*.", StringComparison.Ordinal) ? value[2..] : value;
        return !host.Contains('*') && Uri.CheckHostName(host.TrimEnd('.')) is UriHostNameType.Dns or UriHostNameType.IPv4 or UriHostNameType.IPv6;
    }
}
