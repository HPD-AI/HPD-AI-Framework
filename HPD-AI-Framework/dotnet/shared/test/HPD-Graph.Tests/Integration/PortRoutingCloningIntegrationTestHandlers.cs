using HPDAgent.Graph.Abstractions.Execution;
using HPDAgent.Graph.Abstractions.Handlers;
using HPDAgent.Graph.Core.Context;

namespace HPD.Graph.Tests.Integration;

/// <summary>
/// Test handlers for port routing and cloning integration tests.
/// </summary>

#region Polling Sensor Handlers

public class FileSizePollingHandler : IGraphNodeHandler<GraphContext>
{
    public string HandlerName => "FileSizePollingHandler";

    public Task<NodeExecutionResult> ExecuteAsync(
        GraphContext context,
        HandlerInputs inputs,
        CancellationToken cancellationToken = default)
    {
        // Check if file is ready (simulated via context tags)
        var fileReady = context.Tags.ContainsKey("file_ready") &&
                       context.Tags["file_ready"].Contains("true");

        if (!fileReady)
        {
            // V5: Continue polling
            var suspendToken = context.Tags.ContainsKey("correlation_id")
                ? context.Tags["correlation_id"].FirstOrDefault() ?? Guid.NewGuid().ToString()
                : Guid.NewGuid().ToString();

            return Task.FromResult<NodeExecutionResult>(
                NodeExecutionResult.Suspended.ForPolling(
                    suspendToken: suspendToken,
                    retryAfter: TimeSpan.FromSeconds(5),
                    maxWaitTime: TimeSpan.FromMinutes(5)
                )
            );
        }

        // File ready - get size and route via ports
        var fileSize = int.Parse(context.Tags["file_size"].First());
        var isLarge = fileSize > 1000;

        var port = isLarge ? 0 : 1;
        var outputs = new Dictionary<string, object>
        {
            ["size"] = fileSize,
            ["is_large"] = isLarge
        };

        var portOutputs = new PortOutputs().Add(port, outputs);

        var correlationId = context.Tags.ContainsKey("correlation_id")
            ? context.Tags["correlation_id"].FirstOrDefault() ?? Guid.NewGuid().ToString()
            : Guid.NewGuid().ToString();

        var metadata = new NodeExecutionMetadata
        {
            ExecutionId = Guid.NewGuid().ToString(),
            CorrelationId = correlationId,
            StartedAt = DateTimeOffset.UtcNow,
            CustomMetrics = new Dictionary<string, object>
            {
                ["nodered:file_size"] = fileSize,
                ["nodered:routing_port"] = port
            }
        };

        return Task.FromResult<NodeExecutionResult>(
            NodeExecutionResult.Success.WithPorts(portOutputs, TimeSpan.Zero, metadata)
        );
    }
}

public class LargeFileHandler : IGraphNodeHandler<GraphContext>
{
    public string HandlerName => "LargeFileHandler";

    public Task<NodeExecutionResult> ExecuteAsync(
        GraphContext context,
        HandlerInputs inputs,
        CancellationToken cancellationToken = default)
    {
        // Port 0: namespace is "sensor", key is "sensor.size"
        // IMPORTANT: Only check namespaced key, not fallback "size" key
        // (fallback keys are shared across all ports for backward compat)
        if (!inputs.Contains("sensor.size"))
        {
            // No inputs from port 0 - skip execution
            return Task.FromResult<NodeExecutionResult>(
                new NodeExecutionResult.Skipped(
                    Reason: SkipReason.ConditionNotMet,
                    Message: "No inputs from port 0"
                )
            );
        }

        var size = inputs.Get<int>("sensor.size");

        var output = new Dictionary<string, object>
        {
            ["processed"] = true,
            ["type"] = "large",
            ["original_size"] = size
        };

        return Task.FromResult<NodeExecutionResult>(
            NodeExecutionResult.Success.Single(output, TimeSpan.Zero, new NodeExecutionMetadata())
        );
    }
}

public class SmallFileHandler : IGraphNodeHandler<GraphContext>
{
    public string HandlerName => "SmallFileHandler";

