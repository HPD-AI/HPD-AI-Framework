using BenchmarkDotNet.Attributes;
using HPD.Events;
using HPD.Events.Core;

namespace HPD.Agent.TUI.Benchmarks;

/// <summary>Compares allocation and throughput for the supported agent-event scopes.</summary>
[MemoryDiagnoser]
public sealed class AgentEventRoutingBenchmark
{
    private EventCoordinator _coordinator = null!;
    private EventInbox<TextDeltaEvent> _local;
    private DeliveryInbox<AgentEventDelivery> _exact = null!;
    private DeliveryInbox<AgentEventDelivery> _subtree = null!;
    private AgentEventRouteDescriptor _rootRoute = null!;
    private AgentEventRouteDescriptor _leafRoute = null!;
    private TextDeltaEvent _rootEvent = null!;
    private TextDeltaEvent _leafEvent = null!;

    [GlobalSetup]
    public void Setup()
    {
        _coordinator = new EventCoordinator();
        AgentEventRoutes.Initialize(_coordinator);
        var root = new ThreadKey("benchmark", "root");
        var child = new ThreadKey("benchmark", "child");
        var leaf = new ThreadKey("benchmark", "leaf");
        AgentEventRoutes.RegisterChild(_coordinator, child, root);
        AgentEventRoutes.RegisterChild(_coordinator, leaf, child);
        _local = _coordinator.CreateInbox<TextDeltaEvent>();
        _exact = AgentEventRoutes.CreateDeliveryInbox(_coordinator, root, AgentEventHierarchy.ExactThread);
        _subtree = AgentEventRoutes.CreateDeliveryInbox(_coordinator, root, AgentEventHierarchy.ThreadAndDescendants);
        _rootEvent = CreateEvent(root);
        _leafEvent = CreateEvent(leaf);
        _rootRoute = AgentEventRoutes.Create(_coordinator, _rootEvent)!;
        _leafRoute = AgentEventRoutes.Create(_coordinator, _leafEvent)!;
    }

    [Benchmark(Baseline = true)]
    public bool LocalOnly()
    {
        _coordinator.Emit(_rootEvent, _rootRoute);
        return _local.Reader.TryRead(out _);
    }

    [Benchmark]
    public bool ExactThread()
    {
        _coordinator.Emit(_rootEvent, _rootRoute);
        return _exact.Reader.TryRead(out _);
    }

    [Benchmark]
    public bool FullSubtree()
    {
        _coordinator.Emit(_leafEvent, _leafRoute);
        return _subtree.Reader.TryRead(out _);
    }

    [GlobalCleanup]
    public async Task Cleanup()
    {
        await _local.DisposeAsync();
        await _exact.DisposeAsync();
        await _subtree.DisposeAsync();
        _coordinator.Dispose();
    }

    private static TextDeltaEvent CreateEvent(ThreadKey key) => new("delta", "message")
    {
        SessionId = key.SessionId,
        ThreadId = key.ThreadId
    };
}
