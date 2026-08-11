using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Yarp.ReverseProxy.Model;

namespace HPD.Gateway;

public sealed class GatewayLocalAdmissionOptions
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
    private readonly List<(string Name, TrafficAdmissionKind Kind, TrafficAdmissionRateAlgorithm? Algorithm, GatewayLocalAdmissionOptions Options)> _profiles = [];
    private readonly Dictionary<string, GatewayAdmissionProjectorRegistration> _projectors = new(StringComparer.Ordinal);
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

    private GatewayTrafficAdmissionRegistryBuilder Add(string name, TrafficAdmissionKind kind, TrafficAdmissionRateAlgorithm? algorithm, Action<GatewayLocalAdmissionOptions>? configure)
    {
        if (_profiles.Count >= MaximumProfiles) throw new InvalidOperationException("Traffic-admission profile capacity was exceeded.");
        if (!GatewayIdentifier.IsCanonical(name) || _profiles.Any(value => StringComparer.Ordinal.Equals(value.Name, name)))
            throw new ArgumentException("Traffic-admission profile names must be canonical and unique.", nameof(name));
        var options = new GatewayLocalAdmissionOptions();
        configure?.Invoke(options);
        Validate(options);
        _profiles.Add((name, kind, algorithm, Snapshot(options)));
        return this;
    }

    internal GatewayTrafficAdmissionRegistry Build()
    {
        var concurrencyNames = _profiles.Where(static value => value.Kind == TrafficAdmissionKind.Concurrency)
            .Select(static value => value.Name).Order(StringComparer.Ordinal).ToArray();
        var capabilities = ImmutableArray.CreateBuilder<TrafficAdmissionCapability>(_profiles.Count);
        var runtimes = ImmutableDictionary.CreateBuilder<string, GatewayAdmissionProfileRuntime>(StringComparer.Ordinal);
        foreach (var profile in _profiles.OrderBy(static value => value.Name, StringComparer.Ordinal))
        {
            GatewayAdmissionProjectorRegistration? projector = null;
            if (RequiresProjector(profile.Options.Partition) &&
                (profile.Options.PartitionProjector is null || !_projectors.TryGetValue(profile.Options.PartitionProjector, out projector)))
                throw new InvalidOperationException($"Traffic-admission profile '{profile.Name}' requires an installed partition projector.");
            var ordinal = profile.Kind == TrafficAdmissionKind.Concurrency ? Array.IndexOf(concurrencyNames, profile.Name) : (int?)null;
            var limits = new TrafficAdmissionLimits(profile.Options.MinimumLimit, profile.Options.MaximumLimit,
                profile.Kind == TrafficAdmissionKind.RequestRate ? profile.Options.MinimumPeriod : null,
                profile.Kind == TrafficAdmissionKind.RequestRate ? profile.Options.MaximumPeriod : null,
                profile.Options.MinimumSegments, profile.Options.MaximumSegments, profile.Options.MinimumQueue, profile.Options.MaximumQueue);
            var identityText = string.Join('|', profile.Name, profile.Kind, profile.Algorithm, profile.Options.Partition,
                limits.MinimumLimit, limits.MaximumLimit, limits.MinimumPeriod?.Ticks, limits.MaximumPeriod?.Ticks,
                limits.MinimumSegments, limits.MaximumSegments, limits.MinimumQueue, limits.MaximumQueue,
                projector?.Name, projector?.BehaviorIdentity.Value, ordinal);
            var identity = new ContentHash("sha-256", Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(identityText))));
            var capability = new TrafficAdmissionCapability(profile.Name, 1, TrafficAdmissionScope.ProcessLocal, profile.Kind,
                profile.Algorithm, profile.Options.Partition, TrafficAdmissionFailureDisposition.Reject, limits,
                "hpd.gateway/process-local", identity, ordinal, projector?.Name, projector?.BehaviorIdentity);
            capabilities.Add(capability);
            runtimes.Add(profile.Name, new GatewayAdmissionProfileRuntime(capability, projector, _timeProvider));
        }
        return new GatewayTrafficAdmissionRegistry(capabilities.MoveToImmutable(), runtimes.ToImmutable());
    }

    private static GatewayLocalAdmissionOptions Snapshot(GatewayLocalAdmissionOptions value) => new()
    {
        Partition = value.Partition, MinimumLimit = value.MinimumLimit, MaximumLimit = value.MaximumLimit,
        MinimumPeriod = value.MinimumPeriod, MaximumPeriod = value.MaximumPeriod,
        MinimumSegments = value.MinimumSegments, MaximumSegments = value.MaximumSegments,
        MinimumQueue = value.MinimumQueue, MaximumQueue = value.MaximumQueue, PartitionProjector = value.PartitionProjector
    };

    private static void Validate(GatewayLocalAdmissionOptions value)
    {
        if (!Enum.IsDefined(value.Partition) || value.MinimumLimit < 1 || value.MaximumLimit < value.MinimumLimit || value.MaximumLimit > 100_000_000 ||
            value.MinimumPeriod < TimeSpan.FromMilliseconds(100) || value.MaximumPeriod < value.MinimumPeriod || value.MaximumPeriod > TimeSpan.FromDays(1) ||
            value.MinimumSegments < 2 || value.MaximumSegments < value.MinimumSegments || value.MaximumSegments > 64 ||
            value.MinimumQueue < 0 || value.MaximumQueue < value.MinimumQueue || value.MaximumQueue > 100_000 ||
            (RequiresProjector(value.Partition) &&
             (value.PartitionProjector is null || !GatewayIdentifier.IsCanonical(value.PartitionProjector))) ||
            (!RequiresProjector(value.Partition) && value.PartitionProjector is not null))
            throw new ArgumentException("Traffic-admission options are invalid or unbounded.", nameof(value));
    }

    private static bool RequiresProjector(TrafficAdmissionPartitionKind partition) => partition is
        TrafficAdmissionPartitionKind.AuthenticatedSubject or TrafficAdmissionPartitionKind.Tenant or
        TrafficAdmissionPartitionKind.Consumer or TrafficAdmissionPartitionKind.Custom;
}

