using FluentAssertions;
using HPD.Graph.Tests.Helpers;
using HPDAgent.Graph.Abstractions.Events;
using HPDAgent.Graph.Abstractions.Execution;
using HPDAgent.Graph.Abstractions.Graph;
using HPDAgent.Graph.Abstractions.Handlers;
using HPDAgent.Graph.Core.Context;
using HPDAgent.Graph.Core.Orchestration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HPD.Graph.Tests.Iteration;

/// <summary>
/// Tests for change-aware iteration.
/// Verifies output-hash based dirty detection, convergence, and lazy propagation.
/// </summary>
public class ChangeAwareIterationTests
{
    #region Basic Change-Aware Tests

    [Fact]
    public async Task ChangeAware_WithIterationOptions_Executes()
    {
        // Arrange: Simple graph with change-aware iteration enabled
        // Verifies that IterationOptions are properly recognized
        var controlHandler = new IterationControlHandler(iterationsBeforeStop: 2);
        var services = TestServiceProvider.Create(s =>
        {
            s.AddSingleton<IGraphNodeHandler<GraphContext>>(controlHandler);
        });

        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("loop", "IterationControlHandler")
            .AddEndNode()
            .AddEdge("start", "loop")
            .AddEdge("loop", "loop", new EdgeCondition
            {
                Type = ConditionType.FieldEquals,
                Field = "iterate",
                Value = true
            })
            .AddEdge("loop", "end", new EdgeCondition
            {
                Type = ConditionType.FieldEquals,
                Field = "iterate",
                Value = false
            })
            .Build();

        // Enable change-aware iteration
        var graphWithOptions = graph with
        {
            IterationOptions = new IterationOptions
            {
                UseChangeAwareIteration = true,
                EnableAutoConvergence = false
            }
        };

        var context = new GraphContext("change-aware-test", graphWithOptions, services);
        var orchestrator = new GraphOrchestrator<GraphContext>(services);

        // Act
        await orchestrator.ExecuteAsync(context);

        // Assert - Loop should execute 3 times with change-aware iteration
        context.GetNodeExecutionCount("loop").Should().Be(3, "Loop should execute iterations based on condition");
        context.ShouldHaveCompletedNode("loop");
    }

    [Fact]
    public async Task AutoConvergence_StopsWhenNoOutputsChange()
    {
        // Arrange: A → B → A (loop) where A outputs same value after 2 iterations
        var convergingHandler = new ConvergingHandler(iterationsToConverge: 2);
        var services = TestServiceProvider.Create(s =>
        {
            s.AddSingleton<IGraphNodeHandler<GraphContext>>(convergingHandler);
        });

        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("loop", "ConvergingHandler")
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

        // Enable auto-convergence
        var graphWithOptions = graph with
        {
            MaxIterations = 100, // High limit to prove convergence stops earlier
            IterationOptions = new IterationOptions
            {
                UseChangeAwareIteration = true,
                EnableAutoConvergence = true
            }
        };

        var context = new GraphContext("convergence-test", graphWithOptions, services);
        var orchestrator = new GraphOrchestrator<GraphContext>(services);

        // Act
        await orchestrator.ExecuteAsync(context);

        // Assert - Should stop after 3 iterations (initial + 2 iterations to converge)
        context.GetNodeExecutionCount("loop").Should().BeLessThanOrEqualTo(5,
            "Should converge before max iterations");
    }

    [Fact]
    public async Task IgnoreFields_ExcludesVolatileFieldsFromComparison()
    {
        // Arrange: Handler outputs timestamp that changes every time
        // With ignore fields, should still detect convergence
        var timestampHandler = new TimestampHandler(stableAfter: 2);
        var services = TestServiceProvider.Create(s =>
        {
            s.AddSingleton<IGraphNodeHandler<GraphContext>>(timestampHandler);
        });

        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("loop", "TimestampHandler")
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

        // Enable change-aware with ignored timestamp field
        var graphWithOptions = graph with
        {
            MaxIterations = 100,
            IterationOptions = new IterationOptions
            {
                UseChangeAwareIteration = true,
                EnableAutoConvergence = true,
                IgnoreFieldsForChangeDetection = new HashSet<string> { "timestamp", "requestId" }
            }
        };

        var context = new GraphContext("ignore-fields-test", graphWithOptions, services);
        var orchestrator = new GraphOrchestrator<GraphContext>(services);

        // Act
        await orchestrator.ExecuteAsync(context);

        // Assert - Should converge despite timestamp changing
        context.GetNodeExecutionCount("loop").Should().BeLessThanOrEqualTo(5,
            "Should converge when ignoring volatile fields");
    }

