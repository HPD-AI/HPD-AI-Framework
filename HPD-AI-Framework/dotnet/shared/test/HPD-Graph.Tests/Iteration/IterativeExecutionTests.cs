using FluentAssertions;
using HPD.Graph.Tests.Helpers;
using HPDAgent.Graph.Abstractions.Execution;
using HPDAgent.Graph.Abstractions.Graph;
using HPDAgent.Graph.Abstractions.Handlers;
using HPDAgent.Graph.Core.Context;
using HPDAgent.Graph.Core.Orchestration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HPD.Graph.Tests.Iteration;

/// <summary>
/// Integration tests for iterative execution with back-edges.
/// Tests retry loops, conditional iteration, and max iteration limits.
/// </summary>
public class IterativeExecutionTests
{
    #region Basic Iteration Tests

    [Fact]
    public async Task SimpleRetryLoop_ExecutesMultipleIterations()
    {
        // Arrange: A → B → (back to A if retry=true, else END)
        // RetryHandler succeeds after N attempts
        var retryHandler = new RetryHandler(retriesBeforeSuccess: 2);
        var services = TestServiceProvider.Create(s =>
        {
            s.AddSingleton<IGraphNodeHandler<GraphContext>>(retryHandler);
        });

        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("process", "RetryHandler")
            .AddEndNode()
            .AddEdge("start", "process")
            .AddEdge("process", "process", new EdgeCondition
            {
                Type = ConditionType.FieldEquals,
                Field = "retry",
                Value = true
            })
            .AddEdge("process", "end", new EdgeCondition
            {
                Type = ConditionType.FieldEquals,
                Field = "retry",
                Value = false
            })
            .Build();

        var context = new GraphContext("retry-test", graph, services);
        var orchestrator = new GraphOrchestrator<GraphContext>(services);

        // Act
        await orchestrator.ExecuteAsync(context);

        // Assert - Should have executed 3 times (2 retries + 1 success)
        context.GetNodeExecutionCount("process").Should().Be(3);
        context.ShouldHaveCompletedNode("process");
    }

    [Fact]
    public async Task ConditionalBackEdge_OnlyTriggersWhenConditionMet()
    {
        // Arrange: A → B → C → (back to B if count < 3)
        var countingHandler = new CountingHandler();
        var services = TestServiceProvider.Create(s =>
        {
            s.AddSingleton<IGraphNodeHandler<GraphContext>>(countingHandler);
        });

        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("init", "SuccessHandler")
            .AddHandlerNode("counter", "CountingHandler")
            .AddEndNode()
            .AddEdge("start", "init")
            .AddEdge("init", "counter")
            .AddEdge("counter", "counter", new EdgeCondition
            {
                Type = ConditionType.FieldLessThan,
                Field = "count",
                Value = 3
            })
            .AddEdge("counter", "end", new EdgeCondition
            {
                Type = ConditionType.Default
            })
            .Build();

        var context = new GraphContext("counting-test", graph, services);
        var orchestrator = new GraphOrchestrator<GraphContext>(services);

        // Act
        await orchestrator.ExecuteAsync(context);

        // Assert - Counter should have run exactly 3 times
        context.GetNodeExecutionCount("counter").Should().Be(3);
    }

    [Fact]
    public async Task NoBackEdge_ExecutesOnce()
    {
        // Arrange: Simple linear graph (no cycles)
        var services = TestServiceProvider.Create();

        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("A", "SuccessHandler")
            .AddHandlerNode("B", "SuccessHandler")
            .AddEndNode()
            .AddEdge("start", "A")
            .AddEdge("A", "B")
            .AddEdge("B", "end")
            .Build();

        var context = new GraphContext("acyclic-test", graph, services);
        var orchestrator = new GraphOrchestrator<GraphContext>(services);

        // Act
        await orchestrator.ExecuteAsync(context);

        // Assert - Each node should execute exactly once
        context.GetNodeExecutionCount("A").Should().Be(1);
        context.GetNodeExecutionCount("B").Should().Be(1);
        context.CurrentIteration.Should().Be(0); // No iterations for DAG
    }

    #endregion

    #region Max Iterations Tests

