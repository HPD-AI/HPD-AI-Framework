// Copyright (c) 2025 Einstein Essibu. All rights reserved.

using HPD.Agent.ClientTools;
using System.Text.Json;
using Xunit;

namespace HPD.Agent.Tests.ClientTools;

/// <summary>
/// Unit tests for content type records (TextContent, BinaryContent, JsonContent).
/// Tests record behavior, validation, and serialization compatibility.
/// </summary>
public class ContentTypeTests
{
    // ============================================
    // TextContent Tests
    // ============================================

    [Fact]
    public void TextContent_Constructor_SetsProperties()
    {
        // Arrange & Act
        var content = new TextContent("Hello, World!");

        // Assert
        Assert.Equal("text", content.Type);
        Assert.Equal("Hello, World!", content.Text);
    }

    [Fact]
    public void TextContent_ImplementsInterface()
    {
        // Arrange
        var content = new TextContent("Test");

        // Assert
        Assert.IsAssignableFrom<IToolResultContent>(content);
    }

    [Fact]
    public void TextContent_RecordEquality()
    {
        // Arrange
        var content1 = new TextContent("Same text");
        var content2 = new TextContent("Same text");
        var content3 = new TextContent("Different text");

        // Assert
        Assert.Equal(content1, content2);
        Assert.NotEqual(content1, content3);
    }

    [Fact]
    public void TextContent_WithExpression_CreatesNewInstance()
    {
        // Arrange
        var original = new TextContent("Original");

        // Act
        var modified = original with { Text = "Modified" };

        // Assert
        Assert.Equal("Original", original.Text);
        Assert.Equal("Modified", modified.Text);
    }

    // ============================================
    // BinaryContent Tests
    // ============================================

    [Fact]
    public void BinaryContent_Constructor_WithData()
    {
        // Arrange
        var data = Convert.ToBase64String(new byte[] { 1, 2, 3, 4 });

        // Act
        var content = new BinaryContent(
            MimeType: "image/png",
            Data: data);

        // Assert
        Assert.Equal("binary", content.Type);
        Assert.Equal("image/png", content.MimeType);
        Assert.Equal(data, content.Data);
        Assert.Null(content.Url);
        Assert.Null(content.Id);
        Assert.Null(content.Filename);
    }

    [Fact]
    public void BinaryContent_Constructor_WithUrl()
    {
        // Arrange & Act
        var content = new BinaryContent(
            MimeType: "application/pdf",
            Url: "https://example.com/doc.pdf",
            Filename: "document.pdf");

        // Assert
        Assert.Equal("binary", content.Type);
        Assert.Equal("application/pdf", content.MimeType);
        Assert.Null(content.Data);
        Assert.Equal("https://example.com/doc.pdf", content.Url);
        Assert.Equal("document.pdf", content.Filename);
    }

    [Fact]
    public void BinaryContent_ImplementsInterface()
    {
        // Arrange
        var content = new BinaryContent(MimeType: "image/jpeg");

        // Assert
        Assert.IsAssignableFrom<IToolResultContent>(content);
    }

    [Fact]
    public void BinaryContent_AllProperties()
    {
        // Arrange & Act
        var content = new BinaryContent(
            MimeType: "image/png",
            Data: "base64data",
            Url: "https://example.com/image.png",
            Id: "img-123",
            Filename: "screenshot.png");

        // Assert
        Assert.Equal("binary", content.Type);
        Assert.Equal("image/png", content.MimeType);
        Assert.Equal("base64data", content.Data);
        Assert.Equal("https://example.com/image.png", content.Url);
        Assert.Equal("img-123", content.Id);
        Assert.Equal("screenshot.png", content.Filename);
    }

    // ============================================
    // JsonContent Tests
    // ============================================

    [Fact]
    public void JsonContent_Constructor_WithObject()
    {
        // Arrange
        var value = JsonSerializer.SerializeToElement(new { name = "Test", count = 42 });

        // Act
        var content = new JsonContent(value);

        // Assert
        Assert.Equal("json", content.Type);
        Assert.Equal("Test", content.Value.GetProperty("name").GetString());
        Assert.Equal(42, content.Value.GetProperty("count").GetInt32());
    }

