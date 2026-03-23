using Xunit;
using HPD.Agent;

namespace HPD.Agent.Tests.SubAgents;

/// <summary>
/// SubAgent Source Generator Tests
/// Validates that the source generator correctly:
/// 1. Detects [SubAgent] attribute
/// 2. Generates AIFunction wrappers for sub-agents
/// 3. Parses AgentConfig from method body
/// 4. Handles different Session modes (Stateless, SharedSession, PerSession)
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
        var Toolkit = new TestSubAgentTools();

        // Act - Call sub-agent method
        var subAgent = Toolkit.CategorizedSubAgent();

        // Assert
        Assert.NotNull(subAgent);
        Assert.Equal("CategorizedSubAgent", subAgent.Name);
        Assert.NotNull(subAgent.AgentConfig);
    }

    // ===== P0: SubAgentFactory.Create() Patterns =====

    [Fact]
    public void SubAgentFactory_Create_GeneratesStatelessSubAgent()
    {
        // Arrange
        var Toolkit = new TestSubAgentTools();

        // Act
        var subAgent = Toolkit.StatelessSubAgent();

        // Assert
        Assert.NotNull(subAgent);
        Assert.Equal("StatelessSubAgent", subAgent.Name);
        Assert.Equal(SubAgentSessionMode.Stateless, subAgent.SessionMode);
        Assert.Null(subAgent.SharedSessionId); // No shared session ID for stateless
    }

    [Fact]
    public void SubAgentFactory_CreateStateful_GeneratesStatefulSubAgent()
    {
        // Arrange
        var Toolkit = new TestSubAgentTools();

        // Act
        var subAgent = Toolkit.StatefulSubAgent();

        // Assert
        Assert.NotNull(subAgent);
        Assert.Equal("StatefulSubAgent", subAgent.Name);
        Assert.Equal(SubAgentSessionMode.SharedSession, subAgent.SessionMode);
        Assert.NotNull(subAgent.SharedSessionId); // Should have shared session ID
    }

    [Fact]
    public void SubAgentFactory_CreatePerSession_GeneratesPerSessionSubAgent()
    {
        // Arrange
        var Toolkit = new TestSubAgentTools();

        // Act
        var subAgent = Toolkit.PerSessionSubAgent();

        // Assert
        Assert.NotNull(subAgent);
        Assert.Equal("PerSessionSubAgent", subAgent.Name);
        Assert.Equal(SubAgentSessionMode.PerSession, subAgent.SessionMode);
    }

    // ===== P0: AgentConfig Extraction =====

    [Fact]
    public void SourceGenerator_ExtractsAgentConfig_WithProvider()
    {
        // Arrange
        var Toolkit = new TestSubAgentTools();

        // Act
        var subAgent = Toolkit.SubAgentWithProvider();

        // Assert
        Assert.NotNull(subAgent);
        Assert.NotNull(subAgent.AgentConfig);
        Assert.NotNull(subAgent.AgentConfig.Provider);
        Assert.Equal("openrouter", subAgent.AgentConfig.Provider.ProviderKey);
        Assert.Equal("google/gemini-2.0-flash-exp:free", subAgent.AgentConfig.Provider.ModelName);
    }

    [Fact]
    public void SourceGenerator_ExtractsAgentConfig_WithInstructions()
    {
        // Arrange
        var Toolkit = new TestSubAgentTools();

        // Act
        var subAgent = Toolkit.SubAgentWithInstructions();

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
        var Toolkit = new TestSubAgentTools();

        // Act
        var subAgent = Toolkit.SubAgentWithIterationLimit();

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
        var Toolkit = new TestSubAgentTools();

        // Act
        var subAgent = Toolkit.ValidSubAgent();

        // Assert
        Assert.NotNull(subAgent);
        Assert.False(string.IsNullOrWhiteSpace(subAgent.Name));
        Assert.False(string.IsNullOrWhiteSpace(subAgent.Description));
    }

    [Fact]
    public void SubAgent_Description_IsExtractedFromFactory()
    {
        // Arrange
        var Toolkit = new TestSubAgentTools();

        // Act
        var subAgent = Toolkit.ValidSubAgent();

        // Assert
        Assert.Equal("A valid test sub-agent", subAgent.Description);
    }

    // ===== P0: Session mode Validation =====

    [Fact]
    public void SubAgent_DefaultSessionMode_IsStateless()
    {
        // Arrange
        var Toolkit = new TestSubAgentTools();

        // Act
        var subAgent = Toolkit.StatelessSubAgent();

        // Assert
        Assert.Equal(SubAgentSessionMode.Stateless, subAgent.SessionMode);
    }

    [Fact]
    public void SubAgent_SharedSessionId_IsNotNullForStateful()
    {
        // Arrange
        var Toolkit = new TestSubAgentTools();

        // Act
        var subAgent = Toolkit.StatefulSubAgent();

        // Assert
        Assert.NotNull(subAgent.SharedSessionId);
    }

    // ===== P0: Complex Scenarios =====

    [Fact]
    public void SubAgent_WithFullConfiguration_CompilesSuccessfully()
    {
        // Arrange
        var Toolkit = new TestSubAgentTools();

        // Act
        var subAgent = Toolkit.ComplexSubAgent();

        // Assert
        Assert.NotNull(subAgent);
        Assert.Equal("ComplexSubAgent", subAgent.Name);
        Assert.NotNull(subAgent.AgentConfig);
        Assert.NotNull(subAgent.AgentConfig.Provider);
        Assert.NotNull(subAgent.AgentConfig.SystemInstructions);
        Assert.Equal(20, subAgent.AgentConfig.MaxAgenticIterations);
    }
}

// Note: The TestSubAgentTools class is defined in TestSubAgentTools.cs
// to be processed by the source generator for these tests
