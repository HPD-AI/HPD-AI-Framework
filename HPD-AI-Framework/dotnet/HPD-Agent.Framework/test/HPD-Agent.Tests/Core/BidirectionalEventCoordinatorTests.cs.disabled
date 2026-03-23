using HPD.Agent;
using HPD.Agent.Tests.Infrastructure;
using Microsoft.Extensions.AI;
using Xunit;

namespace HPD.Agent.Tests.Core;

/// <summary>
/// Tests for BidirectionalEventCoordinator ExecutionContext auto-attachment.
/// NOTE: Core cycle detection and event bubbling tests have been migrated to HPD.Events.Tests/EventCoordinatorTests.cs
/// These tests focus on agent-specific behavior (ExecutionContext enrichment).
/// </summary>
public class BidirectionalEventCoordinatorTests
{
    // ===== P0: ExecutionContext Auto-Attachment =====

    [Fact]
    public void Emit_AutoAttachesExecutionContext_WhenEventHasNone()
    {
        // Arrange
        var agent = TestAgentFactory.Create();

        // Set ExecutionContext on agent
        agent.ExecutionContext = new AgentExecutionContext
        {
            AgentName = "TestAgent",
            AgentId = "test-abc123",
            Depth = 0
        };

        // Act - Emit event without ExecutionContext
        var evt = new TestAgentEvent { Message = "Test" };
        Assert.Null(evt.ExecutionContext); // Verify it starts null

        agent.EventCoordinator.Emit(evt);

        // Read emitted event from channel
        agent.EventCoordinator.TryRead(out var emittedEvent);

        // Assert - ExecutionContext should be auto-attached
        Assert.NotNull(emittedEvent);
        Assert.NotNull(emittedEvent.ExecutionContext);
        Assert.Equal("TestAgent", emittedEvent.ExecutionContext!.AgentName);
        Assert.Equal("test-abc123", emittedEvent.ExecutionContext.AgentId);
    }

    [Fact]
    public void Emit_PreservesExecutionContext_WhenEventAlreadyHasOne()
    {
        // Arrange
        var agent = TestAgentFactory.Create();

        agent.ExecutionContext = new AgentExecutionContext
        {
            AgentName = "TestAgent",
            AgentId = "test-abc123",
            Depth = 0
        };

        // Act - Emit event WITH ExecutionContext already set
        var customContext = new AgentExecutionContext
        {
            AgentName = "CustomAgent",
            AgentId = "custom-xyz789",
            Depth = 5
        };

        var evt = new TestAgentEvent { Message = "Test", ExecutionContext = customContext };
        agent.EventCoordinator.Emit(evt);

        // Read emitted event
        agent.EventCoordinator.TryRead(out var emittedEvent);

        // Assert - Original ExecutionContext should be preserved
        Assert.NotNull(emittedEvent!.ExecutionContext);
        Assert.Equal("CustomAgent", emittedEvent.ExecutionContext!.AgentName);
        Assert.Equal("custom-xyz789", emittedEvent.ExecutionContext.AgentId);
        Assert.Equal(5, emittedEvent.ExecutionContext.Depth);
    }

