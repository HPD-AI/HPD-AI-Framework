using FluentAssertions;
using Xunit;

namespace HPD.Agent.Tests.Skills;

/// <summary>
/// Tests for SkillOptions - validates Phase 1 (URL support) functionality
/// </summary>
public class SkillOptionsTests
{
    [Fact]
    public void AddDocumentFromFile_WithValidPath_ShouldSucceed()
    {
        // Arrange
        var options = new SkillOptions();

        // Act
        var result = options.AddDocumentFromFile("./docs/test.md", "Test document");

        // Assert
        result.Should().BeSameAs(options); // Fluent interface
        options.DocumentUploads.Should().HaveCount(1);
        options.DocumentUploads[0].FilePath.Should().Be("./docs/test.md");
        options.DocumentUploads[0].Description.Should().Be("Test document");
        options.DocumentUploads[0].DocumentId.Should().Be("test"); // Auto-derived
        options.DocumentUploads[0].IsFilePath.Should().BeTrue();
        options.DocumentUploads[0].IsUrl.Should().BeFalse();
    }

    [Fact]
    public void AddDocumentFromFile_WithExplicitDocumentId_ShouldUseProvidedId()
    {
        // Arrange
        var options = new SkillOptions();

        // Act
        options.AddDocumentFromFile("./docs/test.md", "Test document", "custom-id");

        // Assert
        options.DocumentUploads[0].DocumentId.Should().Be("custom-id");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public void AddDocumentFromFile_WithEmptyFilePath_ShouldThrow(string filePath)
    {
        // Arrange
        var options = new SkillOptions();

        // Act & Assert
        Assert.Throws<ArgumentException>(() =>
            options.AddDocumentFromFile(filePath, "Test document"));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public void AddDocumentFromFile_WithEmptyDescription_ShouldThrow(string description)
    {
        // Arrange
        var options = new SkillOptions();

        // Act & Assert
        Assert.Throws<ArgumentException>(() =>
            options.AddDocumentFromFile("./test.md", description));
    }

    [Fact]
    public void AddDocumentFromUrl_WithValidHttpUrl_ShouldSucceed()
    {
        // Arrange
        var options = new SkillOptions();

        // Act
        var result = options.AddDocumentFromUrl(
            "http://example.com/docs/test.md",
            "Remote test document");

        // Assert
        result.Should().BeSameAs(options);
        options.DocumentUploads.Should().HaveCount(1);
        options.DocumentUploads[0].Url.Should().Be("http://example.com/docs/test.md");
        options.DocumentUploads[0].Description.Should().Be("Remote test document");
        options.DocumentUploads[0].DocumentId.Should().Be("test"); // Auto-derived from URL path
        options.DocumentUploads[0].IsUrl.Should().BeTrue();
        options.DocumentUploads[0].IsFilePath.Should().BeFalse();
    }

    [Fact]
    public void AddDocumentFromUrl_WithValidHttpsUrl_ShouldSucceed()
    {
        // Arrange
        var options = new SkillOptions();

        // Act
        options.AddDocumentFromUrl(
            "https://raw.githubusercontent.com/microsoft/semantic-kernel/main/README.md",
            "Semantic Kernel README",
            "semantic-kernel-readme");

        // Assert
        options.DocumentUploads[0].Url.Should().Be("https://raw.githubusercontent.com/microsoft/semantic-kernel/main/README.md");
        options.DocumentUploads[0].DocumentId.Should().Be("semantic-kernel-readme");
        options.DocumentUploads[0].IsUrl.Should().BeTrue();
    }

    [Fact]
    public void AddDocumentFromUrl_WithUrlWithoutFilename_ShouldDeriveIdFromHost()
    {
        // Arrange
        var options = new SkillOptions();

        // Act
        options.AddDocumentFromUrl("https://example.com", "Example website");

        // Assert
        options.DocumentUploads[0].DocumentId.Should().Be("example-com");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public void AddDocumentFromUrl_WithEmptyUrl_ShouldThrow(string url)
    {
        // Arrange
        var options = new SkillOptions();

        // Act & Assert
        Assert.Throws<ArgumentException>(() =>
            options.AddDocumentFromUrl(url, "Test document"));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public void AddDocumentFromUrl_WithEmptyDescription_ShouldThrow(string description)
    {
        // Arrange
        var options = new SkillOptions();

        // Act & Assert
        Assert.Throws<ArgumentException>(() =>
            options.AddDocumentFromUrl("https://example.com/test.md", description));
    }

    [Theory]
    [InlineData("ftp://example.com/test.md")]
    [InlineData("file:///C:/test.md")]
    [InlineData("not-a-url")]
    [InlineData("C:\\test.md")]
    public void AddDocumentFromUrl_WithInvalidUrl_ShouldThrow(string invalidUrl)
    {
        // Arrange
        var options = new SkillOptions();

        // Act & Assert
        Assert.Throws<ArgumentException>(() =>
            options.AddDocumentFromUrl(invalidUrl, "Test document"));
    }

    [Fact]
    public void AddDocumentFromFile_MultipleFiles_ShouldAllBeAdded()
    {
        // Arrange
        var options = new SkillOptions();

        // Act
        options
            .AddDocumentFromFile("./docs/file1.md", "First document")
            .AddDocumentFromFile("./docs/file2.pdf", "Second document");

        // Assert
        options.DocumentUploads.Should().HaveCount(2);
        options.DocumentUploads[0].DocumentId.Should().Be("file1");
        options.DocumentUploads[1].DocumentId.Should().Be("file2");
    }

    [Fact]
    public void AddDocumentFromUrl_MultipleUrls_ShouldAllBeAdded()
    {
        // Arrange
        var options = new SkillOptions();

        // Act
        options
            .AddDocumentFromUrl("https://example.com/doc1.md", "First document")
            .AddDocumentFromUrl("http://example.org/doc2.pdf", "Second document");

        // Assert
        options.DocumentUploads.Should().HaveCount(2);
        options.DocumentUploads[0].DocumentId.Should().Be("doc1");
        options.DocumentUploads[1].DocumentId.Should().Be("doc2");
    }

    [Fact]
    public void AddDocument_MixedFileAndUrlDocuments_ShouldAllBeAdded()
    {
        // Arrange
        var options = new SkillOptions();

        // Act
        options
            .AddDocumentFromFile("./docs/local.md", "Local document")
            .AddDocumentFromUrl("https://example.com/remote.md", "Remote document");

        // Assert
        options.DocumentUploads.Should().HaveCount(2);
        options.DocumentUploads[0].IsFilePath.Should().BeTrue();
        options.DocumentUploads[0].IsUrl.Should().BeFalse();
        options.DocumentUploads[1].IsFilePath.Should().BeFalse();
        options.DocumentUploads[1].IsUrl.Should().BeTrue();
    }

    [Theory]
    [InlineData("./docs/my_file.md", "my-file")]
    [InlineData("./docs/My File.md", "my-file")]
    [InlineData("./docs/MY_FILE.MD", "my-file")]
    public void DeriveDocumentId_ShouldNormalizeToKebabCase(string filePath, string expectedId)
    {
        // Arrange
        var options = new SkillOptions();

        // Act
        options.AddDocumentFromFile(filePath, "Test");

        // Assert
        options.DocumentUploads[0].DocumentId.Should().Be(expectedId);
    }

    [Theory]
    [InlineData("https://example.com/my_file.md", "my-file")]
    [InlineData("https://example.com/My File.md", "my-file")]
    [InlineData("https://example.com/MY_FILE.MD", "my-file")]
    public void DeriveDocumentIdFromUrl_ShouldNormalizeToKebabCase(string url, string expectedId)
    {
        // Arrange
        var options = new SkillOptions();

        // Act
        options.AddDocumentFromUrl(url, "Test");

        // Assert
        options.DocumentUploads[0].DocumentId.Should().Be(expectedId);
    }
}
