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

[HpdProvider("anthropic", "Anthropic (Claude)", DocumentationUrl = "https://docs.anthropic.com/")]
[HpdProviderFamily(ProviderClientFamily.Chat)]
[HpdProviderPayload(
    ProviderClientFamily.Chat,
    ProviderPayloadKind.Configuration,
    typeof(AnthropicProviderConfig),
    typeof(AnthropicJsonContext))]
[HpdProviderPayload(
    ProviderClientFamily.Chat,
    ProviderPayloadKind.OperationOptions,
    typeof(AnthropicChatRequestOptions),
    typeof(AnthropicJsonContext))]
[HpdProviderSecretAlias("anthropic:ApiKey", "ANTHROPIC_API_KEY")]
internal class AnthropicProvider : IChatClientProvider, IProviderSecretAliasProvider
{
    public string ProviderKey => "anthropic";
    public string DisplayName => "Anthropic (Claude)";

    /// <summary>
    /// Runtime secret aliases (parallel to the <c>[HpdProviderSecretAlias]</c> manifest attribute)
    /// so that explicitly-registered providers can resolve secrets without a generated composition.
    /// </summary>
    public IReadOnlyList<ProviderSecretAliasRegistration> SecretAliases { get; } =
        new ProviderSecretAliasRegistration[]
        {
            new("anthropic:ApiKey", new[] { "ANTHROPIC_API_KEY" }),
        };

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

        var maxTokens = (config as ChatClientConfig)?.MaxOutputTokens ?? 4096;
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
