using System.Collections.Immutable;
using System.Diagnostics.Metrics;
using System.Net;
using HPD.Gateway.Abstractions;
using HPD.Gateway.Core;
using HPD.Gateway.Yarp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using Polly.Timeout;

namespace HPD.Gateway.Resilience;

public sealed record GatewayResponseRetryProfile
{
    public ImmutableArray<HttpStatusCode> StatusCodes { get; init; } = [];
    public int MaximumRetryAttempts { get; init; } = 1;
    public TimeSpan Delay { get; init; } = TimeSpan.Zero;
    public TimeSpan MaximumRetryAfter { get; init; } = TimeSpan.FromSeconds(5);
}

public sealed record GatewayCircuitBreakerProfile
{
    public ImmutableArray<HttpStatusCode> StatusCodes { get; init; } = [];
    public double FailureRatio { get; init; } = 0.5;
    public int MinimumThroughput { get; init; } = 20;
    public TimeSpan SamplingDuration { get; init; } = TimeSpan.FromSeconds(30);
    public TimeSpan BreakDuration { get; init; } = TimeSpan.FromSeconds(15);
}

public sealed record GatewayOutboundConcurrencyProfile
{
    public int PermitLimit { get; init; } = 100;
    public int QueueLimit { get; init; }
}

public sealed record GatewayAttemptTimeoutProfile
{
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(10);
}

public sealed record GatewayResilienceProfile
{
    public required string Name { get; init; }
    public required int Version { get; init; }
    public GatewayResponseRetryProfile? Retry { get; init; }
    public GatewayCircuitBreakerProfile? CircuitBreaker { get; init; }
    public GatewayOutboundConcurrencyProfile? ConcurrencyLimiter { get; init; }
    public GatewayAttemptTimeoutProfile? AttemptTimeout { get; init; }
}

public sealed class GatewayResilienceRegistryBuilder
{
    private readonly Dictionary<string, GatewayResilienceProfile> _profiles = new(StringComparer.Ordinal);

    public GatewayResilienceRegistryBuilder Add(GatewayResilienceProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        Validate(profile);
        if (!_profiles.TryAdd(profile.Name, profile)) throw new ArgumentException("Resilience profile names must be unique.", nameof(profile));
        return this;
    }

    internal GatewayResilienceRegistry Build() => new(_profiles.ToImmutableDictionary(StringComparer.Ordinal));

    private static void Validate(GatewayResilienceProfile profile)
    {
        if (!GatewayIdentifier.IsCanonical(profile.Name)) throw new ArgumentException("Profile name must be canonical.", nameof(profile));
        if (profile.Version <= 0) throw new ArgumentOutOfRangeException(nameof(profile));
        if (profile.Retry is null && profile.CircuitBreaker is null && profile.ConcurrencyLimiter is null && profile.AttemptTimeout is null)
            throw new ArgumentException("A resilience profile must contain at least one strategy.", nameof(profile));
        if (profile.Retry is { } retry)
        {
            ValidateStatuses(retry.StatusCodes, nameof(profile));
            if (retry.MaximumRetryAttempts is < 1 or > 5 || retry.Delay < TimeSpan.Zero || retry.Delay > TimeSpan.FromSeconds(30) ||
                retry.MaximumRetryAfter < TimeSpan.Zero || retry.MaximumRetryAfter > TimeSpan.FromMinutes(1))
                throw new ArgumentOutOfRangeException(nameof(profile));
        }
        if (profile.CircuitBreaker is { } breaker)
        {
            ValidateStatuses(breaker.StatusCodes, nameof(profile));
            if (breaker.FailureRatio is <= 0 or > 1 || breaker.MinimumThroughput is < 2 or > 10_000 ||
                breaker.SamplingDuration < TimeSpan.FromSeconds(1) || breaker.SamplingDuration > TimeSpan.FromHours(1) ||
                breaker.BreakDuration < TimeSpan.FromSeconds(1) || breaker.BreakDuration > TimeSpan.FromHours(1))
                throw new ArgumentOutOfRangeException(nameof(profile));
        }
        if (profile.ConcurrencyLimiter is { } limiter && (limiter.PermitLimit is < 1 or > 100_000 || limiter.QueueLimit is < 0 or > 100_000))
            throw new ArgumentOutOfRangeException(nameof(profile));
        if (profile.AttemptTimeout is { } timeout && (timeout.Timeout < TimeSpan.FromMilliseconds(10) || timeout.Timeout > TimeSpan.FromHours(1)))
            throw new ArgumentOutOfRangeException(nameof(profile));
    }

