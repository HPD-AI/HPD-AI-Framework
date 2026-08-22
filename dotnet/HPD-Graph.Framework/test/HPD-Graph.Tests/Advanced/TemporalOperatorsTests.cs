using FluentAssertions;
using HPD.Events;
using HPD.Graph.Abstractions.Context;
using HPD.Graph.Abstractions.Execution;
using HPD.Graph.Abstractions.Handlers;
using HPD.Graph.Core.Context;
using HPD.Graph.Core.Orchestration;
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;

using GraphType = HPD.Graph.Abstractions.Graph.Graph;
using NodeType = HPD.Graph.Abstractions.Graph.Node;
using NodeTypeEnum = HPD.Graph.Abstractions.Graph.NodeType;
using EdgeType = HPD.Graph.Abstractions.Graph.Edge;

namespace HPD.Graph.Tests.Advanced;

/// <summary>
/// Tests for graph-owned temporal operators (Edge.Delay and Edge.RetryPolicy).
/// Validates edge-level temporal controls with suspension integration.
/// </summary>
public class TemporalOperatorsTests
{
    // ========== Edge.Delay Tests ==========

    [Fact]
    public async Task EdgeDelay_ShortDelay_ExecutesSynchronously()
    {
        // Arrange: Graph with short delay (< 30s threshold)
        var graph = new GraphType
        {
            Id = "test-graph",
            Name = "Short Delay Test",
            Version = "1.0",
            EntryNodeId = "start",
            ExitNodeId = "delayed",
            Nodes = new List<NodeType>
            {
                new NodeType
                {
                    Id = "start",
                    Name = "Start Node",
                    Type = NodeTypeEnum.Handler,
                    HandlerName = "SuccessHandler"
                },
                new NodeType
                {
                    Id = "delayed",
                    Name = "Delayed Node",
                    Type = NodeTypeEnum.Handler,
                    HandlerName = "SuccessHandler"
                }
            },
            Edges = new List<EdgeType>
            {
                new EdgeType
                {
                    From = "start",
                    To = "delayed",
                    Delay = TimeSpan.FromSeconds(1) // Short delay - should execute synchronously
                }
            }
        };

        var services = new ServiceCollection();
        services.AddSingleton<IGraphNodeHandler<GraphContext>>(new SuccessHandler());
        var serviceProvider = services.BuildServiceProvider();

        var orchestrator = new GraphOrchestrator<GraphContext>(serviceProvider);
        var context = new GraphContext("exec-1", graph, serviceProvider);

        // Act
        var stopwatch = Stopwatch.StartNew();
        var result = await orchestrator.ExecuteAsync(context);
        stopwatch.Stop();

        // Assert
        result.CompletedNodes.Should().Contain("start");
        result.CompletedNodes.Should().Contain("delayed");
        stopwatch.Elapsed.Should().BeGreaterThanOrEqualTo(TimeSpan.FromSeconds(1)); // Delay was applied
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(3)); // Completed without suspension
    }

    [Fact]
    public async Task EdgeDelay_LongDelay_SuspendsExecution()
    {
        // Arrange: Graph with long delay (>= 30s threshold)
        var graph = new GraphType
        {
            Id = "test-graph",
            Name = "Long Delay Test",
            Version = "1.0",
            EntryNodeId = "start",
            ExitNodeId = "delayed",
            Nodes = new List<NodeType>
            {
                new NodeType
                {
                    Id = "start",
                    Name = "Start Node",
                    Type = NodeTypeEnum.Handler,
                    HandlerName = "SuccessHandler"
                },
                new NodeType
                {
                    Id = "delayed",
                    Name = "Delayed Node",
                    Type = NodeTypeEnum.Handler,
                    HandlerName = "SuccessHandler"
                }
            },
            Edges = new List<EdgeType>
            {
                new EdgeType
                {
                    From = "start",
                    To = "delayed",
                    Delay = TimeSpan.FromMinutes(5) // Long delay - should suspend
                }
            }
        };

        var services = new ServiceCollection();
        services.AddSingleton<IGraphNodeHandler<GraphContext>>(new SuccessHandler());
        var serviceProvider = services.BuildServiceProvider();

        var orchestrator = new GraphOrchestrator<GraphContext>(serviceProvider);
        var context = new GraphContext("exec-1", graph, serviceProvider);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<GraphSuspendedException>(async () =>
            await orchestrator.ExecuteAsync(context));

        exception.NodeId.Should().Be("delayed");
        exception.Message.Should().Contain("delay");
        exception.Message.Should().Contain("5");

        // Verify start node completed but delayed node did not
        context.CompletedNodes.Should().Contain("start");
        context.CompletedNodes.Should().NotContain("delayed");
    }

    // ========== Edge.RetryPolicy Tests ==========

    [Fact]
    public async Task EdgeRetryPolicy_ConditionMet_AllowsTraversal()
    {
        // Arrange: Retry policy with condition that immediately succeeds
        var graph = new GraphType
        {
            Id = "test-graph",
            Name = "Retry Policy Success Test",
            Version = "1.0",
            EntryNodeId = "start",
            ExitNodeId = "retryable",
            Nodes = new List<NodeType>
            {
                new NodeType
                {
                    Id = "start",
                    Name = "Start Node",
                    Type = NodeTypeEnum.Handler,
                    HandlerName = "SuccessHandler"
                },
                new NodeType
                {
                    Id = "retryable",
                    Name = "Retryable Node",
                    Type = NodeTypeEnum.Handler,
                    HandlerName = "SuccessHandler"
                }
            },
            Edges = new List<EdgeType>
            {
                new EdgeType
                {
                    From = "start",
                    To = "retryable",
                    RetryPolicy = new EdgeRetryPolicy
                    {
                        RetryInterval = TimeSpan.FromSeconds(1),
                        RetryCondition = async (ctx) =>
                        {
                            await Task.CompletedTask;
                            return true; // Condition met
                        }
                    }
                }
            }
        };

        var services = new ServiceCollection();
        services.AddSingleton<IGraphNodeHandler<GraphContext>>(new SuccessHandler());
        var serviceProvider = services.BuildServiceProvider();

        var orchestrator = new GraphOrchestrator<GraphContext>(serviceProvider);
        var context = new GraphContext("exec-1", graph, serviceProvider);

        // Act
        var result = await orchestrator.ExecuteAsync(context);

        // Assert
        result.CompletedNodes.Should().Contain("start");
        result.CompletedNodes.Should().Contain("retryable");
    }

    [Fact]
    public async Task EdgeRetryPolicy_ConditionNotMet_SuspendsExecution()
    {
        // Arrange: Retry policy with condition that always fails
        var graph = new GraphType
        {
            Id = "test-graph",
            Name = "Retry Policy Suspension Test",
            Version = "1.0",
            EntryNodeId = "start",
            ExitNodeId = "retryable",
            Nodes = new List<NodeType>
            {
                new NodeType
                {
                    Id = "start",
                    Name = "Start Node",
                    Type = NodeTypeEnum.Handler,
                    HandlerName = "SuccessHandler"
                },
                new NodeType
                {
                    Id = "retryable",
                    Name = "Retryable Node",
                    Type = NodeTypeEnum.Handler,
                    HandlerName = "SuccessHandler"
                }
            },
            Edges = new List<EdgeType>
            {
                new EdgeType
                {
                    From = "start",
                    To = "retryable",
                    RetryPolicy = new EdgeRetryPolicy
                    {
                        RetryInterval = TimeSpan.FromSeconds(10),
                        MaxWaitTime = TimeSpan.FromMinutes(1),
                        RetryCondition = async (ctx) =>
                        {
                            await Task.CompletedTask;
                            return false; // Condition not met
                        }
                    }
                }
            }
        };

        var services = new ServiceCollection();
        services.AddSingleton<IGraphNodeHandler<GraphContext>>(new SuccessHandler());
        var serviceProvider = services.BuildServiceProvider();

        var orchestrator = new GraphOrchestrator<GraphContext>(serviceProvider);
        var context = new GraphContext("exec-1", graph, serviceProvider);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<GraphSuspendedException>(async () =>
            await orchestrator.ExecuteAsync(context));

        exception.NodeId.Should().Be("retryable");
        exception.Message.Should().Contain("retry condition not met");

        // Verify start node completed but retryable node did not
        context.CompletedNodes.Should().Contain("start");
        context.CompletedNodes.Should().NotContain("retryable");
    }

    [Fact]
    public async Task EdgeRetryPolicy_StatefulCondition_EventuallySucceeds()
    {
        // Arrange: Retry policy with stateful condition (succeeds after 3 attempts)
        var attemptCount = 0;

        var graph = new GraphType
        {
            Id = "test-graph",
            Name = "Retry Policy Stateful Test",
            Version = "1.0",
            EntryNodeId = "start",
            ExitNodeId = "retryable",
            Nodes = new List<NodeType>
            {
                new NodeType
                {
                    Id = "start",
                    Name = "Start Node",
                    Type = NodeTypeEnum.Handler,
                    HandlerName = "SuccessHandler"
                },
                new NodeType
                {
                    Id = "retryable",
                    Name = "Retryable Node",
                    Type = NodeTypeEnum.Handler,
                    HandlerName = "SuccessHandler"
                }
            },
            Edges = new List<EdgeType>
            {
                new EdgeType
                {
                    From = "start",
                    To = "retryable",
                    RetryPolicy = new EdgeRetryPolicy
                    {
                        RetryInterval = TimeSpan.FromSeconds(1),
                        MaxRetries = 5,
                        RetryCondition = async (ctx) =>
                        {
                            await Task.CompletedTask;
                            attemptCount++;
                            return attemptCount >= 3; // Succeed on 3rd attempt
                        }
                    }
                }
            }
        };

        var services = new ServiceCollection();
        services.AddSingleton<IGraphNodeHandler<GraphContext>>(new SuccessHandler());
        var serviceProvider = services.BuildServiceProvider();

        var orchestrator = new GraphOrchestrator<GraphContext>(serviceProvider);
        var context = new GraphContext("exec-1", graph, serviceProvider);

        // Act - First execution should suspend
        var exception = await Assert.ThrowsAsync<GraphSuspendedException>(async () =>
            await orchestrator.ExecuteAsync(context));

        exception.NodeId.Should().Be("retryable");
        attemptCount.Should().Be(1);

        // Act - Second execution should suspend
        exception = await Assert.ThrowsAsync<GraphSuspendedException>(async () =>
            await orchestrator.ExecuteAsync(context));

        exception.NodeId.Should().Be("retryable");
        attemptCount.Should().Be(2);

        // Act - Third execution should succeed
        var result = await orchestrator.ExecuteAsync(context);

        // Assert
        attemptCount.Should().Be(3);
        result.CompletedNodes.Should().Contain("start");
        result.CompletedNodes.Should().Contain("retryable");
    }

    // ========== Combined Temporal Operators Tests ==========

    [Fact]
    public async Task EdgeTemporalOperator_Delay_IsEvaluated()
    {
        // Arrange: Edge with a short delay.
        var graph = new GraphType
        {
            Id = "test-graph",
            Name = "Combined Temporal Test",
            Version = "1.0",
            EntryNodeId = "start",
            ExitNodeId = "temporal",
            Nodes = new List<NodeType>
            {
                new NodeType
                {
                    Id = "start",
                    Name = "Start Node",
                    Type = NodeTypeEnum.Handler,
                    HandlerName = "SuccessHandler"
                },
                new NodeType
                {
                    Id = "temporal",
                    Name = "Temporal Node",
                    Type = NodeTypeEnum.Handler,
                    HandlerName = "SuccessHandler"
                }
            },
            Edges = new List<EdgeType>
            {
                new EdgeType
                {
                    From = "start",
                    To = "temporal",
                    Delay = TimeSpan.FromSeconds(1)
                }
            }
        };

        var services = new ServiceCollection();
        services.AddSingleton<IGraphNodeHandler<GraphContext>>(new SuccessHandler());
        var serviceProvider = services.BuildServiceProvider();

        var orchestrator = new GraphOrchestrator<GraphContext>(serviceProvider);
        var context = new GraphContext("exec-1", graph, serviceProvider);

        // Act
        var stopwatch = Stopwatch.StartNew();
        var result = await orchestrator.ExecuteAsync(context);
        stopwatch.Stop();

        // Assert
        result.CompletedNodes.Should().Contain("start");
        result.CompletedNodes.Should().Contain("temporal");
        stopwatch.Elapsed.Should().BeGreaterThanOrEqualTo(TimeSpan.FromSeconds(1)); // Delay was applied
    }
}

