using FluentAssertions;
using System.Net;
using HPD.Gateway;
using HPD.Gateway.Admission.Redis;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using Xunit;

namespace HPD.Gateway.Tests;

public sealed class GatewayRedisAdmissionTests
{
    [Fact]
    public void Options_are_snapshotted_bounded_secret_free_and_registration_is_atomic()
    {
        var options = new GatewayRedisAdmissionOptions
        {
            AuthorityId = "deployment-a",
            Configuration = "localhost:6379,password=secret,abortConnect=false",
        };
        GatewayRedisAdmissionSnapshot snapshot = GatewayRedisAdmissionSnapshot.Create("redis", options);
        options.AuthorityId = "changed";
        options.Configuration = "invalid";
        snapshot.AuthorityId.Should().Be("deployment-a");
        snapshot.BehaviorIdentity.Value.Should().MatchRegex("^[0-9a-f]{64}$");
        snapshot.ToString().Should().NotContain("secret");

        foreach (Action<GatewayRedisAdmissionOptions> invalid in new Action<GatewayRedisAdmissionOptions>[]
        {
            value => { value.Configuration = null; value.ConnectionKey = null; },
            value => value.ConnectionKey = "also",
            value => value.KeyPrefix = "bad{tag}",
            value => value.OperationTimeout = TimeSpan.FromTicks(1),
            value => value.MaximumConcurrentInvocations = 4_097,
        })
        {
            var builder = new GatewayTrafficAdmissionRegistryBuilder();
            FluentActions.Invoking(() => builder.UseRedis("redis", value =>
            {
                value.AuthorityId = "deployment-a";
                value.Configuration = "localhost:6379";
                invalid(value);
            })).Should().Throw<ArgumentException>();
            builder.Build().Capabilities.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task Keyed_host_connection_is_required_exactly_and_is_not_owned()
    {
        string? endpoint = Environment.GetEnvironmentVariable("HPD_GATEWAY_REDIS");
        if (endpoint is null) return;
        using IConnectionMultiplexer connection = await ConnectionMultiplexer.ConnectAsync(endpoint);
        var services = new ServiceCollection();
        services.AddKeyedSingleton<IConnectionMultiplexer>("authority", connection);
        var builder = new GatewayTrafficAdmissionRegistryBuilder(services);
        builder.UseRedis("redis", options =>
        {
            options.AuthorityId = "deployment-a";
            options.ConnectionKey = "authority";
        });
        builder.AddSharedFixedWindow("shared", "redis");
        using GatewayTrafficAdmissionRegistry registry = builder.Build();
        registry.Dispose();
        connection.IsConnected.Should().BeTrue();
    }

    [Theory]
    [InlineData(TrafficAdmissionRateAlgorithm.FixedWindow, 0, 0)]
    [InlineData(TrafficAdmissionRateAlgorithm.SlidingWindow, 0, 2)]
    [InlineData(TrafficAdmissionRateAlgorithm.TokenBucket, 1, 0)]
    public async Task Real_redis_executes_every_canonical_algorithm_and_retained_state(
        TrafficAdmissionRateAlgorithm algorithm,
        long tokens,
        int segments)
    {
        string? endpoint = Environment.GetEnvironmentVariable("HPD_GATEWAY_REDIS");
        if (endpoint is null) return;
        string profile = $"p-{(int)algorithm}-{Guid.NewGuid():N}";
        GatewayRedisAdmissionSnapshot snapshot = GatewayRedisAdmissionSnapshot.Create("redis", new GatewayRedisAdmissionOptions
        {
            AuthorityId = "deployment-a",
            Configuration = endpoint,
            KeyPrefix = "hpd:test:admission",
        });
        using var provider = new GatewayRedisAdmissionProvider(snapshot, null);
        var request = new GatewaySharedAdmissionRequest(1, "redis", "deployment-a", profile,
            new ContentHash("sha-256", new string('a', 64)), "partition", algorithm, 1, tokens, 1_000, segments, 1,
            new string('b', 32));
        GatewaySharedAdmissionDecision first = await provider.AcquireAsync(request, default);
        GatewaySharedAdmissionDecision second = await provider.AcquireAsync(request with { AttemptId = new string('c', 32) }, default);
        first.Kind.Should().Be(GatewaySharedAdmissionDecisionKind.Acquired);
        second.Kind.Should().Be(GatewaySharedAdmissionDecisionKind.Rejected);
        GatewaySharedAdmissionRetainedState state = await provider.ObserveStateAsync(request, default);
        GatewaySharedAdmissionContract.IsValidState(request, state).Should().BeTrue();

        GatewaySharedAdmissionDecision conflict = await provider.AcquireAsync(request with
        {
            BehaviorIdentity = new ContentHash("sha-256", new string('d', 64)),
            AttemptId = new string('e', 32)
        }, default);
        conflict.Kind.Should().Be(GatewaySharedAdmissionDecisionKind.ConfigurationConflict);
        provider.GetSnapshot().ConfigurationConflicts.Should().Be(1);
    }

    [Fact]
    public async Task Real_redis_concurrency_is_atomic_cluster_safe_and_recovers_script_cache()
    {
        string? endpoint = Environment.GetEnvironmentVariable("HPD_GATEWAY_REDIS");
        if (endpoint is null) return;
        ConfigurationOptions configuration = ConfigurationOptions.Parse(endpoint);
        configuration.AllowAdmin = true;
        using IConnectionMultiplexer connection = await ConnectionMultiplexer.ConnectAsync(configuration);
        GatewayRedisAdmissionSnapshot snapshot = GatewayRedisAdmissionSnapshot.Create("redis", new GatewayRedisAdmissionOptions
        {
            AuthorityId = "deployment-a",
            ConnectionKey = "host",
            KeyPrefix = "hpd:test:atomic",
            MaximumConcurrentInvocations = 128,
        });
        using var provider = new GatewayRedisAdmissionProvider(snapshot, connection);
        string profile = $"atomic-{Guid.NewGuid():N}";
        var request = new GatewaySharedAdmissionRequest(1, "redis", "deployment-a", profile,
            new ContentHash("sha-256", new string('a', 64)), "tenant-a", TrafficAdmissionRateAlgorithm.FixedWindow,
            40, 0, 60_000, 0, 1, new string('b', 32));
        GatewaySharedAdmissionDecision[] decisions = await Task.WhenAll(Enumerable.Range(0, 100).Select(index =>
            provider.AcquireAsync(request with { AttemptId = index.ToString("x32") }, default).AsTask()));
        decisions.Count(static value => value.Kind == GatewaySharedAdmissionDecisionKind.Acquired).Should().Be(40);
        decisions.Count(static value => value.Kind == GatewaySharedAdmissionDecisionKind.Rejected).Should().Be(60);

        foreach (EndPoint server in connection.GetEndPoints())
            try { await connection.GetServer(server).ScriptFlushAsync(); } catch (RedisServerException) { }
        GatewaySharedAdmissionDecision afterFlush = await provider.AcquireAsync(request with { AttemptId = new string('f', 32) }, default);
        afterFlush.Kind.Should().Be(GatewaySharedAdmissionDecisionKind.Rejected);
        GatewaySharedAdmissionRetainedState state = await provider.ObserveStateAsync(request, default);
        state.Used.Should().Be(40);
    }

    [Fact]
    public async Task Real_redis_bounds_sliding_state_and_cluster_partition_distribution()
    {
        string? endpoint = Environment.GetEnvironmentVariable("HPD_GATEWAY_REDIS");
        if (endpoint is null) return;
        using IConnectionMultiplexer connection = await ConnectionMultiplexer.ConnectAsync(endpoint);
        GatewayRedisAdmissionSnapshot snapshot = GatewayRedisAdmissionSnapshot.Create("redis", new GatewayRedisAdmissionOptions
        {
            AuthorityId = "deployment-a",
            ConnectionKey = "host",
            KeyPrefix = "hpd:test:bounds",
        });
        using var provider = new GatewayRedisAdmissionProvider(snapshot, connection);
        string profile = $"bounds-{Guid.NewGuid():N}";
        var request = new GatewaySharedAdmissionRequest(1, "redis", "deployment-a", profile,
            new ContentHash("sha-256", new string('a', 64)), "partition-0", TrafficAdmissionRateAlgorithm.SlidingWindow,
            100_000_000, 0, 64_000, 64, 1, new string('b', 32));

        (await provider.AcquireAsync(request, default)).Kind.Should().Be(GatewaySharedAdmissionDecisionKind.Acquired);
        long fields = await connection.GetDatabase(snapshot.Database).HashLengthAsync(provider.BuildKey(request));
        fields.Should().BeLessThanOrEqualTo(136, "the 64-segment algorithm retains a fixed bounded hash");

        if (connection.GetEndPoints().Any(endpoint => connection.GetServer(endpoint).ServerType == ServerType.Cluster))
        {
            var slots = Enumerable.Range(0, 64)
                .Select(index => connection.GetHashSlot(provider.BuildKey(request with { PartitionKey = $"partition-{index}" })))
                .Distinct()
                .ToArray();
            slots.Should().HaveCountGreaterThan(8, "partition hash tags must distribute independently across a Redis Cluster");
        }

        var invalid = request with { PartitionKey = "partition-invalid", SegmentsPerWindow = 65, AttemptId = new string('c', 32) };
        (await provider.AcquireAsync(invalid, default)).Kind.Should().Be(GatewaySharedAdmissionDecisionKind.UnavailableBeforePossibleCommit);
        (await connection.GetDatabase(snapshot.Database).KeyExistsAsync(provider.BuildKey(invalid))).Should().BeFalse();
    }
}
