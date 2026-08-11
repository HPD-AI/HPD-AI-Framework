using System.Collections.Immutable;
using System.Security.Claims;
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
    public string? ClaimType { get; set; }
}

public sealed class GatewayTrafficAdmissionRegistryBuilder
{
    private const int MaximumProfiles = 128;
    private readonly List<(string Name, TrafficAdmissionKind Kind, TrafficAdmissionRateAlgorithm? Algorithm, GatewayLocalAdmissionOptions Options)> _profiles = [];

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
            var ordinal = profile.Kind == TrafficAdmissionKind.Concurrency ? Array.IndexOf(concurrencyNames, profile.Name) : (int?)null;
            var limits = new TrafficAdmissionLimits(profile.Options.MinimumLimit, profile.Options.MaximumLimit,
                profile.Kind == TrafficAdmissionKind.RequestRate ? profile.Options.MinimumPeriod : null,
                profile.Kind == TrafficAdmissionKind.RequestRate ? profile.Options.MaximumPeriod : null,
                profile.Options.MinimumSegments, profile.Options.MaximumSegments, profile.Options.MinimumQueue, profile.Options.MaximumQueue);
            var identityText = string.Join('|', profile.Name, profile.Kind, profile.Algorithm, profile.Options.Partition,
                limits.MinimumLimit, limits.MaximumLimit, limits.MinimumPeriod?.Ticks, limits.MaximumPeriod?.Ticks,
                limits.MinimumSegments, limits.MaximumSegments, limits.MinimumQueue, limits.MaximumQueue, profile.Options.ClaimType, ordinal);
            var identity = new ContentHash("sha-256", Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(identityText))));
            var capability = new TrafficAdmissionCapability(profile.Name, 1, TrafficAdmissionScope.ProcessLocal, profile.Kind,
                profile.Algorithm, profile.Options.Partition, TrafficAdmissionFailureDisposition.Reject, limits,
                "hpd.gateway/process-local", identity, ordinal);
            capabilities.Add(capability);
            runtimes.Add(profile.Name, new GatewayAdmissionProfileRuntime(capability, profile.Options.ClaimType));
        }
        return new GatewayTrafficAdmissionRegistry(capabilities.MoveToImmutable(), runtimes.ToImmutable());
    }

    private static GatewayLocalAdmissionOptions Snapshot(GatewayLocalAdmissionOptions value) => new()
    {
        Partition = value.Partition, MinimumLimit = value.MinimumLimit, MaximumLimit = value.MaximumLimit,
        MinimumPeriod = value.MinimumPeriod, MaximumPeriod = value.MaximumPeriod,
        MinimumSegments = value.MinimumSegments, MaximumSegments = value.MaximumSegments,
        MinimumQueue = value.MinimumQueue, MaximumQueue = value.MaximumQueue, ClaimType = value.ClaimType
    };

    private static void Validate(GatewayLocalAdmissionOptions value)
    {
        if (!Enum.IsDefined(value.Partition) || value.MinimumLimit < 1 || value.MaximumLimit < value.MinimumLimit || value.MaximumLimit > 100_000_000 ||
            value.MinimumPeriod < TimeSpan.FromMilliseconds(100) || value.MaximumPeriod < value.MinimumPeriod || value.MaximumPeriod > TimeSpan.FromDays(1) ||
            value.MinimumSegments < 2 || value.MaximumSegments < value.MinimumSegments || value.MaximumSegments > 64 ||
            value.MinimumQueue < 0 || value.MaximumQueue < value.MinimumQueue || value.MaximumQueue > 100_000 ||
            (value.Partition is TrafficAdmissionPartitionKind.Tenant or TrafficAdmissionPartitionKind.Consumer or TrafficAdmissionPartitionKind.Custom &&
             (string.IsNullOrWhiteSpace(value.ClaimType) || value.ClaimType.Length > 256)))
            throw new ArgumentException("Traffic-admission options are invalid or unbounded.", nameof(value));
    }
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
    internal static RateLimiterOptions CreateOptions(GatewayTrafficAdmissionRegistry registry) => new()
    {
        GlobalLimiter = new GatewayTrafficAdmissionLimiter(registry),
        RejectionStatusCode = StatusCodes.Status429TooManyRequests
    };
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
        if (permitCount != 1) return new GatewayAdmissionLease(false, "UnsupportedPermitCount");
        var projected = new List<(TrafficAdmissionEntry Entry, GatewayAdmissionProfileRuntime Runtime, string Key)>();
        foreach (var entry in metadata.Plan.Entries)
        {
            if (!registry.TryGet(entry.ProfileName, out var runtime)) return new GatewayAdmissionLease(false, "ProfileUnavailable");
            var key = runtime.Project(context, metadata.RouteId);
            if (key is null || Encoding.UTF8.GetByteCount(key) > 256) return new GatewayAdmissionLease(false, "PartitionUnavailable");
            projected.Add((entry, runtime, key + "\0" + GatewayRuntimePlanner.HashTrafficAdmission(new TrafficAdmissionPlan { Entries = [entry] }).Value));
        }
        var leases = new List<RateLimitLease>();
        try
        {
            foreach (var item in projected.Where(static value => value.Entry is ConcurrencyAdmissionEntry)
                .OrderBy(static value => value.Runtime.Capability.AcquisitionOrdinal))
            {
                var lease = await item.Runtime.AcquireAsync(item.Entry, item.Key, cancellationToken).ConfigureAwait(false);
                if (!lease.IsAcquired) { lease.Dispose(); return lease; }
                leases.Add(lease);
            }
            foreach (var item in projected.Where(static value => value.Entry is RequestRateAdmissionEntry))
            {
                var lease = await item.Runtime.AcquireAsync(item.Entry, item.Key, cancellationToken).ConfigureAwait(false);
                if (!lease.IsAcquired) { lease.Dispose(); return lease; }
                lease.Dispose();
            }
            return new GatewayAdmissionLease(true, null, leases);
        }
        catch
        {
            foreach (var lease in leases) lease.Dispose();
            throw;
        }
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
    private readonly string? _claimType;
    private int _disposed;
    internal GatewayAdmissionProfileRuntime(TrafficAdmissionCapability capability, string? claimType) { Capability = capability; _claimType = claimType; }
    internal TrafficAdmissionCapability Capability { get; }

    internal string? Project(HttpContext context, RouteId route) => Capability.Partition switch
    {
        TrafficAdmissionPartitionKind.Global => "global",
        TrafficAdmissionPartitionKind.Route => route.Value,
        TrafficAdmissionPartitionKind.SourceIp => context.Connection.RemoteIpAddress?.ToString(),
        TrafficAdmissionPartitionKind.AuthenticatedSubject => context.User.Identity?.IsAuthenticated == true ? context.User.FindFirstValue(ClaimTypes.NameIdentifier) : null,
        TrafficAdmissionPartitionKind.Tenant or TrafficAdmissionPartitionKind.Consumer or TrafficAdmissionPartitionKind.Custom =>
            context.User.Identity?.IsAuthenticated == true ? context.User.FindFirstValue(_claimType!) : null,
        _ => null
    };

    internal ValueTask<RateLimitLease> AcquireAsync(TrafficAdmissionEntry entry, string key, CancellationToken cancellationToken)
    {
        object state;
        lock (_statesGate)
        {
            if (_disposed != 0)
                return ValueTask.FromResult<RateLimitLease>(new GatewayAdmissionLease(false, "Disposed"));
            if (!_states.TryGetValue(key, out state!))
            {
                if (_states.Count >= MaximumPartitions)
                    return ValueTask.FromResult<RateLimitLease>(new GatewayAdmissionLease(false, "PartitionCapacity"));
                state = Create(entry);
                _states.Add(key, state);
            }
        }
        return entry switch
        {
            ConcurrencyAdmissionEntry => ((ConcurrencyLimiter)state).AcquireAsync(1, cancellationToken),
            _ => ValueTask.FromResult(((GatewayLocalRateState)state).Acquire(entry, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()))
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

internal sealed class GatewayLocalRateState
{
    private readonly object _gate = new();
    private long _windowStart = long.MinValue;
    private long _used;
    private long _tokens;
    private long _lastRefill;
    private long _remainder;
    private long[]? _segments;
    private long _segmentEpoch = long.MinValue;

    internal RateLimitLease Acquire(TrafficAdmissionEntry entry, long now)
    {
        lock (_gate)
        {
            return entry switch
            {
                FixedWindowAdmissionEntry value => Fixed(value, now),
                SlidingWindowAdmissionEntry value => Sliding(value, now),
                TokenBucketAdmissionEntry value => Token(value, now),
                _ => new GatewayAdmissionLease(false, "ProfileMismatch")
            };
        }
    }

    private RateLimitLease Fixed(FixedWindowAdmissionEntry value, long now)
    {
        var width = (long)value.Window.TotalMilliseconds;
        var start = now / width * width;
        if (_windowStart != start) { _windowStart = start; _used = 0; }
        if (_used >= value.PermitLimit) return Reject(start + width - now);
        _used++;
        return Accept(value.PermitLimit - _used, start + width - now);
    }

    private RateLimitLease Sliding(SlidingWindowAdmissionEntry value, long now)
    {
        var segmentWidth = (long)value.Window.TotalMilliseconds / value.SegmentsPerWindow;
        var epoch = now / segmentWidth;
        _segments ??= new long[value.SegmentsPerWindow];
        if (_segments.Length != value.SegmentsPerWindow) return new GatewayAdmissionLease(false, "ProfileChanged");
        if (_segmentEpoch == long.MinValue || epoch - _segmentEpoch >= value.SegmentsPerWindow) Array.Clear(_segments);
        else for (var e = _segmentEpoch + 1; e <= epoch; e++) _segments[e % value.SegmentsPerWindow] = 0;
        _segmentEpoch = Math.Max(_segmentEpoch, epoch);
        var used = _segments.Sum();
        if (used >= value.PermitLimit) return Reject(segmentWidth - now % segmentWidth);
        _segments[epoch % value.SegmentsPerWindow]++;
        return Accept(value.PermitLimit - used - 1, segmentWidth - now % segmentWidth);
    }

    private RateLimitLease Token(TokenBucketAdmissionEntry value, long now)
    {
        var period = (long)value.ReplenishmentPeriod.TotalMilliseconds;
        if (_lastRefill == 0) { _lastRefill = now; _tokens = value.TokenLimit; }
        var elapsed = Math.Max(0, now - _lastRefill);
        UInt128 numerator = (UInt128)(ulong)elapsed * (ulong)value.TokensPerPeriod + (ulong)_remainder;
        var added = (long)UInt128.Min(numerator / (ulong)period, (UInt128)(ulong)value.TokenLimit);
        _remainder = (long)(numerator % (ulong)period);
        if (added > 0) { _tokens = Math.Min(value.TokenLimit, _tokens + added); _lastRefill = now; if (_tokens == value.TokenLimit) _remainder = 0; }
        if (_tokens == 0) return Reject(Math.Max(1, (period - _remainder + value.TokensPerPeriod - 1) / value.TokensPerPeriod));
        _tokens--;
        return Accept(_tokens, _tokens == value.TokenLimit ? 0 : period);
    }

    private static RateLimitLease Accept(long remaining, long resetMs) => new GatewayAdmissionLease(true, null, metadata: new Dictionary<string, object?> { ["Remaining"] = remaining, ["ResetAfterMilliseconds"] = Math.Max(0, resetMs) });
    private static RateLimitLease Reject(long retryMs) => new GatewayAdmissionLease(false, "LimitExceeded", metadata: new Dictionary<string, object?> { ["RetryAfter"] = TimeSpan.FromMilliseconds(Math.Max(1, retryMs)), ["RetryAfterMilliseconds"] = Math.Max(1, retryMs), ["ResetAfterMilliseconds"] = Math.Max(1, retryMs) });
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
}
