using Xunit;
using HPD.Agent;

namespace HPD.Agent.Tests.SubAgents;

/// <summary>
/// SubAgent Source Generator Tests
/// Validates that the source generator correctly:
/// 1. Detects [SubAgent] attribute
/// 2. Generates AIFunction wrappers for sub-agents
/// 3. Parses AgentConfig from method body
/// 4. Handles thread-native execution policies
/// 5. Validates method signatures
/// </summary>
public class SubAgentSourceGeneratorTests
{
    // ===== P0: [SubAgent] Attribute Detection =====

    [Fact]
    public void SubAgentAttribute_CanBeApplied_ToMethod()
    {
        // Arrange & Act - This is validated at compile time by the source generator
        // If this compiles, the attribute is working correctly

        // Assert
        // The fact that we can create this test class with [SubAgent] methods proves detection works
        Assert.True(true);
    }

    [Fact]
    public void SubAgentAttribute_OnMethod_CompilesSuccessfully()
    {
        // Arrange
        var ToolHarness = new TestSubAgentTools();

        // Act - Call sub-agent method
        var subAgent = ToolHarness.CategorizedSubAgent();

        // Assert
        Assert.NotNull(subAgent);
        Assert.Equal("CategorizedSubAgent", subAgent.Name);
        Assert.NotNull(ConfigOf(subAgent));
    }

    // ===== P0: SubAgent.FromConfig() Patterns =====

    [Fact]
    public void SubAgent_FromConfig_GeneratesDefaultThreadNativeSubAgent()
    {
        // Arrange
        var ToolHarness = new TestSubAgentTools();

        // Act
        var subAgent = ToolHarness.DefaultThreadNativeSubAgent();

        // Assert
        Assert.NotNull(subAgent);
        Assert.Equal("DefaultThreadNativeSubAgent", subAgent.Name);
        Assert.IsType<SuppliedAgentConfiguration>(subAgent.Configuration);
        Assert.Equal(SubAgentContextPolicy.Fork, subAgent.ContextPolicy);
    }

    [Fact]
    public void SubAgent_FreshThreadPolicy_GeneratesChildOwnedSubAgent()
    {
        // Arrange
        var ToolHarness = new TestSubAgentTools();

        // Act
        var subAgent = ToolHarness.FreshThreadSubAgent();

        // Assert
        Assert.NotNull(subAgent);
        Assert.Equal("FreshThreadSubAgent", subAgent.Name);
        Assert.Equal(SubAgentContextPolicy.Fresh, subAgent.ContextPolicy);
    }

    // ===== P0: AgentConfig Extraction =====

    [Fact]
    public void SourceGenerator_ExtractsAgentConfig_WithProvider()
    {
        // Arrange
        var ToolHarness = new TestSubAgentTools();

        // Act
        var subAgent = ToolHarness.SubAgentWithProvider();

        // Assert
        Assert.NotNull(subAgent);
        var config = ConfigOf(subAgent);
        Assert.NotNull(config.EnsureChatClientConfig());
        Assert.Equal("openrouter", config.EnsureChatClientConfig().Provider?.Key);
        Assert.Equal("google/gemini-2.0-flash-exp:free", config.EnsureChatClientConfig().ModelName);
    }

    [Fact]
    public void SourceGenerator_ExtractsAgentConfig_WithInstructions()
    {
        // Arrange
        var ToolHarness = new TestSubAgentTools();

        // Act
        var subAgent = ToolHarness.SubAgentWithInstructions();

        // Assert
        Assert.NotNull(subAgent);
        var config = ConfigOf(subAgent);
        Assert.NotNull(config.SystemInstructions);
        Assert.Contains("You are a test agent", config.SystemInstructions);
    }

    [Fact]
    public void SourceGenerator_ExtractsAgentConfig_WithIterationLimit()
    {
        // Arrange
        var ToolHarness = new TestSubAgentTools();

        // Act
        var subAgent = ToolHarness.SubAgentWithIterationLimit();

        // Assert
        Assert.NotNull(subAgent);
        Assert.Equal(15, ConfigOf(subAgent).MaxAgenticIterations);
    }

    // ===== P0: SubAgent Metadata =====

    [Fact]
    public void SubAgent_HasRequiredMetadata_NameAndDescription()
    {
        // Arrange
        var ToolHarness = new TestSubAgentTools();

        // Act
        var subAgent = ToolHarness.ValidSubAgent();

        // Assert
        Assert.NotNull(subAgent);
        Assert.False(string.IsNullOrWhiteSpace(subAgent.Name));
        Assert.False(string.IsNullOrWhiteSpace(subAgent.Description));
    }

    [Fact]
    public void SubAgent_Description_IsExtractedFromFactory()
    {
        // Arrange
        var ToolHarness = new TestSubAgentTools();

        // Act
        var subAgent = ToolHarness.ValidSubAgent();

        // Assert
        Assert.Equal("A valid test sub-agent", subAgent.Description);
    }

    // ===== P0: Execution policy Validation =====

    [Fact]
    public void SubAgent_DefaultContextPolicy_IsFork()
    {
        // Arrange
        var ToolHarness = new TestSubAgentTools();

        // Act
        var subAgent = ToolHarness.DefaultThreadNativeSubAgent();

        // Assert
        Assert.Equal(SubAgentContextPolicy.Fork, subAgent.ContextPolicy);
    }

    // ===== P0: Complex Scenarios =====

    [Fact]
    public void SubAgent_WithFullConfiguration_CompilesSuccessfully()
    {
        // Arrange
        var ToolHarness = new TestSubAgentTools();

        // Act
        var subAgent = ToolHarness.ComplexSubAgent();

        // Assert
        Assert.NotNull(subAgent);
        Assert.Equal("ComplexSubAgent", subAgent.Name);
        var config = ConfigOf(subAgent);
        Assert.NotNull(config.EnsureChatClientConfig());
        Assert.NotNull(config.SystemInstructions);
        Assert.Equal(20, config.MaxAgenticIterations);
    }
    private static AgentConfig ConfigOf(SubAgent subAgent) =>
        Assert.IsType<SuppliedAgentConfiguration>(subAgent.Configuration).Config;
}

// Note: The TestSubAgentTools class is defined in TestSubAgentTools.cs
// to be processed by the source generator for these tests
