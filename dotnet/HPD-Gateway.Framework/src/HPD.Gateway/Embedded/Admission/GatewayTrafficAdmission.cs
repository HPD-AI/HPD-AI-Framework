using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Model;

namespace HPD.Gateway;

public class GatewayLocalAdmissionOptions
{
    public TrafficAdmissionPartitionKind Partition { get; set; } = TrafficAdmissionPartitionKind.Global;
    public long MinimumLimit { get; set; } = 1;
    public long MaximumLimit { get; set; } = 100_000_000;
    public TimeSpan MinimumPeriod { get; set; } = TimeSpan.FromSeconds(1);
    public TimeSpan MaximumPeriod { get; set; } = TimeSpan.FromDays(1);
    public int MinimumSegments { get; set; } = 2;
    public int MaximumSegments { get; set; } = 64;
    public int MinimumQueue { get; set; }
    public int MaximumQueue { get; set; } = 100_000;
    public string? PartitionProjector { get; set; }
}

public sealed class GatewayTrafficAdmissionRegistryBuilder
{
    private const int MaximumProfiles = 128;
    private readonly List<GatewayAdmissionProfileRegistration> _profiles = [];
    private readonly Dictionary<string, GatewayAdmissionProjectorRegistration> _projectors = new(StringComparer.Ordinal);
    private readonly Dictionary<string, GatewaySharedAdmissionProviderRegistration> _providers = new(StringComparer.Ordinal);
    private TimeProvider _timeProvider = TimeProvider.System;

    public GatewayTrafficAdmissionRegistryBuilder UseTimeProvider(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        _timeProvider = timeProvider;
        return this;
    }

