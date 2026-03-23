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
        Assert.Empty(state.RegisteredToolKits);
        Assert.Empty(state.ExpandedToolkits);
        Assert.Empty(state.HiddenTools);
        Assert.Empty(state.Context);
        Assert.Null(state.State);
        Assert.Null(state.PendingAugmentation);
    }

    // ============================================
    // Toolkit Registration Tests
    // ============================================

    [Fact]
    public void WithRegisteredToolkit_AddsToolkit()
    {
        // Arrange
        var state = new ClientToolStateData();
        var Toolkit = CreateTestToolkit("TestToolkit");

        // Act
        var updated = state.WithRegisteredToolkit(Toolkit);

        // Assert
        Assert.NotSame(state, updated);
        Assert.Single(updated.RegisteredToolKits);
        Assert.True(updated.RegisteredToolKits.ContainsKey("TestToolkit"));
        Assert.Equal(Toolkit, updated.RegisteredToolKits["TestToolkit"]);
    }

    [Fact]
    public void WithRegisteredToolkit_MultipleCalls_AddsAllToolkits()
    {
        // Arrange
        var state = new ClientToolStateData();
        var Toolkit1 = CreateTestToolkit("Toolkit1");
        var Toolkit2 = CreateTestToolkit("Toolkit2");

        // Act
        var updated = state
            .WithRegisteredToolkit(Toolkit1)
            .WithRegisteredToolkit(Toolkit2);

        // Assert
        Assert.Equal(2, updated.RegisteredToolKits.Count);
        Assert.True(updated.RegisteredToolKits.ContainsKey("Toolkit1"));
        Assert.True(updated.RegisteredToolKits.ContainsKey("Toolkit2"));
    }

    [Fact]
    public void WithRegisteredToolkit_SameName_ReplacesToolkit()
    {
        // Arrange
        var state = new ClientToolStateData();
        var Toolkit1 = CreateTestToolkit("TestToolkit", tools: new[] { CreateTestTool("Tool1") });
        var Toolkit2 = CreateTestToolkit("TestToolkit", tools: new[] { CreateTestTool("Tool2") });

        // Act
        var updated = state
            .WithRegisteredToolkit(Toolkit1)
            .WithRegisteredToolkit(Toolkit2);

        // Assert
        Assert.Single(updated.RegisteredToolKits);
        Assert.Equal("Tool2", updated.RegisteredToolKits["TestToolkit"].Tools[0].Name);
    }

    [Fact]
    public void WithoutRegisteredToolkit_RemovesToolkit()
    {
        // Arrange
        var state = new ClientToolStateData()
            .WithRegisteredToolkit(CreateTestToolkit("Toolkit1"))
            .WithRegisteredToolkit(CreateTestToolkit("Toolkit2"));

        // Act
        var updated = state.WithoutRegisteredToolkit("Toolkit1");

        // Assert
        Assert.Single(updated.RegisteredToolKits);
        Assert.False(updated.RegisteredToolKits.ContainsKey("Toolkit1"));
        Assert.True(updated.RegisteredToolKits.ContainsKey("Toolkit2"));
    }

    [Fact]
    public void WithoutRegisteredToolkit_NonExistent_ReturnsUnchanged()
    {
        // Arrange
        var state = new ClientToolStateData()
            .WithRegisteredToolkit(CreateTestToolkit("Toolkit1"));

        // Act
        var updated = state.WithoutRegisteredToolkit("NonExistent");

        // Assert
        Assert.Single(updated.RegisteredToolKits);
    }

    // ============================================
    // Expanded Toolkits Tests
    // ============================================

    [Fact]
    public void WithExpandedToolkit_AddsToSet()
    {
        // Arrange
        var state = new ClientToolStateData();

        // Act
        var updated = state.WithExpandedToolkit("Toolkit1");

        // Assert
        Assert.NotSame(state, updated);
        Assert.Single(updated.ExpandedToolkits);
        Assert.Contains("Toolkit1", updated.ExpandedToolkits);
    }

    [Fact]
    public void WithExpandedToolkit_Duplicate_NoEffect()
    {
        // Arrange
        var state = new ClientToolStateData()
            .WithExpandedToolkit("Toolkit1");

        // Act
        var updated = state.WithExpandedToolkit("Toolkit1");

        // Assert
        Assert.Single(updated.ExpandedToolkits);
    }

    [Fact]
    public void WithCollapsedToolkit_RemovesFromSet()
    {
        // Arrange
        var state = new ClientToolStateData()
            .WithExpandedToolkit("Toolkit1")
            .WithExpandedToolkit("Toolkit2");

        // Act
        var updated = state.WithCollapsedToolkit("Toolkit1");

        // Assert
        Assert.Single(updated.ExpandedToolkits);
        Assert.DoesNotContain("Toolkit1", updated.ExpandedToolkits);
        Assert.Contains("Toolkit2", updated.ExpandedToolkits);
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
            ExpandToolkits = new HashSet<string> { "Toolkit1" }
        };

        // Act
        var updated = state.WithPendingAugmentation(augmentation);

        // Assert
        Assert.NotSame(state, updated);
        Assert.NotNull(updated.PendingAugmentation);
        Assert.Contains("Toolkit1", updated.PendingAugmentation.ExpandToolkits!);
    }

    [Fact]
    public void ClearPendingAugmentation_RemovesAugmentation()
    {
        // Arrange
        var state = new ClientToolStateData()
            .WithPendingAugmentation(new ClientToolAugmentation { ExpandToolkits = new HashSet<string> { "Toolkit1" } });

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
            .WithRegisteredToolkit(CreateTestToolkit("TestToolkit"));

        // Act
        var updated = container.WithClientTool(testState);

        // Assert
        Assert.NotSame(container, updated);
        Assert.NotNull(updated.ClientTool());
        Assert.Single(updated.ClientTool().RegisteredToolKits);
    }

    [Fact]
    public void ImmutableUpdate_DoesNotModifyOriginal()
    {
        // Arrange
        var original = new ClientToolStateData();

        // Act
        var updated = original
            .WithRegisteredToolkit(CreateTestToolkit("Toolkit1"))
            .WithExpandedToolkit("Toolkit1")
            .WithHiddenTool("Tool1");

        // Assert
        Assert.Empty(original.RegisteredToolKits);
        Assert.Empty(original.ExpandedToolkits);
        Assert.Empty(original.HiddenTools);

        Assert.Single(updated.RegisteredToolKits);
        Assert.Single(updated.ExpandedToolkits);
        Assert.Single(updated.HiddenTools);
    }

    // ============================================
    // Helper Methods
    // ============================================

    private static clientToolKitDefinition CreateTestToolkit(
        string name,
        ClientToolDefinition[]? tools = null)
    {
        return new clientToolKitDefinition(
            Name: name,
            Description: $"Test Toolkit {name}",
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