internal sealed record GatewayAdmissionProjectorRegistration(
    string Name,
    ContentHash BehaviorIdentity,
    IGatewayAdmissionPartitionProjector Projector);

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

internal sealed record GatewayTrafficAdmissionMetadata(
    string ApplicationId,
    ContentHash SymbolicPlanIdentity,
    RouteId RouteId,
    ContentHash AdmissionPlanIdentity,
    TrafficAdmissionPlan Plan);

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
                lease.Dispose();
            }
            return new GatewayAdmissionLease(true, null, leases);
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
    private int _disposed;
    internal GatewayAdmissionProfileRuntime(
        TrafficAdmissionCapability capability,
        GatewayAdmissionProjectorRegistration? projector,
        TimeProvider timeProvider)
    {
        Capability = capability;
        _projector = projector;
        _timeProvider = timeProvider;
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
                if (context.User.Identity?.IsAuthenticated != true || _projector is null)
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
    }
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
        if (used >= value.PermitLimit)
        {
            var retry = SlidingRetry(value, now, segmentWidth, used);
            return GatewayAdmissionLease.Exhausted(value.PermitLimit - used, retry, SlidingReset(now, segmentWidth));
        }
        _segments[currentSlot]++;
        return GatewayAdmissionLease.Acquired(value.PermitLimit - used - 1, SlidingReset(now, segmentWidth));
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
            return GatewayAdmissionLease.Exhausted(0, retry, TokenDelay(value.TokenLimit, period, value.TokensPerPeriod));
        }
        _tokens--;
        return GatewayAdmissionLease.Acquired(_tokens, TokenDelay(value.TokenLimit - _tokens, period, value.TokensPerPeriod));
    }

    private long TokenDelay(long missing, long period, long tokensPerPeriod)
    {
        if (missing <= 0) return 1;
        var numerator = checked(missing * period - _remainder);
        return Math.Max(1, CeilingDivide(Math.Max(0, numerator), tokensPerPeriod));
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
