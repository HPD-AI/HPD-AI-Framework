using Xunit;
using HPD.Agent;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HPD.Agent.Tests.Core;

/// <summary>
/// Tests for AgentExecutionContext functionality including creation, hierarchy, and event attribution
/// </summary>
public class ExecutionContextTests
{
    // ===== P0: AgentExecutionContext Creation =====

    [Fact]
    public void AgentExecutionContext_CanBeCreated_WithRequiredProperties()
    {
        // Arrange & Act
        var context = new AgentExecutionContext
        {
            AgentName = "TestAgent",
            AgentId = "test-abc12345"
        };

        // Assert
        Assert.NotNull(context);
        Assert.Equal("TestAgent", context.AgentName);
        Assert.Equal("test-abc12345", context.AgentId);
        Assert.Null(context.ParentAgentId);
        Assert.Empty(context.AgentChain);
        Assert.Equal(0, context.Depth);
        Assert.False(context.IsSubAgent);
    }

    [Fact]
    public void AgentExecutionContext_RootOrchestrator_HasDepthZero()
    {
        // Arrange & Act
        var context = new AgentExecutionContext
        {
            AgentName = "Orchestrator",
            AgentId = "orchestrator-abc12345",
            ParentAgentId = null,
            AgentChain = new[] { "Orchestrator" },
            Depth = 0
        };

        // Assert
        Assert.Equal(0, context.Depth);
        Assert.False(context.IsSubAgent); // Depth 0 = not a SubAgent
        Assert.Null(context.ParentAgentId);
        Assert.Single(context.AgentChain);
    }

    [Fact]
    public void AgentExecutionContext_DirectSubAgent_HasDepthOne()
    {
        // Arrange & Act
        var context = new AgentExecutionContext
        {
            AgentName = "WeatherExpert",
            AgentId = "orchestrator-abc12345-weatherExpert-def67890",
            ParentAgentId = "orchestrator-abc12345",
            AgentChain = new[] { "Orchestrator", "WeatherExpert" },
            Depth = 1
        };

        // Assert
        Assert.Equal(1, context.Depth);
        Assert.True(context.IsSubAgent); // Depth > 0 = SubAgent
        Assert.Equal("orchestrator-abc12345", context.ParentAgentId);
        Assert.Equal(2, context.AgentChain.Count);
    }

    [Fact]
    public void AgentExecutionContext_NestedSubAgent_HasCorrectDepth()
    {
        // Arrange & Act - 3 levels deep
        var context = new AgentExecutionContext
        {
            AgentName = "DataFetcher",
            AgentId = "orch-abc-domain-xyz-dataFetcher-def",
            ParentAgentId = "orch-abc-domain-xyz",
            AgentChain = new[] { "Orchestrator", "DomainExpert", "DataFetcher" },
            Depth = 2
        };

        // Assert
        Assert.Equal(2, context.Depth);
        Assert.True(context.IsSubAgent);
        Assert.Equal(3, context.AgentChain.Count);
        Assert.Equal("Orchestrator", context.AgentChain[0]);
        Assert.Equal("DomainExpert", context.AgentChain[1]);
        Assert.Equal("DataFetcher", context.AgentChain[2]);
    }

    // ===== P0: IsSubAgent Property =====

    [Theory]
    [InlineData(0, false)] // Root orchestrator
    [InlineData(1, true)]  // Direct SubAgent
    [InlineData(2, true)]  // Nested SubAgent
    [InlineData(5, true)]  // Deep nested SubAgent
    public void AgentExecutionContext_IsSubAgent_CorrectlyIdentifiesSubAgents(int depth, bool expectedIsSubAgent)
    {
        // Arrange & Act
        var context = new AgentExecutionContext
        {
            AgentName = "TestAgent",
            AgentId = "test-123",
            Depth = depth
        };

        // Assert
        Assert.Equal(expectedIsSubAgent, context.IsSubAgent);
    }

    // ===== P0: Hierarchical Agent IDs =====

