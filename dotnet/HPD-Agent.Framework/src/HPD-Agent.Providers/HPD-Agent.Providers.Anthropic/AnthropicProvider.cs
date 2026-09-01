using System;
using System.Collections.Generic;
using System.Threading;
using Anthropic;
using Anthropic.Core;
using HPD.Agent;
using HPD.Agent.Providers;
using HPD.Agent.ErrorHandling;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Providers.Anthropic;

[HpdProvider("anthropic", "Anthropic (Claude)", DocumentationUrl = "https://docs.anthropic.com/")]
[HpdProviderBackend("platform", ProviderAuthenticationKind.ApiKey, IsDefaultBackend = true, IsDefaultAuthentication = true, DefaultSecretKey = "anthropic:ApiKey")]
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
internal class AnthropicProvider : IProvider, IProviderClientFactory<IChatClient>, IProviderSecretAliasProvider
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

    public ProviderClientCredentialBinding ResolveCredentialBinding(ProviderClientBindingDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        return ProviderClientCredentialBinding.ConstructionTime;
    }

    public ValueTask<ProviderClientConstruction<IChatClient>> CreateAsync(
        ProviderClientConstructionContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        var config = context.EffectiveConfig;
        var apiKey = ProviderClientConstructionUtilities.GetRequiredApiKey(context.CredentialBinding);

        // Create the official Anthropic client
        var anthropicClient = new AnthropicClient(new ClientOptions
        {
            ApiKey = apiKey,
            BaseUrl = config.Endpoint?.AbsoluteUri ?? "https://api.anthropic.com"
        });

        const int maxTokens = 4096;
        var chatClient = anthropicClient.AsIChatClient(config.ModelName, maxTokens);

        IChatClient configured = new AnthropicConfiguredChatClient(
            chatClient,
            config.ModelName,
            maxTokens);
        return ValueTask.FromResult(new ProviderClientConstruction<IChatClient>
        {
            Client = configured,
            Owner = ProviderClientConstructionUtilities.Own(configured)
        });
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

    public ProviderValidationResult ValidateConfiguration(EffectiveProviderClientConfig config)
    {
        if (config.Family != ProviderClientFamily.Chat)
            return ProviderValidationResult.Failure("Anthropic supports only chat.");
        if (string.IsNullOrWhiteSpace(config.ModelName))
            return ProviderValidationResult.Failure("Model name is required");

        return ProviderValidationResult.Success();
    }
}
