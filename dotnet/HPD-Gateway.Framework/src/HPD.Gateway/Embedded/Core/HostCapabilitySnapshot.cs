using System.Collections.Immutable;
using System.Text;

namespace HPD.Gateway;

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

public enum DiscoveryRuntimeKind : byte
{
    Microsoft = 0,
    Governed = 1
}

public enum DiscoveryProviderKind : byte
{
    Configuration = 0,
    Dns = 1,
    DnsSrv = 2
}

public sealed record DiscoveryProfileCapability(
    DiscoveryProfileId Id,
    ushort ContractVersion,
    DiscoveryRuntimeKind RuntimeKind,
    ImmutableArray<DiscoveryProviderKind> Providers,
    ImmutableArray<ServiceDiscoveryScheme> Schemes,
    ImmutableArray<DiscoveryStaleBehavior> StaleBehaviors,
    int MaximumEndpoints,
    bool SupportsNamedEndpoints,
    bool SupportsDynamicRefresh,
    bool SupportsHttpAuthorityProjection,
    bool RequiresExplicitTlsServerName,
    ContentHash BehaviorIdentity);

public sealed record HostCapabilityRegistration
{
    public IEnumerable<ListenerCapability> Listeners { get; init; } = [];
    public IEnumerable<DiscoveryProfileCapability> DiscoveryProfiles { get; init; } = [];
    public IEnumerable<ProviderId> SecretProviders { get; init; } = [];
    public GatewayDeclarationFamilies InstalledFamilies { get; init; }
    public IEnumerable<string> AuthorizationPolicies { get; init; } = [];
    public IEnumerable<string> CorsPolicies { get; init; } = [];
    public IEnumerable<TrafficAdmissionCapability> TrafficAdmissionProfiles { get; init; } = [];
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
    private const int MaximumDiscoveryProfiles = 32;
    private const int MaximumDiscoveryProvidersPerProfile = 64;
    private const int MaximumNamedCapabilities = 256;
    private const int MaximumProfiles = 128;
    private const int MaximumListenerHostnames = 64;
    private const int MaximumCustomProtectedHeaders = 32;
    private const int MaximumCapabilityNameBytes = 128;
    private const int MaximumHostnameBytes = 256;

    private HostCapabilitySnapshot(
        ImmutableDictionary<ListenerId, ListenerCapability> listeners,
        ImmutableDictionary<DiscoveryProfileId, DiscoveryProfileCapability> discoveryProfiles,
        ImmutableHashSet<ProviderId> secretProviders,
        HostCapabilityRegistration registration)
    {
        Listeners = listeners;
        DiscoveryProfiles = discoveryProfiles;
        SecretProviders = secretProviders;
        InstalledFamilies = registration.InstalledFamilies;
        AuthorizationPolicies = Names(registration.AuthorizationPolicies);
        CorsPolicies = Names(registration.CorsPolicies);
        TrafficAdmissionProfiles = AdmissionProfiles(registration.TrafficAdmissionProfiles);
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
    public ImmutableDictionary<DiscoveryProfileId, DiscoveryProfileCapability> DiscoveryProfiles { get; }
    public ImmutableHashSet<ProviderId> SecretProviders { get; }
    public GatewayDeclarationFamilies InstalledFamilies { get; }
    public ImmutableHashSet<string> AuthorizationPolicies { get; }
    public ImmutableHashSet<string> CorsPolicies { get; }
    public ImmutableDictionary<string, TrafficAdmissionCapability> TrafficAdmissionProfiles { get; }
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
            Listeners = MaterializeBounded(registration.Listeners, MaximumListeners, nameof(registration.Listeners)),
            DiscoveryProfiles = MaterializeBounded(registration.DiscoveryProfiles, MaximumDiscoveryProfiles, nameof(registration.DiscoveryProfiles)),
            SecretProviders = MaterializeBounded(registration.SecretProviders, MaximumProviders, nameof(registration.SecretProviders)),
            AuthorizationPolicies = MaterializeBounded(registration.AuthorizationPolicies, MaximumNamedCapabilities, nameof(registration.AuthorizationPolicies)),
            CorsPolicies = MaterializeBounded(registration.CorsPolicies, MaximumNamedCapabilities, nameof(registration.CorsPolicies)),
            TrafficAdmissionProfiles = MaterializeBounded(registration.TrafficAdmissionProfiles, MaximumProfiles, nameof(registration.TrafficAdmissionProfiles)),
            RequestTimeoutPolicies = MaterializeBounded(registration.RequestTimeoutPolicies, MaximumNamedCapabilities, nameof(registration.RequestTimeoutPolicies)),
            OutputCacheProfiles = MaterializeBounded(registration.OutputCacheProfiles, MaximumProfiles, nameof(registration.OutputCacheProfiles)),
            SessionAffinityPolicies = MaterializeBounded(registration.SessionAffinityPolicies, MaximumNamedCapabilities, nameof(registration.SessionAffinityPolicies)),
            SessionAffinityFailurePolicies = MaterializeBounded(registration.SessionAffinityFailurePolicies, MaximumNamedCapabilities, nameof(registration.SessionAffinityFailurePolicies)),
            PassiveHealthPolicies = MaterializeBounded(registration.PassiveHealthPolicies, MaximumNamedCapabilities, nameof(registration.PassiveHealthPolicies)),
            ActiveHealthPolicies = MaterializeBounded(registration.ActiveHealthPolicies, MaximumNamedCapabilities, nameof(registration.ActiveHealthPolicies)),
            RequestInspectors = MaterializeBounded(registration.RequestInspectors, MaximumNamedCapabilities, nameof(registration.RequestInspectors)),
            UpstreamResilienceProfiles = MaterializeBounded(registration.UpstreamResilienceProfiles, MaximumProfiles, nameof(registration.UpstreamResilienceProfiles)),
            ProtectedCredentialHeaders = MaterializeBounded(registration.ProtectedCredentialHeaders, MaximumCustomProtectedHeaders, nameof(registration.ProtectedCredentialHeaders))
        };
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

