using Xunit;

namespace HPD.Agent.Tests.Skills;

/// <summary>
/// Tests for Phase 1: Foundation Classes API
/// Validates Skill, SkillAttribute, SkillFactory, and SkillOptions
/// </summary>
public class Phase1ApiTests
{
    // Mock ToolHarnesses for testing
    private static class MockFileSystemTools
    {
        public static string ReadFile(string path) => $"Reading {path}";
        public static void WriteFile(string path, string content) { }
    }

    private static class MockDebugToolHarness
    {
        public static string GetStackTrace() => "Stack trace...";
    }

    [Fact]
    public void SkillFactory_Create_WithoutOptions_CreatesSkill()
    {
        // Arrange & Act
        var skill = SkillFactory.Create(
            name: "TestSkill",
            description: "Test description",
            functionResult: "Skill activated",
            systemPrompt: "Test instructions",
            "MockFileSystemTools.ReadFile"
        );

        // Assert
        Assert.Equal("TestSkill", skill.Name);
        Assert.Equal("Test description", skill.Description);
        Assert.Equal("Skill activated", skill.FunctionResult);
        Assert.Equal("Test instructions", skill.SystemPrompt);
        Assert.Single(skill.References);
        Assert.NotNull(skill.Options);
    }

    [Fact]
    public void SkillFactory_Create_WithOptions_CreatesSkill()
    {
        // Arrange
        var options = new SkillOptions();

        // Act
        var skill = SkillFactory.Create(
            name: "TestSkill",
            description: "Test description",
            functionResult: "Skill activated",
            systemPrompt: "Test instructions",
            options: options,
            "MockFileSystemTools.ReadFile",
            "MockDebugToolHarness.GetStackTrace"
        );

        // Assert
        Assert.Equal("TestSkill", skill.Name);
        Assert.Equal(2, skill.References.Length);
        Assert.Same(options, skill.Options);
    }

    [Fact]
    public void SkillFactory_Create_EmptyName_ThrowsArgumentException()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() =>
            SkillFactory.Create("", "Description", "FunctionResult", "SystemPrompt"));
    }

    [Fact]
    public void SkillFactory_Create_EmptyDescription_ThrowsArgumentException()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() =>
            SkillFactory.Create("Name", "", "FunctionResult", "SystemPrompt"));
    }

    [Fact]
    public void SkillFactory_Create_NoBothInstructions_ThrowsArgumentException()
    {
        // Act & Assert - At least one of FunctionResult or SystemPrompt must be provided
        Assert.Throws<ArgumentException>(() =>
            SkillFactory.Create("Name", "Description", null, null));
    }

    [Fact]
    public void SkillAttribute_CanBeAppliedToMethod()
    {
        // This test just verifies the attribute compiles and can be used
        // The actual method will be tested by the source generator

        [Skill]
        Skill TestMethod()
        {
            return SkillFactory.Create("Test", "Test", "FunctionResult", "SystemPrompt");
        }

        var skill = TestMethod();
        Assert.NotNull(skill);
    }

    [Fact]
    public void SkillAttribute_OnMethod_CompilesSuccessfully()
    {
        [Skill]
        Skill TestMethod()
        {
            return SkillFactory.Create("Test", "Test", "FunctionResult", "SystemPrompt");
        }

        var skill = TestMethod();
        Assert.NotNull(skill);
    }

    [Fact]
    public void Skill_InternalProperties_CanBeSet()
    {
        // Arrange
        var skill = SkillFactory.Create("Test", "Test", "FunctionResult", "SystemPrompt");

        // Act
        skill.ResolvedFunctionReferences = new[] { "ToolHarness1.Func1", "ToolHarness2.Func2" };
        skill.ResolvedToolHarnessTypes = new[] { "ToolHarness1", "ToolHarness2" };

        // Assert
        Assert.Equal(2, skill.ResolvedFunctionReferences.Length);
        Assert.Equal(2, skill.ResolvedToolHarnessTypes.Length);
    }
}
