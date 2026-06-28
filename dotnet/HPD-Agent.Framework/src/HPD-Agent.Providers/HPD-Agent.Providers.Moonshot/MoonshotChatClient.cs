using System.IO;
using System.Text.Json;
using HPD.Agent.Providers.OpenAICompatible;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Providers.Moonshot;

internal sealed class MoonshotChatClient(
    HttpClient httpClient,
    OpenAICompatibleChatClientOptions options)
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
        var thinkingType = CreateThinkingType(options?.Reasoning?.Effort);
        var thinkingKeep = GetThinkingKeep(options);
        if (!string.IsNullOrWhiteSpace(thinkingType) || !string.IsNullOrWhiteSpace(thinkingKeep))
        {
            request.ExtraFields ??= [];
            request.ExtraFields["thinking"] = CreateThinkingJsonElement(thinkingType, thinkingKeep);
        }
    }

    private ChatOptions ApplyDefaults(ChatOptions? options)
    {
        return options?.Clone() ?? new ChatOptions();
    }

    private static string? CreateThinkingType(Microsoft.Extensions.AI.ReasoningEffort? effort)
        => effort switch
        {
            Microsoft.Extensions.AI.ReasoningEffort.None => "disabled",
            Microsoft.Extensions.AI.ReasoningEffort.Low => "enabled",
            Microsoft.Extensions.AI.ReasoningEffort.Medium => "enabled",
            Microsoft.Extensions.AI.ReasoningEffort.High => "enabled",
            Microsoft.Extensions.AI.ReasoningEffort.ExtraHigh => "enabled",
            _ => null
        };

    private static string? GetThinkingKeep(ChatOptions? options)
    {
        if (options?.AdditionalProperties is null ||
            !options.AdditionalProperties.TryGetValue(MoonshotChatRequestOptionKeys.ThinkingKeep, out var value))
        {
            return null;
        }

        return value switch
        {
            string text => text,
            JsonElement { ValueKind: JsonValueKind.String } json => json.GetString(),
            _ => null
        };
    }

    private static JsonElement CreateThinkingJsonElement(string? thinkingType, string? thinkingKeep)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            if (!string.IsNullOrWhiteSpace(thinkingType))
            {
                writer.WriteString("type", thinkingType);
            }

            if (!string.IsNullOrWhiteSpace(thinkingKeep))
            {
                writer.WriteString("keep", thinkingKeep);
            }

            writer.WriteEndObject();
        }

        using var document = JsonDocument.Parse(stream.ToArray());
        return document.RootElement.Clone();
    }
}
