using System.Collections.Immutable;
using System.Text;
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
    UpstreamResilience = 1 << 9,
    CredentialDisposition = 1 << 10,
    AllBaseline = Authorization | Cors | TrafficAdmission | RequestTimeout | OutputCache |
        Telemetry | Inspection | RequestTransforms | ResponseTransforms,
    All = AllBaseline | UpstreamResilience | CredentialDisposition
}

[Flags]
public enum UpstreamResilienceStrategies : byte
{
    None = 0,
    SelectedResponseRetry = 1,
    CircuitBreaker = 2,
    OutboundConcurrencyLimiter = 4,
    PerAttemptTimeout = 8
}

public sealed record UpstreamResilienceCapability(
    string Name,
    int Version,
    UpstreamResilienceStrategies Strategies,
    ImmutableArray<int> RetryStatusCodes,
    int MaximumRetryAttempts);

public enum OutputCacheStoreScope : byte
{
    ProcessLocal = 0
}

public sealed record OutputCacheCapability(
    string Name,
    int Version,
    bool RetainsDefaultSafetyPolicy,
    string StoreId,
    OutputCacheStoreScope StoreScope,
    TimeSpan Expiration,
    long MaximumBodyBytes,
    long StoreCapacityBytes,
    ImmutableArray<string> QueryKeys,
    ImmutableArray<string> HeaderNames);

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
    public IEnumerable<OutputCacheCapability> OutputCacheProfiles { get; init; } = [];
    public IEnumerable<string> SessionAffinityPolicies { get; init; } = [];
    public IEnumerable<string> SessionAffinityFailurePolicies { get; init; } = [];
    public IEnumerable<string> PassiveHealthPolicies { get; init; } = [];
    public IEnumerable<string> ActiveHealthPolicies { get; init; } = [];
    public IEnumerable<string> RequestInspectors { get; init; } = [];
    public IEnumerable<UpstreamResilienceCapability> UpstreamResilienceProfiles { get; init; } = [];
    public IEnumerable<string> ProtectedCredentialHeaders { get; init; } = [];
    public bool AllowInspectionFileSpill { get; init; }
}

public sealed class HostCapabilitySnapshot
{
    private const int MaximumListeners = 64;
    private const int MaximumProviders = 64;
    private const int MaximumProviderParameters = 64;
    private const int MaximumNamedCapabilities = 256;
    private const int MaximumProfiles = 128;
    private const int MaximumListenerHostnames = 64;
    private const int MaximumCapabilityNameBytes = 128;
    private const int MaximumHostnameBytes = 256;

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
        ProtectedCredentialHeaders = ProtectedHeaders(registration.ProtectedCredentialHeaders);
        OutputCacheProfiles = CacheProfiles(registration.OutputCacheProfiles, ProtectedCredentialHeaders);
        OutputCachePolicies = OutputCacheProfiles.Keys.ToImmutableHashSet(StringComparer.Ordinal);
        SessionAffinityPolicies = Names(registration.SessionAffinityPolicies);
        SessionAffinityFailurePolicies = Names(registration.SessionAffinityFailurePolicies);
        PassiveHealthPolicies = Names(registration.PassiveHealthPolicies);
        ActiveHealthPolicies = Names(registration.ActiveHealthPolicies);
        RequestInspectors = Names(registration.RequestInspectors);
        UpstreamResilienceProfiles = ResilienceProfiles(registration.UpstreamResilienceProfiles);
        AllowInspectionFileSpill = registration.AllowInspectionFileSpill;
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
    public ImmutableDictionary<string, OutputCacheCapability> OutputCacheProfiles { get; }
    public ImmutableHashSet<string> SessionAffinityPolicies { get; }
    public ImmutableHashSet<string> SessionAffinityFailurePolicies { get; }
    public ImmutableHashSet<string> PassiveHealthPolicies { get; }
    public ImmutableHashSet<string> ActiveHealthPolicies { get; }
    public ImmutableHashSet<string> RequestInspectors { get; }
    public ImmutableDictionary<string, UpstreamResilienceCapability> UpstreamResilienceProfiles { get; }
    public ImmutableArray<string> ProtectedCredentialHeaders { get; }
    public bool AllowInspectionFileSpill { get; }