    [Fact]
    public void JsonContent_ImplementsInterface()
    {
        // Arrange
        var value = JsonSerializer.SerializeToElement(new { });
        var content = new JsonContent(value);

        // Assert
        Assert.IsAssignableFrom<IToolResultContent>(content);
    }

    [Fact]
    public void JsonContent_WithArray()
    {
        // Arrange
        var value = JsonSerializer.SerializeToElement(new[] { 1, 2, 3 });

        // Act
        var content = new JsonContent(value);

        // Assert
        Assert.Equal("json", content.Type);
        Assert.Equal(JsonValueKind.Array, content.Value.ValueKind);
        Assert.Equal(3, content.Value.GetArrayLength());
    }

    // ============================================
    // Polymorphic Collection Tests
    // ============================================

    [Fact]
    public void MixedContent_CanBeStoredInList()
    {
        // Arrange
        var textContent = new TextContent("Hello");
        var binaryContent = new BinaryContent(MimeType: "image/png", Data: "base64");
        var jsonContent = new JsonContent(JsonSerializer.SerializeToElement(new { key = "value" }));

        // Act
        var contents = new List<IToolResultContent>
        {
            textContent,
            binaryContent,
            jsonContent
        };

        // Assert
        Assert.Equal(3, contents.Count);
        Assert.Equal("text", contents[0].Type);
        Assert.Equal("binary", contents[1].Type);
        Assert.Equal("json", contents[2].Type);
    }

    [Fact]
    public void MixedContent_TypeDiscrimination()
    {
        // Arrange
        var contents = new IToolResultContent[]
        {
            new TextContent("text"),
            new BinaryContent(MimeType: "image/png"),
            new JsonContent(JsonSerializer.SerializeToElement(new { }))
        };

        // Act & Assert
        foreach (var content in contents)
        {
            switch (content)
            {
                case TextContent text:
                    Assert.Equal("text", text.Text);
                    break;
                case BinaryContent binary:
                    Assert.Equal("image/png", binary.MimeType);
                    break;
                case JsonContent json:
                    Assert.Equal(JsonValueKind.Object, json.Value.ValueKind);
                    break;
                default:
                    Assert.Fail("Unknown content type");
                    break;
            }
        }
    }

    // ============================================
    // Serialization Compatibility Tests
    // ============================================

    [Fact]
    public void TextContent_SerializesToJson()
    {
        // Arrange
        var content = new TextContent("Hello, World!");

        // Act
        var json = JsonSerializer.Serialize(content);
        var deserialized = JsonSerializer.Deserialize<TextContent>(json);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal("text", deserialized.Type);
        Assert.Equal("Hello, World!", deserialized.Text);
    }

    [Fact]
    public void BinaryContent_SerializesToJson()
    {
        // Arrange
        var content = new BinaryContent(
            MimeType: "image/png",
            Data: "base64data",
            Filename: "test.png");

        // Act
        var json = JsonSerializer.Serialize(content);
        var deserialized = JsonSerializer.Deserialize<BinaryContent>(json);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal("binary", deserialized.Type);
        Assert.Equal("image/png", deserialized.MimeType);
        Assert.Equal("base64data", deserialized.Data);
        Assert.Equal("test.png", deserialized.Filename);
    }

    [Fact]
    public void JsonContent_SerializesToJson()
    {
        // Arrange
        var value = JsonSerializer.SerializeToElement(new { foo = "bar", num = 123 });
        var content = new JsonContent(value);

        // Act
        var json = JsonSerializer.Serialize(content);
        var deserialized = JsonSerializer.Deserialize<JsonContent>(json);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal("json", deserialized.Type);
        Assert.Equal("bar", deserialized.Value.GetProperty("foo").GetString());
        Assert.Equal(123, deserialized.Value.GetProperty("num").GetInt32());
    }
}
