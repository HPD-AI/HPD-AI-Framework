using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using Anthropic;
using Anthropic.Models.Messages;
using HPD.Agent;
using HPD.Agent.Providers;
using HPD.Agent.ErrorHandling;
using HPD.Agent.Secrets;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Agent.Providers.Anthropic;

internal class AnthropicProvider : IProviderFeatures
{
    public string ProviderKey => "anthropic";
    public string DisplayName => "Anthropic (Claude)";

    [UnconditionalSuppressMessage("Trimming", "IL2026:Members annotated with 'RequiresUnreferencedCodeAttribute' require dynamic access otherwise can break functionality when trimming application code", Justification = "Provider properly registers AOT-compatible deserializer in AnthropicProviderModule")]
    public IChatClient CreateChatClient(ProviderConfig config, IServiceProvider? services = null)
    {
        // Get secret resolver from services
        var secrets = services?.GetService<ISecretResolver>();
        if (secrets == null)
        {
            throw new InvalidOperationException(
                "ISecretResolver is required for provider initialization. " +
                "Ensure the agent builder is properly configured with secret resolution.");
        }

        // Resolve API key using ISecretResolver
        var apiKeyTask = secrets.RequireAsync("anthropic:ApiKey", "Anthropic", config.ApiKey, CancellationToken.None);
        string apiKey = apiKeyTask.GetAwaiter().GetResult();

        // Create the official Anthropic client
        var anthropicClient = new AnthropicClient
        {
            ApiKey = apiKey,
            BaseUrl = config.Endpoint ?? "https://api.anthropic.com"
        };

        // Get config for max tokens
        var anthropicConfig = config.GetTypedProviderConfig<AnthropicProviderConfig>();
        var maxTokens = anthropicConfig?.MaxTokens ?? 4096;

        // Use the SDK's built-in AsIChatClient extension method
        IChatClient chatClient = anthropicClient.AsIChatClient(config.ModelName, maxTokens);

        // Wrap with schema-fixing client to work around Anthropic SDK bug
        // The SDK has a bug where tool schemas are malformed (properties at top level
        // instead of nested under "properties" key). Our wrapper intercepts tools and
        // creates properly formatted Tool objects that bypass the buggy transformation.
        // See AnthropicSchemaFixingChatClient.cs for details.
        chatClient = new AnthropicSchemaFixingChatClient(chatClient);

        // Note: Most configuration (temperature, topP, thinking, etc.) is applied
        // via ChatOptions when calling CompleteAsync/CompleteChatAsync.
        // The AnthropicProviderConfig is stored and can be accessed to build ChatOptions
        // with RawRepresentationFactory for advanced features.

        // Apply client factory middleware if provided
        if (config.AdditionalProperties?.TryGetValue("ClientFactory", out var factoryObj) == true &&
            factoryObj is Func<IChatClient, IChatClient> clientFactory)
        {
            chatClient = clientFactory(chatClient);
        }

        return chatClient;
    }

    public IProviderErrorHandler CreateErrorHandler()
    {
        return new AnthropicErrorHandler();
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
            DefaulTMetadataWindow = 200000, // Claude 3.5 Sonnet
            DocumentationUrl = "https://docs.anthropic.com/"
        };
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026:Members annotated with 'RequiresUnreferencedCodeAttribute' require dynamic access otherwise can break functionality when trimming application code", Justification = "Provider properly registers AOT-compatible deserializer in AnthropicProviderModule")]
    public ProviderValidationResult ValidateConfiguration(ProviderConfig config)
    {
        // Note: API key validation is now deferred to CreateChatClient where ISecretResolver is available
        // This method only validates config structure, not secret resolution
        if (string.IsNullOrEmpty(config.ApiKey))
        {
            return ProviderValidationResult.Failure("API key is required for Anthropic. " +
                "Set it via the apiKey parameter, ANTHROPIC_API_KEY environment variable, or configuration.");
        }

        if (string.IsNullOrEmpty(config.ModelName))
            return ProviderValidationResult.Failure("Model name is required");

        // Validate Anthropic-specific config if present
        var anthropicConfig = config.GetTypedProviderConfig<AnthropicProviderConfig>();
        if (anthropicConfig != null)
        {
            if (anthropicConfig.ThinkingBudgetTokens.HasValue && anthropicConfig.ThinkingBudgetTokens.Value < 1024)
            {
                return ProviderValidationResult.Failure("Thinking budget tokens must be at least 1024");
            }

            if (anthropicConfig.MaxTokens <= 0)
            {
                return ProviderValidationResult.Failure("MaxTokens must be greater than 0");
            }

            if (anthropicConfig.EnablePromptCaching && anthropicConfig.PromptCacheTTLMinutes.HasValue)
            {
                if (anthropicConfig.PromptCacheTTLMinutes < 1 || anthropicConfig.PromptCacheTTLMinutes > 60)
                {
                    return ProviderValidationResult.Failure("PromptCacheTTLMinutes must be between 1 and 60 minutes");
                }
            }
        }

        return ProviderValidationResult.Success();
    }
}
