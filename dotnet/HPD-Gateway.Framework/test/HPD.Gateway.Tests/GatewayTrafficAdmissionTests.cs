using System.Collections.Immutable;
using System.Security.Claims;
using FluentAssertions;
using HPD.Gateway;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HPD.Gateway.Tests;

public sealed class GatewayTrafficAdmissionTests
{
    [Fact]
    public async Task Preliminary_attempt_never_consumes_and_authoritative_acquire_runs_once()
    {
        var (limiter, context) = Create(
            builder => builder.AddLocalFixedWindow("rate"),
            new FixedWindowAdmissionEntry { Profile = "rate", PermitLimit = 1, Window = TimeSpan.FromMinutes(1) });

        for (var index = 0; index < 10; index++) limiter.AttemptAcquire(context).IsAcquired.Should().BeFalse();
        using var first = await limiter.AcquireAsync(context);
        using var second = await limiter.AcquireAsync(context);
        first.IsAcquired.Should().BeTrue();
        second.IsAcquired.Should().BeFalse();
    }

    [Fact]
    public async Task Concurrency_lease_is_reversible_and_queue_zero_rejects()
    {
        var (limiter, context) = Create(
            builder => builder.AddLocalConcurrency("guard"),
            new ConcurrencyAdmissionEntry { Profile = "guard", PermitLimit = 1, QueueLimit = 0 });
        var first = await limiter.AcquireAsync(context);
        using var rejected = await limiter.AcquireAsync(context);
        first.IsAcquired.Should().BeTrue();
        rejected.IsAcquired.Should().BeFalse();
        first.Dispose();
        using var recovered = await limiter.AcquireAsync(context);
        recovered.IsAcquired.Should().BeTrue();
    }

    [Fact]
    public async Task Multiple_guards_use_global_profile_order_and_release_on_later_rejection()
    {
        var builder = new GatewayTrafficAdmissionRegistryBuilder();
        builder.AddLocalConcurrency("z-guard").AddLocalConcurrency("a-guard");
        var registry = builder.Build();
        registry.Capabilities.Where(static value => value.Kind == TrafficAdmissionKind.Concurrency)
            .OrderBy(static value => value.AcquisitionOrdinal).Select(static value => value.Name)
            .Should().Equal("a-guard", "z-guard");
        var plan = new TrafficAdmissionPlan { Entries =
        [
            new ConcurrencyAdmissionEntry { Profile = "z-guard", PermitLimit = 1, QueueLimit = 0 },
            new ConcurrencyAdmissionEntry { Profile = "a-guard", PermitLimit = 1, QueueLimit = 0 }
        ]};
        var limiter = new GatewayTrafficAdmissionLimiter(registry);
        var firstContext = Context(plan);
        var secondContext = Context(plan);
        var first = await limiter.AcquireAsync(firstContext);
        using var rejected = await limiter.AcquireAsync(secondContext);
        rejected.IsAcquired.Should().BeFalse();
        first.Dispose();
        using var recovered = await limiter.AcquireAsync(secondContext);
        recovered.IsAcquired.Should().BeTrue();
    }

    [Fact]
    public async Task Sliding_window_and_token_bucket_enforce_their_local_limits()
    {
        var (sliding, slidingContext) = Create(
            builder => builder.AddLocalSlidingWindow("sliding"),
            new SlidingWindowAdmissionEntry { Profile = "sliding", PermitLimit = 2, Window = TimeSpan.FromSeconds(2), SegmentsPerWindow = 2 });
        (await sliding.AcquireAsync(slidingContext)).IsAcquired.Should().BeTrue();
        (await sliding.AcquireAsync(slidingContext)).IsAcquired.Should().BeTrue();
        (await sliding.AcquireAsync(slidingContext)).IsAcquired.Should().BeFalse();

        var (token, tokenContext) = Create(
            builder => builder.AddLocalTokenBucket("token", options => options.MinimumPeriod = TimeSpan.FromMilliseconds(100)),
            new TokenBucketAdmissionEntry { Profile = "token", TokenLimit = 2, TokensPerPeriod = 1, ReplenishmentPeriod = TimeSpan.FromSeconds(1) });
        (await token.AcquireAsync(tokenContext)).IsAcquired.Should().BeTrue();
        (await token.AcquireAsync(tokenContext)).IsAcquired.Should().BeTrue();
        (await token.AcquireAsync(tokenContext)).IsAcquired.Should().BeFalse();
    }

