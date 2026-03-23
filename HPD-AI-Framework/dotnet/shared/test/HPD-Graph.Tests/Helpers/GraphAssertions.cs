using FluentAssertions;
using HPDAgent.Graph.Abstractions.Execution;
using HPDAgent.Graph.Core.Context;

namespace HPD.Graph.Tests.Helpers;

/// <summary>
/// Custom assertions for graph execution results.
/// </summary>
public static class GraphAssertions
{
    public static void ShouldBeSuccess(this NodeExecutionResult result)
    {
        result.Should().BeOfType<NodeExecutionResult.Success>();
    }

    public static void ShouldBeFailure(this NodeExecutionResult result)
    {
        result.Should().BeOfType<NodeExecutionResult.Failure>();
    }

    public static void ShouldBeSuspended(this NodeExecutionResult result)
    {
        result.Should().BeOfType<NodeExecutionResult.Suspended>();
    }

    public static void ShouldBeSkipped(this NodeExecutionResult result)
    {
        result.Should().BeOfType<NodeExecutionResult.Skipped>();
    }

    public static void ShouldHaveOutput<T>(this NodeExecutionResult.Success result, string key, T expectedValue)
    {
        var port0Outputs = result.PortOutputs.TryGetValue(0, out var outputs) ? outputs : new Dictionary<string, object>();
        port0Outputs.Should().ContainKey(key);
        port0Outputs[key].Should().Be(expectedValue);
    }

    public static void ShouldHaveTransientError(this NodeExecutionResult.Failure result)
    {
        result.IsTransient.Should().BeTrue();
        result.Severity.Should().Be(ErrorSeverity.Transient);
    }

    public static void ShouldHaveFatalError(this NodeExecutionResult.Failure result)
    {
        result.IsTransient.Should().BeFalse();
        result.Severity.Should().Be(ErrorSeverity.Fatal);
    }

    public static void ShouldHaveCompletedNode(this GraphContext context, string nodeId)
    {
        context.IsNodeComplete(nodeId).Should().BeTrue($"Node '{nodeId}' should be marked complete");
    }

    public static void ShouldNotHaveCompletedNode(this GraphContext context, string nodeId)
    {
        context.IsNodeComplete(nodeId).Should().BeFalse($"Node '{nodeId}' should not be marked complete");
    }

    public static void ShouldHaveChannelValue<T>(this GraphContext context, string channelName, T expectedValue)
    {
        var value = context.Channels[channelName].Get<T>();
        value.Should().Be(expectedValue);
    }

    public static void ShouldHaveLogEntry(this GraphContext context, string containing)
    {
        var logs = context.LogEntries;
        logs.Should().Contain(log => log.Message.Contains(containing));
    }
}
