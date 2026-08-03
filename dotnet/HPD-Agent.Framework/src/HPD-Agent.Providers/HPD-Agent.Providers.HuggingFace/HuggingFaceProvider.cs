using System;
using System.Diagnostics.CodeAnalysis;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HPD.Agent;
using HPD.Agent.Providers;
using HPD.Agent.ErrorHandling;
using HPD.Agent.Secrets;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Agent.Providers.HuggingFace;

/// <summary>
/// HuggingFace Serverless Inference API provider implementation.
/// Supports text generation models hosted on HuggingFace's Inference API.
/// </summary>
/// <remarks>
/// <para>
/// This provider uses the HuggingFace Serverless Inference API which provides:
/// - Free access to thousands of models
/// - Automatic model loading and caching
/// - Rate limiting based on your account tier
/// - Support for various model architectures (LLMs, code models, etc.)
/// </para>
/// <para>
/// Supported model types:
/// - Text generation models (GPT, LLaMA, Mistral, etc.)
/// - Instruction-tuned models (chat/instruct variants)
/// - Code generation models (StarCoder, CodeLLaMA, etc.)
/// </para>
/// <para>
/// Authentication:
/// - Requires a HuggingFace API token
/// - Get your token from: https://huggingface.co/settings/tokens
/// </para>
/// </remarks>
[HpdProvider("huggingface", "Hugging Face")]
[HpdProviderFamily(ProviderClientFamily.Chat)]
[HpdProviderPayload(ProviderClientFamily.Chat, ProviderPayloadKind.Configuration, typeof(HuggingFaceProviderConfig), typeof(HuggingFaceJsonContext))]
[HpdProviderPayload(ProviderClientFamily.Chat, ProviderPayloadKind.OperationOptions, typeof(HuggingFaceChatRequestOptions), typeof(HuggingFaceJsonContext))]
[HpdProviderSecretAlias("huggingface:ApiKey", "HUGGINGFACE_API_KEY")]
internal class HuggingFaceProvider : IChatClientProvider
{
    private static readonly Uri DefaultEndpoint = new("https://router.huggingface.co/");

    public string ProviderKey => "huggingface";
    public string DisplayName => "Hugging Face";

    [UnconditionalSuppressMessage("Trimming", "IL2026:Members annotated with 'RequiresUnreferencedCodeAttribute' require dynamic access otherwise can break functionality when trimming application code", Justification = "Provider properly registers AOT-compatible deserializer in provider module")]
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
        string apiKey = await secrets.RequireAsync("huggingface:ApiKey", "Hugging Face", config.ApiKey, cancellationToken).ConfigureAwait(false);

        string? modelName = config.ModelName;
        if (string.IsNullOrEmpty(modelName))
        {
            throw new InvalidOperationException("For HuggingFace, the ModelName (repository ID) must be configured.");
        }

        var endpoint = string.IsNullOrWhiteSpace(config.Endpoint)
            ? DefaultEndpoint
            : new Uri(config.Endpoint, UriKind.Absolute);

