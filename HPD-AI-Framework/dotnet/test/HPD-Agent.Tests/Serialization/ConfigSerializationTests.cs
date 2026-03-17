using System.Text.Json;
using HPD.Agent;
using Xunit;

namespace HPD.Agent.Tests.Serialization;

/// <summary>
/// Tests for config serialization.
/// Verifies that AgentConfig with Toolkits and Middlewares can be
/// serialized to JSON and deserialized back.
/// </summary>
public class ConfigSerializationTests
{
    [Fact]
    public void ToolkitReference_SimpleString_RoundTrip()
    {
        // Arrange
        var reference = new ToolkitReference { Name = "MathToolkit" };
        var options = new JsonSerializerOptions { WriteIndented = true };

        // Act
        var json = JsonSerializer.Serialize(reference, options);
        var deserialized = JsonSerializer.Deserialize<ToolkitReference>(json);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal("MathToolkit", deserialized.Name);
        Assert.Null(deserialized.Functions);
        Assert.Null(deserialized.Config);
        Assert.Null(deserialized.Metadata);
    }

    [Fact]
    public void ToolkitReference_ImplicitConversion_FromString()
    {
        // Arrange & Act
        ToolkitReference reference = "SearchToolkit";

        // Assert
        Assert.Equal("SearchToolkit", reference.Name);
    }

    [Fact]
    public void ToolkitReference_RichSyntax_RoundTrip()
    {
        // Arrange
        var json = """
            {
              "name": "FileToolkit",
              "functions": ["ReadFile", "WriteFile"],
              "config": { "basePath": "/tmp" },
              "metadata": { "allowDelete": false }
            }
            """;

        // Act
        var reference = JsonSerializer.Deserialize<ToolkitReference>(json);
        var serialized = JsonSerializer.Serialize(reference);
        var roundTripped = JsonSerializer.Deserialize<ToolkitReference>(serialized);

        // Assert
        Assert.NotNull(reference);
        Assert.Equal("FileToolkit", reference.Name);
        Assert.NotNull(reference.Functions);
        Assert.Equal(2, reference.Functions.Count);
        Assert.Contains("ReadFile", reference.Functions);
        Assert.Contains("WriteFile", reference.Functions);
        Assert.True(reference.Config.HasValue);
        Assert.True(reference.Metadata.HasValue);

        Assert.NotNull(roundTripped);
        Assert.Equal("FileToolkit", roundTripped.Name);
    }

    [Fact]
    public void MiddlewareReference_SimpleString_RoundTrip()
    {
        // Arrange
        var reference = new MiddlewareReference { Name = "LoggingMiddleware" };
        var options = new JsonSerializerOptions { WriteIndented = true };

        // Act
        var json = JsonSerializer.Serialize(reference, options);
        var deserialized = JsonSerializer.Deserialize<MiddlewareReference>(json);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal("LoggingMiddleware", deserialized.Name);
        Assert.Null(deserialized.Config);
    }

    [Fact]
    public void MiddlewareReference_ImplicitConversion_FromString()
    {
        // Arrange & Act
        MiddlewareReference reference = "RetryMiddleware";

        // Assert
        Assert.Equal("RetryMiddleware", reference.Name);
    }

    [Fact]
    public void MiddlewareReference_RichSyntax_RoundTrip()
    {
        // Arrange
        var json = """
            {
              "name": "RateLimitMiddleware",
              "config": { "requestsPerMinute": 60 }
            }
            """;

        // Act
        var reference = JsonSerializer.Deserialize<MiddlewareReference>(json);
        var serialized = JsonSerializer.Serialize(reference);
        var roundTripped = JsonSerializer.Deserialize<MiddlewareReference>(serialized);

        // Assert
        Assert.NotNull(reference);
        Assert.Equal("RateLimitMiddleware", reference.Name);
        Assert.True(reference.Config.HasValue);

        Assert.NotNull(roundTripped);
        Assert.Equal("RateLimitMiddleware", roundTripped.Name);
    }

