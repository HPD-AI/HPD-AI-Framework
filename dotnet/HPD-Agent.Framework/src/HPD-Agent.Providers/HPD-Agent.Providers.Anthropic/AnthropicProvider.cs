using System;
using System.Collections.Generic;
using System.Threading;
using Anthropic;
using Anthropic.Core;
using HPD.Agent;
using HPD.Agent.Providers;
using HPD.Agent.ErrorHandling;
using HPD.Agent.Secrets;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Agent.Providers.Anthropic;

internal class AnthropicProvider : IChatClientProvider
{
    public string ProviderKey => "anthropic";
    public string DisplayName => "Anthropic (Claude)";

    public IChatClient CreateChatClient(ClientProviderConfig config, IServiceProvider? services = null)
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
        var anthropicClient = new AnthropicClient(new ClientOptions
        {
            ApiKey = apiKey,
            BaseUrl = config.Endpoint ?? "https://api.anthropic.com"
        });

        // Get config for max tokens
        var anthropicConfig = config.GetProviderConfig<AnthropicProviderConfig>();
        var maxTokens = anthropicConfig?.MaxTokens ?? 4096;

        // Note: Most configuration (temperature, topP, thinking, etc.) is applied
        // via ChatOptions when calling CompleteAsync/CompleteChatAsync.
        // The AnthropicProviderConfig is stored and can be accessed to build ChatOptions
        // with RawRepresentationFactory for advanced features.

        return anthropicClient.AsIChatClient(config.ModelName, maxTokens);
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
            DocumentationUri = new Uri("https://docs.anthropic.com/"),
            Families = new Dictionary<ProviderClientFamily, ProviderFamilyDescriptor>
            {
                [ProviderClientFamily.Chat] = new()
                {
                    Family = ProviderClientFamily.Chat,
                    Capabilities = new Dictionary<string, object?>
                    {
                        ["SupportsStreaming"] = true,
                        ["SupportsFunctionCalling"] = true,
                        ["SupportsVision"] = true,
                        ["DefaultMetadataWindow"] = 200000
                    }
                }
            }
        };
    }

    public ProviderValidationResult ValidateConfiguration(ClientProviderConfig config, ProviderClientFamily family)
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
        var anthropicConfig = config.GetProviderConfig<AnthropicProviderConfig>();
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
