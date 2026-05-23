using Rhodium.Kernel;
using Rhodium.Primitives;
using System.Runtime.ExceptionServices;

namespace Rhodium.Platform.Patterns;

/// <summary>
/// Pre-allocated state for parallel hierarchical dispatch.
/// </summary>
public sealed class ParallelDispatchState : IDisposable
{
    private const int MaxCommandsPerStrategy = 32;
    private readonly StrategyTree _tree;
    private readonly StrategyContext[] _contexts;
    private readonly int[][] _indicesByDepth;
    private readonly AllocationCommand[][] _commandBuffers;
    private readonly int[] _commandCounts;
    private readonly object _exceptionGate = new();

    private WorkerItem[]? _workers;
    private CountdownEvent? _phaseComplete;
    private RhodiumRuntime? _activeRuntime;
    private int[] _activeIndices = [];
    private int _activeWorkerCount;
    private StrategyDispatchKind _activeKind;
    private ExceptionDispatchInfo? _firstException;

    public int ParallelThreshold { get; set; } = 128;
    public int ThreadCount { get; }
    internal int LastQueuedWorkerCount { get; private set; }

    public ParallelDispatchState(StrategyTree tree, int threadCount = 0)
    {
        _tree = tree;
        ThreadCount = threadCount > 0 ? threadCount : Environment.ProcessorCount;

        var nodes = tree.Nodes;
        _contexts = new StrategyContext[nodes.Count];
        _commandBuffers = new AllocationCommand[nodes.Count][];
        _commandCounts = new int[nodes.Count];

        for (var i = 0; i < nodes.Count; i++)
        {
            _contexts[i] = new StrategyContext
            {
                Strategy = nodes[i].Strategy,
                Node = nodes[i].Node,
                ChildSnapshots = new PortfolioSnapshot[nodes[i].Node.ChildIds.Length],
                Counters = new int[PortfolioContext.CounterCount],
                OrderIntents = new OrderIntent[32]
            };
            _commandBuffers[i] = new AllocationCommand[MaxCommandsPerStrategy];
        }

        _indicesByDepth = new int[tree.MaxDepth + 1][];
        for (var depth = 0; depth < _indicesByDepth.Length; depth++)
        {
            var indices = new List<int>();
            for (var i = 0; i < _contexts.Length; i++)
            {
                if (_contexts[i].Node.Depth == depth)
                    indices.Add(i);
            }

            _indicesByDepth[depth] = indices.ToArray();
        }

    }

    internal ReadOnlySpan<StrategyContext> Contexts => _contexts;
    internal Span<StrategyContext> MutableContexts => _contexts;
    internal int[] GetIndicesAtDepth(int depth) => _indicesByDepth[depth];
    internal AllocationCommand[] GetCommandBuffer(int contextIndex) => _commandBuffers[contextIndex];
    internal int GetCommandCount(int contextIndex) => Volatile.Read(ref _commandCounts[contextIndex]);
    internal void SetCommandCount(int contextIndex, int count) => Volatile.Write(ref _commandCounts[contextIndex], count);
    internal void ResetCommandCount(int contextIndex) => Volatile.Write(ref _commandCounts[contextIndex], 0);
    internal void MarkSequentialExecution() => LastQueuedWorkerCount = 0;

    internal bool AllDepthsBelowThreshold()
    {
        foreach (var indices in _indicesByDepth)
        {
            if (indices.Length >= ParallelThreshold)
                return false;
        }

        return true;
    }

    internal void ExecuteParallel(RhodiumRuntime runtime, int[] indices, StrategyDispatchKind kind = StrategyDispatchKind.Tick)
    {
        if (indices.Length == 0)
        {
            LastQueuedWorkerCount = 0;
            return;
        }

        _activeRuntime = runtime;
        _activeIndices = indices;
        _activeKind = kind;
        _activeWorkerCount = Math.Min(ThreadCount, indices.Length);
        _firstException = null;
        LastQueuedWorkerCount = _activeWorkerCount;
        var workers = EnsureWorkers();
        var phaseComplete = _phaseComplete ?? throw new InvalidOperationException("Parallel dispatch completion event is not initialized.");
        phaseComplete.Reset(_activeWorkerCount);

        for (var i = 0; i < _activeWorkerCount; i++)
            workers[i].Signal();

        phaseComplete.Wait();

        _activeRuntime = null;
        _activeIndices = Array.Empty<int>();
        _activeKind = StrategyDispatchKind.Tick;
        _activeWorkerCount = 0;

        _firstException?.Throw();
    }

    private void ExecuteWorker(int workerIndex)
    {
        try
        {
            var runtime = _activeRuntime ?? throw new InvalidOperationException("Parallel dispatch runtime is not set.");
            var indices = _activeIndices;
            var kind = _activeKind;
            var workerCount = _activeWorkerCount;
            var chunkSize = (indices.Length + workerCount - 1) / workerCount;
            var start = workerIndex * chunkSize;
            var end = Math.Min(start + chunkSize, indices.Length);

            var market = runtime.CreateMarketKernel();
            for (var i = start; i < end; i++)
                EngineLoops.ExecuteContext(runtime, in market, this, indices[i], kind);
        }
        catch (Exception ex)
        {
            lock (_exceptionGate)
                _firstException ??= ExceptionDispatchInfo.Capture(ex);
        }
        finally
        {
            _phaseComplete?.Signal();
        }
    }

    private WorkerItem[] EnsureWorkers()
    {
        if (_workers is not null)
            return _workers;

        _phaseComplete = new CountdownEvent(1);
        var workers = new WorkerItem[ThreadCount];
        for (var i = 0; i < workers.Length; i++)
            workers[i] = new WorkerItem(this, i);

        _workers = workers;
        return workers;
    }

    public void Dispose()
    {
        if (_workers is not null)
        {
            foreach (var worker in _workers)
                worker.Dispose();
        }

        _phaseComplete?.Dispose();
    }

    private sealed class WorkerItem : IDisposable
    {
        private readonly ParallelDispatchState _owner;
        private readonly int _workerIndex;
        private readonly AutoResetEvent _signal = new(false);
        private readonly Thread _thread;
        private volatile bool _disposed;

        public WorkerItem(ParallelDispatchState owner, int workerIndex)
        {
            _owner = owner;
            _workerIndex = workerIndex;
            _thread = new Thread(Run)
            {
                IsBackground = true,
                Name = $"RhodiumDispatchWorker-{workerIndex}"
            };
            _thread.Start();
        }

        private void Run()
        {
            while (true)
            {
                _signal.WaitOne();
                if (_disposed) return;

                _owner.ExecuteWorker(_workerIndex);
            }
        }

        public void Signal() => _signal.Set();

        public void Dispose()
        {
            _disposed = true;
            _signal.Set();
            _thread.Join();
            _signal.Dispose();
        }
    }
}
