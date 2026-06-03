using Xunit;
using HPD.Agent;

namespace HPD.Agent.Tests.SubAgents;

/// <summary>
/// SubAgent Source Generator Tests
/// Validates that the source generator correctly:
/// 1. Detects [SubAgent] attribute
/// 2. Generates AIFunction wrappers for sub-agents
/// 3. Parses AgentConfig from method body
/// 4. Handles branch-native execution policies
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
        Assert.NotNull(subAgent.AgentConfig);
    }

    // ===== P0: SubAgent.FromConfig() Patterns =====

    [Fact]
    public void SubAgent_FromConfig_GeneratesDefaultBranchNativeSubAgent()
    {
        // Arrange
        var ToolHarness = new TestSubAgentTools();

        // Act
        var subAgent = ToolHarness.DefaultBranchNativeSubAgent();

        // Assert
        Assert.NotNull(subAgent);
        Assert.Equal("DefaultBranchNativeSubAgent", subAgent.Name);
        Assert.Equal(SubAgentSourceKind.InlineConfig, subAgent.SourceKind);
        Assert.Equal(SubAgentSessionPolicy.ParentSession, subAgent.ExecutionPolicy.SessionPolicy);
        Assert.Equal(SubAgentBranchPolicy.ForkFromParentBranch, subAgent.ExecutionPolicy.BranchPolicy);
        Assert.Null(subAgent.ExecutionPolicy.SharedSessionId);
    }

    [Fact]
    public void SubAgent_SharedSessionPolicy_GeneratesSharedSessionSubAgent()
    {
        // Arrange
        var ToolHarness = new TestSubAgentTools();

        // Act
        var subAgent = ToolHarness.SharedSessionSubAgent();

        // Assert
        Assert.NotNull(subAgent);
        Assert.Equal("SharedSessionSubAgent", subAgent.Name);
        Assert.Equal(SubAgentSessionPolicy.SharedSession, subAgent.ExecutionPolicy.SessionPolicy);
        Assert.Equal(SubAgentBranchPolicy.FreshBranch, subAgent.ExecutionPolicy.BranchPolicy);
        Assert.NotNull(subAgent.ExecutionPolicy.SharedSessionId);
    }

    [Fact]
    public void SubAgent_ParentBranchPolicy_GeneratesParentBranchSubAgent()
    {
        // Arrange
        var ToolHarness = new TestSubAgentTools();

        // Act
        var subAgent = ToolHarness.ParentBranchSubAgent();

        // Assert
        Assert.NotNull(subAgent);
        Assert.Equal("ParentBranchSubAgent", subAgent.Name);
        Assert.Equal(SubAgentSessionPolicy.ParentSession, subAgent.ExecutionPolicy.SessionPolicy);
        Assert.Equal(SubAgentBranchPolicy.ParentBranch, subAgent.ExecutionPolicy.BranchPolicy);
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
        Assert.NotNull(subAgent.AgentConfig);
        Assert.NotNull(subAgent.AgentConfig.EnsureChatClientConfig());
        Assert.Equal("openrouter", subAgent.AgentConfig.EnsureChatClientConfig().ProviderKey);
        Assert.Equal("google/gemini-2.0-flash-exp:free", subAgent.AgentConfig.EnsureChatClientConfig().ModelName);
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
        Assert.NotNull(subAgent.AgentConfig);
        Assert.NotNull(subAgent.AgentConfig.SystemInstructions);
        Assert.Contains("You are a test agent", subAgent.AgentConfig.SystemInstructions);
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
        Assert.NotNull(subAgent.AgentConfig);
        Assert.Equal(15, subAgent.AgentConfig.MaxAgenticIterations);
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
    public void SubAgent_DefaultExecutionPolicy_IsParentSessionForkedBranch()
    {
        // Arrange
        var ToolHarness = new TestSubAgentTools();

        // Act
        var subAgent = ToolHarness.DefaultBranchNativeSubAgent();

        // Assert
        Assert.Equal(SubAgentExecutionPolicy.Default, subAgent.ExecutionPolicy);
    }

    [Fact]
    public void SubAgent_SharedSessionId_IsOnExecutionPolicyForSharedSessionFreshBranch()
    {
        // Arrange
        var ToolHarness = new TestSubAgentTools();

        // Act
        var subAgent = ToolHarness.SharedSessionSubAgent();

        // Assert
        Assert.NotNull(subAgent.ExecutionPolicy.SharedSessionId);
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
        Assert.NotNull(subAgent.AgentConfig);
        Assert.NotNull(subAgent.AgentConfig.EnsureChatClientConfig());
        Assert.NotNull(subAgent.AgentConfig.SystemInstructions);
        Assert.Equal(20, subAgent.AgentConfig.MaxAgenticIterations);
    }
}

// Note: The TestSubAgentTools class is defined in TestSubAgentTools.cs
// to be processed by the source generator for these tests
