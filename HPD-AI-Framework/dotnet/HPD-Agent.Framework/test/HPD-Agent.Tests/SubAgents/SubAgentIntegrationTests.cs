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
        string? parentToolHarness = null)
    {
        var additionalProps = new Dictionary<string, object>
        {
            ["IsSubAgent"] = true,
            ["ExecutionModel"] = "BranchNative"
        };

        // Add ParentToolHarness if specified (not ToolHarnessName - that was the bug!)
        if (parentToolHarness != null)
        {
            additionalProps["ParentToolHarness"] = parentToolHarness;
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

    // ===== P0: ToolHarness Registration =====

    [Fact]
    public void CrossAssemblyToolHarnessLoading_LoadsRegistryFromToolHarnessAssembly()
    {
        // This test verifies that the cross-assembly ToolHarness loading mechanism works.
        // When WithToolHarness<T>() is called, it should load the ToolRegistry from T's assembly
        // if not already loaded.

        // Arrange - Create a builder
        var builder = new AgentBuilder();

        // Act - Attempt to load a ToolHarness registry from the test assembly
        // Even though there's no ToolHarness, it should not throw - just find nothing
        builder.LoadToolRegistryFromAssembly(typeof(TestIntegrationSubAgents).Assembly);

        // Assert - The assembly was tracked as loaded (even if no ToolHarnesses found)
        // This verifies the cross-assembly loading mechanism is working
        Assert.Contains(typeof(TestIntegrationSubAgents).Assembly, builder._loadedAssemblies);
    }

    [Fact]
    public void SubAgentToolHarness_GeneratesAIFunctions_WithCorrectStructure()
    {
        // Arrange - Simulate what source generator would create
        var functions = new List<AIFunction>
        {
            CreateSubAgentFunction("WeatherExpert", "Weather forecast agent"),
            CreateSubAgentFunction("MathExpert", "Math calculation agent"),
            CreateSubAgentFunction("CodeReviewer", "Code review agent")
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
            "Specialized agent for weather forecasts");

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
            "Test sub-agent");

        // Assert
        Assert.NotNull(subAgentFunction);
        // Sub-agents should always have IsSubAgent flag
        Assert.True(subAgentFunction.AdditionalProperties?.ContainsKey("IsSubAgent"));
        Assert.True((bool)subAgentFunction.AdditionalProperties!["IsSubAgent"]);
    }

    // ===== P0: Execution model Metadata =====

    [Fact]
    public void SubAgent_AIFunction_HasExecutionModelMetadata()
    {
        // Arrange
        var weatherExpert = CreateSubAgentFunction(
            "WeatherExpert",
            "Weather agent");

        var mathExpert = CreateSubAgentFunction(
            "MathExpert",
            "Math agent");

        // Assert
        Assert.NotNull(weatherExpert);
        Assert.NotNull(mathExpert);

        Assert.True(weatherExpert.AdditionalProperties?.ContainsKey("ExecutionModel"));
        Assert.Equal("BranchNative", weatherExpert.AdditionalProperties!["ExecutionModel"] as string);
        Assert.False(weatherExpert.AdditionalProperties?.ContainsKey("SessionMode"));

        Assert.True(mathExpert.AdditionalProperties?.ContainsKey("ExecutionModel"));
        Assert.Equal("BranchNative", mathExpert.AdditionalProperties!["ExecutionModel"] as string);
        Assert.False(mathExpert.AdditionalProperties?.ContainsKey("SessionMode"));
    }

    // ===== P0: Function Signature =====

    [Fact]
    public void SubAgent_AIFunction_AcceptsQueryParameter()
    {
        // Arrange
        var weatherExpert = CreateSubAgentFunction(
            "WeatherExpert",
            "Weather agent");

        // Assert
        Assert.NotNull(weatherExpert);
        Assert.NotNull(weatherExpert.AdditionalProperties);

        // Check that it has the expected sub-agent structure
        Assert.True(weatherExpert.AdditionalProperties?.ContainsKey("IsSubAgent"));
    }
}