    public Task<NodeExecutionResult> ExecuteAsync(
        GraphContext context,
        HandlerInputs inputs,
        CancellationToken cancellationToken = default)
    {
        // Port 1: namespace is "sensor:port1", key is "sensor:port1.size"
        // IMPORTANT: Only check namespaced key, not fallback keys
        // (fallback keys are shared across all ports for backward compat)
        if (!inputs.Contains("sensor:port1.size"))
        {
            // No inputs from port 1 - skip execution
            return Task.FromResult<NodeExecutionResult>(
                new NodeExecutionResult.Skipped(
                    Reason: SkipReason.ConditionNotMet,
                    Message: "No inputs from port 1"
                )
            );
        }

        var size = inputs.Get<int>("sensor:port1.size");

        var output = new Dictionary<string, object>
        {
            ["processed"] = true,
            ["type"] = "small",
            ["original_size"] = size
        };

        return Task.FromResult<NodeExecutionResult>(
            NodeExecutionResult.Success.Single(output, TimeSpan.Zero, new NodeExecutionMetadata())
        );
    }
}

#endregion

#region Port Routing Handlers

public class DataClassifierHandler : IGraphNodeHandler<GraphContext>
{
    public string HandlerName => "DataClassifierHandler";

    public Task<NodeExecutionResult> ExecuteAsync(
        GraphContext context,
        HandlerInputs inputs,
        CancellationToken cancellationToken = default)
    {
        var dataType = context.Tags.ContainsKey("data_type")
            ? context.Tags["data_type"].FirstOrDefault() ?? "normal"
            : "normal";
        var isUrgent = dataType == "urgent";

        var port = isUrgent ? 0 : 1;
        var outputs = new Dictionary<string, object>
        {
            ["priority"] = isUrgent ? "high" : "low",
            ["data_type"] = dataType
        };

        var portOutputs = new PortOutputs().Add(port, outputs);

        return Task.FromResult<NodeExecutionResult>(
            NodeExecutionResult.Success.WithPorts(portOutputs, TimeSpan.Zero, new NodeExecutionMetadata())
        );
    }
}

public class HighPriorityHandler : IGraphNodeHandler<GraphContext>
{
    public string HandlerName => "HighPriorityHandler";

    public Task<NodeExecutionResult> ExecuteAsync(
        GraphContext context,
        HandlerInputs inputs,
        CancellationToken cancellationToken = default)
    {
        // Port 0: namespace is "classifier", key is "classifier.priority"
        // Only check namespaced key to avoid executing when classifier routes to port 1
        if (!inputs.Contains("classifier.priority"))
        {
            // No inputs from port 0 - skip execution
            return Task.FromResult<NodeExecutionResult>(
                new NodeExecutionResult.Skipped(
                    Reason: SkipReason.ConditionNotMet,
                    Message: "No inputs from port 0"
                )
            );
        }

        var output = new Dictionary<string, object>
        {
            ["processed"] = true,
            ["handler"] = "high_priority"
        };

        return Task.FromResult<NodeExecutionResult>(
            NodeExecutionResult.Success.Single(output, TimeSpan.Zero, new NodeExecutionMetadata())
        );
    }
}

public class LowPriorityHandler : IGraphNodeHandler<GraphContext>
{
    public string HandlerName => "LowPriorityHandler";

    public Task<NodeExecutionResult> ExecuteAsync(
        GraphContext context,
        HandlerInputs inputs,
        CancellationToken cancellationToken = default)
    {
        // Port 1: namespace is "classifier:port1", key is "classifier:port1.priority"
        // Only check namespaced key to avoid executing when classifier routes to port 0
        if (!inputs.Contains("classifier:port1.priority"))
        {
            // No inputs from port 1 - skip execution
            return Task.FromResult<NodeExecutionResult>(
                new NodeExecutionResult.Skipped(
                    Reason: SkipReason.ConditionNotMet,
                    Message: "No inputs from port 1"
                )
            );
        }

        var output = new Dictionary<string, object>
        {
            ["processed"] = true,
            ["handler"] = "low_priority"
        };

        return Task.FromResult<NodeExecutionResult>(
            NodeExecutionResult.Success.Single(output, TimeSpan.Zero, new NodeExecutionMetadata())
        );
    }
}

#endregion

#region Multi-Port Cloning Handlers

