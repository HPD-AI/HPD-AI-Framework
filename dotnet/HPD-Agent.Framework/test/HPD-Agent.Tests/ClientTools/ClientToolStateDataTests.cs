// Copyright (c) 2025 Einstein Essibu. All rights reserved.

using HPD.Agent;
using HPD.Agent.ClientTools;
using System.Collections.Immutable;
using Xunit;

namespace HPD.Agent.Tests.ClientTools;

/// <summary>
/// Unit tests for ClientToolStateData immutable state management.
/// Tests all With* methods and state transitions.
/// </summary>
public class ClientToolStateDataTests
{
    // ============================================
    // Constructor Tests
    // ============================================

    [Fact]
    public void Constructor_CreatesEmptyState()
    {
        // Arrange & Act
        var state = new ClientToolStateData();

        // Assert
        Assert.NotNull(state);
        Assert.Empty(state.RegisteredToolHarnesses);
        Assert.Empty(state.ExpandedToolHarnesses);
        Assert.Empty(state.HiddenTools);
        Assert.Empty(state.Context);
        Assert.Null(state.State);
        Assert.Null(state.PendingAugmentation);
    }

    // ============================================
    // ToolHarness Registration Tests
    // ============================================

    [Fact]
    public void WithRegisteredToolHarness_AddsToolHarness()
    {
        // Arrange
        var state = new ClientToolStateData();
        var ToolHarness = CreateTestToolHarness("TestToolHarness");

        // Act
        var updated = state.WithRegisteredToolHarness(ToolHarness);

        // Assert
        Assert.NotSame(state, updated);
        Assert.Single(updated.RegisteredToolHarnesses);
        Assert.True(updated.RegisteredToolHarnesses.ContainsKey("TestToolHarness"));
        Assert.Equal(ToolHarness, updated.RegisteredToolHarnesses["TestToolHarness"]);
    }

    [Fact]
    public void WithRegisteredToolHarness_MultipleCalls_AddsAllToolHarnesses()
    {
        // Arrange
        var state = new ClientToolStateData();
        var ToolHarness1 = CreateTestToolHarness("ToolHarness1");
        var ToolHarness2 = CreateTestToolHarness("ToolHarness2");

        // Act
        var updated = state
            .WithRegisteredToolHarness(ToolHarness1)
            .WithRegisteredToolHarness(ToolHarness2);

        // Assert
        Assert.Equal(2, updated.RegisteredToolHarnesses.Count);
        Assert.True(updated.RegisteredToolHarnesses.ContainsKey("ToolHarness1"));
        Assert.True(updated.RegisteredToolHarnesses.ContainsKey("ToolHarness2"));
    }

    [Fact]
    public void WithRegisteredToolHarness_SameName_ReplacesToolHarness()
    {
        // Arrange
        var state = new ClientToolStateData();
        var ToolHarness1 = CreateTestToolHarness("TestToolHarness", tools: new[] { CreateTestTool("Tool1") });
        var ToolHarness2 = CreateTestToolHarness("TestToolHarness", tools: new[] { CreateTestTool("Tool2") });

        // Act
        var updated = state
            .WithRegisteredToolHarness(ToolHarness1)
            .WithRegisteredToolHarness(ToolHarness2);

        // Assert
        Assert.Single(updated.RegisteredToolHarnesses);
        Assert.Equal("Tool2", updated.RegisteredToolHarnesses["TestToolHarness"].Tools[0].Name);
    }

    [Fact]
    public void WithoutRegisteredToolHarness_RemovesToolHarness()
    {
        // Arrange
        var state = new ClientToolStateData()
            .WithRegisteredToolHarness(CreateTestToolHarness("ToolHarness1"))
            .WithRegisteredToolHarness(CreateTestToolHarness("ToolHarness2"));

        // Act
        var updated = state.WithoutRegisteredToolHarness("ToolHarness1");

        // Assert
        Assert.Single(updated.RegisteredToolHarnesses);
        Assert.False(updated.RegisteredToolHarnesses.ContainsKey("ToolHarness1"));
        Assert.True(updated.RegisteredToolHarnesses.ContainsKey("ToolHarness2"));
    }