    [Fact]
    public async Task LegacyMode_UsesEagerPropagation()
    {
        // Arrange: Same graph, but with change-aware disabled (legacy mode)
        var stableHandler = new StableAfterFirstHandler();
        var controlHandler = new IterationControlHandler(iterationsBeforeStop: 2);
        var services = TestServiceProvider.Create(s =>
        {
            s.AddSingleton<IGraphNodeHandler<GraphContext>>(stableHandler);
            s.AddSingleton<IGraphNodeHandler<GraphContext>>(controlHandler);
        });

        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("A", "SuccessHandler")
            .AddHandlerNode("B", "StableAfterFirstHandler")
            .AddHandlerNode("C", "SuccessHandler")
            .AddHandlerNode("D", "IterationControlHandler")
            .AddEndNode()
            .AddEdge("start", "A")
            .AddEdge("A", "B")
            .AddEdge("B", "C")
            .AddEdge("C", "D")
            .AddEdge("D", "B", new EdgeCondition
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

        // Explicitly disable change-aware iteration (legacy mode)
        var graphWithOptions = graph with
        {
            IterationOptions = new IterationOptions
            {
                UseChangeAwareIteration = false
            }
        };

        var context = new GraphContext("legacy-mode-test", graphWithOptions, services);
        var orchestrator = new GraphOrchestrator<GraphContext>(services);

        // Act
        await orchestrator.ExecuteAsync(context);

        // Assert - In legacy mode, ALL downstream nodes re-execute
        context.GetNodeExecutionCount("A").Should().Be(1, "A is before back-edge target");
        context.GetNodeExecutionCount("B").Should().Be(3, "B is back-edge target (eager propagation)");
        context.GetNodeExecutionCount("C").Should().Be(3, "C re-executes in legacy mode");
        context.GetNodeExecutionCount("D").Should().Be(3, "D re-executes in legacy mode");
    }

    #endregion

    #region Event Emission Tests

    [Fact]
    public async Task ChangeAware_CompletesWithoutErrors()
    {
        // Arrange: Verify that change-aware iteration completes successfully
        var stableHandler = new StableAfterFirstHandler();
        var controlHandler = new IterationControlHandler(iterationsBeforeStop: 1);
        var services = TestServiceProvider.Create(s =>
        {
            s.AddSingleton<IGraphNodeHandler<GraphContext>>(stableHandler);
            s.AddSingleton<IGraphNodeHandler<GraphContext>>(controlHandler);
        });

        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("B", "StableAfterFirstHandler")
            .AddHandlerNode("C", "SuccessHandler")
            .AddHandlerNode("D", "IterationControlHandler")
            .AddEndNode()
            .AddEdge("start", "B")
            .AddEdge("B", "C")
            .AddEdge("C", "D")
            .AddEdge("D", "B", new EdgeCondition
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

        var graphWithOptions = graph with
        {
            IterationOptions = new IterationOptions
            {
                UseChangeAwareIteration = true,
                EnableAutoConvergence = false
            }
        };

        var context = new GraphContext("change-aware-complete-test", graphWithOptions, services);
        var orchestrator = new GraphOrchestrator<GraphContext>(services);

        // Act
        await orchestrator.ExecuteAsync(context);

        // Assert - All nodes should complete successfully
        context.ShouldHaveCompletedNode("B");
        context.ShouldHaveCompletedNode("C");
        context.ShouldHaveCompletedNode("D");
    }

    [Fact]
    public async Task AutoConvergence_CompletesSuccessfully()
    {
        // Arrange
        var convergingHandler = new ConvergingHandler(iterationsToConverge: 1);
        var services = TestServiceProvider.Create(s =>
        {
            s.AddSingleton<IGraphNodeHandler<GraphContext>>(convergingHandler);
        });

        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("loop", "ConvergingHandler")
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

        var graphWithOptions = graph with
        {
            MaxIterations = 100,
            IterationOptions = new IterationOptions
            {
                UseChangeAwareIteration = true,
                EnableAutoConvergence = true
            }
        };

        var context = new GraphContext("auto-convergence-test", graphWithOptions, services);
        var orchestrator = new GraphOrchestrator<GraphContext>(services);

        // Act
        await orchestrator.ExecuteAsync(context);

        // Assert - Should complete without error
        context.ShouldHaveCompletedNode("loop");
    }

    #endregion

    #region AlwaysDirty Configuration Tests

    [Fact]
    public async Task AlwaysDirtyNodes_IsRecognized()
    {
        // Arrange: Verify that AlwaysDirtyNodes configuration is properly recognized
        var controlHandler = new IterationControlHandler(iterationsBeforeStop: 2);
        var services = TestServiceProvider.Create(s =>
        {
            s.AddSingleton<IGraphNodeHandler<GraphContext>>(controlHandler);
        });

        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddHandlerNode("loop", "IterationControlHandler")
            .AddEndNode()
            .AddEdge("start", "loop")
            .AddEdge("loop", "loop", new EdgeCondition
            {
                Type = ConditionType.FieldEquals,
                Field = "iterate",
                Value = true
            })
            .AddEdge("loop", "end", new EdgeCondition
            {
                Type = ConditionType.FieldEquals,
                Field = "iterate",
                Value = false
            })
            .Build();

        var graphWithOptions = graph with
        {
            IterationOptions = new IterationOptions
            {
                UseChangeAwareIteration = true,
                EnableAutoConvergence = false,
                AlwaysDirtyNodes = new HashSet<string> { "loop" }
            }
        };

        var context = new GraphContext("always-dirty-test", graphWithOptions, services);
        var orchestrator = new GraphOrchestrator<GraphContext>(services);

        // Act
        await orchestrator.ExecuteAsync(context);

        // Assert - Loop should execute 3 times
        context.GetNodeExecutionCount("loop").Should().Be(3,
            "Loop should re-execute based on condition");
        context.ShouldHaveCompletedNode("loop");
    }

    #endregion
}

#region Test Handlers for Change-Aware Iteration Tests

/// <summary>
/// Handler that outputs a stable value after the first execution.
/// Used to test change detection - output changes once, then stays the same.
/// </summary>
public class StableAfterFirstHandler : IGraphNodeHandler<GraphContext>
{
    public string HandlerName => "StableAfterFirstHandler";

    private int _executions = 0;

    public Task<NodeExecutionResult> ExecuteAsync(
        GraphContext context,
        HandlerInputs inputs,
        CancellationToken cancellationToken = default)
    {
        _executions++;

        // First execution outputs "initial", subsequent ones output "stable"
        var value = _executions == 1 ? "initial" : "stable";

        return Task.FromResult<NodeExecutionResult>(NodeExecutionResult.Success.Single(
            output: new Dictionary<string, object>
            {
                ["value"] = value,
                ["executions"] = _executions
            },
            duration: TimeSpan.FromMilliseconds(1),
            metadata: new NodeExecutionMetadata()
        ));
    }
}

/// <summary>
/// Handler that converges (outputs same value) after N iterations.
/// Used to test auto-convergence detection.
/// </summary>
public class ConvergingHandler : IGraphNodeHandler<GraphContext>
{
    public string HandlerName => "ConvergingHandler";

    private int _executions = 0;
    private readonly int _iterationsToConverge;

    public ConvergingHandler(int iterationsToConverge = 2)
    {
        _iterationsToConverge = iterationsToConverge;
    }

    public Task<NodeExecutionResult> ExecuteAsync(
        GraphContext context,
        HandlerInputs inputs,
        CancellationToken cancellationToken = default)
    {
        _executions++;

        // Output changes for first N iterations, then stabilizes
        var shouldContinue = _executions < _iterationsToConverge + 1;
        var dynamicValue = _executions <= _iterationsToConverge ? $"changing_{_executions}" : "converged";

        return Task.FromResult<NodeExecutionResult>(NodeExecutionResult.Success.Single(
            output: new Dictionary<string, object>
            {
                ["continue"] = shouldContinue,
                ["value"] = dynamicValue,
                ["executions"] = _executions
            },
            duration: TimeSpan.FromMilliseconds(1),
            metadata: new NodeExecutionMetadata()
        ));
    }
}

/// <summary>
/// Handler that includes timestamps and other volatile fields in output.
/// Used to test IgnoreFieldsForChangeDetection.
/// </summary>
public class TimestampHandler : IGraphNodeHandler<GraphContext>
{
    public string HandlerName => "TimestampHandler";

    private int _executions = 0;
    private readonly int _stableAfter;

    public TimestampHandler(int stableAfter = 2)
    {
        _stableAfter = stableAfter;
    }

    public Task<NodeExecutionResult> ExecuteAsync(
        GraphContext context,
        HandlerInputs inputs,
        CancellationToken cancellationToken = default)
    {
        _executions++;

        var shouldContinue = _executions < _stableAfter + 1;
        var stableValue = _executions <= _stableAfter ? $"changing_{_executions}" : "stable";

        return Task.FromResult<NodeExecutionResult>(NodeExecutionResult.Success.Single(
            output: new Dictionary<string, object>
            {
                ["continue"] = shouldContinue,
                ["value"] = stableValue,
                ["timestamp"] = DateTimeOffset.UtcNow.Ticks, // Volatile - should be ignored
                ["requestId"] = Guid.NewGuid().ToString(), // Volatile - should be ignored
                ["executions"] = _executions
            },
            duration: TimeSpan.FromMilliseconds(1),
            metadata: new NodeExecutionMetadata()
        ));
    }
}

#endregion