    public GatewayTrafficAdmissionRegistryBuilder AddPartitionProjector(
        string name,
        ContentHash behaviorIdentity,
        IGatewayAdmissionPartitionProjector projector)
    {
        ArgumentNullException.ThrowIfNull(projector);
        if (_projectors.Count >= MaximumProfiles)
            throw new InvalidOperationException("Traffic-admission projector capacity was exceeded.");
        if (!GatewayIdentifier.IsCanonical(name) || _projectors.ContainsKey(name))
            throw new ArgumentException("Traffic-admission projector names must be canonical and unique.", nameof(name));
        if (behaviorIdentity.Algorithm != "sha-256" || behaviorIdentity.Value.Length != 64 ||
            behaviorIdentity.Value.Any(static character => character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
            throw new ArgumentException("Projector behavior identity must be canonical SHA-256.", nameof(behaviorIdentity));
        _projectors.Add(name, new GatewayAdmissionProjectorRegistration(name, behaviorIdentity, projector));
        return this;
    }

    public GatewayTrafficAdmissionRegistryBuilder AddLocalFixedWindow(string name, Action<GatewayLocalAdmissionOptions>? configure = null) => Add(name, TrafficAdmissionKind.RequestRate, TrafficAdmissionRateAlgorithm.FixedWindow, configure);
    public GatewayTrafficAdmissionRegistryBuilder AddLocalSlidingWindow(string name, Action<GatewayLocalAdmissionOptions>? configure = null) => Add(name, TrafficAdmissionKind.RequestRate, TrafficAdmissionRateAlgorithm.SlidingWindow, configure);
    public GatewayTrafficAdmissionRegistryBuilder AddLocalTokenBucket(string name, Action<GatewayLocalAdmissionOptions>? configure = null) => Add(name, TrafficAdmissionKind.RequestRate, TrafficAdmissionRateAlgorithm.TokenBucket, configure);
    public GatewayTrafficAdmissionRegistryBuilder AddLocalConcurrency(string name, Action<GatewayLocalAdmissionOptions>? configure = null) => Add(name, TrafficAdmissionKind.Concurrency, null, configure);

    public GatewayTrafficAdmissionRegistryBuilder AddSharedProvider(
        string providerId,
        IGatewaySharedAdmissionProvider provider,
        Action<GatewaySharedAdmissionProviderOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(configure);
        if (_providers.Count >= MaximumProfiles)
            throw new InvalidOperationException("Shared-admission provider capacity was exceeded.");
        if (!GatewayIdentifier.IsCanonical(providerId) || _providers.ContainsKey(providerId))
            throw new ArgumentException("Shared-admission provider identities must be canonical and unique.", nameof(providerId));
        var options = new GatewaySharedAdmissionProviderOptions
        {
            AuthorityId = string.Empty,
            BehaviorIdentity = new ContentHash("sha-256", string.Empty),
        };
        configure(options);
        ValidateProvider(options);
        _providers.Add(providerId, new GatewaySharedAdmissionProviderRegistration(
            providerId, options.AuthorityId, options.BehaviorIdentity, options.OperationTimeout,
            options.MaximumConcurrentInvocations, provider));
        return this;
    }

    public GatewayTrafficAdmissionRegistryBuilder AddSharedFixedWindow(
        string name, string providerId, Action<GatewaySharedAdmissionProfileOptions>? configure = null) =>
        AddShared(name, providerId, TrafficAdmissionRateAlgorithm.FixedWindow, configure);

    public GatewayTrafficAdmissionRegistryBuilder AddSharedSlidingWindow(
        string name, string providerId, Action<GatewaySharedAdmissionProfileOptions>? configure = null) =>
        AddShared(name, providerId, TrafficAdmissionRateAlgorithm.SlidingWindow, configure);

    public GatewayTrafficAdmissionRegistryBuilder AddSharedTokenBucket(
        string name, string providerId, Action<GatewaySharedAdmissionProfileOptions>? configure = null) =>
        AddShared(name, providerId, TrafficAdmissionRateAlgorithm.TokenBucket, configure);

    private GatewayTrafficAdmissionRegistryBuilder Add(string name, TrafficAdmissionKind kind, TrafficAdmissionRateAlgorithm? algorithm, Action<GatewayLocalAdmissionOptions>? configure)
    {
        if (_profiles.Count >= MaximumProfiles) throw new InvalidOperationException("Traffic-admission profile capacity was exceeded.");
        if (!GatewayIdentifier.IsCanonical(name) || _profiles.Any(value => StringComparer.Ordinal.Equals(value.Name, name)))
            throw new ArgumentException("Traffic-admission profile names must be canonical and unique.", nameof(name));
        var options = new GatewayLocalAdmissionOptions();
        configure?.Invoke(options);
        Validate(options, kind, algorithm);
        _profiles.Add(new GatewayAdmissionProfileRegistration(name, TrafficAdmissionScope.ProcessLocal, kind, algorithm,
            Snapshot(options), TrafficAdmissionFailureDisposition.Reject, null, null));
        return this;
    }

    private GatewayTrafficAdmissionRegistryBuilder AddShared(
        string name,
        string providerId,
        TrafficAdmissionRateAlgorithm algorithm,
        Action<GatewaySharedAdmissionProfileOptions>? configure)
    {
        if (_profiles.Count >= MaximumProfiles) throw new InvalidOperationException("Traffic-admission profile capacity was exceeded.");
        if (!GatewayIdentifier.IsCanonical(name) || _profiles.Any(value => StringComparer.Ordinal.Equals(value.Name, name)))
            throw new ArgumentException("Traffic-admission profile names must be canonical and unique.", nameof(name));
        if (!GatewayIdentifier.IsCanonical(providerId))
            throw new ArgumentException("Shared-admission provider identity must be canonical.", nameof(providerId));
        var options = new GatewaySharedAdmissionProfileOptions();
        configure?.Invoke(options);
        Validate(options, TrafficAdmissionKind.RequestRate, algorithm);
        if (!Enum.IsDefined(options.FailureDisposition) ||
            (options.FailureDisposition == TrafficAdmissionFailureDisposition.LocalFallback &&
             !GatewayIdentifier.IsCanonical(options.LocalFallbackProfile!)) ||
            (options.FailureDisposition != TrafficAdmissionFailureDisposition.LocalFallback && options.LocalFallbackProfile is not null))
            throw new ArgumentException("Shared-admission failure disposition or fallback identity is invalid.", nameof(configure));
        _profiles.Add(new GatewayAdmissionProfileRegistration(name, TrafficAdmissionScope.Deployment,
            TrafficAdmissionKind.RequestRate, algorithm, Snapshot(options), options.FailureDisposition,
            providerId, options.LocalFallbackProfile));
        return this;
    }

    internal GatewayTrafficAdmissionRegistry Build()
    {
        foreach (GatewayAdmissionProfileRegistration profile in _profiles)
        {
            if (RequiresProjector(profile.Options.Partition) &&
                (profile.Options.PartitionProjector is null || !_projectors.ContainsKey(profile.Options.PartitionProjector)))
                throw new InvalidOperationException($"Traffic-admission profile '{profile.Name}' requires an installed partition projector.");
            if (profile.Scope == TrafficAdmissionScope.Deployment &&
                (profile.ProviderId is null || !_providers.ContainsKey(profile.ProviderId)))
                throw new InvalidOperationException($"Traffic-admission profile '{profile.Name}' requires an installed shared provider.");
            if (profile.LocalFallbackProfile is { } fallback && !_profiles.Any(value =>
                    value.Name == fallback && value.Scope == TrafficAdmissionScope.ProcessLocal &&
                    value.Kind == TrafficAdmissionKind.RequestRate && value.Algorithm == profile.Algorithm &&
                    value.Options.Partition == profile.Options.Partition &&
                    StringComparer.Ordinal.Equals(value.Options.PartitionProjector, profile.Options.PartitionProjector) &&
                    Contains(value.Options, profile.Options)))
                throw new InvalidOperationException($"Traffic-admission profile '{profile.Name}' has an invalid local fallback.");
        }
        if (_providers.Keys.Any(provider => !_profiles.Any(profile => profile.ProviderId == provider)))
            throw new InvalidOperationException("Every shared-admission provider must be selected by at least one profile.");
        var concurrencyNames = _profiles.Where(static value => value.Kind == TrafficAdmissionKind.Concurrency)
            .Select(static value => value.Name).Order(StringComparer.Ordinal).ToArray();
        var capabilities = ImmutableArray.CreateBuilder<TrafficAdmissionCapability>(_profiles.Count);
        var runtimes = ImmutableDictionary.CreateBuilder<string, GatewayAdmissionProfileRuntime>(StringComparer.Ordinal);
        var providerRuntimes = _providers.ToDictionary(
            static pair => pair.Key,
            pair => new GatewaySharedAdmissionProviderRuntime(pair.Value, _timeProvider),
            StringComparer.Ordinal);
        foreach (var profile in _profiles
            .OrderBy(static value => value.Scope)
            .ThenBy(static value => value.Name, StringComparer.Ordinal))
        {
            GatewayAdmissionProjectorRegistration? projector = null;
            if (RequiresProjector(profile.Options.Partition) &&
                (profile.Options.PartitionProjector is null || !_projectors.TryGetValue(profile.Options.PartitionProjector, out projector)))
                throw new InvalidOperationException($"Traffic-admission profile '{profile.Name}' requires an installed partition projector.");
            var ordinal = profile.Kind == TrafficAdmissionKind.Concurrency ? Array.IndexOf(concurrencyNames, profile.Name) : (int?)null;
            GatewaySharedAdmissionProviderRuntime? sharedProvider = null;
            GatewayAdmissionProfileRuntime? localFallback = null;
            if (profile.Scope == TrafficAdmissionScope.Deployment)
            {
                if (profile.ProviderId is null || !_providers.TryGetValue(profile.ProviderId, out GatewaySharedAdmissionProviderRegistration? provider))
                    throw new InvalidOperationException($"Traffic-admission profile '{profile.Name}' requires an installed shared provider.");
                sharedProvider = providerRuntimes[profile.ProviderId];
                if (profile.LocalFallbackProfile is not null)
                {
                    if (!runtimes.TryGetValue(profile.LocalFallbackProfile, out localFallback) ||
                        localFallback.Capability.Scope != TrafficAdmissionScope.ProcessLocal ||
                        localFallback.Capability.RateAlgorithm != profile.Algorithm ||
                        localFallback.Capability.Partition != profile.Options.Partition ||
                        !StringComparer.Ordinal.Equals(localFallback.Capability.PartitionProjectorId, profile.Options.PartitionProjector))
                        throw new InvalidOperationException($"Traffic-admission profile '{profile.Name}' has an invalid local fallback.");
                }
            }
            var limits = new TrafficAdmissionLimits(profile.Options.MinimumLimit, profile.Options.MaximumLimit,
                profile.Kind == TrafficAdmissionKind.RequestRate ? profile.Options.MinimumPeriod : null,
                profile.Kind == TrafficAdmissionKind.RequestRate ? profile.Options.MaximumPeriod : null,
                profile.Algorithm == TrafficAdmissionRateAlgorithm.SlidingWindow ? profile.Options.MinimumSegments : 0,
                profile.Algorithm == TrafficAdmissionRateAlgorithm.SlidingWindow ? profile.Options.MaximumSegments : 0,
                profile.Kind == TrafficAdmissionKind.Concurrency ? profile.Options.MinimumQueue : 0,
                profile.Kind == TrafficAdmissionKind.Concurrency ? profile.Options.MaximumQueue : 0);
            var identityText = string.Join('|', profile.Name, profile.Scope, profile.Kind, profile.Algorithm, profile.Options.Partition,
                limits.MinimumLimit, limits.MaximumLimit, limits.MinimumPeriod?.Ticks, limits.MaximumPeriod?.Ticks,
                limits.MinimumSegments, limits.MaximumSegments, limits.MinimumQueue, limits.MaximumQueue,
                projector?.Name, projector?.BehaviorIdentity.Value, ordinal, profile.ProviderId,
                sharedProvider?.AuthorityId, sharedProvider?.BehaviorIdentity.Value,
                sharedProvider?.OperationTimeout.Ticks, sharedProvider?.MaximumConcurrentInvocations,
                profile.FailureDisposition,
                profile.LocalFallbackProfile, localFallback?.Capability.BehaviorIdentity.Value);
            var identity = new ContentHash("sha-256", Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(identityText))));
            var capability = new TrafficAdmissionCapability(profile.Name, 1, profile.Scope, profile.Kind,
                profile.Algorithm, profile.Options.Partition, profile.FailureDisposition, limits,
                sharedProvider?.AuthorityId ?? "hpd.gateway/process-local", identity, ordinal,
                projector?.Name, projector?.BehaviorIdentity, profile.ProviderId,
                sharedProvider?.BehaviorIdentity, sharedProvider?.OperationTimeout,
                sharedProvider?.MaximumConcurrentInvocations, profile.LocalFallbackProfile,
                localFallback?.Capability.BehaviorIdentity);
            capabilities.Add(capability);
            runtimes.Add(profile.Name, new GatewayAdmissionProfileRuntime(capability, projector, _timeProvider,
                sharedProvider, localFallback, profile.ProviderId));
        }
        return new GatewayTrafficAdmissionRegistry(
            capabilities.ToImmutable().OrderBy(static value => value.Name, StringComparer.Ordinal).ToImmutableArray(),
            runtimes.ToImmutable());
    }

