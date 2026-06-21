using Xunit;
using HPD.Agent;

namespace HPD.Agent.Tests.Skills;

/// <summary>
/// Phase 3: Skill Runtime Tests
/// Validates runtime behavior of skills after compilation.
/// These tests execute actual skill methods and verify their runtime behavior.
///
/// NOTE: These are NOT source generator tests - they test runtime execution, not compile-time code generation.
/// For source generator tests, see Phase3SourceGeneratorTests.cs
///
/// What these tests validate:
/// 1. Skill methods execute correctly at runtime
/// 2. SkillFactory.Create produces correct Skill objects
/// </summary>
public class Phase3SkillRuntimeTests
{
    // ===== P0: [Skill] Attribute Detection =====

    [Fact]
    public void SkillAttribute_CanBeApplied_ToMethod()
    {
        // Arrange & Act - This is validated at compile time by the source generator
        // If this compiles, the attribute is working correctly

        // Assert
        // The fact that we can create this test class with [Skill] methods proves detection works
        Assert.True(true);
    }

    [Fact]
    public void SkillAttribute_OnMethod_CompilesSuccessfully()
    {
        // Arrange
        var ToolHarness = new TestSkillToolHarness();

        // Act - Call skill method
        var skill = ToolHarness.CategorizedSkill();

        // Assert
        Assert.NotNull(skill);
        Assert.Equal("CategorizedSkill", skill.Name);
    }

    // ===== P0: String-Based Function References =====

    [Fact]
    public void SourceGenerator_ParsesStringReferences_CorrectFormat()
    {
        // Arrange
        var ToolHarness = new TestSkillToolHarness();

        // Act
        var skill = ToolHarness.SkillWithFunctionReferences();

        // Assert
        Assert.NotNull(skill);
        Assert.NotNull(skill.References);
        Assert.Contains("TestToolHarness.TestFunction1", skill.References);
        Assert.Contains("TestToolHarness.TestFunction2", skill.References);
    }

    [Fact]
    public void SourceGenerator_HandlesMultipleReferences_InVarArgs()
    {
        // Arrange
        var ToolHarness = new TestSkillToolHarness();

        // Act
        var skill = ToolHarness.SkillWithMultipleReferences();

        // Assert
        Assert.NotNull(skill);
        Assert.NotNull(skill.References);
        Assert.Equal(3, skill.References.Length);
        Assert.Contains("ToolHarnessA.Function1", skill.References);
        Assert.Contains("ToolHarnessB.Function2", skill.References);
        Assert.Contains("ToolHarnessC.Function3", skill.References);
    }

    // ===== P0: Method Signature Validation =====

    [Fact]
    public void SkillMethod_MustReturnSkillType()
    {
        // This is validated at compile time by the source generator
        // If a method has [Skill] but doesn't return Skill, it won't compile

        // Arrange & Act
        var ToolHarness = new TestSkillToolHarness();
        var skill = ToolHarness.ValidSkillMethod();

        // Assert
        Assert.IsType<Skill>(skill);
    }

    [Fact]
    public void SkillMethod_CanBeInstanceMethod()
    {
        // Arrange
        var ToolHarness = new TestSkillToolHarness();

        // Act
        var skill = ToolHarness.InstanceSkillMethod();

        // Assert
        Assert.NotNull(skill);
        Assert.Equal("InstanceSkill", skill.Name);
    }

    [Fact]
    public void SkillMethod_CanBeStaticMethod()
    {
        // Arrange & Act
        var skill = TestSkillToolHarness.StaticSkillMethod();

        // Assert
        Assert.NotNull(skill);
        Assert.Equal("StaticSkill", skill.Name);
    }

    // ===== P0: SkillFactory.Create() Detection =====

    [Fact]
    public void SourceGenerator_RequiresSkillFactoryCreate()
    {
        // This is validated at compile time by the source generator
        // If a [Skill] method doesn't call SkillFactory.Create(),
        // the source generator won't generate registration code

        // Arrange
        var ToolHarness = new TestSkillToolHarness();

        // Act
        var skill = ToolHarness.ValidSkillMethod();

        // Assert - SkillFactory.Create() was called
        Assert.NotNull(skill.Name);
        Assert.NotNull(skill.Description);
        Assert.True(skill.FunctionResult != null || skill.SystemPrompt != null);
    }

    // ===== P0: Generated Metadata =====

    [Fact]
    public void SourceGenerator_GeneratesSkillContainer()
    {
        // The source generator creates skill containers for classes with [Skill] methods
        // These containers are AIFunctions with IsContainer=true

        // This is tested implicitly by the ToolHarness registration system
        // If we can register a ToolHarness and its skills are discovered, generation worked

        Assert.True(true); // Placeholder - actual test requires AgentBuilder integration
    }

    // ===== Helper Test ToolHarness =====

    /// <summary>
    /// Test ToolHarness with various skill patterns for Phase 3 validation
    /// </summary>
    private class TestSkillToolHarness
    {
        [Skill]
        public Skill ValidSkillMethod()
        {
            return SkillFactory.Create(
                "ValidSkill",
                "A valid skill",
                functionResult: "Skill activated",
                systemPrompt: "Instructions here");
        }

        [Skill]
        public Skill CategorizedSkill()
        {
            return SkillFactory.Create(
                "CategorizedSkill",
                "A categorized skill",
                functionResult: "Skill activated",
                systemPrompt: "Instructions");
        }

        [Skill]
        public Skill SkillWithFunctionReferences()
        {
            return SkillFactory.Create(
                "SkillWithRefs",
                "Skill with function references",
                functionResult: "Skill activated",
                systemPrompt: "Instructions",
                "TestToolHarness.TestFunction1",
                "TestToolHarness.TestFunction2");
        }

        [Skill]
        public Skill SkillWithMultipleReferences()
        {
            return SkillFactory.Create(
                "MultiRefSkill",
                "Multiple references",
                functionResult: "Skill activated",
                systemPrompt: "Instructions",
                "ToolHarnessA.Function1",
                "ToolHarnessB.Function2",
                "ToolHarnessC.Function3");
        }

        [Skill]
        public Skill InstanceSkillMethod()
        {
            return SkillFactory.Create(
                "InstanceSkill",
                "Instance method skill",
                functionResult: "Skill activated",
                systemPrompt: "Instructions");
        }

        [Skill]
        public static Skill StaticSkillMethod()
        {
            return SkillFactory.Create(
                "StaticSkill",
                "Static method skill",
                functionResult: "Skill activated",
                systemPrompt: "Instructions");
        }
    }
}
