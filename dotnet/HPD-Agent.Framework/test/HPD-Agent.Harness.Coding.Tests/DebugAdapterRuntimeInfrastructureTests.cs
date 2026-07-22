using HPD.Agent.ToolHarness.Coding.Debugging;
using Microsoft.Extensions.DependencyInjection;
using HPD.Agent.ToolHarness.Coding.Debugging.Adapters;

namespace HPD.Agent.ToolHarness.Coding.Tests;

public sealed class DebugAdapterRuntimeInfrastructureTests
{
    [Fact]
    public async Task Availability_cache_coalesces_identical_concurrent_probes()
    {
        var cache = new DebugAdapterAvailabilityCache();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var key = Key();

        async ValueTask<DebugAdapterAvailability> Probe(CancellationToken _)
        {
            if (Interlocked.Increment(ref calls) == 1)
                entered.SetResult();
            await release.Task;
            return new(DebugAdapterAvailabilityKind.Available, Version: "1.0");
        }

        var probes = Enumerable.Range(0, 16)
            .Select(_ => cache.GetOrProbeAsync(key, Probe).AsTask())
            .ToArray();
        await entered.Task;
        release.SetResult();

        var results = await Task.WhenAll(probes);
        calls.Should().Be(1);
        results.Should().OnlyContain(result => result.Kind == DebugAdapterAvailabilityKind.Available);
    }

    [Fact]
    public async Task Availability_cache_uses_distinct_positive_and_negative_ttls()
    {
        var time = new ManualTimeProvider();
        var cache = new DebugAdapterAvailabilityCache(time, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(5));
        var positiveCalls = 0;
        var negativeCalls = 0;

        await cache.GetOrProbeAsync(Key("positive"), _ => ValueTask.FromResult(new DebugAdapterAvailability(DebugAdapterAvailabilityKind.Available, Version: (++positiveCalls).ToString())));
        await cache.GetOrProbeAsync(Key("negative"), _ => ValueTask.FromResult(new DebugAdapterAvailability(DebugAdapterAvailabilityKind.Unavailable, SafeReasonCode: (++negativeCalls).ToString())));
        time.Advance(TimeSpan.FromSeconds(6));
        await cache.GetOrProbeAsync(Key("positive"), _ => ValueTask.FromResult(new DebugAdapterAvailability(DebugAdapterAvailabilityKind.Available, Version: (++positiveCalls).ToString())));
        await cache.GetOrProbeAsync(Key("negative"), _ => ValueTask.FromResult(new DebugAdapterAvailability(DebugAdapterAvailabilityKind.Unavailable, SafeReasonCode: (++negativeCalls).ToString())));

        positiveCalls.Should().Be(1);
        negativeCalls.Should().Be(2);
    }

    [Fact]
    public async Task Availability_cache_invalidation_is_environment_and_endpoint_revision_scoped()
    {
        var cache = new DebugAdapterAvailabilityCache();
        var calls = 0;
        ValueTask<DebugAdapterAvailability> Probe(CancellationToken _) =>
            ValueTask.FromResult(new DebugAdapterAvailability(DebugAdapterAvailabilityKind.Available, Version: (++calls).ToString()));

        await cache.GetOrProbeAsync(Key(environment: "env-a", endpointRevision: 1), Probe);
        await cache.GetOrProbeAsync(Key(environment: "env-b", endpointRevision: 1), Probe);
        cache.InvalidateEnvironment("env-a");
        await cache.GetOrProbeAsync(Key(environment: "env-a", endpointRevision: 1), Probe);
        cache.InvalidateEndpointCatalog(2);
        await cache.GetOrProbeAsync(Key(environment: "env-b", endpointRevision: 2), Probe);

        calls.Should().Be(4);
    }

    [Fact]
    public void DI_composition_is_explicit_and_default_trust_fails_closed()
    {
        var services = new ServiceCollection()
            .AddHPDCodingDebugging()
            .AddHPDBuiltInDebugAdapters()
            .BuildServiceProvider();

        var catalog = services.GetRequiredService<DebugAdapterCatalog>();
        var trust = services.GetRequiredService<IDebugAdapterTrustPolicy>()
            .Evaluate(catalog.Entries[0].Descriptor);

        catalog.Entries.Should().HaveCount(8);
        catalog.GetFactory("debugpy").Should().BeOfType<DebugPyAdapterFactory>();
        trust.TrustLevel.Should().Be(DebugAdapterTrustLevel.Denied);
        trust.ReasonCode.Should().Be("HOST_TRUST_POLICY_NOT_CONFIGURED");
    }

    private static DebugAdapterAvailabilityCacheKey Key(
        string adapter = "fixture",
        string environment = "env",
        long endpointRevision = 1) => new(
            adapter, "package", environment, 1, "linux-x64", "/workspace", "markers", 1, "trust-1", endpointRevision);

    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan duration) => _now += duration;
    }
}