    private static bool Contains(GatewayLocalAdmissionOptions fallback, GatewayLocalAdmissionOptions shared) =>
        fallback.MinimumLimit <= shared.MinimumLimit && fallback.MaximumLimit >= shared.MaximumLimit &&
        fallback.MinimumPeriod <= shared.MinimumPeriod && fallback.MaximumPeriod >= shared.MaximumPeriod &&
        fallback.MinimumSegments <= shared.MinimumSegments && fallback.MaximumSegments >= shared.MaximumSegments;

    private static GatewayLocalAdmissionOptions Snapshot(GatewayLocalAdmissionOptions value) => new()
    {
        Partition = value.Partition, MinimumLimit = value.MinimumLimit, MaximumLimit = value.MaximumLimit,
        MinimumPeriod = value.MinimumPeriod, MaximumPeriod = value.MaximumPeriod,
        MinimumSegments = value.MinimumSegments, MaximumSegments = value.MaximumSegments,
        MinimumQueue = value.MinimumQueue, MaximumQueue = value.MaximumQueue, PartitionProjector = value.PartitionProjector
    };

    private static void Validate(
        GatewayLocalAdmissionOptions value,
        TrafficAdmissionKind kind,
        TrafficAdmissionRateAlgorithm? algorithm)
    {
        var commonInvalid = !Enum.IsDefined(value.Partition) || value.MinimumLimit < 1 ||
            value.MaximumLimit < value.MinimumLimit || value.MaximumLimit > 100_000_000 ||
            (RequiresProjector(value.Partition) &&
             (value.PartitionProjector is null || !GatewayIdentifier.IsCanonical(value.PartitionProjector))) ||
            (!RequiresProjector(value.Partition) && value.PartitionProjector is not null);
        var rateInvalid = kind == TrafficAdmissionKind.RequestRate &&
            (algorithm is not { } rateAlgorithm || !Enum.IsDefined(rateAlgorithm) ||
             value.MinimumPeriod < (rateAlgorithm == TrafficAdmissionRateAlgorithm.TokenBucket
                 ? TimeSpan.FromMilliseconds(100)
                 : TimeSpan.FromSeconds(1)) ||
             value.MaximumPeriod < value.MinimumPeriod || value.MaximumPeriod > TimeSpan.FromDays(1) ||
             value.MinimumPeriod.Ticks % TimeSpan.TicksPerMillisecond != 0 ||
             value.MaximumPeriod.Ticks % TimeSpan.TicksPerMillisecond != 0 ||
             (rateAlgorithm == TrafficAdmissionRateAlgorithm.SlidingWindow &&
              (value.MinimumSegments < 2 || value.MaximumSegments < value.MinimumSegments || value.MaximumSegments > 64)));
        var concurrencyInvalid = kind == TrafficAdmissionKind.Concurrency &&
            (algorithm is not null || value.MinimumQueue < 0 || value.MaximumQueue < value.MinimumQueue || value.MaximumQueue > 100_000);
        if (commonInvalid || rateInvalid || concurrencyInvalid ||
            kind is not (TrafficAdmissionKind.RequestRate or TrafficAdmissionKind.Concurrency))
            throw new ArgumentException("Traffic-admission options are invalid or unbounded.", nameof(value));
    }

    private static bool RequiresProjector(TrafficAdmissionPartitionKind partition) => partition is
        TrafficAdmissionPartitionKind.AuthenticatedSubject or TrafficAdmissionPartitionKind.Tenant or
        TrafficAdmissionPartitionKind.Consumer or TrafficAdmissionPartitionKind.Custom;

    private static void ValidateProvider(GatewaySharedAdmissionProviderOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.AuthorityId) || options.AuthorityId.Length > 256 ||
            options.AuthorityId.Any(char.IsControl) || options.BehaviorIdentity.Algorithm != "sha-256" ||
            options.BehaviorIdentity.Value.Length != 64 ||
            options.BehaviorIdentity.Value.Any(static value => value is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')) ||
            options.OperationTimeout < TimeSpan.FromMilliseconds(1) || options.OperationTimeout > TimeSpan.FromSeconds(30) ||
            options.OperationTimeout.Ticks % TimeSpan.TicksPerMillisecond != 0 ||
            options.MaximumConcurrentInvocations is < 1 or > 4_096)
            throw new ArgumentException("Shared-admission provider options are invalid or unbounded.", nameof(options));
    }
}

internal sealed record GatewayAdmissionProfileRegistration(
    string Name,
    TrafficAdmissionScope Scope,
    TrafficAdmissionKind Kind,
    TrafficAdmissionRateAlgorithm? Algorithm,
    GatewayLocalAdmissionOptions Options,
    TrafficAdmissionFailureDisposition FailureDisposition,
    string? ProviderId,
    string? LocalFallbackProfile);

internal sealed record GatewayAdmissionProjectorRegistration(
    string Name,
    ContentHash BehaviorIdentity,
    IGatewayAdmissionPartitionProjector Projector);

internal sealed class GatewaySharedAdmissionProviderRegistration(
    string providerId,
    string authorityId,
    ContentHash behaviorIdentity,
    TimeSpan operationTimeout,
    int maximumConcurrentInvocations,
    IGatewaySharedAdmissionProvider provider)
{
    internal string ProviderId { get; } = providerId;
    internal string AuthorityId { get; } = authorityId;
    internal ContentHash BehaviorIdentity { get; } = behaviorIdentity;
    internal TimeSpan OperationTimeout { get; } = operationTimeout;
    internal int MaximumConcurrentInvocations { get; } = maximumConcurrentInvocations;
    internal IGatewaySharedAdmissionProvider Provider { get; } = provider;
}

internal sealed class GatewayTrafficAdmissionRegistry(
    ImmutableArray<TrafficAdmissionCapability> capabilities,
    ImmutableDictionary<string, GatewayAdmissionProfileRuntime> runtimes) : IDisposable
{
    private int _disposed;
    internal ImmutableArray<TrafficAdmissionCapability> Capabilities { get; } = capabilities;
    internal bool TryGet(string name, out GatewayAdmissionProfileRuntime runtime) => runtimes.TryGetValue(name, out runtime!);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        foreach (var runtime in runtimes.Values)
            runtime.Dispose();
    }
}

internal sealed class GatewayTrafficAdmissionMetadata
{
    private GatewayTrafficAdmissionMetadata(
        string applicationId,
        ContentHash symbolicPlanIdentity,
        RouteId routeId,
        ContentHash admissionPlanIdentity,
        TrafficAdmissionPlan plan)
    {
        ApplicationId = applicationId;
        SymbolicPlanIdentity = symbolicPlanIdentity;
        RouteId = routeId;
        AdmissionPlanIdentity = admissionPlanIdentity;
        Plan = plan;
    }