    [Fact]
    public void AgentExecutionContext_HierarchicalIds_ShowFullExecutionPath()
    {
        // Arrange - Simulate a 3-level hierarchy
        var rootId = "orchestrator-abc12345";
        var level1Id = $"{rootId}-weatherExpert-def67890";
        var level2Id = $"{level1Id}-dataFetcher-ghi11111";

        // Act - Create contexts
        var rooTMetadata = new AgentExecutionContext
        {
            AgentName = "Orchestrator",
            AgentId = rootId,
            Depth = 0
        };

        var level1Context = new AgentExecutionContext
        {
            AgentName = "WeatherExpert",
            AgentId = level1Id,
            ParentAgentId = rootId,
            Depth = 1
        };

        var level2Context = new AgentExecutionContext
        {
            AgentName = "DataFetcher",
            AgentId = level2Id,
            ParentAgentId = level1Id,
            Depth = 2
        };

        // Assert - IDs show full path
        Assert.Contains("orchestrator", rooTMetadata.AgentId);
        Assert.Contains("orchestrator", level1Context.AgentId);
        Assert.Contains("weatherExpert", level1Context.AgentId);
        Assert.Contains("orchestrator", level2Context.AgentId);
        Assert.Contains("weatherExpert", level2Context.AgentId);
        Assert.Contains("dataFetcher", level2Context.AgentId);
    }

    // ===== P0: Agent Chain =====

    [Fact]
    public void AgentExecutionContext_AgentChain_BuildsCorrectHierarchy()
    {
        // Arrange & Act
        var chain = new List<string> { "Orchestrator", "DomainExpert", "WeatherExpert", "DataFetcher" };
        var context = new AgentExecutionContext
        {
            AgentName = "DataFetcher",
            AgentId = "test-123",
            AgentChain = chain,
            Depth = 3
        };

        // Assert
        Assert.Equal(4, context.AgentChain.Count);
        Assert.Equal("Orchestrator", context.AgentChain[0]);
        Assert.Equal("DomainExpert", context.AgentChain[1]);
        Assert.Equal("WeatherExpert", context.AgentChain[2]);
        Assert.Equal("DataFetcher", context.AgentChain[3]);
    }

    [Fact]
    public void AgentExecutionContext_AgentChain_EmptyForRootWithDefaultInitializer()
    {
        // Arrange & Act - Create without setting AgentChain
        var context = new AgentExecutionContext
        {
            AgentName = "Orchestrator",
            AgentId = "orch-123"
        };

        // Assert - Should have empty chain by default
        Assert.NotNull(context.AgentChain);
        Assert.Empty(context.AgentChain);
    }

    // ===== P0: AgentEvent ExecutionContext Integration =====

    [Fact]
    public void AgentEvent_CanHaveNullExecutionContext()
    {
        // Arrange & Act - Create a test event
        var evt = new TestAgentEvent();

        // Assert - ExecutionContext is optional (null by default)
        Assert.Null(evt.ExecutionContext);
    }

    [Fact]
    public void AgentEvent_CanAttachExecutionContext()
    {
        // Arrange
        var context = new AgentExecutionContext
        {
            AgentName = "WeatherExpert",
            AgentId = "weather-abc123",
            Depth = 1
        };

        // Act - Create event with context using record 'with' syntax
        var evt = new TestAgentEvent
        {
            ExecutionContext = context
        };

        // Assert
        Assert.NotNull(evt.ExecutionContext);
        Assert.Equal("WeatherExpert", evt.ExecutionContext.AgentName);
        Assert.Equal("weather-abc123", evt.ExecutionContext.AgentId);
        Assert.Equal(1, evt.ExecutionContext.Depth);
    }

    [Fact]
    public void AgentEvent_ExecutionContext_SupportsRecordWithSyntax()
    {
        // Arrange
        var originalContext = new AgentExecutionContext
        {
            AgentName = "OriginalAgent",
            AgentId = "original-123",
            Depth = 1
        };

        var evt = new TestAgentEvent { ExecutionContext = originalContext };

        // Act - Create new context using 'with' syntax
        var newContext = new AgentExecutionContext
        {
            AgentName = "NewAgent",
            AgentId = "new-456",
            Depth = 2
        };

        var newEvt = evt with { ExecutionContext = newContext };

        // Assert - Original unchanged, new event has new context
        Assert.Equal("OriginalAgent", evt.ExecutionContext!.AgentName);
        Assert.Equal("NewAgent", newEvt.ExecutionContext!.AgentName);
    }

    // ===== P0: Filtering by ExecutionContext =====

    [Fact]
    public void Events_CanBeFiltered_ByAgentName()
    {
        // Arrange
        var events = new List<AgentEvent>
        {
            new TestAgentEvent { ExecutionContext = new AgentExecutionContext { AgentName = "WeatherExpert", AgentId = "w-1" } },
            new TestAgentEvent { ExecutionContext = new AgentExecutionContext { AgentName = "MathExpert", AgentId = "m-1" } },
            new TestAgentEvent { ExecutionContext = new AgentExecutionContext { AgentName = "WeatherExpert", AgentId = "w-2" } }
        };

        // Act
        var weatherEvents = events.Where(e => e.ExecutionContext?.AgentName == "WeatherExpert").ToList();

        // Assert
        Assert.Equal(2, weatherEvents.Count);
        Assert.All(weatherEvents, e => Assert.Equal("WeatherExpert", e.ExecutionContext!.AgentName));
    }

