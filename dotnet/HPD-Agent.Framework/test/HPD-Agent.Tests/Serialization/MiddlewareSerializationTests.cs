using System.Text.Json;
using HPD.Agent;
using HPD.Agent.Middleware;
using Xunit;

namespace HPD.Agent.Tests.Serialization;

/// <summary>
/// Tests for middleware serialization and MiddlewareFactory.
/// Verifies that MiddlewareReference can be serialized to JSON and deserialized back,
/// and that MiddlewareFactory supports AOT-compatible instantiation.
/// </summary>
public class MiddlewareSerializationTests
{
    #region MiddlewareReference Tests

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
    public void MiddlewareReference_StringSyntax_Serialization()
    {
        // Arrange - simple reference should serialize as string
        var reference = new MiddlewareReference { Name = "LoggingMiddleware" };

        // Act
        var json = JsonSerializer.Serialize(reference);

        // Assert - should be serialized as simple string
        Assert.Equal("\"LoggingMiddleware\"", json);
    }

    [Fact]
    public void MiddlewareReference_RichSyntax_Serialization()
    {
        // Arrange - reference with config should serialize as object
        var configJson = JsonDocument.Parse("""{"timeout": 30}""").RootElement;
        var reference = new MiddlewareReference
        {
            Name = "TimeoutMiddleware",
            Config = configJson
        };

        // Act
        var json = JsonSerializer.Serialize(reference);
        var parsed = JsonDocument.Parse(json);

        // Assert - should be serialized as object
        Assert.Equal(JsonValueKind.Object, parsed.RootElement.ValueKind);
        Assert.Equal("TimeoutMiddleware", parsed.RootElement.GetProperty("name").GetString());
    }

    #endregion

    #region MiddlewareFactory Tests

    [Fact]
    public void MiddlewareFactory_CreateInstance_ReturnsMiddleware()
    {
        // Arrange
        var factory = new MiddlewareFactory(
            Name: "TestMiddleware",
            MiddlewareType: typeof(TestMiddleware),
            CreateInstance: () => new TestMiddleware()
        );

        // Act
        var instance = factory.CreateInstance!();

        // Assert
        Assert.NotNull(instance);
        Assert.IsType<TestMiddleware>(instance);
    }

    [Fact]
    public void MiddlewareFactory_CreateFromConfig_ReturnsConfiguredMiddleware()
    {
        // Arrange
        var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var factory = new MiddlewareFactory(
            Name: "ConfigurableMiddleware",
            MiddlewareType: typeof(ConfigurableTestMiddleware),
            CreateInstance: () => new ConfigurableTestMiddleware(new TestMiddlewareConfig()),
            ConfigType: typeof(TestMiddlewareConfig),
            CreateFromConfig: json =>
            {
                var config = JsonSerializer.Deserialize<TestMiddlewareConfig>(json.GetRawText(), jsonOptions)!;
                return new ConfigurableTestMiddleware(config);
            }
        );

        var configJson = JsonDocument.Parse("""{"MaxRetries": 5, "Enabled": true}""").RootElement;

        // Act
        var instance = factory.CreateFromConfig!(configJson);

        // Assert
        Assert.NotNull(instance);
        Assert.IsType<ConfigurableTestMiddleware>(instance);
        var configuredInstance = (ConfigurableTestMiddleware)instance;
        Assert.Equal(5, configuredInstance.Config.MaxRetries);
        Assert.True(configuredInstance.Config.Enabled);
    }

    [Fact]
    public void MiddlewareFactory_RequiresDI_WhenNoCreateInstance()
    {
        // Arrange
        var factory = new MiddlewareFactory(
            Name: "DIOnlyMiddleware",
            MiddlewareType: typeof(TestMiddleware),
            CreateInstance: null,
            RequiresDI: true
        );

        // Assert
        Assert.True(factory.RequiresDI);
        Assert.Null(factory.CreateInstance);
    }

    [Fact]
    public void MiddlewareFactory_ConfigType_IsSet()
    {
        // Arrange
        var factory = new MiddlewareFactory(
            Name: "ConfigurableMiddleware",
            MiddlewareType: typeof(ConfigurableTestMiddleware),
            CreateInstance: () => new ConfigurableTestMiddleware(new TestMiddlewareConfig()),
            ConfigType: typeof(TestMiddlewareConfig),
            CreateFromConfig: json => new ConfigurableTestMiddleware(
                JsonSerializer.Deserialize<TestMiddlewareConfig>(json.GetRawText())!)
        );

        // Assert
        Assert.Equal(typeof(TestMiddlewareConfig), factory.ConfigType);
    }

    #endregion

    #region AgentConfig with Middlewares Tests

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
    public void AgentConfig_MixedMiddlewareSyntax_Deserializes()
    {
        // Arrange
        var json = """
            {
              "name": "TestAgent",
              "middlewares": [
                "LoggingMiddleware",
                { "name": "RateLimitMiddleware", "config": { "requestsPerMinute": 60 } }
              ]
            }
            """;

        // Act
        var config = JsonSerializer.Deserialize<AgentConfig>(json, HPDJsonContext.Default.AgentConfig);

        // Assert
        Assert.NotNull(config);
        Assert.Equal(2, config.Middlewares.Count);
        Assert.Equal("LoggingMiddleware", config.Middlewares[0].Name);
        Assert.Null(config.Middlewares[0].Config);
        Assert.Equal("RateLimitMiddleware", config.Middlewares[1].Name);
        Assert.True(config.Middlewares[1].Config.HasValue);
    }

    #endregion

    #region Test Helpers

    /// <summary>
    /// Simple test middleware for unit tests.
    /// </summary>
    private class TestMiddleware : IAgentMiddleware
    {
        // Uses default interface implementations
    }

    /// <summary>
    /// Test middleware with configuration for unit tests.
    /// </summary>
    private class ConfigurableTestMiddleware : IAgentMiddleware
    {
        public TestMiddlewareConfig Config { get; }

        public ConfigurableTestMiddleware(TestMiddlewareConfig config)
        {
            Config = config;
        }
    }

    /// <summary>
    /// Test configuration class for middleware.
    /// </summary>
    private class TestMiddlewareConfig
    {
        public int MaxRetries { get; set; } = 3;
        public bool Enabled { get; set; } = false;
    }

    #endregion
}
