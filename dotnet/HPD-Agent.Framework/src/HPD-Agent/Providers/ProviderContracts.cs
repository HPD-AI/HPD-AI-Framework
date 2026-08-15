using System;
using System.Collections.Generic;
using HPD.Agent;
using HPD.Agent.ErrorHandling;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Providers;

/// <summary>
/// Base contract for provider packages. Providers describe a vendor, platform,
/// or capability package and opt into individual client families through
/// provider-family interfaces.
/// </summary>
public interface IProvider
{
    /// <summary>
    /// Unique identifier for this provider (for example, "openai", "anthropic", "silero").
    /// Must be lowercase and URL-safe (used in JSON config).
    /// </summary>
    string ProviderKey { get; }

    /// <summary>
    /// Display name for UI purposes (e.g., "OpenAI", "Anthropic Claude").
    /// </summary>
    string DisplayName { get; }

    /// <summary>
    /// Create an error handler for this provider.
    /// </summary>
    IProviderErrorHandler CreateErrorHandler();

    /// <summary>
    /// Get metadata about this provider's capabilities.
    /// </summary>
    ProviderMetadata GetMetadata();

    /// <summary>
    /// Validate provider-specific configuration for a specific client family.
    /// </summary>
    ProviderValidationResult ValidateConfiguration(
        ProviderClientConfig config,
        ProviderClientFamily family);

    /// <summary>
    /// Validate provider-specific configuration asynchronously with live API testing.
    /// Providers that don't support async validation should return null.
    /// </summary>
    Task<ProviderValidationResult>? ValidateConfigurationAsync(
        ProviderClientConfig config,
        ProviderClientFamily family,
        CancellationToken cancellationToken = default)
        => null;
}

public interface IChatClientProvider : IProvider
{
    /// <summary>Creates a Chat client without blocking asynchronous dependencies.</summary>
    /// <param name="config">The resolved provider construction configuration.</param>
    /// <param name="services">Application services available to the provider.</param>
    /// <param name="cancellationToken">Cancels client creation and secret resolution.</param>
    /// <returns>The created Chat client.</returns>
    ValueTask<IChatClient> CreateChatClientAsync(
        ProviderClientConfig config,
        IServiceProvider? services = null,
        CancellationToken cancellationToken = default);
}

public interface ITextToSpeechClientProvider : IProvider
{
    ITextToSpeechClient CreateTextToSpeechClient(
        ProviderClientConfig config,
        IServiceProvider? services = null);
}

public interface ISpeechToTextClientProvider : IProvider
{
    ISpeechToTextClient CreateSpeechToTextClient(
        ProviderClientConfig config,
        IServiceProvider? services = null);
}

public interface IRealtimeClientProvider : IProvider
{
    IRealtimeClient CreateRealtimeClient(
        ProviderClientConfig config,
        IServiceProvider? services = null);
}

public interface IImageGeneratorProvider : IProvider
{
    IImageGenerator CreateImageGenerator(
        ProviderClientConfig config,
        IServiceProvider? services = null);
}

public interface IEmbeddingGeneratorProvider : IProvider
{
    IEmbeddingGenerator CreateEmbeddingGenerator(
        ProviderClientConfig config,
        IServiceProvider? services = null);
}

public interface IHostedFileClientProvider : IProvider
{
    IHostedFileClient CreateHostedFileClient(
        ProviderClientConfig config,
        IServiceProvider? services = null);
}

public interface IEndOfTurnDetectorProvider : IProvider
{
    IEotDetector CreateEndOfTurnDetector(
        ProviderClientConfig config,
        ProviderComponentLifetimeContext context,
        IServiceProvider? services = null);
}

public enum ProviderClientFamily
{
    Chat,
    TextToSpeech,
    SpeechToText,
    Realtime,
    ImageGeneration,
    Embeddings,
    HostedFiles,
    VoiceActivityDetection,
    EndOfTurnDetection
}

public enum ProviderFamilyLifetime
{
    ReusableClient,
    StatefulPerAudioSession,
    StatefulPerRun,
    StatefulPerTurn
}

public sealed record ProviderComponentLifetimeContext(
    string? AgentId = null,
    string? SessionId = null,
    string? ThreadId = null,
    string? ThreadExecutionId = null,
    string? AudioSessionId = null,
    ProviderFamilyLifetime Lifetime = ProviderFamilyLifetime.ReusableClient);

/// <summary>
/// Metadata about a provider's capabilities.
/// </summary>
public sealed class ProviderFamilyDescriptor
{
    public ProviderClientFamily Family { get; init; }
    public ProviderFamilyLifetime Lifetime { get; init; } = ProviderFamilyLifetime.ReusableClient;
    /// <summary>Gets whether the provider binds the model while constructing this client family.</summary>
    public bool BindsModelToClient { get; init; } = true;
    public IReadOnlyList<string>? SupportedModels { get; init; }
    public string? DefaultModelId { get; init; }
    public IReadOnlyDictionary<string, object?>? Capabilities { get; init; }
}

public class ProviderMetadata
{
    public string ProviderKey { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public Uri? DocumentationUri { get; init; }
    public IReadOnlyDictionary<ProviderClientFamily, ProviderFamilyDescriptor> Families { get; init; }
        = new Dictionary<ProviderClientFamily, ProviderFamilyDescriptor>();
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