    internal string ApplicationId { get; }
    internal ContentHash SymbolicPlanIdentity { get; }
    internal RouteId RouteId { get; }
    internal ContentHash AdmissionPlanIdentity { get; }
    internal TrafficAdmissionPlan Plan { get; }

    internal static GatewayTrafficAdmissionMetadata Create(
        string applicationId,
        ContentHash symbolicPlanIdentity,
        RouteId routeId,
        ContentHash admissionPlanIdentity,
        TrafficAdmissionPlan plan)
    {
        if (!GatewayTrafficAdmissionMetadataCodec.ValidApplicationId(applicationId) ||
            !GatewayTrafficAdmissionMetadataCodec.ValidHash(symbolicPlanIdentity) ||
            !GatewayIdentifier.IsCanonical(routeId.Value) || !GatewayTrafficAdmissionMetadataCodec.ValidHash(admissionPlanIdentity) ||
            GatewayRuntimePlanner.HashTrafficAdmission(plan) != admissionPlanIdentity)
            throw new ArgumentException("Traffic-admission runtime-generation metadata is invalid.");
        return new GatewayTrafficAdmissionMetadata(applicationId, symbolicPlanIdentity, routeId, admissionPlanIdentity, plan);
    }
}

internal static class GatewayTrafficAdmissionMetadataCodec
{
    internal const string Plan = "hpd.gateway.traffic-admission-plan";
    internal const string PlanIdentity = "hpd.gateway.traffic-admission-plan-id";

    internal static string Encode(TrafficAdmissionPlan plan) => Convert.ToBase64String(
        JsonSerializer.SerializeToUtf8Bytes(plan, GatewayJsonSerializerContext.Default.TrafficAdmissionPlan));

    internal static TrafficAdmissionPlan Decode(string encoded)
    {
        if (encoded.Length > 16_384) throw new InvalidOperationException("Encoded traffic-admission plan exceeds its bound.");
        return JsonSerializer.Deserialize(Convert.FromBase64String(encoded), GatewayJsonSerializerContext.Default.TrafficAdmissionPlan)
            ?? throw new InvalidOperationException("Traffic-admission plan metadata is invalid.");
    }

    internal static bool ValidateRoute(RouteConfig route)
    {
        try
        {
            if (route.Metadata is null) return true;
            bool hasPlan = route.Metadata.TryGetValue(Plan, out string? encoded);
            bool hasIdentity = route.Metadata.TryGetValue(PlanIdentity, out string? identity);
            if (hasPlan != hasIdentity) return false;
            if (!hasPlan) return true;
            if (!route.Metadata.TryGetValue(GatewayRuntimePlanner.ApplicationIdMetadata, out string? applicationId) ||
                !route.Metadata.TryGetValue(GatewayRuntimePlanner.SymbolicPlanIdentityMetadata, out string? symbolic) ||
                !ValidApplicationId(applicationId) || !ValidHash(symbolic) || !ValidHash(identity)) return false;
            TrafficAdmissionPlan plan = Decode(encoded!);
            return GatewayRuntimePlanner.HashTrafficAdmission(plan).Value == identity;
        }
        catch { return false; }
    }

