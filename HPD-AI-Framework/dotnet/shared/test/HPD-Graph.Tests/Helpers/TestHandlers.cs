using HPDAgent.Graph.Abstractions.Execution;
using HPDAgent.Graph.Abstractions.Handlers;
using HPDAgent.Graph.Core.Context;

namespace HPD.Graph.Tests.Helpers;

/// <summary>
/// Simple test handler that always succeeds.
/// </summary>
public class SuccessHandler : IGraphNodeHandler<GraphContext>
{
    public string HandlerName => "SuccessHandler";

    public Task<NodeExecutionResult> ExecuteAsync(GraphContext context, HandlerInputs inputs, CancellationToken cancellationToken = default)
    {
        var output = inputs.TryGet<string>("input", out var value) ? value : "success";
        return Task.FromResult<NodeExecutionResult>(NodeExecutionResult.Success.Single(
            output: new Dictionary<string, object> { ["output"] = output },
            duration: TimeSpan.FromMilliseconds(10),
            metadata: new NodeExecutionMetadata()
        ));
    }
}

/// <summary>
/// Test handler that always fails.
/// </summary>
public class FailureHandler : IGraphNodeHandler<GraphContext>
{
    public string HandlerName => "FailureHandler";

    public Task<NodeExecutionResult> ExecuteAsync(GraphContext context, HandlerInputs inputs, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<NodeExecutionResult>(new NodeExecutionResult.Failure(
            Exception: new InvalidOperationException("Test failure"),
            Severity: ErrorSeverity.Fatal,
            IsTransient: false,
            Duration: TimeSpan.FromMilliseconds(5)
        ));
    }
}

/// <summary>
/// Test handler that fails transiently (can be retried).
/// </summary>
public class TransientFailureHandler : IGraphNodeHandler<GraphContext>
{
    public string HandlerName => "TransientFailureHandler";

    private int _attempts = 0;
    private readonly int _failuresBeforeSuccess;

    public TransientFailureHandler(int failuresBeforeSuccess = 2)
    {
        _failuresBeforeSuccess = failuresBeforeSuccess;
    }

    public Task<NodeExecutionResult> ExecuteAsync(GraphContext context, HandlerInputs inputs, CancellationToken cancellationToken = default)
    {
        _attempts++;

        if (_attempts <= _failuresBeforeSuccess)
        {
            return Task.FromResult<NodeExecutionResult>(new NodeExecutionResult.Failure(
                Exception: new InvalidOperationException($"Transient failure (attempt {_attempts})"),
                Severity: ErrorSeverity.Transient,
                IsTransient: true,
                Duration: TimeSpan.FromMilliseconds(5)
            ));
        }

        return Task.FromResult<NodeExecutionResult>(NodeExecutionResult.Success.Single(
            output: new Dictionary<string, object> { ["attempts"] = _attempts },
            duration: TimeSpan.FromMilliseconds(10),
            metadata: new NodeExecutionMetadata()
        ));
    }
}

/// <summary>
/// Test handler that suspends for human input.
/// </summary>
public class SuspendingHandler : IGraphNodeHandler<GraphContext>
{
    public string HandlerName => "SuspendingHandler";

    public Task<NodeExecutionResult> ExecuteAsync(GraphContext context, HandlerInputs inputs, CancellationToken cancellationToken = default)
    {
        var token = Guid.NewGuid().ToString();
        return Task.FromResult<NodeExecutionResult>(
            NodeExecutionResult.Suspended.ForHumanApproval(
                suspendToken: token,
                message: "Waiting for human approval"
            )
        );
    }
}

/// <summary>
/// Test handler that delays execution.
/// </summary>
public class DelayHandler : IGraphNodeHandler<GraphContext>
{
    public string HandlerName => "DelayHandler";

    private readonly TimeSpan _delay;

    public DelayHandler(TimeSpan delay)
    {
        _delay = delay;
    }

    public async Task<NodeExecutionResult> ExecuteAsync(GraphContext context, HandlerInputs inputs, CancellationToken cancellationToken = default)
    {
        await Task.Delay(_delay, cancellationToken);
        return NodeExecutionResult.Success.Single(
            output: new Dictionary<string, object> { ["delayed"] = true },
            duration: _delay,
            metadata: new NodeExecutionMetadata()
        );
    }
}

/// <summary>
/// Test handler that echoes input to output.
/// </summary>
public class EchoHandler : IGraphNodeHandler<GraphContext>
{
    public string HandlerName => "EchoHandler";

