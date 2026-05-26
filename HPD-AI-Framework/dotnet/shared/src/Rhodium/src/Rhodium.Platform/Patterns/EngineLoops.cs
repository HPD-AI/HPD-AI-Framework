using Rhodium.Kernel;
using Rhodium.Events;
using Rhodium.Primitives;

namespace Rhodium.Platform.Patterns;

/// <summary>
/// Unified Kernel dispatch and iteration loops.
/// </summary>
internal static class EngineLoops
{
    internal static void DispatchHierarchical(
        in MarketKernel market,
        StrategyTree tree,
        WorldState world,
        Span<StrategyContext> contexts)
        => DispatchHierarchical(in market, tree, world, contexts, StrategyDispatchKind.Tick);

    internal static void DispatchBarsHierarchical(
        in MarketKernel market,
        StrategyTree tree,
        WorldState world,
        Span<StrategyContext> contexts)
        => DispatchHierarchical(in market, tree, world, contexts, StrategyDispatchKind.Bar);

    internal static void DispatchQuotesHierarchical(
        in MarketKernel market,
        StrategyTree tree,
        WorldState world,
        Span<StrategyContext> contexts,
        in QuoteReceived evt,
        int assetRangeStart,
        int assetRangeLength)
        => DispatchHierarchical(
            in market,
            tree,
            world,
            contexts,
            StrategyDispatchKind.Quote,
            evt,
            null,
            null,
            null,
            null,
            assetRangeStart,
            assetRangeLength);

    internal static void DispatchTradesHierarchical(
        in MarketKernel market,
        StrategyTree tree,
        WorldState world,
        Span<StrategyContext> contexts,
        in TradeOccurred evt,
        int assetRangeStart,
        int assetRangeLength)
        => DispatchHierarchical(
            in market,
            tree,
            world,
            contexts,
            StrategyDispatchKind.Trade,
            null,
            evt,
            null,
            null,
            null,
            assetRangeStart,
            assetRangeLength);

    internal static void DispatchBooksHierarchical(
        in MarketKernel market,
        StrategyTree tree,
        WorldState world,
        Span<StrategyContext> contexts,
        in BookSnapshotReceived evt,
        int assetRangeStart,
        int assetRangeLength)
        => DispatchHierarchical(
            in market,
            tree,
            world,
            contexts,
            StrategyDispatchKind.Book,
            null,
            null,
            evt,
            null,
            null,
            assetRangeStart,
            assetRangeLength);

    internal static void DispatchBookLevelDeltasHierarchical(
        in MarketKernel market,
        StrategyTree tree,
        WorldState world,
        Span<StrategyContext> contexts,
        in BookLevelDeltaReceived evt,
        int assetRangeStart,
        int assetRangeLength)
        => DispatchHierarchical(
            in market,
            tree,
            world,
            contexts,
            StrategyDispatchKind.BookLevelDelta,
            null,
            null,
            null,
            evt,
            null,
            assetRangeStart,
            assetRangeLength);

    internal static void DispatchBookLevelDeltasHierarchical(
        in MarketKernel market,
        StrategyTree tree,
        WorldState world,
        Span<StrategyContext> contexts,
        in BookLevelDeltasReceived evt,
        int assetRangeStart,
        int assetRangeLength)
        => DispatchHierarchical(
            in market,
            tree,
            world,
            contexts,
            StrategyDispatchKind.BookLevelDeltas,
            null,
            null,
            null,
            null,
            evt,
            assetRangeStart,
            assetRangeLength);