    internal static bool ValidApplicationId(string? value) => value is { Length: GatewayRuntimePlan.MaximumApplicationIdLength } &&
        value.All(static character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    internal static bool ValidHash(ContentHash value) => value.Algorithm == "sha-256" && ValidHash(value.Value);
    internal static bool ValidHash(string? value) => value is { Length: 64 } &&
        value.All(static character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
}

internal static class GatewayTrafficAdmissionMiddleware
{
    internal static RateLimiterOptions CreateOptions(GatewayTrafficAdmissionRegistry registry)
    {
        var options = new RateLimiterOptions
        {
            GlobalLimiter = new GatewayTrafficAdmissionLimiter(registry),
            RejectionStatusCode = StatusCodes.Status503ServiceUnavailable
        };
        options.OnRejected = static (context, _) =>
        {
            if (context.Lease.TryGetMetadata(GatewayAdmissionMetadata.Outcome, out var raw) &&
                raw is GatewayAdmissionOutcome.Exhausted)
            {
                context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                if (context.Lease.TryGetMetadata(GatewayAdmissionMetadata.RetryAfterMilliseconds, out var retry) &&
                    retry is long milliseconds && milliseconds > 0)
                {
                    var seconds = Math.Max(1, checked((milliseconds + 999) / 1000));
                    context.HttpContext.Response.Headers.RetryAfter = seconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
                }
            }
            else
            {
                context.HttpContext.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            }
            return ValueTask.CompletedTask;
        };
        return options;
    }
}

internal enum GatewayAdmissionOutcome : byte { Acquired, Exhausted, Infrastructure }

internal static class GatewayAdmissionMetadata
{
    internal const string Outcome = "HPD.Gateway.Admission.Outcome";
    internal const string Remaining = "HPD.Gateway.Admission.Remaining";
    internal const string RetryAfterMilliseconds = "HPD.Gateway.Admission.RetryAfterMilliseconds";
    internal const string ResetAfterMilliseconds = "HPD.Gateway.Admission.ResetAfterMilliseconds";
}

internal sealed class GatewayTrafficAdmissionLimiter(GatewayTrafficAdmissionRegistry registry) : PartitionedRateLimiter<HttpContext>
{
    private static readonly RateLimitLease Noop = new GatewayAdmissionLease(true, null);
    private static readonly RateLimitLease AsyncRequired = new GatewayAdmissionLease(false, "AsynchronousRequired");

    public override RateLimiterStatistics? GetStatistics(HttpContext resource) => null;
    protected override RateLimitLease AttemptAcquireCore(HttpContext resource, int permitCount) =>
        resource.GetEndpoint()?.Metadata.GetMetadata<GatewayTrafficAdmissionMetadata>() is null ? Noop : AsyncRequired;

    protected override async ValueTask<RateLimitLease> AcquireAsyncCore(HttpContext context, int permitCount, CancellationToken cancellationToken)
    {
        var metadata = context.GetEndpoint()?.Metadata.GetMetadata<GatewayTrafficAdmissionMetadata>();
        if (metadata is null) return Noop;
        if (permitCount != 1) return GatewayAdmissionLease.Infrastructure("UnsupportedPermitCount");
        var projected = new List<(TrafficAdmissionEntry Entry, GatewayAdmissionProfileRuntime Runtime, string Key)>();
        foreach (var entry in metadata.Plan.Entries)
        {
            if (!registry.TryGet(entry.ProfileName, out var runtime)) return GatewayAdmissionLease.Infrastructure("ProfileUnavailable");
            var projectedPartition = await runtime.ProjectAsync(context, metadata.RouteId, cancellationToken).ConfigureAwait(false);
            if (!projectedPartition.IsSuccess) return GatewayAdmissionLease.Infrastructure(projectedPartition.Code);
            var key = projectedPartition.Value!;
            projected.Add((entry, runtime, key + "\0" + GatewayRuntimePlanner.HashTrafficAdmission(new TrafficAdmissionPlan { Entries = [entry] }).Value));
        }
        var leases = new List<RateLimitLease>();
        var degradedBypass = false;
        try
        {
            foreach (var item in projected.Where(static value => value.Entry is ConcurrencyAdmissionEntry)
                .OrderBy(static value => value.Runtime.Capability.AcquisitionOrdinal))
            {
                var lease = await item.Runtime.AcquireAsync(item.Entry, item.Key, cancellationToken).ConfigureAwait(false);
                if (!lease.IsAcquired)
                {
                    var rejected = GatewayAdmissionLease.FromRejectedEntry(lease, concurrency: true);
                    lease.Dispose();
                    DisposeAll(leases);
                    return rejected;
                }
                leases.Add(lease);
            }
            foreach (var item in projected.Where(static value => value.Entry is RequestRateAdmissionEntry))
            {
                var lease = await item.Runtime.AcquireAsync(item.Entry, item.Key, cancellationToken).ConfigureAwait(false);
                if (!lease.IsAcquired)
                {
                    var rejected = GatewayAdmissionLease.FromRejectedEntry(lease, concurrency: false);
                    lease.Dispose();
                    DisposeAll(leases);
                    return rejected;
                }
                if (lease.TryGetMetadata("HPD.Gateway.Admission.Degraded", out var degraded) && degraded is "Bypass")
                    degradedBypass = true;
                lease.Dispose();
            }
            return GatewayAdmissionLease.Combined(leases, degradedBypass);
        }
        catch
        {
            DisposeAll(leases);
            throw;
        }
    }

    private static void DisposeAll(List<RateLimitLease> leases)
    {
        foreach (var lease in leases)
            lease.Dispose();
        leases.Clear();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            registry.Dispose();
        base.Dispose(disposing);
    }
}

internal sealed class GatewayAdmissionProfileRuntime : IDisposable
{
    private const int MaximumPartitions = 4096;
    private readonly object _statesGate = new();
    private readonly Dictionary<string, object> _states = new(StringComparer.Ordinal);
    private readonly GatewayAdmissionProjectorRegistration? _projector;
    private readonly TimeProvider _timeProvider;
    private readonly GatewaySharedAdmissionProviderRuntime? _sharedProvider;
    private readonly GatewayAdmissionProfileRuntime? _localFallback;
    private readonly string? _providerId;
    private int _disposed;
    internal GatewayAdmissionProfileRuntime(
        TrafficAdmissionCapability capability,
        GatewayAdmissionProjectorRegistration? projector,
        TimeProvider timeProvider,
        GatewaySharedAdmissionProviderRuntime? sharedProvider = null,
        GatewayAdmissionProfileRuntime? localFallback = null,
        string? providerId = null)
    {
        Capability = capability;
        _projector = projector;
        _timeProvider = timeProvider;
        _sharedProvider = sharedProvider;
        _localFallback = localFallback;
        _providerId = providerId;
    }
    internal TrafficAdmissionCapability Capability { get; }

    internal async ValueTask<GatewayProjectedPartition> ProjectAsync(
        HttpContext context,
        RouteId route,
        CancellationToken cancellationToken)
    {
        string? value;
        switch (Capability.Partition)
        {
            case TrafficAdmissionPartitionKind.Global:
                value = "global";
                break;
            case TrafficAdmissionPartitionKind.Route:
                value = route.Value;
                break;
            case TrafficAdmissionPartitionKind.SourceIp:
                value = context.Connection.RemoteIpAddress?.ToString();
                break;
            case TrafficAdmissionPartitionKind.AuthenticatedSubject:
            case TrafficAdmissionPartitionKind.Tenant:
            case TrafficAdmissionPartitionKind.Consumer:
            case TrafficAdmissionPartitionKind.Custom:
                if (_projector is null ||
                    (Capability.Partition != TrafficAdmissionPartitionKind.Custom && context.User.Identity?.IsAuthenticated != true))
                    return GatewayProjectedPartition.Failed("PartitionUnavailable");
                try
                {
                    GatewayAdmissionPartitionResult? result = await _projector.Projector.ProjectAsync(
                        new GatewayAdmissionPartitionContext(context.User, route), cancellationToken).ConfigureAwait(false);
                    if (result is null || result.Failure is not null || result.Value is not { } projected)
                        return GatewayProjectedPartition.Failed("PartitionProjectionFailed");
                    value = projected;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch
                {
                    return GatewayProjectedPartition.Failed("PartitionProjectionFailed");
                }
                break;
            default:
                return GatewayProjectedPartition.Failed("PartitionUnavailable");
        }

        if (string.IsNullOrEmpty(value) || Encoding.UTF8.GetByteCount(value) > 256 || !value.IsNormalized(NormalizationForm.FormC))
            return GatewayProjectedPartition.Failed("PartitionProjectionInvalid");
        return GatewayProjectedPartition.Success(value);
    }

    internal ValueTask<RateLimitLease> AcquireAsync(TrafficAdmissionEntry entry, string key, CancellationToken cancellationToken)
    {
        if (Capability.Scope == TrafficAdmissionScope.Deployment)
            return AcquireSharedAsync(entry, key, cancellationToken);
        object state;
        lock (_statesGate)
        {
            if (_disposed != 0)
                return ValueTask.FromResult<RateLimitLease>(GatewayAdmissionLease.Infrastructure("Disposed"));
            if (!_states.TryGetValue(key, out state!))
            {
                if (_states.Count >= MaximumPartitions)
                    return ValueTask.FromResult<RateLimitLease>(GatewayAdmissionLease.Infrastructure("PartitionCapacity"));
                state = Create(entry);
                _states.Add(key, state);
            }
        }
        return entry switch
        {
            ConcurrencyAdmissionEntry => ((ConcurrencyLimiter)state).AcquireAsync(1, cancellationToken),
            _ => ValueTask.FromResult(((GatewayLocalRateState)state).Acquire(entry, _timeProvider.GetUtcNow().ToUnixTimeMilliseconds()))
        };
    }

    private async ValueTask<RateLimitLease> AcquireSharedAsync(
        TrafficAdmissionEntry entry,
        string key,
        CancellationToken cancellationToken)
    {
        if (_sharedProvider is null || _providerId is null || entry is not RequestRateAdmissionEntry)
            return GatewayAdmissionLease.Infrastructure("SharedProviderUnavailable");
        GatewaySharedAdmissionRequest request;
        try
        {
            request = CreateSharedRequest(entry, key);
        }
        catch
        {
            return GatewayAdmissionLease.Infrastructure("SharedRequestInvalid");
        }
        GatewaySharedAdmissionDecision decision = await _sharedProvider.AcquireAsync(request, cancellationToken).ConfigureAwait(false);
        switch (decision.Kind)
        {
            case GatewaySharedAdmissionDecisionKind.Acquired:
                return GatewayAdmissionLease.Acquired(decision.Remaining!.Value, decision.ResetAfterMilliseconds!.Value);
            case GatewaySharedAdmissionDecisionKind.Rejected:
                return GatewayAdmissionLease.Exhausted(decision.Remaining!.Value,
                    decision.RetryAfterMilliseconds!.Value, decision.ResetAfterMilliseconds!.Value);
            case GatewaySharedAdmissionDecisionKind.UnavailableBeforePossibleCommit:
                if (Capability.FailureDisposition == TrafficAdmissionFailureDisposition.Bypass)
                    return GatewayAdmissionLease.DegradedBypass();
                if (Capability.FailureDisposition == TrafficAdmissionFailureDisposition.LocalFallback && _localFallback is not null)
                    return await _localFallback.AcquireAsync(entry, key, cancellationToken).ConfigureAwait(false);
                return GatewayAdmissionLease.Infrastructure("SharedProviderUnavailable");
            case GatewaySharedAdmissionDecisionKind.CanceledBeforeDispatch when cancellationToken.IsCancellationRequested:
                throw new OperationCanceledException(cancellationToken);
            case GatewaySharedAdmissionDecisionKind.ConfigurationConflict:
                return GatewayAdmissionLease.Infrastructure("SharedConfigurationConflict");
            case GatewaySharedAdmissionDecisionKind.IndeterminateAfterPossibleCommit:
                return GatewayAdmissionLease.Infrastructure("SharedOutcomeIndeterminate");
            default:
                return GatewayAdmissionLease.Infrastructure("SharedProviderFailure");
        }
    }

    private GatewaySharedAdmissionRequest CreateSharedRequest(TrafficAdmissionEntry entry, string key)
    {
        var algorithm = Capability.RateAlgorithm ?? throw new InvalidOperationException();
        (long limit, long tokens, long period, int segments) = entry switch
        {
            FixedWindowAdmissionEntry value => (value.PermitLimit, 0, checked((long)value.Window.TotalMilliseconds), 0),
            SlidingWindowAdmissionEntry value => (value.PermitLimit, 0, checked((long)value.Window.TotalMilliseconds), value.SegmentsPerWindow),
            TokenBucketAdmissionEntry value => (value.TokenLimit, value.TokensPerPeriod, checked((long)value.ReplenishmentPeriod.TotalMilliseconds), 0),
            _ => throw new InvalidOperationException(),
        };
        return new GatewaySharedAdmissionRequest(GatewaySharedAdmissionContract.Version, _providerId!, Capability.AuthorityId,
            Capability.Name, Capability.BehaviorIdentity, key, algorithm, limit, tokens, period, segments, 1,
            Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16)));
    }

    private static object Create(TrafficAdmissionEntry entry) => entry switch
    {
        ConcurrencyAdmissionEntry value => new ConcurrencyLimiter(new ConcurrencyLimiterOptions
        { PermitLimit = value.PermitLimit, QueueLimit = value.QueueLimit, QueueProcessingOrder = QueueProcessingOrder.OldestFirst }),
        _ => new GatewayLocalRateState()
    };

    public void Dispose()
    {
        object[] states;
        lock (_statesGate)
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;
            states = [.. _states.Values];
            _states.Clear();
        }
        foreach (var state in states)
            (state as IDisposable)?.Dispose();
        _sharedProvider?.Dispose();
    }
}

