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
        Assert.Empty(state.RegisteredHarnesses);
        Assert.Empty(state.ExpandedHarneses);
        Assert.Empty(state.HiddenTools);
        Assert.Empty(state.Context);
        Assert.Null(state.State);
        Assert.Null(state.PendingAugmentation);
    }

    // ============================================
    // Harness Registration Tests
    // ============================================

    [Fact]
    public void WithRegisteredHarness_AddsHarness()
    {
        // Arrange
        var state = new ClientToolStateData();
        var Harness = CreateTestHarness("TestHarness");

        // Act
        var updated = state.WithRegisteredHarness(Harness);

        // Assert
        Assert.NotSame(state, updated);
        Assert.Single(updated.RegisteredHarnesses);
        Assert.True(updated.RegisteredHarnesses.ContainsKey("TestHarness"));
        Assert.Equal(Harness, updated.RegisteredHarnesses["TestHarness"]);
    }

    [Fact]
    public void WithRegisteredHarness_MultipleCalls_AddsAllHarneses()
    {
        // Arrange
        var state = new ClientToolStateData();
        var Harness1 = CreateTestHarness("Harness1");
        var Harness2 = CreateTestHarness("Harness2");

        // Act
        var updated = state
            .WithRegisteredHarness(Harness1)
            .WithRegisteredHarness(Harness2);

        // Assert
        Assert.Equal(2, updated.RegisteredHarnesses.Count);
        Assert.True(updated.RegisteredHarnesses.ContainsKey("Harness1"));
        Assert.True(updated.RegisteredHarnesses.ContainsKey("Harness2"));
    }

    [Fact]
    public void WithRegisteredHarness_SameName_ReplacesHarness()
    {
        // Arrange
        var state = new ClientToolStateData();
        var Harness1 = CreateTestHarness("TestHarness", tools: new[] { CreateTestTool("Tool1") });
        var Harness2 = CreateTestHarness("TestHarness", tools: new[] { CreateTestTool("Tool2") });

        // Act
        var updated = state
            .WithRegisteredHarness(Harness1)
            .WithRegisteredHarness(Harness2);

        // Assert
        Assert.Single(updated.RegisteredHarnesses);
        Assert.Equal("Tool2", updated.RegisteredHarnesses["TestHarness"].Tools[0].Name);
    }

    [Fact]
    public void WithoutRegisteredHarness_RemovesHarness()
    {
        // Arrange
        var state = new ClientToolStateData()
            .WithRegisteredHarness(CreateTestHarness("Harness1"))
            .WithRegisteredHarness(CreateTestHarness("Harness2"));

        // Act
        var updated = state.WithoutRegisteredHarness("Harness1");

        // Assert
        Assert.Single(updated.RegisteredHarnesses);
        Assert.False(updated.RegisteredHarnesses.ContainsKey("Harness1"));
        Assert.True(updated.RegisteredHarnesses.ContainsKey("Harness2"));
    }

    [Fact]
    public void WithoutRegisteredHarness_NonExistent_ReturnsUnchanged()
    {
        // Arrange
        var state = new ClientToolStateData()
            .WithRegisteredHarness(CreateTestHarness("Harness1"));

        // Act
        var updated = state.WithoutRegisteredHarness("NonExistent");

        // Assert
        Assert.Single(updated.RegisteredHarnesses);
    }

    // ============================================
    // Expanded Harneses Tests
    // ============================================

    [Fact]
    public void WithExpandedHarness_AddsToSet()
    {
        // Arrange
        var state = new ClientToolStateData();

        // Act
        var updated = state.WithExpandedHarness("Harness1");

        // Assert
        Assert.NotSame(state, updated);
        Assert.Single(updated.ExpandedHarneses);
        Assert.Contains("Harness1", updated.ExpandedHarneses);
    }

    [Fact]
    public void WithExpandedHarness_Duplicate_NoEffect()
    {
        // Arrange
        var state = new ClientToolStateData()
            .WithExpandedHarness("Harness1");

        // Act
        var updated = state.WithExpandedHarness("Harness1");

        // Assert
        Assert.Single(updated.ExpandedHarneses);
    }

    [Fact]
    public void WithCollapsedHarness_RemovesFromSet()
    {
        // Arrange
        var state = new ClientToolStateData()
            .WithExpandedHarness("Harness1")
            .WithExpandedHarness("Harness2");

        // Act
        var updated = state.WithCollapsedHarness("Harness1");

        // Assert
        Assert.Single(updated.ExpandedHarneses);
        Assert.DoesNotContain("Harness1", updated.ExpandedHarneses);
        Assert.Contains("Harness2", updated.ExpandedHarneses);
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
            ExpandHarneses = new HashSet<string> { "Harness1" }
        };

        // Act
        var updated = state.WithPendingAugmentation(augmentation);

        // Assert
        Assert.NotSame(state, updated);
        Assert.NotNull(updated.PendingAugmentation);
        Assert.Contains("Harness1", updated.PendingAugmentation.ExpandHarneses!);
    }

    [Fact]
    public void ClearPendingAugmentation_RemovesAugmentation()
    {
        // Arrange
        var state = new ClientToolStateData()
            .WithPendingAugmentation(new ClientToolAugmentation { ExpandHarneses = new HashSet<string> { "Harness1" } });

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
            .WithRegisteredHarness(CreateTestHarness("TestHarness"));

        // Act
        var updated = container.WithClientTool(testState);

        // Assert
        Assert.NotSame(container, updated);
        Assert.NotNull(updated.ClientTool());
        Assert.Single(updated.ClientTool().RegisteredHarnesses);
    }

    [Fact]
    public void ImmutableUpdate_DoesNotModifyOriginal()
    {
        // Arrange
        var original = new ClientToolStateData();

        // Act
        var updated = original
            .WithRegisteredHarness(CreateTestHarness("Harness1"))
            .WithExpandedHarness("Harness1")
            .WithHiddenTool("Tool1");

        // Assert
        Assert.Empty(original.RegisteredHarnesses);
        Assert.Empty(original.ExpandedHarneses);
        Assert.Empty(original.HiddenTools);

        Assert.Single(updated.RegisteredHarnesses);
        Assert.Single(updated.ExpandedHarneses);
        Assert.Single(updated.HiddenTools);
    }

    // ============================================
    // Helper Methods
    // ============================================

    private static clientHarnessDefinition CreateTestHarness(
        string name,
        ClientToolDefinition[]? tools = null)
    {
        return new clientHarnessDefinition(
            Name: name,
            Description: $"Test Harness {name}",
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
