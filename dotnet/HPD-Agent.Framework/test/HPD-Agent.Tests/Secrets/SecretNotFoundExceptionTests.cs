// Copyright (c) 2025 Einstein Essibu. All rights reserved.

using HPD.Agent.Secrets;
using HPD.Agent.Providers;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace HPD.Agent.Tests.Secrets;

/// <summary>
/// Unit tests for SecretNotFoundException.
/// Tests error message formatting, key/display name properties, and explicit secret registration.
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
    // Explicit environment registration tests
    // ============================================

    [Theory]
    [InlineData("test-openai:ApiKey", "TEST_OPENAI_API_KEY")]
    [InlineData("test-huggingface:ApiKey", "TEST_HUGGINGFACE_API_KEY")]
    public async Task EnvironmentResolver_UsesRegisteredCanonicalEnvVar(string key, string expectedEnvVar)
    {
        // Arrange
        ProviderContributionRegistry.RegisterSecretAlias(key, expectedEnvVar);
        System.Environment.SetEnvironmentVariable(expectedEnvVar, "test-value");
        var envResolver = new EnvironmentSecretResolver();

        try
        {
            // Act
            var result = await envResolver.ResolveAsync(key);

            // Assert
            Assert.NotNull(result);
            Assert.Equal("test-value", result.Value.Value);
            Assert.Equal($"env:{expectedEnvVar}", result.Value.Source);
        }
        finally
        {
            System.Environment.SetEnvironmentVariable(expectedEnvVar, null);
        }
    }

    [Fact]
    public async Task EnvironmentResolver_DoesNotInferEnvVarNames()
    {
        // Arrange
        System.Environment.SetEnvironmentVariable("MY_CUSTOM_SERVICE_API_KEY", "test-value");
        var envResolver = new EnvironmentSecretResolver();

        try
        {
            // Act
            var result = await envResolver.ResolveAsync("my-custom-service:ApiKey");

            // Assert
            Assert.Null(result);
        }
        finally
        {
            System.Environment.SetEnvironmentVariable("MY_CUSTOM_SERVICE_API_KEY", null);
        }
    }

    [Fact]
    public async Task EnvironmentResolver_RequiresCanonicalSecretKeyCasing()
    {
        // Arrange
        ProviderContributionRegistry.RegisterSecretAlias("openai:ApiKey", "TEST_OPENAI_API_KEY_CANONICAL");
        System.Environment.SetEnvironmentVariable("TEST_OPENAI_API_KEY_CANONICAL", "test-value");
        var envResolver = new EnvironmentSecretResolver();

        try
        {
            // Act
            var result = await envResolver.ResolveAsync("OpenAI:ApiKey");

            // Assert
            Assert.Null(result);
        }
        finally
        {
            System.Environment.SetEnvironmentVariable("TEST_OPENAI_API_KEY_CANONICAL", null);
        }
    }

    [Fact]
    public async Task ExplicitResolver_RequiresCanonicalSecretKeyCasing()
    {
        // Arrange
        var resolver = new ExplicitSecretResolver(new Dictionary<string, string>
        {
            ["openai:ApiKey"] = "test-value"
        });

        // Act
        var result = await resolver.ResolveAsync("OpenAI:ApiKey");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task ConfigurationResolver_UsesCanonicalProviderSection()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Providers:openrouter:ApiKey"] = "test-value"
            })
            .Build();
        var resolver = new ConfigurationSecretResolver(configuration);

        // Act
        var result = await resolver.ResolveAsync("openrouter:ApiKey");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("test-value", result.Value.Value);
        Assert.Equal("config:Providers:openrouter:ApiKey", result.Value.Source);
    }

    [Fact]
    public async Task ConfigurationResolver_DoesNotUseCapitalizedProviderSection()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Providers:Openrouter:ApiKey"] = "test-value"
            })
            .Build();
        var resolver = new ConfigurationSecretResolver(configuration);

        // Act
        var result = await resolver.ResolveAsync("openrouter:ApiKey");

        // Assert
        Assert.Null(result);
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