    internal static void DispatchGroupOnlyHierarchical(
        in MarketKernel market,
        StrategyTree tree,
        WorldState world,
        Span<StrategyContext> contexts)
    {
        Span<AllocationCommand> phaseCommands = stackalloc AllocationCommand[1024];
        for (var depth = 1; depth <= tree.MaxDepth; depth++)
        {
            var phaseCommandCount = 0;

            foreach (ref var context in contexts)
            {
                if (context.Node.Depth != depth) continue;

                try
                {
                    Span<AllocationCommand> commands = stackalloc AllocationCommand[32];
                    var counters = context.Counters.AsSpan();
                    counters.Clear();
                    var orderIntents = context.OrderIntents.AsSpan();
                    orderIntents.Clear();
                    var childSnapshots = BuildChildSnapshots(world, context.Node, market.UniverseSize, context.ChildSnapshots, market.Time);
                    var portfolio = world.BuildContext(
                        context.Node.Id,
                        context.Node.ParentId,
                        context.Node.ChildIds.Span,
                        counters,
                        commands,
                        childSnapshots,
                        orderIntents);

                    if (portfolio.IsPaused) continue;

                    context.Strategy.RunGroupGuarded(in market, ref portfolio);
                    foreach (var command in portfolio.DrainCommands())
                    {
                        if (phaseCommandCount >= phaseCommands.Length)
                            throw new InvalidOperationException("Phase allocation command buffer is full.");

                        phaseCommands[phaseCommandCount++] = command;
                    }

                    world.CommitContext(context.Node.Id, ref portfolio);
                }
                catch (StrategyExecutionInvariantException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    context.Strategy.OnError(ex);
                }
            }

            ApplyAllocationCommands(world, phaseCommands[..phaseCommandCount]);
        }
    }

    private static void DispatchHierarchical(
        in MarketKernel market,
        StrategyTree tree,
        WorldState world,
        Span<StrategyContext> contexts,
        StrategyDispatchKind kind,
        QuoteReceived? quote = null,
        TradeOccurred? trade = null,
        BookSnapshotReceived? book = null,
        BookLevelDeltaReceived? bookDelta = null,
        BookLevelDeltasReceived? bookDeltas = null,
        int assetRangeStart = 0,
        int assetRangeLength = int.MaxValue)
    {
        var maxDepth = tree.MaxDepth;
        Span<AllocationCommand> phaseCommands = stackalloc AllocationCommand[1024];
        var phaseCommandCount = 0;

        for (var depth = 0; depth <= maxDepth; depth++)
        {
            phaseCommandCount = 0;

            foreach (ref var context in contexts)
            {
                if (context.Node.Depth != depth) continue;

                try
                {
                    Span<AllocationCommand> commands = stackalloc AllocationCommand[32];
                    var counters = context.Counters.AsSpan();
                    counters.Clear();
                    var orderIntents = context.OrderIntents.AsSpan();
                    orderIntents.Clear();
                    var childSnapshots = BuildChildSnapshots(world, context.Node, market.UniverseSize, context.ChildSnapshots, market.Time);
                    var portfolio = world.BuildContext(
                        context.Node.Id,
                        context.Node.ParentId,
                        context.Node.ChildIds.Span,
                        counters,
                        commands,
                        childSnapshots,
                        orderIntents);

                    if (portfolio.IsPaused) continue;

                    RunMarketDispatch(
                        context.Strategy,
                        kind,
                        in market,
                        ref portfolio,
                        quote,
                        trade,
                        book,
                        bookDelta,
                        bookDeltas,
                        assetRangeStart,
                        assetRangeLength);
                    foreach (var command in portfolio.DrainCommands())
                    {
                        if (phaseCommandCount >= phaseCommands.Length)
                            throw new InvalidOperationException("Phase allocation command buffer is full.");

                        phaseCommands[phaseCommandCount++] = command;
                    }

                    world.CommitContext(context.Node.Id, ref portfolio);
                }
                catch (StrategyExecutionInvariantException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    context.Strategy.OnError(ex);
                }
            }

            ApplyAllocationCommands(world, phaseCommands[..phaseCommandCount]);
        }
    }

    internal static void DispatchHierarchicalParallel(
        RhodiumRuntime runtime,
        StrategyTree tree,
        ParallelDispatchState state)
        => DispatchHierarchicalParallel(runtime, tree, state, StrategyDispatchKind.Tick);

