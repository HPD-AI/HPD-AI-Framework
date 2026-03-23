// Copyright (c) 2025 Einstein Essibu. All rights reserved.

using HPD.Agent.Audio;
using Xunit;

namespace HPD.Agent.Audio.Tests;

/// <summary>
/// Tests for the text filtering functionality in AudioPipelineMiddleware.
/// </summary>
public class TextFilteringTests
{
    [Fact]
    public void FilterTextForTts_RemovesCodeBlocks()
    {
        // Arrange
        var middleware = new AudioPipelineMiddleware
        {
            EnableTextFiltering = true,
            FilterCodeBlocks = true
        };

        var text = "Here is some code:\n```python\nprint('hello')\n```\nAnd more text.";

        // Act
        var filtered = InvokeFilterTextForTts(middleware, text);

        // Assert
        Assert.DoesNotContain("```", filtered);
        Assert.DoesNotContain("print", filtered);
        Assert.Contains("code omitted", filtered);
        Assert.Contains("And more text", filtered);
    }

    [Fact]
    public void FilterTextForTts_RemovesTables()
    {
        // Arrange
        var middleware = new AudioPipelineMiddleware
        {
            EnableTextFiltering = true,
            FilterTables = true
        };

        var text = "Here is a table:\n| Col1 | Col2 |\n| --- | --- |\n| A | B |\nMore text.";

        // Act
        var filtered = InvokeFilterTextForTts(middleware, text);

        // Assert
        Assert.DoesNotContain("|", filtered);
        Assert.Contains("table omitted", filtered);
        Assert.Contains("More text", filtered);
    }

    [Fact]
    public void FilterTextForTts_SimplifiesUrls()
    {
        // Arrange
        var middleware = new AudioPipelineMiddleware
        {
            EnableTextFiltering = true,
            FilterUrls = true
        };

        var text = "Check out https://www.example.com/path/to/resource for more info.";

        // Act
        var filtered = InvokeFilterTextForTts(middleware, text);

        // Assert
        Assert.DoesNotContain("https://", filtered);
        Assert.DoesNotContain("/path/to/resource", filtered);
        Assert.Contains("example.com", filtered);
    }

    [Fact]
    public void FilterTextForTts_RemovesBoldFormatting()
    {
        // Arrange
        var middleware = new AudioPipelineMiddleware
        {
            EnableTextFiltering = true,
            FilterMarkdownFormatting = true
        };

        var text = "This is **bold** text.";

        // Act
        var filtered = InvokeFilterTextForTts(middleware, text);

        // Assert
        Assert.DoesNotContain("**", filtered);
        Assert.Contains("bold", filtered);
        Assert.Contains("This is", filtered);
    }

    [Fact]
    public void FilterTextForTts_RemovesItalicFormatting()
    {
        // Arrange
        var middleware = new AudioPipelineMiddleware
        {
            EnableTextFiltering = true,
            FilterMarkdownFormatting = true
        };

        var text = "This is *italic* text.";

        // Act
        var filtered = InvokeFilterTextForTts(middleware, text);

        // Assert
        Assert.DoesNotContain("*", filtered);
        Assert.Contains("italic", filtered);
    }

    [Fact]
    public void FilterTextForTts_RemovesStrikethrough()
    {
        // Arrange
        var middleware = new AudioPipelineMiddleware
        {
            EnableTextFiltering = true,
            FilterMarkdownFormatting = true
        };

        var text = "This is ~~strikethrough~~ text.";

        // Act
        var filtered = InvokeFilterTextForTts(middleware, text);

        // Assert
        Assert.DoesNotContain("~~", filtered);
        Assert.Contains("strikethrough", filtered);
    }

    [Fact]
    public void FilterTextForTts_RemovesInlineCode()
    {
        // Arrange
        var middleware = new AudioPipelineMiddleware
        {
            EnableTextFiltering = true,
            FilterMarkdownFormatting = true
        };

        var text = "Use the `print()` function.";

        // Act
        var filtered = InvokeFilterTextForTts(middleware, text);

        // Assert
        Assert.DoesNotContain("`", filtered);
        Assert.Contains("print()", filtered);
    }

