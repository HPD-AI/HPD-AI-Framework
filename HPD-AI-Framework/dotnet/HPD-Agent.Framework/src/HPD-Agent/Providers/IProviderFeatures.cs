// HPD-Agent/Providers/IProviderFeatures.cs
using System;
using System.Collections.Generic;
using HPD.Agent.ErrorHandling;
using Microsoft.Extensions.AI; // For IChatClient

namespace HPD.Agent.Providers;

/// <summary>
/// Represents all capabilities provided by a specific LLM provider.
/// Implementations are contributed by provider packages via ModuleInitializer.
/// </summary>
public interface IProviderFeatures
{
    /// <summary>
    /// Unique identifier for this provider (e.g., "openai", "anthropic").
    /// Must be lowercase and URL-safe (used in JSON config).
    /// </summary>
    string ProviderKey { get; }

    /// <summary>
    /// Display name for UI purposes (e.g., "OpenAI", "Anthropic Claude").
    /// </summary>
    string DisplayName { get; }

    /// <summary>
    /// Create a chat client for this provider from configuration.
    /// </summary>
    /// <param name="config">Provider-specific configuration from AgentConfig</param>
    /// <param name="services">Optional service provider for DI</param>
    /// <returns>Configured IChatClient instance</returns>
    IChatClient CreateChatClient(ProviderConfig config, IServiceProvider? services = null);

    /// <summary>
    /// Create an error handler for this provider.
    /// </summary>
    /// <returns>Provider-specific error handler instance</returns>
    IProviderErrorHandler CreateErrorHandler();

    /// <summary>
    /// Get metadata about this provider's capabilities.
    /// </summary>
    /// <returns>Provider metadata including supported features</returns>
    ProviderMetadata GetMetadata();

    /// <summary>
    /// Validate provider-specific configuration (synchronous).
    /// </summary>
    /// <param name="config">Configuration to validate</param>
    /// <returns>Validation result with error messages if invalid</returns>
    ProviderValidationResult ValidateConfiguration(ProviderConfig config);

    /// <summary>
    /// Validate provider-specific configuration asynchronously with live API testing.
    /// This method can perform network requests to validate API keys, check credit balances,
    /// test model availability, etc. Providers that don't support async validation should
    /// return null (default implementation).
    /// </summary>
    /// <param name="config">Configuration to validate</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Validation result, or null if async validation is not supported</returns>
    Task<ProviderValidationResult>? ValidateConfigurationAsync(ProviderConfig config, CancellationToken cancellationToken = default)
        => null; // Default implementation - providers can override
}

/// <summary>
/// Metadata about a provider's capabilities.
/// </summary>
public class ProviderMetadata
{
    public string ProviderKey { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public bool SupportsStreaming { get; init; } = true;
    public bool SupportsFunctionCalling { get; init; } = true;
    public bool SupportsVision { get; init; } = false;
    public bool SupportsAudio { get; init; } = false;
    public int? DefaulTMetadataWindow { get; init; }
    public string? DocumentationUrl { get; init; }
    public Dictionary<string, object>? CustomProperties { get; init; }
}

/// <summary>
/// Result of provider configuration validation.
/// </summary>
public class ProviderValidationResult
{
    public bool IsValid { get; init; }
    public List<string> Errors { get; init; } = new();
    public List<string> Warnings { get; init; } = new();

    public static ProviderValidationResult Success() => new() { IsValid = true };

    public static ProviderValidationResult Failure(params string[] errors) =>
        new() { IsValid = false, Errors = new List<string>(errors) };
}

/// <summary>
/// Optional interface for providers that support advanced features like credit management,
/// usage tracking, or other provider-specific capabilities beyond the core IProviderFeatures.
/// </summary>
public interface IProviderExtendedFeatures : IProviderFeatures
{
    /// <summary>
    /// Check if the provider supports credit/usage management.
    /// </summary>
    bool SupportsCreditManagement => false;

    /// <summary>
    /// Check if the provider supports attribution headers (e.g., HTTP-Referer, X-Title).
    /// </summary>
    bool SupportsAttribution => false;

    /// <summary>
    /// Check if the provider supports model routing/fallbacks.
    /// </summary>
    bool SupportsModelRouting => false;
}
