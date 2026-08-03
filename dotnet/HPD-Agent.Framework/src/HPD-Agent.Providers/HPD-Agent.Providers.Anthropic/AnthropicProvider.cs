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

    public async ValueTask<IChatClient> CreateChatClientAsync(ProviderClientConfig config, IServiceProvider? services = null, CancellationToken cancellationToken = default)
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
        string apiKey = await secrets.RequireAsync("anthropic:ApiKey", "Anthropic", config.ApiKey, cancellationToken).ConfigureAwait(false);

        // Create the official Anthropic client
        var anthropicClient = new AnthropicClient(new ClientOptions
        {
            ApiKey = apiKey,
            BaseUrl = config.Endpoint ?? "https://api.anthropic.com"
        });

        var maxTokens = config.ChatDefaults?.MaxOutputTokens
            ?? config.DefaultMicrosoftChatOptions?.MaxOutputTokens
            ?? 4096;
        var chatClient = anthropicClient.AsIChatClient(config.ModelName, maxTokens);

        return new AnthropicConfiguredChatClient(
            chatClient,
            config.ModelName,
            maxTokens);
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

    public ProviderValidationResult ValidateConfiguration(ProviderClientConfig config, ProviderClientFamily family)
    {
        // Note: API key validation is now deferred to CreateChatClient where ISecretResolver is available
        // This method only validates config structure, not secret resolution

        if (string.IsNullOrEmpty(config.ModelName))
            return ProviderValidationResult.Failure("Model name is required");

        return ProviderValidationResult.Success();
    }
}