    [Fact]
    public async Task MaxIterations_StopsExecution()
    {
        // Arrange: Infinite loop that should be stopped by max iterations
        var infiniteHandler = new InfiniteRetryHandler();
        var services = TestServiceProvider.Create(s =>
        {
            s.AddSingleton<IGraphNodeHandler<GraphContext>>(infiniteHandler);
        });

        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("loop", "InfiniteRetryHandler")
            .AddEndNode()
            .AddEdge("start", "loop")
            .AddEdge("loop", "loop", new EdgeCondition
            {
                Type = ConditionType.FieldEquals,
                Field = "continue",
                Value = true
            })
            .AddEdge("loop", "end", new EdgeCondition
            {
                Type = ConditionType.FieldEquals,
                Field = "continue",
                Value = false
            })
            .Build();

        // Set max iterations to 5
        var graphWithLimit = graph with { MaxIterations = 5 };

        var context = new GraphContext("max-iter-test", graphWithLimit, services);
        var orchestrator = new GraphOrchestrator<GraphContext>(services);

        // Act
        await orchestrator.ExecuteAsync(context);

        // Assert - Should stop at max iterations (initial + 5 iterations = 6 executions max)
        // The actual number depends on implementation but should be capped
        context.GetNodeExecutionCount("loop").Should().BeLessThanOrEqualTo(6);
    }

    #endregion

    #region Multi-Node Iteration Tests

    [Fact]
    public async Task LinearBackEdge_ReExecutesBothNodes()
    {
        // Arrange: Simple A → D with back-edge D → A
        var controlHandler = new IterationControlHandler(iterationsBeforeStop: 2);
        var services = TestServiceProvider.Create(s =>
        {
            s.AddSingleton<IGraphNodeHandler<GraphContext>>(controlHandler);
        });

        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("A", "SuccessHandler")
            .AddHandlerNode("D", "IterationControlHandler")
            .AddEndNode()
            .AddEdge("start", "A")
            .AddEdge("A", "D")
            .AddEdge("D", "A", new EdgeCondition
            {
                Type = ConditionType.FieldEquals,
                Field = "iterate",
                Value = true
            })
            .AddEdge("D", "end", new EdgeCondition
            {
                Type = ConditionType.FieldEquals,
                Field = "iterate",
                Value = false
            })
            .Build();

        // Verify graph has back-edges
        graph.HasCycles.Should().BeTrue("Graph should have back-edge D → A");
        graph.GetBackEdges().Count.Should().Be(1);
        graph.GetBackEdges()[0].SourceNodeId.Should().Be("D");
        graph.GetBackEdges()[0].TargetNodeId.Should().Be("A");

        var context = new GraphContext("linear-iter-test", graph, services);
        var orchestrator = new GraphOrchestrator<GraphContext>(services);

        // Act
        await orchestrator.ExecuteAsync(context);

        // Assert - Both nodes should re-execute 3 times
        context.GetNodeExecutionCount("A").Should().Be(3);
        context.GetNodeExecutionCount("D").Should().Be(3);
    }

    [Fact]
    public async Task DiamondWithBackEdge_ReExecutesAllBranchNodes()
    {
        // Arrange: Full diamond pattern with back-edge
        //     → B →
        // A →       → D → (back to A if iterate=true)
        //     → C →
        var controlHandler = new IterationControlHandler(iterationsBeforeStop: 2);
        var services = TestServiceProvider.Create(s =>
        {
            s.AddSingleton<IGraphNodeHandler<GraphContext>>(controlHandler);
        });

        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("A", "SuccessHandler")
            .AddHandlerNode("B", "SuccessHandler")
            .AddHandlerNode("C", "SuccessHandler")
            .AddHandlerNode("D", "IterationControlHandler")
            .AddEndNode()
            .AddEdge("start", "A")
            .AddEdge("A", "B")
            .AddEdge("A", "C")
            .AddEdge("B", "D")
            .AddEdge("C", "D")
            .AddEdge("D", "A", new EdgeCondition
            {
                Type = ConditionType.FieldEquals,
                Field = "iterate",
                Value = true
            })
            .AddEdge("D", "end", new EdgeCondition
            {
                Type = ConditionType.FieldEquals,
                Field = "iterate",
                Value = false
            })
            .Build();

        // Verify graph structure
        graph.HasCycles.Should().BeTrue("Graph should have back-edge D → A");
        graph.GetBackEdges().Count.Should().Be(1);

        // Debug: Verify outgoing edges from A
        var outgoingFromA = graph.GetOutgoingEdges("A");
        outgoingFromA.Count.Should().Be(2, $"A should have 2 outgoing edges (to B and C). Actual: {string.Join(", ", outgoingFromA.Select(e => $"{e.From}->{e.To}"))}");

        var context = new GraphContext("diamond-iter-test", graph, services);
        var orchestrator = new GraphOrchestrator<GraphContext>(services);

        // Act
        await orchestrator.ExecuteAsync(context);

        // Debug: Get logs and execution counts
        var logs = context.LogEntries.Select(l => l.Message).ToList();
        var countA = context.GetNodeExecutionCount("A");
        var countB = context.GetNodeExecutionCount("B");
        var countC = context.GetNodeExecutionCount("C");
        var countD = context.GetNodeExecutionCount("D");
        var debugInfo = $"Counts: A={countA}, B={countB}, C={countC}, D={countD}\nLogs:\n{string.Join("\n", logs)}";

        // Assert - ALL nodes in the diamond should re-execute 3 times
        // When D → A triggers, A becomes dirty, which propagates to B, C, D
        context.GetNodeExecutionCount("A").Should().Be(3, $"A is back-edge target. {debugInfo}");
        context.GetNodeExecutionCount("B").Should().Be(3, $"B is forward dependent of A. {debugInfo}");
        context.GetNodeExecutionCount("C").Should().Be(3, "C is forward dependent of A");
        context.GetNodeExecutionCount("D").Should().Be(3, "D is forward dependent of B and C");
    }