    [Fact]
    public void AgentConfig_WithToolkits_RoundTrip()
    {
        // Arrange
        var config = new AgentConfig
        {
            Name = "TestAgent",
            SystemInstructions = "You are a helpful assistant.",
            Toolkits = new List<ToolkitReference>
            {
                "MathToolkit",
                new ToolkitReference
                {
                    Name = "SearchToolkit",
                    Functions = new List<string> { "WebSearch" }
                }
            }
        };

        // Act
        var json = JsonSerializer.Serialize(config, HPDJsonContext.Default.AgentConfig);
        var deserialized = JsonSerializer.Deserialize<AgentConfig>(json, HPDJsonContext.Default.AgentConfig);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal("TestAgent", deserialized.Name);
        Assert.Equal(2, deserialized.Toolkits.Count);
        Assert.Equal("MathToolkit", deserialized.Toolkits[0].Name);
        Assert.Equal("SearchToolkit", deserialized.Toolkits[1].Name);
    }

    [Fact]
    public void AgentConfig_WithMiddlewares_RoundTrip()
    {
        // Arrange
        var config = new AgentConfig
        {
            Name = "TestAgent",
            Middlewares = new List<MiddlewareReference>
            {
                "LoggingMiddleware",
                new MiddlewareReference { Name = "RetryMiddleware" }
            }
        };

        // Act
        var json = JsonSerializer.Serialize(config, HPDJsonContext.Default.AgentConfig);
        var deserialized = JsonSerializer.Deserialize<AgentConfig>(json, HPDJsonContext.Default.AgentConfig);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(2, deserialized.Middlewares.Count);
        Assert.Equal("LoggingMiddleware", deserialized.Middlewares[0].Name);
        Assert.Equal("RetryMiddleware", deserialized.Middlewares[1].Name);
    }

    [Fact]
    public void AgentConfig_CompleteExample_RoundTrip()
    {
        
        var json = """
            {
              "name": "ResearchAgent",
              "systemInstructions": "You are a research assistant.",
              "toolkits": [
                "MathToolkit",
                { "name": "SearchToolkit" },
                { "name": "FileToolkit", "functions": ["ReadFile"] }
              ],
              "middlewares": [
                "LoggingMiddleware",
                "RetryMiddleware"
              ],
              "collapsing": {
                "enabled": true,
                "neverCollapse": ["MathToolkit"]
              }
            }
            """;

        // Act
        var config = JsonSerializer.Deserialize<AgentConfig>(json, HPDJsonContext.Default.AgentConfig);

        // Assert
        Assert.NotNull(config);
        Assert.Equal("ResearchAgent", config.Name);
        Assert.Equal("You are a research assistant.", config.SystemInstructions);

        // Toolkits
        Assert.Equal(3, config.Toolkits.Count);
        Assert.Equal("MathToolkit", config.Toolkits[0].Name);
        Assert.Equal("SearchToolkit", config.Toolkits[1].Name);
        Assert.Equal("FileToolkit", config.Toolkits[2].Name);
        Assert.Single(config.Toolkits[2].Functions!);
        Assert.Equal("ReadFile", config.Toolkits[2].Functions![0]);

        // Middlewares
        Assert.Equal(2, config.Middlewares.Count);
        Assert.Equal("LoggingMiddleware", config.Middlewares[0].Name);
        Assert.Equal("RetryMiddleware", config.Middlewares[1].Name);

        // Collapsing
        Assert.True(config.Collapsing.Enabled);
        Assert.Contains("MathToolkit", config.Collapsing.NeverCollapse);
    }

    [Fact]
    public void ToolkitReference_StringSyntax_Serialization()
    {
        // Arrange - simple reference should serialize as string
        var reference = new ToolkitReference { Name = "MathToolkit" };

        // Act
        var json = JsonSerializer.Serialize(reference);

        // Assert - should be serialized as simple string
        Assert.Equal("\"MathToolkit\"", json);
    }

    [Fact]
    public void ToolkitReference_RichSyntax_Serialization()
    {
        // Arrange - reference with config should serialize as object
        var reference = new ToolkitReference
        {
            Name = "SearchToolkit",
            Functions = new List<string> { "WebSearch" }
        };

        // Act
        var json = JsonSerializer.Serialize(reference);
        var parsed = JsonDocument.Parse(json);

        // Assert - should be serialized as object
        Assert.Equal(JsonValueKind.Object, parsed.RootElement.ValueKind);
        Assert.Equal("SearchToolkit", parsed.RootElement.GetProperty("name").GetString());
    }

    [Fact]
    public void MiddlewareReference_StringSyntax_Serialization()
    {
        // Arrange - simple reference should serialize as string
        var reference = new MiddlewareReference { Name = "LoggingMiddleware" };

        // Act
        var json = JsonSerializer.Serialize(reference);

        // Assert - should be serialized as simple string
        Assert.Equal("\"LoggingMiddleware\"", json);
    }
}