    [Fact]
    public void WithoutRegisteredToolHarness_NonExistent_ReturnsUnchanged()
    {
        // Arrange
        var state = new ClientToolStateData()
            .WithRegisteredToolHarness(CreateTestToolHarness("ToolHarness1"));

        // Act
        var updated = state.WithoutRegisteredToolHarness("NonExistent");

        // Assert
        Assert.Single(updated.RegisteredToolHarnesses);
    }

    // ============================================
    // Expanded ToolHarnesses Tests
    // ============================================

    [Fact]
    public void WithExpandedToolHarness_AddsToSet()
    {
        // Arrange
        var state = new ClientToolStateData();

        // Act
        var updated = state.WithExpandedToolHarness("ToolHarness1");

        // Assert
        Assert.NotSame(state, updated);
        Assert.Single(updated.ExpandedToolHarnesses);
        Assert.Contains("ToolHarness1", updated.ExpandedToolHarnesses);
    }

    [Fact]
    public void WithExpandedToolHarness_Duplicate_NoEffect()
    {
        // Arrange
        var state = new ClientToolStateData()
            .WithExpandedToolHarness("ToolHarness1");

        // Act
        var updated = state.WithExpandedToolHarness("ToolHarness1");

        // Assert
        Assert.Single(updated.ExpandedToolHarnesses);
    }

    [Fact]
    public void WithCollapsedToolHarness_RemovesFromSet()
    {
        // Arrange
        var state = new ClientToolStateData()
            .WithExpandedToolHarness("ToolHarness1")
            .WithExpandedToolHarness("ToolHarness2");

        // Act
        var updated = state.WithCollapsedToolHarness("ToolHarness1");

        // Assert
        Assert.Single(updated.ExpandedToolHarnesses);
        Assert.DoesNotContain("ToolHarness1", updated.ExpandedToolHarnesses);
        Assert.Contains("ToolHarness2", updated.ExpandedToolHarnesses);
    }

    // ============================================
    // Hidden Tools Tests
    // ============================================

    [Fact]
    public void WithHiddenTool_AddsToSet()
    {
        // Arrange
        var state = new ClientToolStateData();

        // Act
        var updated = state.WithHiddenTool("Tool1");

        // Assert
        Assert.NotSame(state, updated);
        Assert.Single(updated.HiddenTools);
        Assert.Contains("Tool1", updated.HiddenTools);
    }

    [Fact]
    public void WithVisibleTool_RemovesFromSet()
    {
        // Arrange
        var state = new ClientToolStateData()
            .WithHiddenTool("Tool1")
            .WithHiddenTool("Tool2");

        // Act
        var updated = state.WithVisibleTool("Tool1");

        // Assert
        Assert.Single(updated.HiddenTools);
        Assert.DoesNotContain("Tool1", updated.HiddenTools);
        Assert.Contains("Tool2", updated.HiddenTools);
    }

    // ============================================
    // Context Tests
    // ============================================

    [Fact]
    public void WithContextItem_AddsItem()
    {
        // Arrange
        var state = new ClientToolStateData();
        var contextItem = new ContextItem("Test description", "test-value", "test-key");

        // Act
        var updated = state.WithContextItem(contextItem);

        // Assert
        Assert.NotSame(state, updated);
        Assert.Single(updated.Context);
        Assert.True(updated.Context.ContainsKey("test-key"));
    }

    [Fact]
    public void WithouTMetadata_RemovesItem()
    {
        // Arrange
        var state = new ClientToolStateData()
            .WithContextItem(new ContextItem("Desc1", "val1", "key1"))
            .WithContextItem(new ContextItem("Desc2", "val2", "key2"));

        // Act
        var updated = state.WithouTMetadata("key1");

        // Assert
        Assert.Single(updated.Context);
        Assert.False(updated.Context.ContainsKey("key1"));
        Assert.True(updated.Context.ContainsKey("key2"));
    }