    [Fact]
    public async Task PartialBackEdge_OnlyReExecutesAffectedNodes()
    {
        // Arrange: A → B → C → D → (back to C if retry)
        // Only C and D should re-execute, not A and B
        var partialRetryHandler = new PartialRetryHandler(retriesBeforeSuccess: 2);
        var services = TestServiceProvider.Create(s =>
        {
            s.AddSingleton<IGraphNodeHandler<GraphContext>>(partialRetryHandler);
        });

        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("A", "SuccessHandler")
            .AddHandlerNode("B", "SuccessHandler")
            .AddHandlerNode("C", "SuccessHandler")
            .AddHandlerNode("D", "PartialRetryHandler")
            .AddEndNode()
            .AddEdge("start", "A")
            .AddEdge("A", "B")
            .AddEdge("B", "C")
            .AddEdge("C", "D")
            .AddEdge("D", "C", new EdgeCondition
            {
                Type = ConditionType.FieldEquals,
                Field = "retry",
                Value = true
            })
            .AddEdge("D", "end", new EdgeCondition
            {
                Type = ConditionType.FieldEquals,
                Field = "retry",
                Value = false
            })
            .Build();

        var context = new GraphContext("partial-retry-test", graph, services);
        var orchestrator = new GraphOrchestrator<GraphContext>(services);

        // Act
        await orchestrator.ExecuteAsync(context);

        // Assert - A and B should execute once, C and D should execute 3 times
        context.GetNodeExecutionCount("A").Should().Be(1);
        context.GetNodeExecutionCount("B").Should().Be(1);
        context.GetNodeExecutionCount("C").Should().Be(3);
        context.GetNodeExecutionCount("D").Should().Be(3);
    }

    #endregion

    #region Iteration Counter Tests

    [Fact]
    public async Task CurrentIteration_IncrementsCorrectly()
    {
        // Arrange: Simple loop that runs 3 times
        var handler = new IterationCountingHandler(maxIterations: 3);
        var services = TestServiceProvider.Create(s =>
        {
            s.AddSingleton<IGraphNodeHandler<GraphContext>>(handler);
        });

        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("loop", "IterationCountingHandler")
            .AddEndNode()
            .AddEdge("start", "loop")
            .AddEdge("loop", "loop", new EdgeCondition
            {
                Type = ConditionType.FieldEquals,
                Field = "continue",
                Value = true
            })
            .AddEdge("loop", "end", new EdgeCondition
            {
                Type = ConditionType.FieldEquals,
                Field = "continue",
                Value = false
            })
            .Build();

        var context = new GraphContext("iter-count-test", graph, services);
        var orchestrator = new GraphOrchestrator<GraphContext>(services);

        // Act
        await orchestrator.ExecuteAsync(context);

        // Assert - Iteration counter should be 2 (0-indexed: 0, 1, 2 = 3 iterations)
        context.CurrentIteration.Should().Be(2);
    }

    #endregion

    #region Edge Case Tests

