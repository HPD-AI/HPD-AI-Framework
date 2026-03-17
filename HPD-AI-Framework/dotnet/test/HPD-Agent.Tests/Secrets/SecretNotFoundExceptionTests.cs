// Copyright (c) 2025 Einstein Essibu. All rights reserved.

using HPD.Agent.Secrets;
using Xunit;

namespace HPD.Agent.Tests.Secrets;

/// <summary>
/// Unit tests for SecretNotFoundException.
/// Tests error message formatting, key/display name properties, and naming conventions.
/// </summary>
public class SecretNotFoundExceptionTests
{
    // ============================================
    // Basic Exception Tests
    // ============================================

    [Fact]
    public void Constructor_SetsPropertiesCorrectly()
    {
        // Arrange & Act
        var exception = new SecretNotFoundException(
            "Test error message",
            "openai:ApiKey",
            "OpenAI API Key");

        // Assert
        Assert.Equal("Test error message", exception.Message);
        Assert.Equal("openai:ApiKey", exception.Key);
        Assert.Equal("OpenAI API Key", exception.DisplayName);
    }

    [Fact]
    public void Constructor_IsException_CanBeCaught()
    {
        // Arrange
        var exception = new SecretNotFoundException(
            "Test message",
            "test:Key",
            "Test Key");

        // Act & Assert
        Assert.IsAssignableFrom<Exception>(exception);
    }

    // ============================================
    // Error Message Format Tests (from RequireAsync)
    // ============================================

    [Fact]
    public async Task RequireAsync_ThrowsException_WithFormattedMessage()
    {
        // Arrange
        var resolver = new EmptySecretResolver();

        // Act & Assert
        var exception = await Assert.ThrowsAsync<SecretNotFoundException>(
            async () => await resolver.RequireAsync("stripe:ApiKey", "Stripe API Key"));

        Assert.Contains("Required secret 'Stripe API Key'", exception.Message);
        Assert.Contains("key: 'stripe:ApiKey'", exception.Message);
        Assert.Contains("environment variables", exception.Message);
        Assert.Contains("configuration file", exception.Message);
        Assert.Contains("secret vault", exception.Message);
    }

    [Fact]
    public async Task RequireAsync_IncludesDisplayNameInMessage()
    {
        // Arrange
        var resolver = new EmptySecretResolver();

        // Act & Assert
        var exception = await Assert.ThrowsAsync<SecretNotFoundException>(
            async () => await resolver.RequireAsync("azure-ai:Endpoint", "Azure AI Endpoint"));

        Assert.Contains("Azure AI Endpoint", exception.Message);
    }

    [Fact]
    public async Task RequireAsync_IncludesKeyInMessage()
    {
        // Arrange
        var resolver = new EmptySecretResolver();

        // Act & Assert
        var exception = await Assert.ThrowsAsync<SecretNotFoundException>(
            async () => await resolver.RequireAsync("openai:ApiKey", "OpenAI API Key"));

        Assert.Contains("openai:ApiKey", exception.Message);
    }

    // ============================================
    // Resolution Options in Message Tests
    // ============================================

    [Fact]
    public async Task RequireAsync_MessageIncludesEnvironmentVariableOption()
    {
        // Arrange
        var resolver = new EmptySecretResolver();

        // Act & Assert
        var exception = await Assert.ThrowsAsync<SecretNotFoundException>(
            async () => await resolver.RequireAsync("test:Key", "Test Key"));

        Assert.Contains("environment variables", exception.Message);
    }

    [Fact]
    public async Task RequireAsync_MessageIncludesConfigurationFileOption()
    {
        // Arrange
        var resolver = new EmptySecretResolver();

        // Act & Assert
        var exception = await Assert.ThrowsAsync<SecretNotFoundException>(
            async () => await resolver.RequireAsync("test:Key", "Test Key"));

        Assert.Contains("configuration file", exception.Message);
    }

    [Fact]
    public async Task RequireAsync_MessageIncludesSecretVaultOption()
    {
        // Arrange
        var resolver = new EmptySecretResolver();

        // Act & Assert
        var exception = await Assert.ThrowsAsync<SecretNotFoundException>(
            async () => await resolver.RequireAsync("test:Key", "Test Key"));

        Assert.Contains("secret vault", exception.Message);
    }

    // ============================================
    // PascalCase to snake_case Conversion Tests
    // ============================================