// ========== Test Helpers ==========

/// <summary>
/// Simple handler that always succeeds with empty output.
/// </summary>
internal class SuccessHandler : IGraphNodeHandler<GraphContext>
{
    public string HandlerName => "SuccessHandler";

    public Task<NodeExecutionResult> ExecuteAsync(GraphContext context, HandlerInputs inputs, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<NodeExecutionResult>(
            NodeExecutionResult.Success.Single(
                new Dictionary<string, object>(),
                TimeSpan.Zero,
                new NodeExecutionMetadata { AttemptNumber = 1 }
            )
        );
    }
}

/// <summary>
/// Handler that increments a counter on each execution.
/// </summary>
internal class CountingHandler : IGraphNodeHandler<GraphContext>
{
    private readonly Action _onExecute;

    public CountingHandler(Action onExecute)
    {
        _onExecute = onExecute;
    }

    public string HandlerName => "CountingHandler";

    public Task<NodeExecutionResult> ExecuteAsync(GraphContext context, HandlerInputs inputs, CancellationToken cancellationToken = default)
    {
        _onExecute();
        return Task.FromResult<NodeExecutionResult>(
            NodeExecutionResult.Success.Single(
                new Dictionary<string, object>(),
                TimeSpan.Zero,
                new NodeExecutionMetadata { AttemptNumber = 1 }
            )
        );
    }
}