    [Fact]
    public async Task Identity_partition_requires_established_authentication_truth()
    {
        var (limiter, context) = Create(
            builder => builder.AddLocalFixedWindow("subject", options => options.Partition = TrafficAdmissionPartitionKind.AuthenticatedSubject),
            new FixedWindowAdmissionEntry { Profile = "subject", PermitLimit = 1, Window = TimeSpan.FromMinutes(1) });
        (await limiter.AcquireAsync(context)).IsAcquired.Should().BeFalse();
        context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "alice")], "test"));
        (await limiter.AcquireAsync(context)).IsAcquired.Should().BeTrue();
    }

    [Fact]
    public void Registry_snapshots_options_and_assigns_deterministic_identity()
    {
        var options = new GatewayLocalAdmissionOptions();
        var builder = new GatewayTrafficAdmissionRegistryBuilder();
        builder.AddLocalFixedWindow("rate", value => { value.MaximumLimit = options.MaximumLimit; value.Partition = options.Partition; });
        var first = builder.Build().Capabilities.Single();
        options.MaximumLimit = 1;
        var secondBuilder = new GatewayTrafficAdmissionRegistryBuilder();
        secondBuilder.AddLocalFixedWindow("rate");
        var second = secondBuilder.Build().Capabilities.Single();
        first.Should().Be(second);
        first.BehaviorIdentity.Value.Should().MatchRegex("^[0-9a-f]{64}$");
    }

    [Fact]
    public async Task Aspnet_two_phase_flow_executes_gateway_plan_once_and_leaves_unselected_endpoints_alone()
    {
        var registryBuilder = new GatewayTrafficAdmissionRegistryBuilder();
        registryBuilder.AddLocalFixedWindow("rate");
        using var registry = registryBuilder.Build();
        var plan = new TrafficAdmissionPlan
        {
            Entries = [new FixedWindowAdmissionEntry { Profile = "rate", PermitLimit = 1, Window = TimeSpan.FromMinutes(1) }]
        };
        var identity = GatewayRuntimePlanner.HashTrafficAdmission(plan);
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddRateLimiter(static _ => { });
        await using var application = builder.Build();
        application.UseRateLimiter(GatewayTrafficAdmissionMiddleware.CreateOptions(registry));
        application.MapGet("/governed", static () => Results.Ok()).WithMetadata(new GatewayTrafficAdmissionMetadata(
            "application", new ContentHash("sha-256", new string('a', 64)), new RouteId("route"), identity, plan));
        application.MapGet("/ordinary", static () => Results.Ok());
        await application.StartAsync();

        using var client = application.GetTestClient();
        (await client.GetAsync("/governed")).StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        (await client.GetAsync("/governed")).StatusCode.Should().Be(System.Net.HttpStatusCode.TooManyRequests);
        (await client.GetAsync("/ordinary")).StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        (await client.GetAsync("/ordinary")).StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
    }

    [Fact]
    public void Legacy_string_policy_surface_and_wire_shape_are_absent()
    {
        typeof(GatewayBuilder).Assembly.GetType("HPD.Gateway.TrafficAdmissionBinding").Should().BeNull();
        typeof(GatewayBuilder).GetMethods().Select(static method => method.Name)
            .Should().NotContain("AddTrafficAdmissionPolicy");
        const string legacy = """
            {"schemaVersion":{"major":1,"minor":0},"canonicalizationVersion":1,"routes":[],"upstreams":[],"rootDefaults":{"trafficAdmission":{"policy":"legacy"}}}
            """;
        GatewayPortableDocumentReader.Read(System.Text.Encoding.UTF8.GetBytes(legacy))
            .IsStructurallyValid.Should().BeFalse();
    }

    private static (GatewayTrafficAdmissionLimiter Limiter, DefaultHttpContext Context) Create(
        Action<GatewayTrafficAdmissionRegistryBuilder> configure,
        TrafficAdmissionEntry entry)
    {
        var builder = new GatewayTrafficAdmissionRegistryBuilder();
        configure(builder);
        var plan = new TrafficAdmissionPlan { Entries = [entry] };
        return (new GatewayTrafficAdmissionLimiter(builder.Build()), Context(plan));
    }

    private static DefaultHttpContext Context(TrafficAdmissionPlan plan)
    {
        var context = new DefaultHttpContext();
        var identity = GatewayRuntimePlanner.HashTrafficAdmission(plan);
        context.SetEndpoint(new Endpoint(null,
            new EndpointMetadataCollection(new GatewayTrafficAdmissionMetadata(
                "application", new ContentHash("sha-256", new string('a', 64)), new RouteId("route"), identity, plan)), "gateway"));
        return context;
    }
}