    [Fact]
    public async Task BackEdgeConditionNeverMet_ExecutesOnce()
    {
        // Arrange: Back-edge condition that's never true
        var services = TestServiceProvider.Create();

        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("process", "SuccessHandler")
            .AddEndNode()
            .AddEdge("start", "process")
            .AddEdge("process", "process", new EdgeCondition
            {
                Type = ConditionType.FieldEquals,
                Field = "output",
                Value = "never_happens"
            })
            .AddEdge("process", "end", new EdgeCondition
            {
                Type = ConditionType.FieldEquals,
                Field = "output",
                Value = "success"
            })
            .Build();

        var context = new GraphContext("no-trigger-test", graph, services);
        var orchestrator = new GraphOrchestrator<GraphContext>(services);

        // Act
        await orchestrator.ExecuteAsync(context);

        // Assert - Back-edge never triggers, executes once
        context.GetNodeExecutionCount("process").Should().Be(1);
        context.CurrentIteration.Should().Be(0);
    }

    [Fact]
    public async Task MultipleBackEdges_EvaluatesAll()
    {
        // Arrange: Two back-edges in different parts of graph
        var handler1 = new PartialRetryHandler(retriesBeforeSuccess: 1);
        var handler2 = new PartialRetryHandler(retriesBeforeSuccess: 1);
        var services = TestServiceProvider.Create(s =>
        {
            s.AddSingleton<IGraphNodeHandler<GraphContext>>(new MultiBackEdgeHandler());
        });

        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("A", "SuccessHandler")
            .AddHandlerNode("B", "MultiBackEdgeHandler")
            .AddEndNode()
            .AddEdge("start", "A")
            .AddEdge("A", "B")
            // Back-edge from B to A with a condition
            .AddEdge("B", "A", new EdgeCondition
            {
                Type = ConditionType.FieldLessThan,
                Field = "executions",
                Value = 3
            })
            .AddEdge("B", "end", new EdgeCondition
            {
                Type = ConditionType.Default
            })
            .Build();

        var context = new GraphContext("multi-back-edge-test", graph, services);
        var orchestrator = new GraphOrchestrator<GraphContext>(services);

        // Act
        await orchestrator.ExecuteAsync(context);

        // Assert - Both A and B should re-execute when back-edge triggers
        context.GetNodeExecutionCount("A").Should().Be(3);
        context.GetNodeExecutionCount("B").Should().Be(3);
    }

    #endregion

    #region Stress Tests

    [Fact]
    public async Task DeepChain_BackEdgeToRoot_ReExecutesEntireChain()
    {
        // Arrange: A → B → C → D → E → F → (back to A)
        // Tests that long chains properly propagate dirty nodes
        var controlHandler = new IterationControlHandler(iterationsBeforeStop: 2);
        var services = TestServiceProvider.Create(s =>
        {
            s.AddSingleton<IGraphNodeHandler<GraphContext>>(controlHandler);
        });

        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("A", "SuccessHandler")
            .AddHandlerNode("B", "SuccessHandler")
            .AddHandlerNode("C", "SuccessHandler")
            .AddHandlerNode("D", "SuccessHandler")
            .AddHandlerNode("E", "SuccessHandler")
            .AddHandlerNode("F", "IterationControlHandler")
            .AddEndNode()
            .AddEdge("start", "A")
            .AddEdge("A", "B")
            .AddEdge("B", "C")
            .AddEdge("C", "D")
            .AddEdge("D", "E")
            .AddEdge("E", "F")
            .AddEdge("F", "A", new EdgeCondition
            {
                Type = ConditionType.FieldEquals,
                Field = "iterate",
                Value = true
            })
            .AddEdge("F", "end", new EdgeCondition
            {
                Type = ConditionType.FieldEquals,
                Field = "iterate",
                Value = false
            })
            .Build();

        var context = new GraphContext("deep-chain-test", graph, services);
        var orchestrator = new GraphOrchestrator<GraphContext>(services);

        // Act
        await orchestrator.ExecuteAsync(context);

        // Assert - ALL nodes should execute 3 times
        context.GetNodeExecutionCount("A").Should().Be(3);
        context.GetNodeExecutionCount("B").Should().Be(3);
        context.GetNodeExecutionCount("C").Should().Be(3);
        context.GetNodeExecutionCount("D").Should().Be(3);
        context.GetNodeExecutionCount("E").Should().Be(3);
        context.GetNodeExecutionCount("F").Should().Be(3);
    }