        var discoveries = ImmutableDictionary.CreateBuilder<DiscoveryProfileId, DiscoveryProfileCapability>();
        foreach (var profile in Required(registration.DiscoveryProfiles, nameof(registration.DiscoveryProfiles)))
        {
            if (!GatewayIdentifier.IsCanonical(profile.Id.Value) || profile.ContractVersion == 0 ||
                !Enum.IsDefined(profile.RuntimeKind) ||
                !ValidEnumSet(profile.Providers, MaximumDiscoveryProvidersPerProfile, allowEmpty: false) ||
                !ValidEnumSet(profile.Schemes, 2, allowEmpty: false) ||
                !ValidEnumSet(profile.StaleBehaviors, 3, allowEmpty: false) ||
                profile.MaximumEndpoints is < 1 or > 256 ||
                profile.BehaviorIdentity.Algorithm != "sha-256" ||
                profile.BehaviorIdentity.Value is not { Length: 64 } hash ||
                !hash.All(static character => character is >= '0' and <= '9' or >= 'a' and <= 'f') ||
                profile.Schemes.Contains(ServiceDiscoveryScheme.Https) != profile.RequiresExplicitTlsServerName)
                throw new ArgumentException("Discovery profile capability is invalid or unbounded.", nameof(registration));
            if (!discoveries.TryAdd(profile.Id, profile))
                throw new ArgumentException("Discovery profile identities must be unique.", nameof(registration));
        }

        var secrets = ImmutableHashSet.CreateBuilder<ProviderId>();
        foreach (var provider in Required(registration.SecretProviders, nameof(registration.SecretProviders)))
        {
            if (!GatewayIdentifier.IsCanonical(provider.Value) || !secrets.Add(provider))
                throw new ArgumentException("Secret provider identities must be canonical and unique.", nameof(registration));
        }

        ValidateNames(registration.AuthorizationPolicies, nameof(registration.AuthorizationPolicies));
        ValidateNames(registration.CorsPolicies, nameof(registration.CorsPolicies));
        _ = AdmissionProfiles(registration.TrafficAdmissionProfiles);
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