public class DualPortSplitterHandler : IGraphNodeHandler<GraphContext>
{
    public string HandlerName => "DualPortSplitterHandler";

    public Task<NodeExecutionResult> ExecuteAsync(
        GraphContext context,
        HandlerInputs inputs,
        CancellationToken cancellationToken = default)
    {
        // Create mutable lists to test cloning
        var port0Data = new List<int> { 1, 2, 3 };
        var port1Data = new List<int> { 10, 20, 30 };

        var portOutputs = new PortOutputs()
            .Add(0, new Dictionary<string, object> { ["data"] = port0Data })
            .Add(1, new Dictionary<string, object> { ["data"] = port1Data });

        return Task.FromResult<NodeExecutionResult>(
            NodeExecutionResult.Success.WithPorts(portOutputs, TimeSpan.Zero, new NodeExecutionMetadata())
        );
    }
}

public class Port0Consumer1 : IGraphNodeHandler<GraphContext>
{
    public string HandlerName => "Port0Consumer1";

    public Task<NodeExecutionResult> ExecuteAsync(
        GraphContext context,
        HandlerInputs inputs,
        CancellationToken cancellationToken = default)
    {
        var data = inputs.Get<object>("data");
        var output = new Dictionary<string, object>
        {
            ["received"] = data,
            ["consumer"] = "port0_consumer1"
        };

        return Task.FromResult<NodeExecutionResult>(
            NodeExecutionResult.Success.Single(output, TimeSpan.Zero, new NodeExecutionMetadata())
        );
    }
}

public class Port0Consumer2 : IGraphNodeHandler<GraphContext>
{
    public string HandlerName => "Port0Consumer2";

    public Task<NodeExecutionResult> ExecuteAsync(
        GraphContext context,
        HandlerInputs inputs,
        CancellationToken cancellationToken = default)
    {
        var data = inputs.Get<object>("data");
        var output = new Dictionary<string, object>
        {
            ["received"] = data,
            ["consumer"] = "port0_consumer2"
        };

        return Task.FromResult<NodeExecutionResult>(
            NodeExecutionResult.Success.Single(output, TimeSpan.Zero, new NodeExecutionMetadata())
        );
    }
}

public class Port1Consumer1 : IGraphNodeHandler<GraphContext>
{
    public string HandlerName => "Port1Consumer1";

    public Task<NodeExecutionResult> ExecuteAsync(
        GraphContext context,
        HandlerInputs inputs,
        CancellationToken cancellationToken = default)
    {
        var data = inputs.Get<object>("data");
        var output = new Dictionary<string, object>
        {
            ["received"] = data,
            ["consumer"] = "port1_consumer1"
        };

        return Task.FromResult<NodeExecutionResult>(
            NodeExecutionResult.Success.Single(output, TimeSpan.Zero, new NodeExecutionMetadata())
        );
    }
}

public class Port1Consumer2 : IGraphNodeHandler<GraphContext>
{
    public string HandlerName => "Port1Consumer2";

    public Task<NodeExecutionResult> ExecuteAsync(
        GraphContext context,
        HandlerInputs inputs,
        CancellationToken cancellationToken = default)
    {
        var data = inputs.Get<object>("data");
        var output = new Dictionary<string, object>
        {
            ["received"] = data,
            ["consumer"] = "port1_consumer2"
        };

        return Task.FromResult<NodeExecutionResult>(
            NodeExecutionResult.Success.Single(output, TimeSpan.Zero, new NodeExecutionMetadata())
        );
    }
}

#endregion

#region Cloning Policy Override Handlers

public class MutableSourceHandler : IGraphNodeHandler<GraphContext>
{
    public string HandlerName => "MutableSourceHandler";

    public Task<NodeExecutionResult> ExecuteAsync(
        GraphContext context,
        HandlerInputs inputs,
        CancellationToken cancellationToken = default)
    {
        var mutableList = new List<int> { 100, 200, 300 };

        var portOutputs = new PortOutputs()
            .Add(0, new Dictionary<string, object> { ["data"] = mutableList })
            .Add(1, new Dictionary<string, object> { ["data"] = mutableList });

        return Task.FromResult<NodeExecutionResult>(
            NodeExecutionResult.Success.WithPorts(portOutputs, TimeSpan.Zero, new NodeExecutionMetadata())
        );
    }
}