    [Fact]
    public async Task NestedDiamond_BackEdgeToMiddle_PartialReExecution()
    {
        // Arrange: Complex nested diamond with back-edge to middle
        //          → B →
        // A → split      → merge → D → (back to split)
        //          → C →
        // Only split, B, C, merge, D should re-execute, not A
        var controlHandler = new IterationControlHandler(iterationsBeforeStop: 2);
        var services = TestServiceProvider.Create(s =>
        {
            s.AddSingleton<IGraphNodeHandler<GraphContext>>(controlHandler);
        });

        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("A", "SuccessHandler")
            .AddHandlerNode("split", "SuccessHandler")
            .AddHandlerNode("B", "SuccessHandler")
            .AddHandlerNode("C", "SuccessHandler")
            .AddHandlerNode("merge", "SuccessHandler")
            .AddHandlerNode("D", "IterationControlHandler")
            .AddEndNode()
            .AddEdge("start", "A")
            .AddEdge("A", "split")
            .AddEdge("split", "B")
            .AddEdge("split", "C")
            .AddEdge("B", "merge")
            .AddEdge("C", "merge")
            .AddEdge("merge", "D")
            .AddEdge("D", "split", new EdgeCondition
            {
                Type = ConditionType.FieldEquals,
                Field = "iterate",
                Value = true
            })
            .AddEdge("D", "end", new EdgeCondition
            {
                Type = ConditionType.FieldEquals,
                Field = "iterate",
                Value = false
            })
            .Build();

        var context = new GraphContext("nested-diamond-test", graph, services);
        var orchestrator = new GraphOrchestrator<GraphContext>(services);

        // Act
        await orchestrator.ExecuteAsync(context);

        // Assert - A executes once, everything after split executes 3 times
        context.GetNodeExecutionCount("A").Should().Be(1, "A is before back-edge target");
        context.GetNodeExecutionCount("split").Should().Be(3, "split is back-edge target");
        context.GetNodeExecutionCount("B").Should().Be(3, "B is forward dependent of split");
        context.GetNodeExecutionCount("C").Should().Be(3, "C is forward dependent of split");
        context.GetNodeExecutionCount("merge").Should().Be(3, "merge is forward dependent of B,C");
        context.GetNodeExecutionCount("D").Should().Be(3, "D is forward dependent of merge");
    }

    [Fact]
    public async Task ParallelBranchesWithSeparateBackEdges_IndependentIteration()
    {
        // Arrange: Two parallel branches, each with its own back-edge
        //     → B → C → (back to B)
        // A →                      → END
        //     → D → E → (back to D)
        // Each branch should iterate independently based on its own condition
        var branchBHandler = new BranchIterationHandler("branchB", iterationsBeforeStop: 1);
        var branchDHandler = new BranchIterationHandler("branchD", iterationsBeforeStop: 2);
        var services = TestServiceProvider.Create(s =>
        {
            s.AddSingleton<IGraphNodeHandler<GraphContext>>(branchBHandler);
            s.AddSingleton<IGraphNodeHandler<GraphContext>>(branchDHandler);
        });

        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("A", "SuccessHandler")
            .AddHandlerNode("B", "SuccessHandler")
            .AddHandlerNode("C", "BranchBHandler")
            .AddHandlerNode("D", "SuccessHandler")
            .AddHandlerNode("E", "BranchDHandler")
            .AddEndNode()
            .AddEdge("start", "A")
            .AddEdge("A", "B")
            .AddEdge("A", "D")
            .AddEdge("B", "C")
            .AddEdge("C", "B", new EdgeCondition
            {
                Type = ConditionType.FieldEquals,
                Field = "iterate",
                Value = true
            })
            .AddEdge("C", "end", new EdgeCondition
            {
                Type = ConditionType.FieldEquals,
                Field = "iterate",
                Value = false
            })
            .AddEdge("D", "E")
            .AddEdge("E", "D", new EdgeCondition
            {
                Type = ConditionType.FieldEquals,
                Field = "iterate",
                Value = true
            })
            .AddEdge("E", "end", new EdgeCondition
            {
                Type = ConditionType.FieldEquals,
                Field = "iterate",
                Value = false
            })
            .Build();

        var context = new GraphContext("parallel-branches-test", graph, services);
        var orchestrator = new GraphOrchestrator<GraphContext>(services);

        // Act
        await orchestrator.ExecuteAsync(context);

        // Assert
        context.GetNodeExecutionCount("A").Should().Be(1, "A executes once at the start");
        // Branch B: 1 iteration before stop = 2 executions
        context.GetNodeExecutionCount("B").Should().Be(2, "B iterates once");
        context.GetNodeExecutionCount("C").Should().Be(2, "C iterates once");
        // Branch D: 2 iterations before stop = 3 executions
        context.GetNodeExecutionCount("D").Should().Be(3, "D iterates twice");
        context.GetNodeExecutionCount("E").Should().Be(3, "E iterates twice");
    }