    public Task<NodeExecutionResult> ExecuteAsync(GraphContext context, HandlerInputs inputs, CancellationToken cancellationToken = default)
    {
        var outputs = new Dictionary<string, object>();
        foreach (var (key, value) in inputs.GetAll())
        {
            outputs[key] = value;
        }

        return Task.FromResult<NodeExecutionResult>(NodeExecutionResult.Success.Single(
            output: outputs,
            duration: TimeSpan.FromMilliseconds(1),
            metadata: new NodeExecutionMetadata()
        ));
    }
}

/// <summary>
/// Test handler that increments a counter.
/// </summary>
public class CounterHandler : IGraphNodeHandler<GraphContext>
{
    public string HandlerName => "CounterHandler";

    public Task<NodeExecutionResult> ExecuteAsync(GraphContext context, HandlerInputs inputs, CancellationToken cancellationToken = default)
    {
        var current = inputs.TryGet<int>("count", out var value) ? value : 0;
        return Task.FromResult<NodeExecutionResult>(NodeExecutionResult.Success.Single(
            output: new Dictionary<string, object> { ["count"] = current + 1 },
            duration: TimeSpan.FromMilliseconds(1),
            metadata: new NodeExecutionMetadata()
        ));
    }
}

/// <summary>
/// Test handler that produces a list of items for Map node testing.
/// Reads the list from the "items" channel or uses default items.
/// </summary>
public class ListProducerHandler : IGraphNodeHandler<GraphContext>
{
    public string HandlerName => "ListProducerHandler";

    public Task<NodeExecutionResult> ExecuteAsync(GraphContext context, HandlerInputs inputs, CancellationToken cancellationToken = default)
    {
        // Try to get items from input, or use default
        List<string> items;
        if (inputs.TryGet<List<string>>("items", out var inputItems))
        {
            items = inputItems;
        }
        else if (inputs.TryGet<string>("item_count", out var countStr) && int.TryParse(countStr, out var count))
        {
            items = Enumerable.Range(0, count).Select(i => $"item_{i}").ToList();
        }
        else
        {
            items = new List<string> { "item1", "item2", "item3" };
        }

        return Task.FromResult<NodeExecutionResult>(NodeExecutionResult.Success.Single(
            output: new Dictionary<string, object> { ["output"] = items },
            duration: TimeSpan.FromMilliseconds(1),
            metadata: new NodeExecutionMetadata()
        ));
    }
}

/// <summary>
/// Test handler that reads from a specific channel and outputs the value.
/// Used for map tests to pass through items from channels.
/// </summary>
public class ChannelReaderHandler : IGraphNodeHandler<GraphContext>
{
    public string HandlerName => "ChannelReaderHandler";

    public Task<NodeExecutionResult> ExecuteAsync(GraphContext context, HandlerInputs inputs, CancellationToken cancellationToken = default)
    {
        // Read from specified channel or default to "map_input"
        var channelName = inputs.TryGet<string>("channel", out var ch) ? ch : "map_input";
        var data = context.Channels[channelName].Get<object>();

        return Task.FromResult<NodeExecutionResult>(NodeExecutionResult.Success.Single(
            output: new Dictionary<string, object> { ["output"] = data ?? new List<object>() },
            duration: TimeSpan.FromMilliseconds(1),
            metadata: new NodeExecutionMetadata()
        ));
    }
}

// ===== HETEROGENEOUS MAP TEST HANDLERS =====

/// <summary>
/// Test handler that produces a mixed list of strings and ints.
/// </summary>
public class MixedTypeListProducerHandler : IGraphNodeHandler<GraphContext>
{
    public string HandlerName => "MixedTypeListProducerHandler";

    public Task<NodeExecutionResult> ExecuteAsync(GraphContext context, HandlerInputs inputs, CancellationToken cancellationToken = default)
    {
        var list = new List<object> { "hello", 42, "world" };  // 2 strings + 1 int
        return Task.FromResult<NodeExecutionResult>(NodeExecutionResult.Success.Single(
            output: new Dictionary<string, object> { ["output"] = list },
            duration: TimeSpan.FromMilliseconds(1),
            metadata: new NodeExecutionMetadata()
        ));
    }
}

/// <summary>
/// Test handler that processes strings.
/// </summary>
public class StringProcessorHandler : IGraphNodeHandler<GraphContext>
{
    public string HandlerName => "StringProcessorHandler";