    [Fact]
    public void FilterTextForTts_RemovesHeaders()
    {
        // Arrange
        var middleware = new AudioPipelineMiddleware
        {
            EnableTextFiltering = true,
            FilterMarkdownFormatting = true
        };

        var text = "# Header\nSome content.\n## Subheader\nMore content.";

        // Act
        var filtered = InvokeFilterTextForTts(middleware, text);

        // Assert
        Assert.DoesNotContain("#", filtered);
        Assert.Contains("Header", filtered);
        Assert.Contains("Subheader", filtered);
    }

    [Fact]
    public void FilterTextForTts_RemovesEmoji()
    {
        // Arrange
        var middleware = new AudioPipelineMiddleware
        {
            EnableTextFiltering = true,
            FilterEmoji = true
        };

        var text = "Hello! 👋 How are you? 😊";

        // Act
        var filtered = InvokeFilterTextForTts(middleware, text);

        // Assert
        Assert.DoesNotContain("👋", filtered);
        Assert.DoesNotContain("😊", filtered);
        Assert.Contains("Hello!", filtered);
        Assert.Contains("How are you?", filtered);
    }

    [Fact]
    public void FilterTextForTts_WhenDisabled_ReturnsOriginal()
    {
        // Arrange
        var middleware = new AudioPipelineMiddleware
        {
            EnableTextFiltering = false
        };

        var text = "Here is **bold** and ```code``` and 😊";

        // Act
        var filtered = InvokeFilterTextForTts(middleware, text);

        // Assert
        Assert.Equal(text, filtered);
    }

    [Fact]
    public void FilterTextForTts_SelectiveFiltering()
    {
        // Arrange
        var middleware = new AudioPipelineMiddleware
        {
            EnableTextFiltering = true,
            FilterCodeBlocks = true,
            FilterTables = false,
            FilterUrls = false,
            FilterMarkdownFormatting = false,
            FilterEmoji = false
        };

        var text = "Text with ```code``` and **bold** and 😊";

        // Act
        var filtered = InvokeFilterTextForTts(middleware, text);

        // Assert
        Assert.DoesNotContain("```", filtered);
        Assert.Contains("**bold**", filtered);
        Assert.Contains("😊", filtered);
    }

    [Fact]
    public void FilterTextForTts_CollapsesMultipleSpaces()
    {
        // Arrange
        var middleware = new AudioPipelineMiddleware
        {
            EnableTextFiltering = true,
            FilterMarkdownFormatting = true
        };

        var text = "Text   with    multiple     spaces.";

        // Act
        var filtered = InvokeFilterTextForTts(middleware, text);

        // Assert
        Assert.DoesNotContain("   ", filtered);
        Assert.Equal("Text with multiple spaces.", filtered);
    }

    [Fact]
    public void FilterTextForTts_ComplexDocument()
    {
        // Arrange
        var middleware = new AudioPipelineMiddleware
        {
            EnableTextFiltering = true,
            FilterCodeBlocks = true,
            FilterTables = true,
            FilterUrls = true,
            FilterMarkdownFormatting = true,
            FilterEmoji = true
        };

        var text = @"# Introduction 🎉

Here is some **important** information about the `API`.

```javascript
const x = 42;
```

Visit https://example.com for more details.

Thanks! 👍";

        // Act
        var filtered = InvokeFilterTextForTts(middleware, text);

        // Assert
        Assert.DoesNotContain("**", filtered);
        Assert.DoesNotContain("`", filtered);
        Assert.DoesNotContain("```", filtered);
        Assert.DoesNotContain("👍", filtered);
        Assert.DoesNotContain("🎉", filtered);
        Assert.DoesNotContain("https://", filtered);

        Assert.Contains("Introduction", filtered);
        Assert.Contains("important", filtered);
        Assert.Contains("API", filtered);
        Assert.Contains("code omitted", filtered);
        Assert.Contains("example.com", filtered);
        Assert.Contains("Thanks!", filtered);
    }

    /// <summary>
    /// Helper to invoke internal FilterTextForTts method via reflection.
    /// </summary>
    private static string InvokeFilterTextForTts(AudioPipelineMiddleware middleware, string text)
    {
        var method = typeof(AudioPipelineMiddleware).GetMethod(
            "FilterTextForTts",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        return (string)method!.Invoke(middleware, [text])!;
    }
}
