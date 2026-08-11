using System.Collections.Concurrent;
using System.Threading.RateLimiting;
using FluentAssertions;
using HPD.Gateway;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace HPD.Gateway.Tests;

public sealed class GatewaySharedAdmissionTests
{
    [Fact]
    public async Task Every_shared_request_dispatches_once_and_preserves_authoritative_facts()
    {
        var provider = new SequenceProvider(
            Acquired(0, 1_000),
            Rejected(0, 500, 500));
        using GatewayTrafficAdmissionRegistry registry = Registry(provider);
        var limiter = new GatewayTrafficAdmissionLimiter(registry);
        TrafficAdmissionPlan plan = Plan();

        using RateLimitLease first = await limiter.AcquireAsync(Context(plan));
        using RateLimitLease second = await limiter.AcquireAsync(Context(plan));

        first.IsAcquired.Should().BeTrue();
        second.IsAcquired.Should().BeFalse();
        Fact(second, GatewayAdmissionMetadata.RetryAfterMilliseconds).Should().Be(500);
        provider.Requests.Should().HaveCount(2);
        provider.Requests.Should().OnlyContain(static request => request.PermitCount == 1);
        provider.Requests.Select(static request => request.AttemptId).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task Unavailable_may_reject_bypass_or_use_exact_local_fallback_but_indeterminate_never_does()
    {
        GatewaySharedAdmissionDecision unavailable = Infrastructure(GatewaySharedAdmissionDecisionKind.UnavailableBeforePossibleCommit);
        using (GatewayTrafficAdmissionRegistry reject = Registry(new SequenceProvider(unavailable)))
        {
            using RateLimitLease lease = await new GatewayTrafficAdmissionLimiter(reject).AcquireAsync(Context(Plan()));
            lease.IsAcquired.Should().BeFalse();
        }
        using (GatewayTrafficAdmissionRegistry bypass = Registry(new SequenceProvider(unavailable), TrafficAdmissionFailureDisposition.Bypass))
        {
            using RateLimitLease lease = await new GatewayTrafficAdmissionLimiter(bypass).AcquireAsync(Context(Plan()));
            lease.IsAcquired.Should().BeTrue();
            lease.TryGetMetadata("HPD.Gateway.Admission.Degraded", out object? degraded).Should().BeTrue();
            degraded.Should().Be("Bypass");
        }
        using (GatewayTrafficAdmissionRegistry fallback = Registry(new SequenceProvider(unavailable, unavailable), TrafficAdmissionFailureDisposition.LocalFallback))
        {
            var limiter = new GatewayTrafficAdmissionLimiter(fallback);
            using RateLimitLease first = await limiter.AcquireAsync(Context(Plan()));
            using RateLimitLease second = await limiter.AcquireAsync(Context(Plan()));
            first.IsAcquired.Should().BeTrue();
            second.IsAcquired.Should().BeFalse();
        }
        using (GatewayTrafficAdmissionRegistry indeterminate = Registry(
            new SequenceProvider(Infrastructure(GatewaySharedAdmissionDecisionKind.IndeterminateAfterPossibleCommit)),
            TrafficAdmissionFailureDisposition.LocalFallback))
        {
            using RateLimitLease lease = await new GatewayTrafficAdmissionLimiter(indeterminate).AcquireAsync(Context(Plan()));
            lease.IsAcquired.Should().BeFalse();
            lease.TryGetMetadata(GatewayAdmissionMetadata.Outcome, out object? outcome).Should().BeTrue();
            outcome.Should().Be(GatewayAdmissionOutcome.Infrastructure);
        }
    }

    [Fact]
    public async Task Underlying_invocation_capacity_survives_caller_deadline_until_real_completion()
    {
        var provider = new HangingProvider();
        using GatewayTrafficAdmissionRegistry registry = Registry(provider, maximumInvocations: 2, timeout: TimeSpan.FromMilliseconds(25));
        var limiter = new GatewayTrafficAdmissionLimiter(registry);
        TrafficAdmissionPlan plan = Plan();

        Task<RateLimitLease>[] first = Enumerable.Range(0, 2)
            .Select(_ => limiter.AcquireAsync(Context(plan)).AsTask()).ToArray();
        await provider.WaitForCalls(2);
        RateLimitLease[] timedOut = await Task.WhenAll(first);
        timedOut.Should().OnlyContain(static lease => !lease.IsAcquired);
        using RateLimitLease saturated = await limiter.AcquireAsync(Context(plan));
        saturated.IsAcquired.Should().BeFalse();
        provider.Calls.Should().Be(2, "saturation must reject before dispatch");

        provider.Complete(Acquired(0, 1_000));
        await SpinWaitAsync(() => provider.Completed == 2);
        using RateLimitLease recovered = await limiter.AcquireAsync(Context(plan));
        recovered.IsAcquired.Should().BeTrue();
        provider.Calls.Should().Be(3);
        foreach (RateLimitLease lease in timedOut) lease.Dispose();
    }

    [Fact]
    public async Task Malformed_provider_results_and_configuration_conflicts_fail_closed()
    {
        GatewaySharedAdmissionDecision[] malformed =
        [
            new(GatewaySharedAdmissionDecisionKind.Acquired, null, null, 1, null, null),
            new(GatewaySharedAdmissionDecisionKind.Rejected, 0, null, 1, null, null),
            new(GatewaySharedAdmissionDecisionKind.UnavailableBeforePossibleCommit, 1, null, null, null, null),
            new((GatewaySharedAdmissionDecisionKind)255, null, null, null, null, null),
        ];
        foreach (GatewaySharedAdmissionDecision decision in malformed.Append(
            Infrastructure(GatewaySharedAdmissionDecisionKind.ConfigurationConflict)))
        {
            using GatewayTrafficAdmissionRegistry registry = Registry(new SequenceProvider(decision));
            using RateLimitLease lease = await new GatewayTrafficAdmissionLimiter(registry).AcquireAsync(Context(Plan()));
            lease.IsAcquired.Should().BeFalse();
            lease.TryGetMetadata(GatewayAdmissionMetadata.Outcome, out object? outcome).Should().BeTrue();
            outcome.Should().Be(GatewayAdmissionOutcome.Infrastructure);
        }
    }

    [Fact]
    public async Task Provider_results_are_correlated_to_the_exact_request_and_impossible_facts_fail_closed()
    {
        GatewaySharedAdmissionDecision[] impossible =
        [
            Acquired(1, 1_000),
            Rejected(1, 1_000, 1_000),
            Acquired(0, 1_001),
            Rejected(0, 500, 1_000),
        ];
        foreach (GatewaySharedAdmissionDecision decision in impossible)
        {
            using GatewayTrafficAdmissionRegistry registry = Registry(new SequenceProvider(decision));
            using RateLimitLease lease = await new GatewayTrafficAdmissionLimiter(registry).AcquireAsync(Context(Plan()));
            lease.IsAcquired.Should().BeFalse();
            lease.TryGetMetadata(GatewayAdmissionMetadata.Outcome, out object? outcome).Should().BeTrue();
            outcome.Should().Be(GatewayAdmissionOutcome.Infrastructure);
        }
    }

    [Fact]
    public async Task Disposal_prevents_waiting_work_from_crossing_the_provider_dispatch_boundary()
    {
        var provider = new HangingProvider();
        using GatewayTrafficAdmissionRegistry registry = Registry(provider, maximumInvocations: 1, timeout: TimeSpan.FromSeconds(5));
        var limiter = new GatewayTrafficAdmissionLimiter(registry);
        Task<RateLimitLease> first = limiter.AcquireAsync(Context(Plan())).AsTask();
        await provider.WaitForCalls(1);
        Task<RateLimitLease> waiting = limiter.AcquireAsync(Context(Plan())).AsTask();
        registry.Dispose();
        using RateLimitLease rejected = await waiting;
        rejected.IsAcquired.Should().BeFalse();
        provider.Calls.Should().Be(1);
        provider.Complete(Acquired(0, 1_000));
        using RateLimitLease initial = await first;
        initial.IsAcquired.Should().BeTrue("work dispatched before the atomic disposal boundary may still resolve");
    }

    [Fact]
    public void Local_fallback_must_authorize_the_same_partition_projector_and_every_shared_bound()
    {
        FluentActions.Invoking(() =>
        {
            var builder = new GatewayTrafficAdmissionRegistryBuilder();
            builder.AddLocalFixedWindow("fallback", options =>
            {
                options.Partition = TrafficAdmissionPartitionKind.SourceIp;
                options.MaximumLimit = 5;
            });
            builder.AddSharedProvider("provider", new SequenceProvider(Infrastructure(GatewaySharedAdmissionDecisionKind.UnavailableBeforePossibleCommit)), options =>
            {
                options.AuthorityId = "deployment-a";
                options.BehaviorIdentity = Hash('b');
            });
            builder.AddSharedFixedWindow("shared", "provider", options =>
            {
                options.MaximumLimit = 10;
                options.FailureDisposition = TrafficAdmissionFailureDisposition.LocalFallback;
                options.LocalFallbackProfile = "fallback";
            });
            _ = builder.Build();
        }).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Shared_request_and_decision_contracts_are_closed_and_bounded()
    {
        GatewaySharedAdmissionRequest request = Request();
        GatewaySharedAdmissionContract.IsValidRequest(request, requireUnitPermit: true).Should().BeTrue();
        GatewaySharedAdmissionContract.IsValidRequest(request with { PermitCount = 2 }, requireUnitPermit: true).Should().BeFalse();
        GatewaySharedAdmissionContract.IsValidRequest(request with { PartitionKey = new string('x', 257) }).Should().BeFalse();
        GatewaySharedAdmissionContract.IsValidRequest(request with { WindowMilliseconds = 1_000, SegmentsPerWindow = 3,
            Algorithm = TrafficAdmissionRateAlgorithm.SlidingWindow }).Should().BeFalse();
        GatewaySharedAdmissionContract.IsValidDecision(Acquired(0, 1)).Should().BeTrue();
        GatewaySharedAdmissionContract.IsValidDecision(Rejected(0, 1, 1)).Should().BeTrue();
    }

    [Fact]
    public void Provider_timeout_and_capacity_participate_in_profile_and_host_identity()
    {
        using GatewayTrafficAdmissionRegistry baseline = Registry(new SequenceProvider(Acquired(1, 1)),
            maximumInvocations: 8, timeout: TimeSpan.FromMilliseconds(100));
        using GatewayTrafficAdmissionRegistry timeoutChanged = Registry(new SequenceProvider(Acquired(1, 1)),
            maximumInvocations: 8, timeout: TimeSpan.FromMilliseconds(101));
        using GatewayTrafficAdmissionRegistry capacityChanged = Registry(new SequenceProvider(Acquired(1, 1)),
            maximumInvocations: 9, timeout: TimeSpan.FromMilliseconds(100));

        TrafficAdmissionCapability original = baseline.Capabilities.Single(static value => value.Name == "shared");
        timeoutChanged.Capabilities.Single(static value => value.Name == "shared").BehaviorIdentity.Should().NotBe(original.BehaviorIdentity);
        capacityChanged.Capabilities.Single(static value => value.Name == "shared").BehaviorIdentity.Should().NotBe(original.BehaviorIdentity);

        HostCapabilitySnapshot.Create(new HostCapabilityRegistration
        {
            InstalledFamilies = GatewayDeclarationFamilies.TrafficAdmission,
            TrafficAdmissionProfiles = baseline.Capabilities,
        }).TrafficAdmissionProfiles["shared"].OperationTimeout.Should().Be(TimeSpan.FromMilliseconds(100));
    }

    [Fact]
    public async Task Public_certification_executes_exact_bounded_vectors_and_fails_closed()
    {
        var state = FixedState(100, 0, 1, 1_000);
        var provider = new SequenceProvider([Acquired(9, 1_000), Rejected(0, 1_000, 1_000)], [state, state]);
        GatewaySharedAdmissionCertificationVector[] vectors =
        [
            new(Request(), Acquired(9, 1_000), state),
            new(Request() with { AttemptId = new string('b', 32) }, Rejected(0, 1_000, 1_000), state),
        ];

        GatewaySharedAdmissionCertificationReport passed =
            await GatewaySharedAdmissionCertification.VerifyAsync(provider, vectors);
        passed.Passed.Should().BeTrue();
        passed.Executed.Should().Be(2);
        passed.Diagnostics.Should().BeEmpty();

        GatewaySharedAdmissionCertificationReport malformed =
            await GatewaySharedAdmissionCertification.VerifyAsync(provider,
                [new(Request() with { PermitCount = 0 }, Acquired(0, 1), state)]);
        malformed.Passed.Should().BeFalse();
        malformed.Executed.Should().Be(0);

        GatewaySharedAdmissionCertificationReport oversized =
            await GatewaySharedAdmissionCertification.VerifyAsync(provider,
                Enumerable.Repeat(vectors[0], GatewaySharedAdmissionCertification.MaximumVectors + 1));
        oversized.Passed.Should().BeFalse();
        oversized.Executed.Should().Be(0);

        GatewaySharedAdmissionContract.IsValidState(Request(), state with
        {
            Segments = [new GatewaySharedAdmissionSegmentState(1, 1)]
        }).Should().BeFalse();

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await FluentActions.Awaiting(async () => await GatewaySharedAdmissionCertification.VerifyAsync(
            provider, vectors, cancellation.Token)).Should().ThrowAsync<OperationCanceledException>();
    }

    [Theory]
    [InlineData(TrafficAdmissionRateAlgorithm.FixedWindow, 0, 0, 900, 900)]
    [InlineData(TrafficAdmissionRateAlgorithm.SlidingWindow, 0, 2, 900, 900)]
    [InlineData(TrafficAdmissionRateAlgorithm.TokenBucket, 1, 0, 1_000, 1_000)]
    public async Task Deterministic_in_process_authorities_cover_every_shared_algorithm(
        TrafficAdmissionRateAlgorithm algorithm,
        long tokensPerPeriod,
        int segments,
        long acquiredReset,
        long rejectedDelay)
    {
        var authority = new InProcessAtomicAuthority { Now = 100 };
        GatewaySharedAdmissionRequest first = Request() with
        {
            Algorithm = algorithm,
            PermitLimit = 1,
            TokensPerPeriod = tokensPerPeriod,
            SegmentsPerWindow = segments,
        };
        GatewaySharedAdmissionRequest second = first with { AttemptId = new string('b', 32) };

        GatewaySharedAdmissionCertificationReport report = await GatewaySharedAdmissionCertification.VerifyAsync(authority,
        [
            new(first, Acquired(0, acquiredReset), ExpectedState(algorithm, 100, acquiredReset)),
            new(second, Rejected(0, rejectedDelay, rejectedDelay), ExpectedState(algorithm, 100, rejectedDelay)),
        ]);

        report.Passed.Should().BeTrue();
        report.Executed.Should().Be(2);
        authority.StateCount.Should().Be(1);
    }

    private static GatewayTrafficAdmissionRegistry Registry(
        IGatewaySharedAdmissionProvider provider,
        TrafficAdmissionFailureDisposition disposition = TrafficAdmissionFailureDisposition.Reject,
        int maximumInvocations = 8,
        TimeSpan? timeout = null)
    {
        var builder = new GatewayTrafficAdmissionRegistryBuilder();
        if (disposition == TrafficAdmissionFailureDisposition.LocalFallback)
            builder.AddLocalFixedWindow("fallback", options => options.MaximumLimit = 10);
        builder.AddSharedProvider("provider", provider, options =>
        {
            options.AuthorityId = "deployment-a";
            options.BehaviorIdentity = Hash('b');
            options.MaximumConcurrentInvocations = maximumInvocations;
            options.OperationTimeout = timeout ?? TimeSpan.FromSeconds(1);
        });
        builder.AddSharedFixedWindow("shared", "provider", options =>
        {
            options.MaximumLimit = 10;
            options.FailureDisposition = disposition;
            options.LocalFallbackProfile = disposition == TrafficAdmissionFailureDisposition.LocalFallback ? "fallback" : null;
        });
        return builder.Build();
    }

    private static TrafficAdmissionPlan Plan() => new()
    {
        Entries = [new FixedWindowAdmissionEntry { Profile = "shared", PermitLimit = 1, Window = TimeSpan.FromSeconds(1) }]
    };

    private static DefaultHttpContext Context(TrafficAdmissionPlan plan)
    {
        var context = new DefaultHttpContext();
        context.SetEndpoint(new Endpoint(null, new EndpointMetadataCollection(GatewayTrafficAdmissionMetadata.Create(
            new string('a', 32), Hash('a'), new RouteId("route"), GatewayRuntimePlanner.HashTrafficAdmission(plan), plan)), "gateway"));
        return context;
    }

    private static GatewaySharedAdmissionRequest Request() => new(1, "provider", "deployment-a", "shared", Hash('a'),
        "partition", TrafficAdmissionRateAlgorithm.FixedWindow, 10, 0, 1_000, 0, 1, new string('a', 32));
    private static GatewaySharedAdmissionDecision Acquired(long remaining, long reset) =>
        new(GatewaySharedAdmissionDecisionKind.Acquired, remaining, null, reset, "observation", null);
    private static GatewaySharedAdmissionDecision Rejected(long remaining, long retry, long reset) =>
        new(GatewaySharedAdmissionDecisionKind.Rejected, remaining, retry, reset, "observation", null);
    private static GatewaySharedAdmissionDecision Infrastructure(GatewaySharedAdmissionDecisionKind kind) =>
        new(kind, null, null, null, null, "safe");
    private static GatewaySharedAdmissionRetainedState FixedState(long observed, long start, long used, long expiry) =>
        new(1, TrafficAdmissionRateAlgorithm.FixedWindow, observed, start, used, null, null, null, [], expiry);

    private static GatewaySharedAdmissionRetainedState ExpectedState(TrafficAdmissionRateAlgorithm algorithm, long observed, long reset) => algorithm switch
    {
        TrafficAdmissionRateAlgorithm.FixedWindow => FixedState(observed, 0, 1, observed + reset),
        TrafficAdmissionRateAlgorithm.SlidingWindow => new(1, algorithm, observed, null, null, null, null, null,
            [new GatewaySharedAdmissionSegmentState(0, 1)], observed + reset),
        TrafficAdmissionRateAlgorithm.TokenBucket => new(1, algorithm, observed, null, null, 0, observed, 0, [], observed + reset),
        _ => throw new InvalidOperationException(),
    };
    private static ContentHash Hash(char value) => new("sha-256", new string(value, 64));
    private static long Fact(RateLimitLease lease, string name)
    {
        lease.TryGetMetadata(name, out object? value).Should().BeTrue();
        return value.Should().BeOfType<long>().Subject;
    }

    private static async Task SpinWaitAsync(Func<bool> predicate)
    {
        for (var index = 0; index < 100 && !predicate(); index++) await Task.Delay(10);
        predicate().Should().BeTrue();
    }

    private sealed class SequenceProvider(
        IEnumerable<GatewaySharedAdmissionDecision> decisions,
        IEnumerable<GatewaySharedAdmissionRetainedState> states) : IGatewaySharedAdmissionCertificationAuthority
    {
        internal SequenceProvider(params GatewaySharedAdmissionDecision[] decisions)
            : this(decisions, Enumerable.Repeat(FixedState(100, 0, 1, 1_000), Math.Max(1, decisions.Length))) { }
        private readonly ConcurrentQueue<GatewaySharedAdmissionDecision> _decisions = new(decisions);
        private readonly ConcurrentQueue<GatewaySharedAdmissionRetainedState> _states = new(states);
        internal ConcurrentQueue<GatewaySharedAdmissionRequest> Requests { get; } = new();
        public ValueTask<GatewaySharedAdmissionDecision> AcquireAsync(GatewaySharedAdmissionRequest request, CancellationToken cancellationToken)
        {
            Requests.Enqueue(request);
            return ValueTask.FromResult(_decisions.TryDequeue(out GatewaySharedAdmissionDecision? decision)
                ? decision : Acquired(0, 1));
        }
        public ValueTask<GatewaySharedAdmissionRetainedState> ObserveStateAsync(GatewaySharedAdmissionRequest request, CancellationToken cancellationToken) =>
            ValueTask.FromResult(_states.TryDequeue(out GatewaySharedAdmissionRetainedState? state) ? state : FixedState(100, 0, 1, 1_000));
    }

    private sealed class HangingProvider : IGatewaySharedAdmissionProvider
    {
        private readonly ConcurrentQueue<TaskCompletionSource<GatewaySharedAdmissionDecision>> _pending = new();
        private int _calls;
        private int _completed;
        private GatewaySharedAdmissionDecision? _completedDecision;
        internal int Calls => Volatile.Read(ref _calls);
        internal int Completed => Volatile.Read(ref _completed);
        public ValueTask<GatewaySharedAdmissionDecision> AcquireAsync(GatewaySharedAdmissionRequest request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            if (Volatile.Read(ref _completedDecision) is { } completed)
                return ValueTask.FromResult(completed);
            var source = new TaskCompletionSource<GatewaySharedAdmissionDecision>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pending.Enqueue(source);
            _ = source.Task.ContinueWith(_ => Interlocked.Increment(ref _completed), TaskScheduler.Default);
            return new(source.Task);
        }
        internal async Task WaitForCalls(int count) => await SpinWaitAsync(() => Calls == count);
        internal void Complete(GatewaySharedAdmissionDecision decision)
        {
            Volatile.Write(ref _completedDecision, decision);
            while (_pending.TryDequeue(out TaskCompletionSource<GatewaySharedAdmissionDecision>? source))
                source.TrySetResult(decision);
        }
    }

    private sealed class InProcessAtomicAuthority : IGatewaySharedAdmissionCertificationAuthority
    {
        private readonly ConcurrentDictionary<string, GatewayLocalRateState> _states = new(StringComparer.Ordinal);
        internal long Now { get; set; }
        internal int StateCount => _states.Count;

        public ValueTask<GatewaySharedAdmissionDecision> AcquireAsync(
            GatewaySharedAdmissionRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!GatewaySharedAdmissionContract.IsValidRequest(request))
                return ValueTask.FromResult(Infrastructure(GatewaySharedAdmissionDecisionKind.UnavailableBeforePossibleCommit));
            var state = _states.GetOrAdd(string.Join('|', request.Profile, request.PartitionKey, request.BehaviorIdentity.Value),
                static _ => new GatewayLocalRateState());
            TrafficAdmissionEntry entry = request.Algorithm switch
            {
                TrafficAdmissionRateAlgorithm.FixedWindow => new FixedWindowAdmissionEntry
                {
                    Profile = request.Profile, PermitLimit = request.PermitLimit,
                    Window = TimeSpan.FromMilliseconds(request.WindowMilliseconds)
                },
                TrafficAdmissionRateAlgorithm.SlidingWindow => new SlidingWindowAdmissionEntry
                {
                    Profile = request.Profile, PermitLimit = request.PermitLimit,
                    Window = TimeSpan.FromMilliseconds(request.WindowMilliseconds),
                    SegmentsPerWindow = request.SegmentsPerWindow
                },
                TrafficAdmissionRateAlgorithm.TokenBucket => new TokenBucketAdmissionEntry
                {
                    Profile = request.Profile, TokenLimit = request.PermitLimit,
                    TokensPerPeriod = request.TokensPerPeriod,
                    ReplenishmentPeriod = TimeSpan.FromMilliseconds(request.WindowMilliseconds)
                },
                _ => throw new InvalidOperationException(),
            };
            using RateLimitLease lease = state.Acquire(entry, Now);
            lease.TryGetMetadata(GatewayAdmissionMetadata.Remaining, out object? remaining);
            lease.TryGetMetadata(GatewayAdmissionMetadata.ResetAfterMilliseconds, out object? reset);
            if (lease.IsAcquired)
                return ValueTask.FromResult(Acquired((long)remaining!, (long)reset!));
            lease.TryGetMetadata(GatewayAdmissionMetadata.RetryAfterMilliseconds, out object? retry);
            return ValueTask.FromResult(Rejected((long)remaining!, (long)retry!, (long)reset!));
        }

        public ValueTask<GatewaySharedAdmissionRetainedState> ObserveStateAsync(
            GatewaySharedAdmissionRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var key = string.Join('|', request.Profile, request.PartitionKey, request.BehaviorIdentity.Value);
            if (!_states.TryGetValue(key, out GatewayLocalRateState? state)) throw new InvalidOperationException("State is absent.");
            GatewayLocalRateStateSnapshot snapshot = state.Snapshot(request.Algorithm);
            return ValueTask.FromResult(new GatewaySharedAdmissionRetainedState(1, snapshot.Algorithm,
                snapshot.LastObservedMilliseconds, snapshot.WindowStartMilliseconds, snapshot.Used, snapshot.Tokens,
                snapshot.LastRefillMilliseconds, snapshot.Remainder, snapshot.Segments, snapshot.ExpiryAtMilliseconds));
        }
    }
}