    private static void ValidateStatuses(ImmutableArray<HttpStatusCode> statuses, string name)
    {
        if (statuses.IsDefaultOrEmpty || statuses.Length > 32 || statuses.Any(static value => (int)value is < 100 or > 599) ||
            statuses.Distinct().Count() != statuses.Length ||
            !statuses.Select(static value => (int)value).SequenceEqual(statuses.Select(static value => (int)value).Order()))
            throw new ArgumentException("Status codes must be initialized, bounded, valid, unique, and sorted.", name);
    }
}

internal sealed class GatewayResilienceRegistry(ImmutableDictionary<string, GatewayResilienceProfile> profiles) : GatewayUpstreamResilienceProvider
{
    private readonly ImmutableDictionary<string, GatewayResilienceProfile> _profiles = profiles;

    internal override ImmutableArray<UpstreamResilienceCapability> Capabilities => _profiles.Values
        .OrderBy(static profile => profile.Name, StringComparer.Ordinal)
        .Select(static profile => new UpstreamResilienceCapability(
            profile.Name,
            profile.Version,
            Strategies(profile),
            profile.Retry?.StatusCodes.Select(static status => (int)status).Order().ToImmutableArray() ?? [],
            profile.Retry?.MaximumRetryAttempts ?? 0))
        .ToImmutableArray();

    internal override bool IsInstalled(string name, int version) =>
        _profiles.TryGetValue(name, out var profile) && profile.Version == version;

    internal override HttpMessageHandler Wrap(string name, int version, HttpMessageHandler inner)
    {
        if (!_profiles.TryGetValue(name, out var profile) || profile.Version != version)
            throw new InvalidOperationException("The selected resilience profile is not installed.");
        var pipeline = BuildPipeline(profile);
        HttpMessageHandler handler = new ResilienceHandler(pipeline) { InnerHandler = inner };
        if (profile.ConcurrencyLimiter is { } limiter) handler = new GatewayConcurrencyHandler(profile.Name, limiter, handler);
        return handler;
    }

    private static ResiliencePipeline<HttpResponseMessage> BuildPipeline(GatewayResilienceProfile profile)
    {
        var builder = new ResiliencePipelineBuilder<HttpResponseMessage>();
        if (profile.Retry is { } retry)
        {
            var statuses = retry.StatusCodes.ToImmutableHashSet();
            builder.AddRetry(new RetryStrategyOptions<HttpResponseMessage>
            {
                MaxRetryAttempts = retry.MaximumRetryAttempts,
                Delay = retry.Delay,
                BackoffType = DelayBackoffType.Constant,
                UseJitter = false,
                ShouldHandle = args => ValueTask.FromResult(
                    args.Outcome.Exception is null && args.Outcome.Result is { } response &&
                    response.RequestMessage is { } request && IsRetryEligible(request) && statuses.Contains(response.StatusCode)),
                DelayGenerator = args => ValueTask.FromResult(BoundedRetryAfter(args.Outcome.Result, retry.MaximumRetryAfter)),
                OnRetry = args =>
                {
                    GatewayResilienceTelemetry.Record(profile.Name, "retry", "attempt");
                    args.Outcome.Result?.Dispose();
                    return default;
                }
            });
        }
        if (profile.CircuitBreaker is { } breaker)
        {
            var statuses = breaker.StatusCodes.ToImmutableHashSet();
            builder.AddCircuitBreaker(new CircuitBreakerStrategyOptions<HttpResponseMessage>
            {
                FailureRatio = breaker.FailureRatio,
                MinimumThroughput = breaker.MinimumThroughput,
                SamplingDuration = breaker.SamplingDuration,
                BreakDuration = breaker.BreakDuration,
                ShouldHandle = args => ValueTask.FromResult(args.Outcome.Result is { } response && statuses.Contains(response.StatusCode)),
                OnOpened = _ => { GatewayResilienceTelemetry.Record(profile.Name, "circuit", "opened"); return default; },
                OnClosed = _ => { GatewayResilienceTelemetry.Record(profile.Name, "circuit", "closed"); return default; },
                OnHalfOpened = _ => { GatewayResilienceTelemetry.Record(profile.Name, "circuit", "half-opened"); return default; }
            });
        }
        if (profile.AttemptTimeout is { } timeout) builder.AddTimeout(new TimeoutStrategyOptions
        {
            Timeout = timeout.Timeout,
            OnTimeout = _ => { GatewayResilienceTelemetry.Record(profile.Name, "timeout", "elapsed"); return default; }
        });
        return builder.Build();
    }