    [Fact]
    public void Emit_BubblesEventWithContext_ToParentCoordinator()
    {
        // Arrange - Create parent and child agents
        var parentAgent = TestAgentFactory.Create(new AgentConfig
        {
            Name = "ParentAgent",
            Provider = new ProviderConfig { ProviderKey = "test", ModelName = "test-model" }
        });
        var childAgent = TestAgentFactory.Create(new AgentConfig
        {
            Name = "ChildAgent",
            Provider = new ProviderConfig { ProviderKey = "test", ModelName = "test-model" }
        });

        // Set ExecutionContexts
        parentAgent.ExecutionContext = new AgentExecutionContext
        {
            AgentName = "ParentAgent",
            AgentId = "parent-abc123",
            Depth = 0
        };

        childAgent.ExecutionContext = new AgentExecutionContext
        {
            AgentName = "ChildAgent",
            AgentId = "parent-abc123-child-def456",
            ParentAgentId = "parent-abc123",
            AgentChain = new[] { "ParentAgent", "ChildAgent" },
            Depth = 1
        };

        // Link child to parent
        childAgent.EventCoordinator.SetParent(parentAgent.EventCoordinator);

        // Act - Child emits event
        var evt = new TestAgentEvent { Message = "Child event" };
        childAgent.EventCoordinator.Emit(evt);

        // Assert - Event should appear in both child and parent channels with context
        // Child channel
        Assert.True(childAgent.EventCoordinator.TryRead(out var childEvent));
        Assert.NotNull(childEvent!.ExecutionContext);
        Assert.Equal("ChildAgent", childEvent.ExecutionContext!.AgentName);

        // Parent channel (bubbled event)
        Assert.True(parentAgent.EventCoordinator.TryRead(out var parentEvent));
        Assert.NotNull(parentEvent!.ExecutionContext);
        Assert.Equal("ChildAgent", parentEvent.ExecutionContext!.AgentName); // Context preserved during bubbling
        Assert.Equal(1, parentEvent.ExecutionContext.Depth);
    }

    [Fact]
    public void Emit_MultipleChildrenEvents_BubbleWithCorrecTMetadatas()
    {
        // Arrange - Parent with two children
        var parent = TestAgentFactory.Create(new AgentConfig
        {
            Name = "Orchestrator",
            Provider = new ProviderConfig { ProviderKey = "test", ModelName = "test-model" }
        });
        var child1 = TestAgentFactory.Create(new AgentConfig
        {
            Name = "WeatherExpert",
            Provider = new ProviderConfig { ProviderKey = "test", ModelName = "test-model" }
        });
        var child2 = TestAgentFactory.Create(new AgentConfig
        {
            Name = "MathExpert",
            Provider = new ProviderConfig { ProviderKey = "test", ModelName = "test-model" }
        });

        parent.ExecutionContext = new AgentExecutionContext
        {
            AgentName = "Orchestrator",
            AgentId = "orch-123",
            Depth = 0
        };

        child1.ExecutionContext = new AgentExecutionContext
        {
            AgentName = "WeatherExpert",
            AgentId = "orch-123-weather-456",
            ParentAgentId = "orch-123",
            Depth = 1
        };

        child2.ExecutionContext = new AgentExecutionContext
        {
            AgentName = "MathExpert",
            AgentId = "orch-123-math-789",
            ParentAgentId = "orch-123",
            Depth = 1
        };

        // Link children to parent
        child1.EventCoordinator.SetParent(parent.EventCoordinator);
        child2.EventCoordinator.SetParent(parent.EventCoordinator);

        // Act - Both children emit events
        child1.EventCoordinator.Emit(new TestAgentEvent { Message = "Weather event" });
        child2.EventCoordinator.Emit(new TestAgentEvent { Message = "Math event" });

        // Assert - Parent should receive both events with correct contexts
        var parentEvents = new List<AgentEvent>();
        while (parent.EventCoordinator.TryRead(out var evt))
        {
            parentEvents.Add(evt);
        }

        Assert.Equal(2, parentEvents.Count);

        // Verify contexts are preserved
        var weatherEvent = parentEvents.FirstOrDefault(e => e.ExecutionContext?.AgentName == "WeatherExpert");
        var mathEvent = parentEvents.FirstOrDefault(e => e.ExecutionContext?.AgentName == "MathExpert");

        Assert.NotNull(weatherEvent);
        Assert.NotNull(mathEvent);
        Assert.Equal("orch-123-weather-456", weatherEvent!.ExecutionContext!.AgentId);
        Assert.Equal("orch-123-math-789", mathEvent!.ExecutionContext!.AgentId);
    }
}

/// <summary>
/// Test event record for event bubbling tests
/// </summary>
internal sealed record TestAgentEvent(string Message = "") : AgentEvent;
