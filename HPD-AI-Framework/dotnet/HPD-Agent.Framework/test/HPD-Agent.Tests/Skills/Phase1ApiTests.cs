using Xunit;

namespace HPD.Agent.Tests.Skills;

/// <summary>
/// Tests for Phase 1: Foundation Classes API
/// Validates Skill, SkillAttribute, SkillFactory, and SkillOptions
/// </summary>
public class Phase1ApiTests
{
    // Mock Toolkits for testing
    private static class MockFileSystemTools
    {
        public static string ReadFile(string path) => $"Reading {path}";
        public static void WriteFile(string path, string content) { }
    }

    private static class MockDebugToolkit
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
        options.AddDocument("test-doc", "Test document description");

        // Act
        var skill = SkillFactory.Create(
            name: "TestSkill",
            description: "Test description",
            functionResult: "Skill activated",
            systemPrompt: "Test instructions",
            options: options,
            "MockFileSystemTools.ReadFile",
            "MockDebugToolkit.GetStackTrace"
        );

        // Assert
        Assert.Equal("TestSkill", skill.Name);
        Assert.Equal(2, skill.References.Length);
        Assert.Single(skill.Options.DocumentReferences);
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
    public void SkillOptions_AddDocument_ValidId_AddsReference()
    {
        // Arrange
        var options = new SkillOptions();

        // Act
        var result = options.AddDocument("test-doc");

        // Assert
        Assert.Same(options, result); // Fluent API
        Assert.Single(options.DocumentReferences);
        Assert.Equal("test-doc", options.DocumentReferences[0].DocumentId);
        Assert.Null(options.DocumentReferences[0].DescriptionOverride);
    }

    [Fact]
    public void SkillOptions_AddDocument_WithDescription_AddsReferenceWithOverride()
    {
        // Arrange
        var options = new SkillOptions();

        // Act
        options.AddDocument("test-doc", "Custom description");

        // Assert
        Assert.Single(options.DocumentReferences);
        Assert.Equal("test-doc", options.DocumentReferences[0].DocumentId);
        Assert.Equal("Custom description", options.DocumentReferences[0].DescriptionOverride);
    }

    [Fact]
    public void SkillOptions_AddDocument_EmptyId_ThrowsArgumentException()
    {
        // Arrange
        var options = new SkillOptions();

        // Act & Assert
        Assert.Throws<ArgumentException>(() => options.AddDocument(""));
    }

    [Fact]
    public void SkillOptions_AddDocument_WhitespaceDescription_ThrowsArgumentException()
    {
        // Arrange
        var options = new SkillOptions();

        // Act & Assert
        Assert.Throws<ArgumentException>(() => options.AddDocument("doc-id", "   "));
    }

    [Fact]
    public void SkillOptions_AddDocumentFromFile_ValidArgs_AddsUpload()
    {
        // Arrange
        var options = new SkillOptions();

        // Act
        var result = options.AddDocumentFromFile(
            "./docs/test.md",
            "Test description");

        // Assert
        Assert.Same(options, result); // Fluent API
        Assert.Single(options.DocumentUploads);
        Assert.Equal("./docs/test.md", options.DocumentUploads[0].FilePath);
        Assert.Equal("test", options.DocumentUploads[0].DocumentId); // Auto-derived
        Assert.Equal("Test description", options.DocumentUploads[0].Description);
    }

    [Fact]
    public void SkillOptions_AddDocumentFromFile_ExplicitId_UsesProvidedId()
    {
        // Arrange
        var options = new SkillOptions();

        // Act
        options.AddDocumentFromFile(
            "./docs/test.md",
            "Test description",
            "custom-id");

        // Assert
        Assert.Single(options.DocumentUploads);
        Assert.Equal("custom-id", options.DocumentUploads[0].DocumentId);
    }

    [Fact]
    public void SkillOptions_AddDocumentFromFile_EmptyFilePath_ThrowsArgumentException()
    {
        // Arrange
        var options = new SkillOptions();

        // Act & Assert
        Assert.Throws<ArgumentException>(() =>
            options.AddDocumentFromFile("", "Description"));
    }

    [Fact]
    public void SkillOptions_AddDocumentFromFile_EmptyDescription_ThrowsArgumentException()
    {
        // Arrange
        var options = new SkillOptions();

        // Act & Assert
        Assert.Throws<ArgumentException>(() =>
            options.AddDocumentFromFile("./file.md", ""));
    }

    [Theory]
    [InlineData("./docs/debugging-workflow.md", "debugging-workflow")]
    [InlineData("./docs/API_Reference.pdf", "api-reference")]
    [InlineData("./docs/Error Codes.docx", "error-codes")]
    [InlineData("guide.txt", "guide")]
    [InlineData("./deep/nested/path/document.md", "document")]
    [InlineData("My_Special-File.md", "my-special-file")]
    public void SkillOptions_AddDocumentFromFile_DerivesCorrectId(
        string filePath,
        string expectedId)
    {
        // Arrange
        var options = new SkillOptions();

        // Act
        options.AddDocumentFromFile(filePath, "Description");

        // Assert
        Assert.Equal(expectedId, options.DocumentUploads[0].DocumentId);
    }

    [Fact]
    public void SkillOptions_FluentApi_ChainsMultipleCalls()
    {
        // Arrange & Act
        var options = new SkillOptions()
            .AddDocument("doc1")
            .AddDocument("doc2", "Description 2")
            .AddDocumentFromFile("./file1.md", "File 1")
            .AddDocumentFromFile("./file2.md", "File 2", "custom-id");

        // Assert
        Assert.Equal(2, options.DocumentReferences.Count);
        Assert.Equal(2, options.DocumentUploads.Count);
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
    public void DocumentReference_Record_SupportsRequiredProperties()
    {
        // Act
        var docRef = new DocumentReference
        {
            DocumentId = "test-doc",
            DescriptionOverride = "Custom description"
        };

        // Assert
        Assert.Equal("test-doc", docRef.DocumentId);
        Assert.Equal("Custom description", docRef.DescriptionOverride);
    }

    [Fact]
    public void DocumentUpload_Record_SupportsRequiredProperties()
    {
        // Act
        var upload = new DocumentUpload
        {
            FilePath = "./test.md",
            DocumentId = "test-id",
            Description = "Test description"
        };

        // Assert
        Assert.Equal("./test.md", upload.FilePath);
        Assert.Equal("test-id", upload.DocumentId);
        Assert.Equal("Test description", upload.Description);
    }

    [Fact]
    public void Skill_InternalProperties_CanBeSet()
    {
        // Arrange
        var skill = SkillFactory.Create("Test", "Test", "FunctionResult", "SystemPrompt");

        // Act
        skill.ResolvedFunctionReferences = new[] { "Toolkit1.Func1", "Toolkit2.Func2" };
        skill.ResolvedToolkitTypes = new[] { "Toolkit1", "Toolkit2" };

        // Assert
        Assert.Equal(2, skill.ResolvedFunctionReferences.Length);
        Assert.Equal(2, skill.ResolvedToolkitTypes.Length);
    }
}