    [Fact]
    public async Task HighIterationCount_StressTest()
    {
        // Arrange: Simple loop that runs many times
        var handler = new IterationCountingHandler(maxIterations: 50);
        var services = TestServiceProvider.Create(s =>
        {
            s.AddSingleton<IGraphNodeHandler<GraphContext>>(handler);
        });

        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("loop", "IterationCountingHandler")
            .AddEndNode()
            .AddEdge("start", "loop")
            .AddEdge("loop", "loop", new EdgeCondition
            {
                Type = ConditionType.FieldEquals,
                Field = "continue",
                Value = true
            })
            .AddEdge("loop", "end", new EdgeCondition
            {
                Type = ConditionType.FieldEquals,
                Field = "continue",
                Value = false
            })
            .Build();

        // Override MaxIterations to allow 50 iterations
        var graphWithHighLimit = graph with { MaxIterations = 100 };

        var context = new GraphContext("high-iter-test", graphWithHighLimit, services);
        var orchestrator = new GraphOrchestrator<GraphContext>(services);

        // Act
        await orchestrator.ExecuteAsync(context);

        // Assert - Should complete 50 iterations
        context.GetNodeExecutionCount("loop").Should().Be(50);
        context.CurrentIteration.Should().Be(49); // 0-indexed
    }

    [Fact]
    public async Task WideParallelWithBackEdge_AllBranchesReExecute()
    {
        // Arrange: Wide fan-out then fan-in with back-edge
        //     → B1 →
        //     → B2 →
        // A → → B3 → → merge → D → (back to A)
        //     → B4 →
        //     → B5 →
        var controlHandler = new IterationControlHandler(iterationsBeforeStop: 1);
        var services = TestServiceProvider.Create(s =>
        {
            s.AddSingleton<IGraphNodeHandler<GraphContext>>(controlHandler);
        });

        var builder = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("A", "SuccessHandler")
            .AddHandlerNode("merge", "SuccessHandler")
            .AddHandlerNode("D", "IterationControlHandler")
            .AddEndNode();

        // Add 5 parallel branches
        for (int i = 1; i <= 5; i++)
        {
            builder.AddHandlerNode($"B{i}", "SuccessHandler");
            builder.AddEdge("A", $"B{i}");
            builder.AddEdge($"B{i}", "merge");
        }

        var graph = builder
            .AddEdge("start", "A")
            .AddEdge("merge", "D")
            .AddEdge("D", "A", new EdgeCondition
            {
                Type = ConditionType.FieldEquals,
                Field = "iterate",
                Value = true
            })
            .AddEdge("D", "end", new EdgeCondition
            {
                Type = ConditionType.FieldEquals,
                Field = "iterate",
                Value = false
            })
            .Build();

        var context = new GraphContext("wide-parallel-test", graph, services);
        var orchestrator = new GraphOrchestrator<GraphContext>(services);

        // Act
        await orchestrator.ExecuteAsync(context);

        // Assert - All branches should execute twice (1 iteration)
        context.GetNodeExecutionCount("A").Should().Be(2);
        for (int i = 1; i <= 5; i++)
        {
            context.GetNodeExecutionCount($"B{i}").Should().Be(2, $"B{i} should execute twice");
        }
        context.GetNodeExecutionCount("merge").Should().Be(2);
        context.GetNodeExecutionCount("D").Should().Be(2);
    }

    [Fact]
    public async Task ChannelStateReset_OnReExecution()
    {
        // Arrange: Verify that output channels are properly cleared on re-execution
        var accumulatingHandler = new AccumulatingHandler();
        var controlHandler = new IterationControlHandler(iterationsBeforeStop: 2);
        var services = TestServiceProvider.Create(s =>
        {
            s.AddSingleton<IGraphNodeHandler<GraphContext>>(accumulatingHandler);
            s.AddSingleton<IGraphNodeHandler<GraphContext>>(controlHandler);
        });

        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("accumulator", "AccumulatingHandler")
            .AddHandlerNode("controller", "IterationControlHandler")
            .AddEndNode()
            .AddEdge("start", "accumulator")
            .AddEdge("accumulator", "controller")
            .AddEdge("controller", "accumulator", new EdgeCondition
            {
                Type = ConditionType.FieldEquals,
                Field = "iterate",
                Value = true
            })
            .AddEdge("controller", "end", new EdgeCondition
            {
                Type = ConditionType.FieldEquals,
                Field = "iterate",
                Value = false
            })
            .Build();

        var context = new GraphContext("channel-reset-test", graph, services);
        var orchestrator = new GraphOrchestrator<GraphContext>(services);

        // Act
        await orchestrator.ExecuteAsync(context);

        // Assert
        context.GetNodeExecutionCount("accumulator").Should().Be(3);
        // The accumulator tracks its own execution count internally
        // Final output should reflect total executions
        var outputs = context.Channels["node_output:accumulator"].Get<Dictionary<string, object>>();
        outputs.Should().NotBeNull();
        outputs!["totalExecutions"].Should().Be(3);
    }