    public Task<NodeExecutionResult> ExecuteAsync(GraphContext context, HandlerInputs inputs, CancellationToken cancellationToken = default)
    {
        var item = inputs.TryGet<string>("item", out var value) ? value : "unknown";
        var processed = $"processed_{item}";

        return Task.FromResult<NodeExecutionResult>(NodeExecutionResult.Success.Single(
            output: new Dictionary<string, object> { ["output"] = processed },
            duration: TimeSpan.FromMilliseconds(1),
            metadata: new NodeExecutionMetadata()
        ));
    }
}

/// <summary>
/// Test handler that processes integers.
/// </summary>
public class IntProcessorHandler : IGraphNodeHandler<GraphContext>
{
    public string HandlerName => "IntProcessorHandler";

    public Task<NodeExecutionResult> ExecuteAsync(GraphContext context, HandlerInputs inputs, CancellationToken cancellationToken = default)
    {
        var item = inputs.TryGet<int>("item", out var value) ? value : 0;
        var processed = item * 2; // Double the number

        return Task.FromResult<NodeExecutionResult>(NodeExecutionResult.Success.Single(
            output: new Dictionary<string, object> { ["output"] = processed },
            duration: TimeSpan.FromMilliseconds(1),
            metadata: new NodeExecutionMetadata()
        ));
    }
}

/// <summary>
/// Test handler that processes items with default logic.
/// </summary>
public class DefaultProcessorHandler : IGraphNodeHandler<GraphContext>
{
    public string HandlerName => "DefaultProcessorHandler";

    public Task<NodeExecutionResult> ExecuteAsync(GraphContext context, HandlerInputs inputs, CancellationToken cancellationToken = default)
    {
        var item = inputs.TryGet<object>("item", out var value) ? value : null;
        var processed = $"default_{item}";

        return Task.FromResult<NodeExecutionResult>(NodeExecutionResult.Success.Single(
            output: new Dictionary<string, object> { ["output"] = processed },
            duration: TimeSpan.FromMilliseconds(1),
            metadata: new NodeExecutionMetadata()
        ));
    }
}

/// <summary>
/// Test handler that produces a list of TestDocument objects.
/// </summary>
public class DocumentListProducerHandler : IGraphNodeHandler<GraphContext>
{
    public string HandlerName => "DocumentListProducerHandler";

    public Task<NodeExecutionResult> ExecuteAsync(GraphContext context, HandlerInputs inputs, CancellationToken cancellationToken = default)
    {
        var docs = new List<TestDocument>
        {
            new() { Type = "pdf", Content = "PDF content" },
            new() { Type = "image", Content = "Image data" },
            new() { Type = "pdf", Content = "Another PDF" }
        };

        return Task.FromResult<NodeExecutionResult>(NodeExecutionResult.Success.Single(
            output: new Dictionary<string, object> { ["output"] = docs },
            duration: TimeSpan.FromMilliseconds(1),
            metadata: new NodeExecutionMetadata()
        ));
    }
}

/// <summary>
/// Test handler that processes PDF documents.
/// </summary>
public class PdfProcessorHandler : IGraphNodeHandler<GraphContext>
{
    public string HandlerName => "PdfProcessorHandler";

    public Task<NodeExecutionResult> ExecuteAsync(GraphContext context, HandlerInputs inputs, CancellationToken cancellationToken = default)
    {
        var doc = inputs.TryGet<TestDocument>("item", out var value) ? value : new TestDocument();
        var processed = $"pdf_processed_{doc.Content}";

        return Task.FromResult<NodeExecutionResult>(NodeExecutionResult.Success.Single(
            output: new Dictionary<string, object> { ["output"] = processed },
            duration: TimeSpan.FromMilliseconds(1),
            metadata: new NodeExecutionMetadata()
        ));
    }
}

/// <summary>
/// Test handler that processes image documents.
/// </summary>
public class ImageProcessorHandler : IGraphNodeHandler<GraphContext>
{
    public string HandlerName => "ImageProcessorHandler";

    public Task<NodeExecutionResult> ExecuteAsync(GraphContext context, HandlerInputs inputs, CancellationToken cancellationToken = default)
    {
        var doc = inputs.TryGet<TestDocument>("item", out var value) ? value : new TestDocument();
        var processed = $"image_processed_{doc.Content}";

        return Task.FromResult<NodeExecutionResult>(NodeExecutionResult.Success.Single(
            output: new Dictionary<string, object> { ["output"] = processed },
            duration: TimeSpan.FromMilliseconds(1),
            metadata: new NodeExecutionMetadata()
        ));
    }
}
