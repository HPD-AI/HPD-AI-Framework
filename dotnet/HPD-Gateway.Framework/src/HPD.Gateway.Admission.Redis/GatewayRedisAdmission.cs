using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace HPD.Gateway.Admission.Redis;

public sealed class GatewayRedisAdmissionOptions
{
    public required string AuthorityId { get; set; }
    public string? Configuration { get; set; }
    public string? ConnectionKey { get; set; }
    public string KeyPrefix { get; set; } = "hpd:gateway:admission";
    public int Database { get; set; } = -1;
    public TimeSpan OperationTimeout { get; set; } = TimeSpan.FromMilliseconds(75);
    public int MaximumConcurrentInvocations { get; set; } = 1_024;
}

public sealed record GatewayRedisAdmissionHealthSnapshot(
    string ProviderId,
    string AuthorityId,
    bool IsConnected,
    long Acquired,
    long Rejected,
    long ConfigurationConflicts,
    long Unavailable,
    long Indeterminate);

public interface IGatewayRedisAdmissionHealth
{
    GatewayRedisAdmissionHealthSnapshot GetSnapshot();
}

public static class GatewayRedisAdmissionExtensions
{
    public static GatewayTrafficAdmissionRegistryBuilder UseRedis(
        this GatewayTrafficAdmissionRegistryBuilder admission,
        string providerId,
        Action<GatewayRedisAdmissionOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(admission);
        ArgumentNullException.ThrowIfNull(configure);
        var mutable = new GatewayRedisAdmissionOptions { AuthorityId = "invalid" };
        configure(mutable);
        GatewayRedisAdmissionSnapshot snapshot = GatewayRedisAdmissionSnapshot.Create(providerId, mutable);
        IConnectionMultiplexer? hostConnection = snapshot.ConnectionKey is null
            ? null
            : ResolveKeyedConnection(admission.HostServices, snapshot.ConnectionKey);
        var provider = new GatewayRedisAdmissionProvider(snapshot, hostConnection);
        try
        {
            admission.AddOwnedSharedProvider(providerId, provider, options =>
            {
                options.AuthorityId = snapshot.AuthorityId;
                options.BehaviorIdentity = snapshot.BehaviorIdentity;
                options.OperationTimeout = snapshot.OperationTimeout;
                options.MaximumConcurrentInvocations = snapshot.MaximumConcurrentInvocations;
            });
            admission.Services.AddSingleton<IGatewayRedisAdmissionHealth>(provider);
        }
        catch
        {
            provider.Dispose();
            throw;
        }
        return admission;
    }

    private static IConnectionMultiplexer ResolveKeyedConnection(IServiceCollection services, string key)
    {
        ServiceDescriptor[] matches = services.Where(descriptor => descriptor.IsKeyedService &&
            descriptor.ServiceType == typeof(IConnectionMultiplexer) &&
            StringComparer.Ordinal.Equals(descriptor.ServiceKey, key)).ToArray();
        if (matches.Length != 1 || matches[0].KeyedImplementationInstance is not IConnectionMultiplexer connection)
            throw new InvalidOperationException("The Redis admission connection key must resolve to exactly one keyed singleton instance.");
        return connection;
    }
}

