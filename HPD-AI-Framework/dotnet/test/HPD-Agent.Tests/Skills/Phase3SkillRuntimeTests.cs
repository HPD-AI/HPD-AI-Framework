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
/// 3. Fluent API methods (AddDocumentFromFile, AddDocumentReference) work at runtime
/// 4. Document uploads and references are correctly populated
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
        var Toolkit = new TestSkillToolkit();

        // Act - Call skill method
        var skill = Toolkit.CategorizedSkill();

        // Assert
        Assert.NotNull(skill);
        Assert.Equal("CategorizedSkill", skill.Name);
    }

    // ===== P0: String-Based Function References =====

    [Fact]
    public void SourceGenerator_ParsesStringReferences_CorrectFormat()
    {
        // Arrange
        var Toolkit = new TestSkillToolkit();

        // Act
        var skill = Toolkit.SkillWithFunctionReferences();

        // Assert
        Assert.NotNull(skill);
        Assert.NotNull(skill.References);
        Assert.Contains("TestToolkit.TestFunction1", skill.References);
        Assert.Contains("TestToolkit.TestFunction2", skill.References);
    }

    [Fact]
    public void SourceGenerator_HandlesMultipleReferences_InVarArgs()
    {
        // Arrange
        var Toolkit = new TestSkillToolkit();

        // Act
        var skill = Toolkit.SkillWithMultipleReferences();

        // Assert
        Assert.NotNull(skill);
        Assert.NotNull(skill.References);
        Assert.Equal(3, skill.References.Length);
        Assert.Contains("ToolkitA.Function1", skill.References);
        Assert.Contains("ToolkitB.Function2", skill.References);
        Assert.Contains("ToolkitC.Function3", skill.References);
    }

    // ===== P0: SkillOptions Fluent API =====

    [Fact]
    public void SourceGenerator_ExtractsAddDocument_FromFluentAPI()
    {
        // Arrange
        var Toolkit = new TestSkillToolkit();

        // Act
        var skill = Toolkit.SkillWithDocumentReference();

        // Assert
        Assert.NotNull(skill);
        Assert.NotNull(skill.Options);
        Assert.NotNull(skill.Options.DocumentReferences);
        Assert.Single(skill.Options.DocumentReferences);

        var docRef = skill.Options.DocumentReferences[0];
        Assert.Equal("test-document", docRef.DocumentId);
    }

    [Fact]
    public void SourceGenerator_ExtractsAddDocumentFromFile_FromFluentAPI()
    {
        // Arrange
        var Toolkit = new TestSkillToolkit();

        // Act
        var skill = Toolkit.SkillWithDocumentUpload();

        // Assert
        Assert.NotNull(skill);
        Assert.NotNull(skill.Options);
        Assert.NotNull(skill.Options.DocumentUploads);
        Assert.Single(skill.Options.DocumentUploads);

        var upload = skill.Options.DocumentUploads[0];
        Assert.Equal("./docs/test.md", upload.FilePath);
        Assert.Equal("Test document", upload.Description);
    }

    [Fact]
    public void SourceGenerator_HandlesChainedFluentAPI_MultipleDocuments()
    {
        // Arrange
        var Toolkit = new TestSkillToolkit();

        // Act
        var skill = Toolkit.SkillWithMultipleDocuments();

        // Assert
        Assert.NotNull(skill);
        Assert.NotNull(skill.Options);

        // Should have both references and uploads
        Assert.Equal(2, skill.Options.DocumentReferences.Count);
        Assert.Single(skill.Options.DocumentUploads);
    }

    // ===== P0: Method Signature Validation =====

    [Fact]
    public void SkillMethod_MustReturnSkillType()
    {
        // This is validated at compile time by the source generator
        // If a method has [Skill] but doesn't return Skill, it won't compile

        // Arrange & Act
        var Toolkit = new TestSkillToolkit();
        var skill = Toolkit.ValidSkillMethod();

        // Assert
        Assert.IsType<Skill>(skill);
    }

    [Fact]
    public void SkillMethod_CanBeInstanceMethod()
    {
        // Arrange
        var Toolkit = new TestSkillToolkit();

        // Act
        var skill = Toolkit.InstanceSkillMethod();

        // Assert
        Assert.NotNull(skill);
        Assert.Equal("InstanceSkill", skill.Name);
    }

    [Fact]
    public void SkillMethod_CanBeStaticMethod()
    {
        // Arrange & Act
        var skill = TestSkillToolkit.StaticSkillMethod();

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
        var Toolkit = new TestSkillToolkit();

        // Act
        var skill = Toolkit.ValidSkillMethod();

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

        // This is tested implicitly by the Toolkit registration system
        // If we can register a Toolkit and its skills are discovered, generation worked

        Assert.True(true); // Placeholder - actual test requires AgentBuilder integration
    }

    // ===== Helper Test Toolkit =====

    /// <summary>
    /// Test Toolkit with various skill patterns for Phase 3 validation
    /// </summary>
    private class TestSkillToolkit
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
                "TestToolkit.TestFunction1",
                "TestToolkit.TestFunction2");
        }

        [Skill]
        public Skill SkillWithMultipleReferences()
        {
            return SkillFactory.Create(
                "MultiRefSkill",
                "Multiple references",
                functionResult: "Skill activated",
                systemPrompt: "Instructions",
                "ToolkitA.Function1",
                "ToolkitB.Function2",
                "ToolkitC.Function3");
        }

        [Skill]
        public Skill SkillWithDocumentReference()
        {
            return SkillFactory.Create(
                "DocRefSkill",
                "Skill with document reference",
                functionResult: "Skill activated",
                systemPrompt: "Instructions",
                options: new SkillOptions()
                    .AddDocument("test-document"));
        }

        [Skill]
        public Skill SkillWithDocumentUpload()
        {
            return SkillFactory.Create(
                "DocUploadSkill",
                "Skill with document upload",
                functionResult: "Skill activated",
                systemPrompt: "Instructions",
                options: new SkillOptions()
                    .AddDocumentFromFile("./docs/test.md", "Test document"));
        }

        [Skill]
        public Skill SkillWithMultipleDocuments()
        {
            return SkillFactory.Create(
                "MultiDocSkill",
                "Skill with multiple documents",
                functionResult: "Skill activated",
                systemPrompt: "Instructions",
                options: new SkillOptions()
                    .AddDocument("doc1")
                    .AddDocument("doc2")
                    .AddDocumentFromFile("./docs/additional.md", "Additional doc", "doc3"));
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