    private static ImmutableDictionary<string, TrafficAdmissionCapability> AdmissionProfiles(IEnumerable<TrafficAdmissionCapability> values)
    {
        var profiles = ImmutableDictionary.CreateBuilder<string, TrafficAdmissionCapability>(StringComparer.Ordinal);
        foreach (var profile in values)
        {
            if (profile is null || !GatewayIdentifier.IsCanonical(profile.Name) || profile.ContractVersion != 1 ||
                !Enum.IsDefined(profile.Scope) || !Enum.IsDefined(profile.Kind) || !Enum.IsDefined(profile.Partition) ||
                !Enum.IsDefined(profile.FailureDisposition) || string.IsNullOrWhiteSpace(profile.AuthorityId) ||
                profile.AuthorityId.Length > 256 || profile.BehaviorIdentity.Algorithm != "sha-256" ||
                profile.BehaviorIdentity.Value is not { Length: 64 } hash ||
                !hash.All(static c => c is >= '0' and <= '9' or >= 'a' and <= 'f') ||
                !IsValidAdmissionScope(profile) ||
                (RequiresAdmissionProjector(profile.Partition) &&
                 (!GatewayIdentifier.IsCanonical(profile.PartitionProjectorId) ||
                  profile.PartitionProjectorIdentity is not { Algorithm: "sha-256", Value.Length: 64 } projectorHash ||
                  !projectorHash.Value.All(static c => c is >= '0' and <= '9' or >= 'a' and <= 'f'))) ||
                (!RequiresAdmissionProjector(profile.Partition) &&
                 (profile.PartitionProjectorId is not null || profile.PartitionProjectorIdentity is not null)) ||
                !IsValidAdmissionShape(profile) ||
                (profile.AcquisitionOrdinal.HasValue != (profile.Kind == TrafficAdmissionKind.Concurrency)) ||
                !profiles.TryAdd(profile.Name, profile))
                throw new ArgumentException("Traffic-admission capabilities are invalid, unsupported, or duplicated.", nameof(values));
        }
        var ordinals = profiles.Values.Where(static p => p.AcquisitionOrdinal.HasValue).Select(static p => p.AcquisitionOrdinal!.Value).Order().ToArray();
        if (!ordinals.SequenceEqual(Enumerable.Range(0, ordinals.Length)))
            throw new ArgumentException("Concurrency acquisition ordinals must form one closed sequence.", nameof(values));
        foreach (TrafficAdmissionCapability profile in profiles.Values.Where(static value => value.Scope == TrafficAdmissionScope.Deployment))
        {
            if (profile.FailureDisposition == TrafficAdmissionFailureDisposition.LocalFallback &&
                (profile.LocalFallbackProfile is not { } fallbackProfile || !GatewayIdentifier.IsCanonical(fallbackProfile) ||
                 profile.LocalFallbackIdentity is not { } fallbackIdentity || !ValidSha256(fallbackIdentity) ||
                 !profiles.TryGetValue(fallbackProfile, out TrafficAdmissionCapability? fallback) ||
                 fallback.Scope != TrafficAdmissionScope.ProcessLocal || fallback.Kind != TrafficAdmissionKind.RequestRate ||
                 fallback.RateAlgorithm != profile.RateAlgorithm || fallback.BehaviorIdentity != fallbackIdentity))
                throw new ArgumentException("Deployment admission fallback correlation is invalid.", nameof(values));
        }
        return profiles.ToImmutable();
    }

    private static bool IsValidAdmissionScope(TrafficAdmissionCapability profile)
    {
        if (profile.Scope == TrafficAdmissionScope.ProcessLocal)
            return profile.FailureDisposition == TrafficAdmissionFailureDisposition.Reject &&
                profile.ProviderId is null && profile.ProviderBehaviorIdentity is null &&
                profile.OperationTimeout is null && profile.MaximumConcurrentInvocations is null &&
                profile.LocalFallbackProfile is null && profile.LocalFallbackIdentity is null;
        return profile.Scope == TrafficAdmissionScope.Deployment && profile.Kind == TrafficAdmissionKind.RequestRate &&
            GatewayIdentifier.IsCanonical(profile.ProviderId) &&
            profile.ProviderBehaviorIdentity is { } providerIdentity && ValidSha256(providerIdentity) &&
            profile.OperationTimeout is { } timeout && timeout >= TimeSpan.FromMilliseconds(1) && timeout <= TimeSpan.FromSeconds(30) &&
            timeout.Ticks % TimeSpan.TicksPerMillisecond == 0 &&
            profile.MaximumConcurrentInvocations is >= 1 and <= 4_096 &&
            (profile.FailureDisposition == TrafficAdmissionFailureDisposition.LocalFallback ||
             profile.LocalFallbackProfile is null && profile.LocalFallbackIdentity is null);
    }