    public static HostCapabilitySnapshot Create(HostCapabilityRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        registration = registration with
        {
            Listeners = Required(registration.Listeners, nameof(registration.Listeners)).ToArray(),
            DiscoveryProviders = Required(registration.DiscoveryProviders, nameof(registration.DiscoveryProviders)).ToArray(),
            SecretProviders = Required(registration.SecretProviders, nameof(registration.SecretProviders)).ToArray(),
            AuthorizationPolicies = Required(registration.AuthorizationPolicies, nameof(registration.AuthorizationPolicies)).ToArray(),
            CorsPolicies = Required(registration.CorsPolicies, nameof(registration.CorsPolicies)).ToArray(),
            TrafficAdmissionPolicies = Required(registration.TrafficAdmissionPolicies, nameof(registration.TrafficAdmissionPolicies)).ToArray(),
            RequestTimeoutPolicies = Required(registration.RequestTimeoutPolicies, nameof(registration.RequestTimeoutPolicies)).ToArray(),
            OutputCacheProfiles = Required(registration.OutputCacheProfiles, nameof(registration.OutputCacheProfiles)).ToArray(),
            SessionAffinityPolicies = Required(registration.SessionAffinityPolicies, nameof(registration.SessionAffinityPolicies)).ToArray(),
            SessionAffinityFailurePolicies = Required(registration.SessionAffinityFailurePolicies, nameof(registration.SessionAffinityFailurePolicies)).ToArray(),
            PassiveHealthPolicies = Required(registration.PassiveHealthPolicies, nameof(registration.PassiveHealthPolicies)).ToArray(),
            ActiveHealthPolicies = Required(registration.ActiveHealthPolicies, nameof(registration.ActiveHealthPolicies)).ToArray(),
            RequestInspectors = Required(registration.RequestInspectors, nameof(registration.RequestInspectors)).ToArray(),
            UpstreamResilienceProfiles = Required(registration.UpstreamResilienceProfiles, nameof(registration.UpstreamResilienceProfiles)).ToArray(),
            ProtectedCredentialHeaders = Required(registration.ProtectedCredentialHeaders, nameof(registration.ProtectedCredentialHeaders)).ToArray()
        };
        RequireMaximum(registration.Listeners, MaximumListeners, nameof(registration.Listeners));
        RequireMaximum(registration.DiscoveryProviders, MaximumProviders, nameof(registration.DiscoveryProviders));
        RequireMaximum(registration.SecretProviders, MaximumProviders, nameof(registration.SecretProviders));
        RequireMaximum(registration.AuthorizationPolicies, MaximumNamedCapabilities, nameof(registration.AuthorizationPolicies));
        RequireMaximum(registration.CorsPolicies, MaximumNamedCapabilities, nameof(registration.CorsPolicies));
        RequireMaximum(registration.TrafficAdmissionPolicies, MaximumNamedCapabilities, nameof(registration.TrafficAdmissionPolicies));
        RequireMaximum(registration.RequestTimeoutPolicies, MaximumNamedCapabilities, nameof(registration.RequestTimeoutPolicies));
        RequireMaximum(registration.OutputCacheProfiles, MaximumProfiles, nameof(registration.OutputCacheProfiles));
        RequireMaximum(registration.SessionAffinityPolicies, MaximumNamedCapabilities, nameof(registration.SessionAffinityPolicies));
        RequireMaximum(registration.SessionAffinityFailurePolicies, MaximumNamedCapabilities, nameof(registration.SessionAffinityFailurePolicies));
        RequireMaximum(registration.PassiveHealthPolicies, MaximumNamedCapabilities, nameof(registration.PassiveHealthPolicies));
        RequireMaximum(registration.ActiveHealthPolicies, MaximumNamedCapabilities, nameof(registration.ActiveHealthPolicies));
        RequireMaximum(registration.RequestInspectors, MaximumNamedCapabilities, nameof(registration.RequestInspectors));
        RequireMaximum(registration.UpstreamResilienceProfiles, MaximumProfiles, nameof(registration.UpstreamResilienceProfiles));
        if ((registration.InstalledFamilies & ~GatewayDeclarationFamilies.All) != 0)
            throw new ArgumentException("Installed declaration-family flags are invalid.", nameof(registration));

        var listeners = ImmutableDictionary.CreateBuilder<ListenerId, ListenerCapability>();
        foreach (var listener in Required(registration.Listeners, nameof(registration.Listeners)))
        {
            if (!GatewayIdentifier.IsCanonical(listener.Id.Value)) throw new ArgumentException("Listener identity is not canonical.", nameof(registration));
            if (!Enum.IsDefined(listener.Role) || listener.Protocols == ListenerProtocols.None || (listener.Protocols & ~(ListenerProtocols.Http1 | ListenerProtocols.Http2 | ListenerProtocols.Http3)) != 0)
                throw new ArgumentException("Listener role or protocols are invalid.", nameof(registration));
            if (listener.Hostnames.IsDefault || listener.Hostnames.Length > MaximumListenerHostnames ||
                listener.Hostnames.Any(static host => !IsHostPattern(host) || !IsBoundedUtf8(host, MaximumHostnameBytes)) ||
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
        ValidateNames(registration.SessionAffinityPolicies, nameof(registration.SessionAffinityPolicies));
        ValidateNames(registration.SessionAffinityFailurePolicies, nameof(registration.SessionAffinityFailurePolicies));
        ValidateNames(registration.PassiveHealthPolicies, nameof(registration.PassiveHealthPolicies));
        ValidateNames(registration.ActiveHealthPolicies, nameof(registration.ActiveHealthPolicies));
        ValidateInspectorNames(registration.RequestInspectors, nameof(registration.RequestInspectors));
        _ = ResilienceProfiles(registration.UpstreamResilienceProfiles);
        _ = ProtectedHeaders(registration.ProtectedCredentialHeaders);
        _ = CacheProfiles(registration.OutputCacheProfiles, ProtectedHeaders(registration.ProtectedCredentialHeaders));
        return new HostCapabilitySnapshot(listeners.ToImmutable(), discoveries.ToImmutable(), secrets.ToImmutable(), registration);
    }

    private static ImmutableHashSet<string> Names(IEnumerable<string> values) => values.ToImmutableHashSet(StringComparer.Ordinal);

    private static ImmutableDictionary<string, UpstreamResilienceCapability> ResilienceProfiles(IEnumerable<UpstreamResilienceCapability> values)
    {
        var profiles = ImmutableDictionary.CreateBuilder<string, UpstreamResilienceCapability>(StringComparer.Ordinal);
        foreach (var profile in values)
        {
            var hasRetry = profile?.Strategies.HasFlag(UpstreamResilienceStrategies.SelectedResponseRetry) == true;
            if (profile is null || !GatewayIdentifier.IsCanonical(profile.Name) || profile.Version <= 0 ||
                profile.Strategies == UpstreamResilienceStrategies.None ||
                (profile.Strategies & ~(UpstreamResilienceStrategies.SelectedResponseRetry | UpstreamResilienceStrategies.CircuitBreaker |
                    UpstreamResilienceStrategies.OutboundConcurrencyLimiter | UpstreamResilienceStrategies.PerAttemptTimeout)) != 0 ||
                profile.RetryStatusCodes.IsDefault || profile.RetryStatusCodes.Length > 32 ||
                profile.RetryStatusCodes.Any(static status => status is < 100 or > 599) ||
                profile.RetryStatusCodes.Distinct().Count() != profile.RetryStatusCodes.Length ||
                !profile.RetryStatusCodes.SequenceEqual(profile.RetryStatusCodes.Order()) ||
                profile.RetryStatusCodes.Any(static status => !IsRetryStatus(status)) ||
                (hasRetry && (profile.RetryStatusCodes.IsEmpty || profile.MaximumRetryAttempts is < 1 or > 5)) ||
                (!hasRetry && (!profile.RetryStatusCodes.IsEmpty || profile.MaximumRetryAttempts != 0)) ||
                !profiles.TryAdd(profile.Name, profile))
                throw new ArgumentException("Upstream resilience capabilities must be canonical, positive-versioned, nonempty, and unique.", nameof(values));
        }
        return profiles.ToImmutable();
    }

    private static bool IsRetryStatus(int status) => status is 408 or 429 || status is >= 500 and <= 599;

    private static ImmutableDictionary<string, OutputCacheCapability> CacheProfiles(
        IEnumerable<OutputCacheCapability> values,
        ImmutableArray<string> protectedHeaders)
    {
        var profiles = ImmutableDictionary.CreateBuilder<string, OutputCacheCapability>(StringComparer.Ordinal);
        foreach (var profile in values)
        {
            if (profile is null || !GatewayIdentifier.IsCanonical(profile.Name) || profile.Version <= 0 ||
                !profile.RetainsDefaultSafetyPolicy || !GatewayIdentifier.IsCanonical(profile.StoreId) ||
                profile.StoreScope != OutputCacheStoreScope.ProcessLocal ||
                profile.Expiration < TimeSpan.FromSeconds(1) || profile.Expiration > TimeSpan.FromDays(1) ||
                profile.MaximumBodyBytes is < 1_024 or > 67_108_864 ||
                profile.StoreCapacityBytes < profile.MaximumBodyBytes || profile.StoreCapacityBytes > 1_073_741_824 ||
                !ValidDimensions(profile.QueryKeys, header: false, protectedHeaders) ||
                !ValidDimensions(profile.HeaderNames, header: true, protectedHeaders) ||
                !profiles.TryAdd(profile.Name, profile))
                throw new ArgumentException("Output Cache capabilities must be bounded, conservative, process-local, and unique.", nameof(values));
        }
        return profiles.ToImmutable();
    }

    private static bool ValidDimensions(ImmutableArray<string> values, bool header, ImmutableArray<string> protectedHeaders)
    {
        if (values.IsDefault || values.Length > 16 || values.Any(static value => string.IsNullOrWhiteSpace(value) || value.Length > 128)) return false;
        if (!values.SequenceEqual(values.Order(StringComparer.Ordinal)) || values.Distinct(StringComparer.OrdinalIgnoreCase).Count() != values.Length) return false;
        return values.All(value => IsHttpToken(value) && (!header || !protectedHeaders.Contains(value, StringComparer.OrdinalIgnoreCase)));
    }

    private static ImmutableArray<string> ProtectedHeaders(IEnumerable<string> values)
    {
        const int maximumCustomHeaders = 32;
        var custom = values.ToArray();
        if (custom.Length > maximumCustomHeaders)
            throw new ArgumentException("Protected credential header count exceeds its bound.", nameof(values));

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Authorization",
            "Proxy-Authorization",
            "Cookie"
        };
        foreach (var value in custom)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > 256 || !IsHttpToken(value) || IsProhibitedCustomProtectedHeader(value) || !names.Add(value))
                throw new ArgumentException("Protected credential headers must be bounded, valid, unique end-to-end HTTP field names.", nameof(values));
        }

