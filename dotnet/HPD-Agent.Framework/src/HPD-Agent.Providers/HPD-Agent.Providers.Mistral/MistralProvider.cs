using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using HPD.Agent.ErrorHandling;
using HPD.Agent.Providers;
using HPD.Agent.Secrets;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Agent.Providers.Mistral;

/// <summary>
/// Mistral provider implementation using the generated Mistral SDK and Microsoft.Extensions.AI.
/// </summary>
[HpdProvider("mistral", "Mistral")]
[HpdProviderFamily(ProviderClientFamily.Chat)]
[HpdProviderPayload(ProviderClientFamily.Chat, ProviderPayloadKind.Configuration, typeof(MistralProviderConfig), typeof(MistralJsonContext))]
[HpdProviderPayload(ProviderClientFamily.Chat, ProviderPayloadKind.OperationOptions, typeof(MistralChatRequestOptions), typeof(MistralJsonContext))]
[HpdProviderSecretAlias("mistral:ApiKey", "MISTRAL_API_KEY")]
internal class MistralProvider : IChatClientProvider, IProviderSecretAliasProvider
{
    public string ProviderKey => "mistral";
    public string DisplayName => "Mistral";

    /// <summary>
    /// Runtime secret aliases (parallel to the <c>[HpdProviderSecretAlias]</c> manifest attribute)
    /// so that explicitly-registered providers can resolve secrets without a generated composition.
    /// </summary>
    public IReadOnlyList<ProviderSecretAliasRegistration> SecretAliases { get; } =
        new ProviderSecretAliasRegistration[]
        {
            new("mistral:ApiKey", new[] { "MISTRAL_API_KEY" }),
        };

    [UnconditionalSuppressMessage("Trimming", "IL2026:Members annotated with 'RequiresUnreferencedCodeAttribute' require dynamic access otherwise can break functionality when trimming application code", Justification = "Provider registers AOT-compatible deserializer in provider module")]
    public async ValueTask<IChatClient> CreateChatClientAsync(ProviderClientConfig config, IServiceProvider? services = null, CancellationToken cancellationToken = default)
    {
        var secrets = services?.GetService<ISecretResolver>();
        if (secrets == null)
        {
            throw new InvalidOperationException(
                "ISecretResolver is required for provider initialization. " +
                "Ensure the agent builder is properly configured with secret resolution.");
        }

        string apiKey = await secrets.RequireAsync("mistral:ApiKey", "Mistral", config.ApiKey, cancellationToken).ConfigureAwait(false);

        string? modelName = config.ModelName;
        if (string.IsNullOrWhiteSpace(modelName))
            throw new InvalidOperationException("For Mistral, the ModelName must be configured.");

        var client = new global::Mistral.MistralClient(authorizations:
        [
            new global::Mistral.EndPointAuthorization
            {
                Type = "ApiKey",
                SchemeId = "ApiKey",
                Location = "Header",
                Name = "Bearer",
                Value = apiKey
            }
        ]);

        return new MistralConfiguredChatClient(client, modelName);
    }

    public IProviderErrorHandler CreateErrorHandler()
    {
        return new MistralErrorHandler();
    }

    public ProviderMetadata GetMetadata()
    {
        return new ProviderMetadata
        {
            ProviderKey = ProviderKey,
            DisplayName = DisplayName,
            DocumentationUri = new Uri("https://docs.mistral.ai/"),
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
                        ["SupportsAudioInput"] = true
                    }
                }
            }
        };
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026:Members annotated with 'RequiresUnreferencedCodeAttribute' require dynamic access otherwise can break functionality when trimming application code", Justification = "Provider registers AOT-compatible deserializer in provider module")]
    public ProviderValidationResult ValidateConfiguration(ProviderClientConfig config, ProviderClientFamily family)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(config.ModelName))
            errors.Add("Model name is required for Mistral");

        return errors.Count > 0
            ? ProviderValidationResult.Failure(errors.ToArray())
            : ProviderValidationResult.Success();
    }

    private sealed class MistralConfiguredChatClient : IChatClient
    {
        private readonly IChatClient _innerClient;
        private readonly string _modelName;
        private ChatClientMetadata? _metadata;

        public MistralConfiguredChatClient(global::Mistral.MistralClient innerClient, string modelName)
        {
            _innerClient = innerClient ?? throw new ArgumentNullException(nameof(innerClient));
            _modelName = modelName ?? throw new ArgumentNullException(nameof(modelName));
        }

        public ChatClientMetadata Metadata =>
            _metadata ??= new ChatClientMetadata("mistral", defaultModelId: _modelName);

        public void Dispose() => _innerClient.Dispose();

        public object? GetService(Type serviceType, object? serviceKey = null)
        {
            if (serviceType == typeof(ChatClientMetadata))
                return Metadata;

            return _innerClient.GetService(serviceType, serviceKey);
        }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => _innerClient.GetResponseAsync(messages, PrepareOptions(options), cancellationToken);

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => _innerClient.GetStreamingResponseAsync(messages, PrepareOptions(options), cancellationToken);

        private ChatOptions PrepareOptions(ChatOptions? options)
        {
            var merged = options?.Clone() ?? new ChatOptions();
            if (string.IsNullOrWhiteSpace(merged.ModelId))
                merged.ModelId = _modelName;

            MistralChatRequestOptionKeys.ApplyRawRequestOptions(merged);

            return merged;
        }
    }
}