internal sealed record GatewayRedisAdmissionSnapshot(
    string ProviderId,
    string AuthorityId,
    string? Configuration,
    string? ConnectionKey,
    string KeyPrefix,
    int Database,
    TimeSpan OperationTimeout,
    int MaximumConcurrentInvocations,
    ContentHash BehaviorIdentity)
{
    public override string ToString() => $"GatewayRedisAdmissionSnapshot {{ ProviderId = {ProviderId}, AuthorityId = {AuthorityId}, ConnectionMode = {(ConnectionKey is null ? "Owned" : "HostOwned")}, KeyPrefix = {KeyPrefix}, Database = {Database}, OperationTimeout = {OperationTimeout}, MaximumConcurrentInvocations = {MaximumConcurrentInvocations}, BehaviorIdentity = {BehaviorIdentity} }}";

    internal static GatewayRedisAdmissionSnapshot Create(string providerId, GatewayRedisAdmissionOptions options)
    {
        if (!GatewayIdentifier.IsCanonical(providerId) || string.IsNullOrWhiteSpace(options.AuthorityId) ||
            options.AuthorityId.Length > 256 || options.AuthorityId.Any(char.IsControl) ||
            (string.IsNullOrWhiteSpace(options.Configuration) == string.IsNullOrWhiteSpace(options.ConnectionKey)) ||
            options.ConnectionKey is { } connectionKey && (!GatewayIdentifier.IsCanonical(connectionKey) || connectionKey.Length > 128) ||
            string.IsNullOrWhiteSpace(options.KeyPrefix) || options.KeyPrefix.Length > 128 ||
            options.KeyPrefix.Any(static value => value is < '!' or > '~' or '{' or '}') ||
            options.Database is < -1 or > 15 || options.OperationTimeout < TimeSpan.FromMilliseconds(1) ||
            options.OperationTimeout > TimeSpan.FromSeconds(30) ||
            options.OperationTimeout.Ticks % TimeSpan.TicksPerMillisecond != 0 ||
            options.MaximumConcurrentInvocations is < 1 or > 4_096)
            throw new ArgumentException("Redis admission options are invalid or unbounded.", nameof(options));

        string? configuration = null;
        if (options.Configuration is { } configured)
        {
            ConfigurationOptions parsed;
            try { parsed = ConfigurationOptions.Parse(configured); }
            catch (Exception exception) { throw new ArgumentException("Redis admission configuration is invalid.", nameof(options), exception); }
            parsed.AbortOnConnectFail = false;
            parsed.AllowAdmin = false;
            configuration = parsed.ToString(includePassword: true);
        }

        var identityText = string.Join('|', "hpd.gateway.redis-admission/v1", providerId, options.AuthorityId,
            options.KeyPrefix, options.Database, options.OperationTimeout.Ticks, options.MaximumConcurrentInvocations,
            GatewayRedisScripts.AcquireSha256, GatewayRedisScripts.ObserveSha256);
        var identity = new ContentHash("sha-256", Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(identityText))));
        return new(providerId, options.AuthorityId, configuration, options.ConnectionKey, options.KeyPrefix,
            options.Database, options.OperationTimeout, options.MaximumConcurrentInvocations, identity);
    }
}

internal sealed class GatewayRedisAdmissionProvider : IGatewaySharedAdmissionCertificationAuthority, IGatewayRedisAdmissionHealth, IDisposable
{
    private readonly GatewayRedisAdmissionSnapshot _snapshot;
    private readonly IConnectionMultiplexer? _hostConnection;
    private readonly SemaphoreSlim _connectionGate = new(1, 1);
    private readonly object _connectionSync = new();
    private IConnectionMultiplexer? _ownedConnection;
    private long _acquired;
    private long _rejected;
    private long _conflicts;
    private long _unavailable;
    private long _indeterminate;
    private int _disposed;

