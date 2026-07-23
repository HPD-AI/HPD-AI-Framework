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
        services.GetRequiredService<IDebugAdapterConfigurationComposer>()
            .Should().BeOfType<BuiltInDebugAdapterConfigurationComposer>();
        services.GetRequiredService<DebugRuntimeServiceFactory>().Should().NotBeNull();
    }

    [Fact]
    public async Task Runtime_service_factory_preserves_manager_isolation()
    {
        using var provider = new ServiceCollection()
            .AddHPDCodingDebugging()
            .BuildServiceProvider();
        await using var firstManager = new DebugSessionManager();
        await using var secondManager = new DebugSessionManager();
        var factory = provider.GetRequiredService<DebugRuntimeServiceFactory>();

        var first = factory.Create(Runtime(firstManager));
        var second = factory.Create(Runtime(secondManager));

        first.Manager.Should().BeSameAs(firstManager);
        second.Manager.Should().BeSameAs(secondManager);
        first.Manager.Should().NotBeSameAs(second.Manager);
        first.Semantics.Should().NotBeSameAs(second.Semantics);
    }

    [Theory]
    [InlineData("debugpy", DebugTargetKind.SourceFile, "stopOnEntry", null)]
    [InlineData("netcoredbg", DebugTargetKind.Executable, "stopAtEntry", null)]
    [InlineData("gdb", DebugTargetKind.Executable, "stopOnEntry", "stopAtBeginningOfMainSubprogram")]
    [InlineData("lldb-dap", DebugTargetKind.Executable, "stopOnEntry", null)]
    [InlineData("codelldb", DebugTargetKind.Executable, "stopOnEntry", null)]
    [InlineData("delve", DebugTargetKind.ProjectDirectory, "stopOnEntry", null)]
    [InlineData("javascript", DebugTargetKind.SourceFile, "stopOnEntry", null)]
    [InlineData("rdbg", DebugTargetKind.SourceFile, "stopOnEntry", null)]
    public void Semantic_launch_configuration_is_closed_and_adapter_specific(
        string adapterId,
        DebugTargetKind targetKind,
        string stopProperty,
        string? secondStopProperty)
    {
        var descriptor = Descriptor(adapterId, targetKind | DebugTargetKind.Process);
        var value = new BuiltInDebugAdapterConfigurationComposer().ComposeLaunch(
            descriptor,
            new("/workspace/target", "/workspace", targetKind, ["--flag"], StopOnEntry: true));

        value.GetProperty("request").GetString().Should().Be("launch");
        value.GetProperty("program").GetString().Should().Be("/workspace/target");
        value.GetProperty("cwd").GetString().Should().Be("/workspace");
        value.GetProperty("args")[0].GetString().Should().Be("--flag");
        value.GetProperty(stopProperty).GetBoolean().Should().BeTrue();
        if (secondStopProperty is not null)
            value.GetProperty(secondStopProperty).GetBoolean().Should().BeTrue();
        if (adapterId == "delve") value.GetProperty("mode").GetString().Should().Be("debug");
        if (adapterId == "javascript") value.GetProperty("type").GetString().Should().Be("pwa-node");
    }

    [Fact]
    public void Semantic_attach_configuration_emits_numeric_process_aliases()
    {
        var descriptor = Descriptor("delve", DebugTargetKind.Process);
        var value = new BuiltInDebugAdapterConfigurationComposer().ComposeAttach(
            descriptor, new("/workspace", ProcessId: "1234"));

        value.GetProperty("request").GetString().Should().Be("attach");
        value.GetProperty("mode").GetString().Should().Be("local");
        value.GetProperty("pid").GetInt64().Should().Be(1234);
        value.GetProperty("processId").GetInt64().Should().Be(1234);
    }

    private static DebugAdapterDescriptor Descriptor(string id, DebugTargetKind targetKinds) => new()
    {
        Id = id,
        Languages = [],
        FileExtensions = [],
        RootMarkers = [],
        TargetKinds = targetKinds,
        Provenance = new() { PackageId = id, PackageVersion = "1", AssemblyName = "tests" }
    };

    private static DebugRuntimeBinding Runtime(DebugSessionManager manager) => new()
    {
        AgentRuntimeRegistrationId = manager.RuntimeId,
        SessionId = "session",
        ThreadId = "thread",
        SessionManager = manager,
        EventScope = new(null, "session", "thread"),
        State = new()
    };

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
