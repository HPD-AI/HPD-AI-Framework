using System.Text.Json;
using HPD.Agent;
using Xunit;

namespace HPD.Agent.Tests.Serialization;

/// <summary>
/// Tests for config serialization.
/// Verifies that AgentConfig with Harneses and Middlewares can be
/// serialized to JSON and deserialized back.
/// </summary>
public class ConfigSerializationTests
{
    [Fact]
    public void HarnessReference_SimpleString_RoundTrip()
    {
        // Arrange
        var reference = new HarnessReference { Name = "MathHarness" };
        var options = new JsonSerializerOptions { WriteIndented = true };

        // Act
        var json = JsonSerializer.Serialize(reference, options);
        var deserialized = JsonSerializer.Deserialize<HarnessReference>(json);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal("MathHarness", deserialized.Name);
        Assert.Null(deserialized.Functions);
        Assert.Null(deserialized.Config);
        Assert.Null(deserialized.Metadata);
    }

    [Fact]
    public void HarnessReference_ImplicitConversion_FromString()
    {
        // Arrange & Act
        HarnessReference reference = "SearchHarness";

        // Assert
        Assert.Equal("SearchHarness", reference.Name);
    }

    [Fact]
    public void HarnessReference_RichSyntax_RoundTrip()
    {
        // Arrange
        var json = """
            {
              "name": "FileHarness",
              "functions": ["ReadFile", "WriteFile"],
              "config": { "basePath": "/tmp" },
              "metadata": { "allowDelete": false }
            }
            """;

        // Act
        var reference = JsonSerializer.Deserialize<HarnessReference>(json);
        var serialized = JsonSerializer.Serialize(reference);
        var roundTripped = JsonSerializer.Deserialize<HarnessReference>(serialized);

        // Assert
        Assert.NotNull(reference);
        Assert.Equal("FileHarness", reference.Name);
        Assert.NotNull(reference.Functions);
        Assert.Equal(2, reference.Functions.Count);
        Assert.Contains("ReadFile", reference.Functions);
        Assert.Contains("WriteFile", reference.Functions);
        Assert.True(reference.Config.HasValue);
        Assert.True(reference.Metadata.HasValue);

        Assert.NotNull(roundTripped);
        Assert.Equal("FileHarness", roundTripped.Name);
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
    public void AgentConfig_WithHarneses_RoundTrip()
    {
        // Arrange
        var config = new AgentConfig
        {
            Name = "TestAgent",
            SystemInstructions = "You are a helpful assistant.",
            Harneses = new List<HarnessReference>
            {
                "MathHarness",
                new HarnessReference
                {
                    Name = "SearchHarness",
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
        Assert.Equal(2, deserialized.Harneses.Count);
        Assert.Equal("MathHarness", deserialized.Harneses[0].Name);
        Assert.Equal("SearchHarness", deserialized.Harneses[1].Name);
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
              "harnesses": [
                "MathHarness",
                { "name": "SearchHarness" },
                { "name": "FileHarness", "functions": ["ReadFile"] }
              ],
              "middlewares": [
                "LoggingMiddleware",
                "RetryMiddleware"
              ],
              "collapsing": {
                "enabled": true,
                "neverCollapse": ["MathHarness"]
              }
            }
            """;

        // Act
        var config = JsonSerializer.Deserialize<AgentConfig>(json, HPDJsonContext.Default.AgentConfig);

        // Assert
        Assert.NotNull(config);
        Assert.Equal("ResearchAgent", config.Name);
        Assert.Equal("You are a research assistant.", config.SystemInstructions);

        // Harneses
        Assert.Equal(3, config.Harneses.Count);
        Assert.Equal("MathHarness", config.Harneses[0].Name);
        Assert.Equal("SearchHarness", config.Harneses[1].Name);
        Assert.Equal("FileHarness", config.Harneses[2].Name);
        Assert.Single(config.Harneses[2].Functions!);
        Assert.Equal("ReadFile", config.Harneses[2].Functions![0]);

        // Middlewares
        Assert.Equal(2, config.Middlewares.Count);
        Assert.Equal("LoggingMiddleware", config.Middlewares[0].Name);
        Assert.Equal("RetryMiddleware", config.Middlewares[1].Name);

        // Collapsing
        Assert.True(config.Collapsing.Enabled);
        Assert.Contains("MathHarness", config.Collapsing.NeverCollapse);
    }

    [Fact]
    public void HarnessReference_StringSyntax_Serialization()
    {
        // Arrange - simple reference should serialize as string
        var reference = new HarnessReference { Name = "MathHarness" };

        // Act
        var json = JsonSerializer.Serialize(reference);

        // Assert - should be serialized as simple string
        Assert.Equal("\"MathHarness\"", json);
    }

    [Fact]
    public void HarnessReference_RichSyntax_Serialization()
    {
        // Arrange - reference with config should serialize as object
        var reference = new HarnessReference
        {
            Name = "SearchHarness",
            Functions = new List<string> { "WebSearch" }
        };

        // Act
        var json = JsonSerializer.Serialize(reference);
        var parsed = JsonDocument.Parse(json);

        // Assert - should be serialized as object
        Assert.Equal(JsonValueKind.Object, parsed.RootElement.ValueKind);
        Assert.Equal("SearchHarness", parsed.RootElement.GetProperty("name").GetString());
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