    private static TimeSpan? BoundedRetryAfter(HttpResponseMessage? response, TimeSpan maximum)
    {
        var retryAfter = response?.Headers.RetryAfter;
        if (retryAfter?.Delta is { } delta) return delta <= maximum ? delta : maximum;
        if (retryAfter?.Date is { } date)
        {
            var delay = date - DateTimeOffset.UtcNow;
            if (delay < TimeSpan.Zero) return TimeSpan.Zero;
            return delay <= maximum ? delay : maximum;
        }
        return null;
    }

    private static bool IsRetryEligible(HttpRequestMessage request) =>
        request.Content is null && request.Version.Major < 3 &&
        (request.Method == HttpMethod.Get || request.Method == HttpMethod.Head || request.Method == HttpMethod.Options || request.Method == HttpMethod.Trace) &&
        !request.Headers.Connection.Contains("Upgrade", StringComparer.OrdinalIgnoreCase) && !request.Headers.Contains("Upgrade");

    private static UpstreamResilienceStrategies Strategies(GatewayResilienceProfile profile)
    {
        var result = UpstreamResilienceStrategies.None;
        if (profile.Retry is not null) result |= UpstreamResilienceStrategies.SelectedResponseRetry;
        if (profile.CircuitBreaker is not null) result |= UpstreamResilienceStrategies.CircuitBreaker;
        if (profile.ConcurrencyLimiter is not null) result |= UpstreamResilienceStrategies.OutboundConcurrencyLimiter;
        if (profile.AttemptTimeout is not null) result |= UpstreamResilienceStrategies.PerAttemptTimeout;
        return result;
    }
}

internal sealed class GatewayConcurrencyHandler : DelegatingHandler
{
    private readonly string _profileName;
    private readonly SemaphoreSlim _permits;
    private readonly int _queueLimit;
    private int _queued;

    internal GatewayConcurrencyHandler(string profileName, GatewayOutboundConcurrencyProfile profile, HttpMessageHandler inner)
    {
        _profileName = profileName;
        _permits = new SemaphoreSlim(profile.PermitLimit, profile.PermitLimit);
        _queueLimit = profile.QueueLimit;
        InnerHandler = inner;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (!await TryEnterAsync(cancellationToken).ConfigureAwait(false))
        {
            GatewayResilienceTelemetry.Record(_profileName, "concurrency", "rejected");
            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { RequestMessage = request };
        }
        try { return await base.SendAsync(request, cancellationToken).ConfigureAwait(false); }
        finally { _permits.Release(); }
    }

    private async ValueTask<bool> TryEnterAsync(CancellationToken cancellationToken)
    {
        if (_permits.Wait(0)) return true;
        if (_queueLimit == 0 || Interlocked.Increment(ref _queued) > _queueLimit)
        {
            Interlocked.Decrement(ref _queued);
            return false;
        }
        try { await _permits.WaitAsync(cancellationToken).ConfigureAwait(false); return true; }
        finally { Interlocked.Decrement(ref _queued); }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _permits.Dispose();
        base.Dispose(disposing);
    }
}

internal static class GatewayResilienceTelemetry
{
    internal const string MeterName = "HPD.Gateway.Resilience";
    internal const string InstrumentName = "hpd.gateway.resilience.events";
    private static readonly Meter Meter = new(MeterName, "1.0.0");
    private static readonly Counter<long> Events = Meter.CreateCounter<long>(InstrumentName);

    internal static void Record(string profileName, string strategy, string outcome) => Events.Add(1,
        new KeyValuePair<string, object?>("hpd.gateway.resilience.profile", profileName),
        new KeyValuePair<string, object?>("hpd.gateway.resilience.strategy", strategy),
        new KeyValuePair<string, object?>("hpd.gateway.resilience.outcome", outcome));
}

public static class GatewayResilienceServiceCollectionExtensions
{
    public static IServiceCollection AddHpdGatewayYarpResilience(
        this IServiceCollection services,
        Action<GatewayResilienceRegistryBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);
        if (services.Any(static descriptor => descriptor.ServiceType == typeof(GatewayUpstreamResilienceProvider)))
            throw new InvalidOperationException("Gateway resilience may be registered only once.");
        var builder = new GatewayResilienceRegistryBuilder();
        configure(builder);
        var registry = builder.Build();
        services.AddSingleton(registry);
        services.AddSingleton<GatewayUpstreamResilienceProvider>(registry);
        return services;
    }
}