    internal static void DispatchBarsHierarchicalParallel(
        RhodiumRuntime runtime,
        StrategyTree tree,
        ParallelDispatchState state)
        => DispatchHierarchicalParallel(runtime, tree, state, StrategyDispatchKind.Bar);

    private static void DispatchHierarchicalParallel(
        RhodiumRuntime runtime,
        StrategyTree tree,
        ParallelDispatchState state,
        StrategyDispatchKind kind)
    {
        if (state.AllDepthsBelowThreshold())
        {
            state.MarkSequentialExecution();
            var market = runtime.CreateMarketKernel();
            DispatchHierarchical(in market, tree, runtime.WorldState, state.MutableContexts, kind);
            return;
        }

        for (var depth = 0; depth <= tree.MaxDepth; depth++)
        {
            var indices = state.GetIndicesAtDepth(depth);
            if (indices.Length == 0) continue;

            if (indices.Length < state.ParallelThreshold)
            {
                ExecuteDepthSequential(runtime, state, indices, kind);
            }
            else
            {
                ExecuteDepthParallel(runtime, state, indices, kind);
                ApplyDepthCommands(runtime.WorldState, state, indices);
            }
        }
    }

    private static void ExecuteDepthSequential(
        RhodiumRuntime runtime,
        ParallelDispatchState state,
        int[] indices,
        StrategyDispatchKind kind)
    {
        state.MarkSequentialExecution();
        var market = runtime.CreateMarketKernel();
        Span<AllocationCommand> phaseCommands = stackalloc AllocationCommand[1024];
        var phaseCommandCount = 0;

        foreach (var contextIndex in indices)
        {
            var context = state.Contexts[contextIndex];

            try
            {
                Span<AllocationCommand> commands = stackalloc AllocationCommand[32];
                var counters = context.Counters.AsSpan();
                counters.Clear();
                var orderIntents = context.OrderIntents.AsSpan();
                orderIntents.Clear();
                var childSnapshots = BuildChildSnapshots(runtime.WorldState, context.Node, market.UniverseSize, context.ChildSnapshots, market.Time);
                var portfolio = runtime.WorldState.BuildContext(
                    context.Node.Id,
                    context.Node.ParentId,
                    context.Node.ChildIds.Span,
                    counters,
                    commands,
                    childSnapshots,
                    orderIntents);

                if (portfolio.IsPaused) continue;

                RunMarketDispatch(context.Strategy, kind, in market, ref portfolio);
                foreach (var command in portfolio.DrainCommands())
                {
                    if (phaseCommandCount >= phaseCommands.Length)
                        throw new InvalidOperationException("Phase allocation command buffer is full.");

                    phaseCommands[phaseCommandCount++] = command;
                }

                runtime.WorldState.CommitContext(context.Node.Id, ref portfolio);
            }
            catch (StrategyExecutionInvariantException)
            {
                throw;
            }
            catch (Exception ex)
            {
                context.Strategy.OnError(ex);
            }
        }

        ApplyAllocationCommands(runtime.WorldState, phaseCommands[..phaseCommandCount]);
    }

    private static void ExecuteDepthParallel(
        RhodiumRuntime runtime,
        ParallelDispatchState state,
        int[] indices,
        StrategyDispatchKind kind)
    {
        runtime.WorldState.Pin();
        try
        {
            state.ExecuteParallel(runtime, indices, kind);
        }
        finally
        {
            runtime.WorldState.Unpin();
        }
    }

