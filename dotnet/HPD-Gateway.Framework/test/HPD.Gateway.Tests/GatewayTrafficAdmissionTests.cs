using System.Collections.Immutable;
using System.Security.Claims;
using System.Net;
using System.Diagnostics;
using FluentAssertions;
using HPD.Gateway;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using System.Threading.RateLimiting;
using Xunit;
using Yarp.ReverseProxy.Configuration;

namespace HPD.Gateway.Tests;

public sealed class GatewayTrafficAdmissionTests
{
    [Fact]
    public void Native_route_metadata_is_complete_hash_bound_and_not_publicly_forgeable()
    {
        TrafficAdmissionPlan plan = new()
        {
            Entries = [new FixedWindowAdmissionEntry { Profile = "rate", PermitLimit = 10, Window = TimeSpan.FromSeconds(1) }]
        };
        ContentHash planIdentity = GatewayRuntimePlanner.HashTrafficAdmission(plan);
        var metadata = ImmutableDictionary<string, string>.Empty
            .Add(GatewayRuntimePlanner.ApplicationIdMetadata, new string('a', 32))
            .Add(GatewayRuntimePlanner.SymbolicPlanIdentityMetadata, new string('b', 64))
            .Add(GatewayTrafficAdmissionMetadataCodec.Plan, GatewayTrafficAdmissionMetadataCodec.Encode(plan))
            .Add(GatewayTrafficAdmissionMetadataCodec.PlanIdentity, planIdentity.Value);
        GatewayTrafficAdmissionMetadataCodec.ValidateRoute(new RouteConfig { RouteId = "route", ClusterId = "upstream", Metadata = metadata })
            .Should().BeTrue();
        GatewayTrafficAdmissionMetadataCodec.ValidateRoute(new RouteConfig
        {
            RouteId = "route", ClusterId = "upstream", Metadata = metadata.SetItem(GatewayTrafficAdmissionMetadataCodec.PlanIdentity, new string('c', 64))
        }).Should().BeFalse();
        GatewayTrafficAdmissionMetadataCodec.ValidateRoute(new RouteConfig
        {
            RouteId = "route", ClusterId = "upstream", Metadata = metadata.Remove(GatewayTrafficAdmissionMetadataCodec.PlanIdentity)
        }).Should().BeFalse();
        typeof(GatewayTrafficAdmissionMetadata).GetConstructors(
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public).Should().BeEmpty();
        typeof(GatewayTrafficAdmissionMetadata).GetProperties(
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)
            .Should().HaveCount(5).And.OnlyContain(static property => property.SetMethod == null);
    }

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
            builder => builder
                .AddPartitionProjector("subject-projector", Hash('b'), new SubjectProjector())
                .AddLocalFixedWindow("subject", options =>
                {
                    options.Partition = TrafficAdmissionPartitionKind.AuthenticatedSubject;
                    options.PartitionProjector = "subject-projector";
                }),
            new FixedWindowAdmissionEntry { Profile = "subject", PermitLimit = 1, Window = TimeSpan.FromMinutes(1) });
        (await limiter.AcquireAsync(context)).IsAcquired.Should().BeFalse();
        context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "alice")], "test"));
        (await limiter.AcquireAsync(context)).IsAcquired.Should().BeTrue();
    }

    [Fact]
    public async Task Later_guard_or_rate_rejection_releases_every_earlier_reversible_guard()
    {
        var builder = new GatewayTrafficAdmissionRegistryBuilder();
        builder.AddLocalConcurrency("a-guard").AddLocalConcurrency("b-guard").AddLocalFixedWindow("rate");
        var registry = builder.Build();
        var limiter = new GatewayTrafficAdmissionLimiter(registry);
        var guardA = new ConcurrencyAdmissionEntry { Profile = "a-guard", PermitLimit = 1, QueueLimit = 0 };
        var guardB = new ConcurrencyAdmissionEntry { Profile = "b-guard", PermitLimit = 1, QueueLimit = 0 };
        var rate = new FixedWindowAdmissionEntry { Profile = "rate", PermitLimit = 1, Window = TimeSpan.FromMinutes(1) };

        var heldB = await limiter.AcquireAsync(Context(new TrafficAdmissionPlan { Entries = [guardB] }));
        using (var rejected = await limiter.AcquireAsync(Context(new TrafficAdmissionPlan { Entries = [guardA, guardB] })))
            rejected.IsAcquired.Should().BeFalse();
        using (var recoveredA = await limiter.AcquireAsync(Context(new TrafficAdmissionPlan { Entries = [guardA] })))
            recoveredA.IsAcquired.Should().BeTrue();
        heldB.Dispose();

        (await limiter.AcquireAsync(Context(new TrafficAdmissionPlan { Entries = [rate] }))).Dispose();
        using (var rejected = await limiter.AcquireAsync(Context(new TrafficAdmissionPlan { Entries = [guardA, rate] })))
            rejected.IsAcquired.Should().BeFalse();
        using var recoveredAfterRate = await limiter.AcquireAsync(Context(new TrafficAdmissionPlan { Entries = [guardA] }));
        recoveredAfterRate.IsAcquired.Should().BeTrue();
    }

    [Fact]
    public void Canonical_local_state_clamps_time_and_returns_exact_sliding_and_token_facts()
    {
        var fixedState = new GatewayLocalRateState();
        var fixedEntry = new FixedWindowAdmissionEntry { Profile = "fixed", PermitLimit = 1, Window = TimeSpan.FromSeconds(1) };
        fixedState.Acquire(fixedEntry, 1_100).IsAcquired.Should().BeTrue();
        var backward = fixedState.Acquire(fixedEntry, 900);
        backward.IsAcquired.Should().BeFalse();
        Fact(backward, GatewayAdmissionMetadata.Remaining).Should().Be(0);
        Fact(backward, GatewayAdmissionMetadata.RetryAfterMilliseconds).Should().Be(900);

        var slidingState = new GatewayLocalRateState();
        var sliding = new SlidingWindowAdmissionEntry { Profile = "sliding", PermitLimit = 2, Window = TimeSpan.FromSeconds(1), SegmentsPerWindow = 2 };
        slidingState.Acquire(sliding, 0).IsAcquired.Should().BeTrue();
        slidingState.Acquire(sliding, 500).IsAcquired.Should().BeTrue();
        var slidingRejected = slidingState.Acquire(sliding, 500);
        Fact(slidingRejected, GatewayAdmissionMetadata.Remaining).Should().Be(0);
        Fact(slidingRejected, GatewayAdmissionMetadata.RetryAfterMilliseconds).Should().Be(500);
        Fact(slidingRejected, GatewayAdmissionMetadata.ResetAfterMilliseconds).Should().Be(1_000);

        var tokenState = new GatewayLocalRateState();
        var token = new TokenBucketAdmissionEntry { Profile = "token", TokenLimit = 1, TokensPerPeriod = 1, ReplenishmentPeriod = TimeSpan.FromSeconds(1) };
        tokenState.Acquire(token, 1_000).IsAcquired.Should().BeTrue();
        Fact(tokenState.Acquire(token, 1_100), GatewayAdmissionMetadata.RetryAfterMilliseconds).Should().Be(900);
        Fact(tokenState.Acquire(token, 1_200), GatewayAdmissionMetadata.RetryAfterMilliseconds).Should().Be(800);
        tokenState.Acquire(token, 900).IsAcquired.Should().BeFalse();
    }

    [Fact]
    public async Task Registry_runtime_uses_the_snapshotted_host_time_provider()
    {
        var time = new ManualTimeProvider(1_100);
        var (limiter, context) = Create(
            builder => builder.UseTimeProvider(time).AddLocalFixedWindow("rate"),
            new FixedWindowAdmissionEntry { Profile = "rate", PermitLimit = 1, Window = TimeSpan.FromSeconds(1) });
        (await limiter.AcquireAsync(context)).IsAcquired.Should().BeTrue();
        time.UnixMilliseconds = 900;
        using var rejected = await limiter.AcquireAsync(context);
        Fact(rejected, GatewayAdmissionMetadata.RetryAfterMilliseconds).Should().Be(900);
    }

    [Fact]
    public async Task Projector_failures_are_total_and_partition_capacity_is_atomic()
    {
        var projectorBuilder = new GatewayTrafficAdmissionRegistryBuilder();
        projectorBuilder.AddPartitionProjector("throwing", Hash('c'), new ThrowingProjector())
            .AddLocalFixedWindow("custom", options =>
            {
                options.Partition = TrafficAdmissionPartitionKind.Custom;
                options.PartitionProjector = "throwing";
            });
        var projectorLimiter = new GatewayTrafficAdmissionLimiter(projectorBuilder.Build());
        var projectorContext = Context(new TrafficAdmissionPlan { Entries = [new FixedWindowAdmissionEntry { Profile = "custom", PermitLimit = 1, Window = TimeSpan.FromMinutes(1) }] });
        projectorContext.User = new ClaimsPrincipal(new ClaimsIdentity([], "test"));
        using var failed = await projectorLimiter.AcquireAsync(projectorContext);
        failed.IsAcquired.Should().BeFalse();
        failed.TryGetMetadata(GatewayAdmissionMetadata.Outcome, out var failureOutcome).Should().BeTrue();
        failureOutcome.Should().Be(GatewayAdmissionOutcome.Infrastructure);

        var capacityBuilder = new GatewayTrafficAdmissionRegistryBuilder();
        capacityBuilder.AddLocalFixedWindow("source", options => options.Partition = TrafficAdmissionPartitionKind.SourceIp);
        using GatewayTrafficAdmissionRegistry capacityRegistry = capacityBuilder.Build();
        var capacityLimiter = new GatewayTrafficAdmissionLimiter(capacityRegistry);
        var capacityPlan = new TrafficAdmissionPlan { Entries = [new FixedWindowAdmissionEntry { Profile = "source", PermitLimit = 1, Window = TimeSpan.FromMinutes(1) }] };
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var acquisitions = Enumerable.Range(1, 4_097).Select(async index =>
        {
            await start.Task;
            var context = Context(capacityPlan);
            context.Connection.RemoteIpAddress = IPAddress.Parse($"2001:db8::{index:x}");
            return (Index: index, Lease: await capacityLimiter.AcquireAsync(context));
        }).ToArray();
        start.TrySetResult();
        var results = await Task.WhenAll(acquisitions);
        results.Count(static result => result.Lease.IsAcquired).Should().Be(4_096);
        var overflowLease = results.Single(static result => !result.Lease.IsAcquired).Lease;
        overflowLease.TryGetMetadata(GatewayAdmissionMetadata.Outcome, out var overflowOutcome).Should().BeTrue();
        overflowOutcome.Should().Be(GatewayAdmissionOutcome.Infrastructure);
        var retainedIndex = results.First(static result => result.Lease.IsAcquired).Index;
        foreach (var result in results) result.Lease.Dispose();

        var retained = Context(capacityPlan);
        retained.Connection.RemoteIpAddress = IPAddress.Parse($"2001:db8::{retainedIndex:x}");
        using var retainedRejection = await capacityLimiter.AcquireAsync(retained);
        retainedRejection.TryGetMetadata(GatewayAdmissionMetadata.Outcome, out var retainedOutcome).Should().BeTrue();
        retainedOutcome.Should().Be(GatewayAdmissionOutcome.Exhausted);

        var anotherNew = Context(capacityPlan);
        anotherNew.Connection.RemoteIpAddress = IPAddress.Parse("2001:db8::1002");
        using var newRejection = await capacityLimiter.AcquireAsync(anotherNew);
        newRejection.TryGetMetadata(GatewayAdmissionMetadata.Outcome, out var newOutcome).Should().BeTrue();
        newOutcome.Should().Be(GatewayAdmissionOutcome.Infrastructure);
        GatewayAdmissionProfileStatus capacityStatus = capacityRegistry.GetCurrent().Profiles.Single();
        capacityStatus.Acquired.Should().Be(4_096);
        capacityStatus.Rejected.Should().Be(1);
        capacityStatus.InfrastructureFailures.Should().Be(2);
    }

    [Fact]
    public void Rate_profile_registration_enforces_algorithm_specific_period_minima()
    {
        FluentActions.Invoking(() => new GatewayTrafficAdmissionRegistryBuilder()
            .AddLocalFixedWindow("fixed", options => options.MinimumPeriod = TimeSpan.FromMilliseconds(999)))
            .Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => new GatewayTrafficAdmissionRegistryBuilder()
            .AddLocalSlidingWindow("sliding", options => options.MinimumPeriod = TimeSpan.FromMilliseconds(999)))
            .Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => new GatewayTrafficAdmissionRegistryBuilder()
            .AddLocalTokenBucket("token", options => options.MinimumPeriod = TimeSpan.FromMilliseconds(99)))
            .Should().Throw<ArgumentException>();

        foreach (var registry in new[]
        {
            new GatewayTrafficAdmissionRegistryBuilder()
                .AddLocalFixedWindow("fixed", options => options.MinimumPeriod = TimeSpan.FromMilliseconds(1_000)).Build(),
            new GatewayTrafficAdmissionRegistryBuilder()
                .AddLocalSlidingWindow("sliding", options => options.MinimumPeriod = TimeSpan.FromMilliseconds(1_000)).Build(),
            new GatewayTrafficAdmissionRegistryBuilder()
                .AddLocalTokenBucket("token", options => options.MinimumPeriod = TimeSpan.FromMilliseconds(100)).Build()
        })
        {
            HostCapabilitySnapshot.Create(new HostCapabilityRegistration
            {
                InstalledFamilies = GatewayDeclarationFamilies.TrafficAdmission,
                TrafficAdmissionProfiles = registry.Capabilities
            }).TrafficAdmissionProfiles.Should().ContainSingle();
            registry.Dispose();
        }
    }

    [Fact]
    public async Task Projector_is_awaited_once_and_malformed_or_canceled_results_remain_closed()
    {
        var projector = new CountingProjector(GatewayAdmissionPartitionResult.Success("tenant-a"));
        var (limiter, context) = CreateProjected(projector);
        using (var acquired = await limiter.AcquireAsync(context))
            acquired.IsAcquired.Should().BeTrue();
        projector.Calls.Should().Be(1);
        var authenticatedContext = Context(context.GetEndpoint()!.Metadata.GetMetadata<GatewayTrafficAdmissionMetadata>()!.Plan);
        authenticatedContext.User = new ClaimsPrincipal(new ClaimsIdentity([], "test"));
        using (var acquired = await limiter.AcquireAsync(authenticatedContext))
            acquired.IsAcquired.Should().BeTrue();
        projector.Calls.Should().Be(2);

        var exact = new CountingProjector(GatewayAdmissionPartitionResult.Success(new string('x', 254) + "é"));
        var (exactLimiter, exactContext) = CreateProjected(exact);
        using (var acquired = await exactLimiter.AcquireAsync(exactContext))
            acquired.IsAcquired.Should().BeTrue();

        var malformed = new CountingProjector(GatewayAdmissionPartitionResult.Success(new string('x', 255) + "é"));
        var (malformedLimiter, malformedContext) = CreateProjected(malformed);
        using (var rejected = await malformedLimiter.AcquireAsync(malformedContext))
        {
            rejected.IsAcquired.Should().BeFalse();
            rejected.TryGetMetadata(GatewayAdmissionMetadata.Outcome, out var outcome).Should().BeTrue();
            outcome.Should().Be(GatewayAdmissionOutcome.Infrastructure);
        }

        foreach (var invalid in new GatewayAdmissionPartitionResult[]
        {
            GatewayAdmissionPartitionResult.Success(""),
            GatewayAdmissionPartitionResult.Success("e\u0301"),
            new("dual", GatewayAdmissionPartitionFailure.Invalid),
            GatewayAdmissionPartitionResult.Failed(GatewayAdmissionPartitionFailure.Unavailable),
            GatewayAdmissionPartitionResult.Failed(GatewayAdmissionPartitionFailure.Invalid),
            GatewayAdmissionPartitionResult.Failed(GatewayAdmissionPartitionFailure.Canceled)
        })
        {
            var (invalidLimiter, invalidContext) = CreateProjected(new CountingProjector(invalid));
            using var rejected = await invalidLimiter.AcquireAsync(invalidContext);
            rejected.IsAcquired.Should().BeFalse();
            rejected.TryGetMetadata(GatewayAdmissionMetadata.Outcome, out var outcome).Should().BeTrue();
            outcome.Should().Be(GatewayAdmissionOutcome.Infrastructure);
        }

        var canceled = new CancelingProjector();
        var (cancelingLimiter, cancelingContext) = CreateProjected(canceled);
        cancelingContext.User = context.User;
        using var cancellation = new CancellationTokenSource();
        var pending = cancelingLimiter.AcquireAsync(cancelingContext, 1, cancellation.Token).AsTask();
        cancellation.Cancel();
        await FluentActions.Awaiting(() => pending).Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Queue_cancellation_and_request_lifetime_release_concurrency_exactly_once()
    {
        var (limiter, context) = Create(
            builder => builder.AddLocalConcurrency("guard", options => options.MaximumQueue = 1),
            new ConcurrencyAdmissionEntry { Profile = "guard", PermitLimit = 1, QueueLimit = 1 });
        var held = await limiter.AcquireAsync(context);
        using var cancellation = new CancellationTokenSource();
        var queued = limiter.AcquireAsync(Context(new TrafficAdmissionPlan
        {
            Entries = [new ConcurrencyAdmissionEntry { Profile = "guard", PermitLimit = 1, QueueLimit = 1 }]
        }), 1, cancellation.Token).AsTask();
        cancellation.Cancel();
        await FluentActions.Awaiting(() => queued).Should().ThrowAsync<OperationCanceledException>();
        held.Dispose();
        using var recovered = await limiter.AcquireAsync(context);
        recovered.IsAcquired.Should().BeTrue();

        var registryBuilder = new GatewayTrafficAdmissionRegistryBuilder();
        registryBuilder.AddLocalConcurrency("http-guard");
        using var registry = registryBuilder.Build();
        var plan = new TrafficAdmissionPlan
        {
            Entries = [new ConcurrencyAdmissionEntry { Profile = "http-guard", PermitLimit = 1, QueueLimit = 0 }]
        };
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddRateLimiter(static _ => { });
        await using var application = builder.Build();
        application.UseRateLimiter(GatewayTrafficAdmissionMiddleware.CreateOptions(registry));
        application.MapGet("/stream", async () =>
        {
            entered.TrySetResult();
            await release.Task;
            return Results.Ok();
        }).WithMetadata(Metadata(plan));
        await application.StartAsync();
        using var client = application.GetTestClient();
        var first = client.GetAsync("/stream");
        await entered.Task;
        (await client.GetAsync("/stream")).StatusCode.Should().Be(System.Net.HttpStatusCode.TooManyRequests);
        release.TrySetResult();
        (await first).StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        (await client.GetAsync("/stream")).StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
    }

    [Fact]
    public async Task Old_and_new_immutable_plan_generations_do_not_share_changed_rate_state()
    {
        var builder = new GatewayTrafficAdmissionRegistryBuilder();
        builder.AddLocalFixedWindow("rate");
        var limiter = new GatewayTrafficAdmissionLimiter(builder.Build());
        var oldPlan = new TrafficAdmissionPlan
        {
            Entries = [new FixedWindowAdmissionEntry { Profile = "rate", PermitLimit = 1, Window = TimeSpan.FromMinutes(1) }]
        };
        var newPlan = new TrafficAdmissionPlan
        {
            Entries = [new FixedWindowAdmissionEntry { Profile = "rate", PermitLimit = 2, Window = TimeSpan.FromMinutes(1) }]
        };
        (await limiter.AcquireAsync(Context(oldPlan))).IsAcquired.Should().BeTrue();
        (await limiter.AcquireAsync(Context(oldPlan))).IsAcquired.Should().BeFalse();
        (await limiter.AcquireAsync(Context(newPlan))).IsAcquired.Should().BeTrue();
        (await limiter.AcquireAsync(Context(newPlan))).IsAcquired.Should().BeTrue();
        (await limiter.AcquireAsync(Context(newPlan))).IsAcquired.Should().BeFalse();
    }

    [Fact]
    public void Host_capability_admission_rejects_every_malformed_typed_limits_shape()
    {
        var baseline = TrafficAdmissionTestData.Capability("rate");
        TrafficAdmissionCapability[] malformed =
        [
            baseline with { ContractVersion = 2 },
            baseline with { RateAlgorithm = (TrafficAdmissionRateAlgorithm)255 },
            baseline with { Limits = null! },
            baseline with { Limits = baseline.Limits with { MinimumLimit = 0 } },
            baseline with { Limits = baseline.Limits with { MaximumLimit = 100_000_001 } },
            baseline with { Limits = baseline.Limits with { MinimumPeriod = TimeSpan.FromTicks(TimeSpan.TicksPerMillisecond + 1) } },
            baseline with { Limits = baseline.Limits with { MaximumPeriod = TimeSpan.FromDays(2) } },
            baseline with { Limits = baseline.Limits with { MinimumSegments = 2, MaximumSegments = 64 } },
            baseline with { Limits = baseline.Limits with { MaximumQueue = 1 } },
            baseline with { Kind = TrafficAdmissionKind.Concurrency, RateAlgorithm = null, AcquisitionOrdinal = 0 }
        ];

        foreach (var capability in malformed)
        {
            FluentActions.Invoking(() => HostCapabilitySnapshot.Create(new HostCapabilityRegistration
            {
                InstalledFamilies = GatewayDeclarationFamilies.TrafficAdmission,
                TrafficAdmissionProfiles = [capability]
            })).Should().Throw<ArgumentException>();
        }

        var sliding = baseline with
        {
            RateAlgorithm = TrafficAdmissionRateAlgorithm.SlidingWindow,
            Limits = baseline.Limits with { MinimumSegments = 2, MaximumSegments = 64 }
        };
        HostCapabilitySnapshot.Create(new HostCapabilityRegistration
        {
            InstalledFamilies = GatewayDeclarationFamilies.TrafficAdmission,
            TrafficAdmissionProfiles = [sliding]
        }).TrafficAdmissionProfiles.Should().ContainKey("rate");

        var concurrency = baseline with
        {
            Kind = TrafficAdmissionKind.Concurrency,
            RateAlgorithm = null,
            AcquisitionOrdinal = 0,
            Limits = new TrafficAdmissionLimits(1, 100_000_000, null, null, 0, 0, 0, 100_000)
        };
        HostCapabilitySnapshot.Create(new HostCapabilityRegistration
        {
            InstalledFamilies = GatewayDeclarationFamilies.TrafficAdmission,
            TrafficAdmissionProfiles = [concurrency]
        }).TrafficAdmissionProfiles.Should().ContainKey("rate");
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
    public void One_deployment_profile_has_one_exact_behavior_across_definitions_root_and_routes()
    {
        var registryBuilder = new GatewayTrafficAdmissionRegistryBuilder();
        registryBuilder.AddSharedProvider("provider", new AlwaysAcquiredProvider(), options =>
        {
            options.AuthorityId = "deployment-a";
            options.BehaviorIdentity = Hash('b');
        });
        registryBuilder.AddSharedFixedWindow("shared", "provider");
        using GatewayTrafficAdmissionRegistry registry = registryBuilder.Build();
        HostCapabilitySnapshot capabilities = HostCapabilitySnapshot.Create(new HostCapabilityRegistration
        {
            InstalledFamilies = GatewayDeclarationFamilies.TrafficAdmission,
            TrafficAdmissionProfiles = registry.Capabilities,
        });
        GatewayConfiguration baseline = GatewayConfigurationTests.CreateValidConfiguration();
        var shared = new TrafficAdmissionPlan
        {
            Entries = [new FixedWindowAdmissionEntry { Profile = "shared", PermitLimit = 100, Window = TimeSpan.FromMinutes(1) }]
        };
        var definitionId = new DefinitionId("shared-definition");
        var definitionReference = new DeclarationReference<TrafficAdmissionPlan> { Definition = definitionId };
        RouteDeclaration first = baseline.Routes[0] with
        {
            Declarations = new RouteDeclarations { TrafficAdmission = definitionReference }
        };
        RouteDeclaration second = baseline.Routes[0] with
        {
            Id = new RouteId("orders-secondary"),
            Match = new HttpRouteMatch { Path = "/secondary/{**catch-all}" },
            Declarations = new RouteDeclarations { TrafficAdmission = new DeclarationReference<TrafficAdmissionPlan> { Inline = shared } }
        };
        var configuration = baseline with
        {
            Definitions = new GatewayDefinitions
            {
                TrafficAdmission = [new DeclarationDefinition<TrafficAdmissionPlan> { Id = definitionId, Specification = shared }]
            },
            RootDefaults = new GatewayRootDeclarations { TrafficAdmission = definitionReference },
            Routes = [first, second],
        };

        GatewayCandidateValidator.Validate(configuration, capabilities).IsValid.Should().BeTrue();

        TrafficAdmissionPlan conflicting = shared with
        {
            Entries = [new FixedWindowAdmissionEntry { Profile = "shared", PermitLimit = 101, Window = TimeSpan.FromMinutes(1) }]
        };
        GatewayConfiguration invalid = configuration with
        {
            Routes = [first, second with
            {
                Declarations = new RouteDeclarations
                {
                    TrafficAdmission = new DeclarationReference<TrafficAdmissionPlan> { Inline = conflicting }
                }
            }]
        };
        GatewayValidationResult result = GatewayCandidateValidator.Validate(invalid, capabilities);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.Path == "routes[1].declarations.trafficAdmission.entries[0]" &&
            error.Message.Contains("conflicting candidate behavior", StringComparison.Ordinal));
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
        application.MapGet("/governed", static () => Results.Ok()).WithMetadata(GatewayTrafficAdmissionMetadata.Create(
            new string('a', 32), new ContentHash("sha-256", new string('a', 64)), new RouteId("route"), identity, plan));
        application.MapGet("/ordinary", static () => Results.Ok());
        var missingPlan = new TrafficAdmissionPlan
        {
            Entries = [new FixedWindowAdmissionEntry { Profile = "missing", PermitLimit = 1, Window = TimeSpan.FromMinutes(1) }]
        };
        application.MapGet("/broken", static () => Results.Ok()).WithMetadata(GatewayTrafficAdmissionMetadata.Create(
            new string('a', 32), new ContentHash("sha-256", new string('a', 64)), new RouteId("broken"),
            GatewayRuntimePlanner.HashTrafficAdmission(missingPlan), missingPlan));
        await application.StartAsync();

        using var client = application.GetTestClient();
        (await client.GetAsync("/governed")).StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        using var exhausted = await client.GetAsync("/governed");
        exhausted.StatusCode.Should().Be(System.Net.HttpStatusCode.TooManyRequests);
        exhausted.Headers.RetryAfter.Should().NotBeNull();
        (await client.GetAsync("/broken")).StatusCode.Should().Be(System.Net.HttpStatusCode.ServiceUnavailable);
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
        context.SetEndpoint(new Endpoint(null, new EndpointMetadataCollection(Metadata(plan)), "gateway"));
        return context;
    }

    private static GatewayTrafficAdmissionMetadata Metadata(TrafficAdmissionPlan plan) => GatewayTrafficAdmissionMetadata.Create(
        new string('a', 32), new ContentHash("sha-256", new string('a', 64)), new RouteId("route"),
        GatewayRuntimePlanner.HashTrafficAdmission(plan), plan);

    private static (GatewayTrafficAdmissionLimiter Limiter, DefaultHttpContext Context) CreateProjected(
        IGatewayAdmissionPartitionProjector projector)
    {
        var builder = new GatewayTrafficAdmissionRegistryBuilder();
        builder.AddPartitionProjector("projector", Hash('d'), projector)
            .AddLocalFixedWindow("custom", options =>
            {
                options.Partition = TrafficAdmissionPartitionKind.Custom;
                options.PartitionProjector = "projector";
            });
        var plan = new TrafficAdmissionPlan
        {
            Entries = [new FixedWindowAdmissionEntry { Profile = "custom", PermitLimit = 2, Window = TimeSpan.FromMinutes(1) }]
        };
        return (new GatewayTrafficAdmissionLimiter(builder.Build()), Context(plan));
    }

    private static long Fact(RateLimitLease lease, string name)
    {
        lease.TryGetMetadata(name, out var value).Should().BeTrue();
        return value.Should().BeOfType<long>().Subject;
    }

    private static ContentHash Hash(char value) => new("sha-256", new string(value, 64));

    private sealed class SubjectProjector : IGatewayAdmissionPartitionProjector
    {
        public ValueTask<GatewayAdmissionPartitionResult> ProjectAsync(GatewayAdmissionPartitionContext context, CancellationToken cancellationToken) =>
            ValueTask.FromResult(context.Principal.FindFirstValue(ClaimTypes.NameIdentifier) is { } subject
                ? GatewayAdmissionPartitionResult.Success(subject)
                : GatewayAdmissionPartitionResult.Failed(GatewayAdmissionPartitionFailure.Unavailable));
    }

    private sealed class ThrowingProjector : IGatewayAdmissionPartitionProjector
    {
        public ValueTask<GatewayAdmissionPartitionResult> ProjectAsync(GatewayAdmissionPartitionContext context, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("projector failure");
    }

    private sealed class CountingProjector(GatewayAdmissionPartitionResult result) : IGatewayAdmissionPartitionProjector
    {
        internal int Calls { get; private set; }
        public async ValueTask<GatewayAdmissionPartitionResult> ProjectAsync(GatewayAdmissionPartitionContext context, CancellationToken cancellationToken)
        {
            Calls++;
            await Task.Yield();
            return result;
        }
    }

    private sealed class CancelingProjector : IGatewayAdmissionPartitionProjector
    {
        public async ValueTask<GatewayAdmissionPartitionResult> ProjectAsync(GatewayAdmissionPartitionContext context, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new UnreachableException();
        }
    }

    private sealed class AlwaysAcquiredProvider : IGatewaySharedAdmissionProvider
    {
        public ValueTask<GatewaySharedAdmissionDecision> AcquireAsync(
            GatewaySharedAdmissionRequest request,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(new GatewaySharedAdmissionDecision(
                GatewaySharedAdmissionDecisionKind.Acquired,
                request.PermitLimit - request.PermitCount,
                null,
                request.WindowMilliseconds,
                "accepted",
                null));
    }

    private sealed class ManualTimeProvider(long unixMilliseconds) : TimeProvider
    {
        internal long UnixMilliseconds { get; set; } = unixMilliseconds;
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.FromUnixTimeMilliseconds(UnixMilliseconds);
    }
}