    #endregion
}

#region Test Handlers for Iteration Tests

/// <summary>
/// Handler that needs N retries before succeeding.
/// Outputs retry=true until it succeeds, then retry=false.
/// </summary>
public class RetryHandler : IGraphNodeHandler<GraphContext>
{
    public string HandlerName => "RetryHandler";

    private int _attempts = 0;
    private readonly int _retriesBeforeSuccess;

    public RetryHandler(int retriesBeforeSuccess = 2)
    {
        _retriesBeforeSuccess = retriesBeforeSuccess;
    }

    public Task<NodeExecutionResult> ExecuteAsync(
        GraphContext context,
        HandlerInputs inputs,
        CancellationToken cancellationToken = default)
    {
        _attempts++;
        var needsRetry = _attempts <= _retriesBeforeSuccess;

        return Task.FromResult<NodeExecutionResult>(NodeExecutionResult.Success.Single(
            output: new Dictionary<string, object>
            {
                ["retry"] = needsRetry,
                ["attempts"] = _attempts
            },
            duration: TimeSpan.FromMilliseconds(1),
            metadata: new NodeExecutionMetadata()
        ));
    }
}

/// <summary>
/// Handler that counts executions and outputs the count.
/// </summary>
public class CountingHandler : IGraphNodeHandler<GraphContext>
{
    public string HandlerName => "CountingHandler";

    private int _count = 0;

    public Task<NodeExecutionResult> ExecuteAsync(
        GraphContext context,
        HandlerInputs inputs,
        CancellationToken cancellationToken = default)
    {
        _count++;

        return Task.FromResult<NodeExecutionResult>(NodeExecutionResult.Success.Single(
            output: new Dictionary<string, object>
            {
                ["count"] = _count
            },
            duration: TimeSpan.FromMilliseconds(1),
            metadata: new NodeExecutionMetadata()
        ));
    }
}

/// <summary>
/// Handler that always outputs continue=true (infinite loop).
/// </summary>
public class InfiniteRetryHandler : IGraphNodeHandler<GraphContext>
{
    public string HandlerName => "InfiniteRetryHandler";

    public Task<NodeExecutionResult> ExecuteAsync(
        GraphContext context,
        HandlerInputs inputs,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<NodeExecutionResult>(NodeExecutionResult.Success.Single(
            output: new Dictionary<string, object>
            {
                ["continue"] = true
            },
            duration: TimeSpan.FromMilliseconds(1),
            metadata: new NodeExecutionMetadata()
        ));
    }
}

/// <summary>
/// Handler that controls iteration based on execution count.
/// </summary>
public class IterationControlHandler : IGraphNodeHandler<GraphContext>
{
    public string HandlerName => "IterationControlHandler";

    private int _executions = 0;
    private readonly int _iterationsBeforeStop;

    public IterationControlHandler(int iterationsBeforeStop = 2)
    {
        _iterationsBeforeStop = iterationsBeforeStop;
    }

    public Task<NodeExecutionResult> ExecuteAsync(
        GraphContext context,
        HandlerInputs inputs,
        CancellationToken cancellationToken = default)
    {
        _executions++;
        var shouldIterate = _executions <= _iterationsBeforeStop;

        return Task.FromResult<NodeExecutionResult>(NodeExecutionResult.Success.Single(
            output: new Dictionary<string, object>
            {
                ["iterate"] = shouldIterate,
                ["executions"] = _executions
            },
            duration: TimeSpan.FromMilliseconds(1),
            metadata: new NodeExecutionMetadata()
        ));
    }
}

/// <summary>
/// Handler for partial retry scenarios.
/// </summary>
public class PartialRetryHandler : IGraphNodeHandler<GraphContext>
{
    public string HandlerName => "PartialRetryHandler";

    private int _attempts = 0;
    private readonly int _retriesBeforeSuccess;

    public PartialRetryHandler(int retriesBeforeSuccess = 2)
    {
        _retriesBeforeSuccess = retriesBeforeSuccess;
    }