public class AlwaysCloneConsumer : IGraphNodeHandler<GraphContext>
{
    public string HandlerName => "AlwaysCloneConsumer";

    public Task<NodeExecutionResult> ExecuteAsync(
        GraphContext context,
        HandlerInputs inputs,
        CancellationToken cancellationToken = default)
    {
        var data = inputs.Get<object>("data");
        var output = new Dictionary<string, object>
        {
            ["received"] = data,
            ["policy"] = "always_clone"
        };

        return Task.FromResult<NodeExecutionResult>(
            NodeExecutionResult.Success.Single(output, TimeSpan.Zero, new NodeExecutionMetadata())
        );
    }
}

public class LazyCloneConsumer : IGraphNodeHandler<GraphContext>
{
    public string HandlerName => "LazyCloneConsumer";

    public Task<NodeExecutionResult> ExecuteAsync(
        GraphContext context,
        HandlerInputs inputs,
        CancellationToken cancellationToken = default)
    {
        var data = inputs.Get<object>("data");
        var output = new Dictionary<string, object>
        {
            ["received"] = data,
            ["policy"] = "lazy_clone"
        };

        return Task.FromResult<NodeExecutionResult>(
            NodeExecutionResult.Success.Single(output, TimeSpan.Zero, new NodeExecutionMetadata())
        );
    }
}

#endregion

#region Correlation Tracking Handlers

public class CorrelationTrackingRouter : IGraphNodeHandler<GraphContext>
{
    public string HandlerName => "CorrelationTrackingRouter";

    public Task<NodeExecutionResult> ExecuteAsync(
        GraphContext context,
        HandlerInputs inputs,
        CancellationToken cancellationToken = default)
    {
        // Get correlation ID from tags
        var correlationId = context.Tags.ContainsKey("correlation_id")
            ? context.Tags["correlation_id"].FirstOrDefault() ?? "none"
            : "none";

        // Track correlation in context
        context.AddTag("router_correlation", correlationId);

        var portOutputs = new PortOutputs()
            .Add(0, new Dictionary<string, object> { ["route"] = "path1" })
            .Add(1, new Dictionary<string, object> { ["route"] = "path2" });

        var metadata = new NodeExecutionMetadata
        {
            ExecutionId = Guid.NewGuid().ToString(),
            CorrelationId = correlationId,
            StartedAt = DateTimeOffset.UtcNow
        };

        return Task.FromResult<NodeExecutionResult>(
            NodeExecutionResult.Success.WithPorts(portOutputs, TimeSpan.Zero, metadata)
        );
    }
}

public class CorrelationTracker1 : IGraphNodeHandler<GraphContext>
{
    public string HandlerName => "CorrelationTracker1";

    public Task<NodeExecutionResult> ExecuteAsync(
        GraphContext context,
        HandlerInputs inputs,
        CancellationToken cancellationToken = default)
    {
        var correlationId = context.Tags.ContainsKey("correlation_id")
            ? context.Tags["correlation_id"].FirstOrDefault() ?? "none"
            : "none";

        context.AddTag("handler1_correlation", correlationId);

        var output = new Dictionary<string, object>
        {
            ["handler"] = "tracker1"
        };

        return Task.FromResult<NodeExecutionResult>(
            NodeExecutionResult.Success.Single(output, TimeSpan.Zero, new NodeExecutionMetadata())
        );
    }
}

public class CorrelationTracker2 : IGraphNodeHandler<GraphContext>
{
    public string HandlerName => "CorrelationTracker2";

    public Task<NodeExecutionResult> ExecuteAsync(
        GraphContext context,
        HandlerInputs inputs,
        CancellationToken cancellationToken = default)
    {
        var correlationId = context.Tags.ContainsKey("correlation_id")
            ? context.Tags["correlation_id"].FirstOrDefault() ?? "none"
            : "none";

        context.AddTag("handler2_correlation", correlationId);

        var output = new Dictionary<string, object>
        {
            ["handler"] = "tracker2"
        };

        return Task.FromResult<NodeExecutionResult>(
            NodeExecutionResult.Success.Single(output, TimeSpan.Zero, new NodeExecutionMetadata())
        );
    }
}