    [Fact]
    public void ClearContext_RemovesAllItems()
    {
        // Arrange
        var state = new ClientToolStateData()
            .WithContextItem(new ContextItem("Desc1", "val1", "key1"))
            .WithContextItem(new ContextItem("Desc2", "val2", "key2"));

        // Act
        var updated = state.ClearContext();

        // Assert
        Assert.Empty(updated.Context);
    }

    // ============================================
    // State Tests
    // ============================================

    [Fact]
    public void WithState_SetsState()
    {
        // Arrange
        var state = new ClientToolStateData();
        var appState = System.Text.Json.JsonSerializer.SerializeToElement(new { foo = "bar" });

        // Act
        var updated = state.WithState(appState);

        // Assert
        Assert.NotSame(state, updated);
        Assert.NotNull(updated.State);
    }

    // ============================================
    // Pending Augmentation Tests
    // ============================================

    [Fact]
    public void WithPendingAugmentation_SetsAugmentation()
    {
        // Arrange
        var state = new ClientToolStateData();
        var augmentation = new ClientToolAugmentation
        {
            ExpandToolHarnesses = new HashSet<string> { "ToolHarness1" }
        };

        // Act
        var updated = state.WithPendingAugmentation(augmentation);

        // Assert
        Assert.NotSame(state, updated);
        Assert.NotNull(updated.PendingAugmentation);
        Assert.Contains("ToolHarness1", updated.PendingAugmentation.ExpandToolHarnesses!);
    }

    [Fact]
    public void ClearPendingAugmentation_RemovesAugmentation()
    {
        // Arrange
        var state = new ClientToolStateData()
            .WithPendingAugmentation(new ClientToolAugmentation { ExpandToolHarnesses = new HashSet<string> { "ToolHarness1" } });

        // Act
        var updated = state.ClearPendingAugmentation();

        // Assert
        Assert.Null(updated.PendingAugmentation);
    }

    // ============================================
    // MiddlewareState Integration Tests
    // ============================================

    [Fact]
    public void MiddlewareState_ClientTool_PropertyExists()
    {
        // Arrange
        var container = new MiddlewareState();

        // Act
        var state = container.ClientTool();

        // Assert - extension method exists and returns null initially
        Assert.Null(state);
    }

    [Fact]
    public void MiddlewareState_WithClientTool_CreatesNewInstance()
    {
        // Arrange
        var container = new MiddlewareState();
        var testState = new ClientToolStateData()
            .WithRegisteredToolHarness(CreateTestToolHarness("TestToolHarness"));

        // Act
        var updated = container.WithClientTool(testState);

        // Assert
        Assert.NotSame(container, updated);
        Assert.NotNull(updated.ClientTool());
        Assert.Single(updated.ClientTool().RegisteredToolHarnesses);
    }

    [Fact]
    public void ImmutableUpdate_DoesNotModifyOriginal()
    {
        // Arrange
        var original = new ClientToolStateData();

        // Act
        var updated = original
            .WithRegisteredToolHarness(CreateTestToolHarness("ToolHarness1"))
            .WithExpandedToolHarness("ToolHarness1")
            .WithHiddenTool("Tool1");

        // Assert
        Assert.Empty(original.RegisteredToolHarnesses);
        Assert.Empty(original.ExpandedToolHarnesses);
        Assert.Empty(original.HiddenTools);

        Assert.Single(updated.RegisteredToolHarnesses);
        Assert.Single(updated.ExpandedToolHarnesses);
        Assert.Single(updated.HiddenTools);
    }

    // ============================================
    // Helper Methods
    // ============================================

    private static clientToolHarnessDefinition CreateTestToolHarness(
        string name,
        ClientToolDefinition[]? tools = null)
    {
        return new clientToolHarnessDefinition(
            Name: name,
            Description: $"Test ToolHarness {name}",
            Tools: tools ?? new[] { CreateTestTool($"{name}_Tool") }
        );
    }

    private static ClientToolDefinition CreateTestTool(string name)
    {
        return new ClientToolDefinition(
            Name: name,
            Description: $"Test tool {name}",
            ParametersSchema: System.Text.Json.JsonDocument.Parse("{}").RootElement
        );
    }
}
