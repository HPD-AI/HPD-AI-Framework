using System.Text.Json;
using HPD.Agent.Providers.OpenAICompatible;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Providers.Xai;

internal sealed class XaiChatClient(
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
        var reasoningEffort = CreateReasoningEffort(options?.Reasoning?.Effort);
        if (!string.IsNullOrWhiteSpace(reasoningEffort))
        {
            request.ExtraFields ??= [];
            request.ExtraFields["reasoning_effort"] = CreateStringJsonElement(reasoningEffort);
        }
    }

    private ChatOptions ApplyDefaults(ChatOptions? options)
    {
        return options?.Clone() ?? new ChatOptions();
    }

    private static string? CreateReasoningEffort(Microsoft.Extensions.AI.ReasoningEffort? effort)
        => effort switch
        {
            Microsoft.Extensions.AI.ReasoningEffort.Low => "low",
            Microsoft.Extensions.AI.ReasoningEffort.Medium => "medium",
            Microsoft.Extensions.AI.ReasoningEffort.High => "high",
            _ => null
        };

    private static JsonElement CreateStringJsonElement(string value)
    {
        using var document = JsonDocument.Parse($"\"{value}\"");
        return document.RootElement.Clone();
    }
}