    internal static void ExecuteContext(
        RhodiumRuntime runtime,
        in MarketKernel market,
        ParallelDispatchState state,
        int contextIndex,
        StrategyDispatchKind kind = StrategyDispatchKind.Tick)
    {
        var context = state.Contexts[contextIndex];
        state.ResetCommandCount(contextIndex);
        var commands = state.GetCommandBuffer(contextIndex);
        var counters = context.Counters.AsSpan();
        counters.Clear();
        var orderIntents = context.OrderIntents.AsSpan();
        orderIntents.Clear();
        var childSnapshots = BuildChildSnapshots(runtime.WorldState, context.Node, market.UniverseSize, context.ChildSnapshots, market.Time);
        var portfolio = runtime.WorldState.BuildContext(
            context.Node.Id,
            context.Node.ParentId,
            context.Node.ChildIds.Span,
            counters,
            commands,
            childSnapshots,
            orderIntents);

        if (portfolio.IsPaused) return;

        try
        {
            RunMarketDispatch(context.Strategy, kind, in market, ref portfolio);
            var drained = portfolio.DrainCommands();
            state.SetCommandCount(contextIndex, drained.Length);
            runtime.WorldState.CommitContext(context.Node.Id, ref portfolio);
        }
        catch (StrategyExecutionInvariantException)
        {
            throw;
        }
        catch (Exception ex)
        {
            context.Strategy.OnError(ex);
        }
    }

    private static void ApplyDepthCommands(
        WorldState world,
        ParallelDispatchState state,
        int[] indices)
    {
        foreach (var contextIndex in indices)
        {
            var commands = state.GetCommandBuffer(contextIndex);
            var count = state.GetCommandCount(contextIndex);
            for (var i = 0; i < count; i++)
                world.ApplyAllocationCommand(commands[i]);
        }
    }

    private static ReadOnlySpan<PortfolioSnapshot> BuildChildSnapshots(
        WorldState world,
        StrategyNode node,
        int universeSize,
        PortfolioSnapshot[] snapshotBuffer,
        Instant snapshotTime)
    {
        if (node.ChildIds.IsEmpty) return default;
        if (snapshotBuffer.Length < node.ChildIds.Length)
            throw new InvalidOperationException("Strategy context child snapshot buffer is smaller than its child set.");

        for (var i = 0; i < node.ChildIds.Length; i++)
            snapshotBuffer[i] = world.BuildSnapshot(node.ChildIds.Span[i], universeSize, snapshotTime);

        return snapshotBuffer.AsSpan(0, node.ChildIds.Length);
    }

    private static void ApplyAllocationCommands(
        WorldState world,
        ReadOnlySpan<AllocationCommand> commands,
        Span<StrategyContext> contexts = default)
    {
        foreach (var command in commands)
            world.ApplyAllocationCommand(command);
    }

    private static void RunMarketDispatch(
        Strategy strategy,
        StrategyDispatchKind kind,
        in MarketKernel market,
        ref PortfolioContext portfolio,
        QuoteReceived? quote = null,
        TradeOccurred? trade = null,
        BookSnapshotReceived? book = null,
        BookLevelDeltaReceived? bookDelta = null,
        BookLevelDeltasReceived? bookDeltas = null,
        int assetRangeStart = 0,
        int assetRangeLength = int.MaxValue)
    {
        switch (kind)
        {
            case StrategyDispatchKind.Quote:
                strategy.RunQuoteGuarded(in market, ref portfolio, quote!, assetRangeStart, assetRangeLength);
                break;
            case StrategyDispatchKind.Trade:
                strategy.RunTradeGuarded(in market, ref portfolio, trade!, assetRangeStart, assetRangeLength);
                break;
            case StrategyDispatchKind.Book:
                strategy.RunBookSnapshotGuarded(in market, ref portfolio, book!, assetRangeStart, assetRangeLength);
                break;
            case StrategyDispatchKind.BookLevelDelta:
                strategy.RunBookLevelDeltaGuarded(in market, ref portfolio, bookDelta!, assetRangeStart, assetRangeLength);
                break;
            case StrategyDispatchKind.BookLevelDeltas:
                strategy.RunBookLevelDeltasGuarded(in market, ref portfolio, bookDeltas!, assetRangeStart, assetRangeLength);
                break;
            case StrategyDispatchKind.Bar:
                strategy.RunBarGuarded(in market, ref portfolio);
                break;
            case StrategyDispatchKind.Tick:
            default:
                strategy.RunTickGuarded(in market, ref portfolio);
                break;
        }
    }
}