        return names.Select(static name => name.ToLowerInvariant()).Order(StringComparer.Ordinal).ToImmutableArray();
    }

    private static bool IsProhibitedCustomProtectedHeader(string value) => value.Equals("Connection", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("Content-Length", StringComparison.OrdinalIgnoreCase) || value.Equals("Host", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("Keep-Alive", StringComparison.OrdinalIgnoreCase) || value.Equals("Proxy-Connection", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("TE", StringComparison.OrdinalIgnoreCase) || value.Equals("Trailer", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase) || value.Equals("Upgrade", StringComparison.OrdinalIgnoreCase);

    private static bool IsHttpToken(string value)
    {
        if (value.Length == 0) return false;
        foreach (var c in value)
            if (!(char.IsAsciiLetterOrDigit(c) || c is '!' or '#' or '$' or '%' or '&' or '\'' or '*' or '+' or '-' or '.' or '^' or '_' or '`' or '|' or '~'))
                return false;
        return true;
    }

    private static void ValidateNames(IEnumerable<string>? values, string name)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in Required(values, name))
        {
            if (string.IsNullOrWhiteSpace(value) || !IsBoundedUtf8(value, MaximumCapabilityNameBytes) || !names.Add(value))
                throw new ArgumentException($"Capability names must be nonblank, unique using ordinal equality, and bounded to {MaximumCapabilityNameBytes} UTF-8 bytes.", name);
        }
    }

    private static void ValidateInspectorNames(IEnumerable<string>? values, string name)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in Required(values, name))
        {
            if (!GatewayIdentifier.IsCanonical(value) || !names.Add(value))
                throw new ArgumentException("Inspector names must be canonical and unique using ordinal equality.", name);
        }
    }

    private static ImmutableArray<string> ValidateParameterNames(ImmutableArray<string> values, string name)
    {
        if (values.IsDefault || values.Length > MaximumProviderParameters ||
            values.Any(static value => string.IsNullOrWhiteSpace(value) || !IsBoundedUtf8(value, MaximumCapabilityNameBytes)) ||
            values.Distinct(StringComparer.Ordinal).Count() != values.Length)
            throw new ArgumentException("Provider parameter names must be initialized, nonblank, and unique.", name);
        return values;
    }

    private static IEnumerable<T> Required<T>(IEnumerable<T>? values, string name) => values ?? throw new ArgumentException("Capability collection cannot be null.", name);

    private static void RequireMaximum<T>(IEnumerable<T> values, int maximum, string name)
    {
        if (values.Count() > maximum)
            throw new ArgumentException($"Capability collection exceeds its maximum of {maximum} entries.", name);
    }

    private static bool IsBoundedUtf8(string value, int maximum) =>
        value.Length <= maximum && Encoding.UTF8.GetByteCount(value) <= maximum;

    private static bool IsHostPattern(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Any(static c => char.IsControl(c) || char.IsWhiteSpace(c))) return false;
        if (value == "*") return true;
        var host = value.StartsWith("*.", StringComparison.Ordinal) ? value[2..] : value;
        return !host.Contains('*') && Uri.CheckHostName(host.TrimEnd('.')) is UriHostNameType.Dns or UriHostNameType.IPv4 or UriHostNameType.IPv6;
    }
}