    [Fact]
    public void Events_CanBeFiltered_ByDepth()
    {
        // Arrange
        var events = new List<AgentEvent>
        {
            new TestAgentEvent { ExecutionContext = new AgentExecutionContext { AgentName = "Root", AgentId = "r-1", Depth = 0 } },
            new TestAgentEvent { ExecutionContext = new AgentExecutionContext { AgentName = "Sub1", AgentId = "s1-1", Depth = 1 } },
            new TestAgentEvent { ExecutionContext = new AgentExecutionContext { AgentName = "Sub2", AgentId = "s2-1", Depth = 1 } },
            new TestAgentEvent { ExecutionContext = new AgentExecutionContext { AgentName = "Nested", AgentId = "n-1", Depth = 2 } }
        };

        // Act - Get only direct SubAgents (depth = 1)
        var directSubAgents = events.Where(e => e.ExecutionContext?.Depth == 1).ToList();

        // Assert
        Assert.Equal(2, directSubAgents.Count);
        Assert.All(directSubAgents, e => Assert.Equal(1, e.ExecutionContext!.Depth));
    }

    [Fact]
    public void Events_CanBeFiltered_ByIsSubAgent()
    {
        // Arrange
        var events = new List<AgentEvent>
        {
            new TestAgentEvent { ExecutionContext = new AgentExecutionContext { AgentName = "Root", AgentId = "r-1", Depth = 0 } },
            new TestAgentEvent { ExecutionContext = new AgentExecutionContext { AgentName = "Sub1", AgentId = "s1-1", Depth = 1 } },
            new TestAgentEvent { ExecutionContext = new AgentExecutionContext { AgentName = "Sub2", AgentId = "s2-1", Depth = 2 } }
        };

        // Act - Get only SubAgent events (not root orchestrator)
        var subAgentEvents = events.Where(e => e.ExecutionContext?.IsSubAgent == true).ToList();

        // Assert
        Assert.Equal(2, subAgentEvents.Count);
        Assert.All(subAgentEvents, e => Assert.True(e.ExecutionContext!.IsSubAgent));
    }

    [Fact]
    public void Events_CanBeFiltered_ByAgentChainContains()
    {
        // Arrange
        var events = new List<AgentEvent>
        {
            new TestAgentEvent
            {
                ExecutionContext = new AgentExecutionContext
                {
                    AgentName = "Weather",
                    AgentId = "w-1",
                    AgentChain = new[] { "Orchestrator", "DomainExpert", "Weather" },
                    Depth = 2
                }
            },
            new TestAgentEvent
            {
                ExecutionContext = new AgentExecutionContext
                {
                    AgentName = "Math",
                    AgentId = "m-1",
                    AgentChain = new[] { "Orchestrator", "Math" },
                    Depth = 1
                }
            },
            new TestAgentEvent
            {
                ExecutionContext = new AgentExecutionContext
                {
                    AgentName = "Data",
                    AgentId = "d-1",
                    AgentChain = new[] { "Orchestrator", "DomainExpert", "Data" },
                    Depth = 2
                }
            }
        };

        // Act - Get events from DomainExpert subtree
        var domainExpertEvents = events
            .Where(e => e.ExecutionContext?.AgentChain.Contains("DomainExpert") == true)
            .ToList();

        // Assert
        Assert.Equal(2, domainExpertEvents.Count);
        Assert.All(domainExpertEvents, e =>
            Assert.Contains("DomainExpert", e.ExecutionContext!.AgentChain));
    }

    // ===== P0: Parent-Child Relationships =====

    [Fact]
    public void AgentExecutionContext_ParentAgentId_LinksChildToParent()
    {
        // Arrange
        var parentId = "orchestrator-abc123";
        var childId = $"{parentId}-weather-def456";

        // Act
        var parenTMetadata = new AgentExecutionContext
        {
            AgentName = "Orchestrator",
            AgentId = parentId,
            ParentAgentId = null,
            Depth = 0
        };

        var childContext = new AgentExecutionContext
        {
            AgentName = "Weather",
            AgentId = childId,
            ParentAgentId = parentId,
            Depth = 1
        };

        // Assert
        Assert.Null(parenTMetadata.ParentAgentId);
        Assert.Equal(parentId, childContext.ParentAgentId);
        Assert.StartsWith(parentId, childContext.AgentId);
    }

    // ===== Helper Test Event =====

    private record TestAgentEvent : AgentEvent;
}
