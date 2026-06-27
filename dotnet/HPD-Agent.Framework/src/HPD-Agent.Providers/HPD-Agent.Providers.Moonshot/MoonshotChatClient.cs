using System.IO;
using System.Text.Json;
using HPD.Agent.Providers.OpenAICompatible;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Providers.Moonshot;

internal sealed class MoonshotChatClient(
    HttpClient httpClient,
    OpenAICompatibleChatClientOptions options,
    MoonshotProviderConfig? config)
    : OpenAICompatibleChatClient(httpClient, options)
{
    protected override OpenAICompatibleChatRequest BuildRequestBody(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options,
        bool stream)
    {
        var merged = ApplyDefaults(options);
        return base.BuildRequestBody(messages, merged, stream);
    }

    protected override void ConfigureRequest(OpenAICompatibleChatRequest request, ChatOptions? options, bool stream)
    {
        if (!string.IsNullOrWhiteSpace(config?.ThinkingType) || !string.IsNullOrWhiteSpace(config?.ThinkingKeep))
        {
            request.ExtraFields ??= [];
            request.ExtraFields["thinking"] = CreateThinkingJsonElement(config);
        }
    }

    private ChatOptions? ApplyDefaults(ChatOptions? options)
    {
        if (config is null)
        {
            return options;
        }

        if (options is null)
        {
            return new ChatOptions
            {
                Temperature = config.Temperature,
                TopP = config.TopP,
                MaxOutputTokens = config.MaxOutputTokens,
                StopSequences = config.StopSequences,
                Seed = config.Seed,
                ResponseFormat = CreateResponseFormat(config.ResponseFormat),
                ToolMode = CreateToolMode(config.ToolChoice)
            };
        }

        return new ChatOptions
        {
            ModelId = options.ModelId,
            Instructions = options.Instructions,
            Tools = options.Tools,
            AllowMultipleToolCalls = options.AllowMultipleToolCalls,
            MaxOutputTokens = options.MaxOutputTokens ?? config.MaxOutputTokens,
            Temperature = options.Temperature ?? config.Temperature,
            TopP = options.TopP ?? config.TopP,
            TopK = options.TopK,
            FrequencyPenalty = options.FrequencyPenalty,
            PresencePenalty = options.PresencePenalty,
            StopSequences = options.StopSequences ?? config.StopSequences,
            ResponseFormat = options.ResponseFormat ?? CreateResponseFormat(config.ResponseFormat),
            Seed = options.Seed ?? config.Seed,
            ToolMode = options.ToolMode ?? CreateToolMode(config.ToolChoice),
            Reasoning = options.Reasoning,
            AdditionalProperties = options.AdditionalProperties,
            RawRepresentationFactory = options.RawRepresentationFactory
        };
    }

    private static ChatResponseFormat? CreateResponseFormat(string? responseFormat)
        => responseFormat?.ToLowerInvariant() switch
        {
            "text" => ChatResponseFormat.Text,
            "json_object" => ChatResponseFormat.Json,
            _ => null
        };

    private static ChatToolMode? CreateToolMode(string? toolChoice)
        => toolChoice?.ToLowerInvariant() switch
        {
            "none" => ChatToolMode.None,
            "required" => ChatToolMode.RequireAny,
            _ => null
        };

    private static JsonElement CreateThinkingJsonElement(MoonshotProviderConfig config)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            if (!string.IsNullOrWhiteSpace(config.ThinkingType))
            {
                writer.WriteString("type", config.ThinkingType);
            }

            if (!string.IsNullOrWhiteSpace(config.ThinkingKeep))
            {
                writer.WriteString("keep", config.ThinkingKeep);
            }

            writer.WriteEndObject();
        }

        using var document = JsonDocument.Parse(stream.ToArray());
        return document.RootElement.Clone();
    }
}
