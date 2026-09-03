using BenchmarkDotNet.Attributes;
using HPD.Events;
using HPD.Events.Core;

namespace HPD.Agent.TUI.Benchmarks;

/// <summary>Compares allocation and throughput for the supported agent-event scopes.</summary>
[MemoryDiagnoser]
public class AgentEventRoutingBenchmark
{
    private EventCoordinator _localCoordinator = null!;
    private EventCoordinator _exactCoordinator = null!;
    private EventCoordinator _subtreeCoordinator = null!;
    private EventInbox<TextDeltaEvent> _local;
    private DeliveryInbox<AgentEventDelivery> _exact = null!;
    private DeliveryInbox<AgentEventDelivery> _subtree = null!;
    private AgentEventRouteDescriptor _localRoute = null!;
    private AgentEventRouteDescriptor _rootRoute = null!;
    private AgentEventRouteDescriptor _leafRoute = null!;
    private TextDeltaEvent _rootEvent = null!;
    private TextDeltaEvent _leafEvent = null!;

    [GlobalSetup]
    public void Setup()
    {
        _localCoordinator = new EventCoordinator();
        _exactCoordinator = new EventCoordinator();
        _subtreeCoordinator = new EventCoordinator();
        AgentEventRoutes.Initialize(_localCoordinator);
        AgentEventRoutes.Initialize(_exactCoordinator);
        AgentEventRoutes.Initialize(_subtreeCoordinator);
        var root = new ThreadKey("benchmark", "root");
        var child = new ThreadKey("benchmark", "child");
        var leaf = new ThreadKey("benchmark", "leaf");
        AgentEventRoutes.RegisterChild(_subtreeCoordinator, child, root);
        AgentEventRoutes.RegisterChild(_subtreeCoordinator, leaf, child);
        _local = _localCoordinator.CreateInbox<TextDeltaEvent>();
        _exact = AgentEventRoutes.CreateDeliveryInbox(_exactCoordinator, root, AgentEventHierarchy.ExactThread);
        _subtree = AgentEventRoutes.CreateDeliveryInbox(_subtreeCoordinator, root, AgentEventHierarchy.ThreadAndDescendants);
        _rootEvent = CreateEvent(root);
        _leafEvent = CreateEvent(leaf);
        _localRoute = AgentEventRoutes.Create(_localCoordinator, _rootEvent)!;
        _rootRoute = AgentEventRoutes.Create(_exactCoordinator, _rootEvent)!;
        _leafRoute = AgentEventRoutes.Create(_subtreeCoordinator, _leafEvent)!;
    }

    [Benchmark(Baseline = true)]
    public bool LocalOnly()
    {
        _localCoordinator.Emit(_rootEvent, _localRoute);
        return _local.Reader.TryRead(out _);
    }

    [Benchmark]
    public bool ExactThread()
    {
        _exactCoordinator.Emit(_rootEvent, _rootRoute);
        return _exact.Reader.TryRead(out _);
    }

    [Benchmark]
    public bool FullSubtree()
    {
        _subtreeCoordinator.Emit(_leafEvent, _leafRoute);
        return _subtree.Reader.TryRead(out _);
    }

    [GlobalCleanup]
    public async Task Cleanup()
    {
        await _local.DisposeAsync();
        await _exact.DisposeAsync();
        await _subtree.DisposeAsync();
        _localCoordinator.Dispose();
        _exactCoordinator.Dispose();
        _subtreeCoordinator.Dispose();
    }

    private static TextDeltaEvent CreateEvent(ThreadKey key) => new("delta", "message")
    {
        SessionId = key.SessionId,
        ThreadId = key.ThreadId
    };
}