    internal GatewayRedisAdmissionProvider(GatewayRedisAdmissionSnapshot snapshot, IConnectionMultiplexer? hostConnection)
    {
        _snapshot = snapshot;
        _hostConnection = hostConnection;
    }

    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties, typeof(GatewayRedisScriptParameters))]
    public async ValueTask<GatewaySharedAdmissionDecision> AcquireAsync(
        GatewaySharedAdmissionRequest request,
        CancellationToken cancellationToken)
    {
        if (!GatewaySharedAdmissionContract.IsValidRequest(request) || request.ProviderId != _snapshot.ProviderId ||
            request.AuthorityId != _snapshot.AuthorityId)
            return Unavailable("redis-request-invalid");
        if (Volatile.Read(ref _disposed) != 0) return Unavailable("redis-provider-disposed");

        IConnectionMultiplexer connection;
        try { connection = await GetConnectionAsync(cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new(GatewaySharedAdmissionDecisionKind.CanceledBeforeDispatch, null, null, null, null, "redis-connect-canceled");
        }
        catch { return Unavailable("redis-connect-unavailable"); }
        if (!connection.IsConnected) return Unavailable("redis-not-connected");
        if (cancellationToken.IsCancellationRequested)
            return new(GatewaySharedAdmissionDecisionKind.CanceledBeforeDispatch, null, null, null, null, "redis-operation-canceled");

        RedisKey key = BuildKey(request);
        var parameters = new GatewayRedisScriptParameters
        {
            Key = key,
            Behavior = request.BehaviorIdentity.Value,
            Algorithm = (int)request.Algorithm,
            Limit = request.PermitLimit,
            Tokens = request.TokensPerPeriod,
            Window = request.WindowMilliseconds,
            Segments = request.SegmentsPerWindow,
            Permits = request.PermitCount,
        };
        try
        {
            IDatabase database = connection.GetDatabase(_snapshot.Database);
            RedisResult result = await database.ScriptEvaluateAsync(GatewayRedisScripts.Acquire, parameters).WaitAsync(cancellationToken).ConfigureAwait(false);
            GatewaySharedAdmissionDecision decision = ParseDecision(result);
            switch (decision.Kind)
            {
                case GatewaySharedAdmissionDecisionKind.Acquired: Interlocked.Increment(ref _acquired); break;
                case GatewaySharedAdmissionDecisionKind.Rejected: Interlocked.Increment(ref _rejected); break;
                case GatewaySharedAdmissionDecisionKind.ConfigurationConflict: Interlocked.Increment(ref _conflicts); break;
            }
            return GatewaySharedAdmissionContract.IsValidDecision(request, decision)
                ? decision
                : Indeterminate("redis-result-invalid");
        }
        catch (OperationCanceledException) { return Indeterminate("redis-outcome-indeterminate"); }
        catch (RedisServerException exception) when (exception.Message.StartsWith("HPD_CONFLICT", StringComparison.Ordinal))
        {
            Interlocked.Increment(ref _conflicts);
            return new(GatewaySharedAdmissionDecisionKind.ConfigurationConflict, null, null, null, null, "redis-configuration-conflict");
        }
        catch { return Indeterminate("redis-operation-indeterminate"); }
    }

    public async ValueTask<GatewaySharedAdmissionRetainedState> ObserveStateAsync(
        GatewaySharedAdmissionRequest request,
        CancellationToken cancellationToken)
    {
        if (!GatewaySharedAdmissionContract.IsValidRequest(request)) throw new ArgumentException("Certification request is invalid.", nameof(request));
        IConnectionMultiplexer connection = await GetConnectionAsync(cancellationToken).ConfigureAwait(false);
        HashEntry[] entries = await connection.GetDatabase(_snapshot.Database).HashGetAllAsync(BuildKey(request)).WaitAsync(cancellationToken).ConfigureAwait(false);
        var state = entries.ToDictionary(static entry => entry.Name.ToString(), static entry => entry.Value.ToString(), StringComparer.Ordinal);
        return ParseState(request, state);
    }

    public GatewayRedisAdmissionHealthSnapshot GetSnapshot() => new(_snapshot.ProviderId, _snapshot.AuthorityId,
        (_hostConnection ?? _ownedConnection)?.IsConnected == true, Interlocked.Read(ref _acquired),
        Interlocked.Read(ref _rejected), Interlocked.Read(ref _conflicts), Interlocked.Read(ref _unavailable),
        Interlocked.Read(ref _indeterminate));

    private async ValueTask<IConnectionMultiplexer> GetConnectionAsync(CancellationToken cancellationToken)
    {
        if (_hostConnection is not null) return _hostConnection;
        if (Volatile.Read(ref _ownedConnection) is { } existing) return existing;
        await _connectionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_ownedConnection is not null) return _ownedConnection;
            if (Volatile.Read(ref _disposed) != 0) throw new ObjectDisposedException(nameof(GatewayRedisAdmissionProvider));
            IConnectionMultiplexer created = await ConnectionMultiplexer.ConnectAsync(_snapshot.Configuration!);
            lock (_connectionSync)
            {
                if (Volatile.Read(ref _disposed) != 0)
                {
                    created.Dispose();
                    throw new ObjectDisposedException(nameof(GatewayRedisAdmissionProvider));
                }
                _ownedConnection = created;
                return created;
            }
        }
        finally { _connectionGate.Release(); }
    }

    internal RedisKey BuildKey(GatewaySharedAdmissionRequest request)
    {
        string authority = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(request.AuthorityId)));
        string partition = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(request.PartitionKey)));
        return $"{_snapshot.KeyPrefix}:v1:{authority}:{request.Profile}:{{{partition}}}:state";
    }

    private static GatewaySharedAdmissionDecision ParseDecision(RedisResult result)
    {
        RedisResult[] values = (RedisResult[]?)result ?? throw new InvalidOperationException("Redis result is not an array.");
        if (values.Length != 6) throw new InvalidOperationException("Redis result has an invalid field count.");
        int kind = ParseInt(values[0]);
        if (kind == 2)
            return new(GatewaySharedAdmissionDecisionKind.ConfigurationConflict, null, null, null, null, "redis-configuration-conflict");
        long remaining = ParseLong(values[1]);
        long retry = ParseLong(values[2]);
        long reset = ParseLong(values[3]);
        long observed = ParseLong(values[4]);
        long expiry = ParseLong(values[5]);
        if (reset < 1 || checked(observed + reset) != expiry)
            throw new InvalidOperationException("Redis result timing fields are inconsistent.");
        string observation = observed.ToString(CultureInfo.InvariantCulture);
        return kind switch
        {
            0 => new(GatewaySharedAdmissionDecisionKind.Acquired, remaining, null, reset, observation, null),
            1 => new(GatewaySharedAdmissionDecisionKind.Rejected, remaining, retry, reset, observation, null),
            _ => throw new InvalidOperationException("Redis result kind is invalid."),
        };
    }

    private static GatewaySharedAdmissionRetainedState ParseState(GatewaySharedAdmissionRequest request, Dictionary<string, string> state)
    {
        long last = Field("last"), expiry = Field("expiry");
        var segments = ImmutableArray.CreateBuilder<GatewaySharedAdmissionSegmentState>();
        for (var index = 0; index < request.SegmentsPerWindow; index++)
        {
            if (state.TryGetValue($"e{index}", out string? epoch) && state.TryGetValue($"c{index}", out string? count) && Parse(count) > 0)
                segments.Add(new(Parse(epoch), Parse(count)));
        }
        return request.Algorithm switch
        {
            TrafficAdmissionRateAlgorithm.FixedWindow => new(1, request.Algorithm, last, Field("windowStart"), Field("used"), null, null, null, [], expiry),
            TrafficAdmissionRateAlgorithm.SlidingWindow => new(1, request.Algorithm, last, null, null, null, null, null,
                segments.OrderBy(static value => value.Epoch).ToImmutableArray(), expiry),
            TrafficAdmissionRateAlgorithm.TokenBucket => new(1, request.Algorithm, last, null, null, Field("available"), Field("refill"), Field("remainder"), [], expiry),
            _ => throw new InvalidOperationException(),
        };

        long Field(string name) => state.TryGetValue(name, out string? value) ? Parse(value) : throw new InvalidOperationException($"Redis state field '{name}' is absent.");
        static long Parse(string value) => long.Parse(value, NumberStyles.None, CultureInfo.InvariantCulture);
    }

    private static int ParseInt(RedisResult result) => checked((int)ParseLong(result));
    private static long ParseLong(RedisResult result) => long.Parse(result.ToString() ?? "", NumberStyles.Integer, CultureInfo.InvariantCulture);

    private GatewaySharedAdmissionDecision Unavailable(string code)
    {
        Interlocked.Increment(ref _unavailable);
        return new(GatewaySharedAdmissionDecisionKind.UnavailableBeforePossibleCommit, null, null, null, null, code);
    }

    private GatewaySharedAdmissionDecision Indeterminate(string code)
    {
        Interlocked.Increment(ref _indeterminate);
        return new(GatewaySharedAdmissionDecisionKind.IndeterminateAfterPossibleCommit, null, null, null, null, code);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        if (_hostConnection is null)
        {
            lock (_connectionSync)
            {
                _ownedConnection?.Dispose();
                _ownedConnection = null;
            }
        }
    }
}

internal sealed class GatewayRedisScriptParameters
{
    public required RedisKey Key { get; init; }
    public required RedisValue Behavior { get; init; }
    public required RedisValue Algorithm { get; init; }
    public required RedisValue Limit { get; init; }
    public required RedisValue Tokens { get; init; }
    public required RedisValue Window { get; init; }
    public required RedisValue Segments { get; init; }
    public required RedisValue Permits { get; init; }
}
