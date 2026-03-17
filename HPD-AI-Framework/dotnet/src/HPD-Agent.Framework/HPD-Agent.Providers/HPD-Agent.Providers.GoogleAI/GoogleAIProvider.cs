using System;
using System.Diagnostics.CodeAnalysis;
using System.Collections.Generic;
using System.Threading;
using GenerativeAI.Microsoft;
using HPD.Agent;
using HPD.Agent.Providers;
using HPD.Agent.ErrorHandling;
using HPD.Agent.Secrets;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Agent.Providers.GoogleAI;

/// <summary>
/// Google AI (Gemini) provider implementation using the Google_GenerativeAI SDK.
/// </summary>
/// <remarks>
/// <para>
/// This provider uses Google's Generative AI SDK:
/// - Google_GenerativeAI for model access
/// - Google_GenerativeAI.Microsoft for IChatClient integration
/// </para>
/// <para>
/// Authentication: API Key (required)
/// </para>
/// </remarks>
internal class GoogleAIProvider : IProviderFeatures
{
    public string ProviderKey => "google-ai";
    public string DisplayName => "Google AI (Gemini)";

    [UnconditionalSuppressMessage("Trimming", "IL2026:Members annotated with 'RequiresUnreferencedCodeAttribute' require dynamic access otherwise can break functionality when trimming application code", Justification = "Provider properly registers AOT-compatible deserializer in provider module")]
    public IChatClient CreateChatClient(ProviderConfig config, IServiceProvider? services = null)
    {
        ArgumentNullException.ThrowIfNull(config);

        // Get secret resolver from services
        var secrets = services?.GetService<ISecretResolver>();
        if (secrets == null)
        {
            throw new InvalidOperationException(
                "ISecretResolver is required for provider initialization. " +
                "Ensure the agent builder is properly configured with secret resolution.");
        }

        // Resolve API key using ISecretResolver
        // Try "google-ai:ApiKey" first, then fallback to "gemini:ApiKey" for compatibility
        string? apiKey = null;
        try
        {
            var apiKeyTask = secrets.RequireAsync("google-ai:ApiKey", "Google AI", config.ApiKey, CancellationToken.None);
            apiKey = apiKeyTask.GetAwaiter().GetResult();
        }
        catch (SecretNotFoundException)
        {
            // Fallback: Try "gemini" as alternative key
            if (config.ApiKey == null)
            {
                var apiKeyTask = secrets.RequireAsync("gemini:ApiKey", "Google AI (Gemini)", null, CancellationToken.None);
                apiKey = apiKeyTask.GetAwaiter().GetResult();
            }
            else
            {
                throw; // Re-throw if explicit config.ApiKey was provided but failed
            }
        }

        string? modelName = config.ModelName;
        if (string.IsNullOrEmpty(modelName))
        {
            throw new InvalidOperationException("For Google AI, the ModelName must be configured.");
        }

        // Create the chat client
        // Note: The GenerativeAIChatClient constructor handles the configuration internally
        // We store the GoogleAIProviderConfig for future use, but the Google_GenerativeAI SDK
        // doesn't currently expose all configuration options in a way that's compatible with
        // our provider model. This is a limitation of the SDK.
        var chatClient = new GenerativeAIChatClient(apiKey, modelName);

        // Apply client factory middleware if provided
        IChatClient finalClient = chatClient;
        if (config.AdditionalProperties?.TryGetValue("ClientFactory", out var factoryObj) == true &&
            factoryObj is Func<IChatClient, IChatClient> clientFactory)
        {
            finalClient = clientFactory(chatClient);
        }

        return finalClient;
    }

    public IProviderErrorHandler CreateErrorHandler()
    {
        return new GoogleAIErrorHandler();
    }

    public ProviderMetadata GetMetadata()
    {
        return new ProviderMetadata
        {
            ProviderKey = ProviderKey,
            DisplayName = DisplayName,
            SupportsStreaming = true,
            SupportsFunctionCalling = true,
            SupportsVision = true,
            DocumentationUrl = "https://ai.google.dev/docs"
        };
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026:Members annotated with 'RequiresUnreferencedCodeAttribute' require dynamic access otherwise can break functionality when trimming application code", Justification = "Provider properly registers AOT-compatible deserializer in provider module")]
    public ProviderValidationResult ValidateConfiguration(ProviderConfig config)
    {
        var errors = new List<string>();

        // Note: API key validation is now deferred to CreateChatClient where ISecretResolver is available
        // This method only validates config structure, not secret resolution
        if (string.IsNullOrEmpty(config.ApiKey))
        {
            errors.Add("API key is required for Google AI. " +
                      "Set it via the apiKey parameter, GOOGLE_AI_API_KEY or GEMINI_API_KEY environment variable, or configuration.");
        }

        if (string.IsNullOrEmpty(config.ModelName))
            errors.Add("Model name is required");

        // Validate Google-specific config if present
        var googleConfig = config.GetTypedProviderConfig<GoogleAIProviderConfig>();
        if (googleConfig != null)
        {
            // Validate Temperature range
            if (googleConfig.Temperature.HasValue &&
                (googleConfig.Temperature.Value < 0 || googleConfig.Temperature.Value > 2))
            {
                errors.Add("Temperature must be between 0 and 2");
            }

            // Validate TopP range
            if (googleConfig.TopP.HasValue &&
                (googleConfig.TopP.Value < 0 || googleConfig.TopP.Value > 1))
            {
                errors.Add("TopP must be between 0 and 1");
            }

            // Validate TopK
            if (googleConfig.TopK.HasValue && googleConfig.TopK.Value < 0)
            {
                errors.Add("TopK must be a positive integer");
            }

            // Validate CandidateCount
            if (googleConfig.CandidateCount.HasValue && googleConfig.CandidateCount.Value != 1)
            {
                errors.Add("CandidateCount currently only supports a value of 1");
            }

            // Validate ResponseMimeType with ResponseSchema
            if (!string.IsNullOrEmpty(googleConfig.ResponseSchema) &&
                googleConfig.ResponseMimeType != "application/json")
            {
                errors.Add("When ResponseSchema is set, ResponseMimeType must be 'application/json'");
            }

            // Validate mutual exclusivity of ResponseSchema and ResponseJsonSchema
            if (!string.IsNullOrEmpty(googleConfig.ResponseSchema) &&
                !string.IsNullOrEmpty(googleConfig.ResponseJsonSchema))
            {
                errors.Add("ResponseSchema and ResponseJsonSchema cannot both be set");
            }
        }

        return errors.Count > 0
            ? ProviderValidationResult.Failure(errors.ToArray())
            : ProviderValidationResult.Success();
    }
}