    [Theory]
    [InlineData("openai:ApiKey", "OPENAI_API_KEY")]
    [InlineData("stripe:ApiKey", "STRIPE_API_KEY")]
    [InlineData("slack:BotToken", "SLACK_BOT_TOKEN")]
    [InlineData("huggingface:ApiKey", "HUGGINGFACE_API_KEY")]
    public async Task ExceptionScenario_KeyConversionToSnakeCase(string key, string expectedEnvVar)
    {
        // This test validates the naming convention documented in error messages
        // by checking that EnvironmentSecretResolver follows the same pattern

        // Arrange
        Environment.SetEnvironmentVariable(expectedEnvVar, "test-value");
        var envResolver = new EnvironmentSecretResolver();

        try
        {
            // Act
            var result = await envResolver.ResolveAsync(key);

            // Assert - the environment resolver should find it using snake_case conversion
            Assert.NotNull(result);
            Assert.Equal("test-value", result.Value.Value);
        }
        finally
        {
            Environment.SetEnvironmentVariable(expectedEnvVar, null);
        }
    }

    // ============================================
    // Hyphenated Scope Handling Tests
    // ============================================

    [Theory]
    [InlineData("azure-ai:ApiKey", "AZURE_AI_API_KEY")]
    [InlineData("azure-ai:Endpoint", "AZURE_AI_ENDPOINT")]
    [InlineData("my-custom-service:ApiKey", "MY_CUSTOM_SERVICE_API_KEY")]
    public async Task ExceptionScenario_HyphenatedScopeConversion(string key, string expectedEnvVar)
    {
        // Validates that hyphenated scopes are converted to underscores

        // Arrange
        Environment.SetEnvironmentVariable(expectedEnvVar, "test-value");
        var envResolver = new EnvironmentSecretResolver();

        try
        {
            // Act
            var result = await envResolver.ResolveAsync(key);

            // Assert
            Assert.NotNull(result);
            Assert.Equal("test-value", result.Value.Value);
        }
        finally
        {
            Environment.SetEnvironmentVariable(expectedEnvVar, null);
        }
    }

    // ============================================
    // Exception Properties Tests
    // ============================================

    [Fact]
    public async Task Exception_PreservesKeyProperty()
    {
        // Arrange
        var resolver = new EmptySecretResolver();

        // Act & Assert
        var exception = await Assert.ThrowsAsync<SecretNotFoundException>(
            async () => await resolver.RequireAsync("custom:Secret", "Custom Secret"));

        Assert.Equal("custom:Secret", exception.Key);
    }

    [Fact]
    public async Task Exception_PreservesDisplayNameProperty()
    {
        // Arrange
        var resolver = new EmptySecretResolver();

        // Act & Assert
        var exception = await Assert.ThrowsAsync<SecretNotFoundException>(
            async () => await resolver.RequireAsync("custom:Secret", "My Custom Secret"));

        Assert.Equal("My Custom Secret", exception.DisplayName);
    }

    [Fact]
    public async Task Exception_MultipleScenarios_DifferentMessages()
    {
        // Arrange
        var resolver = new EmptySecretResolver();

        // Act
        var exception1 = await Assert.ThrowsAsync<SecretNotFoundException>(
            async () => await resolver.RequireAsync("openai:ApiKey", "OpenAI API Key"));

        var exception2 = await Assert.ThrowsAsync<SecretNotFoundException>(
            async () => await resolver.RequireAsync("stripe:ApiKey", "Stripe API Key"));

        // Assert - messages should be different based on the secret
        Assert.NotEqual(exception1.Message, exception2.Message);
        Assert.Contains("OpenAI API Key", exception1.Message);
        Assert.Contains("Stripe API Key", exception2.Message);
    }

    // ============================================
    // Integration Tests
    // ============================================

    [Fact]
    public async Task RealWorldScenario_MissingOpenAIKey()
    {
        // Arrange
        var resolver = new ChainedSecretResolver(
            new ExplicitSecretResolver(),
            new EnvironmentSecretResolver());

        // Act & Assert
        var exception = await Assert.ThrowsAsync<SecretNotFoundException>(
            async () => await resolver.RequireAsync("openai:ApiKey", "OpenAI API Key"));

        Assert.Equal("openai:ApiKey", exception.Key);
        Assert.Equal("OpenAI API Key", exception.DisplayName);
        Assert.Contains("Required secret 'OpenAI API Key'", exception.Message);
    }

    [Fact]
    public async Task RealWorldScenario_MissingAzureEndpoint()
    {
        // Arrange
        var resolver = new ChainedSecretResolver(
            new ExplicitSecretResolver(),
            new EnvironmentSecretResolver());

        // Act & Assert
        var exception = await Assert.ThrowsAsync<SecretNotFoundException>(
            async () => await resolver.RequireAsync("azure-ai:Endpoint", "Azure AI Endpoint"));

        Assert.Equal("azure-ai:Endpoint", exception.Key);
        Assert.Equal("Azure AI Endpoint", exception.DisplayName);
    }

    // ============================================
    // Helper Classes
    // ============================================

    private class EmptySecretResolver : ISecretResolver
    {
        public ValueTask<ResolvedSecret?> ResolveAsync(string key, CancellationToken cancellationToken = default)
        {
            return new ValueTask<ResolvedSecret?>((ResolvedSecret?)null);
        }
    }
}