        var client = new global::HuggingFace.HuggingFaceInferenceClient(apiKey, baseUri: endpoint);
        return new HuggingFaceConfiguredChatClient(client, modelName);
    }

    public IProviderErrorHandler CreateErrorHandler()
    {
        return new HuggingFaceErrorHandler();
    }

    public ProviderMetadata GetMetadata()
    {
        return new ProviderMetadata
        {
            ProviderKey = ProviderKey,
            DisplayName = DisplayName,
            DocumentationUri = new Uri("https://huggingface.co/docs/api-inference/index"),
            Families = new Dictionary<ProviderClientFamily, ProviderFamilyDescriptor>
            {
                [ProviderClientFamily.Chat] = new()
                {
                    Family = ProviderClientFamily.Chat,
                    Capabilities = new Dictionary<string, object?>
                    {
                        ["SupportsStreaming"] = true,
                        ["SupportsFunctionCalling"] = false,
                        ["SupportsVision"] = false
                    }
                }
            }
        };
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026:Members annotated with 'RequiresUnreferencedCodeAttribute' require dynamic access otherwise can break functionality when trimming application code", Justification = "Provider properly registers AOT-compatible deserializer in provider module")]
    public ProviderValidationResult ValidateConfiguration(ProviderClientConfig config, ProviderClientFamily family)
    {
        var errors = new List<string>();

        // Note: API key validation is now deferred to CreateChatClient where ISecretResolver is available
        // This method only validates config structure, not secret resolution

        if (string.IsNullOrEmpty(config.ModelName))
            errors.Add("Model name (repository ID like 'meta-llama/Meta-Llama-3-8B-Instruct') is required");

        if (!string.IsNullOrWhiteSpace(config.Endpoint) &&
            !Uri.IsWellFormedUriString(config.Endpoint, UriKind.Absolute))
        {
            errors.Add("Endpoint must be a valid, absolute URI");
        }

        return errors.Count > 0
            ? ProviderValidationResult.Failure(errors.ToArray())
            : ProviderValidationResult.Success();
    }

    /// <summary>
    /// Wrapper chat client that applies HuggingFace configuration options to requests.
    /// </summary>
    private class HuggingFaceConfiguredChatClient : IChatClient
    {
        private readonly global::HuggingFace.HuggingFaceInferenceClient _client;
        private readonly string _modelName;
        private ChatClientMetadata? _metadata;

        public HuggingFaceConfiguredChatClient(
            global::HuggingFace.HuggingFaceInferenceClient client,
            string modelName)
        {
            _client = client;
            _modelName = modelName;
        }

        public void Dispose()
        {
            // Dispose the underlying client
            _client?.Dispose();
        }

        public object? GetService(Type serviceType, object? serviceKey = null)
        {
            return
                serviceKey is not null ? null :
                serviceType == typeof(ChatClientMetadata) ? (_metadata ??= new("huggingface", _client.BaseUri, _modelName)) :
                serviceType?.IsInstanceOfType(_client) is true ? _client :
                null;
        }

        public async Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var effectiveOptions = options?.Clone() ?? new ChatOptions();
            if (string.IsNullOrWhiteSpace(effectiveOptions.ModelId))
                effectiveOptions.ModelId = _modelName;

            var request = HuggingFaceChatRequestOptionKeys.BuildRequest(messages, effectiveOptions, _modelName, stream: false);
            var response = await _client.ChatCompletionsAsync(request, cancellationToken: cancellationToken).ConfigureAwait(false);
            return ToChatResponse(response);
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var effectiveOptions = options?.Clone() ?? new ChatOptions();
            if (string.IsNullOrWhiteSpace(effectiveOptions.ModelId))
                effectiveOptions.ModelId = _modelName;

            var request = HuggingFaceChatRequestOptionKeys.BuildRequest(messages, effectiveOptions, _modelName, stream: true);
            await foreach (var chunk in _client.ChatCompletionsAsStreamAsync(request, cancellationToken: cancellationToken).ConfigureAwait(false))
            {
                yield return ToChatResponseUpdate(chunk);
            }
        }

        private static ChatResponse ToChatResponse(global::HuggingFace.ChatCompletion response)
        {
            var choice = response.Choices.Count > 0 ? response.Choices[0] : null;
            var message = new ChatMessage
            {
                Role = ChatRole.Assistant,
                RawRepresentation = response
            };

            if (choice?.Message.IsText is true && choice.Message.Text is { } text)
            {
                message.Contents.Add(new TextContent(text.Content) { RawRepresentation = text });
            }
            else if (choice?.Message.IsToolCall is true && choice.Message.ToolCall is { } toolCall)
            {
                foreach (var call in toolCall.ToolCalls)
                {
                    message.Contents.Add(new FunctionCallContent(
                        call.Id,
                        call.Function.Name,
                        ParseArguments(call.Function.Arguments))
                    {
                        RawRepresentation = call
                    });
                }
            }

            var chatResponse = new ChatResponse(message)
            {
                RawRepresentation = response,
                ResponseId = response.Id,
                ModelId = response.Model,
                FinishReason = ToFinishReason(choice?.FinishReason)
            };

            chatResponse.Usage = new UsageDetails
            {
                InputTokenCount = response.Usage.PromptTokens,
                OutputTokenCount = response.Usage.CompletionTokens,
                TotalTokenCount = response.Usage.TotalTokens
            };

            return chatResponse;
        }

        private static ChatResponseUpdate ToChatResponseUpdate(global::HuggingFace.ChatCompletionChunk chunk)
        {
            var choice = chunk.Choices.Count > 0 ? chunk.Choices[0] : null;
            var update = new ChatResponseUpdate
            {
                RawRepresentation = chunk,
                ResponseId = chunk.Id,
                ModelId = chunk.Model,
                Role = ChatRole.Assistant,
                FinishReason = ToFinishReason(choice?.FinishReason)
            };

            if (choice?.Delta.IsTextMessage is true && choice.Delta.TextMessage is { } text)
            {
                update.Contents.Add(new TextContent(text.Content) { RawRepresentation = text });
            }
            else if (choice?.Delta.IsToolCall is true && choice.Delta.ToolCall is { } toolCall)
            {
                foreach (var call in toolCall.ToolCalls)
                {
                    update.Contents.Add(new FunctionCallContent(
                        call.Id,
                        call.Function.Name ?? string.Empty,
                        ParseArguments(call.Function.Arguments))
                    {
                        RawRepresentation = call
                    });
                }
            }

            if (chunk.Usage is { } usage)
            {
                update.Contents.Add(new UsageContent(new UsageDetails
                {
                    InputTokenCount = usage.PromptTokens,
                    OutputTokenCount = usage.CompletionTokens,
                    TotalTokenCount = usage.TotalTokens
                }));
            }

            return update;
        }

        private static ChatFinishReason? ToFinishReason(string? finishReason)
            => finishReason switch
            {
                null => null,
                "stop" => ChatFinishReason.Stop,
                "length" => ChatFinishReason.Length,
                "tool_calls" => ChatFinishReason.ToolCalls,
                _ => new ChatFinishReason(finishReason)
            };

        private static IDictionary<string, object?>? ParseArguments(object? arguments)
        {
            if (arguments is null)
                return null;

            if (arguments is IDictionary<string, object?> dictionary)
                return dictionary;

            if (arguments is JsonElement element)
                return ParseArguments(element);

            if (arguments is string json)
            {
                try
                {
                    using var document = JsonDocument.Parse(json);
                    return ParseArguments(document.RootElement);
                }
                catch (JsonException)
                {
                    return null;
                }
            }

            return null;
        }

        private static IDictionary<string, object?>? ParseArguments(JsonElement element)
        {
            if (element.ValueKind != JsonValueKind.Object)
                return null;

            var result = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                result[property.Name] = property.Value.Clone();
            }

            return result;
        }
    }
}
