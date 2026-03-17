using FluentAssertions;
using HPD.Graph.Tests.Helpers;
using HPDAgent.Graph.Core.Context;
using Xunit;

namespace HPD.Graph.Tests.Context;

/// <summary>
/// Tests for graph context management and isolation.
/// </summary>
public class ContextManagementTests
{
    [Fact]
    public void GraphContext_Creation_ShouldInitializeCorrectly()
    {
        // Arrange
        var graph = new TestGraphBuilder()
            .AddStartNode()
            .AddEndNode()
            .Build();

        var services = TestServiceProvider.Create();

        // Act
        var context = new GraphContext("test-exec", graph, services);

        // Assert
        context.ExecutionId.Should().Be("test-exec");
        context.Graph.Should().Be(graph);
        context.Services.Should().Be(services);
    }

    [Fact]
    public void GraphContext_MarkNodeComplete_ShouldTrackCompletion()
    {
        // Arrange
        var graph = new TestGraphBuilder().AddStartNode().AddEndNode().Build();
        var services = TestServiceProvider.Create();
        var context = new GraphContext("test", graph, services);

        // Act
        context.MarkNodeComplete("node1");
        context.MarkNodeComplete("node2");

        // Assert
        context.IsNodeComplete("node1").Should().BeTrue();
        context.IsNodeComplete("node2").Should().BeTrue();
        context.IsNodeComplete("node3").Should().BeFalse();
    }

    [Fact]
    public void GraphContext_IncrementExecutionCount_ShouldTrack()
    {
        // Arrange
        var graph = new TestGraphBuilder().AddStartNode().AddEndNode().Build();
        var services = TestServiceProvider.Create();
        var context = new GraphContext("test", graph, services);

        // Act
        context.IncrementNodeExecutionCount("node1");
        context.IncrementNodeExecutionCount("node1");
        context.IncrementNodeExecutionCount("node2");

        // Assert
        context.GetNodeExecutionCount("node1").Should().Be(2);
        context.GetNodeExecutionCount("node2").Should().Be(1);
        context.GetNodeExecutionCount("node3").Should().Be(0);
    }

    [Fact]
    public void GraphContext_Logging_ShouldCaptureEntries()
    {
        // Arrange
        var graph = new TestGraphBuilder().AddStartNode().AddEndNode().Build();
        var services = TestServiceProvider.Create();
        var context = new GraphContext("test", graph, services);

        // Act
        context.Log("Component1", "Message 1", HPDAgent.Graph.Abstractions.Context.LogLevel.Information);
        context.Log("Component2", "Message 2", HPDAgent.Graph.Abstractions.Context.LogLevel.Debug);
        context.Log("Component3", "Message 3", HPDAgent.Graph.Abstractions.Context.LogLevel.Warning);

        // Assert
        context.LogEntries.Should().HaveCount(3);
        context.LogEntries.Should().Contain(e => e.Message == "Message 1");
        context.LogEntries.Should().Contain(e => e.Message == "Message 2");
        context.LogEntries.Should().Contain(e => e.Message == "Message 3");
    }

    [Fact]
    public void GraphContext_Tags_ShouldStoreMultipleValues()
    {
        // Arrange
        var graph = new TestGraphBuilder().AddStartNode().AddEndNode().Build();
        var services = TestServiceProvider.Create();
        var context = new GraphContext("test", graph, services);

        // Act
        context.AddTag("category", "value1");
        context.AddTag("category", "value2");
        context.AddTag("category", "value3");
        context.AddTag("other", "single");

        // Assert
        context.Tags.Should().ContainKey("category");
        context.Tags["category"].Should().HaveCount(3);
        context.Tags["category"].Should().Contain("value1");
        context.Tags["category"].Should().Contain("value2");
        context.Tags["category"].Should().Contain("value3");
        context.Tags["other"].Should().ContainSingle("single");
    }

    [Fact]
    public void GraphContext_Channels_ShouldBeAccessible()
    {
        // Arrange
        var graph = new TestGraphBuilder().AddStartNode().AddEndNode().Build();
        var services = TestServiceProvider.Create();
        var context = new GraphContext("test", graph, services);

        // Act
        context.Channels["test_channel"].Set("test_value");
        var value = context.Channels["test_channel"].Get<string>();

        // Assert
        value.Should().Be("test_value");
    }

    [Fact]
    public void GraphContext_CreateIsolatedCopy_ShouldIsolate()
    {
        // Arrange
        var graph = new TestGraphBuilder().AddStartNode().AddEndNode().Build();
        var services = TestServiceProvider.Create();
        var original = new GraphContext("test", graph, services);
        original.MarkNodeComplete("node1");
        original.AddTag("tag1", "value1");

        // Act
        var isolated = original.CreateIsolatedCopy();

        // Assert - Isolated copy SHOULD have completed nodes (needed for PrepareInputs to work)
        // This was changed from BeFalse() to BeTrue() because isolated contexts need to know
        // which upstream nodes have completed in order to properly evaluate edge conditions
        isolated.IsNodeComplete("node1").Should().BeTrue();
        isolated.ExecutionId.Should().Be(original.ExecutionId);
    }

    [Fact]
    public void GraphContext_MergeFrom_ShouldCombineState()
    {
        // Arrange
        var graph = new TestGraphBuilder().AddStartNode().AddEndNode().Build();
        var services = TestServiceProvider.Create();

        var main = new GraphContext("test", graph, services);
        main.MarkNodeComplete("node1");

        var other = new GraphContext("test2", graph, services);
        other.MarkNodeComplete("node2");
        other.AddTag("tag", "value");

        // Act
        main.MergeFrom(other);

        // Assert
        main.IsNodeComplete("node1").Should().BeTrue();
        main.IsNodeComplete("node2").Should().BeTrue();
        main.Tags.Should().ContainKey("tag");
    }

    [Fact]
    public void GraphContext_ExecutionId_ShouldBeUnique()
    {
        // Arrange
        var graph = new TestGraphBuilder().AddStartNode().AddEndNode().Build();
        var services = TestServiceProvider.Create();

        // Act
        var context1 = new GraphContext("exec1", graph, services);
        var context2 = new GraphContext("exec2", graph, services);

        // Assert
        context1.ExecutionId.Should().NotBe(context2.ExecutionId);
    }
}