internal sealed class GatewaySharedAdmissionProviderRuntime : IDisposable
{
    private readonly GatewaySharedAdmissionProviderRegistration _registration;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _capacity;
    private readonly CancellationTokenSource _disposeCancellation = new();
    private readonly object _dispatchGate = new();
    private int _active;
    private long _saturated;
    private long _detached;
    private long _late;
    private int _disposed;

    internal GatewaySharedAdmissionProviderRuntime(
        GatewaySharedAdmissionProviderRegistration registration,
        TimeProvider timeProvider)
    {
        _registration = registration;
        _timeProvider = timeProvider;
        _capacity = new SemaphoreSlim(registration.MaximumConcurrentInvocations, registration.MaximumConcurrentInvocations);
    }

    internal string AuthorityId => _registration.AuthorityId;
    internal ContentHash BehaviorIdentity => _registration.BehaviorIdentity;
    internal TimeSpan OperationTimeout => _registration.OperationTimeout;
    internal int MaximumConcurrentInvocations => _registration.MaximumConcurrentInvocations;

    internal async ValueTask<GatewaySharedAdmissionDecision> AcquireAsync(
        GatewaySharedAdmissionRequest request,
        CancellationToken callerCancellation)
    {
        if (!GatewaySharedAdmissionContract.IsValidRequest(request, requireUnitPermit: true) ||
            !StringComparer.Ordinal.Equals(request.ProviderId, _registration.ProviderId) ||
            !StringComparer.Ordinal.Equals(request.AuthorityId, _registration.AuthorityId))
            return Infrastructure(GatewaySharedAdmissionDecisionKind.UnavailableBeforePossibleCommit, "provider-request-invalid");
        if (Volatile.Read(ref _disposed) != 0)
            return Infrastructure(GatewaySharedAdmissionDecisionKind.UnavailableBeforePossibleCommit, "provider-disposed");
        long startedAt = _timeProvider.GetTimestamp();
        using var deadline = new CancellationTokenSource(_registration.OperationTimeout, _timeProvider);
        using var admission = CancellationTokenSource.CreateLinkedTokenSource(
            callerCancellation, deadline.Token, _disposeCancellation.Token);
        try
        {
            await _capacity.WaitAsync(admission.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (callerCancellation.IsCancellationRequested)
        {
            return Infrastructure(GatewaySharedAdmissionDecisionKind.CanceledBeforeDispatch, "canceled-before-dispatch");
        }
        catch (OperationCanceledException)
        {
            Interlocked.Increment(ref _saturated);
            return Infrastructure(GatewaySharedAdmissionDecisionKind.UnavailableBeforePossibleCommit, "capacity-unavailable");
        }

        if (_timeProvider.GetElapsedTime(startedAt) >= _registration.OperationTimeout)
        {
            _capacity.Release();
            Interlocked.Increment(ref _saturated);
            return Infrastructure(GatewaySharedAdmissionDecisionKind.UnavailableBeforePossibleCommit, "capacity-unavailable");
        }

        Task<GatewaySharedAdmissionDecision> operation;
        lock (_dispatchGate)
        {
            if (Volatile.Read(ref _disposed) != 0 || admission.IsCancellationRequested)
            {
                _capacity.Release();
                return callerCancellation.IsCancellationRequested
                    ? Infrastructure(GatewaySharedAdmissionDecisionKind.CanceledBeforeDispatch, "canceled-before-dispatch")
                    : Infrastructure(GatewaySharedAdmissionDecisionKind.UnavailableBeforePossibleCommit, "provider-disposed");
            }
            Interlocked.Increment(ref _active);
            try
            {
                operation = _registration.Provider.AcquireAsync(request, admission.Token).AsTask();
            }
            catch
            {
                ReleaseCapacity();
                return Infrastructure(GatewaySharedAdmissionDecisionKind.UnavailableBeforePossibleCommit, "provider-invocation-failed");
            }
        }

        if (operation.IsCompleted)
            return CompleteSynchronously(request, operation);

        TimeSpan remaining = _registration.OperationTimeout - _timeProvider.GetElapsedTime(startedAt);
        if (remaining <= TimeSpan.Zero)
        {
            Interlocked.Increment(ref _detached);
            _ = ObserveDetachedAsync(operation);
            return Infrastructure(GatewaySharedAdmissionDecisionKind.IndeterminateAfterPossibleCommit, "provider-outcome-indeterminate");
        }
        Task completed = await Task.WhenAny(operation,
            Task.Delay(remaining, _timeProvider, callerCancellation)).ConfigureAwait(false);
        if (ReferenceEquals(completed, operation))
            return CompleteSynchronously(request, operation);

        Interlocked.Increment(ref _detached);
        _ = ObserveDetachedAsync(operation);
        return Infrastructure(GatewaySharedAdmissionDecisionKind.IndeterminateAfterPossibleCommit, "provider-outcome-indeterminate");
    }

    private GatewaySharedAdmissionDecision CompleteSynchronously(GatewaySharedAdmissionRequest request, Task<GatewaySharedAdmissionDecision> operation)
    {
        try
        {
            GatewaySharedAdmissionDecision result = operation.GetAwaiter().GetResult();
            return GatewaySharedAdmissionContract.IsValidDecision(request, result)
                ? result
                : Infrastructure(GatewaySharedAdmissionDecisionKind.IndeterminateAfterPossibleCommit, "provider-result-invalid");
        }
        catch
        {
            return Infrastructure(GatewaySharedAdmissionDecisionKind.IndeterminateAfterPossibleCommit, "provider-operation-failed");
        }
        finally
        {
            ReleaseCapacity();
        }
    }

    private async Task ObserveDetachedAsync(Task<GatewaySharedAdmissionDecision> operation)
    {
        try { _ = await operation.ConfigureAwait(false); }
        catch { }
        finally
        {
            Interlocked.Increment(ref _late);
            ReleaseCapacity();
        }
    }

    private void ReleaseCapacity()
    {
        Interlocked.Decrement(ref _active);
        _capacity.Release();
    }

    internal GatewaySharedAdmissionProviderStatistics GetStatistics() => new(
        Math.Max(0, Volatile.Read(ref _active)), _registration.MaximumConcurrentInvocations,
        Interlocked.Read(ref _saturated), Interlocked.Read(ref _detached), Interlocked.Read(ref _late),
        Volatile.Read(ref _disposed) != 0);

    public void Dispose()
    {
        var cancel = false;
        lock (_dispatchGate)
            cancel = Interlocked.Exchange(ref _disposed, 1) == 0;
        if (cancel) _disposeCancellation.Cancel();
    }

    private static GatewaySharedAdmissionDecision Infrastructure(
        GatewaySharedAdmissionDecisionKind kind,
        string diagnostic) => new(kind, null, null, null, null, diagnostic);
}

internal readonly record struct GatewayProjectedPartition(bool IsSuccess, string? Value, string Code)
{
    internal static GatewayProjectedPartition Success(string value) => new(true, value, "ok");
    internal static GatewayProjectedPartition Failed(string code) => new(false, null, code);
}

internal sealed class GatewayLocalRateState
{
    private readonly object _gate = new();
    private long _lastObserved = long.MinValue;
    private long _windowStart = long.MinValue;
    private long _used;
    private long _tokens;
    private long _lastRefill;
    private long _remainder;
    private long _expiryAt;
    private bool _tokenInitialized;
    private long[]? _segments;
    private long[]? _segmentIndexes;

    internal RateLimitLease Acquire(TrafficAdmissionEntry entry, long now)
    {
        lock (_gate)
        {
            now = _lastObserved == long.MinValue ? Math.Max(0, now) : Math.Max(_lastObserved, now);
            _lastObserved = now;
            return entry switch
            {
                FixedWindowAdmissionEntry value => Fixed(value, now),
                SlidingWindowAdmissionEntry value => Sliding(value, now),
                TokenBucketAdmissionEntry value => Token(value, now),
                _ => GatewayAdmissionLease.Infrastructure("ProfileMismatch")
            };
        }
    }

    private RateLimitLease Fixed(FixedWindowAdmissionEntry value, long now)
    {
        var width = (long)value.Window.TotalMilliseconds;
        var start = now / width * width;
        if (_windowStart != start) { _windowStart = start; _used = 0; }
        _expiryAt = start + width;
        if (_used >= value.PermitLimit)
            return GatewayAdmissionLease.Exhausted(value.PermitLimit - _used, start + width - now, start + width - now);
        _used++;
        return GatewayAdmissionLease.Acquired(value.PermitLimit - _used, start + width - now);
    }

    private RateLimitLease Sliding(SlidingWindowAdmissionEntry value, long now)
    {
        var segmentWidth = (long)value.Window.TotalMilliseconds / value.SegmentsPerWindow;
        var epoch = now / segmentWidth;
        _segments ??= new long[value.SegmentsPerWindow];
        _segmentIndexes ??= Enumerable.Repeat(long.MinValue, value.SegmentsPerWindow).ToArray();
        if (_segments.Length != value.SegmentsPerWindow)
            return GatewayAdmissionLease.Infrastructure("ProfileChanged");
        var oldestRetained = epoch - value.SegmentsPerWindow + 1;
        for (var index = 0; index < _segments.Length; index++)
        {
            if (_segmentIndexes[index] < oldestRetained || _segmentIndexes[index] > epoch)
            {
                _segments[index] = 0;
                _segmentIndexes[index] = long.MinValue;
            }
        }
        var currentSlot = (int)(epoch % value.SegmentsPerWindow);
        if (_segmentIndexes[currentSlot] != epoch)
        {
            _segments[currentSlot] = 0;
            _segmentIndexes[currentSlot] = epoch;
        }
        var used = _segments.Sum();
        _expiryAt = checked(now + SlidingReset(now, segmentWidth));
        if (used >= value.PermitLimit)
        {
            var retry = SlidingRetry(value, now, segmentWidth, used);
            return GatewayAdmissionLease.Exhausted(value.PermitLimit - used, retry, _expiryAt - now);
        }
        _segments[currentSlot]++;
        _expiryAt = checked(now + SlidingReset(now, segmentWidth));
        return GatewayAdmissionLease.Acquired(value.PermitLimit - used - 1, _expiryAt - now);
    }

    private long SlidingRetry(SlidingWindowAdmissionEntry value, long now, long segmentWidth, long used)
    {
        var remainingUsed = used;
        foreach (var item in _segmentIndexes!.Select((index, slot) => (Index: index, Count: _segments![slot]))
            .Where(static item => item.Index != long.MinValue && item.Count > 0)
            .OrderBy(static item => item.Index))
        {
            remainingUsed -= item.Count;
            if (remainingUsed + 1 <= value.PermitLimit)
                return Math.Max(1, checked((item.Index + value.SegmentsPerWindow) * segmentWidth - now));
        }
        return Math.Max(1, segmentWidth);
    }

    private long SlidingReset(long now, long segmentWidth)
    {
        var newest = _segmentIndexes!.Where((index, slot) => index != long.MinValue && _segments![slot] > 0)
            .DefaultIfEmpty(now / segmentWidth).Max();
        return Math.Max(1, checked((newest + _segments!.Length) * segmentWidth - now));
    }

    private RateLimitLease Token(TokenBucketAdmissionEntry value, long now)
    {
        var period = (long)value.ReplenishmentPeriod.TotalMilliseconds;
        if (!_tokenInitialized)
        {
            _tokenInitialized = true;
            _lastRefill = now;
            _tokens = value.TokenLimit;
        }
        var elapsed = Math.Max(0, now - _lastRefill);
        var wholePeriods = elapsed / period;
        var residual = elapsed % period;
        var missing = value.TokenLimit - _tokens;
        var wholeAdded = wholePeriods >= CeilingDivide(missing, value.TokensPerPeriod)
            ? missing
            : checked(wholePeriods * value.TokensPerPeriod);
        UInt128 fractionalNumerator = (UInt128)(ulong)residual * (ulong)value.TokensPerPeriod + (ulong)_remainder;
        var fractionalAdded = (long)(fractionalNumerator / (ulong)period);
        var added = Math.Min(missing, checked(wholeAdded + fractionalAdded));
        _tokens += added;
        _remainder = _tokens == value.TokenLimit ? 0 : (long)(fractionalNumerator % (ulong)period);
        _lastRefill = now;
        if (_tokens == 0)
        {
            var retry = TokenDelay(1, period, value.TokensPerPeriod);
            var reset = TokenDelay(value.TokenLimit, period, value.TokensPerPeriod);
            _expiryAt = checked(now + reset);
            return GatewayAdmissionLease.Exhausted(0, retry, reset);
        }
        _tokens--;
        var acquiredReset = TokenDelay(value.TokenLimit - _tokens, period, value.TokensPerPeriod);
        _expiryAt = checked(now + acquiredReset);
        return GatewayAdmissionLease.Acquired(_tokens, acquiredReset);
    }

    private long TokenDelay(long missing, long period, long tokensPerPeriod)
    {
        if (missing <= 0) return 1;
        var numerator = checked(missing * period - _remainder);
        return Math.Max(1, CeilingDivide(Math.Max(0, numerator), tokensPerPeriod));
    }

    internal GatewayLocalRateStateSnapshot Snapshot(TrafficAdmissionRateAlgorithm algorithm)
    {
        lock (_gate)
        {
            var segments = _segmentIndexes is null ? ImmutableArray<GatewaySharedAdmissionSegmentState>.Empty :
                _segmentIndexes.Select((epoch, slot) => new GatewaySharedAdmissionSegmentState(epoch, _segments![slot]))
                    .Where(static value => value.Epoch != long.MinValue && value.Count > 0)
                    .OrderBy(static value => value.Epoch).ToImmutableArray();
            return new(algorithm, Math.Max(0, _lastObserved),
                algorithm == TrafficAdmissionRateAlgorithm.FixedWindow ? _windowStart : null,
                algorithm == TrafficAdmissionRateAlgorithm.FixedWindow ? _used : null,
                algorithm == TrafficAdmissionRateAlgorithm.TokenBucket ? _tokens : null,
                algorithm == TrafficAdmissionRateAlgorithm.TokenBucket ? _lastRefill : null,
                algorithm == TrafficAdmissionRateAlgorithm.TokenBucket ? _remainder : null,
                segments, _expiryAt);
        }
    }

    private static long CeilingDivide(long value, long divisor) => value == 0 ? 0 : checked((value - 1) / divisor + 1);
}

internal sealed class GatewayAdmissionLease(bool acquired, string? reason, IEnumerable<RateLimitLease>? owned = null, IReadOnlyDictionary<string, object?>? metadata = null) : RateLimitLease
{
    private readonly RateLimitLease[] _owned = owned?.ToArray() ?? [];
    private readonly IReadOnlyDictionary<string, object?> _metadata = metadata ?? (reason is null ? ImmutableDictionary<string, object?>.Empty : new Dictionary<string, object?> { ["Reason"] = reason });
    private int _disposed;
    public override bool IsAcquired { get; } = acquired;
    public override IEnumerable<string> MetadataNames => _metadata.Keys;
    public override bool TryGetMetadata(string metadataName, out object? metadata) => _metadata.TryGetValue(metadataName, out metadata);
    protected override void Dispose(bool disposing)
    {
        if (!disposing || Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        foreach (var lease in _owned)
            lease.Dispose();
    }

    internal static GatewayAdmissionLease Acquired(long remaining, long resetMilliseconds) => new(true, null, metadata: Facts(
        GatewayAdmissionOutcome.Acquired, remaining, null, Math.Max(1, resetMilliseconds)));

    internal static GatewayAdmissionLease Exhausted(long remaining, long retryMilliseconds, long resetMilliseconds) => new(false, "LimitExceeded", metadata: Facts(
        GatewayAdmissionOutcome.Exhausted, remaining, Math.Max(1, retryMilliseconds), Math.Max(Math.Max(1, retryMilliseconds), resetMilliseconds)));

    internal static GatewayAdmissionLease Infrastructure(string reason) => new(false, reason, metadata: new Dictionary<string, object?>
    {
        [GatewayAdmissionMetadata.Outcome] = GatewayAdmissionOutcome.Infrastructure,
        ["Reason"] = reason
    });

    internal static GatewayAdmissionLease DegradedBypass() => new(true, null, metadata: new Dictionary<string, object?>
    {
        [GatewayAdmissionMetadata.Outcome] = GatewayAdmissionOutcome.Acquired,
        ["HPD.Gateway.Admission.Degraded"] = "Bypass"
    });

    internal static GatewayAdmissionLease Combined(IEnumerable<RateLimitLease> owned, bool degradedBypass) =>
        new(true, null, owned, degradedBypass ? new Dictionary<string, object?>
        {
            [GatewayAdmissionMetadata.Outcome] = GatewayAdmissionOutcome.Acquired,
            ["HPD.Gateway.Admission.Degraded"] = "Bypass"
        } : null);

    internal static GatewayAdmissionLease FromRejectedEntry(RateLimitLease lease, bool concurrency)
    {
        if (lease.TryGetMetadata(GatewayAdmissionMetadata.Outcome, out var outcome) && outcome is GatewayAdmissionOutcome typed)
        {
            var copied = lease.MetadataNames.ToDictionary(name => name, name =>
            {
                lease.TryGetMetadata(name, out var value);
                return value;
            }, StringComparer.Ordinal);
            return new GatewayAdmissionLease(false, null, metadata: copied);
        }
        return concurrency
            ? new GatewayAdmissionLease(false, "ConcurrencyExhausted", metadata: new Dictionary<string, object?>
            {
                [GatewayAdmissionMetadata.Outcome] = GatewayAdmissionOutcome.Exhausted,
                ["Reason"] = "ConcurrencyExhausted"
            })
            : Infrastructure("MalformedRateLease");
    }

    private static IReadOnlyDictionary<string, object?> Facts(
        GatewayAdmissionOutcome outcome,
        long remaining,
        long? retryMilliseconds,
        long resetMilliseconds)
    {
        var facts = new Dictionary<string, object?>
        {
            [GatewayAdmissionMetadata.Outcome] = outcome,
            [GatewayAdmissionMetadata.Remaining] = remaining,
            [GatewayAdmissionMetadata.RetryAfterMilliseconds] = retryMilliseconds,
            [GatewayAdmissionMetadata.ResetAfterMilliseconds] = resetMilliseconds
        };
        if (retryMilliseconds is { } retry)
            facts["RetryAfter"] = TimeSpan.FromMilliseconds(retry);
        return facts;
    }

}

internal sealed record GatewayLocalRateStateSnapshot(
    TrafficAdmissionRateAlgorithm Algorithm, long LastObservedMilliseconds, long? WindowStartMilliseconds,
    long? Used, long? Tokens, long? LastRefillMilliseconds, long? Remainder,
    ImmutableArray<GatewaySharedAdmissionSegmentState> Segments, long ExpiryAtMilliseconds);