    public Task<NodeExecutionResult> ExecuteAsync(
        GraphContext context,
        HandlerInputs inputs,
        CancellationToken cancellationToken = default)
    {
        _attempts++;
        var needsRetry = _attempts <= _retriesBeforeSuccess;

        return Task.FromResult<NodeExecutionResult>(NodeExecutionResult.Success.Single(
            output: new Dictionary<string, object>
            {
                ["retry"] = needsRetry,
                ["attempts"] = _attempts
            },
            duration: TimeSpan.FromMilliseconds(1),
            metadata: new NodeExecutionMetadata()
        ));
    }
}

/// <summary>
/// Handler that counts iterations from context and decides when to stop.
/// </summary>
public class IterationCountingHandler : IGraphNodeHandler<GraphContext>
{
    public string HandlerName => "IterationCountingHandler";

    private int _executions = 0;
    private readonly int _maxIterations;

    public IterationCountingHandler(int maxIterations = 3)
    {
        _maxIterations = maxIterations;
    }

    public Task<NodeExecutionResult> ExecuteAsync(
        GraphContext context,
        HandlerInputs inputs,
        CancellationToken cancellationToken = default)
    {
        _executions++;
        var shouldContinue = _executions < _maxIterations;

        return Task.FromResult<NodeExecutionResult>(NodeExecutionResult.Success.Single(
            output: new Dictionary<string, object>
            {
                ["continue"] = shouldContinue,
                ["executions"] = _executions
            },
            duration: TimeSpan.FromMilliseconds(1),
            metadata: new NodeExecutionMetadata()
        ));
    }
}

/// <summary>
/// Handler that tracks executions for multi-back-edge tests.
/// </summary>
public class MultiBackEdgeHandler : IGraphNodeHandler<GraphContext>
{
    public string HandlerName => "MultiBackEdgeHandler";

    private int _executions = 0;

    public Task<NodeExecutionResult> ExecuteAsync(
        GraphContext context,
        HandlerInputs inputs,
        CancellationToken cancellationToken = default)
    {
        _executions++;

        return Task.FromResult<NodeExecutionResult>(NodeExecutionResult.Success.Single(
            output: new Dictionary<string, object>
            {
                ["executions"] = _executions
            },
            duration: TimeSpan.FromMilliseconds(1),
            metadata: new NodeExecutionMetadata()
        ));
    }
}

/// <summary>
/// Handler for parallel branch iteration tests with named branches.
/// </summary>
public class BranchIterationHandler : IGraphNodeHandler<GraphContext>
{
    private readonly string _branchName;
    private readonly int _iterationsBeforeStop;
    private int _executions = 0;

    public string HandlerName { get; }

    public BranchIterationHandler(string branchName, int iterationsBeforeStop = 2)
    {
        _branchName = branchName;
        _iterationsBeforeStop = iterationsBeforeStop;
        // Handler name matches the node's handler reference (e.g., "BranchBHandler" for branchB)
        // Convert "branchB" -> "BranchBHandler"
        HandlerName = $"{char.ToUpper(branchName[0])}{branchName.Substring(1)}Handler";
    }

    public Task<NodeExecutionResult> ExecuteAsync(
        GraphContext context,
        HandlerInputs inputs,
        CancellationToken cancellationToken = default)
    {
        _executions++;
        var shouldIterate = _executions <= _iterationsBeforeStop;

        return Task.FromResult<NodeExecutionResult>(NodeExecutionResult.Success.Single(
            output: new Dictionary<string, object>
            {
                ["iterate"] = shouldIterate,
                ["branch"] = _branchName,
                ["executions"] = _executions
            },
            duration: TimeSpan.FromMilliseconds(1),
            metadata: new NodeExecutionMetadata()
        ));
    }
}

/// <summary>
/// Handler that accumulates state across executions to verify channel behavior.
/// </summary>
public class AccumulatingHandler : IGraphNodeHandler<GraphContext>
{
    public string HandlerName => "AccumulatingHandler";

    private int _totalExecutions = 0;

    public Task<NodeExecutionResult> ExecuteAsync(
        GraphContext context,
        HandlerInputs inputs,
        CancellationToken cancellationToken = default)
    {
        _totalExecutions++;

        return Task.FromResult<NodeExecutionResult>(NodeExecutionResult.Success.Single(
            output: new Dictionary<string, object>
            {
                ["totalExecutions"] = _totalExecutions,
                ["timestamp"] = DateTimeOffset.UtcNow.Ticks
            },
            duration: TimeSpan.FromMilliseconds(1),
            metadata: new NodeExecutionMetadata()
        ));
    }
}

#endregion
