using System.Text.Json;
using HPD.Agent.StructuredOutput;
using Xunit;

namespace HPD.Agent.Tests.StructuredOutput;

/// <summary>
/// Unit tests for PartialJsonCloser - tests the stack-based JSON closing algorithm
/// used for streaming partial JSON during structured output.
/// </summary>
public class PartialJsonCloserTests
{
    [Fact]
    public void TryClose_EmptyString_ReturnsNull()
    {
        // Arrange
        var input = "";

        // Act
        var result = PartialJsonCloser.TryClose(input);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void TryClose_NullString_ReturnsNull()
    {
        // Act
        var result = PartialJsonCloser.TryClose(null!);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void TryClose_CompleteJson_ReturnsUnchanged()
    {
        // Arrange
        var input = """{"name": "test", "value": 42}""";

        // Act
        var result = PartialJsonCloser.TryClose(input);

        // Assert
        Assert.Equal(input, result);
        AssertValidJson(result);
    }

    [Fact]
    public void TryClose_UnclosedObject_ClosesBracket()
    {
        // Arrange: {"name": "test"
        var input = "{\"name\": \"test\"";

        // Act
        var result = PartialJsonCloser.TryClose(input);

        // Assert: {"name": "test"}
        Assert.NotNull(result);
        Assert.EndsWith("}", result);
        AssertValidJson(result);
    }

    [Fact]
    public void TryClose_UnclosedArray_ClosesBracket()
    {
        // Arrange: [1, 2, 3
        var input = "[1, 2, 3";

        // Act
        var result = PartialJsonCloser.TryClose(input);

        // Assert: [1, 2, 3]
        Assert.NotNull(result);
        Assert.Equal("[1, 2, 3]", result);
        AssertValidJson(result);
    }

    [Fact]
    public void TryClose_NestedStructure_ClosesInOrder()
    {
        // Arrange: {"items":[{"name":"test"
        var input = "{\"items\":[{\"name\":\"test\"";

        // Act
        var result = PartialJsonCloser.TryClose(input);

        // Assert: {"items":[{"name":"test"}]}
        Assert.NotNull(result);
        Assert.EndsWith("}]}", result);
        AssertValidJson(result);
    }

    [Fact]
    public void TryClose_UnclosedString_ClosesQuote()
    {
        // Arrange: {"name": "te
        var input = """{"name": "te""";

        // Act
        var result = PartialJsonCloser.TryClose(input);

        // Assert: {"name": "te"}
        Assert.NotNull(result);
        AssertValidJson(result);

        // Verify the value is preserved
        using var doc = JsonDocument.Parse(result);
        Assert.Equal("te", doc.RootElement.GetProperty("name").GetString());
    }

    [Fact]
    public void TryClose_EscapedQuoteInString_HandlesCorrectly()
    {
        // Arrange: {"val": "say \"hi
        var input = """{"val": "say \"hi""";

        // Act
        var result = PartialJsonCloser.TryClose(input);

        // Assert: Should close the string and object properly
        Assert.NotNull(result);
        AssertValidJson(result);

        // Verify the escaped quote is preserved
        using var doc = JsonDocument.Parse(result);
        var val = doc.RootElement.GetProperty("val").GetString();
        Assert.Contains("\"hi", val);
    }

    [Fact]
    public void TryClose_UnescapedNewline_EscapesIt()
    {
        // Arrange: {"text": "line1\nline2 (actual newline in string)
        var input = "{\"text\": \"line1\nline2";

        // Act
        var result = PartialJsonCloser.TryClose(input);

        // Assert: Newline should be escaped
        Assert.NotNull(result);
        Assert.Contains("\\n", result);
        AssertValidJson(result);
    }

    [Fact]
    public void TryClose_TrailingBackslash_RemovesIt()
    {
        // Arrange: {"path": "C:\
        var input = """{"path": "C:\""";

        // Act
        var result = PartialJsonCloser.TryClose(input);

        // Assert: Trailing backslash should be removed to close string
        Assert.NotNull(result);
        AssertValidJson(result);
    }

    [Fact]
    public void TryClose_DeeplyNested_HandlesCorrectly()
    {
        // Arrange: 10 levels deep
        var input = "{\"a\":{\"b\":{\"c\":{\"d\":{\"e\":{\"f\":{\"g\":{\"h\":{\"i\":{\"j\":\"val\"";

        // Act
        var result = PartialJsonCloser.TryClose(input);

        // Assert: All brackets should be closed in reverse order
        Assert.NotNull(result);
        // 10 closing braces for the 10 nested objects
        Assert.EndsWith("}}}}}}}}}}", result);
        AssertValidJson(result);
    }

    [Fact]
    public void TryClose_BacktrackMode_FindsValidState()
    {
        // Arrange: Incomplete JSON that needs backtracking
        var input = """{"name": "test", "data": {"partial""";

        // Act: closeTrailingStrings: false forces backtracking
        var result = PartialJsonCloser.TryClose(input, closeTrailingStrings: false);

        // Assert: Should backtrack to valid state
        // Result may be null or a valid JSON subset
        if (result != null)
        {
            AssertValidJson(result);
        }
    }

    [Fact]
    public void TryClose_MixedArrayAndObject_ClosesCorrectly()
    {
        // Arrange: {"items": [1, {"nested": "value"
        var input = "{\"items\": [1, {\"nested\": \"value\"";

        // Act
        var result = PartialJsonCloser.TryClose(input);

        // Assert: Should close string, object, array, object in order
        Assert.NotNull(result);
        Assert.EndsWith("}]}", result);
        AssertValidJson(result);
    }

    [Fact]
    public void TryClose_EmptyObject_ReturnsUnchanged()
    {
        // Arrange
        var input = "{}";

        // Act
        var result = PartialJsonCloser.TryClose(input);

        // Assert
        Assert.Equal("{}", result);
        AssertValidJson(result);
    }

    [Fact]
    public void TryClose_EmptyArray_ReturnsUnchanged()
    {
        // Arrange
        var input = "[]";

        // Act
        var result = PartialJsonCloser.TryClose(input);

        // Assert
        Assert.Equal("[]", result);
        AssertValidJson(result);
    }

    [Fact]
    public void TryClose_StringWithSpecialCharacters_PreservesThem()
    {
        // Arrange: String with tabs, unicode
        var input = """{"text": "hello\tworld\u0041""";

        // Act
        var result = PartialJsonCloser.TryClose(input);

        // Assert
        Assert.NotNull(result);
        AssertValidJson(result);
    }

    [Fact]
    public void TryClose_NumberValue_HandlesCorrectly()
    {
        // Arrange: Complete number
        var input = """{"value": 123}""";

        // Act
        var result = PartialJsonCloser.TryClose(input);

        // Assert
        Assert.Equal(input, result);
        AssertValidJson(result);
    }

    [Fact]
    public void TryClose_BooleanValue_HandlesCorrectly()
    {
        // Arrange: Complete boolean
        var input = """{"flag": true}""";

        // Act
        var result = PartialJsonCloser.TryClose(input);

        // Assert
        Assert.Equal(input, result);
        AssertValidJson(result);
    }

    [Fact]
    public void TryClose_NullValue_HandlesCorrectly()
    {
        // Arrange: Complete null
        var input = """{"value": null}""";

        // Act
        var result = PartialJsonCloser.TryClose(input);

        // Assert
        Assert.Equal(input, result);
        AssertValidJson(result);
    }

    [Fact]
    public void TryClose_MultipleStrings_ClosesLast()
    {
        // Arrange: {"first": "one", "second": "two
        var input = """{"first": "one", "second": "two""";

        // Act
        var result = PartialJsonCloser.TryClose(input);

        // Assert
        Assert.NotNull(result);
        AssertValidJson(result);

        using var doc = JsonDocument.Parse(result);
        Assert.Equal("one", doc.RootElement.GetProperty("first").GetString());
        Assert.Equal("two", doc.RootElement.GetProperty("second").GetString());
    }

    /// <summary>
    /// Helper method to assert that a string is valid JSON.
    /// </summary>
    private static void AssertValidJson(string? json)
    {
        Assert.NotNull(json);
        try
        {
            using var doc = JsonDocument.Parse(json);
            // If we get here, it's valid JSON
        }
        catch (JsonException ex)
        {
            Assert.Fail($"Invalid JSON: {ex.Message}\nJSON: {json}");
        }
    }
}