#endregion

#region Metadata Namespacing Handlers

public class NamespacedMetadataHandler : IGraphNodeHandler<GraphContext>
{
    public string HandlerName => "NamespacedMetadataHandler";

    public Task<NodeExecutionResult> ExecuteAsync(
        GraphContext context,
        HandlerInputs inputs,
        CancellationToken cancellationToken = default)
    {
        var metadata = new NodeExecutionMetadata
        {
            ExecutionId = Guid.NewGuid().ToString(),
            CorrelationId = Guid.NewGuid().ToString(),
            StartedAt = DateTimeOffset.UtcNow,
            CustomMetrics = new Dictionary<string, object>
            {
                ["v5:state"] = "active",
                ["nodered:routing"] = "port0",
                ["app_specific"] = "custom_value"
            }
        };

        // Store in context tags for verification
        context.AddTag("v5:state", "active");
        context.AddTag("nodered:routing", "port0");
        context.AddTag("app_specific", "custom_value");

        var output = new Dictionary<string, object>
        {
            ["processed"] = true
        };

        return Task.FromResult<NodeExecutionResult>(
            NodeExecutionResult.Success.Single(output, TimeSpan.Zero, metadata)
        );
    }
}

#endregion

#region Error Monitoring Handlers

public class FailingSourceHandler : IGraphNodeHandler<GraphContext>
{
    public string HandlerName => "FailingSourceHandler";

    public Task<NodeExecutionResult> ExecuteAsync(
        GraphContext context,
        HandlerInputs inputs,
        CancellationToken cancellationToken = default)
    {
        // Test error handling with cloning - but use Warning severity so graph continues
        var portOutputs = new PortOutputs()
            .Add(0, new Dictionary<string, object> { ["data"] = "test_data_port0" })
            .Add(1, new Dictionary<string, object> { ["data"] = "test_data_port1" });

        return Task.FromResult<NodeExecutionResult>(
            NodeExecutionResult.Success.WithPorts(portOutputs, TimeSpan.Zero, new NodeExecutionMetadata())
        );
    }
}

public class GenericConsumer1 : IGraphNodeHandler<GraphContext>
{
    public string HandlerName => "GenericConsumer1";

    public Task<NodeExecutionResult> ExecuteAsync(
        GraphContext context,
        HandlerInputs inputs,
        CancellationToken cancellationToken = default)
    {
        var output = new Dictionary<string, object>
        {
            ["consumer"] = "consumer1"
        };

        return Task.FromResult<NodeExecutionResult>(
            NodeExecutionResult.Success.Single(output, TimeSpan.Zero, new NodeExecutionMetadata())
        );
    }
}

public class GenericConsumer2 : IGraphNodeHandler<GraphContext>
{
    public string HandlerName => "GenericConsumer2";

    public Task<NodeExecutionResult> ExecuteAsync(
        GraphContext context,
        HandlerInputs inputs,
        CancellationToken cancellationToken = default)
    {
        var output = new Dictionary<string, object>
        {
            ["consumer"] = "consumer2"
        };

        return Task.FromResult<NodeExecutionResult>(
            NodeExecutionResult.Success.Single(output, TimeSpan.Zero, new NodeExecutionMetadata())
        );
    }
}

#endregion

#region Complete Workflow Handlers

public class CompleteWorkflowSensor : IGraphNodeHandler<GraphContext>
{
    public string HandlerName => "CompleteWorkflowSensor";