    private static bool ValidSha256(ContentHash value) => value.Algorithm == "sha-256" &&
        value.Value is { Length: 64 } hash && hash.All(static c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static bool RequiresAdmissionProjector(TrafficAdmissionPartitionKind partition) => partition is
        TrafficAdmissionPartitionKind.AuthenticatedSubject or TrafficAdmissionPartitionKind.Tenant or
        TrafficAdmissionPartitionKind.Consumer or TrafficAdmissionPartitionKind.Custom;

    private static bool IsValidAdmissionShape(TrafficAdmissionCapability profile)
    {
        if (profile.Limits is not { } limits ||
            limits.MinimumLimit is < 1 or > 100_000_000 ||
            limits.MaximumLimit < limits.MinimumLimit || limits.MaximumLimit > 100_000_000)
            return false;

        if (profile.Kind == TrafficAdmissionKind.Concurrency)
        {
            return profile.RateAlgorithm is null && limits.MinimumPeriod is null && limits.MaximumPeriod is null &&
                limits.MinimumSegments == 0 && limits.MaximumSegments == 0 &&
                limits.MinimumQueue is >= 0 and <= 100_000 &&
                limits.MaximumQueue >= limits.MinimumQueue && limits.MaximumQueue <= 100_000;
        }

        if (profile.Kind != TrafficAdmissionKind.RequestRate || profile.RateAlgorithm is not { } algorithm ||
            !Enum.IsDefined(algorithm) || limits.MinimumPeriod is not { } minimumPeriod ||
            limits.MaximumPeriod is not { } maximumPeriod || minimumPeriod > maximumPeriod ||
            maximumPeriod > TimeSpan.FromDays(1) || minimumPeriod.Ticks % TimeSpan.TicksPerMillisecond != 0 ||
            maximumPeriod.Ticks % TimeSpan.TicksPerMillisecond != 0 ||
            limits.MinimumQueue != 0 || limits.MaximumQueue != 0)
            return false;

        var minimumAllowed = algorithm == TrafficAdmissionRateAlgorithm.TokenBucket
            ? TimeSpan.FromMilliseconds(100)
            : TimeSpan.FromSeconds(1);
        if (minimumPeriod < minimumAllowed)
            return false;

        return algorithm == TrafficAdmissionRateAlgorithm.SlidingWindow
            ? limits.MinimumSegments is >= 2 and <= 64 &&
              limits.MaximumSegments >= limits.MinimumSegments && limits.MaximumSegments <= 64
            : limits.MinimumSegments == 0 && limits.MaximumSegments == 0;
    }

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
        var custom = values.ToArray();
        if (custom.Length > MaximumCustomProtectedHeaders)
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

    private static bool ValidEnumSet<T>(ImmutableArray<T> values, int maximum, bool allowEmpty) where T : struct, Enum =>
        !values.IsDefault && values.Length <= maximum && (allowEmpty || !values.IsEmpty) &&
        values.All(Enum.IsDefined) && values.Distinct().Count() == values.Length;

    private static IEnumerable<T> Required<T>(IEnumerable<T>? values, string name) => values ?? throw new ArgumentException("Capability collection cannot be null.", name);

    private static T[] MaterializeBounded<T>(IEnumerable<T>? values, int maximum, string name)
    {
        ArgumentNullException.ThrowIfNull(values, name);
        var result = new List<T>(Math.Min(maximum, 16));
        using IEnumerator<T> enumerator = values.GetEnumerator();
        while (enumerator.MoveNext())
        {
            if (result.Count == maximum)
                throw new ArgumentException($"Capability collection exceeds its maximum of {maximum} entries.", name);
            result.Add(enumerator.Current);
        }
        return result.ToArray();
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
