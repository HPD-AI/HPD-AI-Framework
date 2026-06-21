using System.Text.Json;
using HPD.Agent.FFI;
using HPD.Agent.StructuredOutput;
using Xunit;

namespace HPD.Agent.Tests.StructuredOutput;

/// <summary>
/// Unit tests for FFI-specific structured output types and serialization.
/// Tests StructuredResultEventDto and HPDFFIJsonContext serialization.
/// </summary>
public class StructuredOutputFFITests
{
    /// <summary>
    /// Test model for structured output.
    /// </summary>
    public sealed record TestResponse(string Name, int Value);

    [Fact]
    public void StructuredResultEventDto_SerializesCorrectly()
    {
        // Arrange
        var testValue = JsonDocument.Parse("""{"name": "test", "value": 42}""").RootElement;
        var dto = new StructuredResultEventDto(
            Value: testValue,
            IsPartial: false,
            RawJson: """{"name": "test", "value": 42}""",
            TypeName: "TestResponse"
        );

        // Act
        var json = JsonSerializer.Serialize(dto, HPDFFIJsonContext.Default.StructuredResultEventDto);
        var deserialized = JsonSerializer.Deserialize<StructuredResultEventDto>(json, HPDFFIJsonContext.Default.StructuredResultEventDto);

        // Assert
        Assert.NotNull(deserialized);
        Assert.False(deserialized.IsPartial);
        Assert.Equal("TestResponse", deserialized.TypeName);
        Assert.Equal("""{"name": "test", "value": 42}""", deserialized.RawJson);

        // Verify JsonElement value is accessible
        Assert.Equal("test", deserialized.Value.GetProperty("name").GetString());
        Assert.Equal(42, deserialized.Value.GetProperty("value").GetInt32());
    }

    [Fact]
    public void StructuredResultEventDto_PartialFlag_PreservedOnSerialization()
    {
        // Arrange - partial result
        var testValue = JsonDocument.Parse("""{"name": "partial"}""").RootElement;
        var dto = new StructuredResultEventDto(
            Value: testValue,
            IsPartial: true,
            RawJson: """{"name": "partial"}""",
            TypeName: "TestResponse"
        );

        // Act
        var json = JsonSerializer.Serialize(dto, HPDFFIJsonContext.Default.StructuredResultEventDto);

        // Assert - the context uses WriteIndented so there may be whitespace
        Assert.Contains("\"isPartial\": true", json);
    }

    [Fact]
    public void StructuredOutputErrorEvent_SerializesCorrectly()
    {
        // Arrange
        var errorEvent = new StructuredOutputErrorEvent(
            RawJson: "invalid json",
            ErrorMessage: "Unexpected character at position 0",
            ExpectedTypeName: "TestResponse",
            Exception: null
        );

        // Act
        var json = JsonSerializer.Serialize(errorEvent, HPDFFIJsonContext.Default.StructuredOutputErrorEvent);
        var deserialized = JsonSerializer.Deserialize<StructuredOutputErrorEvent>(json, HPDFFIJsonContext.Default.StructuredOutputErrorEvent);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal("invalid json", deserialized.RawJson);
        Assert.Equal("Unexpected character at position 0", deserialized.ErrorMessage);
        Assert.Equal("TestResponse", deserialized.ExpectedTypeName);
        Assert.Null(deserialized.Exception);
    }

    [Fact]
    public void StructuredOutputOptions_SerializesCorrectly()
    {
        // Arrange
        var options = new StructuredOutputOptions
        {
            Mode = "tool",
            SchemaName = "MySchema",
            SchemaDescription = "A test schema",
            ToolName = "submit_result",
            StreamPartials = true,
            PartialDebounceMs = 200
        };

        // Act
        var json = JsonSerializer.Serialize(options, HPDFFIJsonContext.Default.StructuredOutputOptions);
        var deserialized = JsonSerializer.Deserialize<StructuredOutputOptions>(json, HPDFFIJsonContext.Default.StructuredOutputOptions);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal("tool", deserialized.Mode);
        Assert.Equal("MySchema", deserialized.SchemaName);
        Assert.Equal("A test schema", deserialized.SchemaDescription);
        Assert.Equal("submit_result", deserialized.ToolName);
        Assert.True(deserialized.StreamPartials);
        Assert.Equal(200, deserialized.PartialDebounceMs);
    }

    [Fact]
    public void StructuredOutputOptions_OmitsNullValues()
    {
        // Arrange - default options with null optional fields
        var options = new StructuredOutputOptions();

        // Act
        var json = JsonSerializer.Serialize(options, HPDFFIJsonContext.Default.StructuredOutputOptions);

        // Assert - null values should be omitted per JsonIgnoreCondition.WhenWritingNull
        Assert.DoesNotContain("\"schema\":", json.ToLower());
        Assert.DoesNotContain("\"schemaName\":", json);
        Assert.DoesNotContain("\"schemaDescription\":", json);
        Assert.DoesNotContain("\"toolName\":", json);
    }

    [Fact]
    public void StructuredResultEventDto_HandlesComplexNestedValue()
    {
        // Arrange - complex nested JSON
        var complexJson = """
        {
            "users": [
                {"id": 1, "name": "Alice", "roles": ["admin", "user"]},
                {"id": 2, "name": "Bob", "roles": ["user"]}
            ],
            "metadata": {
                "total": 2,
                "page": 1
            }
        }
        """;
        var testValue = JsonDocument.Parse(complexJson).RootElement;
        var dto = new StructuredResultEventDto(
            Value: testValue,
            IsPartial: false,
            RawJson: complexJson,
            TypeName: "UsersResponse"
        );

        // Act
        var json = JsonSerializer.Serialize(dto, HPDFFIJsonContext.Default.StructuredResultEventDto);
        var deserialized = JsonSerializer.Deserialize<StructuredResultEventDto>(json, HPDFFIJsonContext.Default.StructuredResultEventDto);

        // Assert
        Assert.NotNull(deserialized);

        // Verify nested array access
        var users = deserialized.Value.GetProperty("users");
        Assert.Equal(2, users.GetArrayLength());
        Assert.Equal("Alice", users[0].GetProperty("name").GetString());

        // Verify nested object access
        var metadata = deserialized.Value.GetProperty("metadata");
        Assert.Equal(2, metadata.GetProperty("total").GetInt32());
    }

    [Fact]
    public void HPDFFIJsonContext_IncludesStructuredOutputTypes()
    {
        // Assert - verify the context has type info for structured output types
        var context = HPDFFIJsonContext.Default;

        Assert.NotNull(context.StructuredOutputOptions);
        Assert.NotNull(context.StructuredOutputErrorEvent);
        Assert.NotNull(context.StructuredResultEventDto);
    }

    [Fact]
    public void StructuredResultEventDto_IsAgentEvent()
    {
        // Arrange
        var testValue = JsonDocument.Parse("{}").RootElement;
        var dto = new StructuredResultEventDto(
            Value: testValue,
            IsPartial: false,
            RawJson: "{}",
            TypeName: "EmptyResponse"
        );

        // Assert - StructuredResultEventDto inherits from AgentEvent
        Assert.IsAssignableFrom<AgentEvent>(dto);
    }
}