    public Task<NodeExecutionResult> ExecuteAsync(
        GraphContext context,
        HandlerInputs inputs,
        CancellationToken cancellationToken = default)
    {
        // Check if data is ready
        var dataReady = context.Tags.ContainsKey("data_ready") &&
                       context.Tags["data_ready"].Contains("true");

        if (!dataReady)
        {
            var suspendToken = context.Tags.ContainsKey("correlation_id")
                ? context.Tags["correlation_id"].FirstOrDefault() ?? Guid.NewGuid().ToString()
                : Guid.NewGuid().ToString();

            return Task.FromResult<NodeExecutionResult>(
                NodeExecutionResult.Suspended.ForPolling(
                    suspendToken: suspendToken,
                    retryAfter: TimeSpan.FromSeconds(10),
                    maxWaitTime: TimeSpan.FromMinutes(10)
                )
            );
        }

        // Route to all three ports
        var correlationId = context.Tags.ContainsKey("correlation_id")
            ? context.Tags["correlation_id"].FirstOrDefault() ?? Guid.NewGuid().ToString()
            : Guid.NewGuid().ToString();

        var portOutputs = new PortOutputs()
            .Add(0, new Dictionary<string, object> { ["priority"] = "high", ["data"] = "urgent_data" })
            .Add(1, new Dictionary<string, object> { ["priority"] = "medium", ["data"] = "normal_data" })
            .Add(2, new Dictionary<string, object> { ["priority"] = "low", ["data"] = "background_data" });

        var metadata = new NodeExecutionMetadata
        {
            ExecutionId = Guid.NewGuid().ToString(),
            CorrelationId = correlationId,
            StartedAt = DateTimeOffset.UtcNow,
            CustomMetrics = new Dictionary<string, object>
            {
                ["v5:sensor_state"] = "ready",
                ["nodered:ports_activated"] = 3
            }
        };

        context.AddTag("workflow_correlation", correlationId);

        return Task.FromResult<NodeExecutionResult>(
            NodeExecutionResult.Success.WithPorts(portOutputs, TimeSpan.Zero, metadata)
        );
    }
}

public class HighPriorityProcessor : IGraphNodeHandler<GraphContext>
{
    public string HandlerName => "HighPriorityProcessor";

    public Task<NodeExecutionResult> ExecuteAsync(
        GraphContext context,
        HandlerInputs inputs,
        CancellationToken cancellationToken = default)
    {
        var data = inputs.Get<string>("data");
        var output = new Dictionary<string, object>
        {
            ["processed_data"] = data,
            ["priority"] = "high"
        };

        return Task.FromResult<NodeExecutionResult>(
            NodeExecutionResult.Success.Single(output, TimeSpan.Zero, new NodeExecutionMetadata())
        );
    }
}

public class MediumPriorityProcessor : IGraphNodeHandler<GraphContext>
{
    public string HandlerName => "MediumPriorityProcessor";

    public Task<NodeExecutionResult> ExecuteAsync(
        GraphContext context,
        HandlerInputs inputs,
        CancellationToken cancellationToken = default)
    {
        var data = inputs.Get<string>("data");
        var output = new Dictionary<string, object>
        {
            ["processed_data"] = data,
            ["priority"] = "medium"
        };

        return Task.FromResult<NodeExecutionResult>(
            NodeExecutionResult.Success.Single(output, TimeSpan.Zero, new NodeExecutionMetadata())
        );
    }
}

public class LowPriorityProcessor : IGraphNodeHandler<GraphContext>
{
    public string HandlerName => "LowPriorityProcessor";

    public Task<NodeExecutionResult> ExecuteAsync(
        GraphContext context,
        HandlerInputs inputs,
        CancellationToken cancellationToken = default)
    {
        var data = inputs.Get<string>("data");
        var output = new Dictionary<string, object>
        {
            ["processed_data"] = data,
            ["priority"] = "low"
        };

        return Task.FromResult<NodeExecutionResult>(
            NodeExecutionResult.Success.Single(output, TimeSpan.Zero, new NodeExecutionMetadata())
        );
    }
}

public class ResultAggregator : IGraphNodeHandler<GraphContext>
{
    public string HandlerName => "ResultAggregator";

    public Task<NodeExecutionResult> ExecuteAsync(
        GraphContext context,
        HandlerInputs inputs,
        CancellationToken cancellationToken = default)
    {
        var results = new List<object>();

        // Collect all inputs from all upstream nodes
        // Inputs are namespaced by source node
        var allInputs = inputs.GetAll();
        foreach (var kvp in allInputs)
        {
            if (kvp.Key.Contains("processed_data"))
            {
                results.Add(kvp.Value);
            }
        }

        var output = new Dictionary<string, object>
        {
            ["aggregated_results"] = results,
            ["count"] = results.Count
        };

        return Task.FromResult<NodeExecutionResult>(
            NodeExecutionResult.Success.Single(output, TimeSpan.Zero, new NodeExecutionMetadata())
        );
    }
}

#endregion
