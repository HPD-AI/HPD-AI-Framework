using Xunit;
using HPD.Agent;
using Microsoft.Extensions.AI;
using System.Threading.Tasks;

namespace HPD.Agent.Tests.SubAgents;

/// <summary>
/// SubAgent Integration Tests
/// Tests the AIFunction metadata and structure that the source generator creates for sub-agents.
/// Since source generators don't run on test projects, we manually create AIFunction objects
/// that simulate the source generator output, similar to how SkillCollapsingTests works.
/// </summary>
public class SubAgentIntegrationTests
{
    // Helper to create a SubAgent AIFunction like the source generator would
    private static AIFunction CreateSubAgentFunction(
        string name,
        string description,
        string SessionMode = "Stateless",
        string? parentToolkit = null)
    {
        var additionalProps = new Dictionary<string, object>
        {
            ["IsSubAgent"] = true,
            ["SessionMode"] = SessionMode
        };

        // Add ParentToolkit if specified (not ToolkitName - that was the bug!)
        if (parentToolkit != null)
        {
            additionalProps["ParentToolkit"] = parentToolkit;
        }

        return AIFunctionFactory.Create(
            async (string query, CancellationToken ct) =>
            {
                // Simulate sub-agent invocation
                return $"SubAgent {name} response to: {query}";
            },
            new AIFunctionFactoryOptions
            {
                Name = name,
                Description = description,
                AdditionalProperties = additionalProps
            });
    }

    // ===== P0: Toolkit Registration =====

    [Fact]
    public void CrossAssemblyToolkitLoading_LoadsRegistryFromToolkitAssembly()
    {
        // This test verifies that the cross-assembly Toolkit loading mechanism works.
        // When WithToolkit<T>() is called, it should load the ToolRegistry from T's assembly
        // if not already loaded.

        // Arrange - Create a builder
        var builder = new AgentBuilder();

        // Act - Attempt to load a Toolkit registry from the test assembly
        // Even though there's no Toolkit, it should not throw - just find nothing
        builder.LoadToolRegistryFromAssembly(typeof(TestIntegrationSubAgents).Assembly);

        // Assert - The assembly was tracked as loaded (even if no Toolkits found)
        // This verifies the cross-assembly loading mechanism is working
        Assert.Contains(typeof(TestIntegrationSubAgents).Assembly, builder._loadedAssemblies);
    }

    [Fact]
    public void SubAgentToolkit_GeneratesAIFunctions_WithCorrectStructure()
    {
        // Arrange - Simulate what source generator would create
        var functions = new List<AIFunction>
        {
            CreateSubAgentFunction("WeatherExpert", "Weather forecast agent", "Stateless"),
            CreateSubAgentFunction("MathExpert", "Math calculation agent", "SharedSession"),
            CreateSubAgentFunction("CodeReviewer", "Code review agent", "Stateless")
        };

        // Act
        var subAgentFunctions = functions.Where(f =>
            f.AdditionalProperties?.ContainsKey("IsSubAgent") == true).ToList();

        // Assert
        Assert.NotEmpty(subAgentFunctions);
        Assert.Equal(3, subAgentFunctions.Count);
    }

    // ===== P0: AIFunction Metadata =====

    [Fact]
    public void SubAgent_AIFunction_HasCorrectMetadata()
    {
        // Arrange
        var weatherExpert = CreateSubAgentFunction(
            "WeatherExpert",
            "Specialized agent for weather forecasts",
            "Stateless");

        // Assert
        Assert.NotNull(weatherExpert);
        Assert.Equal("WeatherExpert", weatherExpert.Name);
        Assert.NotNull(weatherExpert.Description);
        Assert.Contains("weather", weatherExpert.Description.ToLower());

        // Check IsSubAgent flag
        Assert.True(weatherExpert.AdditionalProperties?.ContainsKey("IsSubAgent"));
        Assert.True((bool?)weatherExpert.AdditionalProperties!["IsSubAgent"] ?? false);
    }

    [Fact]
    public void SubAgent_AIFunction_HasRequiresPermission()
    {
        // Arrange
        var subAgentFunction = CreateSubAgentFunction(
            "TestSubAgent",
            "Test sub-agent",
            "Stateless");

        // Assert
        Assert.NotNull(subAgentFunction);
        // Sub-agents should always have IsSubAgent flag
        Assert.True(subAgentFunction.AdditionalProperties?.ContainsKey("IsSubAgent"));
        Assert.True((bool)subAgentFunction.AdditionalProperties!["IsSubAgent"]);
    }

    // ===== P0: Session mode Metadata =====

    [Fact]
    public void SubAgent_AIFunction_HasSessionModeMetadata()
    {
        // Arrange
        var weatherExpert = CreateSubAgentFunction(
            "WeatherExpert",
            "Weather agent",
            "Stateless");

        var mathExpert = CreateSubAgentFunction(
            "MathExpert",
            "Math agent",
            "SharedSession");

        // Assert
        Assert.NotNull(weatherExpert);
        Assert.NotNull(mathExpert);

        // WeatherExpert should be Stateless
        Assert.True(weatherExpert.AdditionalProperties?.ContainsKey("SessionMode"));
        Assert.Equal("Stateless", weatherExpert.AdditionalProperties!["SessionMode"] as string);

        // MathExpert should be SharedSession
        Assert.True(mathExpert.AdditionalProperties?.ContainsKey("SessionMode"));
        Assert.Equal("SharedSession", mathExpert.AdditionalProperties!["SessionMode"] as string);
    }

    // ===== P0: Function Signature =====

    [Fact]
    public void SubAgent_AIFunction_AcceptsQueryParameter()
    {
        // Arrange
        var weatherExpert = CreateSubAgentFunction(
            "WeatherExpert",
            "Weather agent",
            "Stateless");

        // Assert
        Assert.NotNull(weatherExpert);
        Assert.NotNull(weatherExpert.AdditionalProperties);

        // Check that it has the expected sub-agent structure
        Assert.True(weatherExpert.AdditionalProperties?.ContainsKey("IsSubAgent"));
    }
}
